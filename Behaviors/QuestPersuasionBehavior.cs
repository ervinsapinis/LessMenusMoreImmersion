using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation.Persuasion;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// At Contempt a notable won't simply hand you their problem — but you can talk them into it, with vanilla's own
    /// persuasion (progress bar, chances, critical success and failure). Vanilla's "I heard you may need some help" is
    /// replaced: they say why they don't trust you, you argue in character — Honor, Calculating, Valor, Generosity —
    /// needing two successes. Each argument lands better with a notable who shares the virtue, worse the more they
    /// resent your people or the crueller they are; your standing in their town helps. Win, and vanilla's quest offer
    /// continues; lose, and they won't hear it again for a few days.
    /// </summary>
    public class QuestPersuasionBehavior : CampaignBehaviorBase
    {
        private const float PersuadedDays = 7f;
        private const float RetryDays = 3f;

        // notable StringId -> campaign hour until which they'll offer work anyway (persuaded)
        [NonSerialized] private Dictionary<string, double> _persuadedUntil = new Dictionary<string, double>();
        // notable StringId -> campaign hour after which you may try again (failed)
        [NonSerialized] private Dictionary<string, double> _retryAfter = new Dictionary<string, double>();
        [NonSerialized] private readonly NativePersuasion _persuasion = new NativePersuasion("quest");

        private static QuestPersuasionBehavior? Instance => Campaign.Current?.GetCampaignBehavior<QuestPersuasionBehavior>();

        /// <summary>A contemptuous notable who has been talked into offering work.</summary>
        public static bool IsPersuaded(Hero notable) =>
            Instance != null && Instance._persuadedUntil.TryGetValue(notable.StringId, out var until) && until > CampaignTime.Now.ToHours;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, AddDialogs);
        }

        /// <summary>A notable at Contempt with an issue on offer, not yet persuaded.</summary>
        private static bool NeedsPersuading(out Hero notable)
        {
            var hero = Hero.OneToOneConversationHero;
            notable = hero!;
            if (hero == null || !NotableVoice.Applies(hero)) return false;
            var issue = hero.Issue;
            if (issue == null || !issue.IsOngoingWithoutQuest || hero.IsPrisoner) return false;
            if (Campaign.Current.QuestManager.Quests.Any(q => q.QuestGiver == hero)) return false;
            return NotableVoice.ForConversation(hero).Level == Disposition.Contempt && !IsPersuaded(hero);
        }

        private void AddDialogs(CampaignGameStarter starter)
        {
            try
            {
                starter.AddPlayerLine("lmmi_issue_ask", "hero_main_options", "lmmi_issue_distrust",
                    "{=lmmi_issue_ask}I heard you may need some help with a problem?",
                    () => NeedsPersuading(out _),
                    null, 110,
                    (out TextObject why) =>
                    {
                        why = TextObject.GetEmpty();
                        var n = Hero.OneToOneConversationHero;
                        if (n == null || LmmiSettingsProvider.TestMode) return true;
                        if (_retryAfter.TryGetValue(n.StringId, out var after) && after > CampaignTime.Now.ToHours)
                        {
                            why = new TextObject("{=lmmi_issue_retry}{NAME} has made it clear what they think of you. Give it a few days.");
                            why.SetTextVariable("NAME", n.Name);
                            return false;
                        }
                        return true;
                    });

                starter.AddDialogLine("lmmi_issue_distrust", "lmmi_issue_distrust", _persuasion.Entry,
                    "{=lmmi_issue_distrust}{LMMI_DISTRUST}",
                    () =>
                    {
                        var n = Hero.OneToOneConversationHero;
                        if (n == null) return false;
                        TextObject line;
                        if (NotableDisposition.IsForeigner(n))
                        {
                            line = Flavor.Pick("{=lmmi_issue_distrust_foreign}Help? From a {DEMONYM}? I don't trust your kind with my affairs. Why should I trust you?",
                                "{=lmmi_issue_distrust_foreign_2}You, a {DEMONYM}, help me? I'd sooner trust a wolf with my sheep.",
                                "{=lmmi_issue_distrust_foreign_3}My troubles are my own. I don't hand them to {DEMONYM} strangers.");
                            line.SetTextVariable("DEMONYM", CultureWords.Demonym(Hero.MainHero.Culture));
                        }
                        else
                            line = Flavor.Pick("{=lmmi_issue_distrust_nobody}Help? From some nobody off the road? I don't hand my troubles to strangers. Why should I trust you?",
                                "{=lmmi_issue_distrust_nobody_2}Help? From a nobody? Come back when people know your name.",
                                "{=lmmi_issue_distrust_nobody_3}I don't trust my problems to strangers off the road.");
                        MBTextManager.SetTextVariable("LMMI_DISTRUST", line);
                        return true;
                    },
                    StartPersuasion);

                _persuasion.Register(starter, wonState: IssueManagerOfferToken(), lostState: "hero_main_options");
                _persuasion.AddLeave(starter, "{=lmmi_plea_never}Forget it.", "lmmi_favor_nevermind_resp");
            }
            catch (Exception ex) { LmmiLog.Error("QuestPersuasionBehavior: failed to register dialogs", ex); }
        }

        private static string IssueManagerOfferToken() => "issue_offer";

        private void StartPersuasion()
        {
            var n = Hero.OneToOneConversationHero;
            if (n == null) return;
            var e = NotableDisposition.Evaluate(n);
            var listener = n.CharacterObject;
            float resentment = NotableDisposition.IsForeigner(n)
                ? Math.Max(0f, CultureRelations.Multiplier(n.Culture?.StringId, Hero.MainHero.Culture?.StringId)
                               * LmmiSettingsProvider.ForeignerPrejudicePercent / 100f)
                : 0f;
            float standing = TownStandingBehavior.Get(n.CurrentSettlement);

            var arguments = new[]
            {
                NativePersuasion.Argument(DefaultSkills.Charm, DefaultTraits.Honor,
                    new TextObject("{=lmmi_plea_honor}You have my word. I don't abandon a job, whatever you think of my people."), listener, resentment, standing),
                NativePersuasion.Argument(DefaultSkills.Charm, DefaultTraits.Calculating,
                    new TextObject("{=lmmi_plea_calculating}Your problem won't solve itself, and I don't see anyone else lining up. Do you have a lot of choice?"), listener, resentment, standing),
                NativePersuasion.Argument(DefaultSkills.Leadership, DefaultTraits.Valor,
                    new TextObject("{=lmmi_plea_valor}Whatever it is, I've faced worse. Let me prove it."), listener, resentment, standing),
                NativePersuasion.Argument(DefaultSkills.Charm, DefaultTraits.Generosity,
                    new TextObject("{=lmmi_plea_generosity}I'd do it for less than anyone would ask. Call it an introduction."), listener, resentment, standing),
            };
            // The deeper the contempt, the harder the listener.
            var difficulty = e.Score <= -55f ? PersuasionDifficulty.Hard : e.Score <= -35f ? PersuasionDifficulty.MediumHard : PersuasionDifficulty.Medium;

            var id = n.StringId;
            _persuasion.Start(arguments,
                Flavor.Pick("{=lmmi_plea_opening}Go on, then. Convince me.",
                    "{=lmmi_plea_opening_2}Convince me, then.",
                    "{=lmmi_plea_opening_3}Talk. Make it good."),
                Flavor.Pick("{=lmmi_plea_again}...And?",
                    "{=lmmi_plea_again_2}...Go on.",
                    "{=lmmi_plea_again_3}...Is that all?"),
                Flavor.Pick("{=lmmi_plea_won}...Fine. Maybe you're not like the rest of them. Here's what I need.",
                    "{=lmmi_plea_won_2}...Alright. You've convinced me. Here's the trouble.",
                    "{=lmmi_plea_won_3}...Fine. Maybe I misjudged you. Listen, then."),
                Flavor.Pick("{=lmmi_plea_lost}Words. Everyone has words. Get out of my sight.",
                    "{=lmmi_plea_lost_2}Pretty words. I've heard them before. Leave.",
                    "{=lmmi_plea_lost_3}No. I don't believe a word of it. Go."),
                onWon: () => _persuadedUntil[id] = CampaignTime.Now.ToHours + PersuadedDays * CampaignTime.HoursInDay,
                onLost: () => _retryAfter[id] = CampaignTime.Now.ToHours + RetryDays * CampaignTime.HoursInDay,
                goal: 2f, difficulty: difficulty);
            LmmiLog.Info($"Quest persuasion: {n.Name} ({e.Level} {e.Score:0.#}, resentment {resentment:0.##}, standing {standing:0.#}) — {difficulty}.");
        }

        public override void SyncData(IDataStore dataStore)
        {
            string data = string.Empty;
            if (!dataStore.IsLoading)
            {
                double now = CampaignTime.Now.ToHours;
                var parts = _persuadedUntil.Where(kv => kv.Value > now).Select(kv => "P;" + kv.Key + ";" + kv.Value.ToString("R", CultureInfo.InvariantCulture)).ToList();
                parts.AddRange(_retryAfter.Where(kv => kv.Value > now).Select(kv => "R;" + kv.Key + ";" + kv.Value.ToString("R", CultureInfo.InvariantCulture)));
                data = string.Join("|", parts);
            }
            dataStore.SyncData("lmmi_persuasion_v1", ref data);
            if (!dataStore.IsLoading) return;

            _persuadedUntil = new Dictionary<string, double>();
            _retryAfter = new Dictionary<string, double>();
            foreach (var entry in (data ?? string.Empty).Split('|'))
            {
                var p = entry.Split(';');
                if (p.Length != 3 || !double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var at)) continue;
                if (p[0] == "P") _persuadedUntil[p[1]] = at;
                else if (p[0] == "R") _retryAfter[p[1]] = at;
            }
        }

        /// <summary>
        /// Hides vanilla's "I heard you may need some help" while a contemptuous notable hasn't been persuaded —
        /// our own line (above) takes its place and leads into the exchange.
        /// </summary>
        [HarmonyPatch(typeof(LordConversationsCampaignBehavior), "conversation_hero_main_options_have_issue_on_condition")]
        private static class HideVanillaAsk
        {
            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(ref bool __result)
            {
                try
                {
                    if (__result && NeedsPersuading(out _)) __result = false;
                }
                catch (Exception ex) { LmmiLog.Error("QuestPersuasionBehavior.HideVanillaAsk threw", ex); }
            }
        }
    }
}
