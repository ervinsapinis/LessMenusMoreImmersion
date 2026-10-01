using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Helpers;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// The tavern keeper hears everything and serves you as the town speaks of you (town standing). Despised, he tells
    /// you to get out (unless you pay over the odds to be served); disliked, he's curt, sells his rumors dear and won't
    /// gossip about his regulars; known and better, the first cup of the day is on the house and his news costs less
    /// (honored: nothing at all) and is better. He'll stand the house a round on your coin once a week (the town
    /// remembers it), rent you a bed for the night (you wake rested), and sell what he's heard: hostile armies nearby, who
    /// in town would like to see you hurt — and whether sellswords have been asking after you — what the notables make of
    /// you, and where the lord of the town is these days. Vanilla's own options (work, companions, the owner's clan) stay.
    /// </summary>
    public partial class TavernLifeBehavior
    {
        private enum Topic { Armies, Enemies, Notables, Lord }

        private const float RoundCooldownDays = 7f;
        private const int ServeDespisedFee = 100;

        // "what:settlementId" -> campaign hour it's available again (a round, a room, a free cup, a rumor already paid for)
        [NonSerialized] private Dictionary<string, double> _keeperUntil = new Dictionary<string, double>();
        [NonSerialized] private Mission? _servedDespisedIn;
        [NonSerialized] private TextObject _rumor = TextObject.GetEmpty();
        [NonSerialized] private int _restPhase;            // 0 none, 1 booked (waiting for the talk to end), 2 the screen is dark
        [NonSerialized] private float _restAt, _restHeal, _keeperTime;
        [NonSerialized] private bool _restNeedsFadeIn;

        private static Settlement? Here => Settlement.CurrentSettlement;
        private static StandingBand BandHere => TownStandingBehavior.Band(Here);

        /// <summary>The keeper of a town's tavern, in the tavern (not a hero, not anyone else behind the bar).</summary>
        private static bool TalkingToKeeper()
        {
            if (!LmmiSettingsProvider.EnableTavernGames) return false;
            if (CampaignMission.Current?.Location?.StringId != "tavern") return false;
            if (Here?.IsTown != true) return false;
            var co = CharacterObject.OneToOneConversationCharacter;
            return co != null && !co.IsHero && co.Occupation == Occupation.Tavernkeeper;
        }

        private string Key(string what) => what + ":" + (Here?.StringId ?? "");

        private bool KeeperReady(string what) =>
            !_keeperUntil.TryGetValue(Key(what), out var until) || until <= CampaignTime.Now.ToHours;

        private void KeeperHold(string what, float days) =>
            _keeperUntil[Key(what)] = CampaignTime.Now.ToHours + days * CampaignTime.HoursInDay;

        private static int Prosperity => (int)(Here?.Town?.Prosperity ?? 3000f);

        private static int RoundTo(float value, int step) => Math.Max(0, (int)Math.Round(value / step) * step);

        /// <summary>What he charges you for his news and his beds, against what he'd charge a stranger.</summary>
        private static float Markup(StandingBand band) => band switch
        {
            StandingBand.Honored => 0f,
            StandingBand.Respected => 0.5f,
            StandingBand.Known => 0.75f,
            StandingBand.Disliked => 2f,
            StandingBand.Despised => 2f,
            _ => 1f,
        };

        /// <summary>A round for the whole house: dearer in a rich town (and for a name he'd rather not serve).</summary>
        private static int RoundCost()
        {
            float cost = 40f + Prosperity / 25f;
            if (BandHere <= StandingBand.Disliked) cost *= 1.5f;
            return Math.Max(20, RoundTo(cost, 5));
        }

        private static int RoomCost() => RoundTo((15f + Prosperity / 250f) * Markup(BandHere), 5);

        private int RumorCost(Topic topic)
        {
            if (!KeeperReady("rumor_" + topic)) return 0;   // already paid for today
            float basePrice = topic switch { Topic.Armies => 50f, Topic.Enemies => 60f, Topic.Notables => 30f, _ => 25f };
            return RoundTo(basePrice * (1f + Prosperity / 10000f) * Markup(BandHere), 5);
        }

        private static TextObject PriceTag(int price) => price <= 0
            ? TextObject.GetEmpty()
            : new TextObject("{=lmmi_keeper_price_tag} [{AMOUNT}{GOLD_ICON}]").SetTextVariable("AMOUNT", price);

        private static bool CanPay(int price, out TextObject why)
        {
            why = TextObject.GetEmpty();
            if (Hero.MainHero.Gold >= price) return true;
            why = new TextObject("{=lmmi_cant_afford_alms}You don't have that much on you.");
            return false;
        }

        private static void Pay(int price)
        {
            if (price > 0) Hero.MainHero.ChangeHeroGold(-Math.Min(price, Hero.MainHero.Gold));
        }

        // ---- Dialogs ----

        private void AddKeeperDialogs(CampaignGameStarter starter)
        {
            // Despised: out. (Unless you pay over the odds.)
            starter.AddDialogLine("lmmi_keeper_greet_despised", "start", "lmmi_keeper_out",
                "{=!}{LMMI_KEEPER_OUT}",
                () =>
                {
                    if (!TalkingToKeeper() || BandHere != StandingBand.Despised || _servedDespisedIn == Mission.Current) return false;
                    MBTextManager.SetTextVariable("LMMI_KEEPER_FEE", ServeDespisedFee);
                    LmmiLog.Info($"Tavern: the keeper of {Here?.Name} won't serve you (despised).");
                    return Flavor.Say("LMMI_KEEPER_OUT",
                        "{=lmmi_keeper_greet_despised}You. ...No. Not in my house. Take your trouble somewhere else, before somebody breaks a jug over your head.[if:convo_angry][ib:closed]",
                        "{=lmmi_keeper_greet_despised_2}Out. I'm not serving you, and nobody here wants you drinking next to them.[if:convo_angry][ib:closed]",
                        "{=lmmi_keeper_greet_despised_3}You've a nerve walking in here. Turn around and walk out again.[if:convo_angry][ib:closed]");
                }, null, 111);
            starter.AddPlayerLine("lmmi_keeper_out_go", "lmmi_keeper_out", "close_window",
                "{=lmmi_keeper_out_go}Fine. I'm going.", null, null);
            starter.AddPlayerLine("lmmi_keeper_out_pay", "lmmi_keeper_out", "lmmi_keeper_out_paid",
                "{=lmmi_keeper_out_pay}My coin's as good as anyone's. [{LMMI_KEEPER_FEE}{GOLD_ICON}]", null,
                () =>
                {
                    Pay(ServeDespisedFee);
                    _servedDespisedIn = Mission.Current;
                    LmmiLog.Info($"Tavern: paid {ServeDespisedFee} to be served while despised.");
                }, 100, (out TextObject why) => CanPay(ServeDespisedFee, out why));
            starter.AddDialogLine("lmmi_keeper_out_paid", "lmmi_keeper_out_paid", "tavernkeeper_talk",
                "{=!}{LMMI_KEEPER_OUT_PAID}",
                    () => Flavor.Say("LMMI_KEEPER_OUT_PAID",
                        "{=lmmi_keeper_out_paid}...Hmph. Drink it quick, keep your head down, and go when it's gone.[if:convo_annoyed][ib:closed2]",
                        "{=lmmi_keeper_out_paid_2}...Coin's coin. One drink. Then out.[if:convo_annoyed][ib:closed2]",
                        "{=lmmi_keeper_out_paid_3}Fine. Sit in the corner and keep your mouth shut.[if:convo_annoyed][ib:closed2]"), null);

            // Everyone else he knows of: his own greeting (strangers get vanilla's).
            starter.AddDialogLine("lmmi_keeper_greet", "start", "tavernkeeper_talk", "{=lmmi_keeper_greet}{LMMI_KEEPER_GREET}",
                () =>
                {
                    if (!TalkingToKeeper()) return false;
                    var band = BandHere;
                    TextObject? line = band switch
                    {
                        StandingBand.Despised => Flavor.Pick("{=lmmi_keeper_greet_served}Still here? Drink up and go.[if:convo_annoyed][ib:closed]",
                            "{=lmmi_keeper_greet_served_2}You paid, so you drink. Then you go.[if:convo_annoyed][ib:closed]",
                            "{=lmmi_keeper_greet_served_3}Your cup. Don't make me regret taking your coin.[if:convo_annoyed][ib:closed2]"),
                        StandingBand.Disliked => Flavor.Pick("{=lmmi_keeper_greet_disliked}What'll it be? Coin first — and don't complain about the wine.[if:convo_annoyed][ib:closed]",
                            "{=lmmi_keeper_greet_disliked_2}Coin on the counter first. Then we'll see what's left in the barrel.[if:convo_annoyed][ib:closed]",
                            "{=lmmi_keeper_greet_disliked_3}Oh. You. What'll it be — and keep your voice down.[if:convo_bored][ib:closed]"),
                        StandingBand.Known when KeeperReady("cup") => Flavor.Pick("{=lmmi_keeper_greet_known}{PLAYER.NAME}, isn't it? Heard you've done right by {SETTLEMENT}. Sit — the first cup's on the house.[if:convo_calm_friendly][ib:normal]",
                            "{=lmmi_keeper_greet_known_2}{PLAYER.NAME}! The one who did right by {SETTLEMENT}. First cup's free — don't tell the others.[if:convo_calm_friendly][ib:normal]",
                            "{=lmmi_keeper_greet_known_3}Ah, I know that face. Sit down — this one's on the house.[if:convo_calm_friendly][ib:normal]"),
                        StandingBand.Respected when KeeperReady("cup") => Flavor.Pick("{=lmmi_keeper_greet_respected}{PLAYER.NAME}! Good to see you again. First cup's on me — and from the good barrel, not what I pour the soldiers.[if:convo_relaxed_happy][ib:hip]",
                            "{=lmmi_keeper_greet_respected_2}{PLAYER.NAME}! Come in, come in. Your usual seat's free, and the first cup's on me.[if:convo_relaxed_happy][ib:hip]",
                            "{=lmmi_keeper_greet_respected_3}There's a friend of {SETTLEMENT}! Sit — I've kept a jug of the good stuff back for you.[if:convo_relaxed_happy][ib:hip]"),
                        StandingBand.Honored when KeeperReady("cup") => Flavor.Pick("{=lmmi_keeper_greet_honored}Make room there! {PLAYER.NAME}, welcome back — {SETTLEMENT} owes you, and so do I. Your cup's on the house tonight.[if:convo_delighted][ib:hip]",
                            "{=lmmi_keeper_greet_honored_2}Everyone, look who's here! {PLAYER.NAME}! Drink, eat, ask for anything — tonight it's on me.[if:convo_delighted][ib:hip]",
                            "{=lmmi_keeper_greet_honored_3}{PLAYER.NAME}! {SETTLEMENT} hasn't had a better friend in years. Sit, sit — your cup's on the house.[if:convo_delighted][ib:hip]"),
                        _ => null,
                    };
                    if (line == null) return false;
                    line.SetTextVariable("SETTLEMENT", Here?.Name ?? TextObject.GetEmpty());
                    MBTextManager.SetTextVariable("LMMI_KEEPER_GREET", line);
                    return true;
                },
                () =>
                {
                    if (BandHere < StandingBand.Known) return;
                    KeeperHold("cup", 1f);
                    InformationManager.DisplayMessage(new InformationMessage(Flavor.Pick("{=lmmi_keeper_free_cup}The keeper pours you a cup on the house.",
                        "{=lmmi_keeper_free_cup_2}The keeper slides a cup across to you. 'On the house.'",
                        "{=lmmi_keeper_free_cup_3}A full cup appears in front of you. The keeper waves away your coin.").ToString()));
                }, 110);

            // A round for the house.
            starter.AddPlayerLine("lmmi_keeper_round", "tavernkeeper_talk", "lmmi_keeper_round_resp",
                "{=lmmi_keeper_round}A round for the house — on me. [{LMMI_KEEPER_ROUND}{GOLD_ICON}]",
                () =>
                {
                    if (!TalkingToKeeper()) return false;
                    MBTextManager.SetTextVariable("LMMI_KEEPER_ROUND", RoundCost());
                    return true;
                },
                () =>
                {
                    int cost = RoundCost();
                    Pay(cost);
                    KeeperHold("round", RoundCooldownDays);
                    bool grudging = BandHere <= StandingBand.Disliked;
                    TownStandingBehavior.Adjust(Here, grudging ? 0.5f : 1f, "stood the tavern a round");
                    LmmiLog.Info($"Tavern: you stood the house a round in {Here?.Name} ({cost}).");
                }, 101,
                (out TextObject why) =>
                {
                    if (!KeeperReady("round"))
                    {
                        why = new TextObject("{=lmmi_keeper_round_done}You've stood the house a round this week already.");
                        return false;
                    }
                    return CanPay(RoundCost(), out why);
                });
            starter.AddDialogLine("lmmi_keeper_round_resp", "lmmi_keeper_round_resp", "tavernkeeper_pretalk",
                "{=lmmi_keeper_round_resp}{LMMI_KEEPER_ROUND_LINE}",
                () =>
                {
                    var line = (BandHere <= StandingBand.Disliked
                        ? Flavor.Pick("{=lmmi_keeper_round_grudging}...Drinks on {PLAYER.NAME}, then. The good barrel's empty, mind — they'll get the watered stuff, and they'll know whose it was.[if:convo_bored][ib:closed]",
                            "{=lmmi_keeper_round_grudging_2}...A round from {PLAYER.NAME}. They'll drink it. Don't expect them to thank you.[if:convo_bored][ib:closed]",
                            "{=lmmi_keeper_round_grudging_3}Your coin, their throats. Don't expect a song about it.[if:convo_bored][ib:closed]")
                        : Flavor.Pick("{=lmmi_keeper_round_cheer}Drinks for the house — on {PLAYER.NAME}! ...Hear that? They'll be singing your name by midnight, and not the rude verses.[if:convo_happy][ib:confident]",
                            "{=lmmi_keeper_round_cheer_2}A round on {PLAYER.NAME}! Cups up, everybody![if:convo_happy][ib:confident]",
                            "{=lmmi_keeper_round_cheer_3}You hear that? {PLAYER.NAME}'s buying! Somebody start a song![if:convo_happy][ib:confident]"));
                    MBTextManager.SetTextVariable("LMMI_KEEPER_ROUND_LINE", line);
                    return true;
                }, null);

            // News, for a price.
            starter.AddPlayerLine("lmmi_keeper_rumors", "tavernkeeper_talk", "lmmi_keeper_rumors_resp",
                "{=lmmi_keeper_rumors}Heard anything worth knowing?", TalkingToKeeper, null, 101);
            starter.AddDialogLine("lmmi_keeper_rumors_resp", "lmmi_keeper_rumors_resp", "lmmi_keeper_topics",
                "{=lmmi_keeper_rumors_resp}{LMMI_KEEPER_RUMORS}",
                () =>
                {
                    MBTextManager.SetTextVariable("LMMI_KEEPER_RUMORS", (BandHere switch
                    {
                        StandingBand.Honored => Flavor.Pick("{=lmmi_keeper_rumors_honored}For you? Ask away — no charge.[if:convo_calm_friendly][ib:hip]",
                            "{=lmmi_keeper_rumors_honored_2}Anything you want to know, it's yours. Free.[if:convo_calm_friendly][ib:hip]",
                            "{=lmmi_keeper_rumors_honored_3}For you? Not a copper. Ask.[if:convo_calm_friendly][ib:hip]"),
                        StandingBand.Respected => Flavor.Pick("{=lmmi_keeper_rumors_respected}A thing or two. For a friend, cheap.[if:convo_calm_friendly][ib:normal]",
                            "{=lmmi_keeper_rumors_respected_2}I hear things. For you, they come cheap.[if:convo_calm_friendly][ib:normal]",
                            "{=lmmi_keeper_rumors_respected_3}Ask away. Friend's prices.[if:convo_calm_friendly][ib:normal]"),
                        StandingBand.Known => Flavor.Pick("{=lmmi_keeper_rumors_known}Depends what you're after. Ears cost money, but I'll not fleece you.[if:convo_nonchalant][ib:normal]",
                            "{=lmmi_keeper_rumors_known_2}I might. Information costs, but I'll be fair.[if:convo_nonchalant][ib:normal]",
                            "{=lmmi_keeper_rumors_known_3}A few things. They'll cost you a little.[if:convo_nonchalant][ib:normal]"),
                        StandingBand.Disliked => Flavor.Pick("{=lmmi_keeper_rumors_disliked}News costs. For you, it costs double.[if:convo_annoyed][ib:closed]",
                            "{=lmmi_keeper_rumors_disliked_2}I know things. You'll pay through the nose for them.[if:convo_annoyed][ib:closed]",
                            "{=lmmi_keeper_rumors_disliked_3}Maybe. For you, the price is doubled. Take it or leave it.[if:convo_annoyed][ib:closed]"),
                        _ => Flavor.Pick("{=lmmi_keeper_rumors_stranger}Depends what you're after — and what it's worth to you.[if:convo_nonchalant][ib:closed]",
                            "{=lmmi_keeper_rumors_stranger_2}Maybe. What's it worth to you?[if:convo_nonchalant][ib:closed]",
                            "{=lmmi_keeper_rumors_stranger_3}I hear plenty. Nothing's free, though.[if:convo_nonchalant][ib:closed]"),
                    }));
                    return true;
                }, null);

            void Ask(string id, string text, Topic topic, Func<bool>? extra = null, bool regularsOnly = false)
            {
                starter.AddPlayerLine(id, "lmmi_keeper_topics", "lmmi_keeper_answer", text,
                    () =>
                    {
                        if (extra != null && !extra()) return false;
                        MBTextManager.SetTextVariable("LMMI_KEEPER_PRICE", PriceTag(RumorCost(topic)));
                        return true;
                    },
                    () =>
                    {
                        int price = RumorCost(topic);
                        Pay(price);
                        KeeperHold("rumor_" + topic, 1f);
                        _rumor = Rumor(topic);
                        LmmiLog.Info($"Tavern: bought news ({topic}) for {price}.");
                    }, 100,
                    (out TextObject why) =>
                    {
                        if (regularsOnly && BandHere <= StandingBand.Disliked)
                        {
                            why = new TextObject("{=lmmi_keeper_wont_say}He won't talk about his regulars to the likes of you.");
                            return false;
                        }
                        return CanPay(RumorCost(topic), out why);
                    });
            }
            Ask("lmmi_keeper_ask_armies", "{=lmmi_keeper_ask_armies}Any armies on the move hereabouts?{LMMI_KEEPER_PRICE}", Topic.Armies);
            Ask("lmmi_keeper_ask_enemies", "{=lmmi_keeper_ask_enemies}Anyone around here who'd like to see me hurt?{LMMI_KEEPER_PRICE}", Topic.Enemies, regularsOnly: true);
            Ask("lmmi_keeper_ask_notables", "{=lmmi_keeper_ask_notables}What do the town's notables make of me?{LMMI_KEEPER_PRICE}", Topic.Notables, regularsOnly: true);
            Ask("lmmi_keeper_ask_lord", "{=lmmi_keeper_ask_lord}Where's {LMMI_KEEPER_OWNER} these days?{LMMI_KEEPER_PRICE}", Topic.Lord,
                () =>
                {
                    var owner = Here?.OwnerClan?.Leader;
                    if (owner == null || owner == Hero.MainHero || !owner.IsAlive) return false;
                    MBTextManager.SetTextVariable("LMMI_KEEPER_OWNER", owner.Name);
                    return true;
                });
            starter.AddPlayerLine("lmmi_keeper_ask_done", "lmmi_keeper_topics", "tavernkeeper_pretalk",
                "{=lmmi_keeper_ask_done}That's all.", null, null);
            starter.AddDialogLine("lmmi_keeper_answer", "lmmi_keeper_answer", "lmmi_keeper_topics", "{=lmmi_keeper_answer}{LMMI_KEEPER_RUMOR}",
                () => { MBTextManager.SetTextVariable("LMMI_KEEPER_RUMOR", _rumor); return true; }, null);

            // A bed for the night.
            starter.AddPlayerLine("lmmi_keeper_room", "tavernkeeper_talk", "lmmi_keeper_room_resp",
                "{=lmmi_keeper_room}I'll take a room for the night.{LMMI_KEEPER_ROOM_PRICE}",
                () =>
                {
                    if (!TalkingToKeeper()) return false;
                    MBTextManager.SetTextVariable("LMMI_KEEPER_ROOM_PRICE", PriceTag(RoomCost()));
                    return true;
                },
                () =>
                {
                    int cost = RoomCost();
                    Pay(cost);
                    KeeperHold("room", 1f);
                    var band = BandHere;
                    _restHeal = band <= StandingBand.Disliked ? 0.2f : band >= StandingBand.Respected ? 0.5f : 0.35f;
                    _restPhase = 1;
                    LmmiLog.Info($"Tavern: a room for the night in {Here?.Name} ({cost}).");
                }, 101,
                (out TextObject why) =>
                {
                    if (!KeeperReady("room"))
                    {
                        why = new TextObject("{=lmmi_keeper_room_done}You've already slept here today.");
                        return false;
                    }
                    return CanPay(RoomCost(), out why);
                });
            starter.AddDialogLine("lmmi_keeper_room_resp", "lmmi_keeper_room_resp", "close_window", "{=lmmi_keeper_room_resp}{LMMI_KEEPER_ROOM_LINE}",
                () =>
                {
                    MBTextManager.SetTextVariable("LMMI_KEEPER_ROOM_LINE", (BandHere switch
                    {
                        StandingBand.Honored => Flavor.Pick("{=lmmi_keeper_room_honored}The best room in the house, and not a copper for it. I'll have them bring up hot water.[if:convo_happy][ib:hip]",
                            "{=lmmi_keeper_room_honored_2}My own room, if you want it — I'll sleep in the cellar. No charge.[if:convo_happy][ib:hip]",
                            "{=lmmi_keeper_room_honored_3}Best bed in town, and it's yours. Breakfast too.[if:convo_happy][ib:hip]"),
                        StandingBand.Respected => Flavor.Pick("{=lmmi_keeper_room_respected}The room at the top of the stairs — the quiet one. Sleep well.[if:convo_calm_friendly][ib:normal]",
                            "{=lmmi_keeper_room_respected_2}The corner room — it's warm, and nobody snores next door. Rest well.[if:convo_calm_friendly][ib:normal]",
                            "{=lmmi_keeper_room_respected_3}A good room for a good guest. Top of the stairs.[if:convo_calm_friendly][ib:normal]"),
                        StandingBand.Disliked => Flavor.Pick("{=lmmi_keeper_room_disliked}Room at the back, over the stable. Mind the fleas.[if:convo_bored][ib:closed]",
                            "{=lmmi_keeper_room_disliked_2}There's a cot by the kitchen. Take it or leave it.[if:convo_bored][ib:closed]",
                            "{=lmmi_keeper_room_disliked_3}Back room. The roof leaks. That's what's left.[if:convo_bored][ib:closed]"),
                        StandingBand.Despised => Flavor.Pick("{=lmmi_keeper_room_despised}Room at the back, over the stable. Gone by sunrise.[if:convo_annoyed][ib:closed]",
                            "{=lmmi_keeper_room_despised_2}Stable loft. Out before anyone sees you.[if:convo_annoyed][ib:closed]",
                            "{=lmmi_keeper_room_despised_3}You can sleep in the woodshed. Be gone before the cock crows.[if:convo_annoyed][ib:closed]"),
                        _ => Flavor.Pick("{=lmmi_keeper_room_stranger}Up the stairs, second door. Sheets are clean — mostly.[if:convo_nonchalant][ib:normal]",
                            "{=lmmi_keeper_room_stranger_2}End of the hall. Lock the door if you've anything worth stealing.[if:convo_nonchalant][ib:normal]",
                            "{=lmmi_keeper_room_stranger_3}Room's upstairs. Bed's hard, blankets are thin, price is fair.[if:convo_nonchalant][ib:normal]"),
                    }));
                    return true;
                }, null);
        }

        // ---- What he knows ----

        private TextObject Rumor(Topic topic)
        {
            try
            {
                return topic switch
                {
                    Topic.Armies => ArmiesRumor(),
                    Topic.Enemies => EnemiesRumor(),
                    Topic.Notables => NotablesRumor(),
                    _ => LordRumor(),
                };
            }
            catch (Exception ex)
            {
                LmmiLog.Error($"Tavern: the keeper's news ({topic}) threw", ex);
                return Flavor.Pick("{=lmmi_keeper_rumor_nothing}Nothing worth the coin, truth be told.[if:convo_undecided_closed][ib:closed]",
                            "{=lmmi_keeper_rumor_nothing_2}Quiet lately. Nothing worth selling you.[if:convo_undecided_closed][ib:closed]",
                            "{=lmmi_keeper_rumor_nothing_3}Haven't heard a thing. Come back in a few days.[if:convo_undecided_closed][ib:closed]");
            }
        }

        private static TextObject Near(CampaignVec2 at)
        {
            var near = SettlementHelper.FindNearestSettlementToPoint(at, s => s.IsTown || s.IsCastle || s.IsVillage);
            return near?.Name ?? new TextObject("{=lmmi_keeper_somewhere}somewhere out there");
        }

        /// <summary>The nearest host of a realm at war with you. Known or better: who leads it, how many, and where it's bound.</summary>
        private static TextObject ArmiesRumor()
        {
            var here = Here!;
            var at = here.GatePosition;
            var mine = Hero.MainHero.MapFaction;
            var army = mine == null ? null : Kingdom.All
                .Where(k => k != mine && FactionManager.IsAtWarAgainstFaction(k, mine))
                .SelectMany(k => k.Armies)
                .Where(a => a.LeaderParty != null && a.LeaderParty.IsActive)
                .OrderBy(a => a.LeaderParty.Position.Distance(at))
                .FirstOrDefault();
            if (army == null || army.LeaderParty.Position.Distance(at) > 250f)
                return Flavor.Pick("{=lmmi_keeper_armies_none}No host I've heard of — not this side of the hills, anyway. Quiet times. Long may they last.[if:convo_relaxed_happy][ib:normal]",
                            "{=lmmi_keeper_armies_none_2}No armies about. The roads are as quiet as I've seen them.[if:convo_relaxed_happy][ib:normal]",
                            "{=lmmi_keeper_armies_none_3}Not a banner for days in any direction. Enjoy it.[if:convo_relaxed_happy][ib:normal]");

            var near = Near(army.LeaderParty.Position);
            if (BandHere < StandingBand.Known)
                return new TextObject("{=lmmi_keeper_armies_vague}They say there's a {FACTION} host about, somewhere near {NEAR}. How many? Depends who's drinking.[if:convo_undecided_open][ib:closed]")
                    .SetTextVariable("FACTION", army.Kingdom?.Name ?? TextObject.GetEmpty())
                    .SetTextVariable("NEAR", near);

            var target = army.AiBehaviorObject is Settlement bound && bound != here
                ? new TextObject("{=lmmi_keeper_armies_bound}, and the talk is they're making for {TARGET}").SetTextVariable("TARGET", bound.Name)
                : army.AiBehaviorObject is Settlement same && same == here
                    ? new TextObject("{=lmmi_keeper_armies_here}, and the talk is they're coming here")
                    : TextObject.GetEmpty();
            return new TextObject("{=lmmi_keeper_armies_good}{LEADER} has a {FACTION} host in the field — {COUNT} men or near enough — last seen near {NEAR}{TARGET}. The carters coming in from that way are scared.[if:convo_grave][ib:closed]")
                .SetTextVariable("LEADER", army.LeaderParty.LeaderHero?.Name ?? army.Name ?? TextObject.GetEmpty())
                .SetTextVariable("FACTION", army.Kingdom?.Name ?? TextObject.GetEmpty())
                .SetTextVariable("COUNT", Math.Max(10, RoundTo(army.TotalManCount, 10)))
                .SetTextVariable("NEAR", near)
                .SetTextVariable("TARGET", target);
        }

        /// <summary>
        /// Notables here who hate you (and whether they're the kind to pay to have it done). Known or better: whether
        /// sellswords have been asking after you, and whose coin. Respected or better: lords nearby who'd drink to your end.
        /// </summary>
        private static TextObject EnemiesRumor()
        {
            var here = Here!;
            var band = BandHere;
            var parts = new List<string>();
            foreach (var n in here.Notables.Where(n => n.IsAlive && n.GetRelationWithPlayer() <= -25f).OrderBy(n => n.GetRelationWithPlayer()))
            {
                bool wouldPay = n.GetTraitLevel(DefaultTraits.Honor) < 1 && n.GetTraitLevel(DefaultTraits.Mercy) < 1;
                parts.Add(new TextObject(wouldPay
                        ? "{=lmmi_keeper_enemy_payer}{NAME} hates you, and has the coin and the stomach to pay someone to say so with a club."
                        : "{=lmmi_keeper_enemy}{NAME} has no love for you — though I'd not expect a knife from that one.")
                    .SetTextVariable("NAME", n.Name).ToString());
            }
            if (band >= StandingBand.Known)
            {
                var sender = HiredSwordsBehavior.PriceOnYourHead;
                if (sender != null)
                    parts.Add(new TextObject("{=lmmi_keeper_enemy_contract}And a word to the wise: hard men in no one's colors were in here asking after you. {SENDER}'s silver, I'd wager.")
                        .SetTextVariable("SENDER", sender.Name).ToString());
            }
            if (band >= StandingBand.Respected)
            {
                var at = here.GatePosition;
                var lords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h != Hero.MainHero && h.Clan != Clan.PlayerClan && !h.IsPrisoner && !h.IsChild
                                && h.GetRelationWithPlayer() <= -25f && h.GetTraitLevel(DefaultTraits.Honor) < 1 && h.GetTraitLevel(DefaultTraits.Mercy) < 1)
                    .Where(h => { var p = h.GetCampaignPosition(); return p.IsValid() && p.Distance(at) < 100f; })
                    .OrderBy(h => h.GetRelationWithPlayer()).Take(2).ToList();
                if (lords.Count > 0)
                    parts.Add(new TextObject("{=lmmi_keeper_enemy_lords}And {LORDS} — close by, these days — would drink to your funeral.")
                        .SetTextVariable("LORDS", string.Join(" & ", lords.Select(l => l.Name.ToString()))).ToString());
            }
            if (parts.Count == 0)
                return Flavor.Pick("{=lmmi_keeper_enemies_none}Nobody I'd lose sleep over. You've a quiet name in {SETTLEMENT}.[if:convo_calm_friendly][ib:normal]",
                            "{=lmmi_keeper_enemies_none_2}Nobody's cursing your name in here, not that I've heard.[if:convo_calm_friendly][ib:normal]",
                            "{=lmmi_keeper_enemies_none_3}No grudges against you in {SETTLEMENT}. Rare, that.[if:convo_calm_friendly][ib:normal]")
                    .SetTextVariable("SETTLEMENT", here.Name);
            return new TextObject("{=lmmi_keeper_enemies}{LIST}[if:convo_grave][ib:closed]").SetTextVariable("LIST", string.Join(" ", parts));
        }

        /// <summary>How each notable here regards you, as the tavern hears it.</summary>
        private static TextObject NotablesRumor()
        {
            var here = Here!;
            var parts = new List<string>();
            foreach (var n in here.Notables.Where(n => n.IsAlive))
            {
                var d = NotableDisposition.Get(n);
                parts.Add(new TextObject(d switch
                    {
                        Disposition.Contempt => "{=lmmi_keeper_notable_contempt}{NAME} won't hear your name without spitting",
                        Disposition.Wary => "{=lmmi_keeper_notable_wary}{NAME} would deal with you — for a price",
                        Disposition.Friendly => "{=lmmi_keeper_notable_friendly}{NAME} speaks well of you",
                        _ => "{=lmmi_keeper_notable_trusted}{NAME} would vouch for you, and has",
                    })
                    .SetTextVariable("NAME", n.Name).ToString());
            }
            if (parts.Count == 0)
                return Flavor.Pick("{=lmmi_keeper_notables_none}Nobody of note in town just now.[if:convo_nonchalant][ib:normal]",
                            "{=lmmi_keeper_notables_none_2}The town's big folk are all away. Nobody to ask.[if:convo_nonchalant][ib:normal]",
                            "{=lmmi_keeper_notables_none_3}None of the notables are about. Try another day.[if:convo_nonchalant][ib:normal]");
            return new TextObject("{=lmmi_keeper_notables}Let me think. {LIST}.[if:convo_thinking][ib:normal]").SetTextVariable("LIST", string.Join("; ", parts));
        }

        /// <summary>Where the lord of the town is: in a settlement, on the road (or with an army), or a prisoner.</summary>
        private static TextObject LordRumor()
        {
            var here = Here!;
            var owner = here.OwnerClan?.Leader;
            if (owner == null) return Flavor.Pick("{=lmmi_keeper_rumor_nothing}Nothing worth the coin, truth be told.[if:convo_undecided_closed][ib:closed]",
                            "{=lmmi_keeper_rumor_nothing_2}Quiet lately. Nothing worth selling you.[if:convo_undecided_closed][ib:closed]",
                            "{=lmmi_keeper_rumor_nothing_3}Haven't heard a thing. Come back in a few days.[if:convo_undecided_closed][ib:closed]");
            TextObject line;
            if (owner.IsPrisoner)
                line = new TextObject("{=lmmi_keeper_lord_prisoner}{OWNER}? A prisoner, last I heard — {CAPTOR} has the keeping of that one.[if:convo_grave][ib:closed]")
                    .SetTextVariable("CAPTOR", owner.PartyBelongedToAsPrisoner?.Name ?? new TextObject("{=lmmi_keeper_someone}someone"));
            else if (owner.CurrentSettlement == here)
                line = new TextObject("{=lmmi_keeper_lord_here}{OWNER}? Here in town — up at the keep, I'd think.[if:convo_nonchalant][ib:normal]");
            else if (owner.CurrentSettlement != null)
                line = new TextObject("{=lmmi_keeper_lord_at}{OWNER}? At {WHERE}, last I heard.[if:convo_nonchalant][ib:normal]")
                    .SetTextVariable("WHERE", owner.CurrentSettlement.Name);
            else if (owner.PartyBelongedTo != null && owner.PartyBelongedTo.IsActive)
            {
                var party = owner.PartyBelongedTo;
                line = new TextObject(party.Army != null
                        ? "{=lmmi_keeper_lord_army}{OWNER}? Marching with the army, near {NEAR} last I heard.[if:convo_nonchalant][ib:normal]"
                        : "{=lmmi_keeper_lord_road}{OWNER}? Out on the road with a warband — near {NEAR}, last I heard.[if:convo_nonchalant][ib:normal]")
                    .SetTextVariable("NEAR", Near(party.Position));
            }
            else line = Flavor.Pick("{=lmmi_keeper_lord_unknown}{OWNER}? Couldn't tell you. Nobody's seen hide nor hair.[if:convo_undecided_closed][ib:closed]",
                            "{=lmmi_keeper_lord_unknown_2}{OWNER}? No idea. Nobody's said a word in weeks.[if:convo_undecided_closed][ib:closed]",
                            "{=lmmi_keeper_lord_unknown_3}{OWNER}? Gone off somewhere. Ask a soldier.[if:convo_undecided_closed][ib:closed]");
            return line.SetTextVariable("OWNER", owner.Name);
        }

        // ---- A night's rest ----

        /// <summary>After the talk: the screen goes dark, you sleep, you wake rested (the scene doesn't move on).</summary>
        private void OnMissionTick(float dt)
        {
            try
            {
                _keeperTime += dt;
                if (_restNeedsFadeIn)
                {
                    // Left the tavern while the screen was dark, straight into another scene.
                    _restNeedsFadeIn = false;
                    ScreenFadeController.BeginFadeIn(0.6f);
                }
                if (_restPhase == 0) return;
                var mission = Mission.Current;
                if (mission == null) { _restPhase = 0; return; }
                if (_restPhase == 1)
                {
                    if (mission.Mode == MissionMode.Conversation || Campaign.Current?.ConversationManager?.IsConversationInProgress == true) return;
                    ScreenFadeController.BeginFadeOut(0.8f);
                    _restPhase = 2;
                    _restAt = _keeperTime + 1.8f;
                    return;
                }
                if (_restPhase == 2 && _keeperTime >= _restAt)
                {
                    _restPhase = 0;
                    Rest();
                    ScreenFadeController.BeginFadeIn(1.0f);
                }
            }
            catch (Exception ex)
            {
                LmmiLog.Error("TavernLifeBehavior.OnMissionTick threw", ex);
                if (_restPhase == 2) { try { ScreenFadeController.BeginFadeIn(0.5f); } catch { } }
                _restPhase = 0;
            }
        }

        private void Rest()
        {
            var hero = Hero.MainHero;
            int before = hero.HitPoints;
            hero.HitPoints = Math.Min(hero.MaxHitPoints, hero.HitPoints + (int)(hero.MaxHitPoints * _restHeal));
            var main = Agent.Main;
            if (main != null && main.IsActive())
                main.Health = Math.Min(main.HealthLimit, main.Health + main.HealthLimit * _restHeal);
            InformationManager.DisplayMessage(new InformationMessage((_restHeal < 0.3f
                ? Flavor.Pick("{=lmmi_keeper_rested_fleas}You sleep badly, and scratch in your sleep. Still — you're stiff, but rested.",
                            "{=lmmi_keeper_rested_fleas_2}The bed's alive with fleas, and the man next door snores. You're rested, more or less.",
                            "{=lmmi_keeper_rested_fleas_3}Cold draughts and a lumpy straw mattress. You wake stiff — but rested.")
                : Flavor.Pick("{=lmmi_keeper_rested}You sleep like the dead. You wake stiff, but rested.",
                            "{=lmmi_keeper_rested_2}You sleep through the night without stirring. You wake rested.",
                            "{=lmmi_keeper_rested_3}A real bed, a quiet room. You wake feeling more yourself.")).ToString()));
            LmmiLog.Info($"Tavern: a night's rest ({before} → {hero.HitPoints} hp).");
        }

        private void OnKeeperMissionEnded(IMission mission)
        {
            if (_restPhase == 2) { Rest(); _restNeedsFadeIn = true; }
            _restPhase = 0;
        }

        private void OnKeeperMenuOpened(MenuCallbackArgs args)
        {
            if (!_restNeedsFadeIn || Mission.Current != null) return;
            _restNeedsFadeIn = false;
            ScreenFadeController.BeginFadeIn(0.6f);
        }

        // ---- Save / load: "what:settlementId;hours" separated by '|' ----

        private void SyncKeeper(IDataStore dataStore)
        {
            string data = string.Empty;
            if (!dataStore.IsLoading)
            {
                double now = CampaignTime.Now.ToHours;
                data = string.Join("|", _keeperUntil.Where(kv => kv.Value > now)
                    .Select(kv => kv.Key + ";" + kv.Value.ToString("R", CultureInfo.InvariantCulture)));
            }
            dataStore.SyncData("lmmi_tavern_keeper_v1", ref data);
            if (!dataStore.IsLoading) return;
            _keeperUntil = new Dictionary<string, double>();
            foreach (var entry in (data ?? string.Empty).Split('|'))
            {
                var p = entry.Split(';');
                if (p.Length == 2 && p[0].Length > 0 && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var at))
                    _keeperUntil[p[0]] = at;
            }
        }
    }
}
