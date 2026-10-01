using System;
using System.Linq;
using HarmonyLib;
using LessMenusMoreImmersion.Constants;
using LessMenusMoreImmersion.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade.GauntletUI.Widgets.Menu.Overlay;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Harmony patch that hides the Talk/Visit buttons on settlement menu portraits unless the
    /// player could plausibly walk up to that person: they must have met the hero, and must know
    /// the way to the part of the settlement the hero is currently in (tavern, keep, arena...).
    /// Notables never leave their settlement, so once met they stay reachable; lords and
    /// wanderers move between locations, so their buttons follow the discovery system.
    /// </summary>
    [HarmonyPatch(typeof(OverlayPopupWidget), nameof(OverlayPopupWidget.SetCurrentCharacter))]
    internal static class PortraitActionBlocker
    {
        private static bool _marginsApplied = false;

        [HarmonyPostfix]
        static void Postfix(OverlayPopupWidget __instance, GameMenuPartyItemButtonWidget item)
        {
            try
            {
                // Reset margins first to prevent cumulative expansion
                ResetMargins(__instance);

                if (__instance.ActionButtonsList == null) return;

                var settlement = Settlement.CurrentSettlement;
                Hero? targetHero = GetHeroFromWidget(item, settlement);

                if (targetHero == null || settlement == null)
                {
                    __instance.ActionButtonsList.IsVisible = true;
                    return; // Party entries or unresolved names: leave vanilla behavior alone.
                }

                bool hasMet = HasPlayerMetHero(targetHero);
                bool knowsWay = KnowsWayToHero(targetHero, settlement, out var heroLocation);
                TextObject? blockedText = null;

                if (!hasMet)
                {
                    blockedText = new TextObject("{=lmmi_portrait_not_met}You need to be introduced to this person first.");
                }
                else if (!knowsWay)
                {
                    blockedText = new TextObject("{=lmmi_portrait_location_unknown}{HERO} is in the {LOCATION}. You don't know the way there yet.");
                    blockedText.SetTextVariable("HERO", targetHero.Name);
                    blockedText.SetTextVariable("LOCATION", heroLocation!.Name);
                }

                LmmiLog.Info($"Portrait: '{targetHero.Name}' in {settlement.Name} location='{heroLocation?.StringId ?? "none"}' " +
                             $"met={hasMet} knowsWay={knowsWay} -> {(blockedText == null ? "open" : "blocked")}");

                if (blockedText != null)
                {
                    // Hide buttons and add visual compensation
                    __instance.ActionButtonsList.IsVisible = false;
                    ApplyMargins(__instance);
                    InformationManager.DisplayMessage(new InformationMessage(blockedText.ToString()));
                }
                else
                {
                    __instance.ActionButtonsList.IsVisible = true;
                }
            }
            catch (Exception ex)
            {
                // Never crash the game from a UI postfix, but do log so we can diagnose.
                LmmiLog.Error("PortraitActionBlocker.Postfix threw", ex);
            }
        }

        /// <summary>
        /// Resolves the hero behind a menu portrait. The widget only carries display strings, so we
        /// match its name against every hero present in the settlement: notables, heroes without a
        /// party (wanderers, prisoners, lords staying in the keep) and leaders of parties inside.
        /// </summary>
        private static Hero? GetHeroFromWidget(GameMenuPartyItemButtonWidget? item, Settlement? settlement)
        {
            if (item == null || settlement == null || item.IsPartyItem) return null;

            var name = item.Name;
            if (string.IsNullOrEmpty(name)) return null;

            try
            {
                var candidates = settlement.Notables
                    .Concat(settlement.HeroesWithoutParty)
                    .Concat(settlement.Parties.Select(p => p.LeaderHero))
                    .Where(h => h != null)
                    .ToList();

                // Exact match first. Title mods prepend to the displayed name
                // (Titles shows "Emir Count Adram" for Adram), so fall back to a suffix match.
                var hero = candidates.FirstOrDefault(h => h.Name?.ToString() == name)
                        ?? candidates.FirstOrDefault(h => EndsWithName(name, h.Name?.ToString()))
                        ?? candidates.FirstOrDefault(h => EndsWithName(name, h.FirstName?.ToString()));
                if (hero == null)
                    LmmiLog.Info($"Portrait: no hero named '{name}' found in {settlement.Name} — leaving buttons to vanilla.");
                return hero;
            }
            catch (Exception ex)
            {
                LmmiLog.Debug($"GetHeroFromWidget failed for '{name}': {ex.Message}");
                return null;
            }
        }

        private static bool EndsWithName(string displayedName, string? heroName) =>
            !string.IsNullOrEmpty(heroName) && displayedName.EndsWith(" " + heroName, StringComparison.Ordinal);

        /// <summary>
        /// True if the player knows the way to wherever the hero currently is in this settlement.
        /// Locations without a discoverable feature (e.g. the town center) are always reachable.
        /// </summary>
        private static bool KnowsWayToHero(Hero hero, Settlement settlement, out Location? heroLocation)
        {
            heroLocation = (settlement.LocationComplex ?? LocationComplex.Current)?.GetLocationOfCharacter(hero);
            if (heroLocation == null) return true;

            if (!SettlementMenuOptions.LocationFeatureMap.TryGetValue(heroLocation.StringId, out var feature))
                return true;

            var discovery = Campaign.Current?.GetCampaignBehavior<DisableMenuBehavior>();
            return discovery == null || discovery.HasFeatureAccess(settlement, feature);
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
                    return (bool)isKnownProperty.GetValue(hero);
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
            catch (Exception ex)
            {
                LmmiLog.Debug($"HasPlayerMetHero threw, defaulting to not-met: {ex.Message}");
                return false;
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
            catch (Exception ex)
            {
                LmmiLog.Debug($"ApplyMargins failed: {ex.Message}");
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
            catch (Exception ex)
            {
                LmmiLog.Debug($"ResetMargins failed: {ex.Message}");
            }
        }
    }
}