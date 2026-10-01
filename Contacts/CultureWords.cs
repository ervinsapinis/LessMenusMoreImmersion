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

        /// <summary>
        /// A slur for your people, picked at random each time (so the town doesn't sound like one man). Every one is a
        /// singular noun phrase starting with a consonant sound: lines say "a {SLUR}".
        /// </summary>
        public static TextObject Slur(CultureObject? culture)
        {
            switch (culture?.StringId)
            {
                case "empire": return Pick(
                    "{=lmmi_slur_empire}purple-robed windbag",
                    "{=lmmi_slur_empire_2}senate lapdog",
                    "{=lmmi_slur_empire_3}ledger-licker",
                    "{=lmmi_slur_empire_4}marble-headed snob",
                    "{=lmmi_slur_empire_5}toga-sniffer",
                    "{=lmmi_slur_empire_6}leftover of a dead empire",
                    "{=lmmi_slur_empire_7}bathhouse dandy",
                    "{=lmmi_slur_empire_8}toga-lifter",
                    "{=lmmi_slur_empire_9}perfumed pen-pusher",
                    "{=lmmi_slur_empire_10}senate bootlicker");
                case "sturgia": return Pick(
                    "{=lmmi_slur_sturgia}frozen drunk",
                    "{=lmmi_slur_sturgia_2}Sturgian drunk",
                    "{=lmmi_slur_sturgia_3}mead-soaked bear",
                    "{=lmmi_slur_sturgia_4}snow-brained oaf",
                    "{=lmmi_slur_sturgia_5}frostbitten sot",
                    "{=lmmi_slur_sturgia_6}bearskin lout",
                    "{=lmmi_slur_sturgia_7}bear-cuddler",
                    "{=lmmi_slur_sturgia_8}vomit-beard",
                    "{=lmmi_slur_sturgia_9}snow-humper",
                    "{=lmmi_slur_sturgia_10}lice-ridden mead-guzzler");
                case "aserai": return Pick(
                    "{=lmmi_slur_aserai}desert dog",
                    "{=lmmi_slur_aserai_2}dune rat",
                    "{=lmmi_slur_aserai_3}scorpion-eater",
                    "{=lmmi_slur_aserai_4}sun-addled dune-crawler",
                    "{=lmmi_slur_aserai_5}Nahasa scorpion",
                    "{=lmmi_slur_aserai_6}sultan's lackey",
                    "{=lmmi_slur_aserai_7}scorpion-kisser",
                    "{=lmmi_slur_aserai_8}dune-humper",
                    "{=lmmi_slur_aserai_9}sun-boiled halfwit",
                    "{=lmmi_slur_aserai_10}fly-blown desert rat");
                case "battania": return Pick(
                    "{=lmmi_slur_battania}wood hugger",
                    "{=lmmi_slur_battania_2}woad raider",
                    "{=lmmi_slur_battania_3}tree-talker",
                    "{=lmmi_slur_battania_4}cattle-thieving hillman",
                    "{=lmmi_slur_battania_5}moss-head",
                    "{=lmmi_slur_battania_6}painted brigand",
                    "{=lmmi_slur_battania_7}tree-humper",
                    "{=lmmi_slur_battania_8}squirrel-wooer",
                    "{=lmmi_slur_battania_9}mud-faced woad-dauber",
                    "{=lmmi_slur_battania_10}nettle-bottomed hillman");
                case "khuzait": return Pick(
                    "{=lmmi_slur_khuzait}horse fondler",
                    "{=lmmi_slur_khuzait_2}steppe rat",
                    "{=lmmi_slur_khuzait_3}mare-milker",
                    "{=lmmi_slur_khuzait_4}saddle-sore raider",
                    "{=lmmi_slur_khuzait_5}dung-fire nomad",
                    "{=lmmi_slur_khuzait_6}yurt-crawler",
                    "{=lmmi_slur_khuzait_7}mare-kisser",
                    "{=lmmi_slur_khuzait_8}saddle-sniffer",
                    "{=lmmi_slur_khuzait_9}horse-wife",
                    "{=lmmi_slur_khuzait_10}dung-eater");
                case "vlandia": return Pick(
                    "{=lmmi_slur_vlandia}goat-botherer",
                    "{=lmmi_slur_vlandia_2}goat fondler",
                    "{=lmmi_slur_vlandia_3}tin-can knight",
                    "{=lmmi_slur_vlandia_4}lance-for-hire",
                    "{=lmmi_slur_vlandia_5}turnip lordling",
                    "{=lmmi_slur_vlandia_6}crossbow-cranking cutthroat",
                    "{=lmmi_slur_vlandia_7}goat-kisser",
                    "{=lmmi_slur_vlandia_8}nanny-goat's husband",
                    "{=lmmi_slur_vlandia_9}horse-breathed braggart",
                    "{=lmmi_slur_vlandia_10}turnip-sniffer");
                case "nord": return Pick(
                    "{=lmmi_slur_nord}fish-gutter",
                    "{=lmmi_slur_nord_2}sea-raider",
                    "{=lmmi_slur_nord_3}longboat rat",
                    "{=lmmi_slur_nord_4}herring-breath",
                    "{=lmmi_slur_nord_5}salt-crusted pirate",
                    "{=lmmi_slur_nord_6}wave-wrecked heathen",
                    "{=lmmi_slur_nord_7}herring-kisser",
                    "{=lmmi_slur_nord_8}seal-humper",
                    "{=lmmi_slur_nord_9}walrus-wooer",
                    "{=lmmi_slur_nord_10}fish-breathed lout");
                default: return Pick(
                    "{=lmmi_slur_foreigner}foreigner",
                    "{=lmmi_slur_foreigner_2}stranger",
                    "{=lmmi_slur_foreigner_3}rootless drifter",
                    "{=lmmi_slur_foreigner_4}landless vagrant",
                    "{=lmmi_slur_foreigner_5}flea-bitten vagrant",
                    "{=lmmi_slur_foreigner_6}dung-footed drifter");
            }
        }

        private static TextObject Pick(params string[] lines) => new TextObject(lines[MBRandom.RandomInt(lines.Length)]);
    }
}
