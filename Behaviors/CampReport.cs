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
                    "{=lmmi_camp_rep_m_top_2}Best spirits I've seen in this company, and I've seen a few companies.",
                    "{=lmmi_camp_rep_m_top_3}Never seen them like this. They'd storm a castle with spoons.",
                    "{=lmmi_camp_rep_m_top_4}They're singing, captain. Singing! On a march!",
                    "{=lmmi_camp_rep_m_top_5}Spirits are sky-high. Enjoy it — it never lasts.",
                    "{=lmmi_camp_rep_m_top_6}The lads would walk through fire for you right now.")
                : morale >= 60f ? Pick(
                    "{=lmmi_camp_rep_m_good_1}The men are in good heart.",
                    "{=lmmi_camp_rep_m_good_2b}They're content, mostly. Full bellies and a captain who knows what {?PLAYER.GENDER}she's{?}he's{\\?} about.",
                    "{=lmmi_camp_rep_m_good_3}Good spirits. Nobody's sharpening a knife for anyone.",
                    "{=lmmi_camp_rep_m_good_4}They're well enough — grumbling the way soldiers always grumble.",
                    "{=lmmi_camp_rep_m_good_5}The men are steady, captain.",
                    "{=lmmi_camp_rep_m_good_6}Decent spirits. A few jokes round the fire, which is a good sign.")
                : morale >= 40f ? Pick(
                    "{=lmmi_camp_rep_m_mid_1}The men are holding up.",
                    "{=lmmi_camp_rep_m_mid_2}They're getting by, captain. No more than that.",
                    "{=lmmi_camp_rep_m_mid_3}Middling, captain. They'll do what's asked.",
                    "{=lmmi_camp_rep_m_mid_4}Neither here nor there. They march, they eat, they sleep.",
                    "{=lmmi_camp_rep_m_mid_5}They're tired, but they're not broken.",
                    "{=lmmi_camp_rep_m_mid_6}Could be better. Could be a lot worse.")
                : morale >= 25f ? Pick(
                    "{=lmmi_camp_rep_m_low_1}The men are grumbling.",
                    "{=lmmi_camp_rep_m_low_2}There's grumbling around the fires, and it's getting louder.",
                    "{=lmmi_camp_rep_m_low_3}Spirits are low. You can hear it in the singing — there isn't any.",
                    "{=lmmi_camp_rep_m_low_4}They're sour, captain. Quick to fight each other, slow to fight anyone else.",
                    "{=lmmi_camp_rep_m_low_5}Mutters at every fire. Nothing open yet.",
                    "{=lmmi_camp_rep_m_low_6}Low. Lower than I'd like.")
                : Pick(
                    "{=lmmi_camp_rep_m_bad_1}The men are at the end of their rope, captain.",
                    "{=lmmi_camp_rep_m_bad_2}Spirits are in the mud. I won't dress it up.",
                    "{=lmmi_camp_rep_m_bad_3}They're close to breaking, captain. One more bad day and they'll scatter.",
                    "{=lmmi_camp_rep_m_bad_4}I've seen happier men on the gallows.",
                    "{=lmmi_camp_rep_m_bad_5}It's bad. Some of them won't look you in the eye.",
                    "{=lmmi_camp_rep_m_bad_6}Captain, if something doesn't change soon, they'll change it themselves.");

            var causes = Causes(party);
            var good = causes.Where(c => c.Value > 0.5f).OrderByDescending(c => c.Value).FirstOrDefault();
            var bad = causes.Where(c => c.Value < -0.5f).OrderBy(c => c.Value).FirstOrDefault();
            TextObject why;
            if (good.Text != null && bad.Text != null)
                why = Pick("{=lmmi_camp_rep_why_both_1}What keeps them going is {GOOD}; what drags them down is {BAD}.",
                           "{=lmmi_camp_rep_why_both_2}It's {GOOD} that holds them together — and {BAD} that wears at them.",
                           "{=lmmi_camp_rep_why_both_3}They take heart from {GOOD}, but {BAD} is grinding them down.",
                           "{=lmmi_camp_rep_why_both_4}{GOOD} keeps them on their feet. {BAD} keeps them up at night.",
                           "{=lmmi_camp_rep_why_both_5}Say what you like about {BAD} — {GOOD} makes up for a lot of it.",
                           "{=lmmi_camp_rep_why_both_6}It's a balance: {GOOD} on one side, {BAD} on the other.");
            else if (good.Text != null)
                why = Pick("{=lmmi_camp_rep_why_good_1}It's {GOOD} that does it, mostly.",
                           "{=lmmi_camp_rep_why_good_2}Ask any of them and they'll say it's {GOOD}.",
                           "{=lmmi_camp_rep_why_good_3}Mostly it's {GOOD}.",
                           "{=lmmi_camp_rep_why_good_4}{GOOD} — that's what they talk about at the fires.",
                           "{=lmmi_camp_rep_why_good_5}Credit {GOOD} for that.",
                           "{=lmmi_camp_rep_why_good_6}You can thank {GOOD}.");
            else if (bad.Text != null)
                why = Pick("{=lmmi_camp_rep_why_bad_1}What's eating at them is {BAD}.",
                           "{=lmmi_camp_rep_why_bad_2}If you want it straight: it's {BAD}.",
                           "{=lmmi_camp_rep_why_bad_3}It's {BAD}, mostly.",
                           "{=lmmi_camp_rep_why_bad_4}They'd tell you themselves: {BAD}.",
                           "{=lmmi_camp_rep_why_bad_5}Blame {BAD}.",
                           "{=lmmi_camp_rep_why_bad_6}{BAD} — that's what's wearing them thin.");
            else
                why = Pick("{=lmmi_camp_rep_why_none}Nothing much lifting them, nothing much sinking them.",
                "{=lmmi_camp_rep_why_none_2}Nothing in particular, good or bad. Just the road.",
                "{=lmmi_camp_rep_why_none_3}No great cheer, no great complaint.");
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
                "{=lmmi_camp_rep_c_recent_up_2}the last few days' work",
                "{=lmmi_camp_rep_c_recent_up_3}a run of good luck",
                "{=lmmi_camp_rep_c_recent_up_4}how well the fighting's gone")));
            else if (recent <= -5f) list.Add(new Cause(recent, Pick("{=lmmi_camp_rep_c_recent_down_1}how the last few days went",
                "{=lmmi_camp_rep_c_recent_down_2}what happened lately — nobody's forgotten",
                "{=lmmi_camp_rep_c_recent_down_3}a run of bad luck",
                "{=lmmi_camp_rep_c_recent_down_4}the hard knocks we've taken")));
            if (party.Party.IsStarving) list.Add(new Cause(-30f, Pick("{=lmmi_camp_rep_c_starving}empty bellies",
                "{=lmmi_camp_rep_c_starving_2}hunger, plain and simple",
                "{=lmmi_camp_rep_c_starving_3}the empty cookpots")));
            if (party.HasUnpaidWages > 0f) list.Add(new Cause(-20f * party.HasUnpaidWages, Pick("{=lmmi_camp_rep_c_wages}the pay they're owed",
                "{=lmmi_camp_rep_c_wages_2}the wages nobody's seen",
                "{=lmmi_camp_rep_c_wages_3}an empty pay chest")));
            if (!party.Party.IsStarving)
            {
                float variety = VarietyMorale(party.ItemRoster.FoodVariety);
                if (variety >= 2f) list.Add(new Cause(variety, Pick("{=lmmi_camp_rep_c_variety_up}a decent variety in the pot",
                "{=lmmi_camp_rep_c_variety_up_2}a bit of variety at supper",
                "{=lmmi_camp_rep_c_variety_up_3}good food, and plenty of kinds of it")));
                else if (variety < 0f) list.Add(new Cause(variety, Pick("{=lmmi_camp_rep_c_variety_down}the same gruel every night",
                "{=lmmi_camp_rep_c_variety_down_2}eating the same slop every day",
                "{=lmmi_camp_rep_c_variety_down_3}the same porridge, morning and night")));
            }
            int over = party.Party.NumberOfAllMembers - party.Party.PartySizeLimit;
            if (over > 0) list.Add(new Cause(-TaleWorlds.Library.MathF.Sqrt(over), Pick("{=lmmi_camp_rep_c_size}too many men for too few tents",
                "{=lmmi_camp_rep_c_size_2}sleeping four to a tent",
                "{=lmmi_camp_rep_c_size_3}a column too big for its wagons")));
            var home = CampFoodMorale.Analyze(party);
            if (home.Bonus > 0f) list.Add(new Cause(home.Bonus, Pick("{=lmmi_camp_rep_c_home_up}a taste of home in the pot",
                "{=lmmi_camp_rep_c_home_up_2}food from home in the wagons",
                "{=lmmi_camp_rep_c_home_up_3}the taste of their own country's cooking")));
            if (home.Penalty < 0f) list.Add(new Cause(home.Penalty - 1f, Pick("{=lmmi_camp_rep_c_home_down}nothing from home in the wagons",
                "{=lmmi_camp_rep_c_home_down_2}missing the food they grew up on",
                "{=lmmi_camp_rep_c_home_down_3}not a bite from home in the stores")));
            int leadership = Hero.MainHero.GetSkillValue(DefaultSkills.Leadership);
            if (leadership >= 75) list.Add(new Cause(leadership / 40f, Pick("{=lmmi_camp_rep_c_leader}the way you lead them",
                "{=lmmi_camp_rep_c_leader_2}having a captain they believe in",
                "{=lmmi_camp_rep_c_leader_3}your leadership")));
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
                             "{=lmmi_camp_rep_b_none_2}No fights to speak of lately. The lads are getting soft.",
                             "{=lmmi_camp_rep_b_none_3}We've not fought together yet. That'll come.",
                             "{=lmmi_camp_rep_b_none_4}No blood spilled yet, not since you took command.",
                             "{=lmmi_camp_rep_b_none_5}Nothing to tell about fighting, captain. Not yet.",
                             "{=lmmi_camp_rep_b_none_6}Quiet so far. The new men are still waiting to be tested.");
            else
            {
                float days = last.DaysAgo;
                if (days > 7f)
                    fight = Pick("{=lmmi_camp_rep_b_old_1}It's been {DAYS} days since the last fight. Quiet's good — for a while.",
                                 "{=lmmi_camp_rep_b_old_2}{DAYS} days without a fight. Some of them are itching for one.",
                                 "{=lmmi_camp_rep_b_old_3}{DAYS} days since we last fought. The rust is setting in.",
                                 "{=lmmi_camp_rep_b_old_4}The last fight was {DAYS} days back. Long enough to forget the fear.",
                                 "{=lmmi_camp_rep_b_old_5}{DAYS} days of peace. The veterans don't trust it.",
                                 "{=lmmi_camp_rep_b_old_6}Nothing for {DAYS} days. The young ones are getting bold.");
                else if (last.Outcome > 0)
                    fight = days < 3f
                        ? Pick("{=lmmi_camp_rep_b_won_1}They're still talking about {FOE} — {WHEN}. They'll be telling it for a month.",
                               "{=lmmi_camp_rep_b_won_2}Beating {FOE} {WHEN} put a spring in every step.",
                               "{=lmmi_camp_rep_b_won_3}The win over {FOE} {WHEN} still has them grinning.",
                               "{=lmmi_camp_rep_b_won_4}We gave {FOE} a thrashing {WHEN}. Nobody can sleep for the boasting.",
                               "{=lmmi_camp_rep_b_won_5}{FOE}, {WHEN}. Now there was a fight worth having.",
                               "{=lmmi_camp_rep_b_won_6}Since we beat {FOE} {WHEN}, they walk taller.")
                        : Pick("{=lmmi_camp_rep_b_won_old}We saw off {FOE} {WHEN}. The shine's wearing off, but they remember.",
                               "{=lmmi_camp_rep_b_won_old_2}The victory over {FOE} {WHEN} still warms them, a little.",
                               "{=lmmi_camp_rep_b_won_old_3}They still mention {FOE} now and then — {WHEN}. Fondly.");
                else if (last.Outcome < 0)
                    fight = days < 3f
                        ? Pick("{=lmmi_camp_rep_b_lost_1}That business with {FOE} {WHEN} — they haven't shaken it off.",
                               "{=lmmi_camp_rep_b_lost_2}Nobody talks about {FOE}. Not since {WHEN}. That's how you know.",
                               "{=lmmi_camp_rep_b_lost_3}{FOE} gave us a bloody nose {WHEN}. It still stings.",
                               "{=lmmi_camp_rep_b_lost_4}After {FOE} {WHEN}, some of them have stopped joking.",
                               "{=lmmi_camp_rep_b_lost_5}They keep going over what went wrong with {FOE} {WHEN}.",
                               "{=lmmi_camp_rep_b_lost_6}{FOE}, {WHEN}. Don't bring it up at the fires.")
                        : Pick("{=lmmi_camp_rep_b_lost_old}They've stopped talking about {FOE}. That's a good sign, or a bad one.",
                               "{=lmmi_camp_rep_b_lost_old_2}{FOE}, {WHEN} — it's healing, slowly.",
                               "{=lmmi_camp_rep_b_lost_old_3}Nobody's forgotten {FOE}, but they've stopped dwelling on it.");
                else
                    fight = Pick("{=lmmi_camp_rep_b_fled}We pulled back from {FOE} {WHEN}. Some call it sense. Some call it something else.",
                           "{=lmmi_camp_rep_b_fled_2}We left the field to {FOE} {WHEN}. Nobody's proud of it.",
                           "{=lmmi_camp_rep_b_fled_3}Backing off from {FOE} {WHEN} — the men argue about it at night.");
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
            var line = issues.Count == 0
                ? Pick("{=lmmi_camp_rep_b_fine}{FIGHT} Otherwise, no complaints worth your ear.",
                    "{=lmmi_camp_rep_b_fine_2}{FIGHT} Other than that, the column's in order.",
                    "{=lmmi_camp_rep_b_fine_3}{FIGHT} No other trouble to speak of.")
                : new TextObject("{=lmmi_camp_rep_b_issues}{FIGHT} {ISSUES}");
            line.SetTextVariable("FIGHT", fight);
            if (issues.Count > 0) line.SetTextVariable("ISSUES", Sentences(issues));
            return line;
        }

        private static IEnumerable<TextObject> Issues(MobileParty party)
        {
            float morale = party.Morale;
            int threshold = Campaign.Current.Models.PartyDesertionModel.GetMoraleThresholdForTroopDesertion();
            if (morale < threshold)
                yield return Pick("{=lmmi_camp_rep_i_desert}Men have been slipping away in the night. More will, if nothing changes.",
                    "{=lmmi_camp_rep_i_desert_2}We've lost a few to the night already. The rest are thinking about it.",
                    "{=lmmi_camp_rep_i_desert_3}Every morning there's an empty bedroll or two. They're leaving, captain.");
            else if (morale < threshold + 15f)
                yield return Pick("{=lmmi_camp_rep_i_desert_risk}Some of the newer lads are talking about slipping off in the night.",
                    "{=lmmi_camp_rep_i_desert_risk_2}There's loose talk at some fires — about going home.",
                    "{=lmmi_camp_rep_i_desert_risk_3}A few of the new ones look at the road behind us more than the road ahead.");

            if (party.HasUnpaidWages > 0f)
                yield return Pick("{=lmmi_camp_rep_i_unpaid}And they've not been paid. That won't hold long.",
                    "{=lmmi_camp_rep_i_unpaid_2}And the pay's late. They notice.",
                    "{=lmmi_camp_rep_i_unpaid_3}Pay's owed, and owed pay turns soldiers into bandits.");
            else
            {
                int wage = party.TotalWage;
                if (wage > 0 && Hero.MainHero.Gold < wage * 2)
                    yield return Pick("{=lmmi_camp_rep_i_purse}The pay chest won't cover two more days' wages, and they know it.",
                        "{=lmmi_camp_rep_i_purse_2}The pay chest's nearly dry. The sergeants are nervous.",
                        "{=lmmi_camp_rep_i_purse_3}Two days' wages left in the chest, maybe. They can count, captain.");
            }

            int over = party.Party.NumberOfAllMembers - party.Party.PartySizeLimit;
            if (over > 0)
                yield return Pick("{=lmmi_camp_rep_i_size}We're {N} more than the column can carry — too many for the tents, too many for the pot.",
                    "{=lmmi_camp_rep_i_size_2}{N} more mouths than the column can feed properly. It shows.",
                    "{=lmmi_camp_rep_i_size_3}We've {N} too many for the wagons and tents. Men are sleeping in the open.")
                    .SetTextVariable("N", over);

            var roster = party.MemberRoster;
            int wounded = roster.TotalWoundedRegulars, regulars = roster.TotalRegulars;
            if (wounded >= 3 && regulars > 0 && wounded >= regulars / 4)
                yield return Pick("{=lmmi_camp_rep_i_wounded_many}{N} on the sick list — that's a lot of empty places in the line.",
                    "{=lmmi_camp_rep_i_wounded_many_2}{N} wounded. The surgeon can't keep up.",
                    "{=lmmi_camp_rep_i_wounded_many_3}{N} men laid up. If we fight now, we fight short.").SetTextVariable("N", wounded);
            else if (wounded > 0)
                yield return Pick("{=lmmi_camp_rep_i_wounded}{N} still nursing wounds, nothing that won't mend.",
                    "{=lmmi_camp_rep_i_wounded_2}{N} with cuts and bruises. They'll be fit soon enough.",
                    "{=lmmi_camp_rep_i_wounded_3}{N} on the mend. Nothing serious.").SetTextVariable("N", wounded);

            int prisoners = party.Party.NumberOfPrisoners;
            if (prisoners > 0)
                yield return Pick("{=lmmi_camp_rep_i_prisoners}{N} prisoners to guard and feed — the watch grumbles about it.",
                    "{=lmmi_camp_rep_i_prisoners_2}{N} prisoners slowing us down and eating our bread.",
                    "{=lmmi_camp_rep_i_prisoners_3}Guarding {N} prisoners every night — the men are sick of it.").SetTextVariable("N", prisoners);
        }

        // ---- 3. The wagons ----

        public static TextObject Stores(MobileParty party)
        {
            int days = party.GetNumDaysForFoodToLast();
            var amount = (days <= 1
                    ? Pick("{=lmmi_camp_food_none}The stores are all but gone — we eat tomorrow or we don't.",
                           "{=lmmi_camp_rep_f_none_2}The wagons are near empty. Tomorrow we tighten belts.",
                           "{=lmmi_camp_rep_f_none_3}We're scraping the barrels, captain.",
                           "{=lmmi_camp_rep_f_none_4}There's almost nothing left. We need a market, fast.",
                           "{=lmmi_camp_rep_f_none_5}The cook's boiling the leather straps. That's how bad it is.",
                           "{=lmmi_camp_rep_f_none_6}Empty, near enough. The men know.")
                    : days <= 4
                        ? Pick("{=lmmi_camp_food_low}Food for {DAYS} days, if we're careful.",
                               "{=lmmi_camp_rep_f_low_2}{DAYS} days of food, and that's counting the crusts.",
                               "{=lmmi_camp_rep_f_low_3}Enough for {DAYS} days, no more.",
                               "{=lmmi_camp_rep_f_low_4}We've {DAYS} days of food, if nobody gets greedy.",
                               "{=lmmi_camp_rep_f_low_5}{DAYS} days left in the wagons. Time to think about a market.",
                               "{=lmmi_camp_rep_f_low_6}Short rations soon — we've {DAYS} days' worth.")
                        : Pick("{=lmmi_camp_food_ok}Plenty in the wagons — {DAYS} days' worth.",
                               "{=lmmi_camp_rep_f_ok_2}{DAYS} days of food in the wagons. Nobody's going hungry.",
                               "{=lmmi_camp_rep_f_ok_3}We've food for {DAYS} days. The quartermaster's almost cheerful.",
                               "{=lmmi_camp_rep_f_ok_4}The wagons are heavy — {DAYS} days of eating.",
                               "{=lmmi_camp_rep_f_ok_5}{DAYS} days of food. No worries there.",
                               "{=lmmi_camp_rep_f_ok_6}Plenty to eat. {DAYS} days, by the quartermaster's count."))
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
                        ? Pick("{=lmmi_camp_rep_f_variety_good}There's {LIST} — a proper table.",
                        "{=lmmi_camp_rep_f_variety_good_2}We've {LIST} — the cook's never been happier.",
                        "{=lmmi_camp_rep_f_variety_good_3}{LIST} in the wagons. We eat like lords.")
                        : held.Count >= 2
                            ? Pick("{=lmmi_camp_rep_f_variety_some}There's {LIST}.",
                            "{=lmmi_camp_rep_f_variety_some_2}We've got {LIST}.",
                            "{=lmmi_camp_rep_f_variety_some_3}There's {LIST} to go round.")
                            : Pick("{=lmmi_camp_rep_f_variety_one}Nothing but {LIST}, day after day.",
                            "{=lmmi_camp_rep_f_variety_one_2}{LIST}. Only {LIST}. Always {LIST}.",
                            "{=lmmi_camp_rep_f_variety_one_3}It's {LIST} and nothing else, captain."))
                    .SetTextVariable("LIST", list));
                var missing = new[] { "meat", "fish", "cheese", "beer", "grain" }.Where(id => !held.Contains(id)).Take(3).ToList();
                if (held.Count < 5 && missing.Count > 0)
                    parts.Add(Pick("{=lmmi_camp_rep_f_missing}No {LIST}, though.",
                    "{=lmmi_camp_rep_f_missing_2}Nobody's seen {LIST} in a while.",
                    "{=lmmi_camp_rep_f_missing_3}We could use some {LIST}.").SetTextVariable("LIST", Join(missing.Select(Food).ToList(), or: true)));
            }

            // A taste of home: who's glad, who misses what.
            foreach (var p in CampFoodMorale.Analyze(party).Peoples.Take(3))
            {
                if (p.Held.Count > 0)
                    parts.Add(Pick("{=lmmi_camp_rep_h_glad_1}The {PEOPLE} are glad of the {FOOD}.",
                                   "{=lmmi_camp_rep_h_glad_2}Give the {PEOPLE} their {FOOD} and they'll march anywhere.",
                                   "{=lmmi_camp_rep_h_glad_3}The {PEOPLE} perk up when there's {FOOD} in the pot.",
                                   "{=lmmi_camp_rep_h_glad_4}The {PEOPLE} won't shut up about the {FOOD}. Happily, for once.",
                                   "{=lmmi_camp_rep_h_glad_5}There's {FOOD} for the {PEOPLE}, and they're grateful for it.",
                                   "{=lmmi_camp_rep_h_glad_6}Keep the {FOOD} coming and the {PEOPLE} will follow you anywhere.")
                        .SetTextVariable("PEOPLE", PeopleName(p.Culture)).SetTextVariable("FOOD", Food(p.Held[0])));
                else if (p.Missing.Count > 0)
                    parts.Add(Pick("{=lmmi_camp_rep_h_miss_1}The {PEOPLE} would kill for some {FOOD}.",
                                   "{=lmmi_camp_rep_h_miss_2}The {PEOPLE} keep asking after {FOOD}. There's none.",
                                   "{=lmmi_camp_rep_h_miss_3}The {PEOPLE} grumble there's no {FOOD}.",
                                   "{=lmmi_camp_rep_h_miss_4}Ask the {PEOPLE} what they want and they'll say {FOOD}. Every time.",
                                   "{=lmmi_camp_rep_h_miss_5}The {PEOPLE} would trade their boots for some {FOOD}.",
                                   "{=lmmi_camp_rep_h_miss_6}No {FOOD} for the {PEOPLE}. They notice.")
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
