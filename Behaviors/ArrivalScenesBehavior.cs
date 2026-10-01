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
using SandBox.Missions.AgentBehaviors;
using SandBox.Missions.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
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
    /// People come to you. Shortly after you walk into a town or village, at most one notable may walk up
    /// and start talking: a friend who missed you throws a feast, a friend warns you about a schemer in town,
    /// or someone who despises you tells you to leave. Uses vanilla's own approach mechanism
    /// (ScriptBehavior on the daily behavior group, as the Prodigal Son quest does).
    /// </summary>
    public partial class ArrivalScenesBehavior : CampaignBehaviorBase
    {
        private enum ArrivalKind { Feast, Warning, WarnedOff, Gratitude, Tribute, Petition, Beggar, Heckler, Admirer, LordSnub, LordFlatter, LordRespect }

        private sealed class Arrival
        {
            public Hero Host = null!;                 // the notable; null for a townsperson
            public CharacterObject Speaker = null!;   // who walks up (notable or townsperson)
            public string SpeakerName => Host?.Name?.ToString() ?? Speaker?.Name?.ToString() ?? "someone";
            public ArrivalKind Kind;
            public Hero? Schemer;     // Warning: who to beware of
            public int Gold;          // Gratitude gift
            public TributeGift? Gift; // Tribute gift
            public bool HasAsk;       // Tribute / Petition: what they want back (see ArrivalSycophants.cs)
            public PetitionAsk Ask;
            public Hero? Target;      // Petition: the rival, or the lord to lobby
            public bool Hinted;       // Tribute: you asked what they want
            public Agent Agent = null!;
            public Mission? Mission;
            public float StartedAt;
            public bool Talking;
            public float TalkStartedAt;
        }

        /// <summary>
        /// Someone you agreed to see from the wait menu. Vanilla's "talk to" (the settlement overlay's path) opens the
        /// scene with them brought to you and the conversation already started; their arrival line opens it.
        /// </summary>
        private sealed class Meeting
        {
            public Hero Host = null!;
            public ArrivalKind Kind;
            public Hero? Schemer;
            public Mission? Mission;
        }

        private const float SecondsBeforeApproach = 4f;
        private const float ApproachTimeoutSeconds = 60f;
        private const float MeetingFallbackSeconds = 8f;
        private const float FeastAwayDays = 30f;
        private const float FeastCooldownDays = 30f;
        private const float WarnOffCooldownDays = 10f;
        private const float TownsfolkCooldownDays = 5f;
        private const float LordCooldownDays = 20f;

        // settlement StringId -> campaign hour of your latest arrival; the one before it is kept for "missed you"
        [NonSerialized] private Dictionary<string, double> _lastVisitHours = new Dictionary<string, double>();
        [NonSerialized] private Dictionary<string, double> _previousVisitHours = new Dictionary<string, double>();
        // "heroId|kind" -> campaign hour when that event may happen again
        [NonSerialized] private Dictionary<string, double> _readyAtHours = new Dictionary<string, double>();
        // schemer's StringId -> the friend who warned you about them
        [NonSerialized] private Dictionary<string, string> _warnedBy = new Dictionary<string, string>();
        // settlement StringId -> notable whose warning you defied there this visit (raises the odds of an ambush)
        [NonSerialized] private Dictionary<string, string> _defiedIn = new Dictionary<string, string>();

        [NonSerialized] private Mission? _mission;
        [NonSerialized] private float _sceneTime;
        [NonSerialized] private bool _sceneHandled;
        [NonSerialized] private Arrival? _arrival;
        [NonSerialized] private List<Action> _afterTalk = new List<Action>();
        [NonSerialized] private Meeting? _meeting;
        [NonSerialized] private Dictionary<string, double> _promptedAtHours = new Dictionary<string, double>();
        [NonSerialized] private readonly NativePersuasion _answerHeckler = new NativePersuasion("heckler");

        /// <summary>The friend who warned you about this notable, if any (treachery counterplay).</summary>
        public static Hero? WarnedBy(Hero notable)
        {
            var self = Campaign.Current?.GetCampaignBehavior<ArrivalScenesBehavior>();
            if (self == null || !self._warnedBy.TryGetValue(notable.StringId, out var friendId)) return null;
            return Hero.FindFirst(h => h.StringId == friendId);
        }

        /// <summary>Who told you to leave this settlement and heard "I'll go where I please" — once.</summary>
        public static Hero? ConsumeDefiance(Settlement settlement)
        {
            var self = Campaign.Current?.GetCampaignBehavior<ArrivalScenesBehavior>();
            if (self == null || !self._defiedIn.TryGetValue(settlement.StringId, out var heroId)) return null;
            self._defiedIn.Remove(settlement.StringId);
            return Hero.FindFirst(h => h.StringId == heroId);
        }

        public static void ClearCooldowns() => Campaign.Current?.GetCampaignBehavior<ArrivalScenesBehavior>()?._readyAtHours.Clear();

        /// <summary>Someone is walking up to you or talking to you (or is being brought to you) — street events wait.</summary>
        public static bool IsBusy
        {
            get
            {
                var self = Campaign.Current?.GetCampaignBehavior<ArrivalScenesBehavior>();
                if (self == null) return false;
                if (self._arrival != null) return true;
                var mission = Mission.Current;
                return self._meeting != null && mission != null && self._meeting.Mission == mission;
            }
        }

        public override void RegisterEvents()
        {
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.MissionTickEvent.AddNonSerializedListener(this, OnMissionTick);
            CampaignEvents.ConversationEnded.AddNonSerializedListener(this, OnConversationEnded);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        /// <summary>
        /// Waiting in a town or village (the "wait here" menu): if someone would come looking for you, they do — a prompt
        /// says who; agree, and it goes like vanilla's "talk to" from the settlement overlay: the scene opens with them
        /// beside you and their conversation already started.
        /// </summary>
        private void OnHourlyTick()
        {
            try
            {
                var menu = Campaign.Current?.CurrentMenuContext?.GameMenu?.StringId;
                if (menu != "town_wait_menus" && menu != "village_wait_menus") return;
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null || Hero.MainHero.IsPrisoner || Mission.Current != null) return;
                if (!LmmiSettingsProvider.EnableArrivalScenes || !LmmiSettingsProvider.EnableNotableDisposition) return;
                if (StreetEventsBehavior.IsBusy || FeastBehavior.IsBusy) return;
                double now = CampaignTime.Now.ToHours;
                if (_promptedAtHours.TryGetValue(settlement.StringId, out var last) && now - last < 12.0) return;

                var pick = NotableCandidates(settlement)
                    .Where(c => c.Kind != ArrivalKind.WarnedOff)   // nobody comes to your door just to threaten you
                    .Where(c => MeetingLocation(settlement, c.Host) != null)
                    .OrderBy(c => Priority(c.Kind)).FirstOrDefault();
                if (pick.Host == null) return;
                _promptedAtHours[settlement.StringId] = now;

                var why = new TextObject(pick.Kind switch
                {
                    ArrivalKind.Feast => "{=lmmi_prompt_feast}{NAME} has come looking for you — something about a table laid in your honor.",
                    ArrivalKind.Warning => "{=lmmi_prompt_warning}{NAME} has come looking for you, and wants a quiet word.",
                    ArrivalKind.Gratitude => "{=lmmi_prompt_gratitude}{NAME} has come looking for you, to thank you in person.",
                    ArrivalKind.Tribute => "{=lmmi_prompt_tribute}{NAME} has come to pay their respects.",
                    ArrivalKind.Petition => "{=lmmi_prompt_petition}{NAME} has come looking for you, with a favor to ask.",
                    _ => "{=lmmi_prompt_generic}{NAME} has come looking for you.",
                });
                why.SetTextVariable("NAME", pick.Host.Name);
                var host = pick.Host;
                var kind = pick.Kind;
                var schemer = pick.Schemer;
                LmmiLog.Info($"Arrival prompt while waiting in {settlement.Name}: {host.Name} ({kind}).");
                Campaign.Current!.TimeControlMode = CampaignTimeControlMode.Stop;
                InformationManager.ShowInquiry(new InquiryData(
                    new TextObject("{=lmmi_prompt_title}Someone to see you").ToString(), why.ToString(), true, true,
                    new TextObject("{=lmmi_prompt_meet}Meet them").ToString(), new TextObject("{=lmmi_prompt_later}Not now").ToString(),
                    () => Meet(settlement, host, kind, schemer), null), true);
            }
            catch (Exception ex) { LmmiLog.Error("ArrivalScenesBehavior.OnHourlyTick threw", ex); }
        }

        /// <summary>
        /// Where a meeting with this notable can happen: where vanilla has them (the square, the village center or the
        /// tavern; an alley counts as the square, as in vanilla's "talk to"). Null: they can't come to you (the hall,
        /// the dungeon, not in town).
        /// </summary>
        private static Location? MeetingLocation(Settlement settlement, Hero host)
        {
            var complex = LocationComplex.Current;
            if (complex == null) return null;
            var location = complex.GetLocationOfCharacter(host);
            if (location == null) return null;
            switch (location.StringId)
            {
                case "center":
                case "village_center":
                case "tavern":
                    return location;
                case "alley":
                    return complex.GetLocationWithId(settlement.IsVillage ? "village_center" : "center");
                default:
                    return null;
            }
        }

        /// <summary>
        /// Out of the wait menu and face to face — vanilla's own "talk to" from the settlement overlay
        /// (GameMenuOverlay.ExecuteTroopAction): open the scene with the notable as <c>talkToChar</c>, and
        /// MissionConversationLogic(teleportNearChar) puts them beside you and starts the conversation. Their arrival
        /// line opens it (see <see cref="PromoteMeeting"/>).
        /// </summary>
        private void Meet(Settlement settlement, Hero host, ArrivalKind kind, Hero? schemer)
        {
            try
            {
                _meeting = null;
                GameMenu.SwitchToMenu(settlement.IsTown ? "town" : settlement.IsVillage ? "village" : "castle");
                var encounter = PlayerEncounter.LocationEncounter;
                var location = MeetingLocation(settlement, host);
                if (location == null || encounter == null)
                {
                    LmmiLog.Info($"Arrival: can't meet {host.Name} here (no location); back to the menu.");
                    return;
                }

                _meeting = new Meeting { Host = host, Kind = kind, Schemer = schemer };
                CampaignEventDispatcher.Instance?.OnPlayerStartTalkFromMenu(host);
                var opened = encounter.CreateAndOpenMissionController(location, null, host.CharacterObject);
                _meeting.Mission = opened as Mission;
                if (opened == null)
                {
                    _meeting = null;
                    LmmiLog.Info($"Arrival: no scene opened for the meeting with {host.Name} ({location.StringId}).");
                    return;
                }
                LmmiLog.Info($"Arrival: meeting {host.Name} ({kind}) in {settlement.Name}'s {location.StringId}; vanilla brings them to you.");
            }
            catch (Exception ex)
            {
                _meeting = null;
                LmmiLog.Error("ArrivalScenesBehavior.Meet threw", ex);
            }
        }

        /// <summary>
        /// The meeting's conversation has started (vanilla brought them to you, or you walked up to them yourself):
        /// it becomes this scene's arrival before any "start" line is chosen, so their arrival line opens it.
        /// Called from every arrival line's condition, so it happens whichever line is evaluated first.
        /// </summary>
        private void PromoteMeeting()
        {
            var m = _meeting;
            if (m == null || _arrival != null) return;
            var mission = Mission.Current;
            if (mission == null || (m.Mission != null && m.Mission != mission)) return;
            var hero = Hero.OneToOneConversationHero;
            if (hero == null || hero != m.Host) return;
            var agent = ConversationMission.OneToOneConversationAgent;
            if (agent == null) return;

            _meeting = null;
            var arrival = MakeArrival(m.Host, m.Kind, m.Schemer, agent);
            arrival.Talking = true;
            arrival.TalkStartedAt = _sceneTime;
            _arrival = arrival;
            // Our 1100-priority lines win before vanilla's start conditions, where meeting someone is recorded.
            if (!m.Host.HasMet) m.Host.SetHasMet();
            LmmiLog.Info($"Arrival: {m.Host.Name} is brought to you ({m.Kind}); their arrival line opens the conversation.");
        }

        /// <summary>Vanilla didn't bring them over (no conversation within a few seconds): they walk up instead, if they're here.</summary>
        private void MeetingFallback(Mission mission, Meeting m)
        {
            var agent = mission.Agents.FirstOrDefault(a => a.IsActive() && a.Character == m.Host.CharacterObject);
            var group = agent?.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>();
            if (agent == null || group == null)
            {
                LmmiLog.Info($"Arrival: {m.Host.Name} isn't in this scene after all; the meeting doesn't happen.");
                return;
            }
            LmmiLog.Info($"Arrival: vanilla didn't bring {m.Host.Name} to you; they walk over instead ({m.Kind}).");
            BeginApproach(MakeArrival(m.Host, m.Kind, m.Schemer, agent));
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (party != MobileParty.MainParty || settlement == null) return;
            var id = settlement.StringId;
            if (_lastVisitHours.TryGetValue(id, out var last)) _previousVisitHours[id] = last;
            else _previousVisitHours.Remove(id);
            _lastVisitHours[id] = CampaignTime.Now.ToHours;
        }

        private double? DaysAway(Settlement settlement) =>
            _previousVisitHours.TryGetValue(settlement.StringId, out var previous)
                ? (CampaignTime.Now.ToHours - previous) / CampaignTime.HoursInDay
                : (double?)null;

        // ---- Choosing and starting an arrival ----

        private void OnMissionTick(float dt)
        {
            try
            {
                var mission = Mission.Current;
                if (mission == null) return;
                if (_afterTalk.Count > 0 && mission.Mode != MissionMode.Conversation
                    && Campaign.Current?.ConversationManager?.IsConversationInProgress != true)
                {
                    var actions = _afterTalk.ToList();
                    _afterTalk.Clear();
                    foreach (var action in actions)
                    {
                        try { action(); }
                        catch (Exception ex) { LmmiLog.Error("Arrival: after-conversation action threw", ex); }
                    }
                }
                if (mission != _mission)
                {
                    _mission = mission;
                    _sceneTime = 0f;
                    _sceneHandled = false;
                    if (_arrival != null && _arrival.Mission != mission) _arrival = null;
                    if (_arrival != null) _sceneHandled = true;   // a meeting that started talking before this first tick
                    if (_meeting != null)
                    {
                        // The meeting's own scene: vanilla brings them to you, and nobody else walks up this visit.
                        if (_meeting.Mission == null || _meeting.Mission == mission)
                        {
                            _meeting.Mission = mission;
                            _sceneHandled = true;
                        }
                        else
                        {
                            LmmiLog.Info($"Arrival: the meeting with {_meeting.Host.Name} didn't happen (another scene opened).");
                            _meeting = null;
                        }
                    }
                }
                _sceneTime += dt;

                if (_arrival != null && _arrival.Talking && mission.Mode != MissionMode.Conversation
                    && _sceneTime - _arrival.TalkStartedAt > 5f)
                {
                    LmmiLog.Info($"Arrival: the conversation with {_arrival.SpeakerName} never started; letting go.");
                    ReleaseAgent();
                    _arrival = null;
                }

                if (_arrival != null && !_arrival.Talking && _sceneTime - _arrival.StartedAt > ApproachTimeoutSeconds)
                {
                    LmmiLog.Info($"Arrival: {_arrival.SpeakerName} never reached you ({_arrival.Kind}); giving up.");
                    ReleaseAgent();
                    _arrival = null;
                }

                if (Agent.Main == null || !Agent.Main.IsActive() || mission.Mode == MissionMode.Conversation) return;
                if (mission.GetMissionBehavior<MissionFightHandler>()?.IsThereActiveFight() == true) return;
                if (Campaign.Current?.GetCampaignBehavior<DisableMenuBehavior>()?.IsEscortActive == true) return;

                if (StreetEventsBehavior.IsBusy) return;

                // A meeting whose conversation never started (vanilla couldn't bring them over): they walk up instead.
                if (_meeting != null && _meeting.Mission == mission)
                {
                    if (_arrival == null && _sceneTime > MeetingFallbackSeconds)
                    {
                        var m = _meeting;
                        _meeting = null;
                        if (LmmiSettingsProvider.EnableArrivalScenes && m.Host.IsAlive) MeetingFallback(mission, m);
                    }
                    return;
                }

                if (_sceneHandled || _sceneTime < SecondsBeforeApproach) return;

                _sceneHandled = true;
                if (!LmmiSettingsProvider.EnableArrivalScenes || !LmmiSettingsProvider.EnableNotableDisposition) return;

                var location = CampaignMission.Current?.Location?.StringId;
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null) return;
                if (location == "lordshall") TryLordArrival(mission, settlement);
                else if (location == "center" || location == "village_center") TryStartArrival(mission, settlement);
            }
            catch (Exception ex)
            {
                LmmiLog.Error("ArrivalScenesBehavior.OnMissionTick threw", ex);
                _sceneHandled = true;
            }
        }

        private void TryStartArrival(Mission mission, Settlement settlement)
        {
            var candidates = NotableCandidates(settlement);

            LmmiLog.Info($"Arrival check in {settlement.Name}: {(DaysAway(settlement) is double d ? $"{d:0.#} days since last visit" : "first visit")}, " +
                         $"{candidates.Count} candidate(s){(candidates.Count > 0 ? ": " + string.Join(", ", candidates.Select(c => $"{c.Host.Name} {c.Kind}")) : "")}.");

            foreach (var c in candidates.OrderBy(c => Priority(c.Kind)))
            {
                var agent = mission.Agents.FirstOrDefault(a => a.IsActive() && a.Character == c.Host.CharacterObject);
                var group = agent?.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>();
                if (agent == null || group == null)
                {
                    LmmiLog.Info($"Arrival: {c.Host.Name} isn't in the scene or can't walk (scene mod?) — skipped.");
                    continue;
                }

                LmmiLog.Info($"Arrival: {c.Host.Name} walks up to you ({c.Kind}{(c.Schemer != null ? ", about " + c.Schemer.Name : "")}).");
                BeginApproach(MakeArrival(c.Host, c.Kind, c.Schemer, agent));
                return;
            }

            TryTownsfolkArrival(mission, settlement);
        }

        /// <summary>A notable's arrival, with what they bring or want filled in (gift, petition).</summary>
        private Arrival MakeArrival(Hero host, ArrivalKind kind, Hero? schemer, Agent agent)
        {
            int tier = Clan.PlayerClan?.Tier ?? 0;
            var arrival = new Arrival
            {
                Host = host, Speaker = host.CharacterObject, Kind = kind, Schemer = schemer, Agent = agent,
                Mission = Mission.Current, StartedAt = _sceneTime,
            };
            if (kind == ArrivalKind.Gratitude) arrival.Gold = GratitudeGold(host, tier);
            if (kind == ArrivalKind.Tribute) arrival.Gift = TributeGiftFor(host, tier);
            if (kind == ArrivalKind.Petition && _obligations.TryGetValue(host.StringId, out var owed))
            {
                arrival.HasAsk = true;
                arrival.Ask = owed.Ask;
                arrival.Target = FindHero(owed.TargetId);
            }
            return arrival;
        }

        /// <summary>Which notables here would come looking for you, and why.</summary>
        private List<(Hero Host, ArrivalKind Kind, Hero? Schemer)> NotableCandidates(Settlement settlement)
        {
            var notables = settlement.Notables.Where(n => n.IsAlive && n.IsNotable).ToList();
            var daysAway = DaysAway(settlement);
            var candidates = new List<(Hero Host, ArrivalKind Kind, Hero? Schemer)>();
            int tier = Clan.PlayerClan?.Tier ?? 0;

            foreach (var n in notables)
            {
                var e = NotableDisposition.Evaluate(n);

                // A friend warns you about a schemer in town.
                if (e.Genuine && e.Level >= Disposition.Friendly)
                {
                    var schemer = notables.FirstOrDefault(s => s != n && !_warnedBy.ContainsKey(s.StringId)
                                                               && Treachery.IsTreacherous(s, NotableDisposition.Evaluate(s)));
                    if (schemer != null) candidates.Add((n, ArrivalKind.Warning, schemer));
                }

                // A friend who missed you.
                if (e.Genuine && e.Level >= Disposition.Friendly && (daysAway >= FeastAwayDays || LmmiSettingsProvider.TestMode) && Ready(n, ArrivalKind.Feast))
                    candidates.Add((n, ArrivalKind.Feast, null));

                // Someone cruel who despises you.
                if (e.Level == Disposition.Contempt && n.GetTraitLevel(DefaultTraits.Mercy) <= -1 && Ready(n, ArrivalKind.WarnedOff))
                    candidates.Add((n, ArrivalKind.WarnedOff, null));

                // Someone you did a job for comes to thank you.
                if (TownStandingBehavior.OwesThanks(n) && e.Level >= Disposition.Wary)
                    candidates.Add((n, ArrivalKind.Gratitude, null));

                // A sycophant brings a small gift — rarely, and with strings attached (ArrivalSycophants.cs).
                if (e.IsSycophant && tier >= TributeMinTier && !_obligations.ContainsKey(n.StringId) && TributeReady(n))
                    candidates.Add((n, ArrivalKind.Tribute, null));

                // ...and when the debt comes due, they come to collect.
                if (_obligations.TryGetValue(n.StringId, out var owed) && CampaignTime.Now.ToHours >= owed.DueHours)
                {
                    if (ResolveAsk(n, owed)) candidates.Add((n, ArrivalKind.Petition, FindHero(owed.TargetId)));
                    else
                    {
                        _obligations.Remove(n.StringId);
                        LmmiLog.Info($"Arrival: {n.Name} no longer has anything to ask of you; the debt lapses.");
                    }
                }
            }

            return candidates;
        }

        /// <summary>
        /// The lord's hall: a lord lingering there (not you, not your clan) may come over — to snub a foreign
        /// upstart, to flatter a rising power, or, rarely, because they're genuinely glad to see you.
        /// </summary>
        private void TryLordArrival(Mission mission, Settlement settlement)
        {
            var lords = mission.Agents
                .Where(a => a.IsActive() && a.IsHuman && a != Agent.Main && a.Character is CharacterObject co && co.IsHero)
                .Select(a => (Agent: a, Hero: ((CharacterObject)a.Character).HeroObject))
                .Where(x => x.Hero != null && x.Hero.IsLord && x.Hero.IsAlive && x.Hero.Clan != Clan.PlayerClan
                            && x.Hero != x.Hero.MapFaction?.Leader
                            && x.Agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>() != null)
                .OrderBy(_ => MBRandom.RandomFloat)
                .ToList();

            foreach (var (agent, lord) in lords)
            {
                var e = NotableDisposition.Evaluate(lord);
                ArrivalKind? kind = e.Level == Disposition.Contempt ? ArrivalKind.LordSnub
                    : e.IsSycophant ? ArrivalKind.LordFlatter
                    : e.Genuine && e.Level == Disposition.Trusted ? ArrivalKind.LordRespect
                    : (ArrivalKind?)null;
                if (kind == null || !Ready(lord, kind.Value) || (!LmmiSettingsProvider.TestMode && MBRandom.RandomFloat > 0.5f)) continue;

                SetCooldown(lord, kind.Value, LordCooldownDays);
                LmmiLog.Info($"Arrival: {lord.Name} crosses the hall toward you ({kind}): {e.Describe(lord)}");
                BeginApproach(new Arrival { Host = lord, Speaker = lord.CharacterObject, Kind = kind.Value, Agent = agent, StartedAt = _sceneTime });
                return;
            }
        }

        private void BeginApproach(Arrival arrival)
        {
            _arrival = arrival;
            var agent = arrival.Agent;
            if (agent.Position.Distance(Agent.Main.Position) <= agent.GetInteractionDistanceToUsable(Agent.Main))
            {
                Agent none = null!;
                UsableMachine noMachine = null!;
                var noFrame = WorldFrame.Invalid;
                OnReachedPlayer(agent, ref none, ref noMachine, ref noFrame);
            }
            else
            {
                ScriptBehavior.AddTargetWithDelegate(agent, SelectPlayer, null, OnReachedPlayer);
            }
        }

        /// <summary>
        /// Common folk notice you too: a beggar who sees money walking, a local who hates your kind, or someone
        /// who's heard what you've done for the town. At most one, and only when no notable came.
        /// </summary>
        private bool TryTownsfolkArrival(Mission mission, Settlement settlement)
        {
            var player = Hero.MainHero;
            int tier = Clan.PlayerClan?.Tier ?? 0;
            float standing = TownStandingBehavior.Get(settlement);
            float resentment = CultureRelations.Multiplier(settlement.Culture?.StringId, player?.Culture?.StringId)
                               * LmmiSettingsProvider.ForeignerPrejudicePercent / 100f;

            var kinds = new List<(ArrivalKind Kind, float Chance)>();
            if (resentment > 0f && standing <= 0f && tier <= 3) kinds.Add((ArrivalKind.Heckler, 0.25f * resentment));
            if (standing >= 15f) kinds.Add((ArrivalKind.Admirer, 0.3f));
            if (settlement.IsTown && tier >= 2) kinds.Add((ArrivalKind.Beggar, 0.2f));
            kinds = kinds.OrderBy(_ => MBRandom.RandomFloat).ToList();

            foreach (var (kind, chance) in kinds)
            {
                string key = "town:" + settlement.StringId + ":" + kind;
                bool forced = LmmiSettingsProvider.TestMode;
                if (!forced && _readyAtHours.TryGetValue(key, out var ready) && ready > CampaignTime.Now.ToHours) continue;
                if (!forced && MBRandom.RandomFloat > chance) continue;

                var agent = mission.Agents
                    .Where(a => a.IsActive() && a.IsHuman && a != Agent.Main
                                && a.Character is CharacterObject co && !co.IsHero
                                && (co.Occupation == Occupation.Townsfolk || co.Occupation == Occupation.Villager)
                                && a.Position.Distance(Agent.Main.Position) < 25f
                                && a.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>() != null)
                    .OrderBy(_ => MBRandom.RandomFloat)
                    .FirstOrDefault();
                if (agent == null) continue;

                _readyAtHours[key] = CampaignTime.Now.ToHours + TownsfolkCooldownDays * CampaignTime.HoursInDay;
                LmmiLog.Info($"Arrival: a townsperson walks up to you in {settlement.Name} ({kind}; standing {standing:0.#}, resentment {resentment:0.##}).");
                BeginApproach(new Arrival { Host = null!, Speaker = (CharacterObject)agent.Character, Kind = kind, Agent = agent, StartedAt = _sceneTime });
                return true;
            }
            return false;
        }

        private static int Priority(ArrivalKind kind)
        {
            switch (kind)
            {
                case ArrivalKind.Warning: return 0;
                case ArrivalKind.Gratitude: return 1;
                case ArrivalKind.Petition: return 2;
                case ArrivalKind.Feast: return 3;
                case ArrivalKind.Tribute: return 4;
                default: return 5;
            }
        }

        private static int GratitudeGold(Hero host, int tier) => Math.Max(25, Math.Min(50 + 25 * tier, host.Gold / 5));

        private static bool SelectPlayer(Agent agent, ref Agent targetAgent, ref UsableMachine targetUsableMachine,
            ref WorldFrame targetFrame, ref float customTargetReachedRangeThreshold, ref float customTargetReachedRotationThreshold)
        {
            targetAgent = null!;
            if (Agent.Main != null && agent.Position.Distance(Agent.Main.Position) > agent.GetInteractionDistanceToUsable(Agent.Main))
                targetAgent = Agent.Main;
            return targetAgent != null;
        }

        private bool OnReachedPlayer(Agent agent, ref Agent targetAgent, ref UsableMachine targetUsableMachine, ref WorldFrame targetFrame)
        {
            targetAgent = null!;
            try
            {
                if (_arrival == null || _arrival.Agent != agent) return false;
                var mission = Mission.Current;
                if (mission == null || mission.Mode == MissionMode.Conversation || Agent.Main == null || !Agent.Main.IsActive())
                {
                    LmmiLog.Info($"Arrival: {_arrival.SpeakerName} reached you while you were busy; cancelled.");
                    ReleaseAgent();
                    _arrival = null;
                    return false;
                }

                _arrival.Talking = true;
                _arrival.TalkStartedAt = _sceneTime;
                // Our 1100-priority lines win before vanilla's start conditions, where meeting someone is recorded.
                if (_arrival.Host != null && !_arrival.Host.HasMet) _arrival.Host.SetHasMet();
                mission.GetMissionBehavior<MissionConversationLogic>()?.StartConversation(agent, setActionsInstantly: false);
            }
            catch (Exception ex)
            {
                LmmiLog.Error("ArrivalScenesBehavior.OnReachedPlayer threw", ex);
            }
            return false;
        }

        private void OnConversationEnded(IEnumerable<CharacterObject> characters)
        {
            if (_arrival == null || !_arrival.Talking) return;
            if (!characters.Contains(_arrival.Speaker)) return;
            ReleaseAgent();
            _arrival = null;
        }

        /// <summary>Back to their routine — vanilla does the same after its scripted walks.</summary>
        private void ReleaseAgent()
        {
            try
            {
                _arrival?.Agent?.GetComponent<CampaignAgentComponent>()?.AgentNavigator
                    ?.GetBehaviorGroup<DailyBehaviorGroup>()?.DisableScriptedBehavior();
            }
            catch (Exception ex)
            {
                LmmiLog.Error("ArrivalScenesBehavior.ReleaseAgent threw", ex);
            }
        }

        private bool Ready(Hero hero, ArrivalKind kind) =>
            LmmiSettingsProvider.TestMode
            || !_readyAtHours.TryGetValue(hero.StringId + "|" + kind, out var readyAt) || readyAt <= CampaignTime.Now.ToHours;

        private void SetCooldown(Hero hero, ArrivalKind kind, float days) =>
            _readyAtHours[hero.StringId + "|" + kind] = CampaignTime.Now.ToHours + days * CampaignTime.HoursInDay;

        private bool IsArrival(ArrivalKind kind)
        {
            PromoteMeeting();
            return _arrival != null && _arrival.Talking && _arrival.Kind == kind && _arrival.Host != null
                   && Hero.OneToOneConversationHero == _arrival.Host;
        }

        private bool IsTownsfolkArrival(ArrivalKind kind) =>
            _arrival != null && _arrival.Talking && _arrival.Kind == kind && _arrival.Host == null
            && ConversationMission.OneToOneConversationAgent == _arrival.Agent;

        // ---- Dialogs ----

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try
            {
                AddFeastDialogs(starter);
                AddWarningDialogs(starter);
                AddWarnedOffDialogs(starter);
                AddGratitudeDialogs(starter);
                AddTributeDialogs(starter);
                AddPetitionDialogs(starter);
                AddTownsfolkDialogs(starter);
                AddLordDialogs(starter);
            }
            catch (Exception ex)
            {
                LmmiLog.Error("ArrivalScenesBehavior: failed to register dialogs", ex);
            }
        }

        private void AddFeastDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_arrive_feast", "start", "lmmi_arrive_feast_resp",
                "{=lmmi_arrive_feast}{PLAYER.NAME}! How we've missed you. Come — we've prepared a small feast in your honor.",
                () => IsArrival(ArrivalKind.Feast),
                () => SetCooldown(_arrival!.Host, ArrivalKind.Feast, FeastCooldownDays),
                1100);

            starter.AddPlayerLine("lmmi_arrive_feast_accept", "lmmi_arrive_feast_resp", "lmmi_arrive_feast_done",
                "{=lmmi_arrive_feast_accept}I'd be honored.", null,
                () =>
                {
                    // Held right here, once the conversation closes; if it can't be (no scene), just the effects.
                    var host = _arrival?.Host;
                    var agent = _arrival?.Agent;
                    if (host == null) return;
                    if (agent == null || !FeastBehavior.Begin(host, agent)) ApplyFeastEffects(host);
                });
            starter.AddDialogLine("lmmi_arrive_feast_done", "lmmi_arrive_feast_done", "close_window",
                "{=lmmi_arrive_feast_done}Eat, drink! Your people are welcome at our table too.", null, null);

            starter.AddPlayerLine("lmmi_arrive_feast_decline", "lmmi_arrive_feast_resp", "lmmi_arrive_feast_declined",
                "{=lmmi_arrive_feast_decline}Another time, my friend.", null, null);
            starter.AddDialogLine("lmmi_arrive_feast_declined", "lmmi_arrive_feast_declined", "close_window",
                "{=lmmi_arrive_feast_declined}Of course. Our door is always open to you.", null, null);
        }

        /// <summary>What the feast does for you and your men (called when the evening ends).</summary>
        internal static void ApplyFeastEffects(Hero host)
        {
            try
            {
                var party = MobileParty.MainParty;
                if (host == null || party == null) return;

                party.RecentEventsMorale += 10f;

                int grain = Math.Max(5, party.MemberRoster.TotalManCount / 5);
                party.ItemRoster.AddToCounts(DefaultItems.Grain, grain);

                int healed = 0;
                foreach (var element in party.MemberRoster.GetTroopRoster())
                {
                    if (element.Character == null || element.Character.IsHero || element.WoundedNumber <= 0) continue;
                    int tended = Math.Max(1, element.WoundedNumber / 4);
                    party.MemberRoster.AddToCounts(element.Character, 0, woundedCount: -tended);
                    healed += tended;
                }
                Hero.MainHero.HitPoints = Hero.MainHero.MaxHitPoints;

                ChangeRelationAction.ApplyPlayerRelation(host, 2, affectRelatives: false);

                var msg = new TextObject("{=lmmi_arrive_feast_msg}You and your men feast in {SETTLEMENT}: morale +10, {GRAIN} grain, {HEALED} wounded tended.");
                msg.SetTextVariable("SETTLEMENT", host.CurrentSettlement?.Name ?? TextObject.GetEmpty());
                msg.SetTextVariable("GRAIN", grain);
                msg.SetTextVariable("HEALED", healed);
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
                LmmiLog.Info($"Arrival: feast with {host.Name} — morale +10, grain +{grain}, wounded tended {healed}, relation +2.");
            }
            catch (Exception ex)
            {
                LmmiLog.Error("ArrivalScenesBehavior.ApplyFeast threw", ex);
            }
        }

        private void AddWarningDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_arrive_warning", "start", "lmmi_arrive_warning_resp",
                "{=lmmi_arrive_warning}A word, before you go about your business. Don't trust {SCHEMER}. That one smiles to your face and counts your coin behind your back.",
                () =>
                {
                    if (!IsArrival(ArrivalKind.Warning) || _arrival!.Schemer == null) return false;
                    MBTextManager.SetTextVariable("SCHEMER", _arrival.Schemer.Name);
                    return true;
                },
                () =>
                {
                    _warnedBy[_arrival!.Schemer!.StringId] = _arrival.Host.StringId;
                    LmmiLog.Info($"Arrival: {_arrival.Host.Name} warned you about {_arrival.Schemer.Name}.");
                },
                1100);

            starter.AddPlayerLine("lmmi_arrive_warning_thanks", "lmmi_arrive_warning_resp", "close_window",
                "{=lmmi_arrive_warning_thanks}Thank you. I'll remember that.", null, null);
        }

        private void AddWarnedOffDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_arrive_warnoff", "start", "lmmi_arrive_warnoff_resp",
                "{=lmmi_arrive_warnoff}You. Finish your business and leave. People like you have a way of losing things around here.",
                () => IsArrival(ArrivalKind.WarnedOff),
                () => SetCooldown(_arrival!.Host, ArrivalKind.WarnedOff, WarnOffCooldownDays),
                1100);

            starter.AddPlayerLine("lmmi_arrive_warnoff_threat", "lmmi_arrive_warnoff_resp", "lmmi_arrive_warnoff_advice",
                "{=lmmi_arrive_warnoff_threat}Is that a threat?", null, null);
            starter.AddDialogLine("lmmi_arrive_warnoff_advice", "lmmi_arrive_warnoff_advice", "close_window",
                "{=lmmi_arrive_warnoff_advice}Advice. Take it or don't.", null, null);

            starter.AddPlayerLine("lmmi_arrive_warnoff_defy", "lmmi_arrive_warnoff_resp", "lmmi_arrive_warnoff_shrug",
                "{=lmmi_arrive_warnoff_defy}I'll go where I please.", null,
                () =>
                {
                    var host = _arrival?.Host;
                    if (host?.CurrentSettlement == null) return;
                    _defiedIn[host.CurrentSettlement.StringId] = host.StringId;
                    LmmiLog.Info($"Arrival: you defied {host.Name}'s warning in {host.CurrentSettlement.Name}.");
                });
            starter.AddDialogLine("lmmi_arrive_warnoff_shrug", "lmmi_arrive_warnoff_shrug", "close_window",
                "{=lmmi_arrive_warnoff_shrug}Suit yourself.", null, null);
        }

        private void AddGratitudeDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_arrive_thanks", "start", "lmmi_arrive_thanks_resp",
                "{=lmmi_arrive_thanks}{PLAYER.NAME}! You kept your word — the whole street knows it. It isn't much, but take this for your trouble. {LMMI_GIFT}{GOLD_ICON}.",
                () =>
                {
                    if (!IsArrival(ArrivalKind.Gratitude)) return false;
                    MBTextManager.SetTextVariable("LMMI_GIFT", _arrival!.Gold);
                    return true;
                },
                () => TownStandingBehavior.Thanked(_arrival!.Host),
                1100);

            starter.AddPlayerLine("lmmi_arrive_thanks_take", "lmmi_arrive_thanks_resp", "lmmi_arrive_thanks_taken",
                "{=lmmi_arrive_thanks_take}Thank you.", null,
                () => GiveGift(_arrival!.Host, _arrival.Gold, 1, "thanked you"));
            starter.AddDialogLine("lmmi_arrive_thanks_taken", "lmmi_arrive_thanks_taken", "close_window",
                "{=lmmi_arrive_thanks_taken}No, thank you. Come by any time.", null, null);

            starter.AddPlayerLine("lmmi_arrive_thanks_refuse", "lmmi_arrive_thanks_resp", "lmmi_arrive_thanks_refused",
                "{=lmmi_arrive_thanks_refuse}Keep it. I was glad to help.", null,
                () =>
                {
                    ChangeRelationAction.ApplyPlayerRelation(_arrival!.Host, 3, affectRelatives: false);
                    LmmiLog.Info($"Arrival: you waved off {_arrival.Host.Name}'s thanks (relation +3).");
                });
            starter.AddDialogLine("lmmi_arrive_thanks_refused", "lmmi_arrive_thanks_refused", "close_window",
                "{=lmmi_arrive_thanks_refused}Then I owe you one. I won't forget it.", null, null);
        }

        private void AddTownsfolkDialogs(CampaignGameStarter starter)
        {
            // ---- The beggar ----
            starter.AddDialogLine("lmmi_arrive_beggar", "start", "lmmi_arrive_beggar_resp",
                "{=lmmi_arrive_beggar}Spare a coin, {?PLAYER.GENDER}my lady{?}my lord{\\?}? Just a coin. The winter took everything we had.",
                () => IsTownsfolkArrival(ArrivalKind.Beggar), null, 1100);

            starter.AddPlayerLine("lmmi_arrive_beggar_coin", "lmmi_arrive_beggar_resp", "lmmi_arrive_beggar_thanks",
                "{=lmmi_arrive_beggar_coin}Here. [20{GOLD_ICON}]", null, () => Alms(20, 1f),
                100, (out TextObject why) => CanAfford(20, out why));
            starter.AddDialogLine("lmmi_arrive_beggar_thanks", "lmmi_arrive_beggar_thanks", "close_window",
                "{=lmmi_arrive_beggar_thanks}Bless you. Bless you.", null, null);

            starter.AddPlayerLine("lmmi_arrive_beggar_purse", "lmmi_arrive_beggar_resp", "lmmi_arrive_beggar_moved",
                "{=lmmi_arrive_beggar_purse}Take this, and eat well tonight. [100{GOLD_ICON}]", null, () => Alms(100, 2f),
                100, (out TextObject why) => CanAfford(100, out why));
            starter.AddDialogLine("lmmi_arrive_beggar_moved", "lmmi_arrive_beggar_moved", "close_window",
                "{=lmmi_arrive_beggar_moved}I... thank you. I'll tell everyone what you did. Everyone.", null, null);

            starter.AddPlayerLine("lmmi_arrive_beggar_shove", "lmmi_arrive_beggar_resp", "lmmi_arrive_beggar_shoved",
                "{=lmmi_arrive_beggar_shove}Out of my way.", null,
                () => TownStandingBehavior.Adjust(Settlement.CurrentSettlement, -1f, "shoved a beggar aside"));
            starter.AddDialogLine("lmmi_arrive_beggar_shoved", "lmmi_arrive_beggar_shoved", "close_window",
                "{=lmmi_arrive_beggar_shoved}...Yes, {?PLAYER.GENDER}my lady{?}my lord{\\?}.", null, null);

            // ---- The heckler: common folk resent your kind too ----
            starter.AddDialogLine("lmmi_arrive_heckler", "start", "lmmi_arrive_heckler_resp",
                "{=lmmi_arrive_heckler}{LMMI_HECKLE}",
                () =>
                {
                    if (!IsTownsfolkArrival(ArrivalKind.Heckler)) return false;
                    MBTextManager.SetTextVariable("LMMI_HECKLE", DisableMenuBehavior.GetForeignerInsultText());
                    return true;
                }, null, 1100);

            // Answer him — vanilla persuasion: stare him down, stand him a drink, or ask to be judged on your own deeds.
            starter.AddPlayerLine("lmmi_arrive_heckler_answer", "lmmi_arrive_heckler_resp", _answerHeckler.Entry,
                "{=lmmi_arrive_heckler_answer}[Answer him]", null,
                () =>
                {
                    var agent = _arrival?.Agent;
                    var settlement = Settlement.CurrentSettlement;
                    var listener = agent?.Character as CharacterObject;
                    float resentment = Math.Max(0f, CultureRelations.Multiplier(settlement?.Culture?.StringId, Hero.MainHero.Culture?.StringId)
                                                    * LmmiSettingsProvider.ForeignerPrejudicePercent / 100f);
                    float standing = TownStandingBehavior.Get(settlement);
                    var slur = new TextObject("{=lmmi_arrive_heckler_refuses}Keep talking, {LMMI_HECKLE_SLUR}. Everyone's watching.");
                    slur.SetTextVariable("LMMI_HECKLE_SLUR", CultureWords.Slur(Hero.MainHero.Culture));
                    _answerHeckler.Start(new[]
                        {
                            NativePersuasion.Argument(DefaultSkills.Leadership, DefaultTraits.Valor,
                                new TextObject("{=lmmi_arrive_heckler_warn}Watch your tongue."), listener, resentment, standing, (Clan.PlayerClan?.Tier ?? 0) / 3),
                            NativePersuasion.Argument(DefaultSkills.Charm, DefaultTraits.Generosity,
                                new TextObject("{=lmmi_arrive_heckler_charm}Let me buy you a drink, and you can tell me what my people ever did to you."), listener, resentment, standing),
                            NativePersuasion.Argument(DefaultSkills.Charm, DefaultTraits.Mercy,
                                new TextObject("{=lmmi_arrive_heckler_mercy}What my people did, I didn't. Judge me by what I do here."), listener, resentment, standing),
                        },
                        new TextObject("{=lmmi_arrive_heckler_opening}Well? Got something to say?"),
                        new TextObject("{=lmmi_arrive_heckler_again}Is that it?"),
                        new TextObject("{=lmmi_arrive_heckler_won_over}...Huh. Alright. Maybe you're not like the rest of them."),
                        slur,
                        onWon: () => { TownStandingBehavior.Adjust(settlement, 2f, "won over a heckler"); StreetEventsBehavior.MarkGrateful(agent); },
                        onLost: () => TownStandingBehavior.Adjust(settlement, -1f, "a heckler laughed at you"));
                });

            // Fists: beat him and the town's loudmouths keep quiet for a month — but you did beat up a local.
            starter.AddPlayerLine("lmmi_arrive_heckler_fists", "lmmi_arrive_heckler_resp", "lmmi_arrive_heckler_fight",
                "{=lmmi_arrive_heckler_fists}Say that again. To my face.", null,
                () =>
                {
                    var agent = _arrival?.Agent;
                    var settlement = Settlement.CurrentSettlement;
                    if (agent == null || settlement == null) return;
                    _afterTalk.Add(() =>
                    {
                        if (!agent.IsActive()) return;
                        StreetEventsBehavior.Fistfight(new List<Agent> { agent }, 0.03f, won =>
                        {
                            if (won)
                            {
                                TownStandingBehavior.Adjust(settlement, -1f, "beat up a heckler");
                                _readyAtHours["town:" + settlement.StringId + ":" + ArrivalKind.Heckler] =
                                    CampaignTime.Now.ToHours + 30f * CampaignTime.HoursInDay;
                                InformationManager.DisplayMessage(new InformationMessage(new TextObject(
                                    "{=lmmi_arrive_heckler_beaten}He stays down. The loudmouths of this town will think twice for a while.").ToString()));
                            }
                            else
                            {
                                TownStandingBehavior.Adjust(settlement, -2f, "lost a fight to a heckler");
                                InformationManager.DisplayMessage(new InformationMessage(new TextObject(
                                    "{=lmmi_arrive_heckler_lost}He spits on the cobbles next to you as you get up. Half the street saw.").ToString()));
                            }
                        });
                    });
                });
            starter.AddDialogLine("lmmi_arrive_heckler_fight", "lmmi_arrive_heckler_fight", "close_window",
                "{=lmmi_arrive_heckler_fight}Gladly.", null, null);

            starter.AddPlayerLine("lmmi_arrive_heckler_ignore", "lmmi_arrive_heckler_resp", "close_window",
                "{=lmmi_arrive_heckler_ignore}[Walk past without a word.]", null, null);

            _answerHeckler.Register(starter);
            _answerHeckler.AddLeave(starter, "{=lmmi_arrive_heckler_ignore}[Walk past without a word.]", "close_window");

            // ---- The admirer: word got around ----
            starter.AddDialogLine("lmmi_arrive_admirer", "start", "lmmi_arrive_admirer_resp",
                "{=lmmi_arrive_admirer}You're {PLAYER.NAME}, aren't you? {LMMI_ADMIRE}",
                () =>
                {
                    if (!IsTownsfolkArrival(ArrivalKind.Admirer)) return false;
                    var helped = Settlement.CurrentSettlement != null ? TownStandingBehavior.LastHelped(Settlement.CurrentSettlement) : null;
                    TextObject why;
                    if (helped != null)
                    {
                        why = new TextObject("{=lmmi_admire_helped}My cousin says you helped {HELPED}. Around here, that means something.");
                        why.SetTextVariable("HELPED", helped.Name);
                    }
                    else
                        why = new TextObject("{=lmmi_admire_roads}They say the roads are safer since you came through. Bless you for it.");
                    MBTextManager.SetTextVariable("LMMI_ADMIRE", why);
                    return true;
                }, null, 1100);

            starter.AddPlayerLine("lmmi_arrive_admirer_thanks", "lmmi_arrive_admirer_resp", "lmmi_arrive_admirer_bye",
                "{=lmmi_arrive_admirer_thanks}Glad to help.", null, null);
            starter.AddDialogLine("lmmi_arrive_admirer_bye", "lmmi_arrive_admirer_bye", "close_window",
                "{=lmmi_arrive_admirer_bye}Wait till I tell my wife I spoke to you!", null, null);
        }

        private void AddLordDialogs(CampaignGameStarter starter)
        {
            // ---- The snub ----
            starter.AddDialogLine("lmmi_arrive_lord_snub", "start", "lmmi_arrive_lord_snub_resp",
                "{=lmmi_arrive_lord_snub}{LMMI_SNUB}",
                () =>
                {
                    if (!IsArrival(ArrivalKind.LordSnub)) return false;
                    var lord = _arrival!.Host;
                    TextObject line = NotableDisposition.IsForeigner(lord)
                        ? new TextObject(MBRandom.RandomFloat < 0.5f
                            ? "{=lmmi_lord_snub_foreign_1}One hears the lords of {REALM} will receive anyone these days. Even your kind."
                            : "{=lmmi_lord_snub_foreign_2}Keep your distance, foreigner. This hall is for the nobility of {REALM}.")
                        : new TextObject(MBRandom.RandomFloat < 0.5f
                            ? "{=lmmi_lord_snub_nobody_1}And who might you be? No, don't tell me. I'm sure it's of no consequence."
                            : "{=lmmi_lord_snub_nobody_2}Another petitioner. The steward is over there. Do try not to track mud across the floor.");
                    line.SetTextVariable("REALM", Settlement.CurrentSettlement?.MapFaction?.Name ?? TextObject.GetEmpty());
                    MBTextManager.SetTextVariable("LMMI_SNUB", line);
                    return true;
                }, null, 1100);

            starter.AddPlayerLine("lmmi_arrive_lord_snub_back", "lmmi_arrive_lord_snub_resp", "lmmi_arrive_lord_snub_sneer",
                "{=lmmi_arrive_lord_snub_back}Mind your tongue, my lord.", null,
                () =>
                {
                    ChangeRelationAction.ApplyPlayerRelation(_arrival!.Host, -2, affectRelatives: false);
                    LmmiLog.Info($"Arrival: you answered {_arrival.Host.Name}'s snub (-2).");
                });
            starter.AddDialogLine("lmmi_arrive_lord_snub_sneer", "lmmi_arrive_lord_snub_sneer", "close_window",
                "{=lmmi_arrive_lord_snub_sneer}Or what? Do go on. The whole hall is listening.", null, null);

            starter.AddPlayerLine("lmmi_arrive_lord_snub_bow", "lmmi_arrive_lord_snub_resp", "close_window",
                "{=lmmi_arrive_lord_snub_bow}[Incline your head slightly and move on.]", null, null);

            // ---- The flatterer ----
            starter.AddDialogLine("lmmi_arrive_lord_flatter", "start", "lmmi_arrive_lord_flatter_resp",
                "{=lmmi_arrive_lord_flatter}{PLAYER.NAME}! A pleasure, a true pleasure. You simply must come hunting with my family, when the wars allow.",
                () => IsArrival(ArrivalKind.LordFlatter), null, 1100);
            starter.AddPlayerLine("lmmi_arrive_lord_flatter_yes", "lmmi_arrive_lord_flatter_resp", "lmmi_arrive_lord_flatter_bye",
                "{=lmmi_arrive_lord_flatter_yes}Perhaps I will.", null,
                () => ChangeRelationAction.ApplyPlayerRelation(_arrival!.Host, 1, affectRelatives: false));
            starter.AddDialogLine("lmmi_arrive_lord_flatter_bye", "lmmi_arrive_lord_flatter_bye", "close_window",
                "{=lmmi_arrive_lord_flatter_bye}Splendid! I shall hold you to it.", null, null);
            starter.AddPlayerLine("lmmi_arrive_lord_flatter_cool", "lmmi_arrive_lord_flatter_resp", "close_window",
                "{=lmmi_arrive_lord_flatter_cool}[Nod politely and say nothing.]", null, null);

            // ---- A genuine friend ----
            starter.AddDialogLine("lmmi_arrive_lord_respect", "start", "lmmi_arrive_lord_respect_resp",
                "{=lmmi_arrive_lord_respect}{PLAYER.NAME}. Good. There are few faces in this hall I'm glad to see.",
                () => IsArrival(ArrivalKind.LordRespect), null, 1100);
            starter.AddPlayerLine("lmmi_arrive_lord_respect_same", "lmmi_arrive_lord_respect_resp", "lmmi_arrive_lord_respect_dine",
                "{=lmmi_arrive_lord_respect_same}The feeling is mutual.", null, null);
            starter.AddDialogLine("lmmi_arrive_lord_respect_dine", "lmmi_arrive_lord_respect_dine", "lmmi_arrive_lord_dine_resp",
                "{=lmmi_arrive_lord_respect_dine}Stay and dine with us tonight. The hall could use better company than it's been getting.",
                () => !FeastBehavior.IsBusy, null);
            starter.AddPlayerLine("lmmi_arrive_lord_dine_yes", "lmmi_arrive_lord_dine_resp", "lmmi_arrive_lord_dine_done",
                "{=lmmi_arrive_lord_dine_yes}I'd be glad to.", null,
                () =>
                {
                    var host = _arrival?.Host;
                    var agent = _arrival?.Agent;
                    if (host != null && agent != null) FeastBehavior.Begin(host, agent);
                });
            starter.AddDialogLine("lmmi_arrive_lord_dine_done", "lmmi_arrive_lord_dine_done", "close_window",
                "{=lmmi_arrive_lord_dine_done}Good. Sit by me.", null, null);
            starter.AddPlayerLine("lmmi_arrive_lord_dine_no", "lmmi_arrive_lord_dine_resp", "lmmi_arrive_lord_respect_bye",
                "{=lmmi_arrive_lord_dine_no}Another night, perhaps.", null, null);
            starter.AddDialogLine("lmmi_arrive_lord_respect_bye", "lmmi_arrive_lord_respect_bye", "close_window",
                "{=lmmi_arrive_lord_respect_bye}Find me later. We'll talk properly, away from all these ears.", null, null);
        }

        private static bool CanAfford(int gold, out TextObject why)
        {
            why = TextObject.GetEmpty();
            if (Hero.MainHero.Gold >= gold) return true;
            why = new TextObject("{=lmmi_cant_afford_alms}You don't have that much on you.");
            return false;
        }

        private static void Alms(int gold, float standing)
        {
            Hero.MainHero.ChangeHeroGold(-gold);
            TownStandingBehavior.Adjust(Settlement.CurrentSettlement, standing, $"gave a beggar {gold} gold");
        }

        private static void GiveGift(Hero host, int gold, int relation, string why)
        {
            if (gold > 0) GiveGoldAction.ApplyBetweenCharacters(host, Hero.MainHero, gold);
            if (relation != 0) ChangeRelationAction.ApplyPlayerRelation(host, relation, affectRelatives: false);
            LmmiLog.Info($"Arrival: {host.Name} {why}: {gold} gold{(relation != 0 ? $", relation +{relation}" : "")}.");
        }

        // ---- Save / load ----

        public override void SyncData(IDataStore dataStore)
        {
            string data = dataStore.IsLoading ? string.Empty : BuildSaveString();
            dataStore.SyncData("lmmi_arrivals_v1", ref data);
            if (dataStore.IsLoading) LoadSaveString(data);
        }

        /// <summary>
        /// Entries separated by '|': "V;settlementId;hours", "C;heroId;kind;hours", "W;schemerId;friendId",
        /// plus obligations and investments ("O;…", "I;…", see ArrivalSycophants.cs).
        /// </summary>
        private string BuildSaveString()
        {
            double now = CampaignTime.Now.ToHours;
            var parts = new List<string>();
            parts.AddRange(_lastVisitHours.Select(kv => "V;" + kv.Key + ";" + kv.Value.ToString("R", CultureInfo.InvariantCulture)));
            parts.AddRange(_readyAtHours.Where(kv => kv.Value > now)
                .Select(kv => "C;" + kv.Key.Replace('|', ';') + ";" + kv.Value.ToString("R", CultureInfo.InvariantCulture)));
            parts.AddRange(_warnedBy.Select(kv => "W;" + kv.Key + ";" + kv.Value));
            parts.AddRange(SycophantSaveParts());
            return string.Join("|", parts);
        }

        private void LoadSaveString(string data)
        {
            _lastVisitHours = new Dictionary<string, double>();
            _previousVisitHours = new Dictionary<string, double>();
            _readyAtHours = new Dictionary<string, double>();
            _warnedBy = new Dictionary<string, string>();
            ResetSycophantState();
            if (string.IsNullOrEmpty(data)) return;

            foreach (var entry in data.Split('|'))
            {
                var p = entry.Split(';');
                if (p.Length == 3 && p[0] == "V" && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var visit))
                    _lastVisitHours[p[1]] = visit;
                else if (p.Length == 3 && p[0] == "C" && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var townReady))
                    _readyAtHours[p[1]] = townReady;   // townsfolk cooldowns: "town:settlementId:kind"
                else if (p.Length == 4 && p[0] == "C" && double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var ready))
                    _readyAtHours[p[1] + "|" + p[2]] = ready;
                else if (p.Length == 3 && p[0] == "W")
                    _warnedBy[p[1]] = p[2];
                else LoadSycophantEntry(p);
            }
        }
    }
}
