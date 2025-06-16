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
    /// Now hides buttons cleanly and adjusts popup layout to look better.
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
        /// Patches OverlayPopupWidget.SetCurrentCharacter to disable action buttons when no settlement access
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
                    // Create a cleaner approach: Hide buttons but add visual compensation
                    if (__instance.ActionButtonsList != null)
                    {
                        // Hide the entire action buttons container
                        __instance.ActionButtonsList.IsVisible = false;

                        // Try to add visual compensation by expanding the description area
                        try
                        {
                            // Look for the description/text area and try to expand it
                            var parentWidget = __instance.ActionButtonsList.ParentWidget;
                            if (parentWidget != null)
                            {
                                // Try to find other child widgets that could be expanded
                                for (int i = 0; i < parentWidget.ChildCount; i++)
                                {
                                    var child = parentWidget.GetChild(i);
                                    if (child != null && child != __instance.ActionButtonsList)
                                    {
                                        // Try to expand any text or description area
                                        if (child.GetType().Name.Contains("Text") ||
                                            child.GetType().Name.Contains("Rich") ||
                                            child.GetType().Name.Contains("Panel"))
                                        {
                                            try
                                            {
                                                // Add some bottom margin/padding to fill the space
                                                child.MarginBottom += 50f;
                                                LogToFile($"Added margin to {child.GetType().Name}");
                                            }
                                            catch { /* Continue if margin adjustment fails */ }
                                        }
                                    }
                                }

                                // Alternative approach: try to adjust the main popup container
                                var mainPopup = parentWidget.ParentWidget;
                                if (mainPopup != null)
                                {
                                    try
                                    {
                                        // Add bottom padding to the main popup
                                        mainPopup.MarginBottom += 30f;
                                        LogToFile("Added bottom margin to main popup");
                                    }
                                    catch { /* Continue if fails */ }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogToFile($"Failed to adjust popup layout: {ex.Message}");
                        }

                        LogToFile("SUCCESS: Hidden action buttons and adjusted layout");
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
                    // Ensure buttons are visible and popup layout is normal when player has access
                    if (__instance.ActionButtonsList != null)
                    {
                        __instance.ActionButtonsList.IsVisible = true;

                        // Restore normal layout by removing any added margins
                        try
                        {
                            var parentWidget = __instance.ActionButtonsList.ParentWidget;
                            if (parentWidget != null)
                            {
                                // Reset margins on child widgets
                                for (int i = 0; i < parentWidget.ChildCount; i++)
                                {
                                    var child = parentWidget.GetChild(i);
                                    if (child != null && child != __instance.ActionButtonsList)
                                    {
                                        try
                                        {
                                            child.MarginBottom = 0f;
                                        }
                                        catch { /* Continue if fails */ }
                                    }
                                }

                                // Reset main popup margin
                                var mainPopup = parentWidget.ParentWidget;
                                if (mainPopup != null)
                                {
                                    try
                                    {
                                        mainPopup.MarginBottom = 0f;
                                    }
                                    catch { /* Continue if fails */ }
                                }
                            }

                            LogToFile("Restored popup to normal layout");
                        }
                        catch (Exception ex)
                        {
                            LogToFile($"Failed to restore popup layout: {ex.Message}");
                        }

                        LogToFile("Enabled ActionButtonsList for settlement with access");
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