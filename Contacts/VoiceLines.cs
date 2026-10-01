using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Contacts
{
    /// <summary>
    /// The notables' line bank. Each situation has generic lines, and most have variants for vanilla's four
    /// speaking personas (curt, earnest, ironic, soft-spoken — the same persona vanilla uses to pick a lord's
    /// voiced lines). A pick draws from the persona's lines and the generic ones together.
    /// </summary>
    internal static class VoiceLines
    {
        private static readonly Dictionary<string, string[]> Lines = new Dictionary<string, string[]>
        {
            // ---- First meeting, Wary ----
            ["greet_wary_first"] = new[]
            {
                "{=lmmi_greet_wary_first}I don't know you. State your business.",
                "{=lmmi_greet_wary_first_2}Hm. I don't believe we've dealt before.",
            },
            ["greet_wary_first_curt"] = new[] { "{=lmmi_gwf_curt_1}Who are you? Speak.", "{=lmmi_gwf_curt_2}Yes? What?" },
            ["greet_wary_first_earnest"] = new[] { "{=lmmi_gwf_earnest_1}I don't think we've met. What brings you to me?" },
            ["greet_wary_first_ironic"] = new[] { "{=lmmi_gwf_ironic_1}A new face. How exciting. What do you want?", "{=lmmi_gwf_ironic_2}Well, well. Somebody's lost." },
            ["greet_wary_first_soft"] = new[] { "{=lmmi_gwf_soft_1}Forgive me, I don't believe I know you. What is it you need?" },

            ["greet_wary_first_foreign"] = new[]
            {
                "{=lmmi_greet_wary_first_foreign}You're not from around here, are you? What do you want?",
                "{=lmmi_greet_wary_first_foreign_2}Another stranger. State your business.",
            },
            ["greet_wary_first_foreign_curt"] = new[] { "{=lmmi_gwff_curt_1}Foreigner. What do you want?" },
            ["greet_wary_first_foreign_earnest"] = new[] { "{=lmmi_gwff_earnest_1}You're a long way from home, stranger. What is it?" },
            ["greet_wary_first_foreign_ironic"] = new[] { "{=lmmi_gwff_ironic_1}Lost your way, stranger? Or just lost?" },
            ["greet_wary_first_foreign_soft"] = new[] { "{=lmmi_gwff_soft_1}You're not from here, I think. How can I... help?" },

            // ---- Later meetings, Wary ----
            ["greet_wary"] = new[] { "{=lmmi_greet_wary}Yes?", "{=lmmi_greet_wary_2}What brings you back?" },
            ["greet_wary_curt"] = new[] { "{=lmmi_gw_curt_1}Speak." },
            ["greet_wary_earnest"] = new[] { "{=lmmi_gw_earnest_1}Back again? What is it this time?" },
            ["greet_wary_ironic"] = new[] { "{=lmmi_gw_ironic_1}Back for more, are we?" },
            ["greet_wary_soft"] = new[] { "{=lmmi_gw_soft_1}Oh. It's you. What do you need?" },

            // ---- Sycophants ----
            ["greet_syco_first"] = new[]
            {
                "{=lmmi_greet_syco_first}The famous {PLAYER.NAME}, in my humble town! What an honor, {?PLAYER.GENDER}my lady{?}my lord{\\?}.",
                "{=lmmi_greet_syco_first_2}{?PLAYER.GENDER}My lady{?}My lord{\\?}! Word of your deeds reached us long before you did.",
            },
            ["greet_syco_first_curt"] = new[] { "{=lmmi_gsf_curt_1}{?PLAYER.GENDER}My lady{?}My lord{\\?}. An honor. A true honor." },
            ["greet_syco_first_earnest"] = new[] { "{=lmmi_gsf_earnest_1}I have heard so much about you, {?PLAYER.GENDER}my lady{?}my lord{\\?}! Welcome, welcome!" },
            ["greet_syco_first_ironic"] = new[] { "{=lmmi_gsf_ironic_1}Why, the great {PLAYER.NAME} {?PLAYER.GENDER}herself{?}himself{\\?}! We are truly blessed." },
            ["greet_syco_first_soft"] = new[] { "{=lmmi_gsf_soft_1}It is... a great honor, {?PLAYER.GENDER}my lady{?}my lord{\\?}. Truly." },

            ["greet_syco"] = new[]
            {
                "{=lmmi_greet_syco}{?PLAYER.GENDER}My lady{?}My lord{\\?}! You honor me again.",
                "{=lmmi_greet_syco_2}Ah, {?PLAYER.GENDER}my lady{?}my lord{\\?}! Always a pleasure. Always.",
            },
            ["greet_syco_curt"] = new[] { "{=lmmi_gs_curt_1}{?PLAYER.GENDER}My lady{?}My lord{\\?}. At your service." },
            ["greet_syco_earnest"] = new[] { "{=lmmi_gs_earnest_1}{?PLAYER.GENDER}My lady{?}My lord{\\?}, you've come back! I was just telling everyone about you." },
            ["greet_syco_ironic"] = new[] { "{=lmmi_gs_ironic_1}The day brightens! Our {?PLAYER.GENDER}great lady{?}great lord{\\?} returns." },
            ["greet_syco_soft"] = new[] { "{=lmmi_gs_soft_1}Welcome back, {?PLAYER.GENDER}my lady{?}my lord{\\?}. Whatever you need..." },

            // ---- Genuine, trusted friends ----
            ["greet_friend"] = new[]
            {
                "{=lmmi_greet_friend}{PLAYER.NAME}, my friend! Good to see you.",
                "{=lmmi_greet_friend_2}Ah, there you are. Come, sit.",
            },
            ["greet_friend_curt"] = new[] { "{=lmmi_gf_curt_1}{PLAYER.NAME}. Good. Sit." },
            ["greet_friend_earnest"] = new[] { "{=lmmi_gf_earnest_1}{PLAYER.NAME}! It does my heart good to see you." },
            ["greet_friend_ironic"] = new[] { "{=lmmi_gf_ironic_1}Look what the wind blew in. Sit, sit, before someone important sees you." },
            ["greet_friend_soft"] = new[] { "{=lmmi_gf_soft_1}{PLAYER.NAME}... I'm glad you came." },

            // ---- Again within a day ----
            ["short_contempt"] = new[] { "{=lmmi_short_contempt}You again? What now?" },
            ["short_contempt_curt"] = new[] { "{=lmmi_sc_curt_1}What now." },
            ["short_contempt_ironic"] = new[] { "{=lmmi_sc_ironic_1}Oh, good. You're back." },
            ["short_contempt_earnest"] = new[] { "{=lmmi_sc_earnest_1}Did I not make myself clear the first time?" },
            ["short_wary"] = new[] { "{=lmmi_short_wary}Back already?" },
            ["short_wary_ironic"] = new[] { "{=lmmi_sw_ironic_1}Missed me already?" },
            ["short_syco"] = new[] { "{=lmmi_short_syco}Back so soon, {?PLAYER.GENDER}my lady{?}my lord{\\?}? Always a pleasure!" },
            ["short_friend"] = new[] { "{=lmmi_short_friend}Back already? Good." },
            ["short_friend_earnest"] = new[] { "{=lmmi_sf_earnest_1}Back so soon? Come in, come in." },

            // ---- Handing over to your options ----
            ["prompt_contempt"] = new[] { "{=lmmi_prompt_contempt}Well? Make it quick.", "{=lmmi_prompt_contempt_2}Get on with it." },
            ["prompt_contempt_ironic"] = new[] { "{=lmmi_pc_ironic_1}I'm listening. Barely." },
            ["prompt_contempt_curt"] = new[] { "{=lmmi_pc_curt_1}Out with it." },
            ["prompt_syco"] = new[] { "{=lmmi_prompt_syco}How may I serve you?" },
            ["prompt_syco_earnest"] = new[] { "{=lmmi_ps_earnest_1}Anything you need, you have only to ask." },

            // ---- First-meeting introductions from the contemptuous ----
            ["intro_contempt"] = new[]
            {
                "{=lmmi_intro_contempt_1}{LMMI_INTRO_NAME}. Not that it's any business of yours.",
                "{=lmmi_intro_contempt_2}The name's {LMMI_INTRO_NAME}. Remember it, so you'll know whom to avoid.",
            },
            ["intro_contempt_gang"] = new[] { "{=lmmi_intro_contempt_gang}{LMMI_INTRO_NAME}. People who cross me tend to end up in the river. Remember that." },
            ["intro_contempt_merchant"] = new[] { "{=lmmi_intro_contempt_merchant}I'm {LMMI_INTRO_NAME}. And my goods are not for the likes of you." },
            ["intro_contempt_village"] = new[] { "{=lmmi_intro_contempt_village}{LMMI_INTRO_NAME}. This village doesn't want trouble, and you look like trouble." },
            ["intro_contempt_artisan"] = new[] { "{=lmmi_intro_contempt_artisan}{LMMI_INTRO_NAME}. My workshop, my rules. You'll find no welcome in it." },

            // ---- Word gets around: you helped someone else in town ----
            ["greet_heard_deeds"] = new[]
            {
                "{=lmmi_heard_1}Word is you helped {LMMI_HELPED}. That counts for something around here.",
                "{=lmmi_heard_2}You're the one who sorted out {LMMI_HELPED}'s troubles, aren't you? Everyone's talking.",
            },
            ["greet_heard_deeds_curt"] = new[] { "{=lmmi_heard_curt}You helped {LMMI_HELPED}. I heard. Speak." },
            ["greet_heard_deeds_ironic"] = new[] { "{=lmmi_heard_ironic}Ah, {LMMI_HELPED}'s savior. The whole street won't shut up about you." },
            ["greet_heard_deeds_earnest"] = new[] { "{=lmmi_heard_earnest}{LMMI_HELPED} speaks well of you. That's rare. What can I do for you?" },
            ["greet_heard_deeds_soft"] = new[] { "{=lmmi_heard_soft}I heard what you did for {LMMI_HELPED}. It was... kind." },

            // ---- You stood up for a local: prejudice meets a fact ----
            ["greet_stepped_up_foreign"] = new[]
            {
                "{=lmmi_stepup_1}Huh. Didn't expect this from a {LMMI_SLUR}. Word is you stood up for one of ours.",
                "{=lmmi_stepup_2}So you're the {LMMI_SLUR} everyone's talking about. Stepped in when our own wouldn't, they say.",
            },
            ["greet_stepped_up_foreign_ironic"] = new[] { "{=lmmi_stepup_ironic}A {LMMI_SLUR} playing the hero. Now I've seen everything. ...It was well done, I'll grant you." },
            ["greet_stepped_up_foreign_curt"] = new[] { "{=lmmi_stepup_curt}You. The {LMMI_SLUR} who helped. Hm. Speak, then." },
            ["greet_stepped_up_kin"] = new[] { "{=lmmi_stepup_kin}Word is you stood up for one of ours. Good. That's how it should be." },

            // ---- Farewells (replace vanilla's pleasantries for those who don't mean them) ----
            ["farewell_contempt"] = new[]
            {
                "{=lmmi_bye_contempt_1}Good riddance.",
                "{=lmmi_bye_contempt_2}Don't come back.",
                "{=lmmi_bye_contempt_3}And stay gone.",
            },
            ["farewell_contempt_ironic"] = new[] { "{=lmmi_bye_contempt_ironic}Leaving so soon? What a pity." },
            ["farewell_contempt_curt"] = new[] { "{=lmmi_bye_contempt_curt}Go." },
            ["farewell_wary"] = new[] { "{=lmmi_bye_wary_1}Hm.", "{=lmmi_bye_wary_2}Go on, then.", "{=lmmi_bye_wary_3}Be on your way." },
            ["farewell_wary_earnest"] = new[] { "{=lmmi_bye_wary_earnest}Safe travels, I suppose." },
            ["farewell_syco"] = new[] { "{=lmmi_bye_syco_1}Always a pleasure, {?PLAYER.GENDER}my lady{?}my lord{\\?}! Do come again!", "{=lmmi_bye_syco_2}You honor us. Truly." },

            // ---- Favor openers ----
            ["open_wary_foreign"] = new[]
            {
                "{=lmmi_open_wary_foreign_1}Favors cost coin, stranger. More for your kind.",
                "{=lmmi_open_wary_foreign_2}For a foreigner? It'll cost you. Go on.",
            },
        };

        public static TextObject Pick(string key, Hero hero)
        {
            var pool = new List<string>();
            if (Lines.TryGetValue(key + "_" + PersonaKey(hero), out var personal)) pool.AddRange(personal);
            if (Lines.TryGetValue(key, out var generic)) pool.AddRange(generic);
            if (pool.Count == 0) return TextObject.GetEmpty();
            return new TextObject(pool[MBRandom.RandomInt(pool.Count)]);
        }

        private static string PersonaKey(Hero hero)
        {
            var persona = hero.CharacterObject?.GetPersona();
            if (persona == DefaultTraits.PersonaCurt) return "curt";
            if (persona == DefaultTraits.PersonaEarnest) return "earnest";
            if (persona == DefaultTraits.PersonaIronic) return "ironic";
            return "soft";
        }
    }
}
