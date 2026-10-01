using System;
using System.Collections.Generic;
using System.Linq;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using SandBox.Missions.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
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
    /// "My men will see to it": a handful of your own troops come up and go at the looters — a real fight (vanilla's
    /// MissionFightHandler), which you may join or just watch. They're spawned with a PartyAgentOrigin of your party, so
    /// that vanilla's BattleAgentLogic (which town and village scenes have) books a man knocked out as wounded and a man
    /// killed as dead in your roster — exactly as a battle would. Survivors walk off back to the column and fade
    /// (a fade-out never touches the roster: only a man going down does). Riskier than going yourself: men can die, and
    /// even on a win a sheep or two may go over the hill with the looters.
    /// </summary>
    public partial class StreetEventsBehavior
    {
        private const float SheepLostOnWinChance = 0.35f;
        private const float MenLostMoraleCost = 5f;

        private sealed class MenFight
        {
            public Scene Scene = null!;
            public readonly List<Agent> Men = new List<Agent>();
            public List<Agent> Looters = new List<Agent>();
            public bool Started;
        }

        private const int MenNeeded = 5;   // healthy rank-and-file (not companions) for "my men will see to it"

        /// <summary>Healthy regular troops in your party (heroes don't count).</summary>
        private static int HealthyRegulars()
        {
            int n = 0;
            foreach (var e in MobileParty.MainParty.MemberRoster.GetTroopRoster())
                if (e.Character != null && !e.Character.IsHero) n += Math.Max(0, e.Number - e.WoundedNumber);
            return n;
        }

        [NonSerialized] private MenFight? _menFight;

        /// <summary>
        /// From the plea: send your men (runs after the conversation has closed). Dark; then your men and the looters
        /// face each other in two lines out where the looters are, you a little way behind your men; a moment's
        /// stand-off, and they go at it. Watch, or join in.
        /// </summary>
        private void SendTheMen(Scene sc)
        {
            var mission = Mission.Current;
            var main = Agent.Main;
            var handler = mission?.GetMissionBehavior<MissionFightHandler>();
            if (mission == null || main == null || !main.IsActive() || handler == null || handler.IsThereActiveFight()
                || !sc.Actors.Any(a => a.IsActive()) || _menFight != null)
            {
                MenSawToItOffScene(sc);
                return;
            }
            _menFight = new MenFight { Scene = sc };
            sc.TalkTo = null;
            Go(sc, Step.Working);   // nothing else happens in the scene while it's dark
            ScreenFadeController.BeginFadeOut(0.8f);
            _faded = true;
            sc.ThenAt = _sceneTime + 1.3f;
            sc.Then = () => LineUpMen(sc);
        }

        /// <summary>In the dark: the two lines, facing each other at the looters' spot.</summary>
        private void LineUpMen(Scene sc)
        {
            var fight = _menFight;
            var mission = Mission.Current;
            var main = Agent.Main;
            var looters = sc.Actors.Where(a => a.IsActive()).ToList();
            if (fight == null || fight.Scene != sc || mission == null || main == null || !main.IsActive() || looters.Count == 0)
            {
                _menFight = null;
                if (_faded) { _faded = false; ScreenFadeController.BeginFadeIn(0.6f); }
                MenSawToItOffScene(sc);
                return;
            }
            try
            {
                var anchor = looters[0];
                var toUs = (main.Position - anchor.Position).AsVec2;
                toUs = toUs.LengthSquared < 0.01f ? Vec2.Forward : toUs.Normalized();
                var at = anchor.Position;
                // The looters, in a line where they stand, facing the way you'll come.
                for (int i = 0; i < looters.Count; i++)
                {
                    var spot = OnGround(anchor, SceneSpawner.Around(mission, at, i, looters.Count, 1.5f, toUs));
                    Direct(looters[i])?.Sit(spot, toUs.RotationInRadians, "act_stand_1");
                }
                // Your men, seven paces off, facing them.
                int healthy = HealthyRegulars();
                int want = Math.Min(6, Math.Min(healthy, 4 + (healthy >= 40 ? 2 : healthy >= 20 ? 1 : 0)));
                var troops = PickTroops(want, preferLowTier: false);
                var menAt = at + new Vec3(toUs.x * 7f, toUs.y * 7f, 0f);
                for (int i = 0; i < troops.Count; i++)
                {
                    var spot = OnGround(anchor, SceneSpawner.Around(mission, menAt, i, troops.Count, 1.3f, -toUs));
                    var origin = new PartyAgentOrigin(PartyBase.MainParty, troops[i]);
                    var man = SceneSpawner.Spawn(mission, troops[i], spot, (-toUs).RotationInRadians, civilian: false, origin);
                    if (man == null) continue;
                    fight.Men.Add(man);
                    Direct(man)?.Hold(face: looters[i % looters.Count]);
                }
                // You, a few paces behind your men.
                var youAt = FindSpot(anchor, (menAt + new Vec3(toUs.x * 4f, toUs.y * 4f, 0f)).AsVec2);
                if (youAt != null)
                {
                    main.TeleportToPosition(youAt.Value);
                    var face = -toUs;
                    main.SetMovementDirection(in face);
                    main.LookDirection = new Vec3(face.x, face.y, 0f);
                }
            }
            catch (Exception ex) { LmmiLog.Error("Street: lining up your men threw", ex); }

            if (fight.Men.Count == 0)
            {
                _menFight = null;
                if (_faded) { _faded = false; ScreenFadeController.BeginFadeIn(0.6f); }
                MenSawToItOffScene(sc);
                return;
            }
            fight.Looters = looters;
            LmmiLog.Info($"Street: {fight.Men.Count} of your men ({string.Join(", ", fight.Men.Select(m => m.Name))}) face {looters.Count} looter(s).");

            // Light; a moment's stand-off; at them.
            sc.ThenAt = _sceneTime + 0.4f;
            sc.Then = () =>
            {
                ScreenFadeController.BeginFadeIn(0.8f);
                _faded = false;
                var lead = looters.FirstOrDefault(l => l.IsActive());
                if (lead != null) Bark(lead, new TextObject("{=lmmi_village_raiders_men_standoff}Soldiers? ...Hold your ground, lads. They're only farm-boys in mail."));
                sc.ThenAt = _sceneTime + 2.5f;
                sc.Then = () => StartMenFight(sc, fight);
            };
        }

        private static Vec3 OnGround(Agent anchor, Vec3 at) => FindSpot(anchor, at.AsVec2) ?? at;

        private void StartMenFight(Scene sc, MenFight fight)
        {
            var mission = Mission.Current;
            var main = Agent.Main;
            var handler = mission?.GetMissionBehavior<MissionFightHandler>();
            var men = fight.Men.Where(m => m.IsActive()).ToList();
            var looters = fight.Looters.Where(l => l.IsActive()).ToList();
            if (handler == null || main == null || !main.IsActive() || handler.IsThereActiveFight() || men.Count == 0 || looters.Count == 0)
            {
                CallOffMen();
                MenSawToItOffScene(sc);
                return;
            }
            foreach (var l in looters) Release(l);
            foreach (var m in men) Release(m);
            Go(sc, Step.Fighting);
            fight.Started = true;
            // Join in if you like: hurt, beaten down, never killed (HiredBeatDownPatch; Immortal would zero all damage).
            HiredBeatDownPatch.Guarded = main;
            Bark(men[0], new TextObject("{=lmmi_village_raiders_men_go}You heard {?PLAYER.GENDER}her{?}him{\\?}, lads — run them off!"));
            LmmiLog.Info($"Street: your {men.Count} men go at {looters.Count} looter(s).");
            try
            {
                handler.StartCustomFight(men, looters, dropWeapons: false, isItemUseDisabled: false,
                    won =>
                    {
                        try { OnMenFightEnd(fight, won); }
                        catch (Exception ex) { LmmiLog.Error("Street: your men's fight end threw", ex); _menFight = null; if (_scene == sc) Finish(sc); }
                    });
            }
            catch (Exception ex)
            {
                LmmiLog.Error("Street: starting your men's fight threw", ex);
                UnguardMain();
                CallOffMen();
                MenSawToItOffScene(sc);
            }
        }

        /// <summary>The line-up never came to blows: your men go back unseen.</summary>
        private void CallOffMen()
        {
            var fight = _menFight;
            _menFight = null;
            if (fight == null) return;
            foreach (var m in fight.Men.Where(x => x.IsActive())) { Release(m); m.FadeOut(true, true); }
        }

        private static void UnguardMain()
        {
            var me = Agent.Main;
            if (HiredBeatDownPatch.Guarded != null && ReferenceEquals(HiredBeatDownPatch.Guarded, me)) HiredBeatDownPatch.Guarded = null;
        }

        private void OnMenFightEnd(MenFight fight, bool won)
        {
            var sc = fight.Scene;
            if (_menFight == fight) _menFight = null;
            UnguardMain();
            var mission = Mission.Current;

            // What it cost (vanilla's BattleAgentLogic has already booked it in the roster).
            int killed = fight.Men.Count(m => m.State == AgentState.Killed);
            int wounded = fight.Men.Count(m => m.State == AgentState.Unconscious);
            var survivors = fight.Men.Where(m => m.IsActive()).ToList();
            LmmiLog.Info($"Street: your men's fight is over — {(won ? "won" : "lost")}; {killed} killed, {wounded} wounded, {survivors.Count} standing.");
            if (killed + wounded > 0)
            {
                var cost = new TextObject(killed > 0
                    ? "{=lmmi_village_raiders_men_cost}It cost you: {KILLED} of your men dead, {WOUNDED} wounded."
                    : "{=lmmi_village_raiders_men_cost_wounded}It cost you: {WOUNDED} of your men wounded.");
                cost.SetTextVariable("KILLED", killed);
                cost.SetTextVariable("WOUNDED", wounded);
                InformationManager.DisplayMessage(new InformationMessage(cost.ToString(), Colors.Red));
            }

            // Whatever looters are still on their feet run for it.
            foreach (var a in sc.Actors.Where(x => x.IsActive()).ToList())
            {
                var to = mission != null ? FarPoint(mission, a, sc.Actors.ToArray()) : null;
                if (to != null) HandOver(sc, new SlipAway(a, to.Value, fade: true), a);
                else a.FadeOut(true, true);
            }

            // Your men walk back to the column.
            if (survivors.Count > 0)
            {
                Bark(survivors[MBRandom.RandomInt(survivors.Count)], new TextObject(won
                    ? "{=lmmi_village_raiders_men_back}They won't be back, captain. Not these ones."
                    : "{=lmmi_village_raiders_men_beaten}...There were more of them than we reckoned, captain."));
                var march = new MarchOff(survivors);
                _aftermaths.Add(march);
                march.StartedAt = _sceneTime;
                march.Begin(this);
            }

            CalmAfterLooters(sc);
            var village = sc.Settlement.Village;
            if (won)
            {
                SteppedUp(sc, 3f, "sent your men after looters");   // less than going yourself (5)
                Headman(sc, 1);
                if (MBRandom.RandomFloat < SheepLostOnWinChance)
                {
                    if (village != null) village.Hearth = Math.Max(0f, village.Hearth - 1f);
                    Say("{=lmmi_village_raiders_men_sheep}Your men run the looters off — but a couple of sheep went over the hill with them.");
                }
                else
                {
                    if (village != null) village.Hearth += 1f;
                    Say("{=lmmi_village_raiders_men_done}Your men run the looters out past the last field. The flock comes home.");
                }
                Thank(sc, sc.Requester, "raiders_men");
                return;
            }
            // Beaten by looters: the men take it hard, and the village blames whoever sent them.
            MobileParty.MainParty.RecentEventsMorale -= MenLostMoraleCost;
            foreach (var notable in sc.Settlement.Notables.Where(n => n.IsAlive).ToList())
                ChangeRelationAction.ApplyPlayerRelation(notable, -3, affectRelatives: false, showQuickNotification: true);
            if (village != null) village.Hearth = Math.Max(0f, village.Hearth - 5f);
            Say("{=lmmi_village_raiders_men_lost}The looters see your men off — and half the flock goes with them.");
            var morale = new TextObject("{=lmmi_village_raiders_men_lost_morale}Beaten by looters. Your men won't hear the end of it. (Morale -{MORALE})");
            morale.SetTextVariable("MORALE", (int)MenLostMoraleCost);
            InformationManager.DisplayMessage(new InformationMessage(morale.ToString(), Colors.Red));
            sc.Outcome = "your men were beaten";
            Finish(sc);
        }

        /// <summary>No fight could be staged: your men see to it out of sight, as they used to.</summary>
        private void MenSawToItOffScene(Scene sc)
        {
            foreach (var a in sc.Actors.Where(x => x.IsActive())) a.FadeOut(true, true);
            SteppedUp(sc, 3f, "sent your men after looters");
            Headman(sc, 1);
            Say("{=lmmi_village_raiders_men_done}Your men run the looters out past the last field. The flock comes home.");
            MarkGrateful(sc.Requester);
            sc.Outcome = "your men saw to it";
            Finish(sc);
        }

        /// <summary>
        /// After the looters, win or lose: the whole village calms down (not only those near you), and whoever fetched
        /// you first — or they stay alarmed, their part in the scene never runs, and they never come back to thank you.
        /// </summary>
        private void CalmAfterLooters(Scene sc)
        {
            if (sc.Requester != null && sc.Requester.IsActive()) CalmAgent(sc.Requester);
            var main = Agent.Main;
            var mission = Mission.Current;
            if (main == null || !main.IsActive() || mission == null) return;
            // Now (the fight is already over when vanilla calls back), and again in a moment for any straggler that a
            // still-frightened neighbour set off again.
            CalmBystanders(mission, main.Position, CalmEveryone);
            QueueCalm(main.Position, CalmEveryone);
        }

        /// <summary>Your men, back to the column: they walk off and are gone.</summary>
        private sealed class MarchOff : Aftermath
        {
            private readonly List<Agent> _men;
            public MarchOff(List<Agent> men) { _men = men; }

            public void Begin(StreetEventsBehavior owner)
            {
                var mission = Mission.Current;
                foreach (var m in _men.Where(x => x.IsActive()))
                {
                    CalmAgent(m);
                    var to = mission != null ? FarPoint(mission, m, _men.ToArray()) : null;
                    if (to != null) Direct(m)?.GoTo(to.Value, run: false);
                    else Direct(m)?.Hold();
                }
            }

            public override bool Tick(StreetEventsBehavior owner, float now) => now - StartedAt > 14f || _men.All(m => !m.IsActive());

            public override void End()
            {
                foreach (var m in _men.Where(x => x.IsActive()))
                {
                    Release(m);
                    m.FadeOut(false, true);
                }
            }
        }
    }
}
