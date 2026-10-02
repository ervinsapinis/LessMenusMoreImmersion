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
        public const bool DefaultEnableNotableDisposition = true;
        public const int DefaultForeignerPrejudicePercent = 100;
        public const int DefaultFavorCooldownPercent = 100;
        public const bool DefaultShowDispositionBreakdown = false;
        public const bool DefaultEnableArrivalScenes = true;
        public const bool DefaultEnableStreetEvents = true;
        public const int DefaultCrowdDensityPercent = 200;
        public const int DefaultStreetAmbushPercent = 30;
        public const int DefaultStreetEventChancePercent = 70;
        public const bool DefaultEnableHiredSwords = true;
        public const bool DefaultAllowPlayerExecution = true;
        public const int DefaultGuardKillsForHeadsman = 2;
        public const bool DefaultEnableCastleLife = true;
        public const bool DefaultEnableHallCourt = true;
        public const bool DefaultEnableCamp = true;
        public const bool DefaultEnableLordsTalk = true;
        public const bool DefaultEnableWordTravels = true;
        public const bool DefaultEnableTavernGames = true;
        public const bool DefaultEnableTreachery = true;
        public const bool DefaultEnableAmbushes = true;
        public const bool DefaultTestMode = false;
        public const bool DefaultTestImmortal = false;
        public const int DefaultTestDamagePercent = 100;

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
            _values["ShowDebugOptions"] = false;
            _values["ShowTestingOptions"] = false;
            _values["EnableNotableDisposition"] = DefaultEnableNotableDisposition;
            _values["ForeignerPrejudicePercent"] = DefaultForeignerPrejudicePercent;
            _values["FavorCooldownPercent"] = DefaultFavorCooldownPercent;
            _values["ShowDispositionBreakdown"] = DefaultShowDispositionBreakdown;
            _values["EnableArrivalScenes"] = DefaultEnableArrivalScenes;
            _values["EnableStreetEvents"] = DefaultEnableStreetEvents;
            _values["CrowdDensityPercent"] = DefaultCrowdDensityPercent;
            _values["StreetAmbushPercent"] = DefaultStreetAmbushPercent;
            _values["StreetEventChancePercent"] = DefaultStreetEventChancePercent;
            _values["EnableHiredSwords"] = DefaultEnableHiredSwords;
            _values["AllowPlayerExecution"] = DefaultAllowPlayerExecution;
            _values["GuardKillsForHeadsman"] = DefaultGuardKillsForHeadsman;
            _values["EnableCastleLife"] = DefaultEnableCastleLife;
            _values["EnableHallCourt"] = DefaultEnableHallCourt;
            _values["EnableCamp"] = DefaultEnableCamp;
            _values["EnableLordsTalk"] = DefaultEnableLordsTalk;
            _values["EnableWordTravels"] = DefaultEnableWordTravels;
            _values["EnableTavernGames"] = DefaultEnableTavernGames;
            _values["EnableTreachery"] = DefaultEnableTreachery;
            _values["EnableAmbushes"] = DefaultEnableAmbushes;
            _values["TestMode"] = DefaultTestMode;
            _values["TestImmortal"] = DefaultTestImmortal;
            _values["TestDamagePercent"] = DefaultTestDamagePercent;
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
        public static bool EnableNotableDisposition => Get<bool>("EnableNotableDisposition");
        public static int ForeignerPrejudicePercent => Get<int>("ForeignerPrejudicePercent");
        public static int FavorCooldownPercent => Get<int>("FavorCooldownPercent");
        public static bool ShowDispositionBreakdown => Get<bool>("ShowDispositionBreakdown");
        public static bool EnableArrivalScenes => Get<bool>("EnableArrivalScenes");
        public static bool EnableStreetEvents => Get<bool>("EnableStreetEvents");
        public static int CrowdDensityPercent => Get<int>("CrowdDensityPercent");
        public static int StreetAmbushPercent => Get<int>("StreetAmbushPercent");
        public static int StreetEventChancePercent => Get<int>("StreetEventChancePercent");
        public static bool EnableHiredSwords => Get<bool>("EnableHiredSwords");
        public static bool AllowPlayerExecution => Get<bool>("AllowPlayerExecution");
        public static int GuardKillsForHeadsman => Get<int>("GuardKillsForHeadsman");
        public static bool EnableCastleLife => Get<bool>("EnableCastleLife");
        public static bool EnableHallCourt => Get<bool>("EnableHallCourt");
        public static bool EnableCamp => Get<bool>("EnableCamp");
        public static bool EnableLordsTalk => Get<bool>("EnableLordsTalk");
        public static bool EnableWordTravels => Get<bool>("EnableWordTravels");
        public static bool EnableTavernGames => Get<bool>("EnableTavernGames");
        public static bool EnableTreachery => Get<bool>("EnableTreachery");
        public static bool EnableAmbushes => Get<bool>("EnableAmbushes");
        public static bool TestMode => Get<bool>("TestMode");
        public static bool TestImmortal => Get<bool>("TestImmortal");
        public static int TestDamagePercent => Get<int>("TestDamagePercent");
        public static string TestStreetEventKind
        {
            get
            {
                var names = Behaviors.StreetEventsBehavior.KindNames;
                int i = _values.TryGetValue("TestStreetEventKind", out var v) && v is int n ? n : 0;
                return names[Math.Max(0, Math.Min(names.Length - 1, i))];
            }
        }

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
                    "CrowdDensityPercent",
                    "Crowds in towns and villages (%)",
                    minValue: 50,
                    maxValue: 400,
                    new MCM.Common.ProxyRef<int>(
                        () => Get<int>("CrowdDensityPercent"),
                        v => Set("CrowdDensityPercent", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("How many ordinary townsfolk and villagers fill the streets, as a percentage of vanilla (100 = vanilla, 200 = twice as many). Scenes still cap it by their spawn points and the game's own civilian-count option (Options > Performance). More people cost frames. Applies next time you enter."));

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

            // ===== NOTABLES =====
            builder.CreateGroup("Notables", group =>
            {
                group.SetGroupOrder(2);
                var order = 0;

                group.AddBool(
                    "EnableNotableDisposition",
                    "Notables judge you",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("EnableNotableDisposition"),
                        v => Set("EnableNotableDisposition", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("Notables weigh your relation, clan standing and culture before helping you, and do favors in person for those they regard well. When disabled, notables always help and offer no favors."));

                group.AddInteger(
                    "ForeignerPrejudicePercent",
                    "Foreigner prejudice (%)",
                    minValue: 0,
                    maxValue: 200,
                    new MCM.Common.ProxyRef<int>(
                        () => Get<int>("ForeignerPrejudicePercent"),
                        v => Set("ForeignerPrejudicePercent", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("How much notables hold your foreign culture against you. 0 = not at all, 100 = default, 200 = deeply xenophobic."));

                group.AddInteger(
                    "FavorCooldownPercent",
                    "Favor cooldown (%)",
                    minValue: 25,
                    maxValue: 400,
                    new MCM.Common.ProxyRef<int>(
                        () => Get<int>("FavorCooldownPercent"),
                        v => Set("FavorCooldownPercent", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("Scales how long a notable waits before doing the same favor again. 100 = default."));

                group.AddBool(
                    "EnableArrivalScenes",
                    "Notables come to you",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("EnableArrivalScenes"),
                        v => Set("EnableArrivalScenes", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("When you walk into a town or village, a notable may walk up to you: a friend who missed you throws a feast, a friend warns you about a schemer, or someone who despises you tells you to leave."));

                group.AddBool(
                    "EnableStreetEvents",
                    "Street events",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("EnableStreetEvents"),
                        v => Set("EnableStreetEvents", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("In town and village centres, a local may run up for help: men cornering a girl, two lads brawling, a cutpurse running off, a man who fell and can't get up. Agree and they lead you there. Stepping up makes prejudice count for less in that town for 60 days. Any of them may be a setup."));

                group.AddInteger(
                    "StreetAmbushPercent",
                    "Street events that are setups (%)",
                    minValue: 0,
                    maxValue: 50,
                    new MCM.Common.ProxyRef<int>(
                        () => Get<int>("StreetAmbushPercent"),
                        v => Set("StreetAmbushPercent", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("The base chance that a plea for help is bait: the same plea and the same scene, until you get there and toughs close in. Higher for a foreigner where your people are resented and for a known soft touch; lower for a famous name, and for a while after a setup in that town. 0 = never."));

                group.AddInteger(
                    "StreetEventChancePercent",
                    "Street event chance (%)",
                    minValue: 0,
                    maxValue: 100,
                    new MCM.Common.ProxyRef<int>(
                        () => Get<int>("StreetEventChancePercent"),
                        v => Set("StreetEventChancePercent", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("The chance that something happens on the street each time the town or village rolls for it (every three to four minutes you spend in the square). 0 = never."));

                group.AddBool(
                    "EnableCastleLife",
                    "Castle life",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("EnableCastleLife"),
                        v => Set("EnableCastleLife", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("In castle courtyards: a master-at-arms (bouts with the garrison, wagers, training your companions, drilling your own garrison) and the castellan (news from the roads, veterans the lord can spare, your own castle's report); and the garrison's day around you — drill, dice, prisoners, couriers."));

                group.AddBool(
                    "EnableHallCourt",
                    "Court in the lord's hall",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("EnableHallCourt"),
                        v => Set("EnableHallCourt", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("In lords' halls: petitioners come before the lord, and you may speak for them (vanilla persuasion); in your own hall they come to you, and your judgments move loyalty, security and the town's opinion of you. Lords in the hall trade gossip — wars, prisoners, armies on the move."));

                group.AddBool(
                    "EnableCamp",
                    "Walk around camp (Ctrl+T)",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("EnableCamp"),
                        v => Set("EnableCamp", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("On the map, Ctrl+T makes camp where you stand, on the battle terrain of that place: your companions and men by the fires, to talk to, drink with, tell a story to, spar with or drill. Tab to break camp. Like Warband's 'Take a walk around'."));

                group.AddBool(
                    "EnableLordsTalk",
                    "Lords talk about you",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("EnableLordsTalk"),
                        v => Set("EnableLordsTalk", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("Weekly, a dishonorable enemy (relation -30) may slander you to friends at court, and a true friend (40+) may speak up for you: their friends' relation with you drifts a point or two. If hired swords named who sent them, you can take it to that person's liege (vanilla persuasion). A gang leader who'll deal with you can find men to rough up an enemy of yours, for a price."));

                group.AddBool(
                    "EnableWordTravels",
                    "Word of your deeds travels",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("EnableWordTravels"),
                        v => Set("EnableWordTravels", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("Town standing gained or lost in one place spreads, at a quarter, to up to four towns and castles of the same people within a day's ride: a regional name."));

                group.AddBool(
                    "EnableTavernGames",
                    "Dice in taverns",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("EnableTavernGames"),
                        v => Set("EnableTavernGames", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("Tavern patrons will dice with you for 10, 50 or 100 (two dice each, high roll wins). With Roguery 30+ you can bring your own dice — until you're caught."));

                group.AddBool(
                    "EnableTreachery",
                    "Treacherous notables",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("EnableTreachery"),
                        v => Set("EnableTreachery", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("Dishonorable notables who aren't your friends may cheat you: false trade tips, or taking your coin and tipping off the watch. A friend's warning or Roguery 60+ lets you see it coming."));

                group.AddBool(
                    "EnableAmbushes",
                    "Grudges turn violent",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("EnableAmbushes"),
                        v => Set("EnableAmbushes", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("Cruel notables with a grudge, whose warning you defied, or whose loan you left unpaid may send men after you when you leave (never against parties over 60). A cruel gang leader who despises you may have your purse lifted in the tavern district at night."));

                group.AddBool(
                    "EnableHiredSwords",
                    "Hired swords",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("EnableHiredSwords"),
                        v => Set("EnableHiredSwords", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("A lord or notable who hates you (relation -25 or worse, no Honor or Mercy to stop them) may pay sellswords of their culture, scaled to your clan tier, to find you on the road: a beating at -25, or dragged before them at -50. A cunning lord at war with you may pay them to bring you in as a prisoner. Beat them, outbid whoever paid, or lose and face the consequences. Never against a party over 100."));

                group.AddBool(
                    "AllowPlayerExecution",
                    "You can be executed",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("AllowPlayerExecution"),
                        v => Set("AllowPlayerExecution", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("A cruel enemy (Mercy below 0, relation -60 or worse) who has you dragged before them may have you killed unless you talk them out of it (a hard vanilla persuasion); and a town's headsman waits for anyone who kills enough of its watch (below clan tier 3). Off: held, robbed or a long sentence instead. Your heir carries on, as with any death."));

                group.AddInteger(
                    "GuardKillsForHeadsman",
                    "Guards killed for the headsman",
                    minValue: 1,
                    maxValue: 10,
                    new MCM.Common.ProxyRef<int>(
                        () => Get<int>("GuardKillsForHeadsman"),
                        v => Set("GuardKillsForHeadsman", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("Resisting arrest, kill this many of the watch and get caught, and the sentence is death (unless your clan is tier 3 or higher: then a double sentence and a blood price of 500 per man). Otherwise the cells: a night if you went quietly, longer for your crime rating, +2 days per guard knocked down, +5 per guard killed, up to 90."));
            });

            // ===== DEBUG =====
            builder.CreateGroup("Debug", group =>
            {
                group.SetGroupOrder(3);

                // Collapsed unless ticked: players don't need these.
                group.AddToggle(
                    "ShowDebugOptions",
                    "Debug",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("ShowDebugOptions"),
                        v => Set("ShowDebugOptions", v)),
                    b => b.SetHintText("Logging and tuning aids. Not needed for normal play."));

                group.AddBool(
                    "VerboseLogging",
                    "Verbose logging",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("VerboseLogging"),
                        v => Set("VerboseLogging", v)),
                    b => b.SetOrder(0)
                        .SetHintText("Enables detailed debug messages in lmmi.log. Leave off for normal play."));

                group.AddBool(
                    "ShowDispositionBreakdown",
                    "Show notable disposition breakdown",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("ShowDispositionBreakdown"),
                        v => Set("ShowDispositionBreakdown", v)),
                    b => b.SetOrder(1)
                        .SetHintText("When you start talking to a notable, shows how they regard you and why (deeds, power, affinity, prejudice). For tuning; also written to lmmi.log."));
            });

            // ===== TESTING =====
            builder.CreateGroup("Testing", group =>
            {
                group.SetGroupOrder(4);
                var order = 0;

                // Collapsed unless ticked: cheats and staged events for testing — spoilers for players.
                group.AddToggle(
                    "ShowTestingOptions",
                    "Testing (cheats)",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("ShowTestingOptions"),
                        v => Set("ShowTestingOptions", v)),
                    b => b.SetHintText("Test mode, staged events and cheats. Spoils the surprises; leave closed for normal play."));

                group.AddBool(
                    "TestMode",
                    "Test mode",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("TestMode"),
                        v => Set("TestMode", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("For testing LMMI: map parties ignore you, LMMI events always fire and cooldowns are skipped, skill and trait checks roll for real with the odds shown on screen, and conversations get [Test] lines (relation, how they see you, town standing). Turn off for normal play."));

                group.AddBool(
                    "TestImmortal",
                    "Player immortal",
                    new MCM.Common.ProxyRef<bool>(
                        () => Get<bool>("TestImmortal"),
                        v => Set("TestImmortal", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("In any scene or battle you take hits but never go down (vanilla's Immortal state). Works whether or not Test mode is on."));

                group.AddInteger(
                    "TestDamagePercent",
                    "Your damage (%)",
                    minValue: 10,
                    maxValue: 2000,
                    new MCM.Common.ProxyRef<int>(
                        () => Get<int>("TestDamagePercent"),
                        v => Set("TestDamagePercent", v)),
                    b => b.SetOrder(order++)
                        .SetHintText("Scales the damage you deal, fists and weapons alike (100 = normal, 1000 = ten times). Works whether or not Test mode is on."));

                void Button(string id, string name, string label, System.Action action, string hint) =>
                    group.AddButton(id, name, new MCM.Common.ProxyRef<System.Action>(() => action, _ => { }), label,
                        b => b.SetOrder(order++).SetHintText(hint));

                Button("TestAddGold", "Add gold", "+10,000", Behaviors.TestTools.AddGold, "Adds 10,000 gold to your character.");
                Button("TestHeal", "Heal", "Heal all", Behaviors.TestTools.HealAll, "Restores you, your companions and all wounded troops in your party.");
                Button("TestRenown", "Renown", "Next tier", Behaviors.TestTools.RenownToNextTier, "Adds exactly enough renown for your clan's next tier.");
                Button("TestStandingUp", "Town standing +10", "+10 here", Behaviors.TestTools.StandingUp, "Raises your standing in the settlement you're in.");
                Button("TestStandingDown", "Town standing -10", "-10 here", Behaviors.TestTools.StandingDown, "Lowers your standing in the settlement you're in.");
                Button("TestRelationsHere", "Relations +10 here", "+10 all", Behaviors.TestTools.RelationsHere, "Raises your relation with every notable in the settlement you're in, and its owner. (For one person, use the [Test] lines in conversation.)");
                Button("TestFeast", "Feast now", "Feast", Behaviors.TestTools.FeastNow, "The nearest notable in the scene (within 80 m) throws you a welcome feast on the spot: your companions and best men sit down with the locals.");
                group.AddDropdown(
                    "TestStreetEventKind",
                    "Street event to stage",
                    0,
                    new MCM.Common.ProxyRef<MCM.Common.Dropdown<string>>(
                        () => new MCM.Common.Dropdown<string>(Behaviors.StreetEventsBehavior.KindNames,
                            _values.TryGetValue("TestStreetEventKind", out var v) && v is int n ? n : 0),
                        d => Set("TestStreetEventKind", d.SelectedIndex)),
                    b => b.SetOrder(order++)
                        .SetHintText("Which street event 'Street event now' stages (Any = random). Town-only and village-only kinds still need the right place."));

                Button("TestStreetEvent", "Street event now", "Trigger", Behaviors.TestTools.StreetEvent, "Next time you're free in a town or village centre, something is staged nearby and a local runs up for help: a girl cornered by men, two lads brawling, a cutpurse, or a man who fell. In Test mode at least half are setups (toughs waiting at the end). Checks roll for real; the odds show on screen.");
                Button("TestHiredBeat", "Hired swords: a beating", "Send", () => Behaviors.TestTools.HiredSwords("beat"), "Your worst enemy within reach (or anyone) sends hired swords to beat you. On the map, outside a settlement. Lose to see the beating.");
                Button("TestHiredDoom", "Hired swords: judgment", "Send", () => Behaviors.TestTools.HiredSwords("doom"), "Hired swords come to drag you before whoever paid — who wants you dead. Lose (or surrender) to be delivered, then plead for your life. Death is real if 'Hired swords: execution' is on.");
                Button("TestHiredHold", "Hired swords: war capture", "Send", () => Behaviors.TestTools.HiredSwords("hold"), "A lord at war with you sends hired swords to bring you in as a prisoner.");
                Button("TestLordsTalk", "Lords talk now", "Run", Behaviors.TestTools.LordsTalk, "Runs this week's slander and praise at court right away (an enemy at -30 or worse who isn't honorable may slander you; a friend at 40+ may speak up).");
                Button("TestDiscover", "Discover this settlement", "Discover", Behaviors.TestTools.DiscoverSettlement, "Unlocks every part of the settlement you're in (knowing the way).");
                Button("TestCooldowns", "Clear LMMI cooldowns", "Clear", Behaviors.TestTools.ClearCooldowns, "Favors, vouches, arrival scenes, ambushes and letters can all happen again right away.");
            });

            var settings = builder.BuildAsGlobal();
            if (settings == null) return false;

            settings.Register();
            return true;
        }
    }
}
