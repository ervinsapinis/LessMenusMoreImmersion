using System;
using System.Collections.Generic;
using HarmonyLib;
using LessMenusMoreImmersion.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// "A taste of home": vanilla's party morale counts how many kinds of food are in the wagons, not which. Here, every
    /// people that makes up a real share of your men (15% or more) has favorites — Sturgians their fish, Vlandians their
    /// cheese and beer — and having some in the stores lifts them: +1, +1.5 or +2 by their share, +5 at most in all.
    /// A big group (40% or more) with none of its favorites at all costs 1. Main party only, its own line in the morale
    /// tooltip. The camp's report and the cask read the same analysis.
    /// </summary>
    internal static class CampFoodMorale
    {
        /// <summary>Integration: could become an MCM toggle ("EnableTasteOfHome").</summary>
        public const bool Enabled = true;

        public const float MinShare = 0.15f, LargeShare = 0.40f, MaxBonus = 5f;

        /// <summary>Each people's favorite stores (item ids, verified in SandBoxCore's items and DefaultItems).</summary>
        public static readonly Dictionary<string, string[]> Favorites = new Dictionary<string, string[]>
        {
            ["vlandia"] = new[] { "cheese", "meat", "beer" },
            ["sturgia"] = new[] { "fish", "meat" },
            ["empire"] = new[] { "olives", "grape", "wine" },
            ["aserai"] = new[] { "date_fruit", "olives" },
            ["khuzait"] = new[] { "meat", "butter" },
            ["battania"] = new[] { "meat", "cheese" },
            ["nord"] = new[] { "fish", "beer" },
        };

        internal sealed class People
        {
            public string Culture = "";
            public int Men;
            public float Share;
            public readonly List<string> Held = new List<string>();
            public readonly List<string> Missing = new List<string>();
            public float Bonus;
        }

        internal sealed class Analysis
        {
            public readonly List<People> Peoples = new List<People>();
            public float Bonus, Penalty;
            public bool AtCap => Bonus >= MaxBonus;
            public static readonly Analysis Empty = new Analysis();
        }

        private static Analysis _cached = Analysis.Empty;
        private static int _memberVersion = -1, _itemVersion = -1;
        private static bool _starving;
        private static object? _cachedFor;

        internal static readonly TextObject BonusText = new TextObject("{=lmmi_camp_taste_of_home}A taste of home");
        internal static readonly TextObject PenaltyText = new TextObject("{=lmmi_camp_no_taste_of_home}Nothing from home in the stores");

        /// <summary>The main party's peoples and their favorites, cached until the rosters change.</summary>
        public static Analysis Analyze(MobileParty? party)
        {
            if (!Enabled || party == null || Campaign.Current == null) return Analysis.Empty;
            var members = party.MemberRoster;
            var items = party.ItemRoster;
            bool starving = party.Party.IsStarving;
            if (ReferenceEquals(_cachedFor, party) && members.VersionNo == _memberVersion && items.VersionNo == _itemVersion
                && starving == _starving)
                return _cached;

            var result = new Analysis();
            try
            {
                if (!starving)
                {
                    var counts = new Dictionary<string, int>();
                    int total = 0;
                    for (int i = 0; i < members.Count; i++)
                    {
                        var e = members.GetElementCopyAtIndex(i);
                        if (e.Character == null || e.Number <= 0) continue;
                        total += e.Number;
                        var c = e.Character.Culture?.StringId;
                        if (c == null || !Favorites.ContainsKey(c)) continue;
                        counts.TryGetValue(c, out int n);
                        counts[c] = n + e.Number;
                    }

                    var held = new HashSet<string>();
                    for (int i = 0; i < items.Count; i++)
                    {
                        var e = items.GetElementCopyAtIndex(i);
                        var item = e.EquipmentElement.Item;
                        if (item != null && e.Amount > 0) held.Add(item.StringId);
                    }

                    if (total > 0)
                    {
                        foreach (var kv in counts)
                        {
                            float share = kv.Value / (float)total;
                            if (share < MinShare) continue;
                            var p = new People { Culture = kv.Key, Men = kv.Value, Share = share };
                            foreach (var id in Favorites[kv.Key]) (held.Contains(id) ? p.Held : p.Missing).Add(id);
                            if (p.Held.Count > 0) p.Bonus = share >= 0.5f ? 2f : share >= 0.3f ? 1.5f : 1f;
                            else if (share >= LargeShare) result.Penalty = -1f;
                            result.Bonus += p.Bonus;
                            result.Peoples.Add(p);
                        }
                        result.Peoples.Sort((a, b) => b.Share.CompareTo(a.Share));
                        result.Bonus = Math.Min(MaxBonus, result.Bonus);
                    }
                }
            }
            catch (Exception ex)
            {
                LmmiLog.Error("CampFoodMorale.Analyze threw", ex);
                result = Analysis.Empty;
            }

            _cached = result;
            _cachedFor = party;
            _memberVersion = members.VersionNo;
            _itemVersion = items.VersionNo;
            _starving = starving;
            return result;
        }
    }

    /// <summary>Vanilla's party morale, plus the main party's taste of home as its own tooltip line.</summary>
    [HarmonyPatch(typeof(DefaultPartyMoraleModel), nameof(DefaultPartyMoraleModel.GetEffectivePartyMorale))]
    internal static class CampFoodMoralePatch
    {
        private static bool _failed;

        [HarmonyPostfix]
        private static void Postfix(MobileParty mobileParty, ref ExplainedNumber __result)
        {
            if (_failed || mobileParty == null || mobileParty != MobileParty.MainParty) return;
            try
            {
                var a = CampFoodMorale.Analyze(mobileParty);
                if (a.Bonus > 0f) __result.Add(a.Bonus, CampFoodMorale.BonusText);
                if (a.Penalty < 0f) __result.Add(a.Penalty, CampFoodMorale.PenaltyText);
            }
            catch (Exception ex)
            {
                _failed = true;   // it runs every time anyone reads your morale: fail once, not every frame
                LmmiLog.Error("CampFoodMoralePatch threw; taste of home disabled for this session", ex);
            }
        }
    }
}
