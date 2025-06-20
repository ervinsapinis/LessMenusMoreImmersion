using System;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.GauntletUI.Widgets.Menu.Overlay;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Harmony patch to disable Talk/Visit buttons when player hasn't met the notable before.
    /// Uses "met before" logic instead of settlement access.
    /// </summary>
    [HarmonyPatch(typeof(OverlayPopupWidget), nameof(OverlayPopupWidget.SetCurrentCharacter))]
    internal static class PortraitActionBlocker
    {
        private static bool _marginsApplied = false;

        /// <summary>
        /// Patches OverlayPopupWidget.SetCurrentCharacter to disable action buttons when player hasn't met the notable
        /// </summary>
        [HarmonyPostfix]
        static void Postfix(OverlayPopupWidget __instance, object item)
        {
            try
            {
                // Reset margins first to prevent cumulative expansion
                ResetMargins(__instance);

                // Try to get the hero from the widget
                Hero targetHero = GetHeroFromWidget(item);

                if (targetHero == null)
                {
                    return; // No hero found, no blocking needed
                }

                // Check if player has met this hero before
                bool hasMetBefore = HasPlayerMetHero(targetHero);

                if (!hasMetBefore)
                {
                    // Hide buttons and add visual compensation
                    if (__instance.ActionButtonsList != null)
                    {
                        __instance.ActionButtonsList.IsVisible = false;
                        ApplyMargins(__instance);

                        // Show message to player
                        InformationManager.DisplayMessage(new InformationMessage(
                            "You need to be introduced to this person first."));
                    }
                }
                else
                {
                    // Ensure buttons are visible when player has met the hero
                    if (__instance.ActionButtonsList != null)
                    {
                        __instance.ActionButtonsList.IsVisible = true;
                    }
                }
            }
            catch (Exception ex)
            {
                // Silent fail to prevent crashes
            }
        }

        /// <summary>
        /// Attempts to extract a Hero from the widget item using various methods
        /// </summary>
        private static Hero GetHeroFromWidget(object item)
        {
            if (item == null) return null;

            try
            {
                // Method 1: Try to get hero by index
                var indexProp = item.GetType().GetProperty("Index");
                if (indexProp != null)
                {
                    var index = indexProp.GetValue(item);
                    if (index is int intIndex && Settlement.CurrentSettlement?.Notables != null
                        && intIndex >= 0 && intIndex < Settlement.CurrentSettlement.Notables.Count)
                    {
                        return Settlement.CurrentSettlement.Notables[intIndex];
                    }
                }

                // Method 2: Try to get hero by name matching
                var nameProp = item.GetType().GetProperty("Name");
                if (nameProp != null)
                {
                    var name = nameProp.GetValue(item);
                    if (name is string notableName && Settlement.CurrentSettlement?.Notables != null)
                    {
                        return Settlement.CurrentSettlement.Notables
                            .FirstOrDefault(n => n.Name.ToString() == notableName);
                    }
                }

                // Method 3: Try to get hero from Character property
                var characterProperty = item.GetType().GetProperty("Character");
                if (characterProperty != null)
                {
                    var character = characterProperty.GetValue(item);
                    if (character != null)
                    {
                        var heroProperty = character.GetType().GetProperty("HeroObject");
                        if (heroProperty != null)
                        {
                            return heroProperty.GetValue(character) as Hero;
                        }
                    }
                }
            }
            catch
            {
                // Continue to fallback if any method fails
            }

            // Fallback: use first notable (may not be accurate)
            var settlement = Settlement.CurrentSettlement;
            if (settlement?.Notables != null && settlement.Notables.Count > 0)
            {
                return settlement.Notables[0];
            }

            return null;
        }

        /// <summary>
        /// Checks if the player has met the specified hero before
        /// </summary>
        private static bool HasPlayerMetHero(Hero hero)
        {
            if (hero == null) return false;

            try
            {
                // Primary method: Check HasMet property
                var hasMetProperty = typeof(Hero).GetProperty("HasMet");
                if (hasMetProperty != null)
                {
                    return (bool)hasMetProperty.GetValue(hero);
                }

                // Fallback 1: Check if hero is known to player
                var isKnownProperty = typeof(Hero).GetProperty("IsKnownToPlayer");
                if (isKnownProperty != null)
                {
                    return (bool)isKnownProperty.GetValue(isKnownProperty);
                }

                // Fallback 2: Check if player has any relation with hero
                if (hero.GetRelationWithPlayer() != 0)
                {
                    return true;
                }

                // Fallback 3: Check if hero is allied (same clan/kingdom)
                if (hero.Clan == Clan.PlayerClan ||
                    (hero.Clan?.Kingdom != null && hero.Clan.Kingdom == Clan.PlayerClan.Kingdom))
                {
                    return true;
                }

                // Default: assume not met
                return false;
            }
            catch
            {
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
                            }
                            catch { /* Continue if fails */ }
                        }
                    }

                    _marginsApplied = true;
                }
            }
            catch
            {
                // Silent fail
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
                }
            }
            catch
            {
                // Silent fail
            }
        }
    }
}