using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using Helpers;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// People who hate you act on it. When you leave a settlement, a cruel notable with a real grudge — or one
    /// whose warning you defied, or a loan shark you haven't paid — may send men after you on the road.
    /// Always telegraphed (you see them slip out behind you), never against a real army, and their robbery
    /// threat names who sent them; vanilla's fight or pay-off choices stay. In the tavern district at night,
    /// a cruel gang leader who despises you may have your purse lifted.
    /// </summary>
    public class AmbushBehavior : CampaignBehaviorBase
    {
        private enum Reason { Grudge, Defied, Debt }

        private sealed class Thugs
        {
            public string PartyId = "";
            public string SenderId = "";
            public Reason Why;
            public double SpawnedHours;
        }

        private const int MaxTargetPartySize = 60;
        private const float CooldownDays = 15f;
        private const float ThugLifetimeDays = 3f;
        private const float PurseCooldownDays = 10f;

        [NonSerialized] private Dictionary<string, double> _readyAtHours = new Dictionary<string, double>();
        [NonSerialized] private Dictionary<string, Thugs> _thugs = new Dictionary<string, Thugs>();
        [NonSerialized] private bool _nightInTavern;

        private static AmbushBehavior? Instance => Campaign.Current?.GetCampaignBehavior<AmbushBehavior>();

        public static void ClearCooldowns() => Instance?._readyAtHours.Clear();

        public override void RegisterEvents()
        {
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, (p, s, h) => { if (p == MobileParty.MainParty) _nightInTavern = false; });
            CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, OnSettlementLeft);
            CampaignEvents.MissionTickEvent.AddNonSerializedListener(this, _ =>
            {
                if (!_nightInTavern && Campaign.Current != null && Campaign.Current.IsNight
                    && CampaignMission.Current?.Location?.StringId == "tavern")
                    _nightInTavern = true;
            });
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, DisbandStaleThugs);
        }

        // ---- Leaving a settlement ----

        private void OnSettlementLeft(MobileParty party, Settlement settlement)
        {
            try
            {
                if (party != MobileParty.MainParty || settlement == null) return;
                if (!LmmiSettingsProvider.EnableAmbushes || !LmmiSettingsProvider.EnableNotableDisposition) return;

                if (settlement.IsTown && _nightInTavern) TryLiftPurse(settlement);
                _nightInTavern = false;

                TrySendThugs(settlement);
            }
            catch (Exception ex) { LmmiLog.Error("AmbushBehavior.OnSettlementLeft threw", ex); }
        }

        private void TryLiftPurse(Settlement town)
        {
            if (!Ready("purse:" + town.StringId)) return;
            var boss = town.Notables.FirstOrDefault(n => n.IsGangLeader && n.IsAlive
                && n.GetTraitLevel(DefaultTraits.Mercy) <= -1
                && NotableDisposition.Get(n) == Disposition.Contempt);
            if (boss == null) return;

            int roguery = Hero.MainHero.GetSkillValue(DefaultSkills.Roguery);
            if (roguery >= Treachery.RogueryToSpot)
            {
                LmmiLog.Info($"Purse: a cutpurse of {boss.Name}'s tried you in {town.Name}, but your Roguery ({roguery}) caught it.");
                InformationManager.DisplayMessage(new InformationMessage(
                    Flavor.Pick("{=lmmi_purse_caught}A hand slid toward your purse in the tavern district. You caught the wrist before it got there.",
                        "{=lmmi_purse_caught_2}Fingers brush your belt in the tavern crowd. You grab the wrist — the cutpurse wrenches free and vanishes.",
                        "{=lmmi_purse_caught_3}You feel a tug at your purse. You turn just in time; the thief melts into the crowd.").ToString()));
                SetCooldown("purse:" + town.StringId, PurseCooldownDays);
                return;
            }
            if (!LmmiSettingsProvider.TestMode && MBRandom.RandomFloat > 0.5f) return;

            int stolen = Math.Min(400, Math.Max(20, Hero.MainHero.Gold / 20));
            if (Hero.MainHero.Gold < stolen) return;
            GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, boss, stolen, disableNotification: true);
            SetCooldown("purse:" + town.StringId, PurseCooldownDays);

            var msg = Flavor.Pick("{=lmmi_purse_lifted}Your purse feels lighter. Somewhere in {TOWN}'s tavern district, someone had quick fingers: {GOLD}{GOLD_ICON} gone.",
                    "{=lmmi_purse_lifted_2}Your purse is lighter. Someone in {TOWN}'s tavern district has nimble fingers.",
                    "{=lmmi_purse_lifted_3}You reach for your purse and find it slit open. Somewhere in {TOWN}, a cutpurse is drinking well tonight.");
            msg.SetTextVariable("TOWN", town.Name);
            msg.SetTextVariable("GOLD", stolen);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
            LmmiLog.Info($"Purse: lifted {stolen} in {town.Name} (gang leader {boss.Name} holds you in contempt).");
        }

        private void TrySendThugs(Settlement left)
        {
            var main = MobileParty.MainParty;
            if (main.Army != null || main.IsCurrentlyAtSea || main.MemberRoster.TotalManCount > MaxTargetPartySize) return;

            var candidates = new List<(Hero Sender, Reason Why, float Chance)>();

            foreach (var shark in DebtsBehavior.OverdueSharks())
                candidates.Add((shark, Reason.Debt, 0.6f));

            var defiedBy = ArrivalScenesBehavior.ConsumeDefiance(left);
            if (defiedBy != null && NotableDisposition.Get(defiedBy) == Disposition.Contempt)
                candidates.Add((defiedBy, Reason.Defied, 0.5f));

            foreach (var n in left.Notables.Where(n => n.IsAlive))
            {
                if (n.GetTraitLevel(DefaultTraits.Mercy) <= -1 && n.GetRelationWithPlayer() <= -10
                    && NotableDisposition.Get(n) == Disposition.Contempt)
                    candidates.Add((n, Reason.Grudge, 0.3f));
            }

            foreach (var c in candidates.Where(c => Ready(c.Sender.StringId)))
            {
                float roll = MBRandom.RandomFloat;
                LmmiLog.Info($"Ambush roll: {c.Sender.Name} ({c.Why}) {roll:0.00} vs {c.Chance:0.00}.");
                if (!LmmiSettingsProvider.TestMode && roll > c.Chance) continue;
                Spawn(c.Sender, c.Why, left);
                return;   // one party of thugs per departure
            }
        }

        private void Spawn(Hero sender, Reason why, Settlement left)
        {
            var clan = Clan.BanditFactions.FirstOrDefault(c => c.StringId == "looters") ?? Clan.BanditFactions.FirstOrDefault();
            if (clan?.DefaultPartyTemplate == null) { LmmiLog.Warning("Ambush: no bandit clan to hire thugs from."); return; }

            var main = MobileParty.MainParty;
            var position = NavigationHelper.FindPointAroundPosition(main.Position, MobileParty.NavigationType.Default, 4f, 2f);
            var thugs = BanditPartyComponent.CreateLooterParty("lmmi_thugs_" + sender.StringId, clan, left, false, clan.DefaultPartyTemplate, position);
            if (thugs == null) return;

            int wanted = Math.Max(8, Math.Min(35, (int)(main.MemberRoster.TotalManCount * 0.6f) + 6));
            int have = thugs.MemberRoster.TotalManCount;
            if (have < wanted && clan.BasicTroop != null)
                thugs.MemberRoster.AddToCounts(clan.BasicTroop, wanted - have);

            var name = new TextObject("{=lmmi_thugs_name}{NAME}'s Men");
            name.SetTextVariable("NAME", sender.FirstName ?? sender.Name);
            thugs.Party.SetCustomName(name);
            thugs.ActualClan = clan;
            thugs.Aggressiveness = 1f;
            thugs.InitializePartyTrade(50);
            thugs.ItemRoster.AddToCounts(DefaultItems.Grain, 3);
            thugs.Party.SetVisualAsDirty();
            // Scripted chasers, as vanilla's quests make them: lock the AI so the chase isn't re-planned away.
            thugs.Ai.SetDoNotMakeNewDecisions(true);
            SetPartyAiAction.GetActionForEngagingParty(thugs, main, MobileParty.NavigationType.Default, false);

            _thugs[thugs.StringId] = new Thugs { PartyId = thugs.StringId, SenderId = sender.StringId, Why = why, SpawnedHours = CampaignTime.Now.ToHours };
            SetCooldown(sender.StringId, CooldownDays);

            var warning = new TextObject("{=lmmi_thugs_telegraph}Rough men slip out of {TOWN} behind you, keeping their distance. {NAME}'s people, by the look of them.");
            warning.SetTextVariable("TOWN", left.Name);
            warning.SetTextVariable("NAME", sender.Name);
            InformationManager.DisplayMessage(new InformationMessage(warning.ToString(), Colors.Red));
            LmmiLog.Info($"Ambush: {sender.Name} sent {thugs.MemberRoster.TotalManCount} men after the player leaving {left.Name} ({why}).");
        }

        private void DisbandStaleThugs()
        {
            try
            {
                double now = CampaignTime.Now.ToHours;
                foreach (var t in _thugs.Values.ToList())
                {
                    var party = MobileParty.All.FirstOrDefault(p => p.StringId == t.PartyId);
                    if (party == null || !party.IsActive) { _thugs.Remove(t.PartyId); continue; }
                    if (now - t.SpawnedHours < ThugLifetimeDays * CampaignTime.HoursInDay || party.MapEvent != null) continue;
                    DestroyPartyAction.Apply(null, party);
                    _thugs.Remove(t.PartyId);
                    LmmiLog.Info($"Ambush: {t.PartyId} gave up the chase and scattered.");
                }
            }
            catch (Exception ex) { LmmiLog.Error("AmbushBehavior.DisbandStaleThugs threw", ex); }
        }

        /// <summary>The robbery threat vanilla gives bandits, rewritten for thugs someone sent.</summary>
        internal static TextObject? ThreatFor(MobileParty? party)
        {
            var self = Instance;
            if (party == null || self == null || !self._thugs.TryGetValue(party.StringId, out var t)) return null;
            var sender = Hero.FindFirst(h => h.StringId == t.SenderId);
            TextObject line;
            switch (t.Why)
            {
                case Reason.Debt:
                    line = Flavor.Pick("{=lmmi_thugs_threat_debt}{NAME} wants the coin you owe. All of it, with interest — and if you can't pay, we take it out of your hide.",
                        "{=lmmi_thugs_threat_debt_2}{NAME} wants paying, with interest. Hand it over, or we take it out of your hide.",
                        "{=lmmi_thugs_threat_debt_3}You owe {NAME}. We're here to collect — coin or blood, your choice.");
                    break;
                case Reason.Defied:
                    line = Flavor.Pick("{=lmmi_thugs_threat_defied}{NAME} told you to finish your business and leave. You should have listened.",
                        "{=lmmi_thugs_threat_defied_2}{NAME} warned you to leave. You didn't listen. Now you'll learn.",
                        "{=lmmi_thugs_threat_defied_3}You were told to clear off by {NAME}. You should have.");
                    break;
                default:
                    line = Flavor.Pick("{=lmmi_thugs_threat_grudge}{NAME} sends regards. Hand over your purse, and maybe we let you walk.",
                        "{=lmmi_thugs_threat_grudge_2}{NAME} hasn't forgotten you. Your purse, and maybe we leave you standing.",
                        "{=lmmi_thugs_threat_grudge_3}Compliments of {NAME}. Empty your pockets.");
                    break;
            }
            line.SetTextVariable("NAME", sender?.Name ?? new TextObject("{=lmmi_thugs_someone}Someone you crossed"));
            return line;
        }

        private bool Ready(string key) =>
            LmmiSettingsProvider.TestMode || !_readyAtHours.TryGetValue(key, out var at) || at <= CampaignTime.Now.ToHours;
        private void SetCooldown(string key, float days) => _readyAtHours[key] = CampaignTime.Now.ToHours + days * CampaignTime.HoursInDay;

        // ---- Save / load: "C;key;hours" and "T;partyId;senderId;reason;hours" separated by '|' ----

        public override void SyncData(IDataStore dataStore)
        {
            string data = string.Empty;
            if (!dataStore.IsLoading)
            {
                double now = CampaignTime.Now.ToHours;
                var parts = _readyAtHours.Where(kv => kv.Value > now)
                    .Select(kv => "C;" + kv.Key.Replace(';', ',') + ";" + kv.Value.ToString("R", CultureInfo.InvariantCulture)).ToList();
                parts.AddRange(_thugs.Values.Select(t => string.Join(";", "T", t.PartyId, t.SenderId, ((int)t.Why).ToString(),
                    t.SpawnedHours.ToString("R", CultureInfo.InvariantCulture))));
                data = string.Join("|", parts);
            }
            dataStore.SyncData("lmmi_ambush_v1", ref data);
            if (!dataStore.IsLoading) return;

            _readyAtHours = new Dictionary<string, double>();
            _thugs = new Dictionary<string, Thugs>();
            foreach (var entry in (data ?? string.Empty).Split('|'))
            {
                var p = entry.Split(';');
                if (p.Length == 3 && p[0] == "C" && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var at))
                    _readyAtHours[p[1].Replace(',', ';')] = at;
                else if (p.Length == 5 && p[0] == "T" && int.TryParse(p[3], out var why)
                         && double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var spawned))
                    _thugs[p[1]] = new Thugs { PartyId = p[1], SenderId = p[2], Why = (Reason)why, SpawnedHours = spawned };
            }
        }

        /// <summary>Vanilla's robbery threat ("Your money or your life") — for our thugs, it names who sent them.</summary>
        [HarmonyPatch(typeof(BanditInteractionsCampaignBehavior), "bandit_start_defender_condition")]
        private static class RobberyThreatPatch
        {
            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(bool __result)
            {
                try
                {
                    if (!__result) return;
                    var threat = ThreatFor(PlayerEncounter.EncounteredParty?.MobileParty);
                    if (threat == null) return;
                    MBTextManager.SetTextVariable("ROBBERY_THREAT", threat);
                    LmmiLog.Info($"Ambush: thugs deliver their message: \"{threat}\"");
                }
                catch (Exception ex) { LmmiLog.Error("AmbushBehavior.RobberyThreatPatch threw", ex); }
            }
        }
    }
}
