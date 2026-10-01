using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Contacts
{
    /// <summary>What people call you: the polite word for your people, and the slur they use behind your back.</summary>
    internal static class CultureWords
    {
        public static TextObject Demonym(CultureObject? culture)
        {
            switch (culture?.StringId)
            {
                case "empire": return new TextObject("{=lmmi_demonym_empire}imperial");
                case "sturgia": return new TextObject("{=lmmi_demonym_sturgia}Sturgian");
                case "aserai": return new TextObject("{=lmmi_demonym_aserai}Aserai");
                case "battania": return new TextObject("{=lmmi_demonym_battania}Battanian");
                case "khuzait": return new TextObject("{=lmmi_demonym_khuzait}Khuzait");
                case "vlandia": return new TextObject("{=lmmi_demonym_vlandia}Vlandian");
                case "nord": return new TextObject("{=lmmi_demonym_nord}Nord");
                default: return culture?.Name ?? new TextObject("{=lmmi_demonym_foreigner}foreigner");
            }
        }

        public static TextObject Slur(CultureObject? culture)
        {
            switch (culture?.StringId)
            {
                case "empire": return new TextObject("{=lmmi_slur_empire}purple-robed windbag");
                case "sturgia": return new TextObject("{=lmmi_slur_sturgia}frozen drunk");
                case "aserai": return new TextObject("{=lmmi_slur_aserai}desert dog");
                case "battania": return new TextObject("{=lmmi_slur_battania}wood hugger");
                case "khuzait": return new TextObject("{=lmmi_slur_khuzait}horse fondler");
                case "vlandia": return new TextObject("{=lmmi_slur_vlandia}goat-botherer");
                case "nord": return new TextObject("{=lmmi_slur_nord}fish-gutter");
                default: return new TextObject("{=lmmi_slur_foreigner}foreigner");
            }
        }
    }
}
