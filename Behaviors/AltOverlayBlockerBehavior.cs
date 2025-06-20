using HarmonyLib;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using LessMenusMoreImmersion.Behaviors;
using TaleWorlds.Core;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Campaign behavior to manage ALT highlighting status and show notifications.
    /// </summary>
    public class AltOverlayBlockerBehavior : CampaignBehaviorBase
    {
        private bool _lastBlockStatus = false;
        private string _lastSettlementId = "";

        public override void RegisterEvents()
        {
            CampaignEvents.OnMissionStartedEvent.AddNonSerializedListener(this, OnMissionStarted);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // No data to sync
        }

        private void OnMissionStarted(IMission mission)
        {
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null)
                return;

            bool shouldBlock = ShouldBlockHighlighting();
            string currentSettlementId = settlement.StringId;

            // Only show message when status changes or entering new settlement
            if (shouldBlock != _lastBlockStatus || currentSettlementId != _lastSettlementId)
            {
                if (shouldBlock)
                {
                    InformationManager.DisplayMessage(
                        new InformationMessage($"ALT overlay disabled in {settlement.Name} - get introduced first"));
                }
                else if (_lastBlockStatus && !shouldBlock) // Was blocked, now enabled
                {
                    InformationManager.DisplayMessage(
                        new InformationMessage($"ALT overlay enabled in {settlement.Name}"));
                }

                _lastBlockStatus = shouldBlock;
                _lastSettlementId = currentSettlementId;
            }
        }

        private bool ShouldBlockHighlighting()
        {
            // Only in campaign missions
            if (Campaign.Current == null || Mission.Current == null)
                return false;

            // Only in settlement missions
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null)
                return false;

            // Check settlement access
            var accessBehavior = Campaign.Current.GetCampaignBehavior<DisableMenuBehavior>();
            if (accessBehavior == null)
                return false;

            bool hasAccess = accessBehavior.HasAccessToSettlement(settlement);
            return !hasAccess; // Block if no access
        }
    }

    /// <summary>
    /// Helper class to check if ALT highlighting should be blocked.
    /// Used by the Harmony patches.
    /// </summary>
    public static class HighlightingBlocker
    {
        public static bool ShouldBlockHighlighting()
        {
            try
            {
                // Only in campaign missions
                if (Campaign.Current == null || Mission.Current == null)
                    return false;

                // Only in settlement missions
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null)
                    return false;

                // Check settlement access
                var accessBehavior = Campaign.Current.GetCampaignBehavior<DisableMenuBehavior>();
                if (accessBehavior == null)
                    return false;

                bool hasAccess = accessBehavior.HasAccessToSettlement(settlement);
                return !hasAccess; // Block if no access
            }
            catch
            {
                return false; // Don't block on any error
            }
        }
    }
}

/// <summary>
/// Harmony patch to disable nameplate updates when no settlement access.
/// </summary>
[HarmonyPatch]
internal static class NameMarkerViewPatch
{
    // Use TargetMethod to be more flexible about finding the right method
    static System.Reflection.MethodBase TargetMethod()
    {
        try
        {
            // Try to find the MissionGauntletNameMarkerView class
            var assembly = System.AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "SandBox.GauntletUI");

            if (assembly != null)
            {
                var type = assembly.GetType("SandBox.GauntletUI.Missions.MissionGauntletNameMarkerView");
                if (type != null)
                {
                    return type.GetMethod("OnMissionScreenTick",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                }
            }
        }
        catch
        {
            // If we can't find it, return null and the patch won't apply
        }

        return null;
    }

    static bool Prefix()
    {
        // Block nameplate updates if no settlement access
        return !LessMenusMoreImmersion.Behaviors.HighlightingBlocker.ShouldBlockHighlighting();
    }
}