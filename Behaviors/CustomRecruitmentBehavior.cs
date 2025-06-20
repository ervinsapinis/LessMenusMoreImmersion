using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
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
            // Add notable recruitment dialogs
            AddNotableRecruitmentDialogs(campaignGameStarter);
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
                    int cost = GetRecruitmentArrangementCost();
                    Hero.MainHero.ChangeHeroGold(-cost);

                    // ONLY mark recruitment as arranged - NO settlement access!
                    var settlementId = Settlement.CurrentSettlement.Id.ToString();
                    settlementsWithRecruitmentOrganizer[settlementId] = true;

                    InformationManager.DisplayMessage(new InformationMessage("You've arranged for willing recruits to be available when you visit."));
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
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null) return 500; // Fallback cost

            int totalCost = 0;

            foreach (var notable in settlement.Notables)
            {
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