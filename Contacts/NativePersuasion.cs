using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Conversation.Persuasion;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Contacts
{
    /// <summary>
    /// Vanilla's persuasion, wired into one of our conversations — the same machinery the Prodigal Son and Family Feud
    /// quests use: the progress bar, each argument's skill and trait with its chance shown, critical success and failure,
    /// vanilla's reactions ("That's a good point", "You're wasting my time"). Register once per conversation (its dialog
    /// states), Start it from a consequence and jump to <see cref="Entry"/>; it calls back won or lost.
    /// Arguments are ordinary <see cref="PersuasionOptionArgs"/>; <see cref="Argument"/> builds one whose strength
    /// follows the listener: their own traits (vanilla's trait correlation), then prejudice.
    /// </summary>
    internal sealed class NativePersuasion
    {
        private readonly string _key;
        private PersuasionTask? _task;
        private PersuasionDifficulty _difficulty;
        private TextObject _opening = TextObject.GetEmpty(), _again = TextObject.GetEmpty();
        private TextObject _won = TextObject.GetEmpty(), _lost = TextObject.GetEmpty();
        private Action? _onWon, _onLost;
        private int _round;

        public NativePersuasion(string key) { _key = key; }

        /// <summary>The dialog state to go to once started (the listener's turn).</summary>
        public string Entry => "lmmi_p_" + _key + "_start";

        public bool Active => _task != null;

        /// <param name="goal">Successes needed (a critical success counts double).</param>
        public void Start(IEnumerable<PersuasionOptionArgs> arguments, TextObject opening, TextObject again, TextObject won, TextObject lost,
            Action onWon, Action onLost, float goal = 1f, PersuasionDifficulty difficulty = PersuasionDifficulty.Medium)
        {
            _task = new PersuasionTask(0) { FinalFailLine = lost, TryLaterLine = TextObject.GetEmpty(), SpokenLine = opening };
            foreach (var a in arguments.Take(4)) _task.AddOptionToTask(a);
            _opening = opening;
            _again = again;
            _won = won;
            _lost = lost;
            _onWon = onWon;
            _onLost = onLost;
            _difficulty = difficulty;
            _round = 0;
            ConversationManager.StartPersuasion(goal, 1f, 0f, 2f, 2f, 0f, difficulty);
            LmmiLog.Info($"Persuasion '{_key}': {_task.Options.Count} arguments, goal {goal}, {difficulty}: "
                         + string.Join("; ", _task.Options.Select(o => $"{o.SkillUsed?.Name}/{o.TraitUsed?.Name} {o.ArgumentStrength}")));
        }

        public void Register(CampaignGameStarter starter, string wonState = "close_window", string lostState = "close_window")
        {
            string start = Entry, choose = "lmmi_p_" + _key + "_choose", react = "lmmi_p_" + _key + "_react";

            starter.AddDialogLine("lmmi_p_" + _key + "_won", start, wonState, "{=!}{LMMI_P_LINE}",
                () => Active && ConversationManager.GetPersuasionProgressSatisfied() && Say(_won),
                () => Finish(true), int.MaxValue);
            starter.AddDialogLine("lmmi_p_" + _key + "_ask", start, choose, "{=!}{LMMI_P_LINE}",
                () => Active && !ConversationManager.GetPersuasionIsFailure() && _task!.Options.Any(o => !o.IsBlocked)
                      && Say(_round++ == 0 ? _opening : _again),
                null, 200);
            starter.AddDialogLine("lmmi_p_" + _key + "_lost", start, lostState, "{=!}{LMMI_P_LINE}",
                () => Active && Say(_lost),
                () => Finish(false), 100);

            for (int i = 0; i < 4; i++)
            {
                int k = i;
                starter.AddPlayerLine("lmmi_p_" + _key + "_arg" + k, choose, react, "{=!}{LMMI_P_ARG" + k + "}",
                    () =>
                    {
                        if (_task == null || _task.Options.Count <= k) return false;
                        var option = _task.Options[k];
                        var text = new TextObject("{=!}{PERSUASION_OPTION_LINE} {SUCCESS_CHANCE}");
                        text.SetTextVariable("PERSUASION_OPTION_LINE", option.Line);
                        text.SetTextVariable("SUCCESS_CHANCE", PersuasionHelper.ShowSuccess(option, false));
                        MBTextManager.SetTextVariable("LMMI_P_ARG" + k, text);
                        return true;
                    },
                    () => _task?.Options[k].BlockTheOption(true),
                    100,
                    (out TextObject hint) =>
                    {
                        hint = TextObject.GetEmpty();
                        if (_task == null || _task.Options.Count <= k) return false;
                        if (!_task.Options[k].IsBlocked) return true;
                        hint = new TextObject("{=lmmi_p_blocked}Already tried.");
                        return false;
                    },
                    () => _task!.Options[k]);
            }

            starter.AddDialogLine("lmmi_p_" + _key + "_reaction", react, start, "{=!}{LMMI_P_REACTION}",
                () =>
                {
                    if (_task == null) return false;
                    var last = ConversationManager.GetPersuasionChosenOptions().Last();
                    MBTextManager.SetTextVariable("LMMI_P_REACTION", PersuasionHelper.GetDefaultPersuasionOptionReaction(last.Item2));
                    if (last.Item2 == PersuasionOptionResult.CriticalFailure) _task.BlockAllOptions();
                    LmmiLog.Info($"Persuasion '{_key}': {last.Item1.SkillUsed?.Name}/{last.Item1.TraitUsed?.Name} -> {last.Item2}.");
                    if (LmmiSettingsProvider.TestMode)
                        TaleWorlds.Library.InformationManager.DisplayMessage(new TaleWorlds.Library.InformationMessage(
                            $"[LMMI Test] Persuasion: {last.Item1.SkillUsed?.Name}/{last.Item1.TraitUsed?.Name} ({last.Item1.ArgumentStrength}) -> {last.Item2}",
                            TaleWorlds.Library.Colors.Cyan));
                    return true;
                },
                () =>
                {
                    if (_task == null) return;
                    var last = ConversationManager.GetPersuasionChosenOptions().Last();
                    float difficulty = Campaign.Current.Models.PersuasionModel.GetDifficulty(_difficulty);
                    Campaign.Current.Models.PersuasionModel.GetEffectChances(last.Item1, out var moveOn, out var block, difficulty);
                    _task.ApplyEffects(moveOn, block);
                });
        }

        /// <summary>A way out mid-argument (vanilla's persuasion has none): ends it, counts as neither won nor lost.</summary>
        public void AddLeave(CampaignGameStarter starter, string text, string toState, Action? onLeave = null)
        {
            starter.AddPlayerLine("lmmi_p_" + _key + "_leave", "lmmi_p_" + _key + "_choose", toState, text, () => Active,
                () =>
                {
                    ConversationManager.EndPersuasion();
                    _task = null;
                    LmmiLog.Info($"Persuasion '{_key}': you let it go.");
                    try { onLeave?.Invoke(); }
                    catch (Exception ex) { LmmiLog.Error($"Persuasion '{_key}': leave callback threw", ex); }
                }, 10);
        }

        private static bool Say(TextObject line)
        {
            MBTextManager.SetTextVariable("LMMI_P_LINE", line);
            return true;
        }

        private void Finish(bool won)
        {
            ConversationManager.EndPersuasion();
            _task = null;
            LmmiLog.Info($"Persuasion '{_key}': {(won ? "won" : "lost")}.");
            try { (won ? _onWon : _onLost)?.Invoke(); }
            catch (Exception ex) { LmmiLog.Error($"Persuasion '{_key}': callback threw", ex); }
        }

        /// <summary>
        /// An argument built on a skill and one of your traits. Its strength starts from the listener's own traits
        /// (an honorable listener hears an appeal to honor), then gets harder with prejudice —
        /// a listener who resents your people, a cruel one — and easier with good standing in their town.
        /// </summary>
        public static PersuasionOptionArgs Argument(SkillObject skill, TraitObject trait, TextObject line, CharacterObject? listener,
            float resentment = 0f, float standing = 0f, int extraShift = 0, bool critical = false)
        {
            // Normal, easier the more the listener values the same virtue (a listener who scorns it hears it worse).
            // (Vanilla's GetArgumentStrengthBasedOnTargetTraits starts from +1, which makes any single-trait argument
            // "very easy" — too generous here.)
            var strength = PersuasionArgumentStrength.Normal;
            int shift = extraShift + (listener != null ? Math.Max(-2, Math.Min(2, listener.GetTraitLevel(trait))) : 0);
            if (resentment >= 1.25f) shift -= 2;
            else if (resentment > 0f) shift -= 1;
            if (listener != null && listener.GetTraitLevel(DefaultTraits.Mercy) <= -1) shift -= 1;
            if (standing >= 10f) shift += 1;
            else if (standing <= -10f) shift -= 1;
            int value = Math.Max((int)PersuasionArgumentStrength.ExtremelyHard, Math.Min((int)PersuasionArgumentStrength.ExtremelyEasy, (int)strength + shift));
            return new PersuasionOptionArgs(skill, trait, TraitEffect.Positive, (PersuasionArgumentStrength)value, critical, line);
        }
    }
}
