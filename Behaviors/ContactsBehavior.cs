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
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Notables as contacts. A notable who regards you well enough (see <see cref="NotableDisposition"/>)
    /// will do favors that fit who they are — face to face only, each on a per-notable cooldown — and a
    /// genuine friend will vouch for you with another notable in town, the way past someone's contempt.
    /// </summary>
    public class ContactsBehavior : CampaignBehaviorBase
    {
        // "heroStringId|favorId" -> campaign hour when the favor can be asked again
        [NonSerialized] private Dictionary<string, double> _readyAtHours = new Dictionary<string, double>();
        // vouched-for notable's StringId -> the notable who vouched
        [NonSerialized] private Dictionary<string, string> _vouchedBy = new Dictionary<string, string>();
        [NonSerialized] private DispositionBreakdown? _evaluation;
        [NonSerialized] private Hero? _lastDebugHero;
        [NonSerialized] private int _lastDebugTick;

        private const float VouchCooldownDays = 30f;
        private const int VouchCost = 3;

        private Disposition CurrentLevel => _evaluation?.Level ?? Disposition.Contempt;

        /// <summary>Someone the notable respects has vouched for the player: prejudice counts for a quarter.</summary>
        /// <summary>Another system's vouch (e.g. a delivered letter of introduction).</summary>
        public static void RecordVouch(Hero target, Hero voucher)
        {
            var self = Campaign.Current?.GetCampaignBehavior<ContactsBehavior>();
            if (self != null) self._vouchedBy[target.StringId] = voucher.StringId;
        }

        /// <summary>Who vouched for the player with this notable, if anyone.</summary>
        public static Hero? VoucherFor(Hero notable)
        {
            var self = Campaign.Current?.GetCampaignBehavior<ContactsBehavior>();
            if (self == null || !self._vouchedBy.TryGetValue(notable.StringId, out var id)) return null;
            return Hero.FindFirst(h => h.StringId == id);
        }

        public static void ClearCooldowns() => Campaign.Current?.GetCampaignBehavior<ContactsBehavior>()?._readyAtHours.Clear();

        public static bool IsVouchedFor(Hero notable) =>
            Campaign.Current?.GetCampaignBehavior<ContactsBehavior>()?._vouchedBy.ContainsKey(notable.StringId) == true;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.ConversationEnded.AddNonSerializedListener(this, _ => NotableVoice.EndConversation());
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try
            {
                RecruitSlotPatch.Apply();
                AddDebugReadout(starter);
                AddPromptLine(starter);
                AddFavorDialogs(starter);
                AddVouchDialogs(starter);
            }
            catch (Exception ex)
            {
                LmmiLog.Error("ContactsBehavior: failed to register favor dialogs", ex);
            }
        }

        private void AddFavorDialogs(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("lmmi_favor_ask", "hero_main_options", "lmmi_favor_hub",
                "{=lmmi_favor_ask}I could use a favor.",
                IsFavorConversation,
                () =>
                {
                    var notable = Hero.OneToOneConversationHero;
                    _evaluation = NotableDisposition.Evaluate(notable);
                    LmmiLog.Info($"Favor asked ({notable.Occupation}, {notable.CurrentSettlement?.Name}, clan tier {Clan.PlayerClan?.Tier}): " +
                                 _evaluation.Describe(notable));
                });

            starter.AddDialogLine("lmmi_favor_refuse", "lmmi_favor_hub", "hero_main_options",
                "{=lmmi_favor_refuse}{LMMI_FAVOR_REFUSAL}",
                () =>
                {
                    if (CurrentLevel != Disposition.Contempt) return false;
                    MBTextManager.SetTextVariable("LMMI_FAVOR_REFUSAL", NotableVoice.Refusal(Hero.OneToOneConversationHero));
                    return true;
                },
                null);

            starter.AddDialogLine("lmmi_favor_open", "lmmi_favor_hub", "lmmi_favor_list",
                "{=lmmi_favor_open}{LMMI_FAVOR_OPENER}",
                () =>
                {
                    if (_evaluation == null || _evaluation.Level == Disposition.Contempt) return false;
                    MBTextManager.SetTextVariable("LMMI_FAVOR_OPENER", OpenerText(_evaluation));
                    return true;
                },
                null);

            foreach (var favor in Favors.All)
            {
                var f = favor;
                starter.AddPlayerLine("lmmi_favor_" + f.Id, "lmmi_favor_list", "lmmi_favor_reply",
                    f.PlayerText,
                    () =>
                    {
                        var notable = Hero.OneToOneConversationHero;
                        if (notable == null || !f.AppliesTo(notable)) return false;
                        MBTextManager.SetTextVariable(f.PriceVariable, PriceSuffix(Price(f)));
                        return true;
                    },
                    () => Grant(f),
                    100,
                    (out TextObject explanation) => CanAsk(f, out explanation));
            }

            starter.AddDialogLine("lmmi_favor_reply", "lmmi_favor_reply", "hero_main_options",
                "{=lmmi_favor_reply}{LMMI_FAVOR_REPLY}", null, null);

            starter.AddPlayerLine("lmmi_favor_nevermind", "lmmi_favor_list", "lmmi_favor_nevermind_resp",
                "{=lmmi_favor_nevermind}Never mind.", null, null);
            starter.AddDialogLine("lmmi_favor_nevermind_resp", "lmmi_favor_nevermind_resp", "hero_main_options",
                "{=lmmi_favor_nevermind_resp}As you wish.", null, null);
        }

        /// <summary>
        /// Logs why a notable feels the way they do when a conversation with them starts (and shows it on screen
        /// with the MCM debug option). The line never plays; its condition just reports and returns false.
        /// </summary>
        private void AddDebugReadout(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_disposition_debug", "start", "close_window", string.Empty,
                () =>
                {
                    var notable = Hero.OneToOneConversationHero;
                    if (notable == null || !notable.IsNotable || !LmmiSettingsProvider.EnableNotableDisposition) return false;

                    // "start" conditions can be evaluated more than once per conversation.
                    if (notable == _lastDebugHero && Environment.TickCount - _lastDebugTick < 3000) return false;
                    _lastDebugHero = notable;
                    _lastDebugTick = Environment.TickCount;

                    var text = NotableDisposition.Evaluate(notable).Describe(notable);
                    LmmiLog.Info($"Conversation with notable ({notable.Occupation}, {notable.CurrentSettlement?.Name}, " +
                                 $"clan tier {Clan.PlayerClan?.Tier}, in scene: {CampaignMission.Current?.Location != null}): {text}");
                    if (LmmiSettingsProvider.ShowDispositionBreakdown)
                        InformationManager.DisplayMessage(new InformationMessage("[LMMI] " + text));
                    return false;
                },
                null, 1001);
        }

        /// <summary>
        /// The contemptuous and the fawning don't say vanilla's "So, then. What is it?".
        /// Beats vanilla's lord_start lines (priority 100) only when the voice has something to say.
        /// </summary>
        private static void AddPromptLine(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_notable_prompt", "lord_start", "hero_main_options",
                "{=lmmi_notable_prompt}{LMMI_NOTABLE_PROMPT}",
                () =>
                {
                    var hero = Hero.OneToOneConversationHero;
                    if (!NotableVoice.Applies(hero)) return false;
                    var prompt = NotableVoice.Prompt(hero!);
                    if (prompt == null) return false;
                    MBTextManager.SetTextVariable("LMMI_NOTABLE_PROMPT", prompt);
                    return true;
                },
                null, 110);

            // Farewell: no "Good to meet you" from someone who just insulted you.
            starter.AddDialogLine("lmmi_notable_farewell", "hero_leave", "close_window",
                "{=lmmi_notable_farewell}{LMMI_NOTABLE_FAREWELL}",
                () =>
                {
                    var hero = Hero.OneToOneConversationHero;
                    if (!NotableVoice.Applies(hero)) return false;
                    var bye = NotableVoice.Farewell(hero!);
                    if (bye == null) return false;
                    MBTextManager.SetTextVariable("LMMI_NOTABLE_FAREWELL", bye);
                    return true;
                },
                null, 110);

            // First meeting: a contemptuous notable answers "And who are you?" in kind, not with vanilla's courtesy.
            starter.AddDialogLine("lmmi_notable_intro", "lord_introduction", "lord_start",
                "{=lmmi_notable_intro}{LMMI_NOTABLE_INTRO}",
                () =>
                {
                    var hero = Hero.OneToOneConversationHero;
                    if (!NotableVoice.Applies(hero)) return false;
                    var intro = NotableVoice.Introduction(hero!);
                    if (intro == null) return false;
                    MBTextManager.SetTextVariable("LMMI_NOTABLE_INTRO", intro);
                    return true;
                },
                null, 110);
        }

        // ---- Vouching ----

        private void AddVouchDialogs(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("lmmi_vouch_ask", "lmmi_favor_list", "lmmi_vouch_who",
                "{=lmmi_vouch_ask}Would you put in a word for me with someone here?",
                () => Hero.OneToOneConversationHero != null && VouchCandidates(Hero.OneToOneConversationHero).Count > 0,
                () => ConversationSentence.SetObjectsToRepeatOver(VouchCandidates(Hero.OneToOneConversationHero), 5),
                100,
                (out TextObject explanation) => CanVouch(out explanation));

            starter.AddDialogLine("lmmi_vouch_who", "lmmi_vouch_who", "lmmi_vouch_pick",
                "{=lmmi_vouch_who}With whom?", null, null);

            starter.AddRepeatablePlayerLine("lmmi_vouch_pick", "lmmi_vouch_pick", "lmmi_vouch_result",
                "{=lmmi_vouch_pick}{VOUCH_TARGET.NAME} ({VOUCH_ROLE})", "{=lmmi_vouch_other}Someone else.", "lmmi_vouch_who",
                () =>
                {
                    if (!(ConversationSentence.CurrentProcessedRepeatObject is Hero target)) return false;
                    // Per option, not global: a global variable would be read when the menu is drawn, and every
                    // option would show the last name set (vanilla does the same for its repeatable lists).
                    StringHelpers.SetRepeatableCharacterProperties("VOUCH_TARGET", target.CharacterObject);
                    ConversationSentence.SelectedRepeatLine.SetTextVariable("VOUCH_ROLE", HeroHelper.GetCharacterTypeName(target));
                    return true;
                },
                () => Vouch(ConversationSentence.SelectedRepeatObject as Hero));

            starter.AddPlayerLine("lmmi_vouch_nevermind", "lmmi_vouch_pick", "lmmi_favor_nevermind_resp",
                "{=lmmi_favor_nevermind}Never mind.", null, null);

            starter.AddDialogLine("lmmi_vouch_result", "lmmi_vouch_result", "hero_main_options",
                "{=lmmi_vouch_result}{LMMI_VOUCH_REPLY}", null, null);
        }

        /// <summary>The other notables of the voucher's settlement nobody has vouched for you with yet.</summary>
        private List<Hero> VouchCandidates(Hero voucher)
        {
            var settlement = voucher.CurrentSettlement;
            if (settlement == null) return new List<Hero>();
            return settlement.Notables
                .Where(n => n != voucher && n.IsAlive && !_vouchedBy.ContainsKey(n.StringId))
                .ToList();
        }

        /// <summary>A big favor: only a genuine friend puts their name on the line.</summary>
        private bool CanVouch(out TextObject explanation)
        {
            explanation = TextObject.GetEmpty();
            var voucher = Hero.OneToOneConversationHero;
            if (voucher == null || _evaluation == null) return false;

            if (_evaluation.Level < Disposition.Friendly)
            {
                explanation = new TextObject("{=lmmi_favor_not_trusted}{NAME} doesn't trust you enough for that.");
                explanation.SetTextVariable("NAME", voucher.Name);
                return false;
            }

            if (!_evaluation.Genuine)
            {
                explanation = new TextObject("{=lmmi_favor_sycophant}{NAME} flatters you, but won't stick their neck out for you.");
                explanation.SetTextVariable("NAME", voucher.Name);
                return false;
            }

            if (!LmmiSettingsProvider.TestMode && _readyAtHours.TryGetValue(voucher.StringId + "|vouch", out var readyAt) && readyAt > CampaignTime.Now.ToHours)
            {
                int days = (int)Math.Ceiling((readyAt - CampaignTime.Now.ToHours) / CampaignTime.HoursInDay);
                explanation = new TextObject("{=lmmi_vouch_cooldown}{NAME} vouched for you recently. Come back in {DAYS} days.");
                explanation.SetTextVariable("NAME", voucher.Name);
                explanation.SetTextVariable("DAYS", days);
                return false;
            }

            return true;
        }

        private void Vouch(Hero? target)
        {
            var voucher = Hero.OneToOneConversationHero;
            if (voucher == null || target == null) return;

            TextObject reply;
            int ties = voucher.GetRelation(target);
            if (ties < 0)
            {
                // Rivals: the town's feuds become readable, and asking costs nothing.
                reply = new TextObject("{=lmmi_vouch_rivals}{TARGET}? My word would do you more harm than good. We don't get along.");
                LmmiLog.Info($"Vouch refused: {voucher.Name} and {target.Name} are on bad terms ({ties}).");
            }
            else
            {
                int boost = Math.Max(3, Math.Min(8, 3 + ties / 10));
                _vouchedBy[target.StringId] = voucher.StringId;
                ChangeRelationAction.ApplyPlayerRelation(target, boost, affectRelatives: false);
                ChangeRelationAction.ApplyPlayerRelation(voucher, -VouchCost, affectRelatives: false);

                float cooldownDays = VouchCooldownDays * LmmiSettingsProvider.FavorCooldownPercent / 100f;
                _readyAtHours[voucher.StringId + "|vouch"] = CampaignTime.Now.ToHours + cooldownDays * CampaignTime.HoursInDay;

                reply = new TextObject("{=lmmi_vouch_done}I'll have a word with {TARGET}. After that, they'll at least hear you out.");
                LmmiLog.Info($"Vouch: {voucher.Name} vouched for the player with {target.Name} (ties {ties}, relation +{boost}, voucher -{VouchCost}).");
            }

            reply.SetTextVariable("TARGET", target.Name);
            MBTextManager.SetTextVariable("LMMI_VOUCH_REPLY", reply);
        }

        /// <summary>
        /// Favors are asked face to face: the notable must be in this settlement and a scene must be
        /// loaded. The menu's Quick Talk opens a map conversation with no location, so it doesn't count.
        /// </summary>
        private bool IsFavorConversation()
        {
            if (!LmmiSettingsProvider.EnableNotableDisposition) return false;

            var notable = Hero.OneToOneConversationHero;
            if (notable == null || !notable.IsNotable) return false;

            var settlement = Settlement.CurrentSettlement;
            if (settlement == null || notable.CurrentSettlement != settlement) return false;
            if (CampaignMission.Current?.Location == null) return false;

            return Favors.All.Any(f => f.AppliesTo(notable)) || VouchCandidates(notable).Count > 0;
        }

        private bool CanAsk(Favor favor, out TextObject explanation)
        {
            explanation = TextObject.GetEmpty();
            var notable = Hero.OneToOneConversationHero;
            if (notable == null) return false;

            // A treacherous notable offers bait favors below the usual bar — to a Wary stranger, for coin.
            bool treacherous = Treachery.IsTreacherous(notable, _evaluation);
            bool bait = favor.CanBeBait && treacherous;

            if (CurrentLevel < (bait ? Disposition.Wary : favor.MinDisposition))
            {
                explanation = new TextObject("{=lmmi_favor_not_trusted}{NAME} doesn't trust you enough for that.");
                explanation.SetTextVariable("NAME", notable.Name);
                return false;
            }

            if (favor.RequiresGenuine && !bait && _evaluation?.Genuine != true)
            {
                explanation = new TextObject("{=lmmi_favor_sycophant}{NAME} flatters you, but won't stick their neck out for you.");
                explanation.SetTextVariable("NAME", notable.Name);
                return false;
            }

            // Counterplay: a friend's warning or enough Roguery and you see the swindle coming.
            if (treacherous && favor.TreacheryAffects && Treachery.Detected(notable, out var spotted))
            {
                explanation = spotted;
                NotableVoice.LogOnce("spotted_" + favor.Id, $"Treachery spotted: {notable.Name} would cheat on '{favor.Id}' — {spotted}");
                return false;
            }

            var reason = favor.UnavailableReason(notable);
            if (reason != null)
            {
                explanation = reason;
                return false;
            }

            if (!LmmiSettingsProvider.TestMode && _readyAtHours.TryGetValue(Key(notable, favor), out var readyAt))
            {
                double hoursLeft = readyAt - CampaignTime.Now.ToHours;
                if (hoursLeft > 0)
                {
                    int days = (int)Math.Ceiling(hoursLeft / CampaignTime.HoursInDay);
                    explanation = days <= 1
                        ? new TextObject("{=lmmi_favor_cooldown_tomorrow}You asked for this recently. Come back tomorrow.")
                        : new TextObject("{=lmmi_favor_cooldown}You asked for this recently. Come back in {DAYS} days.");
                    explanation.SetTextVariable("DAYS", days);
                    return false;
                }
            }

            int price = Price(favor);
            if (price > Hero.MainHero.Gold)
            {
                explanation = new TextObject("{=lmmi_favor_cant_afford}You can't afford it.");
                return false;
            }

            return true;
        }

        private void Grant(Favor favor)
        {
            var notable = Hero.OneToOneConversationHero;
            if (notable == null) return;

            bool treacherous = favor.TreacheryAffects && Treachery.IsTreacherous(notable, _evaluation);
            FavorOutcome outcome;
            try
            {
                outcome = favor.Execute(notable, treacherous);
            }
            catch (Exception ex)
            {
                LmmiLog.Error($"Favor '{favor.Id}' threw for {notable.Name}", ex);
                outcome = new FavorOutcome(false, new TextObject("{=lmmi_favor_failed}Hm. Ask me another time."));
            }

            MBTextManager.SetTextVariable("LMMI_FAVOR_REPLY", outcome.Reply);
            if (!outcome.Delivered)
            {
                LmmiLog.Info($"Favor '{favor.Id}' asked of {notable.Name} in {notable.CurrentSettlement?.Name}: nothing to deliver.");
                return;
            }

            int price = Price(favor);
            if (price > 0)
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, notable, price);
            if (favor.RelationCost > 0)
                ChangeRelationAction.ApplyPlayerRelation(notable, -favor.RelationCost, affectRelatives: false);

            float cooldownDays = favor.CooldownDays * LmmiSettingsProvider.FavorCooldownPercent / 100f;
            _readyAtHours[Key(notable, favor)] = CampaignTime.Now.ToHours + cooldownDays * CampaignTime.HoursInDay;

            if (outcome.OnConversationEnd != null)
            {
                // Vanilla runs this with no try/catch while closing the conversation: never let it throw.
                var effect = outcome.OnConversationEnd;
                var id = favor.Id;
                Campaign.Current.ConversationManager.ConversationEndOneShot += () =>
                {
                    try { effect(); }
                    catch (Exception ex) { LmmiLog.Error($"Favor '{id}' effect threw", ex); }
                };
            }

            LmmiLog.Info($"Favor '{favor.Id}' granted by {notable.Name} in {notable.CurrentSettlement?.Name} " +
                         $"(disposition={CurrentLevel}, gold={price}, relation=-{favor.RelationCost}, cooldown={cooldownDays:0.#}d" +
                         (treacherous ? ", TREACHEROUS" : "") + ").");
        }

        /// <summary>Wary notables charge, scaled by their Generosity — and a foreigner pays extra; anyone warmer does it for free.</summary>
        private int Price(Favor favor)
        {
            var notable = Hero.OneToOneConversationHero;
            if (favor.WaryGoldCost <= 0 || notable == null || _evaluation == null) return 0;
            // A swindle always costs coin — even a flatterer's; honest favors are free once they like you.
            bool bait = favor.CanBeBait && Treachery.IsTreacherous(notable, _evaluation);
            if (!bait && CurrentLevel != Disposition.Wary) return 0;
            return (int)Math.Round(favor.WaryGoldCost * NotableDisposition.Greed(notable) * ForeignerMarkup(notable, _evaluation));
        }

        /// <summary>"More for your kind": the foreigner's price, only from someone who actually holds your culture against you.</summary>
        internal static float ForeignerMarkup(Hero notable, DispositionBreakdown evaluation) =>
            NotableDisposition.IsForeigner(notable) && evaluation.Prejudice >= 3f ? 1.25f : 1f;

        private static TextObject PriceSuffix(int price)
        {
            if (price <= 0) return TextObject.GetEmpty();
            var suffix = new TextObject("{=lmmi_favor_price} [{PRICE}{GOLD_ICON}]");
            suffix.SetTextVariable("PRICE", price);
            return suffix;
        }

        private static string Key(Hero notable, Favor favor) => notable.StringId + "|" + favor.Id;

        private static string OpenerText(DispositionBreakdown evaluation)
        {
            var hero = Hero.OneToOneConversationHero;
            if (evaluation.Level == Disposition.Wary && hero != null && ForeignerMarkup(hero, evaluation) > 1f)
                return VoiceLines.Pick("open_wary_foreign", hero).ToString();

            if (evaluation.IsSycophant)
                return new TextObject("{=lmmi_favor_open_sycophant}You honor me, {?PLAYER.GENDER}my lady{?}my lord{\\?}! Whatever you need, it's yours.").ToString();

            switch (evaluation.Level)
            {
                case Disposition.Wary:
                    return new TextObject("{=lmmi_favor_open_wary}Favors cost coin, stranger. What is it?").ToString();
                case Disposition.Trusted:
                    return new TextObject("{=lmmi_favor_open_trusted}You've earned a few favors. What do you need?").ToString();
                default:
                    return new TextObject("{=lmmi_favor_open_friendly}For you? Let's hear it.").ToString();
            }
        }

        // ---- Save / load (flat string, same approach as DisableMenuBehavior) ----

        public override void SyncData(IDataStore dataStore)
        {
            string data = dataStore.IsLoading ? string.Empty : BuildSaveString();
            dataStore.SyncData("lmmi_favors_v1", ref data);
            if (dataStore.IsLoading)
                LoadSaveString(data);

            // "targetId;voucherId" entries separated by '|'
            string vouches = dataStore.IsLoading ? string.Empty
                : string.Join("|", _vouchedBy.Select(kv => kv.Key + ";" + kv.Value));
            dataStore.SyncData("lmmi_vouches_v1", ref vouches);
            if (dataStore.IsLoading)
            {
                _vouchedBy = new Dictionary<string, string>();
                foreach (var entry in (vouches ?? string.Empty).Split('|'))
                {
                    var parts = entry.Split(';');
                    if (parts.Length == 2 && parts[0].Length > 0)
                        _vouchedBy[parts[0]] = parts[1];
                }
            }
        }

        /// <summary>Format: "heroId;favorId;readyAtHours" entries separated by '|'. Expired cooldowns are dropped.</summary>
        private string BuildSaveString()
        {
            double now = CampaignTime.Now.ToHours;
            return string.Join("|", _readyAtHours
                .Where(kv => kv.Value > now)
                .Select(kv => kv.Key.Replace('|', ';') + ";" + kv.Value.ToString("R", CultureInfo.InvariantCulture)));
        }

        private void LoadSaveString(string data)
        {
            _readyAtHours = new Dictionary<string, double>();
            if (string.IsNullOrEmpty(data)) return;

            foreach (var entry in data.Split('|'))
            {
                var parts = entry.Split(';');
                if (parts.Length != 3) continue;
                if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var readyAt)) continue;
                _readyAtHours[parts[0] + "|" + parts[1]] = readyAt;
            }

            LmmiLog.Debug($"ContactsBehavior: restored {_readyAtHours.Count} favor cooldowns.");
        }
    }
}
