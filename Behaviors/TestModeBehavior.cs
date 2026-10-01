using System;
using System.Linq;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Tools for testing LMMI without grinding. The MCM "Testing" buttons call <see cref="TestTools"/> directly;
    /// with Test mode on, map parties ignore you, every LMMI event and roll fires, cooldowns are skipped, and
    /// anyone you talk to has [Test] lines (relation, how they see you, town standing).
    /// </summary>
    public class TestModeBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, () =>
            {
                // (Not while hired swords are hunting you: they have to be able to find you.)
                if (LmmiSettingsProvider.TestMode && MobileParty.MainParty != null && !HiredSwordsBehavior.Hunting)
                    MobileParty.MainParty.IgnoreByOtherPartiesTill(CampaignTime.HoursFromNow(3f));
            });
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, AddTestDialogs);
        }

        public override void SyncData(IDataStore dataStore) { }

        private static void AddTestDialogs(CampaignGameStarter starter)
        {
            // ---- Heroes (notables, lords, companions) ----
            starter.AddPlayerLine("lmmi_test_rel_up", "hero_main_options", "lmmi_test_ack",
                "{=!}[Test] Relation +10", IsHeroTest, () => TestTools.ChangeRelation(Hero.OneToOneConversationHero, 10), 1);
            starter.AddPlayerLine("lmmi_test_rel_down", "hero_main_options", "lmmi_test_ack",
                "{=!}[Test] Relation -10", IsHeroTest, () => TestTools.ChangeRelation(Hero.OneToOneConversationHero, -10), 1);
            starter.AddPlayerLine("lmmi_test_explain", "hero_main_options", "lmmi_test_ack",
                "{=!}[Test] How do you see me?", IsHeroTest, () => TestTools.ShowDisposition(Hero.OneToOneConversationHero), 1);
            starter.AddPlayerLine("lmmi_test_standing_up", "hero_main_options", "lmmi_test_ack",
                "{=!}[Test] Town standing +10", IsHeroTest, () => TestTools.ChangeStanding(10f), 1);
            starter.AddPlayerLine("lmmi_test_standing_down", "hero_main_options", "lmmi_test_ack",
                "{=!}[Test] Town standing -10", IsHeroTest, () => TestTools.ChangeStanding(-10f), 1);
            starter.AddDialogLine("lmmi_test_ack", "lmmi_test_ack", "hero_main_options", "{=!}[Test] Done.", null, null);

            // ---- Townsfolk ----
            starter.AddPlayerLine("lmmi_test_town_up", "town_or_village_player", "lmmi_test_ack_town",
                "{=!}[Test] Town standing +10", () => LmmiSettingsProvider.TestMode, () => TestTools.ChangeStanding(10f), 1);
            starter.AddPlayerLine("lmmi_test_town_down", "town_or_village_player", "lmmi_test_ack_town",
                "{=!}[Test] Town standing -10", () => LmmiSettingsProvider.TestMode, () => TestTools.ChangeStanding(-10f), 1);
            starter.AddDialogLine("lmmi_test_ack_town", "lmmi_test_ack_town", "town_or_village_player", "{=!}[Test] Done.", null, null);
        }

        private static bool IsHeroTest() => LmmiSettingsProvider.TestMode && Hero.OneToOneConversationHero != null;
    }

    /// <summary>One-shot testing actions, callable from MCM buttons or [Test] dialog lines.</summary>
    internal static class TestTools
    {
        private static bool InCampaign(out Hero player)
        {
            player = Hero.MainHero;
            if (Campaign.Current != null && player != null) return true;
            InformationManager.DisplayMessage(new InformationMessage("[LMMI Test] Load a campaign first."));
            return false;
        }

        private static void Say(string message)
        {
            InformationManager.DisplayMessage(new InformationMessage("[LMMI Test] " + message, Colors.Cyan));
            LmmiLog.Info("Test: " + message);
        }

        public static void AddGold()
        {
            if (!InCampaign(out var player)) return;
            GiveGoldAction.ApplyBetweenCharacters(null, player, 10000);
            Say("+10,000 gold.");
        }

        public static void HealAll()
        {
            if (!InCampaign(out var player)) return;
            player.HitPoints = player.MaxHitPoints;
            var party = MobileParty.MainParty;
            int healed = 0;
            foreach (var element in party.MemberRoster.GetTroopRoster())
            {
                if (element.Character == null) continue;
                if (element.Character.IsHero && element.Character.HeroObject != null)
                    element.Character.HeroObject.HitPoints = element.Character.HeroObject.MaxHitPoints;
                else if (element.WoundedNumber > 0)
                {
                    healed += element.WoundedNumber;
                    party.MemberRoster.AddToCounts(element.Character, 0, woundedCount: -element.WoundedNumber);
                }
            }
            Say($"You and your companions healed; {healed} wounded troops back on their feet.");
        }

        public static void RenownToNextTier()
        {
            if (!InCampaign(out _)) return;
            var clan = Clan.PlayerClan;
            if (clan.Tier >= 6) { Say("Already at the top clan tier."); return; }
            float needed = clan.RenownRequirementForNextTier - clan.Renown + 1f;
            clan.AddRenown(Math.Max(1f, needed));
            Say($"Renown +{needed:0}; clan tier is now {clan.Tier}.");
        }

        public static void ChangeRelation(Hero? hero, int amount)
        {
            if (hero == null) return;
            ChangeRelationAction.ApplyPlayerRelation(hero, amount, affectRelatives: false);
            NotableVoice.EndConversation();
            Say($"Relation with {hero.Name} {(amount > 0 ? "+" : "")}{amount} (now {hero.GetRelationWithPlayer():0}).");
        }

        public static void ShowDisposition(Hero? hero)
        {
            if (hero == null) return;
            Say(NotableDisposition.Evaluate(hero).Describe(hero));
        }

        public static void ChangeStanding(float amount)
        {
            if (!InCampaign(out _)) return;
            var settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty.CurrentSettlement;
            if (settlement == null) { Say("Be in a settlement first."); return; }
            TownStandingBehavior.Adjust(settlement, amount, "test mode");
            NotableVoice.EndConversation();
            Say($"Town standing in {settlement.Name} now {TownStandingBehavior.Get(settlement):0.#} ({TownStandingBehavior.Band(settlement)}).");
        }

        public static void StandingUp() => ChangeStanding(10f);
        public static void StandingDown() => ChangeStanding(-10f);

        public static void DiscoverSettlement()
        {
            if (!InCampaign(out _)) return;
            var settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty.CurrentSettlement;
            var discovery = Campaign.Current.GetCampaignBehavior<DisableMenuBehavior>();
            if (settlement == null || discovery == null) { Say("Be in a settlement first."); return; }
            discovery.GrantFullAccess(settlement);
            Say($"You now know every corner of {settlement.Name}.");
        }

        /// <summary>Relation with every notable in this settlement and its owner.</summary>
        public static void RelationsHere()
        {
            if (!InCampaign(out _)) return;
            var settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty.CurrentSettlement;
            if (settlement == null) { Say("Be in a settlement first."); return; }
            var heroes = settlement.Notables.Where(n => n.IsAlive).ToList();
            var owner = settlement.OwnerClan?.Leader;
            if (owner != null && owner != Hero.MainHero && !heroes.Contains(owner)) heroes.Add(owner);
            foreach (var h in heroes) ChangeRelationAction.ApplyPlayerRelation(h, 10, affectRelatives: false, showQuickNotification: false);
            NotableVoice.EndConversation();
            Say($"Relation +10 with {heroes.Count} people in {settlement.Name}: {string.Join(", ", heroes.Select(h => h.Name.ToString()))}.");
        }

        /// <summary>The nearest notable in the scene throws you a feast, right now.</summary>
        public static void FeastNow()
        {
            if (!InCampaign(out _)) return;
            var main = TaleWorlds.MountAndBlade.Agent.Main;
            var mission = TaleWorlds.MountAndBlade.Mission.Current;
            if (main == null || mission == null) { Say("Be in a town or village scene first."); return; }
            var host = mission.Agents
                .Where(a => a.IsActive() && a.Character is CharacterObject co && co.IsHero && co.HeroObject != null
                            && (co.HeroObject.IsNotable || co.HeroObject.IsLord) && co.HeroObject.Clan != Clan.PlayerClan
                            && a.Position.Distance(main.Position) < 80f)
                .OrderBy(a => a.Position.Distance(main.Position))
                .FirstOrDefault();
            if (host == null) { Say("No notable or lord within 80 m. Find one in the square (or a lord in the hall) first."); return; }
            var hero = ((CharacterObject)host.Character).HeroObject;
            Say(FeastBehavior.Begin(hero, host)
                ? $"{hero.Name} throws you a feast — close this menu."
                : "A feast is already under way.");
        }

        public static void StreetEvent()
        {
            if (!InCampaign(out _)) return;
            var kind = LmmiSettingsProvider.TestStreetEventKind;
            if (StreetEventsBehavior.Queue(kind == "Any" ? null : kind))
                Say($"A street event ({kind}) will be staged as soon as you're free in a town or village centre. In Test mode at least half are setups. Checks roll for real; the odds show here.");
        }

        public static void HiredSwords(string job)
        {
            if (!InCampaign(out _)) return;
            HiredSwordsBehavior.TestHire(job);
        }

        public static void LordsTalk()
        {
            if (!InCampaign(out _)) return;
            LordsBehavior.TestWeekly();
            Say("This week's talk at court has run (see the messages, if anyone had anything to say).");
        }

        public static void ClearCooldowns()
        {
            if (!InCampaign(out _)) return;
            HiredSwordsBehavior.ClearCooldowns();
            CastleLifeBehavior.ClearCooldowns();
            HallCourtBehavior.ClearCooldowns();
            CampBehavior.ClearCooldowns();
            ContactsBehavior.ClearCooldowns();
            ArrivalScenesBehavior.ClearCooldowns();
            AmbushBehavior.ClearCooldowns();
            LettersBehavior.ClearCooldowns();
            StreetEventsBehavior.ClearCooldowns();
            Say("All LMMI cooldowns cleared (favors, vouches, arrivals, ambushes, letters).");
        }
    }
}
