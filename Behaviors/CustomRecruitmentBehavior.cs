using System;
using System.Collections.Generic;
using System.Linq;
using LessMenusMoreImmersion.Logging;
using TaleWorlds.CampaignSystem;
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

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("settlementsWithRecruitmentOrganizer", ref settlementsWithRecruitmentOrganizer);
        }

        private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
        {
            try
            {
                // Add notable recruitment dialogs
                AddNotableRecruitmentDialogs(campaignGameStarter);
                AddNotableDirectRecruitmentDialogs(campaignGameStarter);
                LmmiLog.Debug("CustomRecruitmentMenuBehavior: notable recruitment dialogs registered.");
            }
            catch (Exception ex)
            {
                LmmiLog.Error("CustomRecruitmentMenuBehavior.OnSessionLaunched failed", ex);
            }
        }

        /// <summary>
        /// Adds recruitment arrangement dialogs for any notable
        /// </summary>
        /// <param name="campaignGameStarter">The campaign game starter used to add dialogs.</param>
        protected void AddNotableRecruitmentDialogs(CampaignGameStarter campaignGameStarter)
        {
            // Check if current character is a notable in current settlement
            bool isNotableInCurrentSettlement() =>
                CharacterObject.OneToOneConversationCharacter != null &&
                CharacterObject.OneToOneConversationCharacter.IsHero &&
                Settlement.CurrentSettlement != null &&
                Settlement.CurrentSettlement.Notables.Contains(CharacterObject.OneToOneConversationCharacter.HeroObject);

            // Player option: Ask about recruitment arrangement (available to any notable)
            campaignGameStarter.AddPlayerLine(
                "notable_recruitment_arrangement_ask",
                "hero_main_options",
                "notable_recruitment_arrangement_response",
                "{=notable_recruitment_ask}Can you help organize willing recruits for my company?",
                isNotableInCurrentSettlement,
                null
            );

            // Notable explains the arrangement
            campaignGameStarter.AddDialogLine(
                "notable_recruitment_arrangement_response",
                "notable_recruitment_arrangement_response",
                "notable_recruitment_arrangement_offer",
                "{=notable_recruitment_response}Organizing recruits requires cooperation between all the notables here. We'd need to coordinate with everyone - some are... more expensive to convince than others. For {RECRUITMENT_COST}{GOLD_ICON}, I can arrange it so willing volunteers will be ready whenever you visit. Interested?",
                null,
                () => {
                    // Set the recruitment cost variable
                    int recruitmentCost = GetRecruitmentArrangementCost();
                    MBTextManager.SetTextVariable("RECRUITMENT_COST", recruitmentCost);
                }
            );

            // Player option: Accept recruitment arrangement
            campaignGameStarter.AddPlayerLine(
                "notable_recruitment_accept",
                "notable_recruitment_arrangement_offer",
                "notable_recruitment_accepted",
                "{=notable_recruitment_accept}Yes, arrange it. [Pay {RECRUITMENT_COST}{GOLD_ICON}]",
                () => {
                    int recruitmentCost = GetRecruitmentArrangementCost();
                    return Hero.MainHero.Gold >= recruitmentCost;
                },
                () => {
                    try
                    {
                        var settlement = Settlement.CurrentSettlement;
                        if (settlement == null)
                        {
                            LmmiLog.Warning("Recruitment arrangement consequence fired with no current settlement — aborting.");
                            return;
                        }

                        int cost = GetRecruitmentArrangementCost();
                        Hero.MainHero.ChangeHeroGold(-cost);

                        // ONLY mark recruitment as arranged - NO settlement access!
                        var settlementId = settlement.Id.ToString();
                        settlementsWithRecruitmentOrganizer[settlementId] = true;

                        LmmiLog.Info($"Recruitment arrangement unlocked in '{settlement.Name}' (id={settlementId}) for {cost} gold.");
                        InformationManager.DisplayMessage(new InformationMessage("You've arranged for willing recruits to be available when you visit."));
                    }
                    catch (Exception ex)
                    {
                        LmmiLog.Error("Recruitment arrangement consequence threw", ex);
                    }
                }
            );

            // Notable confirms arrangement
            campaignGameStarter.AddDialogLine(
                "notable_recruitment_accepted",
                "notable_recruitment_accepted",
                "close_window",
                "{=notable_recruitment_accepted}Excellent! I'll speak with the other notables. From now on, you'll find willing volunteers ready when you need them.",
                null,
                null
            );

            // Player option: Decline recruitment arrangement
            campaignGameStarter.AddPlayerLine(
                "notable_recruitment_decline",
                "notable_recruitment_arrangement_offer",
                "close_window",
                "{=notable_recruitment_decline}Perhaps another time.",
                null,
                null
            );
        }

        /// <summary>
        /// Gets the cost for arranging recruitment with notables based on their relations.
        /// </summary>
        /// <returns>The cost amount.</returns>
        private int GetRecruitmentArrangementCost()
        {
            try
            {
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null)
                {
                    LmmiLog.Debug("GetRecruitmentArrangementCost: no current settlement, falling back to 500.");
                    return 500; // Fallback cost
                }

                int totalCost = 0;

                foreach (var notable in settlement.Notables ?? Enumerable.Empty<Hero>())
                {
                    if (notable == null) continue;

                    int baseCostPerNotable = 250; // Base cost per notable
                    int relation = (int)notable.GetRelationWithPlayer();

                    // Calculate cost for this notable
                    int notableCost = baseCostPerNotable;

                    if (relation > 0)
                    {
                        // Positive relation: discount
                        notableCost -= (relation * 30);
                    }
                    else if (relation < 0)
                    {
                        // Negative relation: premium
                        notableCost += (Math.Abs(relation) * 75);
                    }

                    // Ensure minimum cost of 50g per notable
                    notableCost = Math.Max(notableCost, 50);
                    totalCost += notableCost;
                }

                return totalCost;
            }
            catch (Exception ex)
            {
                LmmiLog.Error("GetRecruitmentArrangementCost threw", ex);
                return 500;
            }
        }

        /// <summary>
        /// Checks if the settlement has a paid recruitment organizer or should be auto-unlocked
        /// </summary>
        public bool HasRecruitmentOrganizer(Settlement settlement)
        {
            if (settlement == null) return false;

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
        /// Checks if player should automatically have recruitment access
        /// </summary>
        private bool ShouldAutoUnlockRecruitment(Settlement settlement)
        {
            if (settlement == null) return false;

            var playerClan = Clan.PlayerClan;
            if (playerClan == null) return false;

            // Auto-unlock conditions
            if (playerClan.Tier >= 5 || settlement.OwnerClan == playerClan)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Adds recruitment dialog options to existing notable conversations
        /// </summary>
        protected void AddNotableDirectRecruitmentDialogs(CampaignGameStarter campaignGameStarter)
        {
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

            // Player declines bulk recruitment
            campaignGameStarter.AddPlayerLine(
                "notable_decline_recruits",
                "notable_recruits_offer",
                "notable_recruitment_declined",
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
        }

        /// <summary>
        /// Handles direct bulk recruitment from a notable
        /// </summary>
        private void HandleBulkRecruitment(Hero notable)
        {
            try
            {
                if (notable == null)
                {
                    LmmiLog.Warning("HandleBulkRecruitment called with null notable.");
                    return;
                }

                var availableTroops = GetAvailableRecruits(notable);
                if (availableTroops.Count == 0)
                {
                    InformationManager.DisplayMessage(new InformationMessage("I have no willing recruits at the moment."));
                    return;
                }

                var totalCost = CalculateBulkRecruitmentCost(availableTroops);
                var troopDescription = GenerateTroopDescription(availableTroops);

                LmmiLog.Debug($"Bulk recruitment offer from '{notable.Name}': {availableTroops.Count} slots, total {totalCost} gold.");

                // Set variables for dialog
                MBTextManager.SetTextVariable("TROOP_LIST", troopDescription);
                MBTextManager.SetTextVariable("TOTAL_COST", totalCost);
            }
            catch (Exception ex)
            {
                LmmiLog.Error("HandleBulkRecruitment threw", ex);
            }
        }

        /// <summary>
        /// Executes the bulk recruitment transaction (sets slots to null like vanilla)
        /// </summary>
        private void ExecuteBulkRecruitment(Hero notable)
        {
            try
            {
                if (notable == null)
                {
                    LmmiLog.Warning("ExecuteBulkRecruitment called with null notable.");
                    return;
                }

                var availableTroops = GetAvailableRecruits(notable);
                var totalCost = CalculateBulkRecruitmentCost(availableTroops);

                if (Hero.MainHero.Gold >= totalCost)
                {
                    Hero.MainHero.ChangeHeroGold(-totalCost);

                    // Add troops to player party and clear slots (like vanilla)
                    foreach (var (troop, count, slotIndex) in availableTroops)
                    {
                        if (troop == null) continue;
                        MobileParty.MainParty.MemberRoster.AddToCounts(troop, count);

                        if (notable.VolunteerTypes != null && slotIndex >= 0 && slotIndex < notable.VolunteerTypes.Length)
                        {
                            notable.VolunteerTypes[slotIndex] = null; // Clear the slot
                        }
                    }

                    LmmiLog.Info($"Bulk-recruited {availableTroops.Count} troops from '{notable.Name}' for {totalCost} gold.");
                    InformationManager.DisplayMessage(new InformationMessage("All recruits have joined your party!"));
                }
                else
                {
                    LmmiLog.Debug($"Player lacked gold for bulk recruitment ({Hero.MainHero.Gold}/{totalCost}).");
                    InformationManager.DisplayMessage(new InformationMessage("You don't have enough gold."));
                }
            }
            catch (Exception ex)
            {
                LmmiLog.Error("ExecuteBulkRecruitment threw", ex);
            }
        }

        /// <summary>
        /// Gets available recruits from a notable (exactly what vanilla shows)
        /// </summary>
        private List<(CharacterObject troop, int count, int slotIndex)> GetAvailableRecruits(Hero notable)
        {
            var recruits = new List<(CharacterObject, int, int)>();

            try
            {
                if (notable == null || notable.VolunteerTypes == null)
                    return recruits;

                // Check maximum slot index player can access (vanilla eligibility)
                int maxIndex = Campaign.Current.Models.VolunteerModel.MaximumIndexHeroCanRecruitFromHero(
                    Hero.MainHero, notable, -1);

                // Check each slot up to the max allowed
                for (int i = 0; i <= maxIndex && i < notable.VolunteerTypes.Length; i++)
                {
                    if (notable.VolunteerTypes[i] != null)
                    {
                        // Each slot has exactly 1 troop (not multiple)
                        recruits.Add((notable.VolunteerTypes[i], 1, i));
                    }
                }
            }
            catch (Exception ex)
            {
                LmmiLog.Error($"GetAvailableRecruits for '{notable?.Name}' threw", ex);
            }

            return recruits;
        }

        /// <summary>
        /// Calculates bulk recruitment cost with smaller discount
        /// </summary>
        private int CalculateBulkRecruitmentCost(List<(CharacterObject troop, int count, int slotIndex)> troops)
        {
            try
            {
                int totalCost = 0;
                foreach (var (troop, count, slotIndex) in troops)
                {
                    if (troop == null) continue;

                    // Use proper recruitment cost calculation like vanilla
                    int individualCost = (int)Campaign.Current.Models.PartyWageModel.GetTroopRecruitmentCost(
                        troop, Hero.MainHero, false).ResultNumber;
                    totalCost += individualCost * count;
                }

                // Apply smaller 5% discount for bulk recruitment
                return (int)(totalCost * 0.95f);
            }
            catch (Exception ex)
            {
                LmmiLog.Error("CalculateBulkRecruitmentCost threw", ex);
                return 0;
            }
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
    }
}