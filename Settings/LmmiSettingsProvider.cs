using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using LessMenusMoreImmersion.Logging;

namespace LessMenusMoreImmersion.Settings
{
    /// <summary>
    /// Runtime accessor for Less Menus More Immersion configuration values.
    ///
    /// When MCM is present, <see cref="RegisterWithMcm"/> builds a fluent settings
    /// screen. MCM types only appear inside method bodies (never in type signatures,
    /// field types, or base classes), so <c>Assembly.GetTypes()</c> never tries to
    /// resolve MCM — the mod loads cleanly whether MCM is installed or not.
    /// </summary>
    public static class LmmiSettingsProvider
    {
        // ---- Defaults ----
        public const bool DefaultEnableDynamicDiscovery = true;
        public const bool DefaultShowDiscoveryMessages = true;
        public const int DefaultGuideCostBase = 500;
        public const int DefaultVillageArrangementCost = 200;
        public const int DefaultFullAccessClanTier = 5;
        public const int DefaultKingdomAccessClanTier = 3;
        public const bool DefaultVerboseLogging = false;

        // ---- Backing store (same pattern as RBMAlternateXbow) ----
        private static readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);
        private static bool _mcmRegistered;

        static LmmiSettingsProvider()
        {
            _values["EnableDynamicDiscovery"] = DefaultEnableDynamicDiscovery;
            _values["ShowDiscoveryMessages"] = DefaultShowDiscoveryMessages;
            _values["GuideCostBase"] = DefaultGuideCostBase;
            _values["VillageArrangementCost"] = DefaultVillageArrangementCost;
            _values["FullAccessClanTier"] = DefaultFullAccessClanTier;
            _values["KingdomAccessClanTier"] = DefaultKingdomAccessClanTier;
            _values["VerboseLogging"] = DefaultVerboseLogging;
        }

        private static T Get<T>(string key) =>
            _values.TryGetValue(key, out var v) ? (T)v : default!;

        private static void Set<T>(string key, T value) =>
            _values[key] = value!;

        // ---- Public getters used by the rest of the mod ----
        public static bool EnableDynamicDiscovery => Get<bool>("EnableDynamicDiscovery");
        public static bool ShowDiscoveryMessages => Get<bool>("ShowDiscoveryMessages");
        public static int GuideCostBase => Get<int>("GuideCostBase");
        public static int VillageArrangementCost => Get<int>("VillageArrangementCost");
        public static int FullAccessClanTier => Get<int>("FullAccessClanTier");
        public static int KingdomAccessClanTier => Get<int>("KingdomAccessClanTier");
        public static bool VerboseLogging => Get<bool>("VerboseLogging");

        /// <summary>
        /// Wires the logger's verbose-query delegate.
        /// </summary>
        public static void AttachVerboseQueryToLogger()
        {
            LmmiLog.VerboseLoggingQuery = () => VerboseLogging;
        }

        /// <summary>
        /// Checks whether MCM is loaded and, if so, registers the fluent settings screen.
        /// Safe to call multiple times — only registers once.
        /// </summary>
        /// <returns>True if MCM settings were registered.</returns>
        public static bool TryRegisterMcm()
        {
            if (_mcmRegistered) return true;

            try
            {
                bool mcmLoaded = AppDomain.CurrentDomain.GetAssemblies()
                    .Any(a =>
                    {
                        var name = a.GetName().Name ?? string.Empty;
                        return name.StartsWith("MCMv", StringComparison.OrdinalIgnoreCase);
                    });

                if (!mcmLoaded)
                {
                    LmmiLog.Info("MCM not detected — using default settings.");
                    return false;
                }

                _mcmRegistered = RegisterWithMcm();
                if (_mcmRegistered)
                    LmmiLog.Info("MCM settings registered successfully.");
                else
                    LmmiLog.Warning("MCM detected but registration returned false — using defaults.");

                return _mcmRegistered;
            }
            catch (Exception ex)
            {
                LmmiLog.Warning($"MCM registration failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Builds and registers the MCM fluent settings screen. MCM types are only
        /// referenced inside this method body, so <c>Assembly.GetTypes()</c> on our
        /// DLL never resolves them.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool RegisterWithMcm()
        {
            var builder = MCM.Abstractions.FluentBuilder.BaseSettingsBuilder
                .Create("LessMenusMoreImmersion_v1", "Less Menus More Immersion");

            if (builder == null) return false;

            builder
                .SetFolderName("LessMenusMoreImmersion")
                .SetFormat("json");

            // ===== GENERAL =====
            builder.CreateGroup("General", group =>
            {
                group.SetGroupOrder(0);
                var order = 0;

                group.AddBool(
                    "EnableDynamicDiscovery",
                    "Enable dynamic discovery",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("EnableDynamicDiscovery"),
                        v => Set("EnableDynamicDiscovery", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("Unlock settlement menus by exploring (entering scenes, talking to merchants/smiths). When disabled, only the local guide and clan tier grant access."));

                group.AddBool(
                    "ShowDiscoveryMessages",
                    "Show discovery messages",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("ShowDiscoveryMessages"),
                        v => Set("ShowDiscoveryMessages", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("Display a message when you discover a new part of a settlement."));

                group.AddInteger(
                    "GuideCostBase",
                    "Local guide base cost",
                    minValue: 0,
                    maxValue: 5000,
                    new MCM.Common.ProxyRef<int>(
                        () => Get<int>("GuideCostBase"),
                        v => Set("GuideCostBase", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("Base cost paid to a local guide to reveal a full town. Scales with clan tier."));

                group.AddInteger(
                    "VillageArrangementCost",
                    "Village arrangement cost",
                    minValue: 0,
                    maxValue: 2000,
                    new MCM.Common.ProxyRef<int>(
                        () => Get<int>("VillageArrangementCost"),
                        v => Set("VillageArrangementCost", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("Fixed cost paid to a village trader to establish a trading arrangement."));
            });

            // ===== AUTOMATIC ACCESS =====
            builder.CreateGroup("Automatic Access", group =>
            {
                group.SetGroupOrder(1);
                var order = 0;

                group.AddInteger(
                    "FullAccessClanTier",
                    "Full access clan tier",
                    minValue: 0,
                    maxValue: 7,
                    new MCM.Common.ProxyRef<int>(
                        () => Get<int>("FullAccessClanTier"),
                        v => Set("FullAccessClanTier", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("At this clan tier, every settlement is automatically unlocked. Set to 7 to disable."));

                group.AddInteger(
                    "KingdomAccessClanTier",
                    "Same-kingdom access clan tier",
                    minValue: 0,
                    maxValue: 7,
                    new MCM.Common.ProxyRef<int>(
                        () => Get<int>("KingdomAccessClanTier"),
                        v => Set("KingdomAccessClanTier", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("At this clan tier, settlements owned by your kingdom's clans are automatically unlocked. Set to 7 to disable."));
            });

            // ===== DEBUG =====
            builder.CreateGroup("Debug", group =>
            {
                group.SetGroupOrder(2);

                group.AddBool(
                    "VerboseLogging",
                    "Verbose logging",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("VerboseLogging"),
                        v => Set("VerboseLogging", v)),
                    b => b.SetOrder(0)
                        .SetHintText("Enables detailed debug messages in lmmi.log. Leave off for normal play."));
            });

            var settings = builder.BuildAsGlobal();
            if (settings == null) return false;

            settings.Register();
            return true;
        }
    }
}
