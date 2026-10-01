using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Helpers;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using SandBox;
using SandBox.Conversation;
using SandBox.Conversation.MissionLogics;
using SandBox.Missions.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Conversation.Persuasion;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Hired swords. Someone who truly hates you — a lord or a notable with neither honor nor mercy to hold them back —
    /// pays sellswords to deal with you; a cunning lord at war with you may pay them just to bring you in. They find you
    /// in the street of the next town or village you walk: soldiers of the sender's culture, as good as your name
    /// deserves, who walk up and tell you who sent them. Fight them, outbid whoever paid, or give in. Lose, and it depends
    /// on the grudge: a beating and a lighter purse; or you're carried off bound to whoever paid — who holds you for
    /// ransom if at war with you, sends you off humiliated, or, if cruel enough, has you killed unless you can talk them
    /// out of it (vanilla persuasion, and hard). Stay out of towns long enough and they come for you on the road.
    /// </summary>
    public class HiredSwordsBehavior : CampaignBehaviorBase
    {
        private enum Job { Beat, Deliver, Hold }
        private enum Verdict { Hold, Humiliate, Doom }

        private sealed class Band
        {
            public string PartyId = "";
            public string SenderId = "";
            public Job Job;
            public int Fee;
            public double SpawnedHours;
            public bool HasPrisoner;
            public double CapturedHours;
            [NonSerialized] public bool Arrived;
            [NonSerialized] public Verdict? Forced;   // test buttons
        }

        /// <summary>Someone has paid: the sellswords are looking for you.</summary>
        private sealed class Contract
        {
            public string SenderId = "";
            public Job Job;
            public int Fee;
            public double MadeHours;
            public double NotBeforeHours;          // they lost you for now
            [NonSerialized] public Verdict? Forced;
            [NonSerialized] public bool Now;       // test: as soon as you're in a street
        }

        private enum VisitStep { Approaching, Fighting, Leaving }

        /// <summary>They've found you in the street.</summary>
        private sealed class Visit
        {
            public Contract Contract = null!;
            public Hero Sender = null!;
            public Settlement Settlement = null!;
            public List<Agent> Men = new List<Agent>();
            public Agent Lead = null!;
            public VisitStep Step;
            public float StepAt;
            public bool Talking, Chosen;
            public float TalkAt;
            public Action? Then;                  // what happens once the screen is dark
            public float ThenAt = -1f;
            public Agent? Player;                 // you, when they walked up (Agent.Main is cleared once you're down)
            public bool Down;                     // you were knocked out
            public bool Departing;                // bought off: walking away down the street
        }

        /// <summary>Knocked out in the street by the men paid to beat you: robbed, told once you come to.</summary>
        private sealed class KoBeating
        {
            public Hero Sender = null!;
            public Settlement Settlement = null!;
            public int Taken;
        }

        private enum Refusal { None, Personal, Ransom }

        private sealed class Judgment
        {
            public Hero Sender = null!;
            public Verdict Verdict;
            public bool Opened;
            public bool Seen;       // its opening line was spoken
            public bool Spared;
        }

        private const int MaxTargetPartySize = 100;
        private const float SenderCooldownDays = 30f;
        private const float AnyCooldownDays = 7f;
        private const float BandLifetimeDays = 3f;
        private const float DeliveryTimeoutDays = 6f;
        private const float LordRange = 100f;
        private const float NotableRange = 60f;
        private const float ArrivalDistance = 4f;
        private const float ContractDays = 10f;
        private const float RoadAfterDays = 3f;
        private const float ApproachTimeout = 90f;
        private const float DepartTimeout = 40f;
        private const float DepartFadeDistance = 45f;

        [NonSerialized] private Dictionary<string, double> _readyAtHours = new Dictionary<string, double>();
        [NonSerialized] private Dictionary<string, Band> _bands = new Dictionary<string, Band>();
        [NonSerialized] private string? _pendingMenu;
        [NonSerialized] private Band? _menuBand;
        [NonSerialized] private int _taken;
        [NonSerialized] private Judgment? _judgment;
        [NonSerialized] private readonly List<Action> _afterTalk = new List<Action>();
        [NonSerialized] private readonly HashSet<string> _disband = new HashSet<string>();
        [NonSerialized] private bool _releasing;
        [NonSerialized] private readonly NativePersuasion _plea = new NativePersuasion("hired_plea");
        [NonSerialized] private Contract? _contract;
        [NonSerialized] private Visit? _visit;
        [NonSerialized] private Mission? _mission;
        [NonSerialized] private float _sceneTime, _visitAt;
        [NonSerialized] private readonly List<Action> _afterSceneTalk = new List<Action>();
        [NonSerialized] private bool _fightActive;
        [NonSerialized] private bool _fadedOut, _needFadeIn;
        [NonSerialized] private Contract? _takenContract;
        [NonSerialized] private Settlement? _takenIn;
        [NonSerialized] private KoBeating? _koBeating;
        [NonSerialized] private float _koEndAt = -1f;
        // The price to buy them off, fixed when they greet you (it's a share of your purse), and whether they'll take it.
        [NonSerialized] private int _bribe;
        [NonSerialized] private Refusal _refusal;

        /// <summary>They're in the scene with you (street events and arrivals wait).</summary>
        public static bool InScene => Instance?._visit != null;

        /// <summary>Your fight with them is on (vanilla would refuse Tab; see StreetFightLeavePatch).</summary>
        internal static bool FightActive => Instance?._fightActive == true;

        private static HiredSwordsBehavior? Instance => Campaign.Current?.GetCampaignBehavior<HiredSwordsBehavior>();

        public static void ClearCooldowns() => Instance?._readyAtHours.Clear();

        /// <summary>A band is out looking for you (Test mode stops making parties ignore you meanwhile).</summary>
        public static bool Hunting => Instance?._bands.Values.Any(b => !b.HasPrisoner) == true;

        /// <summary>Whoever has paid sellswords to find you right now (a contract out, or a band on the road), if anyone.</summary>
        internal static Hero? PriceOnYourHead
        {
            get
            {
                var self = Instance;
                if (self == null) return null;
                string? id = self._contract?.SenderId ?? self._bands.Values.FirstOrDefault(b => !b.HasPrisoner)?.SenderId;
                return id == null ? null : Hero.FindFirst(h => h.StringId == id && h.IsAlive);
            }
        }

        /// <summary>In an army, you're not alone on the road or in the street: the sellswords wait.</summary>
        private static bool InArmy => MobileParty.MainParty?.Army != null;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnHeroPrisonerTaken);
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnHeroPrisonerReleased);
            CampaignEvents.MissionTickEvent.AddNonSerializedListener(this, OnMissionTick);
            CampaignEvents.ConversationEnded.AddNonSerializedListener(this, OnConversationEnded);
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, OnMissionEnded);
            CampaignEvents.GameMenuOpened.AddNonSerializedListener(this, OnGameMenuOpened);
        }

        // ---- Who would pay to have you dealt with ----

        private void OnDailyTick()
        {
            try
            {
                if (!LmmiSettingsProvider.EnableHiredSwords || !LmmiSettingsProvider.EnableNotableDisposition) return;
                if (InArmy) return;   // no new contracts, no road bands while you march with an army
                double now = CampaignTime.Now.ToHours;

                // Already paid for: stay out of the towns long enough and they come for you on the road.
                if (_contract != null)
                {
                    var c = _contract;
                    var sender = Hero.FindFirst(h => h.StringId == c.SenderId);
                    if (sender == null || !sender.IsAlive || now - c.MadeHours > ContractDays * CampaignTime.HoursInDay)
                    {
                        LmmiLog.Info("Hired swords: the contract lapses.");
                        _contract = null;
                        return;
                    }
                    if (now - c.MadeHours > RoadAfterDays * CampaignTime.HoursInDay && Mission.Current == null && CanBeAmbushed(out _)
                        && (c.Forced != null || WithinReach(sender, MobileParty.MainParty.Position))
                        && SpawnBand(sender, c.Job, c.Fee, engage: true, c.Forced) != null)
                        _contract = null;
                    return;
                }

                if (!Ready("any")) return;
                foreach (var c in Candidates(LordRange, NotableRange).OrderBy(_ => MBRandom.RandomFloat))
                {
                    if (!Ready(c.Sender.StringId)) continue;
                    float roll = MBRandom.RandomFloat;
                    LmmiLog.Info($"Hired swords roll: {c.Sender.Name} ({c.Job}, relation {c.Sender.GetRelationWithPlayer():0}) {roll:0.00} vs {c.Chance:0.00}.");
                    if (roll > c.Chance) continue;
                    if (Hire(c.Sender, c.Job, test: false)) return;   // one contract at a time
                }
            }
            catch (Exception ex) { LmmiLog.Error("HiredSwordsBehavior.OnDailyTick threw", ex); }
        }

        /// <summary>
        /// Lords and notables close enough to know where you are. A grudge (relation -25 or worse) sends men to beat you;
        /// a deep one (-50) to bring you in. A cunning lord at war with you may pay to have you brought in as a prisoner.
        /// The honorable fight their own battles, and the merciful don't hire knives.
        /// </summary>
        private static List<(Hero Sender, Job Job, float Chance)> Candidates(float lordRange, float notableRange)
        {
            var list = new List<(Hero, Job, float)>();
            var here = MobileParty.MainParty.Position;
            foreach (var hero in Hero.AllAliveHeroes)
            {
                if (hero == Hero.MainHero || hero.IsChild || hero.IsPrisoner || hero.Clan == Clan.PlayerClan) continue;
                bool lord = hero.IsLord && hero.Clan != null && !hero.Clan.IsBanditFaction && hero.IsActive;
                if (!lord && !hero.IsNotable) continue;
                if (hero.GetTraitLevel(DefaultTraits.Honor) >= 1) continue;
                var at = hero.GetCampaignPosition();
                if (!at.IsValid() && hero.HomeSettlement != null) at = hero.HomeSettlement.GatePosition;
                if (!at.IsValid() || at.Distance(here) > (lord ? lordRange : notableRange)) continue;

                float relation = hero.GetRelationWithPlayer();
                bool atWar = lord && hero.MapFaction != null && Hero.MainHero.MapFaction != null
                             && FactionManager.IsAtWarAgainstFaction(hero.MapFaction, Hero.MainHero.MapFaction);
                if (relation <= -25f && hero.GetTraitLevel(DefaultTraits.Mercy) < 1)
                {
                    float chance = 0.03f + 0.02f * ((-relation - 25f) / 25f);
                    if (hero.GetTraitLevel(DefaultTraits.Mercy) <= -1) chance *= 1.5f;
                    list.Add((hero, relation <= -50f ? Job.Deliver : Job.Beat, Math.Min(0.15f, chance)));
                }
                else if (atWar && hero.GetTraitLevel(DefaultTraits.Calculating) >= 1)
                    list.Add((hero, Job.Hold, 0.02f));
            }
            return list;
        }

        /// <summary>A small party alone on the road — never a real army, a party in a town, or one at sea.</summary>
        private static bool CanBeAmbushed(out string why)
        {
            var main = MobileParty.MainParty;
            why = "";
            if (main == null || Hero.MainHero.IsPrisoner || !main.IsActive) why = "You're not free on the map.";
            else if (main.CurrentSettlement != null) why = "Leave the settlement first.";
            else if (main.MapEvent != null || PlayerEncounter.Current != null) why = "Finish the current encounter first.";
            else if (main.Army != null) why = "Not while you're in an army.";
            else if (main.IsCurrentlyAtSea) why = "Not at sea.";
            else if (main.MemberRoster.TotalHealthyCount > MaxTargetPartySize) why = $"Your party is too big (over {MaxTargetPartySize} fit men).";
            return why.Length == 0;
        }

        // ---- The band ----

        /// <summary>How many come for you in the street.</summary>
        private static int StreetCount()
        {
            int clanTier = Clan.PlayerClan?.Tier ?? 0;
            return clanTier <= 1 ? 3 : clanTier <= 3 ? 4 : clanTier == 4 ? 5 : 6;
        }

        private static CultureObject? CultureFor(Hero sender) =>
            new[] { sender.Culture, sender.HomeSettlement?.Culture, Hero.MainHero.Culture }.FirstOrDefault(c => c?.BasicTroop != null);

        /// <summary>Someone pays: the sellswords take the job, and look for you in the streets.</summary>
        private bool Hire(Hero sender, Job job, bool test, Verdict? forced = null)
        {
            int tier = TroopTier();
            int fee = StreetCount() * (60 + 60 * tier);
            if (!test && sender.Gold < fee)
            {
                LmmiLog.Info($"Hired swords: {sender.Name} can't afford them ({sender.Gold} of {fee}).");
                return false;
            }
            if (!test) sender.ChangeHeroGold(-fee);
            _contract = new Contract { SenderId = sender.StringId, Job = job, Fee = fee, MadeHours = CampaignTime.Now.ToHours, Forced = forced, Now = test };
            var warning = Warning(sender);
            if (warning != null) InformationManager.DisplayMessage(new InformationMessage(warning.ToString(), Colors.Yellow));
            SetCooldown(sender.StringId, SenderCooldownDays);
            SetCooldown("any", AnyCooldownDays);
            LmmiLog.Info($"Hired swords: {sender.Name} ({(sender.IsLord ? "lord" : "notable")}, relation {sender.GetRelationWithPlayer():0}) paid {fee} "
                         + $"to have you {job switch { Job.Beat => "beaten", Job.Deliver => "brought to them", _ => "taken prisoner" }}"
                         + $"{(forced != null ? $" (test: {forced})" : "")} — they'll find you in the streets.");
            return true;
        }

        /// <summary>
        /// Someone may tip you off: a companion with an ear for the taverns (Roguery 60+), a notable nearby who counts you a
        /// friend, or your own ear (Roguery 60+). Nobody tells you it's the hired swords at the next corner, only that a
        /// price is on your head, and whose.
        /// </summary>
        private static TextObject? Warning(Hero sender)
        {
            var companion = MobileParty.MainParty.MemberRoster.GetTroopRoster()
                .Where(e => e.Character != null && e.Character.IsHero && e.Character.HeroObject != Hero.MainHero)
                .Select(e => e.Character.HeroObject).FirstOrDefault(h => h.GetSkillValue(DefaultSkills.Roguery) >= 60);
            TextObject line;
            if (companion != null)
                line = new TextObject("{=lmmi_hired_warn_companion}{WHO} pulls you aside: word in the taverns is that {SENDER} has put a price on your head.")
                    .SetTextVariable("WHO", companion.Name);
            else
            {
                var here = MobileParty.MainParty.Position;
                var friend = Settlement.All.Where(st => st.IsTown && st.GatePosition.Distance(here) < 80f)
                    .SelectMany(st => st.Notables).FirstOrDefault(n => n.IsAlive && NotableDisposition.Get(n) == Disposition.Trusted);
                if (friend != null)
                    line = new TextObject("{=lmmi_hired_warn_friend}A message from {WHO}: be careful — {SENDER} has hired swords to find you.")
                        .SetTextVariable("WHO", friend.Name);
                else if (Hero.MainHero.GetSkillValue(DefaultSkills.Roguery) >= 60)
                    line = new TextObject("{=lmmi_hired_warn_self}A whisper you weren't meant to hear: {SENDER} has put a price on your head.");
                else return null;
            }
            line.SetTextVariable("SENDER", sender.Name);
            LmmiLog.Info($"Hired swords: you're warned about {sender.Name}'s contract.");
            return line;
        }

        /// <summary>A liege made an example of them (a grievance): no contracts from them for a while, and any pending one is off.</summary>
        public static void Blocked(Hero sender, float days)
        {
            var self = Instance;
            if (self == null) return;
            self.SetCooldown(sender.StringId, days);
            if (self._contract?.SenderId == sender.StringId)
            {
                self._contract = null;
                LmmiLog.Info($"Hired swords: {sender.Name}'s contract is off.");
            }
        }

        /// <summary>How far the sellswords will go looking: they know the region, not the continent.</summary>
        private const float HuntRange = 200f;

        private static bool WithinReach(Hero sender, CampaignVec2 at)
        {
            var from = WhereIs(sender) ?? sender.HomeSettlement?.GatePosition;
            return from != null && from.Value.Distance(at) <= HuntRange;
        }

        /// <summary>On the road: a party of them, sized to yours, riding for you (or, with you already theirs, just a party).</summary>
        private MobileParty? SpawnBand(Hero sender, Job job, int fee, bool engage, Verdict? forced, CampaignVec2? at = null)
        {
            var main = MobileParty.MainParty;
            if (engage && !CanBeAmbushed(out _)) return null;
            var clan = Clan.BanditFactions.FirstOrDefault(c => c.StringId == "looters") ?? Clan.BanditFactions.FirstOrDefault();
            if (clan?.DefaultPartyTemplate == null) { LmmiLog.Warning("Hired swords: no bandit clan to borrow."); return null; }
            int tier = TroopTier();
            var culture = CultureFor(sender);
            if (culture == null) return null;
            var (best, lesser) = TroopsOf(culture, tier);
            if (best.Count == 0) return null;

            int size = engage ? Math.Max(8, Math.Min(90, (int)(main.MemberRoster.TotalHealthyCount * 0.9f) + 4)) : StreetCount() + 4;
            var home = sender.HomeSettlement ?? sender.CurrentSettlement
                       ?? SettlementHelper.FindNearestSettlementToMobileParty(main, MobileParty.NavigationType.Default, s => s.IsTown || s.IsCastle);
            var position = NavigationHelper.FindPointAroundPosition(at ?? main.Position, MobileParty.NavigationType.Default, 5f, 3f);
            var band = BanditPartyComponent.CreateLooterParty("lmmi_hired_" + sender.StringId + "_" + MBRandom.RandomInt(1000000),
                clan, home, false, clan.DefaultPartyTemplate, position);
            if (band == null) return null;

            // Real soldiers, as good as your name deserves: mostly the tier asked for, a few a step below.
            band.MemberRoster.Clear();
            for (int i = 0; i < size; i++)
            {
                var pool = lesser.Count > 0 && MBRandom.RandomFloat < 0.3f ? lesser : best;
                band.MemberRoster.AddToCounts(pool[MBRandom.RandomInt(pool.Count)], 1);
            }
            band.Party.SetCustomName(new TextObject("{=lmmi_hired_name}Hired Swords"));
            band.ActualClan = clan;
            band.Aggressiveness = 1f;
            band.InitializePartyTrade(0);
            band.ItemRoster.AddToCounts(DefaultItems.Grain, 6);
            band.Party.SetVisualAsDirty();
            band.Ai.SetDoNotMakeNewDecisions(true);
            // A paid job, like a quest's party: nobody else on the map hunts them down (vanilla quests do the same:
            // IgnoreByOtherPartiesTill keeps AI parties from targeting them; the player can still meet them).
            band.IgnoreByOtherPartiesTill(CampaignTime.DaysFromNow((engage ? BandLifetimeDays : DeliveryTimeoutDays) + 1f));
            band.SetPartyUsedByQuest(true);
            _bands[band.StringId] = new Band
            {
                PartyId = band.StringId, SenderId = sender.StringId, Job = job, Fee = fee,
                SpawnedHours = CampaignTime.Now.ToHours, Forced = forced,
            };
            if (!engage)
            {
                band.SetMoveModeHold();
                LmmiLog.Info($"Hired swords: {size} of them ride out with you as their prisoner.");
                return band;
            }

            SetPartyAiAction.GetActionForEngagingParty(band, main, MobileParty.NavigationType.Default, false);
            if (LmmiSettingsProvider.TestMode) main.IgnoreByOtherPartiesTill(CampaignTime.Now);   // Test mode hides you from parties
            InformationManager.DisplayMessage(new InformationMessage(new TextObject(
                "{=lmmi_hired_telegraph}Riders on your trail, closing fast — armed like soldiers, wearing no one's colors. Sellswords, and they're after you.").ToString(), Colors.Red));
            LmmiLog.Info($"Hired swords: you've kept out of the towns, so {sender.Name}'s men come for you on the road — {size} tier-{tier} {culture.Name} soldiers ({job}).");
            return band;
        }

        /// <summary>Tier 2 for a nobody, up to tier 5 for a great house.</summary>
        private static int TroopTier()
        {
            int clanTier = Clan.PlayerClan?.Tier ?? 0;
            return clanTier <= 1 ? 2 : clanTier <= 3 ? 3 : clanTier == 4 ? 4 : 5;
        }

        /// <summary>The culture's regulars (both trees): those nearest the tier wanted, and those a step below.</summary>
        internal static (List<CharacterObject> Best, List<CharacterObject> Lesser) TroopsOf(CultureObject culture, int tier)
        {
            var seen = new HashSet<CharacterObject>();
            var queue = new Queue<CharacterObject>();
            foreach (var root in new[] { culture.BasicTroop, culture.EliteBasicTroop })
                if (root != null) queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                if (!seen.Add(t)) continue;
                foreach (var u in t.UpgradeTargets ?? Array.Empty<CharacterObject>())
                    if (u != null) queue.Enqueue(u);
            }
            var all = seen.Where(t => !t.IsHero).ToList();
            if (all.Count == 0) return (new List<CharacterObject>(), new List<CharacterObject>());
            int gap = all.Min(t => Math.Abs(t.Tier - tier));
            var best = all.Where(t => Math.Abs(t.Tier - tier) == gap).ToList();
            int top = best.Max(t => t.Tier);
            var lesser = all.Where(t => t.Tier == top - 1).ToList();
            return (best, lesser);
        }

        private Band? BandOf(MobileParty? party) =>
            party != null && _bands.TryGetValue(party.StringId, out var b) ? b : null;

        private static MobileParty? PartyOf(Band band) => MobileParty.All.FirstOrDefault(p => p.StringId == band.PartyId);

        private static Hero? SenderOf(Band band) => Hero.FindFirst(h => h.StringId == band.SenderId);

        private void Disband(Band band, string why)
        {
            _bands.Remove(band.PartyId);
            var party = PartyOf(band);
            if (party != null && party.IsActive)
            {
                if (party.MapEvent != null) { _disband.Add(party.StringId); return; }
                DestroyPartyAction.Apply(null, party);
            }
            LmmiLog.Info($"Hired swords: the band disbands ({why}).");
        }

        private void OnHourlyTick()
        {
            try
            {
                double now = CampaignTime.Now.ToHours;
                foreach (var band in _bands.Values.ToList())
                {
                    var party = PartyOf(band);
                    if (party == null || !party.IsActive) { _bands.Remove(band.PartyId); continue; }
                    if (band.HasPrisoner)
                    {
                        if (Hero.MainHero.PartyBelongedToAsPrisoner != party.Party) { Disband(band, "they lost their prisoner"); continue; }
                        if (now - band.CapturedHours > DeliveryTimeoutDays * CampaignTime.HoursInDay) { Abandon(band); continue; }
                        if (band.Job != Job.Beat) Steer(band, party);
                        continue;
                    }
                    if (now - band.SpawnedHours > BandLifetimeDays * CampaignTime.HoursInDay && party.MapEvent == null)
                        Disband(band, "gave up the chase");
                    else if (InArmy && party.MapEvent == null && PlayerEncounter.Current == null)
                    {
                        // You rode off with an army: they won't take on a host. The job stands; they'll look for you later.
                        _contract ??= new Contract { SenderId = band.SenderId, Job = band.Job, Fee = band.Fee, MadeHours = now };
                        Disband(band, "you joined an army");
                    }
                }
                foreach (var id in _disband.ToList())
                {
                    var party = MobileParty.All.FirstOrDefault(p => p.StringId == id);
                    if (party == null || !party.IsActive) { _disband.Remove(id); continue; }
                    if (party.MapEvent != null) continue;
                    DestroyPartyAction.Apply(null, party);
                    _disband.Remove(id);
                }
            }
            catch (Exception ex) { LmmiLog.Error("HiredSwordsBehavior.OnHourlyTick threw", ex); }
        }

        // ---- They've got you ----

        private void OnHeroPrisonerTaken(PartyBase capturer, Hero prisoner)
        {
            try
            {
                if (prisoner != Hero.MainHero) return;
                var band = BandOf(capturer?.MobileParty);
                if (band == null) return;
                band.HasPrisoner = true;
                band.CapturedHours = CampaignTime.Now.ToHours;
                var party = capturer!.MobileParty!;
                LmmiLog.Info($"Hired swords: they have you ({band.Job}).");
                if (band.Job == Job.Beat)
                {
                    Rob(band, 0.25f);
                    _menuBand = band;
                    _pendingMenu = "lmmi_hired_beaten";
                    return;
                }
                // Nobody gets in the way of a paid delivery.
                party.IgnoreByOtherPartiesTill(CampaignTime.DaysFromNow(DeliveryTimeoutDays + 1f));
                party.Ai.SetDoNotMakeNewDecisions(true);
                Steer(band, party);
            }
            catch (Exception ex) { LmmiLog.Error("HiredSwordsBehavior.OnHeroPrisonerTaken threw", ex); }
        }

        private void OnHeroPrisonerReleased(Hero hero, PartyBase party, IFaction faction, EndCaptivityDetail detail, bool showNotification)
        {
            try
            {
                if (hero != Hero.MainHero || _releasing) return;
                var band = BandOf(party?.MobileParty);
                if (band == null) return;
                if (_menuBand == band) { _menuBand = null; _pendingMenu = null; }
                Disband(band, $"you got away ({detail})");
            }
            catch (Exception ex) { LmmiLog.Error("HiredSwordsBehavior.OnHeroPrisonerReleased threw", ex); }
        }

        /// <summary>Where whoever paid is now: their party, or the gate of wherever they are.</summary>
        private static CampaignVec2? WhereIs(Hero sender)
        {
            if (!sender.IsAlive || sender.IsPrisoner) return null;
            var at = sender.GetCampaignPosition();
            if (!at.IsValid() && sender.HomeSettlement != null && sender.IsNotable) at = sender.HomeSettlement.GatePosition;
            return at.IsValid() ? at : (CampaignVec2?)null;
        }

        private void Steer(Band band, MobileParty party)
        {
            var sender = SenderOf(band);
            var to = sender != null ? WhereIs(sender) : null;
            if (to == null) { Abandon(band); return; }
            // A lord on the move: keep up with their party. Otherwise, to the gate of wherever they are.
            var theirs = sender!.PartyBelongedTo;
            if (theirs != null && theirs.IsActive && theirs.CurrentSettlement == null && !theirs.IsCurrentlyAtSea)
                party.SetMoveEscortParty(theirs, MobileParty.NavigationType.Default, false);
            else party.SetMoveGoToPoint(to.Value, MobileParty.NavigationType.Default);
        }

        /// <summary>Whoever paid is dead, captured or out of reach: the band takes your purse and cuts you loose.</summary>
        private void Abandon(Band band)
        {
            if (_pendingMenu != null) return;
            Rob(band, 0.3f);
            _menuBand = band;
            _pendingMenu = "lmmi_hired_dumped";
            LmmiLog.Info("Hired swords: nobody to deliver you to — they cut you loose.");
        }

        private void Rob(Band band, float share)
        {
            _taken = Math.Min(Hero.MainHero.Gold, Math.Min(5000, (int)(Hero.MainHero.Gold * share)));
            if (_taken > 0) Hero.MainHero.ChangeHeroGold(-_taken);
        }

        /// <summary>Called every frame (SubModule): menus, the delivery, and the judgment run even with time paused.</summary>
        internal static void OnApplicationTick()
        {
            var self = Instance;
            if (self == null) return;
            try { self.Poll(); }
            catch (Exception ex) { LmmiLog.Error("HiredSwordsBehavior.Poll threw", ex); }
        }

        private void Poll()
        {
            if (!(Game.Current?.GameStateManager?.ActiveState is MapState)) return;
            if (Campaign.Current.ConversationManager?.IsConversationInProgress == true) return;

            foreach (var id in _disband.ToList())
            {
                var gone = MobileParty.All.FirstOrDefault(p => p.StringId == id);
                if (gone == null || !gone.IsActive) { _disband.Remove(id); continue; }
                if (gone.MapEvent != null || PlayerEncounter.Current != null) continue;
                DestroyPartyAction.Apply(null, gone);
                _disband.Remove(id);
            }

            if (_judgment != null && _judgment.Seen && _afterTalk.Count == 0)
            {
                // The conversation closed without a verdict.
                var sender = _judgment.Sender;
                _judgment = null;
                LmmiLog.Info("Hired swords: the judgment ended without a verdict.");
                SendOff(sender, 0.2f);
                return;
            }

            if (_afterTalk.Count > 0)
            {
                var actions = _afterTalk.ToList();
                _afterTalk.Clear();
                foreach (var a in actions)
                {
                    try { a(); }
                    catch (Exception ex) { LmmiLog.Error("Hired swords: after-conversation action threw", ex); }
                }
                return;
            }

            // Arrived: in front of whoever paid.
            if (_pendingMenu == null && Hero.MainHero.IsPrisoner)
            {
                foreach (var band in _bands.Values)
                {
                    if (!band.HasPrisoner || band.Job == Job.Beat || band.Arrived) continue;
                    var party = PartyOf(band);
                    var sender = SenderOf(band);
                    if (party == null || sender == null || Hero.MainHero.PartyBelongedToAsPrisoner != party.Party) continue;
                    var to = WhereIs(sender);
                    if (to == null || party.Position.Distance(to.Value) > ArrivalDistance) continue;
                    party.SetMoveModeHold();
                    band.Arrived = true;
                    _menuBand = band;
                    _pendingMenu = "lmmi_hired_delivered";
                    LmmiLog.Info($"Hired swords: delivered to {sender.Name}.");
                    break;
                }
            }

            if (_pendingMenu != null && Hero.MainHero.IsPrisoner && PlayerEncounter.Current == null
                && Campaign.Current.CurrentMenuContext != null)
            {
                var menu = _pendingMenu;
                _pendingMenu = null;
                if (Campaign.Current.CurrentMenuContext.GameMenu?.StringId != menu) GameMenu.SwitchToMenu(menu);
                return;
            }

            if (_judgment != null && !_judgment.Opened && !Hero.MainHero.IsPrisoner && PlayerEncounter.Current == null
                && Campaign.Current.CurrentMenuContext == null)
            {
                _judgment.Opened = true;
                var sender = _judgment.Sender;
                LmmiLog.Info($"Hired swords: judgment before {sender.Name} ({_judgment.Verdict}).");
                CampaignMapConversation.OpenConversation(new ConversationCharacterData(CharacterObject.PlayerCharacter, PartyBase.MainParty),
                    new ConversationCharacterData(sender.CharacterObject, sender.PartyBelongedTo?.LeaderHero == sender ? sender.PartyBelongedTo.Party : null));
            }
        }

        // ---- In the street ----

        private void OnMissionTick(float dt)
        {
            try
            {
                var mission = Mission.Current;
                if (mission == null) return;
                if (mission != _mission)
                {
                    _mission = mission;
                    _sceneTime = 0f;
                    _visitAt = 20f + MBRandom.RandomFloat * 40f;
                    _visit = null;
                    _afterSceneTalk.Clear();
                    _fightActive = false;
                    _koEndAt = -1f;
                    HiredBeatDownPatch.Guarded = null;
                }
                _sceneTime += dt;

                // Knocked out with them on you: they win (vanilla's "you slip into unconsciousness" won't do).
                if (_visit != null && !_visit.Down && PlayerDown(_visit)) KnockedOut(_visit);
                if (_koEndAt >= 0f && _sceneTime >= _koEndAt)
                {
                    // Vanilla ends the scene 5 s after you go down (LeaveMissionLogic); if nothing did, we do.
                    _koEndAt = -1f;
                    if (!mission.MissionEnded && !mission.IsMissionEnding) mission.EndMission();
                }

                if (_afterSceneTalk.Count > 0 && mission.Mode != MissionMode.Conversation
                    && Campaign.Current?.ConversationManager?.IsConversationInProgress != true)
                {
                    var actions = _afterSceneTalk.ToList();
                    _afterSceneTalk.Clear();
                    foreach (var a in actions)
                    {
                        try { a(); }
                        catch (Exception ex) { LmmiLog.Error("Hired swords: after-conversation action threw", ex); }
                    }
                }

                var main = Agent.Main;
                if (main == null || !main.IsActive()) return;
                if (_visit != null) { UpdateVisit(mission, _visit); return; }

                var c = _contract;
                if (c == null || InArmy) return;
                if (!c.Now)
                {
                    if (!LmmiSettingsProvider.EnableHiredSwords || _sceneTime < _visitAt || CampaignTime.Now.ToHours < c.NotBeforeHours) return;
                }
                var location = CampaignMission.Current?.Location?.StringId;
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null || (location != "center" && location != "village_center")) return;
                if (mission.Mode == MissionMode.Conversation || mission.Mode == MissionMode.Battle) return;
                if (mission.GetMissionBehavior<MissionFightHandler>()?.IsThereActiveFight() == true) return;
                if (StreetEventsBehavior.IsBusy || FeastBehavior.IsBusy || ArrivalScenesBehavior.IsBusy) return;
                if (!Stage(mission, c, settlement)) _visitAt = _sceneTime + 30f;
            }
            catch (Exception ex)
            {
                LmmiLog.Error("HiredSwordsBehavior.OnMissionTick threw", ex);
                _visit = null;
                HiredBeatDownPatch.Guarded = null;
            }
        }

        /// <summary>They walk in from down the street — soldiers, in no one's colors — and come straight for you.</summary>
        private bool Stage(Mission mission, Contract c, Settlement settlement)
        {
            var main = Agent.Main;
            var sender = Hero.FindFirst(h => h.StringId == c.SenderId);
            if (sender == null || !sender.IsAlive) { _contract = null; return false; }
            if (!c.Now && !WithinReach(sender, settlement.GatePosition)) return false;   // too far from home for them
            var culture = CultureFor(sender);
            if (culture == null) { _contract = null; return false; }
            var (best, lesser) = TroopsOf(culture, TroopTier());
            if (best.Count == 0) { _contract = null; return false; }

            // From someone standing 25-45 m off: where they walk in from.
            var from = mission.Agents
                .Where(a => a.IsActive() && a.IsHuman && a != main && a.Position.Distance(main.Position) > 25f && a.Position.Distance(main.Position) < 45f)
                .OrderBy(_ => MBRandom.RandomFloat).FirstOrDefault();
            if (from == null) return false;
            var toYou = (main.Position - from.Position).AsVec2;
            toYou = toYou.LengthSquared < 0.01f ? Vec2.Forward : toYou.Normalized();
            var side = new Vec2(-toYou.y, toYou.x);

            var men = new List<Agent>();
            int n = StreetCount();
            for (int i = 0; i < n; i++)
            {
                var pool = lesser.Count > 0 && MBRandom.RandomFloat < 0.3f ? lesser : best;
                var at2 = from.Position.AsVec2 + side * ((i - (n - 1) / 2f) * 0.9f) - toYou * (i % 2) * 0.8f;
                var at = new Vec3(at2.x, at2.y, from.Position.z);
                at.z = mission.Scene.GetGroundHeightAtPosition(at + new Vec3(0f, 0f, 1.5f));
                var man = SpawnMan(mission, pool[MBRandom.RandomInt(pool.Count)], at, toYou.RotationInRadians);
                if (man != null) men.Add(man);
            }
            if (men.Count < 2)
            {
                foreach (var m in men) m.FadeOut(true, true);
                return false;
            }

            var v = new Visit { Contract = c, Sender = sender, Settlement = settlement, Men = men, Lead = men[0], Step = VisitStep.Approaching, StepAt = _sceneTime, Player = main };
            _visit = v;
            c.Now = false;
            StreetEventsBehavior.Direct(v.Lead)?.Follow(main, 2f, run: false);
            foreach (var m in men.Skip(1)) StreetEventsBehavior.Direct(m)?.Follow(v.Lead, 1.6f, run: false);
            LmmiLog.Info($"Hired swords: {men.Count} of {sender.Name}'s men walk up to you in {settlement.Name} ({c.Job}): "
                         + string.Join(", ", men.Select(m => m.Name)) + ".");
            return true;
        }

        /// <summary>Brought into the scene as vanilla brings in its own extras (MissionAgentHandler), armed, on foot, no bows.</summary>
        private static Agent? SpawnMan(Mission mission, CharacterObject character, Vec3 at, float facing) =>
            SceneSpawner.Spawn(mission, character, at, facing, civilian: false);

        private void UpdateVisit(Mission mission, Visit v)
        {
            var main = Agent.Main;
            float inStep = _sceneTime - v.StepAt;

            if (v.ThenAt >= 0f && _sceneTime >= v.ThenAt)
            {
                var then = v.Then;
                v.Then = null;
                v.ThenAt = -1f;
                then?.Invoke();
                return;
            }

            switch (v.Step)
            {
                case VisitStep.Approaching:
                {
                    if (v.Talking && mission.Mode != MissionMode.Conversation && _sceneTime - v.TalkAt > 5f) v.Talking = false;
                    if (!v.Lead.IsActive()) { EndVisit(v, "their leader is gone"); return; }
                    if (!v.Talking && inStep > ApproachTimeout)
                    {
                        v.Contract.NotBeforeHours = CampaignTime.Now.ToHours + 12.0;
                        EndVisit(v, "they lost you in the crowd");
                        return;
                    }
                    if (v.Talking || v.ThenAt >= 0f || _afterSceneTalk.Count > 0) return;
                    if (mission.Mode == MissionMode.Conversation || mission.Mode == MissionMode.Battle) return;
                    if (v.Lead.Position.Distance(main.Position) > 3.2f) return;
                    v.Talking = true;
                    v.Chosen = false;
                    v.TalkAt = _sceneTime;
                    mission.GetMissionBehavior<MissionConversationLogic>()?.StartConversation(v.Lead, setActionsInstantly: false);
                    return;
                }

                case VisitStep.Fighting:
                    // Blows can't take your last breath (HiredBeatDownPatch); down on one knee, it's over.
                    if (HiredBeatDownPatch.Guarded != main && !LmmiSettingsProvider.TestImmortal) HiredBeatDownPatch.Guarded = main;
                    if (!LmmiSettingsProvider.TestImmortal && main.Health <= Math.Max(6f, main.HealthLimit * 0.12f))
                    {
                        LmmiLog.Info("Hired swords: they beat you down.");
                        mission.GetMissionBehavior<MissionFightHandler>()?.EndFight();
                    }
                    return;

                case VisitStep.Leaving:
                    if (v.Departing) Depart(v, inStep);
                    return;
            }
        }

        /// <summary>Paid off and walking away: each fades once well out of your way; any still about after a while, too.</summary>
        private void Depart(Visit v, float inStep)
        {
            var main = Agent.Main;
            bool late = inStep > DepartTimeout;
            foreach (var m in v.Men)
            {
                if (!m.IsActive()) continue;
                if (!late && main != null && m.Position.Distance(main.Position) < DepartFadeDistance) continue;
                StreetEventsBehavior.Release(m);
                m.FadeOut(true, true);
            }
            if (v.Men.All(m => !m.IsActive()))
            {
                if (_visit == v) _visit = null;
                LmmiLog.Info($"Hired swords: the street visit ends (bought off; gone after {inStep:0} s).");
            }
        }

        /// <summary>You (the agent they walked up to) are down: unconscious or dead.</summary>
        private static bool PlayerDown(Visit v)
        {
            var p = v.Player;
            if (p == null) return false;
            return p.State == AgentState.Unconscious || p.State == AgentState.Killed;
        }

        /// <summary>
        /// Knocked out — a heavy blow, a bad fall — while they're on you: that's their job done. A beating means your purse
        /// (told once you come to); a delivery means you wake across a saddle (the "lmmi_hired_taken" menu). Someone else's
        /// trouble (the watch, resisting arrest) puts you down instead: they slip away and try another day.
        /// </summary>
        private void KnockedOut(Visit v)
        {
            v.Down = true;
            bool ours = !v.Departing && (_fightActive || !StreetEventsBehavior.IsBusy);
            _fightActive = false;
            HiredBeatDownPatch.Guarded = null;
            v.Then = null;
            v.ThenAt = -1f;
            foreach (var m in v.Men) StreetEventsBehavior.Release(m);
            if (_visit == v) _visit = null;

            if (v.Departing)
            {
                LmmiLog.Info("Hired swords: knocked out after you'd paid them off — not their doing.");
                return;
            }
            if (!ours)
            {
                v.Contract.NotBeforeHours = CampaignTime.Now.ToHours + 24.0;
                LmmiLog.Info("Hired swords: you went down to someone else's trouble — they'll try another day.");
                return;
            }
            _contract = null;
            if (v.Contract.Job == Job.Beat)
            {
                int taken = Math.Min(Hero.MainHero.Gold, Math.Min(5000, (int)(Hero.MainHero.Gold * 0.25f)));
                if (taken > 0) Hero.MainHero.ChangeHeroGold(-taken);
                _koBeating = new KoBeating { Sender = v.Sender, Settlement = v.Settlement, Taken = taken };
                LmmiLog.Info($"Hired swords: knocked out in the street — they beat you and take {taken}.");
            }
            else
            {
                _takenContract = v.Contract;
                _takenIn = v.Settlement;
                LmmiLog.Info($"Hired swords: knocked out in the street — they carry you off ({v.Contract.Job}).");
            }
            _koEndAt = _sceneTime + 7f;
        }

        private void EndVisit(Visit v, string why)
        {
            foreach (var m in v.Men.Where(a => a.IsActive()))
            {
                StreetEventsBehavior.Release(m);
                m.FadeOut(true, true);
            }
            if (_visit == v) _visit = null;
            HiredBeatDownPatch.Guarded = null;
            LmmiLog.Info($"Hired swords: the street visit ends ({why}).");
        }

        /// <summary>Weapons out. They're soldiers; you have whatever you walk the streets with.</summary>
        private void Fight(Visit v)
        {
            var mission = Mission.Current;
            var main = Agent.Main;
            var handler = mission?.GetMissionBehavior<MissionFightHandler>();
            var men = v.Men.Where(a => a.IsActive()).ToList();
            if (handler == null || main == null || !main.IsActive() || men.Count == 0 || handler.IsThereActiveFight()) { EndVisit(v, "no fight"); return; }
            foreach (var m in men) StreetEventsBehavior.Release(m);
            // Beaten down, not knocked out: real blows, but none takes your last breath (see HiredBeatDownPatch and
            // UpdateVisit). Not Immortal: in this game version that means no damage at all, i.e. a fight you can't lose.
            if (!LmmiSettingsProvider.TestImmortal) HiredBeatDownPatch.Guarded = main;
            v.Step = VisitStep.Fighting;
            v.StepAt = _sceneTime;
            _fightActive = true;
            StreetEventsBehavior.Bark(v.Lead, Flavor.Pick("{=lmmi_hired_scene_take_him}Take {?PLAYER.GENDER}her{?}him{\\?}!",
                    "{=lmmi_hired_scene_take_him_2}Get {?PLAYER.GENDER}her{?}him{\\?}!",
                    "{=lmmi_hired_scene_take_him_3}Now — take {?PLAYER.GENDER}her{?}him{\\?}!"));
            handler.StartCustomFight(new List<Agent> { main }, men, dropWeapons: false, isItemUseDisabled: false,
                won =>
                {
                    try { OnFightEnd(v, won); }
                    catch (Exception ex) { LmmiLog.Error("Hired swords: fight end threw", ex); _visit = null; }
                });
            LmmiLog.Info($"Hired swords: a fight in the street — you against {men.Count}.");
        }

        private void OnFightEnd(Visit v, bool won)
        {
            _fightActive = false;
            HiredBeatDownPatch.Guarded = null;
            if (v.Down || _visit != v) return;   // you were knocked out: already dealt with (KnockedOut)
            var main = Agent.Main;
            if (main != null && main.IsActive()) StreetEventsBehavior.CalmAround(main.Position);
            if (won) Won(v);
            else if (v.Contract.Job == Job.Beat) Beaten(v, fought: true);
            else Taken(v, fought: true);
        }

        private void Won(Visit v)
        {
            int loot = v.Contract.Fee / 2;
            Hero.MainHero.ChangeHeroGold(loot);
            TownStandingBehavior.Adjust(v.Settlement, 2f, "fought off hired swords in the street");
            ChangeRelationAction.ApplyPlayerRelation(v.Sender, -5, affectRelatives: false);
            var msg = new TextObject("{=lmmi_hired_scene_won}The last of them goes down. In their leader's purse: {GOLD}{GOLD_ICON} of {SENDER}'s silver.");
            msg.SetTextVariable("GOLD", loot);
            msg.SetTextVariable("SENDER", v.Sender.Name);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
            _contract = null;
            _visit = null;
            LmmiLog.Info($"Hired swords: you beat them ({loot} taken off their leader).");
        }

        /// <summary>A beating: the screen goes dark, and you come to poorer.</summary>
        private void Beaten(Visit v, bool fought)
        {
            v.Step = VisitStep.Leaving;
            ScreenFadeController.BeginFadeOut(0.8f);
            _fadedOut = true;
            v.ThenAt = _sceneTime + 1.1f;
            v.Then = () =>
            {
                var main = Agent.Main;
                int taken = Math.Min(Hero.MainHero.Gold, Math.Min(5000, (int)(Hero.MainHero.Gold * 0.25f)));
                if (taken > 0) Hero.MainHero.ChangeHeroGold(-taken);
                if (main != null && main.IsActive()) main.Health = Math.Min(main.Health, Math.Max(1f, main.HealthLimit * (fought ? 0.12f : 0.25f)));
                foreach (var m in v.Men.Where(a => a.IsActive())) { StreetEventsBehavior.Release(m); m.FadeOut(true, true); }
                ScreenFadeController.BeginFadeIn(0.8f);
                _fadedOut = false;
                var msg = new TextObject("{=lmmi_hired_scene_beaten}They take their time over it. When you can see straight again they're gone — and so is {GOLD}{GOLD_ICON}, \"for their trouble\". The message was plain: stay out of {SENDER}'s way.");
                msg.SetTextVariable("GOLD", taken);
                msg.SetTextVariable("SENDER", v.Sender.Name);
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
                _contract = null;
                _visit = null;
                LmmiLog.Info($"Hired swords: beaten in the street ({taken} taken).");
            };
        }

        /// <summary>Carried off: the scene ends, and you ride out of town as their prisoner.</summary>
        private void Taken(Visit v, bool fought)
        {
            v.Step = VisitStep.Leaving;
            ScreenFadeController.BeginFadeOut(0.8f);
            _fadedOut = true;
            v.ThenAt = _sceneTime + 1.1f;
            v.Then = () =>
            {
                foreach (var m in v.Men.Where(a => a.IsActive())) { StreetEventsBehavior.Release(m); m.FadeOut(true, true); }
                _takenContract = v.Contract;
                _takenIn = v.Settlement;
                _contract = null;
                _visit = null;
                LmmiLog.Info($"Hired swords: they carry you off ({(fought ? "beaten down" : "you went quietly")}).");
                Mission.Current?.EndMission();
            };
        }

        /// <summary>Paid off: they walk off down the street — well off, 60-100 m — and are gone once out of your way.</summary>
        private void BoughtOff(Visit v)
        {
            v.Step = VisitStep.Leaving;
            v.StepAt = _sceneTime;
            v.Departing = true;
            _contract = null;
            var mission = Mission.Current;
            var main = Agent.Main;
            var men = v.Men.Where(a => a.IsActive()).ToList();
            if (mission == null || main == null || men.Count == 0) { EndVisit(v, "bought off"); return; }
            var lead = v.Lead.IsActive() ? v.Lead : men[0];
            var to = WalkAwayPoint(mission, lead, main, men);
            if (to == null)
            {
                // Nowhere far to head for: straight away from you, as far as the street goes.
                var away = lead.GetWorldPosition();
                var dir = (lead.Position - main.Position).AsVec2;
                dir = dir.LengthSquared < 0.01f ? Vec2.Forward : dir.Normalized();
                away.SetVec2(lead.Position.AsVec2 + dir * 40f);
                to = away;
            }
            StreetEventsBehavior.Direct(lead)?.GoTo(to.Value, run: false);
            foreach (var m in men.Where(a => a != lead)) StreetEventsBehavior.Direct(m)?.Follow(lead, 1.6f, run: false);
            LmmiLog.Info($"Hired swords: bought off — they walk away ({to.Value.AsVec2.Distance(main.Position.AsVec2):0} m off).");
        }

        /// <summary>
        /// Somewhere 60-100 m from you, the further along "away from you" the better: where someone's standing (so it's
        /// walkable), else anyone 35 m+ away.
        /// </summary>
        private static WorldPosition? WalkAwayPoint(Mission mission, Agent lead, Agent main, List<Agent> men)
        {
            var away = (lead.Position - main.Position).AsVec2;
            away = away.LengthSquared < 0.01f ? Vec2.Forward : away.Normalized();
            float Along(Agent a)
            {
                var d = (a.Position - main.Position).AsVec2;
                return d.LengthSquared < 0.01f ? 0f : Vec2.DotProduct(d.Normalized(), away);
            }
            var others = mission.Agents.Where(a => a.IsActive() && a.IsHuman && a != main && !men.Contains(a)).ToList();
            var spot = others.Where(a => { float d = a.Position.Distance(main.Position); return d >= 60f && d <= 100f; })
                           .OrderByDescending(Along).FirstOrDefault()
                       ?? others.Where(a => { float d = a.Position.Distance(main.Position); return d >= 35f && d <= 150f; })
                           .OrderByDescending(a => a.Position.Distance(main.Position) * (1.5f + Along(a))).FirstOrDefault();
            return spot?.GetWorldPosition();
        }

        private void OnConversationEnded(IEnumerable<CharacterObject> characters)
        {
            var v = _visit;
            if (v == null || !v.Talking || v.Step != VisitStep.Approaching) return;
            v.Talking = false;
            if (v.Chosen) return;
            // Walked out of the conversation: they don't take that for an answer.
            _afterSceneTalk.Add(() => { if (_visit == v && v.Step == VisitStep.Approaching) Fight(v); });
        }

        private void ChooseInScene(Action<Visit> then)
        {
            var v = _visit;
            if (v == null) return;
            v.Chosen = true;
            _afterSceneTalk.Add(() => { if (_visit == v) then(v); });
        }

        private void OnMissionEnded(IMission mission)
        {
            // The scene ended while we had the screen dark: lift it once the menu is up (the fade outlives the scene).
            if (_fadedOut) { _fadedOut = false; _needFadeIn = true; }
            HiredBeatDownPatch.Guarded = null;
            _koEndAt = -1f;
            // Knocked out and the scene ended before a tick noticed.
            if (_visit != null && !_visit.Down && PlayerDown(_visit)) KnockedOut(_visit);
            if (_fightActive && _visit != null)
            {
                // You ran for it.
                _visit.Contract.NotBeforeHours = CampaignTime.Now.ToHours + 24.0;
                var msg = new TextObject("{=lmmi_hired_scene_ran}You got away from {SENDER}'s hired swords — for now.");
                msg.SetTextVariable("SENDER", _visit.Sender.Name);
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
                LmmiLog.Info("Hired swords: you left the scene mid-fight.");
            }
            else if (_visit != null && _visit.Step == VisitStep.Approaching)
                _visit.Contract.NotBeforeHours = CampaignTime.Now.ToHours + 6.0;
            _fightActive = false;
            _visit = null;
        }

        private void OnGameMenuOpened(MenuCallbackArgs args)
        {
            if (_needFadeIn && Mission.Current == null)
            {
                _needFadeIn = false;
                ScreenFadeController.BeginFadeIn(0.6f);
            }
            if (Mission.Current != null) return;
            // Ours, not vanilla's "settlement_player_unconscious" (or whatever menu the scene ended on).
            var open = args.MenuContext?.GameMenu?.StringId;
            if (_takenIn != null)
            {
                if (open != "lmmi_hired_taken") GameMenu.SwitchToMenu("lmmi_hired_taken");
                return;
            }
            if (_koBeating != null && open != "lmmi_hired_ko_beaten")
            {
                if (Settlement.CurrentSettlement == null) { ShowKoBeating(); return; }
                GameMenu.SwitchToMenu("lmmi_hired_ko_beaten");
            }
        }

        /// <summary>Told as a message when there's no settlement menu to tell it in.</summary>
        private void ShowKoBeating()
        {
            var k = _koBeating;
            _koBeating = null;
            if (k == null) return;
            var msg = new TextObject("{=lmmi_hired_scene_beaten}They take their time over it. When you can see straight again they're gone — and so is {GOLD}{GOLD_ICON}, \"for their trouble\". The message was plain: stay out of {SENDER}'s way.");
            msg.SetTextVariable("GOLD", k.Taken);
            msg.SetTextVariable("SENDER", k.Sender.Name);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
        }

        // ---- Judgment ----

        private static Verdict Decide(Hero sender, Job job)
        {
            bool canHold = HoldingParty(sender) != null;
            if (job == Job.Hold) return canHold ? Verdict.Hold : Verdict.Humiliate;
            if (sender.GetTraitLevel(DefaultTraits.Mercy) <= -1 && sender.GetRelationWithPlayer() <= -60f) return Verdict.Doom;
            return canHold ? Verdict.Hold : Verdict.Humiliate;
        }

        /// <summary>Where a lord at war with you can keep you: their own party, or one of their clan's castles or towns.</summary>
        private static PartyBase? HoldingParty(Hero sender)
        {
            if (!sender.IsLord || sender.MapFaction == null || Hero.MainHero.MapFaction == null
                || !FactionManager.IsAtWarAgainstFaction(sender.MapFaction, Hero.MainHero.MapFaction)) return null;
            var party = sender.PartyBelongedTo;
            if (party != null && party.IsActive && party.LeaderHero == sender && party.MapEvent == null) return party.Party;
            var here = MobileParty.MainParty.Position;
            return sender.Clan?.Settlements
                .Where(s => (s.IsTown || s.IsCastle) && s.MapFaction == sender.MapFaction)
                .OrderBy(s => s.GatePosition.Distance(here))
                .FirstOrDefault()?.Party;
        }

        private void Face(Band band)
        {
            var sender = SenderOf(band);
            _releasing = true;
            try { EndCaptivityAction.ApplyByReleasedByChoice(Hero.MainHero); }
            finally { _releasing = false; }
            Disband(band, "the job's done");
            if (sender == null || !sender.IsAlive) return;
            var verdict = band.Forced ?? Decide(sender, band.Job);
            if (verdict == Verdict.Hold && HoldingParty(sender) == null) verdict = Verdict.Humiliate;
            _judgment = new Judgment { Sender = sender, Verdict = verdict };
            LordsBehavior.NoteGrievance(sender);
        }

        private bool Judging(Verdict? verdict = null)
        {
            var j = _judgment;
            return j != null && j.Opened && Hero.OneToOneConversationHero == j.Sender && (verdict == null || j.Verdict == verdict);
        }

        /// <summary>Held for ransom by a lord at war with you: vanilla captivity from here.</summary>
        private void Imprison(Hero sender)
        {
            var party = HoldingParty(sender);
            if (party == null) { SendOff(sender, 0.3f); return; }
            TakePrisonerAction.Apply(party, Hero.MainHero);
            LmmiLog.Info($"Hired swords: {sender.Name} holds you prisoner ({party.Name}).");
        }

        /// <summary>Sent off with a lighter purse; what's taken goes to whoever paid.</summary>
        private void SendOff(Hero sender, float share)
        {
            int taken = Math.Min(Hero.MainHero.Gold, Math.Min(10000, (int)(Hero.MainHero.Gold * share)));
            if (taken > 0) GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, sender, taken, disableNotification: true);
            var msg = new TextObject("{=lmmi_hired_sent_off}{NAME}'s people strip your purse of {GOLD}{GOLD_ICON} and throw you out on the road.");
            msg.SetTextVariable("NAME", sender.Name);
            msg.SetTextVariable("GOLD", taken);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
            LmmiLog.Info($"Hired swords: {sender.Name} sends you off, {taken} lighter.");
        }

        private void Execute(Hero sender)
        {
            if (!LmmiSettingsProvider.AllowPlayerExecution)
            {
                if (HoldingParty(sender) != null) Imprison(sender);
                else SendOff(sender, 0.5f);
                return;
            }
            LmmiLog.Info($"Hired swords: {sender.Name} has you executed.");
            KillCharacterAction.ApplyByExecution(Hero.MainHero, sender, showNotification: true, isForced: false);
        }

        private void Then(Action<Hero> action)
        {
            var j = _judgment;
            if (j == null) return;
            _judgment = null;
            _afterTalk.Add(() => action(j.Sender));
        }

        // ---- Dialogs and menus ----

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try
            {
                AddBandDialogs(starter);
                AddSceneDialogs(starter);
                AddJudgmentDialogs(starter);
                AddMenus(starter);
            }
            catch (Exception ex) { LmmiLog.Error("HiredSwordsBehavior: failed to register dialogs", ex); }
        }

        private Band? TalkingBand() =>
            Hero.OneToOneConversationHero == null ? BandOf(MobileParty.ConversationParty) : null;

        // ---- Buying them off ----

        /// <summary>
        /// What it takes to turn them: twice the fee, plus a share of what you carry (they know who you are) — a quarter of
        /// your purse for a nobody, two-fifths for a great house — and more the more whoever paid hates you (×1 at
        /// relation −25, ×2.5 at −100: they'd be crossing someone dangerous). Never more than four-fifths of your purse.
        /// </summary>
        private static int OutbidPrice(Hero? sender, int fee)
        {
            float price = fee * 2f;
            if (sender != null)
            {
                float relation = sender.GetRelationWithPlayer();
                float hate = 1f + 1.5f * Math.Max(0f, Math.Min(1f, (-relation - 25f) / 75f));
                int tier = Math.Max(0, Math.Min(6, Clan.PlayerClan?.Tier ?? 0));
                float share = Math.Min(0.8f, (0.25f + 0.025f * tier) * hate);
                price += Math.Max(0, Hero.MainHero.Gold) * share;
            }
            return Math.Max(10, (int)Math.Round(price / 10f) * 10);
        }

        /// <summary>
        /// Some won't sell: a cruel enemy who truly hates you pays them for something coin can't undo; a cunning lord at war
        /// has promised them a cut of your ransom.
        /// </summary>
        private static Refusal WontSell(Hero? sender, Job job)
        {
            if (sender == null) return Refusal.None;
            if (sender.GetRelationWithPlayer() <= -70f && sender.GetTraitLevel(DefaultTraits.Mercy) <= -1) return Refusal.Personal;
            if (job == Job.Hold && sender.IsLord && sender.GetTraitLevel(DefaultTraits.Calculating) >= 1 && sender.MapFaction != null
                && Hero.MainHero.MapFaction != null && FactionManager.IsAtWarAgainstFaction(sender.MapFaction, Hero.MainHero.MapFaction))
                return Refusal.Ransom;
            return Refusal.None;
        }

        /// <summary>Fix the price as they greet you, and set the dialog variables for it.</summary>
        private void PriceThem(Hero? sender, Job job, int fee)
        {
            _bribe = OutbidPrice(sender, fee);
            _refusal = WontSell(sender, job);
            var tag = _refusal == Refusal.None
                ? new TextObject("{=lmmi_hired_bribe_tag} [{AMOUNT}{GOLD_ICON}]").SetTextVariable("AMOUNT", _bribe)
                : TextObject.GetEmpty();
            MBTextManager.SetTextVariable("LMMI_HIRED_BRIBE", _bribe);
            MBTextManager.SetTextVariable("LMMI_HIRED_BRIBE_TAG", tag);
            LmmiLog.Info($"Hired swords: buying them off would cost {_bribe} (fee {fee}, purse {Hero.MainHero.Gold}"
                         + $"{(sender != null ? $", relation {sender.GetRelationWithPlayer():0}" : "")}){(_refusal != Refusal.None ? $" — they won't sell ({_refusal})" : "")}.");
        }

        private bool CanBuyThemOff(out TextObject why)
        {
            why = TextObject.GetEmpty();
            if (_refusal == Refusal.Personal)
            {
                why = new TextObject("{=lmmi_hired_wont_sell}They won't take your coin — this one's personal.");
                return false;
            }
            if (_refusal == Refusal.Ransom)
            {
                why = new TextObject("{=lmmi_hired_wont_sell_ransom}They won't take your coin — a share of your ransom is worth more to them.");
                return false;
            }
            if (Hero.MainHero.Gold >= _bribe) return true;
            why = new TextObject("{=lmmi_cant_afford_alms}You don't have that much on you.");
            return false;
        }

        private void AddBandDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_hired_greet", "start", "lmmi_hired_greet_resp",
                "{=lmmi_hired_greet}{LMMI_HIRED_GREET}",
                () =>
                {
                    var band = TalkingBand();
                    if (band == null) return false;
                    var sender = SenderOf(band);
                    var line = (band.Job switch
                    {
                        Job.Beat => Flavor.Pick("{=lmmi_hired_greet_beat}{SENDER} sends regards, {PLAYER.NAME}. Nothing personal — we're paid to leave you sorry, not dead.[if:convo_nonchalant][ib:hip]",
                            "{=lmmi_hired_greet_beat_2}{PLAYER.NAME}, is it? {SENDER} paid us to rearrange your face. Hold still and it'll be quick.[if:convo_nonchalant][ib:hip]",
                            "{=lmmi_hired_greet_beat_3}Message from {SENDER}, {PLAYER.NAME}. It's written on our knuckles.[if:convo_nonchalant][ib:hip]"),
                        Job.Deliver => Flavor.Pick("{=lmmi_hired_greet_deliver}{SENDER} wants a word with you, {PLAYER.NAME} — in person. Come quietly, or come in pieces.[if:convo_predatory][ib:warrior]",
                            "{=lmmi_hired_greet_deliver_2}{SENDER} wants you brought in, {PLAYER.NAME}. Walking or carried — your choice.[if:convo_predatory][ib:warrior]",
                            "{=lmmi_hired_greet_deliver_3}You're coming with us, {PLAYER.NAME}. {SENDER} is waiting, and doesn't like to wait.[if:convo_predatory][ib:warrior]"),
                        _ => Flavor.Pick("{=lmmi_hired_greet_hold}{SENDER} pays well for highborn guests, {PLAYER.NAME}. Come quietly, or come in pieces.[if:convo_predatory][ib:warrior]",
                            "{=lmmi_hired_greet_hold_2}{SENDER}'s paying good silver for you, {PLAYER.NAME}. Hands where we can see them.[if:convo_predatory][ib:warrior]",
                            "{=lmmi_hired_greet_hold_3}There's a ransom on your head, {PLAYER.NAME}, and {SENDER} wants to collect. Come along.[if:convo_predatory][ib:warrior]"),
                    });
                    line.SetTextVariable("SENDER", sender?.Name ?? new TextObject("{=lmmi_thugs_someone}Someone you crossed"));
                    LordsBehavior.NoteGrievance(sender);
                    MBTextManager.SetTextVariable("LMMI_HIRED_GREET", line);
                    MBTextManager.SetTextVariable("LMMI_HIRED_SENDER", sender?.Name ?? TextObject.GetEmpty());
                    PriceThem(sender, band.Job, band.Fee);
                    return true;
                }, null, 1200);

            starter.AddPlayerLine("lmmi_hired_fight", "lmmi_hired_greet_resp", "lmmi_hired_fight_resp",
                "{=lmmi_hired_fight}Come and try it.", null, null);
            starter.AddDialogLine("lmmi_hired_fight_resp", "lmmi_hired_fight_resp", "close_window",
                "{=!}{LMMI_HIRED_FIGHT}",
                    () => Flavor.Say("LMMI_HIRED_FIGHT",
                        "{=lmmi_hired_fight_resp}Have it your way.[if:convo_bared_teeth][ib:aggressive]",
                        "{=lmmi_hired_fight_resp_2}Suit yourself.[if:convo_bared_teeth][ib:aggressive]",
                        "{=lmmi_hired_fight_resp_3}Thought you'd say that.[if:convo_bared_teeth][ib:aggressive]"), null);

            // Sellswords are loyal to coin — up to a point.
            starter.AddPlayerLine("lmmi_hired_outbid", "lmmi_hired_greet_resp", "lmmi_hired_outbid_resp",
                "{=lmmi_hired_outbid_more}Whatever {LMMI_HIRED_SENDER} is paying you, I'll pay more. Turn around.{LMMI_HIRED_BRIBE_TAG}", null,
                () =>
                {
                    var band = TalkingBand();
                    if (band == null) return;
                    int paid = Math.Min(_bribe, Hero.MainHero.Gold);
                    Hero.MainHero.ChangeHeroGold(-paid);
                    _bands.Remove(band.PartyId);
                    _disband.Add(band.PartyId);
                    MobileParty.ConversationParty?.SetMoveModeHold();
                    if (PlayerEncounter.Current != null) PlayerEncounter.LeaveEncounter = true;
                    LmmiLog.Info($"Hired swords: you outbid {SenderOf(band)?.Name} ({paid}).");
                }, 100, CanBuyThemOff);
            starter.AddDialogLine("lmmi_hired_outbid_resp", "lmmi_hired_outbid_resp", "close_window",
                "{=!}{LMMI_HIRED_OUTBID}",
                    () => Flavor.Say("LMMI_HIRED_OUTBID",
                        "{=lmmi_hired_outbid_resp}...Coin's coin. We never found you.[if:convo_mocking_teasing][ib:hip]",
                        "{=lmmi_hired_outbid_resp_2}...Gold talks. We'll tell them you'd already left town.[if:convo_mocking_teasing][ib:hip]",
                        "{=lmmi_hired_outbid_resp_3}...Fair enough. Pleasure doing business.[if:convo_mocking_teasing][ib:hip]"), null);
        }

        private bool TalkingInScene()
        {
            var v = _visit;
            if (v == null || v.Step != VisitStep.Approaching || ConversationMission.OneToOneConversationAgent != v.Lead) return false;
            if (!v.Talking) { v.Talking = true; v.TalkAt = _sceneTime; v.Chosen = false; }
            return true;
        }

        private void AddSceneDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_hired_scene", "start", "lmmi_hired_scene_resp",
                "{=lmmi_hired_scene}{LMMI_HIRED_SCENE}",
                () =>
                {
                    if (!TalkingInScene()) return false;
                    var v = _visit!;
                    var line = (v.Contract.Job switch
                    {
                        Job.Beat => Flavor.Pick("{=lmmi_hired_scene_beat}{PLAYER.NAME}? {SENDER} sends regards. Nothing personal — we're paid to leave you sorry, not dead.[if:convo_nonchalant][ib:hip]",
                            "{=lmmi_hired_scene_beat_2}{PLAYER.NAME}? Thought so. {SENDER} sends regards — the painful kind.[if:convo_nonchalant][ib:hip]",
                            "{=lmmi_hired_scene_beat_3}There you are, {PLAYER.NAME}. {SENDER} paid us to leave a few bruises. Nothing personal.[if:convo_nonchalant][ib:hip]"),
                        Job.Deliver => Flavor.Pick("{=lmmi_hired_scene_deliver}{PLAYER.NAME}. {SENDER} wants a word with you — in person. Come quietly, or we carry you.[if:convo_predatory][ib:warrior]",
                            "{=lmmi_hired_scene_deliver_2}{PLAYER.NAME}. {SENDER} would like a word. Come along nicely.[if:convo_predatory][ib:warrior]",
                            "{=lmmi_hired_scene_deliver_3}Found you, {PLAYER.NAME}. {SENDER} wants you in person. Walk, or be dragged.[if:convo_predatory][ib:warrior]"),
                        _ => Flavor.Pick("{=lmmi_hired_scene_hold}{PLAYER.NAME}. {SENDER} pays well for highborn guests. Come quietly, or we carry you.[if:convo_predatory][ib:warrior]",
                            "{=lmmi_hired_scene_hold_2}{PLAYER.NAME}. {SENDER} has a cell waiting for you. Come quietly.[if:convo_predatory][ib:warrior]",
                            "{=lmmi_hired_scene_hold_3}Ransom money on two legs. {SENDER} sends for you, {PLAYER.NAME}.[if:convo_predatory][ib:warrior]"),
                    });
                    line.SetTextVariable("SENDER", v.Sender.Name);
                    LordsBehavior.NoteGrievance(v.Sender);
                    MBTextManager.SetTextVariable("LMMI_HIRED_SCENE", line);
                    MBTextManager.SetTextVariable("LMMI_HIRED_SENDER", v.Sender.Name);
                    PriceThem(v.Sender, v.Contract.Job, v.Contract.Fee);
                    return true;
                }, null, 1250);

            starter.AddPlayerLine("lmmi_hired_scene_fight", "lmmi_hired_scene_resp", "lmmi_hired_scene_fight_resp",
                "{=lmmi_hired_fight}Come and try it.", null, () => ChooseInScene(Fight));
            starter.AddDialogLine("lmmi_hired_scene_fight_resp", "lmmi_hired_scene_fight_resp", "close_window",
                "{=!}{LMMI_HIRED_FIGHT}",
                    () => Flavor.Say("LMMI_HIRED_FIGHT",
                        "{=lmmi_hired_fight_resp}Have it your way.[if:convo_bared_teeth][ib:aggressive]",
                        "{=lmmi_hired_fight_resp_2}Suit yourself.[if:convo_bared_teeth][ib:aggressive]",
                        "{=lmmi_hired_fight_resp_3}Thought you'd say that.[if:convo_bared_teeth][ib:aggressive]"), null);

            starter.AddPlayerLine("lmmi_hired_scene_outbid", "lmmi_hired_scene_resp", "lmmi_hired_scene_outbid_resp",
                "{=lmmi_hired_outbid_more}Whatever {LMMI_HIRED_SENDER} is paying you, I'll pay more. Turn around.{LMMI_HIRED_BRIBE_TAG}", null,
                () =>
                {
                    var v = _visit;
                    if (v == null) return;
                    int paid = Math.Min(_bribe, Hero.MainHero.Gold);
                    Hero.MainHero.ChangeHeroGold(-paid);
                    LmmiLog.Info($"Hired swords: you outbid {v.Sender.Name} ({paid}).");
                    ChooseInScene(BoughtOff);
                }, 100, CanBuyThemOff);
            starter.AddDialogLine("lmmi_hired_scene_outbid_resp", "lmmi_hired_scene_outbid_resp", "close_window",
                "{=!}{LMMI_HIRED_OUTBID}",
                    () => Flavor.Say("LMMI_HIRED_OUTBID",
                        "{=lmmi_hired_outbid_resp}...Coin's coin. We never found you.[if:convo_mocking_teasing][ib:hip]",
                        "{=lmmi_hired_outbid_resp_2}...Gold talks. We'll tell them you'd already left town.[if:convo_mocking_teasing][ib:hip]",
                        "{=lmmi_hired_outbid_resp_3}...Fair enough. Pleasure doing business.[if:convo_mocking_teasing][ib:hip]"), null);

            starter.AddPlayerLine("lmmi_hired_scene_take", "lmmi_hired_scene_resp", "lmmi_hired_scene_take_resp",
                "{=lmmi_hired_scene_take}Get it over with.", () => _visit?.Contract.Job == Job.Beat,
                () => ChooseInScene(v => Beaten(v, fought: false)));
            starter.AddDialogLine("lmmi_hired_scene_take_resp", "lmmi_hired_scene_take_resp", "close_window",
                "{=!}{LMMI_HIRED_TAKE}",
                    () => Flavor.Say("LMMI_HIRED_TAKE",
                        "{=lmmi_hired_scene_take_resp}Sensible. This won't take long.[if:convo_nonchalant][ib:confident]",
                        "{=lmmi_hired_scene_take_resp_2}Wise. Won't take long.[if:convo_nonchalant][ib:confident]",
                        "{=lmmi_hired_scene_take_resp_3}Good. Less trouble for everyone.[if:convo_nonchalant][ib:confident]"), null);

            starter.AddPlayerLine("lmmi_hired_scene_yield", "lmmi_hired_scene_resp", "lmmi_hired_scene_yield_resp",
                "{=lmmi_hired_scene_yield}I'll come quietly.", () => _visit != null && _visit.Contract.Job != Job.Beat,
                () => ChooseInScene(v => Taken(v, fought: false)));
            starter.AddDialogLine("lmmi_hired_scene_yield_resp", "lmmi_hired_scene_yield_resp", "close_window",
                "{=!}{LMMI_HIRED_YIELD}",
                    () => Flavor.Say("LMMI_HIRED_YIELD",
                        "{=lmmi_hired_scene_yield_resp}Sensible. Hands where we can see them.[if:convo_stern][ib:warrior]",
                        "{=lmmi_hired_scene_yield_resp_2}Good choice. Walk ahead of us.[if:convo_stern][ib:warrior]",
                        "{=lmmi_hired_scene_yield_resp_3}Smart. Keep your hands where we can see them.[if:convo_stern][ib:warrior]"), null);

            // Carried off: out of the town, and onto the road as their prisoner.
            starter.AddGameMenu("lmmi_hired_taken",
                "{=lmmi_hired_taken}You come to bound across a saddle, riding out of {LMMI_HIRED_TOWN} among {LMMI_HIRED_SENDER}'s hired swords.",
                args =>
                {
                    MBTextManager.SetTextVariable("LMMI_HIRED_TOWN", _takenIn?.Name ?? TextObject.GetEmpty());
                    var sender = _takenContract != null ? Hero.FindFirst(h => h.StringId == _takenContract.SenderId) : null;
                    MBTextManager.SetTextVariable("LMMI_HIRED_SENDER", sender?.Name ?? new TextObject("{=lmmi_thugs_someone}Someone you crossed"));
                });
            starter.AddGameMenuOption("lmmi_hired_taken", "lmmi_hired_taken_continue", "{=lmmi_hired_continue}Continue...",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                args =>
                {
                    var c = _takenContract;
                    var where = _takenIn;
                    _takenContract = null;
                    _takenIn = null;
                    var sender = c != null ? Hero.FindFirst(h => h.StringId == c.SenderId) : null;
                    PlayerEncounter.LeaveSettlement();
                    PlayerEncounter.Finish(true);
                    if (c == null || sender == null || where == null) return;
                    var band = SpawnBand(sender, c.Job, c.Fee, engage: false, c.Forced, where.GatePosition);
                    if (band == null) { LmmiLog.Warning("Hired swords: couldn't make the band that carries you off — you're free."); return; }
                    TakePrisonerAction.Apply(band.Party, Hero.MainHero);
                });
        }

        private void AddJudgmentDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_hired_judge", "start", "lmmi_hired_judge_resp",
                "{=lmmi_hired_judge}{LMMI_HIRED_JUDGE}",
                () =>
                {
                    if (!Judging()) return false;
                    _judgment!.Seen = true;
                    var line = (_judgment!.Verdict switch
                    {
                        Verdict.Hold => Flavor.Pick("{=lmmi_hired_judge_hold}Well, well. {PLAYER.NAME}, at my mercy — and it cost me less than a good horse. You'll be my guest until your people find the ransom.[if:convo_very_stern][ib:closed2]",
                            "{=lmmi_hired_judge_hold_2}So. {PLAYER.NAME}, in my hall, in chains. Your family will pay, or you'll grow old here.[if:convo_very_stern][ib:closed2]",
                            "{=lmmi_hired_judge_hold_3}Welcome, {PLAYER.NAME}. You'll be staying a while — until somebody pays what you're worth.[if:convo_very_stern][ib:closed2]"),
                        Verdict.Doom => Flavor.Pick("{=lmmi_hired_judge_doom}Kneel. ...I've waited a long time for this, {PLAYER.NAME}. Any last words, before my men earn their pay?[if:convo_furious][ib:aggressive]",
                            "{=lmmi_hired_judge_doom_2}On your knees, {PLAYER.NAME}. I've dreamed of this. Say your prayers.[if:convo_furious][ib:aggressive]",
                            "{=lmmi_hired_judge_doom_3}At last. {PLAYER.NAME}, kneeling before me. Anything to say before the end?[if:convo_furious][ib:aggressive]"),
                        _ => Flavor.Pick("{=lmmi_hired_judge_humiliate}Look at you. I wanted you to see my face, so you'd know who did this to you. Now crawl back to your people — and stay out of my way.[if:convo_contemptuous][ib:hip]",
                            "{=lmmi_hired_judge_humiliate_2}Look at the great {PLAYER.NAME} now. Remember this the next time you cross me.[if:convo_contemptuous][ib:hip]",
                            "{=lmmi_hired_judge_humiliate_3}Bound and beaten. That's how I'll remember you, {PLAYER.NAME}. Now get out of my sight.[if:convo_contemptuous][ib:hip]"),
                    });
                    MBTextManager.SetTextVariable("LMMI_HIRED_JUDGE", line);
                    return true;
                }, null, 1300);

            // Held for ransom.
            starter.AddPlayerLine("lmmi_hired_hold_curse", "lmmi_hired_judge_resp", "lmmi_hired_hold_end",
                "{=lmmi_hired_hold_curse}You'll regret this.", () => Judging(Verdict.Hold), null);
            starter.AddPlayerLine("lmmi_hired_hold_ransom", "lmmi_hired_judge_resp", "lmmi_hired_hold_end",
                "{=lmmi_hired_hold_ransom}Name your price, then.", () => Judging(Verdict.Hold), null);
            starter.AddDialogLine("lmmi_hired_hold_end", "lmmi_hired_hold_end", "close_window",
                "{=!}{LMMI_HIRED_HOLD_END}",
                    () => Flavor.Say("LMMI_HIRED_HOLD_END",
                        "{=lmmi_hired_hold_end}In good time. Take {?PLAYER.GENDER}her{?}him{\\?} away.[if:convo_stern][ib:closed]",
                        "{=lmmi_hired_hold_end_2}We'll see. Lock {?PLAYER.GENDER}her{?}him{\\?} up.[if:convo_stern][ib:closed]",
                        "{=lmmi_hired_hold_end_3}Later. Get {?PLAYER.GENDER}her{?}him{\\?} out of my sight.[if:convo_stern][ib:closed]"),
                () => Then(Imprison));

            // Humiliated.
            starter.AddPlayerLine("lmmi_hired_humiliate_threat", "lmmi_hired_judge_resp", "lmmi_hired_humiliate_end",
                "{=lmmi_hired_humiliate_threat}This isn't over.", () => Judging(Verdict.Humiliate), null);
            starter.AddPlayerLine("lmmi_hired_humiliate_silent", "lmmi_hired_judge_resp", "lmmi_hired_humiliate_end",
                "{=lmmi_hired_humiliate_silent}(Say nothing.)", () => Judging(Verdict.Humiliate), null);
            starter.AddDialogLine("lmmi_hired_humiliate_end", "lmmi_hired_humiliate_end", "close_window",
                "{=!}{LMMI_HIRED_HUMILIATE_END}",
                    () => Flavor.Say("LMMI_HIRED_HUMILIATE_END",
                        "{=lmmi_hired_humiliate_end}It is for today. Empty {?PLAYER.GENDER}her{?}his{\\?} purse and throw {?PLAYER.GENDER}her{?}him{\\?} out.[if:convo_mocking_revenge][ib:hip]",
                        "{=lmmi_hired_humiliate_end_2}Over for now. Strip {?PLAYER.GENDER}her{?}his{\\?} purse and toss {?PLAYER.GENDER}her{?}him{\\?} in the road.[if:convo_mocking_revenge][ib:hip]",
                        "{=lmmi_hired_humiliate_end_3}We'll see. Take {?PLAYER.GENDER}her{?}his{\\?} coin and throw {?PLAYER.GENDER}her{?}him{\\?} out.[if:convo_mocking_revenge][ib:hip]"),
                () => Then(s => SendOff(s, 0.2f)));

            // Doomed: plead, or face it.
            starter.AddPlayerLine("lmmi_hired_plead", "lmmi_hired_judge_resp", _plea.Entry,
                "{=lmmi_hired_plead}[Plead for your life] Hear me out. I'm worth more to you alive.", () => Judging(Verdict.Doom),
                () =>
                {
                    var j = _judgment;
                    if (j == null) return;
                    var listener = j.Sender.CharacterObject;
                    float resentment = Math.Max(0f, CultureRelations.Multiplier(j.Sender.Culture?.StringId, Hero.MainHero.Culture?.StringId)
                                                    * LmmiSettingsProvider.ForeignerPrejudicePercent / 100f);
                    var lost = new TextObject(LmmiSettingsProvider.AllowPlayerExecution
                        ? "{=lmmi_hired_plea_lost}Enough. I've heard enough. Get it done.[if:convo_furious][ib:aggressive]"
                        : HoldingParty(j.Sender) != null
                            ? "{=lmmi_hired_plea_lost_held}Enough. ...No. Dead, you'd be a martyr. You'll rot in my cells instead.[if:convo_angry][ib:closed2]"
                            : "{=lmmi_hired_plea_lost_robbed}Enough. ...No. Dead, you'd be a martyr. Take everything {?PLAYER.GENDER}she{?}he{\\?} has and throw {?PLAYER.GENDER}her{?}him{\\?} in the road.[if:convo_angry][ib:closed2]");
                    _plea.Start(new[]
                        {
                            NativePersuasion.Argument(DefaultSkills.Trade, DefaultTraits.Calculating,
                                new TextObject("{=lmmi_hired_plea_ransom}Dead, I'm worth nothing to you. Alive, my clan will pay whatever you ask."), listener, resentment),
                            NativePersuasion.Argument(DefaultSkills.Leadership, DefaultTraits.Honor,
                                new TextObject("{=lmmi_hired_plea_honor}Kill a bound captive, and every hall in Calradia will know what you are."), listener, resentment),
                            NativePersuasion.Argument(DefaultSkills.Charm, DefaultTraits.Mercy,
                                new TextObject("{=lmmi_hired_plea_mercy}Whatever I did to you, my blood won't undo it."), listener, resentment),
                            NativePersuasion.Argument(DefaultSkills.Roguery, DefaultTraits.Valor,
                                new TextObject("{=lmmi_hired_plea_threat}My people know where I rode. Kill me, and they'll burn everything you own."), listener, resentment),
                        },
                        Flavor.Pick("{=lmmi_hired_plea_opening}Go on, then. Beg.[if:convo_mocking_revenge][ib:closed]",
                            "{=lmmi_hired_plea_opening_2}Beg, then. I'm listening.[if:convo_mocking_revenge][ib:closed]",
                            "{=lmmi_hired_plea_opening_3}Let's hear you grovel.[if:convo_mocking_revenge][ib:closed]"),
                        Flavor.Pick("{=lmmi_hired_plea_again}Is that all?[if:convo_undecided_closed][ib:closed2]",
                            "{=lmmi_hired_plea_again_2}And?[if:convo_undecided_closed][ib:closed2]",
                            "{=lmmi_hired_plea_again_3}Go on.[if:convo_undecided_closed][ib:closed2]"),
                        Flavor.Pick("{=lmmi_hired_plea_won}...Damn you. You're worth more to me breathing — for now.[if:convo_thinking][ib:closed2]",
                            "{=lmmi_hired_plea_won_2}...Fine. You live. For now.[if:convo_thinking][ib:closed2]",
                            "{=lmmi_hired_plea_won_3}...Curse you. Alive, you're worth more. Don't make me regret it.[if:convo_thinking][ib:closed2]"),
                        lost,
                        onWon: () =>
                        {
                            if (_judgment != null) _judgment.Spared = true;
                            Then(s =>
                            {
                                ChangeRelationAction.ApplyPlayerRelation(s, 5, affectRelatives: false);
                                if (HoldingParty(s) != null) Imprison(s);
                                else SendOff(s, 0.3f);
                            });
                        },
                        onLost: () => Then(Execute),
                        goal: 2f,
                        difficulty: j.Sender.GetRelationWithPlayer() <= -80f ? PersuasionDifficulty.VeryHard : PersuasionDifficulty.Hard);
                });
            starter.AddPlayerLine("lmmi_hired_defy", "lmmi_hired_judge_resp", "lmmi_hired_defy_resp",
                "{=lmmi_hired_defy}Get it over with.", () => Judging(Verdict.Doom),
                () =>
                {
                    // A brave enemy may respect a brave end.
                    var j = _judgment;
                    if (j == null) return;
                    j.Spared = j.Sender.GetTraitLevel(DefaultTraits.Valor) >= 1 && MBRandom.RandomFloat < 0.5f;
                    LmmiLog.Info($"Hired swords: you defy {j.Sender.Name} — {(j.Spared ? "spared for it" : "no quarter")}.");
                });
            starter.AddDialogLine("lmmi_hired_defy_spared", "lmmi_hired_defy_resp", "close_window",
                "{=lmmi_hired_defy_spared}...Hah. Not a word of begging. I'll give you that much. Get out of my sight, before I change my mind.[if:convo_approving][ib:warrior]",
                () => _judgment?.Spared == true, () => Then(s => SendOff(s, 0.3f)));
            starter.AddDialogLine("lmmi_hired_defy_doomed", "lmmi_hired_defy_resp", "close_window",
                "{=lmmi_hired_defy_doomed}As you wish.[if:convo_grave][ib:closed2]", () => _judgment?.Spared != true, () => Then(Execute));

            _plea.Register(starter);
        }

        private void AddMenus(CampaignGameStarter starter)
        {
            // Knocked out in the street by men paid to beat you (instead of vanilla's "settlement_player_unconscious").
            starter.AddGameMenu("lmmi_hired_ko_beaten",
                "{=lmmi_hired_ko_beaten}You went down hard — and they didn't stop there. You come to in a doorway in {LMMI_HIRED_TOWN}, every rib aching and your purse {LMMI_HIRED_TAKEN}{GOLD_ICON} lighter, \"for their trouble\". Stay out of {LMMI_HIRED_SENDER}'s way, they said. You heard.",
                args =>
                {
                    var k = _koBeating;
                    var here = Settlement.CurrentSettlement;
                    MBTextManager.SetTextVariable("LMMI_HIRED_TOWN", k?.Settlement.Name ?? here?.Name ?? TextObject.GetEmpty());
                    MBTextManager.SetTextVariable("LMMI_HIRED_TAKEN", k?.Taken ?? 0);
                    MBTextManager.SetTextVariable("LMMI_HIRED_SENDER", k?.Sender.Name ?? new TextObject("{=lmmi_thugs_someone}Someone you crossed"));
                    var mesh = here?.SettlementComponent?.WaitMeshName;
                    if (!string.IsNullOrEmpty(mesh)) args.MenuContext?.SetBackgroundMeshName(mesh);
                }, GameMenu.MenuOverlayType.SettlementWithBoth);
            starter.AddGameMenuOption("lmmi_hired_ko_beaten", "lmmi_hired_ko_beaten_continue", "{=lmmi_hired_continue}Continue...",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                args =>
                {
                    _koBeating = null;
                    var here = Settlement.CurrentSettlement;
                    if (here == null) { GameMenu.ExitToLast(); return; }
                    GameMenu.SwitchToMenu(here.IsVillage ? "village" : here.IsCastle ? "castle" : "town");
                });

            starter.AddGameMenu("lmmi_hired_beaten",
                "{=lmmi_hired_beaten}They take their time over it. When they're done, they go through your purse — {LMMI_HIRED_TAKEN}{GOLD_ICON}, \"for our trouble\" — and leave you in the road with a message: stay out of {LMMI_HIRED_SENDER}'s way.",
                args => SetMenuText());
            starter.AddGameMenuOption("lmmi_hired_beaten", "lmmi_hired_beaten_continue", "{=lmmi_hired_continue}Continue...",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                args => LetGo("they left you in the road"));

            starter.AddGameMenu("lmmi_hired_dumped",
                "{=lmmi_hired_dumped}Whoever was to pay for the rest of the job can't be found. The sellswords argue it out, empty your purse of {LMMI_HIRED_TAKEN}{GOLD_ICON}, and cut you loose.",
                args => SetMenuText());
            starter.AddGameMenuOption("lmmi_hired_dumped", "lmmi_hired_dumped_continue", "{=lmmi_hired_continue}Continue...",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                args => LetGo("nobody to deliver you to"));

            starter.AddGameMenu("lmmi_hired_delivered",
                "{=lmmi_hired_delivered}Bound and bruised, you're dragged before {LMMI_HIRED_SENDER}.",
                args => SetMenuText());
            starter.AddGameMenuOption("lmmi_hired_delivered", "lmmi_hired_delivered_face", "{=lmmi_hired_delivered_face}Face them.",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                args =>
                {
                    var band = _menuBand;
                    _menuBand = null;
                    if (band == null) { LetGo("lost track"); return; }
                    Face(band);
                });
        }

        private void SetMenuText()
        {
            var sender = _menuBand != null ? SenderOf(_menuBand) : null;
            MBTextManager.SetTextVariable("LMMI_HIRED_SENDER", sender?.Name ?? new TextObject("{=lmmi_thugs_someone}Someone you crossed"));
            MBTextManager.SetTextVariable("LMMI_HIRED_TAKEN", _taken);
        }

        private void LetGo(string why)
        {
            var band = _menuBand;
            _menuBand = null;
            _releasing = true;
            try { EndCaptivityAction.ApplyByReleasedAfterBattle(Hero.MainHero); }
            finally { _releasing = false; }
            if (band != null) Disband(band, why);
        }

        // ---- Test ----

        /// <summary>Test mode: the worst enemy near enough (or anyone, if none), paying for the job asked for.</summary>
        internal static void TestHire(string job)
        {
            var self = Instance;
            if (self == null) return;
            try
            {
                var want = job == "hold" ? Job.Hold : job == "beat" ? Job.Beat : Job.Deliver;
                Verdict? forced = job == "doom" ? Verdict.Doom : (Verdict?)null;
                var here = Settlement.CurrentSettlement?.GatePosition ?? MobileParty.MainParty.Position;
                Hero? sender = Candidates(400f, 300f).Where(c => c.Job == want).OrderBy(c => c.Sender.GetRelationWithPlayer()).Select(c => c.Sender).FirstOrDefault();
                if (sender == null)
                {
                    bool Near(Hero h) { var at = h.GetCampaignPosition(); return at.IsValid() && at.Distance(here) < 300f; }
                    sender = Hero.AllAliveHeroes
                        .Where(h => h != Hero.MainHero && !h.IsChild && !h.IsPrisoner && h.Clan != Clan.PlayerClan
                                    && (h.IsNotable || (h.IsLord && h.Clan != null && !h.Clan.IsBanditFaction && h.IsActive)) && Near(h)
                                    && (want != Job.Hold || (h.IsLord && h.MapFaction != null && Hero.MainHero.MapFaction != null
                                                             && FactionManager.IsAtWarAgainstFaction(h.MapFaction, Hero.MainHero.MapFaction))))
                        .OrderBy(h => h.GetRelationWithPlayer()).ThenBy(h => h.GetCampaignPosition().Distance(here)).FirstOrDefault();
                }
                if (sender == null)
                {
                    Say(want == Job.Hold ? "[LMMI Test] No lord at war with you within reach." : "[LMMI Test] Nobody within reach to send them.");
                    return;
                }
                if (self.Hire(sender, want, test: true, forced))
                    Say($"[LMMI Test] {sender.Name} (relation {sender.GetRelationWithPlayer():0}) has paid hired swords to "
                        + $"{(forced != null ? "bring you to judgment (plea for your life)" : want == Job.Beat ? "beat you" : want == Job.Hold ? "take you prisoner" : "bring you in")}. "
                        + "They'll walk up to you as soon as you're in a town or village centre.");
            }
            catch (Exception ex) { LmmiLog.Error("HiredSwordsBehavior.TestHire threw", ex); }
        }

        private static void Say(string text) => InformationManager.DisplayMessage(new InformationMessage(text, Colors.Cyan));

        private bool Ready(string key) =>
            LmmiSettingsProvider.TestMode || !_readyAtHours.TryGetValue(key, out var at) || at <= CampaignTime.Now.ToHours;

        private void SetCooldown(string key, float days) => _readyAtHours[key] = CampaignTime.Now.ToHours + days * CampaignTime.HoursInDay;

        // ---- Save / load: "C;key;hours" and "B;partyId;senderId;job;fee;spawnedHours;hasPrisoner;capturedHours" separated by '|' ----

        public override void SyncData(IDataStore dataStore)
        {
            string data = string.Empty;
            if (!dataStore.IsLoading)
            {
                double now = CampaignTime.Now.ToHours;
                var parts = _readyAtHours.Where(kv => kv.Value > now)
                    .Select(kv => "C;" + kv.Key + ";" + kv.Value.ToString("R", CultureInfo.InvariantCulture)).ToList();
                if (_contract != null)
                    parts.Add(string.Join(";", "K", _contract.SenderId, ((int)_contract.Job).ToString(CultureInfo.InvariantCulture),
                        _contract.Fee.ToString(CultureInfo.InvariantCulture), _contract.MadeHours.ToString("R", CultureInfo.InvariantCulture),
                        _contract.NotBeforeHours.ToString("R", CultureInfo.InvariantCulture)));
                parts.AddRange(_bands.Values.Select(b => string.Join(";", "B", b.PartyId, b.SenderId, ((int)b.Job).ToString(CultureInfo.InvariantCulture),
                    b.Fee.ToString(CultureInfo.InvariantCulture), b.SpawnedHours.ToString("R", CultureInfo.InvariantCulture),
                    b.HasPrisoner ? "1" : "0", b.CapturedHours.ToString("R", CultureInfo.InvariantCulture))));
                data = string.Join("|", parts);
            }
            dataStore.SyncData("lmmi_hired_v1", ref data);
            if (!dataStore.IsLoading) return;

            _readyAtHours = new Dictionary<string, double>();
            _bands = new Dictionary<string, Band>();
            _contract = null;
            foreach (var entry in (data ?? string.Empty).Split('|'))
            {
                var p = entry.Split(';');
                if (p.Length == 3 && p[0] == "C" && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var at))
                    _readyAtHours[p[1]] = at;
                else if (p.Length == 6 && p[0] == "K" && int.TryParse(p[2], out var kjob) && int.TryParse(p[3], out var kfee)
                         && double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var made)
                         && double.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var notBefore))
                    _contract = new Contract { SenderId = p[1], Job = (Job)kjob, Fee = kfee, MadeHours = made, NotBeforeHours = notBefore };
                else if (p.Length == 8 && p[0] == "B" && int.TryParse(p[3], out var job) && int.TryParse(p[4], out var fee)
                         && double.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var spawned)
                         && double.TryParse(p[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var captured))
                    _bands[p[1]] = new Band
                    {
                        PartyId = p[1], SenderId = p[2], Job = (Job)job, Fee = fee, SpawnedHours = spawned,
                        HasPrisoner = p[6] == "1", CapturedHours = captured,
                    };
            }
        }
    }
}
