using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using SandBox;
using SandBox.Missions.AgentBehaviors;
using SandBox.Missions.MissionLogics;
using SandBox.Objects.AnimationPoints;
using SandBox.Objects.Usables;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
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
    /// A friend's welcome feast, held right where you are. Accept and the screen goes dark; when it comes back a tavern
    /// table stands in the square, laid the way vanilla's own taverns lay one — stew, steak, grapes, bread, mugs —
    /// with your companions and some of your men seated among the host's people, and the host raises a cup to you.
    /// Walk among them, talk, toast the host; leave when you like (or when the night is done) and the evening fades
    /// out: they stand up, the feast's effects apply, everyone goes home.
    /// Everything is vanilla's authored work, not placed by guesswork: the table and its four chair sets are copied from
    /// the empire tavern's layout (bd_table_c + bd_chair_c_tavern_set), the chairs are real Chair machines initialised
    /// the way Mission.CreateMissionObjectFromPrefab does it, guests sit with vanilla's own chair logic (sit-down,
    /// eating/drinking loops, talking to the neighbour, stand-up), and the dishes sit where a vanilla tavern has them.
    /// Your people come in the way vanilla brings a clan member into a scene (ClanMemberRolesCampaignBehavior).
    /// </summary>
    public partial class FeastBehavior : CampaignBehaviorBase
    {
        private enum Phase { Pending, Darkening, Feasting, Ending, Lingering }   // Lingering: a hall feast going on after you're done

        private sealed class Feast
        {
            public Hero Host = null!;
            public Agent HostAgent = null!;
            public Settlement Settlement = null!;
            public Phase Phase;
            public float PhaseAt;
            public Vec3 Center;
            public readonly List<GameEntity> Props = new List<GameEntity>();
            public readonly List<GameEntity> Furniture = new List<GameEntity>();   // scripted (the chairs): removed the vanilla way
            public readonly List<Agent> Spawned = new List<Agent>();   // your people, brought in for the evening
            public readonly List<Agent> Borrowed = new List<Agent>();  // the host and locals (and your own followers), released after
            public readonly List<Agent> Yours = new List<Agent>();
            public readonly List<Agent> Theirs = new List<Agent>();
            public float NextBarkAt;
            public bool Toasted, ToastedHost, EndRequested, Applied, LeftEarly;
            public float Radius;                                        // the table area: kept clear of passers-by
            public float NextShooAt;
            public readonly Dictionary<Agent, float> Shooed = new Dictionary<Agent, float>();   // passer-by -> release time
            public readonly List<Agent> Held = new List<Agent>();      // the watch, kept off the table area until it ends
            public readonly List<Chair> Chairs = new List<Chair>();    // the chairs we brought (not a hall's own)
            public Vec2 Along = Vec2.Forward;                           // the table's long axis, toward the host's end
            public float HalfTable;                                     // half the length of the table(s), end to end
            public bool InHall;
            public readonly FeastStaff Staff = new FeastStaff();
        }

        private const float FeastSeconds = 75f;
        private const float EarlySeconds = 40f;      // leave before this and the host takes it badly
        private const float WalkAwayDistance = 20f;
        private const float HoursPass = 3.5f;
        private const float FadeOut = 0.8f;
        private const float FadeIn = 1.0f;
        private const int MaxCompanions = 4;
        private const int MaxTroops = 4;
        private const int SeatsPerTable = 8;
        private const float TableSpacing = 3.4f;   // end to end, chairs clear of each other

        // bd_table_c: ~3 m x 1.1 m, long side along its local X, top at 0.797 m.
        private const float TableTop = 0.797f;

        // Four single tavern chairs a side (vanilla's sittable "bd_chair_c", the same chair the tavern sets use), evenly
        // spaced and a hand further out than the tavern has them, so no knee or elbow ends up in the table: x along,
        // y across, yaw (each chair faces the table). Single chairs have no paired neighbour, so nothing swaps a
        // guest's eating or drinking for the "talk to the next chair" loop.
        private const float ChairOut = 1.32f;
        private static readonly (float X, float Y, float Yaw)[] ChairSpots =
        {
            (-1.125f, -ChairOut, 0f), (-0.375f, -ChairOut, 0f), (0.375f, -ChairOut, 0f), (1.125f, -ChairOut, 0f),
            (-1.125f, ChairOut, (float)Math.PI), (-0.375f, ChairOut, (float)Math.PI), (0.375f, ChairOut, (float)Math.PI), (1.125f, ChairOut, (float)Math.PI),
        };

        // Laid tables copied from vanilla scenes: prefab, x, y, yaw. A tavern table (empire_house_c_tavern_a)...
        private static readonly (string Prefab, float X, float Y, float Yaw)[] TavernSpread =
        {
            ("foods_cauldron_stew", -0.78f, 0.31f, -0.55f), ("kitchen_plate_grape", -1.12f, -0.12f, -0.47f),
            ("kitchen_mug_d", -0.31f, -0.37f, -5.94f), ("kitchen_plate_steak", 1.18f, -0.02f, -0.47f),
            ("kitchen_mug_d", 0.80f, 0.26f, -2.87f), ("kitchen_plate_steak", -0.74f, -0.37f, -3.56f),
            ("kitchen_plate_b", -0.27f, 0.08f, -5.84f), ("kitchen_bottle_a", 0.13f, 0.25f, -0.39f),
            ("kitchen_mug_d", -1.32f, 0.14f, -2.61f), ("kitchen_plate_sauce", -0.04f, -0.36f, -0.47f),
            ("foods_bread", -0.27f, 0.12f, -5.86f), ("foods_bread_piece", -0.99f, 0.22f, -0.55f),
            ("foods_cauldron_stew", 0.77f, -0.40f, -0.55f), ("kitchen_mug_d", 0.85f, -0.18f, -5.52f),
            ("foods_bread_piece", 0.50f, -0.49f, -0.55f), ("kitchen_bowl_a", 0.29f, -0.01f, -0.39f),
            ("foods_chicken_roast", 0.05f, 0.40f, 1.2f),
        };

        // ...and a heartier one (mountain_hideout_004): mutton, ham, bread, stew.
        private static readonly (string Prefab, float X, float Y, float Yaw)[] HeartySpread =
        {
            ("foods_carrots_a", -0.67f, 0.16f, 3.38f), ("kitchen_plate_b", 0.84f, -0.44f, -0.09f),
            ("foods_Cooked mutton", 0.72f, 0.40f, 0.95f), ("foods_cured_ham_leg", -0.32f, -0.34f, 0.95f),
            ("foods_Cooked mutton", -0.40f, -0.43f, 0.95f), ("kitchen_plate_a", -1.22f, -0.23f, 0.37f),
            ("foods_cured_ham_leg", -0.94f, 0.43f, 3.94f), ("foods_bread_piece", -0.14f, -0.29f, 0.95f),
            ("foods_Cooked mutton", 0.77f, -0.41f, 1.57f), ("kitchen_cutting_food_bread", -1.21f, -0.20f, 0.95f),
            ("foods_cauldron_stew", -0.16f, 0.14f, 0.95f), ("kitchen_plate_c", -0.97f, 0.41f, 0.37f),
            ("kitchen_food_bread_c_slice", -0.88f, 0.40f, 0.95f), ("kitchen_plate_c", 0.77f, 0.36f, 0.37f),
            ("foods_bread_piece", 0.83f, 0.25f, 2.02f), ("kitchen_bottle_c", 0.36f, -0.18f, 0.37f),
            ("kitchen_mug_d", 0.10f, 0.45f, 1.1f), ("kitchen_mug_d", -0.55f, 0.05f, 2.3f),
        };


        [NonSerialized] private Feast? _feast;
        [NonSerialized] private Mission? _mission;
        [NonSerialized] private float _time;

        private static FeastBehavior? Instance => Campaign.Current?.GetCampaignBehavior<FeastBehavior>();

        public static bool IsBusy => Instance?._feast != null;

        /// <summary>From the feast arrival: start once the conversation has closed. False if there's no feast to hold here.</summary>
        public static bool Begin(Hero host, Agent hostAgent)
        {
            var self = Instance;
            if (self == null || self._feast != null || Mission.Current == null || hostAgent == null || !hostAgent.IsActive()) return false;
            self._feast = new Feast
            {
                Host = host, HostAgent = hostAgent, Settlement = Settlement.CurrentSettlement ?? host.CurrentSettlement,
                Phase = Phase.Pending, PhaseAt = self._time,
            };
            LmmiLog.Info($"Feast: {host.Name} is throwing a feast for you in {self._feast.Settlement?.Name}.");
            return true;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.MissionTickEvent.AddNonSerializedListener(this, OnMissionTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, AddDialogs);
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, OnMissionEnded);
        }

        /// <summary>The scene ends with the feast still on (you tabbed out to the map, or left through a door).</summary>
        private void OnMissionEnded(IMission mission)
        {
            var f = _feast;
            if (f == null) return;
            try { Abandon(f); }
            catch (Exception ex) { LmmiLog.Error("FeastBehavior.OnMissionEnded threw", ex); }
            _feast = null;
        }

        /// <summary>
        /// You left the scene mid-feast: the food was eaten all the same — but leaving without a word, before the
        /// goodbye, is walking out on the host. Nothing is removed (the scene is going away); the music stops.
        /// </summary>
        private void Abandon(Feast f)
        {
            StopMusic(f);
            if (f.Phase == Phase.Feasting && !f.EndRequested)
            {
                LmmiLog.Info($"Feast: you left the scene mid-feast ({_time - f.PhaseAt:0}s in) without saying goodbye.");
                LeaveEarly(f, walkedOff: true);
            }
            if (f.Phase >= Phase.Feasting && !f.Applied) ApplyEffects(f);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnMissionTick(float dt)
        {
            var mission = Mission.Current;
            if (mission == null) return;
            if (mission != _mission)
            {
                if (_feast != null) Abandon(_feast);
                _mission = mission;
                _feast = null;
            }
            _time += dt;
            var f = _feast;
            if (f == null) return;

            try
            {
                switch (f.Phase)
                {
                    case Phase.Pending:
                        if (mission.Mode == MissionMode.Conversation || Campaign.Current.ConversationManager.IsConversationInProgress) return;
                        ScreenFadeController.BeginFadeOut(FadeOut);
                        Go(f, Phase.Darkening);
                        break;

                    case Phase.Darkening:
                        if (_time - f.PhaseAt < FadeOut + 0.15f) return;
                        if (!Build(f, mission))
                        {
                            Cleanup(f);
                            ApplyEffects(f);
                            InformationManager.DisplayMessage(new InformationMessage(
                                Flavor.Pick("{=lmmi_feast_indoors}There's no room in the square, so the feast is held indoors.",
                                    "{=lmmi_feast_indoors_2}The square's too crowded for a table; the feast moves indoors.",
                                    "{=lmmi_feast_indoors_3}There's nowhere to lay a table out here, so the feast is held inside.").ToString()));
                            ScreenFadeController.BeginFadeIn(FadeIn);
                            _feast = null;
                            return;
                        }
                        ScreenFadeController.BeginFadeIn(FadeIn);
                        Go(f, Phase.Feasting);
                        f.NextBarkAt = _time + FadeIn + 0.6f;
                        break;

                    case Phase.Feasting:
                        if (Agent.Main == null || !Agent.Main.IsActive()) return;
                        if (mission.Mode == MissionMode.Conversation) return;
                        if (Agent.Main.Position.Distance(f.Center) > WalkAwayDistance && !f.EndRequested)
                        {
                            // Walked off: the evening ends — and if it was too soon, the host notices.
                            if (_time - f.PhaseAt < (f.InHall ? HallEarlySeconds : EarlySeconds)) LeaveEarly(f, walkedOff: true);
                            f.EndRequested = true;
                        }
                        if (f.EndRequested || _time - f.PhaseAt > (f.InHall ? HallFeastSeconds : FeastSeconds))
                        {
                            if (f.InHall) { EndInHall(f); return; }
                            ScreenFadeController.BeginFadeOut(FadeOut);
                            Go(f, Phase.Ending);
                            return;
                        }
                        if (_time >= f.NextShooAt) Shoo(f, mission);
                        if (_time >= f.NextBarkAt) Chatter(f);
                        TickStaff(f, mission);
                        break;

                    case Phase.Lingering:
                        // A hall feast after you're done with it: everyone stays at the table until the scene ends.
                        if (mission.Mode == MissionMode.Conversation) return;
                        if (_time >= f.NextShooAt) Shoo(f, mission);
                        if (_time >= f.NextBarkAt) { Chatter(f); f.NextBarkAt += 8f; }
                        TickStaff(f, mission);
                        break;

                    case Phase.Ending:
                        if (_time - f.PhaseAt < FadeOut + 0.15f) return;
                        Cleanup(f);
                        ApplyEffects(f);
                        InformationManager.DisplayMessage(new InformationMessage(PassTime(mission).ToString()));
                        ScreenFadeController.BeginFadeIn(FadeIn);
                        _feast = null;
                        break;
                }
            }
            catch (Exception ex)
            {
                LmmiLog.Error("FeastBehavior.OnMissionTick threw", ex);
                try { Cleanup(f); if (!f.Applied) ApplyEffects(f); } catch { }
                try { ScreenFadeController.BeginFadeIn(FadeIn); } catch { }
                _feast = null;
            }
        }

        private void Go(Feast f, Phase phase)
        {
            f.Phase = phase;
            f.PhaseAt = _time;
        }

        // ---- Laying the table ----

        private bool Build(Feast f, Mission mission)
        {
            var main = Agent.Main;
            if (main == null || !main.IsActive() || !f.HostAgent.IsActive()) return false;
            if (CampaignMission.Current?.Location?.StringId == "lordshall") return BuildInHall(f, mission);
            var scene = mission.Scene;

            // Nothing else plays out around a feast.
            StreetEventsBehavior.CancelAll();
            if (!FindSpot(mission, f.HostAgent, out var center, 3.5f)) return false;

            // Who's coming: your people, and enough of the host's to fill the benches.
            var yours = YourPeople(mission);
            int tables = yours.Count >= 4 ? 2 : 1;
            if (tables == 2)
            {
                if (FindSpot(mission, f.HostAgent, out var wide, 4.8f)) center = wide;
                else
                {
                    tables = 1;   // no room for two: the first four of yours, and the host's people
                    yours = yours.Take(SeatsPerTable - 4).ToList();
                }
            }
            f.Center = center;
            int seats = tables * SeatsPerTable;
            var locals = mission.Agents
                .Where(a => IsLocalAdult(a) && !IsDirected(a) && a.Position.Distance(center) < 50f && a != f.HostAgent)
                .OrderBy(a => a.Position.Distance(center))
                .Take(Math.Max(0, seats - 1 - yours.Count)).ToList();

            // The long side runs from you (the foot) toward the host; tables end to end.
            var toHost = (f.HostAgent.Position - main.Position).AsVec2;
            var along = toHost.LengthSquared > 0.01f ? toHost.Normalized() : Vec2.Forward;
            float span = (tables - 1) * TableSpacing;
            var chairs = LayTables(f, scene, center, along, tables);
            if (chairs.Count == 0) return false;
            var tableRot = Yaw(along);

            // The seat at the host's end for the host, the one at the foot for you; the rest for yours and theirs.
            chairs = chairs.OrderByDescending(c => Vec2.DotProduct((c.GameEntity.GetGlobalFrame().origin - center).AsVec2, along)).ToList();
            // Yours: the chair beside the host's.
            var hostAt = chairs[0].GameEntity.GetGlobalFrame().origin;
            var playerChair = chairs.Skip(1).OrderBy(c => c.GameEntity.GetGlobalFrame().origin.Distance(hostAt)).First();
            chairs.Remove(playerChair);
            f.Radius = span / 2f + 3.2f;
            f.Borrowed.AddRange(locals);   // invited: not cleared away
            Evacuate(f, mission, center);
            foreach (var l in locals) f.Borrowed.Remove(l);
            if (!SitOnChair(f.HostAgent, chairs[0], f)) return false;
            f.Borrowed.Add(f.HostAgent);
            f.Theirs.Add(f.HostAgent);

            var order = new List<(Agent? Agent, CharacterObject? Character, bool Mine)>();
            int yi = 0, li = 0;
            while (yi < yours.Count || li < locals.Count)
            {
                if (yi < yours.Count) { var y = yours[yi++]; order.Add((y.Agent, y.Character, true)); }
                if (li < locals.Count) order.Add((locals[li++], null, false));
            }

            int seat = 1;
            foreach (var (existing, character, mine) in order)
            {
                if (seat >= chairs.Count) break;
                // Someone whose moves don't include this chair's eating (a guard's set has no seated eating) takes
                // the next chair along that suits them, so nobody holds a ham they can't eat.
                if (existing != null && !Fits(existing, chairs[seat]))
                {
                    int swap = Enumerable.Range(seat + 1, chairs.Count - seat - 1).FirstOrDefault(i => Fits(existing, chairs[i]));
                    if (swap > seat) (chairs[seat], chairs[swap]) = (chairs[swap], chairs[seat]);
                }
                var chair = chairs[seat];
                var agent = existing;
                if (agent == null && character != null)
                {
                    var sp = FreePoint(chair);
                    if (sp == null) { seat++; continue; }
                    var spFrame = sp.GameEntity.GetGlobalFrame();
                    agent = SpawnGuest(mission, character, spFrame.origin, spFrame.rotation.f.AsVec2.RotationInRadians);
                    if (agent == null) continue;
                    f.Spawned.Add(agent);
                }
                else if (agent != null) f.Borrowed.Add(agent);
                if (agent == null) continue;
                if (!SitOnChair(agent, chair, f)) continue;
                seat++;
                (mine ? f.Yours : f.Theirs).Add(agent);
            }

            SpawnStaff(f, mission);

            // You, in the seat kept for you (or standing at the foot if the chair won't have you).
            Redress(PointFor(playerChair, f), "act_sit_1", "");   // you'd be eating air: the player gets no prop
            if (!SitPlayer(playerChair, f))
            {
                var foot = Ground(scene, center - tableRot.s * (span / 2f + 2.6f));
                main.TeleportToPosition(foot);
                var dir = along;
                main.SetMovementDirection(in dir);
            }

            LmmiLog.Info($"Feast: {tables} table(s) laid at {center} ({chairs.Count} chairs, {f.Props.Count} props); "
                         + $"yours: {string.Join(", ", f.Yours.Select(a => a.Name))}; theirs: {string.Join(", ", f.Theirs.Select(a => a.Name))}.");
            return true;
        }

        /// <summary>
        /// Lay the tables end to end along the given axis (the host's end toward +along): table, dishes, four chairs a side.
        /// Sets the feast's table geometry; returns the chairs.
        /// </summary>
        private static List<Chair> LayTables(Feast f, Scene scene, Vec3 center, Vec2 along, int tables, int perSide = 4)
        {
            var tableRot = Yaw(along);
            float span = (tables - 1) * TableSpacing;
            var chairs = new List<Chair>();
            // On the floor at hand (a hall's floor is a mesh: a ground probe can find the cellar or a beam instead).
            Vec3 Floor(Vec3 at)
            {
                var g = Ground(scene, at);
                if (Math.Abs(g.z - center.z) > 0.4f) g.z = center.z;
                return g;
            }
            for (int t = 0; t < tables; t++)
            {
                var tableAt = center + tableRot.s * (t * TableSpacing - span / 2f);
                Vec3 Local(float x, float y, float z) => tableAt + tableRot.s * x + tableRot.f * y + new Vec3(0f, 0f, z);

                // The table, then the dishes on it (decoration: no physics, no scripts).
                var table = Place(scene, "bd_table_c", Floor(tableAt), tableRot, physics: true);
                if (table == null) continue;
                f.Props.Add(table);
                float top = Floor(tableAt).z + TableTop;
                foreach (var (prefab, x, y, yaw) in t % 2 == 0 ? TavernSpread : HeartySpread)
                {
                    var rot = tableRot;
                    rot.RotateAboutUp(yaw);
                    var at = Local(x, y, 0f);
                    at.z = top;
                    var dish = Place(scene, prefab, at, rot);
                    if (dish != null) f.Props.Add(dish);
                }

                // The chairs: real vanilla Chair machines, four a side, set to eat and drink.
                foreach (var (x, y, yaw) in ChairSpotsFor(perSide))
                {
                    var rot = tableRot;
                    rot.RotateAboutUp(yaw);
                    var chairEntity = PlaceFurniture(scene, "bd_chair_c", Floor(Local(x, y, 0f)), rot);
                    if (chairEntity == null) continue;
                    f.Furniture.Add(chairEntity);
                    chairs.AddRange(ChairsIn(chairEntity));
                }
            }
            if (chairs.Count == 0) return chairs;
            f.Along = along;
            f.HalfTable = span / 2f + 1.5f;
            f.Chairs.AddRange(chairs);
            return chairs;
        }

        /// <summary>Chairs for a side of the table: four (the full table), or three or two for the smaller layouts a tight hall gets.</summary>
        private static IEnumerable<(float X, float Y, float Yaw)> ChairSpotsFor(int perSide)
        {
            if (perSide >= 4) return ChairSpots;
            var xs = perSide == 3 ? new[] { -0.75f, 0f, 0.75f } : new[] { -0.375f, 0.375f };
            return xs.Select(x => (x, -ChairOut, 0f)).Concat(xs.Select(x => (x, ChairOut, (float)Math.PI)));
        }

        private static bool Walkable(Scene scene, Vec3 at)
        {
            var record = PathFaceRecord.NullFaceRecord;
            scene.GetNavMeshFaceIndex(ref record, at, false);
            return record.IsValid();
        }

        private static bool IsDirected(Agent a) =>
            a.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>()?.ScriptedBehavior != null;

        private static bool IsGuest(Feast f, Agent a) =>
            a == Agent.Main || f.Borrowed.Contains(a) || f.Spawned.Contains(a) || f.Staff.All.Contains(a) || f.Held.Contains(a);

        /// <summary>
        /// While it's dark: ordinary townsfolk near the table go about their business elsewhere (they're faded out —
        /// they'll be back next visit), and anyone else standing where the table goes steps out of the way.
        /// </summary>
        private static void Evacuate(Feast f, Mission mission, Vec3 center)
        {
            var scene = mission.Scene;
            int faded = 0, moved = 0;
            foreach (var a in mission.Agents.ToList())
            {
                if (!a.IsActive() || !a.IsHuman || a == Agent.Main || IsGuest(f, a)) continue;
                var off = (a.Position - center).AsVec2;
                float d = off.Length;
                bool common = a.Character is CharacterObject co && !co.IsHero
                              && (co.Occupation == Occupation.Townsfolk || co.Occupation == Occupation.Villager);
                if (common && d < f.Radius + 8f && !IsDirected(a))
                {
                    if (a.IsUsingGameObject) a.StopUsingGameObject(false);
                    a.FadeOut(true, false);
                    faded++;
                    continue;
                }
                // The watch: off their post (a guard post can sit right where the table goes) and kept out of the
                // way, looking on, until the evening is over.
                if (StreetEventsBehavior.IsWatch(a) && d < f.Radius + 3f && !IsDirected(a))
                {
                    var stage = StreetEventsBehavior.Direct(a);
                    if (stage == null) continue;
                    var away = off.LengthSquared < 0.01f ? Vec2.FromRotation(MBRandom.RandomFloat * 6.28f) : off.Normalized();
                    foreach (float r in new[] { f.Radius + 2f, f.Radius + 4f, f.Radius + 6f })
                    {
                        var spot = Ground(scene, center + new Vec3(away.x, away.y, 0f) * r);
                        if (!Walkable(scene, spot)) continue;
                        a.TeleportToPosition(spot);
                        break;
                    }
                    stage.Hold(f.HostAgent);
                    f.Held.Add(a);
                    moved++;
                    continue;
                }
                if (d > f.Radius || a.IsUsingGameObject) continue;
                var dir = off.LengthSquared < 0.01f ? Vec2.FromRotation(MBRandom.RandomFloat * 6.28f) : off.Normalized();
                var to = Ground(scene, center + new Vec3(dir.x, dir.y, 0f) * (f.Radius + 1.5f));
                if (!Walkable(scene, to)) continue;
                a.TeleportToPosition(to);
                moved++;
            }
            if (faded + moved > 0) LmmiLog.Info($"Feast: cleared the area — {faded} passer(s)-by gone about their business, {moved} stepped aside.");
        }

        /// <summary>Keep the table clear: passers-by who wander in are sent on their way (and let go again).</summary>
        private void Shoo(Feast f, Mission mission)
        {
            f.NextShooAt = _time + 1f;
            if (f.Radius <= 0f) return;
            foreach (var kv in f.Shooed.Where(kv => kv.Value <= _time).ToList())
            {
                f.Shooed.Remove(kv.Key);
                if (!kv.Key.IsActive()) continue;
                var group = kv.Key.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>();
                if (group?.ScriptedBehavior is LmmiStageBehavior) group.DisableScriptedBehavior();
            }
            foreach (var a in mission.Agents.ToList())
            {
                if (!a.IsActive() || !a.IsHuman || IsGuest(f, a) || f.Shooed.ContainsKey(a)) continue;
                var off = (a.Position - f.Center).AsVec2;
                if (off.Length > f.Radius) continue;
                if (StreetEventsBehavior.IsWatch(a))
                {
                    // A guard walking his round into the table area is waved off it, and stays off it till the end.
                    if (IsDirected(a) && !(a.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>()?.ScriptedBehavior is LmmiStageBehavior)) continue;
                    var watchStage = StreetEventsBehavior.Direct(a);
                    if (watchStage == null) continue;
                    var outward = off.LengthSquared < 0.01f ? Vec2.Forward : off.Normalized();
                    var post = a.GetWorldPosition();
                    post.SetVec2(f.Center.AsVec2 + outward * (f.Radius + 3f));
                    watchStage.GoTo(post, run: false, face: f.HostAgent);
                    f.Held.Add(a);
                    continue;
                }
                if (a.IsUsingGameObject) continue;
                var group = a.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>();
                if (group == null || (group.ScriptedBehavior != null && !(group.ScriptedBehavior is LmmiStageBehavior))) continue;
                var dir = off.LengthSquared < 0.01f ? Vec2.Forward : off.Normalized();
                var to = a.GetWorldPosition();
                to.SetVec2(f.Center.AsVec2 + dir * (f.Radius + 4f));
                var stage = group.GetBehavior<LmmiStageBehavior>() ?? group.AddBehavior<LmmiStageBehavior>();
                if (group.ScriptedBehavior != stage) group.SetScriptedBehavior<LmmiStageBehavior>();
                stage.GoTo(to, run: false);
                f.Shooed[a] = _time + 8f;
            }
        }

        /// <summary>Sit the player down the vanilla way (the same chair use you get pressing the use key on a tavern chair).</summary>
        private static bool SitPlayer(Chair chair, Feast f)
        {
            try
            {
                var main = Agent.Main;
                var point = PointFor(chair, f);
                if (main == null || point == null) return false;
                var was = main.Position;
                // A hair above the floor: a hall's floor is a mesh, and a foot placed exactly on it can slip through.
                main.TeleportToPosition(point.GameEntity.GetGlobalFrame().origin + new Vec3(0f, 0f, 0.05f));
                FacePoint(main, point);
                main.UseGameObject(point);
                for (int i = 0; i < 40 && main.CurrentlyUsedGameObject != null; i++)
                    main.CurrentlyUsedGameObject.SimulateTick(0.1f);
                if (main.IsUsingGameObject) return true;
                main.TeleportToPosition(was);   // the chair wouldn't have you: back where you stood
                return false;
            }
            catch (Exception ex)
            {
                LmmiLog.Error("Feast: couldn't seat you", ex);
                return false;
            }
        }

        /// <summary>The evening moves on in the scene itself: the sun goes down while the screen is dark.</summary>
        private static TextObject PassTime(Mission mission)
        {
            try
            {
                var scene = mission.Scene;
                float hour = (scene.TimeOfDay + HoursPass) % 24f;
                scene.TimeOfDay = hour;
                LmmiLog.Info($"Feast: the scene's clock moves on to {hour:0.0}h.");
                return hour >= 20f || hour < 5f
                    ? Flavor.Pick("{=lmmi_feast_night}The feast runs late into the night. Nobody leaves hungry.",
                        "{=lmmi_feast_night_2}The last of the wine goes round long after dark. Somebody is still singing.",
                        "{=lmmi_feast_night_3}Midnight finds the table bare and the guests asleep on the benches.")
                    : hour >= 17f
                        ? Flavor.Pick("{=lmmi_feast_evening}The feast runs on into the evening. Nobody leaves hungry.",
                            "{=lmmi_feast_evening_2}The sun goes down on empty plates and full bellies.",
                            "{=lmmi_feast_evening_3}By evening the bread's gone, the wine's going, and someone has started a song.")
                        : Flavor.Pick("{=lmmi_feast_afternoon}The feast runs long into the afternoon. Nobody leaves hungry.",
                            "{=lmmi_feast_afternoon_2}The afternoon slips away over food and talk.",
                            "{=lmmi_feast_afternoon_3}Hours pass over plates and cups. Nobody notices.");
            }
            catch (Exception ex)
            {
                LmmiLog.Error("Feast: moving the scene's clock threw", ex);
                return new TextObject("{=lmmi_feast_night}The feast runs late into the night. Nobody leaves hungry.");
            }
        }

        private void LeaveEarly(Feast f, bool walkedOff)
        {
            if (f.LeftEarly) return;
            f.LeftEarly = true;
            TaleWorlds.CampaignSystem.Actions.ChangeRelationAction.ApplyPlayerRelation(f.Host, walkedOff ? -2 : -1, affectRelatives: false);
            if (walkedOff)
            {
                var msg = Flavor.Pick("{=lmmi_feast_walked_off}You walked out of {NAME}'s feast without a word. That won't be forgotten soon.",
                    "{=lmmi_feast_walked_off_2}{NAME} watches you walk out of the feast without a goodbye. People noticed.",
                    "{=lmmi_feast_walked_off_3}Leaving {NAME}'s table without a word — the whole street saw it.");
                msg.SetTextVariable("NAME", f.Host.Name);
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
            }
            LmmiLog.Info($"Feast: you left {f.Host.Name}'s feast early ({(walkedOff ? "walked off" : "made your excuses")}).");
        }

        private static Vec3 Ground(Scene scene, Vec3 at)
        {
            at.z = scene.GetGroundHeightAtPosition(at + new Vec3(0f, 0f, 1.5f));
            return at;
        }

        private static Mat3 Yaw(Vec2 xAxis)
        {
            // Local X (s) along the given direction, Z up, Y (f) completing the frame (s = f x u).
            var x = xAxis.Normalized();
            var rot = Mat3.Identity;
            rot.s = new Vec3(x.x, x.y, 0f);
            rot.f = new Vec3(-x.y, x.x, 0f);
            rot.u = new Vec3(0f, 0f, 1f);
            return rot;
        }

        /// <summary>A decorative prop (no physics, no scripts). Anything carrying scripts is refused — an uninitialised
        /// usable machine would crash vanilla's interaction focus.</summary>
        private static GameEntity? Place(Scene scene, string prefab, Vec3 at, Mat3 rot, bool physics = false)
        {
            try
            {
                var frame = new MatrixFrame(in rot, in at);
                // Something solid (the table) is created where it stands, the way vanilla spawns solid props at run time
                // (WorkshopMissionHandler, CaravanBattleMissionHandler, Mission.CreateMissionObjectFromPrefab): its static
                // physics body is built there. Created at the origin and moved after, the body stayed behind and you
                // walked through the table. Decoration (the dishes) gets no physics at all.
                var e = physics
                    ? GameEntity.Instantiate(scene, prefab, frame, false)
                    : GameEntity.Instantiate(scene, prefab, false, false, "");
                if (e == null) return null;
                if (e.GetFirstScriptOfTypeRecursive<ScriptComponentBehavior>() != null)
                {
                    LmmiLog.Info($"Feast: '{prefab}' carries scripts — not placed.");
                    e.Remove(88);
                    return null;
                }
                if (physics) LmmiLog.Info($"Feast: '{prefab}' placed solid (physics body: {e.HasPhysicsBody()}, flags {e.BodyFlag}).");
                else e.SetGlobalFrame(in frame);
                return e;
            }
            catch (Exception ex)
            {
                LmmiLog.Error($"Feast: couldn't place '{prefab}'", ex);
                return null;
            }
        }

        /// <summary>Scripted furniture (chairs), set up exactly as Mission.CreateMissionObjectFromPrefab does it — sit
        /// points configured before their scripts initialise, then registered with the mission.</summary>
        private static GameEntity? PlaceFurniture(Scene scene, string prefab, Vec3 at, Mat3 rot)
        {
            try
            {
                var frame = new MatrixFrame(in rot, in at);
                var e = GameEntity.Instantiate(scene, prefab, frame, false);
                if (e == null) return null;
                // One fixed thing per chair (vanilla's random pick would leave half the table empty-handed): a few tuck
                // into a ham leg (vanilla's own pairing: sp_npc_eating_alone), most drink (the seated tavern drinking
                // loop, with the mug vanilla shapes for a drinking hand), and some just sit. The pair loop is the same
                // loop, so a neighbour never turns it into "talking to the next chair" with a mug in hand.
                float roll = MBRandom.RandomFloat;
                var (loop, item) = roll < 0.3f ? (EatLoop, EatItem) : roll < 0.8f ? (DrinkLoop, DrinkItem) : (SitLoop, "");
                foreach (var point in Scripts<AnimationPoint>(e)) Dress(point, loop, item);
                e.CallScriptCallbacks(true);
                return e;
            }
            catch (Exception ex)
            {
                LmmiLog.Error($"Feast: couldn't place '{prefab}'", ex);
                return null;
            }
        }

        private static IEnumerable<T> Scripts<T>(GameEntity e) where T : ScriptComponentBehavior
        {
            foreach (var sc in e.GetScriptComponents())
                if (sc is T t) yield return t;
            foreach (var child in e.GetChildren())
                foreach (var t in Scripts<T>(child))
                    yield return t;
        }

        private static IEnumerable<Chair> ChairsIn(GameEntity set) => Scripts<Chair>(set);

        private static StandingPoint? FreePoint(Chair chair) =>
            chair.StandingPoints?
                .Where(p => !p.HasUser && !p.IsDeactivated)
                .OrderBy(p => p.GameEntity.HasTag("alternative") ? 1 : 0)
                .FirstOrDefault();

        private const string EatLoop = "act_sit_eating_1", EatItem = "foods_cured_ham_leg";
        private const string DrinkLoop = "act_sit_and_drink_idle", DrinkItem = "kitchen_mug_a_drink_anim";
        private const string SitLoop = "act_sit_1";

        private static void Dress(AnimationPoint point, string loop, string item)
        {
            if (point is ChairUsePoint chairPoint)
            {
                // The chair's own random eat/drink/lean pick stays out of it: "None" always plays what we set.
                chairPoint.Eat = false;
                chairPoint.Drink = false;
                chairPoint.NearTable = false;
            }
            point.LoopStartAction = loop;
            if (loop != SitLoop) point.PairLoopStartAction = loop;
            point.RightHandItem = item;
            point.LeftHandItem = "";
        }

        // AnimationPoint reads its actions and props once, at init; re-reading them for a chair already in play
        // (to suit who sits there) goes through its own SetActionCodes, after clearing the props it had queued.
        private static readonly FieldInfo? ItemsForBones = typeof(AnimationPoint).GetField("_itemsForBones", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo? SelectedRight = typeof(AnimationPoint).GetProperty("SelectedRightHandItem", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo? SelectedLeft = typeof(AnimationPoint).GetProperty("SelectedLeftHandItem", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo? SetActionCodesMethod = typeof(AnimationPoint).GetMethod("SetActionCodes", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Change what an (unused) chair point does. False if this game version won't let us.</summary>
        private static bool Redress(StandingPoint? standingPoint, string loop, string item)
        {
            if (!(standingPoint is AnimationPoint point) || point.HasUser) return false;
            try
            {
                if (ItemsForBones == null || SelectedRight == null || SelectedLeft == null || SetActionCodesMethod == null) return false;
                Dress(point, loop, item);
                (ItemsForBones.GetValue(point) as System.Collections.IList)?.Clear();
                SelectedRight.SetValue(point, "");
                SelectedLeft.SetValue(point, "");
                SetActionCodesMethod.Invoke(point, null);
                return true;
            }
            catch (Exception ex)
            {
                LmmiLog.Error("Feast: re-dressing a chair threw", ex);
                return false;
            }
        }

        private static bool HasAction(Agent agent, string action)
        {
            if (string.IsNullOrEmpty(action)) return true;
            var code = ActionIndexCache.Create(action);
            return MBActionSet.CheckActionAnimationClipExists(agent.ActionSet, in code);
        }

        /// <summary>Can this agent play what the chair's set to (a guard's moves have no seated eating)?</summary>
        private static bool Fits(Agent agent, Chair chair)
        {
            var p = chair.StandingPoints?.OfType<AnimationPoint>().FirstOrDefault();
            return p == null || HasAction(agent, p.LoopStartAction);
        }

        /// <summary>
        /// Where to stand to sit down. A hall's chairs: the first free point, as before. Ours: the free point clearest of
        /// the table and the neighbouring chairs (behind the chair in the middle of a side, beside it at the ends), so
        /// nobody starts - or ends - their sit-down inside the table or the next chair.
        /// </summary>
        private static StandingPoint? PointFor(Chair chair, Feast f)
        {
            if (!f.Chairs.Contains(chair)) return FreePoint(chair);
            var side = new Vec2(-f.Along.y, f.Along.x);
            return chair.StandingPoints?
                .Where(p => !p.HasUser && !p.IsDeactivated)
                .OrderByDescending(p =>
                {
                    var at = p.GameEntity.GetGlobalFrame().origin.AsVec2;
                    float near = f.Chairs.Where(c => c != chair)
                        .Select(c => c.GameEntity.GetGlobalFrame().origin.AsVec2.Distance(at))
                        .DefaultIfEmpty(9f).Min();
                    var local = at - f.Center.AsVec2;
                    float x = Vec2.DotProduct(local, f.Along), y = Vec2.DotProduct(local, side);
                    bool overTable = Math.Abs(x) < f.HalfTable + 0.3f && Math.Abs(y) < 0.95f;
                    return Math.Min(near, 1.5f) - (overTable ? 5f : 0f);
                })
                .FirstOrDefault();
        }

        private static void FacePoint(Agent agent, StandingPoint point)
        {
            var dir = point.GameEntity.GetGlobalFrame().rotation.f.AsVec2;
            if (dir.LengthSquared < 0.0001f) return;
            dir = dir.Normalized();
            agent.SetMovementDirection(in dir);
            agent.LookDirection = new Vec3(dir.x, dir.y, 0f);
        }

        /// <summary>Sit someone on a vanilla chair: at the sit point, facing as it wants, using it, fast-forwarded (as
        /// vanilla simulates agents at spawn) so they're already seated, with their cup or ham, when the lights come up.</summary>
        private static bool SitOnChair(Agent agent, Chair chair, Feast f)
        {
            try
            {
                var point = PointFor(chair, f);
                if (point == null) return false;
                // Our chairs: one of three things per guest, as their moves allow (eating, else drinking, else sitting).
                if (f.Chairs.Contains(chair))
                {
                    float roll = MBRandom.RandomFloat;
                    var (loop, item) = roll < 0.3f ? (EatLoop, EatItem) : roll < 0.8f ? (DrinkLoop, DrinkItem) : (SitLoop, "");
                    if (loop == EatLoop && !HasAction(agent, EatLoop)) (loop, item) = (DrinkLoop, DrinkItem);
                    if (loop == DrinkLoop && !HasAction(agent, DrinkLoop)) (loop, item) = (SitLoop, "");
                    Redress(point, loop, item);
                }
                var group = agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>();
                if (group == null) return false;
                if (agent.IsUsingGameObject) agent.StopUsingGameObject(false);
                var stage = group.GetBehavior<LmmiStageBehavior>() ?? group.AddBehavior<LmmiStageBehavior>();
                if (group.ScriptedBehavior != stage) group.SetScriptedBehavior<LmmiStageBehavior>();
                stage.Occupy();

                agent.TeleportToPosition(point.GameEntity.GetGlobalFrame().origin + new Vec3(0f, 0f, 0.05f));
                // Facing the way the point wants: the chair's arrive check needs it, or the fast-forward never starts
                // the sit-down and they'd do it in plain view.
                FacePoint(agent, point);
                agent.UseGameObject(point);
                for (int i = 0; i < 50 && agent.CurrentlyUsedGameObject != null; i++)
                    agent.CurrentlyUsedGameObject.SimulateTick(0.1f);
                return agent.IsUsingGameObject;
            }
            catch (Exception ex)
            {
                LmmiLog.Error($"Feast: couldn't seat {agent.Name}", ex);
                return false;
            }
        }

        /// <summary>Open, level ground near the host: nothing solid within 3.5 m (else 2.6 m) at waist height, no step over 35 cm.</summary>
        private static bool FindSpot(Mission mission, Agent host, out Vec3 spot, float wanted)
        {
            var scene = mission.Scene;
            var candidates = new List<Vec3> { host.Position, Agent.Main.Position };
            candidates.AddRange(mission.Agents.Where(a => a.IsActive() && a.IsHuman && a.Position.Distance(host.Position) < 35f)
                .Select(a => a.Position));
            foreach (float radius in wanted > 4f ? new[] { wanted } : new[] { wanted, 2.8f })
            foreach (var c in candidates.OrderBy(p => p.Distance(host.Position)))
            {
                float g = scene.GetGroundHeightAtPosition(c);
                var center = new Vec3(c.x, c.y, g);
                bool clear = true;
                for (int k = 0; k < 12 && clear; k++)
                {
                    var dir = Vec2.FromRotation(k * 0.5236f);
                    var ring = center.AsVec2 + dir * radius;
                    float h = scene.GetGroundHeightAtPosition(new Vec3(ring.x, ring.y, g + 2f));
                    if (Math.Abs(h - g) > 0.35f) { clear = false; break; }
                    var from = new Vec3(center.x, center.y, g + 0.9f);
                    var to = new Vec3(ring.x, ring.y, g + 0.9f);
                    if (scene.RayCastForClosestEntityOrTerrain(from, to, out float _, 0.05f)) clear = false;
                    // People walk here: on the navmesh (village crop fields and gardens aren't).
                    if (clear && !Walkable(scene, new Vec3(ring.x, ring.y, h))) clear = false;
                    var mid = center.AsVec2 + dir * (radius * 0.5f);
                    if (clear && !Walkable(scene, new Vec3(mid.x, mid.y, g))) clear = false;
                }
                if (clear && !Walkable(scene, center)) clear = false;
                // Not on top of a guard post: no watchman within the table area and a few steps beyond.
                if (clear && mission.Agents.Any(a => a.IsActive() && a.IsHuman && StreetEventsBehavior.IsWatch(a)
                                                     && a.Position.AsVec2.Distance(center.AsVec2) < radius + 3f))
                    clear = false;
                if (!clear) continue;
                spot = center;
                LmmiLog.Info($"Feast: clear ground found ({radius} m around).");
                return true;
            }
            spot = Vec3.Zero;
            return false;
        }

        // ---- The guests ----

        private static bool IsLocalAdult(Agent a) =>
            a.IsActive() && a.IsHuman && a != Agent.Main && !a.IsUsingGameObject && a.Age >= 18f
            && a.Character is CharacterObject co && !co.IsHero
            && (co.Occupation == Occupation.Townsfolk || co.Occupation == Occupation.Villager)
            && co != co.Culture?.Beggar && co != co.Culture?.FemaleBeggar
            && a.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>() != null;

        /// <summary>Your companions (already following you, or brought in) and a few of your best men.</summary>
        private static List<(Agent? Agent, CharacterObject Character)> YourPeople(Mission mission)
        {
            var list = new List<(Agent?, CharacterObject)>();
            var roster = MobileParty.MainParty?.MemberRoster;
            if (roster == null) return list;
            LmmiLog.Info($"Feast: your party — {roster.TotalManCount} men ({roster.TotalWounded} wounded), heroes: "
                         + string.Join(", ", roster.GetTroopRoster().Where(e => e.Character?.IsHero == true).Select(e => e.Character.Name)));
            foreach (var e in roster.GetTroopRoster().Where(e => e.Character != null && e.Character.IsHero
                         && e.Character.HeroObject != Hero.MainHero && e.Character.HeroObject.IsAlive && !e.Character.HeroObject.IsWounded)
                         .Take(MaxCompanions))
            {
                var here = mission.Agents.FirstOrDefault(a => a.IsActive() && a.Character == e.Character);
                list.Add((here, e.Character));
            }
            int troops = 0;
            foreach (var e in roster.GetTroopRoster().Where(e => e.Character != null && !e.Character.IsHero && e.Number - e.WoundedNumber > 0)
                         .OrderByDescending(e => e.Character.Tier))
            {
                for (int i = 0; i < e.Number - e.WoundedNumber && troops < MaxTroops; i++, troops++)
                    list.Add((null, e.Character));
                if (troops >= MaxTroops) break;
            }
            return list;
        }

        /// <summary>Brought into the scene the way vanilla brings in a clan member who follows you (ClanMemberRolesCampaignBehavior).</summary>
        private static Agent? SpawnGuest(Mission mission, CharacterObject character, Vec3 at, float facing)
        {
            try
            {
                var handler = mission.GetMissionBehavior<MissionAgentHandler>();
                if (handler == null) return null;
                IAgentOriginBase origin = character.IsHero
                    ? new PartyAgentOrigin(PartyBase.MainParty, character, -1, default(UniqueTroopDescriptor), false, false)
                    : new SimpleAgentOrigin(character, -1, null, default(UniqueTroopDescriptor));
                var data = new AgentData(origin).Monster(TaleWorlds.Core.FaceGen.GetBaseMonsterFromRace(character.Race)).NoHorses(true);
                var lc = new LocationCharacter(data, SandBoxManager.Instance.AgentBehaviorManager.AddFixedCharacterBehaviors,
                    null, true, LocationCharacter.CharacterRelations.Friendly, null, character.IsHero, false, null, false, false, true, null, false);
                var dir = Vec2.FromRotation(facing);
                var rot = Mat3.Identity;
                rot.f = new Vec3(dir.x, dir.y, 0f);
                rot.u = new Vec3(0f, 0f, 1f);
                rot.s = Vec3.CrossProduct(rot.f, rot.u);
                var frame = new MatrixFrame(in rot, in at);
                return handler.SpawnWanderingAgentWithInitialFrame(lc, frame, WeakGameEntity.Invalid, true, false);
            }
            catch (Exception ex)
            {
                LmmiLog.Error($"Feast: couldn't bring {character.Name} to the table", ex);
                return null;
            }
        }

        // ---- The evening ----

        private void Chatter(Feast f)
        {
            f.NextBarkAt = _time + 6f + MBRandom.RandomFloat * 4f;
            if (!f.Toasted)
            {
                f.Toasted = true;
                var toast = Flavor.Pick("{=lmmi_feast_toast}To {PLAYER.NAME}! May your road be short, your purse heavy, and your cup never empty!",
                    "{=lmmi_feast_toast_2}Friends! Raise your cups to {PLAYER.NAME} — the finest company this table has seen in a year!",
                    "{=lmmi_feast_toast_3}To {PLAYER.NAME}, home from the road! May the next one be kinder!",
                    "{=lmmi_feast_toast_4}A toast! To {PLAYER.NAME}, and to all who ride with {?PLAYER.GENDER}her{?}him{\\?}!");
                Bark(f.HostAgent, toast);
                return;
            }
            bool mine = MBRandom.RandomFloat < 0.5f && f.Yours.Any(a => a.IsActive());
            var pool = (mine ? f.Yours : f.Theirs).Where(a => a.IsActive()).ToList();
            if (pool.Count == 0) return;
            var who = pool[MBRandom.RandomInt(pool.Count)];
            string[] lines = mine
                ? new[]
                {
                    "{=lmmi_feast_yours_1}Better than camp stew, eh?",
                    "{=lmmi_feast_yours_2}To {PLAYER.NAME}!",
                    "{=lmmi_feast_yours_3}Pass the bread, friend. No — the whole loaf.",
                    "{=lmmi_feast_yours_4}I could get used to this.",
                    "{=lmmi_feast_yours_5}Don't tell the others we ate this well.",
                    "{=lmmi_feast_yours_6}Is there more of that ham, or did the sergeant eat it all?",
                    "{=lmmi_feast_yours_7}Mind the captain's cup — keep it full!",
                    "{=lmmi_feast_yours_8}Real chairs. I'd forgotten what they feel like.",
                    "{=lmmi_feast_yours_9}A man could sleep a week after a meal like this.",
                    "{=lmmi_feast_yours_10}Somebody save me a heel of bread for the road.",
                    "{=lmmi_feast_yours_11}No marching tomorrow, eh, captain? Eh?",
                    "{=lmmi_feast_yours_12}This wine's better than anything we've had in a month.",
                    "{=lmmi_feast_yours_13}If I die tomorrow, I die full.",
                    "{=lmmi_feast_yours_14}Here's to whoever's paying for all this!",
                    "{=lmmi_feast_yours_15}Careful — the captain's watching how much you drink.",
                }
                : new[]
                {
                    "{=lmmi_feast_theirs_1}Eat, eat! There's plenty.",
                    "{=lmmi_feast_theirs_2}Another round for our guests!",
                    "{=lmmi_feast_theirs_3}Try the ham — my own smokehouse.",
                    "{=lmmi_feast_theirs_4}So, is it true what they say about you?",
                    "{=lmmi_feast_theirs_5}A good harvest, good company. What more is there?",
                    "{=lmmi_feast_theirs_6}Have some more — you're all skin and bone, the lot of you!",
                    "{=lmmi_feast_theirs_7}Our guests! Fill those cups!",
                    "{=lmmi_feast_theirs_8}This town knows how to treat its friends.",
                    "{=lmmi_feast_theirs_9}My grandmother's recipe, that stew. Don't ask what's in it.",
                    "{=lmmi_feast_theirs_10}Sing, someone! It's too quiet.",
                    "{=lmmi_feast_theirs_11}To good harvests and short wars!",
                    "{=lmmi_feast_theirs_12}More bread down this end!",
                    "{=lmmi_feast_theirs_13}Tell us about the fighting! Was it as bad as they say?",
                    "{=lmmi_feast_theirs_14}You'll stay the night, surely? Nobody rides out after a feast like this.",
                    "{=lmmi_feast_theirs_15}Mind the wine — it's stronger than it looks.",
                };
            Bark(who, new TextObject(lines[MBRandom.RandomInt(lines.Length)]));
        }

        private static readonly string[] CompanionLines =
        {
            "{=lmmi_feast_comp_1}Now this is living, {PLAYER.NAME}. We should make friends in every town.",
            "{=lmmi_feast_comp_2}A roof, a fire, and nobody trying to kill us. I'd almost forgotten.",
            "{=lmmi_feast_comp_3}Don't look now, but {HOST} has been eyeing that ham like it owes them money.",
            "{=lmmi_feast_comp_4}To you, captain. May the road be kinder tomorrow.",
            "{=lmmi_feast_comp_5}{HOST} keeps a good table. Stay in their favor, captain — it's worth more than gold.",
            "{=lmmi_feast_comp_6}I've eaten worse at a king's table. Well — at a king's kitchen door.",
            "{=lmmi_feast_comp_7}Enjoy it while it lasts. Tomorrow it's hard biscuit again.",
            "{=lmmi_feast_comp_8}Look at the lads. First time in a month I've seen them smile.",
            "{=lmmi_feast_comp_9}You know, I could settle somewhere like {SETTLEMENT}. For a week. Maybe two.",
            "{=lmmi_feast_comp_10}Don't drink too much, captain. Somebody has to find the road tomorrow.",
            "{=lmmi_feast_comp_11}A toast — to whoever's paying, and to you for knowing them.",
            "{=lmmi_feast_comp_12}This is what we fight for, isn't it? Nights like this.",
        };
        private static readonly string[] NotableLines =
        {
            "{=lmmi_feast_notable_1}{HOST} knows how to keep a table. Mark it — that one's a friend worth having.",
            "{=lmmi_feast_notable_2}Good company, good food. {SETTLEMENT} hasn't seen a night like this in a while.",
            "{=lmmi_feast_notable_3}Business can wait until tomorrow. Tonight, we drink.",
            "{=lmmi_feast_notable_4}You've done well to make a friend of {HOST}. Not many manage it.",
            "{=lmmi_feast_notable_5}Eat! We'll talk business when the plates are empty.",
            "{=lmmi_feast_notable_6}{SETTLEMENT} looks after its friends. Remember that, out on the road.",
            "{=lmmi_feast_notable_7}If you need anything while you're in town, come to me. Tonight we're all friends.",
            "{=lmmi_feast_notable_8}I haven't seen {HOST} this cheerful since the last harvest fair.",
            "{=lmmi_feast_notable_9}A good name is worth a dozen good swords. Yours is growing in {SETTLEMENT}.",
        };
        private static readonly string[] SoldierLines =
        {
            "{=lmmi_feast_soldier_1}Better than camp stew, eh?",
            "{=lmmi_feast_soldier_2}If the lads back at camp could see us now...",
            "{=lmmi_feast_soldier_3}To the captain!",
            "{=lmmi_feast_soldier_4}I'll march twice as far tomorrow on this, I swear it.",
            "{=lmmi_feast_soldier_5}Captain! Best night we've had since the last town.",
            "{=lmmi_feast_soldier_6}My mother cooked like this. Before the war took the farm.",
            "{=lmmi_feast_soldier_7}Try the ham, captain. I've had four slices. Don't tell the sergeant.",
            "{=lmmi_feast_soldier_8}I'd follow you anywhere, captain. Especially to another one of these.",
            "{=lmmi_feast_soldier_9}Is it true we've a few days' rest? Please say it's true.",
            "{=lmmi_feast_soldier_10}To the captain, and to full bellies!",
            "{=lmmi_feast_soldier_11}They're friendlier here than the last place. Nobody's thrown anything yet.",
            "{=lmmi_feast_soldier_12}I'll be sick tomorrow, captain, but it'll be worth it.",
        };
        private static readonly string[] LocalLines =
        {
            "{=lmmi_feast_local_1}To your health, {?PLAYER.GENDER}my lady{?}my lord{\\?}!",
            "{=lmmi_feast_local_2}Eat, eat! There's plenty.",
            "{=lmmi_feast_local_3}Best stew in {SETTLEMENT}, I swear it. My cousin made it.",
            "{=lmmi_feast_local_4}Another round for our guest!",
            "{=lmmi_feast_local_5}They say you've seen half of Calradia. Is it true what they say about the north?",
            "{=lmmi_feast_local_6}Welcome to {SETTLEMENT}! Have you tried the cheese?",
            "{=lmmi_feast_local_7}My little one wants to know if you've ever killed a dragon. I said probably.",
            "{=lmmi_feast_local_8}We don't see many great folk at our table. Eat, eat!",
            "{=lmmi_feast_local_9}Is it true you've been to the capital? What's it like?",
            "{=lmmi_feast_local_10}Here's to your health — and to our host's purse, for paying for all this!",
            "{=lmmi_feast_local_11}Watch the wine, {?PLAYER.GENDER}my lady{?}my lord{\\?}. It sneaks up on you.",
            "{=lmmi_feast_local_12}Will you stay long in {SETTLEMENT}? We'd be glad of it.",
            "{=lmmi_feast_local_13}My brother says he saw you fight once. He hasn't stopped talking about it.",
            "{=lmmi_feast_local_14}Tell us a story from the road! Something with blood in it.",
            "{=lmmi_feast_local_15}A toast — to the guest who makes {SETTLEMENT} proud!",
        };

        private static void Bark(Agent? who, TextObject text)
        {
            if (who == null || !who.IsActive()) return;
            MBInformationManager.AddQuickInformation(text, 0, who.Character, null, "");
        }

        private static void Cleanup(Feast f)
        {
            if (Agent.Main != null && Agent.Main.IsActive() && Agent.Main.IsUsingGameObject) Agent.Main.StopUsingGameObject(false);
            // The watch goes back to its posts; the staff go home; the music stops.
            foreach (var a in f.Held) StreetEventsBehavior.Release(a);
            f.Held.Clear();
            StopMusic(f);
            foreach (var a in f.Staff.All)
            {
                if (!a.IsActive()) continue;
                if (a.IsUsingGameObject) a.StopUsingGameObject(false);
                a.FadeOut(true, true);
            }
            f.Staff.Clear();
            foreach (var a in f.Shooed.Keys)
            {
                if (!a.IsActive()) continue;
                var group = a.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>();
                if (group?.ScriptedBehavior is LmmiStageBehavior) group.DisableScriptedBehavior();
            }
            f.Shooed.Clear();
            foreach (var a in f.Spawned)
            {
                if (!a.IsActive()) continue;
                if (a.IsUsingGameObject) a.StopUsingGameObject(false);
                a.FadeOut(true, true);
            }
            foreach (var a in f.Borrowed)
            {
                if (!a.IsActive()) continue;
                if (a.IsUsingGameObject) a.StopUsingGameObject(false);
                var group = a.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>();
                if (group?.ScriptedBehavior is LmmiStageBehavior) group.DisableScriptedBehavior();
            }
            foreach (var e in f.Props)
            {
                try { e.Remove(88); } catch (Exception ex) { LmmiLog.Error("Feast: removing a prop threw", ex); }
            }
            f.Props.Clear();
            foreach (var e in f.Furniture)
            {
                // As Mission.RemoveSpawnedMissionObjects does: children first, then the entity (scripts get OnRemoved).
                try { e.RemoveAllChildren(); e.Remove(75); } catch (Exception ex) { LmmiLog.Error("Feast: removing furniture threw", ex); }
            }
            f.Furniture.Clear();
            LmmiLog.Info("Feast: the table is cleared.");
        }

        private static void ApplyEffects(Feast f)
        {
            if (f.Applied) return;
            f.Applied = true;
            ArrivalScenesBehavior.ApplyFeastEffects(f.Host);
        }

        // ---- Talking to the host at the table ----

        private bool AtTable() =>
            _feast != null && _feast.Phase == Phase.Feasting && Hero.OneToOneConversationHero == _feast.Host;

        private void AddDialogs(CampaignGameStarter starter)
        {
            try
            {
                starter.AddDialogLine("lmmi_feast_host", "start", "lmmi_feast_host_resp",
                    "{=!}{LMMI_FEAST_HOST}",
                    () => AtTable() && Flavor.Say("LMMI_FEAST_HOST",
                        "{=lmmi_feast_host}Eat, drink! Tonight you're among friends.",
                        "{=lmmi_feast_host_2}There you are! Sit, sit — the best cuts are coming your way.",
                        "{=lmmi_feast_host_3}My friend! Is everything to your liking? Say the word and it's yours.",
                        "{=lmmi_feast_host_4}Enjoying yourself? Good! That's what tonight is for."), null, 1100);

                starter.AddPlayerLine("lmmi_feast_toast_host", "lmmi_feast_host_resp", "lmmi_feast_toast_host_resp",
                    "{=lmmi_feast_toast_host}A toast — to our host!",
                    () => _feast != null && !_feast.ToastedHost,
                    () =>
                    {
                        var f = _feast;
                        if (f == null) return;
                        f.ToastedHost = true;
                        TaleWorlds.CampaignSystem.Actions.ChangeRelationAction.ApplyPlayerRelation(f.Host, 1, affectRelatives: false);
                        Bark(f.Theirs.FirstOrDefault(a => a != f.HostAgent && a.IsActive()),
                            Flavor.Pick("{=lmmi_feast_toast_cheer}To our host!", "{=lmmi_feast_toast_cheer_2}Hear, hear! To our host!",
                                "{=lmmi_feast_toast_cheer_3}To the host — long may the cellar last!"));
                    });
                starter.AddDialogLine("lmmi_feast_toast_host_resp", "lmmi_feast_toast_host_resp", "close_window",
                    "{=!}{LMMI_FEAST_TOAST_RESP}",
                    () => Flavor.Say("LMMI_FEAST_TOAST_RESP",
                        "{=lmmi_feast_toast_host_resp}Ha! To all of us, then — and to many more nights like this one.",
                        "{=lmmi_feast_toast_host_resp_2}To you, my friend! Now drink — that cup's half empty.",
                        "{=lmmi_feast_toast_host_resp_3}Ha! You'll make me blush. To all of us!"), null);

                starter.AddPlayerLine("lmmi_feast_leave", "lmmi_feast_host_resp", "lmmi_feast_leave_resp",
                    "{=lmmi_feast_leave}It's been a fine evening. I should be going — thank you for this.", null,
                    () =>
                    {
                        var f = _feast;
                        if (f == null) return;
                        if (_time - f.PhaseAt < (f.InHall ? HallEarlySeconds : EarlySeconds)) LeaveEarly(f, walkedOff: false);
                        f.EndRequested = true;
                    });
                starter.AddDialogLine("lmmi_feast_leave_resp_early", "lmmi_feast_leave_resp", "close_window",
                    "{=!}{LMMI_FEAST_LEAVE_EARLY}",
                    () => _feast?.LeftEarly == true && Flavor.Say("LMMI_FEAST_LEAVE_EARLY",
                        "{=lmmi_feast_leave_resp_early}Already? We've barely poured the wine. ...Well. I suppose someone like you has somewhere more important to be.",
                        "{=lmmi_feast_leave_resp_early_2}So soon? The meat's only just come out. ...Well, if you must.",
                        "{=lmmi_feast_leave_resp_early_3}Leaving? Before the singing? ...As you like. I'm sure you're very busy."), null);
                starter.AddDialogLine("lmmi_feast_leave_resp", "lmmi_feast_leave_resp", "close_window",
                    "{=!}{LMMI_FEAST_LEAVE}",
                    () => Flavor.Say("LMMI_FEAST_LEAVE",
                        "{=lmmi_feast_leave_resp}Safe roads, my friend. Don't be a stranger.",
                        "{=lmmi_feast_leave_resp_2}Go well, my friend. My door's always open to you.",
                        "{=lmmi_feast_leave_resp_3}Thank you for coming. Don't leave it so long next time."), null);

                // Everyone else at the table: a word and a cup, no errands.
                starter.AddDialogLine("lmmi_feast_guest", "start", "lmmi_feast_guest_resp",
                    "{=lmmi_feast_guest}{LMMI_FEAST_GUEST}",
                    () =>
                    {
                        var f = _feast;
                        var who = SandBox.Conversation.ConversationMission.OneToOneConversationAgent;
                        if (f == null || (f.Phase != Phase.Feasting && f.Phase != Phase.Lingering) || who == null) return false;
                        if (who == f.HostAgent && f.Phase == Phase.Feasting) return false;   // the host has his own words till you're done
                        bool mine = f.Yours.Contains(who);
                        if (!mine && !f.Theirs.Contains(who)) return false;
                        var hero = (who.Character as CharacterObject)?.HeroObject;
                        string[] lines = hero != null && mine ? CompanionLines
                            : hero != null ? NotableLines
                            : mine ? SoldierLines
                            : LocalLines;
                        var line = new TextObject(lines[MBRandom.RandomInt(lines.Length)]);
                        line.SetTextVariable("HOST", f.Host.Name);
                        line.SetTextVariable("SETTLEMENT", f.Settlement?.Name ?? TextObject.GetEmpty());
                        MBTextManager.SetTextVariable("LMMI_FEAST_GUEST", line);
                        return true;
                    }, null, 1250);
                starter.AddPlayerLine("lmmi_feast_guest_cheers", "lmmi_feast_guest_resp", "close_window",
                    "{=lmmi_feast_guest_cheers}Cheers!", null, null);
                starter.AddPlayerLine("lmmi_feast_guest_else", "lmmi_feast_guest_resp", "hero_main_options",
                    "{=lmmi_feast_guest_else}There's something else I wanted to ask you.",
                    () => SandBox.Conversation.ConversationMission.OneToOneConversationAgent?.Character is CharacterObject co && co.IsHero, null);

                starter.AddPlayerLine("lmmi_feast_stay", "lmmi_feast_host_resp", "close_window",
                    "{=lmmi_feast_stay}Carry on — I'm just stretching my legs.", null, null);

                AddStaffDialogs(starter);
            }
            catch (Exception ex) { LmmiLog.Error("FeastBehavior: failed to register dialogs", ex); }
        }
    }
}
