using System;
using System.Collections.Generic;
using System.Linq;
using LessMenusMoreImmersion.Logging;
using SandBox;
using SandBox.Missions.AgentBehaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// The harvest, as a little scene of its own. The screen goes dark; when it comes back you're in the field beside
    /// the man who asked, a scythe in your hands, swinging it the way the villagers do (vanilla's farm-work animations
    /// and tool props, the ones its own field workers use), with him and a neighbour or two working beside you. A few
    /// seconds of that, dark again — and when the light returns the afternoon has gone: the scene's clock has moved on
    /// and the sun with it. Or your men do it (four of them, spawned for the scene and sent back after), and grumble.
    /// The player's own set-up (controller, action set, prop) is always given back — at the end, or whenever the scene is
    /// cut short; a mission that ends mid-scene fades back in from the menu (OnMissionEnded).
    /// </summary>
    public partial class StreetEventsBehavior
    {
        private const float FieldWorkSeconds = 9f;
        private const float MenFieldWorkSeconds = 7f;
        private const float HarvestHours = 4f;
        private const float MenHarvestHours = 3f;
        private const float MenHarvestMoraleCost = 4f;
        /// <summary>Re-light the scene for the new hour (the engine's time-of-day atmosphere) as well as moving its clock.</summary>
        private static readonly bool RelightAfterHarvest = true;

        /// <summary>A farm-work loop and the tool that goes with it (vanilla's pairings, sp_editor_spawnpoints.xml).</summary>
        private sealed class FarmTool
        {
            public readonly string Act, Prop;
            public FarmTool(string act, string prop) { Act = act; Prop = prop; }
        }

        private static readonly FarmTool Scythe = new FarmTool("act_npc_farmer_scythe_using", "farmer_scythe");
        private static readonly FarmTool[] FarmTools =
        {
            new FarmTool("act_npc_farmer_bush_cutting_while_stand", "farmer_sickle"),
            new FarmTool("act_npc_villager_pitchforking", "pitchfork"),
            new FarmTool("act_npc_farmer_digging", "digging_axe"),
            new FarmTool("act_npc_farmer_scythe_using", "farmer_scythe"),
        };

        private static readonly ActionIndexCache ScytheLoop = ActionIndexCache.Create("act_npc_farmer_scythe_using");

        private sealed class PropOn
        {
            public Agent Agent = null!;
            public sbyte Bone;
            public string Prefab = "";
        }

        /// <summary>What the field work borrowed, to give back.</summary>
        private sealed class FieldWork
        {
            public bool YourMen;
            public bool PlayerWorks;
            public bool SwappedSet;
            public MBActionSet PlayerSet;
            public bool Froze;
            public AgentControllerType Controller = AgentControllerType.Player;
            public int PlayerProp = -1;
            public float NextLoopCheck;
            public readonly List<PropOn> Props = new List<PropOn>();
            public readonly List<Agent> Borrowed = new List<Agent>();   // villagers not otherwise in the scene
            public readonly List<Agent> Spawned = new List<Agent>();    // your men
        }

        [NonSerialized] private FieldWork? _work;

        /// <summary>To the field: dark first (everyone is put in place unseen), then the work.</summary>
        private void BeginFieldWork(Scene sc, bool yourMen)
        {
            sc.TalkTo = null;
            Go(sc, Step.Working);
            ScreenFadeController.BeginFadeOut(0.8f);
            _faded = true;
            sc.ThenAt = _sceneTime + 1.3f;
            sc.Then = () => SetUpFieldWork(sc, yourMen);
            LmmiLog.Info($"Street: the harvest — {(yourMen ? "your men go to the field" : "you go to the field")}.");
        }

        private void SetUpFieldWork(Scene sc, bool yourMen)
        {
            var mission = Mission.Current;
            var main = Agent.Main;
            var hand = sc.Actors.FirstOrDefault(a => a.IsActive());
            if (mission == null || main == null || !main.IsActive() || hand == null)
            {
                // Nobody to work beside: the afternoon passes in the dark, as it used to.
                FinishFieldWork(sc, yourMen, staged: false);
                return;
            }

            var work = new FieldWork { YourMen = yourMen };
            _work = work;
            float seconds = yourMen ? MenFieldWorkSeconds : FieldWorkSeconds;
            Agent? talker = hand;
            try
            {
                var f = hand.Frame.rotation.f.AsVec2;
                f = f.LengthSquared < 0.01f ? Vec2.Forward : f.Normalized();
                var side = new Vec2(-f.y, f.x);
                float facing = f.RotationInRadians;

                // The field hand works where he stands.
                SetToWork(hand, hand.Position, facing, FarmTools[0], work);

                if (!yourMen)
                {
                    // You, at his right hand, with a scythe.
                    PutPlayerToWork(main, FieldSpot(hand, side, f, 1), facing, work);

                    // Whoever fetched you, and maybe a neighbour, on his other side.
                    var helpers = new List<Agent>();
                    if (sc.Requester != null && sc.Requester.IsActive() && sc.Requester != hand && Dist(sc.Requester, hand) < 60f)
                        helpers.Add(sc.Requester);
                    if (helpers.Count == 0 || MBRandom.RandomFloat < 0.6f)
                    {
                        var neighbour = mission.Agents
                            .Where(a => a != hand && a != sc.Requester && Usable(a) && IsAdultLocal(a) && Dist(a, hand) < 35f)
                            .OrderBy(_ => MBRandom.RandomFloat).FirstOrDefault();
                        if (neighbour != null) { helpers.Add(neighbour); work.Borrowed.Add(neighbour); }
                    }
                    for (int i = 0; i < helpers.Count; i++)
                        SetToWork(helpers[i], FieldSpot(hand, side, f, -(i + 1)), facing, FarmTools[1 + i % (FarmTools.Length - 1)], work);
                }
                else
                {
                    // Four of your men in a row along the field — soldiers, not farmhands.
                    var troops = PickTroops(4, preferLowTier: true);
                    int[] slots = { -1, 1, -2, 2 };
                    for (int i = 0; i < troops.Count; i++)
                    {
                        var at = FieldSpot(hand, side, f, slots[i % slots.Length]);
                        var man = SceneSpawner.Spawn(mission, troops[i], at, facing, civilian: true);
                        if (man == null) continue;
                        work.Spawned.Add(man);
                        man.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);
                        man.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
                        SetToWork(man, at, facing, FarmTools[(i + 1) % FarmTools.Length], work);
                    }
                    if (work.Spawned.Count > 0) talker = work.Spawned[MBRandom.RandomInt(work.Spawned.Count)];
                    // You watch from the edge of the field.
                    var watchAt = FindSpot(hand, hand.Position.AsVec2 + f * 5f) ?? FindSpot(hand, hand.Position.AsVec2 - f * 5f);
                    if (watchAt != null)
                    {
                        main.TeleportToPosition(watchAt.Value);
                        var toRow = (hand.Position - watchAt.Value).AsVec2;
                        toRow = toRow.LengthSquared < 0.01f ? -f : toRow.Normalized();
                        main.SetMovementDirection(in toRow);
                        main.LookDirection = new Vec3(toRow.x, toRow.y, 0f);
                    }
                }
                LmmiLog.Info($"Street: field work — {(yourMen ? $"{work.Spawned.Count} of your men" : $"you and {1 + work.Borrowed.Count + (sc.Requester != null && sc.Requester.IsActive() ? 1 : 0)} villager(s)")} for {seconds:0} s.");
            }
            catch (Exception ex) { LmmiLog.Error("Street: setting up the field work threw", ex); }

            // Light; a word while you work; dark again; the afternoon gone.
            sc.ThenAt = _sceneTime + 0.4f;
            sc.Then = () =>
            {
                ScreenFadeController.BeginFadeIn(0.8f);
                _faded = false;
                sc.ThenAt = _sceneTime + 3f;
                sc.Then = () =>
                {
                    if (talker != null && talker.IsActive())
                        Bark(talker, new TextObject(yourMen
                            ? "{=lmmi_village_harvest_men_grumble}Signed on to fight, I did. Not to cut somebody's barley..."
                            : "{=lmmi_village_harvest_work_tip}Keep the blade low and let it swing — that's it! We'll make a farmer of you yet."));
                    sc.ThenAt = _sceneTime + Math.Max(1f, seconds - 3f);
                    sc.Then = () =>
                    {
                        ScreenFadeController.BeginFadeOut(0.8f);
                        _faded = true;
                        sc.ThenAt = _sceneTime + 1.3f;
                        sc.Then = () => FinishFieldWork(sc, yourMen, staged: true);
                    };
                };
            };
        }

        /// <summary>The screen is dark: the afternoon has passed. Give everything back, light, and the rewards.</summary>
        private void FinishFieldWork(Scene sc, bool yourMen, bool staged)
        {
            var mission = Mission.Current;
            StopFieldWork();
            if (mission != null) AdvanceClock(mission, yourMen ? MenHarvestHours : HarvestHours);
            if (_faded) { ScreenFadeController.BeginFadeIn(0.8f); _faded = false; }
            if (!staged) LmmiLog.Info("Street: field work couldn't be staged — the afternoon passes in the dark.");
            if (yourMen) MenHarvestRewards(sc);
            else HarvestRewards(sc);
        }

        /// <summary>A villager (or one of your men) at work: in place, the loop, the tool in hand.</summary>
        private static void SetToWork(Agent agent, Vec3 at, float facing, FarmTool tool, FieldWork work)
        {
            try
            {
                var stage = Direct(agent);
                if (stage == null) return;
                stage.Sit(at, facing, tool.Act);   // sets down whatever they carried; holds the pose
                var nav = agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
                if (nav == null || agent.AgentVisuals == null) return;
                sbyte bone = agent.AgentVisuals.GetRealBoneIndex(HumanBone.ItemR);
                nav.SetPrefabVisibility(bone, tool.Prop, true);
                work.Props.Add(new PropOn { Agent = agent, Bone = bone, Prefab = tool.Prop });
            }
            catch (Exception ex) { LmmiLog.Error($"Street: putting {agent.Name} to work threw", ex); }
        }

        /// <summary>
        /// You, swinging a scythe. The farm-work animations live in the villagers' action set (as_human_hideout_bandit,
        /// the base of as_human_villager), not in yours (as_human_warrior): you borrow theirs for the scene. Your controls
        /// are set aside meanwhile (as vanilla does during deployment), so a stray key doesn't break the swing.
        /// </summary>
        private static void PutPlayerToWork(Agent main, Vec3 at, float facing, FieldWork work)
        {
            try
            {
                main.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);
                main.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
                main.TeleportToPosition(at);
                var dir = Vec2.FromRotation(facing);
                main.SetMovementDirection(in dir);
                main.LookDirection = new Vec3(dir.x, dir.y, 0f);

                work.Controller = main.Controller;
                main.Controller = AgentControllerType.None;
                work.Froze = true;

                if (main.Monster != null)
                {
                    var set = MBGlobals.GetActionSet(ActionSetCode.GenerateActionSetNameWithSuffix(main.Monster, main.IsFemale, "_villager"));
                    if (set.IsValid && set.GetName() != main.ActionSet.GetName())
                    {
                        work.PlayerSet = main.ActionSet;
                        var data = MonsterExtensions.FillAnimationSystemData(main.Monster, set, main.Character.GetStepSize(), false);
                        main.SetActionSet(ref data);
                        work.SwappedSet = true;
                    }
                }
                if (main.AgentVisuals != null)
                    work.PlayerProp = main.AddSynchedPrefabComponentToBone(Scythe.Prop, main.AgentVisuals.GetRealBoneIndex(HumanBone.ItemR));
                main.SetActionChannel(0, in ScytheLoop, ignorePriority: true);
                work.PlayerWorks = true;
            }
            catch (Exception ex) { LmmiLog.Error("Street: putting you to work threw", ex); }
        }

        /// <summary>While you work: keep the swing going (anything that interrupts it, it comes back).</summary>
        private void TickFieldWork()
        {
            var w = _work;
            var main = Agent.Main;
            if (w == null || !w.PlayerWorks || main == null || !main.IsActive() || _sceneTime < w.NextLoopCheck) return;
            w.NextLoopCheck = _sceneTime + 0.5f;
            try
            {
                if (main.GetCurrentAction(0) != ScytheLoop) main.SetActionChannel(0, in ScytheLoop, ignorePriority: true);
            }
            catch (Exception ex) { LmmiLog.Error("Street: keeping you at work threw", ex); w.PlayerWorks = false; }
        }

        /// <summary>Give back whatever the field work took (safe to call twice, and on any path out).</summary>
        private void StopFieldWork()
        {
            var w = _work;
            if (w == null) return;
            _work = null;
            foreach (var p in w.Props)
            {
                try
                {
                    if (p.Agent.IsActive())
                        p.Agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.SetPrefabVisibility(p.Bone, p.Prefab, false);
                }
                catch (Exception ex) { LmmiLog.Error("Street: putting a tool away threw", ex); }
            }
            foreach (var a in w.Borrowed) Release(a);
            foreach (var a in w.Spawned)
            {
                try { if (a.IsActive()) { Release(a); a.FadeOut(true, true); } }
                catch (Exception ex) { LmmiLog.Error("Street: sending a man back threw", ex); }
            }
            var main = Agent.Main;
            if (main == null || !main.IsActive()) return;
            try
            {
                if (w.PlayerWorks || w.SwappedSet) main.SetActionChannel(0, in ActionIndexCache.act_none, ignorePriority: true);
                if (w.PlayerProp >= 0) main.SetSynchedPrefabComponentVisibility(w.PlayerProp, false);
                if (w.SwappedSet && w.PlayerSet.IsValid && main.Monster != null)
                {
                    var data = MonsterExtensions.FillAnimationSystemData(main.Monster, w.PlayerSet, main.Character.GetStepSize(), false);
                    main.SetActionSet(ref data);
                }
            }
            catch (Exception ex) { LmmiLog.Error("Street: giving you back your hands threw", ex); }
            finally
            {
                try
                {
                    if (w.Froze && main.Controller == AgentControllerType.None)
                        main.Controller = w.Controller == AgentControllerType.None ? AgentControllerType.Player : w.Controller;
                }
                catch (Exception ex) { LmmiLog.Error("Street: giving you back your controls threw", ex); }
            }
        }

        /// <summary>
        /// The afternoon has gone: move the scene's clock on (lamps and other time-driven scene scripts follow
        /// Scene.TimeOfDay) and re-light it for the new hour — the mission's sun and sky are set once from the campaign's
        /// atmosphere when it opens and don't follow the clock, so the engine's time-of-day atmosphere is applied
        /// (Scene.SetPhotoAtmosphereViaTod, as vanilla's port scene does). Outdoors only.
        /// </summary>
        private static void AdvanceClock(Mission mission, float hours)
        {
            try
            {
                var scene = mission.Scene;
                if (scene == null) return;
                float before = scene.TimeOfDay;
                float tod = (before + hours) % 24f;
                scene.TimeOfDay = tod;
                bool relit = false;
                if (RelightAfterHarvest && !scene.IsAtmosphereIndoor)
                {
                    scene.SetPhotoAtmosphereViaTod(tod, false);
                    relit = true;
                }
                LmmiLog.Info($"Street: the scene's clock moves on, {before:0.0}h -> {tod:0.0}h{(relit ? " (re-lit)" : "")}.");
            }
            catch (Exception ex) { LmmiLog.Error("Street: moving the scene's clock threw", ex); }
        }

        /// <summary>Somewhere to stand in the row along the field: slot i to the side of the anchor, on the navmesh.</summary>
        private static Vec3 FieldSpot(Agent anchor, Vec2 side, Vec2 forward, int slot)
        {
            var c = anchor.Position.AsVec2;
            return FindSpot(anchor, c + side * (slot * 1.9f))
                   ?? FindSpot(anchor, c + side * (slot * 1.9f) - forward * 1.6f)
                   ?? FindSpot(anchor, c + side * (slot * 1.2f))
                   ?? FindSpot(anchor, c - forward * (1.4f * Math.Abs(slot)))
                   ?? anchor.Position + new Vec3(side.x * 0.8f * slot, side.y * 0.8f * slot, 0f);
        }

        private static Vec3? FindSpot(Agent anchor, Vec2 at)
        {
            try
            {
                var wp = anchor.GetWorldPosition();
                wp.SetVec2(at);
                if (wp.GetNavMesh() == UIntPtr.Zero) return null;
                return wp.GetGroundVec3();
            }
            catch { return null; }
        }

        /// <summary>
        /// Healthy rank-and-file from your party (never heroes), at most as many of a kind as you have fit to go.
        /// </summary>
        private static List<CharacterObject> PickTroops(int count, bool preferLowTier)
        {
            var pool = new List<CharacterObject>();
            foreach (var e in MobileParty.MainParty.MemberRoster.GetTroopRoster())
            {
                if (e.Character == null || e.Character.IsHero) continue;
                int healthy = e.Number - e.WoundedNumber;
                for (int i = 0; i < healthy && i < 40; i++) pool.Add(e.Character);
            }
            return pool
                .OrderBy(c => preferLowTier ? c.Tier + MBRandom.RandomFloat * 2f : (c.IsRanged ? 10f : 0f) + MBRandom.RandomFloat * 3f)
                .Take(count).ToList();
        }

        /// <summary>A long afternoon in the fields: grain for your column, a village that remembers.</summary>
        private void HarvestRewards(Scene sc)
        {
            var crop = VillageCrop(sc.Settlement);
            int bags = 2 + (MobileParty.MainParty.MemberRoster.TotalHealthyCount >= 20 ? 2 : 0);
            if (crop != null) MobileParty.MainParty.ItemRoster.AddToCounts(crop, bags);
            Hero.MainHero.AddSkillXp(DefaultSkills.Athletics, 50f);
            SteppedUp(sc, 3f, "worked the harvest");
            Headman(sc, 2);
            if (sc.Settlement.Village != null) sc.Settlement.Village.Hearth += 1f;
            MarkGrateful(sc.Requester);
            foreach (var a in sc.Actors) MarkGrateful(a);
            var msg = new TextObject("{=lmmi_village_harvest_done}You work the fields until your back aches and the first drops fall. The {CROP} is in. They press {BAGS} sacks of it on you, and won't hear a refusal.");
            msg.SetTextVariable("CROP", crop?.Name ?? TextObject.GetEmpty());
            msg.SetTextVariable("BAGS", bags);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
            sc.Outcome = "worked the harvest";
            Finish(sc);
        }

        /// <summary>Your men in the fields: the village is grateful, the men less so.</summary>
        private void MenHarvestRewards(Scene sc)
        {
            var crop = VillageCrop(sc.Settlement);
            if (crop != null) MobileParty.MainParty.ItemRoster.AddToCounts(crop, 1);
            SteppedUp(sc, 2f, "lent your men to the harvest");
            Headman(sc, 1);
            MarkGrateful(sc.Requester);
            foreach (var a in sc.Actors) MarkGrateful(a);
            Say("{=lmmi_village_harvest_men_done}Your men spend the afternoon in the fields. They come back sunburnt, grumbling, and carrying a sack of thanks.");
            // Soldiers aren't farmhands, and they know whose idea it was.
            MobileParty.MainParty.RecentEventsMorale -= MenHarvestMoraleCost;
            var morale = new TextObject("{=lmmi_village_harvest_men_morale}Your men didn't sign on to bring in somebody else's harvest. (Morale -{MORALE})");
            morale.SetTextVariable("MORALE", (int)MenHarvestMoraleCost);
            InformationManager.DisplayMessage(new InformationMessage(morale.ToString(), Colors.Red));
            LmmiLog.Info($"Street: your men worked the harvest (morale -{MenHarvestMoraleCost}).");
            sc.Outcome = "lent your men";
            Finish(sc);
        }
    }
}
