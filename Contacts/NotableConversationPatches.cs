using System;
using System.Linq;
using HarmonyLib;
using LessMenusMoreImmersion.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Contacts
{
    /// <summary>
    /// Notables talk through vanilla's lord conversation flow. These patches only change the words
    /// (and, at Contempt, whether they'll give you work) — the flow and its side effects, such as
    /// marking the notable as met, stay vanilla.
    /// All run last: other dialogue mods (e.g. Dramalord rewrites the short-absence greeting with a
    /// "my lord" salutation) patch the same methods, and disposition has to have the final word.
    /// </summary>
    internal static class NotableConversationPatches
    {
        /// <summary>Vanilla picks the opening line ("Good to meet you", "I've heard of you") here.</summary>
        [HarmonyPatch(typeof(LordConversationsCampaignBehavior), "conversations_set_voiced_line")]
        private static class OpeningLine
        {
            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix()
            {
                try
                {
                    var hero = Hero.OneToOneConversationHero;
                    if (!NotableVoice.Applies(hero)) return;

                    var line = NotableVoice.Greeting(hero!, Campaign.Current.ConversationManager.CurrentConversationIsFirst);
                    if (line != null)
                        MBTextManager.SetTextVariable("VOICED_LINE", line);
                }
                catch (Exception ex)
                {
                    LmmiLog.Error("NotableConversationPatches.OpeningLine threw", ex);
                }
            }
        }

        /// <summary>Talking to the same notable again within a day.</summary>
        [HarmonyPatch(typeof(LordConversationsCampaignBehavior), "conversation_lord_greets_under_24_hours_on_condition")]
        private static class ShortAbsenceLine
        {
            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(bool __result)
            {
                try
                {
                    if (!__result) return;
                    var hero = Hero.OneToOneConversationHero;
                    if (!NotableVoice.Applies(hero)) return;

                    var line = NotableVoice.ShortAbsenceGreeting(hero!);
                    if (line != null)
                        MBTextManager.SetTextVariable("SHORT_ABSENCE_GREETING", line);
                }
                catch (Exception ex)
                {
                    LmmiLog.Error("NotableConversationPatches.ShortAbsenceLine threw", ex);
                }
            }
        }

        /// <summary>No "I know your name" or compliments on your deeds from people who scorn or merely tolerate you.</summary>
        [HarmonyPatch(typeof(LordConversationsCampaignBehavior), "conversation_lord_makes_comment_on_condition")]
        private static class DeedComment
        {
            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(ref bool __result)
            {
                try
                {
                    if (!__result) return;
                    var hero = Hero.OneToOneConversationHero;
                    if (NotableVoice.Applies(hero) && NotableVoice.SuppressesComments(hero!))
                        __result = false;
                }
                catch (Exception ex)
                {
                    LmmiLog.Error("NotableConversationPatches.DeedComment threw", ex);
                }
            }
        }

        // The Contempt quest gate moved to QuestPersuasionBehavior: a persuasion exchange instead of a greyed-out option.
    }
}
