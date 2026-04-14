using LessMenusMoreImmersion.Behaviors;
using LessMenusMoreImmersion.Constants;
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

            // Handle 'Trade' action: disable unless the marketplace has been discovered.
            if (settlementAction == SettlementAction.Trade)
            {
                if (behaviorInstance != null && !behaviorInstance.HasFeatureAccess(settlement, SettlementMenuOptions.Features.Trade))
                {
                    shouldBeDisabled = true;
                    disabledText = new TextObject("{=lmmi_undiscovered}You haven't discovered this part of the settlement yet.");
                    return false;
                }
            }

            // Handle 'Craft' action: disable unless the smithy has been discovered.
            if (settlementAction == SettlementAction.Craft)
            {
                if (behaviorInstance != null && !behaviorInstance.HasFeatureAccess(settlement, SettlementMenuOptions.Features.Smithy))
                {
                    shouldBeDisabled = true;
                    disabledText = new TextObject("{=lmmi_undiscovered}You haven't discovered this part of the settlement yet.");
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
            disableOption = false;
            disabledText = null;

            var behaviorInstance = Campaign.Current?.GetCampaignBehavior<DisableMenuBehavior>();
            if (behaviorInstance == null)
                return base.CanMainHeroAccessLocation(settlement, locationId, out disableOption, out disabledText);

            // "center" (take a walk) and "port" (Naval DLC) are always open.
            if (locationId == "center" || locationId == "port")
                return base.CanMainHeroAccessLocation(settlement, locationId, out disableOption, out disabledText);

            // Inside an active mission all doors are physically open — don't add a
            // menu-level tooltip that would contradict what the player can see.
            if (Mission.Current != null && Mission.Current.Scene != null)
                return base.CanMainHeroAccessLocation(settlement, locationId, out disableOption, out disabledText);

            // --- Per-feature access (dynamic discovery system) ---
            // If this location is mapped to a feature, gate on that feature only.
            if (SettlementMenuOptions.LocationFeatureMap.TryGetValue(locationId, out var requiredFeature))
            {
                if (!behaviorInstance.HasFeatureAccess(settlement, requiredFeature))
                {
                    disableOption = true;
                    disabledText = new TextObject("{=lmmi_undiscovered}You haven't discovered this part of the settlement yet.");
                    return false;
                }
                return base.CanMainHeroAccessLocation(settlement, locationId, out disableOption, out disabledText);
            }

            // --- Fallback: unmapped locations still require full settlement access ---
            if (!behaviorInstance.HasAccessToSettlement(settlement))
            {
                disableOption = true;
                disabledText = new TextObject("{=U7v8W9x0Y}You don't know the settlement by heart.");
                return false;
            }

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