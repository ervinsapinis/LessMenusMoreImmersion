using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.MountAndBlade;
using LessMenusMoreImmersion.Constants;
using LessMenusMoreImmersion.Logging;
using TaleWorlds.Core;
using TaleWorlds.CampaignSystem.Settlements.Locations;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Campaign behavior that lazily applies nameplate Harmony patches on the first mission tick.
    /// Using MissionTickEvent guarantees SandBox.GauntletUI is loaded before we search for it.
    /// </summary>
    public class AltOverlayBlockerBehavior : CampaignBehaviorBase
    {
        private static bool _patchesApplied;

        public override void RegisterEvents()
        {
            CampaignEvents.MissionTickEvent.AddNonSerializedListener(this, OnMissionTick);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnMissionTick(float dt)
        {
            if (_patchesApplied) return;
            NameMarkerPatches.TryApply();
            _patchesApplied = true;
        }
    }

    /// <summary>
    /// Locates MissionGauntletNameMarkerView and applies Prefix + Postfix patches at runtime.
    /// </summary>
    internal static class NameMarkerPatches
    {
        public static void TryApply()
        {
            try
            {
                // Locate the SandBox.GauntletUI assembly (loaded by Bannerlord at this point)
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "SandBox.GauntletUI");

                if (assembly == null)
                {
                    // Log related assemblies to help diagnose name mismatch
                    var candidates = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetName().Name)
                        .Where(n => n != null && (n.Contains("Sand") || n.Contains("Gauntlet") || n.Contains("NameMarker")))
                        .ToList();
                    LmmiLog.Warning($"NameMarkerPatches: SandBox.GauntletUI not found. Candidates: [{string.Join(", ", candidates)}]");
                    return;
                }

                // Find the name marker view type
                var viewType = assembly.GetType("SandBox.GauntletUI.Missions.MissionGauntletNameMarkerView");
                if (viewType == null)
                {
                    var nameMarkerTypes = assembly.GetTypes()
                        .Where(t => t.Name.Contains("NameMarker") || t.Name.Contains("Marker"))
                        .Select(t => t.FullName)
                        .ToList();
                    LmmiLog.Warning($"NameMarkerPatches: MissionGauntletNameMarkerView not found. Types with 'Marker': [{string.Join(", ", nameMarkerTypes)}]");
                    return;
                }

                // Find OnMissionScreenTick
                var tickMethod = viewType.GetMethod("OnMissionScreenTick", BindingFlags.Public | BindingFlags.Instance);
                if (tickMethod == null)
                {
                    var methods = viewType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Select(m => m.Name).ToList();
                    LmmiLog.Warning($"NameMarkerPatches: OnMissionScreenTick not found. Public instance methods: [{string.Join(", ", methods)}]");
                    return;
                }

                // Hand the type to the filter patch for reflection caching
                NameMarkerFilterPatch.Initialize(viewType);

                var harmony = new Harmony("LessMenusMoreImmersion.NameMarker");

                var prefix = typeof(NameMarkerViewPatch)
                    .GetMethod(nameof(NameMarkerViewPatch.Prefix), BindingFlags.Static | BindingFlags.Public);
                var postfix = typeof(NameMarkerFilterPatch)
                    .GetMethod(nameof(NameMarkerFilterPatch.Postfix), BindingFlags.Static | BindingFlags.Public);

                harmony.Patch(tickMethod,
                    prefix: prefix != null ? new HarmonyMethod(prefix) : null,
                    postfix: postfix != null ? new HarmonyMethod(postfix) : null);

                LmmiLog.Info($"NameMarkerPatches: Patches applied to {viewType.Name}.{tickMethod.Name}.");
            }
            catch (Exception ex)
            {
                LmmiLog.Error("NameMarkerPatches.TryApply failed", ex);
            }
        }
    }

    /// <summary>
    /// Prefix: completely block nameplate updates in undiscovered sub-locations (tavern, smithy, etc.)
    /// </summary>
    internal static class NameMarkerViewPatch
    {
        public static bool Prefix()
        {
            try
            {
                if (Campaign.Current == null || Mission.Current == null) return true;

                var settlement = Settlement.CurrentSettlement;
                if (settlement == null) return true;

                var accessBehavior = Campaign.Current.GetCampaignBehavior<DisableMenuBehavior>();
                if (accessBehavior == null) return true;

                if (accessBehavior.HasAccessToSettlement(settlement)) return true;

                var locationId = CampaignMission.Current?.Location?.StringId;
                // Never block in the center — Postfix handles per-agent filtering there
                if (string.IsNullOrEmpty(locationId) || locationId == "center") return true;

                if (SettlementMenuOptions.LocationFeatureMap.TryGetValue(locationId!, out var feature))
                    return accessBehavior.HasFeatureAccess(settlement, feature);

                return true;
            }
            catch
            {
                return true;
            }
        }
    }

    /// <summary>
    /// Postfix: in the town center, selectively hide nameplates for undiscovered NPCs.
    /// </summary>
    internal static class NameMarkerFilterPatch
    {
        private static Type? _markerViewType;
        private static FieldInfo? _dataSourceField;
        private static PropertyInfo? _targetsProperty;
        private static PropertyInfo? _isEnabledProperty;
        private static bool _reflectionReady;
        private static bool _reflectionFailed;
        
        // Cache for generic properties (Target property depends on runtime subclass)
        private static readonly System.Collections.Generic.Dictionary<Type, PropertyInfo?> _targetPropertiesCache = new();
        private static readonly System.Collections.Generic.Dictionary<Type, FieldInfo?> _identifierFieldsCache = new();
        private static readonly System.Collections.Generic.HashSet<string> _loggedMarkers = new();

        public static void Initialize(Type markerViewType)
        {
            _markerViewType = markerViewType;
            _reflectionReady = false;
            _reflectionFailed = false;
        }

        public static void Postfix(object __instance)
        {
            if (_reflectionFailed) return;

            try
            {
                if (!_reflectionReady)
                {
                    CacheReflection();
                    if (_reflectionFailed) return;
                }

                var settlement = Settlement.CurrentSettlement;
                if (settlement == null || !settlement.IsTown) return;

                var accessBehavior = Campaign.Current?.GetCampaignBehavior<DisableMenuBehavior>();
                if (accessBehavior == null) return;

                // Full settlement access = show everything
                if (accessBehavior.HasAccessToSettlement(settlement)) return;

                // Only filter in the town center
                var locationId = CampaignMission.Current?.Location?.StringId;
                if (locationId != "center") return;

                var dataSource = _dataSourceField!.GetValue(__instance);
                if (dataSource == null) return;

                var targets = _targetsProperty!.GetValue(dataSource) as System.Collections.IList;
                if (targets == null) return;

                foreach (var target in targets)
                {
                    if (target == null) continue;
                    
                    var targetType = target.GetType();

                    // Check if it's an Agent marker (e.g., MissionNameMarkerTargetVM<Agent>)
                    if (!_targetPropertiesCache.TryGetValue(targetType, out var targetProp))
                    {
                        targetProp = targetType.GetProperty("Target", BindingFlags.Public | BindingFlags.Instance);
                        _targetPropertiesCache[targetType] = targetProp;
                    }

                    if (targetProp != null)
                    {
                        var agent = targetProp.GetValue(target) as Agent;
                        if (agent != null)
                        {
                            if (!ShouldShowAgent(agent, accessBehavior, settlement))
                                _isEnabledProperty!.SetValue(target, false);
                            continue;
                        }
                    }

                    // Since it wasn't an Agent, let's see what it actually is!
                    // Deep inspection logging for all non-Agent markers
                    var nameProp = targetType.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
                    var iconTypeProp = targetType.GetProperty("IconType", BindingFlags.Public | BindingFlags.Instance);
                    
                    var targetName = nameProp?.GetValue(target) as string ?? "unknown";
                    var iconType = iconTypeProp?.GetValue(target) as string ?? "unknown";
                    
                    // See what the Target property holds, if any
                    string targetInfo = "none";
                    if (targetProp != null)
                    {
                        var targetObj = targetProp.GetValue(target);
                        if (targetObj != null)
                            targetInfo = targetObj.GetType().Name;
                        else
                            targetInfo = "null";
                    }

                    // Does it have an Identifier field?
                    if (!_identifierFieldsCache.TryGetValue(targetType, out var identifierField))
                    {
                        identifierField = targetType.GetField("Identifier", BindingFlags.Public | BindingFlags.Instance);
                        _identifierFieldsCache[targetType] = identifierField;
                    }
                    string identifierVal = identifierField?.GetValue(target) as string ?? "none";

                    // Log this unique marker type once
                    var logKey = $"Marker_{targetType.Name}_{targetName}";
                    if (!_loggedMarkers.Contains(logKey))
                    {
                        LmmiLog.Info($"NON-AGENT MARKER: Type='{targetType.Name}', Name='{targetName}', IconType='{iconType}', TargetObj='{targetInfo}', Identifier='{identifierVal}'");
                        _loggedMarkers.Add(logKey);
                    }

                    // Temporary hardcoded filter based on Name (since we know the localized names like "The Tavern" etc)
                    // We will refine this once we see the exact output in the log
                    if (!string.IsNullOrEmpty(targetName))
                    {
                        string lowerName = targetName.ToLowerInvariant();
                        if (lowerName.Contains("tavern"))
                        {
                            if (!accessBehavior.HasFeatureAccess(settlement, SettlementMenuOptions.Features.Backstreet))
                                _isEnabledProperty!.SetValue(target, false);
                        }
                        else if (lowerName.Contains("arena"))
                        {
                            if (!accessBehavior.HasFeatureAccess(settlement, SettlementMenuOptions.Features.Arena))
                                _isEnabledProperty!.SetValue(target, false);
                        }
                        else if (lowerName.Contains("lord") || lowerName.Contains("keep") || lowerName.Contains("dungeon") || lowerName.Contains("prison"))
                        {
                            if (!accessBehavior.HasFeatureAccess(settlement, SettlementMenuOptions.Features.Keep))
                                _isEnabledProperty!.SetValue(target, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LmmiLog.Warning($"NameMarkerFilterPatch.Postfix error: {ex.Message}");
                _reflectionFailed = true;
            }
        }

        private static void CacheReflection()
        {
            if (_markerViewType == null)
            {
                LmmiLog.Warning("NameMarkerFilterPatch.CacheReflection: _markerViewType is null.");
                _reflectionFailed = true;
                return;
            }

            try
            {
                // Try known field names for the ViewModel
                _dataSourceField =
                    _markerViewType.GetField("_dataSource", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? _markerViewType.GetField("_missionNameMarkerVM", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? _markerViewType.GetField("_nameMarkerVM", BindingFlags.NonPublic | BindingFlags.Instance);

                if (_dataSourceField == null)
                {
                    var allFields = _markerViewType
                        .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(f => $"{f.Name}({f.FieldType.Name})").ToList();
                    LmmiLog.Warning($"NameMarkerFilterPatch: VM field not found. NonPublic instance fields: [{string.Join(", ", allFields)}]");
                    _reflectionFailed = true;
                    return;
                }

                LmmiLog.Info($"NameMarkerFilterPatch: Using VM field '{_dataSourceField.Name}' ({_dataSourceField.FieldType.Name})");

                var vmType = _dataSourceField.FieldType;

                // Try known property names for the Targets list
                _targetsProperty =
                    vmType.GetProperty("Targets", BindingFlags.Public | BindingFlags.Instance)
                    ?? vmType.GetProperty("MarkerList", BindingFlags.Public | BindingFlags.Instance);

                if (_targetsProperty == null)
                {
                    var allProps = vmType
                        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Select(p => $"{p.Name}({p.PropertyType.Name})").ToList();
                    LmmiLog.Warning($"NameMarkerFilterPatch: Targets property not found on {vmType.Name}. Props: [{string.Join(", ", allProps)}]");
                    _reflectionFailed = true;
                    return;
                }

                LmmiLog.Info($"NameMarkerFilterPatch: Using targets property '{_targetsProperty.Name}'");

                var genericArgs = _targetsProperty.PropertyType.GetGenericArguments();
                if (genericArgs.Length == 0)
                {
                    LmmiLog.Warning($"NameMarkerFilterPatch: '{_targetsProperty.Name}' has no generic type args (type={_targetsProperty.PropertyType.Name}).");
                    _reflectionFailed = true;
                    return;
                }

                var itemType = genericArgs[0];
                LmmiLog.Info($"NameMarkerFilterPatch: Target base item type: {itemType.FullName}");

                _isEnabledProperty = itemType.GetProperty("IsEnabled", BindingFlags.Public | BindingFlags.Instance);

                if (_isEnabledProperty == null)
                {
                    var props = itemType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Select(p => p.Name).ToList();
                    LmmiLog.Warning($"NameMarkerFilterPatch: Missing IsEnabled on {itemType.Name}. All props: [{string.Join(", ", props)}]");
                    _reflectionFailed = true;
                    return;
                }

                _reflectionReady = true;
                LmmiLog.Info("NameMarkerFilterPatch: Base reflection cached. Subclass properties will be resolved dynamically.");
            }
            catch (Exception ex)
            {
                LmmiLog.Warning($"NameMarkerFilterPatch.CacheReflection exception: {ex.Message}");
                _reflectionFailed = true;
            }
        }

        private static bool ShouldShowAgent(Agent agent, DisableMenuBehavior accessBehavior, Settlement settlement)
        {
            // Always show the player
            if (agent.IsPlayerControlled) return true;

            if (agent.IsHero)
            {
                var charObj = agent.Character as CharacterObject;
                var hero = charObj?.HeroObject;
                if (hero == null) return true; // Unknown hero — show safely

                // Companions and clan members always visible
                if (hero.Clan == Clan.PlayerClan) return true;

                // Other heroes (lords/notables) — only if the Keep is discovered
                return accessBehavior.HasFeatureAccess(settlement, SettlementMenuOptions.Features.Keep);
            }

            var character = agent.Character as CharacterObject;
            if (character == null) return false;

            switch (character.Occupation)
            {
                case Occupation.ShopWorker:
                case Occupation.GoodsTrader:
                case Occupation.Merchant:
                    return accessBehavior.HasFeatureAccess(settlement, SettlementMenuOptions.Features.Trade);

                case Occupation.Tavernkeeper:
                case Occupation.TavernWench:
                case Occupation.TavernGameHost:
                case Occupation.GangLeader:
                case Occupation.Gangster:
                    return accessBehavior.HasFeatureAccess(settlement, SettlementMenuOptions.Features.Backstreet);

                case Occupation.Weaponsmith:
                case Occupation.Armorer:
                case Occupation.Blacksmith:
                    return accessBehavior.HasFeatureAccess(settlement, SettlementMenuOptions.Features.Smithy);
            }

            // Fallback string-ID checks for smithy/arena NPCs without a distinct occupation
            if (character.StringId != null)
            {
                if (character.StringId.IndexOf("smith", StringComparison.OrdinalIgnoreCase) >= 0)
                    return accessBehavior.HasFeatureAccess(settlement, SettlementMenuOptions.Features.Smithy);

                if (character.StringId.IndexOf("arena", StringComparison.OrdinalIgnoreCase) >= 0)
                    return accessBehavior.HasFeatureAccess(settlement, SettlementMenuOptions.Features.Arena);
            }

            // Default: random townsfolk — hide
            return false;
        }

        private static bool ShouldShowLocationIdentifier(string identifier, DisableMenuBehavior accessBehavior, Settlement settlement)
        {
            if (string.IsNullOrEmpty(identifier)) return true;

            // "tavern", "arena", "lordshall", etc. map to feature names
            if (SettlementMenuOptions.LocationFeatureMap.TryGetValue(identifier, out var feature))
                return accessBehavior.HasFeatureAccess(settlement, feature);

            return true;
        }
    }
}
