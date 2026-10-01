using LessMenusMoreImmersion.Behaviors;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Contacts
{
    /// <summary>
    /// What a notable says to you, given how they regard you. One evaluation per conversation, so the greeting,
    /// the "what is it?" prompt and any refusal agree — and someone who greeted you with an insult doesn't
    /// repeat it word for word when you ask for a favor.
    /// Friendly notables who genuinely like you keep vanilla's lines.
    /// </summary>
    internal static class NotableVoice
    {
        private static Hero? _hero;
        private static DispositionBreakdown? _evaluation;
        private static TextObject? _greeting;
        private static bool _insulted;
        private static readonly System.Collections.Generic.HashSet<string> _logged = new System.Collections.Generic.HashSet<string>();

        /// <summary>A notable of the settlement you're in, with the disposition system enabled.</summary>
        public static bool Applies(Hero? hero) =>
            LmmiSettingsProvider.EnableNotableDisposition
            && hero != null && hero.IsNotable
            && hero.CurrentSettlement != null && hero.CurrentSettlement == Settlement.CurrentSettlement;

        public static DispositionBreakdown ForConversation(Hero hero)
        {
            if (_hero != hero || _evaluation == null)
            {
                _hero = hero;
                _evaluation = NotableDisposition.Evaluate(hero);
                _greeting = null;
                _insulted = false;
            }
            return _evaluation;
        }

        /// <summary>Dialog conditions re-run whenever options rebuild; log each fact once per conversation.</summary>
        public static void LogOnce(string key, string message)
        {
            if (_logged.Add(key)) LmmiLog.Info(message);
        }

        public static void EndConversation()
        {
            _logged.Clear();
            _hero = null;
            _evaluation = null;
            _greeting = null;
            _insulted = false;
        }

        /// <summary>Replaces vanilla's opening line, or null to keep it.</summary>
        public static TextObject? Greeting(Hero hero, bool firstMeeting)
        {
            var e = ForConversation(hero);
            if (_greeting != null) return _greeting;   // conditions can run more than once; keep the first pick

            // You stepped up for a local recently: the first time each notable brings it up.
            if ((Tone(e) == VoiceTone.Contempt || Tone(e) == VoiceTone.Wary || Tone(e) == VoiceTone.Vanilla)
                && TownStandingBehavior.RemarkOnce(hero))
            {
                bool foreign = NotableDisposition.IsForeigner(hero);
                _greeting = VoiceLines.Pick(foreign ? "greet_stepped_up_foreign" : "greet_stepped_up_kin", hero);
                _greeting.SetTextVariable("LMMI_SLUR", CultureWords.Slur(Hero.MainHero.Culture));
                LogOnce("greeting", $"Voice: {hero.Name} remarks on your good deed in town: \"{_greeting}\"");
                return _greeting;
            }

            // Word gets around: someone who hasn't dealt with you, in a town where you've done good.
            var helped = hero.CurrentSettlement != null ? TownStandingBehavior.LastHelped(hero.CurrentSettlement) : null;
            var tone = Tone(e);
            if ((tone == VoiceTone.Wary || tone == VoiceTone.Vanilla) && helped != null && helped != hero
                && e.TownStanding >= 8f && e.Relation < 5f && MBRandom.RandomFloat < 0.5f)
            {
                _greeting = VoiceLines.Pick("greet_heard_deeds", hero);
                _greeting.SetTextVariable("LMMI_HELPED", helped.Name);
                LogOnce("greeting", $"Voice: {hero.Name} heard of your deeds for {helped.Name} (standing {e.TownStanding:0.#}): \"{_greeting}\"");
                return _greeting;
            }

            switch (tone)
            {
                case VoiceTone.Contempt:
                    _insulted = true;
                    _greeting = new TextObject("{=!}" + ContemptLine(hero, e, asGreeting: true));
                    break;
                case VoiceTone.Wary:
                    _greeting = !firstMeeting ? VoiceLines.Pick("greet_wary", hero)
                        : VoiceLines.Pick(NotableDisposition.IsForeigner(hero) ? "greet_wary_first_foreign" : "greet_wary_first", hero);
                    break;
                case VoiceTone.Sycophant:
                    _greeting = VoiceLines.Pick(firstMeeting ? "greet_syco_first" : "greet_syco", hero);
                    break;
                case VoiceTone.Friend:
                    _greeting = VoiceLines.Pick("greet_friend", hero);
                    break;
            }
            LogOnce("greeting", $"Voice: {hero.Name} greeting ({Tone(e)}, {e.Level}, first meeting: {firstMeeting}): " +
                         (_greeting != null ? $"\"{_greeting}\"" : "vanilla"));
            return _greeting;
        }

        /// <summary>Replaces vanilla's "you again within a day" line, or null to keep it.</summary>
        public static TextObject? ShortAbsenceGreeting(Hero hero)
        {
            var tone = Tone(ForConversation(hero));
            LogOnce("short", $"Voice: {hero.Name} short-absence greeting ({tone}){(tone == VoiceTone.Vanilla ? ": vanilla" : "")}");
            switch (tone)
            {
                case VoiceTone.Contempt: _insulted = true; return VoiceLines.Pick("short_contempt", hero);
                case VoiceTone.Wary: return VoiceLines.Pick("short_wary", hero);
                case VoiceTone.Sycophant: return VoiceLines.Pick("short_syco", hero);
                case VoiceTone.Friend: return VoiceLines.Pick("short_friend", hero);
                default: return null;
            }
        }

        /// <summary>
        /// The line that hands over to your options ("So, then. What is it?"), or null for vanilla's.
        /// Only the contemptuous and the fawning say it differently.
        /// </summary>
        public static TextObject? Prompt(Hero hero)
        {
            switch (Tone(ForConversation(hero)))
            {
                case VoiceTone.Contempt: return VoiceLines.Pick("prompt_contempt", hero);
                case VoiceTone.Sycophant: return VoiceLines.Pick("prompt_syco", hero);
                default: return null;
            }
        }

        /// <summary>
        /// A contemptuous notable's answer to "And who are you?" at a first meeting, instead of vanilla's
        /// polite self-introduction; null keeps vanilla's.
        /// </summary>
        public static TextObject? Introduction(Hero hero)
        {
            if (Tone(ForConversation(hero)) != VoiceTone.Contempt) return null;
            string key = hero.IsGangLeader ? "intro_contempt_gang"
                : hero.IsMerchant ? "intro_contempt_merchant"
                : hero.IsArtisan ? "intro_contempt_artisan"
                : hero.IsHeadman || hero.IsRuralNotable ? "intro_contempt_village"
                : "intro_contempt";
            // Half the time the type-specific line, half a generic one.
            var line = MBRandom.RandomFloat < 0.5f ? VoiceLines.Pick(key, hero) : VoiceLines.Pick("intro_contempt", hero);
            line.SetTextVariable("LMMI_INTRO_NAME", hero.FirstName ?? hero.Name);
            LogOnce("intro", $"Voice: {hero.Name} introduces themselves with contempt: \"{line}\"");
            return line;
        }

        /// <summary>Replaces vanilla's farewell ("Good to meet you", "Walk the path of the righteous") when it wouldn't be meant.</summary>
        public static TextObject? Farewell(Hero hero)
        {
            switch (Tone(ForConversation(hero)))
            {
                case VoiceTone.Contempt: return VoiceLines.Pick("farewell_contempt", hero);
                case VoiceTone.Wary: return VoiceLines.Pick("farewell_wary", hero);
                case VoiceTone.Sycophant: return VoiceLines.Pick("farewell_syco", hero);
                default: return null;
            }
        }

        /// <summary>Vanilla follows the greeting with "I know your name" or a comment on your deeds. Not from these people.</summary>
        public static bool SuppressesComments(Hero hero)
        {
            var tone = Tone(ForConversation(hero));
            bool suppress = tone == VoiceTone.Contempt || tone == VoiceTone.Wary || tone == VoiceTone.Sycophant;
            if (suppress) LogOnce("comment", $"Voice: {hero.Name} ({tone}) skips vanilla's \"I know your name\" / deed comment.");
            return suppress;
        }

        /// <summary>Refusing a favor at Contempt.</summary>
        public static string Refusal(Hero hero)
        {
            var e = ForConversation(hero);
            if (_insulted)
                return Pick("{=lmmi_refuse_again_1}You have some nerve asking me for anything.",
                            "{=lmmi_refuse_again_2}I've told you what I think of you. No.",
                            "{=lmmi_refuse_again_3}Favors? From me? Ha.",
                            "{=lmmi_refuse_again_4}Ask again and see what happens.",
                            "{=lmmi_refuse_again_5}The answer hasn't changed. It won't.",
                            "{=lmmi_refuse_again_6}Are you deaf? No.",
                            "{=lmmi_refuse_again_7}I'd sooner help a rat.",
                            "{=lmmi_refuse_again_8}Get out of my sight.",
                            "{=lmmi_refuse_again_9}Still here? Still no.").ToString();
            _insulted = true;
            return ContemptLine(hero, e, asGreeting: false);
        }

        /// <summary>Refusing directions at Contempt.</summary>
        public static string Dismissal(Hero hero)
        {
            ForConversation(hero);
            if (_insulted)
                return Pick("{=lmmi_dismiss_1}Find your own way.",
                            "{=lmmi_dismiss_2}Ask someone who cares.",
                            "{=lmmi_dismiss_3}Find someone who gives a damn.",
                            "{=lmmi_dismiss_4}Not my problem.",
                            "{=lmmi_dismiss_5}Go and ask the watch. They're paid to be nice.",
                            "{=lmmi_dismiss_6}Wander about. You'll trip over it eventually.").ToString();
            _insulted = true;
            return ContemptLine(hero, _evaluation!, asGreeting: true);
        }

        private static string ContemptLine(Hero hero, DispositionBreakdown e, bool asGreeting)
        {
            if (NotableDisposition.IsForeigner(hero) && e.Prejudice > 0f)
                return DisableMenuBehavior.GetForeignerInsultText();

            if (asGreeting)
                return e.Relation <= -10f
                    ? Pick("{=lmmi_greet_grudge_1}You. I remember what you did.",
                           "{=lmmi_greet_grudge_2}You've got some nerve showing your face here.",
                           "{=lmmi_greet_grudge_3}Oh, it's you. I haven't forgotten.",
                           "{=lmmi_greet_grudge_4}You've got a nerve, coming to me after last time.",
                           "{=lmmi_greet_grudge_5}I'd hoped never to see you again.",
                           "{=lmmi_greet_grudge_6}You. Say your piece and get out.").ToString()
                    : Pick("{=lmmi_greet_nobody_1}Who let you in here?",
                           "{=lmmi_greet_nobody_2}Whatever you're selling, I'm not buying.",
                           "{=lmmi_greet_nobody_3}I don't have time for vagrants.",
                           "{=lmmi_greet_nobody_4}Who are you supposed to be?",
                           "{=lmmi_greet_nobody_5}I don't talk to every vagrant who wanders in.",
                           "{=lmmi_greet_nobody_6}If you're begging, try the market square.",
                           "{=lmmi_greet_nobody_7}Nobody asked you in.",
                           "{=lmmi_greet_nobody_8}I'm busy. Whatever it is, no.",
                           "{=lmmi_greet_nobody_9}Do I look like I've time for nobodies?").ToString();

            if (e.Relation <= -10f)
                return Pick("{=lmmi_favor_grudge_1}After what you've done? You've got some nerve.",
                            "{=lmmi_favor_grudge_2}I remember you. The answer is no.",
                            "{=lmmi_favor_grudge_3}Favors are for friends. You are not one.",
                            "{=lmmi_favor_grudge_4}Help you? I'd rather help your enemies.",
                            "{=lmmi_favor_grudge_5}I've a long memory, and you're in it. No.",
                            "{=lmmi_favor_grudge_6}Not after what you did. Never.",
                            "{=lmmi_favor_grudge_7}You burned that bridge yourself.",
                            "{=lmmi_favor_grudge_8}Ask someone who doesn't know you.",
                            "{=lmmi_favor_grudge_9}The only favor I'll do you is not calling the guards.").ToString();

            return Pick("{=lmmi_favor_nobody_1}Favors? I don't know you, and I don't do favors for nobodies.",
                        "{=lmmi_favor_nobody_2}Come back when your name means something around here.",
                        "{=lmmi_favor_nobody_3}You've given me no reason to help you. Yet.",
                            "{=lmmi_favor_nobody_4}Who are you to ask me for anything?",
                            "{=lmmi_favor_nobody_5}Make a name for yourself first. Then we'll talk.",
                            "{=lmmi_favor_nobody_6}I don't do favors for strangers nobody's heard of.",
                            "{=lmmi_favor_nobody_7}Do something for this town, and maybe it'll do something for you.",
                            "{=lmmi_favor_nobody_8}Favors are earned. You've earned nothing.",
                            "{=lmmi_favor_nobody_9}Nobody owes you anything here. Least of all me.").ToString();
        }

        private enum VoiceTone { Vanilla, Contempt, Wary, Sycophant, Friend }

        private static VoiceTone Tone(DispositionBreakdown e)
        {
            if (e.Level == Disposition.Contempt) return VoiceTone.Contempt;
            if (e.Level == Disposition.Wary) return VoiceTone.Wary;
            if (e.IsSycophant) return VoiceTone.Sycophant;
            if (e.Level == Disposition.Trusted) return VoiceTone.Friend;
            return VoiceTone.Vanilla;   // genuinely Friendly: vanilla's lines fit
        }

        private static TextObject Pick(params string[] lines) =>
            new TextObject(lines[MBRandom.RandomInt(lines.Length)]);
    }
}
