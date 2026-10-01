using System;
using System.Collections.Generic;
using System.Linq;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using SandBox;
using SandBox.Missions.MissionLogics;
using SandBox.Objects;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>The hired help for one evening: a tavern maid standing with her jug, and for a grand enough host a band and dancers.</summary>
    internal sealed class FeastStaff
    {
        public Agent? Maid;
        public readonly List<Agent> Musicians = new List<Agent>();
        public readonly List<Agent> Dancers = new List<Agent>();
        public readonly Dictionary<Agent, InstrumentData> Playing = new Dictionary<Agent, InstrumentData>();
        public SoundEvent? Track;
        public float NextTrackAt;
        public Vec3 BandAt;

        public float NextTickAt;

        public IEnumerable<Agent> All
        {
            get
            {
                if (Maid != null) yield return Maid;
                foreach (var a in Musicians) yield return a;
                foreach (var a in Dancers) yield return a;
            }
        }

        public void Clear()
        {
            Maid = null;
            Musicians.Clear();
            Dancers.Clear();
            Playing.Clear();
        }
    }

    public partial class FeastBehavior
    {
        private const string MaidJug = "kitchen_pitcher_b_tavern";   // what vanilla's tavern maid carries (TavernEmployeesCampaignBehavior)

        /// <summary>
        /// Bring in the help, the way vanilla brings in a tavern's (TavernEmployeesCampaignBehavior,
        /// CommonTownsfolkCampaignBehavior): the culture's tavern maid with a jug in her off hand, its musicians and dancers
        /// in their own action sets. A maid always; a band for an influential host (Power 100+), dancers for a powerful one
        /// (200+); a lord's hall gets everything.
        /// </summary>
        private void SpawnStaff(Feast f, Mission mission)
        {
            try
            {
                var culture = f.Settlement?.Culture ?? f.Host.Culture;
                if (culture == null) return;
                float power = f.Host.IsNotable ? f.Host.Power : 1000f;
                int bandSize = f.InHall ? 3 : power >= 100f ? 2 : 0;
                int dancers = f.InHall ? 2 : power >= 200f ? 1 : 0;
                var scene = mission.Scene;

                Vec3 maidAt, bandAt, tableAt;
                Vec2 maidFacing;
                var seats = SeatPoints(f);
                if (f.Chairs.Count > 0)
                {
                    // At the foot of the table, facing the host's end.
                    var side = new Vec2(-f.Along.y, f.Along.x);
                    Vec3 At(float x, float y)
                    {
                        var p = f.Center.AsVec2 + f.Along * x + side * y;
                        return Ground(scene, new Vec3(p.x, p.y, f.Center.z));
                    }
                    bandAt = At(-(f.HalfTable + 1.9f), 0f);   // only if there's no room to stand further off (below)
                    tableAt = f.Center;

                    // The maid stands off the table's end nearest the host, jug out, facing the table (like vanilla's, she stays put).
                    float endSign = Vec2.DotProduct((f.HostAgent.Position - f.Center).AsVec2, f.Along) >= 0f ? 1f : -1f;
                    maidAt = default;
                    maidFacing = -f.Along * endSign;
                    bool placed = false;
                    foreach (var (ex, ey) in new[] { (endSign, 0f), (endSign, 1f), (endSign, -1f), (-endSign, 0f), (-endSign, 1f), (-endSign, -1f) })
                    {
                        var cand = At(ex * (f.HalfTable + 0.9f), ey);
                        if (Math.Abs(cand.z - f.Center.z) > 0.6f || !Walkable(scene, cand)) continue;
                        maidAt = cand;
                        maidFacing = -f.Along * ex;
                        placed = true;
                        break;
                    }
                    if (!placed) maidAt = At(endSign * (f.HalfTable + 0.9f), 0f);   // spawn check below skips her if it isn't walkable
                }
                else
                {
                    if (!OpenFloor(mission, f.HostAgent.Position, out maidAt)) maidAt = f.HostAgent.Position;
                    bandAt = maidAt;
                    var toHost = (f.HostAgent.Position - maidAt).AsVec2;
                    maidFacing = toHost.LengthSquared > 0.01f ? toHost.Normalized() : Vec2.Forward;
                    float sx = 0f, sy = 0f;
                    foreach (var s2 in seats) { sx += s2.x; sy += s2.y; }
                    tableAt = seats.Count > 0 ? new Vec3(sx / seats.Count, sy / seats.Count, f.HostAgent.Position.z) : f.HostAgent.Position;
                }

                // The band well back from the tables (not in the diners' laps), facing them; the dancers between the two.
                var facing = maidFacing;
                bool far = FindBandSpot(mission, seats, tableAt, out var farSpot);
                if (far)
                {
                    bandAt = farSpot;
                    var toTables = (tableAt - bandAt).AsVec2;
                    facing = toTables.LengthSquared > 0.01f ? toTables.Normalized() : maidFacing;
                }
                float bandD = NearestSeat(seats, bandAt.AsVec2);
                var across = new Vec2(-facing.y, facing.x);
                f.Staff.BandAt = bandAt;

                if (culture.TavernWench != null && Walkable(scene, maidAt))
                {
                    // The tavern wench herself (face, name, barmaid idle with the pitcher out) in the culture's female dancer's outfit.
                    f.Staff.Maid = SpawnHired(mission, culture.TavernWench, "_barmaid", maidAt, maidFacing, MaidJug, culture.FemaleDancer?.FirstCivilianEquipment);
                    StreetEventsBehavior.Direct(f.Staff.Maid)?.Hold();   // stands where she is (no loop: the barmaid idle holds the pitcher out)
                }
                for (int i = 0; i < bandSize && culture.Musician != null; i++)
                {
                    var p2 = bandAt.AsVec2 + across * ((i - (bandSize - 1) / 2f) * 1.1f);
                    var at = Ground(scene, new Vec3(p2.x, p2.y, bandAt.z));
                    if (!Walkable(scene, at)) continue;
                    var m = SpawnHired(mission, culture.Musician, "_musician", at, facing, null);
                    if (m == null) continue;
                    f.Staff.Musicians.Add(m);
                    StreetEventsBehavior.Direct(m)?.Hold(f.HostAgent, "act_musician_idle_stand_cheerful");
                }
                int danced = 0;
                for (int i = 0; i < dancers && culture.FemaleDancer != null; i++)
                {
                    float off = (i - (dancers - 1) / 2f) * 1.3f;
                    Vec3 at;
                    if (far)
                    {
                        if (!DancerSpot(scene, seats, bandAt, bandD, facing, across, off, out at)) continue;
                    }
                    else
                    {
                        var p2 = bandAt.AsVec2 + facing * 1.1f + across * off;
                        at = Ground(scene, new Vec3(p2.x, p2.y, bandAt.z));
                        if (!Walkable(scene, at)) continue;
                    }
                    var d = SpawnHired(mission, culture.FemaleDancer, "_dancer", at, facing, null);
                    if (d == null) continue;
                    f.Staff.Dancers.Add(d);
                    danced++;
                    StreetEventsBehavior.Direct(d)?.Hold(f.HostAgent, "act_musician_idle_stand_cheerful");
                }
                LmmiLog.Info(far
                    ? $"Feast: the band stands {bandD:0.0} m from the nearest seat, facing the tables ({danced} dancer(s) between)."
                    : "Feast: no open floor well back from the tables — the band stays close.");
                f.Staff.NextTrackAt = _time + FadeIn + 2f;
                LmmiLog.Info($"Feast: the help — maid: {(f.Staff.Maid != null ? "yes" : "no")}, musicians: {f.Staff.Musicians.Count}, "
                             + $"dancers: {f.Staff.Dancers.Count} (host power {power:0}).");
            }
            catch (Exception ex) { LmmiLog.Error("Feast: bringing in the help threw", ex); }
        }

        private static Agent? SpawnHired(Mission mission, CharacterObject character, string actionSuffix, Vec3 at, Vec2 facing, string? offHandPrefab, Equipment? outfit = null)
        {
            try
            {
                var handler = mission.GetMissionBehavior<MissionAgentHandler>();
                if (handler == null) return null;
                var monster = TaleWorlds.Core.FaceGen.GetMonsterWithSuffix(character.Race, "_settlement");
                Campaign.Current.Models.AgeModel.GetAgeLimitForLocation(character, out int minAge, out int maxAge, "");
                var data = new AgentData(new SimpleAgentOrigin(character, -1, null, default(UniqueTroopDescriptor)))
                    .Monster(monster).Age(MBRandom.RandomInt(minAge, Math.Max(minAge + 1, maxAge))).NoHorses(true);
                if (outfit != null) data.Equipment(new Equipment(outfit));
                var lc = new LocationCharacter(data, SandBoxManager.Instance.AgentBehaviorManager.AddFixedCharacterBehaviors,
                    null, true, LocationCharacter.CharacterRelations.Neutral,
                    TaleWorlds.Core.ActionSetCode.GenerateActionSetNameWithSuffix(data.AgentMonster, data.AgentIsFemale, actionSuffix),
                    true, false, null, false, false, true, null, false);
                if (offHandPrefab != null) lc.PrefabNamesForBones[data.AgentMonster.OffHandItemBoneIndex] = offHandPrefab;
                var dir = facing.LengthSquared > 0.0001f ? facing.Normalized() : Vec2.Forward;
                var rot = Mat3.Identity;
                rot.f = new Vec3(dir.x, dir.y, 0f);
                rot.u = new Vec3(0f, 0f, 1f);
                rot.s = Vec3.CrossProduct(rot.f, rot.u);
                return handler.SpawnWanderingAgentWithInitialFrame(lc, new MatrixFrame(in rot, in at), WeakGameEntity.Invalid, true, false);
            }
            catch (Exception ex)
            {
                LmmiLog.Error($"Feast: couldn't bring in {character.Name}", ex);
                return null;
            }
        }

        private const float BandMinDistance = 6f;      // from the nearest seat: the band's own space, not the diners' laps
        private const float BandIdealDistance = 7.5f;
        private const float DancerMinDistance = 4f;

        private sealed class BandCandidate
        {
            public Vec3 P;
            public float D;
            public bool? Room, Sees;
        }

        /// <summary>Where people sit (xy): the laid chairs, the guests, the host and you.</summary>
        private static List<Vec2> SeatPoints(Feast f)
        {
            var seats = new List<Vec2>();
            foreach (var c in f.Chairs) seats.Add(c.GameEntity.GetGlobalFrame().origin.AsVec2);
            foreach (var a in f.Yours.Concat(f.Theirs)) if (a.IsActive()) seats.Add(a.Position.AsVec2);
            if (f.HostAgent != null && f.HostAgent.IsActive()) seats.Add(f.HostAgent.Position.AsVec2);
            if (Agent.Main != null && Agent.Main.IsActive()) seats.Add(Agent.Main.Position.AsVec2);
            return seats;
        }

        private static float NearestSeat(List<Vec2> seats, Vec2 p)
        {
            float best = float.MaxValue;
            foreach (var s in seats) best = Math.Min(best, s.Distance(p));
            return best;
        }

        /// <summary>
        /// A stand for the band: walkable floor on the tables' level, 6 m or more from every seat (about 7.5 by choice),
        /// with room to stand and, if it can be had, a clear line to the tables so they play to the diners and not to a wall.
        /// </summary>
        private static bool FindBandSpot(Mission mission, List<Vec2> seats, Vec3 tableAt, out Vec3 spot)
        {
            var scene = mission.Scene;
            float floor = tableAt.z;
            var cands = new List<BandCandidate>();
            for (float r = 4f; r <= 24f; r += 1.5f)
            for (int k = 0; k < 24; k++)
            {
                var p2 = tableAt.AsVec2 + Vec2.FromRotation(k * 0.2618f) * r;
                float d = NearestSeat(seats, p2);
                if (d < BandMinDistance) continue;
                var p = Ground(scene, new Vec3(p2.x, p2.y, floor));
                if (Math.Abs(p.z - floor) > 0.5f || !Walkable(scene, p)) continue;
                cands.Add(new BandCandidate { P = p, D = d });
            }
            var eye = new Vec3(tableAt.x, tableAt.y, floor + 1.4f);
            foreach (var (minD, needSight) in new[] { (BandIdealDistance - 0.5f, true), (BandMinDistance, true), (BandMinDistance, false) })
                foreach (var c in cands.Where(x => x.D >= minD).OrderBy(x => Math.Abs(x.D - BandIdealDistance)))
                {
                    if (!c.Room.HasValue)
                        c.Room = !mission.Agents.Any(a => a.IsActive() && a.Position.Distance(c.P) < 1.6f) && ClearAround(scene, c.P, 1.4f);
                    if (!c.Room.Value) continue;
                    if (needSight)
                    {
                        if (!c.Sees.HasValue)
                            c.Sees = !scene.RayCastForClosestEntityOrTerrain(new Vec3(c.P.x, c.P.y, floor + 1.4f), eye, out float _, 0.05f);
                        if (!c.Sees.Value) continue;
                    }
                    spot = c.P;
                    return true;
                }
            spot = tableAt;
            return false;
        }

        /// <summary>A dancer's place off the band: ahead of it toward the tables, but never nearer a seat than 4 m.</summary>
        private static bool DancerSpot(Scene scene, List<Vec2> seats, Vec3 band, float bandD, Vec2 facing, Vec2 across, float off, out Vec3 at)
        {
            for (float ahead = Math.Min(3f, bandD - DancerMinDistance - 0.3f); ahead >= 1f; ahead -= 0.5f)
            {
                var p2 = band.AsVec2 + facing * ahead + across * off;
                if (NearestSeat(seats, p2) < DancerMinDistance) continue;
                var p = Ground(scene, new Vec3(p2.x, p2.y, band.z));
                if (Math.Abs(p.z - band.z) > 0.5f || !Walkable(scene, p)) continue;
                at = p;
                return true;
            }
            var p3 = band.AsVec2 + facing * 1f + across * off;
            at = Ground(scene, new Vec3(p3.x, p3.y, band.z));
            return NearestSeat(seats, p3) >= DancerMinDistance && Walkable(scene, at);
        }

        /// <summary>Nothing solid within r of the spot at waist height, all round.</summary>
        private static bool ClearAround(Scene scene, Vec3 p, float r)
        {
            for (int j = 0; j < 8; j++)
            {
                var d = Vec2.FromRotation(j * 0.785f);
                var from = new Vec3(p.x, p.y, p.z + 0.9f);
                var to = new Vec3(p.x + d.x * r, p.y + d.y * r, p.z + 0.9f);
                if (scene.RayCastForClosestEntityOrTerrain(from, to, out float _, 0.05f)) return false;
            }
            return true;
        }

        /// <summary>A lord's hall: a patch of open, level floor near the host for the band.</summary>
        private static bool OpenFloor(Mission mission, Vec3 near, out Vec3 spot)
        {
            var scene = mission.Scene;
            foreach (float r in new[] { 3.5f, 5f, 6.5f, 8f })
            for (int k = 0; k < 16; k++)
            {
                var dir = Vec2.FromRotation(k * 0.3927f);
                var p2 = near.AsVec2 + dir * r;
                var p = Ground(scene, new Vec3(p2.x, p2.y, near.z));
                if (Math.Abs(p.z - near.z) > 0.6f || !Walkable(scene, p)) continue;
                if (mission.Agents.Any(a => a.IsActive() && a.Position.Distance(p) < 1.6f)) continue;
                if (!ClearAround(scene, p, 1.4f)) continue;
                spot = p;
                return true;
            }
            spot = near;
            return false;
        }

        private void TickStaff(Feast f, Mission mission)
        {
            if (_time < f.Staff.NextTickAt) return;
            f.Staff.NextTickAt = _time + 0.5f;
            try
            {
                TickMusic(f, mission);
            }
            catch (Exception ex) { LmmiLog.Error("Feast: the help's tick threw", ex); }
        }

        // ---- The band: the settlement's own tunes, as vanilla's musicians play them (MusicianGroup) ----

        private void TickMusic(Feast f, Mission mission)
        {
            var st = f.Staff;
            if (st.Musicians.Count == 0) return;
            if (st.Track != null)
            {
                bool playing;
                try { playing = st.Track.IsPlaying(); } catch { playing = false; }
                if (playing) return;
                // Between tunes: a breather (vanilla leaves 8 s), instruments down, dancers catch their breath.
                StopMusic(f);
                st.NextTrackAt = _time + 8f;
                foreach (var m in st.Musicians) StreetEventsBehavior.Direct(m)?.Hold(f.HostAgent, "act_musician_idle_stand_calm");
                foreach (var d in st.Dancers) StreetEventsBehavior.Direct(d)?.Hold(f.HostAgent, "act_musician_idle_stand_cheerful");
                return;
            }
            if (_time < st.NextTrackAt) return;
            StartTrack(f, mission);
        }

        private void StartTrack(Feast f, Mission mission)
        {
            var st = f.Staff;
            st.NextTrackAt = float.MaxValue;   // until this one ends (or for good, if there's no music to be had)
            var track = PickTrack(f);
            var instruments = track?.Instruments.Where(i => i.InstrumentEntities.Count > 0).ToList() ?? new List<InstrumentData>();
            if (instruments.Count == 0)
                instruments = MBObjectManager.Instance.GetObjectTypeList<InstrumentData>().Where(i => i.InstrumentEntities.Count > 0).ToList();
            for (int i = 0; i < st.Musicians.Count; i++)
            {
                var m = st.Musicians[i];
                if (!m.IsActive() || instruments.Count == 0) continue;
                var instrument = instruments[i % instruments.Count];
                Equip(st, m, instrument);
                StreetEventsBehavior.Direct(m)?.Hold(f.HostAgent, instrument.StandingAction);
            }
            foreach (var d in st.Dancers) StreetEventsBehavior.Direct(d)?.Hold(f.HostAgent, "act_dance_norse");
            if (track == null) { LmmiLog.Info("Feast: no tune to be had — the band mimes."); return; }
            try
            {
                int id = SoundEvent.GetEventIdFromString(track.MusicPath);
                var ev = SoundEvent.CreateEvent(id, mission.Scene);
                ev.SetPosition(st.BandAt);
                ev.Play();
                st.Track = ev;
                LmmiLog.Info($"Feast: the band strikes up '{track.StringId}'.");
            }
            catch (Exception ex)
            {
                LmmiLog.Error("Feast: starting the music threw", ex);
                st.Track = null;
            }
        }

        private static SettlementMusicData? PickTrack(Feast f)
        {
            var all = MBObjectManager.Instance.GetObjectTypeList<SettlementMusicData>();
            if (all == null || all.Count == 0) return null;
            var culture = f.Settlement?.Culture ?? f.Host.Culture;
            string where = f.InHall ? "lords_hall" : "tavern";
            var pool = all.Where(t => t.Culture == culture && t.LocationId == where).ToList();
            if (pool.Count == 0) pool = all.Where(t => t.Culture == culture).ToList();
            if (pool.Count == 0) pool = all.ToList();
            return pool[MBRandom.RandomInt(pool.Count)];
        }

        /// <summary>The instrument in hand, as PlayMusicPoint does it: its props on the bones the instrument data names.</summary>
        private static void Equip(FeastStaff st, Agent musician, InstrumentData instrument)
        {
            try
            {
                var navigator = musician.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
                if (navigator == null || musician.AgentVisuals == null) return;
                if (st.Playing.TryGetValue(musician, out var old))
                {
                    if (old == instrument) return;
                    foreach (var (bone, prefab) in old.InstrumentEntities)
                        navigator.SetPrefabVisibility(musician.AgentVisuals.GetRealBoneIndex(bone), prefab, false);
                }
                foreach (var (bone, prefab) in instrument.InstrumentEntities)
                    navigator.SetPrefabVisibility(musician.AgentVisuals.GetRealBoneIndex(bone), prefab, true);
                st.Playing[musician] = instrument;
            }
            catch (Exception ex) { LmmiLog.Error("Feast: handing out an instrument threw", ex); }
        }

        private static void StopMusic(Feast f)
        {
            var ev = f.Staff.Track;
            f.Staff.Track = null;
            if (ev == null) return;
            try { ev.Stop(); ev.Release(); }
            catch (Exception ex) { LmmiLog.Error("Feast: stopping the music threw", ex); }
        }

        // ---- A word with the maid ----

        private bool TalkingToMaid()
        {
            var f = _feast;
            var who = SandBox.Conversation.ConversationMission.OneToOneConversationAgent;
            return f != null && (f.Phase == Phase.Feasting || f.Phase == Phase.Lingering) && who != null && who == f.Staff.Maid;
        }

        private void AddStaffDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_feast_maid", "start", "lmmi_feast_maid_resp",
                "{=!}{LMMI_FEAST_MAID}", () => TalkingToMaid() && Flavor.Say("LMMI_FEAST_MAID",
                        "{=lmmi_feast_maid}More wine, {?PLAYER.GENDER}my lady{?}my lord{\\?}?",
                        "{=lmmi_feast_maid_2}Another cup, {?PLAYER.GENDER}my lady{?}my lord{\\?}? There's plenty.",
                        "{=lmmi_feast_maid_3}Thirsty? The wine's good tonight — the master opened the old barrel.",
                        "{=lmmi_feast_maid_4}Can I fill that for you?"), null, 1300);

            starter.AddPlayerLine("lmmi_feast_maid_fill", "lmmi_feast_maid_resp", "lmmi_feast_maid_fill_resp",
                "{=lmmi_feast_maid_fill}Fill it to the brim.", null, null);
            starter.AddDialogLine("lmmi_feast_maid_fill_resp", "lmmi_feast_maid_fill_resp", "close_window",
                "{=!}{LMMI_FEAST_MAID_FILL}",
                    () => Flavor.Say("LMMI_FEAST_MAID_FILL",
                        "{=lmmi_feast_maid_fill_resp}There you are. Mind the floor — it starts to tilt after the third cup.",
                        "{=lmmi_feast_maid_fill_resp_2}Right to the brim! Careful how you carry it.",
                        "{=lmmi_feast_maid_fill_resp_3}There. Don't tell the cook I gave you the good stuff."), null);

            starter.AddPlayerLine("lmmi_feast_maid_herself", "lmmi_feast_maid_resp", "lmmi_feast_maid_herself_resp",
                "{=lmmi_feast_maid_herself}Pour one for yourself, too.", null, null);
            starter.AddDialogLine("lmmi_feast_maid_herself_resp", "lmmi_feast_maid_herself_resp", "close_window",
                "{=!}{LMMI_FEAST_MAID_HERSELF}",
                    () => Flavor.Say("LMMI_FEAST_MAID_HERSELF",
                        "{=lmmi_feast_maid_herself_resp}Ha! Not while the master's watching. ...Maybe later.",
                        "{=lmmi_feast_maid_herself_resp_2}Oh, I couldn't! ...Well. One sip.",
                        "{=lmmi_feast_maid_herself_resp_3}If I drank with every guest, I'd be under the table by now."), null);

            starter.AddPlayerLine("lmmi_feast_maid_water", "lmmi_feast_maid_resp", "lmmi_feast_maid_water_resp",
                "{=lmmi_feast_maid_water}Just water for me, if you have it.", null, null);
            starter.AddDialogLine("lmmi_feast_maid_water_resp", "lmmi_feast_maid_water_resp", "close_window",
                "{=!}{LMMI_FEAST_MAID_WATER}",
                    () => Flavor.Say("LMMI_FEAST_MAID_WATER",
                        "{=lmmi_feast_maid_water_resp}Water? At a feast? ...I'll see what I can find.",
                        "{=lmmi_feast_maid_water_resp_2}Water... I think there's some in the kitchen. Somewhere.",
                        "{=lmmi_feast_maid_water_resp_3}Water! You'll be the only sober one here. Very wise."), null);

            starter.AddPlayerLine("lmmi_feast_maid_no", "lmmi_feast_maid_resp", "close_window",
                "{=lmmi_feast_maid_no}Not just now, thank you.", null, null);
        }
    }
}
