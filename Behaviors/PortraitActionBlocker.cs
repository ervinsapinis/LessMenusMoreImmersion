using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.GauntletUI.Widgets.Menu.Overlay;
using TaleWorlds.MountAndBlade.GauntletUI.Widgets.Menu;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Harmony patch to disable Talk/Visit buttons when player hasn't met the notable before.
    /// Now uses "met before" logic instead of settlement access.
    /// </summary>
    [HarmonyPatch(typeof(OverlayPopupWidget), nameof(OverlayPopupWidget.SetCurrentCharacter))]
    internal static class PortraitActionBlocker
    {
        private static int _patchCallCount = 0;
        private static string _logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LessMenusMoreImmersion_Debug.txt");
        private static bool _marginsApplied = false; // Track if we've already applied margins

        static PortraitActionBlocker()
        {
            LogToFile("OverlayPopupWidget PortraitActionBlocker class loaded - now using 'met before' logic");
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
        /// Patches OverlayPopupWidget.SetCurrentCharacter to disable action buttons when player hasn't met the notable
        /// </summary>
        [HarmonyPostfix]
        static void Postfix(OverlayPopupWidget __instance, object item) // ← MAIN CHANGE: object instead of GameMenuPartyItemButtonWidget
        {
            _patchCallCount++;

            // Reduce logging spam
            if (_patchCallCount <= 5)
            {
                string callMessage = $"OverlayPopupWidget.SetCurrentCharacter called #{_patchCallCount}";
                LogToFile(callMessage);
                InformationManager.DisplayMessage(new InformationMessage($"[LM] {callMessage}"));
            }

            try
            {
                // Log widget type for debugging
                string widgetType = item?.GetType().Name ?? "null";
                LogToFile($"Processing widget type: {widgetType}");

                // Reset margins first to prevent cumulative expansion
                ResetMargins(__instance);

                // Try to get the hero from whatever widget we received
                Hero targetHero = null;
                try
                {
                    // SIMPLE APPROACH: Log basic widget properties to find the identifier
                    LogToFile($"Widget ToString: {item?.ToString() ?? "null"}");

                    // Check for simple identifying properties
                    var indexProp = item?.GetType().GetProperty("Index");
                    if (indexProp != null)
                    {
                        var index = indexProp.GetValue(item);
                        LogToFile($"Widget Index: {index}");

                        // Try to use index to get the right notable
                        if (index is int intIndex && Settlement.CurrentSettlement?.Notables != null
                            && intIndex >= 0 && intIndex < Settlement.CurrentSettlement.Notables.Count)
                        {
                            targetHero = Settlement.CurrentSettlement.Notables[intIndex];
                            LogToFile($"SUCCESS: Got hero {targetHero.Name} using Index {intIndex}");
                        }
                    }

                    var idProp = item?.GetType().GetProperty("Id") ?? item?.GetType().GetProperty("ID");
                    if (idProp != null)
                    {
                        var id = idProp.GetValue(item);
                        LogToFile($"Widget ID: {id}");
                    }

                    var nameProp = item?.GetType().GetProperty("Name");
                    if (nameProp != null)
                    {
                        var name = nameProp.GetValue(item);
                        LogToFile($"Widget Name: {name}");

                        // Try to find the notable with this exact name
                        if (name is string notableName && Settlement.CurrentSettlement?.Notables != null)
                        {
                            var matchingNotable = Settlement.CurrentSettlement.Notables
                                .FirstOrDefault(n => n.Name.ToString() == notableName);

                            if (matchingNotable != null)
                            {
                                targetHero = matchingNotable;
                                LogToFile($"SUCCESS: Found matching notable {targetHero.Name} using Name property");
                            }
                            else
                            {
                                LogToFile($"No notable found with name '{notableName}' in settlement");
                            }
                        }
                    }

                    // Also try the original Character property approach
                    if (targetHero == null)
                    {
                        var characterProperty = item?.GetType().GetProperty("Character");
                        if (characterProperty != null)
                        {
                            var character = characterProperty.GetValue(item);
                            if (character != null)
                            {
                                var heroProperty = character.GetType().GetProperty("HeroObject");
                                if (heroProperty != null)
                                {
                                    targetHero = heroProperty.GetValue(character) as Hero;
                                    LogToFile($"SUCCESS: Got hero {targetHero?.Name} from Character property");
                                }
                            }
                        }
                        else
                        {
                            LogToFile($"No 'Character' property found on {widgetType}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogToFile($"Failed to get hero from item: {ex.Message}");
                }

                // Fallback: try to get hero from settlement notables
                if (targetHero == null)
                {
                    var settlement = Settlement.CurrentSettlement;
                    if (settlement != null && settlement.Notables.Count > 0)
                    {
                        // Use first notable as fallback - this may cause wrong hero detection!
                        targetHero = settlement.Notables[0];
                        LogToFile($"WARNING: Using fallback hero {targetHero.Name} - THIS MAY BE THE WRONG HERO!");
                        LogToFile($"Settlement has {settlement.Notables.Count} notables total");

                        // Log all notables for debugging
                        for (int i = 0; i < settlement.Notables.Count; i++)
                        {
                            LogToFile($"  Notable[{i}]: {settlement.Notables[i].Name} (HasMet: {settlement.Notables[i].HasMet})");
                        }
                    }
                }

                if (targetHero == null)
                {
                    LogToFile("No target hero found - no blocking will occur");
                    return;
                }

                LogToFile($"Detected target hero: {targetHero.Name}");

                // Check if player has met this hero before
                bool hasMetBefore = HasPlayerMetHero(targetHero);
                string metMessage = $"Met check for {targetHero.Name}: {hasMetBefore}";
                LogToFile(metMessage);

                if (_patchCallCount <= 3)
                {
                    InformationManager.DisplayMessage(new InformationMessage($"[LM] {metMessage}"));
                }

                if (!hasMetBefore)
                {
                    // Hide buttons and add visual compensation
                    if (__instance.ActionButtonsList != null)
                    {
                        __instance.ActionButtonsList.IsVisible = false;
                        ApplyMargins(__instance);

                        LogToFile("SUCCESS: Hidden action buttons - player hasn't met this notable");

                        if (_patchCallCount <= 2)
                        {
                            InformationManager.DisplayMessage(new InformationMessage("[LM] SUCCESS: Hidden portrait buttons - haven't met"));
                        }

                        // Show message to player
                        InformationManager.DisplayMessage(new InformationMessage(
                            "You need to be introduced to this person first."));
                    }
                    else
                    {
                        LogToFile("ActionButtonsList is null");
                    }
                }
                else
                {
                    // Ensure buttons are visible when player has met the hero
                    if (__instance.ActionButtonsList != null)
                    {
                        __instance.ActionButtonsList.IsVisible = true;
                        LogToFile("Enabled ActionButtonsList - player has met this notable");
                    }
                }
            }
            catch (Exception ex)
            {
                LogToFile($"OverlayPopupWidget patch error: {ex.Message}");
                InformationManager.DisplayMessage(new InformationMessage($"[LM] Error: {ex.Message}"));
            }
        }

        /// <summary>
        /// Checks if the player has met the specified hero before
        /// </summary>
        private static bool HasPlayerMetHero(Hero hero)
        {
            if (hero == null) return false;

            try
            {
                // Method 1: Check HasMet property (THIS IS THE IMPORTANT ONE)
                var hasMetProperty = typeof(Hero).GetProperty("HasMet");
                if (hasMetProperty != null)
                {
                    var hasMet = (bool)hasMetProperty.GetValue(hero);
                    LogToFile($"HasMet property for {hero.Name}: {hasMet}");
                    return hasMet; // ONLY USE THIS - ignore settlement access for portrait blocking
                }

                // Fallback methods if HasMet property doesn't exist (shouldn't happen)
                LogToFile("WARNING: HasMet property not found - using fallback methods");

                // Method 2: Check if hero is known to player
                var isKnownProperty = typeof(Hero).GetProperty("IsKnownToPlayer");
                if (isKnownProperty != null)
                {
                    var isKnown = (bool)isKnownProperty.GetValue(hero);
                    LogToFile($"IsKnownToPlayer property for {hero.Name}: {isKnown}");
                    return isKnown;
                }

                // Method 3: Check conversation history or relation
                if (hero.GetRelationWithPlayer() != 0)
                {
                    LogToFile($"Player has relation {hero.GetRelationWithPlayer()} with {hero.Name} - assuming met");
                    return true;
                }

                // Method 4: Check if hero is part of player clan or allied
                if (hero.Clan == Clan.PlayerClan ||
                    (hero.Clan?.Kingdom != null && hero.Clan.Kingdom == Clan.PlayerClan.Kingdom))
                {
                    LogToFile($"{hero.Name} is allied - assuming met");
                    return true;
                }

                // REMOVED Method 5 (settlement access check) - portrait blocking should be independent!

                // Default: assume not met
                LogToFile($"No meeting evidence found for {hero.Name} - assuming not met");
                return false;
            }
            catch (Exception ex)
            {
                LogToFile($"Error checking if player met {hero?.Name}: {ex.Message}");
                return false; // Default to not met if we can't determine
            }
        }

        /// <summary>
        /// Applies visual compensation margins to fill button space
        /// </summary>
        private static void ApplyMargins(OverlayPopupWidget instance)
        {
            if (_marginsApplied) return; // Prevent cumulative application

            try
            {
                var parentWidget = instance.ActionButtonsList.ParentWidget;
                if (parentWidget != null)
                {
                    // Add margin to other elements to fill the button space
                    for (int i = 0; i < parentWidget.ChildCount; i++)
                    {
                        var child = parentWidget.GetChild(i);
                        if (child != null && child != instance.ActionButtonsList)
                        {
                            try
                            {
                                child.MarginBottom += 50f;
                                LogToFile($"Added margin to {child.GetType().Name}");
                            }
                            catch { /* Continue if fails */ }
                        }
                    }

                    _marginsApplied = true;
                    LogToFile("Applied visual compensation margins");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"Failed to apply margins: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets all applied margins to prevent cumulative expansion
        /// </summary>
        private static void ResetMargins(OverlayPopupWidget instance)
        {
            if (!_marginsApplied) return; // Nothing to reset

            try
            {
                var parentWidget = instance.ActionButtonsList?.ParentWidget;
                if (parentWidget != null)
                {
                    // Reset margins on all child widgets
                    for (int i = 0; i < parentWidget.ChildCount; i++)
                    {
                        var child = parentWidget.GetChild(i);
                        if (child != null && child != instance.ActionButtonsList)
                        {
                            try
                            {
                                child.MarginBottom = 0f;
                            }
                            catch { /* Continue if fails */ }
                        }
                    }

                    _marginsApplied = false;
                    LogToFile("Reset visual compensation margins");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"Failed to reset margins: {ex.Message}");
            }
        }
    }
}