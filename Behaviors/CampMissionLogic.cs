using System;
using System.Collections.Generic;
using System.Linq;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Your camp, on the battle terrain of the place you stopped: tents in your culture's style around a few fires, your
    /// companions and off-duty men sitting and standing around them, a couple of sentries at the edge, the fires
    /// crackling and — with ten or more of you — the murmur of a crowd. They talk among themselves (barks that follow
    /// the party's morale, food and pay); you can walk among them and talk (CampBehavior's dialogs).
    /// Two little scenes borrow the men from their fires: a war story (they get up and gather round you) and a drill
    /// (they fall in, in ranks, armed, and go through the motions to a sergeant's shouting). Tab breaks camp.
    /// </summary>
    public class CampMissionLogic : MissionLogic
    {
        private const int MaxMen = 24;
        private const float FireRing = 2.3f;
        private const int CrowdAt = 10;
        private const string CrowdSound = "event:/mission/ambient/detail/tavern_exterior";   // a crowd's murmur (walla), looping, 50 m
        private const string FireSound = "event:/mission/ambient/detail/fire/fire_medium";   // looping, 25 m

        private sealed class SeatSpot
        {
            public Agent Agent = null!;
            public Vec3 At;
            public float Facing;
            public string Loop = "";
            public bool Sentry;

            // Borrowed by a scene: walk (or run) there, face that way, do that. Then back to the fire.
            public bool Staged, Arrived, Returning, Run;
            public Vec3 To;
            public float ToFacing, SentAt, ResentAt;
            public string? ToLoop;
            public EquipmentIndex GivenMain = EquipmentIndex.None, GivenShield = EquipmentIndex.None;
        }

        private enum SceneKind { None, Story, Drill }

        private readonly List<GameEntity> _props = new List<GameEntity>();
        private readonly List<SeatSpot> _seats = new List<SeatSpot>();
        private readonly List<Vec3> _fires = new List<Vec3>();
        private readonly List<SoundEvent> _sounds = new List<SoundEvent>();
        private readonly List<(float At, Action Do)> _cues = new List<(float, Action)>();
        private static readonly Dictionary<string, ActionIndexCache> Acts = new Dictionary<string, ActionIndexCache>();
        public readonly List<Agent> Men = new List<Agent>();
        public readonly List<Agent> Companions = new List<Agent>();
        private Vec3 _center;
        private float _time, _nextBarkAt = 6f, _nextPoseAt, _nextStageAt;
        private bool _built;

        // The running scene.
        private SceneKind _scene;
        private readonly List<SeatSpot> _cast = new List<SeatSpot>();
        private float _sceneTime, _gatherBy;
        private Action? _onGathered;
        private SeatSpot? _sergeant;
        private int _drillStep;
        private float _drillNextAt;
        private TextObject? _drillOutcome;

        public static CampMissionLogic? Current => Mission.Current?.GetMissionBehavior<CampMissionLogic>();

        /// <summary>A scene (story, drill) is running: the men are spoken for.</summary>
        public bool Busy => _scene != SceneKind.None;

        public override void AfterStart()
        {
            try { Build(); }
            catch (Exception ex) { LmmiLog.Error("Camp: building the camp threw", ex); }
        }

        private static ActionIndexCache Act(string name)
        {
            if (!Acts.TryGetValue(name, out var act)) Acts[name] = act = ActionIndexCache.Create(name);
            return act;
        }

        // ---- Laying out the camp ----

        private static bool Walkable(Scene scene, Vec3 at)
        {
            var record = PathFaceRecord.NullFaceRecord;
            scene.GetNavMeshFaceIndex(ref record, at, false);
            return record.IsValid();
        }

        private static Vec3 Ground(Scene scene, Vec2 at)
        {
            var v = new Vec3(at.x, at.y, 0f);
            v.z = scene.GetGroundHeightAtPosition(new Vec3(at.x, at.y, 1000f));
            return v;
        }

        /// <summary>The middle of the playable area (the scene's walk boundary), or of the scene.</summary>
        private Vec2 Middle()
        {
            try
            {
                foreach (var kv in Mission.Boundaries)
                {
                    var pts = kv.Value?.ToList();
                    if (pts == null || pts.Count < 3) continue;
                    return new Vec2(pts.Average(p => p.x), pts.Average(p => p.y));
                }
            }
            catch (Exception ex) { LmmiLog.Error("Camp: reading the boundaries threw", ex); }
            Mission.Scene.GetBoundingBox(out var min, out var max);
            return new Vec2((min.x + max.x) / 2f, (min.y + max.y) / 2f);
        }

        /// <summary>Level, walkable ground for the fires: the middle if it'll do, else the nearest that will.</summary>
        private Vec3 FindCampground(Vec2 middle)
        {
            var scene = Mission.Scene;
            for (float r = 0f; r <= 60f; r += 5f)
            {
                int steps = r <= 0f ? 1 : 12;
                for (int k = 0; k < steps; k++)
                {
                    var c2 = middle + Vec2.FromRotation(k * 0.5236f) * r;
                    var c = Ground(scene, c2);
                    if (!Walkable(scene, c)) continue;
                    bool level = true;
                    for (int j = 0; j < 8 && level; j++)
                    {
                        var ring = Ground(scene, c2 + Vec2.FromRotation(j * 0.785f) * 8f);
                        if (Math.Abs(ring.z - c.z) > 1.2f || !Walkable(scene, ring)) level = false;
                    }
                    if (level) return c;
                }
            }
            return Ground(scene, middle);
        }

        private void Build()
        {
            if (_built) return;
            _built = true;
            var mission = Mission;
            var scene = mission.Scene;
            var team = mission.PlayerTeam;
            if (team == null) { LmmiLog.Warning("Camp: no player team."); return; }

            var center = FindCampground(Middle());
            _center = center;
            var roster = MobileParty.MainParty.MemberRoster;
            var companions = roster.GetTroopRoster()
                .Where(e => e.Character != null && e.Character.IsHero && e.Character.HeroObject != Hero.MainHero
                            && e.Character.HeroObject.IsAlive && !e.Character.HeroObject.IsWounded)
                .Select(e => e.Character).Take(8).ToList();
            var troops = new List<CharacterObject>();
            foreach (var e in roster.GetTroopRoster().Where(e => e.Character != null && !e.Character.IsHero))
                for (int i = 0; i < e.Number - e.WoundedNumber; i++) troops.Add(e.Character);
            troops = troops.OrderBy(_ => MBRandom.RandomFloat).Take(MaxMen).ToList();

            // Fires: one for every ten or so, up to three.
            int fireCount = Math.Max(1, Math.Min(3, (companions.Count + troops.Count + 9) / 10));
            _fires.Add(center);
            for (int i = 1; i < fireCount; i++)
            {
                var at = Ground(scene, center.AsVec2 + Vec2.FromRotation(i * 2.094f + 0.5f) * 9f);
                _fires.Add(Walkable(scene, at) ? at : Ground(scene, center.AsVec2 + Vec2.FromRotation(i * 2.094f + 0.5f) * 5f));
            }
            for (int i = 0; i < _fires.Count; i++) Place(i == 0 ? "burning_campfire_big" : "burning_campfire", _fires[i], 0f);

            // Tents in your people's style, in a ring facing the fires.
            var tents = TentsFor(Hero.MainHero.Culture?.StringId);
            int tentCount = Math.Min(8, 3 + (troops.Count + companions.Count) / 5);
            for (int i = 0; i < tentCount; i++)
            {
                float angle = i * (6.2832f / tentCount) + 0.3f;
                var at = Ground(scene, center.AsVec2 + Vec2.FromRotation(angle) * (15f + (i % 2) * 3f));
                if (!Walkable(scene, at)) continue;
                Place(tents[i % tents.Length], at, angle + 3.1416f);
            }

            // You: at the main fire.
            var youAt = Ground(scene, center.AsVec2 + new Vec2(3.4f, 0f));
            SpawnPlayer(youAt, (center.AsVec2 - youAt.AsVec2).Normalized());

            // Around the fires: companions first (at the main fire), then the men.
            var people = companions.Select(c => (c, true)).Concat(troops.Select(t => (t, false))).ToList();
            // Two sentries at the edge, armed and facing out.
            var sentries = people.Where(p => !p.Item2).Take(2).ToList();
            foreach (var s in sentries) people.Remove(s);
            int seat = 0;
            foreach (var (character, isCompanion) in people)
            {
                int fire = Math.Min(_fires.Count - 1, seat / 9);
                int slot = seat % 9;
                if (fire == 0 && slot == 0) { seat++; slot = 1; }   // the slot where you stand
                var f = _fires[fire];
                float angle = slot * (6.2832f / 9f);
                var at = Ground(scene, f.AsVec2 + Vec2.FromRotation(angle) * FireRing);
                float facing = (f.AsVec2 - at.AsVec2).Normalized().RotationInRadians;
                bool sit = MBRandom.RandomFloat < 0.6f;
                var agent = SpawnMan(character, at, facing, civilian: true);
                seat++;
                if (agent == null) continue;
                (isCompanion ? Companions : Men).Add(agent);
                Seat(agent, at, facing, sit ? "act_conversation_sit_floor_loop"
                    : MBRandom.RandomInt(2) == 0 ? "act_conversation_hip_loop" : "act_conversation_closed_loop");
            }
            for (int i = 0; i < sentries.Count; i++)
            {
                float angle = i * 3.1416f + 1.2f;
                var at = Ground(scene, center.AsVec2 + Vec2.FromRotation(angle) * 20f);
                if (!Walkable(scene, at)) at = Ground(scene, center.AsVec2 + Vec2.FromRotation(angle) * 12f);
                var agent = SpawnMan(sentries[i].Item1, at, angle, civilian: false);
                if (agent == null) continue;
                Men.Add(agent);
                Seat(agent, at, angle, "act_conversation_closed2_loop").Sentry = true;
            }

            StartSounds(Men.Count + Companions.Count + 1);
            LmmiLog.Info($"Camp: made camp on '{mission.SceneName}' — {_fires.Count} fire(s), {_props.Count} props, "
                         + $"{Companions.Count} companion(s), {Men.Count} men, {_sounds.Count} sound(s).");
        }

        private static string[] TentsFor(string? culture) => culture switch
        {
            "aserai" => new[] { "tent_aserai_a", "tent_aserai_b", "tent_aserai_c", "tent_aserai_d" },
            "khuzait" => new[] { "khuzait_tent_a", "khuzait_tent_b" },
            "battania" => new[] { "tents_pict_a", "tents_pict_b", "tents_pict_c", "tents_pict_d" },
            "sturgia" => new[] { "sturgia_village_tent_a", "sturgia_village_tent_b" },
            _ => new[] { "tent_vlandia_a", "tent_vlandia_b", "tent_vlandia_c" },
        };

        private void Place(string prefab, Vec3 at, float yaw)
        {
            try
            {
                var rot = Mat3.Identity;
                rot.RotateAboutUp(yaw);
                var frame = new MatrixFrame(in rot, in at);
                var e = GameEntity.Instantiate(Mission.Scene, prefab, frame, true);
                if (e != null) _props.Add(e);
                else LmmiLog.Info($"Camp: no prefab '{prefab}'.");
            }
            catch (Exception ex) { LmmiLog.Error($"Camp: placing '{prefab}' threw", ex); }
        }

        /// <summary>The fires crackle; a camp of ten or more murmurs like a crowd.</summary>
        private void StartSounds(int people)
        {
            try
            {
                foreach (var f in _fires) PlayLoop(FireSound, f + new Vec3(0f, 0f, 0.4f));
                if (people >= CrowdAt) PlayLoop(CrowdSound, _center + new Vec3(0f, 0f, 1.5f));
            }
            catch (Exception ex) { LmmiLog.Error("Camp: starting the camp's sounds threw", ex); }
        }

        private void PlayLoop(string id, Vec3 at)
        {
            var sound = SoundEvent.CreateEventFromString(id, Mission.Scene);
            if (sound == null || !sound.IsValid) { LmmiLog.Info($"Camp: no sound '{id}'."); return; }
            sound.SetPosition(at);
            sound.Play();
            _sounds.Add(sound);
        }

        private void SpawnPlayer(Vec3 at, Vec2 dir)
        {
            var team = Mission.PlayerTeam;
            var player = CharacterObject.PlayerCharacter;
            var data = new AgentBuildData(player).Team(team).InitialPosition(in at).InitialDirection(in dir)
                .CivilianEquipment(false).NoHorses(true)
                .ClothingColor1(team.Color).ClothingColor2(team.Color2)
                .TroopOrigin(new PartyAgentOrigin(PartyBase.MainParty, player, -1, default(UniqueTroopDescriptor), false, false))
                .Controller(AgentControllerType.Player);
            if (Hero.MainHero.ClanBanner != null) data.Banner(Hero.MainHero.ClanBanner);
            var agent = Mission.SpawnAgent(data, false);
            LmmiLog.Info($"Camp: you're at the fire ({agent?.Name}).");
        }

        private Agent? SpawnMan(CharacterObject character, Vec3 at, float facing, bool civilian)
        {
            try
            {
                var team = Mission.PlayerTeam;
                var dir = Vec2.FromRotation(facing);
                var data = new AgentBuildData(character).Team(team).InitialPosition(in at).InitialDirection(in dir)
                    .CivilianEquipment(civilian).NoHorses(true)
                    .ClothingColor1(team.Color).ClothingColor2(team.Color2)
                    .TroopOrigin(new SimpleAgentOrigin(character, -1, null, default(UniqueTroopDescriptor)))
                    .Controller(AgentControllerType.AI);
                var agent = Mission.SpawnAgent(data, false);
                agent?.SetWatchState(Agent.WatchState.Patrolling);
                return agent;
            }
            catch (Exception ex)
            {
                LmmiLog.Error($"Camp: couldn't bring {character.Name} to the fire", ex);
                return null;
            }
        }

        // ---- Seats and stage marks ----

        private SeatSpot Seat(Agent agent, Vec3 at, float facing, string loop)
        {
            var s = new SeatSpot { Agent = agent, At = at, Facing = facing, Loop = loop };
            _seats.Add(s);
            Pose(s);
            return s;
        }

        private SeatSpot? SeatOf(Agent? agent) => agent == null ? null : _seats.FirstOrDefault(x => x.Agent == agent);

        /// <summary>Hold them where they are, facing their way, doing their loop (at the fire, or on a scene's mark).</summary>
        private static void Pose(SeatSpot s)
        {
            var a = s.Agent;
            if (!a.IsActive()) return;
            bool staged = s.Staged && s.Arrived;
            var wp = a.GetWorldPosition();
            a.SetScriptedPositionAndDirection(ref wp, staged ? s.ToFacing : s.Facing, false,
                Agent.AIScriptedFrameFlags.DoNotRun | Agent.AIScriptedFrameFlags.ConsiderRotation);
            var loop = staged ? s.ToLoop : s.Loop;
            if (string.IsNullOrEmpty(loop)) return;
            var act = Act(loop!);
            if (a.GetCurrentAction(0) != act) a.SetActionChannel(0, in act, ignorePriority: true);
        }

        /// <summary>Up from the fire and over there (a scene's mark).</summary>
        private void Send(SeatSpot s, Vec3 to, float facing, string? loop, bool run)
        {
            s.Staged = true;
            s.Arrived = false;
            s.Returning = false;
            s.To = to;
            s.ToFacing = facing;
            s.ToLoop = loop;
            s.Run = run;
            s.SentAt = s.ResentAt = _time;
            Walk(s.Agent, to, facing, run);
        }

        private void Walk(Agent a, Vec3 to, float facing, bool run)
        {
            if (!a.IsActive()) return;
            a.SetActionChannel(0, in ActionIndexCache.act_none, ignorePriority: true);
            var wp = new WorldPosition(Mission.Scene, UIntPtr.Zero, to, false);
            a.SetScriptedPositionAndDirection(ref wp, facing, false,
                (run ? Agent.AIScriptedFrameFlags.None : Agent.AIScriptedFrameFlags.DoNotRun) | Agent.AIScriptedFrameFlags.ConsiderRotation);
        }

        /// <summary>Back to their place at the fire.</summary>
        private void Release(SeatSpot s)
        {
            s.Staged = false;
            s.Arrived = false;
            s.Returning = true;
            s.SentAt = s.ResentAt = _time;
            Walk(s.Agent, s.At, s.Facing, run: false);
        }

        /// <summary>A quarter-second check: who's on their mark, who's back at the fire.</summary>
        private void UpdateMarks()
        {
            foreach (var s in _seats)
            {
                var a = s.Agent;
                if (!a.IsActive() || !(s.Staged && !s.Arrived || s.Returning)) continue;
                var target = s.Staged ? s.To : s.At;
                bool there = a.Position.AsVec2.Distance(target.AsVec2) < 0.8f || _time - s.SentAt > 14f;
                if (there)
                {
                    if (s.Staged) s.Arrived = true;
                    else s.Returning = false;
                    Pose(s);
                }
                else if (_time - s.ResentAt > 3f)
                {
                    s.ResentAt = _time;
                    Walk(a, target, s.Staged ? s.ToFacing : s.Facing, s.Staged && s.Run);
                }
            }
        }

        /// <summary>After a bout, a cheer or a conversation: back to where they sat.</summary>
        public void Resettle(Agent agent)
        {
            var s = SeatOf(agent);
            if (s == null) return;
            if (s.Staged || s.Returning || s.Agent.Position.AsVec2.Distance(s.At.AsVec2) > 1.5f) Release(s);
            else Pose(s);
        }

        // ---- Cheers and voices ----

        private void Cue(float delay, Action act) => _cues.Add((_time + delay, act));

        private static void Voice(Agent a, SkinVoiceManager.SkinVoiceType voice)
        {
            if (a.IsActive()) a.MakeVoice(voice, SkinVoiceManager.CombatVoiceNetworkPredictionType.NoPrediction);
        }

        /// <summary>Everyone near you turns to the fire and cheers — with the cry they give when a field is won.</summary>
        public void Cheer() => Cheer(_seats.Where(s => Agent.Main != null && s.Agent.IsActive() && !s.Sentry
                                                       && s.Agent.Position.Distance(Agent.Main.Position) < 15f).ToList());

        private void Cheer(List<SeatSpot> who)
        {
            foreach (var s in who)
            {
                var a = s.Agent;
                if (!a.IsActive()) continue;
                bool sitting = !s.Staged && s.Loop == "act_conversation_sit_floor_loop" || s.Staged && s.ToLoop == "act_conversation_sit_floor_loop";
                if (sitting)
                {
                    // Up on their feet for it (the next pose sits them back down).
                    var act = Act(MBRandom.RandomInt(2) == 0 ? "act_cheer_1" : "act_cheer_3");
                    a.SetActionChannel(0, in act, ignorePriority: true);
                }
                else
                {
                    // As after a battle (AgentVictoryLogic): the cheer on the upper body, weapon raised if they hold one.
                    a.SetActionChannel(0, in ActionIndexCache.act_none, ignorePriority: true);
                    var act = Act(MBRandom.RandomInt(3) == 0 ? "act_cheer_" + (1 + MBRandom.RandomInt(4))
                        : "act_cheering_high_0" + (1 + MBRandom.RandomInt(8)));
                    a.SetActionChannel(1, in act, ignorePriority: true);
                }
                // Not all at once: a ragged roar, like the end of a battle.
                if (MBRandom.RandomFloat < 0.75f) Cue(MBRandom.RandomFloat * 1.2f, () => Voice(a, SkinVoiceManager.VoiceType.Victory));
            }
            _nextPoseAt = _time + 4.5f;
        }

        // ---- The war story: they gather round ----

        /// <summary>
        /// The men nearby get up and gather round you — the listener in front, a half-circle of others, the front row
        /// sitting. <paramref name="onReady"/> once they're settled (or after a few seconds). False if it can't.
        /// </summary>
        public bool GatherRound(Agent listener, Action onReady)
        {
            var main = Agent.Main;
            var scene = Mission.Scene;
            if (Busy || main == null || !main.IsActive() || !listener.IsActive()) return false;
            var p = main.Position;
            var fwd = (listener.Position - p).AsVec2;
            fwd = fwd.LengthSquared > 0.01f ? fwd.Normalized() : main.LookDirection.AsVec2.Normalized();
            float facePlayer = (-fwd).RotationInRadians;

            _cast.Clear();
            var ls = SeatOf(listener);
            if (ls != null)
            {
                var at = Ground(scene, p.AsVec2 + fwd * 1.7f);
                Send(ls, Walkable(scene, at) ? at : listener.Position, facePlayer, "act_conversation_hip_loop", run: false);
                _cast.Add(ls);
            }
            var others = _seats.Where(s => s.Agent != listener && !s.Sentry && s.Agent.IsActive() && !s.Staged
                                           && s.Agent.Position.Distance(p) < 16f)
                .OrderBy(s => s.Agent.Position.Distance(p)).Take(9).ToList();
            for (int i = 0; i < others.Count; i++)
            {
                bool front = i < 5;
                int k = front ? i : i - 5;
                int n = front ? Math.Min(5, others.Count) : others.Count - 5;
                float spread = front ? 2.1f : 1.9f;   // radians across the half-circle
                float angle = n <= 1 ? 0f : -spread / 2f + spread * k / (n - 1);
                float radius = front ? 2.9f : 4.2f;
                var dir = fwd.TransformToParentUnitF(Vec2.FromRotation(angle));
                var at = Ground(scene, p.AsVec2 + dir * radius);
                if (!Walkable(scene, at)) at = Ground(scene, p.AsVec2 + dir * (radius - 0.7f));
                if (!Walkable(scene, at)) continue;
                float face = (p.AsVec2 - at.AsVec2).Normalized().RotationInRadians;
                Send(others[i], at, face, front ? "act_conversation_sit_floor_loop"
                    : MBRandom.RandomInt(2) == 0 ? "act_conversation_closed_loop" : "act_conversation_hip_loop", run: false);
                _cast.Add(others[i]);
            }
            _scene = SceneKind.Story;
            _sceneTime = 0f;
            _gatherBy = 7f;
            _onGathered = onReady;
            if (others.Count > 0)
                Cue(1.5f, () => StreetEventsBehavior.Bark(others[MBRandom.RandomInt(others.Count)].Agent, Flavor.Pick(
                    "{=lmmi_camp_gather_bark_1}A story! Shove over, you.",
                    "{=lmmi_camp_gather_bark_2}This one's good, I've heard it. Twice.",
                    "{=lmmi_camp_gather_bark_3}Bring your cup, it's a long one.",
                    "{=lmmi_camp_gather_bark_4}Move your feet, I can't see the captain.",
                    "{=lmmi_camp_gather_bark_5}Is this the one with the bear? Tell me it's the one with the bear.",
                    "{=lmmi_camp_gather_bark_6}Quiet! Quiet, the captain's starting.")));
            LmmiLog.Info($"Camp: {_cast.Count} gather round for a story.");
            return true;
        }

        /// <summary>The story landed (a roar, the victory cry) or it didn't (yawns, mutters); then back to the fires.</summary>
        public void StoryReaction(bool? won)
        {
            if (_scene != SceneKind.Story) return;
            var cast = _cast.Where(s => s.Agent.IsActive()).ToList();
            if (won == true) Cheer(cast);
            else if (won == false && cast.Count > 0)
            {
                foreach (var s in cast)
                {
                    bool sitting = s.ToLoop == "act_conversation_sit_floor_loop";
                    var act = Act(sitting ? "act_conversation_normal_negative_sit_floor"
                        : MBRandom.RandomInt(2) == 0 ? "act_conversation_weary_negative" : "act_conversation_closed_negative");
                    float delay = MBRandom.RandomFloat * 1.5f;
                    var a = s.Agent;
                    Cue(delay, () => { if (a.IsActive()) a.SetActionChannel(0, in act, ignorePriority: true); });
                }
                var lines = new[]
                {
                    "{=lmmi_camp_story_bored_1}*yawns*",
                    "{=lmmi_camp_story_bored_2}Was there a point to that, captain?",
                    "{=lmmi_camp_story_bored_3}I was there. That's not how it went.",
                    "{=lmmi_camp_story_bored_4}Wake me when we get to the part with the loot.",
                    "{=lmmi_camp_story_bored_5}My grandmother tells it better.",
                    "{=lmmi_camp_story_bored_6}So... who won?",
                    "{=lmmi_camp_story_bored_7}*snores*",
                    "{=lmmi_camp_story_bored_8}That's the third time the captain's mentioned the ford.",
                    "{=lmmi_camp_story_bored_9}Is it over? Can I go back to my fire?",
                    "{=lmmi_camp_story_bored_10}I've heard better stories from the mule.",
                    "{=lmmi_camp_story_bored_11}The captain's better with a sword than a tale.",
                    "{=lmmi_camp_story_bored_12}Right. Lovely. Who's got the dice?",
                }.OrderBy(_ => MBRandom.RandomFloat).Take(Math.Min(2, cast.Count)).ToList();
                for (int i = 0; i < lines.Count; i++)
                {
                    var who = cast[MBRandom.RandomInt(cast.Count)].Agent;
                    var line = new TextObject(lines[i]);
                    Cue(0.8f + i * 2.2f, () => StreetEventsBehavior.Bark(who, line));
                }
                _nextPoseAt = _time + 4.5f;
            }
            Cue(won == null ? 0.5f : 5.5f, EndScene);
        }

        // ---- The drill: ranks, arms, a sergeant's shouting ----

        /// <summary>
        /// The men fall in in ranks before you, take up their arms (their own battle kit, lent for the drill), go through
        /// the motions to the sergeant's orders, roar, and are dismissed back to their fires; then <paramref name="outcome"/>
        /// is shown. False if it can't (the effects were already applied by the dialog).
        /// </summary>
        public bool StartDrill(Agent? sergeant, TextObject outcome)
        {
            var main = Agent.Main;
            var scene = Mission.Scene;
            if (Busy || main == null || !main.IsActive()) return false;
            var drillers = _seats.Where(s => !s.Sentry && s.Agent.IsActive() && s.Agent != sergeant && Men.Contains(s.Agent)).ToList();
            if (drillers.Count == 0) return false;

            // Ranks a few paces in front of you — or wherever there's room.
            var p = main.Position.AsVec2;
            var look = main.LookDirection.AsVec2;
            look = look.LengthSquared > 0.01f ? look.Normalized() : Vec2.Forward;
            int perRow = Math.Min(8, Math.Max(4, (drillers.Count + 2) / 3));
            int rows = (drillers.Count + perRow - 1) / perRow;
            Vec2 bestDir = look;
            List<Vec3>? best = null;
            int bestOk = -1;
            foreach (float turn in new[] { 0f, 0.785f, -0.785f, 1.571f, -1.571f, 3.1416f })
            {
                var dir = look.TransformToParentUnitF(Vec2.FromRotation(turn));
                var spots = RankSpots(scene, p + dir * 6f, dir, drillers.Count, perRow);
                int ok = spots.Count(v => Walkable(scene, v));
                if (ok > bestOk) { bestOk = ok; best = spots; bestDir = dir; }
                if (ok >= drillers.Count * 0.85f) break;
            }
            if (best == null || bestOk < Math.Min(3, drillers.Count)) return false;

            float faceYou = (-bestDir).RotationInRadians;
            _cast.Clear();
            int placed = 0;
            for (int i = 0; i < drillers.Count && i < best.Count; i++)
            {
                if (!Walkable(scene, best[i])) continue;
                var s = drillers[i];
                Arm(s);
                Send(s, best[i], faceYou, null, run: true);
                _cast.Add(s);
                placed++;
            }

            // The sergeant: between you and the ranks, off to one side, facing them.
            _sergeant = SeatOf(sergeant);
            if (_sergeant != null)
            {
                var side = bestDir.LeftVec();
                var at = Ground(scene, p + bestDir * 3.2f + side * (perRow * 0.7f + 1.2f));
                if (!Walkable(scene, at)) at = Ground(scene, p + bestDir * 2.5f + side * 1.5f);
                Send(_sergeant, at, bestDir.RotationInRadians + 0.5f, null, run: true);
            }

            _scene = SceneKind.Drill;
            _sceneTime = 0f;
            _drillStep = 0;
            _drillNextAt = 9f;
            _drillOutcome = outcome;
            Shout(Flavor.Pick("{=lmmi_camp_drill_bark_fallin}Fall in! {ROWS} ranks — move, move, move!", "{=lmmi_camp_drill_bark_fallin_2}{ROWS} ranks! Shoulder to shoulder, you maggots — move!",
                "{=lmmi_camp_drill_bark_fallin_3}On your feet! Fall in, {ROWS} deep! Last man in digs the latrine!"), ("ROWS", rows));
            LmmiLog.Info($"Camp: drill — {placed} men in {rows} rank(s) of {perRow}.");
            return true;
        }

        private static List<Vec3> RankSpots(Scene scene, Vec2 origin, Vec2 dir, int count, int perRow)
        {
            var side = dir.LeftVec();
            var spots = new List<Vec3>();
            for (int i = 0; i < count; i++)
            {
                int row = i / perRow, col = i % perRow;
                int inRow = Math.Min(perRow, count - row * perRow);
                var at = origin + dir * (row * 1.7f) + side * ((col - (inRow - 1) / 2f) * 1.35f);
                spots.Add(Ground(scene, at));
            }
            return spots;
        }

        /// <summary>Their own battle kit for the drill: a melee weapon (or a bow) and a shield, in free slots.</summary>
        private static void Arm(SeatSpot s)
        {
            try
            {
                var a = s.Agent;
                var ch = a.Character as CharacterObject;
                var kit = ch?.FirstBattleEquipment ?? ch?.Equipment;
                if (kit == null) return;
                EquipmentElement main = default, ranged = default, shield = default;
                for (var i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                {
                    var el = kit[i];
                    if (el.IsEmpty) continue;
                    switch (el.Item.Type)
                    {
                        case ItemObject.ItemTypeEnum.OneHandedWeapon:
                        case ItemObject.ItemTypeEnum.TwoHandedWeapon:
                        case ItemObject.ItemTypeEnum.Polearm:
                            if (main.IsEmpty) main = el;
                            break;
                        case ItemObject.ItemTypeEnum.Bow:
                        case ItemObject.ItemTypeEnum.Crossbow:
                            if (ranged.IsEmpty) ranged = el;
                            break;
                        case ItemObject.ItemTypeEnum.Shield:
                            if (shield.IsEmpty) shield = el;
                            break;
                    }
                }
                if (main.IsEmpty) { main = ranged; shield = default; }
                if (main.IsEmpty) return;
                bool twoHanded = main.Item.Type == ItemObject.ItemTypeEnum.TwoHandedWeapon || main.Item.Type == ItemObject.ItemTypeEnum.Bow
                                 || main.Item.Type == ItemObject.ItemTypeEnum.Crossbow;
                s.GivenMain = Give(a, main);
                if (!shield.IsEmpty && !twoHanded && s.GivenMain != EquipmentIndex.None) s.GivenShield = Give(a, shield);
            }
            catch (Exception ex) { LmmiLog.Error("Camp: arming a man for the drill threw", ex); }
        }

        private static EquipmentIndex Give(Agent a, EquipmentElement el)
        {
            for (var i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                if (!a.Equipment[i].IsEmpty) continue;
                var w = new MissionWeapon(el.Item, el.ItemModifier, null);
                a.EquipWeaponWithNewEntity(i, ref w);
                return i;
            }
            return EquipmentIndex.None;
        }

        private static void Wield(SeatSpot s)
        {
            var a = s.Agent;
            if (!a.IsActive()) return;
            try
            {
                if (s.GivenShield != EquipmentIndex.None) a.TryToWieldWeaponInSlot(s.GivenShield, Agent.WeaponWieldActionType.WithAnimation, false);
                if (s.GivenMain != EquipmentIndex.None) a.TryToWieldWeaponInSlot(s.GivenMain, Agent.WeaponWieldActionType.WithAnimation, false);
            }
            catch (Exception ex) { LmmiLog.Error("Camp: wielding for the drill threw", ex); }
        }

        private static void Sheathe(SeatSpot s)
        {
            var a = s.Agent;
            if (!a.IsActive()) return;
            try
            {
                a.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.WithAnimation);
                a.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.WithAnimation);
            }
            catch (Exception ex) { LmmiLog.Error("Camp: sheathing after the drill threw", ex); }
        }

        private static void Disarm(SeatSpot s)
        {
            var a = s.Agent;
            try
            {
                if (a.IsActive())
                {
                    a.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);
                    a.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
                    if (s.GivenShield != EquipmentIndex.None && !a.Equipment[s.GivenShield].IsEmpty) a.RemoveEquippedWeapon(s.GivenShield);
                    if (s.GivenMain != EquipmentIndex.None && !a.Equipment[s.GivenMain].IsEmpty) a.RemoveEquippedWeapon(s.GivenMain);
                }
            }
            catch (Exception ex) { LmmiLog.Error("Camp: taking back the drill weapons threw", ex); }
            s.GivenMain = s.GivenShield = EquipmentIndex.None;
        }

        /// <summary>The order gesture for what they hold (vanilla's, from giving orders in battle).</summary>
        private static string Gesture(Agent a, bool follow)
        {
            var w = a.WieldedWeapon;
            var cls = w.IsEmpty || w.Item?.PrimaryWeapon == null ? WeaponClass.Undefined : w.Item.PrimaryWeapon.WeaponClass;
            switch (cls)
            {
                case WeaponClass.Dagger:
                case WeaponClass.OneHandedSword:
                case WeaponClass.OneHandedAxe:
                case WeaponClass.Mace:
                case WeaponClass.Pick:
                case WeaponClass.OneHandedPolearm:
                    return follow ? "act_command_follow" : "act_command";
                case WeaponClass.TwoHandedSword:
                case WeaponClass.TwoHandedAxe:
                case WeaponClass.TwoHandedMace:
                case WeaponClass.TwoHandedPolearm:
                case WeaponClass.LowGripPolearm:
                case WeaponClass.Crossbow:
                case WeaponClass.Javelin:
                    return follow ? "act_command_follow_2h" : "act_command_2h";
                case WeaponClass.Bow:
                    return follow ? "act_command_follow_bow" : "act_command_bow";
                default:
                    return follow ? "act_command_follow_unarmed" : "act_command_unarmed";
            }
        }

        private void Motion(bool follow, SkinVoiceManager.SkinVoiceType voice)
        {
            foreach (var s in _cast)
            {
                var a = s.Agent;
                if (!a.IsActive()) continue;
                var act = Act(Gesture(a, follow));
                float delay = MBRandom.RandomFloat * 0.35f;   // a ragged rank, not a machine
                Cue(delay, () => { if (a.IsActive()) a.SetActionChannel(1, in act, ignorePriority: true); });
                if (MBRandom.RandomFloat < 0.5f) Cue(delay + 0.15f, () => Voice(a, voice));
            }
        }

        private void Shout(string text, params (string Key, int Value)[] vars) => Shout(new TextObject(text), vars);

        private void Shout(TextObject line, params (string Key, int Value)[] vars)
        {
            foreach (var (key, value) in vars) line.SetTextVariable(key, value);
            var who = _sergeant?.Agent;
            if (who == null || !who.IsActive()) who = _cast.FirstOrDefault(s => s.Agent.IsActive())?.Agent;
            StreetEventsBehavior.Bark(who, line);
        }

        private void TickDrill()
        {
            if (_sceneTime < _drillNextAt && !(_drillStep == 0 && _cast.All(s => s.Arrived || !s.Agent.IsActive()) && _sceneTime > 2f)) return;
            switch (_drillStep++)
            {
                case 0:
                    Shout(Flavor.Pick("{=lmmi_camp_drill_bark_arms}Arms!", "{=lmmi_camp_drill_bark_arms_2}Take up arms!", "{=lmmi_camp_drill_bark_arms_3}Weapons — ready!"));
                    foreach (var s in _cast) Wield(s);
                    _drillNextAt = _sceneTime + 3.2f;
                    break;
                case 1:
                    Shout(Flavor.Pick("{=lmmi_camp_drill_bark_thrust}Forward — strike!", "{=lmmi_camp_drill_bark_thrust_2}Step and strike!", "{=lmmi_camp_drill_bark_thrust_3}At them — strike!"));
                    Motion(false, SkinVoiceManager.VoiceType.Yell);
                    _drillNextAt = _sceneTime + 3f;
                    break;
                case 2:
                    Shout(Flavor.Pick("{=lmmi_camp_drill_bark_again}Recover! Again — like you mean it!", "{=lmmi_camp_drill_bark_again_2}Back! Again! My grandmother hits harder!", "{=lmmi_camp_drill_bark_again_3}Recover! That was a tickle — again!"));
                    Motion(true, SkinVoiceManager.VoiceType.Grunt);
                    _drillNextAt = _sceneTime + 3f;
                    break;
                case 3:
                    Shout(Flavor.Pick("{=lmmi_camp_drill_bark_strike}Strike!", "{=lmmi_camp_drill_bark_strike_2}Now — strike!", "{=lmmi_camp_drill_bark_strike_3}Strike, and put your back in it!"));
                    Motion(false, SkinVoiceManager.VoiceType.Yell);
                    _drillNextAt = _sceneTime + 3f;
                    break;
                case 4:
                    Shout(Flavor.Pick("{=lmmi_camp_drill_bark_hold}Hold the line! Nobody moves until I say!", "{=lmmi_camp_drill_bark_hold_2}Hold! Hold, damn you! A wall, not a fence!", "{=lmmi_camp_drill_bark_hold_3}Steady! The first man to flinch stands watch all night!"));
                    _drillNextAt = _sceneTime + 3f;
                    break;
                case 5:
                    Shout(Flavor.Pick("{=lmmi_camp_drill_bark_cry}Now let the whole valley hear you!", "{=lmmi_camp_drill_bark_cry_2}Now shout! Make them hear you in the next town!", "{=lmmi_camp_drill_bark_cry_3}Let them hear you! Louder!"));
                    Cheer(_cast);
                    _drillNextAt = _sceneTime + 4.5f;
                    break;
                case 6:
                    Shout(Flavor.Pick("{=lmmi_camp_drill_bark_dismissed}Dismissed! Back to your fires.", "{=lmmi_camp_drill_bark_dismissed_2}Enough! Dismissed — and well done, the lot of you.", "{=lmmi_camp_drill_bark_dismissed_3}Dismissed! Go and eat before the cook gives it to the dogs."));
                    foreach (var s in _cast) Sheathe(s);
                    _drillNextAt = _sceneTime + 1.6f;
                    break;
                default:
                    var outcome = _drillOutcome;
                    EndScene();
                    if (outcome != null) InformationManager.DisplayMessage(new InformationMessage(outcome.ToString(), Colors.Green));
                    break;
            }
        }

        /// <summary>Whatever scene is running ends: weapons back, everyone back to their fire.</summary>
        public void EndScene()
        {
            if (_scene == SceneKind.None) return;
            foreach (var s in _cast) { Disarm(s); Release(s); }
            if (_sergeant != null && _sergeant.Staged) Release(_sergeant);
            LmmiLog.Info($"Camp: the {_scene.ToString().ToLowerInvariant()} is over; back to the fires.");
            _cast.Clear();
            _sergeant = null;
            _onGathered = null;
            _drillOutcome = null;
            _scene = SceneKind.None;
        }

        // ---- Every frame ----

        public override void OnMissionTick(float dt)
        {
            _time += dt;
            try
            {
                var main = Agent.Main;
                if (main == null || !main.IsActive()) return;
                bool talking = Mission.Mode == MissionMode.Conversation;
                bool fighting = Mission.GetMissionBehavior<SandBox.Missions.MissionLogics.MissionFightHandler>()?.IsThereActiveFight() == true;

                if (_cues.Count > 0)
                {
                    var due = _cues.Where(c => c.At <= _time).ToList();
                    _cues.RemoveAll(c => c.At <= _time);
                    foreach (var c in due)
                    {
                        try { c.Do(); }
                        catch (Exception ex) { LmmiLog.Error("Camp: a cue threw", ex); }
                    }
                }

                if (!talking && !fighting)
                {
                    if (_scene != SceneKind.None) _sceneTime += dt;
                    if (_time >= _nextStageAt)
                    {
                        _nextStageAt = _time + 0.25f;
                        UpdateMarks();
                    }
                    if (_scene == SceneKind.Story && _onGathered != null
                        && (_sceneTime >= _gatherBy || _cast.All(s => s.Arrived || !s.Agent.IsActive())))
                    {
                        var ready = _onGathered;
                        _onGathered = null;
                        ready();
                    }
                    if (_scene == SceneKind.Drill) TickDrill();
                }

                // Keep the poses (a conversation or a bout interrupts them; someone walking somewhere is left to it).
                if (_time >= _nextPoseAt && !talking && !fighting)
                {
                    _nextPoseAt = _time + 3f;
                    foreach (var s in _seats)
                        if (!s.Returning && (!s.Staged || s.Arrived)) Pose(s);
                }

                if (_time >= _nextBarkAt && !talking && !fighting && _scene == SceneKind.None)
                {
                    _nextBarkAt = _time + 18f + MBRandom.RandomFloat * 18f;
                    var near = Men.Concat(Companions).Where(a => a.IsActive() && a.Position.Distance(main.Position) < 12f).ToList();
                    if (near.Count > 0) StreetEventsBehavior.Bark(near[MBRandom.RandomInt(near.Count)], CampBehavior.CampLine());
                }
            }
            catch (Exception ex) { LmmiLog.Error("Camp: tick threw", ex); }
        }

        protected override void OnEndMission()
        {
            foreach (var sound in _sounds)
            {
                try { sound.Stop(); sound.Release(); } catch { }
            }
            _sounds.Clear();
            foreach (var e in _props)
            {
                try { e.Remove(75); } catch { }
            }
            _props.Clear();
            _cues.Clear();
            CampBehavior.OnCampEnded();
        }
    }
}
