using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation.Persuasion;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Lords talk about you. Each week an enemy who's no stranger to dishonor may slander you to their friends at court,
    /// and a true friend may speak up for you — relations drift, and word reaches you. If someone paid sellswords against
    /// you, you can bring it before their liege (vanilla persuasion): a fine, half of it yours, and the sender shamed — or
    /// the liege won't hear it. And two can play that game: a gang leader will find men to rough up an enemy of yours, for a
    /// price, and word of who paid has a way of getting out.
    /// </summary>
    public class LordsBehavior : CampaignBehaviorBase
    {
        private sealed class Revenge
        {
            public string TargetId = "";
            public string BrokerId = "";
            public double DueHours;
        }

        private const float GrievanceDays = 90f;

        [NonSerialized] private Dictionary<string, double> _grievances = new Dictionary<string, double>();   // sender -> when you learned
        [NonSerialized] private HashSet<string> _accused = new HashSet<string>();
        [NonSerialized] private List<Revenge> _revenge = new List<Revenge>();
        [NonSerialized] private List<Hero> _enemies = new List<Hero>();
        [NonSerialized] private Hero? _chosenEnemy;
        [NonSerialized] private int _revengePrice;
        [NonSerialized] private readonly NativePersuasion _grievance = new NativePersuasion("grievance");
        [NonSerialized] private string _lastSlanderer = "";
        [NonSerialized] private double _lastSlanderHours = -1;

        /// <summary>Test: run this week's talk at court now.</summary>
        public static void TestWeekly() => Instance?.OnWeeklyTick();

        private static LordsBehavior? Instance => Campaign.Current?.GetCampaignBehavior<LordsBehavior>();

        /// <summary>From the hired swords: they named who sent them.</summary>
        public static void NoteGrievance(Hero? sender)
        {
            var self = Instance;
            if (self == null || sender == null) return;
            if (!self._grievances.ContainsKey(sender.StringId)) LmmiLog.Info($"Lords: you know {sender.Name} paid for sellswords against you.");
            self._grievances[sender.StringId] = CampaignTime.Now.ToHours;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        // ---- Slander and praise ----

        /// <summary>A lord's friends at court: fellow clan leaders of the realm (and their own clan) they think well of.</summary>
        private static List<Hero> FriendsOf(Hero lord)
        {
            var pool = new List<Hero>();
            var kingdom = lord.Clan?.Kingdom;
            if (kingdom != null) pool.AddRange(kingdom.Clans.Where(c => !c.IsEliminated && c.Leader != null).Select(c => c.Leader));
            if (lord.Clan != null) pool.AddRange(lord.Clan.Heroes.Where(h => h.IsLord));
            return pool.Distinct().Where(h => h != lord && h != Hero.MainHero && h.IsAlive && h.Clan != Clan.PlayerClan && lord.GetRelation(h) >= 20).ToList();
        }

        private void OnWeeklyTick()
        {
            try
            {
                if (!LmmiSettingsProvider.EnableLordsTalk) return;
                Hero? slanderer = null, praiser = null;
                int slandered = 0, praised = 0;
                foreach (var lord in Hero.AllAliveHeroes.Where(h => h.IsLord && h.Clan != null && h.Clan != Clan.PlayerClan && !h.IsPrisoner && !h.IsChild)
                             .OrderBy(_ => MBRandom.RandomFloat))
                {
                    float relation = lord.GetRelationWithPlayer();
                    if (slanderer == null && relation <= -30f && lord.GetTraitLevel(DefaultTraits.Honor) <= 0 && MBRandom.RandomFloat < 0.25f)
                    {
                        foreach (var friend in FriendsOf(lord).OrderBy(_ => MBRandom.RandomFloat).Take(2))
                        {
                            if (friend.GetRelationWithPlayer() <= -40f) continue;
                            ChangeRelationAction.ApplyPlayerRelation(friend, MBRandom.RandomInt(1, 3) * -1, affectRelatives: false, showQuickNotification: false);
                            slandered++;
                        }
                        if (slandered > 0) slanderer = lord;
                    }
                    else if (praiser == null && relation >= 40f && MBRandom.RandomFloat < 0.25f)
                    {
                        foreach (var friend in FriendsOf(lord).OrderBy(_ => MBRandom.RandomFloat).Take(2))
                        {
                            if (friend.GetRelationWithPlayer() >= 40f) continue;
                            ChangeRelationAction.ApplyPlayerRelation(friend, 1, affectRelatives: false, showQuickNotification: false);
                            praised++;
                        }
                        if (praised > 0) praiser = lord;
                    }
                    if (slanderer != null && praiser != null) break;
                }
                if (slanderer != null)
                {
                    _lastSlanderer = slanderer.StringId;
                    _lastSlanderHours = CampaignTime.Now.ToHours;
                    var msg = new TextObject("{=lmmi_lords_slander}Word reaches you: {LORD} has been speaking ill of you at court. {N} of {LORD}'s friends think less of you for it.");
                    msg.SetTextVariable("LORD", slanderer.Name);
                    msg.SetTextVariable("N", slandered);
                    InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
                    LmmiLog.Info($"Lords: {slanderer.Name} slanders you to {slandered} friend(s).");
                }
                if (praiser != null)
                {
                    var msg = new TextObject("{=lmmi_lords_praise}Word reaches you: {LORD} has been speaking well of you. {N} of {LORD}'s friends think better of you for it.");
                    msg.SetTextVariable("LORD", praiser.Name);
                    msg.SetTextVariable("N", praised);
                    InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
                    LmmiLog.Info($"Lords: {praiser.Name} speaks up for you to {praised} friend(s).");
                }

                // Old grievances lapse.
                double now = CampaignTime.Now.ToHours;
                foreach (var key in _grievances.Where(kv => now - kv.Value > GrievanceDays * CampaignTime.HoursInDay).Select(kv => kv.Key).ToList())
                    _grievances.Remove(key);
            }
            catch (Exception ex) { LmmiLog.Error("LordsBehavior.OnWeeklyTick threw", ex); }
        }

        // ---- A grievance before the liege ----

        /// <summary>Who the sender answers to: their clan's leader, their realm's ruler, or (a notable) the lord of their town.</summary>
        private static bool IsLiegeOf(Hero lord, Hero sender)
        {
            if (lord == sender) return false;
            if (sender.IsLord)
                return (sender.Clan?.Leader == lord && sender.Clan.Leader != sender) || sender.Clan?.Kingdom?.Leader == lord;
            var place = sender.CurrentSettlement ?? sender.HomeSettlement;
            return place != null && (place.OwnerClan?.Leader == lord || (place.MapFaction as Kingdom)?.Leader == lord);
        }

        private Hero? GrievanceFor(Hero lord) =>
            _grievances.Keys.Where(id => !_accused.Contains(id)).Select(id => Hero.FindFirst(h => h.StringId == id))
                .FirstOrDefault(s => s != null && s.IsAlive && IsLiegeOf(lord, s));

        // ---- Your own hired swords ----

        private static List<Hero> Enemies(Hero broker) =>
            Hero.AllAliveHeroes.Where(h => h != broker && h != Hero.MainHero && !h.IsChild && !h.IsPrisoner && h.Clan != Clan.PlayerClan
                                           && (h.IsLord || h.IsNotable) && h.GetRelationWithPlayer() <= -20f)
                .OrderBy(h => h.GetRelationWithPlayer()).Take(3).ToList();

        private static int PriceFor(Hero target, Hero broker)
        {
            int basePrice = target.IsLord ? 1500 + 20 * (target.PartyBelongedTo?.MemberRoster.TotalManCount ?? 20) : 900;
            float greed = NotableDisposition.Get(broker) == Disposition.Wary ? 1.5f : 1f;
            return (int)(basePrice * greed / 50f) * 50;
        }

        private void OnHourlyTick()
        {
            try
            {
                double now = CampaignTime.Now.ToHours;
                foreach (var r in _revenge.Where(r => r.DueHours <= now).ToList())
                {
                    _revenge.Remove(r);
                    Resolve(r);
                }
            }
            catch (Exception ex) { LmmiLog.Error("LordsBehavior.OnHourlyTick threw", ex); }
        }

        /// <summary>Word comes back: it was done, or it wasn't — and maybe they know who paid.</summary>
        private void Resolve(Revenge r)
        {
            var target = Hero.FindFirst(h => h.StringId == r.TargetId);
            var broker = Hero.FindFirst(h => h.StringId == r.BrokerId);
            if (target == null || !target.IsAlive) return;
            int men = target.PartyBelongedTo?.MemberRoster.TotalHealthyCount ?? 0;
            float chance = target.IsLord ? Math.Max(0.25f, 0.65f - men / 250f) : 0.8f;
            bool done = MBRandom.RandomFloat < chance;
            float discovery = Math.Max(0.1f, (done ? 0.3f : 0.5f) - (broker != null ? broker.GetRelationWithPlayer() / 200f : 0f));
            bool found = MBRandom.RandomFloat < discovery;
            TextObject msg;
            if (done)
            {
                int purse = Math.Min(target.Gold, Math.Max(100, target.Gold / 6));
                if (purse > 0) target.ChangeHeroGold(-purse);
                target.HitPoints = Math.Max(1, (int)(target.MaxHitPoints * 0.2f));
                var party = target.PartyBelongedTo;
                if (party != null && party.LeaderHero == target)
                {
                    int toWound = Math.Min(party.MemberRoster.TotalHealthyCount / 5, 20);
                    foreach (var e in party.MemberRoster.GetTroopRoster().Where(e => !e.Character.IsHero).OrderBy(_ => MBRandom.RandomFloat).ToList())
                    {
                        if (toWound <= 0) break;
                        int n = Math.Min(toWound, e.Number - e.WoundedNumber);
                        if (n <= 0) continue;
                        party.MemberRoster.WoundTroop(e.Character, n);
                        toWound -= n;
                    }
                }
                msg = new TextObject(target.IsLord
                    ? "{=lmmi_lords_revenge_done_lord}Word comes: {TARGET}'s column was set upon by sellswords on the road. They left with {TARGET}'s purse, and {TARGET} is abed with wounds."
                    : "{=lmmi_lords_revenge_done_notable}Word comes: {TARGET} was waylaid in a back street and badly beaten. {TARGET}'s strongbox is lighter, too.");
            }
            else
                msg = new TextObject("{=lmmi_lords_revenge_failed}Word comes: the men you paid for came back bloodied and empty-handed. {TARGET} was ready for them.");
            msg.SetTextVariable("TARGET", target.Name);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), done ? Colors.Green : Colors.Red));
            if (found)
            {
                ChangeRelationAction.ApplyPlayerRelation(target, -15, affectRelatives: true);
                var known = new TextObject("{=lmmi_lords_revenge_found}And {TARGET} knows who paid for it.");
                known.SetTextVariable("TARGET", target.Name);
                InformationManager.DisplayMessage(new InformationMessage(known.ToString(), Colors.Red));
            }
            LmmiLog.Info($"Lords: your hired swords against {target.Name} — {(done ? "done" : "failed")} ({chance:P0}); {(found ? "found out" : "unknown")}.");
        }

        // ---- Dialogs ----

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try
            {
                AddGrievanceDialogs(starter);
                AddReputationDialogs(starter);
                AddRevengeDialogs(starter);
            }
            catch (Exception ex) { LmmiLog.Error("LordsBehavior: failed to register dialogs", ex); }
        }

        private void AddGrievanceDialogs(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("lmmi_lords_grievance", "hero_main_options", "lmmi_lords_grievance_resp",
                "{=lmmi_lords_grievance}I've a grievance against {LMMI_GRIEVANCE_SENDER}, who answers to you. {?LMMI_GRIEVANCE_FEMALE}She{?}He{\\?} paid sellswords to come after me.",
                () =>
                {
                    var lord = Hero.OneToOneConversationHero;
                    if (lord == null || !lord.IsLord) return false;
                    var sender = GrievanceFor(lord);
                    if (sender == null) return false;
                    MBTextManager.SetTextVariable("LMMI_GRIEVANCE_SENDER", sender.Name);
                    MBTextManager.SetTextVariable("LMMI_GRIEVANCE_FEMALE", sender.IsFemale ? 1 : 0);
                    return true;
                }, null);
            starter.AddDialogLine("lmmi_lords_grievance_resp", "lmmi_lords_grievance_resp", "lmmi_lords_grievance_choice",
                "{=lmmi_lords_grievance_resp}That's a grave charge against one of my own. What proof do you have?", null, null);

            starter.AddPlayerLine("lmmi_lords_grievance_argue", "lmmi_lords_grievance_choice", _grievance.Entry,
                "{=lmmi_lords_grievance_argue}Their own men named {LMMI_GRIEVANCE_SENDER} — and came with {?LMMI_GRIEVANCE_FEMALE}her{?}his{\\?} silver in their purses.", null,
                () =>
                {
                    var lord = Hero.OneToOneConversationHero;
                    var sender = lord != null ? GrievanceFor(lord) : null;
                    if (lord == null || sender == null) return;
                    _accused.Add(sender.StringId);
                    var listener = lord.CharacterObject;
                    int friendship = lord.GetRelation(sender);
                    var difficulty = friendship >= 30 ? PersuasionDifficulty.Hard : friendship >= 0 ? PersuasionDifficulty.MediumHard : PersuasionDifficulty.Medium;
                    _grievance.Start(new[]
                        {
                            NativePersuasion.Argument(DefaultSkills.Charm, DefaultTraits.Honor,
                                new TextObject("{=lmmi_lords_grievance_honor}I ask for justice, nothing more. Your law, applied to your own."), listener),
                            NativePersuasion.Argument(DefaultSkills.Leadership, DefaultTraits.Calculating,
                                new TextObject("{=lmmi_lords_grievance_calc}A vassal who hires knives against your guests will one day hire them against you."), listener),
                            NativePersuasion.Argument(DefaultSkills.Charm, DefaultTraits.Generosity,
                                new TextObject("{=lmmi_lords_grievance_generous}Let it be seen to be settled, and I'll bear no grudge against your house."), listener),
                        },
                        new TextObject("{=lmmi_lords_grievance_opening}Go on."),
                        new TextObject("{=lmmi_lords_grievance_again}And?"),
                        new TextObject("{=lmmi_lords_grievance_won}...You're right. {LMMI_GRIEVANCE_SENDER} will answer for this, and pay for it."),
                        new TextObject("{=lmmi_lords_grievance_lost}I'll not have my own shamed on the word of hired thugs. Leave it."),
                        onWon: () => Punish(lord, sender),
                        onLost: () =>
                        {
                            ChangeRelationAction.ApplyPlayerRelation(lord, -3, affectRelatives: false);
                            LmmiLog.Info($"Lords: {lord.Name} won't hear your grievance against {sender.Name}.");
                        },
                        goal: 2f, difficulty: difficulty);
                });
            starter.AddPlayerLine("lmmi_lords_grievance_drop", "lmmi_lords_grievance_choice", "lord_pretalk",
                "{=lmmi_lords_grievance_drop}...Nothing I can prove. Forget I spoke.", null, null);

            _grievance.Register(starter, wonState: "lord_pretalk", lostState: "lord_pretalk");
        }

        /// <summary>The liege makes an example: a fine (half to you), the sender shamed at court, and no more contracts from them for a while.</summary>
        private static void Punish(Hero lord, Hero sender)
        {
            int tier = sender.Clan?.Tier ?? 1;
            int fine = Math.Min(sender.Gold, 1500 + 500 * tier);
            if (fine > 0)
            {
                sender.ChangeHeroGold(-fine);
                Hero.MainHero.ChangeHeroGold(fine / 2);
                lord.ChangeHeroGold(fine - fine / 2);
            }
            ChangeRelationAction.ApplyPlayerRelation(lord, 3, affectRelatives: false);
            ChangeRelationAction.ApplyPlayerRelation(sender, -10, affectRelatives: false);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(lord, sender, -10, showQuickNotification: false);
            HiredSwordsBehavior.Blocked(sender, 60f);
            var msg = new TextObject("{=lmmi_lords_grievance_fined}{LORD} fines {SENDER} {FINE} denars before the court — half of it to you.");
            msg.SetTextVariable("LORD", lord.Name);
            msg.SetTextVariable("SENDER", sender.Name);
            msg.SetTextVariable("FINE", fine);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
            LmmiLog.Info($"Lords: {lord.Name} fines {sender.Name} {fine} on your grievance.");
        }

        /// <summary>
        /// "What do they say of me at court?" — a lord who doesn't despise you tells you straight: how the realm's lords
        /// regard you, where in the realm your name is honored or cursed, and who has been talking.
        /// </summary>
        private void AddReputationDialogs(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("lmmi_lords_reputation", "hero_main_options", "lmmi_lords_reputation_resp",
                "{=lmmi_lords_reputation}What do they say of me at court?",
                () =>
                {
                    var lord = Hero.OneToOneConversationHero;
                    return lord != null && lord.IsLord && lord.Clan != Clan.PlayerClan && lord.Clan?.Kingdom != null
                           && lord.GetRelationWithPlayer() >= 0f && LmmiSettingsProvider.EnableLordsTalk;
                }, null);
            starter.AddDialogLine("lmmi_lords_reputation_resp", "lmmi_lords_reputation_resp", "lord_pretalk",
                "{=lmmi_lords_reputation_resp}{LMMI_REPUTATION}",
                () =>
                {
                    var lord = Hero.OneToOneConversationHero;
                    var kingdom = lord?.Clan?.Kingdom;
                    if (lord == null || kingdom == null) return false;
                    MBTextManager.SetTextVariable("LMMI_REPUTATION", Reputation(lord, kingdom));
                    return true;
                }, null);
        }

        private TextObject Reputation(Hero lord, Kingdom kingdom)
        {
            var leaders = kingdom.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader != Hero.MainHero && c != Clan.PlayerClan)
                .Select(c => c.Leader).ToList();
            float avg = leaders.Count > 0 ? leaders.Average(h => h.GetRelationWithPlayer()) : 0f;
            var court = new TextObject(avg >= 20f ? "{=lmmi_lords_rep_liked}The lords of {KINGDOM} speak well of you — most would ride with you."
                : avg >= 5f ? "{=lmmi_lords_rep_respected}In {KINGDOM} your name carries some weight. Men listen when it's spoken."
                : avg >= -5f ? "{=lmmi_lords_rep_unknown}Honestly? Most at the court of {KINGDOM} couldn't put a face to your name."
                : avg >= -20f ? "{=lmmi_lords_rep_distrusted}At the court of {KINGDOM}, they don't trust you. I'd tread carefully."
                : "{=lmmi_lords_rep_hated}They hate you in {KINGDOM}. I'd not turn my back at court, were I you.");
            court.SetTextVariable("KINGDOM", kingdom.Name);

            var places = kingdom.Settlements.Where(st => st.IsTown || st.IsCastle).ToList();
            var best = places.OrderByDescending(TownStandingBehavior.Get).FirstOrDefault();
            var worst = places.OrderBy(TownStandingBehavior.Get).FirstOrDefault();
            var line = new TextObject("{=lmmi_lords_rep_line}{COURT}{BEST}{WORST}{TALK}");
            line.SetTextVariable("COURT", court);
            line.SetTextVariable("BEST", best != null && TownStandingBehavior.Band(best) >= StandingBand.Known
                ? new TextObject("{=lmmi_lords_rep_best} In {PLACE} they're fond of you.").SetTextVariable("PLACE", best.Name)
                : TextObject.GetEmpty());
            line.SetTextVariable("WORST", worst != null && TownStandingBehavior.Band(worst) <= StandingBand.Disliked
                ? new TextObject("{=lmmi_lords_rep_worst} In {PLACE}, though, they spit at your name.").SetTextVariable("PLACE", worst.Name)
                : TextObject.GetEmpty());
            var slanderer = _lastSlanderHours >= 0 && CampaignTime.Now.ToHours - _lastSlanderHours < 30 * CampaignTime.HoursInDay
                ? Hero.FindFirst(h => h.StringId == _lastSlanderer) : null;
            line.SetTextVariable("TALK", slanderer != null && slanderer != lord
                ? new TextObject("{=lmmi_lords_rep_slander} And {NAME} has been saying things about you. I'd find out why.").SetTextVariable("NAME", slanderer.Name)
                : TextObject.GetEmpty());
            return line;
        }

        private void AddRevengeDialogs(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("lmmi_lords_revenge", "hero_main_options", "lmmi_lords_revenge_who",
                "{=lmmi_lords_revenge}I need some men to deal with someone for me. Quietly.",
                () =>
                {
                    var h = Hero.OneToOneConversationHero;
                    if (h == null || !h.IsGangLeader || !LmmiSettingsProvider.EnableLordsTalk) return false;
                    if (NotableDisposition.Get(h) < Disposition.Wary) return false;
                    _enemies = Enemies(h);
                    return _enemies.Count > 0 && _revenge.Count == 0;
                }, null);
            starter.AddDialogLine("lmmi_lords_revenge_who", "lmmi_lords_revenge_who", "lmmi_lords_revenge_pick",
                "{=lmmi_lords_revenge_who}Who?", null, null);
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                starter.AddPlayerLine("lmmi_lords_revenge_pick" + k, "lmmi_lords_revenge_pick", "lmmi_lords_revenge_price",
                    "{=!}{LMMI_REVENGE_TARGET" + k + "}",
                    () =>
                    {
                        if (_enemies.Count <= k) return false;
                        var t = _enemies[k];
                        var line = new TextObject(t.IsLord
                            ? "{=lmmi_lords_revenge_target_lord}{NAME}, and whoever rides with {NAME}."
                            : "{=lmmi_lords_revenge_target_notable}{NAME}.");
                        line.SetTextVariable("NAME", t.Name);
                        MBTextManager.SetTextVariable("LMMI_REVENGE_TARGET" + k, line);
                        return true;
                    },
                    () =>
                    {
                        var broker = Hero.OneToOneConversationHero;
                        if (broker == null || _enemies.Count <= k) return;
                        _chosenEnemy = _enemies[k];
                        _revengePrice = PriceFor(_chosenEnemy, broker);
                    });
            }
            starter.AddPlayerLine("lmmi_lords_revenge_nobody", "lmmi_lords_revenge_pick", "lord_pretalk",
                "{=lmmi_lords_revenge_nobody}...On second thought, no one.", null, null);

            starter.AddDialogLine("lmmi_lords_revenge_price", "lmmi_lords_revenge_price", "lmmi_lords_revenge_deal",
                "{=lmmi_lords_revenge_price}{LMMI_REVENGE_QUOTE}",
                () =>
                {
                    if (_chosenEnemy == null) return false;
                    var quote = new TextObject(_chosenEnemy.IsLord
                        ? "{=lmmi_lords_revenge_quote_lord}{NAME}? That's no back-alley job — a lord rides with a guard. {PRICE} denars, and I'll find men who don't mind the risk. No promises."
                        : "{=lmmi_lords_revenge_quote_notable}{NAME}... I know where {NAME} drinks. {PRICE} denars, and {NAME} won't walk right for a month.");
                    quote.SetTextVariable("NAME", _chosenEnemy.Name);
                    quote.SetTextVariable("PRICE", _revengePrice);
                    MBTextManager.SetTextVariable("LMMI_REVENGE_QUOTE", quote);
                    MBTextManager.SetTextVariable("LMMI_REVENGE_PRICE", _revengePrice);
                    return true;
                }, null);
            starter.AddPlayerLine("lmmi_lords_revenge_yes", "lmmi_lords_revenge_deal", "lmmi_lords_revenge_done",
                "{=lmmi_lords_revenge_yes}Do it. [{LMMI_REVENGE_PRICE}{GOLD_ICON}]", null,
                () =>
                {
                    var broker = Hero.OneToOneConversationHero;
                    if (_chosenEnemy == null || broker == null) return;
                    Hero.MainHero.ChangeHeroGold(-_revengePrice);
                    broker.ChangeHeroGold(_revengePrice / 2);
                    _revenge.Add(new Revenge
                    {
                        TargetId = _chosenEnemy.StringId, BrokerId = broker.StringId,
                        DueHours = CampaignTime.Now.ToHours + (LmmiSettingsProvider.TestMode ? 1 : 24 + MBRandom.RandomInt(48)),
                    });
                    Hero.MainHero.AddSkillXp(DefaultSkills.Roguery, 40f);
                    LmmiLog.Info($"Lords: you pay {broker.Name} {_revengePrice} to have {_chosenEnemy.Name} dealt with.");
                }, 100,
                (out TextObject why) =>
                {
                    why = TextObject.GetEmpty();
                    if (Hero.MainHero.Gold >= _revengePrice) return true;
                    why = new TextObject("{=lmmi_cant_afford_alms}You don't have that much on you.");
                    return false;
                });
            starter.AddDialogLine("lmmi_lords_revenge_done", "lmmi_lords_revenge_done", "lord_pretalk",
                "{=lmmi_lords_revenge_done}Give it a day or three. You'll hear. Everyone will.", null, null);
            starter.AddPlayerLine("lmmi_lords_revenge_no", "lmmi_lords_revenge_deal", "lord_pretalk",
                "{=lmmi_lords_revenge_no}Too rich for me.", null, null);
        }

        // ---- Save / load: "G;senderId;hours", "A;senderId", "R;targetId;brokerId;dueHours" separated by '|' ----

        public override void SyncData(IDataStore dataStore)
        {
            string data = string.Empty;
            if (!dataStore.IsLoading)
            {
                var parts = _grievances.Select(kv => "G;" + kv.Key + ";" + kv.Value.ToString("R", CultureInfo.InvariantCulture)).ToList();
                parts.AddRange(_accused.Select(id => "A;" + id));
                parts.AddRange(_revenge.Select(r => "R;" + r.TargetId + ";" + r.BrokerId + ";" + r.DueHours.ToString("R", CultureInfo.InvariantCulture)));
                data = string.Join("|", parts);
            }
            dataStore.SyncData("lmmi_lords_v1", ref data);
            if (!dataStore.IsLoading) return;
            _grievances = new Dictionary<string, double>();
            _accused = new HashSet<string>();
            _revenge = new List<Revenge>();
            foreach (var entry in (data ?? string.Empty).Split('|'))
            {
                var p = entry.Split(';');
                if (p.Length == 3 && p[0] == "G" && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var at)) _grievances[p[1]] = at;
                else if (p.Length == 2 && p[0] == "A") _accused.Add(p[1]);
                else if (p.Length == 4 && p[0] == "R" && double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var due))
                    _revenge.Add(new Revenge { TargetId = p[1], BrokerId = p[2], DueHours = due });
            }
        }
    }
}
