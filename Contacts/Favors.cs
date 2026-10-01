using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Contacts
{
    /// <summary>
    /// What asking for a favor produced: the notable's reply, whether anything was actually
    /// delivered (only delivered favors cost anything or start a cooldown), and the world effect,
    /// which runs once the conversation closes.
    /// </summary>
    internal sealed class FavorOutcome
    {
        public FavorOutcome(bool delivered, TextObject reply, Action? onConversationEnd = null)
        {
            Delivered = delivered;
            Reply = reply;
            OnConversationEnd = onConversationEnd;
        }

        public bool Delivered { get; }
        public TextObject Reply { get; }
        public Action? OnConversationEnd { get; }
    }

    internal sealed class Favor
    {
        public Favor(string id, string playerText, Func<Hero, bool> appliesTo, Disposition minDisposition,
            float cooldownDays, int relationCost, int waryGoldCost, bool requiresGenuine,
            Func<Hero, TextObject?> unavailableReason, Func<Hero, bool, FavorOutcome> execute,
            bool treacheryAffects = false, bool canBeBait = false)
        {
            Id = id;
            PlayerText = playerText;
            AppliesTo = appliesTo;
            MinDisposition = minDisposition;
            CooldownDays = cooldownDays;
            RelationCost = relationCost;
            WaryGoldCost = waryGoldCost;
            RequiresGenuine = requiresGenuine;
            UnavailableReason = unavailableReason;
            Execute = execute;
            TreacheryAffects = treacheryAffects;
            CanBeBait = canBeBait;
        }

        public string Id { get; }

        /// <summary>Localizable player line; ends with this favor's price variable.</summary>
        public string PlayerText { get; }

        public string PriceVariable => "LMMI_FAVOR_PRICE_" + Id.ToUpperInvariant();
        public Func<Hero, bool> AppliesTo { get; }
        public Disposition MinDisposition { get; }
        public float CooldownDays { get; }
        public int RelationCost { get; }

        /// <summary>Gold asked by a Wary notable. Friendly and Trusted notables do it for free.</summary>
        public int WaryGoldCost { get; }

        /// <summary>A big favor: sycophants flatter you but won't stick their neck out for it.</summary>
        public bool RequiresGenuine { get; }

        /// <summary>Returns why the favor makes no sense right now (e.g. not wanted by the watch), or null.</summary>
        public Func<Hero, TextObject?> UnavailableReason { get; }

        /// <summary>Runs the favor. The flag says whether a treacherous notable is doing it.</summary>
        public Func<Hero, bool, FavorOutcome> Execute { get; }

        /// <summary>A treacherous notable cheats you on this one (so Roguery or a friend's warning can spot it).</summary>
        public bool TreacheryAffects { get; }

        /// <summary>A treacherous notable offers it below the usual bar — to a Wary stranger, for coin. It's bait.</summary>
        public bool CanBeBait { get; }
    }

    internal static class Favors
    {
        public static readonly IReadOnlyList<Favor> All = new[]
        {
            new Favor("gang_hideouts",
                "{=lmmi_favor_gang_hideouts}Anything out there I should know about?{LMMI_FAVOR_PRICE_GANG_HIDEOUTS}",
                n => n.IsGangLeader, Disposition.Wary, cooldownDays: 7, relationCost: 0, waryGoldCost: 300,
                requiresGenuine: false, _ => null, RevealHideouts,
                treacheryAffects: true),

            new Favor("gang_crime",
                "{=lmmi_favor_gang_crime}The watch is looking for me. Make it go away.{LMMI_FAVOR_PRICE_GANG_CRIME}",
                n => n.IsGangLeader, Disposition.Friendly, cooldownDays: 14, relationCost: 5, waryGoldCost: 400,
                requiresGenuine: true, NotWantedHere, ClearCrimeRating,
                treacheryAffects: true, canBeBait: true),

            new Favor("merchant_tips",
                "{=lmmi_favor_merchant_tips}What's worth buying here, and where does it sell?{LMMI_FAVOR_PRICE_MERCHANT_TIPS}",
                n => n.IsMerchant, Disposition.Wary, cooldownDays: 3, relationCost: 0, waryGoldCost: 150,
                requiresGenuine: false, _ => null, GiveTradeTips,
                treacheryAffects: true),

            new Favor("gang_prisoners",
                "{=lmmi_favor_gang_prisoners}Who's rotting in the keep these days?{LMMI_FAVOR_PRICE_GANG_PRISONERS}",
                n => n.IsGangLeader, Disposition.Wary, cooldownDays: 5, relationCost: 0, waryGoldCost: 100,
                requiresGenuine: false, _ => null, (n, _) => NamePrisoners(n)),

            new Favor("village_provisions",
                "{=lmmi_favor_village_provisions}My people are hungry. Can your village spare provisions?{LMMI_FAVOR_PRICE_VILLAGE_PROVISIONS}",
                n => n.IsHeadman || n.IsRuralNotable, Disposition.Wary, cooldownDays: 10, relationCost: 0, waryGoldCost: 120,
                requiresGenuine: false, _ => null, (n, _) => SpareProvisions(n)),

            new Favor("tend_wounded",
                "{=lmmi_favor_tend_wounded}I have wounded men. Is there anyone here who could tend to them?{LMMI_FAVOR_PRICE_TEND_WOUNDED}",
                n => n.IsHeadman || n.IsRuralNotable || n.IsArtisan, Disposition.Friendly, cooldownDays: 7, relationCost: 0, waryGoldCost: 0,
                requiresGenuine: false, _ => null, (n, _) => TendWounded(n)),
        };

        private const int HideoutsRevealed = 2;
        private const int TownsConsideredForTips = 6;
        private const int TipsGiven = 3;
        private const float MaxCrimeCleared = 30f;
        private const int TradeRumorDays = 15;
        private const float CrimeWhenBetrayed = 15f;

        // ---- Gang leader: nearby hideouts ----

        private static FavorOutcome RevealHideouts(Hero gangLeader, bool treacherous)
        {
            var here = gangLeader.CurrentSettlement;
            var hideouts = Hideout.All
                .Where(h => h.IsInfested && !h.IsSpotted)
                .OrderBy(h => Distance(h.Settlement, here))
                .Take(HideoutsRevealed)
                .ToList();

            if (hideouts.Count == 0)
                return new FavorOutcome(false,
                    Flavor.Pick("{=lmmi_favor_gang_hideouts_none}Nothing out there worth your while. The roads have been quiet.",
                        "{=lmmi_favor_gang_hideouts_none_2}Quiet out there. No nests worth mentioning.",
                        "{=lmmi_favor_gang_hideouts_none_3}Nothing worth your time. The bandits have moved on."));

            var places = hideouts.Select(h => NearestPlace(h.Settlement).Name.ToString()).Distinct().ToList();
            var reply = Flavor.Pick("{=lmmi_favor_gang_hideouts_reply}There's a nest of bandits holed up near {PLACES}. You didn't hear it from me.",
                        "{=lmmi_favor_gang_hideouts_reply_2}Bandits are holed up near {PLACES}. Not a word about where you heard it.",
                        "{=lmmi_favor_gang_hideouts_reply_3}There's a hideout near {PLACES}. Keep my name out of it.");
            reply.SetTextVariable("PLACES", JoinWithAnd(places));

            return new FavorOutcome(true, reply, () =>
            {
                foreach (var hideout in hideouts)
                {
                    hideout.IsSpotted = true;
                    hideout.Settlement.IsVisible = true;
                }

                // Bait: a crooked gang leader sends you where his rivals — or his friends — are waiting in numbers.
                if (treacherous)
                {
                    var spawner = Campaign.Current?.GetCampaignBehavior<BanditSpawnCampaignBehavior>();
                    var bait = hideouts[0];
                    int added = 0;
                    for (int i = 0; i < 2 && spawner != null; i++)
                        if (spawner.AddBanditToHideout(bait) != null) added++;
                    Logging.LmmiLog.Info($"TREACHERY: {gangLeader.Name}'s hideout tip is bait — {added} extra bands sent to the hideout near {NearestPlace(bait.Settlement).Name}.");
                }
            });
        }

        // ---- Gang leader: crime rating ----

        private static TextObject? NotWantedHere(Hero gangLeader)
        {
            var faction = gangLeader.CurrentSettlement?.MapFaction;
            return faction == null || faction.MainHeroCrimeRating <= 0
                ? Flavor.Pick("{=lmmi_favor_gang_crime_clean}You're not wanted for anything here.",
                        "{=lmmi_favor_gang_crime_clean_2}Nobody's after you here. Relax.",
                        "{=lmmi_favor_gang_crime_clean_3}Your name's clean in this town.")
                : null;
        }

        private static FavorOutcome ClearCrimeRating(Hero gangLeader, bool treacherous)
        {
            var faction = gangLeader.CurrentSettlement.MapFaction;
            var reply = Flavor.Pick("{=lmmi_favor_gang_crime_reply}Consider it done. A few coins in the right hands and the watch forgets a face.",
                        "{=lmmi_favor_gang_crime_reply_2}It's handled. The right people will forget your face.",
                        "{=lmmi_favor_gang_crime_reply_3}Done. A purse here, a word there, and you were never here.");

            if (treacherous)
            {
                // Same words, opposite deed: the coin buys a tip-off, not silence.
                return new FavorOutcome(true, reply, () =>
                {
                    ChangeCrimeRatingAction.Apply(faction, CrimeWhenBetrayed);
                    var news = new TextObject("{=lmmi_betrayed_crime}The watch seems to know exactly where to find you. {NAME} took your coin — and talked.");
                    news.SetTextVariable("NAME", gangLeader.Name);
                    InformationManager.DisplayMessage(new InformationMessage(news.ToString(), Colors.Red));
                    Logging.LmmiLog.Info($"TREACHERY: {gangLeader.Name} double-crossed the player: crime +{CrimeWhenBetrayed} with {faction.Name}.");
                });
            }

            float amount = Math.Min(faction.MainHeroCrimeRating, MaxCrimeCleared);
            return new FavorOutcome(true, reply, () => ChangeCrimeRatingAction.Apply(faction, -amount));
        }

        // ---- Merchant: trade tips ----

        private static FavorOutcome GiveTradeTips(Hero merchant, bool treacherous)
        {
            var here = merchant.CurrentSettlement?.Town;
            var noTips = new FavorOutcome(false,
                Flavor.Pick("{=lmmi_favor_merchant_tips_none}Nothing worth the trip right now. Ask me again in a few days.",
                        "{=lmmi_favor_merchant_tips_none_2}Nothing worth hauling just now. Come back later.",
                        "{=lmmi_favor_merchant_tips_none_3}The markets are flat this week. Nothing I'd bet on."));
            if (here == null) return noTips;

            var market = here.Settlement.ItemRoster;
            var nearby = Town.AllTowns
                .Where(t => t != here)
                .OrderBy(t => Distance(t.Settlement, here.Settlement))
                .Take(TownsConsideredForTips)
                .ToList();
            if (nearby.Count == 0) return noTips;
            if (treacherous) return FalseTradeTips(merchant, here, nearby);

            // A friend's tip beats tavern gossip: the best margins on goods actually on sale here.
            var tips = Items.AllTradeGoods
                .Where(item => market.GetItemNumber(item) > 0)
                .Select(item =>
                {
                    int buyHere = here.GetItemPrice(item, MobileParty.MainParty, isSelling: false);
                    var best = nearby
                        .Select(t => (Town: t, Sell: t.GetItemPrice(item, MobileParty.MainParty, isSelling: true)))
                        .OrderByDescending(x => x.Sell)
                        .First();
                    return (Item: item, BuyHere: buyHere, best.Town, best.Sell);
                })
                .Where(x => x.Sell - x.BuyHere > Math.Max(5, x.BuyHere / 10))
                .OrderByDescending(x => x.Sell - x.BuyHere)
                .Take(TipsGiven)
                .ToList();

            if (tips.Count == 0) return noTips;

            var lines = tips.Select(x =>
            {
                var line = new TextObject("{=lmmi_favor_merchant_tip}{ITEM} goes for {BUY}{GOLD_ICON} here and fetches {SELL}{GOLD_ICON} in {TOWN}.");
                line.SetTextVariable("ITEM", x.Item.Name);
                line.SetTextVariable("BUY", x.BuyHere);
                line.SetTextVariable("SELL", x.Sell);
                line.SetTextVariable("TOWN", x.Town.Name);
                return line.ToString();
            });

            var reply = new TextObject("{=lmmi_favor_merchant_tips_reply}Here's what I'd do. {TIPS}");
            reply.SetTextVariable("TIPS", string.Join(" ", lines));

            var rumors = tips
                .Select(x => new TradeRumor(x.Town.Settlement, x.Item,
                    x.Town.GetItemPrice(x.Item), x.Town.GetItemPrice(x.Item, null, isSelling: true), TradeRumorDays))
                .ToList();

            return new FavorOutcome(true, reply, () =>
                Campaign.Current?.GetCampaignBehavior<TradeRumorsCampaignBehavior>()?.AddTradeRumors(rumors, here.Settlement));
        }

        /// <summary>
        /// A swindler's tips: real goods on sale here, a real town nearby, and a selling price that isn't.
        /// They go into your trade rumors like any other tip; you find out when you get there.
        /// </summary>
        private static FavorOutcome FalseTradeTips(Hero merchant, Town here, List<Town> nearby)
        {
            var market = here.Settlement.ItemRoster;
            var onSale = Items.AllTradeGoods.Where(item => market.GetItemNumber(item) > 0).ToList();
            if (onSale.Count == 0)
                return new FavorOutcome(false,
                    Flavor.Pick("{=lmmi_favor_merchant_tips_none}Nothing worth the trip right now. Ask me again in a few days.",
                        "{=lmmi_favor_merchant_tips_none_2}Nothing worth hauling just now. Come back later.",
                        "{=lmmi_favor_merchant_tips_none_3}The markets are flat this week. Nothing I'd bet on."));

            var tips = onSale.OrderBy(_ => MBRandom.RandomFloat).Take(TipsGiven).Select(item =>
            {
                int buyHere = here.GetItemPrice(item, MobileParty.MainParty, isSelling: false);
                var town = nearby[MBRandom.RandomInt(nearby.Count)];
                int fakeSell = (int)(buyHere * (1.5f + MBRandom.RandomFloat * 0.4f));
                return (Item: item, BuyHere: buyHere, Town: town, Sell: fakeSell);
            }).ToList();

            var lines = tips.Select(x =>
            {
                var line = new TextObject("{=lmmi_favor_merchant_tip}{ITEM} goes for {BUY}{GOLD_ICON} here and fetches {SELL}{GOLD_ICON} in {TOWN}.");
                line.SetTextVariable("ITEM", x.Item.Name);
                line.SetTextVariable("BUY", x.BuyHere);
                line.SetTextVariable("SELL", x.Sell);
                line.SetTextVariable("TOWN", x.Town.Name);
                return line.ToString();
            });

            var reply = new TextObject("{=lmmi_favor_merchant_tips_reply}Here's what I'd do. {TIPS}");
            reply.SetTextVariable("TIPS", string.Join(" ", lines));

            var rumors = tips
                .Select(x => new TradeRumor(x.Town.Settlement, x.Item, (int)(x.Sell * 1.05f), x.Sell, TradeRumorDays))
                .ToList();

            Logging.LmmiLog.Info($"TREACHERY: {merchant.Name} gave false tips: " +
                                 string.Join("; ", tips.Select(x => $"{x.Item.Name} 'sells for' {x.Sell} in {x.Town.Name} (really {x.Town.GetItemPrice(x.Item, MobileParty.MainParty, isSelling: true)})")));

            return new FavorOutcome(true, reply, () =>
                Campaign.Current?.GetCampaignBehavior<TradeRumorsCampaignBehavior>()?.AddTradeRumors(rumors, here.Settlement));
        }

        // ---- Gang leader: who's in the dungeon ----

        private static FavorOutcome NamePrisoners(Hero gangLeader)
        {
            var settlement = gangLeader.CurrentSettlement;
            var prisoners = settlement?.Party?.PrisonRoster?.GetTroopRoster()
                .Where(e => e.Character != null && e.Character.IsHero && e.Character.HeroObject != null)
                .Select(e => e.Character.HeroObject)
                .ToList() ?? new List<Hero>();

            if (prisoners.Count == 0)
                return new FavorOutcome(false,
                    Flavor.Pick("{=lmmi_favor_gang_prisoners_none}Nobody worth knowing. Drunks and debtors, the lot of them.",
                        "{=lmmi_favor_gang_prisoners_none_2}Nobody important. Thieves and drunkards.",
                        "{=lmmi_favor_gang_prisoners_none_3}Just the usual — debtors and pickpockets."));

            var names = prisoners.Take(5).Select(h => h.Clan != null && h.Clan != Clan.PlayerClan
                ? new TextObject("{=lmmi_favor_prisoner_of_clan}{NAME} of the {CLAN}").SetTextVariable("NAME", h.Name).SetTextVariable("CLAN", h.Clan.Name).ToString()
                : h.Name.ToString()).ToList();

            var reply = Flavor.Pick("{=lmmi_favor_gang_prisoners_reply}Down in the dungeon? {NAMES}. The guards drink more than they should, and some of them owe me. If you ever wanted someone out... you didn't hear it from me.",
                        "{=lmmi_favor_gang_prisoners_reply_2}In the cells? {NAMES}. A few of the guards owe me favors, if it ever comes to that.",
                        "{=lmmi_favor_gang_prisoners_reply_3}{NAMES}, down in the dungeon. The jailers can be bought. Just saying.");
            reply.SetTextVariable("NAMES", JoinWithAnd(names));
            return new FavorOutcome(true, reply);
        }

        // ---- Village: provisions ----

        private static FavorOutcome SpareProvisions(Hero notable)
        {
            var party = MobileParty.MainParty;
            int grain = Math.Max(4, Math.Min(30, party.MemberRoster.TotalManCount / 6));
            var reply = Flavor.Pick("{=lmmi_favor_village_provisions_reply}We can spare {AMOUNT} sacks of grain. It isn't much, but it will carry your people a few days.",
                        "{=lmmi_favor_village_provisions_reply_2}We can give you {AMOUNT} sacks of grain. Not much, but it's honest.",
                        "{=lmmi_favor_village_provisions_reply_3}{AMOUNT} sacks — that's what we can spare. It'll keep your people going a while.");
            reply.SetTextVariable("AMOUNT", grain);
            return new FavorOutcome(true, reply, () =>
            {
                party.ItemRoster.AddToCounts(DefaultItems.Grain, grain);
                var msg = new TextObject("{=lmmi_favor_village_provisions_msg}{NAME} spared you {AMOUNT} grain.");
                msg.SetTextVariable("NAME", notable.Name);
                msg.SetTextVariable("AMOUNT", grain);
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
            });
        }

        // ---- Village or guild: tending the wounded ----

        private static FavorOutcome TendWounded(Hero notable)
        {
            var party = MobileParty.MainParty;
            int wounded = party.MemberRoster.TotalWoundedRegulars;
            if (wounded <= 0)
                return new FavorOutcome(false,
                    Flavor.Pick("{=lmmi_favor_tend_wounded_none}Your people look hale enough to me.",
                        "{=lmmi_favor_tend_wounded_none_2}Your men seem fit enough.",
                        "{=lmmi_favor_tend_wounded_none_3}Nobody here needs tending that I can see."));

            var reply = notable.IsArtisan
                ? Flavor.Pick("{=lmmi_favor_tend_wounded_guild}Bring them to the guildhall. Our bonesetter has seen worse — mostly from our own apprentices.",
                        "{=lmmi_favor_tend_wounded_guild_2}Send them to the guildhall. Our bonesetter is rough, but good.",
                        "{=lmmi_favor_tend_wounded_guild_3}The guild has a surgeon. Bring them along.")
                : Flavor.Pick("{=lmmi_favor_tend_wounded_village}Bring them in. Our wise-woman will see to them. She's stubborn, but she knows her herbs.",
                        "{=lmmi_favor_tend_wounded_village_2}Bring them to the wise-woman. She'll grumble, but she'll heal them.",
                        "{=lmmi_favor_tend_wounded_village_3}Our healer will see to them. She's done it for every war since her grandmother's.");

            return new FavorOutcome(true, reply, () =>
            {
                int healed = 0;
                foreach (var element in party.MemberRoster.GetTroopRoster())
                {
                    if (element.Character == null || element.Character.IsHero || element.WoundedNumber <= 0) continue;
                    int tended = Math.Max(1, element.WoundedNumber / 2);
                    party.MemberRoster.AddToCounts(element.Character, 0, woundedCount: -tended);
                    healed += tended;
                }
                var msg = new TextObject("{=lmmi_favor_tend_wounded_msg}{HEALED} of your wounded were tended in {SETTLEMENT}.");
                msg.SetTextVariable("HEALED", healed);
                msg.SetTextVariable("SETTLEMENT", notable.CurrentSettlement?.Name ?? TextObject.GetEmpty());
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
                Logging.LmmiLog.Info($"Favor: {notable.Name} had {healed} wounded tended.");
            });
        }

        // ---- Helpers ----

        private static float Distance(Settlement a, Settlement b) =>
            a.GetPosition2D.Distance(b.GetPosition2D);

        private static Settlement NearestPlace(Settlement from) =>
            Settlement.All
                .Where(s => s.IsTown || s.IsCastle || s.IsVillage)
                .OrderBy(s => Distance(s, from))
                .First();

        private static string JoinWithAnd(List<string> names)
        {
            if (names.Count == 1) return names[0];
            var text = new TextObject("{=lmmi_favor_and}{FIRST} and {SECOND}");
            text.SetTextVariable("FIRST", string.Join(", ", names.Take(names.Count - 1)));
            text.SetTextVariable("SECOND", names[names.Count - 1]);
            return text.ToString();
        }
    }
}
