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
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// A castle is a garrison, not a market. In the courtyard: a master-at-arms with three of his men waiting around a
    /// practice ring (bouts with fists or wooden blades, a wager on each, champion of the yard for the one who beats all
    /// three; your companions trained, your own garrison drilled) and the castellan (news from the roads, the lord's
    /// whereabouts, prisoners of note, bandits about, the castle itself, veterans the lord can spare; for your own castle
    /// a report, a doubled watch, stores laid in). Around them the garrison lives: a sergeant drilling recruits, a
    /// prisoner walked across the yard, dice by the wall, a courier with news — played by the men already in the yard,
    /// with anyone extra coming in out of sight. (Bouts: CastleYardBouts.cs; the castellan: CastleCastellan.cs.)
    /// </summary>
    public partial class CastleLifeBehavior : CampaignBehaviorBase
    {
        private sealed class Vignette
        {
            public string Kind = "";
            public readonly List<Agent> Spawned = new List<Agent>();    // brought in out of sight: walk off out of sight after
            public readonly List<Agent> Borrowed = new List<Agent>();   // from the yard: handed back to their routine after
            public readonly List<Agent> Cast = new List<Agent>();       // must reach their marks before it plays
            public float StartedAt;
            public float ArrivedAt = -1f;
            public float Length;
            public readonly List<(float After, Agent Who, TextObject Line)> Barks = new List<(float, Agent, TextObject)>();
            public Action? OnArrive;
            public Action<Vignette>? Tick;
            public Action? AtEnd;
            public bool Over;
        }

        private const string LoopSitFloor = "act_conversation_sit_floor_loop";

        [NonSerialized] private Dictionary<string, double> _readyAtHours = new Dictionary<string, double>();
        [NonSerialized] private Mission? _mission;
        [NonSerialized] private float _time;
        [NonSerialized] private bool _staged;
        [NonSerialized] private Settlement? _castle;
        [NonSerialized] private Agent? _master, _castellan;
        [NonSerialized] private bool _masterBarked, _castellanBarked;
        [NonSerialized] private float _nextVignetteAt;
        [NonSerialized] private Vignette? _vignette;
        [NonSerialized] private readonly List<Action> _afterTalk = new List<Action>();
        [NonSerialized] private readonly List<(float At, Action Do)> _timed = new List<(float, Action)>();
        [NonSerialized] private readonly CastleStaging.Leavers _leavers = new CastleStaging.Leavers();
        [NonSerialized] private List<(CharacterObject Character, int Number)> _veterans = new List<(CharacterObject, int)>();
        [NonSerialized] private int _veteranCost;

        private static CastleLifeBehavior? Instance => Campaign.Current?.GetCampaignBehavior<CastleLifeBehavior>();

        /// <summary>Something of ours is going on in the courtyard.</summary>
        public static bool IsBusy => Instance?._vignette != null || Instance?._bout != null;

        public static void ClearCooldowns() => Instance?._readyAtHours.Clear();

        public override void RegisterEvents()
        {
            CampaignEvents.MissionTickEvent.AddNonSerializedListener(this, OnMissionTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        // ---- The courtyard ----

        private static bool InCourtyard(out Settlement castle)
        {
            castle = Settlement.CurrentSettlement!;
            return castle != null && castle.IsCastle && CampaignMission.Current?.Location?.StringId == "center";
        }

        private void OnMissionTick(float dt)
        {
            try
            {
                var mission = Mission.Current;
                if (mission == null) return;
                if (mission != _mission)
                {
                    _mission = mission;
                    _time = 0f;
                    _staged = false;
                    _master = _castellan = null;
                    _masterBarked = _castellanBarked = false;
                    _vignette = null;
                    ResetYard();
                    _afterTalk.Clear();
                    _timed.Clear();
                    _leavers.Clear();
                    _nextVignetteAt = 25f + MBRandom.RandomFloat * 20f;
                }
                _time += dt;

                if (_afterTalk.Count > 0 && mission.Mode != MissionMode.Conversation
                    && Campaign.Current?.ConversationManager?.IsConversationInProgress != true)
                {
                    var actions = _afterTalk.ToList();
                    _afterTalk.Clear();
                    foreach (var a in actions)
                    {
                        try { a(); }
                        catch (Exception ex) { LmmiLog.Error("Castle: after-conversation action threw", ex); }
                    }
                }
                if (_timed.Count > 0 && mission.Mode != MissionMode.Conversation)
                {
                    var due = _timed.Where(t => t.At <= _time).ToList();
                    foreach (var t in due)
                    {
                        _timed.Remove(t);
                        try { t.Do(); }
                        catch (Exception ex) { LmmiLog.Error("Castle: a timed action threw", ex); }
                    }
                }
                _leavers.Tick(mission, _time);

                if (!LmmiSettingsProvider.EnableCastleLife) return;
                var main = Agent.Main;
                if (main == null || !main.IsActive() || !InCourtyard(out var castle)) return;
                _castle = castle;

                if (!_staged && _time > 1f && mission.Mode != MissionMode.Conversation) Stage(mission, castle);

                // They notice you coming.
                if (_master != null && _master.IsActive() && !_masterBarked && _bout == null && _master.Position.Distance(main.Position) < 7f)
                {
                    _masterBarked = true;
                    StreetEventsBehavior.Bark(_master, new TextObject(castle.OwnerClan == Clan.PlayerClan
                        ? "{=lmmi_castle_master_hail_owner}Shields up — the {?PLAYER.GENDER}lady{?}lord{\\?}'s watching!"
                        : "{=lmmi_castle_master_hail}You there! Fancy yourself with your fists? The lads could use a new face to knock down."));
                }
                if (_castellan != null && _castellan.IsActive() && !_castellanBarked && _castellan.Position.Distance(main.Position) < 6f)
                {
                    _castellanBarked = true;
                    var hail = new TextObject(castle.OwnerClan == Clan.PlayerClan
                        ? "{=lmmi_castle_castellan_hail_owner}My {?PLAYER.GENDER}lady{?}lord{\\?}. Welcome home."
                        : "{=lmmi_castle_castellan_hail}The castellan of {CASTLE}. If you've business, I'm the one to see.");
                    hail.SetTextVariable("CASTLE", castle.Name);
                    StreetEventsBehavior.Bark(_castellan, hail);
                }

                TickBout(mission);
                TickVignette(mission, castle);
            }
            catch (Exception ex)
            {
                LmmiLog.Error("CastleLifeBehavior.OnMissionTick threw", ex);
                _staged = true;
            }
        }

        private void At(float delay, Action what) => _timed.Add((_time + delay, what));

        /// <summary>The garrison's men the lord keeps here — or, lacking a garrison, the culture's regulars.</summary>
        private static List<CharacterObject> GarrisonTroops(Settlement castle)
        {
            var roster = castle.Town?.GarrisonParty?.MemberRoster;
            var list = roster != null
                ? roster.GetTroopRoster().Where(e => e.Character != null && !e.Character.IsHero && e.Number - e.WoundedNumber > 0)
                    .Select(e => e.Character).ToList()
                : new List<CharacterObject>();
            if (list.Count == 0 && castle.Culture != null)
            {
                var (best, lesser) = HiredSwordsBehavior.TroopsOf(castle.Culture, 3);
                list.AddRange(best);
                list.AddRange(lesser);
            }
            return list;
        }

        private static int Tier(Agent a) => (a.Character as CharacterObject)?.Tier ?? 0;

        private void Stage(Mission mission, Settlement castle)
        {
            _staged = true;
            var main = Agent.Main;
            var troops = GarrisonTroops(castle);
            if (troops.Count == 0) return;
            var byTier = troops.OrderByDescending(t => t.Tier).ToList();

            // The practice ring: the master-at-arms at its edge, three of his men around it.
            try { StageRing(mission, castle, byTier[0]); }
            catch (Exception ex) { LmmiLog.Error("Castle: staging the ring threw", ex); }

            // The castellan: somewhere else in the yard, a little way in from where you stand.
            var anchors = mission.Agents
                .Where(a => a.IsActive() && a.IsHuman && a != main && a.Position.Distance(main.Position) > 8f && a.Position.Distance(main.Position) < 30f
                            && (!_hasRing || a.Position.Distance(_ringCenter) > 9f))
                .OrderBy(a => a.Position.Distance(main.Position)).ToList();
            var castellanAt = anchors.Count > 1 ? anchors[1].Position : anchors.Count > 0 ? anchors[0].Position : main.Position + main.LookDirection * 10f;
            castellanAt = CastleStaging.Ground(mission, castellanAt + new Vec3(-1.5f, -0.75f, 0f));
            var face = (main.Position - castellanAt).AsVec2;
            _castellan = SceneSpawner.Spawn(mission, byTier.Count > 1 ? byTier[1] : byTier[0], castellanAt,
                face.LengthSquared < 0.01f ? 0f : face.Normalized().RotationInRadians, civilian: true);
            if (_castellan != null) StreetEventsBehavior.Direct(_castellan)?.Hold(face: main, loop: "act_conversation_closed_loop");
            LmmiLog.Info($"Castle: {castle.Name} — master-at-arms {_master?.Name}, castellan {_castellan?.Name}, ring {string.Join(", ", _ring.Select(r => $"{r.Troop.Name} (tier {r.Troop.Tier})"))} ({troops.Count} kinds of troop in the garrison).");
        }

        // ---- The garrison's day ----

        private static bool HasArrived(Agent a)
        {
            if (!a.IsActive()) return true;
            var stage = a.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>()?.GetBehavior<LmmiStageBehavior>();
            return stage == null || stage.Arrived;
        }

        private void TickVignette(Mission mission, Settlement castle)
        {
            var main = Agent.Main;
            var v = _vignette;
            if (v != null)
            {
                v.Tick?.Invoke(v);
                if (v.Over) { EndVignette(v); return; }
                if (v.ArrivedAt < 0f)
                {
                    if (!v.Cast.All(HasArrived) && _time - v.StartedAt < 45f) return;
                    v.ArrivedAt = _time;
                    try { v.OnArrive?.Invoke(); } catch (Exception ex) { LmmiLog.Error("Castle: vignette arrival threw", ex); }
                }
                foreach (var b in v.Barks.Where(b => _time >= v.ArrivedAt + b.After).ToList())
                {
                    v.Barks.Remove(b);
                    if (b.Who.IsActive() && b.Who.Position.Distance(main.Position) < 25f) StreetEventsBehavior.Bark(b.Who, b.Line);
                }
                if (_time >= v.ArrivedAt + v.Length) EndVignette(v);
                return;
            }
            if (_time < _nextVignetteAt || _bout != null || mission.Mode == MissionMode.Conversation) return;
            if (mission.GetMissionBehavior<SandBox.Missions.MissionLogics.MissionFightHandler>()?.IsThereActiveFight() == true) return;
            _nextVignetteAt = _time + 60f + MBRandom.RandomFloat * 40f;
            var troops = GarrisonTroops(castle);
            if (troops.Count == 0) return;
            int pick = MBRandom.RandomInt(4);
            try
            {
                _vignette = pick switch
                {
                    0 => Drill(mission, troops),
                    1 => Prisoner(mission, castle, troops) ?? Dice(mission, troops),
                    2 => Dice(mission, troops),
                    _ => Courier(mission, castle, troops),
                };
                if (_vignette != null) LmmiLog.Info($"Castle: {_vignette.Kind} — {_vignette.Borrowed.Count} from the yard, {_vignette.Spawned.Count} brought in out of sight.");
            }
            catch (Exception ex) { LmmiLog.Error("Castle: a vignette threw", ex); _vignette = null; }
        }

        private void EndVignette(Vignette v)
        {
            if (_vignette == v) _vignette = null;
            try { v.AtEnd?.Invoke(); } catch (Exception ex) { LmmiLog.Error("Castle: vignette end threw", ex); }
            foreach (var a in v.Borrowed) StreetEventsBehavior.Release(a);
            var mission = Mission.Current;
            foreach (var a in v.Spawned.Where(a => a.IsActive()))
            {
                if (mission != null) _leavers.Add(mission, a, _time);
                else a.FadeOut(true, true);
            }
        }

        /// <summary>A vignette that couldn't be cast: give back who we took, send off who we brought.</summary>
        private Vignette? Abandon(Vignette v)
        {
            v.AtEnd = null;
            EndVignette(v);
            return null;
        }

        /// <summary>A spot in the yard <paramref name="min"/>-<paramref name="max"/> m from you where someone's standing, clear of the ring.</summary>
        private bool YardSpot(Mission mission, float min, float max, out Vec3 at)
        {
            var main = Agent.Main;
            var a = mission.Agents.Where(x => x.IsActive() && x.IsHuman && x != main && x.Position.Distance(main.Position) > min && x.Position.Distance(main.Position) < max
                                              && Math.Abs(x.Position.z - main.Position.z) < 3f
                                              && (!_hasRing || x.Position.Distance(_ringCenter) > 9f)
                                              && (_castellan == null || x.Position.Distance(_castellan.Position) > 5f))
                .OrderBy(_ => MBRandom.RandomFloat).FirstOrDefault();
            at = a?.Position ?? Vec3.Zero;
            return a != null;
        }

        private static CharacterObject Any(List<CharacterObject> troops) => troops[MBRandom.RandomInt(troops.Count)];

        /// <summary>
        /// Men for a vignette: free soldiers already in the yard first (they walk over); only if there aren't enough,
        /// more of the garrison come in from somewhere you can't see, and walk over too.
        /// </summary>
        private List<Agent> CastFor(Mission mission, Vignette v, Vec3 at, int want, int need, Func<CharacterObject> extra)
        {
            var people = CastleStaging.FreeSoldiers(mission, at, 45f, want);
            v.Borrowed.AddRange(people);
            while (people.Count < want)
            {
                var who = CastleStaging.SpawnHidden(mission, extra(), civilian: false, headingTo: at);
                if (who == null) break;
                v.Spawned.Add(who);
                people.Add(who);
            }
            return people.Count >= need ? people : new List<Agent>();
        }

        /// <summary>A sergeant bawling at a line of recruits.</summary>
        private Vignette? Drill(Mission mission, List<CharacterObject> troops)
        {
            if (!YardSpot(mission, 12f, 35f, out var at)) return null;
            var v = new Vignette { Kind = "drill", StartedAt = _time, Length = 35f };
            var lowest = troops.OrderBy(t => t.Tier).ToList();
            var people = CastFor(mission, v, at, 5, 3, () => lowest[Math.Min(MBRandom.RandomInt(2), lowest.Count - 1)]);
            if (people.Count == 0) return Abandon(v);
            var sergeant = people.OrderByDescending(Tier).First();
            var line = people.Where(a => a != sergeant).ToList();

            // The sergeant where the spot is; the line a few paces off, facing him, along whichever way is open.
            var main = Agent.Main;
            var from = sergeant.GetWorldPosition();
            from.SetVec2(at.AsVec2);
            Vec2 dir = Vec2.Forward;
            float start = MBRandom.RandomFloat * 6.283f;
            for (int k = 0; k < 8; k++)
            {
                var d = Vec2.FromRotation(start + k * 0.785f);
                if (CastleStaging.ClearLine(mission, from, at + new Vec3(d.x, d.y, 0f) * 3.5f)) { dir = d; break; }
            }
            var side = new Vec2(-dir.y, dir.x);
            CastleStaging.WalkTo(sergeant, at, face: line[line.Count / 2], loop: "act_conversation_aggressive_loop");
            for (int i = 0; i < line.Count; i++)
            {
                var p2 = at.AsVec2 + dir * 3f + side * ((i - (line.Count - 1) / 2f) * 1.1f);
                CastleStaging.WalkTo(line[i], CastleStaging.Ground(mission, new Vec3(p2.x, p2.y, at.z)), face: sergeant, loop: "act_conversation_closed2_loop");
            }
            v.Cast.AddRange(people);
            v.Barks.Add((2f, sergeant, new TextObject("{=lmmi_castle_drill_1}Shields UP! Up, I said! You call that a shield wall? My grandmother holds a wall better than that!")));
            v.Barks.Add((12f, line[0], new TextObject("{=lmmi_castle_drill_2}Yes, sergeant! Sorry, sergeant!")));
            v.Barks.Add((20f, sergeant, new TextObject("{=lmmi_castle_drill_3}Again! Until your arms fall off — and then again!")));
            return v;
        }

        /// <summary>A prisoner from the cells, walked across the yard under guard and back out of sight.</summary>
        private Vignette? Prisoner(Mission mission, Settlement castle, List<CharacterObject> troops)
        {
            var prisoners = castle.Party?.PrisonRoster?.GetTroopRoster().Where(e => e.Character != null && !e.Character.IsHero && e.Number > 0).ToList();
            if (prisoners == null || prisoners.Count == 0) return null;
            var main = Agent.Main;
            // Out of the cells (somewhere you can't see), across the yard where you can, and off out of sight again.
            if (!CastleStaging.HiddenSpot(mission, 15f, 55f, out var from)) return null;
            if (!YardSpot(mission, 8f, 25f, out var across)) return null;
            if (!CastleStaging.HiddenSpot(mission, 15f, 60f, out var to, awayFrom: from)) return null;
            var v = new Vignette { Kind = "prisoner", StartedAt = _time, Length = 80f };
            var who = prisoners[MBRandom.RandomInt(prisoners.Count)].Character;
            var prisoner = SceneSpawner.Spawn(mission, who, from, 0f, civilian: true);
            if (prisoner == null) return null;
            v.Spawned.Add(prisoner);
            // His guard: one of the yard's men if one's near the cells, else one who comes out with him.
            var guard = CastleStaging.FreeSoldiers(mission, from, 25f, 1).FirstOrDefault();
            if (guard != null) v.Borrowed.Add(guard);
            else
            {
                guard = SceneSpawner.Spawn(mission, Any(troops), CastleStaging.Ground(mission, from + new Vec3(1.2f, 0f, 0f)), 0f, civilian: false);
                if (guard == null) return Abandon(v);
                v.Spawned.Add(guard);
            }
            StreetEventsBehavior.Direct(prisoner)?.Hold(loop: "act_dungeon_prisoner_idle_exhausted");
            StreetEventsBehavior.Direct(guard)?.Follow(prisoner, 1.3f, run: false);
            int phase = 0;     // 0 waiting for the guard, 1 crossing, 2 leaving
            float phaseAt = _time;
            bool barked = false;
            v.Tick = vv =>
            {
                if (!prisoner.IsActive()) { vv.Over = true; return; }
                if (phase == 0 && (!guard.IsActive() || guard.Position.Distance(prisoner.Position) < 3f || _time - phaseAt > 20f))
                {
                    phase = 1;
                    phaseAt = _time;
                    CastleStaging.WalkTo(prisoner, across, speed: 0.6f);
                }
                else if (phase == 1 && (HasArrived(prisoner) || _time - phaseAt > 40f))
                {
                    phase = 2;
                    phaseAt = _time;
                    CastleStaging.WalkTo(prisoner, to, speed: 0.6f);
                }
                else if (phase == 2 && (HasArrived(prisoner) || _time - phaseAt > 40f)) vv.Over = true;
                if (!barked && phase >= 1 && main != null && prisoner.Position.Distance(main.Position) < 18f)
                {
                    barked = true;
                    float now = _time - vv.ArrivedAt;
                    vv.Barks.Add((now, prisoner, new TextObject("{=lmmi_castle_prisoner_1}Water... please, just a mouthful...")));
                    vv.Barks.Add((now + 5f, guard, new TextObject("{=lmmi_castle_prisoner_2}Keep walking. You'll get your water when the lord says.")));
                }
            };
            LmmiLog.Info($"Castle: a prisoner ({who.Name}) is walked across the yard.");
            return v;
        }

        /// <summary>Off-duty men dicing by the wall.</summary>
        private Vignette? Dice(Mission mission, List<CharacterObject> troops)
        {
            if (!YardSpot(mission, 10f, 30f, out var at)) return null;
            var v = new Vignette { Kind = "dice", StartedAt = _time, Length = 40f };
            var men = CastFor(mission, v, at, 3, 2, () => Any(troops));
            if (men.Count == 0) return Abandon(v);
            for (int i = 0; i < men.Count; i++)
            {
                var dir = Vec2.FromRotation(i * 6.283f / men.Count);
                var seat = CastleStaging.Ground(mission, at + new Vec3(dir.x, dir.y, 0f) * 0.9f);
                CastleStaging.WalkTo(men[i], seat, face: men[(i + 1) % men.Count], loop: LoopSitFloor);
            }
            v.Cast.AddRange(men);
            v.Barks.Add((4f, men[0], new TextObject("{=lmmi_castle_dice_1}Sixes! Pay up, the lot of you.")));
            v.Barks.Add((14f, men[1], new TextObject("{=lmmi_castle_dice_2}Those dice are loaded, I swear it.")));
            v.Barks.Add((24f, men[0], new TextObject("{=lmmi_castle_dice_3}Loaded with luck, friend. Another round?")));
            return v;
        }

        /// <summary>A courier runs in with news for the castellan — and you overhear it.</summary>
        private Vignette? Courier(Mission mission, Settlement castle, List<CharacterObject> troops)
        {
            var main = Agent.Main;
            var target = _castellan != null && _castellan.IsActive() ? _castellan : null;
            if (target == null) return null;
            var v = new Vignette { Kind = "courier", StartedAt = _time, Length = 14f };
            // One of the yard's men from the far side (out of sight if any), else a rider in from the gate where you can't see.
            var courier = CastleStaging.FreeSoldiers(mission, target.Position, 70f, 20)
                .Where(a => a.Position.Distance(target.Position) > 20f)
                .OrderByDescending(a => CastleStaging.OutOfSight(mission, a.Position) ? 1 : 0)
                .ThenByDescending(a => a.Position.Distance(target.Position))
                .FirstOrDefault();
            if (courier != null) v.Borrowed.Add(courier);
            else
            {
                courier = CastleStaging.SpawnHidden(mission, troops.OrderBy(t => t.Tier).First(), civilian: false, headingTo: target.Position, min: 20f);
                if (courier == null) return null;
                v.Spawned.Add(courier);
            }
            StreetEventsBehavior.Direct(courier)?.Follow(target, 1.8f, run: true);
            v.Cast.Add(courier);
            var news = News(castle);
            v.OnArrive = () => StreetEventsBehavior.Direct(target)?.Hold(face: courier, loop: "act_conversation_closed_loop");
            v.Barks.Add((0.5f, courier, new TextObject("{=lmmi_castle_courier_1}Riders from the border, castellan! News!")));
            v.Barks.Add((7f, target, new TextObject("{=lmmi_castle_courier_2}Catch your breath, lad. Then tell the sergeant — and get yourself some bread.")));
            v.AtEnd = () =>
            {
                if (target.IsActive() && main != null && main.IsActive())
                {
                    StreetEventsBehavior.Direct(target)?.Hold(face: main, loop: "act_conversation_closed_loop");
                    if (target.Position.Distance(main.Position) < 25f)
                    {
                        var heard = new TextObject("{=lmmi_castle_courier_heard}You overhear the courier's report: {NEWS}");
                        heard.SetTextVariable("NEWS", news);
                        InformationManager.DisplayMessage(new InformationMessage(heard.ToString(), Colors.Yellow));
                    }
                }
            };
            return v;
        }

        /// <summary>What the castle knows of the roads: the nearest hostile host, or quiet.</summary>
        internal static TextObject News(Settlement place)
        {
            var at = place.GatePosition;
            var hostile = MobileParty.All
                .Where(p => p.IsActive && p.IsLordParty && p.LeaderHero != null && p.MapFaction != null && p.CurrentSettlement == null
                            && (FactionManager.IsAtWarAgainstFaction(p.MapFaction, place.MapFaction)
                                || (Hero.MainHero.MapFaction != null && FactionManager.IsAtWarAgainstFaction(p.MapFaction, Hero.MainHero.MapFaction)))
                            && (p.Army == null || p.Army.LeaderParty == p)
                            && p.Position.Distance(at) < 150f)
                .OrderBy(p => p.Position.Distance(at)).FirstOrDefault();
            if (hostile == null)
                return new TextObject("{=lmmi_castle_news_quiet}The roads are quiet. Too quiet, if you ask me.");
            var near = SettlementHelper.FindNearestSettlementToPoint(hostile.Position, s => s.IsTown || s.IsCastle || s.IsVillage);
            int men = hostile.Army != null ? (int)hostile.Army.TotalManCount : hostile.MemberRoster.TotalManCount;
            var line = new TextObject(hostile.Army != null
                ? "{=lmmi_castle_news_army}{LEADER}'s army — {MEN} men, more or less — was seen near {PLACE}."
                : "{=lmmi_castle_news_party}{LEADER} is riding near {PLACE} with {MEN} men.");
            line.SetTextVariable("LEADER", hostile.LeaderHero.Name);
            line.SetTextVariable("MEN", men);
            line.SetTextVariable("PLACE", near?.Name ?? new TextObject("{=lmmi_castle_news_wilds}the wilds"));
            return line;
        }

        // ---- Shared ----

        private bool TalkingTo(Agent? who) => who != null && _castle != null && ConversationMission.OneToOneConversationAgent == who;

        private void Choose(Action then)
        {
            _afterTalk.Add(then);
        }

        private static List<Hero> Companions() => MobileParty.MainParty.MemberRoster.GetTroopRoster()
            .Where(e => e.Character != null && e.Character.IsHero && e.Character.HeroObject != Hero.MainHero && e.Character.HeroObject.IsAlive)
            .Select(e => e.Character.HeroObject).ToList();

        private static SkillObject BestCombatSkill(Hero h)
        {
            var combat = new[] { DefaultSkills.OneHanded, DefaultSkills.TwoHanded, DefaultSkills.Polearm, DefaultSkills.Bow, DefaultSkills.Crossbow, DefaultSkills.Throwing, DefaultSkills.Riding, DefaultSkills.Athletics };
            return combat.OrderByDescending(h.GetSkillValue).First();
        }

        private int TrainCost => Companions().Count * 100;

        private bool Ready(string key) => LmmiSettingsProvider.TestMode || !_readyAtHours.TryGetValue(key, out var at) || at <= CampaignTime.Now.ToHours;
        private void Cooldown(string key, float days) => _readyAtHours[key] = CampaignTime.Now.ToHours + days * CampaignTime.HoursInDay;

        /// <summary>Not again until the next day begins.</summary>
        private void UntilTomorrow(string key) => _readyAtHours[key] = (Math.Floor(CampaignTime.Now.ToDays) + 1.0) * CampaignTime.HoursInDay;

        // ---- Dialogs ----

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try
            {
                AddMasterDialogs(starter);
                AddCastellanDialogs(starter);
            }
            catch (Exception ex) { LmmiLog.Error("CastleLifeBehavior: failed to register dialogs", ex); }
        }

        // ---- Save / load: "key;readyAtHours" separated by '|' ----

        public override void SyncData(IDataStore dataStore)
        {
            string data = string.Empty;
            if (!dataStore.IsLoading)
            {
                double now = CampaignTime.Now.ToHours;
                data = string.Join("|", _readyAtHours.Where(kv => kv.Value > now)
                    .Select(kv => kv.Key + ";" + kv.Value.ToString("R", CultureInfo.InvariantCulture)));
            }
            dataStore.SyncData("lmmi_castle_v1", ref data);
            if (!dataStore.IsLoading) return;
            _readyAtHours = new Dictionary<string, double>();
            foreach (var entry in (data ?? string.Empty).Split('|'))
            {
                var p = entry.Split(';');
                if (p.Length == 2 && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var at))
                    _readyAtHours[p[0]] = at;
            }
        }
    }
}
