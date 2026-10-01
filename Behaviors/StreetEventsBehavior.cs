using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using SandBox;
using SandBox.Conversation;
using SandBox.Conversation.MissionLogics;
using SandBox.Missions.AgentBehaviors;
using SandBox.Missions.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation.Persuasion;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Street events, staged in the scene. Something is happening a little way off — men have cornered a girl,
    /// two youths are beating each other bloody, a boy has cut a purse, a man has fallen and can't get up — and a
    /// local runs up and asks for help. Agree, and they lead you there; what you do about it (words, coin, fists or
    /// a steady hand) is remembered: step up and the town talks, and prejudice counts for less there for a while.
    /// Walk away, and it plays out without you: the girl is led off, the brawl ends with someone on the ground.
    /// Any of them may be a setup: the same plea, the same scene — until you get there and the toughs close in. Nothing
    /// tells you beforehand (a good rogue may get a bad feeling, and a bad feeling may be wrong).
    /// Built from vanilla pieces: a scripted daily behavior moves the actors, vanilla's alarmed/fight behaviors and
    /// teams make two locals brawl, MissionFightHandler runs your own fights, and contour outlines and portrait barks
    /// show who's who.
    /// </summary>
    public partial class StreetEventsBehavior : CampaignBehaviorBase
    {
        private enum Kind { Rescue, Brawl, Thief, Injured, Debt, LostChild, Shakedown, Dispute, Harvest, Raiders }

        private enum Step
        {
            Asking,       // the requester walks up and asks for help
            Guiding,      // you said yes: they lead you there
            Confronting,  // you're there: someone steps up to you
            Fighting,     // fists
            Chasing,      // the thief runs, you run after him
            Caught,       // you caught the thief
            Thanking,     // someone comes to thank you
            Consoling,    // the thief got away: his victim comes back to you
            Questioned,   // the watch turned up: explain yourself
            Searching,    // LostChild: somewhere around here...
            Comforting,   // LostChild: you found him, crying
            Returning,    // LostChild: he's with you; back to his mother
            Fleeing,      // you ran from the toughs; they're after you
            Working,      // Harvest: the screen is dark, the work is done
        }

        /// <summary>Two locals fighting each other: vanilla's alarmed fight behavior, on opposing teams.</summary>
        private sealed class Brawl
        {
            public readonly List<Agent> Lads;
            public readonly Dictionary<Agent, Team> OldTeams = new Dictionary<Agent, Team>();
            public bool On;

            public Brawl(Agent a, Agent b) { Lads = new List<Agent> { a, b }; }

            public void Start(Mission mission)
            {
                if (On || Lads.Any(l => !l.IsActive())) return;
                var sides = new[] { mission.PlayerEnemyTeam, mission.PlayerAllyTeam };
                for (int i = 0; i < 2; i++)
                {
                    var lad = Lads[i];
                    if (!OldTeams.ContainsKey(lad)) OldTeams[lad] = lad.Team;
                    StripWeapons(lad, null);
                    lad.SetTeam(sides[i], true);
                    lad.SetMortalityState(Agent.MortalityState.Mortal);
                    HiredBeatDownPatch.AlsoGuarded.Add(lad);   // bruises, not knockouts, until someone steps in (Immortal would zero all damage)
                    ForceFight(lad);
                }
                On = true;
                LmmiLog.Info($"Street: {Lads[0].Name} and {Lads[1].Name} come to blows.");
            }

            public void Stop()
            {
                var mission = Mission.Current;
                if (Lads.FirstOrDefault(l => l.IsActive()) is Agent any) Instance?.QueueCalm(any.Position);
                foreach (var lad in Lads)
                {
                    if (!lad.IsActive()) continue;
                    try
                    {
                        var alarmed = lad.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<AlarmedBehaviorGroup>();
                        if (alarmed != null)
                        {
                            alarmed.DisableCalmDown = false;
                            alarmed.DisableScriptedBehavior();
                        }
                        lad.ResetEnemyCaches();
                        lad.InvalidateTargetAgent();
                        lad.InvalidateAIWeaponSelections();
                        lad.SetWatchState(Agent.WatchState.Patrolling);
                        if (OldTeams.TryGetValue(lad, out var old) && mission != null)
                            lad.SetTeam(new Team(old.MBTeam, BattleSideEnum.None, mission), true);
                        lad.SetMortalityState(Agent.MortalityState.Mortal);
                        HiredBeatDownPatch.AlsoGuarded.Remove(lad);
                        if (lad.Health < lad.HealthLimit * 0.35f) lad.Health = lad.HealthLimit * 0.35f;
                    }
                    catch (Exception ex) { LmmiLog.Error("Street: Brawl.Stop threw", ex); }
                }
                On = false;
            }
        }

        private sealed class Scene
        {
            public Kind Kind;
            public Step Step;
            public Settlement Settlement = null!;
            public Agent Requester = null!;
            public Agent? Victim;                            // Rescue: the girl; Thief: the one robbed (= requester)
            public List<Agent> Actors = new List<Agent>();   // Rescue: the men; Brawl: the two youths; Thief: the thief; Injured: the one who fell
            public bool Gang;                                // Rescue: the gang's own men
            public WorldPosition Escape;                     // Thief: where he's running to
            public float StepAt;
            public Agent? TalkTo;
            public bool Talking;
            public float TalkAt;
            public bool Chosen;                              // a choice was made in the current conversation
            public Brawl? Brawl;
            public bool GuideWaiting;
            public string Outcome = "";                      // which thanks to say
            public float StartedAt;
            public Agent? Watch;                             // a guard who came to see what the noise is
            public bool WatchInterrupted;
            public bool NoWatchNearby;
            public Agent Spot => Kind == Kind.Rescue || Kind == Kind.Debt || Kind == Kind.Shakedown ? (Victim ?? Actors[0]) : Actors[0];
            // A setup: the whole thing is staged to bring you somewhere quiet. Nothing shows it until you get there.
            public bool Ambush;
            public bool Revealed;                            // the toughs have shown their hand
            public List<Agent> Crew = new List<Agent>();     // toughs waiting nearby (the brawl's onlookers, the thief's friends)
            public bool RequesterGone;                       // whoever brought you here has gone
            public bool Baited;                              // Thief: he's reached his friends and waits for you
            public bool Hunched;                             // the Roguery hunch has been rolled
            public bool BeatOnly;                            // no talking, they just want to hurt a foreigner
            public int Toll;                                 // what they want for letting you walk
            public bool Mended;                              // Injured: your Medicine worked
            public int Debt;                                 // Debt: what he owes
            public float NextCryAt;                          // LostChild: his next wail
            public bool Spawned;                             // the actors were brought in (Raiders): they leave, not go home
            public float ThenAt = -1f;                       // something to do a moment from now (after a fade)
            public Action? Then;
            public float ThankRange = 35f;                   // Thanking: how far you may be before they give up on you
        }

        /// <summary>
        /// What happens after you leave it (or hand it over): runs on its own, outliving the scene. Returns true when done.
        /// </summary>
        private abstract class Aftermath
        {
            public float StartedAt;
            public abstract bool Tick(StreetEventsBehavior owner, float now);
            public abstract void End();
        }

        private const float SceneChance = 0.3f;
        private const float CooldownDays = 1f;
        private const float AskTimeout = 60f;
        private const float GuideTimeout = 150f;
        private const float ChaseTimeout = 45f;
        private const float ThiefSpeed = 0.7f;
        private const float CatchDistance = 2.5f;
        private const int PayOffGold = 100;
        private const int RentGold = 50;
        private const int SplitGold = 60;
        private const float SetupQuietDays = 10f;    // a crew that just tried you won't try again soon
        private const float HunchOnSetup = 0.6f;     // a good rogue smells a setup...
        private const float HunchOnGenuine = 0.15f;  // ...and sometimes one that isn't there
        private const uint ThiefContour = 0xFFE03C31u;
        private const float SearchTimeout = 120f;
        // A lost child: somewhere 60–110 m off; his mother only knows roughly (she takes you to within ~36 m), and you
        // hear him crying only once you're within 25 m. No outline: the crying is the clue.
        private const float LostMinDistance = 60f, LostMaxDistance = 110f, LostFallbackMinDistance = 45f;
        private const float LostGuideDistance = 36f;
        private const float LostArriveDistance = 40f;
        private const float LostCryDistance = 25f;
        private const float LostCryNearDistance = 12f;
        private const float LostSearchTimeout = 150f;
        private const float FleeTimeout = 25f;
        private const float ToughsCatchDistance = 2.2f;   // the toughs chasing you: this close and they have you
        private const float ToughsSprintFactor = 1.08f;   // running flat out, a touch quicker than a man in a hurry
        private const int ShakedownFine = 40;
        private const uint GuideContour = 0xFFE8B84Au;

        [NonSerialized] private Dictionary<string, double> _readyAtHours = new Dictionary<string, double>();
        // Setups: how many times you've stepped up here since the last one (word gets round who stops to help), and when it was.
        [NonSerialized] private Dictionary<string, int> _softTouch = new Dictionary<string, int>();
        [NonSerialized] private Dictionary<string, double> _setupAtHours = new Dictionary<string, double>();
        [NonSerialized] private Mission? _mission;
        [NonSerialized] private float _sceneTime;
        [NonSerialized] private float _rollAt;
        [NonSerialized] private bool _rolled;
        [NonSerialized] private bool _forced;
        [NonSerialized] private Kind? _forcedKind;
        [NonSerialized] private Scene? _scene;
        [NonSerialized] private readonly HashSet<Agent> _grateful = new HashSet<Agent>();

        /// <summary>Someone you stood up for, or stood with, this visit: they've no quarrel with your people now.</summary>
        public static bool IsGrateful(Agent? agent) => agent != null && Instance?._grateful.Contains(agent) == true;

        public static void MarkGrateful(Agent? agent)
        {
            if (agent != null && agent.IsActive()) Instance?._grateful.Add(agent);
        }
        [NonSerialized] private List<Aftermath> _aftermaths = new List<Aftermath>();
        [NonSerialized] private List<Action> _afterTalk = new List<Action>();

        // A street fistfight is on: leaving the scene means running from it.
        [NonSerialized] private bool _streetFight;
        [NonSerialized] private Settlement? _streetFightIn;
        [NonSerialized] private bool _caughtLeaving;

        /// <summary>One of our fistfights is running (vanilla would refuse Tab; see StreetFightLeavePatch) — or your men's.</summary>
        internal static bool StreetFightActive => Instance is StreetEventsBehavior self && (self._streetFight || self._menFight != null);

        // After a fight: bystanders calm down a moment later.
        [NonSerialized] private float _calmAt = -1f;
        [NonSerialized] private Vec3 _calmCenter;
        [NonSerialized] private float _calmRadius = CalmRadius;
        private const float CalmRadius = 45f;
        private const float CalmEveryone = 100000f;

        // Resisting arrest: the guards who came for you, and what happens once the scene ends.
        private enum Aftercare { None, Jail, Escaped }
        [NonSerialized] private readonly HashSet<Agent> _resisting = new HashSet<Agent>();
        [NonSerialized] private Settlement? _resistIn;
        [NonSerialized] private bool _knockedOutResisting;
        [NonSerialized] private float _endMissionAt = -1f;
        [NonSerialized] private Aftercare _aftercare;
        [NonSerialized] private bool _faded, _needFadeIn;   // the harvest's dark (outlives the scene if you leave)
        [NonSerialized] private Settlement? _aftercareIn;

        // What you did this time (for the magistrate), and the sentence being served (saved): vanilla lets a prisoner go
        // the moment their captors aren't at war with them — a sentence keeps you in until it's served.
        private enum Sentence { None, Serving, Ending }
        [NonSerialized] private int _downedResisting, _killedResisting;
        [NonSerialized] private float _incidentCrime;
        [NonSerialized] private bool _wentQuietly;
        [NonSerialized] private bool _sentencePrepared;
        [NonSerialized] private int _sentenceDays;
        [NonSerialized] private int _bloodPrice;
        [NonSerialized] private bool _headsman;
        // Who decides a killer's fate (the owner's character) and what follows: a commuted sentence is served, then
        // flogged (health to a sliver) and banished (town standing to Despised) — both saved with the sentence.
        private enum Verdict { None, Noble, Cruel, Ransom, Merciful, Fond, Lenient, Paid }
        [NonSerialized] private Verdict _verdict;
        [NonSerialized] private int _bloodOffer;
        [NonSerialized] private bool _commuted, _flogged;
        [NonSerialized] private Sentence _sentence;
        [NonSerialized] private double _sentenceUntilHours = -1;
        [NonSerialized] private string _sentenceIn = "";
        [NonSerialized] private float _sentenceCrime;
        [NonSerialized] private int _sentenceKilled;

        // Vanilla persuasion, one per kind of talk.
        [NonSerialized] private readonly NativePersuasion _talkDownMen = new NativePersuasion("street_men");
        [NonSerialized] private readonly NativePersuasion _talkDownYouths = new NativePersuasion("street_youths");
        [NonSerialized] private readonly NativePersuasion _explainToWatch = new NativePersuasion("street_watch");
        [NonSerialized] private readonly NativePersuasion _talkPastToughs = new NativePersuasion("street_lure");
        [NonSerialized] private readonly NativePersuasion _talkDownCollectors = new NativePersuasion("street_debt");
        [NonSerialized] private readonly NativePersuasion _talkDownGuard = new NativePersuasion("street_shakedown");

        private static StreetEventsBehavior? Instance => Campaign.Current?.GetCampaignBehavior<StreetEventsBehavior>();

        /// <summary>A street event is playing out (or its aftermath) — arrivals wait.</summary>
        public static bool IsBusy => Instance is StreetEventsBehavior self && (self._scene != null || self._aftermaths.Count > 0 || self._resistIn != null);

        /// <summary>Test mode: stage one as soon as you're free in a town or village centre.</summary>
        public static bool Queue(string? kind = null)
        {
            var self = Instance;
            if (self == null) return false;
            self._forced = true;
            self._forcedKind = kind != null && Enum.TryParse<Kind>(kind, true, out var k) ? k : (Kind?)null;
            return true;
        }

        /// <summary>The kinds of street event, for the test menu ("Any" first).</summary>
        public static string[] KindNames => new[] { "Any" }.Concat(Enum.GetNames(typeof(Kind))).ToArray();

        public static void ClearCooldowns() => Instance?._readyAtHours.Clear();

        /// <summary>Call off whatever is playing out (a feast is starting): scene and aftermaths, everyone released.</summary>
        public static void CancelAll()
        {
            var self = Instance;
            if (self == null) return;
            try
            {
                if (self._scene != null) { self._scene.Outcome = "called off"; self.Finish(self._scene); }
                foreach (var a in self._aftermaths)
                {
                    try { a.End(); } catch (Exception ex) { LmmiLog.Error("Street: ending an aftermath threw", ex); }
                }
                self._aftermaths.Clear();
                self._afterTalk.Clear();
            }
            catch (Exception ex) { LmmiLog.Error("Street: CancelAll threw", ex); }
        }

        public override void RegisterEvents()
        {
            CampaignEvents.MissionTickEvent.AddNonSerializedListener(this, OnMissionTick);
            CampaignEvents.ConversationEnded.AddNonSerializedListener(this, OnConversationEnded);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, OnMissionEnded);
            CampaignEvents.GameMenuOpened.AddNonSerializedListener(this, OnGameMenuOpened);
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnHeroPrisonerReleased);
        }

        /// <summary>Out of the cells before the sentence was served (escaped, ransomed, freed by vanilla): it no longer holds.</summary>
        private void OnHeroPrisonerReleased(Hero prisoner, PartyBase party, IFaction capturerFaction, EndCaptivityDetail detail, bool showNotification)
        {
            try
            {
                if (prisoner != Hero.MainHero || _sentence == Sentence.None) return;
                LmmiLog.Info($"Street: out of the cells of {_sentenceIn} before the sentence ended ({detail}) — the sentence no longer holds.");
                ClearSentence();
            }
            catch (Exception ex) { LmmiLog.Error("Street: OnHeroPrisonerReleased threw", ex); }
        }

        // ---- Scheduling ----

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
                    _rolled = false;
                    _rollAt = 20f + MBRandom.RandomFloat * 30f;
                    _scene = null;
                    _aftermaths.Clear();
                    _afterTalk.Clear();
                    _wadingIn = false;
                    _calmAt = -1f;
                    _endMissionAt = -1f;
                    _grateful.Clear();
                    _streetFight = false;
                    _work = null;       // the old scene's agents are gone with it
                    _menFight = null;
                    _armedFight = null;
                    HiredBeatDownPatch.AlsoGuarded.RemoveWhere(a => a.Mission != mission);
                }
                _sceneTime += dt;

                // Choices made in a conversation run once it has fully closed — vanilla tidies up the conversation
                // partner after its own end-of-conversation callbacks, so removing or re-teaming them earlier crashes.
                if (_afterTalk.Count > 0 && mission.Mode != MissionMode.Conversation
                    && Campaign.Current?.ConversationManager?.IsConversationInProgress != true)
                {
                    var actions = _afterTalk.ToList();
                    _afterTalk.Clear();
                    foreach (var action in actions)
                    {
                        try { action(); }
                        catch (Exception ex) { LmmiLog.Error("Street: after-conversation action threw", ex); }
                    }
                }

                if (_calmAt >= 0f && _sceneTime >= _calmAt)
                {
                    _calmAt = -1f;
                    float radius = _calmRadius;
                    _calmRadius = CalmRadius;
                    CalmBystanders(mission, _calmCenter, radius);
                }
                if (_endMissionAt >= 0f && _sceneTime >= _endMissionAt)
                {
                    _endMissionAt = -1f;
                    LmmiLog.Info("Street: the scene ends — you're taken to the cells.");
                    mission.EndMission();
                    return;
                }

                if (Agent.Main == null || !Agent.Main.IsActive()) return;

                for (int i = _aftermaths.Count - 1; i >= 0; i--)
                {
                    var a = _aftermaths[i];
                    bool done;
                    try { done = a.Tick(this, _sceneTime); }
                    catch (Exception ex) { LmmiLog.Error("Street: aftermath threw", ex); done = true; }
                    if (!done) continue;
                    try { a.End(); } catch (Exception ex) { LmmiLog.Error("Street: aftermath end threw", ex); }
                    _aftermaths.RemoveAt(i);
                }

                if (_scene != null)
                {
                    Update(_scene, mission);
                    return;
                }
                if (_aftermaths.Count > 0) return;

                if (!_forced && (_rolled || _sceneTime < _rollAt)) return;
                if (mission.Mode == MissionMode.Conversation || mission.Mode == MissionMode.Battle) return;
                if (mission.GetMissionBehavior<MissionFightHandler>()?.IsThereActiveFight() == true) return;
                if (ArrivalScenesBehavior.IsBusy || FeastBehavior.IsBusy || HiredSwordsBehavior.InScene) return;
                if (Campaign.Current?.GetCampaignBehavior<DisableMenuBehavior>()?.IsEscortActive == true) return;
                var location = CampaignMission.Current?.Location?.StringId;
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null || settlement.IsCastle || (location != "center" && location != "village_center")) return;

                bool forced = _forced;
                var forcedKind = _forcedKind;
                _forced = false;
                _forcedKind = null;
                _rolled = true;
                if (!forced && (!LmmiSettingsProvider.EnableStreetEvents || !LmmiSettingsProvider.EnableNotableDisposition)) return;
                if (!forced && !LmmiSettingsProvider.TestMode)
                {
                    if (_readyAtHours.TryGetValue(settlement.StringId, out var ready) && ready > CampaignTime.Now.ToHours) return;
                    if (MBRandom.RandomFloat > SceneChance) return;
                }

                // Whether this one is a setup is decided before anything is staged — the plea is the same either way.
                bool ambush = MBRandom.RandomFloat < AmbushChance(settlement);
                foreach (var kind in (forcedKind != null ? new[] { forcedKind.Value } : KindsFor(settlement)).OrderBy(_ => MBRandom.RandomFloat))
                {
                    var scene = Setup(kind, mission, settlement, ambush);
                    if (scene == null) continue;
                    _scene = scene;
                    _readyAtHours[settlement.StringId] = CampaignTime.Now.ToHours + CooldownDays * CampaignTime.HoursInDay;
                    return;
                }
                LmmiLog.Info($"Street: nothing could be staged in {settlement.Name} (not enough people about).");
                if (forced)
                    InformationManager.DisplayMessage(new InformationMessage("[LMMI Test] No street event could be staged here — find a busier spot (people 15–60m away)."));
            }
            catch (Exception ex)
            {
                LmmiLog.Error("StreetEventsBehavior.OnMissionTick threw", ex);
                try { if (_scene != null) Finish(_scene); } catch { }
                _scene = null;
            }
        }

        /// <summary>
        /// What can happen where: the watch and the moneylenders are town business; land, harvests and herds are the
        /// village's; everything else can happen in either. (Castles have their own life: CastleLifeBehavior.)
        /// </summary>
        private static IEnumerable<Kind> KindsFor(Settlement settlement)
        {
            yield return Kind.Rescue;
            yield return Kind.Brawl;
            yield return Kind.Thief;
            yield return Kind.Injured;
            yield return Kind.LostChild;
            if (settlement.IsTown)
            {
                yield return Kind.Debt;
                yield return Kind.Shakedown;
            }
            if (settlement.IsVillage)
            {
                yield return Kind.Dispute;
                yield return Kind.Harvest;
                yield return Kind.Raiders;
            }
        }

        // ---- Casting ----

        internal static bool IsTeen(Agent a) =>
            a.Character is CharacterObject co && co.Culture is CultureObject c
            && (co == c.TownsmanTeenager || co == c.VillagerMaleTeenager || co == c.TownswomanTeenager || co == c.VillagerFemaleTeenager);

        private static bool IsMaleTeen(Agent a) =>
            a.Character is CharacterObject co && co.Culture is CultureObject c && (co == c.TownsmanTeenager || co == c.VillagerMaleTeenager);

        internal static bool IsBeggar(Agent a) =>
            a.Character is CharacterObject co && co.Culture is CultureObject c && (co == c.Beggar || co == c.FemaleBeggar);

        private static bool Usable(Agent a) =>
            a.IsActive() && a.IsHuman && a != Agent.Main && !a.IsUsingGameObject && (a.Age >= 18f || IsTeen(a))
            && a.Character is CharacterObject co && !co.IsHero
            && a.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>() is DailyBehaviorGroup daily
            && daily.ScriptedBehavior == null   // already part of something (a feast, an arrival, an escort)
            && a.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<AlarmedBehaviorGroup>() != null;

        /// <summary>A child of the town or village, free to be part of something.</summary>
        private static bool IsChildLocal(Agent a) =>
            a.IsActive() && a.IsHuman && a != Agent.Main && !a.IsUsingGameObject
            && a.Character is CharacterObject co && !co.IsHero && co.Culture is CultureObject c
            && (co == c.TownsmanChild || co == c.TownswomanChild || co == c.VillagerMaleChild || co == c.VillagerFemaleChild)
            && a.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>() is DailyBehaviorGroup daily
            && daily.ScriptedBehavior == null;

        internal static bool IsLocal(Agent a) =>
            a.Character is CharacterObject co && (co.Occupation == Occupation.Townsfolk || co.Occupation == Occupation.Villager);

        internal static bool IsAdultLocal(Agent a) => IsLocal(a) && a.Age >= 18f && !IsTeen(a) && !IsBeggar(a);

        private static float Dist(Agent a, Agent b) => a.Position.Distance(b.Position);

        /// <summary>Somewhere walkable 25–90 m off, away from you: where another person is standing.</summary>
        private static WorldPosition? FarPoint(Mission mission, Agent from, params Agent[] not)
        {
            var main = Agent.Main;
            var away = (from.Position - main.Position).AsVec2;
            away = away.LengthSquared < 0.01f ? Vec2.Forward : away.Normalized();
            var spot = mission.Agents
                .Where(a => a.IsActive() && a.IsHuman && a != main && a != from && !not.Contains(a)
                            && Dist(a, from) > 25f && Dist(a, main) > 30f && Dist(a, main) < 90f)
                .OrderByDescending(a => Vec2.DotProduct((a.Position - main.Position).AsVec2.Normalized(), away))
                .FirstOrDefault();
            return spot?.GetWorldPosition();
        }

        internal static bool IsWatch(Agent a) =>
            a.Character is CharacterObject co && !co.IsHero
            && (co.Occupation == Occupation.Guard || co.Occupation == Occupation.Soldier || co.Occupation == Occupation.PrisonGuard);

        /// <summary>Trouble starts where the watch isn't looking.</summary>
        private static bool NearWatch(Mission mission, Vec3 at, float within = 25f) =>
            mission.Agents.Any(a => a.IsActive() && a.IsHuman && IsWatch(a) && a.Position.Distance(at) < within);

        private static Agent? FindGuard(Mission mission, Vec3 near, float within, params Agent[] not) =>
            mission.Agents
                .Where(a => a.IsActive() && a.IsHuman && a != Agent.Main && !not.Contains(a) && a.Character is CharacterObject co && !co.IsHero
                            && (co.Occupation == Occupation.Guard || co.Occupation == Occupation.Soldier)
                            && a.Position.Distance(near) < within
                            && a.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>() != null)
                .OrderBy(a => a.Position.Distance(near))
                .FirstOrDefault();

        /// <summary>
        /// How likely it is that whoever runs up to you is bait. Higher for a foreigner where your people are resented
        /// and for a known soft touch (word gets round who stops to help); lower for a famous name, and for a while after
        /// a crew here has tried it.
        /// </summary>
        private float AmbushChance(Settlement settlement)
        {
            int percent = LmmiSettingsProvider.StreetAmbushPercent;
            if (percent <= 0) return 0f;
            float chance = percent / 100f;
            if (settlement.Culture != Hero.MainHero.Culture) chance += Math.Min(0.25f, 0.1f * ResentmentIn(settlement));
            if (_softTouch.TryGetValue(settlement.StringId, out var soft)) chance += 0.03f * Math.Min(5, soft);
            int tier = Clan.PlayerClan?.Tier ?? 0;
            if (tier > 3) chance -= 0.05f * (tier - 3);
            if (_setupAtHours.TryGetValue(settlement.StringId, out var at) && CampaignTime.Now.ToHours - at < SetupQuietDays * CampaignTime.HoursInDay)
                chance *= 0.3f;
            if (LmmiSettingsProvider.TestMode) chance = Math.Max(chance, 0.5f);
            chance = Math.Max(0.03f, Math.Min(0.6f, chance));
            LmmiLog.Info($"Street: setup chance in {settlement.Name} {chance:P0}.");
            return chance;
        }

        private void NoteSetup(Settlement settlement)
        {
            _softTouch[settlement.StringId] = 0;
            _setupAtHours[settlement.StringId] = CampaignTime.Now.ToHours;
        }

        private void NoteSoftTouch(Settlement settlement)
        {
            _softTouch.TryGetValue(settlement.StringId, out var soft);
            _softTouch[settlement.StringId] = Math.Min(5, soft + 1);
        }

        private static ItemObject? VillageCrop(Settlement settlement) => settlement.Village?.VillageType?.PrimaryProduction;

        private static CharacterObject? LooterCharacter()
        {
            var clan = Clan.BanditFactions.FirstOrDefault(c => c.StringId == "looters");
            return clan?.Culture?.BasicTroop ?? CharacterObject.Find("looter");
        }

        /// <summary>The village's headman (or a notable) hears how you handled it.</summary>
        private static void Headman(Scene sc, int delta)
        {
            var headman = sc.Settlement.Notables.FirstOrDefault(n => n.IsAlive && n.IsHeadman) ?? sc.Settlement.Notables.FirstOrDefault(n => n.IsAlive);
            if (headman == null || delta == 0) return;
            ChangeRelationAction.ApplyPlayerRelation(headman, delta, affectRelatives: false, showQuickNotification: true);
        }

        /// <summary>The debtor, the lost child and the crooked watchman; the dispute, the harvest and the raiders.</summary>
        private bool SetupMore(Kind kind, Scene scene, Mission mission, List<Agent> people, bool ambush, int toughs,
            Func<Agent, bool> IsTough, Func<Agent, bool> Rough, Func<Agent, int, Agent[], List<Agent>> CrewNear,
            Func<Agent[], Agent?> PickRequester)
        {
            var main = Agent.Main;
            switch (kind)
            {
                case Kind.Debt:
                {
                    // A man cornered by the moneylender's collectors (the gang's own, if any are about).
                    var debtor = people
                        .Where(a => IsAdultLocal(a) && !a.IsFemale && Dist(a, main) > 18f && Dist(a, main) < 60f && !NearWatch(mission, a.Position))
                        .OrderBy(a => Math.Abs(Dist(a, main) - 35f)).FirstOrDefault();
                    if (debtor == null) return false;
                    var men = people.Where(a => a != debtor && Rough(a) && Dist(a, debtor) < 45f)
                        .OrderBy(a => IsTough(a) ? 0 : 1).ThenBy(a => Dist(a, debtor)).Take(Math.Min(3, toughs)).ToList();
                    if (men.Count < 2) return false;
                    var requester = PickRequester(men.Append(debtor).ToArray());
                    if (requester == null) return false;
                    scene.Actors = men;
                    scene.Victim = debtor;
                    scene.Requester = requester;
                    scene.Gang = men.Count(IsTough) >= 2;
                    scene.Ambush = ambush;   // a setup: he owes nobody anything
                    scene.Debt = 100 + MBRandom.RandomInt(16) * 10;
                    float baseAngle = MBRandom.RandomFloat * 6.2832f;
                    for (int i = 0; i < men.Count; i++)
                    {
                        var pos = debtor.GetWorldPosition();
                        pos.SetVec2(debtor.Position.AsVec2 + Vec2.FromRotation(baseAngle + i * (3.6f / men.Count)) * 1.3f);
                        Direct(men[i])?.GoTo(pos, run: false, face: debtor, loop: i == 0 ? "act_conversation_threat_point" : null);
                    }
                    Direct(debtor)?.Hold(face: men[0], loop: "act_bullied");
                    return true;
                }

                case Kind.LostChild:
                {
                    // A little one wandered off — well off; the mother is frantic. (Somewhere 60–110 m away if there's a
                    // child that far; nearer only if there isn't.)
                    var child = mission.Agents
                        .Where(a => IsChildLocal(a) && Dist(a, main) > LostFallbackMinDistance && Dist(a, main) < LostMaxDistance
                                    && !NearWatch(mission, a.Position, 15f))
                        .OrderBy(a => Dist(a, main) >= LostMinDistance ? 0 : 1).ThenBy(_ => MBRandom.RandomFloat).FirstOrDefault();
                    if (child == null) return false;
                    // A setup: the little one is bait, and the men loitering near him aren't there by chance.
                    if (ambush)
                    {
                        scene.Crew = CrewNear(child, toughs, new Agent[0]);
                        scene.Ambush = scene.Crew.Count >= 2;
                        if (!scene.Ambush) scene.Crew = new List<Agent>();
                    }
                    // His mother, and only ever his mother.
                    var requester = people
                        .Where(a => IsAdultLocal(a) && a.IsFemale && !scene.Crew.Contains(a) && Dist(a, main) > 4f && Dist(a, main) < 25f)
                        .OrderBy(_ => MBRandom.RandomFloat).FirstOrDefault();
                    if (requester == null) return false;
                    scene.Actors = new List<Agent> { child };
                    scene.Requester = requester;
                    Direct(child)?.Hold(loop: "act_scared_idle_2");   // lost, frightened, and staying put
                    foreach (var c in scene.Crew) Direct(c)?.Hold(face: child);
                    return true;
                }

                case Kind.Dispute:
                {
                    // Two neighbours at each other's throats over a boundary stone.
                    var pool = people.Where(a => IsAdultLocal(a) && !a.IsFemale && Dist(a, main) > 15f && Dist(a, main) < 60f
                                                 && !NearWatch(mission, a.Position)).ToList();
                    Agent? a1 = null, a2 = null;
                    foreach (var man in pool.OrderBy(_ => MBRandom.RandomFloat))
                    {
                        var other = pool.Where(o => o != man && Dist(o, man) < 25f).OrderBy(o => Dist(o, man)).FirstOrDefault();
                        if (other == null) continue;
                        a1 = man; a2 = other;
                        break;
                    }
                    if (a1 == null || a2 == null) return false;
                    // A setup: nobody's arguing about any stone, and the onlookers aren't just looking.
                    if (ambush)
                    {
                        scene.Crew = CrewNear(a1, Math.Max(0, toughs - 2), new[] { a2 });
                        scene.Ambush = true;
                    }
                    var requester = PickRequester(scene.Crew.Append(a1).Append(a2).ToArray());
                    if (requester == null) return false;
                    scene.Actors = new List<Agent> { a1, a2 };
                    scene.Requester = requester;
                    var toA2 = (a2.Position - a1.Position).AsVec2;
                    toA2 = toA2.LengthSquared < 0.01f ? Vec2.Forward : toA2.Normalized();
                    var pos = a1.GetWorldPosition();
                    pos.SetVec2(a1.Position.AsVec2 + toA2 * 1.5f);
                    Direct(a1)?.Hold(face: a2, loop: "act_argue");
                    Direct(a2)?.GoTo(pos, run: false, face: a1, loop: "act_argue_2");
                    foreach (var c in scene.Crew) Direct(c)?.Hold(face: a1);
                    return true;
                }

                case Kind.Harvest:
                {
                    // The crop's still standing and a storm's coming: every pair of hands.
                    if (VillageCrop(scene.Settlement) == null) return false;
                    var hand = people.Where(a => IsAdultLocal(a) && Dist(a, main) > 30f && Dist(a, main) < 70f)
                        .OrderBy(_ => MBRandom.RandomFloat).FirstOrDefault();
                    if (hand == null) return false;
                    if (ambush)
                    {
                        scene.Crew = CrewNear(hand, toughs, new Agent[0]);
                        scene.Ambush = scene.Crew.Count >= 2;
                        if (!scene.Ambush) scene.Crew = new List<Agent>();
                    }
                    var requester = PickRequester(scene.Crew.Append(hand).ToArray());
                    if (requester == null) return false;
                    scene.Actors = new List<Agent> { hand };
                    scene.Requester = requester;
                    Direct(hand)?.Hold();
                    foreach (var c in scene.Crew) Direct(c)?.Hold(face: hand);
                    return true;
                }

                case Kind.Raiders:
                {
                    // Looters at the edge of the village, driving off the flock.
                    var looter = LooterCharacter();
                    if (looter == null) return false;
                    var edge = mission.Agents
                        .Where(a => a.IsActive() && a.IsHuman && a != main && Dist(a, main) > 40f && Dist(a, main) < 75f)
                        .OrderBy(_ => MBRandom.RandomFloat).FirstOrDefault();
                    if (edge == null) return false;
                    var requester = PickRequester(new Agent[0]);
                    if (requester == null) return false;
                    int n = 3 + Math.Min(2, (Clan.PlayerClan?.Tier ?? 0) / 2);
                    var away = (edge.Position - main.Position).AsVec2;
                    away = away.LengthSquared < 0.01f ? Vec2.Forward : away.Normalized();
                    var men = new List<Agent>();
                    for (int i = 0; i < n; i++)
                    {
                        var m = SceneSpawner.Spawn(mission, looter, SceneSpawner.Around(mission, edge.Position, i, n, 1.1f, away),
                            (-away).RotationInRadians, civilian: false);
                        if (m != null) men.Add(m);
                    }
                    if (men.Count < 2)
                    {
                        foreach (var m in men) m.FadeOut(true, true);
                        return false;
                    }
                    scene.Actors = men;
                    scene.Requester = requester;
                    scene.Spawned = true;
                    scene.Ambush = false;   // they're bandits already
                    foreach (var m in men) Direct(m)?.Hold();
                    return true;
                }

                case Kind.Shakedown:
                {
                    // One of the watch "fining" someone for nothing — and no other guard in sight to see it.
                    var guard = mission.Agents
                        .Where(a => a.IsActive() && a.IsHuman && a != main && IsWatch(a) && !a.IsUsingGameObject
                                    && a.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>() is DailyBehaviorGroup g
                                    && g.ScriptedBehavior == null
                                    && Dist(a, main) > 18f && Dist(a, main) < 60f
                                    && !mission.Agents.Any(o => o != a && o.IsActive() && o.IsHuman && IsWatch(o) && Dist(o, a) < 20f))
                        .OrderBy(_ => MBRandom.RandomFloat).FirstOrDefault();
                    if (guard == null) return false;
                    var mark = people.Where(a => (IsAdultLocal(a) || IsBeggar(a)) && Dist(a, guard) < 30f)
                        .OrderBy(a => Dist(a, guard)).FirstOrDefault();
                    if (mark == null) return false;
                    var requester = PickRequester(new[] { mark });
                    if (requester == null) return false;
                    scene.Actors = new List<Agent> { guard };
                    scene.Victim = mark;
                    scene.Requester = requester;
                    scene.Ambush = false;   // the watch doesn't do setups
                    var pos = mark.GetWorldPosition();
                    pos.SetVec2(mark.Position.AsVec2 + Vec2.FromRotation(MBRandom.RandomFloat * 6.2832f) * 1.2f);
                    Direct(guard)?.GoTo(pos, run: false, face: mark, loop: "act_conversation_threat_point");
                    Direct(mark)?.Hold(face: guard, loop: "act_bullied");
                    return true;
                }
            }
            return false;
        }

        private Scene? Setup(Kind kind, Mission mission, Settlement settlement, bool ambush)
        {
            var main = Agent.Main;
            var people = mission.Agents.Where(Usable).ToList();
            int tier = Clan.PlayerClan?.Tier ?? 0;
            int toughs = tier >= 4 ? 4 : tier >= 2 ? 3 : 2;
            var scene = new Scene { Kind = kind, Settlement = settlement, Step = Step.Asking, StepAt = _sceneTime };
            var bodyguard = settlement.Culture?.GangleaderBodyguard;
            bool IsTough(Agent a) => a.Character is CharacterObject co && (co.Occupation == Occupation.Gangster || co == bodyguard);
            bool Rough(Agent a) => !a.IsFemale && a.Age >= 18f && !IsTeen(a) && (IsTough(a) || IsAdultLocal(a));

            // Toughs loitering near a spot (a setup's crew): the gang's own first.
            List<Agent> CrewNear(Agent near, int n, Agent[] not) => people
                .Where(a => Rough(a) && a != near && !not.Contains(a) && Dist(a, near) < 20f)
                .OrderBy(a => IsTough(a) ? 0 : 1).ThenBy(a => Dist(a, near)).Take(n).ToList();

            Agent? PickRequester(Agent[] not) => people
                .Where(a => IsAdultLocal(a) && !not.Contains(a) && Dist(a, main) > 4f && Dist(a, main) < 25f)
                .OrderBy(_ => MBRandom.RandomFloat).FirstOrDefault();

            switch (kind)
            {
                case Kind.Rescue:
                {
                    var victim = people
                        .Where(a => IsAdultLocal(a) && a.IsFemale && Dist(a, main) > 18f && Dist(a, main) < 60f && !NearWatch(mission, a.Position))
                        .OrderBy(a => Math.Abs(Dist(a, main) - 35f)).FirstOrDefault();
                    if (victim == null) return null;
                    var men = people.Where(a => a != victim && !a.IsFemale && a.Age >= 18f && !IsTeen(a) && Dist(a, victim) < 45f).ToList();
                    var gang = men.Where(IsTough).OrderBy(a => Dist(a, victim)).Take(toughs).ToList();
                    scene.Gang = gang.Count >= 2;
                    scene.Actors = scene.Gang ? gang
                        : men.Where(IsAdultLocal).OrderBy(a => Dist(a, victim)).Take(toughs).ToList();
                    if (scene.Actors.Count < 2) return null;
                    var requester = PickRequester(scene.Actors.Append(victim).ToArray());
                    if (requester == null) return null;
                    scene.Victim = victim;
                    scene.Requester = requester;
                    scene.Ambush = ambush;   // a setup: she's one of them

                    // The men close in around her; she's frightened.
                    float baseAngle = MBRandom.RandomFloat * 6.2832f;
                    for (int i = 0; i < scene.Actors.Count; i++)
                    {
                        var pos = victim.GetWorldPosition();
                        var dir = Vec2.FromRotation(baseAngle + i * (4.2f / scene.Actors.Count));
                        pos.SetVec2(victim.Position.AsVec2 + dir * 1.4f);
                        Direct(scene.Actors[i])?.GoTo(pos, run: false, face: victim, loop: i == 0 ? "act_conversation_threat_point" : null);
                    }
                    Direct(victim)?.Hold(face: scene.Actors[0], loop: "act_scared_idle_1");
                    break;
                }

                case Kind.Brawl:
                {
                    if (mission.PlayerEnemyTeam == null || mission.PlayerAllyTeam == null) return null;
                    bool InRange(Agent a) => Dist(a, main) > 15f && Dist(a, main) < 60f && !NearWatch(mission, a.Position);
                    // A pair of equals: two teenage boys (vanilla's youths can fight), or two young men of 18-20.
                    // Never a grown man against a boy.
                    Agent? a1 = null, a2 = null;
                    foreach (var pool in new[]
                    {
                        people.Where(a => IsMaleTeen(a) && InRange(a)).ToList(),
                        people.Where(a => IsAdultLocal(a) && !a.IsFemale && a.Age < 21f && InRange(a)).ToList(),
                    }.OrderBy(_ => MBRandom.RandomFloat))
                    {
                        foreach (var lad in pool)
                        {
                            var other = pool.Where(o => o != lad && Dist(o, lad) < 30f).OrderBy(o => Dist(o, lad)).FirstOrDefault();
                            if (other == null) continue;
                            a1 = lad; a2 = other;
                            break;
                        }
                        if (a1 != null) break;
                    }
                    if (a1 == null || a2 == null) return null;
                    // A setup: the brawl is for show, and a few of the onlookers aren't just looking.
                    if (ambush)
                    {
                        scene.Crew = CrewNear(a1, Math.Max(0, toughs - 2), new[] { a2 });
                        scene.Ambush = true;
                    }
                    var requester = PickRequester(scene.Crew.Append(a1).Append(a2).ToArray());
                    if (requester == null) return null;
                    scene.Actors = new List<Agent> { a1, a2 };
                    scene.Brawl = new Brawl(a1, a2);
                    scene.Requester = requester;

                    // One goes after the other; they square up, then it comes to blows (see Update).
                    Direct(a1)?.Hold(face: a2, loop: "act_argue");
                    Direct(a2)?.Follow(a1, 1.3f, run: true);
                    foreach (var c in scene.Crew) Direct(c)?.Hold(face: a1);
                    break;
                }

                case Kind.Thief:
                {
                    var victim = people.Where(a => IsAdultLocal(a) && Dist(a, main) > 6f && Dist(a, main) < 25f && !NearWatch(mission, a.Position))
                        .OrderBy(_ => MBRandom.RandomFloat).FirstOrDefault();
                    if (victim == null) return null;
                    // A street kid or a beggar, if there's one about.
                    var thief = people.Where(a => a != victim && (IsTeen(a) || IsBeggar(a)) && Dist(a, victim) < 25f)
                                    .OrderBy(a => Dist(a, victim)).FirstOrDefault()
                                ?? people.Where(a => a != victim && IsAdultLocal(a) && !a.IsFemale && a.Age <= 30f && Dist(a, victim) < 20f)
                                    .OrderBy(a => a.Age).FirstOrDefault();
                    if (thief == null) return null;
                    if (ambush)
                    {
                        // A setup: he runs to where his friends are waiting, out of the watch's sight.
                        var lead = people
                            .Where(a => Rough(a) && a != victim && a != thief && Dist(a, thief) > 25f && Dist(a, thief) < 60f
                                        && Dist(a, main) > 25f && !NearWatch(mission, a.Position))
                            .OrderBy(a => IsTough(a) ? 0 : 1).ThenBy(_ => MBRandom.RandomFloat).FirstOrDefault();
                        var crew = lead != null ? CrewNear(lead, toughs - 1, new[] { victim, thief }) : new List<Agent>();
                        if (lead != null && crew.Count >= 1)
                        {
                            scene.Crew = new List<Agent> { lead };
                            scene.Crew.AddRange(crew);
                            scene.Escape = lead.GetWorldPosition();
                            scene.Ambush = true;
                            Direct(lead)?.Hold();
                            foreach (var c in crew) Direct(c)?.Follow(lead, 1.6f, run: false);
                        }
                    }
                    if (!scene.Ambush)
                    {
                        var escape = FarPoint(mission, thief, victim);
                        if (escape == null) return null;
                        scene.Escape = escape.Value;
                    }
                    scene.Actors = new List<Agent> { thief };
                    scene.Victim = victim;
                    scene.Requester = victim;
                    Direct(thief)?.Hold();   // lingering, trying to look innocent — he runs when you come for him
                    break;
                }

                case Kind.Injured:
                {
                    // A local who came off a ladder or a cart, sitting in the street and unable to stand.
                    var hurt = people
                        .Where(a => IsAdultLocal(a) && !a.IsFemale && Dist(a, main) > 18f && Dist(a, main) < 60f && !NearWatch(mission, a.Position))
                        .OrderBy(a => Math.Abs(Dist(a, main) - 35f)).FirstOrDefault();
                    if (hurt == null) return null;
                    // A setup: he's fine, and his friends are standing around him.
                    if (ambush)
                    {
                        scene.Crew = CrewNear(hurt, toughs - 1, new Agent[0]);
                        scene.Ambush = scene.Crew.Count >= 1;
                        if (!scene.Ambush) scene.Crew = new List<Agent>();
                    }
                    var requester = PickRequester(scene.Crew.Append(hurt).ToArray());
                    if (requester == null) return null;
                    scene.Actors = new List<Agent> { hurt };
                    scene.Requester = requester;
                    Direct(hurt)?.Hold(loop: "act_sit_beggar_idle");
                    foreach (var c in scene.Crew) Direct(c)?.Hold(face: hurt);
                    break;
                }
            }

            if (kind == Kind.Debt || kind == Kind.LostChild || kind == Kind.Shakedown
                || kind == Kind.Dispute || kind == Kind.Harvest || kind == Kind.Raiders)
            {
                if (!SetupMore(kind, scene, mission, people, ambush, toughs, IsTough, Rough, CrewNear, PickRequester)) return null;
            }

            if (scene.Ambush)
            {
                // Some just want to hurt one of your kind; the rest want your purse.
                scene.BeatOnly = Foreign(scene) && ResentmentIn(settlement) >= 1f && MBRandom.RandomFloat < 0.5f;
                scene.Toll = Math.Max(50, Math.Min(400, (int)(Hero.MainHero.Gold * 0.15f) / 10 * 10));
                NoteSetup(settlement);
            }
            scene.TalkTo = scene.Requester;
            scene.StartedAt = _sceneTime;
            Direct(scene.Requester)?.Follow(main, 2f, run: true);
            LmmiLog.Info($"Street: {kind} staged in {settlement.Name} — requester {scene.Requester.Name}"
                         + (scene.Victim != null && scene.Victim != scene.Requester ? $", victim {scene.Victim.Name}" : "")
                         + $", actors {string.Join(", ", scene.Actors.Select(a => $"{a.Name}{(IsTeen(a) ? " (youth)" : IsBeggar(a) ? " (beggar)" : "")}"))}{(scene.Gang ? " (gang)" : "")}"
                         + (scene.Ambush ? $" — a SETUP ({(scene.BeatOnly ? "a beating" : $"toll {scene.Toll}")}; crew: {string.Join(", ", scene.Crew.Select(a => a.Name))})" : "") + ".");
            return scene;
        }

        // ---- Directing ----

        internal static LmmiStageBehavior? Direct(Agent? agent)
        {
            if (agent == null || !agent.IsActive()) return null;
            var group = agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>();
            if (group == null) return null;
            if (agent.IsUsingGameObject) agent.StopUsingGameObject(false);
            var stage = group.GetBehavior<LmmiStageBehavior>() ?? group.AddBehavior<LmmiStageBehavior>();
            if (group.ScriptedBehavior != stage) group.SetScriptedBehavior<LmmiStageBehavior>();
            return stage;
        }

        /// <summary>Their part in the scene, if they're playing one — without taking them over (unlike <see cref="Direct"/>).</summary>
        internal static LmmiStageBehavior? Staged(Agent? agent)
        {
            if (agent == null || !agent.IsActive()) return null;
            var group = agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>();
            return group?.ScriptedBehavior as LmmiStageBehavior;
        }

        internal static void Release(Agent? agent)
        {
            if (agent == null || !agent.IsActive()) return;
            try
            {
                Contour(agent, null);
                var group = agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>();
                if (group?.ScriptedBehavior is LmmiStageBehavior) group.DisableScriptedBehavior();
            }
            catch (Exception ex) { LmmiLog.Error("Street: Release threw", ex); }
        }

        private static void Contour(Agent? agent, uint? color)
        {
            try { if (agent != null && agent.IsActive()) agent.AgentVisuals?.SetContourColor(color, true); }
            catch (Exception ex) { LmmiLog.Error("Street: contour threw", ex); }
        }

        /// <summary>A line called out in passing — vanilla's portrait notification, no conversation.</summary>
        internal static void Bark(Agent? who, TextObject text) => Bark(who, text, portrait: !IsLittleOne(who));

        /// <param name="portrait">Show who's speaking. Off for a voice you hear but can't place — and always for
        /// children: vanilla draws a portrait from the character template, and a child's template comes out as some
        /// random grown man.</param>
        internal static void Bark(Agent? who, TextObject text, bool portrait)
        {
            if (who == null || !who.IsActive()) return;
            SetWords(text);
            MBInformationManager.AddQuickInformation(text, 0, portrait ? who.Character : null, null, "");
        }

        /// <summary>A child (not a youth): their portrait can't be drawn.</summary>
        private static bool IsLittleOne(Agent? a) =>
            a != null && (a.Age < 13f || (a.Character is CharacterObject co && co.Culture is CultureObject c
                                          && (co == c.TownsmanChild || co == c.TownswomanChild || co == c.VillagerMaleChild || co == c.VillagerFemaleChild)));

        private void Finish(Scene sc)
        {
            // Cut short in the middle of the field work (a feast, an error): give the player back, and the light.
            if (_work != null) StopFieldWork();
            if (_menFight != null && _menFight.Scene == sc && !_menFight.Started) CallOffMen();
            if (_faded) { _faded = false; ScreenFadeController.BeginFadeIn(0.6f); }
            if (sc.Brawl?.On == true) sc.Brawl.Stop();
            if (!sc.RequesterGone) Release(sc.Requester);
            Release(sc.Victim);
            Release(sc.Watch);
            foreach (var a in sc.Actors)
            {
                if (sc.Spawned) { if (a.IsActive()) a.FadeOut(true, true); }
                else Release(a);
            }
            foreach (var a in sc.Crew) Release(a);
            if (_scene == sc) _scene = null;
            LmmiLog.Info($"Street: {sc.Kind} over ({sc.Outcome}).");
        }

        private void Go(Scene sc, Step step)
        {
            sc.Step = step;
            sc.StepAt = _sceneTime;
        }

        private void Thank(Scene sc, Agent? who, string outcome)
        {
            sc.Outcome = outcome;
            MarkGrateful(who);
            MarkGrateful(sc.Requester);
            if (who == null || !who.IsActive()) { Finish(sc); return; }
            sc.TalkTo = who;
            // From across the village (they ran when the fighting started), they hurry back — and don't give up on
            // you just because they're far off.
            float far = Agent.Main != null ? Dist(who, Agent.Main) : 0f;
            sc.ThankRange = Math.Max(35f, far + 15f);
            Direct(who)?.Follow(Agent.Main!, 2f, run: far > 15f);
            Go(sc, Step.Thanking);
        }

        /// <summary>
        /// Frightened by a fight: back to their day. Vanilla only stops their running — they stay alarmed (and an
        /// alarmed agent's scripted daily part never runs), so this resets the alarm outright.
        /// </summary>
        private static void CalmAgent(Agent a)
        {
            if (a == null || !a.IsActive()) return;
            try
            {
                var alarmed = a.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<AlarmedBehaviorGroup>();
                if (alarmed != null)
                {
                    alarmed.DisableCalmDown = false;
                    alarmed.DisableScriptedBehavior();
                    alarmed.ResetAlarmFactor();
                }
                a.SetWatchState(Agent.WatchState.Patrolling);
            }
            catch (Exception ex) { LmmiLog.Error("Street: CalmAgent threw", ex); }
        }

        /// <summary>Hand agents over to an aftermath: the scene no longer releases them.</summary>
        private void HandOver(Scene sc, Aftermath aftermath, params Agent?[] agents)
        {
            foreach (var a in agents)
            {
                if (a == null) continue;
                sc.Actors.Remove(a);
                if (sc.Victim == a) sc.Victim = null;
            }
            aftermath.StartedAt = _sceneTime;
            _aftermaths.Add(aftermath);
        }

        // ---- The scene, tick by tick ----

        private void Update(Scene sc, Mission mission)
        {
            var main = Agent.Main;
            float inStep = _sceneTime - sc.StepAt;

            // A conversation we asked for that never opened.
            if (sc.Talking && mission.Mode != MissionMode.Conversation && _sceneTime - sc.TalkAt > 5f) sc.Talking = false;

            // Something to do a moment from now (the screen went dark for it).
            if (sc.ThenAt >= 0f && _sceneTime >= sc.ThenAt)
            {
                var then = sc.Then;
                sc.Then = null;
                sc.ThenAt = -1f;
                then?.Invoke();
                return;
            }
            if (sc.Step == Step.Working)
            {
                TickFieldWork();
                return;
            }

            // An armed fight: you're beaten down, never killed.
            if (sc.Step == Step.Fighting && _armedFight == sc && !LmmiSettingsProvider.TestImmortal
                && main.Health <= Math.Max(6f, main.HealthLimit * 0.12f))
            {
                LmmiLog.Info("Street: beaten down.");
                mission.GetMissionBehavior<MissionFightHandler>()?.EndFight();
                return;
            }

            // The two youths come to blows once they're face to face.
            if (sc.Brawl != null && !sc.Brawl.On && sc.Step <= Step.Guiding && sc.Actors.Count == 2 && sc.Actors.All(a => a.IsActive()))
            {
                if (Dist(sc.Actors[0], sc.Actors[1]) < 1.9f) sc.Brawl.Start(mission);
                else if (inStep > 30f && sc.Step == Step.Asking && !sc.Talking && Dist(sc.Actors[0], sc.Actors[1]) > 6f)
                {
                    LmmiLog.Info("Street: the youths never met up; calling it off.");
                    Finish(sc);
                    return;
                }
            }

            if (CheckWatch(sc, mission)) return;

            switch (sc.Step)
            {
                case Step.Asking:
                    if (!sc.Talking && inStep > AskTimeout) { sc.Outcome = "they never reached you"; Finish(sc); return; }
                    TryTalk(sc, mission);
                    break;

                case Step.Guiding:
                {
                    var spot = sc.Spot;
                    if (!spot.IsActive()) { sc.Outcome = "the scene broke up"; Finish(sc); return; }
                    float toSpot = Dist(main, spot);
                    // A lost child: she only knows roughly — once she's taken you as near as she thinks (and you're
                    // with her), the rest is up to you.
                    bool there = sc.Kind == Kind.LostChild
                        ? toSpot < LostArriveDistance || (!sc.GuideWaiting && Staged(sc.Requester)?.Arrived == true && Dist(main, sc.Requester) < 6f)
                        : sc.Kind == Kind.Raiders ? toSpot < 9f : toSpot < (sc.Kind == Kind.Brawl ? 4f : 4.5f);
                    if (there)
                    {
                        BeginConfrontation(sc);
                        break;
                    }
                    if (inStep > GuideTimeout || toSpot > (sc.Kind == Kind.LostChild ? LostMaxDistance + 40f : 90f))
                    {
                        Say("{=lmmi_street_too_late}By the time you get there, it's over. Nobody looks at you.",
                        "{=lmmi_street_too_late_2}You arrive to an empty street. Whatever happened, it's over.",
                        "{=lmmi_street_too_late_3}Too late. The street's quiet again, and nobody meets your eye.");
                        if (!sc.Ambush) TownStandingBehavior.Adjust(sc.Settlement, -1f, "said they'd help, and didn't");
                        sc.Outcome = "you never came";
                        LetItPlayOut(sc);
                        return;
                    }
                    // Lead the way; wait if you fall behind.
                    float behind = Dist(main, sc.Requester);
                    if (!sc.GuideWaiting && behind > 14f) { sc.GuideWaiting = true; Direct(sc.Requester)?.Hold(face: main); }
                    else if (sc.GuideWaiting && behind < 7f) { sc.GuideWaiting = false; Direct(sc.Requester)?.Follow(spot, GuideDistance(sc), run: true); }
                    break;
                }

                case Step.Confronting:
                case Step.Caught:
                case Step.Comforting:
                case Step.Thanking:
                case Step.Consoling:
                case Step.Questioned:
                    if (sc.TalkTo == null || !sc.TalkTo.IsActive()) { sc.Outcome = "they left"; Finish(sc); return; }
                    // Someone hurrying back to thank you gets some slack while they close the distance (and only that).
                    if (sc.Step == Step.Thanking) sc.ThankRange = Math.Max(35f, Math.Min(sc.ThankRange, Dist(main, sc.TalkTo) + 15f));
                    if (!sc.Talking && (inStep > 45f || Dist(main, sc.TalkTo) > (sc.Step == Step.Thanking ? sc.ThankRange : 35f)))
                    {
                        if (sc.Step == Step.Confronting || sc.Step == Step.Caught || sc.Step == Step.Comforting)
                        {
                            sc.Outcome = "you walked away";
                            LetItPlayOut(sc);
                            return;
                        }
                        Finish(sc);
                        return;
                    }
                    TryTalk(sc, mission);
                    break;

                case Step.Chasing:
                {
                    var thief = sc.Actors.FirstOrDefault();
                    if (sc.Ambush) { ChaseIntoTrap(sc, thief, inStep); break; }
                    if (thief == null || !thief.IsActive()) { ThiefEscaped(sc); return; }
                    if (Dist(main, thief) < CatchDistance)
                    {
                        sc.TalkTo = thief;
                        Contour(thief, null);
                        Direct(thief)?.Hold(face: main, loop: "act_scared_idle_2");
                        Go(sc, Step.Caught);
                        LmmiLog.Info($"Street: you caught the thief after {inStep:0} s.");
                        break;
                    }
                    var stage = Direct(thief);
                    if (inStep > ChaseTimeout || (stage != null && inStep > 3f && stage.Arrived)) ThiefEscaped(sc);
                    break;
                }

                case Step.Searching:
                {
                    // Somewhere around here. Close enough and you spot him.
                    var child = sc.Actors.FirstOrDefault();
                    if (child == null || !child.IsActive()) { sc.Outcome = "he turned up"; Finish(sc); return; }
                    float d = Dist(main, child);
                    // No outline: you hear him before you see him — and only once you're close. A voice, not a face.
                    if (d < LostCryDistance && _sceneTime >= sc.NextCryAt)
                    {
                        sc.NextCryAt = _sceneTime + 7f + MBRandom.RandomFloat * 4f;
                        var cry = Flavor.Pick(
                            "{=lmmi_street_lost_cry_1}Mama! Mamaaa!",
                            "{=lmmi_street_lost_cry_2}*sniff* ...Mama? Where are you?",
                            "{=lmmi_street_lost_cry_3}I want to go home... I want my mama...",
                            "{=lmmi_street_lost_cry_4}Mama! Mama, where are you?",
                            "{=lmmi_street_lost_cry_5}*sobs* I'm lost... I'm lost...",
                            "{=lmmi_street_lost_cry_6}Mama, I'm scared!",
                            "{=lmmi_street_lost_cry_7}Somebody help me find my mama!",
                            "{=lmmi_street_lost_cry_8}*hiccup* I want to go home!",
                            "{=lmmi_street_lost_cry_9}Mamaaa! I'm here!");
                        var heard = new TextObject(d < LostCryNearDistance
                            ? "{=lmmi_street_lost_cry_near}(Close by, a child is crying) {CRY}"
                            : "{=lmmi_street_lost_cry_far}(Somewhere nearby, a child is crying) {CRY}");
                        heard.SetTextVariable("CRY", cry);
                        Bark(child, heard, portrait: false);
                    }
                    if (d < (sc.Ambush ? 6f : 3f))
                    {
                        if (sc.Ambush) { Reveal(sc); return; }
                        Direct(child)?.Hold(face: main, loop: "act_scared_idle_2");
                        sc.TalkTo = child;
                        Go(sc, Step.Comforting);
                        LmmiLog.Info($"Street: you found the child after {inStep:0} s.");
                        break;
                    }
                    if (inStep > LostSearchTimeout)
                    {
                        Say("{=lmmi_street_lost_turned_up}Somewhere behind you, a woman shrieks with relief. The little one turned up on his own.",
                        "{=lmmi_street_lost_turned_up_2}Behind you, a mother's cry turns to laughter. The little one found his own way back.",
                        "{=lmmi_street_lost_turned_up_3}Word comes down the street: the child turned up by himself, safe and sound.");
                        sc.Outcome = "he turned up on his own";
                        Finish(sc);
                        return;
                    }
                    break;
                }

                case Step.Returning:
                {
                    var child = sc.Actors.FirstOrDefault();
                    if (child == null || !child.IsActive() || !sc.Requester.IsActive()) { sc.Outcome = "reunited"; Finish(sc); return; }
                    if (Dist(child, sc.Requester) < 3f)
                    {
                        Contour(sc.Requester, null);
                        Direct(child)?.Follow(sc.Requester, 1f, run: false);
                        SteppedUp(sc, 3f, "found a lost child");
                        MarkGrateful(child);
                        Thank(sc, sc.Requester, "child_found");
                        return;
                    }
                    if (inStep > SearchTimeout) { sc.Outcome = "reunited"; Finish(sc); return; }
                    break;
                }

                case Step.Fleeing:
                {
                    // They come after you: caught, and it's a beating; clear of them (or at the watch's side), they give up.
                    var chasers = sc.Actors.Where(a => a.IsActive()).ToList();
                    if (chasers.Count == 0) { Finish(sc); return; }
                    var nearest = chasers.OrderBy(a => Dist(a, main)).First();
                    if (Dist(nearest, main) < ToughsCatchDistance)
                    {
                        foreach (var t in chasers) Direct(t)?.Follow(main, 1.2f, run: true);   // the sprint's over
                        Bark(nearest, Flavor.Pick("{=lmmi_street_toughs_caught}Got you!",
                        "{=lmmi_street_toughs_caught_2}Gotcha!",
                        "{=lmmi_street_toughs_caught_3}Not so fast!"));
                        sc.BeatOnly = true;   // past talking now
                        LmmiLog.Info("Street: the toughs catch you.");
                        StartFight(sc, 0.04f, won => OnAmbushFightEnd(sc, won));
                        return;
                    }
                    bool watch = FindGuard(mission, main.Position, 10f) != null;
                    if (watch || chasers.All(a => Dist(a, main) > 30f) || inStep > FleeTimeout)
                    {
                        Bark(nearest, (watch
                            ? Flavor.Pick("{=lmmi_street_toughs_watch}The watch — leave it!",
                        "{=lmmi_street_toughs_watch_2}The watch! Scatter!",
                        "{=lmmi_street_toughs_watch_3}Guards — leave it, leave it!")
                            : Flavor.Pick("{=lmmi_street_toughs_coward}Run, then! Run home to your mother!",
                        "{=lmmi_street_toughs_coward_2}Go on, run! Coward!",
                        "{=lmmi_street_toughs_coward_3}That's it, run! Don't come back!")));
                        TownStandingBehavior.Adjust(sc.Settlement, -1f, "ran from a pack of street toughs");
                        if (watch) Say("{=lmmi_street_ran_to_watch}They pull up short at the sight of the watch. Half the street saw you run to them.",
                            "{=lmmi_street_ran_to_watch_2}The watch! They scatter — but the street saw you run behind the guards.",
                            "{=lmmi_street_ran_to_watch_3}They stop dead when they see the guards. You're safe — and the whole street saw you run.");
                        else Say("{=lmmi_street_ran_toughs}You got away. Half the street watched you run.",
                            "{=lmmi_street_ran_toughs_2}You lose them in the alleys. Somebody laughs as you pass.",
                            "{=lmmi_street_ran_toughs_3}They give up the chase. You're safe — and the talk of the street.");
                        Disperse(sc, chasers);
                        sc.Outcome = watch ? "ran to the watch" : "outran them";
                        Finish(sc);
                        return;
                    }
                    break;
                }

                case Step.Fighting:
                    break;   // MissionFightHandler calls back
            }
        }

        /// <summary>You ran from the toughs: they come after you.</summary>
        private void Chase(Scene sc)
        {
            var main = Agent.Main;
            var lead = sc.Actors.FirstOrDefault(a => a.IsActive());
            if (lead == null || main == null) { Finish(sc); return; }
            Bark(lead, Flavor.Pick("{=lmmi_street_toughs_after}After {?PLAYER.GENDER}her{?}him{\\?}!",
                        "{=lmmi_street_toughs_after_2}Don't let {?PLAYER.GENDER}her{?}him{\\?} get away!",
                        "{=lmmi_street_toughs_after_3}Catch {?PLAYER.GENDER}her{?}him{\\?}!"));
            // Flat out, all the way: no slowing as they close in (see LmmiStageBehavior.Chase).
            foreach (var t in sc.Actors.Where(a => a.IsActive())) Direct(t)?.Chase(main, ToughsSprintFactor);
            sc.TalkTo = null;
            Go(sc, Step.Fleeing);
            LmmiLog.Info("Street: you run from the toughs — they come after you.");
        }

        /// <summary>A setup: he runs to his friends. Catch him, or get close to them, and they close in.</summary>
        private void ChaseIntoTrap(Scene sc, Agent? thief, float inStep)
        {
            var main = Agent.Main;
            var lead = sc.Crew.FirstOrDefault(a => a.IsActive());
            if (lead == null) { ThiefEscaped(sc); return; }
            bool caught = thief != null && thief.IsActive() && Dist(main, thief) < CatchDistance;
            if (caught || Dist(main, lead) < 9f)
            {
                LmmiLog.Info($"Street: the chase ends in a trap ({(caught ? "you caught him" : "you ran into his friends")}).");
                Reveal(sc);
                return;
            }
            var stage = thief != null && thief.IsActive() ? Direct(thief) : null;
            if (!sc.Baited && stage != null && inStep > 3f && stage.Arrived)
            {
                sc.Baited = true;
                stage.Hold(face: main);   // with his friends, waiting for you
            }
            if (inStep > ChaseTimeout && Dist(main, lead) > 30f) ThiefEscaped(sc);
        }

        /// <summary>
        /// It was a setup. Whoever brought you here melts away — the girl, the "victim", the one who begged — and the
        /// toughs close in around you.
        /// </summary>
        private void Reveal(Scene sc)
        {
            var main = Agent.Main;
            var mission = Mission.Current;
            if (sc.Brawl?.On == true) sc.Brawl.Stop();
            sc.Brawl = null;
            var toughs = (sc.Kind == Kind.Thief || sc.Kind == Kind.LostChild || sc.Kind == Kind.Harvest ? sc.Crew : sc.Actors.Concat(sc.Crew)).Where(a => a.IsActive()).Distinct().ToList();
            var bait = new List<Agent>();
            if (sc.Kind == Kind.Thief || sc.Kind == Kind.LostChild || sc.Kind == Kind.Harvest) bait.AddRange(sc.Actors);
            if (sc.Victim != null) bait.Add(sc.Victim);
            bait.Add(sc.Requester);
            bait = bait.Where(a => a != null && a.IsActive() && !toughs.Contains(a)).Distinct().ToList();
            sc.Actors = toughs;
            sc.Crew = new List<Agent>();
            sc.Revealed = true;
            if (toughs.Count == 0) { sc.Outcome = "the toughs left"; Finish(sc); return; }

            // A parting word from the bait, and they're gone.
            Agent? talker = sc.Kind == Kind.Rescue || sc.Kind == Kind.Debt ? sc.Victim : sc.Kind == Kind.Thief ? bait.FirstOrDefault(b => b != sc.Victim) : sc.Requester;
            if (talker != null && bait.Contains(talker))
                Bark(talker, (sc.Kind == Kind.Rescue
                    ? Flavor.Pick("{=lmmi_street_bait_girl}Sorry, love. Nothing personal.",
                        "{=lmmi_street_bait_girl_2}Don't look so surprised, sweetheart.",
                        "{=lmmi_street_bait_girl_3}Thanks for coming. Really.")
                    : sc.Kind == Kind.Thief
                        ? Flavor.Pick("{=lmmi_street_bait_thief}Told you, lads. Soft as butter, this one.",
                        "{=lmmi_street_bait_thief_2}Told you they'd follow. Every time.",
                        "{=lmmi_street_bait_thief_3}Easy pickings, lads.")
                        : sc.Kind == Kind.Debt
                            ? Flavor.Pick("{=lmmi_street_bait_debtor}Told you someone would come running.",
                        "{=lmmi_street_bait_debtor_2}See? Never fails.",
                        "{=lmmi_street_bait_debtor_3}Good of you to drop by.")
                            : Flavor.Pick("{=lmmi_street_bait_sorry}...Sorry. It was you or me.",
                        "{=lmmi_street_bait_sorry_2}...Forgive me. They made me.",
                        "{=lmmi_street_bait_sorry_3}I'm sorry. I had no choice.")));
            foreach (var b in bait)
            {
                Contour(b, null);
                var away = mission != null ? FarPoint(mission, b, toughs.ToArray()) : null;
                if (away == null) continue;
                if (b == sc.Requester) sc.RequesterGone = true;
                HandOver(sc, new SlipAway(b, away.Value), b);
            }

            // They close in around you; the one in front does the talking.
            var lead = toughs[0];
            Direct(lead)?.Follow(main, 1.8f, run: true);
            var toLead = (lead.Position - main.Position).AsVec2;
            float baseAngle = toLead.LengthSquared < 0.01f ? 0f : toLead.Normalized().RotationInRadians;
            for (int i = 1; i < toughs.Count; i++)
            {
                var pos = main.GetWorldPosition();
                var dir = Vec2.FromRotation(baseAngle + i * (6.2832f / toughs.Count));
                pos.SetVec2(main.Position.AsVec2 + dir * 2.6f);
                Direct(toughs[i])?.GoTo(pos, run: true, face: main);
            }
            sc.TalkTo = lead;
            Go(sc, Step.Confronting);
            LmmiLog.Info($"Street: it was a setup ({sc.Kind}) — {toughs.Count} tough(s) close in, led by {lead.Name}"
                         + $"{(sc.BeatOnly ? ", out to hurt a foreigner" : $", toll {sc.Toll}")}.");
        }

        /// <summary>The Roguery hunch: a good rogue may smell a setup — and now and then smells one that isn't there.</summary>
        private void Hunch(Scene sc)
        {
            sc.Hunched = true;
            int roguery = Hero.MainHero.GetSkillValue(DefaultSkills.Roguery);
            if (roguery < Treachery.RogueryToSpot) return;
            float roll = MBRandom.RandomFloat;
            bool hunch = roll < (sc.Ambush ? HunchOnSetup : HunchOnGenuine);
            LmmiLog.Info($"Street: Roguery {roguery} hunch roll {roll:0.00} ({(sc.Ambush ? "setup" : "genuine")}) -> {(hunch ? "a bad feeling" : "nothing")}.");
            if (hunch) Say("{=lmmi_street_hunch}Something in the way they ask sets your teeth on edge. (Roguery)",
                        "{=lmmi_street_hunch_2}Something's not right about this. You can't say what. (Roguery)",
                        "{=lmmi_street_hunch_3}The plea sounds rehearsed. Your hand drifts to your purse. (Roguery)");
        }

        /// <summary>A refusal the street remembers — unless nobody was really in trouble (a setup: nobody saw a thing).</summary>
        private void Shame(float amount, string why)
        {
            if (_scene?.Ambush == true) return;
            TownStandingBehavior.Adjust(Settlement.CurrentSettlement, amount, why);
        }

        // ---- The watch: a guard passing by, or anyone after a while, comes to see what the noise is ----

        private static Vec3 SpotOf(Scene sc)
        {
            var a = sc.Actors.FirstOrDefault(x => x.IsActive()) ?? sc.Victim ?? Agent.Main;
            return a != null && a.IsActive() ? a.Position : Agent.Main.Position;
        }

        /// <summary>True once the watch has taken over the scene (this tick's work is done).</summary>
        private bool CheckWatch(Scene sc, Mission mission)
        {
            // Only a fight that has started draws the watch — not you walking up to talk. And nobody arrests you for
            // fighting looters (your own men, in the field, are not the watch either).
            if (sc.Step != Step.Fighting || sc.Kind == Kind.Raiders) return false;
            var main = Agent.Main;

            if (sc.Watch == null)
            {
                if (sc.NoWatchNearby) return false;
                var at = sc.Step == Step.Fighting || sc.Step == Step.Chasing ? main.Position : SpotOf(sc);
                var passing = FindGuard(mission, at, 12f, sc.Actors.ToArray());
                if (passing == null && _sceneTime - sc.StartedAt < 90f) return false;
                var guard = passing ?? FindGuard(mission, at, 90f, sc.Actors.ToArray());
                if (guard == null) { sc.NoWatchNearby = true; return false; }
                sc.Watch = guard;
                Direct(guard)?.Follow(main, 2.5f, run: true);
                Bark(guard, Flavor.Pick("{=lmmi_street_watch_coming}Oi! What's going on over there?",
                        "{=lmmi_street_watch_coming_2}Hey! What's this?",
                        "{=lmmi_street_watch_coming_3}You there! Stop that!"));
                LmmiLog.Info($"Street: {guard.Name} of the watch comes to see ({(passing != null ? "passing by" : "it took too long")}).");
                return false;
            }

            if (!sc.Watch.IsActive()) { sc.Watch = null; sc.NoWatchNearby = true; return false; }
            if (Dist(sc.Watch, main) > 3.5f) return false;

            // The watch is here: everything stops.
            LmmiLog.Info($"Street: the watch breaks it up ({sc.Kind}, {sc.Step}).");
            if (sc.Step == Step.Fighting)
            {
                sc.WatchInterrupted = true;
                mission.GetMissionBehavior<MissionFightHandler>()?.EndFight();
            }
            if (sc.Brawl?.On == true) sc.Brawl.Stop();
            foreach (var a in sc.Actors.Where(x => x.IsActive()).ToList())
            {
                Contour(a, null);
                var to = FarPoint(mission, a, sc.Actors.ToArray());
                if (to != null) HandOver(sc, new SlipAway(a, to.Value), a);
            }
            Contour(sc.Requester, null);
            Direct(sc.Requester)?.Hold(face: main);
            sc.TalkTo = sc.Watch;
            Direct(sc.Watch)?.Follow(main, 1.8f, run: false);
            Go(sc, Step.Questioned);
            return true;
        }

        private static float Resentment(Scene sc) => ResentmentIn(sc.Settlement);

        private static float ResentmentIn(Settlement settlement) =>
            Math.Max(0f, CultureRelations.Multiplier(settlement.Culture?.StringId, Hero.MainHero.Culture?.StringId)
                         * LmmiSettingsProvider.ForeignerPrejudicePercent / 100f);

        private static bool Witness(Scene sc)
        {
            if (sc.Revealed) return false;   // the only witnesses were in on it
            var w = sc.Victim ?? sc.Requester;
            return w != null && w.IsActive() && Dist(w, Agent.Main) < 15f;
        }

        private static float TownStanding(Scene sc) => Math.Max(-20f, Math.Min(20f, TownStandingBehavior.Get(sc.Settlement))) / 100f;

        private void Cleared(Scene sc)
        {
            if (sc.Revealed)
            {
                TownStandingBehavior.Adjust(sc.Settlement, 1f, "the watch believed robbers jumped you");
                sc.Outcome = "the watch took your word";
                Finish(sc);
                return;
            }
            TownStandingBehavior.Adjust(sc.Settlement, sc.Kind == Kind.Brawl ? 1f : 2f, "the watch took your word for it");
            if (sc.Kind == Kind.Rescue || sc.Kind == Kind.Thief || sc.Kind == Kind.Debt || sc.Kind == Kind.Shakedown)
                Thank(sc, sc.Victim, sc.Kind == Kind.Rescue ? "rescued" : sc.Kind == Kind.Thief ? "purse_back" : sc.Kind == Kind.Debt ? "debt_freed" : "shakedown");
            else Thank(sc, sc.Requester, "brawl_stopped");
        }

        private void Fined(Scene sc)
        {
            int fine = (int)Math.Round(100 * TownStandingBehavior.FineFactor(sc.Settlement) / 10f) * 10;
            TownStandingBehavior.Adjust(sc.Settlement, -1f, "the watch fined you for brawling");
            if (Hero.MainHero.Gold >= fine)
            {
                Hero.MainHero.ChangeHeroGold(-fine);
                var paid = new TextObject("{=lmmi_street_fined_n}You pay the watch {FINE} denars. Justice, of a sort.");
                paid.SetTextVariable("FINE", fine);
                InformationManager.DisplayMessage(new InformationMessage(paid.ToString()));
            }
            else if (sc.Settlement.MapFaction != null)
            {
                ChangeCrimeRatingAction.Apply(sc.Settlement.MapFaction, 15f);
                Say("{=lmmi_street_fined_crime}You can't pay the fine. The watch takes your name instead.");
            }
            sc.Outcome = "fined by the watch";
            Finish(sc);
        }

        private static int FineFor(Scene sc) =>
            (int)Math.Round((sc.WatchInterrupted ? 200 : 100) * TownStandingBehavior.FineFactor(sc.Settlement) / 10f) * 10;

        private void PayFine(Scene sc)
        {
            int fine = FineFor(sc);
            Hero.MainHero.ChangeHeroGold(-fine);
            TownStandingBehavior.Adjust(sc.Settlement, -1f, "fined by the watch");
            sc.Outcome = "paid the watch's fine";
            Finish(sc);
        }

        /// <summary>Taken to the cells: the scene ends, then vanilla's captivity (as a failed prison break does it).</summary>
        private void GoToJail(Scene sc)
        {
            TownStandingBehavior.Adjust(sc.Settlement, -2f, "was taken to the cells");
            _wentQuietly = true;
            _downedResisting = _killedResisting = 0;
            _incidentCrime = 0f;
            _sentencePrepared = false;
            sc.Outcome = "went quietly";
            var settlement = sc.Settlement;
            Finish(sc);
            _aftercare = Aftercare.Jail;
            _aftercareIn = settlement;
            _endMissionAt = _sceneTime + 1.5f;
            Say("{=lmmi_street_jail_walk}The watch closes in on either side of you.",
                        "{=lmmi_street_jail_walk_2}Two of the watch take you by the arms.",
                        "{=lmmi_street_jail_walk_3}The watch fall in on either side of you, hands on their clubs.");
        }

        /// <summary>
        /// Resisting arrest: the nearby watch turns on you, weapons out. There's only one way this ends well — get out of
        /// the town. Every guard you put down makes it worse (crime), and if they put you down, it's the cells.
        /// </summary>
        private void ResistArrest(Scene sc)
        {
            var mission = Mission.Current;
            var main = Agent.Main;
            if (mission == null || main == null || mission.PlayerEnemyTeam == null) { Fined(sc); return; }
            var settlement = sc.Settlement;
            var guards = mission.Agents
                .Where(a => a.IsActive() && a.IsHuman && IsWatch(a) && a.Position.Distance(main.Position) < 60f)
                .OrderBy(a => a.Position.Distance(main.Position)).Take(4).ToList();
            if (sc.Watch != null && sc.Watch.IsActive() && !guards.Contains(sc.Watch)) guards.Insert(0, sc.Watch);
            sc.Outcome = "resisted arrest";
            sc.Watch = null;   // not released: he's coming for you
            Finish(sc);

            _resisting.Clear();
            foreach (var g in guards)
            {
                Release(g);
                g.SetTeam(mission.PlayerEnemyTeam, true);
                ForceFight(g);
                _resisting.Add(g);
            }
            _resistIn = settlement;
            _knockedOutResisting = false;
            _downedResisting = _killedResisting = 0;
            _incidentCrime = 15f;
            _wentQuietly = false;
            _sentencePrepared = false;
            if (settlement.MapFaction != null) ChangeCrimeRatingAction.Apply(settlement.MapFaction, 15f);
            var msg = new TextObject("{=lmmi_street_resist}Resisting arrest! Get out of {SETTLEMENT} — every guard you strike down will be remembered.");
            msg.SetTextVariable("SETTLEMENT", settlement.Name);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
            LmmiLog.Info($"Street: you resist arrest in {settlement.Name}: {guards.Count} guard(s) come for you.");
        }

        /// <summary>From <see cref="StreetHitWatcher"/>: you're leaving the scene (Tab). With a guard on you, you're caught.</summary>
        internal static void OnLeavingScene()
        {
            var self = Instance;
            if (self == null || self._resistIn == null) return;
            self._caughtLeaving = GuardsOnYou();
            LmmiLog.Info(self._caughtLeaving ? "Street: you try to slip away — a guard grabs you." : "Street: you slip away from the watch.");
        }

        /// <summary>A guard you're resisting is within 12 m.</summary>
        internal static bool GuardsOnYou()
        {
            var self = Instance;
            var main = Agent.Main;
            return self != null && self._resistIn != null && main != null && main.IsActive()
                   && self._resisting.Any(g => g.IsActive() && g.Position.Distance(main.Position) < 12f);
        }

        /// <summary>From <see cref="StreetHitWatcher"/>: someone went down.</summary>
        internal static void OnAgentRemoved(Agent affected, Agent? affector, AgentState state)
        {
            var self = Instance;
            if (self == null || self._resistIn == null) return;
            try
            {
                if (affected == Agent.Main)
                {
                    // Down, and taken: the cells.
                    self._knockedOutResisting = true;
                    self._aftercare = Aftercare.Jail;
                    self._aftercareIn = self._resistIn;
                    self._endMissionAt = self._sceneTime + 3f;
                    LmmiLog.Info("Street: they put you down — you'll wake in the cells.");
                    return;
                }
                if (!self._resisting.Remove(affected) || affector != Agent.Main) return;
                float crime = state == AgentState.Killed ? 25f : 10f;
                if (self._resistIn.MapFaction != null) ChangeCrimeRatingAction.Apply(self._resistIn.MapFaction, crime);
                self._incidentCrime += crime;
                if (state == AgentState.Killed) self._killedResisting++;
                else self._downedResisting++;
                var msg = new TextObject(state == AgentState.Killed
                    ? "{=lmmi_street_guard_killed}You killed a guard of {SETTLEMENT}. They'll hang you for this."
                    : "{=lmmi_street_guard_downed}A guard goes down. {SETTLEMENT} will remember it.");
                msg.SetTextVariable("SETTLEMENT", self._resistIn.Name);
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
                LmmiLog.Info($"Street: a guard {(state == AgentState.Killed ? "killed" : "knocked out")} while resisting (+{crime} crime).");
            }
            catch (Exception ex) { LmmiLog.Error("Street: OnAgentRemoved threw", ex); }
        }

        private void OnMissionEnded(IMission mission)
        {
            if (_faded) { _faded = false; _needFadeIn = true; }
            _work = null;   // the agents go with the scene; nothing to give back
            if ((_armedFight != null || _menFight != null) && HiredBeatDownPatch.Guarded != null
                && ReferenceEquals(HiredBeatDownPatch.Guarded, Agent.Main))
                HiredBeatDownPatch.Guarded = null;
            _armedFight = null;
            if (_menFight != null)
            {
                LmmiLog.Info("Street: you left the village with your men still fighting the looters.");
                _menFight = null;
            }
            if (_scene != null && _scene.Step == Step.Fleeing)
            {
                TownStandingBehavior.Adjust(_scene.Settlement, -1f, "ran from a pack of street toughs");
                LmmiLog.Info("Street: you left the scene with the toughs at your heels.");
            }
            if (_streetFight)
            {
                // You ran from a street fight.
                _streetFight = false;
                TownStandingBehavior.Adjust(_streetFightIn, -1f, "ran from a street fight");
                InformationManager.DisplayMessage(new InformationMessage(new TextObject(
                    "{=lmmi_street_ran}You ran from the fight. The street will talk about that.").ToString()));
                LmmiLog.Info("Street: you left the scene mid-fight.");
            }
            if (_resistIn == null) return;
            if (_caughtLeaving && !_knockedOutResisting)
            {
                // A hand on your collar at the gate.
                if (_resistIn.MapFaction != null) ChangeCrimeRatingAction.Apply(_resistIn.MapFaction, 10f);
                _incidentCrime += 10f;
                _aftercare = Aftercare.Jail;
                _aftercareIn = _resistIn;
            }
            else if (!_knockedOutResisting && _aftercare == Aftercare.None)
            {
                // Out of the scene on your own feet: you got away.
                _aftercare = Aftercare.Escaped;
                _aftercareIn = _resistIn;
            }
            _resistIn = null;
            _caughtLeaving = false;
            _resisting.Clear();
        }

        /// <summary>Back in the menus after the scene: the cells, or a quick exit from town.</summary>
        private void OnGameMenuOpened(MenuCallbackArgs args)
        {
            if (_needFadeIn && Mission.Current == null)
            {
                _needFadeIn = false;
                ScreenFadeController.BeginFadeIn(0.6f);
            }
            if (_aftercare == Aftercare.None || Mission.Current != null || _aftercareIn == null) return;
            if (_aftercare == Aftercare.Jail && !_sentencePrepared) PrepareSentence(_aftercareIn);
            var menu = _aftercare == Aftercare.Jail ? "lmmi_street_jailed" : "lmmi_street_escaped";
            if (args.MenuContext?.GameMenu?.StringId == menu) return;
            GameMenu.SwitchToMenu(menu);
        }

        /// <summary>
        /// The magistrate's sentence. Going quietly: a night, and longer the worse your name is here. Resisting: longer
        /// still, more for every guard you put down, far more for every one you killed. Kill enough of the watch and the
        /// owner decides: a noble pays a blood price and loses the owner's goodwill; a commoner hangs — unless the owner
        /// is merciful or fond of you (flogged and banished instead), or, being neither cruel nor kind, takes blood money.
        /// </summary>
        private void PrepareSentence(Settlement where)
        {
            _sentencePrepared = true;
            float crime = where.MapFaction?.MainHeroCrimeRating ?? 0f;
            int days = _wentQuietly
                ? 1 + (int)(crime / 20f)
                : 2 + (int)(crime / 10f) + 2 * _downedResisting + 5 * _killedResisting;
            bool murderer = _killedResisting > 0 && _killedResisting >= Math.Max(1, LmmiSettingsProvider.GuardKillsForHeadsman);
            _headsman = false;
            _bloodPrice = _bloodOffer = 0;
            _commuted = _flogged = false;
            _verdict = Verdict.None;
            if (murderer)
            {
                days *= 2;
                var owner = where.OwnerClan?.Leader;
                if (owner == Hero.MainHero) owner = null;
                int mercy = owner?.GetTraitLevel(DefaultTraits.Mercy) ?? 0;
                if (IsNoble())
                {
                    _verdict = Verdict.Noble;
                    _bloodPrice = 500 * _killedResisting;
                }
                else if (!ExecutionPossible(where))
                {
                    _verdict = Verdict.Lenient;
                    _commuted = _flogged = where.OwnerClan != Clan.PlayerClan;
                }
                else if (mercy > 0 || (owner != null && owner.GetRelationWithPlayer() >= 30f))
                {
                    _verdict = mercy > 0 ? Verdict.Merciful : Verdict.Fond;
                    _commuted = _flogged = true;
                }
                else
                {
                    _headsman = true;
                    _verdict = mercy < 0 ? Verdict.Cruel : Verdict.Ransom;
                    if (mercy == 0) _bloodOffer = 2000 * _killedResisting;
                }
            }
            // How the town speaks of you counts with the magistrate too.
            days += TownStandingBehavior.SentenceDays(where);
            _sentenceDays = Math.Max(1, Math.Min(90, days));
            LmmiLog.Info($"Street: sentenced in {where.Name} — crime {crime:0}, {_downedResisting} guard(s) downed, {_killedResisting} killed"
                         + $"{(_wentQuietly ? ", went quietly" : "")} -> {(_headsman ? "the headsman" : $"{_sentenceDays} day(s)")}"
                         + (_verdict != Verdict.None ? $", verdict {_verdict}" : "")
                         + (_bloodPrice > 0 ? $", blood price {_bloodPrice}" : "") + (_bloodOffer > 0 ? $", blood money offered {_bloodOffer}" : "") + ".");
        }

        /// <summary>Noble enough to be spared the rope: a vassal of a kingdom (or its ruler — not a hired sword), or a house of tier 3+.</summary>
        private static bool IsNoble()
        {
            var clan = Clan.PlayerClan;
            return clan != null && (clan.Tier >= 3 || (clan.Kingdom != null && !clan.IsUnderMercenaryService));
        }

        /// <summary>Could killing the watch here end on the gallows? A commoner, with the setting on, in a town that isn't your own.</summary>
        private static bool ExecutionPossible(Settlement? where) =>
            where != null && LmmiSettingsProvider.AllowPlayerExecution && !IsNoble() && where.OwnerClan != Clan.PlayerClan;

        /// <summary>The one deciding: "Lord X" — or the magistrate, when the town has no lord to speak of.</summary>
        private static TextObject LordText(Settlement? where)
        {
            var owner = where?.OwnerClan?.Leader;
            if (owner == null || owner == Hero.MainHero) return new TextObject("{=lmmi_street_lord_magistrate}The magistrate");
            var lord = new TextObject("{=lmmi_street_lord}{?LORD.GENDER}Lady{?}Lord{\\?} {LORD.NAME}");
            Helpers.StringHelpers.SetCharacterProperties("LORD", owner.CharacterObject, lord);
            return lord;
        }

        private static void PayBloodPrice(Settlement where, int gold)
        {
            int paid = Math.Min(Hero.MainHero.Gold, gold);
            var owner = where.OwnerClan?.Leader;
            if (paid > 0 && owner != null && owner != Hero.MainHero) GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, owner, paid);
            else if (paid > 0) Hero.MainHero.ChangeHeroGold(-paid);
        }

        private void SetJailText()
        {
            var where = _aftercareIn;
            MBTextManager.SetTextVariable("LMMI_JAIL_TOWN", where?.Name ?? TextObject.GetEmpty());
            TextObject sentence;
            switch (_verdict)
            {
                case Verdict.Cruel:
                    sentence = new TextObject("{=lmmi_street_verdict_cruel}{LORD} is not a merciful man, and the watch's dead are still lying in the street. The magistrate will hear it at dawn — nobody expects it to take long.");
                    break;
                case Verdict.Ransom:
                    sentence = new TextObject("{=lmmi_street_verdict_ransom}{LORD} is no butcher, but the watch's dead will be answered for: {GOLD}{GOLD_ICON} in blood money, or the headsman at dawn.");
                    break;
                case Verdict.Merciful:
                case Verdict.Fond:
                case Verdict.Lenient:
                    sentence = new TextObject(_verdict == Verdict.Merciful
                        ? "{=lmmi_street_verdict_merciful}{LORD} is a merciful man, and commutes the sentence: {TAIL}"
                        : _verdict == Verdict.Fond
                            ? "{=lmmi_street_verdict_fond}{LORD} thinks too well of you to see you hang, and commutes the sentence: {TAIL}"
                            : "{=lmmi_street_verdict_lenient}{LORD} has no stomach for a hanging today, and commutes the sentence: {TAIL}");
                    var tail = new TextObject("{=lmmi_street_verdict_tail}{DAYS} days in the cells, a flogging for the watch's dead, and no welcome in {TOWN} after.");
                    tail.SetTextVariable("DAYS", _sentenceDays);
                    tail.SetTextVariable("TOWN", where?.Name ?? TextObject.GetEmpty());
                    sentence.SetTextVariable("TAIL", tail);
                    break;
                case Verdict.Paid:
                    sentence = new TextObject("{=lmmi_street_verdict_paid}{LORD} takes the blood money and spares your neck. {DAYS} days in the cells, and no welcome in {TOWN} after.");
                    break;
                case Verdict.Noble:
                    sentence = new TextObject("{=lmmi_street_sentence_noble}Your name spares your neck. The magistrate gives you {DAYS} days in the cells, and names a blood price for the dead: {GOLD}{GOLD_ICON}.");
                    break;
                default:
                    sentence = _sentenceDays <= 1
                        ? new TextObject("{=lmmi_street_sentence_night}The magistrate gives you a night in the cells to think it over.")
                        : new TextObject("{=lmmi_street_sentence_days}The magistrate gives you {DAYS} days in the cells.");
                    break;
            }
            sentence.SetTextVariable("LORD", LordText(where));
            sentence.SetTextVariable("TOWN", where?.Name ?? TextObject.GetEmpty());
            sentence.SetTextVariable("DAYS", _sentenceDays);
            sentence.SetTextVariable("GOLD", _verdict == Verdict.Ransom ? _bloodOffer : _bloodPrice);
            MBTextManager.SetTextVariable("LMMI_JAIL_SENTENCE", sentence);
        }

        /// <summary>
        /// From <see cref="JailSentencePatch"/>, before every tick of vanilla's captivity check: whether vanilla's check
        /// may run. While a sentence is being served it does — escape chances, ransom offers — except that its "your
        /// captors aren't at war with you, so they let you go" is held off (<paramref name="releaseGuarded"/>: the
        /// patch's guard on that branch is in place; without it, vanilla is skipped outright as before). When the time
        /// is up, our own menu — served, or the headsman — and vanilla waits while it's open.
        /// </summary>
        internal static bool CaptivityCheck(bool releaseGuarded)
        {
            var self = Instance;
            if (self == null || self._sentence == Sentence.None) return true;
            if (!self.InSentencedCells())
            {
                LmmiLog.Info("Street: you're no longer in the cells you were sentenced to — vanilla's captivity takes over.");
                self.ClearSentence();
                return true;
            }
            if (self._sentence == Sentence.Ending) return false;   // our menu is up
            if (CampaignTime.Now.ToHours < self._sentenceUntilHours) return releaseGuarded;
            self._sentence = Sentence.Ending;
            GameMenu.SwitchToMenu(self._headsman ? "lmmi_street_headsman" : self._flogged ? "lmmi_street_flogged" : "lmmi_street_served");
            return false;
        }

        /// <summary>From <see cref="JailSentencePatch"/>, inside vanilla's check: a sentence is holding you in these cells.</summary>
        internal static bool HoldsSentence()
        {
            var self = Instance;
            return self != null && self._sentence == Sentence.Serving && self.InSentencedCells();
        }

        private bool InSentencedCells()
        {
            var captor = PlayerCaptivity.IsCaptive ? PlayerCaptivity.CaptorParty : null;
            return captor != null && captor.IsSettlement && captor.Settlement?.StringId == _sentenceIn;
        }

        /// <summary>Sentenced: the lord's family whose cells you're in think the less of you — far less if you killed their men.</summary>
        private static void ShameBeforeOwners(Settlement jail, bool killedGuards)
        {
            try
            {
                var clan = jail.OwnerClan;
                if (clan == null || clan == Clan.PlayerClan) return;
                int delta = killedGuards ? -5 : -2;
                var family = clan.Heroes.Where(h => h.IsAlive && !h.IsChild && h != Hero.MainHero).ToList();
                foreach (var h in family) ChangeRelationAction.ApplyPlayerRelation(h, delta, affectRelatives: false, showQuickNotification: false);
                if (family.Count == 0) return;
                var msg = new TextObject("{=lmmi_street_sentenced_clan}Word of your sentence reaches the {CLAN}. They think the less of you for it. (Relation {DELTA} with {COUNT} of them)");
                msg.SetTextVariable("CLAN", clan.Name);
                msg.SetTextVariable("DELTA", delta);
                msg.SetTextVariable("COUNT", family.Count);
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
                LmmiLog.Info($"Street: sentenced in {jail.Name} — relation {delta} with {family.Count} member(s) of {clan.Name}.");
            }
            catch (Exception ex) { LmmiLog.Error("Street: ShameBeforeOwners threw", ex); }
        }

        private void ClearSentence()
        {
            _sentence = Sentence.None;
            _sentenceUntilHours = -1;
            _sentenceIn = "";
            _sentenceCrime = 0f;
            _sentenceKilled = 0;
            _headsman = false;
            _commuted = _flogged = false;
            _verdict = Verdict.None;
            _bloodOffer = 0;
        }

        private static Settlement? SentencedIn(StreetEventsBehavior self) =>
            string.IsNullOrEmpty(self._sentenceIn) ? null : Settlement.Find(self._sentenceIn);

        /// <summary>The sentence is served: out of the cells, crime paid down — and, if it was commuted, banished and flogged.</summary>
        private void ReleaseFromCells()
        {
            try
            {
                var where = SentencedIn(this);
                float crime = _sentenceCrime;
                bool commuted = _commuted, flogged = _flogged;
                ClearSentence();
                EndCaptivityAction.ApplyByReleasedByChoice(Hero.MainHero);
                // Released at the gate, outside — but the town's menu (from before the cells) is still on the stack, and
                // "Leave" from it would crash (no current settlement). Close it.
                for (int i = 0; i < 3 && Campaign.Current.CurrentMenuContext != null && MobileParty.MainParty.CurrentSettlement == null; i++)
                    GameMenu.ExitToLast();
                // Time served pays for what you did this time.
                if (where?.MapFaction != null && crime > 0f)
                    ChangeCrimeRatingAction.Apply(where.MapFaction, -Math.Min(where.MapFaction.MainHeroCrimeRating, crime));
                // Banished: after the crime relief, or it would lift the town's opinion back up.
                if (commuted && where != null)
                {
                    float standing = TownStandingBehavior.Get(where);
                    if (standing > -25f) TownStandingBehavior.Adjust(where, -25f - standing - 1f, "banished after a commuted sentence");
                }
                if (flogged)
                {
                    var hero = Hero.MainHero;
                    hero.HitPoints = Math.Max(1, (int)(hero.MaxHitPoints * 0.15f));
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=lmmi_street_flogged_msg}The lash has left you barely able to stand.").ToString(), Colors.Red));
                }
                LmmiLog.Info($"Street: sentence served in {where?.Name}{(commuted ? " (commuted: banished" + (flogged ? ", flogged)" : ")") : "")}.");
            }
            catch (Exception ex) { LmmiLog.Error("Street: ReleaseFromCells threw", ex); }
        }

        /// <summary>A noble who killed the watch: they were the owner's men, and a great name doesn't make him forget it.</summary>
        private static void NobleKin(Settlement where)
        {
            try
            {
                var lord = where.OwnerClan?.Leader;
                if (lord == null || lord == Hero.MainHero) return;
                ChangeRelationAction.ApplyPlayerRelation(lord, -10, affectRelatives: false, showQuickNotification: false);
                var msg = new TextObject("{=lmmi_street_noble_kin}Those were {LORD}'s men you killed. A great name won't make {LORD} forget it. (Relation -10)");
                msg.SetTextVariable("LORD", lord.Name);
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
                LmmiLog.Info($"Street: relation -10 with {lord.Name} (his men, killed by a noble).");
            }
            catch (Exception ex) { LmmiLog.Error("Street: NobleKin threw", ex); }
        }

        private void AddAftercareMenus(CampaignGameStarter starter)
        {
            starter.AddGameMenu("lmmi_street_jailed",
                "{=lmmi_street_jailed}The watch marches you through the streets of {LMMI_JAIL_TOWN} and down into the cells. {LMMI_JAIL_SENTENCE}",
                args => SetJailText());
            starter.AddGameMenuOption("lmmi_street_jailed", "lmmi_street_jailed_continue",
                "{=lmmi_street_jailed_continue}Continue",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                args =>
                {
                    var where = _aftercareIn;
                    _aftercare = Aftercare.None;
                    _aftercareIn = null;
                    if (where == null) return;
                    // A village has no cells: the lord's town or castle does.
                    var jail = where.IsVillage ? where.Village?.Bound ?? where : where;
                    if (!_sentencePrepared) PrepareSentence(where);
                    _sentencePrepared = false;
                    if (_bloodPrice > 0) PayBloodPrice(where, _bloodPrice);
                    PlayerEncounter.LeaveSettlement();
                    PlayerEncounter.Finish(true);
                    TakePrisonerAction.Apply(jail.Party, Hero.MainHero);
                    ShameBeforeOwners(jail, _killedResisting > 0);
                    if (_verdict == Verdict.Noble) NobleKin(where);
                    _sentence = Sentence.Serving;
                    _sentenceIn = jail.StringId;
                    _sentenceUntilHours = CampaignTime.Now.ToHours + (_headsman ? 8.0 : _sentenceDays * (double)CampaignTime.HoursInDay);
                    _sentenceCrime = _incidentCrime;
                    _sentenceKilled = _killedResisting;
                    if (!_headsman)
                    {
                        var msg = new TextObject("{=lmmi_street_sentenced}Sentenced: {DAYS} day(s) in the cells of {TOWN}.");
                        msg.SetTextVariable("DAYS", _sentenceDays);
                        msg.SetTextVariable("TOWN", jail.Name);
                        InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
                    }
                    LmmiLog.Info($"Street: you're a prisoner in {jail.Name} ({(_headsman ? "until dawn: the headsman" : $"{_sentenceDays} day(s)")}).");
                });

            // Neither cruel nor kind: the owner will take blood money in place of the rope — offered here, once.
            starter.AddGameMenuOption("lmmi_street_jailed", "lmmi_street_jailed_bloodmoney",
                "{=lmmi_street_jailed_bloodmoney}Offer blood money ({GOLD}{GOLD_ICON})",
                args =>
                {
                    if (!_headsman || _verdict != Verdict.Ransom || _bloodOffer <= 0) return false;
                    args.optionLeaveType = GameMenuOption.LeaveType.Bribe;
                    args.Text.SetTextVariable("GOLD", _bloodOffer);
                    if (!CanAfford(_bloodOffer, out var why)) { args.IsEnabled = false; args.Tooltip = why; }
                    return true;
                },
                args =>
                {
                    try
                    {
                        var where = _aftercareIn;
                        if (where == null || !_headsman || _bloodOffer <= 0 || Hero.MainHero.Gold < _bloodOffer) return;
                        PayBloodPrice(where, _bloodOffer);
                        LmmiLog.Info($"Street: blood money paid in {where.Name} ({_bloodOffer}) — the headsman is off, the sentence stands doubled.");
                        _headsman = false;
                        _verdict = Verdict.Paid;
                        _commuted = true;
                        _flogged = false;
                        _bloodOffer = 0;
                        GameMenu.SwitchToMenu("lmmi_street_jailed");
                    }
                    catch (Exception ex) { LmmiLog.Error("Street: paying blood money threw", ex); }
                });

            starter.AddGameMenu("lmmi_street_served",
                "{=lmmi_street_served}Your time is served. The jailer shoves you out through the gate of {LMMI_JAIL_TOWN} with your gear in a sack. As far as the magistrate is concerned, the matter is closed.",
                args => MBTextManager.SetTextVariable("LMMI_JAIL_TOWN", SentencedIn(this)?.Name ?? TextObject.GetEmpty()));
            starter.AddGameMenuOption("lmmi_street_served", "lmmi_street_served_continue",
                "{=lmmi_street_jailed_continue}Continue",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                args => ReleaseFromCells());

            // A commuted sentence ends at the post, before the gate opens.
            starter.AddGameMenu("lmmi_street_flogged",
                "{=lmmi_street_flogged}Your time is served — but not before the lash. They strip you in the square of {LMMI_JAIL_TOWN} and the jailer counts out the strokes, one for every hour the watch's dead will go unburied. Then the gate opens and your gear is thrown after you. {LMMI_JAIL_TOWN} won't be glad to see you again.",
                args => MBTextManager.SetTextVariable("LMMI_JAIL_TOWN", SentencedIn(this)?.Name ?? TextObject.GetEmpty()));
            starter.AddGameMenuOption("lmmi_street_flogged", "lmmi_street_flogged_continue",
                "{=lmmi_street_jailed_continue}Continue",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                args => ReleaseFromCells());

            starter.AddGameMenu("lmmi_street_headsman",
                "{=lmmi_street_headsman}At dawn they bring you up into the square. {LMMI_JAIL_KILLED} of the watch dead by your hand — and for that, {LMMI_JAIL_TOWN} has only one sentence.",
                args =>
                {
                    MBTextManager.SetTextVariable("LMMI_JAIL_TOWN", SentencedIn(this)?.Name ?? TextObject.GetEmpty());
                    MBTextManager.SetTextVariable("LMMI_JAIL_KILLED", _sentenceKilled);
                });
            starter.AddGameMenuOption("lmmi_street_headsman", "lmmi_street_headsman_face",
                "{=lmmi_street_headsman_face}Face the headsman.",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                args =>
                {
                    var where = SentencedIn(this);
                    ClearSentence();
                    var executer = where?.OwnerClan?.Leader;
                    if (executer == Hero.MainHero) executer = null;
                    LmmiLog.Info($"Street: the headsman in {where?.Name}.");
                    KillCharacterAction.ApplyByExecution(Hero.MainHero, executer, showNotification: true, isForced: false);
                });

            starter.AddGameMenu("lmmi_street_escaped",
                "{=lmmi_street_escaped}You slip out of {LMMI_JAIL_TOWN} with the watch at your heels. They'll remember your face.",
                args => MBTextManager.SetTextVariable("LMMI_JAIL_TOWN", _aftercareIn?.Name ?? TextObject.GetEmpty()));
            starter.AddGameMenuOption("lmmi_street_escaped", "lmmi_street_escaped_leave",
                "{=lmmi_street_escaped_leave}Keep riding",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                args =>
                {
                    _aftercare = Aftercare.None;
                    _aftercareIn = null;
                    PlayerEncounter.LeaveSettlement();
                    PlayerEncounter.Finish(true);
                    LmmiLog.Info("Street: you got away.");
                }, isLeave: true);
        }

        // ---- After a fight: the street calms down ----

        /// <summary>After someone else's fight (the hired swords): the frightened calm down a moment later.</summary>
        internal static void CalmAround(Vec3 around) => Instance?.QueueCalm(around);

        private void QueueCalm(Vec3 around, float radius = CalmRadius)
        {
            // A wider calm already queued (the whole village, after the looters) isn't narrowed by a later one.
            if (_calmAt >= 0f && _calmRadius > radius) radius = _calmRadius;
            _calmCenter = around;
            _calmRadius = radius;
            _calmAt = _sceneTime + 2f;
        }

        /// <summary>Everyone the fight frightened goes back to their day (vanilla only stops their running; they stay
        /// panicked). Not anyone still fighting: a brawl in progress, or guards you're resisting.</summary>
        private void CalmBystanders(Mission mission, Vec3 around, float radius = CalmRadius)
        {
            if (mission.GetMissionBehavior<MissionFightHandler>()?.IsThereActiveFight() == true) { QueueCalm(around, radius); return; }
            int calmed = 0;
            foreach (var a in mission.Agents.ToList())
            {
                if (!a.IsActive() || !a.IsHuman || a == Agent.Main || _resisting.Contains(a)) continue;
                if (a.Position.Distance(around) > radius) continue;
                if (!(a.IsAlarmed() || a.IsCautious() || a.IsPatrollingCautious())) continue;
                if (_scene?.Brawl?.On == true && _scene.Brawl.Lads.Contains(a)) continue;
                if (_aftermaths.OfType<BrawlPlaysOut>().Any(b => b.Brawling && b.Lads.Contains(a))) continue;
                CalmAgent(a);
                calmed++;
            }
            if (calmed > 0) LmmiLog.Info($"Street: {calmed} bystander(s) calm down{(radius >= CalmEveryone ? " (the whole scene)" : "")}.");
        }

        private void TryTalk(Scene sc, Mission mission)
        {
            var who = sc.TalkTo;
            if (who == null || sc.Talking || !who.IsActive()) return;
            if (mission.Mode == MissionMode.Conversation || mission.Mode == MissionMode.Battle) return;
            if (_afterTalk.Count > 0) return;
            if (Dist(who, Agent.Main) > 3.2f) return;
            if (sc.Step == Step.Asking && !sc.Hunched) Hunch(sc);
            sc.Talking = true;
            sc.Chosen = false;
            sc.TalkAt = _sceneTime;
            mission.GetMissionBehavior<MissionConversationLogic>()?.StartConversation(who, setActionsInstantly: false);
        }

        private void OnConversationEnded(IEnumerable<CharacterObject> characters)
        {
            var sc = _scene;
            if (sc == null || !sc.Talking || sc.TalkTo == null) return;
            if (!characters.Contains(sc.TalkTo.Character as CharacterObject)) return;
            sc.Talking = false;
            if (sc.Chosen) return;

            // Left without answering: it goes on without you.
            _afterTalk.Add(() =>
            {
                if (_scene != sc) return;
                switch (sc.Step)
                {
                    case Step.Asking:
                    case Step.Confronting:
                    case Step.Caught:
                    case Step.Comforting:
                        sc.Outcome = "you walked away";
                        LetItPlayOut(sc);
                        break;
                    case Step.Questioned:
                        Fined(sc);   // walked out on the watch: the fine stands
                        break;
                    default:
                        Finish(sc);
                        break;
                }
            });
        }

        private void BeginConfrontation(Scene sc)
        {
            var main = Agent.Main;
            Contour(sc.Requester, null);
            if (sc.Ambush) { Reveal(sc); return; }
            Direct(sc.Requester)?.Hold(face: sc.Spot);
            switch (sc.Kind)
            {
                case Kind.Rescue:
                {
                    var lead = sc.Actors.FirstOrDefault(a => a.IsActive());
                    if (lead == null) { sc.Outcome = "the men left"; Finish(sc); return; }
                    foreach (var r in sc.Actors.Where(a => a != lead && a.IsActive())) Direct(r)?.Hold(face: main);
                    Direct(lead)?.Follow(main, 1.8f, run: false);
                    sc.TalkTo = lead;
                    break;
                }
                case Kind.Injured:
                    // He's in no state to talk: whoever fetched you does.
                    Direct(sc.Requester)?.Follow(main, 1.8f, run: false);
                    sc.TalkTo = sc.Requester;
                    break;
                case Kind.Debt:
                {
                    var lead = sc.Actors.FirstOrDefault(a => a.IsActive());
                    if (lead == null) { sc.Outcome = "the collectors left"; Finish(sc); return; }
                    foreach (var r in sc.Actors.Where(a => a != lead && a.IsActive())) Direct(r)?.Hold(face: main);
                    Direct(lead)?.Follow(main, 1.8f, run: false);
                    sc.TalkTo = lead;
                    break;
                }
                case Kind.Shakedown:
                {
                    var guard = sc.Actors.FirstOrDefault(a => a.IsActive());
                    if (guard == null) { sc.Outcome = "the guard left"; Finish(sc); return; }
                    Direct(sc.Victim)?.Hold(face: main, loop: "act_bullied");
                    Direct(guard)?.Follow(main, 1.8f, run: false);
                    sc.TalkTo = guard;
                    break;
                }
                case Kind.Dispute:
                {
                    var accuser = sc.Actors[0];
                    if (!accuser.IsActive()) { sc.Outcome = "they went home"; Finish(sc); return; }
                    if (sc.Actors.Count > 1 && sc.Actors[1].IsActive())
                    {
                        Direct(sc.Actors[1])?.Hold(face: main, loop: "act_argue_2");
                        Bark(sc.Actors[1], Flavor.Pick("{=lmmi_village_dispute_other}Don't listen to him! That stone's stood there since my grandfather's day!",
                        "{=lmmi_village_dispute_other_2}Lies! My family's farmed that strip for three generations!",
                        "{=lmmi_village_dispute_other_3}Don't believe a word! He moved it himself, the snake!"));
                    }
                    Direct(accuser)?.Follow(main, 1.8f, run: false);
                    sc.TalkTo = accuser;
                    break;
                }
                case Kind.Harvest:
                {
                    // The work: a short scene in the field beside him, then the screen goes dark and the sun has moved
                    // when it comes back (see StreetHarvest.cs).
                    BeginFieldWork(sc, yourMen: false);
                    return;
                }
                case Kind.Raiders:
                {
                    var lead = sc.Actors.FirstOrDefault(a => a.IsActive());
                    if (lead == null) { sc.Outcome = "the looters left"; Finish(sc); return; }
                    Bark(lead, Flavor.Pick("{=lmmi_village_raiders_spotted}Oi! This one wants a fight — have at {?PLAYER.GENDER}her{?}him{\\?}!",
                        "{=lmmi_village_raiders_spotted_2}Look, lads — a hero! Get {?PLAYER.GENDER}her{?}him{\\?}!",
                        "{=lmmi_village_raiders_spotted_3}Fresh meat! Have at {?PLAYER.GENDER}her{?}him{\\?}!"));
                    ArmedFight(sc, won => OnRaidersFightEnd(sc, won));
                    return;
                }
                case Kind.LostChild:
                    // She only knows roughly: "He was just here!" You look.
                    Bark(sc.Requester, Flavor.Pick("{=lmmi_street_lost_here}He was just here — I swear he was just here!",
                        "{=lmmi_street_lost_here_2}He was right here a moment ago!",
                        "{=lmmi_street_lost_here_3}This is where I lost him — oh gods, where is he?"));
                    Direct(sc.Requester)?.Hold(face: main);
                    Contour(sc.Requester, GuideContour);
                    sc.TalkTo = null;
                    Go(sc, Step.Searching);
                    return;
                default:
                {
                    sc.Brawl?.Stop();
                    var a1 = sc.Actors[0];
                    if (!a1.IsActive()) { sc.Outcome = "the youths left"; Finish(sc); return; }
                    Direct(sc.Actors[1])?.Hold(face: main);
                    Direct(a1)?.Follow(main, 1.8f, run: false);
                    sc.TalkTo = a1;
                    break;
                }
            }
            Go(sc, Step.Confronting);
        }

        /// <summary>You refused, backed down or never came: the scene carries on without you, visibly.</summary>
        private void LetItPlayOut(Scene sc, bool shrugged = true)
        {
            var mission = Mission.Current;
            if (sc.Revealed)
            {
                // You walked away from the toughs (or ran): they don't let you.
                if (sc.Step == Step.Confronting && sc.Actors.Any(a => a.IsActive())) { Chase(sc); return; }
                Finish(sc);
                return;
            }
            switch (sc.Kind)
            {
                case Kind.Rescue when mission != null && sc.Victim != null && sc.Victim.IsActive():
                {
                    var lead = sc.Actors.FirstOrDefault(a => a.IsActive());
                    var away = lead != null ? FarPoint(mission, lead, sc.Victim) : null;
                    if (lead != null && away != null)
                    {
                        var drag = new DragAway(lead, sc.Actors.Where(a => a != lead && a.IsActive()).ToList(), sc.Victim, away.Value, sc.Requester);
                        HandOver(sc, drag, sc.Actors.Append(sc.Victim).ToArray());
                        drag.Begin();
                    }
                    break;
                }
                case Kind.Brawl when mission != null && sc.Brawl != null && sc.Actors.Count == 2:
                {
                    var brawl = sc.Brawl;
                    sc.Brawl = null;   // the aftermath owns it now
                    var guard = FindGuard(mission, sc.Actors[0].Position, 80f);
                    var after = new BrawlPlaysOut(brawl, guard, sc.Requester, shrugged);
                    HandOver(sc, after, brawl.Lads.ToArray());
                    after.Begin(mission);
                    break;
                }
                case Kind.Thief:
                {
                    var thief = sc.Actors.FirstOrDefault();
                    if (thief != null && thief.IsActive())
                    {
                        Contour(thief, null);
                        HandOver(sc, new SlipAway(thief, sc.Escape), thief);
                    }
                    break;
                }
                case Kind.Debt when mission != null && sc.Victim != null && sc.Victim.IsActive():
                case Kind.Shakedown when mission != null && sc.Victim != null && sc.Victim.IsActive():
                {
                    // They get what they came for, and walk off.
                    var lead = sc.Actors.FirstOrDefault(a => a.IsActive());
                    if (lead == null) break;
                    var menace = new Menace(lead, sc.Actors.Where(a => a != lead && a.IsActive()).ToList(), sc.Victim,
                        (sc.Kind == Kind.Debt
                            ? Flavor.Pick("{=lmmi_street_debt_menace}Next week. Every coin — or it's your hands.",
                        "{=lmmi_street_debt_menace_2}Next time we come, it's your fingers.",
                        "{=lmmi_street_debt_menace_3}One week. Then we stop asking nicely.")
                            : Flavor.Pick("{=lmmi_street_shakedown_menace}Pleasure doing business. Same time next week.",
                        "{=lmmi_street_shakedown_menace_2}See you next week, friend. Have the fine ready.",
                        "{=lmmi_street_shakedown_menace_3}That's the law, that is. Same time next week.")),
                        (sc.Kind == Kind.Debt
                            ? Flavor.Pick("{=lmmi_street_debt_after}...They took the lot. Everything.",
                        "{=lmmi_street_debt_after_2}...Everything. They took everything.",
                        "{=lmmi_street_debt_after_3}...How am I to feed my family now?")
                            : Flavor.Pick("{=lmmi_street_shakedown_after}Thieves. The watch are the worst thieves of all.",
                        "{=lmmi_street_shakedown_after_2}That's the watch for you. Robbers with a badge.",
                        "{=lmmi_street_shakedown_after_3}Who do we call when the watch is the thief?")));
                    HandOver(sc, menace, sc.Actors.Append(sc.Victim).ToArray());
                    break;
                }
                case Kind.Dispute when mission != null && mission.PlayerEnemyTeam != null && mission.PlayerAllyTeam != null && sc.Actors.Count == 2 && sc.Actors.All(a => a.IsActive()):
                {
                    // Nobody settled it: they settle it themselves.
                    var brawl = new Brawl(sc.Actors[0], sc.Actors[1]);
                    var after = new BrawlPlaysOut(brawl, null, sc.Requester, shrugged);
                    HandOver(sc, after, brawl.Lads.ToArray());
                    after.Begin(mission);
                    break;
                }
                case Kind.Raiders when mission != null:
                {
                    // They drive the flock off.
                    foreach (var a in sc.Actors.Where(x => x.IsActive()).ToList())
                    {
                        var to = FarPoint(mission, a, sc.Actors.ToArray());
                        if (to != null) HandOver(sc, new SlipAway(a, to.Value, fade: true), a);
                    }
                    if (sc.Settlement.Village != null) sc.Settlement.Village.Hearth = Math.Max(0f, sc.Settlement.Village.Hearth - 3f);
                    Say("{=lmmi_village_raiders_gone}The looters drive half the flock off into the hills.",
                        "{=lmmi_village_raiders_gone_2}The looters drive off half the sheep, whooping as they go.",
                        "{=lmmi_village_raiders_gone_3}Bleating fades into the hills. Half the flock is gone.");
                    break;
                }
                case Kind.LostChild when mission != null && sc.Actors.FirstOrDefault() is Agent lost && lost.IsActive() && sc.Requester.IsActive():
                {
                    // She finds him herself.
                    var find = new Reunite(sc.Requester, lost, together: false);
                    HandOver(sc, find, lost);
                    sc.RequesterGone = true;
                    break;
                }
                case Kind.Injured when mission != null && sc.Actors.FirstOrDefault() is Agent hurt && hurt.IsActive():
                {
                    // Whoever fetched you gets him on his feet, and they hobble off together.
                    var away = FarPoint(mission, hurt, sc.Requester);
                    if (away != null && sc.Requester.IsActive())
                    {
                        var hobble = new HobbleOff(hurt, sc.Requester, away.Value);
                        HandOver(sc, hobble, hurt);
                        sc.RequesterGone = true;
                        hobble.Begin();
                    }
                    break;
                }
            }
            Finish(sc);
        }

        // ---- Aftermaths ----

        /// <summary>The men lead the girl off.</summary>
        private sealed class DragAway : Aftermath
        {
            private readonly Agent _lead, _victim;
            private readonly List<Agent> _men;
            private readonly WorldPosition _to;
            private readonly Agent? _requester;
            private bool _barked;

            public DragAway(Agent lead, List<Agent> men, Agent victim, WorldPosition to, Agent? requester)
            { _lead = lead; _men = men; _victim = victim; _to = to; _requester = requester; }

            public void Begin()
            {
                Direct(_lead)?.GoTo(_to, run: false);
                foreach (var m in _men) Direct(m)?.Follow(_lead, 1.6f, run: false);
                Direct(_victim)?.Follow(_lead, 1.2f, run: false);
                Bark(_lead, Flavor.Pick("{=lmmi_street_drag_lead}Come along, girl. Nobody's coming for you.",
                        "{=lmmi_street_drag_lead_2}Quiet, girl. You're coming with us.",
                        "{=lmmi_street_drag_lead_3}Move it, girl. Nobody's going to help you."));
                LmmiLog.Info("Street: the men lead the girl away.");
            }

            public override bool Tick(StreetEventsBehavior owner, float now)
            {
                if (!_barked && now - StartedAt > 6f)
                {
                    _barked = true;
                    Bark(_requester, Flavor.Pick("{=lmmi_street_drag_requester}Heaven help her. Nobody else will.",
                        "{=lmmi_street_drag_requester_2}Nobody will help her. Nobody.",
                        "{=lmmi_street_drag_requester_3}Gods forgive us all."));
                }
                return now - StartedAt > 30f || !_lead.IsActive() || Direct(_lead)?.Arrived == true && now - StartedAt > 5f;
            }

            public override void End()
            {
                Release(_lead);
                foreach (var m in _men) Release(m);
                Release(_victim);
            }
        }

        /// <summary>Nobody stepped in: the brawl ends with a knockout, or the watch wades in first.</summary>
        private sealed class BrawlPlaysOut : Aftermath
        {
            private readonly Brawl _brawl;
            private readonly Agent? _guard;
            private readonly Agent? _requester;
            private readonly bool _shrugged;
            private float _koAt;
            private Agent? _loser;
            private bool _koStarted, _settled, _guardArrived;
            private float _settledAt;

            public BrawlPlaysOut(Brawl brawl, Agent? guard, Agent? requester, bool shrugged)
            { _brawl = brawl; _guard = guard; _requester = requester; _shrugged = shrugged; }

            public IReadOnlyList<Agent> Lads => _brawl.Lads;
            public bool Brawling => _brawl.On;

            /// <summary>You stepped in after all: stop, send the guard back, and hand the youths over.</summary>
            public List<Agent> TakeOver()
            {
                if (_brawl.On) _brawl.Stop();
                Release(_guard);
                return _brawl.Lads.Where(l => l.IsActive()).ToList();
            }

            public void Begin(Mission mission)
            {
                var a = _brawl.Lads[0];
                var b = _brawl.Lads[1];
                if (!_brawl.On && a.IsActive() && b.IsActive())
                {
                    Direct(a)?.Hold(face: b);
                    Direct(b)?.Follow(a, 1.3f, run: true);
                    if (Dist(a, b) < 3f) _brawl.Start(mission);
                }
                _koAt = StartedAt + 10f + MBRandom.RandomFloat * 5f;
                _loser = _brawl.Lads[MBRandom.RandomInt(2)];
                if (_guard != null) Direct(_guard)?.Follow(a, 2.2f, run: true);
                LmmiLog.Info($"Street: the brawl goes on{(_guard != null ? $"; {_guard.Name} of the watch is coming" : "; no watch nearby")}.");
            }

            public override bool Tick(StreetEventsBehavior owner, float now)
            {
                var mission = Mission.Current;
                if (mission == null) return true;
                var a = _brawl.Lads[0];
                var b = _brawl.Lads[1];

                if (!_settled)
                {
                    // Squaring up, if they hadn't yet.
                    if (!_brawl.On && a.IsActive() && b.IsActive() && Dist(a, b) < 1.9f) _brawl.Start(mission);

                    // The watch gets there first.
                    if (_guard != null && _guard.IsActive() && _brawl.Lads.Any(l => l.IsActive() && Dist(_guard, l) < 3.2f))
                    {
                        _brawl.Stop();
                        _guardArrived = true;
                        Bark(_guard, Flavor.Pick("{=lmmi_street_guard_breakup}Oi! Break it up, the pair of you! Go home before I take you in!",
                        "{=lmmi_street_guard_breakup_2}Oi! Enough! Both of you, home — now!",
                        "{=lmmi_street_guard_breakup_3}Break it up! Do I have to crack your heads?"));
                        Scatter(mission);
                        Settle(now, "the watch broke it up");
                    }
                    // Or someone goes down.
                    else if (_brawl.On && !_koStarted && now >= _koAt && _loser != null && _loser.IsActive())
                    {
                        _koStarted = true;
                        _loser.SetMortalityState(Agent.MortalityState.Mortal);
                        HiredBeatDownPatch.AlsoGuarded.Remove(_loser);
                        _loser.Health = Math.Min(_loser.Health, 2f);
                    }
                    else if (_koStarted && _loser != null && !_loser.IsActive())
                    {
                        _brawl.Stop();
                        Say("{=lmmi_street_brawl_ko}One of the youths goes down and doesn't get up. The other stands over him, breathing hard, then runs.",
                        "{=lmmi_street_brawl_ko_2}A sickening crack — one of them is down and not moving. The other bolts.",
                        "{=lmmi_street_brawl_ko_3}One lad drops like a sack of grain. The other stares, then runs.");
                        Scatter(mission);
                        Settle(now, "someone got knocked out");
                    }
                    else if (now - StartedAt > 45f)
                    {
                        _brawl.Stop();
                        Scatter(mission);
                        Settle(now, "they wore themselves out");
                    }
                    return false;
                }

                // The watch arrives late to a knockout.
                if (!_guardArrived && _guard != null && _guard.IsActive() && _loser != null
                    && Dist(_guard, a.IsActive() ? a : b.IsActive() ? b : _guard) < 4f)
                {
                    _guardArrived = true;
                    Bark(_guard, Flavor.Pick("{=lmmi_street_guard_late}What happened here? ...Someone fetch a healer.",
                        "{=lmmi_street_guard_late_2}What's all this? ...Get a healer, quick.",
                        "{=lmmi_street_guard_late_3}Who did this? Someone fetch a healer!"));
                }
                return now - _settledAt > (_guard != null && !_guardArrived ? 12f : 5f);
            }

            private void Settle(float now, string how)
            {
                _settled = true;
                _settledAt = now;
                LmmiLog.Info($"Street: the brawl is over — {how}.");
                if (_shrugged)
                    Bark(_requester, Flavor.Pick("{=lmmi_street_brawl_reproach}Well. Thanks for nothing.",
                        "{=lmmi_street_brawl_reproach_2}You could have stopped it. You didn't.",
                        "{=lmmi_street_brawl_reproach_3}A lot of help you were."));
            }

            private void Scatter(Mission mission)
            {
                foreach (var lad in _brawl.Lads.Where(l => l.IsActive()))
                {
                    var to = FarPoint(mission, lad, _brawl.Lads.ToArray());
                    if (to != null) Direct(lad)?.GoTo(to.Value, run: false);
                }
            }

            public override void End()
            {
                if (_brawl.On) _brawl.Stop();
                foreach (var l in _brawl.Lads) Release(l);
                Release(_guard);
            }
        }

        /// <summary>The collectors (or the crooked guard) take what they came for, say their piece, and walk off.</summary>
        private sealed class Menace : Aftermath
        {
            private readonly Agent _lead, _victim;
            private readonly List<Agent> _men;
            private readonly TextObject _line, _after;
            private int _stage;

            public Menace(Agent lead, List<Agent> men, Agent victim, TextObject line, TextObject after)
            { _lead = lead; _men = men; _victim = victim; _line = line; _after = after; }

            public override bool Tick(StreetEventsBehavior owner, float now)
            {
                if (!_lead.IsActive()) return true;
                if (_stage == 0 && now - StartedAt > 1f) { _stage = 1; Bark(_lead, _line); }
                if (_stage == 1 && now - StartedAt > 8f)
                {
                    _stage = 2;
                    var mission = Mission.Current;
                    foreach (var m in _men.Append(_lead).Where(a => a.IsActive()))
                    {
                        var to = mission != null ? FarPoint(mission, m, _victim) : null;
                        if (to != null) Direct(m)?.GoTo(to.Value, run: false);
                    }
                }
                if (_stage == 2 && now - StartedAt > 12f) { _stage = 3; Bark(_victim, _after); }
                return now - StartedAt > 25f;
            }

            public override void End()
            {
                Release(_lead);
                foreach (var m in _men) Release(m);
                Release(_victim);
            }
        }

        /// <summary>The mother finds her little one on her own.</summary>
        private sealed class Reunite : Aftermath
        {
            private readonly Agent _mother, _child;
            private bool _together;
            private float _togetherAt = -1f;

            /// <param name="together">Already together (you brought him back): they just go home.</param>
            public Reunite(Agent mother, Agent child, bool together)
            {
                _mother = mother;
                _child = child;
                if (together) GoHome();
                else Direct(mother)?.Follow(child, 1.2f, run: true);   // she goes looking herself
            }

            private void GoHome()
            {
                _together = true;
                var mission = Mission.Current;
                var away = mission != null ? FarPoint(mission, _mother, _child) : null;
                if (away != null) Direct(_mother)?.GoTo(away.Value, run: false);
                Direct(_child)?.Follow(_mother, 1f, run: false);
            }

            public override bool Tick(StreetEventsBehavior owner, float now)
            {
                if (!_mother.IsActive() || !_child.IsActive()) return true;
                if (_togetherAt < 0f && _together) _togetherAt = now;
                if (!_together && (Dist(_mother, _child) < 2f || now - StartedAt > 35f))
                {
                    Bark(_mother, Flavor.Pick("{=lmmi_street_lost_scold}There you are! Don't you ever, ever do that again!",
                        "{=lmmi_street_lost_scold_2}Where have you been? You scared me half to death!",
                        "{=lmmi_street_lost_scold_3}Never, ever run off like that again! Come here!"));
                    GoHome();
                    _togetherAt = now;
                }
                return (_together && now - _togetherAt > 25f) || now - StartedAt > 75f;
            }

            public override void End() { Release(_mother); Release(_child); }
        }

        /// <summary>The one who fell is helped to his feet, and limps off leaning on whoever fetched you.</summary>
        private sealed class HobbleOff : Aftermath
        {
            private readonly Agent _hurt, _helper;
            private readonly WorldPosition _to;
            private bool _up;

            public HobbleOff(Agent hurt, Agent helper, WorldPosition to) { _hurt = hurt; _helper = helper; _to = to; }

            public void Begin() => Direct(_helper)?.Follow(_hurt, 1.2f, run: false);

            public override bool Tick(StreetEventsBehavior owner, float now)
            {
                if (!_hurt.IsActive()) return true;
                if (!_up && (!_helper.IsActive() || Dist(_helper, _hurt) < 1.8f || now - StartedAt > 12f))
                {
                    _up = true;
                    Direct(_hurt)?.GoTo(_to, run: false, speed: 0.35f);   // limping
                    if (_helper.IsActive()) Direct(_helper)?.Follow(_hurt, 1f, run: false);
                }
                return now - StartedAt > 40f || (_up && now - StartedAt > 15f && Direct(_hurt)?.Arrived == true);
            }

            public override void End()
            {
                Release(_hurt);
                Release(_helper);
            }
        }

        /// <summary>The thief strolls off with the purse (or with his half of it).</summary>
        private sealed class SlipAway : Aftermath
        {
            private readonly Agent _thief;
            private readonly bool _fade;
            /// <param name="fade">Brought in for the scene: gone for good once out of sight.</param>
            public SlipAway(Agent thief, WorldPosition to, bool fade = false) { _thief = thief; _fade = fade; Direct(thief)?.GoTo(to, run: !fade ? false : true); }
            public override bool Tick(StreetEventsBehavior owner, float now) => now - StartedAt > 15f || !_thief.IsActive();
            public override void End()
            {
                if (_fade && _thief.IsActive()) _thief.FadeOut(true, true);
                else Release(_thief);
            }
        }

        /// <summary>The watch walks the thief off.</summary>
        private sealed class Arrest : Aftermath
        {
            private readonly Agent _thief;
            private readonly Agent? _guard;
            private readonly WorldPosition _to;
            private bool _collared;
            private float _collaredAt;

            public Arrest(Agent thief, Agent? guard, WorldPosition to) { _thief = thief; _guard = guard; _to = to; }

            public void Begin()
            {
                Direct(_thief)?.Hold(face: _guard, loop: "act_scared_idle_2");
                if (_guard != null) Direct(_guard)?.Follow(_thief, 1.3f, run: true);
                else Say("{=lmmi_street_thief_taken}The watch drags the boy off, still protesting.",
                        "{=lmmi_street_thief_taken_2}The guard hauls the boy away by the collar.",
                        "{=lmmi_street_thief_taken_3}The watch takes the thief off, kicking and wailing.");
                LmmiLog.Info(_guard != null ? $"Street: {_guard.Name} of the watch comes for the thief." : "Street: no watch nearby; the thief is taken off-scene.");
            }

            public override bool Tick(StreetEventsBehavior owner, float now)
            {
                if (!_thief.IsActive()) return true;
                if (_guard == null || !_guard.IsActive()) return now - StartedAt > 2f;
                if (!_collared && Dist(_guard, _thief) < 2.4f)
                {
                    _collared = true;
                    _collaredAt = now;
                    Bark(_guard, Flavor.Pick("{=lmmi_street_guard_collar}Right, you. You're coming with me.",
                        "{=lmmi_street_guard_collar_2}That's it, you. You're coming with me.",
                        "{=lmmi_street_guard_collar_3}Hands where I can see them. You're coming along."));
                    Direct(_guard)?.GoTo(_to, run: false);
                    Direct(_thief)?.Follow(_guard, 1.1f, run: false);
                }
                return (_collared && now - _collaredAt > 14f) || now - StartedAt > 50f;
            }

            public override void End()
            {
                Release(_guard);
                if (_thief.IsActive()) _thief.FadeOut(false, true);   // off to the cells (never during a conversation: aftermaths tick outside one)
            }
        }

        // ---- The youths' brawl ----

        private static void ForceFight(Agent agent)
        {
            var alarmed = agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<AlarmedBehaviorGroup>();
            if (alarmed == null) return;
            alarmed.DisableCalmDown = true;
            if (alarmed.GetBehavior<FightBehavior>() == null) alarmed.AddBehavior<FightBehavior>();
            alarmed.SetScriptedBehavior<FightBehavior>();
            agent.SetWatchState(Agent.WatchState.Alarmed);
        }

        // ---- Your fights ----

        [NonSerialized] private Scene? _armedFight;

        /// <summary>Weapons out (looters don't do fists). You're beaten down at low health, never killed.</summary>
        private void ArmedFight(Scene sc, Action<bool> onEnd)
        {
            var mission = Mission.Current;
            var main = Agent.Main;
            var handler = mission?.GetMissionBehavior<MissionFightHandler>();
            var foes = sc.Actors.Where(a => a.IsActive()).ToList();
            if (handler == null || main == null || !main.IsActive() || foes.Count == 0 || handler.IsThereActiveFight())
            {
                sc.Outcome = "the fight never started";
                Finish(sc);
                return;
            }
            foreach (var f in foes) Release(f);
            Go(sc, Step.Fighting);
            _armedFight = sc;
            // Beaten down, never killed: blows can't take your last hit point (HiredBeatDownPatch; Immortal would
            // zero all damage in this game version, and the 12% beaten-down check in Update could never trigger).
            HiredBeatDownPatch.Guarded = main;
            _streetFight = true;
            _streetFightIn = sc.Settlement;
            LmmiLog.Info($"Street: an armed fight — you against {foes.Count}.");
            handler.StartCustomFight(new List<Agent> { main }, foes, dropWeapons: false, isItemUseDisabled: false,
                won =>
                {
                    try
                    {
                        _armedFight = null;
                        _streetFight = false;
                        var me = Agent.Main;
                        if (ReferenceEquals(HiredBeatDownPatch.Guarded, me)) HiredBeatDownPatch.Guarded = null;
                        if (me != null && me.IsActive()) QueueCalm(me.Position);
                        onEnd(won);
                    }
                    catch (Exception ex) { LmmiLog.Error("Street: armed fight end threw", ex); if (_scene == sc) Finish(sc); }
                });
        }

        private void OnRaidersFightEnd(Scene sc, bool won)
        {
            // Whoever's still standing runs for it.
            var mission = Mission.Current;
            foreach (var a in sc.Actors.Where(x => x.IsActive()).ToList())
            {
                var to = mission != null ? FarPoint(mission, a, sc.Actors.ToArray()) : null;
                if (to != null) HandOver(sc, new SlipAway(a, to.Value, fade: true), a);
                else a.FadeOut(true, true);
            }
            // The whole village was frightened, not just whoever stood near you — and whoever fetched you ran with
            // the rest: calm them first, or they never come back to thank you.
            CalmAfterLooters(sc);
            var village = sc.Settlement.Village;
            if (won)
            {
                SteppedUp(sc, 5f, "drove looters off a village's flock");
                Headman(sc, 2);
                int loot = 20 + MBRandom.RandomInt(61);
                Hero.MainHero.ChangeHeroGold(loot);
                if (village != null) village.Hearth += 2f;
                var msg = new TextObject("{=lmmi_village_raiders_won}The looters scatter, leaving the flock and {GOLD}{GOLD_ICON} in a dropped purse.");
                msg.SetTextVariable("GOLD", loot);
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
                MarkGrateful(sc.Requester);
                Thank(sc, sc.Requester, "raiders");
                return;
            }
            TownStandingBehavior.Adjust(sc.Settlement, 1f, "fought looters for a village's flock");
            if (village != null) village.Hearth = Math.Max(0f, village.Hearth - 3f);
            Say("{=lmmi_village_raiders_lost}You come to in the grass. The looters are gone — and so is half the flock.",
                        "{=lmmi_village_raiders_lost_2}You wake in the grass with a splitting head. Half the flock's gone with the looters.",
                        "{=lmmi_village_raiders_lost_3}The world spins back into place. The looters — and half the sheep — are long gone.");
            sc.Outcome = "the looters won";
            Finish(sc);
        }

        private void StartFight(Scene sc, float healthPerLevel, Action<bool> onEnd)
        {
            if (sc.Brawl?.On == true) sc.Brawl.Stop();
            Go(sc, Step.Fighting);
            if (!Fistfight(sc.Actors.Where(a => a.IsActive()).ToList(), healthPerLevel,
                    won => { try { onEnd(won); } catch (Exception ex) { LmmiLog.Error("Street: fight end threw", ex); if (_scene == sc) Finish(sc); } }))
            {
                sc.Outcome = "the fight never started";
                Finish(sc);
            }
        }

        /// <summary>You against them, fists only (vanilla's StartCustomFight); false if it couldn't start.</summary>
        internal static bool Fistfight(List<Agent> foes, float healthPerLevel, Action<bool> onEnd)
        {
            var mission = Mission.Current;
            var handler = mission?.GetMissionBehavior<MissionFightHandler>();
            if (foes.Count == 0 || handler == null || handler.IsThereActiveFight() || Agent.Main == null || !Agent.Main.IsActive()) return false;

            // Tougher the bigger your name: more health per level of yours.
            float mult = Math.Min(2.2f, 0.8f + healthPerLevel * Hero.MainHero.Level);
            foreach (var f in foes)
            {
                f.SetMortalityState(Agent.MortalityState.Mortal);
                HiredBeatDownPatch.AlsoGuarded.Remove(f);
                StripWeapons(f, null);
                f.HealthLimit *= mult;
                f.Health = f.HealthLimit;
            }
            var stash = new MissionEquipment();
            StripWeapons(Agent.Main, stash);
            if (Instance is StreetEventsBehavior me) { me._streetFight = true; me._streetFightIn = Settlement.CurrentSettlement; }
            LmmiLog.Info($"Street: fistfight — you against {foes.Count} ({string.Join(", ", foes.Select(f => $"{f.Name} {f.HealthLimit:0} hp"))}), health ×{mult:0.00}.");

            handler.StartCustomFight(new List<Agent> { Agent.Main }, foes, dropWeapons: false, isItemUseDisabled: false,
                won =>
                {
                    try { ReturnWeapons(stash); } catch (Exception ex) { LmmiLog.Error("Street: ReturnWeapons threw", ex); }
                    if (Instance is StreetEventsBehavior me2) me2._streetFight = false;
                    if (Agent.Main != null && Agent.Main.IsActive()) Instance?.QueueCalm(Agent.Main.Position);
                    onEnd(won);
                });
            return true;
        }

        // ---- You hit someone in a scene: it's your fight now ----

        /// <summary>From <see cref="StreetHitWatcher"/>: the player landed a blow on someone.</summary>
        internal static void OnPlayerHit(Agent victim)
        {
            var self = Instance;
            if (self == null || victim == null || victim == Agent.Main) return;
            try { self.PlayerHit(victim); }
            catch (Exception ex) { LmmiLog.Error("Street: OnPlayerHit threw", ex); }
        }

        [NonSerialized] private bool _wadingIn;

        private void PlayerHit(Agent victim)
        {
            if (_wadingIn) return;
            if (Mission.Current?.GetMissionBehavior<MissionFightHandler>()?.IsThereActiveFight() == true) return;

            // A brawler in the scene: you waded in.
            var sc = _scene;
            if (sc != null && sc.Step != Step.Fighting && (sc.Actors.Contains(victim) || sc.Crew.Contains(victim)))
            {
                if (sc.Kind == Kind.Thief && !sc.Revealed && sc.Actors.Contains(victim))
                {
                    if (sc.Step != Step.Chasing) return;
                    if (sc.Ambush) { Reveal(sc); return; }   // his friends saw that
                    // A thump stops him as well as a hand on the collar.
                    sc.TalkTo = victim;
                    Contour(victim, null);
                    Direct(victim)?.Hold(face: Agent.Main, loop: "act_scared_idle_2");
                    Go(sc, Step.Caught);
                    return;
                }
                if ((sc.Kind == Kind.Injured || sc.Kind == Kind.LostChild || sc.Kind == Kind.Harvest) && !sc.Ambush) return;   // no threat to anyone
                victim.SetMortalityState(Agent.MortalityState.Mortal);
                _wadingIn = true;
                LmmiLog.Info($"Street: you hit {victim.Name} — it's your fight now ({sc.Kind}).");
                _afterTalk.Add(() =>
                {
                    _wadingIn = false;
                    if (_scene != sc || sc.Step == Step.Fighting) return;
                    Contour(sc.Requester, null);
                    if (sc.Ambush)
                    {
                        if (!sc.Revealed) Reveal(sc);
                        if (_scene != sc) return;
                        StartFight(sc, 0.04f, won => OnAmbushFightEnd(sc, won));
                    }
                    else if (sc.Kind == Kind.Rescue || sc.Kind == Kind.Debt) StartFight(sc, 0.04f, won => OnRescueFightEnd(sc, won));
                    else if (sc.Kind == Kind.Shakedown) StartFight(sc, 0.03f, won => OnGuardFightEnd(sc, won));
                    else if (sc.Kind == Kind.Raiders) ArmedFight(sc, won => OnRaidersFightEnd(sc, won));
                    else StartFight(sc, 0.02f, won => OnBrawlFightEnd(sc, won));
                });
                return;
            }

            // A brawl you left to play out: same, with less credit.
            var brawl = _aftermaths.OfType<BrawlPlaysOut>().FirstOrDefault(b => b.Lads.Contains(victim));
            if (brawl != null)
            {
                victim.SetMortalityState(Agent.MortalityState.Mortal);
                _wadingIn = true;
                LmmiLog.Info($"Street: you hit {victim.Name} — you waded into the brawl you'd left.");
                _afterTalk.Add(() =>
                {
                    _wadingIn = false;
                    if (!_aftermaths.Contains(brawl)) return;
                    var lads = brawl.TakeOver();
                    _aftermaths.Remove(brawl);
                    var settlement = Settlement.CurrentSettlement;
                    Fistfight(lads, 0.02f, won =>
                    {
                        foreach (var l in lads) Release(l);
                        if (won && settlement != null)
                            TownStandingBehavior.MarkSteppedUp(settlement, 2f, "knocked sense into two brawlers after all");
                    });
                });
            }
        }

        private static void StripWeapons(Agent agent, MissionEquipment? stash)
        {
            stash?.FillFrom(agent.Equipment);
            agent.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);
            agent.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
            for (var i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                if (!agent.Equipment[i].IsEmpty) agent.RemoveEquippedWeapon(i);
        }

        private static void ReturnWeapons(MissionEquipment stash)
        {
            var main = Agent.Main;
            if (main == null || !main.IsActive()) return;
            for (var i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                var w = stash[i];
                if (!w.IsEmpty && main.Equipment[i].IsEmpty) main.EquipWeaponWithNewEntity(i, ref w);
            }
        }

        private void OnRescueFightEnd(Scene sc, bool won)
        {
            if (sc.WatchInterrupted) return;   // the watch stopped it: questioning follows
            bool debt = sc.Kind == Kind.Debt;
            if (!won)
            {
                TownStandingBehavior.Adjust(sc.Settlement, 2f, "took a beating for a local");
                Say(debt
                    ? "{=lmmi_street_debt_lost}You come to on the cobbles. They took what he owed — and a little extra for their trouble."
                    : "{=lmmi_street_rescue_lost}You come to on the cobbles. At least the girl got away while they were busy with you.");
                sc.Outcome = "you lost";
                Finish(sc);
                return;
            }
            SteppedUp(sc, debt ? 5f : 6f, debt ? "saw off a moneylender's collectors" : "stood up for a woman in the street");
            if (sc.Gang)
            {
                var boss = sc.Settlement.Notables.FirstOrDefault(n => n.IsGangLeader && n.IsAlive);
                if (boss != null)
                {
                    ChangeRelationAction.ApplyPlayerRelation(boss, -3, affectRelatives: false);
                    var msg = new TextObject("{=lmmi_street_rescue_gang}Those were {NAME}'s men. {NAME} won't like this.");
                    msg.SetTextVariable("NAME", boss.Name);
                    InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
                }
            }
            foreach (var a in sc.Actors) Release(a);
            Thank(sc, sc.Victim, debt ? "debt_freed" : "rescued");
        }

        /// <summary>You laid hands on one of the watch.</summary>
        private void OnGuardFightEnd(Scene sc, bool won)
        {
            if (sc.WatchInterrupted) return;
            foreach (var a in sc.Actors) Release(a);
            if (won)
            {
                SteppedUp(sc, 4f, "stood up to a crooked guard");
                if (sc.Settlement.MapFaction != null) ChangeCrimeRatingAction.Apply(sc.Settlement.MapFaction, 5f);
                Say("{=lmmi_street_shakedown_won}The guard picks himself up and stalks off. He won't report it — how would he explain it? — but he'll know your face.",
                        "{=lmmi_street_shakedown_won_2}The guard limps off, cursing under his breath. He'll remember you.",
                        "{=lmmi_street_shakedown_won_3}He scrambles up and hurries away, looking back at you over his shoulder.");
                var guard = sc.Actors.FirstOrDefault(a => a.IsActive());
                if (guard != null) Disperse(sc, new[] { guard });
                Thank(sc, sc.Victim, "shakedown");
                return;
            }
            TownStandingBehavior.Adjust(sc.Settlement, 1f, "stood up to a crooked guard");
            if (Hero.MainHero.Gold >= 100)
            {
                Hero.MainHero.ChangeHeroGold(-100);
                Say("{=lmmi_street_shakedown_lost}The guard stands over you, breathing hard. \"Assaulting the watch. That's a hundred.\" He takes it from your purse himself.");
            }
            else if (sc.Settlement.MapFaction != null)
            {
                ChangeCrimeRatingAction.Apply(sc.Settlement.MapFaction, 15f);
                Say("{=lmmi_street_shakedown_lost_crime}The guard stands over you. \"Assaulting the watch.\" He takes your name — you'll hear more of this.");
            }
            sc.Outcome = "the guard won";
            Finish(sc);
        }

        private void OnAmbushFightEnd(Scene sc, bool won)
        {
            if (sc.WatchInterrupted) return;
            foreach (var a in sc.Actors) Release(a);
            if (won)
            {
                TownStandingBehavior.Adjust(sc.Settlement, 1f, "fought off robbers in an alley");
                Say("{=lmmi_street_lure_won}They stay down. Word will get around: that one's no easy prey.",
                        "{=lmmi_street_lure_won_2}They lie groaning in the dirt. They'll pick easier marks from now on.",
                        "{=lmmi_street_lure_won_3}Nobody gets up. The street will hear about this.");
                if (sc.Gang && sc.Settlement.Notables.FirstOrDefault(n => n.IsGangLeader && n.IsAlive) is Hero boss)
                {
                    ChangeRelationAction.ApplyPlayerRelation(boss, -2, affectRelatives: false);
                    var msg = new TextObject("{=lmmi_street_rescue_gang}Those were {NAME}'s men. {NAME} won't like this.");
                    msg.SetTextVariable("NAME", boss.Name);
                    InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
                }
                sc.Outcome = "fought them off";
            }
            else
            {
                int taken = Math.Min(Hero.MainHero.Gold, sc.Toll * 3 / 2);
                if (taken > 0) Hero.MainHero.ChangeHeroGold(-taken);
                var msg = new TextObject("{=lmmi_street_lure_robbed}You come to with your purse {TAKEN} denars lighter.");
                msg.SetTextVariable("TAKEN", taken);
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
                sc.Outcome = "robbed";
            }
            Finish(sc);
        }

        private void OnBrawlFightEnd(Scene sc, bool won)
        {
            if (sc.WatchInterrupted) return;
            foreach (var a in sc.Actors) Release(a);
            if (won)
            {
                SteppedUp(sc, 3f, "knocked sense into two brawlers");
                Thank(sc, sc.Requester, "brawl_stopped");
            }
            else
            {
                TownStandingBehavior.Adjust(sc.Settlement, 1f, "tried to stop a brawl");
                Say("{=lmmi_street_brawl_beaten}The youths leave you in the dirt and wander off laughing. Well. You tried.",
                        "{=lmmi_street_brawl_beaten_2}The two of them shove you in the mud and walk off arm in arm. Youth.",
                        "{=lmmi_street_brawl_beaten_3}You end up flat on your back while they laugh and go back to their quarrel.");
                sc.Outcome = "you lost";
                Finish(sc);
            }
        }

        /// <summary>They walk off, and are left to themselves a little later.</summary>
        private void Disperse(Scene sc, IEnumerable<Agent> agents)
        {
            var mission = Mission.Current;
            if (mission == null) return;
            foreach (var a in agents.Where(x => x.IsActive()).ToList())
            {
                var to = FarPoint(mission, a, sc.Actors.ToArray());
                if (to == null) continue;
                HandOver(sc, new SlipAway(a, to.Value), a);
            }
        }

        // ---- The thief ----

        private void ThiefEscaped(Scene sc)
        {
            var thief = sc.Actors.FirstOrDefault();
            if (thief != null && thief.IsActive()) { Contour(thief, null); thief.FadeOut(false, true); }
            Say("{=lmmi_street_thief_escaped}The thief ducks into an alley and is gone.",
                        "{=lmmi_street_thief_escaped_2}The boy vanishes into the crowd. Gone.",
                        "{=lmmi_street_thief_escaped_3}A flash of heels round a corner, and the thief is gone.");
            LmmiLog.Info("Street: the thief got away.");
            sc.Actors.Clear();
            sc.TalkTo = sc.Victim;
            if (sc.Victim == null || !sc.Victim.IsActive()) { sc.Outcome = "he got away"; Finish(sc); return; }
            Direct(sc.Victim)?.Follow(Agent.Main, 2f, run: false);
            Go(sc, Step.Consoling);
        }

        // ---- Outcomes ----

        private static void SteppedUp(Scene sc, float standing, string why)
        {
            TownStandingBehavior.MarkSteppedUp(sc.Settlement, standing, why);
            if (!sc.Ambush) Instance?.NoteSoftTouch(sc.Settlement);   // word gets round who stops to help
            var word = sc.Settlement.Culture == Hero.MainHero.Culture
                ? Flavor.Pick("{=lmmi_street_word_kin}People saw that. By nightfall half of {SETTLEMENT} will know one of their own stepped in.",
                        "{=lmmi_street_word_kin_2}Folk saw that. One of their own stepped in — {SETTLEMENT} will be talking about it tonight.",
                        "{=lmmi_street_word_kin_3}Heads turn. By morning, all of {SETTLEMENT} will know you stepped in.")
                : Flavor.Pick("{=lmmi_street_word_foreign}People saw that. By nightfall half of {SETTLEMENT} will know a {DEMONYM} stepped in when their own didn't.",
                        "{=lmmi_street_word_foreign_2}Folk saw that. A {DEMONYM}, stepping in when their own wouldn't — {SETTLEMENT} won't forget it.",
                        "{=lmmi_street_word_foreign_3}People stare. A {DEMONYM}, doing what their own neighbors wouldn't. Word will spread through {SETTLEMENT}.");
            word.SetTextVariable("SETTLEMENT", sc.Settlement.Name);
            word.SetTextVariable("DEMONYM", CultureWords.Demonym(Hero.MainHero.Culture));
            InformationManager.DisplayMessage(new InformationMessage(word.ToString(), Colors.Green));
        }

        private static void Say(string text) =>
            InformationManager.DisplayMessage(new InformationMessage(new TextObject(text).ToString()));

        /// <summary>One of several ways of saying it.</summary>
        private static void Say(params string[] lines) =>
            InformationManager.DisplayMessage(new InformationMessage(Flavor.Pick(lines).ToString()));

        private static bool CanAfford(int gold, out TextObject why)
        {
            why = TextObject.GetEmpty();
            if (Hero.MainHero.Gold >= gold) return true;
            why = new TextObject("{=lmmi_cant_afford_alms}You don't have that much on you.");
            return false;
        }

        // ---- Dialogs ----

        private bool Talk(Kind kind, Step step)
        {
            var sc = _scene;
            if (sc == null || sc.Kind != kind || sc.Step != step || sc.TalkTo == null || sc.Revealed) return false;
            if (ConversationMission.OneToOneConversationAgent != sc.TalkTo) return false;
            if (!sc.Talking) { sc.Talking = true; sc.TalkAt = _sceneTime; sc.Chosen = false; }
            return true;
        }

        /// <summary>The toughs who were waiting for you, whatever the plea was.</summary>
        private bool TalkToughs()
        {
            var sc = _scene;
            if (sc == null || !sc.Revealed || sc.Step != Step.Confronting || sc.TalkTo == null) return false;
            if (ConversationMission.OneToOneConversationAgent != sc.TalkTo) return false;
            if (!sc.Talking) { sc.Talking = true; sc.TalkAt = _sceneTime; sc.Chosen = false; }
            return true;
        }

        /// <summary>Consequence helper: a choice was made; the rest runs once the conversation has fully closed.</summary>
        private void Choose(Action<Scene> then)
        {
            var sc = _scene;
            if (sc == null) return;
            sc.Chosen = true;
            _afterTalk.Add(() => { if (_scene == sc) then(sc); });
        }

        private static void SetWords(TextObject line)
        {
            line.SetTextVariable("DEMONYM", CultureWords.Demonym(Hero.MainHero.Culture));
            line.SetTextVariable("SLUR", CultureWords.Slur(Hero.MainHero.Culture));
        }

        private static bool Foreign(Scene sc) => sc.Settlement.Culture != Hero.MainHero.Culture;

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try
            {
                AddRescueDialogs(starter);
                AddBrawlDialogs(starter);
                AddThiefDialogs(starter);
                AddInjuredDialogs(starter);
                AddDebtDialogs(starter);
                AddLostChildDialogs(starter);
                AddShakedownDialogs(starter);
                AddVillageDialogs(starter);
                AddSuspicionDialogs(starter);
                AddToughsDialogs(starter);
                AddThanksDialogs(starter);
                AddWatchDialogs(starter);
                AddAftercareMenus(starter);

                _talkDownMen.Register(starter);
                _talkDownMen.AddLeave(starter, "{=lmmi_street_confront_leave}...Never mind.", "lmmi_street_confront_left",
                    () => { TownStandingBehavior.Adjust(Settlement.CurrentSettlement, -2f, "backed down from the men harassing a girl");
                            Choose(sc => { sc.Outcome = "you backed down"; LetItPlayOut(sc); }); });
                _talkDownYouths.Register(starter);
                _talkDownYouths.AddLeave(starter, "{=lmmi_street_lads_leave}Carry on, then.", "lmmi_street_lads_carry_on",
                    () => Choose(sc => { sc.Outcome = "you let them carry on"; LetItPlayOut(sc); }));
                _explainToWatch.Register(starter, lostState: "lmmi_street_sentence");
                _explainToWatch.AddLeave(starter, "{=lmmi_street_watch_fine}Fine. Write it down.", "lmmi_street_watch_fine_resp",
                    () => Choose(Fined));
            }
            catch (Exception ex) { LmmiLog.Error("StreetEventsBehavior: failed to register dialogs", ex); }
        }

        /// <summary>How close the guide takes you: right there — or, for a lost child, only as near as she thinks.</summary>
        private static float GuideDistance(Scene sc) => sc.Kind == Kind.LostChild ? LostGuideDistance : 6f;

        private void LeadOn(Scene sc)
        {
            Contour(sc.Requester, GuideContour);   // so you can keep them in sight through the crowd
            Direct(sc.Requester)?.Follow(sc.Spot, GuideDistance(sc), run: true);
            Go(sc, Step.Guiding);
        }

        private void AddRescueDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_street_rescue", "start", "lmmi_street_rescue_resp",
                "{=!}{LMMI_STREET_RESCUE}",
                () => Talk(Kind.Rescue, Step.Asking) && Flavor.Say("LMMI_STREET_RESCUE",
                        "{=lmmi_street_rescue}Please, {?PLAYER.GENDER}madam{?}sir{\\?}! Some men have cornered a girl over there, and nobody's doing a thing! Please — help her!",
                        "{=lmmi_street_rescue_2}{?PLAYER.GENDER}Madam{?}Sir{\\?}, please! There's a girl down there with men all round her — nobody will lift a finger!",
                        "{=lmmi_street_rescue_3}Help, please! They've got a girl backed against a wall, and the watch is nowhere!"), null, 1100);

            starter.AddPlayerLine("lmmi_street_rescue_go", "lmmi_street_rescue_resp", "lmmi_street_lead_on",
                "{=lmmi_street_rescue_go}Show me.", null, () => Choose(LeadOn));
            starter.AddPlayerLine("lmmi_street_rescue_no", "lmmi_street_rescue_resp", "lmmi_street_rescue_refused",
                "{=lmmi_street_rescue_no}Not my problem.", null,
                () =>
                {
                    Shame(-2f, "walked past a woman in trouble");
                    Choose(sc => { sc.Outcome = "you refused"; LetItPlayOut(sc); });
                });
            starter.AddDialogLine("lmmi_street_rescue_refused", "lmmi_street_rescue_refused", "close_window",
                "{=!}{LMMI_STREET_R1}",
                    () => Flavor.Say("LMMI_STREET_R1",
                        "{=lmmi_street_rescue_refused}...Heaven help her, then. Nobody else will.",
                        "{=lmmi_street_rescue_refused_2}...Then she's on her own. Gods help her.",
                        "{=lmmi_street_rescue_refused_3}...Coward."), null);

            starter.AddDialogLine("lmmi_street_lead_on", "lmmi_street_lead_on", "close_window",
                "{=!}{LMMI_STREET_R2}",
                    () => Flavor.Say("LMMI_STREET_R2",
                        "{=lmmi_street_lead_on}This way — hurry!",
                        "{=lmmi_street_lead_on_2}Follow me, quick!",
                        "{=lmmi_street_lead_on_3}Over here — hurry, please!"), null);

            // The men: one of them steps up to you.
            starter.AddDialogLine("lmmi_street_confront", "start", "lmmi_street_confront_resp",
                "{=lmmi_street_confront}{LMMI_STREET_CONFRONT}",
                () =>
                {
                    if (!Talk(Kind.Rescue, Step.Confronting)) return false;
                    var sc = _scene!;
                    TextObject line;
                    if (sc.Gang)
                        line = Foreign(sc)
                            ? Flavor.Pick("{=lmmi_street_confront_gang_foreign}This street's ours, {SLUR}. Walk away while you still can.",
                            "{=lmmi_street_confront_gang_foreign_2}Our street, {SLUR}. Turn around.",
                            "{=lmmi_street_confront_gang_foreign_3}Get lost, {SLUR}, or join her.")
                            : Flavor.Pick("{=lmmi_street_confront_gang}This street's ours. Walk away while you still can, {?PLAYER.GENDER}woman{?}friend{\\?}.",
                            "{=lmmi_street_confront_gang_2}This is our patch. Clear off, {?PLAYER.GENDER}woman{?}friend{\\?}.",
                            "{=lmmi_street_confront_gang_3}You're on our street. Walk away.");
                    else
                        line = Foreign(sc)
                            ? Flavor.Pick("{=lmmi_street_confront_foreign}Keep walking, {SLUR}. This is between us and the girl.",
                            "{=lmmi_street_confront_foreign_2}Move along, {SLUR}. Nothing to see.",
                            "{=lmmi_street_confront_foreign_3}Mind your business, {SLUR}.")
                            : Flavor.Pick("{=lmmi_street_confront_kin}Keep walking, friend. This is none of your business.",
                            "{=lmmi_street_confront_kin_2}Walk away, friend. It's nothing to do with you.",
                            "{=lmmi_street_confront_kin_3}Go on, mind your own business.");
                    SetWords(line);
                    MBTextManager.SetTextVariable("LMMI_STREET_CONFRONT", line);
                    return true;
                }, null, 1100);

            starter.AddPlayerLine("lmmi_street_confront_fight", "lmmi_street_confront_resp", "lmmi_street_confront_wrong",
                "{=lmmi_street_confront_fight}Let her go. Now.", null,
                () => Choose(sc => StartFight(sc, 0.04f, won => OnRescueFightEnd(sc, won))));
            starter.AddDialogLine("lmmi_street_confront_wrong", "lmmi_street_confront_wrong", "close_window",
                "{=!}{LMMI_STREET_R3}",
                    () => Flavor.Say("LMMI_STREET_R3",
                        "{=lmmi_street_confront_wrong}Wrong answer.",
                        "{=lmmi_street_confront_wrong_2}Bad choice.",
                        "{=lmmi_street_confront_wrong_3}Have it your way."), null);

            starter.AddPlayerLine("lmmi_street_confront_talk", "lmmi_street_confront_resp", _talkDownMen.Entry,
                "{=lmmi_street_confront_talk}[Talk them down] Listen to me, all of you...", null,
                () =>
                {
                    var sc = _scene;
                    if (sc == null) return;
                    var listener = sc.TalkTo?.Character as CharacterObject;
                    float r = Resentment(sc), st = TownStandingBehavior.Get(sc.Settlement);
                    int gang = sc.Gang ? -1 : 0;   // the gang's own don't scare easily
                    _talkDownMen.Start(new[]
                        {
                            NativePersuasion.Argument(DefaultSkills.Leadership, DefaultTraits.Valor,
                                new TextObject("{=lmmi_street_confront_scare}Do you know who I am? Walk away while you still can."), listener, r, st, gang + (Clan.PlayerClan?.Tier ?? 0) / 3),
                            NativePersuasion.Argument(DefaultSkills.Roguery, DefaultTraits.Calculating,
                                new TextObject("{=lmmi_street_confront_watch}The watch is two streets over. Scatter now, and nobody hangs for this."), listener, r, st, gang),
                            NativePersuasion.Argument(DefaultSkills.Charm, DefaultTraits.Mercy,
                                new TextObject("{=lmmi_street_confront_mercy}She's somebody's daughter. Go home to yours."), listener, r, st, gang),
                        },
                        Flavor.Pick("{=lmmi_street_confront_opening}Talk fast.",
                            "{=lmmi_street_confront_opening_2}Make it quick.",
                            "{=lmmi_street_confront_opening_3}Well? We're listening."),
                        Flavor.Pick("{=lmmi_street_confront_again}Is that all you've got?",
                            "{=lmmi_street_confront_again_2}That's it?",
                            "{=lmmi_street_confront_again_3}Go on, then."),
                        Flavor.Pick("{=lmmi_street_confront_scared_won}...Tch. She's not worth the trouble. Come on, lads.",
                            "{=lmmi_street_confront_scared_won_2}...Fine. She's not worth it. Let's go.",
                            "{=lmmi_street_confront_scared_won_3}...Tch. Not worth a fight. Come on."),
                        Flavor.Pick("{=lmmi_street_confront_scared_lost}Big words. Let's see you back them up.",
                            "{=lmmi_street_confront_scared_lost_2}Talk's cheap. Let's see what you've got.",
                            "{=lmmi_street_confront_scared_lost_3}Nice speech. Now let's dance."),
                        onWon: () => Choose(s2 =>
                        {
                            SteppedUp(s2, 4f, "talked down the men harassing a girl");
                            Disperse(s2, s2.Actors.ToList());
                            Thank(s2, s2.Victim, "rescued");
                        }),
                        onLost: () => Choose(s2 => StartFight(s2, 0.04f, won => OnRescueFightEnd(s2, won))));
                });

            starter.AddPlayerLine("lmmi_street_confront_pay", "lmmi_street_confront_resp", "lmmi_street_confront_paid",
                "{=lmmi_street_confront_pay}Here's 100{GOLD_ICON}. Go find somewhere else to be.", null,
                () =>
                {
                    Hero.MainHero.ChangeHeroGold(-PayOffGold);
                    Choose(sc =>
                    {
                        SteppedUp(sc, 2f, "paid off the men harassing a girl");
                        Disperse(sc, sc.Actors.ToList());
                        Thank(sc, sc.Victim, "rescued");
                    });
                }, 100, (out TextObject why) => CanAfford(PayOffGold, out why));
            starter.AddDialogLine("lmmi_street_confront_paid", "lmmi_street_confront_paid", "close_window",
                "{=!}{LMMI_STREET_R4}",
                    () => Flavor.Say("LMMI_STREET_R4",
                        "{=lmmi_street_confront_paid}...Easiest coin I ever made. Come on, lads.",
                        "{=lmmi_street_confront_paid_2}...Heh. Generous. Let's go, lads.",
                        "{=lmmi_street_confront_paid_3}...Your coin, your girl. Come on, boys."), null);

            starter.AddPlayerLine("lmmi_street_confront_leave", "lmmi_street_confront_resp", "lmmi_street_confront_left",
                "{=lmmi_street_confront_leave}...Never mind.", null,
                () =>
                {
                    TownStandingBehavior.Adjust(Settlement.CurrentSettlement, -2f, "backed down from the men harassing a girl");
                    Choose(sc => { sc.Outcome = "you backed down"; LetItPlayOut(sc); });
                });
            starter.AddDialogLine("lmmi_street_confront_left", "lmmi_street_confront_left", "close_window",
                "{=!}{LMMI_STREET_R5}",
                    () => Flavor.Say("LMMI_STREET_R5",
                        "{=lmmi_street_confront_left}Thought so.",
                        "{=lmmi_street_confront_left_2}Smart.",
                        "{=lmmi_street_confront_left_3}That's right. Keep walking."), null);
        }

        private void AddBrawlDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_street_brawl", "start", "lmmi_street_brawl_resp",
                "{=!}{LMMI_STREET_BRAWL}",
                () => Talk(Kind.Brawl, Step.Asking) && Flavor.Say("LMMI_STREET_BRAWL",
                        "{=lmmi_street_brawl}Somebody stop them! Two youngsters are beating each other bloody over there, and the watch is nowhere!",
                        "{=lmmi_street_brawl_2}Quick! Two lads are trying to kill each other over there, and everyone's just watching!",
                        "{=lmmi_street_brawl_3}Please, somebody! Two boys are fighting — there's blood everywhere!"), null, 1100);
            starter.AddPlayerLine("lmmi_street_brawl_go", "lmmi_street_brawl_resp", "lmmi_street_lead_on",
                "{=lmmi_street_rescue_go}Show me.", null, () => Choose(LeadOn));
            starter.AddPlayerLine("lmmi_street_brawl_no", "lmmi_street_brawl_resp", "lmmi_street_brawl_shrug",
                "{=lmmi_street_brawl_no}Boys will be boys.", null,
                () =>
                {
                    Shame(-1f, "shrugged at a brawl");
                    Choose(sc => { sc.Outcome = "you shrugged"; LetItPlayOut(sc); });
                });
            starter.AddDialogLine("lmmi_street_brawl_shrug", "lmmi_street_brawl_shrug", "close_window",
                "{=!}{LMMI_STREET_R6}",
                    () => Flavor.Say("LMMI_STREET_R6",
                        "{=lmmi_street_brawl_shrug}...Fine. I'll fetch the guard myself.",
                        "{=lmmi_street_brawl_shrug_2}...I'll find the watch, then.",
                        "{=lmmi_street_brawl_shrug_3}...Somebody's going to die and you don't care. Fine."), null);

            // One of the youths, when you get there.
            starter.AddDialogLine("lmmi_street_lads", "start", "lmmi_street_lads_resp",
                "{=lmmi_street_lads}{LMMI_STREET_LADS}",
                () =>
                {
                    if (!Talk(Kind.Brawl, Step.Confronting)) return false;
                    var line = Foreign(_scene!)
                        ? Flavor.Pick("{=lmmi_street_lads_foreign}What are you looking at, {SLUR}? This is between me and him!",
                            "{=lmmi_street_lads_foreign_2}Get lost, {SLUR}!",
                            "{=lmmi_street_lads_foreign_3}Back off, {SLUR}, this isn't your fight!")
                        : Flavor.Pick("{=lmmi_street_lads_kin}Stay out of it! This is between me and him!",
                            "{=lmmi_street_lads_kin_2}Keep out of it!",
                            "{=lmmi_street_lads_kin_3}This is our fight!");
                    SetWords(line);
                    MBTextManager.SetTextVariable("LMMI_STREET_LADS", line);
                    return true;
                }, null, 1100);

            starter.AddPlayerLine("lmmi_street_lads_talk", "lmmi_street_lads_resp", _talkDownYouths.Entry,
                "{=lmmi_street_lads_talk}[Talk them down] Enough. Both of you, look at me.", null,
                () =>
                {
                    var sc = _scene;
                    if (sc == null) return;
                    var listener = sc.TalkTo?.Character as CharacterObject;
                    float r = Resentment(sc), st = TownStandingBehavior.Get(sc.Settlement);
                    _talkDownYouths.Start(new[]
                        {
                            NativePersuasion.Argument(DefaultSkills.Leadership, DefaultTraits.Valor,
                                new TextObject("{=lmmi_street_lads_valor}Oi! Break it up, the pair of you!"), listener, r, st, 1),
                            NativePersuasion.Argument(DefaultSkills.Charm, DefaultTraits.Mercy,
                                new TextObject("{=lmmi_street_lads_charm}Easy, lads. Whatever it is, it isn't worth a cracked skull."), listener, r, st, 1),
                            NativePersuasion.Argument(DefaultSkills.Roguery, DefaultTraits.Calculating,
                                new TextObject("{=lmmi_street_lads_watch}The watch is coming. Scatter — or spend the night in a cell."), listener, r, st, 1),
                        },
                        Flavor.Pick("{=lmmi_street_lads_opening}...What?",
                            "{=lmmi_street_lads_opening_2}...Yeah?",
                            "{=lmmi_street_lads_opening_3}What do you want?"),
                        Flavor.Pick("{=lmmi_street_lads_again}So?",
                            "{=lmmi_street_lads_again_2}And?",
                            "{=lmmi_street_lads_again_3}Is that it?"),
                        Flavor.Pick("{=lmmi_street_lads_talked_won}...Yeah. Alright. It's not worth it.",
                            "{=lmmi_street_lads_talked_won_2}...Fine. Fine! It's over.",
                            "{=lmmi_street_lads_talked_won_3}...He started it. But — alright."),
                        Flavor.Pick("{=lmmi_street_lads_talked_lost}Piss off. Nobody asked you.",
                            "{=lmmi_street_lads_talked_lost_2}Get lost, this is our business.",
                            "{=lmmi_street_lads_talked_lost_3}Mind your own business!"),
                        onWon: () => Choose(s2 =>
                        {
                            SteppedUp(s2, 3f, "talked down a brawl");
                            Disperse(s2, s2.Actors.ToList());
                            Thank(s2, s2.Requester, "brawl_stopped");
                        }),
                        onLost: () =>
                        {
                            TownStandingBehavior.Adjust(Settlement.CurrentSettlement, 1f, "tried to talk down a brawl");
                            Choose(s2 => { s2.Outcome = "they went back at it"; LetItPlayOut(s2, shrugged: false); });
                        });
                });
            starter.AddPlayerLine("lmmi_street_lads_fists", "lmmi_street_lads_resp", "lmmi_street_lads_tryit",
                "{=lmmi_street_lads_fists}I'll knock your heads together myself.", null,
                () => Choose(sc => StartFight(sc, 0.02f, won => OnBrawlFightEnd(sc, won))));
            starter.AddPlayerLine("lmmi_street_lads_leave", "lmmi_street_lads_resp", "lmmi_street_lads_carry_on",
                "{=lmmi_street_lads_leave}Carry on, then.", null,
                () =>
                {
                    TownStandingBehavior.Adjust(Settlement.CurrentSettlement, -1f, "egged on a brawl");
                    Choose(sc => { sc.Outcome = "you let them carry on"; LetItPlayOut(sc); });
                });

            starter.AddDialogLine("lmmi_street_lads_tryit", "lmmi_street_lads_tryit", "close_window",
                "{=!}{LMMI_STREET_R7}",
                    () => Flavor.Say("LMMI_STREET_R7",
                        "{=lmmi_street_lads_tryit}Try it!",
                        "{=lmmi_street_lads_tryit_2}Go on, then!",
                        "{=lmmi_street_lads_tryit_3}You and whose army?"), null);
            starter.AddDialogLine("lmmi_street_lads_carry_on", "lmmi_street_lads_carry_on", "close_window",
                "{=!}{LMMI_STREET_R8}",
                    () => Flavor.Say("LMMI_STREET_R8",
                        "{=lmmi_street_lads_carry_on}Don't mind if we do.",
                        "{=lmmi_street_lads_carry_on_2}Thought so.",
                        "{=lmmi_street_lads_carry_on_3}Ha! Right, then."), null);
        }

        private void AddThiefDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_street_thief", "start", "lmmi_street_thief_resp",
                "{=!}{LMMI_STREET_THIEF}",
                () => Talk(Kind.Thief, Step.Asking) && Flavor.Say("LMMI_STREET_THIEF",
                        "{=lmmi_street_thief}Thief! That little rat just cut my purse — he's still standing right over there, bold as you like! Please, the rent's in it!",
                        "{=lmmi_street_thief_2}My purse! That boy cut my purse — look, he's right over there, laughing at me! It's all our rent money!",
                        "{=lmmi_street_thief_3}Stop him! He's got my purse — the little thief's still hanging about over there! That was our rent!"), null, 1100);

            starter.AddPlayerLine("lmmi_street_thief_chase", "lmmi_street_thief_resp", "lmmi_street_thief_go",
                "{=lmmi_street_thief_chase}Stop, thief!", null,
                () => Choose(sc =>
                {
                    var thief = sc.Actors.FirstOrDefault();
                    if (thief == null || !thief.IsActive()) { ThiefEscaped(sc); return; }
                    Contour(thief, ThiefContour);
                    Direct(thief)?.GoTo(sc.Escape, run: true, speed: ThiefSpeed);   // quick, but catchable
                    Direct(sc.Victim)?.Hold(face: thief);
                    Bark(thief, Flavor.Pick("{=lmmi_street_thief_bolts}Catch me if you can!",
                        "{=lmmi_street_thief_bolts_2}Too slow!",
                        "{=lmmi_street_thief_bolts_3}You'll never catch me!"));
                    Go(sc, Step.Chasing);
                }));
            starter.AddDialogLine("lmmi_street_thief_go", "lmmi_street_thief_go", "close_window",
                "{=!}{LMMI_STREET_R9}",
                    () => Flavor.Say("LMMI_STREET_R9",
                        "{=lmmi_street_thief_go}Go! Go!",
                        "{=lmmi_street_thief_go_2}Run! Get him!",
                        "{=lmmi_street_thief_go_3}After him — quick!"), null);

            starter.AddPlayerLine("lmmi_street_thief_rogue", "lmmi_street_thief_resp", "lmmi_street_thief_wait",
                "{=lmmi_street_thief_rogue}[Roguery] (Catch the boy's eye) I know that trick, lad. Drop it, or the watch hears your name.",
                () => Hero.MainHero.GetSkillValue(DefaultSkills.Roguery) >= Treachery.RogueryToSpot,
                () => Choose(sc =>
                {
                    Say("{=lmmi_street_thief_dropped}The boy freezes, sets the purse down on a step, and walks off very fast.",
                        "{=lmmi_street_thief_dropped_2}The boy's face goes white. He drops the purse and slinks away.",
                        "{=lmmi_street_thief_dropped_3}The purse hits the cobbles. The boy is already halfway down the alley.");
                    SteppedUp(sc, 3f, "saw through a cutpurse");
                    var thief = sc.Actors.FirstOrDefault();
                    if (thief != null && thief.IsActive()) HandOver(sc, new SlipAway(thief, sc.Escape), thief);
                    Thank(sc, sc.Victim, "purse_back");
                }));
            starter.AddDialogLine("lmmi_street_thief_wait", "lmmi_street_thief_wait", "close_window",
                "{=!}{LMMI_STREET_R10}",
                    () => Flavor.Say("LMMI_STREET_R10",
                        "{=lmmi_street_thief_wait}...He's putting it down. He's putting it down!",
                        "{=lmmi_street_thief_wait_2}He's dropping it! He's dropping it!",
                        "{=lmmi_street_thief_wait_3}Look — he's leaving it on the step!"), null);

            starter.AddPlayerLine("lmmi_street_thief_pay", "lmmi_street_thief_resp", "lmmi_street_purse_blessed",
                "{=lmmi_street_thief_pay}Let him go. Here — this should cover the rent. [50{GOLD_ICON}]", null,
                () =>
                {
                    Hero.MainHero.ChangeHeroGold(-RentGold);
                    Choose(sc =>
                    {
                        SteppedUp(sc, 2f, "covered a stranger's rent");
                        sc.Outcome = "you paid the rent";
                        LetItPlayOut(sc);
                    });
                }, 100, (out TextObject why) => CanAfford(RentGold, out why));
            starter.AddDialogLine("lmmi_street_purse_blessed", "lmmi_street_purse_blessed", "close_window",
                "{=!}{LMMI_STREET_R11}",
                    () => Flavor.Say("LMMI_STREET_R11",
                        "{=lmmi_street_purse_blessed}Bless you! I'll tell everyone — everyone!",
                        "{=lmmi_street_purse_blessed_2}You've saved us! Thank you — thank you!",
                        "{=lmmi_street_purse_blessed_3}The gods bless you, {?PLAYER.GENDER}madam{?}sir{\\?}!"), null);

            starter.AddPlayerLine("lmmi_street_thief_no", "lmmi_street_thief_resp", "lmmi_street_purse_shrug",
                "{=lmmi_street_purse_no}Tough luck.", null,
                () => Choose(sc => { sc.Outcome = "you shrugged"; LetItPlayOut(sc); }));
            starter.AddDialogLine("lmmi_street_purse_shrug", "lmmi_street_purse_shrug", "close_window",
                "{=!}{LMMI_STREET_R12}",
                    () => Flavor.Say("LMMI_STREET_R12",
                        "{=lmmi_street_purse_shrug}...Of course. Why would you care.",
                        "{=lmmi_street_purse_shrug_2}...Right. Thanks for nothing.",
                        "{=lmmi_street_purse_shrug_3}...Of course. Nobody ever cares."), null);

            // Caught him.
            starter.AddDialogLine("lmmi_street_caught", "start", "lmmi_street_caught_resp",
                "{=!}{LMMI_STREET_CAUGHT}",
                () => Talk(Kind.Thief, Step.Caught) && Flavor.Say("LMMI_STREET_CAUGHT",
                        "{=lmmi_street_caught}Alright! Alright! Here — take it, just don't call the guard!",
                        "{=lmmi_street_caught_2}Ow! Let go! Fine — take it, take it!",
                        "{=lmmi_street_caught_3}Don't call the watch! Please — here's the purse!"), null, 1100);
            starter.AddPlayerLine("lmmi_street_caught_return", "lmmi_street_caught_resp", "lmmi_street_caught_off",
                "{=lmmi_street_caught_return}Give it here. Now get lost.", null,
                () => Choose(sc =>
                {
                    SteppedUp(sc, 3f, "caught a cutpurse");
                    var thief = sc.Actors.FirstOrDefault();
                    if (thief != null && thief.IsActive()) HandOver(sc, new SlipAway(thief, sc.Escape), thief);
                    Thank(sc, sc.Victim, "purse_back");
                }));
            starter.AddDialogLine("lmmi_street_caught_off", "lmmi_street_caught_off", "close_window",
                "{=!}{LMMI_STREET_R13}",
                    () => Flavor.Say("LMMI_STREET_R13",
                        "{=lmmi_street_caught_off}...Yes, {?PLAYER.GENDER}madam{?}sir{\\?}.",
                        "{=lmmi_street_caught_off_2}...Going, {?PLAYER.GENDER}madam{?}sir{\\?}.",
                        "{=lmmi_street_caught_off_3}...Yes. Sorry. Going."), null);

            starter.AddPlayerLine("lmmi_street_caught_split", "lmmi_street_caught_resp", "lmmi_street_caught_split_ok",
                "{=lmmi_street_caught_split}[Roguery] Half for me, and I never saw your face.", null,
                () =>
                {
                    Hero.MainHero.ChangeHeroGold(SplitGold);
                    Hero.MainHero.AddSkillXp(DefaultSkills.Roguery, 40f);
                    Choose(sc => { sc.Outcome = "you split the purse with the thief"; LetItPlayOut(sc); });
                });
            starter.AddDialogLine("lmmi_street_caught_split_ok", "lmmi_street_caught_split_ok", "close_window",
                "{=!}{LMMI_STREET_R14}",
                    () => Flavor.Say("LMMI_STREET_R14",
                        "{=lmmi_street_caught_split_ok}...Heh. You're alright. Here.",
                        "{=lmmi_street_caught_split_ok_2}...Heh. Deal. Here's yours.",
                        "{=lmmi_street_caught_split_ok_3}...You're one of us, then. Here."), null);

            starter.AddPlayerLine("lmmi_street_caught_guard", "lmmi_street_caught_resp", "lmmi_street_caught_dragged",
                "{=lmmi_street_caught_guard}Guards! Over here!", null,
                () => Choose(sc =>
                {
                    SteppedUp(sc, 3f, "handed a cutpurse to the watch");
                    var thief = sc.Actors.FirstOrDefault();
                    var mission = Mission.Current;
                    if (thief != null && thief.IsActive() && mission != null)
                    {
                        var arrest = new Arrest(thief, FindGuard(mission, thief.Position, 90f), sc.Escape);
                        HandOver(sc, arrest, thief);
                        arrest.Begin();
                    }
                    Thank(sc, sc.Victim, "purse_back");
                }));
            starter.AddDialogLine("lmmi_street_caught_dragged", "lmmi_street_caught_dragged", "close_window",
                "{=!}{LMMI_STREET_R15}",
                    () => Flavor.Say("LMMI_STREET_R15",
                        "{=lmmi_street_caught_dragged}No! No, please — I'll give it back!",
                        "{=lmmi_street_caught_dragged_2}Let go of me! I'll give it back, I swear!",
                        "{=lmmi_street_caught_dragged_3}No! Not the cells — please!"), null);

            // He got away: she comes back to you.
            starter.AddDialogLine("lmmi_street_gone", "start", "lmmi_street_gone_resp",
                "{=!}{LMMI_STREET_GONE}",
                () => Talk(Kind.Thief, Step.Consoling) && Flavor.Say("LMMI_STREET_GONE",
                        "{=lmmi_street_gone}He's gone... it was everything we had. The rent's due tomorrow.",
                        "{=lmmi_street_gone_2}Gone... and the rent with him. What do I tell the landlord?",
                        "{=lmmi_street_gone_3}He's vanished. That was everything we had."), null, 1100);
            starter.AddPlayerLine("lmmi_street_gone_pay", "lmmi_street_gone_resp", "lmmi_street_purse_blessed",
                "{=lmmi_street_gone_pay}Here. This should cover it. [50{GOLD_ICON}]", null,
                () =>
                {
                    Hero.MainHero.ChangeHeroGold(-RentGold);
                    Choose(sc => { SteppedUp(sc, 2f, "covered a stranger's rent"); Finish(sc); });
                }, 100, (out TextObject why) => CanAfford(RentGold, out why));
            starter.AddPlayerLine("lmmi_street_gone_sorry", "lmmi_street_gone_resp", "lmmi_street_gone_sorry_resp",
                "{=lmmi_street_gone_sorry}I'm sorry. He was too quick.", null,
                () => Choose(Finish));
            starter.AddDialogLine("lmmi_street_gone_sorry_resp", "lmmi_street_gone_sorry_resp", "close_window",
                "{=!}{LMMI_STREET_R16}",
                    () => Flavor.Say("LMMI_STREET_R16",
                        "{=lmmi_street_gone_sorry_resp}...You tried. That's more than most.",
                        "{=lmmi_street_gone_sorry_resp_2}...At least you tried.",
                        "{=lmmi_street_gone_sorry_resp_3}...Thank you anyway. Few would have bothered."), null);
        }

        private void AddWatchDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_street_watch", "start", "lmmi_street_watch_resp",
                "{=lmmi_street_watch}{LMMI_STREET_WATCH}",
                () =>
                {
                    var sc = _scene;
                    if (sc == null || sc.Step != Step.Questioned || sc.TalkTo == null
                        || ConversationMission.OneToOneConversationAgent != sc.TalkTo) return false;
                    if (!sc.Talking) { sc.Talking = true; sc.TalkAt = _sceneTime; sc.Chosen = false; }
                    var line = Foreign(sc) && Resentment(sc) > 0f
                        ? Flavor.Pick("{=lmmi_street_watch_foreign}You. {DEMONYM}. Trouble in our streets — I might have known. Explain yourself.",
                            "{=lmmi_street_watch_foreign_2}Trouble, and a {DEMONYM} in the middle of it. Talk.",
                            "{=lmmi_street_watch_foreign_3}Of course it's a {DEMONYM}. Explain yourself.")
                        : Flavor.Pick("{=lmmi_street_watch_kin}What's all this, then? Explain yourself.",
                            "{=lmmi_street_watch_kin_2}Right. What happened here?",
                            "{=lmmi_street_watch_kin_3}Explain this. Now.");
                    SetWords(line);
                    MBTextManager.SetTextVariable("LMMI_STREET_WATCH", line);
                    return true;
                }, null, 1100);

            starter.AddPlayerLine("lmmi_street_watch_explain", "lmmi_street_watch_resp", _explainToWatch.Entry,
                "{=lmmi_street_watch_explain}Let me explain.", null,
                () =>
                {
                    var sc = _scene;
                    if (sc == null) return;
                    var listener = sc.TalkTo?.Character as CharacterObject;
                    float r = Resentment(sc), st = TownStandingBehavior.Get(sc.Settlement);
                    int witness = Witness(sc) ? 1 : 0;   // the one you helped is standing right there
                    var explanation = new TextObject(sc.Revealed
                        ? "{=lmmi_street_explain_setup}It was a setup. They lured me down here to rob me, and I defended myself."
                        : sc.Kind == Kind.Rescue
                        ? "{=lmmi_street_explain_rescue}They were harassing a woman. I stepped in."
                        : sc.Kind == Kind.Debt
                        ? "{=lmmi_street_explain_debt}Moneylender's men were about to break a man's hands over a debt. I stopped them."
                        : sc.Kind == Kind.Shakedown
                        ? "{=lmmi_street_explain_shakedown}One of yours was robbing a man in the street and calling it a fine. I stopped him."
                        : sc.Kind == Kind.Brawl
                            ? "{=lmmi_street_explain_brawl}Two youngsters were beating each other bloody. I broke it up."
                            : "{=lmmi_street_explain_thief}That boy cut a purse. I was after him.");
                    _explainToWatch.Start(new[]
                        {
                            NativePersuasion.Argument(DefaultSkills.Charm, DefaultTraits.Honor, explanation, listener, r, st, witness),
                            NativePersuasion.Argument(DefaultSkills.Leadership, DefaultTraits.Valor,
                                new TextObject("{=lmmi_street_watch_valor}I'd do it again. Arrest me for it, if that's the law here."), listener, r, st, witness),
                            NativePersuasion.Argument(DefaultSkills.Roguery, DefaultTraits.Calculating,
                                new TextObject("{=lmmi_street_watch_rogue}Nobody got hurt who didn't have it coming. Let's not make paperwork for either of us."), listener, r, st),
                        },
                        Flavor.Pick("{=lmmi_street_watch_opening}Go on, then. I'm listening.",
                            "{=lmmi_street_watch_opening_2}Talk. I'm listening.",
                            "{=lmmi_street_watch_opening_3}Let's hear it."),
                        Flavor.Pick("{=lmmi_street_watch_again}Hm. And?",
                            "{=lmmi_street_watch_again_2}And?",
                            "{=lmmi_street_watch_again_3}Go on."),
                        Flavor.Pick("{=lmmi_street_watch_won}...Right. Move along. And next time, fetch the watch.",
                            "{=lmmi_street_watch_won_2}...Fine. Move on, and keep out of trouble.",
                            "{=lmmi_street_watch_won_3}...Alright. But I'll be watching you."),
                        Flavor.Pick("{=lmmi_street_watch_lost}Save it for the magistrate. That's a hundred denars, or your name in the ledger.",
                            "{=lmmi_street_watch_lost_2}Tell it to the magistrate. A hundred denars, or your name goes in the book.",
                            "{=lmmi_street_watch_lost_3}Not good enough. A hundred, or the ledger."),
                        onWon: () => Choose(Cleared),
                        onLost: () => LmmiLog.Info("Street: the watch didn't buy it — sentencing."));
                });
            starter.AddPlayerLine("lmmi_street_watch_bribe", "lmmi_street_watch_resp", "lmmi_street_watch_bribed",
                "{=lmmi_street_watch_bribe}Here — for your trouble. [50{GOLD_ICON}]", null,
                () =>
                {
                    Hero.MainHero.ChangeHeroGold(-50);
                    Choose(s => { s.Outcome = "bribed the watch"; Finish(s); });
                }, 100, (out TextObject why) => CanAfford(50, out why));
            starter.AddPlayerLine("lmmi_street_watch_fine", "lmmi_street_watch_resp", "lmmi_street_watch_fine_resp",
                "{=lmmi_street_watch_fine}Fine. Write it down.", null,
                () => Choose(Fined));

            // Didn't convince him: pay (double if he had to break up a fight), go quietly, or resist.
            starter.AddDialogLine("lmmi_street_sentence", "lmmi_street_sentence", "lmmi_street_sentence_resp",
                "{=lmmi_street_sentence}{LMMI_STREET_SENTENCE}",
                () =>
                {
                    var sc = _scene;
                    if (sc == null) return false;
                    var line = new TextObject(sc.WatchInterrupted
                        ? "{=lmmi_street_sentence_fight}Brawling in the street, and a story to go with it. That's {FINE} denars — or you can explain it to the magistrate from a cell."
                        : "{=lmmi_street_sentence_plain}That's {FINE} denars — or a night in the cells. Your choice.");
                    line.SetTextVariable("FINE", FineFor(sc));
                    MBTextManager.SetTextVariable("LMMI_STREET_SENTENCE", line);
                    MBTextManager.SetTextVariable("LMMI_STREET_FINE", FineFor(sc));
                    return true;
                }, null);
            starter.AddPlayerLine("lmmi_street_sentence_pay", "lmmi_street_sentence_resp", "lmmi_street_sentence_paid",
                "{=lmmi_street_sentence_pay}Here. [{LMMI_STREET_FINE}{GOLD_ICON}]", null,
                () => Choose(sc => PayFine(sc)), 100,
                (out TextObject why) => CanAfford(_scene != null ? FineFor(_scene) : 100, out why));
            starter.AddPlayerLine("lmmi_street_sentence_jail", "lmmi_street_sentence_resp", "lmmi_street_sentence_quietly",
                "{=lmmi_street_sentence_jail}I'll go quietly.", null,
                () => Choose(GoToJail));
            starter.AddPlayerLine("lmmi_street_sentence_resist", "lmmi_street_sentence_resp", "lmmi_street_sentence_resisted",
                "{=lmmi_street_sentence_resist}You'll have to take me.", null,
                () => Choose(ResistArrest));
            starter.AddDialogLine("lmmi_street_sentence_paid", "lmmi_street_sentence_paid", "close_window",
                "{=!}{LMMI_STREET_R17}",
                    () => Flavor.Say("LMMI_STREET_R17",
                        "{=lmmi_street_sentence_paid}Wise. Now move along.",
                        "{=lmmi_street_sentence_paid_2}Good. On your way.",
                        "{=lmmi_street_sentence_paid_3}That'll do. Off with you, and keep out of trouble."), null);
            starter.AddDialogLine("lmmi_street_sentence_quietly", "lmmi_street_sentence_quietly", "close_window",
                "{=!}{LMMI_STREET_R18}",
                    () => Flavor.Say("LMMI_STREET_R18",
                        "{=lmmi_street_sentence_quietly}Smart. This way — and no sudden moves.",
                        "{=lmmi_street_sentence_quietly_2}Sensible. Come along.",
                        "{=lmmi_street_sentence_quietly_3}Good. Walk ahead of me, slowly."), null);
            starter.AddDialogLine("lmmi_street_sentence_resisted_warn", "lmmi_street_sentence_resisted", "close_window",
                "{=lmmi_street_sentence_resisted_warn}Kill one of my men and you'll hang for it. Guards! To me!",
                () => ExecutionPossible(_scene?.Settlement), null, 110);
            starter.AddDialogLine("lmmi_street_sentence_resisted", "lmmi_street_sentence_resisted", "close_window",
                "{=!}{LMMI_STREET_R19}",
                    () => Flavor.Say("LMMI_STREET_R19",
                        "{=lmmi_street_sentence_resisted}Guards! To me!",
                        "{=lmmi_street_sentence_resisted_2}Guards! Here, now!",
                        "{=lmmi_street_sentence_resisted_3}Help! Guards! Resisting!"), null);

            starter.AddDialogLine("lmmi_street_watch_bribed", "lmmi_street_watch_bribed", "close_window",
                "{=!}{LMMI_STREET_R20}",
                    () => Flavor.Say("LMMI_STREET_R20",
                        "{=lmmi_street_watch_bribed}...I didn't see anything. Neither did you.",
                        "{=lmmi_street_watch_bribed_2}...Well. I suppose I was looking the other way.",
                        "{=lmmi_street_watch_bribed_3}...Nothing happened here. Move along."), null);
            starter.AddDialogLine("lmmi_street_watch_fine_resp", "lmmi_street_watch_fine_resp", "close_window",
                "{=!}{LMMI_STREET_R21}",
                    () => Flavor.Say("LMMI_STREET_R21",
                        "{=lmmi_street_watch_fine_resp}At least you know how this works.",
                        "{=lmmi_street_watch_fine_resp_2}At least you're sensible.",
                        "{=lmmi_street_watch_fine_resp_3}Good. Paperwork it is."), null);
        }

        private void AddInjuredDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_street_injured", "start", "lmmi_street_injured_resp",
                "{=!}{LMMI_STREET_LURE}",
                () => Talk(Kind.Injured, Step.Asking) && Flavor.Say("LMMI_STREET_LURE",
                        "{=lmmi_street_lure}Please, {?PLAYER.GENDER}madam{?}sir{\\?}! My brother fell — down that way — and I can't lift him alone. Please, help me!",
                        "{=lmmi_street_lure_2}Please, help! My brother's hurt — just down there — he can't stand up!",
                        "{=lmmi_street_lure_3}{?PLAYER.GENDER}Madam{?}Sir{\\?}, please! My brother fell and hurt his leg — I can't move him by myself!"), null, 1100);
            starter.AddPlayerLine("lmmi_street_injured_go", "lmmi_street_injured_resp", "lmmi_street_lead_on",
                "{=lmmi_street_rescue_go}Show me.", null, () => Choose(LeadOn));
            starter.AddPlayerLine("lmmi_street_injured_no", "lmmi_street_injured_resp", "lmmi_street_injured_refused",
                "{=lmmi_street_lure_no}Find someone else.", null,
                () =>
                {
                    Shame(-1f, "wouldn't help a man who fell");
                    Choose(sc => { sc.Outcome = "you didn't follow"; LetItPlayOut(sc); });
                });
            starter.AddDialogLine("lmmi_street_injured_refused", "lmmi_street_injured_refused", "close_window",
                "{=!}{LMMI_STREET_R22}",
                    () => Flavor.Say("LMMI_STREET_R22",
                        "{=lmmi_street_lure_refused}...Suit yourself.",
                        "{=lmmi_street_lure_refused_2}...Fine. I'll find someone kinder.",
                        "{=lmmi_street_lure_refused_3}...As you like."), null);

            // There he is, sitting in the street.
            starter.AddDialogLine("lmmi_street_injured_here", "start", "lmmi_street_injured_here_resp",
                "{=!}{LMMI_STREET_INJURED}",
                () => Talk(Kind.Injured, Step.Confronting) && Flavor.Say("LMMI_STREET_INJURED",
                        "{=lmmi_street_injured_here}Here — this is him. He came off a ladder and his leg went under him. Can you help me get him up?",
                        "{=lmmi_street_injured_here_2}This is him. Fell off a cart and twisted his leg badly. Can you help?",
                        "{=lmmi_street_injured_here_3}Here he is. The leg's bent wrong, see? Help me get him up?"), null, 1100);

            starter.AddPlayerLine("lmmi_street_injured_medicine", "lmmi_street_injured_here_resp", "lmmi_street_injured_looked",
                "{=lmmi_street_injured_medicine}[Medicine] Let me look at that leg first.", null,
                () =>
                {
                    var sc = _scene;
                    if (sc == null) return;
                    int medicine = Hero.MainHero.GetSkillValue(DefaultSkills.Medicine);
                    float chance = Math.Min(0.95f, 0.2f + medicine / 125f);
                    float roll = MBRandom.RandomFloat;
                    sc.Mended = roll < chance;
                    LmmiLog.Info($"Street: Medicine {medicine}: {chance:P0}, rolled {roll:0.00} -> {(sc.Mended ? "set the leg" : "no good")}.");
                    if (LmmiSettingsProvider.TestMode)
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"[LMMI Test] Medicine {medicine}: {chance:P0} chance -> {(sc.Mended ? "success" : "failure")}", Colors.Cyan));
                    Choose(s2 =>
                    {
                        if (s2.Mended)
                        {
                            Hero.MainHero.AddSkillXp(DefaultSkills.Medicine, 60f);
                            Say("{=lmmi_street_injured_splint}You splint the leg with a slat and his own belt. He'll walk on it again.",
                        "{=lmmi_street_injured_splint_2}A slat of wood and a strip of cloth, and the leg is set straight.",
                        "{=lmmi_street_injured_splint_3}You set the bone with one sharp pull. He screams — then sighs with relief.");
                            SteppedUp(s2, 3f, "set a stranger's broken leg");
                        }
                        else TownStandingBehavior.Adjust(s2.Settlement, 1f, "helped a man who fell");
                        HelpHome(s2);
                    });
                });
            starter.AddDialogLine("lmmi_street_injured_set", "lmmi_street_injured_looked", "close_window",
                "{=!}{LMMI_STREET_SET}",
                () => _scene?.Mended == true && Flavor.Say("LMMI_STREET_SET",
                        "{=lmmi_street_injured_set}You... you set it, like a proper physician! Bless you — we won't forget this.",
                        "{=lmmi_street_injured_set_2}You've done this before! He'll walk again — thank you!",
                        "{=lmmi_street_injured_set_3}A proper bone-setter! The gods sent you. Thank you!"), null);
            starter.AddDialogLine("lmmi_street_injured_howl", "lmmi_street_injured_looked", "close_window",
                "{=!}{LMMI_STREET_HOWL}",
                () => _scene?.Mended != true && Flavor.Say("LMMI_STREET_HOWL",
                        "{=lmmi_street_injured_howl}He howls the moment you touch it. ...Best leave it to a physician. Thank you for trying — I'll get him home.",
                        "{=lmmi_street_injured_howl_2}He screams when you touch it. Best leave it — I'll find a physician. Thank you anyway.",
                        "{=lmmi_street_injured_howl_3}No, no — stop, it's hurting him. I'll fetch a healer. Thanks for trying."), null);

            starter.AddPlayerLine("lmmi_street_injured_up", "lmmi_street_injured_here_resp", "lmmi_street_injured_up_resp",
                "{=lmmi_street_injured_up}Up you get. Lean on me.", null,
                () => Choose(sc => { SteppedUp(sc, 2f, "helped a man who fell"); HelpHome(sc); }));
            starter.AddDialogLine("lmmi_street_injured_up_resp", "lmmi_street_injured_up_resp", "close_window",
                "{=!}{LMMI_STREET_R23}",
                    () => Flavor.Say("LMMI_STREET_R23",
                        "{=lmmi_street_injured_up_resp}Thank you. There's not many who'd stop.",
                        "{=lmmi_street_injured_up_resp_2}Thanks. Most people just walk past.",
                        "{=lmmi_street_injured_up_resp_3}Bless you. He'll be alright now."), null);

            starter.AddPlayerLine("lmmi_street_injured_leave", "lmmi_street_injured_here_resp", "lmmi_street_injured_leave_resp",
                "{=lmmi_street_injured_leave}He'll live. Fetch a physician.", null,
                () =>
                {
                    TownStandingBehavior.Adjust(Settlement.CurrentSettlement, -1f, "left a man who fell where he lay");
                    Choose(sc => { sc.Outcome = "you left him there"; LetItPlayOut(sc); });
                });
            starter.AddDialogLine("lmmi_street_injured_leave_resp", "lmmi_street_injured_leave_resp", "close_window",
                "{=!}{LMMI_STREET_R24}",
                    () => Flavor.Say("LMMI_STREET_R24",
                        "{=lmmi_street_injured_leave_resp}...Right. Thanks for nothing.",
                        "{=lmmi_street_injured_leave_resp_2}...Thanks for nothing, then.",
                        "{=lmmi_street_injured_leave_resp_3}...Right. A physician. With what money?"), null);
        }

        /// <summary>He's on his feet: the two of them hobble off, grateful.</summary>
        private void HelpHome(Scene sc)
        {
            MarkGrateful(sc.Requester);
            MarkGrateful(sc.Actors.FirstOrDefault());
            sc.Outcome = "helped him up";
            LetItPlayOut(sc);
        }

        /// <summary>
        /// Any plea may be bait, and you can say so — a gamble: right, and whatever was waiting for you waits for someone
        /// else; wrong, and someone who really needed help walks off to find someone who cares.
        /// </summary>
        private void AddSuspicionDialogs(CampaignGameStarter starter)
        {
            foreach (var plea in new[] { "lmmi_street_rescue_resp", "lmmi_street_brawl_resp", "lmmi_street_thief_resp", "lmmi_street_injured_resp",
                                         "lmmi_street_debt_resp", "lmmi_street_lost_resp", "lmmi_street_shakedown_resp",
                                         "lmmi_village_dispute_resp", "lmmi_village_harvest_resp", "lmmi_village_raiders_resp" })
                starter.AddPlayerLine(plea + "_suspect", plea, "lmmi_street_suspect",
                    "{=lmmi_street_suspect}(Stay where you are) Why come to me? There are people closer.", null,
                    () => Choose(sc =>
                    {
                        if (sc.Ambush)
                        {
                            Hero.MainHero.AddSkillXp(DefaultSkills.Roguery, 30f);
                            Say("{=lmmi_street_suspect_right}They melt back into the crowd. Whatever was waiting for you is waiting for someone else now.",
                        "{=lmmi_street_suspect_right_2}They exchange a look and drift off. Whatever was planned won't happen to you.",
                        "{=lmmi_street_suspect_right_3}They back away, muttering. You were right to be wary.");
                            sc.Outcome = "you saw through it";
                            Finish(sc);
                        }
                        else
                        {
                            TownStandingBehavior.Adjust(sc.Settlement, -1f, "treated a plea for help as a trick");
                            sc.Outcome = "you doubted them";
                            LetItPlayOut(sc);
                        }
                    }), 90);
            starter.AddDialogLine("lmmi_street_suspect_caught", "lmmi_street_suspect", "close_window",
                "{=lmmi_street_suspect_caught}...Forget it. Forget I asked.", () => _scene?.Ambush == true, null);
            starter.AddDialogLine("lmmi_street_suspect_wronged", "lmmi_street_suspect", "close_window",
                "{=lmmi_street_suspect_wronged}Because you were here! ...Never mind. I'll find someone who cares.", () => _scene?.Ambush != true, null);
        }

        /// <summary>A setup, sprung: the toughs who were waiting for you. What they say depends on how you were brought.</summary>
        private void AddToughsDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_street_toughs", "start", "lmmi_street_toughs_resp",
                "{=lmmi_street_toughs}{LMMI_STREET_TOUGHS}",
                () =>
                {
                    if (!TalkToughs()) return false;
                    var sc = _scene!;
                    bool foreign = Foreign(sc);
                    TextObject line;
                    switch (sc.Kind)
                    {
                        case Kind.Rescue:
                            line = sc.BeatOnly
                                ? new TextObject("{=lmmi_street_toughs_girl_beat}She's with us, {SLUR}. Nobody here needs rescuing — except you, and nobody's coming.")
                                : foreign
                                    ? new TextObject("{=lmmi_street_toughs_girl_foreign}She's with us, {SLUR}. Works every time on your kind. {TOLL} denars, and you walk out of here with your teeth.")
                                    : new TextObject("{=lmmi_street_toughs_girl}She's with us, friend. Works every time. {TOLL} denars, and you walk out of here with your teeth.");
                            break;
                        case Kind.Brawl:
                            line = sc.BeatOnly
                                ? new TextObject("{=lmmi_street_toughs_brawl_beat}Fell for it, {SLUR}. Now we've got someone worth hitting.")
                                : foreign
                                    ? new TextObject("{=lmmi_street_toughs_brawl_foreign}Fell for it, {SLUR}. We weren't fighting each other — we were waiting for you. {TOLL} denars, and you walk out of here with your teeth.")
                                    : new TextObject("{=lmmi_street_toughs_brawl}Fell for it. We weren't fighting each other, friend — we were waiting for you. {TOLL} denars, and you walk out of here with your teeth.");
                            break;
                        case Kind.Thief:
                            line = sc.BeatOnly
                                ? new TextObject("{=lmmi_street_toughs_thief_beat}Chased him all this way, {SLUR}? Good. We've been waiting.")
                                : foreign
                                    ? new TextObject("{=lmmi_street_toughs_thief_foreign}Chased him all this way, {SLUR}? Good. {TOLL} denars, and you walk out of here with your teeth.")
                                    : new TextObject("{=lmmi_street_toughs_thief}Chased him all this way, friend? Good. {TOLL} denars, and you walk out of here with your teeth.");
                            break;
                        case Kind.Debt:
                            line = sc.BeatOnly
                                ? new TextObject("{=lmmi_street_toughs_debt_beat}Nobody owes anybody, {SLUR}. We just wanted you down here.")
                                : foreign
                                    ? new TextObject("{=lmmi_street_toughs_debt_foreign}Funny thing, {SLUR} — he doesn't owe us a coin. You do. {TOLL} denars, and you walk out of here with your teeth.")
                                    : new TextObject("{=lmmi_street_toughs_debt}Funny thing, friend — he doesn't owe us a coin. You do. {TOLL} denars, and you walk out of here with your teeth.");
                            break;
                        case Kind.Dispute:
                            line = sc.BeatOnly
                                ? new TextObject("{=lmmi_street_toughs_dispute_beat}Nobody's arguing about any stone, {SLUR}. We just wanted you out here.")
                                : foreign
                                    ? new TextObject("{=lmmi_street_toughs_dispute_foreign}Everybody wants to be a judge, {SLUR}. Here's our ruling: {TOLL} denars, and you walk away with your teeth.")
                                    : new TextObject("{=lmmi_street_toughs_dispute}Everybody wants to be a judge. Here's our ruling, friend: {TOLL} denars, and you walk away with your teeth.");
                            break;
                        case Kind.Harvest:
                            line = sc.BeatOnly
                                ? new TextObject("{=lmmi_street_toughs_harvest_beat}Only thing we're harvesting today is you, {SLUR}.")
                                : foreign
                                    ? new TextObject("{=lmmi_street_toughs_harvest_foreign}Only thing we're harvesting today is your purse, {SLUR}. {TOLL} denars, and you walk away with your teeth.")
                                    : new TextObject("{=lmmi_street_toughs_harvest}Only thing we're harvesting today is your purse, friend. {TOLL} denars, and you walk away with your teeth.");
                            break;
                        case Kind.LostChild:
                            line = sc.BeatOnly
                                ? new TextObject("{=lmmi_street_toughs_child_beat}The brat's ours, {SLUR}. So are you.")
                                : foreign
                                    ? new TextObject("{=lmmi_street_toughs_child_foreign}The brat's ours, {SLUR} — and so's your purse. {TOLL} denars, and you walk out of here with your teeth.")
                                    : new TextObject("{=lmmi_street_toughs_child}The brat's ours, friend — and so's your purse. {TOLL} denars, and you walk out of here with your teeth.");
                            break;
                        default:
                            line = sc.BeatOnly
                                ? new TextObject("{=lmmi_street_toughs_beat}No brother, {SLUR}. Just us. We don't like your kind walking our streets.")
                                : foreign
                                    ? new TextObject("{=lmmi_street_toughs_toll_foreign}No brother, {SLUR}. Just us. Your purse — {TOLL} denars — and you walk out of here with your teeth.")
                                    : new TextObject("{=lmmi_street_toughs_toll}No brother, friend. Just us. {TOLL} denars, and you walk out of here with your teeth.");
                            break;
                    }
                    SetWords(line);
                    line.SetTextVariable("TOLL", sc.Toll);
                    MBTextManager.SetTextVariable("LMMI_STREET_TOUGHS", line);
                    MBTextManager.SetTextVariable("LMMI_STREET_TOLL", sc.Toll);
                    return true;
                }, null, 1100);

            starter.AddPlayerLine("lmmi_street_toughs_pay", "lmmi_street_toughs_resp", "lmmi_street_toughs_paid",
                "{=lmmi_street_toughs_pay}Here. Take it. [{LMMI_STREET_TOLL}{GOLD_ICON}]",
                () => _scene?.BeatOnly != true,
                () =>
                {
                    var sc = _scene;
                    if (sc == null) return;
                    Hero.MainHero.ChangeHeroGold(-sc.Toll);
                    Choose(s2 => { Disperse(s2, s2.Actors.ToList()); s2.Outcome = "paid the toll"; Finish(s2); });
                }, 100, (out TextObject why) => CanAfford(_scene?.Toll ?? 50, out why));
            starter.AddDialogLine("lmmi_street_toughs_paid", "lmmi_street_toughs_paid", "close_window",
                "{=!}{LMMI_STREET_R25}",
                    () => Flavor.Say("LMMI_STREET_R25",
                        "{=lmmi_street_toughs_paid}Pleasure doing business. Don't come back this way.",
                        "{=lmmi_street_toughs_paid_2}Nice doing business. Now get lost.",
                        "{=lmmi_street_toughs_paid_3}Good. Don't let us see you round here again."), null);

            starter.AddPlayerLine("lmmi_street_toughs_talk", "lmmi_street_toughs_resp", _talkPastToughs.Entry,
                "{=lmmi_street_toughs_talk}[Talk your way out] Think this through, lads.",
                () => _scene?.BeatOnly != true,
                () =>
                {
                    var sc = _scene;
                    if (sc == null) return;
                    var listener = sc.TalkTo?.Character as CharacterObject;
                    float r = Resentment(sc), st = TownStandingBehavior.Get(sc.Settlement);
                    int gang = sc.Gang ? -1 : 0;
                    _talkPastToughs.Start(new[]
                        {
                            NativePersuasion.Argument(DefaultSkills.Leadership, DefaultTraits.Valor,
                                new TextObject("{=lmmi_street_toughs_valor}You picked the wrong mark. Walk away while you still can."), listener, r, st, gang + (Clan.PlayerClan?.Tier ?? 0) / 3),
                            NativePersuasion.Argument(DefaultSkills.Roguery, DefaultTraits.Calculating,
                                new TextObject("{=lmmi_street_toughs_rogue}Half the street saw me come down here. The watch won't be far behind."), listener, r, st, gang),
                            NativePersuasion.Argument(DefaultSkills.Charm, DefaultTraits.Generosity,
                                new TextObject("{=lmmi_street_toughs_generous}Ten denars for your trouble, and we all go home. That's the best deal you'll get today."), listener, r, st, gang),
                        },
                        Flavor.Pick("{=lmmi_street_toughs_opening}Go on. Make it good.",
                            "{=lmmi_street_toughs_opening_2}Talk fast.",
                            "{=lmmi_street_toughs_opening_3}Go on, then. Amuse us."),
                        Flavor.Pick("{=lmmi_street_toughs_again}Anything else?",
                            "{=lmmi_street_toughs_again_2}Is that all?",
                            "{=lmmi_street_toughs_again_3}And?"),
                        Flavor.Pick("{=lmmi_street_toughs_won}...Not worth it. Go on, get out of here.",
                            "{=lmmi_street_toughs_won_2}...Not worth the trouble. Go on.",
                            "{=lmmi_street_toughs_won_3}...Fine. Off with you, before we change our minds."),
                        Flavor.Pick("{=lmmi_street_toughs_lost}Enough talk.",
                            "{=lmmi_street_toughs_lost_2}We're done talking.",
                            "{=lmmi_street_toughs_lost_3}Talk's over."),
                        onWon: () => Choose(s2 =>
                        {
                            TownStandingBehavior.Adjust(s2.Settlement, 1f, "talked past robbers in an alley");
                            Disperse(s2, s2.Actors.ToList());
                            s2.Outcome = "talked past them";
                            Finish(s2);
                        }),
                        onLost: () => Choose(s2 => StartFight(s2, 0.04f, won => OnAmbushFightEnd(s2, won))));
                });

            starter.AddPlayerLine("lmmi_street_toughs_fight", "lmmi_street_toughs_resp", "lmmi_street_toughs_fighting",
                "{=lmmi_street_toughs_fight}Come and take it.", () => _scene?.BeatOnly != true,
                () => Choose(sc => StartFight(sc, 0.04f, won => OnAmbushFightEnd(sc, won))));
            starter.AddPlayerLine("lmmi_street_toughs_come_on", "lmmi_street_toughs_resp", "lmmi_street_toughs_fighting",
                "{=lmmi_street_toughs_come_on}Come on, then.", () => _scene?.BeatOnly == true,
                () => Choose(sc => StartFight(sc, 0.04f, won => OnAmbushFightEnd(sc, won))));
            starter.AddPlayerLine("lmmi_street_toughs_run", "lmmi_street_toughs_resp", "lmmi_street_toughs_ran",
                "{=lmmi_street_toughs_run}[Back away and run]", null,
                () => Choose(sc => { sc.Outcome = "you ran"; LetItPlayOut(sc); }));
            starter.AddDialogLine("lmmi_street_toughs_fighting", "lmmi_street_toughs_fighting", "close_window",
                "{=!}{LMMI_STREET_R26}",
                    () => Flavor.Say("LMMI_STREET_R26",
                        "{=lmmi_street_toughs_fighting}Get {?PLAYER.GENDER}her{?}him{\\?}!",
                        "{=lmmi_street_toughs_fighting_2}Take {?PLAYER.GENDER}her{?}him{\\?}!",
                        "{=lmmi_street_toughs_fighting_3}Grab {?PLAYER.GENDER}her{?}him{\\?}, lads!"), null);
            starter.AddDialogLine("lmmi_street_toughs_ran", "lmmi_street_toughs_ran", "close_window",
                "{=!}{LMMI_STREET_R27}",
                    () => Flavor.Say("LMMI_STREET_R27",
                        "{=lmmi_street_toughs_ran}Ha! Look at {?PLAYER.GENDER}her{?}him{\\?} go!",
                        "{=lmmi_street_toughs_ran_2}Ha! Run, rabbit, run!",
                        "{=lmmi_street_toughs_ran_3}Look at that! Off like a hare!"), null);

            _talkPastToughs.Register(starter);
            _talkPastToughs.AddLeave(starter, "{=lmmi_street_toughs_pay_after}...Fine. Take it.", "lmmi_street_toughs_paid",
                () =>
                {
                    var sc = _scene;
                    if (sc == null) return;
                    Hero.MainHero.ChangeHeroGold(-Math.Min(Hero.MainHero.Gold, sc.Toll));
                    Choose(s2 => { Disperse(s2, s2.Actors.ToList()); s2.Outcome = "paid the toll"; Finish(s2); });
                });
        }

        /// <summary>The moneylender's men: the gang boss hears who got in the way (or who paid).</summary>
        private static void GangNotice(Scene sc, int delta)
        {
            if (!sc.Gang) return;
            var boss = sc.Settlement.Notables.FirstOrDefault(n => n.IsGangLeader && n.IsAlive);
            if (boss == null) return;
            ChangeRelationAction.ApplyPlayerRelation(boss, delta, affectRelatives: false);
            if (delta >= 0) return;
            var msg = new TextObject("{=lmmi_street_rescue_gang}Those were {NAME}'s men. {NAME} won't like this.");
            msg.SetTextVariable("NAME", boss.Name);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
        }

        private void AddDebtDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_street_debt", "start", "lmmi_street_debt_resp",
                "{=!}{LMMI_STREET_DEBT}",
                () => Talk(Kind.Debt, Step.Asking) && Flavor.Say("LMMI_STREET_DEBT",
                        "{=lmmi_street_debt}Please, {?PLAYER.GENDER}madam{?}sir{\\?}! The moneylender's men have my neighbour cornered over a debt — they say they'll break his hands! Please!",
                        "{=lmmi_street_debt_2}Help! The moneylender's thugs have my neighbour against a wall — they'll break his hands!",
                        "{=lmmi_street_debt_3}{?PLAYER.GENDER}Madam{?}Sir{\\?}, please! Collectors have cornered a man over a debt. They're going to break his fingers!"), null, 1100);
            starter.AddPlayerLine("lmmi_street_debt_go", "lmmi_street_debt_resp", "lmmi_street_lead_on",
                "{=lmmi_street_rescue_go}Show me.", null, () => Choose(LeadOn));
            starter.AddPlayerLine("lmmi_street_debt_no", "lmmi_street_debt_resp", "lmmi_street_debt_refused",
                "{=lmmi_street_debt_no}A debt's a debt.", null,
                () =>
                {
                    Shame(-1f, "shrugged at a man about to lose his hands");
                    Choose(sc => { sc.Outcome = "you refused"; LetItPlayOut(sc); });
                });
            starter.AddDialogLine("lmmi_street_debt_refused", "lmmi_street_debt_refused", "close_window",
                "{=!}{LMMI_STREET_R28}",
                    () => Flavor.Say("LMMI_STREET_R28",
                        "{=lmmi_street_debt_refused}...Easy to say, with a full purse.",
                        "{=lmmi_street_debt_refused_2}...Easy for you to say.",
                        "{=lmmi_street_debt_refused_3}...Then they'll break his hands, and that's on you."), null);

            // The collectors.
            starter.AddDialogLine("lmmi_street_collect", "start", "lmmi_street_collect_resp",
                "{=lmmi_street_collect}{LMMI_STREET_COLLECT}",
                () =>
                {
                    if (!Talk(Kind.Debt, Step.Confronting)) return false;
                    var sc = _scene!;
                    var line = Foreign(sc)
                        ? Flavor.Pick("{=lmmi_street_collect_foreign}Walk on, {SLUR}. He owes {DEBT} denars, and he pays — one way or another.",
                            "{=lmmi_street_collect_foreign_2}Move on, {SLUR}. He owes {DEBT} denars, and he'll pay.",
                            "{=lmmi_street_collect_foreign_3}Not your business, {SLUR}. {DEBT} denars — he pays one way or another.")
                        : Flavor.Pick("{=lmmi_street_collect_kin}Walk on, friend. He owes {DEBT} denars, and he pays — one way or another.",
                            "{=lmmi_street_collect_kin_2}Keep walking. He owes {DEBT} denars.",
                            "{=lmmi_street_collect_kin_3}Not your concern, friend. {DEBT} denars, and he pays.");
                    SetWords(line);
                    line.SetTextVariable("DEBT", sc.Debt);
                    MBTextManager.SetTextVariable("LMMI_STREET_COLLECT", line);
                    MBTextManager.SetTextVariable("LMMI_STREET_DEBT", sc.Debt);
                    return true;
                }, null, 1100);

            starter.AddPlayerLine("lmmi_street_collect_pay", "lmmi_street_collect_resp", "lmmi_street_collect_paid",
                "{=lmmi_street_collect_pay}Then I'll pay it. Here. [{LMMI_STREET_DEBT}{GOLD_ICON}]", null,
                () =>
                {
                    var sc = _scene;
                    if (sc == null) return;
                    Hero.MainHero.ChangeHeroGold(-sc.Debt);
                    Choose(s2 =>
                    {
                        SteppedUp(s2, 3f, "paid a stranger's debt");
                        GangNotice(s2, 1);
                        Disperse(s2, s2.Actors.ToList());
                        Thank(s2, s2.Victim, "debt_paid");   // you paid: he owes you now (fought or talked down: "debt_freed")
                    });
                }, 100, (out TextObject why) => CanAfford(_scene?.Debt ?? 100, out why));
            starter.AddDialogLine("lmmi_street_collect_paid", "lmmi_street_collect_paid", "close_window",
                "{=!}{LMMI_STREET_R29}",
                    () => Flavor.Say("LMMI_STREET_R29",
                        "{=lmmi_street_collect_paid}...Paid in full. Your lucky day, old man.",
                        "{=lmmi_street_collect_paid_2}...Debt's clear. You've a rich friend, old man.",
                        "{=lmmi_street_collect_paid_3}...Well, well. Somebody likes you, old man."), null);

            starter.AddPlayerLine("lmmi_street_collect_talk", "lmmi_street_collect_resp", _talkDownCollectors.Entry,
                "{=lmmi_street_collect_talk}[Talk them down] Think about this for a moment.", null,
                () =>
                {
                    var sc = _scene;
                    if (sc == null) return;
                    var listener = sc.TalkTo?.Character as CharacterObject;
                    float r = Resentment(sc), st = TownStandingBehavior.Get(sc.Settlement);
                    int gang = sc.Gang ? -1 : 0;
                    _talkDownCollectors.Start(new[]
                        {
                            NativePersuasion.Argument(DefaultSkills.Trade, DefaultTraits.Calculating,
                                new TextObject("{=lmmi_street_collect_calc}Break his hands and he'll never earn it back. Where's your money then?"), listener, r, st, gang),
                            NativePersuasion.Argument(DefaultSkills.Charm, DefaultTraits.Mercy,
                                new TextObject("{=lmmi_street_collect_mercy}Give him a week. He's good for it — look at him."), listener, r, st, gang),
                            NativePersuasion.Argument(DefaultSkills.Leadership, DefaultTraits.Valor,
                                new TextObject("{=lmmi_street_collect_valor}Touch him and you'll answer to me."), listener, r, st, gang + (Clan.PlayerClan?.Tier ?? 0) / 3),
                        },
                        Flavor.Pick("{=lmmi_street_collect_opening}You've got until I lose my patience.",
                            "{=lmmi_street_collect_opening_2}Speak quick.",
                            "{=lmmi_street_collect_opening_3}Say your piece."),
                        Flavor.Pick("{=lmmi_street_collect_again}And?",
                            "{=lmmi_street_collect_again_2}Anything else?",
                            "{=lmmi_street_collect_again_3}Make it quick."),
                        Flavor.Pick("{=lmmi_street_collect_won}...A week. One week, and not a day more.",
                            "{=lmmi_street_collect_won_2}...Seven days. Then we come back.",
                            "{=lmmi_street_collect_won_3}...Alright. One week."),
                        Flavor.Pick("{=lmmi_street_collect_lost}Enough talk. Hold his arm.",
                            "{=lmmi_street_collect_lost_2}No more talk. Grab his hand.",
                            "{=lmmi_street_collect_lost_3}Enough. Do it."),
                        onWon: () => Choose(s2 =>
                        {
                            SteppedUp(s2, 3f, "talked down a moneylender's collectors");
                            GangNotice(s2, -1);
                            Disperse(s2, s2.Actors.ToList());
                            Thank(s2, s2.Victim, "debt_freed");
                        }),
                        onLost: () => Choose(s2 => StartFight(s2, 0.04f, won => OnRescueFightEnd(s2, won))));
                });

            starter.AddPlayerLine("lmmi_street_collect_fight", "lmmi_street_collect_resp", "lmmi_street_confront_wrong",
                "{=lmmi_street_collect_fight}Let go of him.", null,
                () => Choose(sc => StartFight(sc, 0.04f, won => OnRescueFightEnd(sc, won))));

            starter.AddPlayerLine("lmmi_street_collect_leave", "lmmi_street_collect_resp", "lmmi_street_collect_left",
                "{=lmmi_street_collect_leave}...Carry on.", null, CollectorsLeft);
            starter.AddDialogLine("lmmi_street_collect_left", "lmmi_street_collect_left", "close_window",
                "{=!}{LMMI_STREET_COLLECT_LEFT}",
                    () => Flavor.Say("LMMI_STREET_COLLECT_LEFT",
                        "{=lmmi_street_collect_left}Wise.",
                        "{=lmmi_street_collect_left_2}Smart.",
                        "{=lmmi_street_collect_left_3}Good. Run along."), null);

            _talkDownCollectors.Register(starter);
            _talkDownCollectors.AddLeave(starter, "{=lmmi_street_collect_leave}...Carry on.", "lmmi_street_collect_left", CollectorsLeft);
        }

        private void CollectorsLeft()
        {
            TownStandingBehavior.Adjust(Settlement.CurrentSettlement, -2f, "walked away from a man about to lose his hands");
            Choose(sc => { sc.Outcome = "you walked away"; LetItPlayOut(sc); });
        }

        private void AddLostChildDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_street_lost", "start", "lmmi_street_lost_resp",
                "{=!}{LMMI_STREET_LOST}",
                () => Talk(Kind.LostChild, Step.Asking) && Flavor.Say("LMMI_STREET_LOST",
                        "{=lmmi_street_lost}Please — have you seen my little one? I turned my back for a moment and he was gone! Please, help me look!",
                        "{=lmmi_street_lost_2}Have you seen a little boy? Please! He was right beside me, and now he's gone!",
                        "{=lmmi_street_lost_3}My son — I can't find my son! Please, help me look for him!"), null, 1100);
            starter.AddPlayerLine("lmmi_street_lost_go", "lmmi_street_lost_resp", "lmmi_street_lost_lead",
                "{=lmmi_street_lost_go}I'll help you look.", null, () => Choose(LeadOn));
            starter.AddDialogLine("lmmi_street_lost_lead", "lmmi_street_lost_lead", "close_window",
                "{=!}{LMMI_STREET_LOST_LEAD}",
                    () => Flavor.Say("LMMI_STREET_LOST_LEAD",
                        "{=lmmi_street_lost_lead}He went this way, I think — come!",
                        "{=lmmi_street_lost_lead_2}This way — I think he went this way!",
                        "{=lmmi_street_lost_lead_3}Come, please — I last saw him over here!"), null);
            starter.AddPlayerLine("lmmi_street_lost_no", "lmmi_street_lost_resp", "lmmi_street_lost_refused",
                "{=lmmi_street_lost_no}He'll turn up.", null,
                () =>
                {
                    Shame(-1f, "wouldn't help a mother look for her child");
                    Choose(sc => { sc.Outcome = "you refused"; LetItPlayOut(sc); });
                });
            starter.AddDialogLine("lmmi_street_lost_refused", "lmmi_street_lost_refused", "close_window",
                "{=!}{LMMI_STREET_LOST_NO}",
                    () => Flavor.Say("LMMI_STREET_LOST_NO",
                        "{=lmmi_street_lost_refused}...Heaven forgive you.",
                        "{=lmmi_street_lost_refused_2}...How can you say that? He's just a child!",
                        "{=lmmi_street_lost_refused_3}...Then I'll look alone. Gods help me."), null);

            // Found him: small, frightened, and crying.
            starter.AddDialogLine("lmmi_street_lost_child", "start", "lmmi_street_lost_child_resp",
                "{=lmmi_street_lost_child}{LMMI_STREET_CHILD}",
                () =>
                {
                    if (!Talk(Kind.LostChild, Step.Comforting)) return false;
                    var sc = _scene!;
                    var line = Foreign(sc) && Resentment(sc) > 0f
                        ? Flavor.Pick("{=lmmi_street_lost_child_foreign}*sniff* M-mama says not to talk to {DEMONYM}s... but I can't find her... I want my mama...",
                                    "{=lmmi_street_lost_child_foreign_2}*sniff* Y-you talk funny... I want my mama...",
                                    "{=lmmi_street_lost_child_foreign_3}*sniff* Are you a {DEMONYM}? Mama says... mama... I want my mama...")
                        : Flavor.Pick("{=lmmi_street_lost_child_kin}*sniff* I c-can't find my mama... I was only looking at the horses, and then she was gone...",
                                    "{=lmmi_street_lost_child_kin_2}*sniff* I... I can't find my mama anywhere...",
                                    "{=lmmi_street_lost_child_kin_3}*sniff* I was following the cart and then... and then she was gone...");
                    SetWords(line);
                    MBTextManager.SetTextVariable("LMMI_STREET_CHILD", line);
                    return true;
                }, null, 1100);
            starter.AddPlayerLine("lmmi_street_lost_child_come", "lmmi_street_lost_child_resp", "lmmi_street_lost_child_ok",
                "{=lmmi_street_lost_child_come}Hush, now. Your mother's looking for you — come on, I'll take you to her.", null,
                () => Choose(sc =>
                {
                    var child = sc.Actors.FirstOrDefault();
                    if (child == null || !child.IsActive()) { Finish(sc); return; }
                    Direct(child)?.Follow(Agent.Main, 1.4f, run: false);
                    Direct(sc.Requester)?.Follow(Agent.Main, 2.5f, run: true);
                    Contour(sc.Requester, GuideContour);
                    sc.TalkTo = null;
                    Go(sc, Step.Returning);
                }));
            starter.AddDialogLine("lmmi_street_lost_child_ok", "lmmi_street_lost_child_ok", "close_window",
                "{=!}{LMMI_STREET_CHILD_OK}",
                    () => Flavor.Say("LMMI_STREET_CHILD_OK",
                        "{=lmmi_street_lost_child_ok}*sniff* ...Promise?",
                        "{=lmmi_street_lost_child_ok_2}*sniff* ...Really?",
                        "{=lmmi_street_lost_child_ok_3}*sniff* ...You'll take me to Mama?"), null);
            starter.AddPlayerLine("lmmi_street_lost_child_leave", "lmmi_street_lost_child_resp", "lmmi_street_lost_child_left",
                "{=lmmi_street_lost_child_leave}(Leave him be.)", null,
                () =>
                {
                    TownStandingBehavior.Adjust(Settlement.CurrentSettlement, -1f, "left a lost child crying in the street");
                    Choose(sc => { sc.Outcome = "you left him"; LetItPlayOut(sc); });
                });
            starter.AddDialogLine("lmmi_street_lost_child_left", "lmmi_street_lost_child_left", "close_window",
                "{=!}{LMMI_STREET_CHILD_LEFT}",
                    () => Flavor.Say("LMMI_STREET_CHILD_LEFT",
                        "{=lmmi_street_lost_child_left}*sniff*...",
                        "{=lmmi_street_lost_child_left_2}*sob*...",
                        "{=lmmi_street_lost_child_left_3}...Mama?"), null);
        }

        /// <summary>The mother takes her little one home, and he trots along beside her.</summary>
        private void GoHomeTogether(Scene sc)
        {
            var child = sc.Actors.FirstOrDefault();
            if (Mission.Current != null && child != null && child.IsActive() && sc.Requester.IsActive())
            {
                HandOver(sc, new Reunite(sc.Requester, child, together: true), child);
                sc.RequesterGone = true;
            }
            Finish(sc);
        }

        private void AddVillageDialogs(CampaignGameStarter starter)
        {
            // ---- The boundary stone ----
            starter.AddDialogLine("lmmi_village_dispute", "start", "lmmi_village_dispute_resp",
                "{=!}{LMMI_VILLAGE_DISPUTE}",
                () => Talk(Kind.Dispute, Step.Asking) && Flavor.Say("LMMI_VILLAGE_DISPUTE",
                        "{=lmmi_village_dispute}{?PLAYER.GENDER}My lady{?}Sir{\\?}, please — two of our neighbours are at each other's throats over a boundary stone, and the headman's no use. They'll listen to someone like you. Before blood's spilled!",
                        "{=lmmi_village_dispute_2}{?PLAYER.GENDER}My lady{?}Sir{\\?}, please! Two of our neighbours are about to kill each other over a boundary stone. They'll listen to you!",
                        "{=lmmi_village_dispute_3}Please, come quick — there's a fight brewing over a field boundary, and the headman's useless. Before somebody gets hurt!"), null, 1100);
            starter.AddPlayerLine("lmmi_village_dispute_go", "lmmi_village_dispute_resp", "lmmi_street_lead_on",
                "{=lmmi_street_rescue_go}Show me.", null, () => Choose(LeadOn));
            starter.AddPlayerLine("lmmi_village_dispute_no", "lmmi_village_dispute_resp", "lmmi_village_dispute_refused",
                "{=lmmi_village_dispute_no}Not my village, not my stone.", null,
                () =>
                {
                    Shame(-1f, "wouldn't settle a quarrel between neighbours");
                    Choose(sc => { sc.Outcome = "you refused"; LetItPlayOut(sc); });
                });
            starter.AddDialogLine("lmmi_village_dispute_refused", "lmmi_village_dispute_refused", "close_window",
                "{=!}{LMMI_VILLAGE_DISPUTE_NO}",
                    () => Flavor.Say("LMMI_VILLAGE_DISPUTE_NO",
                        "{=lmmi_village_dispute_refused}...Then they'll settle it with their fists.",
                        "{=lmmi_village_dispute_refused_2}...Then there'll be blood before supper.",
                        "{=lmmi_village_dispute_refused_3}...They'll go at each other with pitchforks, then."), null);

            starter.AddDialogLine("lmmi_village_dispute_claim", "start", "lmmi_village_dispute_claim_resp",
                "{=lmmi_village_dispute_claim}{LMMI_DISPUTE}",
                () =>
                {
                    if (!Talk(Kind.Dispute, Step.Confronting)) return false;
                    var sc = _scene!;
                    var line = Foreign(sc) && Resentment(sc) > 0f
                        ? Flavor.Pick("{=lmmi_village_dispute_claim_foreign}Even a {DEMONYM} can see it — he moved the boundary stone ten paces into my barley in the night! Tell him!",
                                    "{=lmmi_village_dispute_claim_foreign_2}You're a stranger, but you're not blind — he moved that stone ten paces into my land!",
                                    "{=lmmi_village_dispute_claim_foreign_3}Even a {DEMONYM} can see it! The stone's moved — tell him!")
                        : Flavor.Pick("{=lmmi_village_dispute_claim_kin}He moved the boundary stone in the night — ten paces into my barley! You've eyes, {?PLAYER.GENDER}my lady{?}sir{\\?} — tell him!",
                                    "{=lmmi_village_dispute_claim_kin_2}He shifted the stone last night — ten paces of my barley, gone! Tell him, {?PLAYER.GENDER}my lady{?}sir{\\?}!",
                                    "{=lmmi_village_dispute_claim_kin_3}Look at it! That stone's moved, and he moved it! Tell him he's a thief!");
                    SetWords(line);
                    MBTextManager.SetTextVariable("LMMI_DISPUTE", line);
                    return true;
                }, null, 1100);

            starter.AddPlayerLine("lmmi_village_dispute_honor", "lmmi_village_dispute_claim_resp", "lmmi_village_dispute_honor_resp",
                "{=lmmi_village_dispute_honor}The stone goes back where it stood. Old boundaries are the law here.", null,
                () => Choose(sc =>
                {
                    SteppedUp(sc, 2f, "settled a boundary dispute by the old law");
                    Headman(sc, 1);
                    if (sc.Actors.Count > 1) Bark(sc.Actors[1], Flavor.Pick("{=lmmi_village_dispute_sulk}...Fine. FINE. Back it goes.",
                                    "{=lmmi_village_dispute_sulk_2}...Alright. Back it goes. For now.",
                                    "{=lmmi_village_dispute_sulk_3}...Fine. Have your stone."));
                    Thank(sc, sc.Requester, "judged");
                }));
            starter.AddDialogLine("lmmi_village_dispute_honor_resp", "lmmi_village_dispute_honor_resp", "close_window",
                "{=!}{LMMI_VILLAGE_HONOR}",
                    () => Flavor.Say("LMMI_VILLAGE_HONOR",
                        "{=lmmi_village_dispute_honor_resp}Ha! You hear that? Back it goes!",
                        "{=lmmi_village_dispute_honor_resp_2}Ha! Justice! Back it goes!",
                        "{=lmmi_village_dispute_honor_resp_3}You heard! The stone goes back where it stood!"), null);

            starter.AddPlayerLine("lmmi_village_dispute_split", "lmmi_village_dispute_claim_resp", "lmmi_village_dispute_split_resp",
                "{=lmmi_village_dispute_split}[Charm] Split the strip between you, and shake on it — then you both still have a neighbour.", null,
                () =>
                {
                    var sc = _scene;
                    if (sc == null) return;
                    int charm = Hero.MainHero.GetSkillValue(DefaultSkills.Charm);
                    float chance = Math.Min(0.9f, 0.3f + charm / 150f);
                    float roll = MBRandom.RandomFloat;
                    sc.Mended = roll < chance;
                    LmmiLog.Info($"Street: Charm {charm}: {chance:P0}, rolled {roll:0.00} -> {(sc.Mended ? "they shake on it" : "no")}.");
                    if (LmmiSettingsProvider.TestMode)
                        InformationManager.DisplayMessage(new InformationMessage($"[LMMI Test] Charm {charm}: {chance:P0} chance -> {(sc.Mended ? "success" : "failure")}", Colors.Cyan));
                    Choose(s2 =>
                    {
                        if (s2.Mended)
                        {
                            SteppedUp(s2, 3f, "made two neighbours shake hands");
                            Headman(s2, 1);
                            Thank(s2, s2.Requester, "judged");
                        }
                        else { s2.Outcome = "they wouldn't split it"; LetItPlayOut(s2, shrugged: false); }
                    });
                });
            starter.AddDialogLine("lmmi_village_dispute_split_ok", "lmmi_village_dispute_split_resp", "close_window",
                "{=!}{LMMI_VILLAGE_SPLIT_OK}", () => _scene?.Mended == true && Flavor.Say("LMMI_VILLAGE_SPLIT_OK",
                        "{=lmmi_village_dispute_split_ok}...Half. Half, and he buys the first round. ...Alright. Alright.",
                        "{=lmmi_village_dispute_split_ok_2}...Fine. Half each. But I'm keeping the pear tree.",
                        "{=lmmi_village_dispute_split_ok_3}...Alright. Half. And he can buy me a drink for my trouble."), null);
            starter.AddDialogLine("lmmi_village_dispute_split_no", "lmmi_village_dispute_split_resp", "close_window",
                "{=!}{LMMI_VILLAGE_SPLIT_NO}", () => _scene?.Mended != true && Flavor.Say("LMMI_VILLAGE_SPLIT_NO",
                        "{=lmmi_village_dispute_split_no}Split it? Split MY land with that thief? Never!",
                        "{=lmmi_village_dispute_split_no_2}Half? HALF? It's all mine, every clod of it!",
                        "{=lmmi_village_dispute_split_no_3}Never! I'd rather see it burn than share it with him!"), null);

            starter.AddPlayerLine("lmmi_village_dispute_calc", "lmmi_village_dispute_claim_resp", "lmmi_village_dispute_calc_resp",
                "{=lmmi_village_dispute_calc}Whoever sows that strip this year gives the other a tenth of the crop. Everyone eats.", null,
                () => Choose(sc =>
                {
                    SteppedUp(sc, 2f, "found a sensible end to a boundary dispute");
                    Headman(sc, 2);
                    Thank(sc, sc.Requester, "judged");
                }));
            starter.AddDialogLine("lmmi_village_dispute_calc_resp", "lmmi_village_dispute_calc_resp", "close_window",
                "{=!}{LMMI_VILLAGE_CALC}",
                    () => Flavor.Say("LMMI_VILLAGE_CALC",
                        "{=lmmi_village_dispute_calc_resp}...A tenth. Hm. I can live with a tenth.",
                        "{=lmmi_village_dispute_calc_resp_2}...A tenth. Fair enough, I suppose.",
                        "{=lmmi_village_dispute_calc_resp_3}...Hm. A tenth's better than a broken head."), null);

            starter.AddPlayerLine("lmmi_village_dispute_bribe", "lmmi_village_dispute_claim_resp", "lmmi_village_dispute_bribe_resp",
                "{=lmmi_village_dispute_bribe}[Roguery] Make it worth my while, and I'll say whose stone it is.", null,
                () => Choose(sc =>
                {
                    Hero.MainHero.ChangeHeroGold(40);
                    Hero.MainHero.AddSkillXp(DefaultSkills.Roguery, 30f);
                    TownStandingBehavior.Adjust(sc.Settlement, -2f, "sold a judgment");
                    if (sc.Actors.Count > 1) Bark(sc.Actors[1], Flavor.Pick("{=lmmi_village_dispute_bought}Bought and paid for! I knew it!",
                                    "{=lmmi_village_dispute_bought_2}Bought! He bought you!",
                                    "{=lmmi_village_dispute_bought_3}Bribed! I knew it!"));
                    sc.Outcome = "sold the judgment";
                    Finish(sc);
                }));
            starter.AddDialogLine("lmmi_village_dispute_bribe_resp", "lmmi_village_dispute_bribe_resp", "close_window",
                "{=!}{LMMI_VILLAGE_BRIBE}",
                    () => Flavor.Say("LMMI_VILLAGE_BRIBE",
                        "{=lmmi_village_dispute_bribe_resp}...Here. Forty denars. Now say it.",
                        "{=lmmi_village_dispute_bribe_resp_2}...Forty denars. Now say it's my stone.",
                        "{=lmmi_village_dispute_bribe_resp_3}...Here. Make it a good judgment."), null);

            starter.AddPlayerLine("lmmi_village_dispute_leave", "lmmi_village_dispute_claim_resp", "lmmi_village_dispute_left",
                "{=lmmi_village_dispute_leave}Settle it yourselves.", null,
                () =>
                {
                    TownStandingBehavior.Adjust(Settlement.CurrentSettlement, -1f, "walked away from a quarrel between neighbours");
                    Choose(sc => { sc.Outcome = "you walked away"; LetItPlayOut(sc); });
                });
            starter.AddDialogLine("lmmi_village_dispute_left", "lmmi_village_dispute_left", "close_window",
                "{=!}{LMMI_VILLAGE_LEFT}",
                    () => Flavor.Say("LMMI_VILLAGE_LEFT",
                        "{=lmmi_village_dispute_left}Fine! Then we will!",
                        "{=lmmi_village_dispute_left_2}Fine! We'll settle it ourselves!",
                        "{=lmmi_village_dispute_left_3}Then don't blame us when it comes to blows!"), null);

            // ---- The harvest ----
            starter.AddDialogLine("lmmi_village_harvest", "start", "lmmi_village_harvest_resp",
                "{=lmmi_village_harvest}{LMMI_HARVEST}",
                () =>
                {
                    if (!Talk(Kind.Harvest, Step.Asking)) return false;
                    var sc = _scene!;
                    var line = Flavor.Pick("{=lmmi_village_harvest_ask}Storm's coming over the hills and half the {CROP} is still in the field. We could use every pair of hands — even yours, {?PLAYER.GENDER}madam{?}sir{\\?}!",
                        "{=lmmi_village_harvest_ask_2}The sky's black over the hills and the {CROP} is still standing. Every hand helps — yours too, {?PLAYER.GENDER}madam{?}sir{\\?}!",
                        "{=lmmi_village_harvest_ask_3}Rain's coming, and half the {CROP} is still in the field. Could you lend a hand?");
                    line.SetTextVariable("CROP", VillageCrop(sc.Settlement)?.Name ?? TextObject.GetEmpty());
                    MBTextManager.SetTextVariable("LMMI_HARVEST", line);
                    return true;
                }, null, 1100);
            starter.AddPlayerLine("lmmi_village_harvest_go", "lmmi_village_harvest_resp", "lmmi_village_harvest_lead",
                "{=lmmi_village_harvest_go}I'll lend a hand.", null, () => Choose(LeadOn));
            starter.AddDialogLine("lmmi_village_harvest_lead", "lmmi_village_harvest_lead", "close_window",
                "{=!}{LMMI_VILLAGE_HARVEST_LEAD}",
                    () => Flavor.Say("LMMI_VILLAGE_HARVEST_LEAD",
                        "{=lmmi_village_harvest_lead}Bless you! The field's this way — quick, before it breaks!",
                        "{=lmmi_village_harvest_lead_2}Thank you! This way — hurry, the sky's getting dark!",
                        "{=lmmi_village_harvest_lead_3}Bless you! Quick, to the field before the rain!"), null);
            starter.AddPlayerLine("lmmi_village_harvest_men", "lmmi_village_harvest_resp", "lmmi_village_harvest_men_resp",
                "{=lmmi_village_harvest_men}My men will help.", () => MobileParty.MainParty.MemberRoster.TotalHealthyCount >= 9,
                () => Choose(sc => BeginFieldWork(sc, yourMen: true)));   // a few of them, in the field (StreetHarvest.cs)
            starter.AddDialogLine("lmmi_village_harvest_men_resp", "lmmi_village_harvest_men_resp", "close_window",
                "{=!}{LMMI_VILLAGE_HARVEST_MEN}",
                    () => Flavor.Say("LMMI_VILLAGE_HARVEST_MEN",
                        "{=lmmi_village_harvest_men_resp}Soldiers in the fields! Well, why not — bless you!",
                        "{=lmmi_village_harvest_men_resp_2}Soldiers bringing in the harvest! I've seen it all now — thank you!",
                        "{=lmmi_village_harvest_men_resp_3}Your men? In our fields? Bless you!"), null);
            starter.AddPlayerLine("lmmi_village_harvest_no", "lmmi_village_harvest_resp", "lmmi_village_harvest_refused",
                "{=lmmi_village_harvest_no}Not today.", null,
                () => Choose(sc => { sc.Outcome = "you had other work"; Finish(sc); }));
            starter.AddDialogLine("lmmi_village_harvest_refused", "lmmi_village_harvest_refused", "close_window",
                "{=!}{LMMI_VILLAGE_HARVEST_NO}",
                    () => Flavor.Say("LMMI_VILLAGE_HARVEST_NO",
                        "{=lmmi_village_harvest_refused}...Suit yourself.",
                        "{=lmmi_village_harvest_refused_2}...We'll manage. Somehow.",
                        "{=lmmi_village_harvest_refused_3}...Fine. We'll lose half of it, then."), null);

            // ---- The looters ----
            starter.AddDialogLine("lmmi_village_raiders", "start", "lmmi_village_raiders_resp",
                "{=!}{LMMI_VILLAGE_RAIDERS}",
                () => Talk(Kind.Raiders, Step.Asking) && Flavor.Say("LMMI_VILLAGE_RAIDERS",
                        "{=lmmi_village_raiders}Looters! At the edge of the village — they're driving off our sheep! Please, {?PLAYER.GENDER}madam{?}sir{\\?}, you've a blade!",
                        "{=lmmi_village_raiders_2}Help! Looters are taking our sheep — right there, at the edge of the fields! You've a sword, {?PLAYER.GENDER}madam{?}sir{\\?}!",
                        "{=lmmi_village_raiders_3}Please! Bandits are stealing the flock! There's nobody here who can fight them!"), null, 1100);
            starter.AddPlayerLine("lmmi_village_raiders_go", "lmmi_village_raiders_resp", "lmmi_street_lead_on",
                "{=lmmi_street_rescue_go}Show me.", null, () => Choose(LeadOn));
            starter.AddPlayerLine("lmmi_village_raiders_men", "lmmi_village_raiders_resp", "lmmi_village_raiders_men_resp",
                "{=lmmi_village_raiders_men}My men will see to it.", () => HealthyRegulars() >= MenNeeded,
                () => Choose(SendTheMen));   // a real fight, with real casualties (StreetLooters.cs)
            starter.AddDialogLine("lmmi_village_raiders_men_resp", "lmmi_village_raiders_men_resp", "close_window",
                "{=!}{LMMI_VILLAGE_RAIDERS_MEN}",
                    () => Flavor.Say("LMMI_VILLAGE_RAIDERS_MEN",
                        "{=lmmi_village_raiders_men_resp}Bless you — bless you all!",
                        "{=lmmi_village_raiders_men_resp_2}Your soldiers! Thank you — hurry!",
                        "{=lmmi_village_raiders_men_resp_3}Gods bless you and every one of your men!"), null);
            starter.AddPlayerLine("lmmi_village_raiders_no", "lmmi_village_raiders_resp", "lmmi_village_raiders_refused",
                "{=lmmi_village_raiders_no}Not my sheep.", null,
                () =>
                {
                    Shame(-1f, "wouldn't help a village against looters");
                    Choose(sc => { sc.Outcome = "you refused"; LetItPlayOut(sc); });
                });
            starter.AddDialogLine("lmmi_village_raiders_refused", "lmmi_village_raiders_refused", "close_window",
                "{=!}{LMMI_VILLAGE_RAIDERS_NO}",
                    () => Flavor.Say("LMMI_VILLAGE_RAIDERS_NO",
                        "{=lmmi_village_raiders_refused}...Then we'll go hungry this winter.",
                        "{=lmmi_village_raiders_refused_2}...Then we'll eat grass this winter.",
                        "{=lmmi_village_raiders_refused_3}...Thanks for nothing."), null);
        }

        private void AddShakedownDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_street_shakedown", "start", "lmmi_street_shakedown_resp",
                "{=lmmi_street_shakedown}Please, {?PLAYER.GENDER}madam{?}sir{\\?}... one of the watch is shaking down some poor soul over there. \"Fines\" for nothing. Nobody dares say a word to him.",
                () => Talk(Kind.Shakedown, Step.Asking), null, 1100);
            starter.AddPlayerLine("lmmi_street_shakedown_go", "lmmi_street_shakedown_resp", "lmmi_street_lead_on",
                "{=lmmi_street_rescue_go}Show me.", null, () => Choose(LeadOn));
            starter.AddPlayerLine("lmmi_street_shakedown_no", "lmmi_street_shakedown_resp", "lmmi_street_shakedown_refused",
                "{=lmmi_street_shakedown_no}I don't cross the watch.", null,
                () =>
                {
                    Shame(-1f, "wouldn't cross a crooked guard");
                    Choose(sc => { sc.Outcome = "you refused"; LetItPlayOut(sc); });
                });
            starter.AddDialogLine("lmmi_street_shakedown_refused", "lmmi_street_shakedown_refused", "close_window",
                "{=!}{LMMI_STREET_SHAKE_NO}",
                    () => Flavor.Say("LMMI_STREET_SHAKE_NO",
                        "{=lmmi_street_shakedown_refused}...No. Nobody does.",
                        "{=lmmi_street_shakedown_refused_2}...Of course not. Nobody does.",
                        "{=lmmi_street_shakedown_refused_3}...No. I understand. They'd ruin you too."), null);

            // The guard.
            starter.AddDialogLine("lmmi_street_shake", "start", "lmmi_street_shake_resp",
                "{=lmmi_street_shake}{LMMI_STREET_SHAKE}",
                () =>
                {
                    if (!Talk(Kind.Shakedown, Step.Confronting)) return false;
                    var line = Foreign(_scene!)
                        ? Flavor.Pick("{=lmmi_street_shake_foreign}Official business, {SLUR}. Move along, before I find something to fine you for.",
                                    "{=lmmi_street_shake_foreign_2}Watch business, {SLUR}. Walk on, before I fine you too.",
                                    "{=lmmi_street_shake_foreign_3}Keep moving, {SLUR}. This doesn't concern you.")
                        : Flavor.Pick("{=lmmi_street_shake_kin}Official business. Move along, friend.",
                                    "{=lmmi_street_shake_kin_2}Watch business, friend. Keep walking.",
                                    "{=lmmi_street_shake_kin_3}Nothing to see here. Move along.");
                    SetWords(line);
                    MBTextManager.SetTextVariable("LMMI_STREET_SHAKE", line);
                    MBTextManager.SetTextVariable("LMMI_STREET_SHAKE_FINE", ShakedownFine);
                    return true;
                }, null, 1100);

            starter.AddPlayerLine("lmmi_street_shake_pay", "lmmi_street_shake_resp", "lmmi_street_shake_paid",
                "{=lmmi_street_shake_pay}Whatever his fine is, here. Now leave him be. [{LMMI_STREET_SHAKE_FINE}{GOLD_ICON}]", null,
                () =>
                {
                    Hero.MainHero.ChangeHeroGold(-ShakedownFine);
                    Choose(sc =>
                    {
                        SteppedUp(sc, 2f, "paid off a crooked guard");
                        Disperse(sc, sc.Actors.ToList());
                        Thank(sc, sc.Victim, "shakedown");
                    });
                }, 100, (out TextObject why) => CanAfford(ShakedownFine, out why));
            starter.AddDialogLine("lmmi_street_shake_paid", "lmmi_street_shake_paid", "close_window",
                "{=!}{LMMI_STREET_SHAKE_PAID}",
                    () => Flavor.Say("LMMI_STREET_SHAKE_PAID",
                        "{=lmmi_street_shake_paid}...Hm. Seems his fine's been paid. Move along, the both of you.",
                        "{=lmmi_street_shake_paid_2}...Fine's been paid. Off with you both.",
                        "{=lmmi_street_shake_paid_3}...Hmph. Lucky him. Move along."), null);

            starter.AddPlayerLine("lmmi_street_shake_talk", "lmmi_street_shake_resp", _talkDownGuard.Entry,
                "{=lmmi_street_shake_talk}[Talk him down] Is this how the watch keeps the peace?", null,
                () =>
                {
                    var sc = _scene;
                    if (sc == null) return;
                    var listener = sc.TalkTo?.Character as CharacterObject;
                    float r = Resentment(sc), st = TownStandingBehavior.Get(sc.Settlement);
                    _talkDownGuard.Start(new[]
                        {
                            NativePersuasion.Argument(DefaultSkills.Leadership, DefaultTraits.Honor,
                                new TextObject("{=lmmi_street_shake_honor}Your captain would love to hear how you keep the peace."), listener, r, st),
                            NativePersuasion.Argument(DefaultSkills.Roguery, DefaultTraits.Calculating,
                                new TextObject("{=lmmi_street_shake_calc}Half the street is watching you. Is this worth your post?"), listener, r, st),
                            NativePersuasion.Argument(DefaultSkills.Charm, DefaultTraits.Mercy,
                                new TextObject("{=lmmi_street_shake_mercy}He's got nothing. Look at him. Let him go."), listener, r, st),
                        },
                        Flavor.Pick("{=lmmi_street_shake_opening}Careful, now.",
                                    "{=lmmi_street_shake_opening_2}Watch your mouth.",
                                    "{=lmmi_street_shake_opening_3}Choose your words carefully."),
                        Flavor.Pick("{=lmmi_street_shake_again}Anything else?",
                                    "{=lmmi_street_shake_again_2}And?",
                                    "{=lmmi_street_shake_again_3}Is that all?"),
                        Flavor.Pick("{=lmmi_street_shake_won}...Tch. Get out of here, the both of you.",
                                    "{=lmmi_street_shake_won_2}...Bah. Get lost, the pair of you.",
                                    "{=lmmi_street_shake_won_3}...Fine. Go. Both of you."),
                        Flavor.Pick("{=lmmi_street_shake_lost}Move along — or you're next.",
                                    "{=lmmi_street_shake_lost_2}Walk away, or you'll be next.",
                                    "{=lmmi_street_shake_lost_3}Keep talking and you'll share his fine."),
                        onWon: () => Choose(s2 =>
                        {
                            SteppedUp(s2, 3f, "shamed a crooked guard");
                            Disperse(s2, s2.Actors.ToList());
                            Thank(s2, s2.Victim, "shakedown");
                        }),
                        onLost: () =>
                        {
                            TownStandingBehavior.Adjust(Settlement.CurrentSettlement, 1f, "stood up to a crooked guard");
                            Choose(s2 => { s2.Outcome = "the guard wasn't moved"; LetItPlayOut(s2); });
                        });
                });

            starter.AddPlayerLine("lmmi_street_shake_fight", "lmmi_street_shake_resp", "lmmi_street_shake_fight_resp",
                "{=lmmi_street_shake_fight}Hands off him.", null,
                () => Choose(sc => StartFight(sc, 0.03f, won => OnGuardFightEnd(sc, won))));
            starter.AddDialogLine("lmmi_street_shake_fight_resp", "lmmi_street_shake_fight_resp", "close_window",
                "{=!}{LMMI_STREET_SHAKE_FIGHT}",
                    () => Flavor.Say("LMMI_STREET_SHAKE_FIGHT",
                        "{=lmmi_street_shake_fight_resp}You'll regret that.",
                        "{=lmmi_street_shake_fight_resp_2}You'll pay for that!",
                        "{=lmmi_street_shake_fight_resp_3}Big mistake, friend."), null);

            starter.AddPlayerLine("lmmi_street_shake_leave", "lmmi_street_shake_resp", "lmmi_street_shake_left",
                "{=lmmi_street_shake_leave}...Sorry. Carry on.", null, GuardLeft);
            starter.AddDialogLine("lmmi_street_shake_left", "lmmi_street_shake_left", "close_window",
                "{=!}{LMMI_STREET_SHAKE_LEFT}",
                    () => Flavor.Say("LMMI_STREET_SHAKE_LEFT",
                        "{=lmmi_street_shake_left}That's right.",
                        "{=lmmi_street_shake_left_2}Good. Mind your own business.",
                        "{=lmmi_street_shake_left_3}Smart. Keep walking."), null);

            _talkDownGuard.Register(starter);
            _talkDownGuard.AddLeave(starter, "{=lmmi_street_shake_leave}...Sorry. Carry on.", "lmmi_street_shake_left", GuardLeft);
        }

        private void GuardLeft()
        {
            TownStandingBehavior.Adjust(Settlement.CurrentSettlement, -1f, "backed down from a crooked guard");
            Choose(sc => { sc.Outcome = "you backed down"; LetItPlayOut(sc); });
        }

        private void AddThanksDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_street_thanks", "start", "lmmi_street_thanks_resp",
                "{=lmmi_street_thanks}{LMMI_STREET_THANKS}",
                () =>
                {
                    var sc = _scene;
                    if (sc == null || sc.Step != Step.Thanking || sc.TalkTo == null
                        || ConversationMission.OneToOneConversationAgent != sc.TalkTo) return false;
                    if (!sc.Talking) { sc.Talking = true; sc.TalkAt = _sceneTime; sc.Chosen = false; }
                    bool foreign = Foreign(sc);
                    TextObject line;
                    switch (sc.Outcome)
                    {
                        case "rescued":
                            line = foreign
                                ? Flavor.Pick("{=lmmi_street_thanks_rescue_foreign}Thank you... I thought— A {DEMONYM}, of all people. You stood up for me when my own neighbours looked away. Everyone will hear of it, I swear.",
                                    "{=lmmi_street_thanks_rescue_foreign_2}Thank you... you didn't have to. A {DEMONYM}, and you stepped in when no one else would. I'll tell everyone.",
                                    "{=lmmi_street_thanks_rescue_foreign_3}I won't forget this. Nobody else helped — only you, a {DEMONYM}. Thank you.")
                                : Flavor.Pick("{=lmmi_street_thanks_rescue_kin}Thank you... thank you. One of our own, standing up for me when the rest just watched. Everyone will hear of it.",
                                    "{=lmmi_street_thanks_rescue_kin_2}Thank you — you saved me. I'll make sure everyone knows.",
                                    "{=lmmi_street_thanks_rescue_kin_3}When everyone else looked away, you didn't. Thank you.");
                            break;
                        case "debt_freed":   // you fought them off, or talked them down: the debt's still his, his hands are safe
                            line = foreign
                                ? Flavor.Pick("{=lmmi_street_thanks_debt_freed_foreign}A {DEMONYM}, standing between me and the moneylender's men... I don't know what to say. Thank you. Everyone will hear of it.",
                                    "{=lmmi_street_thanks_debt_freed_foreign_2}A {DEMONYM} saved my hands. I won't forget it — and neither will anyone else.",
                                    "{=lmmi_street_thanks_debt_freed_foreign_3}Thank you. I never thought a {DEMONYM} would stand up for someone like me.")
                                : Flavor.Pick("{=lmmi_street_thanks_debt_freed_kin}I thought that was it for my hands. Thank you — I mean it. Everyone will hear what you did.",
                                    "{=lmmi_street_thanks_debt_freed_kin_2}My hands — still whole. Thank you. I'll tell everyone.",
                                    "{=lmmi_street_thanks_debt_freed_kin_3}Thank you. I was sure they'd break my fingers.");
                            break;
                        case "debt_paid":    // you paid it out of your own purse: now he owes you
                            line = foreign
                                ? Flavor.Pick("{=lmmi_street_thanks_debt_paid_foreign}A {DEMONYM}, paying a stranger's debt out of {?PLAYER.GENDER}her{?}his{\\?} own purse... Every coin of that is yours again the day I have it, I swear. And everyone will hear of it.",
                                    "{=lmmi_street_thanks_debt_paid_foreign_2}You paid my debt — a {DEMONYM}, paying for me! I'll pay you back, I swear.",
                                    "{=lmmi_street_thanks_debt_paid_foreign_3}Out of your own purse... I'll repay every coin, and tell everyone what a {DEMONYM} did for me.")
                                : Flavor.Pick("{=lmmi_street_thanks_debt_paid_kin}You paid it — all of it, out of your own purse. I'll repay you when I can, every coin, I swear it. And I'll tell everyone what you did.",
                                    "{=lmmi_street_thanks_debt_paid_kin_2}You paid it all. I owe you everything — I'll pay you back, I swear it.",
                                    "{=lmmi_street_thanks_debt_paid_kin_3}My debt, cleared! I'll repay you, every coin. Thank you.");
                            break;
                        case "child_found":
                            line = foreign
                                ? Flavor.Pick("{=lmmi_street_thanks_child_foreign}My baby— oh, thank you, thank you. And a {DEMONYM}... I'll never say a word against your people again, not one.",
                                    "{=lmmi_street_thanks_child_foreign_2}My boy! Thank you — oh, thank you! I'll never say another word against your kind.",
                                    "{=lmmi_street_thanks_child_foreign_3}You found him! A {DEMONYM} found my baby! Thank you!")
                                : Flavor.Pick("{=lmmi_street_thanks_child_kin}My baby! Oh, thank you — I thought... thank you. Everyone will hear what you did.",
                                    "{=lmmi_street_thanks_child_kin_2}My boy! You found him! Thank you, thank you!",
                                    "{=lmmi_street_thanks_child_kin_3}Oh, thank the gods — and you! Thank you!");
                            break;
                        case "judged":
                            line = foreign
                                ? Flavor.Pick("{=lmmi_street_thanks_judged_foreign}A {DEMONYM}, settling our quarrels, and fairly too. Who'd have thought. The whole village will hear of it.",
                                    "{=lmmi_street_thanks_judged_foreign_2}A fair judgment, and from a {DEMONYM}. The village will remember.",
                                    "{=lmmi_street_thanks_judged_foreign_3}Who'd have thought a {DEMONYM} would judge us so fairly? Thank you.")
                                : Flavor.Pick("{=lmmi_street_thanks_judged_kin}Thank you. They'd have been at it with scythes by nightfall. The whole village will hear you were fair.",
                                    "{=lmmi_street_thanks_judged_kin_2}Thank you. You've saved a lot of broken heads today.",
                                    "{=lmmi_street_thanks_judged_kin_3}Fairly judged. The whole village will say so.");
                            break;
                        case "raiders_men":
                            line = foreign
                                ? Flavor.Pick("{=lmmi_street_thanks_raiders_men_foreign}{DEMONYM} soldiers, running looters off our fields... Who'd have thought it. Bless them — and bless you for sending them.",
                                    "{=lmmi_street_thanks_raiders_men_foreign_2}{DEMONYM} soldiers, fighting for our flock! Thank you — and thank them!",
                                    "{=lmmi_street_thanks_raiders_men_foreign_3}Your men saved our sheep. Bless them, {DEMONYM} or not!")
                                : Flavor.Pick("{=lmmi_street_thanks_raiders_men_kin}Your men ran them off! Bless them, every one — and bless you for sending them.",
                                    "{=lmmi_street_thanks_raiders_men_kin_2}Your men chased them off! Thank them for us — every one!",
                                    "{=lmmi_street_thanks_raiders_men_kin_3}The flock's safe, thanks to your soldiers. Bless them!");
                            break;
                        case "raiders":
                            line = foreign
                                ? Flavor.Pick("{=lmmi_street_thanks_raiders_foreign}A {DEMONYM}, bleeding for our sheep... I'll not hear a word against your people again. There's bread and ale at every door for you tonight.",
                                    "{=lmmi_street_thanks_raiders_foreign_2}A {DEMONYM}, fighting for our sheep! There's a meal and a bed for you in any house here.",
                                    "{=lmmi_street_thanks_raiders_foreign_3}You bled for us, {DEMONYM}. We won't forget it.")
                                : Flavor.Pick("{=lmmi_street_thanks_raiders_kin}You ran them off! The whole flock — bless you. There's bread and ale at every door for you tonight.",
                                    "{=lmmi_street_thanks_raiders_kin_2}The sheep are safe! Bless you — eat with us tonight!",
                                    "{=lmmi_street_thanks_raiders_kin_3}You drove them off! The whole village owes you.");
                            break;
                        case "shakedown":
                            line = foreign
                                ? Flavor.Pick("{=lmmi_street_thanks_shakedown_foreign}A {DEMONYM} standing up to the watch for one of us... I don't know what to say. Thank you. People will hear.",
                                    "{=lmmi_street_thanks_shakedown_foreign_2}A {DEMONYM}, facing down the watch for me... Thank you. I'll tell everyone.",
                                    "{=lmmi_street_thanks_shakedown_foreign_3}Thank you. I never thought a {DEMONYM} would stand up for us against them.")
                                : Flavor.Pick("{=lmmi_street_thanks_shakedown_kin}Nobody stands up to them. Nobody. Thank you — the whole street will hear of it.",
                                    "{=lmmi_street_thanks_shakedown_kin_2}Somebody finally stood up to them! Thank you!",
                                    "{=lmmi_street_thanks_shakedown_kin_3}Thank you. The street will cheer you tonight.");
                            break;
                        case "purse_back":
                            line = foreign
                                ? Flavor.Pick("{=lmmi_street_thanks_purse_foreign}My purse! Bless you — bless you! A {DEMONYM}... I'll tell everyone, I swear it.",
                                    "{=lmmi_street_thanks_purse_foreign_2}My purse — bless you! And from a {DEMONYM}, too. I'll tell everyone.",
                                    "{=lmmi_street_thanks_purse_foreign_3}You got it back! Thank you — I'll never speak ill of your people again.")
                                : Flavor.Pick("{=lmmi_street_thanks_purse_kin}My purse! Bless you! The rent's safe... I'll tell everyone what you did.",
                                    "{=lmmi_street_thanks_purse_kin_2}My purse! The rent's saved! Thank you!",
                                    "{=lmmi_street_thanks_purse_kin_3}You caught him! Thank you — bless you!");
                            break;
                        default:
                            line = foreign
                                ? Flavor.Pick("{=lmmi_street_thanks_brawl_foreign}A {DEMONYM} breaking up our youngsters' fights... Didn't think I'd live to see it. The whole street saw — they'll talk, mark my words.",
                                    "{=lmmi_street_thanks_brawl_foreign_2}A {DEMONYM}, stopping our lads from killing each other. People will talk about this.",
                                    "{=lmmi_street_thanks_brawl_foreign_3}Thank you. I didn't think a {DEMONYM} would care what happens to our boys.")
                                : Flavor.Pick("{=lmmi_street_thanks_brawl_kin}Thank heavens someone stepped in. The whole street saw it — one of our own, doing what the watch wouldn't.",
                                    "{=lmmi_street_thanks_brawl_kin_2}Thank you — somebody had to stop them.",
                                    "{=lmmi_street_thanks_brawl_kin_3}Thank you for stepping in. They'd have killed each other.");
                            break;
                    }
                    SetWords(line);
                    MBTextManager.SetTextVariable("LMMI_STREET_THANKS", line);
                    return true;
                }, null, 1100);
            starter.AddPlayerLine("lmmi_street_thanks_ok", "lmmi_street_thanks_resp", "close_window",
                "{=lmmi_street_thanks_ok}Glad to help.", null,
                () => Choose(sc => { if (sc.Kind == Kind.LostChild) GoHomeTogether(sc); else Finish(sc); }));
        }

        // ---- Save / load: "settlementId;readyAtHours" entries separated by '|';
        //      setups: "settlementId;softTouches;lastSetupHours" ----

        public override void SyncData(IDataStore dataStore)
        {
            string data = string.Empty, setups = string.Empty;
            if (!dataStore.IsLoading)
            {
                double now = CampaignTime.Now.ToHours;
                data = string.Join("|", _readyAtHours.Where(kv => kv.Value > now)
                    .Select(kv => kv.Key + ";" + kv.Value.ToString("R", CultureInfo.InvariantCulture)));
                setups = string.Join("|", _softTouch.Keys.Union(_setupAtHours.Keys).Select(id =>
                    id + ";" + (_softTouch.TryGetValue(id, out var soft) ? soft : 0).ToString(CultureInfo.InvariantCulture)
                    + ";" + (_setupAtHours.TryGetValue(id, out var at) ? at : -1.0).ToString("R", CultureInfo.InvariantCulture)));
            }
            dataStore.SyncData("lmmi_street_v1", ref data);
            dataStore.SyncData("lmmi_street_setups_v1", ref setups);
            // The sentence: "untilHours;settlementId;crime;headsman;killed;commuted;flogged" (older saves stop at "killed").
            string sentence = string.Empty;
            if (!dataStore.IsLoading && _sentence != Sentence.None)
                sentence = string.Join(";", _sentenceUntilHours.ToString("R", CultureInfo.InvariantCulture), _sentenceIn,
                    _sentenceCrime.ToString("R", CultureInfo.InvariantCulture), _headsman ? "1" : "0", _sentenceKilled.ToString(CultureInfo.InvariantCulture),
                    _commuted ? "1" : "0", _flogged ? "1" : "0");
            dataStore.SyncData("lmmi_street_sentence_v1", ref sentence);
            if (dataStore.IsLoading)
            {
                ClearSentence();
                var sp = (sentence ?? string.Empty).Split(';');
                if ((sp.Length == 5 || sp.Length == 7) && double.TryParse(sp[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var until)
                    && float.TryParse(sp[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var crime)
                    && int.TryParse(sp[4], out var killed))
                {
                    _sentence = Sentence.Serving;
                    _sentenceUntilHours = until;
                    _sentenceIn = sp[1];
                    _sentenceCrime = crime;
                    _headsman = sp[3] == "1";
                    _sentenceKilled = killed;
                    if (sp.Length == 7) { _commuted = sp[5] == "1"; _flogged = sp[6] == "1"; }
                }
            }
            if (!dataStore.IsLoading) return;

            _readyAtHours = new Dictionary<string, double>();
            foreach (var entry in (data ?? string.Empty).Split('|'))
            {
                var p = entry.Split(';');
                if (p.Length == 2 && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var at))
                    _readyAtHours[p[0]] = at;
            }
            _softTouch = new Dictionary<string, int>();
            _setupAtHours = new Dictionary<string, double>();
            foreach (var entry in (setups ?? string.Empty).Split('|'))
            {
                var p = entry.Split(';');
                if (p.Length != 3) continue;
                if (int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var soft) && soft > 0) _softTouch[p[0]] = soft;
                if (double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var at) && at >= 0) _setupAtHours[p[0]] = at;
            }
        }
    }

    /// <summary>
    /// A part in a street scene, run as the daily behavior group's scripted behavior (the slot vanilla's own
    /// ScriptBehavior uses): walk or run somewhere and stay, or keep up with someone; face someone; loop an animation
    /// once there. Anyone carrying something sets it down first (and picks it back up when released). Disabling the
    /// scripted behavior hands the agent back to its routine.
    /// </summary>
    public sealed class LmmiStageBehavior : AgentBehavior
    {
        private static readonly ActionIndexCache PutDownBegin = ActionIndexCache.Create("act_pickup_down_begin");
        private static readonly ActionIndexCache PutDownEnd = ActionIndexCache.Create("act_pickup_down_end");

        private WorldPosition _target;
        private bool _hasTarget;
        private Agent? _follow;
        private float _followDistance;
        private Agent? _face;
        private bool _run;
        private ActionIndexCache _loop = ActionIndexCache.act_none;
        private bool _hasLoop;
        private bool _looping;
        private float _loopTimer;
        private float _faceTimer;
        private bool _sent;
        private Vec3 _sentTo;
        private float _speed = -1f;
        private bool _limited;
        private bool _seated;
        private bool _occupying;
        private MBActionSet _originalSet;
        private bool _swappedSet;

        // Chasing someone flat out (Chase): re-aimed a few times a second, a little ahead of where they're going.
        private bool _sprint;
        private float _sprintTimer;
        private bool _boosted;
        private float _baseSpeedMultiplier;

        // Setting down whatever they carry: 0 = not checked, 1 = bending down, 2 = straightening up, 3 = done.
        private int _putDown;
        private float _putDownTimer;
        private bool _hidMeshes, _droppedItem;

        public LmmiStageBehavior(AgentBehaviorGroup behaviorGroup) : base(behaviorGroup) { }

        public void GoTo(WorldPosition position, bool run, Agent? face = null, string? loop = null, float speed = -1f)
        {
            EndSprint();
            _speed = speed;
            _follow = null;
            _target = position;
            _hasTarget = true;
            _run = run;
            _face = face;
            _sent = false;
            SetLoop(loop);
        }

        public void Hold(Agent? face = null, string? loop = null) => GoTo(OwnerAgent.GetWorldPosition(), false, face, loop);

        /// <summary>They're using a vanilla object (a chair): stay out of its way. Released, they stand up and go.</summary>
        public void Occupy()
        {
            EndSprint();
            _follow = null;
            _face = null;
            _hasTarget = false;
            _sent = true;
            _seated = false;
            _occupying = true;
            SetAside(OwnerAgent);
            SetLoop(null);
            Navigator.ClearTarget();
        }

        /// <summary>Put them exactly here, facing that way, doing that (e.g. seated at a table): no walking, no turning after.</summary>
        public void Sit(Vec3 position, float facing, string act)
        {
            EndSprint();
            var me = OwnerAgent;
            _follow = null;
            _face = null;
            _hasTarget = false;
            _sent = true;
            _seated = true;
            Navigator.ClearTarget();
            SetAside(me);
            me.TeleportToPosition(position);
            var dir = Vec2.FromRotation(facing);
            me.SetMovementDirection(in dir);
            var here = me.GetWorldPosition();
            me.SetScriptedPositionAndDirection(ref here, facing, false,
                Agent.AIScriptedFrameFlags.DoNotRun | Agent.AIScriptedFrameFlags.ConsiderRotation);
            SetLoop(act);
            _loopTimer = 0.2f;
        }

        /// <summary>Instantly set down whatever they carry (hidden during a fade, so no animation needed).</summary>
        private void SetAside(Agent me)
        {
            _putDown = 3;
            bool civilian = me.Character is TaleWorlds.CampaignSystem.CharacterObject co
                            && (co.Occupation == TaleWorlds.CampaignSystem.Occupation.Townsfolk || co.Occupation == TaleWorlds.CampaignSystem.Occupation.Villager);
            if (!civilian || !Navigator.IsCarryingSomething()) return;
            for (var i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                if (!me.Equipment[i].IsEmpty) { me.RemoveEquippedWeapon(i); _droppedItem = true; }
            Navigator.HoldAndHideRecentlyUsedMeshes();
            _hidMeshes = true;
            UseCommonActionSet(me);
        }

        /// <summary>
        /// Carriers are spawned with a carrying animation set (arms held for a basket or sack): with the load set down,
        /// switch to the ordinary townsfolk set so the arms come down too. Restored on release.
        /// </summary>
        private void UseCommonActionSet(Agent me)
        {
            try
            {
                if (_swappedSet || me.Monster == null) return;
                var set = MBGlobals.GetActionSet(ActionSetCode.GenerateActionSetNameWithSuffix(me.Monster, me.IsFemale, "_villager"));
                if (!set.IsValid || set.GetName() == me.ActionSet.GetName()) return;
                _originalSet = me.ActionSet;
                var data = MonsterExtensions.FillAnimationSystemData(me.Monster, set, me.Character.GetStepSize(), false);
                me.SetActionSet(ref data);
                _swappedSet = true;
            }
            catch (Exception ex) { LmmiLog.Error("LmmiStageBehavior: swapping the action set threw", ex); }
        }

        private void RestoreActionSet(Agent me)
        {
            if (!_swappedSet) return;
            _swappedSet = false;
            try
            {
                if (!_originalSet.IsValid || me.Monster == null) return;
                var data = MonsterExtensions.FillAnimationSystemData(me.Monster, _originalSet, me.Character.GetStepSize(), false);
                me.SetActionSet(ref data);
            }
            catch (Exception ex) { LmmiLog.Error("LmmiStageBehavior: restoring the action set threw", ex); }
        }

        public void Follow(Agent who, float distance, bool run)
        {
            EndSprint();
            _speed = -1f;
            _follow = who;
            _followDistance = distance;
            _run = run;
            _hasTarget = false;
            _face = who;
            _sent = false;
            SetLoop(null);
        }

        /// <summary>
        /// Run someone down: flat out until they're caught (whoever directs this decides when that is). Unlike
        /// <see cref="Follow"/>, which walks once close and slows to a stop as it arrives, the chaser aims a little
        /// ahead of the quarry, never slows down (AIScriptedFrameFlags.NeverSlowDown) and has no speed cap; with
        /// <paramref name="speedFactor"/> &gt; 1 they're a touch faster than they'd otherwise run (restored after).
        /// </summary>
        public void Chase(Agent who, float speedFactor = 1f)
        {
            Follow(who, 0.5f, run: true);
            _sprint = true;
            _sprintTimer = 0f;
            var me = OwnerAgent;
            try
            {
                Navigator.ClearTarget();   // we steer them ourselves from here; the navigator mustn't stop them "on arrival"
                me.SetMaximumSpeedLimit(-1f, false);
                if (speedFactor > 1.001f && me.AgentDrivenProperties != null)
                {
                    _baseSpeedMultiplier = me.AgentDrivenProperties.MaxSpeedMultiplier;
                    me.AgentDrivenProperties.MaxSpeedMultiplier = _baseSpeedMultiplier * speedFactor;
                    me.UpdateCustomDrivenProperties();
                    _boosted = true;
                }
            }
            catch (Exception ex) { LmmiLog.Error("LmmiStageBehavior: starting a chase threw", ex); }
        }

        private void EndSprint()
        {
            if (!_sprint && !_boosted) return;
            _sprint = false;
            if (!_boosted) return;
            _boosted = false;
            try
            {
                var me = OwnerAgent;
                if (me != null && me.IsActive() && me.AgentDrivenProperties != null)
                {
                    me.AgentDrivenProperties.MaxSpeedMultiplier = _baseSpeedMultiplier;
                    me.UpdateCustomDrivenProperties();
                }
            }
            catch (Exception ex) { LmmiLog.Error("LmmiStageBehavior: ending a chase threw", ex); }
        }

        /// <summary>One step of a chase: aim where they'll be in half a second, and keep running.</summary>
        private void SprintTick(Agent me, float dt)
        {
            var quarry = _follow;
            if (quarry == null || !quarry.IsActive()) { _follow = null; EndSprint(); return; }
            _sprintTimer -= dt;
            if (_sprintTimer > 0f) return;
            _sprintTimer = 0.25f;
            var target = quarry.GetWorldPosition();
            var lead = quarry.Velocity.AsVec2 * 0.5f;
            if (lead.LengthSquared > 9f) lead = lead.Normalized() * 3f;
            if (lead.LengthSquared > 0.25f)
            {
                var ahead = target;
                ahead.SetVec2(quarry.Position.AsVec2 + lead);
                if (ahead.GetNavMesh() != UIntPtr.Zero) target = ahead;
            }
            me.SetScriptedPosition(ref target, false, Agent.AIScriptedFrameFlags.NeverSlowDown);
            _sent = true;
        }

        public bool Arrived => _putDown >= 3 && (_follow != null
            ? _follow.IsActive() && OwnerAgent.Position.Distance(_follow.Position) <= _followDistance + 0.8f
            : !_hasTarget || OwnerAgent.Position.AsVec2.Distance(_target.AsVec2) <= 1.5f);

        private void SetLoop(string? action)
        {
            StopLoop();
            _hasLoop = action != null;
            if (action != null) _loop = ActionIndexCache.Create(action);
            _loopTimer = 0.5f;
        }

        private void StopLoop()
        {
            if (_looping && OwnerAgent.IsActive())
                OwnerAgent.SetActionChannel(0, in ActionIndexCache.act_none, ignorePriority: true);
            _looping = false;
        }

        protected override void OnActivate()
        {
            _putDown = 0;
        }

        /// <summary>Bend down, leave the load on the ground (a held item is dropped; a shouldered one is set aside), straighten up.</summary>
        private bool PuttingDown(Agent me, float dt)
        {
            if (_putDown >= 3) return false;
            if (_putDown == 0)
            {
                // Civilians only: a guard's spear or a tough's club is not a load to set down.
                bool civilian = me.Character is TaleWorlds.CampaignSystem.CharacterObject co
                                && (co.Occupation == TaleWorlds.CampaignSystem.Occupation.Townsfolk || co.Occupation == TaleWorlds.CampaignSystem.Occupation.Villager);
                if (!civilian || !Navigator.IsCarryingSomething()) { _putDown = 3; return false; }
                Navigator.ClearTarget();
                me.SetActionChannel(0, in PutDownBegin, ignorePriority: true);
                _putDown = 1;
                _putDownTimer = 0.8f;
                return true;
            }
            _putDownTimer -= dt;
            if (_putDownTimer > 0f) return true;
            if (_putDown == 1)
            {
                var main = me.GetPrimaryWieldedItemIndex();
                if (main >= EquipmentIndex.WeaponItemBeginSlot) { me.DropItem(main); _droppedItem = true; }
                var off = me.GetOffhandWieldedItemIndex();
                if (off >= EquipmentIndex.WeaponItemBeginSlot) { me.DropItem(off); _droppedItem = true; }
                Navigator.HoldAndHideRecentlyUsedMeshes();
                _hidMeshes = true;
                UseCommonActionSet(me);
                me.SetActionChannel(0, in PutDownEnd, ignorePriority: true);
                _putDown = 2;
                _putDownTimer = 0.7f;
                return true;
            }
            _putDown = 3;
            _sent = false;
            return false;
        }

        public override void Tick(float dt, bool isSimulation)
        {
            var me = OwnerAgent;
            if (me == null || !me.IsActive()) return;
            if (_occupying) return;   // the chair's own animation point runs them
            if (_seated)
            {
                // Keep the pose; re-take it only if something (a conversation) interrupted it.
                _loopTimer -= dt;
                if (_hasLoop && _loopTimer <= 0f)
                {
                    _loopTimer = 1.5f;
                    if (me.GetCurrentAction(0) != _loop) _looping = me.SetActionChannel(0, in _loop, ignorePriority: true);
                }
                return;
            }
            if (PuttingDown(me, dt)) return;
            if (_sprint) { SprintTick(me, dt); return; }
            var flags = _run ? Agent.AIScriptedFrameFlags.None : Agent.AIScriptedFrameFlags.DoNotRun;

            if (_follow != null)
            {
                if (!_follow.IsActive()) { _follow = null; return; }
                if (me.Position.Distance(_follow.Position) > _followDistance)
                {
                    if (!_sent || _sentTo.Distance(_follow.Position) > 1.5f)
                    {
                        Navigator.SetTargetFrame(_follow.GetWorldPosition(), Facing(me.Position, _follow.Position),
                            _followDistance * 0.8f, -10f, flags, true);
                        _sentTo = _follow.Position;
                        _sent = true;
                    }
                }
                else if (_sent)
                {
                    Navigator.ClearTarget();
                    _sent = false;
                }
            }
            else if (_hasTarget && !_sent)
            {
                // A speed cap (a fleeing thief you can actually catch), or none.
                if (_speed > 0f) { me.SetMaximumSpeedLimit(_speed, true); _limited = true; }
                else if (_limited) { me.SetMaximumSpeedLimit(-1f, false); _limited = false; }
                Navigator.SetTargetFrame(_target, _face != null ? Facing(_target.GetGroundVec3(), _face.Position) : me.Frame.rotation.f.AsVec2.RotationInRadians,
                    0.7f, -10f, flags, true);
                _sent = true;
            }

            if (!Arrived) return;

            // Once there: turn to whoever matters, and act the part.
            _faceTimer -= dt;
            if (_face != null && _face.IsActive() && _faceTimer <= 0f)
            {
                _faceTimer = 1f;
                var here = me.GetWorldPosition();
                me.SetScriptedPositionAndDirection(ref here, Facing(me.Position, _face.Position), false,
                    Agent.AIScriptedFrameFlags.DoNotRun | Agent.AIScriptedFrameFlags.ConsiderRotation);
            }
            if (_hasLoop)
            {
                _loopTimer -= dt;
                if (_loopTimer <= 0f)
                {
                    _loopTimer = 3f;
                    if (me.GetCurrentAction(0) != _loop) _looping = me.SetActionChannel(0, in _loop, ignorePriority: false);
                }
            }
        }

        private static float Facing(Vec3 from, Vec3 to)
        {
            var d = (to - from).AsVec2;
            return d.LengthSquared < 0.0001f ? 0f : d.Normalized().RotationInRadians;
        }

        protected override void OnDeactivate()
        {
            try
            {
                EndSprint();
                StopLoop();
                if (_putDown == 1 || _putDown == 2) OwnerAgent.SetActionChannel(0, in ActionIndexCache.act_none, ignorePriority: true);
                if (_limited && OwnerAgent.IsActive()) OwnerAgent.SetMaximumSpeedLimit(-1f, false);
                _limited = false;
                _seated = false;
                if (_occupying && OwnerAgent.IsActive() && OwnerAgent.IsUsingGameObject) OwnerAgent.StopUsingGameObject(true);
                _occupying = false;
                _follow = null;
                _face = null;
                _hasTarget = false;
                _hasLoop = false;
                Navigator.ClearTarget();
                if (OwnerAgent.IsActive())
                {
                    OwnerAgent.DisableScriptedMovement();
                    // Back to what they were carrying.
                    RestoreActionSet(OwnerAgent);
                    if (_hidMeshes) Navigator.RecoverRecentlyUsedMeshes();
                    if (_droppedItem) Navigator.SetSpecialItem();
                }
                _hidMeshes = _droppedItem = false;
                _putDown = 0;
            }
            catch (Exception ex) { LmmiLog.Error("LmmiStageBehavior.OnDeactivate threw", ex); }
        }

        public override float GetAvailability(bool isSimulation) => 0f;

        public override string GetDebugInfo() => "LMMI street scene";
    }
}
