using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using static TaleWorlds.CampaignSystem.ComponentInterfaces.SettlementAccessModel;
using TaleWorlds.MountAndBlade;
using LessMenusMoreImmersion.Behaviors;

namespace LessMenusMoreImmersion.Models
{
    public class CustomSettlementAccessModel : DefaultSettlementAccessModel
    {
        public override bool CanMainHeroDoSettlementAction(Settlement settlement, SettlementAction settlementAction, out bool shouldBeDisabled, out TextObject disabledText)
        {
            var behaviorInstance = Campaign.Current.GetCampaignBehavior<DisableMenuBehavior>();

            // Initialize default values
            shouldBeDisabled = false;
            disabledText = null;

            // Handle 'Manage Town' action
            if (settlementAction == SettlementAction.ManageTown)
            {
                if (settlement.OwnerClan.Leader != Hero.MainHero)
                {
                    // Do not show 'Manage Town' option at all
                    return false;
                }
                else
                {
                    // Player is the owner, proceed with base implementation
                    return base.CanMainHeroDoSettlementAction(settlement, settlementAction, out shouldBeDisabled, out disabledText);
                }
            }

            // Handle 'Recruit Troops' action: always allow
            if (settlementAction == SettlementAction.RecruitTroops)
            {
                shouldBeDisabled = false;
                disabledText = null;
                return true;
            }

            // Handle 'Trade' action in villages: disable unless access is granted
            if (settlementAction == SettlementAction.Trade && settlement.IsVillage)
            {
                if (!behaviorInstance.HasAccessToSettlement(settlement))
                {
                    shouldBeDisabled = true;
                    disabledText = new TextObject("You don't know the settlement by heart.");
                    return false;
                }
            }

            // Handle 'WaitInSettlement' action: always allow
            if (settlementAction == SettlementAction.WaitInSettlement)
            {
                shouldBeDisabled = false;
                disabledText = null;
                return true;
            }

            // Handle other actions (e.g., Craft, JoinTournament, WatchTournament, WalkAroundTheArena)
            // These should be allowed only if access is granted
            if (!behaviorInstance.HasAccessToSettlement(settlement))
            {
                shouldBeDisabled = true;
                disabledText = new TextObject("You don't know the settlement by heart.");
                return false;
            }

            // For all other actions, use base implementation
            return base.CanMainHeroDoSettlementAction(settlement, settlementAction, out shouldBeDisabled, out disabledText);
        }

        public override bool CanMainHeroAccessLocation(Settlement settlement, string locationId, out bool disableOption, out TextObject disabledText)
        {
            var behaviorInstance = Campaign.Current.GetCampaignBehavior<DisableMenuBehavior>();

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
                        disabledText = new TextObject("You don't know the settlement by heart.");
                        return false;
                    }
                }
            }

            // If the player has access, defer to the base implementation
            return base.CanMainHeroAccessLocation(settlement, locationId, out disableOption, out disabledText);
        }

        public override bool IsRequestMeetingOptionAvailable(Settlement settlement, out bool disableOption, out TextObject disabledText)
        {
            var behaviorInstance = Campaign.Current.GetCampaignBehavior<DisableMenuBehavior>();

            if (behaviorInstance == null)
            {
                return base.IsRequestMeetingOptionAvailable(settlement, out disableOption, out disabledText);
            }

            if (!behaviorInstance.HasAccessToSettlement(settlement))
            {
                disableOption = true;
                disabledText = new TextObject("You don't know the settlement by heart.");
                return false;
            }

            return base.IsRequestMeetingOptionAvailable(settlement, out disableOption, out disabledText);
        }
    }
}
