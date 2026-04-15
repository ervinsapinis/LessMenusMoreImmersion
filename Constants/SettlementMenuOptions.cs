using System.Collections.Generic;
using System.Linq;

namespace LessMenusMoreImmersion.Constants
{
    internal static class SettlementMenuOptions
    {
        public static class MenuOptions
        {
            // Town Menu Options
            public static readonly string TownKeep = "town_keep";
            public static readonly string TownArena = "town_arena";
            public static readonly string TownBackstreet = "town_backstreet";
            public static readonly string ManageProduction = "manage_production";
            public static readonly string Trade = "trade";
            public static readonly string TownSmithy = "town_smithy";

            // Town Keep Menu Options
            public static readonly string TownLordsHall = "town_lords_hall";
            public static readonly string TownLordsHallGoToDungeon = "town_lords_hall_go_to_dungeon";
            public static readonly string LeaveTroopsToGarrison = "leave_troops_to_garrison";
            public static readonly string ManageGarrison = "manage_garrison";
            public static readonly string OpenStash = "open_stash";

            // Castle Menu Options
            public static readonly string CastleLordsHall = "castle_lords_hall";
            public static readonly string CastlePrison = "castle_prison";
        }

        /// <summary>
        /// Per-feature identifiers used for dynamic discovery tracking.
        /// </summary>
        public static class Features
        {
            public const string Keep = "keep";
            public const string Arena = "arena";
            public const string Backstreet = "backstreet";
            public const string Trade = "trade";
            public const string Smithy = "smithy";

            // Mission-only features (don't unlock town menu options)
            public const string Alley = "alley";
            public const string Waterfront = "waterfront";
            public const string Clearing = "clearing";
            public const string Barber = "barber";
        }

        /// <summary>
        /// Maps gated menu option IDs to the feature they belong to.
        /// Menu options not present here are not gated by the discovery system.
        /// </summary>
        public static readonly Dictionary<string, string> OptionFeatureMap = new Dictionary<string, string>
        {
            { MenuOptions.TownKeep, Features.Keep },
            { MenuOptions.TownArena, Features.Arena },
            { MenuOptions.TownBackstreet, Features.Backstreet },
            { MenuOptions.Trade, Features.Trade },
            { MenuOptions.TownSmithy, Features.Smithy },
            { MenuOptions.TownLordsHall, Features.Keep },
            { MenuOptions.TownLordsHallGoToDungeon, Features.Keep },
            { MenuOptions.LeaveTroopsToGarrison, Features.Keep },
            { MenuOptions.ManageGarrison, Features.Keep },
            { MenuOptions.OpenStash, Features.Keep },
            { MenuOptions.CastleLordsHall, Features.Keep },
            { MenuOptions.CastlePrison, Features.Keep },
        };

        /// <summary>
        /// Maps in-mission location IDs to features that get discovered when the player enters them.
        /// </summary>
        public static readonly Dictionary<string, string> LocationFeatureMap = new Dictionary<string, string>
        {
            // Town center IS the marketplace — entering it discovers Trade.
            // (Trade can't rely on conversation occupation because the market NPCs
            // the player actually walks up to have Occupation.Wanderer, not Merchant.)
            // "center" intentionally omitted — entering the town square doesn't
            // auto-discover Trade; the player must talk to a merchant or be escorted.
            { "tavern",      Features.Backstreet },
            { "lordshall",   Features.Keep },
            { "prison",      Features.Keep },
            { "arena",            Features.Arena },
            { "arena_practice_fight", Features.Arena },
            { "smith",  Features.Smithy },
            { "smithy", Features.Smithy },
            { "alley_1",     Features.Alley },
            { "alley_2",     Features.Alley },
            { "alley_3",     Features.Alley },
            { "waterfront",  Features.Waterfront },
            { "clearing",    Features.Clearing },
            { "barber",      Features.Barber },
        };

        /// <summary>
        /// All gated menu option IDs (derived from OptionFeatureMap for backward compatibility).
        /// </summary>
        public static List<string> AllOptions => OptionFeatureMap.Keys.ToList();

        /// <summary>
        /// Returns a player-facing name for a feature, used in discovery messages.
        /// </summary>
        public static string GetFeatureDisplayName(string feature)
        {
            switch (feature)
            {
                case Features.Keep: return "keep";
                case Features.Arena: return "arena";
                case Features.Backstreet: return "tavern district";
                case Features.Trade: return "marketplace";
                case Features.Smithy: return "smithy";
                case Features.Alley: return "alley";
                case Features.Waterfront: return "waterfront";
                case Features.Clearing: return "clearing";
                case Features.Barber: return "barber";
                default: return feature;
            }
        }
    }
}
