using System;
using System.IO;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Harmony patch to disable ALT key highlighting in settlements where player doesn't have access.
    /// This prevents the "xray" overlay that shows character names and location markers.
    /// Uses Prefix to intercept ALT before other systems can process it.
    /// </summary>
    [HarmonyPatch(typeof(Input), nameof(Input.IsKeyDown))]
    internal static class AltOverlayBlocker
    {
        private static bool _shouldBlockAlt = false;
        private static string _currentSettlementId = "";
        private static DateTime _lastAccessCheck = DateTime.MinValue;
        private static string _logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AltBlockerDebug.txt");

        private static void LogToFile(string message)
        {
            try
            {
                File.AppendAllText(_logFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}\n");
            }
            catch { /* Silent fail */ }
        }

        [HarmonyPrefix]
        static bool Prefix(InputKey key)
        {
            // Only process ALT keys
            if (key != InputKey.LeftAlt && key != InputKey.RightAlt)
                return true; // Continue with original method

            // Check if we need to recalculate block status (settlement changed)
            UpdateBlockStatusIfNeeded();

            if (_shouldBlockAlt)
            {
                return false; // Block ALT - skip original method
            }

            return true; // Allow ALT - continue with original method
        }

        private static void UpdateBlockStatusIfNeeded()
        {
            Settlement settlement = Settlement.CurrentSettlement;
            string currentSettlementId = settlement?.StringId ?? "";
            DateTime now = DateTime.Now;

            // Recalculate if:
            // 1. Settlement changed, OR
            // 2. Been 10 seconds since last check (to catch access changes from paying guide/trader)
            bool settlementChanged = currentSettlementId != _currentSettlementId;
            bool timeToRecheck = (now - _lastAccessCheck).TotalSeconds > 10;

            if (settlementChanged || timeToRecheck)
            {
                _currentSettlementId = currentSettlementId;
                _lastAccessCheck = now;

                bool oldBlockStatus = _shouldBlockAlt;
                bool newBlockStatus = ShouldBlockAlt();

                if (newBlockStatus != oldBlockStatus)
                {
                    LogToFile($"ALT block status changed: {oldBlockStatus} -> {newBlockStatus} in {settlement?.Name.ToString() ?? "no settlement"}");
                    _shouldBlockAlt = newBlockStatus;

                    if (newBlockStatus && settlement != null)
                    {
                        // Entering restricted settlement
                        InformationManager.DisplayMessage(
                            new InformationMessage($"[LM] ALT overlay disabled in {settlement.Name} - get introduced first"));
                    }
                    else if (!newBlockStatus && settlement != null && oldBlockStatus)
                    {
                        // Gained access to settlement
                        InformationManager.DisplayMessage(
                            new InformationMessage($"[LM] ALT overlay enabled in {settlement.Name}"));
                    }
                }
                else
                {
                    _shouldBlockAlt = newBlockStatus;
                }
            }
        }

        /// <summary>
        /// Public method to force recalculation of ALT blocking status.
        /// Can be called when settlement access is granted.
        /// </summary>
        public static void RefreshBlockStatus()
        {
            _lastAccessCheck = DateTime.MinValue; // Force immediate recheck
        }

        private static bool ShouldBlockAlt()
        {
            // Only apply in missions (settlement interiors)
            if (Mission.Current == null)
                return false;

            // Get current settlement
            Settlement settlement = Settlement.CurrentSettlement;
            if (settlement == null)
                return false;

            // Check if player has settlement access using existing logic
            var accessBehavior = Campaign.Current?.GetCampaignBehavior<DisableMenuBehavior>();
            if (accessBehavior == null)
                return false;

            bool hasAccess = accessBehavior.HasAccessToSettlement(settlement);
            return !hasAccess; // Block if no access
        }
    }
}