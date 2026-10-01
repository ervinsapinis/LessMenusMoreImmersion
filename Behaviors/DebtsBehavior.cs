using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Merchants and gang leaders lend coin, on terms that follow how they see you: a genuine friend lends cheap,
    /// a friendly merchant lends at business rates, a wary one charges a stranger's rate and goes to the magistrate
    /// if you don't pay; a dishonorable merchant or any gang leader lends at a shark's terms and collects with men
    /// instead of letters. Nobody lends to someone they despise. Asked face to face, repaid in person; defaulting
    /// costs relation and your name in town — or, with a shark, invites his collectors onto the road (AmbushBehavior).
    /// </summary>
    public class DebtsBehavior : CampaignBehaviorBase
    {
        private enum Terms { None = 0, Friend = 1, Business = 2, Stranger = 3, Shark = 4 }

        private sealed class Debt
        {
            public string LenderId = "";
            public int Owed;
            public double DueHours;
            public Terms Terms;
            public bool Defaulted;
            public bool Shark => Terms == Terms.Shark;
        }

        //                                            interest  days  base cap  per tier  share of lender's gold
        private static (float Interest, float Days, int Cap, int PerTier, float Share) Of(Terms t)
        {
            switch (t)
            {
                case Terms.Friend: return (0.10f, 30f, 1000, 500, 0.5f);
                case Terms.Business: return (0.20f, 30f, 700, 300, 0.4f);
                case Terms.Stranger: return (0.35f, 20f, 400, 200, 0.25f);
                default: return (0.40f, 20f, 800, 400, 0.6f);
            }
        }

        [NonSerialized] private Dictionary<string, Debt> _debts = new Dictionary<string, Debt>();
        [NonSerialized] private int _offerAmount;
        [NonSerialized] private int _offerOwed;
        [NonSerialized] private Terms _offerTerms;

        private static DebtsBehavior? Instance => Campaign.Current?.GetCampaignBehavior<DebtsBehavior>();

        /// <summary>Loan sharks you owe and haven't paid on time — their collectors may be on the road.</summary>
        public static List<Hero> OverdueSharks()
        {
            var self = Instance;
            if (self == null) return new List<Hero>();
            return self._debts.Values.Where(d => d.Shark && d.Defaulted)
                .Select(d => Hero.FindFirst(h => h.StringId == d.LenderId))
                .Where(h => h != null && h.IsAlive)
                .ToList()!;
        }

        public static int OwedTo(Hero lender) =>
            Instance != null && Instance._debts.TryGetValue(lender.StringId, out var d) ? d.Owed : 0;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try { AddDialogs(starter); }
            catch (Exception ex) { LmmiLog.Error("DebtsBehavior: failed to register dialogs", ex); }
        }

        private void AddDialogs(CampaignGameStarter starter)
        {
            // ---- Asking for a loan (listed with the favors) ----
            starter.AddPlayerLine("lmmi_loan_ask", "lmmi_favor_list", "lmmi_loan_offer",
                "{=lmmi_loan_ask}I'm short of coin. Would you lend me some?",
                () =>
                {
                    var n = Hero.OneToOneConversationHero;
                    return n != null && (n.IsMerchant || n.IsGangLeader) && !_debts.ContainsKey(n.StringId);
                },
                PrepareOffer,
                100,
                (out TextObject why) => CanBorrow(out why));

            starter.AddDialogLine("lmmi_loan_poor", "lmmi_loan_offer", "hero_main_options",
                "{=!}{LMMI_LOAN_POOR}",
                () => _offerAmount < 200 && Flavor.Say("LMMI_LOAN_POOR",
                        "{=lmmi_loan_poor}I'm stretched thin myself, I'm afraid. Ask me another time.",
                        "{=lmmi_loan_poor_2}My own coffers are thin just now. Try me later.",
                        "{=lmmi_loan_poor_3}I've nothing to spare this season, I'm sorry."), null);

            starter.AddDialogLine("lmmi_loan_offer", "lmmi_loan_offer", "lmmi_loan_terms",
                "{=lmmi_loan_offer}{LMMI_LOAN_OFFER}",
                () => _offerAmount >= 200, null);

            starter.AddPlayerLine("lmmi_loan_accept", "lmmi_loan_terms", "lmmi_loan_done",
                "{=lmmi_loan_accept}Agreed.", null, AcceptLoan);
            starter.AddDialogLine("lmmi_loan_done", "lmmi_loan_done", "hero_main_options",
                "{=!}{LMMI_LOAN_DONE}",
                    () => Flavor.Say("LMMI_LOAN_DONE",
                        "{=lmmi_loan_done}Then it's done. Don't make me regret it.",
                        "{=lmmi_loan_done_2}Done. Pay me on time and we'll stay friends.",
                        "{=lmmi_loan_done_3}Then we have a deal. I'll be counting the days."), null);

            starter.AddPlayerLine("lmmi_loan_decline", "lmmi_loan_terms", "lmmi_loan_declined",
                "{=lmmi_loan_decline}On second thought, no.", null, null);
            starter.AddDialogLine("lmmi_loan_declined", "lmmi_loan_declined", "hero_main_options",
                "{=!}{LMMI_LOAN_DECLINED}",
                    () => Flavor.Say("LMMI_LOAN_DECLINED",
                        "{=lmmi_loan_declined}As you like.",
                        "{=lmmi_loan_declined_2}Suit yourself.",
                        "{=lmmi_loan_declined_3}Your choice. The offer may not stand forever."), null);

            // ---- Repaying (any conversation with the lender) ----
            starter.AddPlayerLine("lmmi_loan_repay", "hero_main_options", "lmmi_loan_repaid",
                "{=lmmi_loan_repay}I've come to settle my debt. [{LMMI_LOAN_REPAY}{GOLD_ICON}]",
                () =>
                {
                    var n = Hero.OneToOneConversationHero;
                    if (n == null || !_debts.TryGetValue(n.StringId, out var d)) return false;
                    MBTextManager.SetTextVariable("LMMI_LOAN_REPAY", d.Owed);
                    return true;
                },
                Repay,
                105,
                (out TextObject why) =>
                {
                    why = TextObject.GetEmpty();
                    var n = Hero.OneToOneConversationHero;
                    if (n == null || !_debts.TryGetValue(n.StringId, out var d)) return false;
                    if (Hero.MainHero.Gold >= d.Owed) return true;
                    why = new TextObject("{=lmmi_loan_repay_short}You don't have enough coin.");
                    return false;
                });

            starter.AddDialogLine("lmmi_loan_repaid", "lmmi_loan_repaid", "hero_main_options",
                "{=lmmi_loan_repaid}{LMMI_LOAN_REPAID_TEXT}", null, null);
        }

        private static Terms TermsFor(Hero n, DispositionBreakdown e)
        {
            if (e.Level == Disposition.Contempt) return Terms.None;
            if (n.IsGangLeader) return Terms.Shark;
            if (!n.IsMerchant) return Terms.None;
            if (Treachery.IsTreacherous(n, e)) return Terms.Shark;
            if (e.Level == Disposition.Trusted && e.Genuine) return Terms.Friend;
            if (e.Level >= Disposition.Friendly) return Terms.Business;
            return Terms.Stranger;
        }

        private bool CanBorrow(out TextObject why)
        {
            why = TextObject.GetEmpty();
            var n = Hero.OneToOneConversationHero;
            if (n == null) return false;
            if (TermsFor(n, NotableDisposition.Evaluate(n)) != Terms.None) return true;

            why = new TextObject("{=lmmi_loan_refused_contempt}{NAME} wouldn't lend a copper to someone they despise.");
            why.SetTextVariable("NAME", n.Name);
            return false;
        }

        private void PrepareOffer()
        {
            var n = Hero.OneToOneConversationHero;
            if (n == null) return;
            var e = NotableDisposition.Evaluate(n);
            _offerTerms = TermsFor(n, e);
            var t = Of(_offerTerms);
            int tier = Clan.PlayerClan?.Tier ?? 0;
            _offerAmount = Math.Min(t.Cap + t.PerTier * tier, (int)(n.Gold * t.Share)) / 50 * 50;
            _offerOwed = (int)Math.Round(_offerAmount * (1f + t.Interest));

            TextObject offer;
            switch (_offerTerms)
            {
                case Terms.Friend:
                    offer = Flavor.Pick("{=lmmi_loan_offer_fair}For you? I can spare {LMMI_LOAN_AMOUNT}{GOLD_ICON}. Pay me back {LMMI_LOAN_OWED}{GOLD_ICON} within {LMMI_LOAN_DAYS} days. I trust you — but my ledger doesn't.",
                        "{=lmmi_loan_offer_fair_2}I'll lend you {LMMI_LOAN_AMOUNT}{GOLD_ICON}. Bring back {LMMI_LOAN_OWED}{GOLD_ICON} within {LMMI_LOAN_DAYS} days, friend.",
                        "{=lmmi_loan_offer_fair_3}{LMMI_LOAN_AMOUNT}{GOLD_ICON}, for a friend. {LMMI_LOAN_OWED}{GOLD_ICON} back in {LMMI_LOAN_DAYS} days — no more than fair.");
                    break;
                case Terms.Business:
                    offer = Flavor.Pick("{=lmmi_loan_offer_business}I can do {LMMI_LOAN_AMOUNT}{GOLD_ICON}. Business is business: {LMMI_LOAN_OWED}{GOLD_ICON} back within {LMMI_LOAN_DAYS} days.",
                        "{=lmmi_loan_offer_business_2}{LMMI_LOAN_AMOUNT}{GOLD_ICON}, at the usual rate: {LMMI_LOAN_OWED}{GOLD_ICON} in {LMMI_LOAN_DAYS} days.",
                        "{=lmmi_loan_offer_business_3}I can lend {LMMI_LOAN_AMOUNT}{GOLD_ICON}. You'll owe {LMMI_LOAN_OWED}{GOLD_ICON} by {LMMI_LOAN_DAYS} days from now.");
                    break;
                case Terms.Stranger:
                    offer = NotableDisposition.IsForeigner(n)
                        ? Flavor.Pick("{=lmmi_loan_offer_stranger_foreign}Your kind aren't known for settling their debts, {DEMONYM}. {LMMI_LOAN_AMOUNT}{GOLD_ICON}, and you pay back {LMMI_LOAN_OWED}{GOLD_ICON} within {LMMI_LOAN_DAYS} days. Miss it, and I go to the magistrate.",
                            "{=lmmi_loan_offer_stranger_foreign_2}I don't trust {DEMONYM} borrowers. {LMMI_LOAN_AMOUNT}{GOLD_ICON}, {LMMI_LOAN_OWED}{GOLD_ICON} back in {LMMI_LOAN_DAYS} days — or the magistrate hears of it.",
                            "{=lmmi_loan_offer_stranger_foreign_3}A {DEMONYM} wanting coin. {LMMI_LOAN_AMOUNT}{GOLD_ICON}, then; {LMMI_LOAN_OWED}{GOLD_ICON} within {LMMI_LOAN_DAYS} days. Late, and I go to the law.")
                        : Flavor.Pick("{=lmmi_loan_offer_stranger}I don't know you, and I don't lend cheap to people I don't know. {LMMI_LOAN_AMOUNT}{GOLD_ICON}; you pay back {LMMI_LOAN_OWED}{GOLD_ICON} within {LMMI_LOAN_DAYS} days. Miss it, and I go to the magistrate.",
                            "{=lmmi_loan_offer_stranger_2}Strangers pay dear. {LMMI_LOAN_AMOUNT}{GOLD_ICON}, {LMMI_LOAN_OWED}{GOLD_ICON} back in {LMMI_LOAN_DAYS} days, or the magistrate hears your name.",
                            "{=lmmi_loan_offer_stranger_3}{LMMI_LOAN_AMOUNT}{GOLD_ICON}. {LMMI_LOAN_OWED}{GOLD_ICON} within {LMMI_LOAN_DAYS} days. I don't know you, so I take no chances.");
                    offer.SetTextVariable("DEMONYM", CultureWords.Demonym(Hero.MainHero.Culture));
                    break;
                default:
                    offer = n.IsGangLeader
                        ? Flavor.Pick("{=lmmi_loan_offer_gang}Need coin? Everyone does. {LMMI_LOAN_AMOUNT}{GOLD_ICON} now, {LMMI_LOAN_OWED}{GOLD_ICON} in {LMMI_LOAN_DAYS} days. You don't want to find out what happens if you're late.",
                            "{=lmmi_loan_offer_gang_2}Coin? Sure. {LMMI_LOAN_AMOUNT}{GOLD_ICON} now, {LMMI_LOAN_OWED}{GOLD_ICON} in {LMMI_LOAN_DAYS} days. Don't make me send the boys.",
                            "{=lmmi_loan_offer_gang_3}{LMMI_LOAN_AMOUNT}{GOLD_ICON}, cash in hand. {LMMI_LOAN_OWED}{GOLD_ICON} back in {LMMI_LOAN_DAYS} days. My lads hate chasing debtors — they do it anyway.")
                        : Flavor.Pick("{=lmmi_loan_offer_shark}{LMMI_LOAN_AMOUNT}{GOLD_ICON}, today. You'll owe me {LMMI_LOAN_OWED}{GOLD_ICON} in {LMMI_LOAN_DAYS} days. And friend — I always collect.",
                            "{=lmmi_loan_offer_shark_2}{LMMI_LOAN_AMOUNT}{GOLD_ICON} now. {LMMI_LOAN_OWED}{GOLD_ICON} in {LMMI_LOAN_DAYS} days. I've never lost a debtor yet.",
                            "{=lmmi_loan_offer_shark_3}Here's {LMMI_LOAN_AMOUNT}{GOLD_ICON}. You owe me {LMMI_LOAN_OWED}{GOLD_ICON} in {LMMI_LOAN_DAYS} days. Late payers have accidents.");
                    break;
            }
            offer.SetTextVariable("LMMI_LOAN_AMOUNT", _offerAmount);
            offer.SetTextVariable("LMMI_LOAN_OWED", _offerOwed);
            offer.SetTextVariable("LMMI_LOAN_DAYS", (int)t.Days);
            MBTextManager.SetTextVariable("LMMI_LOAN_OFFER", offer);
            LmmiLog.Info($"Loan offer: {n.Name} ({e.Level}{(e.Genuine ? ", genuine" : "")}) — {_offerTerms} terms: {_offerAmount} for {_offerOwed} in {t.Days} days.");
        }

        private void AcceptLoan()
        {
            var n = Hero.OneToOneConversationHero;
            if (n == null || _offerAmount < 200) return;
            GiveGoldAction.ApplyBetweenCharacters(n, Hero.MainHero, _offerAmount);
            float days = Of(_offerTerms).Days;
            _debts[n.StringId] = new Debt
            {
                LenderId = n.StringId,
                Owed = _offerOwed,
                DueHours = CampaignTime.Now.ToHours + days * CampaignTime.HoursInDay,
                Terms = _offerTerms,
            };
            LmmiLog.Info($"Loan: {n.Name} lent {_offerAmount}, owed {_offerOwed} in {days} days ({_offerTerms} terms).");
        }

        private void Repay()
        {
            var n = Hero.OneToOneConversationHero;
            if (n == null || !_debts.TryGetValue(n.StringId, out var d)) return;
            GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, n, d.Owed);
            _debts.Remove(n.StringId);

            TextObject reply;
            if (d.Shark)
                reply = (d.Defaulted
                    ? Flavor.Pick("{=lmmi_loan_repaid_shark_late}There. Was that so hard? My men will be glad to hear they can stand down.",
                        "{=lmmi_loan_repaid_shark_late_2}About time. I'll call off the dogs.",
                        "{=lmmi_loan_repaid_shark_late_3}Paid at last. You were lucky my men hadn't found you yet.")
                    : Flavor.Pick("{=lmmi_loan_repaid_shark}Smart. Very smart.",
                        "{=lmmi_loan_repaid_shark_2}Good. We understand each other.",
                        "{=lmmi_loan_repaid_shark_3}Prompt. I like that in a debtor."));
            else if (d.Defaulted)
                reply = Flavor.Pick("{=lmmi_loan_repaid_late}Late, but paid. I'll remember both.",
                        "{=lmmi_loan_repaid_late_2}Late. Paid, but late. I won't forget it.",
                        "{=lmmi_loan_repaid_late_3}Finally. Next time, be on time.");
            else
            {
                reply = Flavor.Pick("{=lmmi_loan_repaid_fair}Right on time. A pleasure doing business with you.",
                        "{=lmmi_loan_repaid_fair_2}On the day, as promised. Good dealing.",
                        "{=lmmi_loan_repaid_fair_3}Paid in full, and on time. My door's open next time.");
                ChangeRelationAction.ApplyPlayerRelation(n, d.Terms == Terms.Stranger ? 3 : 2, affectRelatives: false);   // a stranger who pays earns trust
            }
            MBTextManager.SetTextVariable("LMMI_LOAN_REPAID_TEXT", reply);
            LmmiLog.Info($"Loan: repaid {d.Owed} to {n.Name}{(d.Defaulted ? " (late)" : "")}.");
        }

        private void OnDailyTick()
        {
            try
            {
                double now = CampaignTime.Now.ToHours;
                foreach (var d in _debts.Values.ToList())
                {
                    var lender = Hero.FindFirst(h => h.StringId == d.LenderId);
                    if (lender == null || !lender.IsAlive)
                    {
                        _debts.Remove(d.LenderId);   // the debt died with the lender
                        continue;
                    }
                    if (d.Defaulted || now < d.DueHours) continue;

                    d.Defaulted = true;
                    d.Owed = (int)Math.Round(d.Owed * 1.1f);   // late fee
                    ChangeRelationAction.ApplyPlayerRelation(lender, d.Terms == Terms.Friend ? -15 : -10, affectRelatives: false);

                    TextObject news;
                    if (d.Shark)
                        news = Flavor.Pick("{=lmmi_loan_overdue_shark}Your debt to {NAME} of {TOWN} is overdue. They say {NAME} always collects. You should watch the roads.",
                        "{=lmmi_loan_overdue_shark_2}Your debt to {NAME} of {TOWN} is overdue. {NAME}'s men are asking where you ride.",
                        "{=lmmi_loan_overdue_shark_3}Overdue: your debt to {NAME} of {TOWN}. Mind the roads — {NAME} collects.");
                    else if (d.Terms == Terms.Stranger && lender.CurrentSettlement?.MapFaction != null)
                    {
                        // A stranger's loan comes with the magistrate.
                        news = Flavor.Pick("{=lmmi_loan_overdue_stranger}Your debt to {NAME} of {TOWN} is overdue. {NAME} has gone to the magistrate: the watch there will be looking for you.",
                        "{=lmmi_loan_overdue_stranger_2}Your debt to {NAME} of {TOWN} is overdue. The magistrate there has your name now.",
                        "{=lmmi_loan_overdue_stranger_3}{NAME} of {TOWN} has reported your overdue debt. The watch will be looking for you.");
                        ChangeCrimeRatingAction.Apply(lender.CurrentSettlement.MapFaction, 15f);
                        TownStandingBehavior.Adjust(lender.CurrentSettlement, -5f, $"defaulted on {lender.Name}'s loan");
                    }
                    else
                    {
                        news = Flavor.Pick("{=lmmi_loan_overdue_fair}Your debt to {NAME} of {TOWN} is overdue. {NAME} is telling anyone who'll listen.",
                        "{=lmmi_loan_overdue_fair_2}Your debt to {NAME} of {TOWN} is overdue. {NAME} is complaining about you to everyone.",
                        "{=lmmi_loan_overdue_fair_3}{NAME} of {TOWN} is waiting on your overdue debt — and telling the whole town about it.");
                        TownStandingBehavior.Adjust(lender.CurrentSettlement, -5f, $"defaulted on {lender.Name}'s loan");
                    }
                    news.SetTextVariable("NAME", lender.Name);
                    news.SetTextVariable("TOWN", lender.CurrentSettlement?.Name ?? lender.HomeSettlement?.Name ?? TextObject.GetEmpty());
                    InformationManager.DisplayMessage(new InformationMessage(news.ToString(), Colors.Red));
                    LmmiLog.Info($"Loan: defaulted on {lender.Name} (now owe {d.Owed}){(d.Shark ? ", collectors may come" : "")}.");
                }
            }
            catch (Exception ex) { LmmiLog.Error("DebtsBehavior.OnDailyTick threw", ex); }
        }

        // ---- Save / load: "lenderId;owed;dueHours;shark;defaulted;terms" entries separated by '|' ----

        public override void SyncData(IDataStore dataStore)
        {
            string data = dataStore.IsLoading ? string.Empty
                : string.Join("|", _debts.Values.Select(d => string.Join(";", d.LenderId, d.Owed.ToString(CultureInfo.InvariantCulture),
                    d.DueHours.ToString("R", CultureInfo.InvariantCulture), d.Shark ? "1" : "0", d.Defaulted ? "1" : "0",
                    ((int)d.Terms).ToString(CultureInfo.InvariantCulture))));
            dataStore.SyncData("lmmi_debts_v1", ref data);
            if (!dataStore.IsLoading) return;

            _debts = new Dictionary<string, Debt>();
            foreach (var entry in (data ?? string.Empty).Split('|'))
            {
                var p = entry.Split(';');
                if (p.Length != 5 && p.Length != 6) continue;
                if (!int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var owed)) continue;
                if (!double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var due)) continue;
                var terms = p[3] == "1" ? Terms.Shark : Terms.Friend;
                if (p.Length == 6 && int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ti) && ti >= 1 && ti <= 4) terms = (Terms)ti;
                _debts[p[0]] = new Debt { LenderId = p[0], Owed = owed, DueHours = due, Terms = terms, Defaulted = p[4] == "1" };
            }
        }
    }
}
