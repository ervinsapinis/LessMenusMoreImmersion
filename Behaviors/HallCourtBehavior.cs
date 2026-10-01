using System;
using System.Collections.Generic;
using System.Linq;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using SandBox.Conversation;
using SandBox.Conversation.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// The lord's hall holds court. A petitioner comes before the lord — seed grain taken with the tax, a son pressed into
    /// the garrison, a poacher dragged in, a widow's field claimed — and the lord rules as their nature bids, unless you
    /// speak up first (vanilla persuasion) or back the lord. In your own hall they come to you, and what you rule moves
    /// the town's loyalty and security, your purse, and how the town speaks of you. And lords in the hall talk: wars,
    /// prisoners, armies on the move — and you.
    /// </summary>
    public class HallCourtBehavior : CampaignBehaviorBase
    {
        private enum PetitionKind { SeedGrain, Pressed, Poacher, Widow, Quartered, Feud }

        private sealed class Petition
        {
            public PetitionKind Kind;
            public Settlement Settlement = null!;
            public Agent Petitioner = null!;
            public Agent? Escort;
            public Hero? Judge;
            public Agent? JudgeAgent;
            public bool PlayerJudges;
            public float StartedAt;
            public bool Spoken;
            public float RuleAt = -1f;
            public bool Resolved;
            public float LeaveAt = -1f;
            public bool Talking;
            public bool Paused;         // a feast is on: the petition waits until it's over
            public float PausedAt;
            public float TalkAt;
            public Hero? A, B;          // Feud: the two notables
            public bool Won;            // your intercession, or your split
        }

        [NonSerialized] private Dictionary<string, double> _readyAtHours = new Dictionary<string, double>();
        [NonSerialized] private Mission? _mission;
        [NonSerialized] private float _time;
        [NonSerialized] private float _courtAt, _nextGossipAt;
        [NonSerialized] private bool _courtRolled;
        [NonSerialized] private Petition? _petition;
        [NonSerialized] private readonly List<Action> _afterTalk = new List<Action>();
        [NonSerialized] private readonly NativePersuasion _intercede = new NativePersuasion("court");

        private static HallCourtBehavior? Instance => Campaign.Current?.GetCampaignBehavior<HallCourtBehavior>();

        public static bool IsBusy => Instance?._petition != null;

        public static void ClearCooldowns() => Instance?._readyAtHours.Clear();

        public override void RegisterEvents()
        {
            CampaignEvents.MissionTickEvent.AddNonSerializedListener(this, OnMissionTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.ConversationEnded.AddNonSerializedListener(this, OnConversationEnded);
        }

        private static bool InHall(out Settlement settlement)
        {
            settlement = Settlement.CurrentSettlement!;
            return settlement != null && (settlement.IsTown || settlement.IsCastle) && CampaignMission.Current?.Location?.StringId == "lordshall";
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
                    _courtRolled = false;
                    _courtAt = 8f + MBRandom.RandomFloat * 8f;
                    _nextGossipAt = 20f + MBRandom.RandomFloat * 20f;
                    _petition = null;
                    _afterTalk.Clear();
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
                        catch (Exception ex) { LmmiLog.Error("Court: after-conversation action threw", ex); }
                    }
                }

                if (!LmmiSettingsProvider.EnableHallCourt) return;
                var main = Agent.Main;
                if (main == null || !main.IsActive() || !InHall(out var settlement)) return;

                if (_petition != null)
                {
                    // The feast wins: a pending petition waits (the petitioner steps aside) and resumes after.
                    var pending = _petition;
                    if (FeastBehavior.IsBusy && pending.LeaveAt < 0f) PausePetition(mission, pending);
                    else
                    {
                        if (pending.Paused) ResumePetition(pending);
                        UpdatePetition(mission, pending);
                    }
                }
                else if (!_courtRolled && _time >= _courtAt && mission.Mode != MissionMode.Conversation
                         && !FeastBehavior.IsBusy && !ArrivalScenesBehavior.IsBusy)
                {
                    _courtRolled = true;
                    TryCourt(mission, settlement);
                }

                if (_time >= _nextGossipAt)
                {
                    // Nobody talks shop over a feast: try again once it's over.
                    if (FeastBehavior.IsBusy) _nextGossipAt = _time + 20f;
                    else
                    {
                        _nextGossipAt = _time + 40f + MBRandom.RandomFloat * 30f;
                        Gossip(mission, settlement);
                    }
                }
            }
            catch (Exception ex)
            {
                LmmiLog.Error("HallCourtBehavior.OnMissionTick threw", ex);
                _petition = null;
            }
        }

        // ---- Court ----

        private void TryCourt(Mission mission, Settlement settlement)
        {
            var main = Agent.Main;
            bool yours = settlement.OwnerClan == Clan.PlayerClan;
            string key = "court:" + settlement.StringId;
            if (!LmmiSettingsProvider.TestMode)
            {
                if (_readyAtHours.TryGetValue(key, out var at) && at > CampaignTime.Now.ToHours) return;
                if (MBRandom.RandomFloat > (yours ? 0.6f : 0.45f)) return;
            }

            Agent? judgeAgent = null;
            Hero? judge = null;
            if (!yours)
            {
                // The lord (or one of the lord's house) sits in judgment.
                judgeAgent = mission.Agents
                    .Where(a => a.IsActive() && a.Character is CharacterObject co && co.IsHero && co.HeroObject != null
                                && co.HeroObject.Clan != null && co.HeroObject.Clan == settlement.OwnerClan && co.HeroObject.IsLord)
                    .OrderBy(a => ((CharacterObject)a.Character).HeroObject == settlement.OwnerClan.Leader ? 0 : 1)
                    .FirstOrDefault();
                if (judgeAgent == null) return;
                judge = ((CharacterObject)judgeAgent.Character).HeroObject;
            }

            var kinds = new List<PetitionKind> { PetitionKind.SeedGrain, PetitionKind.Pressed, PetitionKind.Poacher, PetitionKind.Widow };
            if (yours) kinds.Add(PetitionKind.Quartered);
            var notables = settlement.Notables.Where(n => n.IsAlive).ToList();
            if (yours && notables.Count >= 2) kinds.Add(PetitionKind.Feud);
            var kind = kinds[MBRandom.RandomInt(kinds.Count)];

            // In from the door (someone standing far off), toward the one who judges.
            var target = yours ? main : judgeAgent!;
            var from = mission.Agents
                .Where(a => a.IsActive() && a.IsHuman && a != main && a != target && a.Position.Distance(target.Position) > 8f && a.Position.Distance(target.Position) < 30f
                            && Math.Abs(a.Position.z - target.Position.z) < 1f)
                .OrderByDescending(a => a.Position.Distance(target.Position)).FirstOrDefault();
            var at2 = from?.Position ?? target.Position + new Vec3(6f, 0f, 0f);
            var culture = settlement.Culture;
            var who = culture == null ? null : settlement.IsTown ? (MBRandom.RandomInt(2) == 0 ? culture.Townsman : culture.Townswoman) : culture.Villager;
            if (kind == PetitionKind.Widow && culture != null) who = settlement.IsTown ? culture.Townswoman : culture.VillageWoman ?? culture.Townswoman;
            if (who == null) return;
            var petitioner = SceneSpawner.Spawn(mission, who, at2, 0f, civilian: true);
            if (petitioner == null) return;
            var p = new Petition { Kind = kind, Settlement = settlement, Petitioner = petitioner, Judge = judge, JudgeAgent = judgeAgent, PlayerJudges = yours, StartedAt = _time };
            if (kind == PetitionKind.Poacher)
            {
                // Dragged in by the watch.
                var guardType = settlement.Town?.GarrisonParty?.MemberRoster.GetTroopRoster().Where(e => e.Character != null && !e.Character.IsHero)
                    .Select(e => e.Character).FirstOrDefault() ?? culture?.BasicTroop;
                if (guardType != null)
                {
                    p.Escort = SceneSpawner.Spawn(mission, guardType, at2 + new Vec3(1f, 0f, 0f), 0f, civilian: false);
                    if (p.Escort != null) StreetEventsBehavior.Direct(p.Escort)?.Follow(petitioner, 1.1f, run: false);
                }
            }
            if (kind == PetitionKind.Feud)
            {
                var pair = notables.OrderBy(_ => MBRandom.RandomFloat).Take(2).ToList();
                p.A = pair[0];
                p.B = pair[1];
            }
            StreetEventsBehavior.Direct(petitioner)?.Follow(target, yours ? 2f : 2.2f, run: false);
            _petition = p;
            _readyAtHours[key] = CampaignTime.Now.ToHours + (yours ? 3 : 1) * CampaignTime.HoursInDay;
            LmmiLog.Info($"Court: a petition ({kind}) in {settlement.Name} before {(yours ? "you" : judge?.Name.ToString())}.");
        }

        private void PausePetition(Mission mission, Petition p)
        {
            if (p.Paused) return;
            if (p.Talking || mission.Mode == MissionMode.Conversation || _afterTalk.Count > 0) return;   // let them finish first
            if (!p.Petitioner.IsActive()) { EndPetition(p); return; }
            p.Paused = true;
            p.PausedAt = _time;
            StreetEventsBehavior.Direct(p.Petitioner)?.Hold(loop: "act_conversation_closed_loop");
            if (p.Escort != null && p.Escort.IsActive()) StreetEventsBehavior.Direct(p.Escort)?.Hold(face: p.Petitioner);
            LmmiLog.Info($"Court: a feast begins — the petition ({p.Kind}) waits.");
        }

        private void ResumePetition(Petition p)
        {
            p.Paused = false;
            float waited = _time - p.PausedAt;
            p.StartedAt += waited;
            if (p.RuleAt >= 0f) p.RuleAt += waited;
            p.Talking = false;
            if (!p.Petitioner.IsActive()) return;
            var target = p.PlayerJudges ? Agent.Main : p.JudgeAgent;
            if (p.Spoken && !p.PlayerJudges && target != null) StreetEventsBehavior.Direct(p.Petitioner)?.Hold(face: target, loop: "act_bullied");
            else if (target != null) StreetEventsBehavior.Direct(p.Petitioner)?.Follow(target, p.PlayerJudges ? 2f : 2.2f, run: false);
            if (p.Escort != null && p.Escort.IsActive()) StreetEventsBehavior.Direct(p.Escort)?.Follow(p.Petitioner, 1.1f, run: false);
            LmmiLog.Info($"Court: the feast is over — the petition ({p.Kind}) resumes after {waited:0}s.");
        }

        private void UpdatePetition(Mission mission, Petition p)
        {
            var main = Agent.Main;
            if (!p.Petitioner.IsActive()) { EndPetition(p); return; }

            if (p.LeaveAt >= 0f)
            {
                if (_time >= p.LeaveAt + 14f) EndPetition(p);
                return;
            }

            if (p.PlayerJudges)
            {
                // They come to you.
                if (p.Talking && mission.Mode != MissionMode.Conversation && _time - p.TalkAt > 5f) p.Talking = false;
                if (_time - p.StartedAt > 90f) { Leave(p); return; }
                if (p.Talking || mission.Mode == MissionMode.Conversation || _afterTalk.Count > 0) return;
                if (p.Petitioner.Position.Distance(main.Position) > 3.2f) return;
                p.Talking = true;
                p.TalkAt = _time;
                mission.GetMissionBehavior<MissionConversationLogic>()?.StartConversation(p.Petitioner, setActionsInstantly: false);
                return;
            }

            // Before the lord.
            var judge = p.JudgeAgent;
            if (judge == null || !judge.IsActive()) { Leave(p); return; }
            if (!p.Spoken && (p.Petitioner.Position.Distance(judge.Position) < 3f || _time - p.StartedAt > 25f))
            {
                p.Spoken = true;
                p.RuleAt = _time + 25f;
                StreetEventsBehavior.Direct(p.Petitioner)?.Hold(face: judge, loop: "act_bullied");
                if (Near(p.Petitioner)) StreetEventsBehavior.Bark(p.Petitioner, PleaLine(p));
                LmmiLog.Info($"Court: the petition is heard ({p.Kind}).");
            }
            if (p.Spoken && !p.Resolved && _time >= p.RuleAt && mission.Mode != MissionMode.Conversation
                && Campaign.Current?.ConversationManager?.IsConversationInProgress != true)
                Rule(p, LordWouldGrant(p.Judge!), interceded: false);
        }

        private static bool Near(Agent a) => Agent.Main != null && a.Position.Distance(Agent.Main.Position) < 20f;

        /// <summary>The merciful and generous grant it, the cruel throw it out; the rest could go either way.</summary>
        private static bool LordWouldGrant(Hero lord)
        {
            if (lord.GetTraitLevel(DefaultTraits.Mercy) >= 1 || lord.GetTraitLevel(DefaultTraits.Generosity) >= 1) return true;
            if (lord.GetTraitLevel(DefaultTraits.Mercy) <= -1) return false;
            return MBRandom.RandomFloat < 0.5f;
        }

        private void Rule(Petition p, bool granted, bool interceded)
        {
            p.Resolved = true;
            var judge = p.JudgeAgent;
            if (judge != null && judge.IsActive() && Near(judge)) StreetEventsBehavior.Bark(judge, RulingLine(p, granted));
            if (Near(p.Petitioner))
                StreetEventsBehavior.Bark(p.Petitioner, new TextObject(granted
                    ? "{=lmmi_court_thanks}Bless you, my lord — bless you!"
                    : "{=lmmi_court_despair}...Then heaven help us."));
            if (granted && interceded)
            {
                StreetEventsBehavior.MarkGrateful(p.Petitioner);
                TownStandingBehavior.Adjust(p.Settlement, 3f, "spoke for a petitioner in the lord's hall");
            }
            LmmiLog.Info($"Court: {p.Judge?.Name} {(granted ? "grants" : "refuses")} the petition{(interceded ? " (you spoke up)" : "")}.");
            Leave(p);
        }

        private void Leave(Petition p)
        {
            p.Resolved = true;
            p.LeaveAt = _time;
            var mission = Mission.Current;
            var main = Agent.Main;
            foreach (var a in new[] { p.Petitioner, p.Escort })
            {
                if (a == null || !a.IsActive() || mission == null) continue;
                var away = mission.Agents.Where(x => x.IsActive() && x.IsHuman && x != main && x != a && x.Position.Distance(a.Position) > 12f)
                    .OrderByDescending(x => x.Position.Distance(a.Position)).FirstOrDefault();
                if (away == null) continue;
                var wp = a.GetWorldPosition();
                wp.SetVec2(away.Position.AsVec2);
                StreetEventsBehavior.Direct(a)?.GoTo(wp, run: false);
            }
        }

        private void EndPetition(Petition p)
        {
            foreach (var a in new[] { p.Petitioner, p.Escort })
            {
                if (a == null || !a.IsActive()) continue;
                StreetEventsBehavior.Release(a);
                a.FadeOut(true, true);
            }
            if (_petition == p) _petition = null;
        }

        private static TextObject Lord(Hero? h) => new TextObject(h != null && h.IsFemale ? "{=lmmi_court_my_lady}my lady" : "{=lmmi_court_my_lord}my lord");

        private static TextObject PleaLine(Petition p)
        {
            var line = new TextObject(p.Kind switch
            {
                PetitionKind.SeedGrain => "{=lmmi_court_plea_grain}{LORD}, the tax collector took our seed grain along with the tax. Without it there's no crop next year — we'll starve before spring.",
                PetitionKind.Pressed => "{=lmmi_court_plea_pressed}{LORD}, your men took my son for the garrison. He's all I have to work the land — please, send him home.",
                PetitionKind.Poacher => "{=lmmi_court_plea_poacher}{LORD}, it was one deer — one! My children hadn't eaten in three days. Have mercy!",
                PetitionKind.Widow => "{=lmmi_court_plea_widow}{LORD}, my husband's not a month in the ground and his brother's claimed our field. Where am I to go?",
                _ => "{=lmmi_court_plea_generic}{LORD}, I beg you to hear me.",
            });
            line.SetTextVariable("LORD", Lord(p.Judge));
            return line;
        }

        private static TextObject RulingLine(Petition p, bool granted)
        {
            if (granted)
                return new TextObject(p.Kind switch
                {
                    PetitionKind.SeedGrain => "{=lmmi_court_grant_grain}Give them back their seed. A field that isn't sown pays no tax.",
                    PetitionKind.Pressed => "{=lmmi_court_grant_pressed}Send the boy home. The garrison can spare one farmer's son.",
                    PetitionKind.Poacher => "{=lmmi_court_grant_poacher}Let him go. A hungry man isn't a thief — this once.",
                    PetitionKind.Widow => "{=lmmi_court_grant_widow}The field is hers while she lives. Tell the brother he'll answer to me.",
                    _ => "{=lmmi_court_grant_generic}Granted.",
                });
            return new TextObject(p.Kind switch
            {
                PetitionKind.SeedGrain => "{=lmmi_court_deny_grain}The tax is the tax. If I spare you, I spare every village in the valley.",
                PetitionKind.Pressed => "{=lmmi_court_deny_pressed}He serves. We're at war, and every man counts.",
                PetitionKind.Poacher => "{=lmmi_court_deny_poacher}The law is the law. Take his hand.",
                PetitionKind.Widow => "{=lmmi_court_deny_widow}The land passes to the brother. That's custom.",
                _ => "{=lmmi_court_deny_generic}Refused.",
            });
        }

        // ---- Your own court: what you rule moves the town ----

        private static void Move(Settlement s, float loyalty = 0f, float security = 0f, float standing = 0f, string why = "")
        {
            var town = s.Town;
            if (town != null)
            {
                if (loyalty != 0f) town.Loyalty = TaleWorlds.Library.MathF.Clamp(town.Loyalty + loyalty, 0f, 100f);
                if (security != 0f) town.Security = TaleWorlds.Library.MathF.Clamp(town.Security + security, 0f, 100f);
            }
            if (standing != 0f) TownStandingBehavior.Adjust(s, standing, why);
            var parts = new List<string>();
            if (loyalty != 0f) parts.Add($"{new TextObject("{=lmmi_court_loyalty}Loyalty")} {(loyalty > 0 ? "+" : "")}{loyalty:0}");
            if (security != 0f) parts.Add($"{new TextObject("{=lmmi_court_security}Security")} {(security > 0 ? "+" : "")}{security:0}");
            if (parts.Count > 0)
                InformationManager.DisplayMessage(new InformationMessage($"{s.Name}: {string.Join(", ", parts)}", loyalty + security >= 0 ? Colors.Green : Colors.Red));
            LmmiLog.Info($"Court: your ruling in {s.Name} — loyalty {loyalty:+0;-0;0}, security {security:+0;-0;0}, standing {standing:+0;-0;0} ({why}).");
        }

        private void Judged(Action<Petition> effect)
        {
            var p = _petition;
            if (p == null) return;
            p.Resolved = true;
            _afterTalk.Add(() =>
            {
                try { effect(p); }
                catch (Exception ex) { LmmiLog.Error("Court: ruling threw", ex); }
                Leave(p);
            });
        }

        private void OnConversationEnded(IEnumerable<CharacterObject> characters)
        {
            var p = _petition;
            if (p == null || !p.PlayerJudges || !p.Talking) return;
            p.Talking = false;
            if (!p.Resolved) _afterTalk.Add(() => Leave(p));   // turned away without a hearing
        }

        // ---- Gossip ----

        private void Gossip(Mission mission, Settlement settlement)
        {
            var main = Agent.Main;
            var speakers = mission.Agents.Where(a => a.IsActive() && a != main && a.Character is CharacterObject co && co.IsHero
                                                     && co.HeroObject != null && co.HeroObject.IsLord && co.HeroObject.Clan != Clan.PlayerClan
                                                     && a.Position.Distance(main.Position) < 15f
                                                     && (_petition == null || a != _petition.JudgeAgent)).ToList();
            if (speakers.Count == 0) return;
            var speaker = speakers[MBRandom.RandomInt(speakers.Count)];
            var hero = ((CharacterObject)speaker.Character).HeroObject;
            var line = GossipLine(hero, settlement);
            if (line == null) return;
            StreetEventsBehavior.Bark(speaker, line);
        }

        private static TextObject? GossipLine(Hero speaker, Settlement settlement)
        {
            var options = new List<TextObject>();
            var realm = speaker.MapFaction as Kingdom;

            // A war.
            var enemy = Kingdom.All.Where(k => !k.IsEliminated && realm != null && k != realm && FactionManager.IsAtWarAgainstFaction(k, realm))
                .OrderBy(_ => MBRandom.RandomFloat).FirstOrDefault();
            if (enemy != null)
                options.Add(new TextObject("{=lmmi_gossip_war}The war with {KINGDOM} drags on. Every week, more widows — and still the council talks of glory.")
                    .SetTextVariable("KINGDOM", enemy.Name));
            else if (realm != null)
                options.Add(new TextObject("{=lmmi_gossip_peace}Peace. Enjoy it. It never lasts past the harvest."));

            // A lord in someone's dungeon.
            var prisoner = Hero.AllAliveHeroes.Where(h => h.IsLord && h.IsPrisoner && h.PartyBelongedToAsPrisoner?.Settlement != null && h != speaker)
                .OrderBy(_ => MBRandom.RandomFloat).FirstOrDefault();
            if (prisoner != null)
                options.Add(new TextObject("{=lmmi_gossip_prisoner}They say {PRISONER} is rotting in the cells at {PLACE}. Nobody's paid the ransom.")
                    .SetTextVariable("PRISONER", prisoner.Name).SetTextVariable("PLACE", prisoner.PartyBelongedToAsPrisoner.Settlement.Name));

            // An army on the move.
            var army = MobileParty.All.Where(p => p.IsActive && p.Army != null && p.Army.LeaderParty == p && p.LeaderHero != null)
                .OrderByDescending(p => p.Army.TotalManCount).FirstOrDefault();
            if (army != null)
                options.Add(new TextObject("{=lmmi_gossip_army}{LEADER} has an army in the field — {MEN} strong, last I heard. Someone's going to bleed.")
                    .SetTextVariable("LEADER", army.LeaderHero.Name).SetTextVariable("MEN", (int)army.Army.TotalManCount));

            // You.
            var band = TownStandingBehavior.Band(settlement);
            if (band != StandingBand.Unknown)
            {
                var deed = new TextObject(band switch
                {
                    StandingBand.Honored => "{=lmmi_gossip_you_honored}the town would follow {?PLAYER.GENDER}her{?}him{\\?} into a fire",
                    StandingBand.Respected => "{=lmmi_gossip_you_respected}{?PLAYER.GENDER}she's{?}he's{\\?} a friend to this place",
                    StandingBand.Known => "{=lmmi_gossip_you_known}{?PLAYER.GENDER}she's{?}he's{\\?} done right by the town",
                    StandingBand.Disliked => "{=lmmi_gossip_you_disliked}{?PLAYER.GENDER}she's{?}he's{\\?} nothing but trouble",
                    _ => "{=lmmi_gossip_you_despised}{?PLAYER.GENDER}she{?}he{\\?} should've been run out of the gates long ago",
                });
                options.Add(new TextObject("{=lmmi_gossip_you}That's {PLAYER.NAME}, over there. They say {DEED}.").SetTextVariable("DEED", deed));
            }
            return options.Count == 0 ? null : options[MBRandom.RandomInt(options.Count)];
        }

        // ---- Dialogs ----

        private bool Pending(bool playerJudges)
        {
            var p = _petition;
            return p != null && p.PlayerJudges == playerJudges && !p.Resolved;
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try
            {
                AddInterceptDialogs(starter);
                AddOwnCourtDialogs(starter);
            }
            catch (Exception ex) { LmmiLog.Error("HallCourtBehavior: failed to register dialogs", ex); }
        }

        /// <summary>Someone else's court: speak for the petitioner, or back the lord.</summary>
        private void AddInterceptDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_court_lord", "start", "lmmi_court_lord_resp",
                "{=lmmi_court_lord}I'm hearing a petition. Unless you've something to add to it?",
                () =>
                {
                    var p = _petition;
                    return p != null && !p.PlayerJudges && p.Spoken && !p.Resolved && p.Judge != null
                           && Hero.OneToOneConversationHero == p.Judge && !p.Paused && !FeastBehavior.IsBusy;
                }, null, 1300);

            starter.AddPlayerLine("lmmi_court_intercede", "lmmi_court_lord_resp", _intercede.Entry,
                "{=lmmi_court_intercede}[Speak for the petitioner] Hear them out, {LMMI_COURT_LORD}.",
                () => { MBTextManager.SetTextVariable("LMMI_COURT_LORD", Lord(_petition?.Judge)); return true; },
                () =>
                {
                    var p = _petition;
                    var lord = p?.Judge;
                    if (p == null || lord == null) return;
                    var listener = lord.CharacterObject;
                    float st = TownStandingBehavior.Get(p.Settlement);
                    _intercede.Start(new[]
                        {
                            NativePersuasion.Argument(DefaultSkills.Charm, DefaultTraits.Mercy,
                                new TextObject("{=lmmi_court_arg_mercy}They've nothing left to give. Mercy costs you less than their despair will."), listener, 0f, st),
                            NativePersuasion.Argument(DefaultSkills.Trade, DefaultTraits.Calculating,
                                new TextObject("{=lmmi_court_arg_calc}A ruined farmer pays no taxes next year. This is the cheaper ruling."), listener, 0f, st),
                            NativePersuasion.Argument(DefaultSkills.Leadership, DefaultTraits.Honor,
                                new TextObject("{=lmmi_court_arg_honor}Your people are watching how you judge the least of them. So is your name."), listener, 0f, st),
                        },
                        new TextObject("{=lmmi_court_opening}Go on. Briefly."),
                        new TextObject("{=lmmi_court_again}And?"),
                        new TextObject("{=lmmi_court_won}...Very well. You make a fair point."),
                        new TextObject("{=lmmi_court_lost}Enough. You forget whose hall this is."),
                        onWon: () =>
                        {
                            var p2 = _petition;
                            if (p2 == null) return;
                            p2.Resolved = true;
                            if (p2.Judge != null && p2.Judge.GetTraitLevel(DefaultTraits.Mercy) >= 0)
                                ChangeRelationAction.ApplyPlayerRelation(p2.Judge, 1, affectRelatives: false);
                            _afterTalk.Add(() => Rule(p2, granted: true, interceded: true));
                        },
                        onLost: () =>
                        {
                            var p2 = _petition;
                            if (p2 == null) return;
                            p2.Resolved = true;
                            if (p2.Judge != null) ChangeRelationAction.ApplyPlayerRelation(p2.Judge, -2, affectRelatives: false);
                            _afterTalk.Add(() => Rule(p2, granted: false, interceded: true));
                        });
                });

            starter.AddPlayerLine("lmmi_court_back_lord", "lmmi_court_lord_resp", "lmmi_court_backed",
                "{=lmmi_court_back_lord}Justice must be seen to be done, {LMMI_COURT_LORD}. Rule as you see fit.",
                () => { MBTextManager.SetTextVariable("LMMI_COURT_LORD", Lord(_petition?.Judge)); return true; },
                () =>
                {
                    var p = _petition;
                    if (p?.Judge == null) return;
                    ChangeRelationAction.ApplyPlayerRelation(p.Judge, 1, affectRelatives: false);
                    TownStandingBehavior.Adjust(p.Settlement, -1f, "backed the lord against a petitioner");
                });
            starter.AddDialogLine("lmmi_court_backed", "lmmi_court_backed", "close_window",
                "{=lmmi_court_backed}Quite so.", null, null);

            starter.AddPlayerLine("lmmi_court_nothing", "lmmi_court_lord_resp", "close_window",
                "{=lmmi_court_nothing}Nothing. Carry on.", null, null);

            _intercede.Register(starter);
        }

        /// <summary>Your own court: petitioners come to you.</summary>
        private void AddOwnCourtDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_court_own", "start", "lmmi_court_own_resp",
                "{=lmmi_court_own}{LMMI_COURT_PLEA}",
                () =>
                {
                    var p = _petition;
                    if (p == null || !p.PlayerJudges || p.Resolved || p.Paused || ConversationMission.OneToOneConversationAgent != p.Petitioner) return false;
                    if (!p.Talking) { p.Talking = true; p.TalkAt = _time; }
                    TextObject line;
                    if (p.Kind == PetitionKind.Quartered)
                        line = new TextObject("{=lmmi_court_plea_quartered}{LORD}, your soldiers are quartered in my house. They've eaten my stores bare, and one of them won't keep his hands off my daughter.");
                    else if (p.Kind == PetitionKind.Feud)
                        line = new TextObject("{=lmmi_court_plea_feud}{LORD}, {A} and {B} are at each other's throats over a debt — the whole street's taking sides. Only you can settle it.")
                            .SetTextVariable("A", p.A?.Name ?? TextObject.GetEmpty()).SetTextVariable("B", p.B?.Name ?? TextObject.GetEmpty());
                    else line = PleaLine(new Petition { Kind = p.Kind, Judge = Hero.MainHero });
                    line.SetTextVariable("LORD", Lord(Hero.MainHero));
                    MBTextManager.SetTextVariable("LMMI_COURT_PLEA", line);
                    if (p.A != null) MBTextManager.SetTextVariable("LMMI_COURT_A", p.A.Name);
                    if (p.B != null) MBTextManager.SetTextVariable("LMMI_COURT_B", p.B.Name);
                    return true;
                }, null, 1300);

            bool Is(PetitionKind k) => _petition?.Kind == k && _petition.PlayerJudges;
            void Option(string id, string text, PetitionKind kind, string reply, Action<Petition> effect, int gold = 0)
            {
                starter.AddPlayerLine(id, "lmmi_court_own_resp", id + "_resp", text, () => Is(kind),
                    () => { if (gold > 0) Hero.MainHero.ChangeHeroGold(-gold); Judged(effect); }, 100,
                    gold <= 0 ? null : (ConversationSentence.OnClickableConditionDelegate)((out TextObject why) =>
                    {
                        why = TextObject.GetEmpty();
                        if (Hero.MainHero.Gold >= gold) return true;
                        why = new TextObject("{=lmmi_cant_afford_alms}You don't have that much on you.");
                        return false;
                    }));
                starter.AddDialogLine(id + "_resp", id + "_resp", "close_window", reply, null, null);
            }

            // Seed grain taken with the tax.
            Option("lmmi_court_grain_pay", "{=lmmi_court_grain_pay}Here — two hundred from my own purse. Buy seed. [200{GOLD_ICON}]", PetitionKind.SeedGrain,
                "{=lmmi_court_grain_pay_resp}Bless you, {?PLAYER.GENDER}my lady{?}my lord{\\?}! The village won't forget this.",
                p => Move(p.Settlement, loyalty: 3f, standing: 3f, why: "paid for a village's seed grain"), 200);
            Option("lmmi_court_grain_half", "{=lmmi_court_grain_half}Half the grain goes back. The other half is owed.", PetitionKind.SeedGrain,
                "{=lmmi_court_grain_half_resp}...Half. It'll be a thin year, but we'll live. Thank you.",
                p => Move(p.Settlement, loyalty: 1f, standing: 1f, why: "returned half a village's seed grain"));
            Option("lmmi_court_grain_no", "{=lmmi_court_grain_no}The tax is the tax.", PetitionKind.SeedGrain,
                "{=lmmi_court_grain_no_resp}...Then heaven help us.",
                p => Move(p.Settlement, loyalty: -2f, standing: -2f, why: "refused a village its seed grain"));

            // A son pressed into the garrison.
            Option("lmmi_court_pressed_free", "{=lmmi_court_pressed_free}Send the boy home. The garrison can spare one farmer's son.", PetitionKind.Pressed,
                "{=lmmi_court_pressed_free_resp}Thank you — thank you! I'll tell everyone how you judged.",
                p => Move(p.Settlement, loyalty: 2f, security: -1f, standing: 2f, why: "sent a pressed boy home"));
            Option("lmmi_court_pressed_keep", "{=lmmi_court_pressed_keep}He serves. We need every man on the walls.", PetitionKind.Pressed,
                "{=lmmi_court_pressed_keep_resp}...Then keep him alive, at least. Please.",
                p => Move(p.Settlement, loyalty: -1f, security: 1f, why: "kept a pressed boy in the garrison"));

            // The poacher.
            Option("lmmi_court_poacher_hang", "{=lmmi_court_poacher_hang}Hang him. The forest law is plain.", PetitionKind.Poacher,
                "{=lmmi_court_poacher_hang_resp}No! No — please—",
                p => Move(p.Settlement, security: 3f, standing: -3f, why: "hanged a poacher"));
            Option("lmmi_court_poacher_fine", "{=lmmi_court_poacher_fine}A fine, and a warning. Next time it's the hand.", PetitionKind.Poacher,
                "{=lmmi_court_poacher_fine_resp}Yes — yes, thank you. It won't happen again.",
                p => { Hero.MainHero.ChangeHeroGold(30); Move(p.Settlement, security: 1f, standing: 1f, why: "fined a poacher"); });
            Option("lmmi_court_poacher_free", "{=lmmi_court_poacher_free}Let him go. A hungry man isn't a thief.", PetitionKind.Poacher,
                "{=lmmi_court_poacher_free_resp}Bless you! I'll — I'll not forget this.",
                p => Move(p.Settlement, loyalty: 1f, security: -1f, standing: 3f, why: "pardoned a hungry poacher"));

            // The widow's field.
            Option("lmmi_court_widow_hers", "{=lmmi_court_widow_hers}The field is hers while she lives. The brother can wait.", PetitionKind.Widow,
                "{=lmmi_court_widow_hers_resp}Thank you. I'd nowhere else to go.",
                p => Move(p.Settlement, loyalty: 1f, standing: 2f, why: "kept a widow on her land"));
            Option("lmmi_court_widow_custom", "{=lmmi_court_widow_custom}Custom gives it to the brother. I won't overturn custom.", PetitionKind.Widow,
                "{=lmmi_court_widow_custom_resp}...I understand. I'll go to my sister's.",
                p => Move(p.Settlement, standing: -1f, why: "gave a widow's field to her brother-in-law"));

            // Soldiers quartered.
            Option("lmmi_court_quartered_pay", "{=lmmi_court_quartered_pay}I'll pay for what they ate — and the man answers to me. [150{GOLD_ICON}]", PetitionKind.Quartered,
                "{=lmmi_court_quartered_pay_resp}That's more than fair. Thank you.",
                p => Move(p.Settlement, loyalty: 2f, standing: 2f, why: "paid for a quartered garrison and disciplined a soldier"), 150);
            Option("lmmi_court_quartered_no", "{=lmmi_court_quartered_no}They're here to defend you. Feed them, and hold your tongue.", PetitionKind.Quartered,
                "{=lmmi_court_quartered_no_resp}...As you say.",
                p => Move(p.Settlement, loyalty: -2f, security: 1f, standing: -2f, why: "sided with the garrison over a townsman"));

            // A feud between notables.
            Option("lmmi_court_feud_a", "{=lmmi_court_feud_a}{LMMI_COURT_A} is in the right.", PetitionKind.Feud,
                "{=lmmi_court_feud_resp}It'll be done as you say.",
                p =>
                {
                    if (p.A != null) ChangeRelationAction.ApplyPlayerRelation(p.A, 5, affectRelatives: false);
                    if (p.B != null) ChangeRelationAction.ApplyPlayerRelation(p.B, -5, affectRelatives: false);
                    Move(p.Settlement, loyalty: 1f, why: "settled a feud between notables");
                });
            Option("lmmi_court_feud_b", "{=lmmi_court_feud_b}{LMMI_COURT_B} is in the right.", PetitionKind.Feud,
                "{=lmmi_court_feud_resp}It'll be done as you say.",
                p =>
                {
                    if (p.B != null) ChangeRelationAction.ApplyPlayerRelation(p.B, 5, affectRelatives: false);
                    if (p.A != null) ChangeRelationAction.ApplyPlayerRelation(p.A, -5, affectRelatives: false);
                    Move(p.Settlement, loyalty: 1f, why: "settled a feud between notables");
                });
            Option("lmmi_court_feud_split", "{=lmmi_court_feud_split}[Charm] They share the loss, shake hands, and I hear no more of it.", PetitionKind.Feud,
                "{=lmmi_court_feud_split_resp}...We'll see if they shake.",
                p =>
                {
                    int charm = Hero.MainHero.GetSkillValue(DefaultSkills.Charm);
                    bool ok = MBRandom.RandomFloat < Math.Min(0.9f, 0.3f + charm / 150f);
                    int d = ok ? 2 : -2;
                    if (p.A != null) ChangeRelationAction.ApplyPlayerRelation(p.A, d, affectRelatives: false);
                    if (p.B != null) ChangeRelationAction.ApplyPlayerRelation(p.B, d, affectRelatives: false);
                    InformationManager.DisplayMessage(new InformationMessage(new TextObject(ok
                        ? "{=lmmi_court_feud_split_ok}They shake — grudgingly. The street settles."
                        : "{=lmmi_court_feud_split_no}Neither will give an inch. Now both of them resent you.").ToString(), ok ? Colors.Green : Colors.Red));
                    Move(p.Settlement, loyalty: ok ? 2f : 0f, why: ok ? "made two notables shake hands" : "failed to settle a feud");
                });

            starter.AddPlayerLine("lmmi_court_own_later", "lmmi_court_own_resp", "lmmi_court_own_later_resp",
                "{=lmmi_court_own_later}Not now. Come back another day.", null,
                () =>
                {
                    var p = _petition;
                    if (p == null) return;
                    Judged(pp => Move(pp.Settlement, loyalty: -1f, why: "turned a petitioner away"));
                });
            starter.AddDialogLine("lmmi_court_own_later_resp", "lmmi_court_own_later_resp", "close_window",
                "{=lmmi_court_own_later_resp}...Yes, {?PLAYER.GENDER}my lady{?}my lord{\\?}.", null, null);
        }

        // ---- Save / load: "key;readyAtHours" separated by '|' ----

        public override void SyncData(IDataStore dataStore)
        {
            string data = string.Empty;
            if (!dataStore.IsLoading)
            {
                double now = CampaignTime.Now.ToHours;
                data = string.Join("|", _readyAtHours.Where(kv => kv.Value > now)
                    .Select(kv => kv.Key + ";" + kv.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            }
            dataStore.SyncData("lmmi_court_v1", ref data);
            if (!dataStore.IsLoading) return;
            _readyAtHours = new Dictionary<string, double>();
            foreach (var entry in (data ?? string.Empty).Split('|'))
            {
                var p = entry.Split(';');
                if (p.Length == 2 && double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var at))
                    _readyAtHours[p[0]] = at;
            }
        }
    }
}
