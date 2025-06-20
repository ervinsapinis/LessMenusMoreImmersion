using LessMenusMoreImmersion.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Models
{
    /// <summary>
    /// Custom settlement access model to control player actions based on settlement access.
    /// </summary>
    public class CustomSettlementAccessModel : DefaultSettlementAccessModel
    {
        /// <summary>
        /// Determines whether the main hero can perform a specific action in the settlement.
        /// </summary>
        /// <param name="settlement">The settlement.</param>
        /// <param name="settlementAction">The action to check.</param>
        /// <param name="shouldBeDisabled">Outputs whether the option should be disabled.</param>
        /// <param name="disabledText">Outputs the text explaining why the option is disabled.</param>
        /// <returns>True if the action can be performed; otherwise, false.</returns>
        public override bool CanMainHeroDoSettlementAction(Settlement settlement, SettlementAction settlementAction, out bool shouldBeDisabled, out TextObject? disabledText)
        {
            var behaviorInstance = Campaign.Current?.GetCampaignBehavior<DisableMenuBehavior>();
            var recruitmentBehaviorInstance = Campaign.Current?.GetCampaignBehavior<CustomRecruitmentMenuBehavior>();

            // Initialize default values
            shouldBeDisabled = false;
            disabledText = null;

            // Handle 'Manage Town' action - hide if not owner
            if (settlementAction == SettlementAction.ManageTown)
            {
                if (settlement.OwnerClan.Leader != Hero.MainHero)
                {
                    return false; // Hide completely
                }
                else
                {
                    return base.CanMainHeroDoSettlementAction(settlement, settlementAction, out shouldBeDisabled, out disabledText);
                }
            }

            // Handle 'Recruit Troops' action - check recruitment arrangements
            if (settlementAction == SettlementAction.RecruitTroops)
            {
                if (recruitmentBehaviorInstance != null)
                {
                    bool hasArrangement = recruitmentBehaviorInstance.HasRecruitmentOrganizer(settlement);
                    if (!hasArrangement)
                    {
                        shouldBeDisabled = true;
                        disabledText = new TextObject("{=recruit_no_arrangement}You need to arrange with the local notables to organize recruitment first.");
                        return false;
                    }
                    else
                    {
                        // Has arrangement - allow recruitment
                        return base.CanMainHeroDoSettlementAction(settlement, settlementAction, out shouldBeDisabled, out disabledText);
                    }
                }
                // If recruitment behavior not found, allow base implementation
                return base.CanMainHeroDoSettlementAction(settlement, settlementAction, out shouldBeDisabled, out disabledText);
            }

            // Handle 'Trade' action: disable unless access is granted
            if (settlementAction == SettlementAction.Trade)
            {
                if (behaviorInstance != null && !behaviorInstance.HasAccessToSettlement(settlement))
                {
                    shouldBeDisabled = true;
                    disabledText = new TextObject("{=U7v8W9x0Y}You don't know the settlement by heart.");
                    return false;
                }
            }

            // Handle 'Craft' action: disable unless access is granted
            if (settlementAction == SettlementAction.Craft)
            {
                if (behaviorInstance != null && !behaviorInstance.HasAccessToSettlement(settlement))
                {
                    shouldBeDisabled = true;
                    disabledText = new TextObject("{=U7v8W9x0Y}You don't know the settlement by heart.");
                    return false;
                }
            }

            // For all other actions, use the base implementation
            return base.CanMainHeroDoSettlementAction(settlement, settlementAction, out shouldBeDisabled, out disabledText);
        }

        /// <summary>
        /// Determines whether the main hero can access a specific location in the settlement.
        /// </summary>
        /// <param name="settlement">The settlement.</param>
        /// <param name="locationId">The location ID.</param>
        /// <param name="disableOption">Outputs whether the option should be disabled.</param>
        /// <param name="disabledText">Outputs the text explaining why the option is disabled.</param>
        /// <returns>True if the location can be accessed; otherwise, false.</returns>
        public override bool CanMainHeroAccessLocation(Settlement settlement, string locationId, out bool disableOption, out TextObject? disabledText)
        {
            var behaviorInstance = Campaign.Current?.GetCampaignBehavior<DisableMenuBehavior>();

            if (behaviorInstance == null)
            {
                return base.CanMainHeroAccessLocation(settlement, locationId, out disableOption, out disabledText);
            }

            if (!behaviorInstance.HasAccessToSettlement(settlement))
            {
                // If we are in a mission, allow access to all locations
                if (Mission.Current != null && Mission.Current.Scene != null)
                {
                    disableOption = false;
                    disabledText = null;
                    return true; // Doors will be unlocked during missions
                }
                else
                {
                    // In the settlement menu, allow access only to 'center' (to take a walk)
                    if (locationId == "center")
                    {
                        disableOption = false;
                        disabledText = null;
                        return true;
                    }
                    else
                    {
                        disableOption = true;
                        disabledText = new TextObject("{=U7v8W9x0Y}You don't know the settlement by heart.");
                        return false;
                    }
                }
            }

            // If the player has access, defer to the base implementation
            return base.CanMainHeroAccessLocation(settlement, locationId, out disableOption, out disabledText);
        }

        /// <summary>
        /// Determines whether the "Request Meeting" option is available in the settlement.
        /// </summary>
        /// <param name="settlement">The settlement.</param>
        /// <param name="disableOption">Outputs whether the option should be disabled.</param>
        /// <param name="disabledText">Outputs the text explaining why the option is disabled.</param>
        /// <returns>True if the option is available; otherwise, false.</returns>
        public override bool IsRequestMeetingOptionAvailable(Settlement settlement, out bool disableOption, out TextObject disabledText)
        {
            var behaviorInstance = Campaign.Current?.GetCampaignBehavior<DisableMenuBehavior>();

            if (behaviorInstance == null)
            {
                return base.IsRequestMeetingOptionAvailable(settlement, out disableOption, out disabledText);
            }

            if (!behaviorInstance.HasAccessToSettlement(settlement))
            {
                disableOption = true;
                disabledText = new TextObject("{=U7v8W9x0Y}You don't know the settlement by heart.");
                return false;
            }

            return base.IsRequestMeetingOptionAvailable(settlement, out disableOption, out disabledText);
        }
    }
}