using System;
using System.Collections.Generic;
using System.Linq;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using SandBox.Conversation;
using SandBox.Missions.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// The practice ring. From the moment you walk into the yard the master-at-arms stands at its edge with three of his
    /// men around it, greenest to best. Ask for a bout and you fight them in order — fists, or wooden blades the way the
    /// training field does it (a wooden sword and a shield, a wooden twohander or a blunt practice spear, to suit each
    /// fighter) — and the yard gathers round to cheer. First one down loses: nobody is killed or knocked senseless (a
    /// guard on both fighters' health stops the fall at the last point; the bout is called there), your own weapons and
    /// your own bruises are handed back after. Each bout can carry a wager at odds set by the fighter's tier. Beat the
    /// third and you're champion of the yard (standing, a lot of practice); the master then won't take your coin at that
    /// castle for a week, though he'll still let you spar for nothing. Lose and your run's over until tomorrow.
    /// </summary>
    public partial class CastleLifeBehavior
    {
        private sealed class RingFighter
        {
            public Agent Agent = null!;
            public CharacterObject Troop = null!;
            public Vec3 Post;
            public float Facing;
            public bool Beaten;
        }

        private sealed class Bout
        {
            public RingFighter Fighter = null!;
            public Agent Foe = null!;
            public int Index;
            public bool Wooden;
            public SkillObject Style = null!;
            public int Wager;
            public float Odds;
            public float StartedAt;
            public Agent? Down;
            public bool Ending, Ended, Draw;
            public readonly MissionEquipment PlayerStash = new MissionEquipment();
            public readonly MissionEquipment FoeStash = new MissionEquipment();
            public float PlayerHealth;   // before the bout: given back after
            public float FoeLimit;       // his own health limit, before we toughened him
            public float PlayerDownAt, FoeDownAt;
            public float NextCheerAt;
            public readonly List<Agent> Watchers = new List<Agent>();
            public readonly List<Agent> Crowd = new List<Agent>();   // borrowed from the yard
            public Agent.OnAgentHealthChangedDelegate? Guard;
        }

        private static readonly string[] Cheers = { "act_cheer_1", "act_cheer_2", "act_cheer_3", "act_cheer_4", "act_cheering_low_01", "act_cheering_high_01" };
        private static readonly string[] RingLoops = { "act_conversation_warrior_loop", "act_conversation_confident_loop", "act_conversation_hip_loop" };
        private const string LoopKneel = "act_main_story_conspirator_kneel_down_1_continue";
        private const float RingRadius = 3.6f;

        [NonSerialized] private List<RingFighter> _ring = new List<RingFighter>();
        [NonSerialized] private Vec3 _ringCenter;
        [NonSerialized] private bool _hasRing;
        [NonSerialized] private Vec3 _masterPost;
        [NonSerialized] private Bout? _bout;
        [NonSerialized] private bool _pendingWooden;

        private void ResetYard()
        {
            if (_bout != null) Unguard(_bout);
            _bout = null;
            _ring = new List<RingFighter>();
            _hasRing = false;
        }

        // ---- The ring ----

        /// <summary>Three of the garrison, of rising tier: a green one, a seasoned one, the best they've got.</summary>
        private static List<CharacterObject> RingTroops(Settlement castle)
        {
            var garrison = GarrisonTroops(castle);
            var culture = castle.Culture;
            var picks = new List<CharacterObject>();
            int prev = -1;
            foreach (int target in new[] { 2, 4, 5 })
            {
                var pick = garrison.Where(t => t.Tier > prev && Math.Abs(t.Tier - target) <= 1)
                    .OrderBy(t => Math.Abs(t.Tier - target)).ThenBy(_ => MBRandom.RandomFloat).FirstOrDefault();
                if (pick == null && culture != null)
                {
                    var (best, lesser) = HiredSwordsBehavior.TroopsOf(culture, target);
                    pick = best.Concat(lesser).Where(t => t.Tier > prev)
                        .OrderBy(t => Math.Abs(t.Tier - target)).ThenBy(_ => MBRandom.RandomFloat).FirstOrDefault();
                }
                pick ??= garrison.Where(t => t.Tier > prev).OrderBy(t => t.Tier).FirstOrDefault()
                         ?? garrison.Where(t => t.Tier >= prev).OrderBy(_ => MBRandom.RandomFloat).FirstOrDefault();
                if (pick == null) break;
                picks.Add(pick);
                prev = pick.Tier;
            }
            return picks;
        }

        /// <summary>
        /// A clear patch of the yard 9-28 m in from where you stand (open in every direction the ring needs), the master
        /// at the edge nearest you, three of his men around the rest of it. Placed as you arrive, not when you ask.
        /// </summary>
        private void StageRing(Mission mission, Settlement castle, CharacterObject masterType)
        {
            var main = Agent.Main;
            var troops = RingTroops(castle);
            var centres = mission.Agents
                .Where(a => a.IsActive() && a.IsHuman && a != main && a.Position.Distance(main.Position) > 9f && a.Position.Distance(main.Position) < 28f
                            && Math.Abs(a.Position.z - main.Position.z) < 1.5f)
                .OrderBy(_ => MBRandom.RandomFloat).Take(30)
                .Select(a => a.GetWorldPosition()).ToList();
            var ahead = main.GetWorldPosition();
            var look = main.LookDirection.AsVec2;
            look = look.LengthSquared < 0.01f ? Vec2.Forward : look.Normalized();
            ahead.SetVec2(main.Position.AsVec2 + look * 12f);
            centres.Add(ahead);

            WorldPosition? bestCentre = null;
            float[]? bestAngles = null;
            int bestScore = -1;
            foreach (var c in centres)
            {
                var centre = c.GetGroundVec3();
                if (!CastleStaging.OnNavMesh(mission, centre)) continue;
                var toMain = (main.Position - centre).AsVec2;
                float baseAngle = toMain.LengthSquared < 0.01f ? 0f : toMain.Normalized().RotationInRadians;
                var angles = new[] { baseAngle, baseAngle + 1.75f, baseAngle + 3.14f, baseAngle - 1.75f };
                int score = angles.Count(an =>
                {
                    var d = Vec2.FromRotation(an);
                    return CastleStaging.ClearLine(mission, c, centre + new Vec3(d.x, d.y, 0f) * RingRadius);
                });
                if (score > bestScore) { bestScore = score; bestCentre = c; bestAngles = angles; }
                if (score == angles.Length) break;
            }
            if (bestCentre == null || bestAngles == null) return;
            _ringCenter = bestCentre.Value.GetGroundVec3();
            _hasRing = true;

            Vec3 PostAt(float angle)
            {
                var d = Vec2.FromRotation(angle);
                return CastleStaging.Ground(mission, _ringCenter + new Vec3(d.x, d.y, 0f) * RingRadius);
            }
            float FacingCentre(Vec3 from) { var d = (_ringCenter - from).AsVec2; return d.LengthSquared < 0.01f ? 0f : d.Normalized().RotationInRadians; }

            _masterPost = PostAt(bestAngles[0]);
            var toYou = (main.Position - _masterPost).AsVec2;
            _master = SceneSpawner.Spawn(mission, masterType, _masterPost, toYou.LengthSquared < 0.01f ? 0f : toYou.Normalized().RotationInRadians, civilian: false);
            if (_master != null) StreetEventsBehavior.Direct(_master)?.Hold(face: main, loop: "act_conversation_hip_loop");

            for (int i = 0; i < troops.Count && i < 3; i++)
            {
                var post = PostAt(bestAngles[i + 1]);
                float facing = FacingCentre(post);
                var man = SceneSpawner.Spawn(mission, troops[i], post, facing, civilian: true);
                if (man == null) continue;
                _ring.Add(new RingFighter { Agent = man, Troop = troops[i], Post = post, Facing = facing });
                StreetEventsBehavior.Direct(man)?.Hold(loop: RingLoops[i % RingLoops.Length]);
            }
            LmmiLog.Info($"Castle: the practice ring ({bestScore}/4 sides open), {_ring.Count} fighters.");
        }

        private int NextFighter => _ring.FindIndex(f => !f.Beaten);

        private static float OddsFor(CharacterObject troop, int index) =>
            (float)Math.Round(1.3f + 0.2f * troop.Tier + 0.15f * index, 1);

        private bool WagersRefused => _castle != null && !Ready("champ:" + _castle.StringId);
        private bool LostToday => _castle != null && !Ready("run:" + _castle.StringId);

        // ---- Practice weapons ----

        private static ItemObject? Item(string id)
        {
            try { return Game.Current?.ObjectManager?.GetObject<ItemObject>(id); }
            catch { return null; }
        }

        private static bool PracticeWeaponsExist => Item("wooden_sword_t1") != null;

        /// <summary>One-handed (with a shield), two-handed or polearm: whatever they fight best with.</summary>
        private static SkillObject StyleOf(Func<SkillObject, int> skill)
        {
            var styles = new[] { DefaultSkills.OneHanded, DefaultSkills.TwoHanded, DefaultSkills.Polearm };
            return styles.OrderByDescending(skill).First();
        }

        private static void Strip(Agent agent, MissionEquipment stash)
        {
            stash.FillFrom(agent.Equipment);
            agent.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);
            agent.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
            ClearWeapons(agent);
        }

        private static void ClearWeapons(Agent agent)
        {
            for (var i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                if (!agent.Equipment[i].IsEmpty) agent.RemoveEquippedWeapon(i);
        }

        private static void GiveBack(Agent agent, MissionEquipment stash)
        {
            if (!agent.IsActive()) return;
            agent.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);
            agent.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
            ClearWeapons(agent);
            for (var i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                var w = stash[i];
                if (!w.IsEmpty) agent.EquipWeaponWithNewEntity(i, ref w);
            }
        }

        /// <summary>The training field's kit: a wooden sword and a shield, a wooden twohander, or a blunt practice spear.</summary>
        private static bool Arm(Agent agent, SkillObject style)
        {
            ItemObject? weapon = style == DefaultSkills.TwoHanded ? Item("wooden_2hsword_t1")
                : style == DefaultSkills.Polearm ? Item("practice_spear_t1") : null;
            bool sword = weapon == null;
            weapon ??= Item("wooden_sword_t1");
            if (weapon == null) return false;
            var w = new MissionWeapon(weapon, null, null);
            agent.EquipWeaponWithNewEntity(EquipmentIndex.Weapon0, ref w);
            if (sword)
            {
                var shield = Item("bound_horsemans_kite_shield") ?? Item("old_kite_shield");
                if (shield != null)
                {
                    var s = new MissionWeapon(shield, null, null);
                    agent.EquipWeaponWithNewEntity(EquipmentIndex.Weapon1, ref s);
                }
            }
            agent.WieldInitialWeapons(Agent.WeaponWieldActionType.Instant, Equipment.InitialWeaponEquipPreference.MeleeForMainHand);
            return true;
        }

        // ---- A bout ----

        private void Refund(int wager)
        {
            if (wager > 0) Hero.MainHero.ChangeHeroGold(wager);
        }

        private void StartBout(bool wooden, int wager)
        {
            var mission = Mission.Current;
            var castle = _castle;
            var main = Agent.Main;
            var handler = mission?.GetMissionBehavior<MissionFightHandler>();
            int index = NextFighter;
            if (mission == null || castle == null || main == null || !main.IsActive() || handler == null || handler.IsThereActiveFight()
                || _bout != null || index < 0)
            {
                Refund(wager);
                return;
            }
            var fighter = _ring[index];
            var foe = fighter.Agent;
            if (foe == null || !foe.IsActive())
            {
                // He's wandered off: another of his kind comes over from wherever you can't see.
                foe = CastleStaging.SpawnHidden(mission, fighter.Troop, civilian: true, headingTo: _ringCenter) ?? SceneSpawner.Spawn(mission, fighter.Troop, fighter.Post, fighter.Facing, civilian: true);
                if (foe == null) { Refund(wager); return; }
                fighter.Agent = foe;
            }

            var b = new Bout
            {
                Fighter = fighter, Foe = foe, Index = index, Wooden = wooden, Wager = wager,
                Odds = OddsFor(fighter.Troop, index), StartedAt = _time, NextCheerAt = _time + 3f,
            };
            try
            {
                StreetEventsBehavior.Release(foe);
                // Weapons: yours put by, theirs too; practice weapons if that's the bout.
                Strip(main, b.PlayerStash);
                Strip(foe, b.FoeStash);
                if (wooden)
                {
                    b.Style = StyleOf(Hero.MainHero.GetSkillValue);
                    var foeStyle = StyleOf(fighter.Troop.GetSkillValue);
                    if (!Arm(main, b.Style) || !Arm(foe, foeStyle)) { b.Wooden = false; ClearWeapons(main); ClearWeapons(foe); }
                }
                if (!b.Wooden) b.Style = DefaultSkills.OneHanded;

                // Fresh for the bout (your own bruises are handed back after); he's tougher the bigger your name, and each
                // of the three a little tougher than the last.
                b.PlayerHealth = main.Health;
                main.Health = main.HealthLimit;
                b.PlayerDownAt = Math.Max(5f, main.HealthLimit * 0.15f);
                float mult = Math.Min(2.2f, 0.8f + 0.02f * Hero.MainHero.Level) * (1f + 0.1f * index);
                b.FoeLimit = foe.HealthLimit;
                foe.SetMortalityState(Agent.MortalityState.Mortal);
                foe.HealthLimit = b.FoeLimit * mult;
                foe.Health = foe.HealthLimit;
                b.FoeDownAt = Math.Max(5f, foe.HealthLimit * 0.12f);
                Guard(b);
                _bout = b;

                GatherCrowd(mission, b);
                if (_master != null && _master.IsActive())
                    StreetEventsBehavior.Bark(_master, Flavor.Pick("{=lmmi_castle_spar_begin}Right, lads — make room! First one on his back loses.",
                        "{=lmmi_castle_spar_begin_2}Clear the ring! Fists up — first to hit the dirt loses.",
                        "{=lmmi_castle_spar_begin_3}Make room, you lot! Let's see what our guest is made of."));
                LmmiLog.Info($"Castle: bout {index + 1}/{_ring.Count} with {fighter.Troop.Name} (tier {fighter.Troop.Tier}, {foe.HealthLimit:0} hp), "
                             + $"{(b.Wooden ? $"wooden ({b.Style.Name})" : "fists")}{(wager > 0 ? $", {wager} wagered at {b.Odds:0.0}:1" : "")}.");
                handler.StartCustomFight(new List<Agent> { main }, new List<Agent> { foe }, dropWeapons: false, isItemUseDisabled: false,
                    won =>
                    {
                        try { BoutEnded(b, won); }
                        catch (Exception ex) { LmmiLog.Error("Castle: bout end threw", ex); }
                    });
            }
            catch (Exception ex)
            {
                LmmiLog.Error("Castle: starting the bout threw", ex);
                b.Draw = true;
                BoutEnded(b, false);
            }
        }

        /// <summary>
        /// Down, never dead: when a fighter's health falls to the knock-down mark the bout is called (next tick), and
        /// health never reaches zero, so the engine never kills or knocks out either of you — whatever the weapons'
        /// damage type (other mods make wooden blades cut).
        /// </summary>
        private static void Guard(Bout b)
        {
            b.Guard = (agent, oldHealth, newHealth) =>
            {
                try
                {
                    if (b.Ended) return;
                    float mark = agent == b.Foe ? b.FoeDownAt : b.PlayerDownAt;
                    if (newHealth > mark) return;
                    if (b.Down == null) b.Down = agent;
                    if (agent.Health < 1f) agent.Health = 1f;
                }
                catch (Exception ex) { LmmiLog.Error("Castle: bout health guard threw", ex); }
            };
            b.Foe.OnAgentHealthChanged += b.Guard;
            if (Agent.Main != null) Agent.Main.OnAgentHealthChanged += b.Guard;
            // And no single blow takes the last point (trimmed where every blow lands).
            HiredBeatDownPatch.Guarded = Agent.Main;
            HiredBeatDownPatch.AlsoGuarded.Add(b.Foe);
        }

        private static void Unguard(Bout b)
        {
            HiredBeatDownPatch.AlsoGuarded.Remove(b.Foe);
            if (Agent.Main != null && ReferenceEquals(HiredBeatDownPatch.Guarded, Agent.Main)) HiredBeatDownPatch.Guarded = null;
            if (b.Guard == null) return;
            try
            {
                b.Foe.OnAgentHealthChanged -= b.Guard;
                if (Agent.Main != null) Agent.Main.OnAgentHealthChanged -= b.Guard;
            }
            catch (Exception ex) { LmmiLog.Error("Castle: unguarding threw", ex); }
            b.Guard = null;
        }

        /// <summary>The yard gathers round: the master, the men still waiting their turn, and whoever of the watch is free.</summary>
        private void GatherCrowd(Mission mission, Bout b)
        {
            var main = Agent.Main;
            var around = (main.Position + b.Foe.Position) * 0.5f;
            if (_master != null && _master.IsActive()) b.Watchers.Add(_master);
            foreach (var f in _ring.Where(f => f != b.Fighter && f.Agent != null && f.Agent.IsActive())) b.Watchers.Add(f.Agent);
            var extra = CastleStaging.FreeSoldiers(mission, around, 30f, 5);
            b.Crowd.AddRange(extra);
            b.Watchers.AddRange(extra);
            if (b.Watchers.Count == 0) return;

            var from = main.GetWorldPosition();
            from.SetVec2(around.AsVec2);
            float start = MBRandom.RandomFloat * 6.283f;
            var spots = new List<Vec3>();
            for (int k = 0; k < 16 && spots.Count < b.Watchers.Count; k++)
            {
                var d = Vec2.FromRotation(start + k * 6.283f / 16f * 3f);   // spread round the circle, not bunched
                var spot = CastleStaging.Ground(mission, around + new Vec3(d.x, d.y, 0f) * (5.5f + MBRandom.RandomFloat * 0.8f));
                if (spots.Any(s => s.Distance(spot) < 1.2f)) continue;
                if (CastleStaging.ClearLine(mission, from, spot)) spots.Add(spot);
            }
            for (int i = 0; i < b.Watchers.Count; i++)
            {
                var w = b.Watchers[i];
                string cheer = Cheers[MBRandom.RandomInt(Cheers.Length)];
                if (i < spots.Count) CastleStaging.WalkTo(w, spots[i], face: main, loop: cheer);
                else StreetEventsBehavior.Direct(w)?.Hold(face: main, loop: cheer);
            }
        }

        private void TickBout(Mission mission)
        {
            var b = _bout;
            if (b == null || b.Ended) return;
            var handler = mission.GetMissionBehavior<MissionFightHandler>();
            if (!b.Ending)
            {
                bool over = false, won = false;
                if (b.Down != null) { over = true; won = b.Down == b.Foe; }
                else if (!b.Foe.IsActive()) { over = true; won = true; }
                else if (_time - b.StartedAt > 240f) { over = true; b.Draw = true; }
                if (over)
                {
                    b.Ending = true;
                    LmmiLog.Info($"Castle: the bout is called — {(b.Draw ? "a draw" : won ? "he's down" : "you're down")}.");
                    if (handler != null && handler.IsThereActiveFight()) handler.EndFight(overrideDuelWonByPlayer: won);
                    else BoutEnded(b, won);
                    return;
                }
            }

            // The crowd.
            if (_time >= b.NextCheerAt)
            {
                b.NextCheerAt = _time + 5f + MBRandom.RandomFloat * 4f;
                var who = b.Watchers.Where(a => a.IsActive()).OrderBy(_ => MBRandom.RandomFloat).FirstOrDefault();
                if (who != null) StreetEventsBehavior.Bark(who, CheerLine(b));
            }
        }

        private TextObject CheerLine(Bout b)
        {
            bool foreign = _castle?.Culture != null && _castle.Culture != Hero.MainHero.Culture;
            var options = new List<TextObject>
            {
                new TextObject("{=lmmi_castle_cheer_1}Hit him! Hit him!"),
                new TextObject("{=lmmi_castle_cheer_2}Guard up, you fool — guard up!"),
                new TextObject("{=lmmi_castle_cheer_3}Ooh — that one'll leave a mark!"),
                new TextObject("{=lmmi_castle_cheer_4}Get in close! Close!"),
                new TextObject("{=lmmi_castle_cheer_8}Watch the left! The left!"),
                new TextObject("{=lmmi_castle_cheer_9}Stop dancing and hit something!"),
                new TextObject("{=lmmi_castle_cheer_10}Ha! He felt that one in his grandfather's bones!"),
                new TextObject("{=lmmi_castle_cheer_11}Keep your feet! Keep your feet!"),
                new TextObject("{=lmmi_castle_cheer_12}Is that a fight or a dance? Hit!"),
                new TextObject("{=lmmi_castle_cheer_13}Go for the knees! Nobody fights well without knees!"),
                new TextObject("{=lmmi_castle_cheer_14}My grandmother hits harder than that!"),
                new TextObject("{=lmmi_castle_cheer_15}Down! Put them down!"),
                new TextObject("{=lmmi_castle_cheer_5}Show {?PLAYER.GENDER}her{?}him{\\?} how we fight in {CASTLE}!").SetTextVariable("CASTLE", _castle?.Name ?? TextObject.GetEmpty()),
            };
            if (foreign)
                options.Add(Flavor.Pick("{=lmmi_castle_cheer_6}Two denars on the {DEMONYM}! ...No? Nobody?",
                    "{=lmmi_castle_cheer_6b}Put the {DEMONYM} in the dirt! Show them how we do it here!",
                    "{=lmmi_castle_cheer_6c}Come on, it's only a {DEMONYM}! Finish it!").SetTextVariable("DEMONYM", CultureWords.Demonym(Hero.MainHero.Culture)));
            if (b.Wager > 0)
                options.Add(Flavor.Pick("{=lmmi_castle_cheer_7}My week's pay is on you, lad — don't you dare go down!",
                    "{=lmmi_castle_cheer_7b}There's coin riding on this — don't you fall over!",
                    "{=lmmi_castle_cheer_7c}I bet against you, so go on, fall down!"));
            return options[MBRandom.RandomInt(options.Count)];
        }

        private void BoutEnded(Bout b, bool won)
        {
            if (b.Ended) return;
            b.Ended = true;
            if (_bout == b) _bout = null;
            Unguard(b);
            var main = Agent.Main;
            var foe = b.Foe;
            var castle = _castle;

            // Everyone's own weapons back, and your own bruises (a practice bout costs you nothing but pride).
            try
            {
                if (main != null && main.IsActive())
                {
                    GiveBack(main, b.PlayerStash);
                    main.Health = Math.Max(1f, Math.Min(main.HealthLimit, b.PlayerHealth));
                    StreetEventsBehavior.CalmAround(main.Position);
                }
                if (foe.IsActive())
                {
                    GiveBack(foe, b.FoeStash);
                    foe.HealthLimit = b.FoeLimit;
                    foe.Health = foe.HealthLimit;
                }
            }
            catch (Exception ex) { LmmiLog.Error("Castle: giving the weapons back threw", ex); }

            // The crowd goes back to what it was doing; the ring re-forms.
            foreach (var c in b.Crowd) StreetEventsBehavior.Release(c);
            foreach (var f in _ring.Where(f => f != b.Fighter && f.Agent != null && f.Agent.IsActive()))
                CastleStaging.WalkTo(f.Agent, f.Post, loop: RingLoops[_ring.IndexOf(f) % RingLoops.Length]);
            if (_master != null && _master.IsActive() && main != null)
                CastleStaging.WalkTo(_master, _masterPost, face: main, loop: "act_conversation_hip_loop");

            if (castle == null) return;
            var fighter = b.Fighter;
            var skill = b.Wooden ? b.Style : DefaultSkills.OneHanded;

            if (b.Draw)
            {
                Refund(b.Wager);
                if (_master != null) StreetEventsBehavior.Bark(_master, Flavor.Pick("{=lmmi_castle_spar_draw}Enough! I'm calling it. Neither of you is going down today — wagers back.",
                        "{=lmmi_castle_spar_draw_2}Break! That's enough. Nobody's winning this one today — wagers back.",
                        "{=lmmi_castle_spar_draw_3}Stop, stop! You'll be at it till dark. A draw — take your coin back."));
                SendBack(fighter, kneel: false);
                return;
            }

            if (!won)
            {
                // Your run's over for today: next time you start again from the first of them.
                UntilTomorrow("run:" + castle.StringId);
                foreach (var f in _ring) f.Beaten = false;
                Hero.MainHero.AddSkillXp(DefaultSkills.Athletics, 20f + 10f * b.Index);
                Hero.MainHero.AddSkillXp(skill, 10f + 10f * b.Index);
                if (_master != null) StreetEventsBehavior.Bark(_master, Flavor.Pick("{=lmmi_castle_spar_lost}Down you go! Don't take it hard — he does this to everyone.",
                        "{=lmmi_castle_spar_lost_2}And down! Brush yourself off — better fighters than you have kissed that dirt.",
                        "{=lmmi_castle_spar_lost_3}That's a fall! No shame in it. He's been doing this since he could walk."));
                var msg = (b.Wager > 0
                    ? Flavor.Pick("{=lmmi_castle_spar_lost_purse}You pick yourself up off the dirt. Your {GOLD}{GOLD_ICON} goes round the garrison — and that's your run done for today.",
                        "{=lmmi_castle_spar_lost_purse_2}The ground comes up fast. By the time you're on your feet, your {GOLD}{GOLD_ICON} is already being shared out — and your run's over for today.",
                        "{=lmmi_castle_spar_lost_purse_3}Flat on your back, and {GOLD}{GOLD_ICON} lighter. That's the end of your run today.")
                    : Flavor.Pick("{=lmmi_castle_spar_lost_plain}You pick yourself up off the dirt. That's your run done for today — come back tomorrow and start again.",
                        "{=lmmi_castle_spar_lost_plain_2}Flat on your back. That's the end of your run — try again tomorrow, from the first.",
                        "{=lmmi_castle_spar_lost_plain_3}You spit out a mouthful of dust. Done for today. Tomorrow you start again."));
                msg.SetTextVariable("GOLD", b.Wager);
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
                if (foe.IsActive())
                {
                    StreetEventsBehavior.Direct(foe)?.Hold(face: main, loop: "act_cheer_1");
                    At(4f, () => SendBack(fighter, kneel: false));
                }
                LmmiLog.Info($"Castle: you lost bout {b.Index + 1} to {fighter.Troop.Name}.");
                return;
            }

            fighter.Beaten = true;
            SendBack(fighter, kneel: true);
            int payout = b.Wager > 0 ? (int)Math.Round(b.Wager * b.Odds) : 0;
            if (payout > 0) Hero.MainHero.ChangeHeroGold(payout);
            bool champion = _ring.All(f => f.Beaten);
            if (champion)
            {
                bool fresh = Ready("champ:" + castle.StringId);
                Hero.MainHero.AddSkillXp(DefaultSkills.Athletics, 150f);
                Hero.MainHero.AddSkillXp(skill, 150f);
                if (fresh)
                {
                    TownStandingBehavior.Adjust(castle, 2f, "became champion of the yard");
                    Cooldown("champ:" + castle.StringId, 7f);
                }
                if (_master != null) StreetEventsBehavior.Bark(_master, (fresh
                    ? Flavor.Pick("{=lmmi_castle_spar_champion}All three! Lads — the champion of the yard! ...And you'll not get another wager out of me this week, thank you.",
                        "{=lmmi_castle_spar_champion_2}Three for three! Somebody fetch a cup for the champion of the yard! ...And no more wagers from you this week.",
                        "{=lmmi_castle_spar_champion_3}Ha! All three of them! The yard's yours — but my purse is closed to you for a week.")
                    : Flavor.Pick("{=lmmi_castle_spar_champion_again}All three again. I'd hire you, if I could afford you.",
                        "{=lmmi_castle_spar_champion_again_2}All three, again! Are you sure you don't want a post here?",
                        "{=lmmi_castle_spar_champion_again_3}You've done it again. My lads will need a new trade.")));
                var msg = (payout > 0
                    ? Flavor.Pick("{=lmmi_castle_champion_purse}Champion of the yard at {CASTLE}! The garrison roars your name, and {GOLD}{GOLD_ICON} changes hands — most of it to you.",
                        "{=lmmi_castle_champion_purse_2}Champion of the yard at {CASTLE}! They chant your name, and {GOLD}{GOLD_ICON} lands in your purse.",
                        "{=lmmi_castle_champion_purse_3}All three beaten at {CASTLE}! The garrison cheers — and pays — {GOLD}{GOLD_ICON}, most of it to you.")
                    : Flavor.Pick("{=lmmi_castle_champion_plain}Champion of the yard at {CASTLE}! The garrison roars your name — they'll be talking about this for a month.",
                        "{=lmmi_castle_champion_plain_2}Champion of the yard at {CASTLE}! The men are already telling it to the night watch.",
                        "{=lmmi_castle_champion_plain_3}All three beaten at {CASTLE}! The garrison's cheer shakes the walls."));
                msg.SetTextVariable("CASTLE", castle.Name);
                msg.SetTextVariable("GOLD", payout);
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
                foreach (var w in b.Watchers.Where(w => w.IsActive() && w != _master && _ring.All(f => f.Agent != w))) StreetEventsBehavior.Release(w);
                LmmiLog.Info($"Castle: champion of the yard at {castle.Name}{(fresh ? "" : " (again this week: no standing)")}.");
                return;
            }

            Hero.MainHero.AddSkillXp(DefaultSkills.Athletics, 40f + 20f * b.Index);
            Hero.MainHero.AddSkillXp(skill, 30f + 20f * b.Index);
            TownStandingBehavior.Adjust(castle, 0.5f, "bested the garrison's man in the yard");
            if (_master != null) StreetEventsBehavior.Bark(_master, Flavor.Pick("{=lmmi_castle_spar_won}Ha! Not bad at all. Someone help him up.",
                        "{=lmmi_castle_spar_won_2}Well fought! Somebody pick him up and dust him off.",
                        "{=lmmi_castle_spar_won_3}Ha! Down he goes. Next!"));
            var won1 = (payout > 0
                ? Flavor.Pick("{=lmmi_castle_spar_won_purse}The yard roars. You collect {GOLD}{GOLD_ICON} from the grumbling garrison.",
                        "{=lmmi_castle_spar_won_purse_2}Cheers and groans round the ring. {GOLD}{GOLD_ICON} comes your way.",
                        "{=lmmi_castle_spar_won_purse_3}A good win — and a good purse. {GOLD}{GOLD_ICON} from the losers.")
                : Flavor.Pick("{=lmmi_castle_spar_won_plain}The yard roars. The garrison will be talking about that bout for a week.",
                        "{=lmmi_castle_spar_won_plain_2}Cheers round the ring. Somebody claps you on the back.",
                        "{=lmmi_castle_spar_won_plain_3}The garrison whoops. They'll be retelling that one at supper."));
            won1.SetTextVariable("GOLD", payout);
            InformationManager.DisplayMessage(new InformationMessage(won1.ToString(), Colors.Green));
            LmmiLog.Info($"Castle: you won bout {b.Index + 1} against {fighter.Troop.Name}{(payout > 0 ? $" (+{payout})" : "")}.");
        }

        /// <summary>The fighter goes back to his place at the ring — after a moment on his knees, if he went down.</summary>
        private void SendBack(RingFighter f, bool kneel)
        {
            var a = f.Agent;
            if (a == null || !a.IsActive()) return;
            int i = _ring.IndexOf(f);
            void Back()
            {
                if (a.IsActive()) CastleStaging.WalkTo(a, f.Post, loop: RingLoops[Math.Max(0, i) % RingLoops.Length]);
            }
            if (!kneel) { Back(); return; }
            StreetEventsBehavior.Direct(a)?.Hold(loop: LoopKneel);
            At(5f, Back);
        }

        // ---- The master-at-arms: dialogs ----

        private void AddMasterDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_castle_master", "start", "lmmi_castle_master_resp",
                "{=lmmi_castle_master}{LMMI_CASTLE_MASTER}",
                () =>
                {
                    if (!TalkingTo(_master)) return false;
                    var castle = _castle!;
                    bool resent = castle.Culture != Hero.MainHero.Culture
                                  && CultureRelations.Multiplier(castle.Culture?.StringId, Hero.MainHero.Culture?.StringId) > 0f;
                    var line = (LostToday
                        ? Flavor.Pick("{=lmmi_castle_master_beaten}Still standing? Good. No more bouts for you today — come back tomorrow and start from the bottom.",
                        "{=lmmi_castle_master_beaten_2}Back already? Your run's done for today. Rest those bruises.",
                        "{=lmmi_castle_master_beaten_3}No more for you today. Come back tomorrow, start with the green one.")
                        : WagersRefused
                            ? Flavor.Pick("{=lmmi_castle_master_champ}The champion of the yard! The lads are still rubbing their jaws.",
                        "{=lmmi_castle_master_champ_2}The champion graces us again! What can I do for you?",
                        "{=lmmi_castle_master_champ_3}Ah, the one who flattened my best. Here to gloat?")
                            : castle.OwnerClan == Clan.PlayerClan
                                ? Flavor.Pick("{=lmmi_castle_master_owner}My {?PLAYER.GENDER}lady{?}lord{\\?}. The men are drilling — care to show them how it's done?",
                        "{=lmmi_castle_master_owner_2}My {?PLAYER.GENDER}lady{?}lord{\\?}. Come to see the lads sweat?",
                        "{=lmmi_castle_master_owner_3}My {?PLAYER.GENDER}lady{?}lord{\\?}. The garrison's ready — ring or drill, you've only to say.")
                                : resent
                                    ? Flavor.Pick("{=lmmi_castle_master_foreign_ring}A {DEMONYM} in our yard. Three of my lads are waiting in the ring — fists or wooden blades. Let's see how they fight where you come from.",
                        "{=lmmi_castle_master_foreign_ring_2}A {DEMONYM}, eh? My lads will be glad of a foreign face to punch. Three of them, in the ring.",
                        "{=lmmi_castle_master_foreign_ring_3}Fancy your chances, {DEMONYM}? Three of mine are waiting. Fists or wooden blades.")
                                    : Flavor.Pick("{=lmmi_castle_master_kin_ring}Master-at-arms. Three of my lads are waiting in the ring — fists or wooden blades, your choice. Nobody dies in my yard.",
                        "{=lmmi_castle_master_kin_ring_2}Master-at-arms. Want a bout? Three of my lads, one after another. Fists or wooden blades.",
                        "{=lmmi_castle_master_kin_ring_3}Three of my best, one at a time. Beat them all and you'll be the talk of the barracks."));
                    line.SetTextVariable("DEMONYM", CultureWords.Demonym(Hero.MainHero.Culture));
                    MBTextManager.SetTextVariable("LMMI_CASTLE_MASTER", line);
                    MBTextManager.SetTextVariable("LMMI_CASTLE_TRAIN_COST", TrainCost);
                    return true;
                }, null, 1200);

            // The ring: who's next, and how.
            starter.AddPlayerLine("lmmi_castle_master_ring", "lmmi_castle_master_resp", "lmmi_castle_master_next",
                "{=lmmi_castle_master_ring}Put me in the ring.", () => _ring.Count > 0 && _bout == null, null, 100,
                (out TextObject why) =>
                {
                    why = TextObject.GetEmpty();
                    if (LostToday) { why = new TextObject("{=lmmi_castle_ring_tomorrow}You've had your beating for today. Come back tomorrow."); return false; }
                    if (NextFighter < 0) { why = new TextObject("{=lmmi_castle_ring_done}You've beaten all three. Nobody else wants a turn today."); return false; }
                    return true;
                });
            starter.AddDialogLine("lmmi_castle_master_next", "lmmi_castle_master_next", "lmmi_castle_master_arms",
                "{=lmmi_castle_master_next_line}{LMMI_CASTLE_NEXT}",
                () =>
                {
                    int i = NextFighter;
                    if (i < 0) return false;
                    var line = (i == 0
                        ? Flavor.Pick("{=lmmi_castle_next_1}First, the greenest of them: {TROOP}. Fists, or wooden blades?",
                        "{=lmmi_castle_next_1_2}The green one first: {TROOP}. Go easy — or don't. Fists or wooden blades?",
                        "{=lmmi_castle_next_1_3}Start with {TROOP}. Still wet behind the ears. Fists, or wooden blades?")
                        : i == 1
                            ? Flavor.Pick("{=lmmi_castle_next_2}Next, {TROOP}. A few more winters in this yard than the last one. Fists, or wooden blades?",
                        "{=lmmi_castle_next_2_2}Now {TROOP}. Tougher than the last. Fists, or wooden blades?",
                        "{=lmmi_castle_next_2_3}{TROOP} next. Seen a few fights, this one. Fists, or wooden blades?")
                            : Flavor.Pick("{=lmmi_castle_next_3}Last, and best: {TROOP}. Nobody's put this one down all season. Fists, or wooden blades?",
                        "{=lmmi_castle_next_3_2}The best I've got: {TROOP}. Fists, or wooden blades?",
                        "{=lmmi_castle_next_3_3}And now {TROOP}. Win this and you're champion. Fists, or wooden blades?"));
                    line.SetTextVariable("TROOP", _ring[i].Troop.Name);
                    MBTextManager.SetTextVariable("LMMI_CASTLE_NEXT", line);
                    return true;
                }, null);

            starter.AddPlayerLine("lmmi_castle_master_fists", "lmmi_castle_master_arms", "lmmi_castle_master_stake",
                "{=lmmi_castle_master_fists}Fists.", null, () => _pendingWooden = false);
            starter.AddPlayerLine("lmmi_castle_master_wooden", "lmmi_castle_master_arms", "lmmi_castle_master_stake",
                "{=lmmi_castle_master_wooden}Wooden blades.", null, () => _pendingWooden = true, 100,
                (out TextObject why) =>
                {
                    why = TextObject.GetEmpty();
                    if (PracticeWeaponsExist) return true;
                    why = new TextObject("{=lmmi_castle_no_practice}The practice weapons are nowhere to be found.");
                    return false;
                });
            starter.AddPlayerLine("lmmi_castle_master_arms_back", "lmmi_castle_master_arms", "lmmi_castle_master_left",
                "{=lmmi_castle_master_arms_back}On second thought — not now.", null, null);

            starter.AddDialogLine("lmmi_castle_master_stake", "lmmi_castle_master_stake", "lmmi_castle_master_stake_resp",
                "{=lmmi_castle_master_stake}{LMMI_CASTLE_STAKE}",
                () =>
                {
                    int i = NextFighter;
                    if (i < 0) return false;
                    var line = (WagersRefused
                        ? Flavor.Pick("{=lmmi_castle_stake_refused}And don't bother reaching for your purse — I'm not taking the champion's coin this week. For the practice, then.",
                        "{=lmmi_castle_stake_refused_2}Keep your purse closed, champion. No coin from you this week. Just for the practice.",
                        "{=lmmi_castle_stake_refused_3}I'm not betting against you again this week. Fight for the fun of it.")
                        : Flavor.Pick("{=lmmi_castle_stake_odds}Coin on it? The lads will give you {ODDS} to one against {TROOP}.",
                        "{=lmmi_castle_stake_odds_2}Want to put coin on it? {ODDS} to one, against {TROOP}.",
                        "{=lmmi_castle_stake_odds_3}The lads are offering {ODDS} to one against {TROOP}. Interested?"));
                    line.SetTextVariable("ODDS", OddsFor(_ring[i].Troop, i).ToString("0.0"));
                    line.SetTextVariable("TROOP", _ring[i].Troop.Name);
                    MBTextManager.SetTextVariable("LMMI_CASTLE_STAKE", line);
                    return true;
                }, null);
            void Stake(string id, string text, int gold)
            {
                starter.AddPlayerLine(id, "lmmi_castle_master_stake_resp", "lmmi_castle_master_go", text, null,
                    () =>
                    {
                        if (gold > 0) Hero.MainHero.ChangeHeroGold(-gold);
                        bool wooden = _pendingWooden;
                        Choose(() => StartBout(wooden, gold));
                    }, 100,
                    gold <= 0 ? null : (ConversationSentence.OnClickableConditionDelegate)((out TextObject why) =>
                    {
                        why = TextObject.GetEmpty();
                        if (WagersRefused) { why = new TextObject("{=lmmi_castle_stake_no_coin}He won't take your coin this week."); return false; }
                        if (Hero.MainHero.Gold >= gold) return true;
                        why = new TextObject("{=lmmi_cant_afford_alms}You don't have that much on you.");
                        return false;
                    }));
            }
            Stake("lmmi_castle_stake_none", "{=lmmi_castle_stake_none}No stake. For the practice.", 0);
            Stake("lmmi_castle_stake_100", "{=lmmi_castle_stake_100}A hundred says I put them down. [100{GOLD_ICON}]", 100);
            Stake("lmmi_castle_stake_300", "{=lmmi_castle_stake_300}Three hundred says I put them down. [300{GOLD_ICON}]", 300);
            starter.AddDialogLine("lmmi_castle_master_go", "lmmi_castle_master_go", "close_window",
                "{=lmmi_castle_master_go}{LMMI_CASTLE_GO}",
                () =>
                {
                    MBTextManager.SetTextVariable("LMMI_CASTLE_GO", (_pendingWooden
                        ? Flavor.Pick("{=lmmi_castle_go_wooden}Wooden blades it is. Hard as you like — they won't cut. The rest of your kit stays with me.",
                        "{=lmmi_castle_go_wooden_2}Wooden it is. They sting, but they won't cut. Hand over your steel.",
                        "{=lmmi_castle_go_wooden_3}Practice blades. Give me your real ones — you'll get them back after.")
                        : Flavor.Pick("{=lmmi_castle_go_fists}Ha! That's the spirit. Weapons down — fists only.",
                        "{=lmmi_castle_go_fists_2}Fists! Good. Leave your blades with me.",
                        "{=lmmi_castle_go_fists_3}Bare knuckles. The way it should be. Weapons down.")));
                    return true;
                }, null);

            // A word with one of the ring.
            starter.AddDialogLine("lmmi_castle_fighter", "start", "close_window",
                "{=lmmi_castle_fighter}{LMMI_CASTLE_FIGHTER}",
                () =>
                {
                    var me = ConversationMission.OneToOneConversationAgent;
                    var f = _castle == null || me == null ? null : _ring.FirstOrDefault(r => r.Agent == me);
                    if (f == null) return false;
                    MBTextManager.SetTextVariable("LMMI_CASTLE_FIGHTER", (f.Beaten
                        ? Flavor.Pick("{=lmmi_castle_fighter_beaten}You got lucky. Next time, it's your face in the dirt.",
                        "{=lmmi_castle_fighter_beaten_2}My jaw's still ringing. Next time, I'll be ready.",
                        "{=lmmi_castle_fighter_beaten_3}Enjoy it while it lasts. I'll get you next time.")
                        : Flavor.Pick("{=lmmi_castle_fighter_ready}Want a go? Ask the master-at-arms — he sets the bouts.",
                        "{=lmmi_castle_fighter_ready_2}Looking for a fight? Talk to the master-at-arms.",
                        "{=lmmi_castle_fighter_ready_3}The master-at-arms decides who fights. Ask him.")));
                    return true;
                }, null, 1200);

            starter.AddPlayerLine("lmmi_castle_master_train", "lmmi_castle_master_resp", "lmmi_castle_master_train_resp",
                "{=lmmi_castle_master_train}Put my companions through their paces. [{LMMI_CASTLE_TRAIN_COST}{GOLD_ICON}]",
                () => Companions().Count > 0,
                () =>
                {
                    int cost = TrainCost;
                    Hero.MainHero.ChangeHeroGold(-cost);
                    Cooldown("train", 3f);
                    var names = new List<string>();
                    foreach (var h in Companions())
                    {
                        var skill = BestCombatSkill(h);
                        h.AddSkillXp(skill, 150f);
                        names.Add($"{h.Name} ({skill.Name})");
                    }
                    var msg = new TextObject("{=lmmi_castle_trained}The master-at-arms drills your companions until they can barely stand: {NAMES}.");
                    msg.SetTextVariable("NAMES", string.Join(", ", names));
                    InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
                    LmmiLog.Info($"Castle: companions trained for {cost}: {string.Join(", ", names)}.");
                }, 100,
                (out TextObject why) =>
                {
                    why = TextObject.GetEmpty();
                    if (!Ready("train")) { why = new TextObject("{=lmmi_castle_train_tired}Your companions are still aching from the last time."); return false; }
                    if (Hero.MainHero.Gold >= TrainCost) return true;
                    why = new TextObject("{=lmmi_cant_afford_alms}You don't have that much on you.");
                    return false;
                });
            starter.AddDialogLine("lmmi_castle_master_train_resp", "lmmi_castle_master_train_resp", "close_window",
                "{=!}{LMMI_CASTLE_TRAIN_RESP}",
                    () => Flavor.Say("LMMI_CASTLE_TRAIN_RESP",
                        "{=lmmi_castle_master_train_resp}Leave them with me. You'll get them back sore, and better.",
                        "{=lmmi_castle_master_train_resp_2}I'll run them ragged. They'll thank me — eventually.",
                        "{=lmmi_castle_master_train_resp_3}Send them over. I'll knock some sense into them, and some skill."), null);

            starter.AddPlayerLine("lmmi_castle_master_drill", "lmmi_castle_master_resp", "lmmi_castle_master_drill_resp",
                "{=lmmi_castle_master_drill}Drill the garrison. Hard.", () => _castle?.OwnerClan == Clan.PlayerClan,
                () =>
                {
                    var castle = _castle;
                    var garrison = castle?.Town?.GarrisonParty;
                    if (castle == null || garrison == null) return;
                    Cooldown("drill:" + castle.StringId, 7f);
                    int leadership = Hero.MainHero.GetSkillValue(DefaultSkills.Leadership);
                    int perMan = 40 + leadership / 2;
                    int men = 0;
                    foreach (var e in garrison.MemberRoster.GetTroopRoster().Where(e => e.Character != null && !e.Character.IsHero).ToList())
                    {
                        garrison.MemberRoster.AddXpToTroop(e.Character, perMan * e.Number);
                        men += e.Number;
                    }
                    Hero.MainHero.AddSkillXp(DefaultSkills.Leadership, 40f);
                    var msg = new TextObject("{=lmmi_castle_drilled}The garrison drills from dawn to dusk under your eye — {MEN} men, harder and sharper for it.");
                    msg.SetTextVariable("MEN", men);
                    InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
                    LmmiLog.Info($"Castle: drilled {men} of the garrison in {castle.Name} ({perMan} xp each).");
                }, 100,
                (out TextObject why) =>
                {
                    why = TextObject.GetEmpty();
                    if (_castle == null || Ready("drill:" + _castle.StringId)) return true;
                    why = new TextObject("{=lmmi_castle_drill_tired}They drilled only days ago. Push them now and they'll break.");
                    return false;
                });
            starter.AddDialogLine("lmmi_castle_master_drill_resp", "lmmi_castle_master_drill_resp", "close_window",
                "{=!}{LMMI_CASTLE_DRILL_RESP}",
                    () => Flavor.Say("LMMI_CASTLE_DRILL_RESP",
                        "{=lmmi_castle_master_drill_resp}Yes, my {?PLAYER.GENDER}lady{?}lord{\\?}. They'll curse your name by noon and thank you by the next siege.",
                        "{=lmmi_castle_master_drill_resp_2}At once, my {?PLAYER.GENDER}lady{?}lord{\\?}. They'll hate you for a day and love you for a lifetime.",
                        "{=lmmi_castle_master_drill_resp_3}Hard it is, my {?PLAYER.GENDER}lady{?}lord{\\?}. I'll have them running the walls in full kit."), null);

            starter.AddPlayerLine("lmmi_castle_master_leave", "lmmi_castle_master_resp", "lmmi_castle_master_left",
                "{=lmmi_castle_master_leave}Another time.", null, null);
            starter.AddDialogLine("lmmi_castle_master_left", "lmmi_castle_master_left", "close_window",
                "{=!}{LMMI_CASTLE_LEFT}",
                    () => Flavor.Say("LMMI_CASTLE_LEFT",
                        "{=lmmi_castle_master_left}The yard's always open.",
                        "{=lmmi_castle_master_left_2}Come back when you're ready to bleed a little.",
                        "{=lmmi_castle_master_left_3}As you like. The ring will still be here."), null);
        }
    }
}
