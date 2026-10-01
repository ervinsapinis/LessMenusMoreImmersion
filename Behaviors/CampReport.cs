using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// "How are the men keeping?" — a soldier's answer in three breaths: their spirits and what's behind them, the last
    /// fight and what's wrong in the column, then the wagons (how long the food lasts, what's in it, what each people
    /// misses from home). Everything read from the party; the wording varies.
    /// </summary>
    internal static class CampReport
    {
        private static TextObject Pick(params string[] lines) => new TextObject(lines[MBRandom.RandomInt(lines.Length)]);

        // ---- 1. Spirits ----

        public static TextObject Morale(MobileParty party)
        {
            float morale = party.Morale;
            var head = morale >= 80f ? Pick(
                    "{=lmmi_camp_rep_m_top_1}The men are in fine spirits — they'd follow you into the sea.",
                    "{=lmmi_camp_rep_m_top_2}Best spirits I've seen in this company, and I've seen a few companies.")
                : morale >= 60f ? Pick(
                    "{=lmmi_camp_rep_m_good_1}The men are in good heart.",
                    "{=lmmi_camp_rep_m_good_2}They're content, mostly. Full bellies and a captain who knows what he's about.")
                : morale >= 40f ? Pick(
                    "{=lmmi_camp_rep_m_mid_1}The men are holding up.",
                    "{=lmmi_camp_rep_m_mid_2}They're getting by, captain. No more than that.")
                : morale >= 25f ? Pick(
                    "{=lmmi_camp_rep_m_low_1}The men are grumbling.",
                    "{=lmmi_camp_rep_m_low_2}There's grumbling around the fires, and it's getting louder.")
                : Pick(
                    "{=lmmi_camp_rep_m_bad_1}The men are at the end of their rope, captain.",
                    "{=lmmi_camp_rep_m_bad_2}Spirits are in the mud. I won't dress it up.");

            var causes = Causes(party);
            var good = causes.Where(c => c.Value > 0.5f).OrderByDescending(c => c.Value).FirstOrDefault();
            var bad = causes.Where(c => c.Value < -0.5f).OrderBy(c => c.Value).FirstOrDefault();
            TextObject why;
            if (good.Text != null && bad.Text != null)
                why = Pick("{=lmmi_camp_rep_why_both_1}What keeps them going is {GOOD}; what drags them down is {BAD}.",
                           "{=lmmi_camp_rep_why_both_2}It's {GOOD} that holds them together — and {BAD} that wears at them.");
            else if (good.Text != null)
                why = Pick("{=lmmi_camp_rep_why_good_1}It's {GOOD} that does it, mostly.",
                           "{=lmmi_camp_rep_why_good_2}Ask any of them and they'll say it's {GOOD}.");
            else if (bad.Text != null)
                why = Pick("{=lmmi_camp_rep_why_bad_1}What's eating at them is {BAD}.",
                           "{=lmmi_camp_rep_why_bad_2}If you want it straight: it's {BAD}.");
            else
                why = new TextObject("{=lmmi_camp_rep_why_none}Nothing much lifting them, nothing much sinking them.");
            if (good.Text != null) why.SetTextVariable("GOOD", good.Text);
            if (bad.Text != null) why.SetTextVariable("BAD", bad.Text);

            var line = new TextObject("{=lmmi_camp_rep_morale}{HEAD} {WHY}");
            line.SetTextVariable("HEAD", head);
            line.SetTextVariable("WHY", why);
            return line;
        }

        private struct Cause
        {
            public float Value;
            public TextObject? Text;
            public Cause(float value, TextObject text) { Value = value; Text = text; }
        }

        /// <summary>The main things vanilla's morale model (and the taste of home) count, worded as a soldier would.</summary>
        private static List<Cause> Causes(MobileParty party)
        {
            var list = new List<Cause>();
            float recent = party.RecentEventsMorale;
            if (recent >= 5f) list.Add(new Cause(recent, Pick("{=lmmi_camp_rep_c_recent_up_1}the way things have gone lately",
                "{=lmmi_camp_rep_c_recent_up_2}the last few days' work")));
            else if (recent <= -5f) list.Add(new Cause(recent, Pick("{=lmmi_camp_rep_c_recent_down_1}how the last few days went",
                "{=lmmi_camp_rep_c_recent_down_2}what happened lately — nobody's forgotten")));
            if (party.Party.IsStarving) list.Add(new Cause(-30f, new TextObject("{=lmmi_camp_rep_c_starving}empty bellies")));
            if (party.HasUnpaidWages > 0f) list.Add(new Cause(-20f * party.HasUnpaidWages, new TextObject("{=lmmi_camp_rep_c_wages}the pay they're owed")));
            if (!party.Party.IsStarving)
            {
                float variety = VarietyMorale(party.ItemRoster.FoodVariety);
                if (variety >= 2f) list.Add(new Cause(variety, new TextObject("{=lmmi_camp_rep_c_variety_up}a decent variety in the pot")));
                else if (variety < 0f) list.Add(new Cause(variety, new TextObject("{=lmmi_camp_rep_c_variety_down}the same gruel every night")));
            }
            int over = party.Party.NumberOfAllMembers - party.Party.PartySizeLimit;
            if (over > 0) list.Add(new Cause(-TaleWorlds.Library.MathF.Sqrt(over), new TextObject("{=lmmi_camp_rep_c_size}too many men for too few tents")));
            var home = CampFoodMorale.Analyze(party);
            if (home.Bonus > 0f) list.Add(new Cause(home.Bonus, new TextObject("{=lmmi_camp_rep_c_home_up}a taste of home in the pot")));
            if (home.Penalty < 0f) list.Add(new Cause(home.Penalty - 1f, new TextObject("{=lmmi_camp_rep_c_home_down}nothing from home in the wagons")));
            int leadership = Hero.MainHero.GetSkillValue(DefaultSkills.Leadership);
            if (leadership >= 75) list.Add(new Cause(leadership / 40f, new TextObject("{=lmmi_camp_rep_c_leader}the way you lead them")));
            return list;
        }

        /// <summary>Vanilla's food variety morale (DefaultPartyMoraleModel.CalculateFoodVarietyMoraleBonus).</summary>
        private static float VarietyMorale(int kinds) => kinds switch
        {
            0 => -2f, 1 => -2f, 2 => -1f, 3 => 0f, 4 => 1f, 5 => 2f, 6 => 3f, 7 => 5f, 8 => 6f, 9 => 7f, 10 => 8f, 11 => 9f, _ => 10f,
        };

        // ---- 2. The last fight, and what's wrong ----

        public static TextObject Battles(MobileParty party, CampBehavior.Battle? last)
        {
            TextObject fight;
            if (last == null)
                fight = Pick("{=lmmi_camp_rep_b_none_1}We've not drawn steel together yet that anyone remembers.",
                             "{=lmmi_camp_rep_b_none_2}No fights to speak of lately. The lads are getting soft.");
            else
            {
                float days = last.DaysAgo;
                if (days > 7f)
                    fight = Pick("{=lmmi_camp_rep_b_old_1}It's been {DAYS} days since the last fight. Quiet's good — for a while.",
                                 "{=lmmi_camp_rep_b_old_2}{DAYS} days without a fight. Some of them are itching for one.");
                else if (last.Outcome > 0)
                    fight = days < 3f
                        ? Pick("{=lmmi_camp_rep_b_won_1}They're still talking about {FOE} — {WHEN}. They'll be telling it for a month.",
                               "{=lmmi_camp_rep_b_won_2}Beating {FOE} {WHEN} put a spring in every step.")
                        : Pick("{=lmmi_camp_rep_b_won_old}We saw off {FOE} {WHEN}. The shine's wearing off, but they remember.");
                else if (last.Outcome < 0)
                    fight = days < 3f
                        ? Pick("{=lmmi_camp_rep_b_lost_1}That business with {FOE} {WHEN} — they haven't shaken it off.",
                               "{=lmmi_camp_rep_b_lost_2}Nobody talks about {FOE}. Not since {WHEN}. That's how you know.")
                        : Pick("{=lmmi_camp_rep_b_lost_old}They've stopped talking about {FOE}. That's a good sign, or a bad one.");
                else
                    fight = Pick("{=lmmi_camp_rep_b_fled}We pulled back from {FOE} {WHEN}. Some call it sense. Some call it something else.");
                fight.SetTextVariable("FOE", string.IsNullOrEmpty(last.Foe) ? new TextObject("{=lmmi_camp_rep_b_them}them") : new TextObject("{=!}" + last.Foe));
                fight.SetTextVariable("WHEN", When(last.HoursAgo));
                fight.SetTextVariable("DAYS", (int)days);
                if (last.Dead > 0 && days <= 7f)
                {
                    var dead = new TextObject("{=lmmi_camp_rep_b_dead}{FIGHT} We buried {DEAD} of our own.");
                    dead.SetTextVariable("FIGHT", fight);
                    dead.SetTextVariable("DEAD", last.Dead);
                    fight = dead;
                }
            }

            var issues = Issues(party).Take(3).ToList();
            var line = new TextObject(issues.Count == 0
                ? "{=lmmi_camp_rep_b_fine}{FIGHT} Otherwise, no complaints worth your ear."
                : "{=lmmi_camp_rep_b_issues}{FIGHT} {ISSUES}");
            line.SetTextVariable("FIGHT", fight);
            if (issues.Count > 0) line.SetTextVariable("ISSUES", Sentences(issues));
            return line;
        }

        private static IEnumerable<TextObject> Issues(MobileParty party)
        {
            float morale = party.Morale;
            int threshold = Campaign.Current.Models.PartyDesertionModel.GetMoraleThresholdForTroopDesertion();
            if (morale < threshold)
                yield return new TextObject("{=lmmi_camp_rep_i_desert}Men have been slipping away in the night. More will, if nothing changes.");
            else if (morale < threshold + 15f)
                yield return new TextObject("{=lmmi_camp_rep_i_desert_risk}Some of the newer lads are talking about slipping off in the night.");

            if (party.HasUnpaidWages > 0f)
                yield return new TextObject("{=lmmi_camp_rep_i_unpaid}And they've not been paid. That won't hold long.");
            else
            {
                int wage = party.TotalWage;
                if (wage > 0 && Hero.MainHero.Gold < wage * 2)
                    yield return new TextObject("{=lmmi_camp_rep_i_purse}The pay chest won't cover two more days' wages, and they know it.");
            }

            int over = party.Party.NumberOfAllMembers - party.Party.PartySizeLimit;
            if (over > 0)
                yield return new TextObject("{=lmmi_camp_rep_i_size}We're {N} more than the column can carry — too many for the tents, too many for the pot.")
                    .SetTextVariable("N", over);

            var roster = party.MemberRoster;
            int wounded = roster.TotalWoundedRegulars, regulars = roster.TotalRegulars;
            if (wounded >= 3 && regulars > 0 && wounded >= regulars / 4)
                yield return new TextObject("{=lmmi_camp_rep_i_wounded_many}{N} on the sick list — that's a lot of empty places in the line.").SetTextVariable("N", wounded);
            else if (wounded > 0)
                yield return new TextObject("{=lmmi_camp_rep_i_wounded}{N} still nursing wounds, nothing that won't mend.").SetTextVariable("N", wounded);

            int prisoners = party.Party.NumberOfPrisoners;
            if (prisoners > 0)
                yield return new TextObject("{=lmmi_camp_rep_i_prisoners}{N} prisoners to guard and feed — the watch grumbles about it.").SetTextVariable("N", prisoners);
        }

        // ---- 3. The wagons ----

        public static TextObject Stores(MobileParty party)
        {
            int days = party.GetNumDaysForFoodToLast();
            var amount = (days <= 1
                    ? Pick("{=lmmi_camp_food_none}The stores are all but gone — we eat tomorrow or we don't.",
                           "{=lmmi_camp_rep_f_none_2}The wagons are near empty. Tomorrow we tighten belts.")
                    : days <= 4
                        ? Pick("{=lmmi_camp_food_low}Food for {DAYS} days, if we're careful.",
                               "{=lmmi_camp_rep_f_low_2}{DAYS} days of food, and that's counting the crusts.")
                        : Pick("{=lmmi_camp_food_ok}Plenty in the wagons — {DAYS} days' worth.",
                               "{=lmmi_camp_rep_f_ok_2}{DAYS} days of food in the wagons. Nobody's going hungry."))
                .SetTextVariable("DAYS", days);

            var held = new List<string>();
            var items = party.ItemRoster;
            for (int i = 0; i < items.Count; i++)
            {
                var e = items.GetElementCopyAtIndex(i);
                var item = e.EquipmentElement.Item;
                if (item != null && item.IsFood && e.Amount > 0 && !held.Contains(item.StringId)) held.Add(item.StringId);
            }

            var parts = new List<TextObject> { amount };
            if (held.Count > 0)
            {
                var list = Join(held.Select(Food).ToList());
                parts.Add((held.Count >= 4
                        ? new TextObject("{=lmmi_camp_rep_f_variety_good}There's {LIST} — a proper table.")
                        : held.Count >= 2
                            ? new TextObject("{=lmmi_camp_rep_f_variety_some}There's {LIST}.")
                            : new TextObject("{=lmmi_camp_rep_f_variety_one}Nothing but {LIST}, day after day."))
                    .SetTextVariable("LIST", list));
                var missing = new[] { "meat", "fish", "cheese", "beer", "grain" }.Where(id => !held.Contains(id)).Take(3).ToList();
                if (held.Count < 5 && missing.Count > 0)
                    parts.Add(new TextObject("{=lmmi_camp_rep_f_missing}No {LIST}, though.").SetTextVariable("LIST", Join(missing.Select(Food).ToList(), or: true)));
            }

            // A taste of home: who's glad, who misses what.
            foreach (var p in CampFoodMorale.Analyze(party).Peoples.Take(3))
            {
                if (p.Held.Count > 0)
                    parts.Add(Pick("{=lmmi_camp_rep_h_glad_1}The {PEOPLE} are glad of the {FOOD}.",
                                   "{=lmmi_camp_rep_h_glad_2}Give the {PEOPLE} their {FOOD} and they'll march anywhere.")
                        .SetTextVariable("PEOPLE", PeopleName(p.Culture)).SetTextVariable("FOOD", Food(p.Held[0])));
                else if (p.Missing.Count > 0)
                    parts.Add(Pick("{=lmmi_camp_rep_h_miss_1}The {PEOPLE} would kill for some {FOOD}.",
                                   "{=lmmi_camp_rep_h_miss_2}The {PEOPLE} keep asking after {FOOD}. There's none.")
                        .SetTextVariable("PEOPLE", PeopleName(p.Culture)).SetTextVariable("FOOD", Food(p.Missing[MBRandom.RandomInt(p.Missing.Count)])));
            }
            return Sentences(parts);
        }

        // ---- Words ----

        public static TextObject PeopleName(string culture) => new TextObject(culture switch
        {
            "vlandia" => "{=lmmi_camp_people_vlandia}Vlandians",
            "sturgia" => "{=lmmi_camp_people_sturgia}Sturgians",
            "empire" => "{=lmmi_camp_people_empire}imperial lads",
            "aserai" => "{=lmmi_camp_people_aserai}Aserai",
            "khuzait" => "{=lmmi_camp_people_khuzait}Khuzaits",
            "battania" => "{=lmmi_camp_people_battania}Battanians",
            "nord" => "{=lmmi_camp_people_nord}Nords",
            _ => "{=lmmi_camp_people_other}others",
        });

        public static TextObject Food(string id)
        {
            switch (id)
            {
                case "grain": return new TextObject("{=lmmi_camp_food_grain}grain");
                case "meat": return new TextObject("{=lmmi_camp_food_meat}meat");
                case "fish": return new TextObject("{=lmmi_camp_food_fish}fish");
                case "cheese": return new TextObject("{=lmmi_camp_food_cheese}cheese");
                case "butter": return new TextObject("{=lmmi_camp_food_butter}butter");
                case "olives": return new TextObject("{=lmmi_camp_food_olives}olives");
                case "date_fruit": return new TextObject("{=lmmi_camp_food_dates}dates");
                case "grape": return new TextObject("{=lmmi_camp_food_grapes}grapes");
                case "beer": return new TextObject("{=lmmi_camp_food_beer}beer");
                case "wine": return new TextObject("{=lmmi_camp_food_wine}wine");
            }
            var item = TaleWorlds.ObjectSystem.MBObjectManager.Instance?.GetObject<ItemObject>(id);
            return item?.Name ?? new TextObject("{=!}" + id);
        }

        /// <summary>"a", "a and b", "a, b and c" (or "or").</summary>
        public static TextObject Join(IList<TextObject> items, bool or = false)
        {
            if (items.Count == 0) return TextObject.GetEmpty();
            if (items.Count == 1) return items[0];
            var tail = new TextObject(or ? "{=lmmi_camp_list_or}{A} or {B}" : "{=lmmi_camp_list_and}{A} and {B}");
            tail.SetTextVariable("A", items[items.Count - 2]);
            tail.SetTextVariable("B", items[items.Count - 1]);
            for (int i = items.Count - 3; i >= 0; i--)
            {
                var more = new TextObject("{=lmmi_camp_list_comma}{A}, {B}");
                more.SetTextVariable("A", items[i]);
                more.SetTextVariable("B", tail);
                tail = more;
            }
            return tail;
        }

        private static TextObject Sentences(IList<TextObject> parts)
        {
            var text = parts[0];
            for (int i = 1; i < parts.Count; i++)
            {
                var next = new TextObject("{=lmmi_camp_rep_join}{A} {B}");
                next.SetTextVariable("A", text);
                next.SetTextVariable("B", parts[i]);
                text = next;
            }
            return text;
        }

        public static TextObject When(float hoursAgo)
        {
            if (hoursAgo < 18f) return new TextObject("{=lmmi_camp_when_today}earlier today");
            int days = Math.Max(1, (int)Math.Round(hoursAgo / CampaignTime.HoursInDay));
            if (days == 1) return new TextObject("{=lmmi_camp_when_yesterday}yesterday");
            return new TextObject("{=lmmi_camp_when_days}{N} days back").SetTextVariable("N", days);
        }
    }
}
