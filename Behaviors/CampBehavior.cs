using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using SandBox;
using SandBox.Conversation;
using SandBox.Conversation.MissionLogics;
using SandBox.Missions.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation.Persuasion;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Source.Missions;
using TaleWorlds.ObjectSystem;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Warband's "Take a walk around", back: Ctrl+T or the map bar's camp button makes camp where you stand, on that
    /// place's battle terrain — once a day. Walk among your men by the fires and talk: how they're keeping (spirits,
    /// the last fight, what's wrong, the wagons), a cask from the wagons (wine or beer; once a camp), a war story told to
    /// the men gathered round (vanilla persuasion; once a camp), a bout (three a camp), or a drill (they fall in in ranks;
    /// once a camp). The per-camp limits are not bypassed by Test mode (only the once-a-day camp and the cooldowns are).
    /// Tab breaks camp.
    /// </summary>
    public class CampBehavior : CampaignBehaviorBase
    {
        public const string MissionName = "LmmiCamp";
        private const int MaxBouts = 3;

        /// <summary>A fight the player was in: when, how it went, against whom, our dead.</summary>
        public sealed class Battle
        {
            public double AtHours;
            public int Outcome;   // 1 won, -1 lost, 0 nobody won (withdrew)
            public int Dead;
            public string Foe = "";
            public float HoursAgo => (float)(CampaignTime.Now.ToHours - AtHours);
            public float DaysAgo => HoursAgo / CampaignTime.HoursInDay;
        }

        [NonSerialized] private Dictionary<string, double> _readyAtHours = new Dictionary<string, double>();
        [NonSerialized] private readonly List<Action> _afterTalk = new List<Action>();
        [NonSerialized] private List<Battle> _battles = new List<Battle>();
        [NonSerialized] private int _lastCampDay = -1;
        [NonSerialized] private bool _hinted;

        // This camp only.
        [NonSerialized] private int _bouts;
        [NonSerialized] private bool _caskOpened, _storyTold, _drilled;

        // The war story: gathered round, then the talk resumes with the same man.
        [NonSerialized] private readonly NativePersuasion _story = new NativePersuasion("camp_story");
        [NonSerialized] private Agent? _storyListener;
        [NonSerialized] private bool _storyReady, _storyAsked;
        [NonSerialized] private float _storyGiveUpAt;

        private static CampBehavior? Instance => Campaign.Current?.GetCampaignBehavior<CampBehavior>();

        public static void ClearCooldowns()
        {
            var self = Instance;
            if (self == null) return;
            self._readyAtHours.Clear();
            self._lastCampDay = -1;
        }

        public static Battle? LastBattle => Instance?._battles.LastOrDefault();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.MissionTickEvent.AddNonSerializedListener(this, OnMissionTick);
            CampaignEvents.OnPlayerBattleEndEvent.AddNonSerializedListener(this, OnPlayerBattleEnd);
        }

        // ---- Making camp ----

        private static int Today => (int)Math.Floor(CampaignTime.Now.ToDays);

        /// <summary>Every frame (SubModule): Ctrl+T on the map, and the map bar button's icon.</summary>
        internal static void OnApplicationTick()
        {
            try
            {
                if (!LmmiSettingsProvider.EnableCamp) return;
                if (!(Game.Current?.GameStateManager?.ActiveState is MapState map)) return;
                CampMapBarIcon.Ensure();
                if (map.AtMenu) return;
                Hint();
                if (!(Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)) || !Input.IsKeyPressed(InputKey.T)) return;
                TryMakeCamp();
            }
            catch (Exception ex) { LmmiLog.Error("CampBehavior.OnApplicationTick threw", ex); }
        }

        /// <summary>Once, on the map: how to make camp.</summary>
        internal static void Hint()
        {
            var self = Instance;
            if (self == null || self._hinted || !LmmiSettingsProvider.EnableCamp) return;
            self._hinted = true;
            InformationManager.DisplayMessage(new InformationMessage(new TextObject(
                "{=lmmi_camp_hint}(Less Menus, More Immersion) The camp button on the map bar (or Ctrl+T) makes camp where you stand — walk among your men.").ToString(), Colors.Gray));
        }

        /// <summary>Ctrl+T and the map bar's button.</summary>
        internal static void TryMakeCamp()
        {
            if (!CanCampNow(out var why))
            {
                InformationManager.DisplayMessage(new InformationMessage(why.ToString()));
                return;
            }
            OpenCamp();
        }

        internal static bool CanCampNow(out TextObject why)
        {
            var main = MobileParty.MainParty;
            why = TextObject.GetEmpty();
            var self = Instance;
            if (self == null || Campaign.Current == null) why = new TextObject("{=lmmi_camp_no_talk}Not now.");
            else if (Campaign.Current.ConversationManager?.IsConversationInProgress == true) why = new TextObject("{=lmmi_camp_no_talk}Not now.");
            else if (main == null || Hero.MainHero.IsPrisoner || !main.IsActive) why = new TextObject("{=lmmi_camp_no_free}You're in no position to make camp.");
            else if (main.CurrentSettlement != null) why = new TextObject("{=lmmi_camp_no_town}You can't make camp inside a settlement.");
            else if (main.MapEvent != null || PlayerEncounter.Current != null) why = new TextObject("{=lmmi_camp_no_battle}Not in the middle of an encounter.");
            else if (main.IsCurrentlyAtSea) why = new TextObject("{=lmmi_camp_no_sea}There's nowhere to pitch a tent at sea.");
            else if (main.Army != null && main.Army.LeaderParty != main) why = new TextObject("{=lmmi_camp_no_army}You march with someone else's army; they decide where to camp.");
            else if (Game.Current?.GameStateManager?.ActiveState is MapState map && map.AtMenu) why = new TextObject("{=lmmi_camp_no_talk}Not now.");
            else if (!LmmiSettingsProvider.TestMode && self._lastCampDay == Today) why = new TextObject("{=lmmi_camp_no_today}You've already made camp today.");
            return string.IsNullOrEmpty(why.ToString());
        }

        private static void OpenCamp()
        {
            var self = Instance;
            var main = MobileParty.MainParty;
            if (self == null || main == null) return;
            var position = main.Position;
            var patch = Campaign.Current.MapSceneWrapper.GetMapPatchAtPosition(in position);
            string scene = Campaign.Current.Models.SceneModel.GetBattleSceneForMapPatch(patch, false);
            if (string.IsNullOrEmpty(scene))
            {
                LmmiLog.Warning("Camp: no battle scene for this spot.");
                return;
            }
            LmmiLog.Info($"Camp: making camp on '{scene}'.");
            self._lastCampDay = Today;
            self._bouts = 0;
            self._caskOpened = self._storyTold = self._drilled = false;
            self.ForgetStory();
            self._afterTalk.Clear();
            Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
            var rec = SandBoxMissions.CreateSandBoxMissionInitializerRecord(scene, "", false, DecalAtlasGroup.Battle);
            MissionState.OpenNew(MissionName, rec, mission => new MissionBehavior[]
            {
                new MissionOptionsComponent(),
                new CampaignMissionComponent(),
                new MissionBasicTeamLogic(),
                new BasicLeaveMissionLogic(),
                new MissionConversationLogic(),
                new MissionAgentLookHandler(),
                new AgentHumanAILogic(),
                new MissionFightHandler(),
                new MissionHardBorderPlacer(),
                new MissionBoundaryPlacer(),
                new MissionBoundaryCrossingHandler(10f),
                new EquipmentControllerLeaveLogic(),
                new CampMissionLogic(),
            }, true, true);
        }

        /// <summary>From the mission's end: nothing queued for this camp may run in another scene.</summary>
        internal static void OnCampEnded()
        {
            var self = Instance;
            if (self == null) return;
            self._afterTalk.Clear();
            self.ForgetStory();
        }

        private void ForgetStory()
        {
            _storyListener = null;
            _storyReady = _storyAsked = false;
        }

        private void OnMissionTick(float dt)
        {
            var mission = Mission.Current;
            if (mission == null) return;
            bool talking = mission.Mode == MissionMode.Conversation || Campaign.Current?.ConversationManager?.IsConversationInProgress == true;
            if (_afterTalk.Count > 0 && !talking)
            {
                var actions = _afterTalk.ToList();
                _afterTalk.Clear();
                foreach (var a in actions)
                {
                    try { a(); }
                    catch (Exception ex) { LmmiLog.Error("Camp: after-conversation action threw", ex); }
                }
            }

            // The men are gathered: the story-teller's listener takes up the talk again.
            if (_storyReady && !talking && _afterTalk.Count == 0)
            {
                var listener = _storyListener;
                if (listener == null || !listener.IsActive() || _storyAsked || mission.CurrentTime > _storyGiveUpAt)
                {
                    LmmiLog.Info("Camp: the story never got told.");
                    ForgetStory();
                    CampMissionLogic.Current?.StoryReaction(null);
                    return;
                }
                _storyAsked = true;
                mission.GetMissionBehavior<MissionConversationLogic>()?.StartConversation(listener, setActionsInstantly: false);
            }
        }

        // ---- What the men remember: the last fights ----

        private void OnPlayerBattleEnd(MapEvent mapEvent)
        {
            try
            {
                if (mapEvent == null || mapEvent.IsRaid) return;
                var side = PartyBase.MainParty.Side;
                if (side == BattleSideEnum.None) return;
                int outcome = mapEvent.WinningSide == side ? 1 : mapEvent.WinningSide == BattleSideEnum.None ? 0 : -1;
                var other = mapEvent.GetMapEventSide(side == BattleSideEnum.Attacker ? BattleSideEnum.Defender : BattleSideEnum.Attacker);
                string foe = other?.LeaderParty?.Name?.ToString() ?? "";
                int dead = 0;
                var ours = PartyBase.MainParty.MapEventSide?.Parties?.FirstOrDefault(p => p.Party == PartyBase.MainParty);
                if (ours != null) dead = ours.DiedInBattle?.TotalManCount ?? 0;
                _battles.Add(new Battle { AtHours = CampaignTime.Now.ToHours, Outcome = outcome, Dead = dead, Foe = foe });
                if (_battles.Count > 6) _battles.RemoveAt(0);
                LmmiLog.Info($"Camp: remembered a fight — {(outcome > 0 ? "won" : outcome < 0 ? "lost" : "withdrew")} against '{foe}', {dead} dead.");
            }
            catch (Exception ex) { LmmiLog.Error("Camp: remembering the battle threw", ex); }
        }

        // ---- What the men say ----

        private static int FoodDays() => MobileParty.MainParty.GetNumDaysForFoodToLast();

        /// <summary>A line overheard by the fire: it follows morale, food, pay and the last fight.</summary>
        internal static TextObject CampLine()
        {
            float morale = MobileParty.MainParty.Morale;
            var pool = new List<string>();
            if (FoodDays() <= 2) pool.AddRange(new[]
            {
                "{=lmmi_camp_line_hungry_1}Is there anything in that pot but water?",
                "{=lmmi_camp_line_hungry_2}My belt's on the last notch. The last one.",
                "{=lmmi_camp_line_hungry_3}I'd eat my own boots if they weren't already holes.",
                "{=lmmi_camp_line_hungry_4}Two days of nothing but thin gruel. My stomach's eating itself.",
                "{=lmmi_camp_line_hungry_5}If the quartermaster says 'tomorrow' once more, I'll cook him.",
                "{=lmmi_camp_line_hungry_6}Somebody snare a rabbit. A rat. Anything.",
            });
            if (morale < 40f) pool.AddRange(new[]
            {
                "{=lmmi_camp_line_low_1}Another day's march to nowhere.",
                "{=lmmi_camp_line_low_2}I didn't sign on to die in a ditch for nothing.",
                "{=lmmi_camp_line_low_3}If we don't see a fight or a town soon, I'll desert myself.",
                "{=lmmi_camp_line_low_4}Pay's late, the food's bad, and the captain's got that look again.",
                "{=lmmi_camp_line_low_5}Feels like we've been walking in circles for a week.",
                "{=lmmi_camp_line_low_6}My brother's farm needs hands. Maybe I should go and be some.",
                "{=lmmi_camp_line_low_7}Nobody sings any more. Have you noticed?",
                "{=lmmi_camp_line_low_8}What are we even fighting for? Anyone?",
                "{=lmmi_camp_line_low_9}I've stopped counting the days. It doesn't help.",
            });
            else if (morale > 70f) pool.AddRange(new[]
            {
                "{=lmmi_camp_line_high_1}Did you see them run? I'll be telling that one to my grandchildren.",
                "{=lmmi_camp_line_high_2}Best company I've marched with, and I've marched with a few.",
                "{=lmmi_camp_line_high_3}Pass the skin — a toast to the captain!",
                "{=lmmi_camp_line_high_4}Whatever the captain's paying, I'd do it for half. Don't tell the captain.",
                "{=lmmi_camp_line_high_5}We could take a castle, the way we're going.",
                "{=lmmi_camp_line_high_6}Never had boots this good. Took them off a dead man, mind.",
                "{=lmmi_camp_line_high_7}Fill your cup! Fortune's smiling on this lot.",
                "{=lmmi_camp_line_high_8}They'll write songs about this company. Bad songs, but still.",
                "{=lmmi_camp_line_high_9}I've stopped worrying. The captain knows what we're about.",
            });
            var last = LastBattle;
            if (last != null && last.DaysAgo <= 3f)
                pool.AddRange(last.Outcome > 0
                    ? new[] { "{=lmmi_camp_line_won_1}Three of them, I swear. Three! Well — two and a half.",
                        "{=lmmi_camp_line_won_2}Did you see their faces when we came over the hill? Priceless.",
                        "{=lmmi_camp_line_won_3}My shield arm's still shaking. Good fight, though. Good fight.",
                        "{=lmmi_camp_line_won_4}Here's to the ones who ran — and the ones who didn't get the chance." }
                    : new[] { "{=lmmi_camp_line_lost_1}We'll pay them back. Next time.", "{=lmmi_camp_line_lost_2}Leave his bedroll where it is. Just — leave it.",
                        "{=lmmi_camp_line_lost_3}We should've held the line. I know we should've held the line.",
                        "{=lmmi_camp_line_lost_4}Nobody's hungry tonight. Funny, that.",
                        "{=lmmi_camp_line_lost_5}Next time I'm not running. Next time they run.",
                        "{=lmmi_camp_line_lost_6}Drink to the lads we left behind. Go on — drink." });
            pool.AddRange(new[]
            {
                "{=lmmi_camp_line_1}Who's got first watch? Not me. Not again.",
                "{=lmmi_camp_line_2}My boots are more hole than boot.",
                "{=lmmi_camp_line_3}Wind's changing. Rain by morning, mark me.",
                "{=lmmi_camp_line_4}Sing the one about the miller's daughter. No — the other one.",
                "{=lmmi_camp_line_5}Keep the fire low. No sense telling the whole valley we're here.",
                "{=lmmi_camp_line_6}Three dice, a sword, and a friend. What more does a man need?",
                "{=lmmi_camp_line_7}Who took my whetstone? I know one of you took my whetstone.",
                "{=lmmi_camp_line_8}Shift your feet, you're in my light.",
                "{=lmmi_camp_line_9}That sergeant snores like a siege engine.",
                "{=lmmi_camp_line_10}Pass the salt. Don't tell me we've no salt.",
                "{=lmmi_camp_line_11}Ever been to the coast? Neither have I. One day.",
                "{=lmmi_camp_line_12}My knee says it'll rain. My knee's never wrong.",
                "{=lmmi_camp_line_13}Smell that? Somebody's cooking something that used to be a horse.",
                "{=lmmi_camp_line_14}Tell the one about the miller's wife and the tax collector!",
                "{=lmmi_camp_line_15}If we march at dawn again, I'll eat my helmet.",
                "{=lmmi_camp_line_16}Hear that? Wolves. Or the cook.",
                "{=lmmi_camp_line_17}Keep the dice where I can see them, Rolf.",
                "{=lmmi_camp_line_18}Stars are out. Means it'll be cold. Means nothing, really.",
            });
            return new TextObject(pool[MBRandom.RandomInt(pool.Count)]);
        }

        private bool Ready(string key) => LmmiSettingsProvider.TestMode || !_readyAtHours.TryGetValue(key, out var at) || at <= CampaignTime.Now.ToHours;
        private void Cooldown(string key, float hours) => _readyAtHours[key] = CampaignTime.Now.ToHours + hours;

        private static bool TalkingToMan()
        {
            var camp = CampMissionLogic.Current;
            var who = ConversationMission.OneToOneConversationAgent;
            return camp != null && who != null && camp.Men.Contains(who);
        }

        private static bool Busy(out TextObject why)
        {
            why = TextObject.GetEmpty();
            if (CampMissionLogic.Current?.Busy != true) return false;
            why = new TextObject("{=lmmi_camp_busy}Not now — the men are busy.");
            return true;
        }

        /// <summary>What a cask takes from the wagons: one unit of wine or beer per 25 men (at least one), or what there is.</summary>
        private struct CaskPour
        {
            public ItemObject? Wine, Beer;
            public int WineUse, BeerUse, Need;
            public int Use => WineUse + BeerUse;
            public bool Any => Use > 0;

            public TextObject Describe()
            {
                if (!Any) return new TextObject("{=lmmi_camp_cask_use_none}no wine or beer");
                var parts = new List<string>();
                if (WineUse > 0 && Wine != null)
                {
                    var t = new TextObject("{=lmmi_camp_cask_piece}{COUNT}x {ITEM}");
                    t.SetTextVariable("COUNT", WineUse);
                    t.SetTextVariable("ITEM", Wine.Name);
                    parts.Add(t.ToString());
                }
                if (BeerUse > 0 && Beer != null)
                {
                    var t = new TextObject("{=lmmi_camp_cask_piece}{COUNT}x {ITEM}");
                    t.SetTextVariable("COUNT", BeerUse);
                    t.SetTextVariable("ITEM", Beer.Name);
                    parts.Add(t.ToString());
                }
                return new TextObject("{=!}" + string.Join(", ", parts));
            }
        }

        private static CaskPour PlanCask()
        {
            var plan = new CaskPour();
            try
            {
                var party = MobileParty.MainParty;
                if (party == null) return plan;
                plan.Wine = MBObjectManager.Instance?.GetObject<ItemObject>("wine");
                plan.Beer = MBObjectManager.Instance?.GetObject<ItemObject>("beer");
                plan.Need = Math.Max(1, party.MemberRoster.TotalManCount / 25);
                int wine = plan.Wine == null ? 0 : party.ItemRoster.GetItemNumber(plan.Wine);
                int beer = plan.Beer == null ? 0 : party.ItemRoster.GetItemNumber(plan.Beer);
                // Pour from the bigger stack each time, so neither runs dry first.
                for (int i = 0; i < plan.Need && (wine > 0 || beer > 0); i++)
                {
                    if (wine >= beer) { wine--; plan.WineUse++; }
                    else { beer--; plan.BeerUse++; }
                }
            }
            catch (Exception ex) { LmmiLog.Error("Camp: planning the cask threw", ex); }
            return plan;
        }

        /// <summary>The cask: +2; +3 after a fight in the last three days, +5 after a lost one; halved when spirits are
        /// already high, one less when the taste of home is already at its cap.</summary>
        private static float CaskMorale(out Battle? recent)
        {
            var party = MobileParty.MainParty;
            float gain = 2f;
            recent = LastBattle;
            if (recent != null && recent.DaysAgo > 3f) recent = null;
            if (recent != null) gain = recent.Outcome > 0 ? 3f : 5f;
            if (party.Morale > 70f) gain = (float)Math.Ceiling(gain / 2f);
            if (CampFoodMorale.Analyze(party).AtCap) gain -= 1f;
            return Math.Max(1f, gain);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try { AddDialogs(starter); }
            catch (Exception ex) { LmmiLog.Error("CampBehavior: failed to register dialogs", ex); }
        }

        private void AddDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("lmmi_camp_man", "start", "lmmi_camp_man_resp",
                "{=lmmi_camp_man}{LMMI_CAMP_GREET}",
                () =>
                {
                    if (!TalkingToMan()) return false;
                    float morale = MobileParty.MainParty.Morale;
                    var line = morale >= 70f
                        ? Flavor.Pick("{=lmmi_camp_man_high}Captain! Fine night for it. Pull up a log.",
                            "{=lmmi_camp_man_high_2}Captain! Come and sit — there's a spot by the fire.",
                            "{=lmmi_camp_man_high_3}Evening, captain! The lads are in fine spirits tonight.")
                        : morale >= 40f
                            ? Flavor.Pick("{=lmmi_camp_man_mid}Captain.", "{=lmmi_camp_man_mid_2}Captain. Quiet night.",
                                "{=lmmi_camp_man_mid_3}Evening, captain. Something you need?")
                            : Flavor.Pick("{=lmmi_camp_man_low}...Captain. The lads are grumbling, if you want it straight.",
                                "{=lmmi_camp_man_low_2}Captain. ...Don't expect smiles tonight.",
                                "{=lmmi_camp_man_low_3}What now, captain? ...Sorry. Long day.");
                    MBTextManager.SetTextVariable("LMMI_CAMP_GREET", line);
                    MBTextManager.SetTextVariable("LMMI_CAMP_CASK_USE", PlanCask().Describe());
                    return true;
                }, null, 1200);

            // How they're keeping: spirits, then the last fight and what's wrong, then the wagons.
            starter.AddPlayerLine("lmmi_camp_ask", "lmmi_camp_man_resp", "lmmi_camp_report",
                "{=lmmi_camp_ask}How are the men keeping?", null, null);
            starter.AddDialogLine("lmmi_camp_report", "lmmi_camp_report", "lmmi_camp_report_2",
                "{=lmmi_camp_report}{LMMI_CAMP_REPORT}",
                () => Say("LMMI_CAMP_REPORT", () => CampReport.Morale(MobileParty.MainParty)), null);
            starter.AddDialogLine("lmmi_camp_report_2", "lmmi_camp_report_2", "lmmi_camp_report_3",
                "{=lmmi_camp_report_2}{LMMI_CAMP_REPORT_2}",
                () => Say("LMMI_CAMP_REPORT_2", () => CampReport.Battles(MobileParty.MainParty, LastBattle)), null);
            starter.AddDialogLine("lmmi_camp_report_3", "lmmi_camp_report_3", "lmmi_camp_man_resp",
                "{=lmmi_camp_report_3}{LMMI_CAMP_REPORT_3}",
                () => Say("LMMI_CAMP_REPORT_3", () => CampReport.Stores(MobileParty.MainParty)), null);

            // A cask from the wagons (wine or beer): once a camp, whatever the test mode says.
            starter.AddPlayerLine("lmmi_camp_drink", "lmmi_camp_man_resp", "lmmi_camp_drink_resp",
                "{=lmmi_camp_cask}Break out a cask from the wagons — a round for everyone. [{LMMI_CAMP_CASK_USE}]", null, null, 100,
                (out TextObject why) =>
                {
                    if (Busy(out why)) return false;
                    if (_caskOpened) return true;   // the man answers
                    if (PlanCask().Any) return true;
                    why = new TextObject("{=lmmi_camp_cask_none}There's no wine or beer in the wagons.");
                    return false;
                });
            starter.AddDialogLine("lmmi_camp_drink_again", "lmmi_camp_drink_resp", "lmmi_camp_man_resp",
                "{=!}{LMMI_CAMP_CASK_AGAIN}",
                () => _caskOpened && Flavor.Say("LMMI_CAMP_CASK_AGAIN",
                        "{=lmmi_camp_cask_again}Another cask, captain? One a night. Any more and nobody stands watch.",
                        "{=lmmi_camp_cask_again_2}Another? The first one's barely touched the ground, captain. Tomorrow.",
                        "{=lmmi_camp_cask_again_3}One's plenty, captain. Somebody has to be able to see the sentries."), null, 200);
            starter.AddDialogLine("lmmi_camp_drink_resp", "lmmi_camp_drink_resp", "close_window",
                "{=!}{LMMI_CAMP_CASK_RESP}",
                () => Flavor.Say("LMMI_CAMP_CASK_RESP", "{=lmmi_camp_cask_resp}Lads! The captain's opened a cask! To the captain!",
                    "{=lmmi_camp_cask_resp_2}You hear that? Drinks on the captain! Cups out, lads!",
                    "{=lmmi_camp_cask_resp_3}Now that's a captain! Somebody fetch the cups before it changes its mind!"),
                () =>
                {
                    var plan = PlanCask();
                    if (!plan.Any) return;
                    float full = CaskMorale(out var recent);
                    float gain = Math.Max(1f, (float)Math.Round(full * plan.Use / plan.Need, MidpointRounding.AwayFromZero));
                    var items = MobileParty.MainParty.ItemRoster;
                    if (plan.WineUse > 0 && plan.Wine != null) items.AddToCounts(plan.Wine, -plan.WineUse);
                    if (plan.BeerUse > 0 && plan.Beer != null) items.AddToCounts(plan.Beer, -plan.BeerUse);
                    MobileParty.MainParty.RecentEventsMorale += gain;
                    _caskOpened = true;
                    var poured = new TextObject("{=lmmi_camp_cask_poured}You tap {POURED} from the wagons.");
                    poured.SetTextVariable("POURED", plan.Describe());
                    _afterTalk.Add(() =>
                    {
                        CampMissionLogic.Current?.Cheer();
                        InformationManager.DisplayMessage(new InformationMessage((recent == null
                            ? Flavor.Pick("{=lmmi_camp_drink_plain}The wine goes round. It's a good night.",
                                "{=lmmi_camp_drink_plain_2}Cups go round the fires. For a while nobody complains about anything.",
                                "{=lmmi_camp_drink_plain_3}The cask empties fast. Someone starts a song; most of them know the words.")
                            : recent.Outcome > 0
                                ? Flavor.Pick("{=lmmi_camp_drink_won}The wine goes round, and the battle gets bigger with every cup.",
                                    "{=lmmi_camp_drink_won_2}They drink to the victory. By the third cup, every man of them killed ten.",
                                    "{=lmmi_camp_drink_won_3}The fight's retold at every fire, louder each time.")
                                : Flavor.Pick("{=lmmi_camp_drink_lost}The wine goes round. Someone starts a song for the ones who didn't come back, and the whole camp joins in.",
                                    "{=lmmi_camp_drink_lost_2}They drink quietly, one cup poured out on the ground for each empty bedroll.",
                                    "{=lmmi_camp_drink_lost_3}Nobody talks about the fight. They just drink, and that's enough.")).ToString()));
                        InformationManager.DisplayMessage(new InformationMessage(poured.ToString()));
                    });
                    LmmiLog.Info($"Camp: a cask for the men ({plan.WineUse} wine, {plan.BeerUse} beer of {plan.Need} wanted): morale +{gain}.");
                    if (LmmiSettingsProvider.TestMode)
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"[LMMI Test] Cask: morale +{gain} (full {full}, {plan.Use}/{plan.Need} poured; recent fight: {(recent == null ? "none" : recent.Outcome > 0 ? "won" : "lost")}, morale {MobileParty.MainParty.Morale:0}, taste of home at cap: {CampFoodMorale.Analyze(MobileParty.MainParty).AtCap})", Colors.Cyan));
                });

            AddStory(starter);

            // A bout: three a camp.
            starter.AddPlayerLine("lmmi_camp_spar", "lmmi_camp_man_resp", "lmmi_camp_spar_resp",
                "{=lmmi_camp_spar}Fancy a bout? Fists only.", null, null, 100,
                (out TextObject why) => !Busy(out why));
            starter.AddDialogLine("lmmi_camp_spar_enough", "lmmi_camp_spar_resp", "lmmi_camp_man_resp",
                "{=!}{LMMI_CAMP_SPAR_ENOUGH}",
                () => _bouts >= MaxBouts && Flavor.Say("LMMI_CAMP_SPAR_ENOUGH",
                        "{=lmmi_camp_spar_enough}Come on, captain, calm down — who'll strike camp at this rate?",
                        "{=lmmi_camp_spar_enough_2}Three's plenty, captain. Leave some teeth in the company.",
                        "{=lmmi_camp_spar_enough_3}No more tonight, captain — the surgeon's run out of patience."), null, 200);
            starter.AddDialogLine("lmmi_camp_spar_resp", "lmmi_camp_spar_resp", "close_window",
                "{=!}{LMMI_CAMP_SPAR_RESP}",
                () => Flavor.Say("LMMI_CAMP_SPAR_RESP", "{=lmmi_camp_spar_resp}With you, captain? ...Alright. Don't hold it against me.",
                    "{=lmmi_camp_spar_resp_2}Ha! You're on, captain. No ducking behind your rank.",
                    "{=lmmi_camp_spar_resp_3}Clear a space, lads! The captain wants a beating!"),
                () =>
                {
                    _bouts++;
                    var foe = ConversationMission.OneToOneConversationAgent;
                    _afterTalk.Add(() => Spar(foe));
                });

            // A drill: they fall in, in ranks.
            starter.AddPlayerLine("lmmi_camp_drill", "lmmi_camp_man_resp", "lmmi_camp_drill_resp",
                "{=lmmi_camp_drill}On your feet — all of you. Drill.", null, null, 100,
                (out TextObject why) =>
                {
                    if (Busy(out why)) return false;
                    if (_drilled || Ready("drill")) return true;   // once drilled, the man answers
                    why = new TextObject("{=lmmi_camp_drill_done}They drilled today already. Push them any harder and they'll break.");
                    return false;
                });
            starter.AddDialogLine("lmmi_camp_drill_again", "lmmi_camp_drill_resp", "lmmi_camp_man_resp",
                "{=!}{LMMI_CAMP_DRILL_AGAIN}",
                () => _drilled && Flavor.Say("LMMI_CAMP_DRILL_AGAIN",
                        "{=lmmi_camp_drill_again}Again, captain? We've only just stopped aching. One drill a camp — let the lads eat.",
                        "{=lmmi_camp_drill_again_2}Not again, captain. They're dead on their feet as it is.",
                        "{=lmmi_camp_drill_again_3}Captain, if we drill once more there'll be nobody left to march."), null, 200);
            starter.AddDialogLine("lmmi_camp_drill_resp", "lmmi_camp_drill_resp", "close_window",
                "{=!}{LMMI_CAMP_DRILL_RESP}",
                () => Flavor.Say("LMMI_CAMP_DRILL_RESP", "{=lmmi_camp_drill_resp}...Yes, captain. On your feet, you heard!",
                    "{=lmmi_camp_drill_resp_2}Drill! Up, the lot of you — the captain wants to see you sweat!",
                    "{=lmmi_camp_drill_resp_3}...Right, captain. You heard! Leave the pot — it'll keep!"),
                () =>
                {
                    var party = MobileParty.MainParty;
                    var roster = party.MemberRoster;
                    int leadership = Hero.MainHero.GetSkillValue(DefaultSkills.Leadership);
                    int perMan = 20 + leadership / 5;
                    int men = 0;
                    foreach (var e in roster.GetTroopRoster().Where(e => e.Character != null && !e.Character.IsHero).ToList())
                    {
                        roster.AddXpToTroop(e.Character, perMan * (e.Number - e.WoundedNumber));
                        men += e.Number - e.WoundedNumber;
                    }
                    // Idle hands welcome it; men who've just bled resent it.
                    var last = LastBattle;
                    bool fought = last != null && last.DaysAgo <= 7f;
                    float morale = fought ? -2f : 2f;
                    party.RecentEventsMorale += morale;
                    Hero.MainHero.AddSkillXp(DefaultSkills.Leadership, 40f);
                    Cooldown("drill", 24f);
                    _drilled = true;
                    var msg = fought
                        ? Flavor.Pick("{=lmmi_camp_drilled}You drill them until the light goes — {MEN} men, sharper for it, and cursing your name. They've bled enough this week.",
                            "{=lmmi_camp_drilled_2}{MEN} men drill until they can barely lift a shield. They do it — but they've bled enough this week, and they let you know it.",
                            "{=lmmi_camp_drilled_3}You put {MEN} tired men through their paces. They're sharper for it. They're also muttering.")
                        : Flavor.Pick("{=lmmi_camp_drilled_idle}You drill them until the light goes — {MEN} men, sharper for it, and glad of something to do.",
                            "{=lmmi_camp_drilled_idle_2}{MEN} men drill until dusk. After a quiet week, they're glad to sweat.",
                            "{=lmmi_camp_drilled_idle_3}The drill shakes the idleness out of {MEN} men. Somebody even smiles.");
                    msg.SetTextVariable("MEN", men);
                    var sergeant = ConversationMission.OneToOneConversationAgent;
                    _afterTalk.Add(() =>
                    {
                        var camp = CampMissionLogic.Current;
                        if (camp == null || !camp.StartDrill(sergeant, msg))
                            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
                    });
                    LmmiLog.Info($"Camp: drilled {men} men ({perMan} xp each), morale {morale:+0;-0}.");
                });

            starter.AddPlayerLine("lmmi_camp_leave", "lmmi_camp_man_resp", "close_window",
                "{=lmmi_camp_leave}Carry on.", null, null);
        }

        private static bool Say(string variable, Func<TextObject> line)
        {
            try { MBTextManager.SetTextVariable(variable, line()); }
            catch (Exception ex)
            {
                LmmiLog.Error($"Camp: the report ({variable}) threw", ex);
                MBTextManager.SetTextVariable(variable, new TextObject("{=lmmi_camp_man_mid}Captain."));
            }
            return true;
        }

        // ---- The war story ----

        private void AddStory(CampaignGameStarter starter)
        {
            // Ask for their ear: the men get up and gather round you.
            starter.AddPlayerLine("lmmi_camp_story", "lmmi_camp_man_resp", "lmmi_camp_story_resp",
                "{=lmmi_camp_story}[Leadership] Gather round, lads. Let me tell you how it really went, that time...", null, null, 100,
                (out TextObject why) =>
                {
                    if (Busy(out why)) return false;
                    if (_storyTold || Ready("story")) return true;   // once told, the man answers
                    why = new TextObject("{=lmmi_camp_story_done}They've heard all your stories this week. Twice.");
                    return false;
                });
            starter.AddDialogLine("lmmi_camp_story_again", "lmmi_camp_story_resp", "lmmi_camp_man_resp",
                "{=!}{LMMI_CAMP_STORY_AGAIN}",
                () => _storyTold && Flavor.Say("LMMI_CAMP_STORY_AGAIN",
                        "{=lmmi_camp_story_again}Another one, captain? We've heard enough for one night. Save it for the road.",
                        "{=lmmi_camp_story_again_2}No more stories tonight, captain. Some of us want to sleep.",
                        "{=lmmi_camp_story_again_3}We've heard that one, captain. Twice. Tomorrow, eh?"), null, 200);
            starter.AddDialogLine("lmmi_camp_story_resp", "lmmi_camp_story_resp", "close_window",
                "{=!}{LMMI_CAMP_STORY_RESP}",
                () => Flavor.Say("LMMI_CAMP_STORY_RESP", "{=lmmi_camp_story_resp}Oi! Quiet, you lot — the captain's telling one!",
                    "{=lmmi_camp_story_resp_2}A story! Gather round, gather round — and somebody kick Torvin awake.",
                    "{=lmmi_camp_story_resp_3}Shut it, the lot of you! Captain's got a tale!"),
                () =>
                {
                    var listener = ConversationMission.OneToOneConversationAgent;
                    Cooldown("story", 24f);
                    _storyTold = true;
                    _storyListener = listener;
                    _storyReady = _storyAsked = false;
                    _afterTalk.Add(() =>
                    {
                        var camp = CampMissionLogic.Current;
                        var mission = Mission.Current;
                        if (listener == null || !listener.IsActive() || mission == null) { ForgetStory(); return; }
                        _storyGiveUpAt = mission.CurrentTime + 30f;
                        if (camp == null || !camp.GatherRound(listener, () => _storyReady = true)) _storyReady = true;
                    });
                });

            // They're settled: the same man takes up the talk.
            starter.AddDialogLine("lmmi_camp_story_open", "start", "lmmi_camp_story_pick",
                "{=lmmi_camp_story_open}{LMMI_CAMP_STORY_OPEN}",
                () =>
                {
                    if (!_storyReady || _storyListener == null || ConversationMission.OneToOneConversationAgent != _storyListener) return false;
                    MBTextManager.SetTextVariable("LMMI_CAMP_STORY_OPEN", Flavor.Pick(
                        "{=lmmi_camp_story_open_1}Right, they're all ears, captain. Go on — and don't skip the good part.",
                        "{=lmmi_camp_story_open_2}Settle down, lads, settle down. ...Go on, captain.",
                        "{=lmmi_camp_story_open_3}Everyone's here, captain. Make it a good one.",
                        "{=lmmi_camp_story_open_4}Quiet at the back! ...All yours, captain.",
                        "{=lmmi_camp_story_open_5}Fire's built up, cups are full. Let's hear it, captain.",
                        "{=lmmi_camp_story_open_6}They're waiting, captain. Don't make it a short one."));
                    return true;
                },
                () => _storyReady = false, 1300);

            starter.AddPlayerLine("lmmi_camp_story_recent", "lmmi_camp_story_pick", _story.Entry,
                "{=lmmi_camp_story_recent}[Tell them about the fight with {LMMI_CAMP_FOE}]",
                () =>
                {
                    var last = LastBattle;
                    if (last == null || last.DaysAgo > 30f || string.IsNullOrEmpty(last.Foe)) return false;
                    MBTextManager.SetTextVariable("LMMI_CAMP_FOE", new TextObject("{=!}" + last.Foe));
                    return true;
                },
                () => StartStory(recent: true));
            starter.AddPlayerLine("lmmi_camp_story_old", "lmmi_camp_story_pick", _story.Entry,
                "{=lmmi_camp_story_old}[Tell an old war story]", null,
                () => StartStory(recent: false));
            starter.AddPlayerLine("lmmi_camp_story_never", "lmmi_camp_story_pick", "close_window",
                "{=lmmi_camp_story_never}...On second thought — another night, lads.", null,
                () => _afterTalk.Add(() => CampMissionLogic.Current?.StoryReaction(null)));

            _story.Register(starter);
            _story.AddLeave(starter, "{=lmmi_camp_story_trail_off}[Trail off] ...Anyway. You had to be there.", "close_window",
                () => _afterTalk.Add(() => CampMissionLogic.Current?.StoryReaction(null)));
        }

        private void StartStory(bool recent)
        {
            var listener = (_storyListener ?? ConversationMission.OneToOneConversationAgent)?.Character as CharacterObject;
            var opening = recent
                ? Flavor.Pick("{=lmmi_camp_story_opening_recent}That one? We were there, captain. Go on then — tell it right.",
                    "{=lmmi_camp_story_opening_recent_2}Ha! That fight! Half of us still have the bruises. Go on.",
                    "{=lmmi_camp_story_opening_recent_3}We remember it, captain. Let's see if you remember it right.")
                : Flavor.Pick("{=lmmi_camp_story_opening_old}An old one, eh? Go on, captain. From the start.",
                    "{=lmmi_camp_story_opening_old_2}Before our time, was it? Go on, then. Make it a good one.",
                    "{=lmmi_camp_story_opening_old_3}An old campaign! Was it as bad as the old-timers say?");
            _story.Start(new[]
                {
                    NativePersuasion.Argument(DefaultSkills.Leadership, DefaultTraits.Valor,
                        new TextObject("{=lmmi_camp_story_arg_charge}The charge. A handful of us, a slope, and twice our number at the top — and not one of you hung back."), listener),
                    NativePersuasion.Argument(DefaultSkills.Charm, DefaultTraits.Mercy,
                        new TextObject("{=lmmi_camp_story_arg_fallen}The ones who didn't come back. Let me tell you who they were, so it's remembered right."), listener),
                    NativePersuasion.Argument(DefaultSkills.Tactics, DefaultTraits.Calculating,
                        new TextObject("{=lmmi_camp_story_arg_trick}The trick that won it. Here's the ford, here's the treeline — and here's where they never thought to look."), listener),
                },
                opening,
                Flavor.Pick("{=lmmi_camp_story_and_then}And then?", "{=lmmi_camp_story_and_then_2}Go on, go on!",
                    "{=lmmi_camp_story_and_then_3}And? What happened next?"),
                Flavor.Pick("{=lmmi_camp_story_won}Ha! That's how it was — that's exactly how it was! To the captain!",
                    "{=lmmi_camp_story_won_2}Ha! I'd forgotten that part! To the captain!",
                    "{=lmmi_camp_story_won_3}That's a story worth bleeding for! Again, captain, again!"),
                Flavor.Pick("{=lmmi_camp_story_lost}...Hrm. Sorry, captain — long day. Was that the end?",
                    "{=lmmi_camp_story_lost_2}...Right. Very good, captain. Very... long.",
                    "{=lmmi_camp_story_lost_3}Is that it? I thought there'd be more fighting in it."),
                onWon: () => _afterTalk.Add(() => StoryTold(true)),
                onLost: () => _afterTalk.Add(() => StoryTold(false)),
                goal: 2f, difficulty: PersuasionDifficulty.Medium);
        }

        private void StoryTold(bool won)
        {
            int leadership = Hero.MainHero.GetSkillValue(DefaultSkills.Leadership);
            Hero.MainHero.AddSkillXp(DefaultSkills.Leadership, won ? 50f : 15f);
            if (won) MobileParty.MainParty.RecentEventsMorale += 3f;
            CampMissionLogic.Current?.StoryReaction(won);
            InformationManager.DisplayMessage(new InformationMessage((won
                ? Flavor.Pick("{=lmmi_camp_story_good}By the end they're on their feet, roaring. They'll tell it better than you did.",
                    "{=lmmi_camp_story_good_2}They hang on every word. By morning it'll be all over the column.",
                    "{=lmmi_camp_story_good_3}When you finish there's a moment of silence — then the whole camp cheers.")
                : Flavor.Pick("{=lmmi_camp_story_bad}Somewhere around the second ambush, someone starts snoring.",
                    "{=lmmi_camp_story_bad_2}By the end, half of them have drifted back to their own fires.",
                    "{=lmmi_camp_story_bad_3}Polite nods. Someone coughs. The fire crackles.")).ToString(), won ? Colors.Green : Colors.White));
            LmmiLog.Info($"Camp: the war story {(won ? "landed" : "fell flat")} (Leadership {leadership}).");
            ForgetStory();
        }

        /// <summary>A bout by the fire: fists, first to fall. Both of you learn something.</summary>
        private static void Spar(Agent? foe)
        {
            var camp = CampMissionLogic.Current;
            if (foe == null || !foe.IsActive() || camp == null) return;
            var character = foe.Character as CharacterObject;
            camp.Cheer();
            if (!StreetEventsBehavior.Fistfight(new List<Agent> { foe }, 0.015f, won =>
                {
                    try
                    {
                        Hero.MainHero.AddSkillXp(DefaultSkills.Athletics, won ? 50f : 20f);
                        if (character != null) MobileParty.MainParty.MemberRoster.AddXpToTroop(character, 30);
                        MobileParty.MainParty.RecentEventsMorale += 1f;
                        InformationManager.DisplayMessage(new InformationMessage((won
                            ? Flavor.Pick("{=lmmi_camp_spar_won}He stays down, laughing. The men will be talking about that for days.",
                                "{=lmmi_camp_spar_won_2}He taps the ground and grins up at you. The fires cheer.",
                                "{=lmmi_camp_spar_won_3}Down he goes. Someone's already collecting on a wager.")
                            : Flavor.Pick("{=lmmi_camp_spar_lost}You're on your back, looking at the stars. The camp roars.",
                                "{=lmmi_camp_spar_lost_2}You land in the dirt. Somebody shouts 'Again!' — and laughs.",
                                "{=lmmi_camp_spar_lost_3}He helps you up, apologizing. The others are not so polite.")).ToString()));
                        LmmiLog.Info($"Camp: a bout with {character?.Name} — {(won ? "won" : "lost")}.");
                        if (foe.IsActive()) camp.Resettle(foe);
                    }
                    catch (Exception ex) { LmmiLog.Error("Camp: bout end threw", ex); }
                }))
                LmmiLog.Info("Camp: the bout couldn't start.");
        }

        // ---- Save / load ----
        // Cooldowns "key;readyAtHours" separated by '|'; the last camp's day; fights "hours;outcome;dead;foe" separated by '|'.

        public override void SyncData(IDataStore dataStore)
        {
            var inv = CultureInfo.InvariantCulture;
            string data = string.Empty, battles = string.Empty;
            int day = _lastCampDay;
            if (!dataStore.IsLoading)
            {
                double now = CampaignTime.Now.ToHours;
                data = string.Join("|", _readyAtHours.Where(kv => kv.Value > now)
                    .Select(kv => kv.Key + ";" + kv.Value.ToString("R", inv)));
                battles = string.Join("|", _battles.Select(b => b.AtHours.ToString("R", inv) + ";" + b.Outcome + ";" + b.Dead + ";"
                                                               + (b.Foe ?? "").Replace("|", " ").Replace(";", " ")));
            }
            dataStore.SyncData("lmmi_camp_v1", ref data);
            dataStore.SyncData("lmmi_camp_day_v1", ref day);
            dataStore.SyncData("lmmi_camp_battles_v1", ref battles);
            if (!dataStore.IsLoading) return;

            _readyAtHours = new Dictionary<string, double>();
            foreach (var entry in (data ?? string.Empty).Split('|'))
            {
                var p = entry.Split(';');
                if (p.Length == 2 && double.TryParse(p[1], NumberStyles.Float, inv, out var at))
                    _readyAtHours[p[0]] = at;
            }
            _lastCampDay = day;
            _battles = new List<Battle>();
            foreach (var entry in (battles ?? string.Empty).Split('|'))
            {
                var p = entry.Split(new[] { ';' }, 4);
                if (p.Length == 4 && double.TryParse(p[0], NumberStyles.Float, inv, out var at)
                    && int.TryParse(p[1], NumberStyles.Integer, inv, out var outcome) && int.TryParse(p[2], NumberStyles.Integer, inv, out var dead))
                    _battles.Add(new Battle { AtHours = at, Outcome = outcome, Dead = dead, Foe = p[3] });
            }
        }
    }
}
