using System;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using SandBox.Conversation;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Dice in the tavern: any patron will play (well — most; some won't dice with your kind). Two dice each, high roll
    /// takes the pot: ten, fifty or a hundred. With some Roguery you can bring your own dice — a sure thing, until someone
    /// notices. Win too often in one evening and the room stops playing with you.
    /// The tavern keeper (TavernKeeper.cs, the other half of this class) treats you as the town speaks of you.
    /// </summary>
    public partial class TavernLifeBehavior : CampaignBehaviorBase
    {
        private const int WinsPerVisit = 5;

        [NonSerialized] private Mission? _mission;
        [NonSerialized] private int _wins;
        [NonSerialized] private int _stake;
        [NonSerialized] private bool _cheating;
        [NonSerialized] private TextObject _result = TextObject.GetEmpty();
        [NonSerialized] private bool _caught;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.MissionTickEvent.AddNonSerializedListener(this, OnMissionTick);
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, OnKeeperMissionEnded);
            CampaignEvents.GameMenuOpened.AddNonSerializedListener(this, OnKeeperMenuOpened);
        }

        public override void SyncData(IDataStore dataStore) => SyncKeeper(dataStore);

        private bool InTavernWithPatron()
        {
            if (!LmmiSettingsProvider.EnableTavernGames) return false;
            if (CampaignMission.Current?.Location?.StringId != "tavern") return false;
            var who = ConversationMission.OneToOneConversationAgent;
            if (who == null || !(who.Character is CharacterObject co) || co.IsHero) return false;
            if (co.Occupation != Occupation.Townsfolk && co.Occupation != Occupation.Villager) return false;
            if (Mission.Current != _mission) { _mission = Mission.Current; _wins = 0; }
            return true;
        }

        /// <summary>Some won't dice with a foreigner their town resents (and who's done nothing for it).</summary>
        private static bool Refuses()
        {
            var s = Settlement.CurrentSettlement;
            if (s == null || s.Culture == Hero.MainHero.Culture) return false;
            float resentment = CultureRelations.Multiplier(s.Culture?.StringId, Hero.MainHero.Culture?.StringId)
                               * LmmiSettingsProvider.ForeignerPrejudicePercent / 100f;
            if (resentment < 1f || TownStandingBehavior.Get(s) > 0f) return false;
            return (ConversationMission.OneToOneConversationAgent?.Index ?? 0) % 2 == 0;   // the same patron always answers the same
        }

        private static int D6() => MBRandom.RandomInt(1, 7);

        private void Roll(int stake, bool cheat)
        {
            _stake = stake;
            _cheating = cheat;
            _caught = false;
            int mine, theirs;
            do
            {
                theirs = D6() + D6();
                mine = cheat ? Math.Min(12, Math.Max(D6() + D6(), theirs + 1)) : D6() + D6();
            } while (mine == theirs);

            if (cheat)
            {
                int roguery = Hero.MainHero.GetSkillValue(DefaultSkills.Roguery);
                _caught = MBRandom.RandomFloat < Math.Max(0.05f, 0.45f - roguery / 250f);
                Hero.MainHero.AddSkillXp(DefaultSkills.Roguery, 20f);
            }
            if (_caught)
            {
                int pay = Math.Min(Hero.MainHero.Gold, stake * 2);
                Hero.MainHero.ChangeHeroGold(-pay);
                TownStandingBehavior.Adjust(Settlement.CurrentSettlement, -2f, "caught cheating at dice");
                _result = Flavor.Pick("{=lmmi_dice_caught}Hold on — let me see those. ...LOADED! Cheat! You'll pay double, or I'll have the whole room on you!",
                    "{=lmmi_dice_caught_2}Wait. Wait! These dice are weighted! Pay double, cheat, or I'll break your fingers!",
                    "{=lmmi_dice_caught_3}Ha! Thought I wouldn't notice? Loaded dice! Double, now, or we take it out of your hide!");
                LmmiLog.Info($"Tavern: caught cheating at dice ({pay} paid).");
                return;
            }
            bool won = mine > theirs;
            Hero.MainHero.ChangeHeroGold(won ? stake : -stake);
            if (won) _wins++;
            _result = won
                ? Flavor.Pick("{=lmmi_dice_won}{MINE} against {THEIRS}. ...Yours. Curse your luck.",
                    "{=lmmi_dice_won_2}Your {MINE} to my {THEIRS}. Take it — and choke on it.",
                    "{=lmmi_dice_won_3}{MINE}? Against my {THEIRS}? The gods hate me tonight.",
                    "{=lmmi_dice_won_4}{MINE} beats {THEIRS}. Fine. Fine! It's yours.")
                : Flavor.Pick("{=lmmi_dice_lost}{MINE} against {THEIRS} — mine! Better luck next round.",
                    "{=lmmi_dice_lost_2}Your {MINE} to my {THEIRS}. Pay up, friend.",
                    "{=lmmi_dice_lost_3}{THEIRS}! Ha! Your {MINE} won't cut it.",
                    "{=lmmi_dice_lost_4}{MINE} against {THEIRS}. The coin's mine, I think.");
            _result.SetTextVariable("MINE", mine);
            _result.SetTextVariable("THEIRS", theirs);
            LmmiLog.Info($"Tavern: dice for {stake}{(cheat ? " (loaded)" : "")}: {mine} vs {theirs} — {(won ? "won" : "lost")}.");
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try { AddDialogs(starter); }
            catch (Exception ex) { LmmiLog.Error("TavernLifeBehavior: failed to register dialogs", ex); }
            try { AddKeeperDialogs(starter); }
            catch (Exception ex) { LmmiLog.Error("TavernLifeBehavior: failed to register the tavern keeper's dialogs", ex); }
            // Market prices by town standing: patched onto whichever trade price model the campaign ended up with.
            StandingPricePatch.Apply();
        }

        private void AddDialogs(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("lmmi_dice_ask", "town_or_village_player", "lmmi_dice_answer",
                "{=lmmi_dice_ask}Fancy a game of dice?", InTavernWithPatron, null, 110);

            starter.AddDialogLine("lmmi_dice_refuse", "lmmi_dice_answer", "town_or_village_pretalk",
                "{=lmmi_dice_refuse}{LMMI_DICE_REFUSAL}",
                () =>
                {
                    TextObject? line = null;
                    if (_wins >= WinsPerVisit) line = Flavor.Pick("{=lmmi_dice_refuse_wins}Not with you. You've cleaned out half the room tonight.",
                        "{=lmmi_dice_refuse_wins_2}Play with you again? I'd sooner hand you my purse and save us both the time.",
                        "{=lmmi_dice_refuse_wins_3}No more. You've the gods' own luck tonight, and I've a family to feed.");
                    else if (Refuses())
                    {
                        line = Flavor.Pick("{=lmmi_dice_refuse_foreign}I don't dice with {DEMONYM}s. Find your own kind.",
                            "{=lmmi_dice_refuse_foreign_2}Dice with a {SLUR}? Not in this life.",
                            "{=lmmi_dice_refuse_foreign_3}Your kind cheat at everything. Go and roll with your own.",
                            "{=lmmi_dice_refuse_foreign_4}No {DEMONYM} coin on my table. Off with you.");
                        line.SetTextVariable("DEMONYM", CultureWords.Demonym(Hero.MainHero.Culture));
                        line.SetTextVariable("SLUR", CultureWords.Slur(Hero.MainHero.Culture));
                    }
                    if (line == null) return false;
                    MBTextManager.SetTextVariable("LMMI_DICE_REFUSAL", line);
                    return true;
                }, null, 110);
            starter.AddDialogLine("lmmi_dice_answer", "lmmi_dice_answer", "lmmi_dice_stakes",
                "{=!}{LMMI_DICE_ANSWER}",
                    () => Flavor.Say("LMMI_DICE_ANSWER",
                        "{=lmmi_dice_answer}Always. What's the stake?",
                        "{=lmmi_dice_answer_2}Ha! Sit down. What are we playing for?",
                        "{=lmmi_dice_answer_3}Feeling lucky? What's the stake, then?"), null);

            void Stake(string id, string text, int stake, bool cheat, Func<bool>? extra = null)
            {
                starter.AddPlayerLine(id, "lmmi_dice_stakes", "lmmi_dice_result", text,
                    () => extra == null || extra(),
                    () => Roll(stake, cheat), 100,
                    (out TextObject why) =>
                    {
                        why = TextObject.GetEmpty();
                        if (Hero.MainHero.Gold >= stake) return true;
                        why = new TextObject("{=lmmi_cant_afford_alms}You don't have that much on you.");
                        return false;
                    });
            }
            Stake("lmmi_dice_10", "{=lmmi_dice_10}Ten denars. [10{GOLD_ICON}]", 10, false);
            Stake("lmmi_dice_50", "{=lmmi_dice_50}Fifty. [50{GOLD_ICON}]", 50, false);
            Stake("lmmi_dice_100", "{=lmmi_dice_100}A hundred. [100{GOLD_ICON}]", 100, false);
            Stake("lmmi_dice_cheat", "{=lmmi_dice_cheat}[Roguery] Fifty — and we'll use my dice. [50{GOLD_ICON}]", 50, true,
                () => Hero.MainHero.GetSkillValue(DefaultSkills.Roguery) >= 30);
            starter.AddPlayerLine("lmmi_dice_nevermind", "lmmi_dice_stakes", "town_or_village_pretalk",
                "{=lmmi_dice_nevermind}On second thought, no.", null, null);

            starter.AddDialogLine("lmmi_dice_result_caught", "lmmi_dice_result", "close_window",
                "{=lmmi_dice_result_caught}{LMMI_DICE_RESULT}",
                () => { if (!_caught) return false; MBTextManager.SetTextVariable("LMMI_DICE_RESULT", _result); return true; }, null);
            starter.AddDialogLine("lmmi_dice_result", "lmmi_dice_result", "lmmi_dice_again",
                "{=lmmi_dice_result}{LMMI_DICE_RESULT}",
                () => { MBTextManager.SetTextVariable("LMMI_DICE_RESULT", _result); return true; }, null);
            starter.AddPlayerLine("lmmi_dice_again", "lmmi_dice_again", "lmmi_dice_answer",
                "{=lmmi_dice_again}Another round.", null, null);
            starter.AddPlayerLine("lmmi_dice_enough", "lmmi_dice_again", "town_or_village_pretalk",
                "{=lmmi_dice_enough}That's enough for me.", null, null);
        }
    }
}
