using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
            public static readonly string RecruitVolunteers = "recruit_volunteers";
            public static readonly string Trade = "trade";
            public static readonly string TownSmithy = "town_smithy";

            // Town Keep Menu Options
            public static readonly string TownLordsHall = "town_lords_hall";
            public static readonly string TownLordsHallGoToDungeon = "town_lords_hall_go_to_dungeon";
            public static readonly string LeaveTroopsToGarrison = "leave_troops_to_garrison";
            public static readonly string ManageGarrison = "manage_garrison";
            public static readonly string OpenStash = "open_stash";
            public static readonly string TownCastleBack = "town_castle_back";

            // Castle Menu Options
            public static readonly string TakeAWalkAroundTheCastle = "take_a_walk_around_the_castle";
            public static readonly string CastleLordsHall = "castle_lords_hall";
            public static readonly string CastlePrison = "castle_prison";
            public static readonly string ManageProductionCastle = "manage_production";
        }

        public static List<string> AllOptions
        {
            get
            {
                return typeof(MenuOptions)
                    .GetFields(BindingFlags.Static | BindingFlags.Public)
                    .Where(field => field.FieldType == typeof(string))
                    .Select(field => (string)field.GetValue(null))
                    .ToList();
            }
        }
    }
}