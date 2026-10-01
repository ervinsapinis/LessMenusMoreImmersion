using System;
using System.Collections.Generic;
using System.Linq;
using LessMenusMoreImmersion.Logging;
using SandBox;
using SandBox.Missions.AgentBehaviors;
using SandBox.Missions.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Stagecraft for the castle yard and the lord's hall: who in the scene is free to be borrowed, where someone new can
    /// come in without being seen to appear (a doorway out of sight, or somewhere behind you or behind a wall), and
    /// walking people to their marks. Nobody pops into existence next to someone else.
    /// </summary>
    internal static class CastleStaging
    {
        /// <summary>The player can't see this spot: far off, well behind, or with a wall or building in between.</summary>
        public static bool OutOfSight(Mission mission, Vec3 at)
        {
            var main = Agent.Main;
            if (main == null || !main.IsActive()) return true;
            float d = at.Distance(main.Position);
            if (d > 55f) return true;
            var to = (at - main.Position).AsVec2;
            var look = main.LookDirection.AsVec2;
            if (d > 12f && to.LengthSquared > 0.01f && look.LengthSquared > 0.01f
                && Vec2.DotProduct(to.Normalized(), look.Normalized()) < -0.35f) return true;
            try
            {
                var eye = main.GetEyeGlobalPosition();
                var head = at + new Vec3(0f, 0f, 1.6f);
                float full = eye.Distance(head);
                if (mission.Scene.RayCastForClosestEntityOrTerrain(eye, head, out float hit, 0.05f) && hit < full - 1f) return true;
            }
            catch (Exception ex) { LmmiLog.Error("CastleStaging: sight check threw", ex); }
            return false;
        }

        /// <summary>On the navigation mesh (somewhere people can stand and walk).</summary>
        public static bool OnNavMesh(Mission mission, Vec3 at)
        {
            try { return new WorldPosition(mission.Scene, UIntPtr.Zero, at, false).GetNavMesh() != UIntPtr.Zero; }
            catch { return false; }
        }

        public static Vec3 Ground(Mission mission, Vec3 at)
        {
            at.z = mission.Scene.GetGroundHeightAtPosition(at + new Vec3(0f, 0f, 1.5f));
            return at;
        }

        /// <summary>The doorways vanilla's own people come and go by (npc_passage).</summary>
        private static IEnumerable<Vec3> Doorways(Mission mission)
        {
            var doors = mission.GetMissionBehavior<MissionAgentHandler>()?.TownPassageProps;
            if (doors == null) yield break;
            foreach (var m in doors)
            {
                Vec3 pos;
                try
                {
                    var sp = m.StandingPoints?.FirstOrDefault();
                    pos = sp != null ? sp.GameEntity.GlobalPosition : m.GameEntity.GlobalPosition;
                }
                catch { continue; }
                yield return pos;
            }
        }

        /// <summary>
        /// Somewhere walkable the player can't see, <paramref name="min"/>–<paramref name="max"/> metres off, on about
        /// the player's level: a doorway first, else a little beside someone already standing there. Nearest to
        /// <paramref name="near"/> if given, else any.
        /// </summary>
        public static bool HiddenSpot(Mission mission, float min, float max, out Vec3 at, Vec3? near = null, Vec3? awayFrom = null, bool behindFar = false)
        {
            at = Vec3.Zero;
            var main = Agent.Main;
            if (main == null || !main.IsActive()) return false;
            var candidates = new List<(Vec3 Pos, bool Door)>();
            foreach (var d in Doorways(mission)) candidates.Add((d, true));
            foreach (var a in mission.Agents)
            {
                if (!a.IsActive() || !a.IsHuman || a == main) continue;
                var off = Vec2.FromRotation(MBRandom.RandomFloat * 6.283f) * 1.1f;
                candidates.Add((Ground(mission, a.Position + new Vec3(off.x, off.y, 0f)), false));
            }
            var look = main.LookDirection.AsVec2;
            look = look.LengthSquared < 0.01f ? Vec2.Forward : look.Normalized();
            bool Behind(Vec3 p2) { var to = (p2 - main.Position).AsVec2; return to.LengthSquared > 0.01f && Vec2.DotProduct(to.Normalized(), look) < 0f; }
            var fitting = candidates
                .Where(c =>
                {
                    float d = c.Pos.Distance(main.Position);
                    return d >= min && d <= max && Math.Abs(c.Pos.z - main.Position.z) < 4f
                           && (!awayFrom.HasValue || c.Pos.Distance(awayFrom.Value) > 12f);
                })
                .ToList();
            fitting = behindFar
                // Behind you first, and as far off as the yard allows: they have a walk in, not a pop-in.
                ? fitting.OrderBy(c => Behind(c.Pos) ? 0 : 1).ThenByDescending(c => c.Pos.Distance(main.Position)).ToList()
                : fitting.OrderBy(c => c.Door ? 0 : 1).ThenBy(c => near.HasValue ? c.Pos.Distance(near.Value) : MBRandom.RandomFloat).ToList();
            foreach (var c in fitting)
            {
                if (!OutOfSight(mission, c.Pos) || !OnNavMesh(mission, c.Pos)) continue;
                at = c.Pos;
                return true;
            }
            return false;
        }

        /// <summary>Someone of the watch or the garrison with nothing else to do: not at a post, not part of anything.</summary>
        public static bool IsFreeSoldier(Agent a) =>
            a.IsActive() && a.IsHuman && a != Agent.Main && !a.IsUsingGameObject
            && a.Character is CharacterObject co && !co.IsHero && StreetEventsBehavior.IsWatch(a)
            && a.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>() is DailyBehaviorGroup daily
            && daily.ScriptedBehavior == null;

        /// <summary>Free soldiers within <paramref name="within"/> m of a spot (and about its level), nearest first.</summary>
        public static List<Agent> FreeSoldiers(Mission mission, Vec3 near, float within, int max, ICollection<Agent>? not = null) =>
            mission.Agents.Where(a => IsFreeSoldier(a) && (not == null || !not.Contains(a))
                                      && a.Position.Distance(near) < within && Math.Abs(a.Position.z - near.z) < 3f)
                .OrderBy(a => a.Position.Distance(near)).Take(max).ToList();

        /// <summary>Walk (or run) to a spot; once there, face someone and loop an action.</summary>
        public static bool WalkTo(Agent a, Vec3 to, Agent? face = null, string? loop = null, bool run = false, float speed = -1f)
        {
            var stage = StreetEventsBehavior.Direct(a);
            if (stage == null) return false;
            var wp = a.GetWorldPosition();
            wp.SetVec2(to.AsVec2);
            stage.GoTo(wp, run, face, loop, speed);
            return true;
        }

        /// <summary>A straight, unobstructed walk across the navmesh from where someone stands to a point.</summary>
        public static bool ClearLine(Mission mission, WorldPosition from, Vec3 to, float radius = 0.4f)
        {
            try
            {
                var a = from;
                var b = from;
                b.SetVec2(to.AsVec2);
                return mission.Scene.IsLineToPointClear(ref a, ref b, radius);
            }
            catch { return false; }
        }

        /// <summary>Bring someone new in, out of the player's sight; they then walk to their mark.</summary>
        public static Agent? SpawnHidden(Mission mission, CharacterObject who, bool civilian, Vec3 headingTo, float min = 15f, float max = 60f)
        {
            if (!HiddenSpot(mission, min, max, out var at, near: headingTo, behindFar: true)) return null;
            var dir = (headingTo - at).AsVec2;
            float facing = dir.LengthSquared < 0.01f ? 0f : dir.Normalized().RotationInRadians;
            return SceneSpawner.Spawn(mission, who, at, facing, civilian);
        }

        /// <summary>Someone we brought in leaves the way they came: out of sight, then gone.</summary>
        public sealed class Leavers
        {
            private readonly List<(Agent Who, float Deadline)> _list = new List<(Agent, float)>();

            public void Add(Mission mission, Agent a, float now)
            {
                if (a == null || !a.IsActive()) return;
                if (HiddenSpot(mission, 15f, 70f, out var to, near: a.Position)) WalkTo(a, to);
                _list.Add((a, now + 45f));
            }

            public void Tick(Mission mission, float now)
            {
                for (int i = _list.Count - 1; i >= 0; i--)
                {
                    var (who, deadline) = _list[i];
                    if (!who.IsActive()) { _list.RemoveAt(i); continue; }
                    if (now > deadline || (now > deadline - 40f && OutOfSight(mission, who.Position) && who.Position.Distance(Agent.Main?.Position ?? who.Position) > 8f))
                    {
                        StreetEventsBehavior.Release(who);
                        who.FadeOut(true, true);
                        _list.RemoveAt(i);
                    }
                }
            }

            public void Clear() => _list.Clear();
        }
    }
}
