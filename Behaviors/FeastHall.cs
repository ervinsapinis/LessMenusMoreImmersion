using System;
using System.Collections.Generic;
using System.Linq;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using SandBox;
using SandBox.Missions.MissionLogics;
using SandBox.Objects.Usables;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// A feast in a lord's hall, Warband-style: the lords in town (or a short ride off), the town's notables, your
    /// companions and the best of both households' men, at two or three long tables laid on the hall's open floor, the host
    /// at the head and you two seats down. When you've had your fill (or wander off) the evening's effects apply, and
    /// the hall feasts on around you until you leave.
    /// </summary>
    public partial class FeastBehavior
    {
        private const float HallFeastSeconds = 150f;
        private const float HallEarlySeconds = 70f;
        private const int MaxHallTables = 3;
        private const int HallSeatsTarget = 20;         // the host and you included
        private const float NearbyLordDistance = 6f;    // map units: a short ride
        private const int MaxHallCompanions = 6;

        private bool BuildInHall(Feast f, Mission mission)
        {
            var host = f.HostAgent;
            var scene = mission.Scene;
            StreetEventsBehavior.CancelAll();
            f.InHall = true;
            f.Center = host.Position;
            f.Radius = 0f;

            var guests = HallGuests(f, mission);
            int wanted = Math.Max(1, Math.Min(MaxHallTables, (guests.Count + 2 + SeatsPerTable - 1) / SeatsPerTable));
            var chairs = new List<Chair>();
            Chair? hostChair = null;
            if (FitTables(mission, host, wanted, out var center, out var along, out int tables, out int perSide))
            {
                if (Vec2.DotProduct((host.Position - center).AsVec2, along) < 0f) along = -along;   // the host's end
                f.Center = center;
                f.Radius = (tables - 1) * TableSpacing / 2f + 3.2f;
                chairs = LayTables(f, scene, center, along, tables, perSide);
                hostChair = chairs.OrderByDescending(c => Vec2.DotProduct((c.GameEntity.GetGlobalFrame().origin - center).AsVec2, along)).FirstOrDefault();
                LmmiLog.Info($"Feast: {tables} of {wanted} wanted table(s) of {perSide * 2} laid in the hall at {center}.");
            }
            if (chairs.Count == 0)
            {
                // No open floor for even one table: the hall's own chairs on the host's floor, as before.
                f.Center = host.Position;
                f.Radius = 0f;
                float floor = host.Position.z;
                chairs = mission.MissionObjects.OfType<Chair>()
                    .Where(c => !c.IsDeactivated && FreePoint(c) is StandingPoint sp
                                && Math.Abs(sp.GameEntity.GetGlobalFrame().origin.z - floor) < 0.8f
                                && c.GameEntity.GetGlobalFrame().origin.Distance(host.Position) < 25f)
                    .ToList();
                hostChair = chairs.OrderBy(c => c.GameEntity.GetGlobalFrame().origin.Distance(host.Position)).FirstOrDefault();
                LmmiLog.Info($"Feast: no open floor in the hall for a table — using its own {chairs.Count} chairs.");
            }
            if (hostChair == null || chairs.Count < 2) { LmmiLog.Info("Feast: no chairs in the hall."); return false; }

            // Seats by nearness to the host's: the nearest for the guest of honour, the next for you.
            var hostAt = hostChair.GameEntity.GetGlobalFrame().origin;
            var seats = chairs.Where(c => c != hostChair).OrderBy(c => c.GameEntity.GetGlobalFrame().origin.Distance(hostAt)).ToList();
            var playerChair = seats.Count >= 2 && guests.Count > 0 ? seats[1] : seats[0];
            seats.Remove(playerChair);
            if (f.Chairs.Count > 0) Redress(PointFor(playerChair, f), SitLoop, "");

            foreach (var g in guests) if (g.Agent != null) f.Borrowed.Add(g.Agent);   // invited: not cleared away
            if (f.Radius > 0f) Evacuate(f, mission, f.Center);

            if (!SitOnChair(host, hostChair, f)) return false;
            f.Borrowed.Add(host);
            f.Theirs.Add(host);

            int seat = 0;
            foreach (var (existing, character, mine) in guests)
            {
                if (seat >= seats.Count) break;
                if (existing != null && f.Chairs.Count > 0 && !Fits(existing, seats[seat]))
                {
                    int swap = Enumerable.Range(seat + 1, seats.Count - seat - 1).FirstOrDefault(i => Fits(existing, seats[i]));
                    if (swap > seat) (seats[seat], seats[swap]) = (seats[swap], seats[seat]);
                }
                var chair = seats[seat];
                var agent = existing;
                if (agent == null)
                {
                    var sp = PointFor(chair, f);
                    if (sp == null) { seat++; continue; }
                    var spFrame = sp.GameEntity.GetGlobalFrame();
                    float facing = spFrame.rotation.f.AsVec2.RotationInRadians;
                    agent = character.IsHero && character.HeroObject != null
                        ? SpawnHallHero(mission, character.HeroObject, spFrame.origin, facing)
                        : SpawnGuest(mission, character, spFrame.origin, facing);
                    if (agent == null) continue;
                    f.Spawned.Add(agent);
                }
                if (!SitOnChair(agent, chair, f)) continue;
                seat++;
                (mine ? f.Yours : f.Theirs).Add(agent);
            }

            bool seated = SitPlayer(playerChair, f);
            SpawnStaff(f, mission);
            LmmiLog.Info($"Feast in the hall with {f.Host.Name} ({chairs.Count} chairs, {(seated ? "you're seated" : "you stay standing")}): "
                         + $"yours: {string.Join(", ", f.Yours.Select(a => a.Name))}; theirs: {string.Join(", ", f.Theirs.Select(a => a.Name))}.");
            return true;
        }

        /// <summary>
        /// Who dines: lords in town or a short ride off (not at war with you, not ill-disposed, not off at war themselves),
        /// the host's spouse and clan first; the town's notables; your companions; then the best men of the host's party
        /// and garrison and of yours. Already in the scene: they come over; elsewhere: they're brought in for the evening.
        /// </summary>
        private static List<(Agent? Agent, CharacterObject Character, bool Mine)> HallGuests(Feast f, Mission mission)
        {
            var here = f.Settlement;
            var host = f.Host;
            var me = Hero.MainHero;
            Agent? Present(CharacterObject c) =>
                mission.Agents.FirstOrDefault(a => a.IsActive() && a.Character == c && a != Agent.Main && a != f.HostAgent);
            bool Welcome(Hero h) =>
                h != host && h != me && h.IsAlive && !h.IsChild && !h.IsPrisoner && h.Clan != Clan.PlayerClan
                && h.GetRelationWithPlayer() >= 0f
                && (h.MapFaction == null || me.MapFaction == null || !h.MapFaction.IsAtWarWith(me.MapFaction));
            bool Free(Hero h)
            {
                var p = h.PartyBelongedTo;
                return p == null || (p.MapEvent == null && p.BesiegedSettlement == null && (p.Army == null || p.CurrentSettlement == here));
            }

            var lords = new List<Hero>();
            void AddLord(Hero? h)
            {
                if (h != null && h.IsLord && !lords.Contains(h) && Welcome(h) && Free(h)) lords.Add(h);
            }
            foreach (var a in mission.Agents)
                if (a.IsActive() && a.Character is CharacterObject co && co.IsHero) AddLord(co.HeroObject);
            if (here != null)
            {
                foreach (var h in here.HeroesWithoutParty) AddLord(h);
                foreach (var p in here.Parties) AddLord(p.LeaderHero);
                foreach (var p in MobileParty.AllLordParties)
                    if (p.CurrentSettlement == null && p.LeaderHero != null && p.Position.Distance(here.Position) <= NearbyLordDistance)
                        AddLord(p.LeaderHero);
            }
            var ordered = lords
                .OrderByDescending(h => h == host.Spouse)
                .ThenByDescending(h => h.Clan != null && h.Clan == host.Clan)
                .ThenByDescending(h => h.Clan?.Tier ?? 0)
                .ToList();
            var notables = here?.Notables
                .Where(h => h != host && h.IsAlive && !h.IsPrisoner && h.GetRelationWithPlayer() > -10f)
                .OrderByDescending(h => h.Power).ToList() ?? new List<Hero>();
            var theirs = ordered.Concat(notables).Select(h => h.CharacterObject).ToList();
            var yours = MobileParty.MainParty?.MemberRoster.GetTroopRoster()
                .Where(e => e.Character != null && e.Character.IsHero && e.Character.HeroObject != me
                            && e.Character.HeroObject.IsAlive && !e.Character.HeroObject.IsWounded)
                .Select(e => e.Character).Take(MaxHallCompanions).ToList() ?? new List<CharacterObject>();

            int target = HallSeatsTarget - 2;
            var list = new List<(Agent?, CharacterObject, bool)>();
            int ti = 0, yi = 0;
            while ((ti < theirs.Count || yi < yours.Count) && list.Count < target)
            {
                // Two of theirs to one of yours, near the head of the table.
                for (int k = 0; k < 2 && ti < theirs.Count && list.Count < target; k++, ti++) list.Add((Present(theirs[ti]), theirs[ti], false));
                if (yi < yours.Count && list.Count < target) { list.Add((Present(yours[yi]), yours[yi], true)); yi++; }
            }

            // The rest of the benches: the best men of both households, at the foot.
            int left = target - list.Count;
            if (left > 0)
            {
                var theirMen = BestTroops(host.PartyBelongedTo?.MemberRoster, left)
                    .Concat(BestTroops(here?.Town?.GarrisonParty?.MemberRoster, left))
                    .OrderByDescending(c => c.Tier).Take((left + 1) / 2).ToList();
                var yourMen = BestTroops(MobileParty.MainParty?.MemberRoster, left - theirMen.Count);
                for (int i = 0; i < Math.Max(theirMen.Count, yourMen.Count); i++)
                {
                    if (i < yourMen.Count) list.Add((null, yourMen[i], true));
                    if (i < theirMen.Count) list.Add((null, theirMen[i], false));
                }
            }
            LmmiLog.Info($"Feast: the hall's guest list — {ordered.Count} lord(s), {notables.Count} notable(s), {yours.Count} companion(s), "
                         + $"{list.Count(g => !g.Item2.IsHero)} soldier(s); {list.Count} in all.");
            return list;
        }

        private static List<CharacterObject> BestTroops(TroopRoster? roster, int count)
        {
            var men = new List<CharacterObject>();
            if (roster == null || count <= 0) return men;
            foreach (var e in roster.GetTroopRoster().Where(e => e.Character != null && !e.Character.IsHero && e.Number - e.WoundedNumber > 0)
                         .OrderByDescending(e => e.Character.Tier))
            {
                for (int i = 0; i < e.Number - e.WoundedNumber && men.Count < count; i++) men.Add(e.Character);
                if (men.Count >= count) break;
            }
            return men;
        }

        private const float HallSearchRadius = 25f;   // the hall's floor, not just the host's corner of it
        private const float HallGridStep = 2f;

        // Why a candidate table spot was turned down (counted, so the log says what's wrong with a hall).
        private enum Why { Ok, Level, Nav, Waist, Head, Seated }

        private static readonly Vec2[] HallHeadings = BuildHeadings();

        // Twelve headings 15 degrees apart (a table is the same turned half way round), the world's axes first: most halls
        // are laid out square to them.
        private static Vec2[] BuildHeadings()
        {
            var order = new[] { 0, 6, 3, 9, 1, 2, 4, 5, 7, 8, 10, 11 };
            return order.Select(i => Vec2.FromRotation(i * 0.2618f)).ToArray();
        }

        /// <summary>
        /// Open, level floor for the tables, end to end (with their chairs and room to walk behind them): found over the
        /// whole of the host's floor within 25 m, nearest to him first, on the navmesh (a hall's own furniture is cut out
        /// of it), reachable from where he stands, with nothing solid across it at waist or head height and nobody seated
        /// there. Biggest layout first: the wanted number of tables of eight chairs, fewer tables, then one of six seats and
        /// one of four. Why spots were turned down is logged, per layout tried.
        /// </summary>
        private static bool FitTables(Mission mission, Agent host, int wanted, out Vec3 center, out Vec2 along, out int tables, out int perSide)
        {
            var scene = mission.Scene;
            var near = host.Position;
            float floor = near.z;

            // The floor to choose from, once: a grid on the host's level and the navmesh, nearest to him first.
            var points = new List<Vec2>();
            int offLevel = 0, offNav = 0;
            for (float dx = -HallSearchRadius; dx <= HallSearchRadius; dx += HallGridStep)
            for (float dy = -HallSearchRadius; dy <= HallSearchRadius; dy += HallGridStep)
            {
                if (dx * dx + dy * dy > HallSearchRadius * HallSearchRadius) continue;
                var p = new Vec3(near.x + dx, near.y + dy, floor + 1f);
                float h = scene.GetGroundHeightAtPosition(p);
                if (Math.Abs(h - floor) > 0.3f) { offLevel++; continue; }
                p.z = h;
                if (!Walkable(scene, p)) { offNav++; continue; }
                points.Add(p.AsVec2);
            }
            points = points.OrderBy(p => p.DistanceSquared(near.AsVec2)).ToList();
            LmmiLog.Info($"Feast: hall floor within {HallSearchRadius:0} m of the host: {points.Count} usable point(s), "
                         + $"{offLevel} off his level, {offNav} off the navmesh.");

            var stages = new List<(int Tables, int PerSide)>();
            for (int n = wanted; n >= 1; n--) stages.Add((n, 4));
            stages.Add((1, 3));
            stages.Add((1, 2));
            foreach (var (n, side) in stages)
            {
                // The smaller layouts squeeze into tighter spots: less room left to walk round them.
                bool tight = side < 4;
                float hl = (n - 1) * TableSpacing / 2f + 1.5f + (tight ? 0.3f : 0.7f);
                float hw = ChairOut + (tight ? 0.4f : 0.6f);
                var why = new int[6];
                int tried = 0, unreachable = 0;
                foreach (var c2 in points)
                foreach (var ax in HallHeadings)
                {
                    tried++;
                    var w = RectClear(mission, new Vec3(c2.x, c2.y, floor), ax, hl, hw, floor);
                    why[(int)w]++;
                    if (w != Why.Ok) continue;
                    var at = new Vec3(c2.x, c2.y, floor);
                    if (!Reachable(scene, host, at)) { unreachable++; continue; }
                    center = at;
                    along = ax;
                    tables = n;
                    perSide = side;
                    LmmiLog.Info($"Feast: hall layout {n} x {side * 2} seats fits {near.AsVec2.Distance(c2):0.0} m from the host after {tried} spot(s) tried "
                                 + $"(off level {why[(int)Why.Level]}, off navmesh {why[(int)Why.Nav]}, blocked at waist {why[(int)Why.Waist]}, "
                                 + $"at head {why[(int)Why.Head]}, seat taken {why[(int)Why.Seated]}, unreachable {unreachable}).");
                    return true;
                }
                LmmiLog.Info($"Feast: hall layout {n} x {side * 2} seats doesn't fit: {tried} spot(s) tried, off level {why[(int)Why.Level]}, "
                             + $"off navmesh {why[(int)Why.Nav]}, blocked at waist height {why[(int)Why.Waist]}, blocked at head height {why[(int)Why.Head]}, "
                             + $"seat taken {why[(int)Why.Seated]}, unreachable {unreachable}.");
            }
            center = near;
            along = Vec2.Forward;
            tables = 0;
            perSide = 0;
            return false;
        }

        /// <summary>Can the host walk there? (The floor beyond a wall is no place for a table he and you must sit at.) If the
        /// engine can't say, yes.</summary>
        private static bool Reachable(Scene scene, Agent host, Vec3 at)
        {
            try
            {
                var from = host.GetWorldPosition();
                var to = new WorldPosition(scene, UIntPtr.Zero, at, false);
                return scene.GetPathDistanceBetweenPositions(ref from, ref to, 0.3f, out float _);
            }
            catch (Exception ex)
            {
                if (!_reachWarned) { _reachWarned = true; LmmiLog.Error("Feast: the reachability check for the tables threw", ex); }
                return true;
            }
        }

        private static bool _reachWarned;

        /// <summary>
        /// Is the rectangle (hl along ax, hw across, about c) free for tables and chairs? Every sample on the host's level
        /// and the navmesh; then nothing solid across it at waist height (a seated guest's torso, the table's dishes) or at
        /// head height (the maid walking round). Nothing lower is probed: a rug, a crate or a bench seat doesn't spoil a
        /// feast, and the navmesh already has the hall's real furniture cut out; the old knee-high probe turned good halls
        /// down for clutter.
        /// </summary>
        private static Why RectClear(Mission mission, Vec3 c, Vec2 ax, float hl, float hw, float floor)
        {
            var scene = mission.Scene;
            var side = new Vec2(-ax.y, ax.x);
            Vec3 P(float x, float y, float z)
            {
                var p = c.AsVec2 + ax * x + side * y;
                return new Vec3(p.x, p.y, z);
            }
            int nx = (int)Math.Ceiling(2f * hl / 0.9f), ny = (int)Math.Ceiling(2f * hw / 0.9f);
            for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= ny; j++)
            {
                var p = P(-hl + 2f * hl * i / nx, -hw + 2f * hw * j / ny, floor + 1f);
                float h = scene.GetGroundHeightAtPosition(p);
                if (Math.Abs(h - floor) > 0.3f) return Why.Level;
                p.z = h;
                if (!Walkable(scene, p)) return Why.Nav;
            }
            foreach (var (z, why) in new[] { (0.9f, Why.Waist), (1.5f, Why.Head) })
            {
                foreach (float y in new[] { -hw, 0f, hw })
                    if (scene.RayCastForClosestEntityOrTerrain(P(-hl, y, floor + z), P(hl, y, floor + z), out float _, 0.05f)) return why;
                foreach (float x in new[] { -hl, 0f, hl })
                    if (scene.RayCastForClosestEntityOrTerrain(P(x, -hw, floor + z), P(x, hw, floor + z), out float _, 0.05f)) return why;
            }
            return mission.Agents.Any(a => a.IsActive() && a.IsUsingGameObject && Math.Abs(a.Position.z - floor) < 1f
                                           && Math.Abs(Vec2.DotProduct(a.Position.AsVec2 - c.AsVec2, ax)) < hl
                                           && Math.Abs(Vec2.DotProduct(a.Position.AsVec2 - c.AsVec2, side)) < hw)
                ? Why.Seated : Why.Ok;
        }

        /// <summary>A lord or notable brought to the table the way vanilla puts them in a scene (HeroAgentSpawnCampaignBehavior).</summary>
        private static Agent? SpawnHallHero(Mission mission, Hero hero, Vec3 at, float facing)
        {
            if (hero.Clan == Clan.PlayerClan || hero.IsPlayerCompanion) return SpawnGuest(mission, hero.CharacterObject, at, facing);
            try
            {
                var handler = mission.GetMissionBehavior<MissionAgentHandler>();
                if (handler == null) return null;
                var co = hero.CharacterObject;
                IAgentOriginBase origin = hero.PartyBelongedTo != null
                    ? new PartyAgentOrigin(hero.PartyBelongedTo.Party, co, -1, default(UniqueTroopDescriptor), false, false)
                    : hero.IsNotable
                        ? new PartyAgentOrigin(null, co, -1, default(UniqueTroopDescriptor), false, false)
                        : (IAgentOriginBase)new SimpleAgentOrigin(co, -1, null, default(UniqueTroopDescriptor));
                var data = new AgentData(origin).Monster(TaleWorlds.Core.FaceGen.GetMonsterWithSuffix(co.Race, "_settlement")).NoHorses(true);
                uint color = hero.MapFaction?.Color ?? 0xFFCCCCCCu;
                data.ClothingColor1(color).ClothingColor2(hero.MapFaction?.Color2 ?? color);
                string suffix = hero.IsLord ? "_lord"
                    : hero.IsArtisan ? "_villager_artisan" : hero.IsMerchant ? "_villager_merchant" : hero.IsPreacher ? "_villager_preacher"
                    : hero.IsGangLeader ? "_villager_gangleader" : hero.IsRuralNotable ? "_villager_ruralnotable"
                    : hero.IsFemale ? "_lord" : "_villager_merchant";
                var lc = new LocationCharacter(data, SandBoxManager.Instance.AgentBehaviorManager.AddFixedCharacterBehaviors,
                    null, true, LocationCharacter.CharacterRelations.Neutral,
                    TaleWorlds.Core.ActionSetCode.GenerateActionSetNameWithSuffix(data.AgentMonster, hero.IsFemale, suffix),
                    true, false, null, false, false, true, null, false);
                var dir = Vec2.FromRotation(facing);
                var rot = Mat3.Identity;
                rot.f = new Vec3(dir.x, dir.y, 0f);
                rot.u = new Vec3(0f, 0f, 1f);
                rot.s = Vec3.CrossProduct(rot.f, rot.u);
                return handler.SpawnWanderingAgentWithInitialFrame(lc, new MatrixFrame(in rot, in at), WeakGameEntity.Invalid, true, false);
            }
            catch (Exception ex)
            {
                LmmiLog.Error($"Feast: couldn't bring {hero.Name} to the hall's table", ex);
                return null;
            }
        }

        /// <summary>The evening's done for you (goodbye, the hour, or you wandered off): the hall feasts on regardless.</summary>
        private void EndInHall(Feast f)
        {
            ApplyEffects(f);
            InformationManager.DisplayMessage(new InformationMessage(Flavor.Pick(
                "{=lmmi_feast_hall_goes_on}The hall feasts on around you; nobody is in a hurry to leave the table.",
                "{=lmmi_feast_hall_goes_on_2}The hall carries on around you — cups raised, voices loud, nobody leaving.",
                "{=lmmi_feast_hall_goes_on_3}You've had your fill, but the hall hasn't. The feast goes on.").ToString()));
            Go(f, Phase.Lingering);
            LmmiLog.Info($"Feast: the hall feast with {f.Host.Name} goes on without you.");
        }
    }
}
