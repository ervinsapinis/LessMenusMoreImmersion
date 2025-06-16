using System;
using System.IO;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.GauntletUI.Widgets.Menu.Overlay;
using TaleWorlds.MountAndBlade.GauntletUI.Widgets.Menu;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Harmony patch to disable Talk/Visit buttons by patching OverlayPopupWidget.SetCurrentCharacter
    /// Since the ViewModel approach doesn't work, we'll patch the widget directly.
    /// </summary>
    [HarmonyPatch(typeof(OverlayPopupWidget), nameof(OverlayPopupWidget.SetCurrentCharacter))]
    internal static class PortraitActionBlocker
    {
        private static int _patchCallCount = 0;
        private static string _logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LessMenusMoreImmersion_Debug.txt");

        static PortraitActionBlocker()
        {
            LogToFile("OverlayPopupWidget PortraitActionBlocker class loaded");
        }

        private static void LogToFile(string message)
        {
            try
            {
                File.AppendAllText(_logFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}\n");
            }
            catch { /* Silent fail */ }
        }

        /// <summary>
        /// Patches OverlayPopupWidget.SetCurrentCharacter to hide action buttons when no settlement access
        /// </summary>
        [HarmonyPostfix]
        static void Postfix(OverlayPopupWidget __instance, GameMenuPartyItemButtonWidget item)
        {
            _patchCallCount++;

            string callMessage = $"OverlayPopupWidget.SetCurrentCharacter called #{_patchCallCount}";
            LogToFile(callMessage);
            InformationManager.DisplayMessage(new InformationMessage($"[LM] {callMessage}"));

            try
            {
                // Check if this is a notable character interaction
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null)
                {
                    LogToFile("No current settlement");
                    return;
                }

                var beh = Campaign.Current?.GetCampaignBehavior<DisableMenuBehavior>();
                if (beh == null)
                {
                    LogToFile("DisableMenuBehavior not found!");
                    return;
                }

                bool hasAccess = beh.HasAccessToSettlement(settlement);
                string accessMessage = $"Access check for {settlement.Name}: {hasAccess}";
                LogToFile(accessMessage);
                InformationManager.DisplayMessage(new InformationMessage($"[LM] {accessMessage}"));

                if (!hasAccess)
                {
                    // Hide the action buttons by making them invisible
                    if (__instance.ActionButtonsList != null)
                    {
                        __instance.ActionButtonsList.IsVisible = false;
                        LogToFile("SUCCESS: Hidden ActionButtonsList for no-access settlement");
                        InformationManager.DisplayMessage(new InformationMessage("[LM] SUCCESS: Hidden portrait action buttons"));

                        // Show message to player
                        InformationManager.DisplayMessage(new InformationMessage(
                            "You must visit the settlement to meet this person."));
                    }
                    else
                    {
                        LogToFile("ActionButtonsList is null");
                    }
                }
                else
                {
                    // Ensure buttons are visible when player has access
                    if (__instance.ActionButtonsList != null)
                    {
                        __instance.ActionButtonsList.IsVisible = true;
                        LogToFile("Allowing ActionButtonsList for settlement with access");
                    }
                }
            }
            catch (Exception ex)
            {
                LogToFile($"OverlayPopupWidget patch error: {ex.Message}");
                InformationManager.DisplayMessage(new InformationMessage($"[LM] Error: {ex.Message}"));
            }
        }
    }
}