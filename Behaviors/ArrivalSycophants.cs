using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Helpers;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Sycophants: power buys service, never loyalty — and a flatterer's gift is the opening move of a trade.
    /// Rarely (once a year per notable, one a season anywhere, and only on a lucky roll), a sycophant brings a
    /// small gift in kind. Take it and you owe them: some days later they come to collect — a word against a
    /// rival, a word with the town's lord, a place in your company for their son, money for a venture, or (in a
    /// town you own) a trade charter. Grant it and they gain; refuse it and they remember. Ignore the debt and
    /// they remember that too. Refusing the gift itself insults the proud; a charming refusal costs nothing.
    /// </summary>
    public partial class ArrivalScenesBehavior
    {
        private enum PetitionAsk { Rival, GoodWord, Son, Invest, Concession }

        /// <summary>A gift taken: what the giver will ask for, and when.</summary>
        private sealed class Obligation
        {
            public string HeroId = "";
            public double DueHours;
            public PetitionAsk Ask;
            public string TargetId = "";
            public bool Announced;
        }

        /// <summary>Money put into a notable's venture; comes back (or not) on the due day.</summary>
        private sealed class Investment
        {
            public string HeroId = "";
            public string SettlementId = "";
            public double DueHours;
            public int Amount;
            public int Payout;   // 0: it was a swindle
        }

        private sealed class TributeGift
        {
            public ItemObject? Item;
            public int Count;
            public int Gold;
            public TextObject Description = TextObject.GetEmpty();
        }

        private const int TributeMinTier = 4;
        private const float TributeCooldownDays = 84f;          // a year, per notable
        private const float TributeAnywhereCooldownDays = 21f;  // a season, across every town
        private const float TributeRerollDays = 7f;             // a failed roll: they think about it again next week
        private const float TributeChance = 0.35f;
        private const float ObligationMinDays = 4f;
        private const float ObligationMaxDays = 8f;
        private const float ObligationGraceDays = 20f;          // after it comes due, before ignoring it counts
        private const int GracefulRefusalCharm = 75;
        private const float InvestMinDays = 20f;
        private const float InvestMaxDays = 35f;
        private const float CharterProsperity = 150f;
        private const float CharterLoyalty = 5f;
        private const string AnyTributeKey = "any|Tribute";

        // notable StringId -> what they'll ask for having given you a gift
        [NonSerialized] private Dictionary<string, Obligation> _obligations = new Dictionary<string, Obligation>();
        [NonSerialized] private List<Investment> _investments = new List<Investment>();

        // ---- Who comes, and what they bring ----

        /// <summary>
        /// Rare by design: the per-notable and realm-wide cooldowns, then a roll. A failed roll puts it off a week,
        /// so walking in and out of town doesn't re-roll it.
        /// </summary>
        private bool TributeReady(Hero notable)
        {
            if (LmmiSettingsProvider.TestMode) return true;
            if (!Ready(notable, ArrivalKind.Tribute)) return false;
            if (_readyAtHours.TryGetValue(AnyTributeKey, out var any) && any > CampaignTime.Now.ToHours) return false;
            if (MBRandom.RandomFloat < TributeChance) return true;
            SetCooldown(notable, ArrivalKind.Tribute, TributeRerollDays);
            LmmiLog.Info($"Arrival: {notable.Name} thought about bringing you a gift, but not this week.");
            return false;
        }

        /// <summary>A token, not a fortune — in kind, the way a merchant, an artisan or a headman would give it.</summary>
        private static TributeGift TributeGiftFor(Hero host, int tier)
        {
            var gift = new TributeGift();
            string? text = null;
            if (host.IsMerchant) { gift.Item = FindItem("wine"); gift.Count = 2; text = "{=lmmi_tribute_gift_wine}two amphorae of our best wine"; }
            else if (host.IsArtisan) { gift.Item = DefaultItems.Tools; gift.Count = 1; text = "{=lmmi_tribute_gift_tools}a load of good tools from our own workshop"; }
            else if (host.IsHeadman || host.IsRuralNotable) { gift.Item = FindItem("sheep"); gift.Count = 2; text = "{=lmmi_tribute_gift_sheep}two fat sheep from our own flock"; }

            if (gift.Item != null && text != null)
            {
                gift.Description = new TextObject(text);
                return gift;
            }

            gift.Item = null;
            gift.Count = 0;
            gift.Gold = Math.Max(25, Math.Min(30 * tier + MBRandom.RandomInt(40), host.Gold / 10));
            gift.Description = new TextObject("{=lmmi_tribute_gift_gold}a purse of {GOLD}{GOLD_ICON}, for the road");
            gift.Description.SetTextVariable("GOLD", gift.Gold);
            return gift;
        }

        private static ItemObject? FindItem(string id)
        {
            try { return MBObjectManager.Instance?.GetObject<ItemObject>(id); }
            catch { return null; }
        }

        /// <summary>The proud take a refused gift as an insult.</summary>
        private static bool IsProud(Hero hero) =>
            hero.GetTraitLevel(DefaultTraits.Valor) + hero.GetTraitLevel(DefaultTraits.Generosity) >= 1;

        // ---- What they want back ----

        private static (PetitionAsk Ask, Hero? Target)? PickAsk(Hero host)
        {
            var options = new List<(PetitionAsk Ask, Hero? Target, float Weight)>();
            if (CanCharter(host)) options.Add((PetitionAsk.Concession, null, 3f));
            if (CanInvest(host) && Hero.MainHero.Gold >= InvestAmount()) options.Add((PetitionAsk.Invest, null, host.IsMerchant ? 2.5f : 1.5f));
            if (SonTroop(host) != null && PartyHasRoom()) options.Add((PetitionAsk.Son, null, host.IsHeadman || host.IsRuralNotable ? 3f : 1.5f));
            var lord = LordToLobby(host);
            if (lord != null) options.Add((PetitionAsk.GoodWord, lord, 1.5f));
            var rival = RivalOf(host);
            if (rival != null) options.Add((PetitionAsk.Rival, rival, 1.5f));
            if (options.Count == 0) return null;

            float roll = MBRandom.RandomFloat * options.Sum(o => o.Weight);
            foreach (var o in options)
            {
                roll -= o.Weight;
                if (roll <= 0f) return (o.Ask, o.Target);
            }
            var last = options[options.Count - 1];
            return (last.Ask, last.Target);
        }

        /// <summary>Still makes sense now that it's due? If not, they want something else; false if nothing fits.</summary>
        private static bool ResolveAsk(Hero host, Obligation o)
        {
            var target = FindHero(o.TargetId);
            bool valid;
            switch (o.Ask)
            {
                case PetitionAsk.Rival:
                    if (target == null || !target.IsAlive || target == host || target.CurrentSettlement != host.CurrentSettlement)
                        target = RivalOf(host);
                    valid = target != null;
                    break;
                case PetitionAsk.GoodWord:
                    target = LordToLobby(host);
                    valid = target != null;
                    break;
                case PetitionAsk.Son:
                    valid = SonTroop(host) != null && PartyHasRoom();
                    break;
                case PetitionAsk.Invest:
                    valid = CanInvest(host);
                    break;
                case PetitionAsk.Concession:
                    valid = CanCharter(host);
                    break;
                default:
                    valid = false;
                    break;
            }

            if (!valid)
            {
                var pick = PickAsk(host);
                if (pick == null) return false;
                o.Ask = pick.Value.Ask;
                target = pick.Value.Target;
            }
            o.TargetId = target?.StringId ?? "";
            return true;
        }

        private static Hero? FindHero(string? id) => string.IsNullOrEmpty(id) ? null : Hero.Find(id);

        private static Hero? RivalOf(Hero host) =>
            host.CurrentSettlement?.Notables
                .Where(r => r != host && r.IsAlive)
                .OrderBy(r => host.GetRelation(r))
                .FirstOrDefault();

        /// <summary>The lord who holds the town (a village: its town) — if it isn't you and you're not at war with them.</summary>
        private static Hero? LordToLobby(Hero host)
        {
            var settlement = host.CurrentSettlement;
            var clan = settlement?.OwnerClan;
            var lord = clan?.Leader;
            if (settlement == null || lord == null || !lord.IsAlive || lord == Hero.MainHero || clan == Clan.PlayerClan) return null;
            var mine = Hero.MainHero?.MapFaction;
            if (mine != null && settlement.MapFaction != null && mine.IsAtWarWith(settlement.MapFaction)) return null;
            return lord;
        }

        private static bool CanInvest(Hero host) => host.IsMerchant || host.IsArtisan || host.IsGangLeader;

        private static bool CanCharter(Hero host)
        {
            var settlement = host.CurrentSettlement;
            return settlement != null && settlement.IsTown && settlement.Town != null && settlement.OwnerClan == Clan.PlayerClan
                   && (host.IsMerchant || host.IsArtisan);
        }

        private static int InvestAmount() => 500 + 250 * Math.Max(0, Clan.PlayerClan?.Tier ?? 0);

        /// <summary>Their son: the best of the notable's volunteers, one step up if he's raw.</summary>
        private static CharacterObject? SonTroop(Hero host)
        {
            var best = host.VolunteerTypes?.Where(t => t != null && !t.IsHero).OrderByDescending(t => t.Tier).FirstOrDefault()
                       ?? host.Culture?.BasicTroop;
            if (best != null && best.Tier <= 1 && best.UpgradeTargets != null && best.UpgradeTargets.Length > 0 && best.UpgradeTargets[0] != null)
                best = best.UpgradeTargets[0];
            return best;
        }

        private static bool PartyHasRoom()
        {
            var party = MobileParty.MainParty;
            return party != null && party.MemberRoster.TotalManCount < party.Party.PartySizeLimit;
        }

        // ---- Dialog text ----

        private enum PetitionPart { Ask, Hint, Yes, No, Thanks, Sulk }

        private static TextObject PetitionText(PetitionAsk ask, PetitionPart part)
        {
            string s = (ask, part) switch
            {
                (PetitionAsk.Rival, PetitionPart.Ask) => "{=lmmi_petition_rival_ask}{?PLAYER.GENDER}My lady{?}My lord{\\?}, the small matter I mentioned. {LMMI_TARGET.NAME} has been... difficult. Squeezing my trade, spreading lies about me. A word from someone of your standing would put {?LMMI_TARGET.GENDER}her{?}him{\\?} in {?LMMI_TARGET.GENDER}her{?}his{\\?} place.",
                (PetitionAsk.Rival, PetitionPart.Hint) => "{=lmmi_petition_rival_hint}Want? Nothing, nothing! Only... {LMMI_TARGET.NAME} has been making my life difficult. One day I may ask you to have a word. But that's for another day.",
                (PetitionAsk.Rival, PetitionPart.Yes) => "{=lmmi_petition_rival_yes}I'll have a word with {LMMI_TARGET.NAME}.",
                (PetitionAsk.Rival, PetitionPart.No) => "{=lmmi_petition_rival_no}Your quarrels are your own.",
                (PetitionAsk.Rival, PetitionPart.Thanks) => "{=lmmi_petition_rival_thanks}You are too kind! I won't forget this. Nobody here will.",
                (PetitionAsk.Rival, _) => "{=lmmi_petition_rival_sulk}I see. I had hoped my gift meant... well. Never mind. Good day.",

                (PetitionAsk.GoodWord, PetitionPart.Ask) => "{=lmmi_petition_word_ask}{?PLAYER.GENDER}My lady{?}My lord{\\?}, the small matter I mentioned. {LMMI_TARGET.NAME} sets the tolls and grants the charters here, and hears a hundred petitions a week. A word from you, and mine might be the one {?LMMI_TARGET.GENDER}she{?}he{\\?} actually reads.",
                (PetitionAsk.GoodWord, PetitionPart.Hint) => "{=lmmi_petition_word_hint}Want? Nothing! Well... {LMMI_TARGET.NAME} listens to people like you. One day I may ask you to put in a word for me. Another day.",
                (PetitionAsk.GoodWord, PetitionPart.Yes) => "{=lmmi_petition_word_yes}I'll speak to {LMMI_TARGET.NAME} for you.",
                (PetitionAsk.GoodWord, PetitionPart.No) => "{=lmmi_petition_word_no}I don't carry other people's petitions.",
                (PetitionAsk.GoodWord, PetitionPart.Thanks) => "{=lmmi_petition_word_thanks}You won't regret it. My house remembers its friends.",
                (PetitionAsk.GoodWord, _) => "{=lmmi_petition_word_sulk}Of course. I'll... find another way. Good day.",

                (PetitionAsk.Son, PetitionPart.Ask) => "{=lmmi_petition_son_ask}{?PLAYER.GENDER}My lady{?}My lord{\\?}, it's about my son. He's of age, he's restless, and he talks of nothing but your company. Take him with you. He'll serve well, and I'd sleep easier knowing whose banner he follows.",
                (PetitionAsk.Son, PetitionPart.Hint) => "{=lmmi_petition_son_hint}Want? Nothing! Though my son is of an age where he dreams of the road, and of banners like yours. Perhaps one day... but no. Another time.",
                (PetitionAsk.Son, PetitionPart.Yes) => "{=lmmi_petition_son_yes}Send him to me. He'll have a place in my company.",
                (PetitionAsk.Son, PetitionPart.No) => "{=lmmi_petition_son_no}I have no place for him.",
                (PetitionAsk.Son, PetitionPart.Thanks) => "{=lmmi_petition_son_thanks}Bless you! He'll be at your camp by nightfall. Bring him home in one piece, {?PLAYER.GENDER}my lady{?}my lord{\\?}.",
                (PetitionAsk.Son, _) => "{=lmmi_petition_son_sulk}No place. For my son. ...I see how it is.",

                (PetitionAsk.Invest, PetitionPart.Ask) => "{=lmmi_petition_invest_ask}{?PLAYER.GENDER}My lady{?}My lord{\\?}, a proposition. A venture (a caravan share, never mind the details) wants a partner of means. {LMMI_AMOUNT}{GOLD_ICON} now, and in a month or so you'll have it back, and a good deal more.",
                (PetitionAsk.Invest, PetitionPart.Hint) => "{=lmmi_petition_invest_hint}Want? Nothing! Though I have a venture in mind that could use a partner of means. We'll speak of it another day.",
                (PetitionAsk.Invest, PetitionPart.Yes) => "{=lmmi_petition_invest_yes}Count me in. [{LMMI_AMOUNT}{GOLD_ICON}]",
                (PetitionAsk.Invest, PetitionPart.No) => "{=lmmi_petition_invest_no}I keep my coin where I can see it.",
                (PetitionAsk.Invest, PetitionPart.Thanks) => "{=lmmi_petition_invest_thanks}You won't regret it. I'll send word, and your share, when the venture comes in.",
                (PetitionAsk.Invest, _) => "{=lmmi_petition_invest_sulk}Your loss, {?PLAYER.GENDER}my lady{?}my lord{\\?}. Your loss.",

                (PetitionAsk.Concession, PetitionPart.Ask) => "{=lmmi_petition_charter_ask}{?PLAYER.GENDER}My lady{?}My lord{\\?}, this is your town now. Grant my house a charter, first right at the market before the others, and I'll see {LMMI_SETTLEMENT} grow fat on it. The other guilds will grumble, of course. They always do.",
                (PetitionAsk.Concession, PetitionPart.Hint) => "{=lmmi_petition_charter_hint}Want? Nothing! Though a house like mine could do great things for your town, with the right charter. Another day.",
                (PetitionAsk.Concession, PetitionPart.Yes) => "{=lmmi_petition_charter_yes}You'll have your charter.",
                (PetitionAsk.Concession, PetitionPart.No) => "{=lmmi_petition_charter_no}The market stays open to all.",
                (PetitionAsk.Concession, PetitionPart.Thanks) => "{=lmmi_petition_charter_thanks}Your town won't regret it. And neither will you.",
                _ => "{=lmmi_petition_charter_sulk}Open to all. How noble. How... unprofitable. Good day.",
            };
            return new TextObject(s);
        }

        /// <summary>The petition text with this arrival's rival or lord, price and town filled in.</summary>
        private TextObject PetitionLine(PetitionPart part)
        {
            var a = _arrival!;
            var text = PetitionText(a.Ask, part);
            if (a.Target != null) StringHelpers.SetCharacterProperties("LMMI_TARGET", a.Target.CharacterObject, text);
            text.SetTextVariable("LMMI_AMOUNT", InvestAmount());
            text.SetTextVariable("LMMI_SETTLEMENT", a.Host.CurrentSettlement?.Name ?? TextObject.GetEmpty());
            return text;
        }

        private bool IsPetitionAnswer() => _arrival != null && _arrival.Talking && _arrival.Kind == ArrivalKind.Petition && _arrival.HasAsk;

        private bool IsTributeAnswer() => _arrival != null && _arrival.Talking && _arrival.Kind == ArrivalKind.Tribute && _arrival.Gift != null;

        // ---- Tribute: the gift ----

        private void AddTributeDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_arrive_tribute_offer", "start", "lmmi_arrive_tribute_resp",
                "{=lmmi_arrive_tribute_offer}{?PLAYER.GENDER}My lady{?}My lord{\\?}! What an honor. Please, a small token of our esteem: {LMMI_GIFT}. I insist, I insist.",
                () =>
                {
                    if (!IsArrival(ArrivalKind.Tribute) || _arrival!.Gift == null) return false;
                    MBTextManager.SetTextVariable("LMMI_GIFT", _arrival.Gift.Description);
                    return true;
                },
                () =>
                {
                    var a = _arrival!;
                    SetCooldown(a.Host, ArrivalKind.Tribute, TributeCooldownDays);
                    _readyAtHours[AnyTributeKey] = CampaignTime.Now.ToHours + TributeAnywhereCooldownDays * CampaignTime.HoursInDay;
                    // What they'll want back is decided now, so asking "what's the catch?" gets a straight answer.
                    var pick = PickAsk(a.Host);
                    a.HasAsk = pick != null;
                    a.Ask = pick?.Ask ?? PetitionAsk.Rival;
                    a.Target = pick?.Target;
                    LmmiLog.Info($"Arrival: {a.Host.Name} offers you a gift{(pick != null ? $"; will ask: {a.Ask}{(a.Target != null ? " (" + a.Target.Name + ")" : "")}" : "; wants nothing in particular")}.");
                },
                1100);

            // Take it — and with it, an obligation.
            starter.AddPlayerLine("lmmi_arrive_tribute_take", "lmmi_arrive_tribute_resp", "lmmi_arrive_tribute_taken",
                "{=lmmi_arrive_tribute_take}You're too generous.", IsTributeAnswer,
                () =>
                {
                    var a = _arrival!;
                    GiveTribute(a.Host, a.Gift!);
                    if (a.HasAsk) Oblige(a.Host, a.Ask, a.Target);
                });
            starter.AddDialogLine("lmmi_arrive_tribute_owed", "lmmi_arrive_tribute_taken", "close_window",
                "{=lmmi_arrive_tribute_owed}Not at all, not at all! Only, when the time comes, remember who your friends are. I'll call on you.",
                null, null);

            // What's the catch?
            starter.AddPlayerLine("lmmi_arrive_tribute_catch", "lmmi_arrive_tribute_resp", "lmmi_arrive_tribute_hint",
                "{=lmmi_arrive_tribute_catch}And what would you want in return?",
                () => IsTributeAnswer() && !_arrival!.Hinted,
                () => _arrival!.Hinted = true);
            starter.AddDialogLine("lmmi_arrive_tribute_hint", "lmmi_arrive_tribute_hint", "lmmi_arrive_tribute_resp",
                "{=lmmi_arrive_tribute_hint}{LMMI_TRIBUTE_HINT}",
                () =>
                {
                    if (_arrival == null) return false;
                    MBTextManager.SetTextVariable("LMMI_TRIBUTE_HINT", _arrival.HasAsk
                        ? PetitionLine(PetitionPart.Hint)
                        : new TextObject("{=lmmi_arrive_tribute_nothing}Want? Nothing at all! Only your good opinion, {?PLAYER.GENDER}my lady{?}my lord{\\?}."));
                    return true;
                }, null);

            // A charming refusal: nobody loses face, nobody owes anybody.
            starter.AddPlayerLine("lmmi_arrive_tribute_graceful", "lmmi_arrive_tribute_resp", "lmmi_arrive_tribute_graced",
                "{=lmmi_arrive_tribute_graceful}Your friendship is gift enough. Keep it, and we'll owe each other nothing.",
                () => IsTributeAnswer() && Hero.MainHero.GetSkillValue(DefaultSkills.Charm) >= GracefulRefusalCharm,
                () =>
                {
                    ChangeRelationAction.ApplyPlayerRelation(_arrival!.Host, 1, affectRelatives: false);
                    Hero.MainHero.AddSkillXp(DefaultSkills.Charm, 30f);
                    LmmiLog.Info($"Arrival: you gracefully declined {_arrival.Host.Name}'s gift (relation +1, no obligation).");
                });
            starter.AddDialogLine("lmmi_arrive_tribute_graced", "lmmi_arrive_tribute_graced", "close_window",
                "{=lmmi_arrive_tribute_graced}Ha! Nobody's ever turned me down so kindly. As you wish.", null, null);

            // Refuse it: the proud take it as an insult.
            starter.AddPlayerLine("lmmi_arrive_tribute_refuse", "lmmi_arrive_tribute_resp", "lmmi_arrive_tribute_refused",
                "{=lmmi_arrive_tribute_refuse}I can't accept this.", IsTributeAnswer,
                () =>
                {
                    var host = _arrival!.Host;
                    bool proud = IsProud(host);
                    ChangeRelationAction.ApplyPlayerRelation(host, proud ? -4 : -1, affectRelatives: false);
                    MBTextManager.SetTextVariable("LMMI_TRIBUTE_REBUFF", proud
                        ? new TextObject("{=lmmi_arrive_tribute_insulted}You'd refuse me? Here, where the whole street can see? ...As you wish, {?PLAYER.GENDER}my lady{?}my lord{\\?}. I won't trouble you again.")
                        : new TextObject("{=lmmi_arrive_tribute_refused}Of course, of course. Forgive me. I meant no offense."));
                    LmmiLog.Info($"Arrival: you refused {host.Name}'s gift ({(proud ? "proud, insulted: -4" : "-1")}).");
                });
            starter.AddDialogLine("lmmi_arrive_tribute_rebuff", "lmmi_arrive_tribute_refused", "close_window",
                "{=lmmi_arrive_tribute_rebuff}{LMMI_TRIBUTE_REBUFF}", null, null);
        }

        private static void GiveTribute(Hero host, TributeGift gift)
        {
            if (gift.Item != null && gift.Count > 0 && MobileParty.MainParty != null)
            {
                MobileParty.MainParty.ItemRoster.AddToCounts(gift.Item, gift.Count);
                var msg = new TextObject("{=lmmi_tribute_gift_msg}{NAME} gave you {COUNT}x {ITEM}.");
                msg.SetTextVariable("NAME", host.Name);
                msg.SetTextVariable("COUNT", gift.Count);
                msg.SetTextVariable("ITEM", gift.Item.Name);
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString()));
                LmmiLog.Info($"Arrival: {host.Name} gave you {gift.Count}x {gift.Item.Name}.");
            }
            else if (gift.Gold > 0)
            {
                GiveGoldAction.ApplyBetweenCharacters(host, Hero.MainHero, gift.Gold);
                LmmiLog.Info($"Arrival: {host.Name} gave you {gift.Gold} gold.");
            }
        }

        private void Oblige(Hero host, PetitionAsk ask, Hero? target)
        {
            double now = CampaignTime.Now.ToHours;
            double due = LmmiSettingsProvider.TestMode
                ? now
                : now + (ObligationMinDays + MBRandom.RandomFloat * (ObligationMaxDays - ObligationMinDays)) * CampaignTime.HoursInDay;
            _obligations[host.StringId] = new Obligation { HeroId = host.StringId, DueHours = due, Ask = ask, TargetId = target?.StringId ?? "" };
            LmmiLog.Info($"Arrival: you owe {host.Name} now ({ask}{(target != null ? ", " + target.Name : "")}); they'll call in {(due - now) / CampaignTime.HoursInDay:0.#} days.");
        }

        // ---- Petition: they come to collect ----

        private void AddPetitionDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_arrive_petition_ask", "start", "lmmi_arrive_petition_resp",
                "{=lmmi_arrive_petition_ask}{LMMI_PETITION}",
                () =>
                {
                    if (!IsArrival(ArrivalKind.Petition) || !_arrival!.HasAsk) return false;
                    MBTextManager.SetTextVariable("LMMI_PETITION", PetitionLine(PetitionPart.Ask));
                    return true;
                },
                () =>
                {
                    // Heard out: the debt is settled whatever you answer.
                    _obligations.Remove(_arrival!.Host.StringId);
                    LmmiLog.Info($"Arrival: {_arrival.Host.Name} calls in the favor: {_arrival.Ask}{(_arrival.Target != null ? " (" + _arrival.Target.Name + ")" : "")}.");
                },
                1100);

            starter.AddPlayerLine("lmmi_arrive_petition_grant", "lmmi_arrive_petition_resp", "lmmi_arrive_petition_granted",
                "{=lmmi_arrive_petition_grant}{LMMI_PETITION_YES}",
                () =>
                {
                    if (!IsPetitionAnswer()) return false;
                    MBTextManager.SetTextVariable("LMMI_PETITION_YES", PetitionLine(PetitionPart.Yes));
                    return true;
                },
                () =>
                {
                    MBTextManager.SetTextVariable("LMMI_PETITION_REPLY", PetitionLine(PetitionPart.Thanks));
                    GrantPetition();
                },
                100,
                (out TextObject why) => CanGrantPetition(out why));
            starter.AddDialogLine("lmmi_arrive_petition_thanks", "lmmi_arrive_petition_granted", "close_window",
                "{=lmmi_arrive_petition_thanks}{LMMI_PETITION_REPLY}", null, null);

            starter.AddPlayerLine("lmmi_arrive_petition_refuse", "lmmi_arrive_petition_resp", "lmmi_arrive_petition_refused",
                "{=lmmi_arrive_petition_refuse}{LMMI_PETITION_NO}",
                () =>
                {
                    if (!IsPetitionAnswer()) return false;
                    MBTextManager.SetTextVariable("LMMI_PETITION_NO", PetitionLine(PetitionPart.No));
                    return true;
                },
                () =>
                {
                    MBTextManager.SetTextVariable("LMMI_PETITION_REPLY", PetitionLine(PetitionPart.Sulk));
                    RefusePetition();
                });
            starter.AddDialogLine("lmmi_arrive_petition_sulk", "lmmi_arrive_petition_refused", "close_window",
                "{=lmmi_arrive_petition_sulk}{LMMI_PETITION_REPLY}", null, null);
        }

        private bool CanGrantPetition(out TextObject why)
        {
            why = TextObject.GetEmpty();
            var a = _arrival;
            if (a == null) return false;
            switch (a.Ask)
            {
                case PetitionAsk.Son:
                    if (PartyHasRoom()) return true;
                    why = new TextObject("{=lmmi_petition_no_room}You have no room in your party.");
                    return false;
                case PetitionAsk.Invest:
                    if (!CanAfford(InvestAmount(), out why)) return false;
                    // Counterplay: a friend's warning or enough Roguery and you see the swindle coming.
                    if (Treachery.IsTreacherous(a.Host, NotableDisposition.Evaluate(a.Host)) && Treachery.Detected(a.Host, out var spotted))
                    {
                        why = spotted;
                        return false;
                    }
                    return true;
                default:
                    return true;
            }
        }

        private void GrantPetition()
        {
            var a = _arrival;
            if (a == null) return;
            var host = a.Host;
            var settlement = host.CurrentSettlement;
            switch (a.Ask)
            {
                case PetitionAsk.Rival:
                    ChangeRelationAction.ApplyPlayerRelation(host, 5, affectRelatives: false);
                    if (a.Target != null)
                    {
                        ChangeRelationAction.ApplyPlayerRelation(a.Target, -5, affectRelatives: false);
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(host, a.Target, -5, showQuickNotification: false);
                    }
                    LmmiLog.Info($"Petition: you took {host.Name}'s side against {a.Target?.Name} (+5 / -5).");
                    break;

                case PetitionAsk.GoodWord:
                {
                    var lord = a.Target;
                    ChangeRelationAction.ApplyPlayerRelation(host, 5, affectRelatives: false);
                    host.AddPower(10f);
                    if (lord != null)
                    {
                        // A friend indulges you; anyone else doesn't like being lobbied for a merchant's tolls.
                        int cost = lord.GetRelationWithPlayer() >= 20f ? -1 : -2;
                        ChangeRelationAction.ApplyPlayerRelation(lord, cost, affectRelatives: false);
                        var msg = new TextObject("{=lmmi_petition_word_msg}You put in a word for {NAME} with {LORD}. {LORD} doesn't much like being lobbied.");
                        msg.SetTextVariable("NAME", host.Name);
                        msg.SetTextVariable("LORD", lord.Name);
                        InformationManager.DisplayMessage(new InformationMessage(msg.ToString()));
                        LmmiLog.Info($"Petition: a word for {host.Name} with {lord.Name} (+5, power +10; lord {cost}).");
                    }
                    break;
                }

                case PetitionAsk.Son:
                {
                    var troop = SonTroop(host);
                    ChangeRelationAction.ApplyPlayerRelation(host, 6, affectRelatives: false);
                    if (troop != null && MobileParty.MainParty != null)
                    {
                        MobileParty.MainParty.MemberRoster.AddToCounts(troop, 1);
                        var msg = new TextObject("{=lmmi_petition_son_msg}{NAME}'s son joins your company as a {TROOP}.");
                        msg.SetTextVariable("NAME", host.Name);
                        msg.SetTextVariable("TROOP", troop.Name);
                        InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
                    }
                    LmmiLog.Info($"Petition: {host.Name}'s son joins you ({troop?.Name}); relation +6.");
                    break;
                }

                case PetitionAsk.Invest:
                {
                    int amount = InvestAmount();
                    GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, host, amount);
                    ChangeRelationAction.ApplyPlayerRelation(host, 3, affectRelatives: false);
                    bool swindle = Treachery.IsTreacherous(host, NotableDisposition.Evaluate(host));
                    // Honest ventures mostly pay; some go badly. A swindler's never do.
                    int payout = swindle ? 0
                        : MBRandom.RandomFloat < 0.8f ? (int)(amount * (1.3f + 0.3f * MBRandom.RandomFloat))
                        : (int)(amount * (0.5f + 0.3f * MBRandom.RandomFloat));
                    double due = CampaignTime.Now.ToHours
                                 + (LmmiSettingsProvider.TestMode ? 1f : InvestMinDays + MBRandom.RandomFloat * (InvestMaxDays - InvestMinDays)) * CampaignTime.HoursInDay;
                    _investments.Add(new Investment
                    {
                        HeroId = host.StringId, SettlementId = settlement?.StringId ?? "", DueHours = due, Amount = amount, Payout = payout,
                    });
                    LmmiLog.Info($"Petition: you put {amount} into {host.Name}'s venture; due in {(due - CampaignTime.Now.ToHours) / CampaignTime.HoursInDay:0.#} days, " +
                                 (swindle ? "TREACHERY: it's a swindle (pays nothing)." : $"pays {payout}."));
                    break;
                }

                case PetitionAsk.Concession:
                {
                    ChangeRelationAction.ApplyPlayerRelation(host, 8, affectRelatives: false);
                    host.AddPower(20f);
                    var town = settlement?.Town;
                    if (town != null)
                    {
                        town.Prosperity += CharterProsperity;
                        town.Loyalty -= CharterLoyalty;
                        foreach (var other in settlement!.Notables.Where(o => o != host && o.IsAlive && (o.IsMerchant || o.IsArtisan)).ToList())
                            ChangeRelationAction.ApplyPlayerRelation(other, -2, affectRelatives: false, showQuickNotification: false);
                        var msg = new TextObject("{=lmmi_petition_charter_msg}{SETTLEMENT}: {NAME}'s house has its charter. Prosperity +{PROSPERITY}, loyalty -{LOYALTY}; the other guilds grumble.");
                        msg.SetTextVariable("SETTLEMENT", settlement.Name);
                        msg.SetTextVariable("NAME", host.Name);
                        msg.SetTextVariable("PROSPERITY", (int)CharterProsperity);
                        msg.SetTextVariable("LOYALTY", (int)CharterLoyalty);
                        InformationManager.DisplayMessage(new InformationMessage(msg.ToString()));
                    }
                    LmmiLog.Info($"Petition: charter for {host.Name} in {settlement?.Name} (+8, power +20, prosperity +{CharterProsperity}, loyalty -{CharterLoyalty}, other guilds -2).");
                    break;
                }
            }
        }

        private void RefusePetition()
        {
            var a = _arrival;
            if (a == null) return;
            // You took the gift. Refusing the favor it bought is the insult — how much depends on what they asked.
            int cost = a.Ask switch
            {
                PetitionAsk.Son => -4,      // you'd shame the family
                PetitionAsk.Invest => -2,   // business is business
                PetitionAsk.Concession => -3,
                PetitionAsk.GoodWord => -3,
                _ => -4,
            };
            ChangeRelationAction.ApplyPlayerRelation(a.Host, cost, affectRelatives: false);
            LmmiLog.Info($"Petition: you refused {a.Host.Name} ({a.Ask}, {cost}).");
        }

        // ---- Debts come due; ventures come in ----

        private void OnDailyTick()
        {
            try
            {
                double now = CampaignTime.Now.ToHours;
                foreach (var o in _obligations.Values.ToList())
                {
                    var hero = FindHero(o.HeroId);
                    if (hero == null || !hero.IsAlive) { _obligations.Remove(o.HeroId); continue; }
                    if (now < o.DueHours) continue;
                    var place = hero.CurrentSettlement ?? hero.HomeSettlement;
                    if (!o.Announced)
                    {
                        o.Announced = true;
                        if (Settlement.CurrentSettlement == place) continue;   // they'll find you here
                        var msg = new TextObject("{=lmmi_obligation_due}Word from {SETTLEMENT}: {NAME} would like a word next time you're in town. You did take the gift.");
                        msg.SetTextVariable("SETTLEMENT", place?.Name ?? TextObject.GetEmpty());
                        msg.SetTextVariable("NAME", hero.Name);
                        InformationManager.DisplayMessage(new InformationMessage(msg.ToString()));
                        LmmiLog.Info($"Arrival: {hero.Name}'s favor is due ({o.Ask}).");
                    }
                    else if (now > o.DueHours + ObligationGraceDays * CampaignTime.HoursInDay)
                    {
                        _obligations.Remove(o.HeroId);
                        ChangeRelationAction.ApplyPlayerRelation(hero, -4, affectRelatives: false);
                        SetCooldown(hero, ArrivalKind.Tribute, TributeCooldownDays * 2f);
                        var msg = new TextObject("{=lmmi_obligation_ignored}You took {NAME}'s gift in {SETTLEMENT} and never came back to hear them out. They won't forget it.");
                        msg.SetTextVariable("SETTLEMENT", place?.Name ?? TextObject.GetEmpty());
                        msg.SetTextVariable("NAME", hero.Name);
                        InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
                        LmmiLog.Info($"Arrival: you ignored {hero.Name}'s favor ({o.Ask}); relation -4.");
                    }
                }

                foreach (var inv in _investments.Where(i => now >= i.DueHours).ToList())
                {
                    _investments.Remove(inv);
                    PayOut(inv);
                }
            }
            catch (Exception ex) { LmmiLog.Error("ArrivalScenesBehavior.OnDailyTick threw", ex); }
        }

        private static void PayOut(Investment inv)
        {
            var hero = FindHero(inv.HeroId);
            var place = string.IsNullOrEmpty(inv.SettlementId) ? null : Settlement.Find(inv.SettlementId);
            TextObject msg;
            Color color;
            if (hero == null || !hero.IsAlive)
            {
                msg = new TextObject("{=lmmi_invest_dead}{NAME} of {SETTLEMENT} is dead, and nobody remembers your share of the venture.");
                color = Colors.Red;
                LmmiLog.Info($"Venture: {inv.HeroId} is dead; your {inv.Amount} is lost.");
            }
            else if (inv.Payout <= 0)
            {
                msg = new TextObject("{=lmmi_invest_swindle}Word from {SETTLEMENT}: {NAME}'s venture has 'failed'. Your {AMOUNT}{GOLD_ICON} is gone, and nobody there seems surprised but you.");
                color = Colors.Red;
                LmmiLog.Info($"TREACHERY: {hero.Name} kept your {inv.Amount} ('the venture failed').");
            }
            else
            {
                int fromHero = Math.Min(Math.Max(0, hero.Gold), inv.Payout);
                if (fromHero > 0) GiveGoldAction.ApplyBetweenCharacters(hero, Hero.MainHero, fromHero);
                if (inv.Payout > fromHero) GiveGoldAction.ApplyBetweenCharacters(null!, Hero.MainHero, inv.Payout - fromHero);
                if (inv.Payout >= inv.Amount)
                {
                    ChangeRelationAction.ApplyPlayerRelation(hero, 2, affectRelatives: false);
                    msg = new TextObject("{=lmmi_invest_paid}{NAME}'s venture in {SETTLEMENT} came in: {PAYOUT}{GOLD_ICON} back on your {AMOUNT}{GOLD_ICON}.");
                    color = Colors.Green;
                }
                else
                {
                    msg = new TextObject("{=lmmi_invest_poor}{NAME}'s venture in {SETTLEMENT} went badly. They send what's left: {PAYOUT}{GOLD_ICON} of your {AMOUNT}{GOLD_ICON}.");
                    color = Colors.Yellow;
                }
                LmmiLog.Info($"Venture: {hero.Name} pays back {inv.Payout} on {inv.Amount}.");
            }
            msg.SetTextVariable("NAME", hero?.Name ?? new TextObject("{=lmmi_invest_someone}Your partner"));
            msg.SetTextVariable("SETTLEMENT", place?.Name ?? TextObject.GetEmpty());
            msg.SetTextVariable("AMOUNT", inv.Amount);
            msg.SetTextVariable("PAYOUT", inv.Payout);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), color));
        }

        // ---- Save / load ----

        /// <summary>"O;heroId;dueHours;ask;targetId;announced" and "I;heroId;settlementId;dueHours;amount;payout".</summary>
        private IEnumerable<string> SycophantSaveParts()
        {
            foreach (var o in _obligations.Values)
                yield return "O;" + o.HeroId + ";" + o.DueHours.ToString("R", CultureInfo.InvariantCulture) + ";" + (int)o.Ask + ";" + o.TargetId + ";" + (o.Announced ? "1" : "0");
            foreach (var i in _investments)
                yield return "I;" + i.HeroId + ";" + i.SettlementId + ";" + i.DueHours.ToString("R", CultureInfo.InvariantCulture) + ";"
                             + i.Amount.ToString(CultureInfo.InvariantCulture) + ";" + i.Payout.ToString(CultureInfo.InvariantCulture);
        }

        private void ResetSycophantState()
        {
            _obligations = new Dictionary<string, Obligation>();
            _investments = new List<Investment>();
        }

        private bool LoadSycophantEntry(string[] p)
        {
            if (p.Length == 6 && p[0] == "O"
                && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var due)
                && int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ask)
                && Enum.IsDefined(typeof(PetitionAsk), ask))
            {
                _obligations[p[1]] = new Obligation { HeroId = p[1], DueHours = due, Ask = (PetitionAsk)ask, TargetId = p[4], Announced = p[5] == "1" };
                return true;
            }
            if (p.Length == 6 && p[0] == "I"
                && double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var at)
                && int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount)
                && int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var payout))
            {
                _investments.Add(new Investment { HeroId = p[1], SettlementId = p[2], DueHours = at, Amount = amount, Payout = payout });
                return true;
            }
            // Saves from before obligations: "P;heroId;hours" — a petition against a rival, whatever fits when it's due.
            if (p.Length == 3 && p[0] == "P" && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var after))
            {
                _obligations[p[1]] = new Obligation { HeroId = p[1], DueHours = after, Ask = PetitionAsk.Rival };
                return true;
            }
            return false;
        }
    }
}
