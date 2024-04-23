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
            //town
            public static readonly string Smithy = "town_smithy";
            //public static readonly string Keep = "town_keep";
            public static readonly string Market = "trade";
            public static readonly string Arena = "town_arena";
            public static readonly string TavernDistrict = "town_backstreet";

            //castle
            public static readonly string LordsHall = "castle_lords_hall";
            //public static readonly string Prison = "castle_prison";

            //village
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
