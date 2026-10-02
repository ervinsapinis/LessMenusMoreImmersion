using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// The castellan keeps the castle for its lord, and knows what a castle knows: who's on the roads, where the lord is,
    /// who's in the cells, where the bandits lair (he'll mark it on your map), the castle's villages and walls. How much he
    /// tells you depends on who you are: an enemy gets nothing, a resented stranger little, the realm's own the lot. In
    /// your own castle he reports, and does what you tell him: double the watch (security, for the extra pay), lay in
    /// stores (food, at market price).
    /// </summary>
    public partial class CastleLifeBehavior
    {
        private static bool AtWarWithYou(Settlement s) =>
            s.MapFaction != null && Hero.MainHero.MapFaction != null && FactionManager.IsAtWarAgainstFaction(s.MapFaction, Hero.MainHero.MapFaction);

        private static bool Resents(Settlement s) =>
            s.Culture != null && s.Culture != Hero.MainHero.Culture
            && CultureRelations.Multiplier(s.Culture.StringId, Hero.MainHero.Culture?.StringId) > 0f
            && LmmiSettingsProvider.ForeignerPrejudicePercent > 0;

        /// <summary>Your own, the realm's own, the lord's friend, or a name the castle knows.</summary>
        private static bool Trusts(Settlement castle)
        {
            if (castle.OwnerClan == Clan.PlayerClan) return true;
            if (castle.MapFaction != null && castle.MapFaction == Hero.MainHero.MapFaction) return true;
            var owner = castle.OwnerClan?.Leader;
            if (owner != null && owner.GetRelationWithPlayer() >= 10f) return true;
            return TownStandingBehavior.Band(castle) >= StandingBand.Known;
        }

        private static TextObject PlaceNear(CampaignVec2 at)
        {
            var near = SettlementHelper.FindNearestSettlementToPoint(at, s => s.IsTown || s.IsCastle || s.IsVillage);
            return near?.Name ?? new TextObject("{=lmmi_castle_news_wilds}the wilds");
        }

        /// <summary>North, south-east...: map +y is north, +x is east.</summary>
        private static TextObject Direction(CampaignVec2 from, CampaignVec2 to)
        {
            var d = to.ToVec2() - from.ToVec2();
            if (d.LengthSquared < 0.01f) return new TextObject("{=lmmi_castle_dir_here}close by");
            double deg = Math.Atan2(d.y, d.x) * 180.0 / Math.PI;
            int sector = (int)Math.Round(((deg % 360.0) + 360.0) % 360.0 / 45.0) % 8;
            return new TextObject(sector switch
            {
                0 => "{=lmmi_castle_dir_e}east",
                1 => "{=lmmi_castle_dir_ne}north-east",
                2 => "{=lmmi_castle_dir_n}north",
                3 => "{=lmmi_castle_dir_nw}north-west",
                4 => "{=lmmi_castle_dir_w}west",
                5 => "{=lmmi_castle_dir_sw}south-west",
                6 => "{=lmmi_castle_dir_s}south",
                _ => "{=lmmi_castle_dir_se}south-east",
            });
        }

        // ---- What the castellan knows ----

        /// <summary>Where the lord is: in the hall, at another fief, in the field (with an army, at a siege), or a captive.</summary>
        private static TextObject LordWhereabouts(Settlement castle)
        {
            var lord = castle.OwnerClan?.Leader;
            if (lord == null || !lord.IsAlive)
                return Flavor.Pick("{=lmmi_castle_lord_none}There's no lord to speak of. We hold the walls for the realm.",
                        "{=lmmi_castle_lord_none_2}No lord holds this place now. We keep the walls for the realm, and wait.",
                        "{=lmmi_castle_lord_none_3}Lord? There's none. Just us, the walls, and whoever the realm sends next.");
            if (AtWarWithYou(castle))
                return Flavor.Pick("{=lmmi_castle_lord_war}Where my lord is, is no business of an enemy's. Next question — or the gate.",
                        "{=lmmi_castle_lord_war_2}You think I'd tell an enemy where my lord sleeps? Ask something else, or leave.",
                        "{=lmmi_castle_lord_war_3}My lord's whereabouts are not for your ears. Move on.");
            if (!Trusts(castle))
                return (Resents(castle)
                    ? Flavor.Pick("{=lmmi_castle_lord_vague_foreign}Away. And I don't tell a {DEMONYM} more than that.",
                        "{=lmmi_castle_lord_vague_foreign_2}Gone. That's all a {DEMONYM} gets from me.",
                        "{=lmmi_castle_lord_vague_foreign_3}My lord's affairs aren't for foreign ears, {DEMONYM}.")
                    : Flavor.Pick("{=lmmi_castle_lord_vague}Away, on the realm's business. I don't tell strangers more than that.",
                        "{=lmmi_castle_lord_vague_2}Elsewhere. A stranger doesn't need to know where.",
                        "{=lmmi_castle_lord_vague_3}On business. If you need to know more, earn my lord's trust first."))
                    .SetTextVariable("DEMONYM", CultureWords.Demonym(Hero.MainHero.Culture));

            TextObject line;
            if (lord.IsPrisoner)
            {
                var by = lord.PartyBelongedToAsPrisoner;
                if (by?.IsSettlement == true)
                    line = new TextObject("{=lmmi_castle_lord_captive_at}Taken captive. They hold {LORD} in the cells at {PLACE}, and nobody's paid the ransom yet.")
                        .SetTextVariable("PLACE", by.Settlement.Name);
                else if (by?.LeaderHero != null)
                    line = new TextObject("{=lmmi_castle_lord_captive_of}Taken captive by {CAPTOR}. We've had no word since.")
                        .SetTextVariable("CAPTOR", by.LeaderHero.Name);
                else line = new TextObject("{=lmmi_castle_lord_captive}Taken captive. We've had no word since.");
            }
            else if (lord.CurrentSettlement == castle)
                line = new TextObject("{=lmmi_castle_lord_here}Here — up in the hall. Go and see for yourself, if you've business with {LORD}.");
            else if (lord.CurrentSettlement != null)
                line = new TextObject("{=lmmi_castle_lord_at}{LORD} is at {PLACE}, last we heard.").SetTextVariable("PLACE", lord.CurrentSettlement.Name);
            else if (lord.PartyBelongedTo is MobileParty party)
            {
                if (party.BesiegedSettlement != null)
                    line = new TextObject("{=lmmi_castle_lord_siege}{LORD} has {PLACE} under siege. We send what men we can spare.")
                        .SetTextVariable("PLACE", party.BesiegedSettlement.Name);
                else if (party.Army != null && party.Army.LeaderParty != party && party.Army.LeaderParty?.LeaderHero != null)
                    line = new TextObject("{=lmmi_castle_lord_army}Riding with {LEADER}'s army, near {PLACE}. War business.")
                        .SetTextVariable("LEADER", party.Army.LeaderParty.LeaderHero.Name).SetTextVariable("PLACE", PlaceNear(party.Position));
                else if (party.Army != null)
                    line = new TextObject("{=lmmi_castle_lord_own_army}Leading an army in the field — near {PLACE}, the last rider said.")
                        .SetTextVariable("PLACE", PlaceNear(party.Position));
                else
                    line = new TextObject("{=lmmi_castle_lord_field}In the field near {PLACE}, with {MEN} men at {?LORD_FEMALE}her{?}his{\\?} back.")
                        .SetTextVariable("PLACE", PlaceNear(party.Position)).SetTextVariable("MEN", party.MemberRoster.TotalManCount)
                        .SetTextVariable("LORD_FEMALE", lord.IsFemale ? 1 : 0);
            }
            else if (lord.LastKnownClosestSettlement != null)
                line = new TextObject("{=lmmi_castle_lord_last}Travelling. Last we heard, near {PLACE}.").SetTextVariable("PLACE", lord.LastKnownClosestSettlement.Name);
            else line = Flavor.Pick("{=lmmi_castle_lord_unknown}Nobody tells the castellan anything. Away.",
                        "{=lmmi_castle_lord_unknown_2}Couldn't say. The last rider brought no word of my lord.",
                        "{=lmmi_castle_lord_unknown_3}Wherever my lord is, nobody's thought to tell me.");
            line.SetTextVariable("LORD", lord.Name);
            return line;
        }

        /// <summary>Lords and ladies in this castle's cells.</summary>
        private static TextObject Prisoners(Settlement castle)
        {
            var roster = castle.Party?.PrisonRoster;
            var heroes = roster?.GetTroopRoster().Where(e => e.Character != null && e.Character.IsHero && e.Character.HeroObject != null)
                .Select(e => e.Character.HeroObject).ToList() ?? new List<Hero>();
            int common = roster?.TotalRegulars ?? 0;
            var commonLine = new TextObject(common > 0
                ? "{=lmmi_castle_prisoners_common}And {N} common prisoners eating our bread."
                : "{=lmmi_castle_prisoners_empty}Otherwise the cells are empty.").SetTextVariable("N", common);
            if (heroes.Count == 0)
                return (common > 0
                    ? new TextObject("{=lmmi_castle_prisoners_none_common}No one worth a ransom. Just {N} common prisoners eating our bread.")
                    : Flavor.Pick("{=lmmi_castle_prisoners_none}No one. The cells are empty — the rats are bored.",
                        "{=lmmi_castle_prisoners_none_2}Empty cells. Not so much as a cattle thief.",
                        "{=lmmi_castle_prisoners_none_3}Nobody down there. The jailer's taken up whittling.")).SetTextVariable("N", common);
            var list = string.Join(", ", heroes.Select(h => h.Clan != null && h.Clan.Name != null ? $"{h.Name} ({h.Clan.Name})" : h.Name.ToString()));
            bool yours = heroes.Any(h => h.MapFaction != null && h.MapFaction == Hero.MainHero.MapFaction && h.MapFaction != castle.MapFaction);
            var line = new TextObject(AtWarWithYou(castle) && yours
                ? "{=lmmi_castle_prisoners_gloat}We hold {LIST}. Friends of yours, perhaps? Their ransom won't be cheap. {COMMON}"
                : "{=lmmi_castle_prisoners_list}We hold {LIST} in the cells. {COMMON}");
            line.SetTextVariable("LIST", list);
            line.SetTextVariable("COMMON", commonLine);
            return line;
        }

        /// <summary>Bandit bands within a day's ride, and the nearest lair (which he marks on your map).</summary>
        private static TextObject Bandits(Settlement castle, out Settlement? lairToMark)
        {
            lairToMark = null;
            if (AtWarWithYou(castle))
                return Flavor.Pick("{=lmmi_castle_bandits_war}Bandits? Pray they find you before we do.",
                        "{=lmmi_castle_bandits_war_2}Bandits are the least of your worries here, enemy.",
                        "{=lmmi_castle_bandits_war_3}Worry about our crossbows, not the bandits.");
            var at = castle.GatePosition;
            var bands = MobileParty.All.Where(p => p.IsActive && p.IsBandit && p.CurrentSettlement == null && p.Position.Distance(at) < 60f)
                .OrderBy(p => p.Position.Distance(at)).ToList();
            TextObject roads;
            if (bands.Count == 0)
                roads = Flavor.Pick("{=lmmi_castle_bandits_quiet}No bands on the roads near us — the patrols see to that.",
                        "{=lmmi_castle_bandits_quiet_2}The roads are clean. The patrols hang anyone they catch.",
                        "{=lmmi_castle_bandits_quiet_3}No bandits worth the name nearby. We made sure of it.");
            else
            {
                var nearest = bands[0];
                var kind = bands.GroupBy(p => (p.ActualClan?.Name ?? p.MapFaction?.Name)?.ToString() ?? "")
                    .OrderByDescending(g => g.Count()).First().Key;
                roads = new TextObject(bands.Count == 1
                        ? "{=lmmi_castle_bandits_one}One band on the roads — {KIND}, last seen {DIRECTION} of here, near {PLACE}."
                        : "{=lmmi_castle_bandits_bands}{COUNT} bands roam within a day's ride — {KIND}, mostly. The nearest was seen {DIRECTION} of here, near {PLACE}.")
                    .SetTextVariable("COUNT", bands.Count)
                    .SetTextVariable("KIND", string.IsNullOrEmpty(kind) ? new TextObject("{=lmmi_castle_bandits_kind}cutthroats") : new TextObject(kind))
                    .SetTextVariable("DIRECTION", Direction(at, nearest.Position))
                    .SetTextVariable("PLACE", PlaceNear(nearest.Position));
            }
            var lair = Settlement.All.Where(s => s.IsHideout && s.Hideout != null && s.Hideout.IsInfested && s.Position.Distance(at) < 120f)
                .OrderBy(s => s.Position.Distance(at)).FirstOrDefault();
            TextObject lairLine;
            if (lair == null) lairLine = Flavor.Pick("{=lmmi_castle_bandits_no_lair}Where they lair, nobody knows. Somewhere far off, I hope.",
                        "{=lmmi_castle_bandits_no_lair_2}Their hole could be anywhere. Nobody's found it yet.",
                        "{=lmmi_castle_bandits_no_lair_3}No one knows where they hide. Somewhere far, with luck.");
            else if (!Trusts(castle) && Resents(castle))
                lairLine = Flavor.Pick("{=lmmi_castle_bandits_lair_secret}Where they lair is the garrison's business, not a {DEMONYM}'s.",
                        "{=lmmi_castle_bandits_lair_secret_2}Where they hide is for the garrison to know, not a {DEMONYM}.",
                        "{=lmmi_castle_bandits_lair_secret_3}That's garrison business. A {DEMONYM} doesn't need to know.")
                    .SetTextVariable("DEMONYM", CultureWords.Demonym(Hero.MainHero.Culture));
            else
            {
                if (!lair.Hideout.IsSpotted) lairToMark = lair;
                lairLine = new TextObject(lair.Hideout.IsSpotted
                        ? "{=lmmi_castle_bandits_lair_known}Their lair's {DIRECTION} of here, in the wilds near {PLACE}. You'll know the place."
                        : "{=lmmi_castle_bandits_lair}Their lair's {DIRECTION} of here, in the wilds near {PLACE}. I'll mark it on your map — burn it out and the garrison will drink to you.")
                    .SetTextVariable("DIRECTION", Direction(at, lair.Position))
                    .SetTextVariable("PLACE", PlaceNear(lair.Position));
            }
            return new TextObject("{=lmmi_castle_bandits}{ROADS} {LAIR}").SetTextVariable("ROADS", roads).SetTextVariable("LAIR", lairLine);
        }

        /// <summary>The castellan marks a lair on your map: vanilla's own spotting.</summary>
        private static void MarkLair(Settlement lair)
        {
            try
            {
                if (lair.Hideout == null || lair.Hideout.IsSpotted) return;
                lair.Hideout.IsSpotted = true;
                lair.IsVisible = true;
                CampaignEventDispatcher.Instance?.OnHideoutSpotted(MobileParty.MainParty.Party, lair.Party);
                var msg = new TextObject("{=lmmi_castle_lair_marked}The castellan marks a bandit lair on your map.");
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Yellow));
                LmmiLog.Info($"Castle: the castellan marked {lair.Name} on your map.");
            }
            catch (Exception ex) { LmmiLog.Error("Castle: marking the lair threw", ex); }
        }

        private static TextObject VillageState(Village v)
        {
            return new TextObject(v.VillageState switch
            {
                Village.VillageStates.Looted => "{=lmmi_castle_village_looted}burned out",
                Village.VillageStates.BeingRaided => "{=lmmi_castle_village_raided}being raided as we speak",
                Village.VillageStates.ForcedForSupplies => "{=lmmi_castle_village_squeezed}squeezed by soldiers",
                Village.VillageStates.ForcedForVolunteers => "{=lmmi_castle_village_squeezed}squeezed by soldiers",
                _ => v.Hearth < 200f ? "{=lmmi_castle_village_poor}struggling"
                    : v.Hearth < 600f ? "{=lmmi_castle_village_middling}getting by"
                    : "{=lmmi_castle_village_thriving}thriving",
            });
        }

        /// <summary>Who holds it and for whom, its villages and how they fare, its walls and (if he trusts you) its garrison.</summary>
        private static TextObject About(Settlement castle)
        {
            var clan = castle.OwnerClan;
            var line = new TextObject(clan?.Leader != null
                ? "{=lmmi_castle_about}{CASTLE} belongs to the {CLAN}; {LORD} holds it for {REALM}. {VILLAGES} {GARRISON}"
                : "{=lmmi_castle_about_nolord}{CASTLE} is held for {REALM}. {VILLAGES} {GARRISON}");
            line.SetTextVariable("CASTLE", castle.Name);
            line.SetTextVariable("CLAN", clan?.Name ?? TextObject.GetEmpty());
            line.SetTextVariable("LORD", clan?.Leader?.Name ?? TextObject.GetEmpty());
            line.SetTextVariable("REALM", castle.MapFaction?.Name ?? new TextObject("{=lmmi_castle_no_lord}the realm"));
            var villages = castle.BoundVillages.Where(v => v?.Settlement != null).ToList();
            line.SetTextVariable("VILLAGES", villages.Count == 0
                ? new TextObject("{=lmmi_castle_about_no_villages}No villages of its own — it lives on what the roads bring.")
                : new TextObject("{=lmmi_castle_about_villages}Its villages: {LIST}.")
                    .SetTextVariable("LIST", string.Join(", ", villages.Select(v => $"{v.Settlement.Name} ({VillageState(v)})"))));
            int wall = castle.Town?.GetWallLevel() ?? 1;
            var walls = new TextObject(wall >= 3 ? "{=lmmi_castle_walls_3}behind walls no one has breached in living memory"
                : wall == 2 ? "{=lmmi_castle_walls_2}behind good stone"
                : "{=lmmi_castle_walls_1}behind old walls that could use a mason");
            int men = castle.Town?.GarrisonParty?.MemberRoster.TotalManCount ?? 0;
            line.SetTextVariable("GARRISON", AtWarWithYou(castle)
                ? new TextObject("{=lmmi_castle_about_garrison_war}As for the garrison: enough to see you off.")
                : Trusts(castle)
                    ? new TextObject("{=lmmi_castle_about_garrison}{MEN} men on the walls, {WALLS}.").SetTextVariable("MEN", men).SetTextVariable("WALLS", walls)
                    : new TextObject("{=lmmi_castle_about_garrison_vague}Enough men on the walls, {WALLS}.").SetTextVariable("WALLS", walls));
            return line;
        }

        // ---- Your own castle ----

        private static TextObject Report(Settlement castle)
        {
            var town = castle.Town;
            var garrison = town?.GarrisonParty?.MemberRoster.TotalManCount ?? 0;
            int prisoners = castle.Party?.PrisonRoster?.TotalManCount ?? 0;
            float food = town?.FoodStocks ?? 0f;
            float change = town?.FoodChange ?? 0f;
            int days = change < 0f ? (int)(food / -change) : 99;
            var line = new TextObject("{=lmmi_castle_report}{GARRISON} men in the garrison, {PRISONERS} in the cells. {FOOD} Loyalty is {LOYALTY}, the roads are {SECURITY}.");
            line.SetTextVariable("GARRISON", garrison);
            line.SetTextVariable("PRISONERS", prisoners);
            line.SetTextVariable("FOOD", new TextObject(days >= 99
                ? "{=lmmi_castle_report_food_ok}The stores are holding."
                : "{=lmmi_castle_report_food_days}Food for {DAYS} days at this rate.").SetTextVariable("DAYS", days));
            line.SetTextVariable("LOYALTY", Word(town?.Loyalty ?? 50f));
            line.SetTextVariable("SECURITY", Word(town?.Security ?? 50f));
            return line;
        }

        private static TextObject Word(float value) => new TextObject(value >= 70f ? "{=lmmi_castle_word_high}good"
            : value >= 40f ? "{=lmmi_castle_word_mid}middling" : "{=lmmi_castle_word_low}poor");

        private static int WatchCost(Settlement castle) =>
            Math.Min(600, 150 + 2 * (castle.Town?.GarrisonParty?.MemberRoster.TotalManCount ?? 0));

        private const float WatchSecurity = 10f;

        private void DoubleTheWatch(Settlement castle)
        {
            var town = castle.Town;
            if (town == null) return;
            int cost = WatchCost(castle);
            Hero.MainHero.ChangeHeroGold(-cost);
            float before = town.Security;
            town.Security = TaleWorlds.Library.MathF.Min(100f, town.Security + WatchSecurity);
            Cooldown("watch:" + castle.StringId, 7f);
            var msg = new TextObject("{=lmmi_castle_watch_done}The watch is doubled, and the extra shifts are paid. {CASTLE}: Security +{N}.");
            msg.SetTextVariable("CASTLE", castle.Name);
            msg.SetTextVariable("N", (int)Math.Round(town.Security - before));
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
            LmmiLog.Info($"Castle: doubled the watch at {castle.Name} for {cost} (security {before:0} -> {town.Security:0}).");
        }

        private static int StoresRoom(Settlement castle)
        {
            var town = castle.Town;
            if (town == null) return 0;
            return Math.Max(0, Math.Min(40, (int)(town.FoodStocksUpperLimit() - town.FoodStocks)));
        }

        private const int StoresPricePerUnit = 12;

        private void LayInStores(Settlement castle)
        {
            var town = castle.Town;
            int add = StoresRoom(castle);
            if (town == null || add <= 0) return;
            int cost = add * StoresPricePerUnit;
            Hero.MainHero.ChangeHeroGold(-cost);
            town.FoodStocks += add;
            Cooldown("stores:" + castle.StringId, 7f);
            var msg = new TextObject("{=lmmi_castle_stores_done}Carts of grain and salt meat roll in through the gate. {CASTLE}: Food +{N}.");
            msg.SetTextVariable("CASTLE", castle.Name);
            msg.SetTextVariable("N", add);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
            LmmiLog.Info($"Castle: laid in {add} food at {castle.Name} for {cost}.");
        }

        // ---- Veterans ----

        /// <summary>Would the castellan let some of the garrison go to you? Your own realm, the lord's friend, or a name the castle respects.</summary>
        private static bool CanSpareVeterans(Settlement castle, out TextObject refusal)
        {
            refusal = TextObject.GetEmpty();
            var owner = castle.OwnerClan?.Leader;
            bool sameRealm = castle.MapFaction != null && castle.MapFaction == Hero.MainHero.MapFaction;
            bool friend = owner != null && owner.GetRelationWithPlayer() >= 10f;
            bool known = TownStandingBehavior.Band(castle) >= StandingBand.Known;
            if (AtWarWithYou(castle))
            {
                refusal = Flavor.Pick("{=lmmi_castle_vets_war}Our men, for an enemy of the realm? Get out of my sight before I call the guard.",
                        "{=lmmi_castle_vets_war_2}Men for an enemy? Leave, before I have you thrown from the walls.",
                        "{=lmmi_castle_vets_war_3}Give our soldiers to our enemy? Get out.");
                return false;
            }
            if (sameRealm || friend || known) return true;
            float resentment = CultureRelations.Multiplier(castle.Culture?.StringId, Hero.MainHero.Culture?.StringId)
                               * LmmiSettingsProvider.ForeignerPrejudicePercent / 100f;
            refusal = (resentment > 0f && castle.Culture != Hero.MainHero.Culture
                ? Flavor.Pick("{=lmmi_castle_vets_foreign}The garrison isn't for hire — least of all to a {DEMONYM}.",
                        "{=lmmi_castle_vets_foreign_2}Our soldiers, serving a {DEMONYM}? Not while I keep this castle.",
                        "{=lmmi_castle_vets_foreign_3}The garrison serves this castle, not some passing {DEMONYM}.")
                : Flavor.Pick("{=lmmi_castle_vets_stranger}My lord doesn't know you. The garrison isn't for hire to strangers.",
                        "{=lmmi_castle_vets_stranger_2}You're a stranger to my lord. No men for you.",
                        "{=lmmi_castle_vets_stranger_3}Earn my lord's trust first. Then we'll talk about soldiers."));
            refusal.SetTextVariable("DEMONYM", CultureWords.Demonym(Hero.MainHero.Culture));
            return false;
        }

        /// <summary>Seasoned men (tier 3+) the garrison could spare: at most five, and never more than an eighth of it.</summary>
        private void OfferVeterans(Settlement castle)
        {
            _veterans = new List<(CharacterObject, int)>();
            _veteranCost = 0;
            var garrison = castle.Town?.GarrisonParty;
            if (garrison == null) return;
            int total = garrison.MemberRoster.TotalHealthyCount;
            int spare = Math.Min(5, total / 8);
            foreach (var e in garrison.MemberRoster.GetTroopRoster().Where(e => e.Character != null && !e.Character.IsHero && e.Character.Tier >= 3)
                         .OrderByDescending(e => e.Character.Tier))
            {
                if (spare <= 0) break;
                int n = Math.Min(spare, e.Number - e.WoundedNumber);
                if (n <= 0) continue;
                _veterans.Add((e.Character, n));
                _veteranCost += n * 2 * Campaign.Current.Models.PartyWageModel.GetTroopRecruitmentCost(e.Character, Hero.MainHero).RoundedResultNumber;
                spare -= n;
            }
        }

        private void TakeVeterans(Settlement castle)
        {
            var garrison = castle.Town?.GarrisonParty;
            if (garrison == null || _veterans.Count == 0) return;
            Hero.MainHero.ChangeHeroGold(-_veteranCost);
            var owner = castle.OwnerClan?.Leader;
            if (owner != null && owner != Hero.MainHero) owner.ChangeHeroGold(_veteranCost / 2);
            int men = 0;
            foreach (var e in _veterans)
            {
                garrison.MemberRoster.AddToCounts(e.Character, -e.Number);
                MobileParty.MainParty.MemberRoster.AddToCounts(e.Character, e.Number);
                men += e.Number;
            }
            Cooldown("vets:" + castle.StringId, 7f);
            var msg = new TextObject("{=lmmi_castle_vets_taken}{MEN} of {CASTLE}'s garrison join your company.");
            msg.SetTextVariable("MEN", men);
            msg.SetTextVariable("CASTLE", castle.Name);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
            LmmiLog.Info($"Castle: {men} veterans from {castle.Name} for {_veteranCost}.");
            _veterans.Clear();
        }

        // ---- Dialogs ----

        private void AddCastellanDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_castle_castellan", "start", "lmmi_castle_castellan_resp",
                "{=lmmi_castle_castellan}{LMMI_CASTELLAN}",
                () =>
                {
                    if (!TalkingTo(_castellan)) return false;
                    var castle = _castle!;
                    var owner = castle.OwnerClan?.Leader;
                    bool resent = castle.Culture != Hero.MainHero.Culture
                                  && CultureRelations.Multiplier(castle.Culture?.StringId, Hero.MainHero.Culture?.StringId) > 0f;
                    var line = (castle.OwnerClan == Clan.PlayerClan
                        ? Flavor.Pick("{=lmmi_castle_castellan_owner}My {?PLAYER.GENDER}lady{?}lord{\\?}. The garrison stands ready. What do you need?",
                        "{=lmmi_castle_castellan_owner_2}My {?PLAYER.GENDER}lady{?}lord{\\?}. All's well at {CASTLE}. What are your orders?",
                        "{=lmmi_castle_castellan_owner_3}My {?PLAYER.GENDER}lady{?}lord{\\?}. The walls are manned and the stores counted. How can I serve?")
                        : resent
                            ? Flavor.Pick("{=lmmi_castle_castellan_foreign}I keep {CASTLE} for {LORD}. State your business, {DEMONYM}, and be quick about it.",
                        "{=lmmi_castle_castellan_foreign_2}I keep {CASTLE} for {LORD}. A {DEMONYM} at my door — say what you want and be brief.",
                        "{=lmmi_castle_castellan_foreign_3}{CASTLE} answers to {LORD}, not to {DEMONYM} visitors. What do you want?")
                            : Flavor.Pick("{=lmmi_castle_castellan_kin}I keep {CASTLE} for {LORD}. What's your business?",
                        "{=lmmi_castle_castellan_kin_2}I keep {CASTLE} for {LORD}. What can I do for you?",
                        "{=lmmi_castle_castellan_kin_3}Castellan of {CASTLE}, in {LORD}'s name. Speak."));
                    line.SetTextVariable("CASTLE", castle.Name);
                    line.SetTextVariable("LORD", owner?.Name ?? new TextObject("{=lmmi_castle_no_lord}the realm"));
                    line.SetTextVariable("DEMONYM", CultureWords.Demonym(Hero.MainHero.Culture));
                    MBTextManager.SetTextVariable("LMMI_CASTELLAN", line);
                    return true;
                }, null, 1200);

            // A question, an answer, and back to the options.
            void Ask(string id, string question, Func<bool>? when, Func<TextObject> answer, Action? afterwards = null)
            {
                starter.AddPlayerLine(id, "lmmi_castle_castellan_resp", id + "_resp", question,
                    when == null ? (ConversationSentence.OnConditionDelegate?)null : () => when(), null);
                starter.AddDialogLine(id + "_resp", id + "_resp", "lmmi_castle_castellan_resp", "{=lmmi_castle_castellan_answer}{LMMI_CASTLE_ANSWER}",
                    () => { MBTextManager.SetTextVariable("LMMI_CASTLE_ANSWER", answer()); return true; },
                    afterwards == null ? (ConversationSentence.OnConsequenceDelegate?)null : () => afterwards());
            }

            // News from the roads.
            starter.AddPlayerLine("lmmi_castle_castellan_news", "lmmi_castle_castellan_resp", "lmmi_castle_castellan_news_resp",
                "{=lmmi_castle_castellan_news}Any news from the roads?", null, null);
            starter.AddDialogLine("lmmi_castle_castellan_news_resp", "lmmi_castle_castellan_news_resp", "lmmi_castle_castellan_resp",
                "{=lmmi_castle_castellan_news_line}{LMMI_CASTLE_NEWS}",
                () => { MBTextManager.SetTextVariable("LMMI_CASTLE_NEWS", News(_castle!)); return true; }, null);

            Ask("lmmi_castle_castellan_lord", "{=lmmi_castle_castellan_lord}Where is your lord?",
                () => _castle != null && _castle.OwnerClan != Clan.PlayerClan, () => LordWhereabouts(_castle!));
            Ask("lmmi_castle_castellan_prisoners", "{=lmmi_castle_castellan_prisoners}Any prisoners of note?",
                null, () => Prisoners(_castle!));
            Settlement? lair = null;
            Ask("lmmi_castle_castellan_bandits", "{=lmmi_castle_castellan_bandits}Bandits about?",
                null, () => Bandits(_castle!, out lair), () => { if (lair != null) MarkLair(lair); lair = null; });
            Ask("lmmi_castle_castellan_about", "{=lmmi_castle_castellan_about}Tell me about this castle.",
                () => _castle != null && _castle.OwnerClan != Clan.PlayerClan, () => About(_castle!));

            // Your own castle: the report, and orders.
            starter.AddPlayerLine("lmmi_castle_castellan_report", "lmmi_castle_castellan_resp", "lmmi_castle_castellan_report_resp",
                "{=lmmi_castle_castellan_report}Report.", () => _castle?.OwnerClan == Clan.PlayerClan, null);
            starter.AddDialogLine("lmmi_castle_castellan_report_resp", "lmmi_castle_castellan_report_resp", "lmmi_castle_castellan_resp",
                "{=lmmi_castle_castellan_report_line}{LMMI_CASTLE_REPORT}",
                () => { MBTextManager.SetTextVariable("LMMI_CASTLE_REPORT", Report(_castle!)); return true; }, null);

            starter.AddPlayerLine("lmmi_castle_castellan_watch", "lmmi_castle_castellan_resp", "lmmi_castle_castellan_watch_resp",
                "{=lmmi_castle_castellan_watch}Double the watch, and pay the men for it. [{LMMI_CASTLE_WATCH_COST}{GOLD_ICON}]",
                () =>
                {
                    if (_castle?.OwnerClan != Clan.PlayerClan) return false;
                    MBTextManager.SetTextVariable("LMMI_CASTLE_WATCH_COST", WatchCost(_castle));
                    return true;
                },
                () => { var c = _castle; if (c != null) DoubleTheWatch(c); }, 100,
                (out TextObject why) =>
                {
                    why = TextObject.GetEmpty();
                    if (_castle == null) return false;
                    if (!Ready("watch:" + _castle.StringId)) { why = new TextObject("{=lmmi_castle_watch_tired}The men are already standing double shifts. Another week and they'll drop."); return false; }
                    if (Hero.MainHero.Gold >= WatchCost(_castle)) return true;
                    why = new TextObject("{=lmmi_cant_afford_alms}You don't have that much on you.");
                    return false;
                });
            starter.AddDialogLine("lmmi_castle_castellan_watch_resp", "lmmi_castle_castellan_watch_resp", "lmmi_castle_castellan_resp",
                "{=!}{LMMI_CASTELLAN_WATCH}",
                    () => Flavor.Say("LMMI_CASTELLAN_WATCH",
                        "{=lmmi_castle_castellan_watch_resp}Double it is. Nobody'll come near the walls without us knowing — and the roads will feel it too.",
                        "{=lmmi_castle_castellan_watch_resp_2}Double shifts it is. The men won't love it, but the coin will help.",
                        "{=lmmi_castle_castellan_watch_resp_3}I'll have torches on every tower tonight. Nothing will move out there without us seeing."), null);

            starter.AddPlayerLine("lmmi_castle_castellan_stores", "lmmi_castle_castellan_resp", "lmmi_castle_castellan_stores_resp",
                "{=lmmi_castle_castellan_stores}Lay in stores against a siege. [{LMMI_CASTLE_STORES_COST}{GOLD_ICON}]",
                () =>
                {
                    if (_castle?.OwnerClan != Clan.PlayerClan) return false;
                    MBTextManager.SetTextVariable("LMMI_CASTLE_STORES_COST", Math.Max(1, StoresRoom(_castle)) * StoresPricePerUnit);
                    return true;
                },
                () => { var c = _castle; if (c != null) LayInStores(c); }, 100,
                (out TextObject why) =>
                {
                    why = TextObject.GetEmpty();
                    if (_castle == null) return false;
                    if (!Ready("stores:" + _castle.StringId)) { why = new TextObject("{=lmmi_castle_stores_tired}The carts went out only days ago. The merchants have nothing more to sell us."); return false; }
                    if (StoresRoom(_castle) <= 0) { why = new TextObject("{=lmmi_castle_stores_full}The granaries are full to the rafters."); return false; }
                    if (Hero.MainHero.Gold >= StoresRoom(_castle) * StoresPricePerUnit) return true;
                    why = new TextObject("{=lmmi_cant_afford_alms}You don't have that much on you.");
                    return false;
                });
            starter.AddDialogLine("lmmi_castle_castellan_stores_resp", "lmmi_castle_castellan_stores_resp", "lmmi_castle_castellan_resp",
                "{=!}{LMMI_CASTELLAN_STORES}",
                    () => Flavor.Say("LMMI_CASTELLAN_STORES",
                        "{=lmmi_castle_castellan_stores_resp}I'll send the carts out today. Let them come — we'll still be eating when they're boiling their boots.",
                        "{=lmmi_castle_castellan_stores_resp_2}The carts go out at first light. We'll have grain to the rafters.",
                        "{=lmmi_castle_castellan_stores_resp_3}Good thinking. Full granaries win more sieges than tall walls."), null);

            // Someone else's castle: veterans the lord can spare.
            starter.AddPlayerLine("lmmi_castle_castellan_vets", "lmmi_castle_castellan_resp", "lmmi_castle_castellan_vets_resp",
                "{=lmmi_castle_castellan_vets}Could your lord spare a few of the garrison?",
                () => _castle != null && _castle.OwnerClan != Clan.PlayerClan, null, 100,
                (out TextObject why) =>
                {
                    why = TextObject.GetEmpty();
                    if (_castle == null || Ready("vets:" + _castle.StringId)) return true;
                    why = new TextObject("{=lmmi_castle_vets_spent}He's already spared what he can this week.");
                    return false;
                });
            starter.AddDialogLine("lmmi_castle_castellan_vets_no", "lmmi_castle_castellan_vets_resp", "lmmi_castle_castellan_resp",
                "{=lmmi_castle_castellan_vets_no_line}{LMMI_CASTLE_REFUSAL}",
                () =>
                {
                    if (CanSpareVeterans(_castle!, out var refusal)) return false;
                    MBTextManager.SetTextVariable("LMMI_CASTLE_REFUSAL", refusal);
                    return true;
                }, null);
            starter.AddDialogLine("lmmi_castle_castellan_vets_none", "lmmi_castle_castellan_vets_resp", "lmmi_castle_castellan_resp",
                "{=lmmi_castle_castellan_vets_none}The garrison's thin as it is. I can't spare a single man.",
                () =>
                {
                    if (!CanSpareVeterans(_castle!, out _)) return false;
                    OfferVeterans(_castle!);
                    return _veterans.Count == 0;
                }, null);
            starter.AddDialogLine("lmmi_castle_castellan_vets_offer", "lmmi_castle_castellan_vets_resp", "lmmi_castle_castellan_vets_deal",
                "{=lmmi_castle_castellan_vets_offer}{LMMI_CASTLE_OFFER}",
                () =>
                {
                    if (_veterans.Count == 0) return false;
                    var line = new TextObject("{=lmmi_castle_castellan_vets_offer_line}I can spare {MEN} of them — {LIST}. {COST} denars, to cover their pay and my lord's trouble.");
                    line.SetTextVariable("MEN", _veterans.Sum(v => v.Number));
                    line.SetTextVariable("LIST", string.Join(", ", _veterans.Select(v => $"{v.Number} {v.Character.Name}")));
                    line.SetTextVariable("COST", _veteranCost);
                    MBTextManager.SetTextVariable("LMMI_CASTLE_OFFER", line);
                    MBTextManager.SetTextVariable("LMMI_CASTLE_VETS_COST", _veteranCost);
                    return true;
                }, null);
            starter.AddPlayerLine("lmmi_castle_castellan_vets_yes", "lmmi_castle_castellan_vets_deal", "lmmi_castle_castellan_vets_done",
                "{=lmmi_castle_castellan_vets_yes}Done. [{LMMI_CASTLE_VETS_COST}{GOLD_ICON}]", null,
                () => { var c = _castle; if (c != null) TakeVeterans(c); }, 100,
                (out TextObject why) =>
                {
                    why = TextObject.GetEmpty();
                    if (Hero.MainHero.Gold >= _veteranCost) return true;
                    why = new TextObject("{=lmmi_cant_afford_alms}You don't have that much on you.");
                    return false;
                });
            starter.AddDialogLine("lmmi_castle_castellan_vets_done", "lmmi_castle_castellan_vets_done", "lmmi_castle_castellan_resp",
                "{=!}{LMMI_CASTELLAN_VETS_DONE}",
                    () => Flavor.Say("LMMI_CASTELLAN_VETS_DONE",
                        "{=lmmi_castle_castellan_vets_done}They'll report to your sergeant. Feed them well — they're used to it.",
                        "{=lmmi_castle_castellan_vets_done_2}They're yours now. Good men — bring them back alive if you can.",
                        "{=lmmi_castle_castellan_vets_done_3}Done. They'll grumble about leaving, but they'll fight for you."), null);
            starter.AddPlayerLine("lmmi_castle_castellan_vets_no_thanks", "lmmi_castle_castellan_vets_deal", "lmmi_castle_castellan_resp",
                "{=lmmi_castle_castellan_vets_no_thanks}Too rich for me.", null, () => _veterans.Clear());

            starter.AddPlayerLine("lmmi_castle_castellan_leave", "lmmi_castle_castellan_resp", "close_window",
                "{=lmmi_castle_castellan_leave}That's all.", null, null);
        }
    }
}
