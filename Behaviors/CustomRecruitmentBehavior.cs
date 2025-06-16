using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Behaviors
{
    public class CustomRecruitmentMenuBehavior : CampaignBehaviorBase
    {
        private static Dictionary<string, bool> settlementsWithRecruitmentOrganizer = new Dictionary<string, bool>();
        private static Dictionary<string, Hero> settlementOrganizerNotables = new Dictionary<string, Hero>();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.AfterGameMenuOpenedEvent.AddNonSerializedListener(this, OnGameMenuOpened);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("settlementsWithRecruitmentOrganizer", ref settlementsWithRecruitmentOrganizer);
            dataStore.SyncData("settlementOrganizerNotables", ref settlementOrganizerNotables);
        }

        private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
        {
            // Add custom recruitment to all settlement types
            AddCustomRecruitment(campaignGameStarter, "village");
            AddCustomRecruitment(campaignGameStarter, "town");
            AddCustomRecruitment(campaignGameStarter, "castle");

            // Add notable recruitment dialogs
            AddNotableRecruitmentDialogs(campaignGameStarter);
        }

        private void AddCustomRecruitment(CampaignGameStarter campaignGameStarter, string menuId)
        {
            campaignGameStarter.AddGameMenuOption(
                menuId,
                "recruit_volunteers_custom",
                "{=!}Recruit volunteers",
                RecruitVolunteersCondition,
                RecruitVolunteersConsequence,
                false,
                -1,
                false,
                null
            );
        }

        private bool RecruitVolunteersCondition(MenuCallbackArgs args)
        {
            if (!HasRecruitmentOrganizer(Settlement.CurrentSettlement))
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject("{=no_recruitment_organizer}You need to arrange with the local notables to organize recruitment first.");
                return true;
            }

            args.IsEnabled = true;
            return true;
        }

        private void RecruitVolunteersConsequence(MenuCallbackArgs args)
        {
            // Call vanilla recruitment
            args.MenuContext.OpenRecruitVolunteers();
        }

        private void OnGameMenuOpened(MenuCallbackArgs args)
        {
            var menuId = args.MenuContext.GameMenu.StringId;
            if (menuId == "village" || menuId == "town" || menuId == "castle")
            {
                CustomVillageMenuBehavior.RemoveMenuOption(args.MenuContext, "recruit_volunteers");
            }
        }

        /// <summary>
        /// Adds recruitment dialog options to existing notable conversations
        /// </summary>
        protected void AddNotableRecruitmentDialogs(CampaignGameStarter campaignGameStarter)
        {
            // Check if this is a conversation with a notable in a settlement where we don't have an organizer
            bool canOfferRecruitmentServices() =>
                CharacterObject.OneToOneConversationCharacter?.HeroObject != null &&
                CharacterObject.OneToOneConversationCharacter.HeroObject.IsNotable &&
                Settlement.CurrentSettlement != null &&
                !HasRecruitmentOrganizer(Settlement.CurrentSettlement) &&
                !IsRecruitmentBlocked(Settlement.CurrentSettlement, CharacterObject.OneToOneConversationCharacter.HeroObject);

            // Check if this notable has recruits available
            bool hasRecruitsAvailable() =>
                CharacterObject.OneToOneConversationCharacter?.HeroObject != null &&
                GetAvailableRecruits(CharacterObject.OneToOneConversationCharacter.HeroObject).Count > 0;

            // Player asks about direct recruitment
            campaignGameStarter.AddPlayerLine(
                "notable_ask_recruits",
                "hero_main_options",
                "notable_recruits_response",
                "{=ask_notable_recruits}What recruits do you have available?",
                hasRecruitsAvailable,
                null
            );

            // Notable responds with available recruits
            campaignGameStarter.AddDialogLine(
                "notable_recruits_response",
                "notable_recruits_response",
                "notable_recruits_offer",
                "{=notable_recruits_list}I can offer you: {TROOP_LIST} for {TOTAL_COST}{GOLD_ICON} total.",
                null,
                () => {
                    HandleBulkRecruitment(CharacterObject.OneToOneConversationCharacter.HeroObject);
                }
            );

            // Player accepts bulk recruitment
            campaignGameStarter.AddPlayerLine(
                "notable_accept_recruits",
                "notable_recruits_offer",
                "notable_recruits_accepted",
                "{=accept_bulk_recruits}I'll take them all. [Pay {TOTAL_COST}{GOLD_ICON}]",
                () => {
                    var notable = CharacterObject.OneToOneConversationCharacter?.HeroObject;
                    if (notable != null)
                    {
                        var availableTroops = GetAvailableRecruits(notable);
                        var totalCost = CalculateBulkRecruitmentCost(availableTroops);
                        return Hero.MainHero.Gold >= totalCost;
                    }
                    return false;
                },
                () => {
                    ExecuteBulkRecruitment(CharacterObject.OneToOneConversationCharacter.HeroObject);
                }
            );

            // Notable confirms recruitment
            campaignGameStarter.AddDialogLine(
                "notable_recruits_accepted",
                "notable_recruits_accepted",
                "hero_main_options",
                "{=bulk_recruitment_success}Excellent! The lads are yours now. Fight well!",
                null,
                null
            );

            campaignGameStarter.AddPlayerLine(
                "notable_decline_recruits",
                "notable_recruits_offer",
                "notable_recruitment_declined", // Go to new state
                "{=decline_bulk_recruits}Perhaps another time.",
                null,
                null
            );

            // Add a transition back to main options
            campaignGameStarter.AddDialogLine(
                "notable_recruitment_declined_response",
                "notable_recruitment_declined",
                "hero_main_options",
                "{=notable_recruitment_ok}Very well. Perhaps when you're ready.",
                null,
                null
            );
            // Player asks about organizing recruitment
            campaignGameStarter.AddPlayerLine(
                "notable_ask_organizer",
                "hero_main_options",
                "notable_organizer_response",
                "{=ask_recruitment_organizer}Can you organize willing recruits to show up when I arrive?",
                canOfferRecruitmentServices,
                null
            );

            // Notable offers organizer service
            campaignGameStarter.AddDialogLine(
                "notable_organizer_response",
                "notable_organizer_response",
                "notable_organizer_offer",
                "{=notable_organizer_offer}Aye, for {ORGANIZER_COST}{GOLD_ICON} I'll hire a lad to gather volunteers. Mind it'll cost normal rates then.",
                null,
                () => {
                    MBTextManager.SetTextVariable("ORGANIZER_COST", GetRecruitmentOrganizerCost(Settlement.CurrentSettlement));
                }
            );

            // Player accepts organizer service
            campaignGameStarter.AddPlayerLine(
                "notable_accept_organizer",
                "notable_organizer_offer",
                "notable_organizer_accepted",
                "{=accept_organizer}That sounds good. [Pay {ORGANIZER_COST}{GOLD_ICON}]",
                () => Hero.MainHero.Gold >= GetRecruitmentOrganizerCost(Settlement.CurrentSettlement),
                () => {
                    PayNotableForRecruitmentOrganizer(CharacterObject.OneToOneConversationCharacter.HeroObject, GetRecruitmentOrganizerCost(Settlement.CurrentSettlement));
                }
            );

            // Player declines organizer service
            campaignGameStarter.AddPlayerLine(
                "notable_decline_organizer",
                "notable_organizer_offer",
                "hero_main_options",
                "{=decline_organizer}Perhaps another time.",
                null,
                null
            );

            // Notable confirms organizer service
            campaignGameStarter.AddDialogLine(
                "notable_organizer_accepted",
                "notable_organizer_accepted",
                "hero_main_options",
                "{=organizer_confirmed}Excellent! My boy will have volunteers ready whenever you visit.",
                null,
                null
            );
        }

        // ==== RECRUITMENT SYSTEM METHODS ====

        /// <summary>
        /// Checks if the settlement has a paid recruitment organizer or should be auto-unlocked
        /// </summary>
        public bool HasRecruitmentOrganizer(Settlement settlement)
        {
            // Check for auto-unlock first
            if (ShouldAutoUnlockRecruitment(settlement))
            {
                return true;
            }

            // Check if organizer was paid
            var settlementId = settlement.Id.ToString();
            return settlementsWithRecruitmentOrganizer.ContainsKey(settlementId) &&
                   settlementsWithRecruitmentOrganizer[settlementId];
        }

        /// <summary>
        /// Pays a notable to organize recruitment for the settlement
        /// </summary>
        private void PayNotableForRecruitmentOrganizer(Hero notable, int cost)
        {
            if (Hero.MainHero.Gold >= cost)
            {
                Hero.MainHero.ChangeHeroGold(-cost);
                var settlementId = notable.CurrentSettlement.Id.ToString();
                settlementsWithRecruitmentOrganizer[settlementId] = true;
                settlementOrganizerNotables[settlementId] = notable;

                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=organizer_hired}The notable agrees to organize recruitment. You can now use the standard recruitment option.").ToString()));
            }
            else
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=not_enough_gold}You don't have enough gold.").ToString()));
            }
        }

        /// <summary>
        /// Gets the cost for hiring a recruitment organizer based on player status
        /// </summary>
        private int GetRecruitmentOrganizerCost(Settlement settlement)
        {
            var playerClan = Clan.PlayerClan;

            // Clan ≥ 5 OR settlement owner: Auto-enabled, no fee needed
            if (playerClan.Tier >= 5 || settlement.OwnerClan == playerClan)
            {
                return 0; // This will auto-enable
            }

            // Clan 3-4 and same kingdom OR same culture: Half cost
            if (playerClan.Tier >= 3 &&
                (settlement.OwnerClan.Kingdom == playerClan.Kingdom ||
                 settlement.Culture == playerClan.Culture))
            {
                return 150;
            }

            // Clan ≤ 2: Full cost
            return 300;
        }

        /// <summary>
        /// Checks if recruitment should be blocked due to war or poor relations
        /// </summary>
        private bool IsRecruitmentBlocked(Settlement settlement, Hero notable)
        {
            // Block if at war with settlement owner
            if (FactionManager.IsAtWarAgainstFaction(
                settlement.OwnerClan, Hero.MainHero.MapFaction))
            {
                return true;
            }

            // Block if notable dislikes player
            if (Hero.MainHero.GetRelation(notable) < 0)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Handles direct bulk recruitment from a notable
        /// </summary>
        private void HandleBulkRecruitment(Hero notable)
        {
            var availableTroops = GetAvailableRecruits(notable);
            if (availableTroops.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=no_recruits_available}I have no willing recruits at the moment.").ToString()));
                return;
            }

            var totalCost = CalculateBulkRecruitmentCost(availableTroops);
            var troopDescription = GenerateTroopDescription(availableTroops);

            // Set variables for dialog
            MBTextManager.SetTextVariable("TROOP_LIST", troopDescription);
            MBTextManager.SetTextVariable("TOTAL_COST", totalCost);
        }

        /// <summary>
        /// Executes the bulk recruitment transaction (sets slots to null like vanilla)
        /// </summary>
        private void ExecuteBulkRecruitment(Hero notable)
        {
            var availableTroops = GetAvailableRecruits(notable);
            var totalCost = CalculateBulkRecruitmentCost(availableTroops);

            if (Hero.MainHero.Gold >= totalCost)
            {
                Hero.MainHero.ChangeHeroGold(-totalCost);

                // Add troops to player party and clear slots (like vanilla)
                foreach (var (troop, count, slotIndex) in availableTroops)
                {
                    MobileParty.MainParty.MemberRoster.AddToCounts(troop, count);
                    notable.VolunteerTypes[slotIndex] = null; // Clear the slot
                }

                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=bulk_recruitment_complete}All recruits have joined your party!").ToString()));
            }
            else
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=not_enough_gold}You don't have enough gold.").ToString()));
            }
        }

        /// <summary>
        /// Gets available recruits from a notable (exactly what vanilla shows)
        /// </summary>
        private List<(CharacterObject troop, int count, int slotIndex)> GetAvailableRecruits(Hero notable)
        {
            var recruits = new List<(CharacterObject, int, int)>();

            // Check maximum slot index player can access (vanilla eligibility)
            int maxIndex = Campaign.Current.Models.VolunteerModel.MaximumIndexHeroCanRecruitFromHero(
                Hero.MainHero, notable);

            // Check each slot up to the max allowed
            for (int i = 0; i <= maxIndex && i < notable.VolunteerTypes.Length; i++)
            {
                if (notable.VolunteerTypes[i] != null)
                {
                    // Each slot has exactly 1 troop (not multiple)
                    recruits.Add((notable.VolunteerTypes[i], 1, i));
                }
            }

            return recruits;
        }

        /// <summary>
        /// Calculates bulk recruitment cost with smaller discount
        /// </summary>
        private int CalculateBulkRecruitmentCost(List<(CharacterObject troop, int count, int slotIndex)> troops)
        {
            int totalCost = 0;
            foreach (var (troop, count, slotIndex) in troops)
            {
                // Use proper recruitment cost calculation like vanilla
                int individualCost = Campaign.Current.Models.PartyWageModel.GetTroopRecruitmentCost(
                    troop, Hero.MainHero, false);
                totalCost += individualCost * count;
            }

            // Apply smaller 5% discount for bulk recruitment (was 15%, way too much)
            return (int)(totalCost * 0.95f);
        }

        /// <summary>
        /// Generates description of available troops (groups identical types)
        /// </summary>
        private string GenerateTroopDescription(List<(CharacterObject troop, int count, int slotIndex)> troops)
        {
            // Group identical troops
            var groupedTroops = troops
                .GroupBy(t => t.troop.StringId)
                .Select(g => new {
                    Troop = g.First().troop,
                    TotalCount = g.Sum(x => x.count)
                })
                .ToList();

            var descriptions = new List<string>();
            foreach (var group in groupedTroops)
            {
                if (group.TotalCount == 1)
                {
                    descriptions.Add($"1 {group.Troop.Name}");
                }
                else
                {
                    descriptions.Add($"{group.TotalCount} {group.Troop.Name}s"); // Add 's' for plural
                }
            }

            return string.Join(", ", descriptions);
        }

        /// <summary>
        /// Checks if player should automatically have recruitment access
        /// </summary>
        private bool ShouldAutoUnlockRecruitment(Settlement settlement)
        {
            var playerClan = Clan.PlayerClan;

            // Auto-unlock conditions
            if (playerClan.Tier >= 5 || settlement.OwnerClan == playerClan)
            {
                return true;
            }

            return false;
        }
    }
}