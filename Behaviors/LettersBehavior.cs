using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Helpers;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Letters of introduction: a genuine friend writes to someone in a nearby town who wants nothing to do
    /// with you. Carry the letter there (the town is tracked on the map, as vanilla quests are) and hand it over
    /// in person: it works as that friend vouching for you — prejudice counts for a quarter, and the door opens.
    /// The way a foreigner makes a name in a new land, one seal at a time.
    /// </summary>
    public class LettersBehavior : CampaignBehaviorBase
    {
        private sealed class Letter
        {
            public string TargetId = "";
            public string FromId = "";
            public double ExpiresHours;
        }

        private const float LetterDays = 60f;
        private const float WriterCooldownDays = 30f;
        private const int TownsConsidered = 8;
        private const int DeliveryRelation = 5;

        [NonSerialized] private Dictionary<string, Letter> _letters = new Dictionary<string, Letter>();   // by target
        [NonSerialized] private Dictionary<string, double> _writerReadyAt = new Dictionary<string, double>();
        [NonSerialized] private Hero? _proposalWriter;
        [NonSerialized] private Hero? _proposalTarget;

        public static void ClearCooldowns() => Campaign.Current?.GetCampaignBehavior<LettersBehavior>()?._writerReadyAt.Clear();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, ExpireLetters);
            CampaignEvents.ConversationEnded.AddNonSerializedListener(this, _ => { _proposalWriter = null; _proposalTarget = null; });
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try
            {
                AddWritingDialogs(starter);
                AddDeliveryDialogs(starter);
                foreach (var letter in _letters.Values)
                    Track(Hero.FindFirst(h => h.StringId == letter.TargetId));   // trackers aren't saved
            }
            catch (Exception ex) { LmmiLog.Error("LettersBehavior: failed to register dialogs", ex); }
        }

        // ---- Getting a letter ----

        private void AddWritingDialogs(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("lmmi_letter_ask", "lmmi_favor_list", "lmmi_letter_offer",
                "{=lmmi_letter_ask}Do you know anyone in the towns around here who might open a door for me?",
                () =>
                {
                    var writer = Hero.OneToOneConversationHero;
                    return writer != null && ProposedTarget(writer) != null;
                },
                null,
                100,
                (out TextObject why) => CanWrite(out why));

            starter.AddDialogLine("lmmi_letter_offer", "lmmi_letter_offer", "lmmi_letter_take",
                "{=lmmi_letter_offer}{LMMI_LETTER_TARGET.NAME} in {LMMI_LETTER_TOWN} owes me a favor or two. Give {?LMMI_LETTER_TARGET.GENDER}her{?}him{\\?} this letter — whatever {?LMMI_LETTER_TARGET.GENDER}she thinks{?}he thinks{\\?} of your kind, {?LMMI_LETTER_TARGET.GENDER}she'll{?}he'll{\\?} hear you out.",
                () =>
                {
                    var target = _proposalTarget;
                    if (target == null) return false;
                    StringHelpers.SetCharacterProperties("LMMI_LETTER_TARGET", target.CharacterObject);
                    MBTextManager.SetTextVariable("LMMI_LETTER_TOWN", target.CurrentSettlement?.Name ?? TextObject.GetEmpty());
                    return true;
                },
                null);

            starter.AddPlayerLine("lmmi_letter_take_yes", "lmmi_letter_take", "lmmi_letter_given",
                "{=lmmi_letter_take_yes}I'll deliver it myself.", null, WriteLetter);
            starter.AddDialogLine("lmmi_letter_given", "lmmi_letter_given", "hero_main_options",
                "{=lmmi_letter_given}Safe travels. And don't read it — the seal will tell.", null, null);

            starter.AddPlayerLine("lmmi_letter_take_no", "lmmi_letter_take", "lmmi_favor_nevermind_resp",
                "{=lmmi_letter_take_no}Perhaps another time.", null, null);
        }

        private bool CanWrite(out TextObject why)
        {
            why = TextObject.GetEmpty();
            var writer = Hero.OneToOneConversationHero;
            if (writer == null) return false;

            var e = NotableDisposition.Evaluate(writer);
            if (e.Level < Disposition.Friendly || !e.Genuine)
            {
                why = new TextObject("{=lmmi_letter_not_friend}{NAME} doesn't put {?NAME_HERO.GENDER}her{?}his{\\?} seal on letters for strangers.");
                why.SetTextVariable("NAME", writer.Name);
                StringHelpers.SetCharacterProperties("NAME_HERO", writer.CharacterObject, why);
                return false;
            }
            if (!LmmiSettingsProvider.TestMode && _writerReadyAt.TryGetValue(writer.StringId, out var ready) && ready > CampaignTime.Now.ToHours)
            {
                why = new TextObject("{=lmmi_letter_cooldown}{NAME} wrote a letter for you recently. Ask again later.");
                why.SetTextVariable("NAME", writer.Name);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Someone in a nearby town whose door is closed to you (Wary or worse), same trade as the writer if possible —
        /// a merchant writes to a merchant. Cached for the conversation.
        /// </summary>
        private Hero? ProposedTarget(Hero writer)
        {
            if (_proposalWriter == writer) return _proposalTarget;
            _proposalWriter = writer;
            _proposalTarget = null;

            var home = writer.CurrentSettlement;
            if (home == null) return null;

            var towns = Town.AllTowns.Select(t => t.Settlement)
                .Where(s => s != home)
                .OrderBy(s => s.GetPosition2D.Distance(home.GetPosition2D))
                .Take(TownsConsidered);

            _proposalTarget = towns
                .SelectMany(s => s.Notables)
                .Where(n => n.IsAlive && !_letters.ContainsKey(n.StringId) && !ContactsBehavior.IsVouchedFor(n))
                .Select(n => (Hero: n, Eval: NotableDisposition.Evaluate(n)))
                .Where(x => x.Eval.Level <= Disposition.Wary)
                .OrderBy(x => x.Hero.Occupation == writer.Occupation ? 0 : 1)
                .ThenBy(x => x.Eval.Score)
                .Select(x => x.Hero)
                .FirstOrDefault();
            return _proposalTarget;
        }

        private void WriteLetter()
        {
            var writer = Hero.OneToOneConversationHero;
            var target = _proposalTarget;
            if (writer == null || target == null) return;

            _letters[target.StringId] = new Letter
            {
                TargetId = target.StringId,
                FromId = writer.StringId,
                ExpiresHours = CampaignTime.Now.ToHours + LetterDays * CampaignTime.HoursInDay,
            };
            _writerReadyAt[writer.StringId] = CampaignTime.Now.ToHours + WriterCooldownDays * CampaignTime.HoursInDay;
            Track(target);

            var msg = new TextObject("{=lmmi_letter_carry}You carry {WRITER}'s letter of introduction to {TARGET} in {TOWN}.");
            msg.SetTextVariable("WRITER", writer.Name);
            msg.SetTextVariable("TARGET", target.Name);
            msg.SetTextVariable("TOWN", target.CurrentSettlement?.Name ?? TextObject.GetEmpty());
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
            LmmiLog.Info($"Letter: {writer.Name} wrote to {target.Name} in {target.CurrentSettlement?.Name} for the player.");
        }

        // ---- Delivering it ----

        private void AddDeliveryDialogs(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("lmmi_letter_deliver", "hero_main_options", "lmmi_letter_delivered",
                "{=lmmi_letter_deliver}I carry a letter for you, from {LMMI_LETTER_FROM}.",
                () =>
                {
                    var target = Hero.OneToOneConversationHero;
                    if (target == null || !_letters.TryGetValue(target.StringId, out var letter)) return false;
                    if (CampaignMission.Current?.Location == null) return false;   // a letter is handed over in person
                    var from = Hero.FindFirst(h => h.StringId == letter.FromId);
                    MBTextManager.SetTextVariable("LMMI_LETTER_FROM", from?.Name ?? new TextObject("{=lmmi_letter_old_friend}an old friend"));
                    return true;
                },
                DeliverLetter, 106);

            starter.AddDialogLine("lmmi_letter_delivered", "lmmi_letter_delivered", "hero_main_options",
                "{=lmmi_letter_delivered}{LMMI_LETTER_FROM}'s seal... Well. Any friend of {LMMI_LETTER_FROM} is worth hearing out. What can I do for you?",
                null, null);
        }

        private void DeliverLetter()
        {
            var target = Hero.OneToOneConversationHero;
            if (target == null || !_letters.TryGetValue(target.StringId, out var letter)) return;
            _letters.Remove(target.StringId);
            Untrack(target);

            var from = Hero.FindFirst(h => h.StringId == letter.FromId);
            if (from != null) ContactsBehavior.RecordVouch(target, from);
            ChangeRelationAction.ApplyPlayerRelation(target, DeliveryRelation, affectRelatives: false);
            NotableVoice.EndConversation();   // re-judge with the letter in hand
            LmmiLog.Info($"Letter: delivered {from?.Name}'s letter to {target.Name} — vouched, relation +{DeliveryRelation}.");
        }

        private void ExpireLetters()
        {
            try
            {
                double now = CampaignTime.Now.ToHours;
                foreach (var letter in _letters.Values.ToList())
                {
                    var target = Hero.FindFirst(h => h.StringId == letter.TargetId);
                    if (target != null && target.IsAlive && letter.ExpiresHours > now) continue;
                    _letters.Remove(letter.TargetId);
                    Untrack(target);
                    if (target == null) continue;
                    var msg = new TextObject("{=lmmi_letter_stale}The letter of introduction to {TARGET} has gone stale. Too late to deliver it now.");
                    msg.SetTextVariable("TARGET", target.Name);
                    InformationManager.DisplayMessage(new InformationMessage(msg.ToString()));
                }
            }
            catch (Exception ex) { LmmiLog.Error("LettersBehavior.ExpireLetters threw", ex); }
        }

        private void Track(Hero? target)
        {
            try
            {
                var settlement = target?.CurrentSettlement;
                var tracker = Campaign.Current?.VisualTrackerManager;
                if (settlement == null || tracker == null || tracker.CheckTracked(settlement)) return;
                tracker.RegisterObject(settlement);
            }
            catch (Exception ex) { LmmiLog.Error("LettersBehavior.Track threw", ex); }
        }

        private void Untrack(Hero? target)
        {
            try
            {
                var settlement = target?.CurrentSettlement;
                var tracker = Campaign.Current?.VisualTrackerManager;
                if (settlement == null || tracker == null) return;
                // Keep the marker if another letter still goes to the same settlement.
                bool stillNeeded = _letters.Values.Any(l => Hero.FindFirst(h => h.StringId == l.TargetId)?.CurrentSettlement == settlement);
                if (!stillNeeded && tracker.CheckTracked(settlement)) tracker.RemoveTrackedObject(settlement);
            }
            catch (Exception ex) { LmmiLog.Error("LettersBehavior.Untrack threw", ex); }
        }

        // ---- Save / load: "L;targetId;fromId;expires" and "W;writerId;readyAt" separated by '|' ----

        public override void SyncData(IDataStore dataStore)
        {
            string data = string.Empty;
            if (!dataStore.IsLoading)
            {
                var parts = _letters.Values.Select(l => string.Join(";", "L", l.TargetId, l.FromId, l.ExpiresHours.ToString("R", CultureInfo.InvariantCulture))).ToList();
                parts.AddRange(_writerReadyAt.Select(kv => string.Join(";", "W", kv.Key, kv.Value.ToString("R", CultureInfo.InvariantCulture))));
                data = string.Join("|", parts);
            }
            dataStore.SyncData("lmmi_letters_v1", ref data);
            if (!dataStore.IsLoading) return;

            _letters = new Dictionary<string, Letter>();
            _writerReadyAt = new Dictionary<string, double>();
            foreach (var entry in (data ?? string.Empty).Split('|'))
            {
                var p = entry.Split(';');
                if (p.Length == 4 && p[0] == "L" && double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var exp))
                    _letters[p[1]] = new Letter { TargetId = p[1], FromId = p[2], ExpiresHours = exp };
                else if (p.Length == 3 && p[0] == "W" && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ready))
                    _writerReadyAt[p[1]] = ready;
            }
        }
    }
}
