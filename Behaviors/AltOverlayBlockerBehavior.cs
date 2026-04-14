using HarmonyLib;
using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using LessMenusMoreImmersion.Behaviors;
using LessMenusMoreImmersion.Constants;
using LessMenusMoreImmersion.Logging;
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
            // Status-change notification removed — the per-feature blocking fires
            // dynamically on every tick via HighlightingBlocker, so a one-shot
            // on-enter message would be misleading (features can unlock mid-session).
        }

        private bool ShouldBlockHighlighting() => HighlightingBlocker.ShouldBlockHighlighting();
    }

    /// <summary>
    /// Helper class to check if ALT highlighting should be blocked.
    /// Used by the Harmony patches.
    /// </summary>
    public static class HighlightingBlocker
    {
        /// <summary>
        /// Called every tick while Alt is held. Returns true when nameplate
        /// rendering should be suppressed for the current scene.
        ///
        /// Logic mirrors <see cref="CustomSettlementAccessModel.CanMainHeroAccessLocation"/>:
        /// - Full settlement access → never block.
        /// - Current scene maps to a feature → block only until that feature is discovered.
        /// - Unmapped scene (shouldn't normally occur) → block until full access.
        /// </summary>
        /// <summary>
        /// Returns true only when the player is inside a sub-location scene
        /// (tavern, smithy, lordshall, etc.) that has NOT been discovered yet.
        ///
        /// The town center ("center") is NEVER blocked — companions, passage
        /// markers and all agents show normally there.
        /// </summary>
        public static bool ShouldBlockHighlighting()
        {
            try
            {
                if (Campaign.Current == null || Mission.Current == null)
                    return false;

                var settlement = Settlement.CurrentSettlement;
                if (settlement == null)
                    return false;

                var accessBehavior = Campaign.Current.GetCampaignBehavior<DisableMenuBehavior>();
                if (accessBehavior == null)
                    return false;

                // Full access → never block anywhere.
                if (accessBehavior.HasAccessToSettlement(settlement))
                    return false;

                var locationId = CampaignMission.Current?.Location?.StringId;

                // Town center / unmapped → never block.
                if (string.IsNullOrEmpty(locationId) || locationId == "center")
                    return false;

                // Sub-location with a feature mapping → block only if undiscovered.
                if (SettlementMenuOptions.LocationFeatureMap.TryGetValue(locationId!, out var feature))
                    return !accessBehavior.HasFeatureAccess(settlement, feature);

                // Unknown sub-location → don't block.
                return false;
            }
            catch
            {
                return false;
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
        catch (Exception ex)
        {
            // If we can't find it, return null and the patch won't apply
            LmmiLog.Warning($"NameMarkerViewPatch.TargetMethod failed to locate target: {ex.Message}");
        }

        return null;
    }

    static bool Prefix()
    {
        // Block nameplate updates if no settlement access
        return !LessMenusMoreImmersion.Behaviors.HighlightingBlocker.ShouldBlockHighlighting();
    }
}