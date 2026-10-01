using System;
using System.Collections.Generic;
using System.Linq;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using SandBox;
using SandBox.Missions.AgentBehaviors;
using SandBox.Missions.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Street life that happens whether you take part or not: a quarrel over short weight, a preacher and a few
    /// listeners, two drunks propping each other up, the watch moving a beggar along, a dance in a northern town.
    /// Staged a little way off (12–35 m) for half a minute with the same scripted behavior the street events use,
    /// vanilla animations (act_argue, act_preacher, act_drunk_pair, act_stand_beggar, act_dance_norse) and a line or two
    /// if you're close enough to hear. Nothing asks anything of you.
    /// </summary>
    public class AmbientStreetLifeBehavior : CampaignBehaviorBase
    {
        private enum Kind { Quarrel, Preacher, Drunks, MovedAlong, Dance }

        private sealed class Vignette
        {
            public Kind Kind;
            public readonly List<Agent> Cast = new List<Agent>();
            public float Until;
            public float NextLineAt;
            public int Lines;
        }

        [NonSerialized] private Mission? _mission;
        [NonSerialized] private float _time;
        [NonSerialized] private float _nextAt;
        [NonSerialized] private Vignette? _now;

        public override void RegisterEvents()
        {
            CampaignEvents.MissionTickEvent.AddNonSerializedListener(this, OnMissionTick);
        }

        public override void SyncData(IDataStore dataStore) { }

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
                    _now = null;
                    _nextAt = 25f + MBRandom.RandomFloat * 25f;
                }
                _time += dt;
                var main = Agent.Main;
                if (main == null || !main.IsActive()) return;

                if (_now != null)
                {
                    Play(_now, main);
                    if (_time >= _now.Until || StreetEventsBehavior.IsBusy || FeastBehavior.IsBusy) End(_now);
                    return;
                }

                if (_time < _nextAt) return;
                _nextAt = _time + 45f + MBRandom.RandomFloat * 45f;
                if (!LmmiSettingsProvider.EnableStreetEvents || mission.Mode == MissionMode.Conversation || mission.Mode == MissionMode.Battle) return;
                if (StreetEventsBehavior.IsBusy || FeastBehavior.IsBusy || ArrivalScenesBehavior.IsBusy) return;
                if (mission.GetMissionBehavior<MissionFightHandler>()?.IsThereActiveFight() == true) return;
                var location = CampaignMission.Current?.Location?.StringId;
                if (location != "center" && location != "village_center") return;
                if (Settlement.CurrentSettlement?.IsCastle == true) return;   // a castle yard has its own life (CastleLifeBehavior)
                if (HiredSwordsBehavior.InScene) return;

                foreach (var kind in ((Kind[])Enum.GetValues(typeof(Kind))).OrderBy(_ => MBRandom.RandomFloat))
                {
                    var v = Stage(kind, mission, main);
                    if (v == null) continue;
                    _now = v;
                    LmmiLog.Info($"Street life: {kind} ({string.Join(", ", v.Cast.Select(a => a.Name))}).");
                    return;
                }
            }
            catch (Exception ex)
            {
                LmmiLog.Error("AmbientStreetLifeBehavior.OnMissionTick threw", ex);
                if (_now != null) { try { End(_now); } catch { } }
            }
        }

        // ---- Casting ----

        private static bool Free(Agent a) =>
            a.IsActive() && a.IsHuman && a != Agent.Main && !a.IsUsingGameObject
            && a.Character is CharacterObject co && !co.IsHero
            && a.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>() is DailyBehaviorGroup g
            && g.ScriptedBehavior == null
            && !(a.IsAlarmed() || a.IsCautious());

        private static bool InView(Agent a, Agent main)
        {
            float d = a.Position.Distance(main.Position);
            return d > 12f && d < 35f;
        }

        private static WorldPosition Beside(Agent anchor, Agent other, float distance)
        {
            var dir = (other.Position - anchor.Position).AsVec2;
            dir = dir.LengthSquared < 0.01f ? Vec2.FromRotation(MBRandom.RandomFloat * 6.28f) : dir.Normalized();
            var pos = anchor.GetWorldPosition();
            pos.SetVec2(anchor.Position.AsVec2 + dir * distance);
            return pos;
        }

        private Vignette? Stage(Kind kind, Mission mission, Agent main)
        {
            var people = mission.Agents.Where(Free).ToList();
            var v = new Vignette { Kind = kind, Until = _time + 25f + MBRandom.RandomFloat * 10f, NextLineAt = _time + 3f };
            switch (kind)
            {
                case Kind.Quarrel:
                {
                    var a = people.FirstOrDefault(x => StreetEventsBehavior.IsAdultLocal(x) && InView(x, main));
                    var b = a == null ? null : people.Where(x => x != a && StreetEventsBehavior.IsAdultLocal(x) && x.Position.Distance(a.Position) < 20f)
                        .OrderBy(x => x.Position.Distance(a.Position)).FirstOrDefault();
                    if (a == null || b == null) return null;
                    StreetEventsBehavior.Direct(a)?.Hold(face: b, loop: "act_argue");
                    StreetEventsBehavior.Direct(b)?.GoTo(Beside(a, b, 1.3f), run: false, face: a, loop: "act_argue_2");
                    v.Cast.Add(a); v.Cast.Add(b);
                    return v;
                }
                case Kind.Preacher:
                {
                    var p = people.FirstOrDefault(x => StreetEventsBehavior.IsAdultLocal(x) && !x.IsFemale && x.Age >= 35f && InView(x, main))
                            ?? people.FirstOrDefault(x => StreetEventsBehavior.IsAdultLocal(x) && !x.IsFemale && InView(x, main));
                    if (p == null) return null;
                    var listeners = people.Where(x => x != p && StreetEventsBehavior.IsLocal(x) && x.Position.Distance(p.Position) < 25f)
                        .OrderBy(x => x.Position.Distance(p.Position)).Take(3).ToList();
                    if (listeners.Count < 2) return null;
                    StreetEventsBehavior.Direct(p)?.Hold(loop: "act_preacher");
                    for (int i = 0; i < listeners.Count; i++)
                    {
                        var at = p.GetWorldPosition();
                        var dir = Vec2.FromRotation(p.Frame.rotation.f.AsVec2.RotationInRadians + (i - 1) * 0.5f);
                        at.SetVec2(p.Position.AsVec2 + p.Frame.rotation.f.AsVec2.Normalized() * 3f + new Vec2(dir.y, -dir.x) * ((i - 1) * 1.2f));
                        StreetEventsBehavior.Direct(listeners[i])?.GoTo(at, run: false, face: p);
                    }
                    v.Cast.Add(p);
                    v.Cast.AddRange(listeners);
                    v.Until += 10f;
                    return v;
                }
                case Kind.Drunks:
                {
                    var a = people.FirstOrDefault(x => StreetEventsBehavior.IsAdultLocal(x) && !x.IsFemale && InView(x, main));
                    var b = a == null ? null : people.Where(x => x != a && StreetEventsBehavior.IsAdultLocal(x) && !x.IsFemale && x.Position.Distance(a.Position) < 20f)
                        .OrderBy(x => x.Position.Distance(a.Position)).FirstOrDefault();
                    if (a == null || b == null) return null;
                    int pair = 1 + MBRandom.RandomInt(2) * 2;   // act_drunk_pair_1/2 or 3/4 go together
                    StreetEventsBehavior.Direct(a)?.Hold(face: b, loop: "act_drunk_pair_" + pair);
                    StreetEventsBehavior.Direct(b)?.GoTo(Beside(a, b, 1.1f), run: false, face: a, loop: "act_drunk_pair_" + (pair + 1));
                    v.Cast.Add(a); v.Cast.Add(b);
                    return v;
                }
                case Kind.MovedAlong:
                {
                    var beggar = people.FirstOrDefault(x => StreetEventsBehavior.IsBeggar(x) && InView(x, main));
                    var guard = beggar == null ? null : mission.Agents
                        .Where(x => x.IsActive() && x.IsHuman && StreetEventsBehavior.IsWatch(x) && !x.IsUsingGameObject
                                    && x.Position.Distance(beggar.Position) < 40f
                                    && x.GetComponent<CampaignAgentComponent>()?.AgentNavigator?.GetBehaviorGroup<DailyBehaviorGroup>()?.ScriptedBehavior == null)
                        .OrderBy(x => x.Position.Distance(beggar.Position)).FirstOrDefault();
                    if (beggar == null || guard == null) return null;
                    StreetEventsBehavior.Direct(beggar)?.Hold(face: guard, loop: "act_stand_beggar_1");
                    StreetEventsBehavior.Direct(guard)?.Follow(beggar, 1.6f, run: false);
                    v.Cast.Add(guard); v.Cast.Add(beggar);
                    v.Until = _time + 22f;
                    return v;
                }
                case Kind.Dance:
                {
                    var culture = Settlement.CurrentSettlement?.Culture?.StringId;
                    if (culture != "sturgia" && culture != "nord") return null;
                    var d = people.FirstOrDefault(x => StreetEventsBehavior.IsAdultLocal(x) && InView(x, main));
                    if (d == null) return null;
                    var crowd = people.Where(x => x != d && StreetEventsBehavior.IsLocal(x) && x.Position.Distance(d.Position) < 20f)
                        .OrderBy(x => x.Position.Distance(d.Position)).Take(3).ToList();
                    if (crowd.Count < 2) return null;
                    StreetEventsBehavior.Direct(d)?.Hold(loop: "act_dance_norse");
                    for (int i = 0; i < crowd.Count; i++)
                    {
                        var at = d.GetWorldPosition();
                        at.SetVec2(d.Position.AsVec2 + Vec2.FromRotation(i * 2.1f) * 2.6f);
                        StreetEventsBehavior.Direct(crowd[i])?.GoTo(at, run: false, face: d, loop: "act_cheer_" + (1 + i % 4));
                    }
                    v.Cast.Add(d);
                    v.Cast.AddRange(crowd);
                    return v;
                }
            }
            return null;
        }

        // ---- Playing it out: a line or two if you're close enough to hear ----

        private static readonly Dictionary<Kind, string[][]> Lines = new Dictionary<Kind, string[][]>
        {
            [Kind.Quarrel] = new[]
            {
                new[] { "{=lmmi_life_quarrel_1}You short-weighted me, you thief!", "{=lmmi_life_quarrel_2}Prove it! Go on, prove it!" },
                new[] { "{=lmmi_life_quarrel_3}That's my stall, and everyone knows it!", "{=lmmi_life_quarrel_4}Your stall? Your grandfather lost it at dice!" },
                new[] { "{=lmmi_life_quarrel_5}You owe me for the barley, and you know it.", "{=lmmi_life_quarrel_6}After the harvest! I told you — after the harvest!" },
                new[] { "{=lmmi_life_quarrel_7}That goat ate my cabbages!", "{=lmmi_life_quarrel_8}Then fence your cabbages!" },
                new[] { "{=lmmi_life_quarrel_9}Watered wine! You sold me watered wine!", "{=lmmi_life_quarrel_10}It's not my fault you can't hold it!" },
                new[] { "{=lmmi_life_quarrel_11}Your boy broke my shutter with his stick!", "{=lmmi_life_quarrel_12}My boy was with me all morning!" },
                new[] { "{=lmmi_life_quarrel_13}Three coppers for an egg? Robbery!", "{=lmmi_life_quarrel_14}Then lay your own eggs!" },
                new[] { "{=lmmi_life_quarrel_15}You moved the boundary stone. I saw you.", "{=lmmi_life_quarrel_16}I never! The rain shifted it!" },
                new[] { "{=lmmi_life_quarrel_17}That's my bucket. My mark's burned on the bottom.", "{=lmmi_life_quarrel_18}You can't even make your mark!" },
            },
            [Kind.Preacher] = new[]
            {
                new[] { "{=lmmi_life_preach_1}Repent! Heaven sees every coin you cheat your neighbor of!", "{=lmmi_life_preach_2}Give to the poor, and be given to in turn!" },
                new[] { "{=lmmi_life_preach_3}The rains fail because this city has forgotten its duty!", "{=lmmi_life_preach_4}Lords make war and the poor bury their sons. Remember that!" },
                new[] { "{=lmmi_life_preach_5}The war is a judgment! Mend your ways before it comes to your door!", "{=lmmi_life_preach_6}Feed the hungry, for tomorrow you may be one of them!" },
                new[] { "{=lmmi_life_preach_7}Gold rusts and silk rots! What will you carry to your grave?", "{=lmmi_life_preach_8}The merchant who cheats on his scales cheats his own soul!" },
                new[] { "{=lmmi_life_preach_9}Pride brought down the Empire, and it'll bring down this town too!", "{=lmmi_life_preach_10}Open your hands to the poor — aye, and your purses too!" },
                new[] { "{=lmmi_life_preach_11}Turn from the tavern and the dice! They eat your children's bread!", "{=lmmi_life_preach_12}Every lie you tell is a stone in your shoe on the last road!" },
            },
            [Kind.Drunks] = new[]
            {
                new[] { "{=lmmi_life_drunk_1}...and I said to him, I said — who're you calling a goat?", "{=lmmi_life_drunk_2}You're my besht friend. You know that? My besht friend." },
                new[] { "{=lmmi_life_drunk_3}One more! Jusht one more, then home.", "{=lmmi_life_drunk_4}Your wife'll have your hide." },
                new[] { "{=lmmi_life_drunk_5}I could take him. I could take the whole watch.", "{=lmmi_life_drunk_6}Sit down before you fall down." },
                new[] { "{=lmmi_life_drunk_7}Shing with me! Oh, the miller's daughter...", "{=lmmi_life_drunk_8}Not that one again. Not that one." },
                new[] { "{=lmmi_life_drunk_9}Where'sh my horse? I had a horse.", "{=lmmi_life_drunk_10}You came on foot, you fool." },
                new[] { "{=lmmi_life_drunk_11}I love thish town. I love all of you.", "{=lmmi_life_drunk_12}You said that about the last town, and they threw you out." },
            },
            [Kind.MovedAlong] = new[]
            {
                new[] { "{=lmmi_life_moved_1}Move along, you. Go on — not in front of the stalls.", "{=lmmi_life_moved_2}Please... just a coin..." },
                new[] { "{=lmmi_life_moved_3}Up, you. You can't sleep here.", "{=lmmi_life_moved_4}Where else am I to go?" },
                new[] { "{=lmmi_life_moved_5}Off the steps. The merchants are complaining.", "{=lmmi_life_moved_6}I'm going, I'm going..." },
            },
            [Kind.Dance] = new[]
            {
                new[] { "{=lmmi_life_dance_1}Hey! Hey! Faster!", "{=lmmi_life_dance_2}Ha! Look at him go!" },
                new[] { "{=lmmi_life_dance_3}Clap, then! Clap along!", "{=lmmi_life_dance_4}Go on! Go on!" },
                new[] { "{=lmmi_life_dance_5}Ha! Not bad at all!", "{=lmmi_life_dance_6}Another! Play another!" },
            },
        };

        private void Play(Vignette v, Agent main)
        {
            if (_time < v.NextLineAt || v.Lines >= 2) return;
            v.NextLineAt = _time + 6f + MBRandom.RandomFloat * 4f;
            var speaker = v.Cast.ElementAtOrDefault(v.Lines % Math.Max(1, v.Kind == Kind.Dance ? v.Cast.Count : 2));
            if (v.Kind == Kind.Preacher) speaker = v.Cast[0];
            if (v.Kind == Kind.Dance) speaker = v.Cast.ElementAtOrDefault(1 + v.Lines);
            if (speaker == null || !speaker.IsActive() || speaker.Position.Distance(main.Position) > 20f) return;
            var set = Lines[v.Kind][MBRandom.RandomInt(Lines[v.Kind].Length)];
            StreetEventsBehavior.Bark(speaker, new TextObject(set[v.Lines % set.Length]));
            v.Lines++;
        }

        private void End(Vignette v)
        {
            foreach (var a in v.Cast) StreetEventsBehavior.Release(a);
            if (_now == v) _now = null;
        }
    }
}
