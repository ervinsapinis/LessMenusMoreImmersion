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
                "{=lmmi_greet_wary_first_3}Do I know you? No. What do you want?",
                "{=lmmi_greet_wary_first_4}A new face. Speak your piece.",
                "{=lmmi_greet_wary_first_5}I don't deal with people I don't know. But go on.",
                "{=lmmi_greet_wary_first_6}Hm. You're not from my street. What is it?",
            },
            ["greet_wary_first_curt"] = new[] { "{=lmmi_gwf_curt_1}Who are you? Speak.", "{=lmmi_gwf_curt_2}Yes? What?", "{=lmmi_gwf_curt_3}Name and business. Quickly.", "{=lmmi_gwf_curt_4}What." },
            ["greet_wary_first_earnest"] = new[] { "{=lmmi_gwf_earnest_1}I don't think we've met. What brings you to me?", "{=lmmi_gwf_earnest_2}I don't believe we've been introduced. What can I do for you?", "{=lmmi_gwf_earnest_3}You'll forgive me — I don't know your face. What brings you?" },
            ["greet_wary_first_ironic"] = new[] { "{=lmmi_gwf_ironic_1}A new face. How exciting. What do you want?", "{=lmmi_gwf_ironic_2}Well, well. Somebody's lost.", "{=lmmi_gwf_ironic_3}Ah, a stranger with a purpose. How rare. Go on.", "{=lmmi_gwf_ironic_4}Let me guess: you want something." },
            ["greet_wary_first_soft"] = new[] { "{=lmmi_gwf_soft_1}Forgive me, I don't believe I know you. What is it you need?", "{=lmmi_gwf_soft_2}I'm sorry, have we met? I don't think we have.", "{=lmmi_gwf_soft_3}Oh — hello. I don't know you, I think. What is it?" },

            ["greet_wary_first_foreign"] = new[]
            {
                "{=lmmi_greet_wary_first_foreign}You're not from around here, are you? What do you want?",
                "{=lmmi_greet_wary_first_foreign_2}Another stranger. State your business.",
                "{=lmmi_greet_wary_first_foreign_3}That accent's not from here. What do you want?",
                "{=lmmi_greet_wary_first_foreign_4}A foreigner, in my house. Well? Speak.",
                "{=lmmi_greet_wary_first_foreign_5}You've come a long way to bother me. What is it?",
                "{=lmmi_greet_wary_first_foreign_6}Not one of ours, are you? Make it quick.",
            },
            ["greet_wary_first_foreign_curt"] = new[] { "{=lmmi_gwff_curt_1}Foreigner. What do you want?", "{=lmmi_gwff_curt_2}Outlander. Speak.", "{=lmmi_gwff_curt_3}Foreign. What do you want?" },
            ["greet_wary_first_foreign_earnest"] = new[] { "{=lmmi_gwff_earnest_1}You're a long way from home, stranger. What is it?", "{=lmmi_gwff_earnest_2}From far off, I'd guess. I'll hear you out — briefly.", "{=lmmi_gwff_earnest_3}Strangers don't often come to me. What is it you need?" },
            ["greet_wary_first_foreign_ironic"] = new[] { "{=lmmi_gwff_ironic_1}Lost your way, stranger? Or just lost?", "{=lmmi_gwff_ironic_2}Ah, a traveler. Come to admire our charming town?", "{=lmmi_gwff_ironic_3}Another foreigner. We're becoming quite the crossroads." },
            ["greet_wary_first_foreign_soft"] = new[] { "{=lmmi_gwff_soft_1}You're not from here, I think. How can I... help?", "{=lmmi_gwff_soft_2}You're a long way from home, I think. What is it?", "{=lmmi_gwff_soft_3}Forgive me, I don't know your people's customs. What do you need?" },

            // ---- Later meetings, Wary ----
            ["greet_wary"] = new[] { "{=lmmi_greet_wary}Yes?", "{=lmmi_greet_wary_2}What brings you back?", "{=lmmi_greet_wary_3}You again. What is it?", "{=lmmi_greet_wary_4}Well?", "{=lmmi_greet_wary_5}Back, are you? Speak.", "{=lmmi_greet_wary_6}What do you need this time?" },
            ["greet_wary_curt"] = new[] { "{=lmmi_gw_curt_1}Speak.", "{=lmmi_gw_curt_2}Yes. What.", "{=lmmi_gw_curt_3}Go on." },
            ["greet_wary_earnest"] = new[] { "{=lmmi_gw_earnest_1}Back again? What is it this time?", "{=lmmi_gw_earnest_2}Again? Very well — what is it?", "{=lmmi_gw_earnest_3}You've come back. What can I do?" },
            ["greet_wary_ironic"] = new[] { "{=lmmi_gw_ironic_1}Back for more, are we?", "{=lmmi_gw_ironic_2}You do keep turning up.", "{=lmmi_gw_ironic_3}Ah. My favorite stranger." },
            ["greet_wary_soft"] = new[] { "{=lmmi_gw_soft_1}Oh. It's you. What do you need?", "{=lmmi_gw_soft_2}Oh — hello again.", "{=lmmi_gw_soft_3}You're back. Is something wrong?" },

            // ---- Sycophants ----
            ["greet_syco_first"] = new[]
            {
                "{=lmmi_greet_syco_first}The famous {PLAYER.NAME}, in my humble town! What an honor, {?PLAYER.GENDER}my lady{?}my lord{\\?}.",
                "{=lmmi_greet_syco_first_2}{?PLAYER.GENDER}My lady{?}My lord{\\?}! Word of your deeds reached us long before you did.",
                "{=lmmi_greet_syco_first_3}{?PLAYER.GENDER}My lady{?}My lord{\\?}! In my house! Someone fetch the good wine!",
                "{=lmmi_greet_syco_first_4}The renowned {PLAYER.NAME}! The stories don't do you justice — you're grander still in person.",
                "{=lmmi_greet_syco_first_5}Welcome, welcome, {?PLAYER.GENDER}my lady{?}my lord{\\?}! Anything you need, anything at all.",
                "{=lmmi_greet_syco_first_6}Such an honor! I've followed your exploits for years, {?PLAYER.GENDER}my lady{?}my lord{\\?}.",
            },
            ["greet_syco_first_curt"] = new[] { "{=lmmi_gsf_curt_1}{?PLAYER.GENDER}My lady{?}My lord{\\?}. An honor. A true honor.", "{=lmmi_gsf_curt_2}{PLAYER.NAME}. An honor. Your servant.", "{=lmmi_gsf_curt_3}{?PLAYER.GENDER}My lady{?}My lord{\\?}. Whatever you require." },
            ["greet_syco_first_earnest"] = new[] { "{=lmmi_gsf_earnest_1}I have heard so much about you, {?PLAYER.GENDER}my lady{?}my lord{\\?}! Welcome, welcome!", "{=lmmi_gsf_earnest_2}Is it really you? {PLAYER.NAME}, here? Please, sit, sit!", "{=lmmi_gsf_earnest_3}I'd hoped one day to meet you, {?PLAYER.GENDER}my lady{?}my lord{\\?}. Truly!" },
            ["greet_syco_first_ironic"] = new[] { "{=lmmi_gsf_ironic_1}Why, the great {PLAYER.NAME} {?PLAYER.GENDER}herself{?}himself{\\?}! We are truly blessed.", "{=lmmi_gsf_ironic_2}What a day! A legend walks among us humble traders.", "{=lmmi_gsf_ironic_3}{PLAYER.NAME}! The bards don't do you justice. Not nearly." },
            ["greet_syco_first_soft"] = new[] { "{=lmmi_gsf_soft_1}It is... a great honor, {?PLAYER.GENDER}my lady{?}my lord{\\?}. Truly.", "{=lmmi_gsf_soft_2}I... I've heard so much about you. Welcome.", "{=lmmi_gsf_soft_3}Please, come in, {?PLAYER.GENDER}my lady{?}my lord{\\?}. You honor this house." },

            ["greet_syco"] = new[]
            {
                "{=lmmi_greet_syco}{?PLAYER.GENDER}My lady{?}My lord{\\?}! You honor me again.",
                "{=lmmi_greet_syco_2}Ah, {?PLAYER.GENDER}my lady{?}my lord{\\?}! Always a pleasure. Always.",
                "{=lmmi_greet_syco_3}{?PLAYER.GENDER}My lady{?}My lord{\\?}! I was hoping you'd come by.",
                "{=lmmi_greet_syco_4}Back again! The town is brighter for it, {?PLAYER.GENDER}my lady{?}my lord{\\?}.",
                "{=lmmi_greet_syco_5}Ah, my most honored visitor!",
                "{=lmmi_greet_syco_6}{PLAYER.NAME}! Sit, sit — I'll have them bring something.",
            },
            ["greet_syco_curt"] = new[] { "{=lmmi_gs_curt_1}{?PLAYER.GENDER}My lady{?}My lord{\\?}. At your service.", "{=lmmi_gs_curt_2}{PLAYER.NAME}. Welcome back. At your service.", "{=lmmi_gs_curt_3}{?PLAYER.GENDER}My lady{?}My lord{\\?}. Command me." },
            ["greet_syco_earnest"] = new[] { "{=lmmi_gs_earnest_1}{?PLAYER.GENDER}My lady{?}My lord{\\?}, you've come back! I was just telling everyone about you.", "{=lmmi_gs_earnest_2}You've come back! I told my whole family you would.", "{=lmmi_gs_earnest_3}Welcome, welcome! I've thought of nothing but your last visit." },
            ["greet_syco_ironic"] = new[] { "{=lmmi_gs_ironic_1}The day brightens! Our {?PLAYER.GENDER}great lady{?}great lord{\\?} returns.", "{=lmmi_gs_ironic_2}The great one returns! Mind the step, {?PLAYER.GENDER}my lady{?}my lord{\\?} — it's beneath you.", "{=lmmi_gs_ironic_3}And the sun comes out. Welcome back." },
            ["greet_syco_soft"] = new[] { "{=lmmi_gs_soft_1}Welcome back, {?PLAYER.GENDER}my lady{?}my lord{\\?}. Whatever you need...", "{=lmmi_gs_soft_2}Welcome back. I'm... so glad you came.", "{=lmmi_gs_soft_3}Oh, {?PLAYER.GENDER}my lady{?}my lord{\\?}. Please — what can I do?" },

            // ---- Genuine, trusted friends ----
            ["greet_friend"] = new[]
            {
                "{=lmmi_greet_friend}{PLAYER.NAME}, my friend! Good to see you.",
                "{=lmmi_greet_friend_2}Ah, there you are. Come, sit.",
                "{=lmmi_greet_friend_3}There's the face I wanted to see. Sit down, tell me everything.",
                "{=lmmi_greet_friend_4}{PLAYER.NAME}! Come in, come in — you look like the road's been hard on you.",
                "{=lmmi_greet_friend_5}Well met, old friend.",
                "{=lmmi_greet_friend_6}You've been away too long. What's the news?",
            },
            ["greet_friend_curt"] = new[] { "{=lmmi_gf_curt_1}{PLAYER.NAME}. Good. Sit.", "{=lmmi_gf_curt_2}Friend. Sit.", "{=lmmi_gf_curt_3}{PLAYER.NAME}. Good to see you." },
            ["greet_friend_earnest"] = new[] { "{=lmmi_gf_earnest_1}{PLAYER.NAME}! It does my heart good to see you.", "{=lmmi_gf_earnest_2}My friend! Come here — it's been far too long.", "{=lmmi_gf_earnest_3}{PLAYER.NAME}! You're a sight for sore eyes, truly." },
            ["greet_friend_ironic"] = new[] { "{=lmmi_gf_ironic_1}Look what the wind blew in. Sit, sit, before someone important sees you.", "{=lmmi_gf_ironic_2}Still alive, then? The gods must like you.", "{=lmmi_gf_ironic_3}Ah, trouble walks in. Sit — I'll pour." },
            ["greet_friend_soft"] = new[] { "{=lmmi_gf_soft_1}{PLAYER.NAME}... I'm glad you came.", "{=lmmi_gf_soft_2}You came. I'm so glad.", "{=lmmi_gf_soft_3}{PLAYER.NAME}... it's good to see you safe." },

            // ---- Again within a day ----
            ["short_contempt"] = new[] { "{=lmmi_short_contempt}You again? What now?", "{=lmmi_short_contempt_2}Again? I've nothing for you.", "{=lmmi_short_contempt_3}Haven't you bothered me enough today?", "{=lmmi_short_contempt_4}You don't give up, do you?" },
            ["short_contempt_curt"] = new[] { "{=lmmi_sc_curt_1}What now.", "{=lmmi_sc_curt_2}No." },
            ["short_contempt_ironic"] = new[] { "{=lmmi_sc_ironic_1}Oh, good. You're back.", "{=lmmi_sc_ironic_2}Twice in one day. Lucky me." },
            ["short_contempt_earnest"] = new[] { "{=lmmi_sc_earnest_1}Did I not make myself clear the first time?", "{=lmmi_sc_earnest_2}I thought I'd been clear." },
            ["short_contempt_soft"] = new[] { "{=lmmi_sc_soft_1}Please... not again.", "{=lmmi_sc_soft_2}I'd rather you didn't keep coming here." },
            ["short_wary"] = new[] { "{=lmmi_short_wary}Back already?", "{=lmmi_short_wary_2}You again. Well?", "{=lmmi_short_wary_3}Forget something?", "{=lmmi_short_wary_4}Back so soon? What is it?" },
            ["short_wary_ironic"] = new[] { "{=lmmi_sw_ironic_1}Missed me already?", "{=lmmi_sw_ironic_2}Couldn't stay away, could you?" },
            ["short_wary_curt"] = new[] { "{=lmmi_sw_curt_1}Again. Speak.", "{=lmmi_sw_curt_2}What now?" },
            ["short_wary_earnest"] = new[] { "{=lmmi_sw_earnest_1}Back already? Is something the matter?" },
            ["short_wary_soft"] = new[] { "{=lmmi_sw_soft_1}Oh — you're back." },
            ["short_syco"] = new[] { "{=lmmi_short_syco}Back so soon, {?PLAYER.GENDER}my lady{?}my lord{\\?}? Always a pleasure!", "{=lmmi_short_syco_2}Back already, {?PLAYER.GENDER}my lady{?}my lord{\\?}! What a delight.", "{=lmmi_short_syco_3}Again! You spoil us, {?PLAYER.GENDER}my lady{?}my lord{\\?}.", "{=lmmi_short_syco_4}Twice in one day — the neighbors will be so jealous." },
            ["short_friend"] = new[] { "{=lmmi_short_friend}Back already? Good.", "{=lmmi_short_friend_2}Back again? Sit down.", "{=lmmi_short_friend_3}Forgot something? Or did you just miss the company?", "{=lmmi_short_friend_4}Good. I wasn't done talking to you." },
            ["short_friend_earnest"] = new[] { "{=lmmi_sf_earnest_1}Back so soon? Come in, come in.", "{=lmmi_sf_earnest_2}You're back! Sit, sit." },
            ["short_friend_ironic"] = new[] { "{=lmmi_sf_ironic_1}Back already? People will talk." },
            ["short_friend_curt"] = new[] { "{=lmmi_sf_curt_1}Back. Good." },
            ["short_friend_soft"] = new[] { "{=lmmi_sf_soft_1}Oh, you're back. Good." },

            // ---- Handing over to your options ----
            ["prompt_contempt"] = new[] { "{=lmmi_prompt_contempt}Well? Make it quick.", "{=lmmi_prompt_contempt_2}Get on with it.", "{=lmmi_prompt_contempt_3}Spit it out.", "{=lmmi_prompt_contempt_4}What do you want?", "{=lmmi_prompt_contempt_5}I'm busy. Talk.", "{=lmmi_prompt_contempt_6}Say what you came to say, and go." },
            ["prompt_contempt_ironic"] = new[] { "{=lmmi_pc_ironic_1}I'm listening. Barely.", "{=lmmi_pc_ironic_2}Go on. Dazzle me." },
            ["prompt_contempt_curt"] = new[] { "{=lmmi_pc_curt_1}Out with it.", "{=lmmi_pc_curt_2}Talk." },
            ["prompt_contempt_earnest"] = new[] { "{=lmmi_pc_earnest_1}Say it plainly, and then leave." },
            ["prompt_contempt_soft"] = new[] { "{=lmmi_pc_soft_1}What is it you want from me?" },
            ["prompt_syco"] = new[] { "{=lmmi_prompt_syco}How may I serve you?", "{=lmmi_prompt_syco_2}Whatever you wish, {?PLAYER.GENDER}my lady{?}my lord{\\?}.", "{=lmmi_prompt_syco_3}Command me. Please.", "{=lmmi_prompt_syco_4}What can this humble servant do for you?" },
            ["prompt_syco_earnest"] = new[] { "{=lmmi_ps_earnest_1}Anything you need, you have only to ask.", "{=lmmi_ps_earnest_2}Name it, and it's done!" },
            ["prompt_syco_ironic"] = new[] { "{=lmmi_ps_ironic_1}Ask, and you shall receive. Probably." },
            ["prompt_syco_curt"] = new[] { "{=lmmi_ps_curt_1}Your orders?" },
            ["prompt_syco_soft"] = new[] { "{=lmmi_ps_soft_1}Anything... anything at all." },

            // ---- First-meeting introductions from the contemptuous ----
            ["intro_contempt"] = new[]
            {
                "{=lmmi_intro_contempt_1}{LMMI_INTRO_NAME}. Not that it's any business of yours.",
                "{=lmmi_intro_contempt_2}The name's {LMMI_INTRO_NAME}. Remember it, so you'll know whom to avoid.",
                "{=lmmi_intro_contempt_3}{LMMI_INTRO_NAME}. You'll not need to remember it.",
                "{=lmmi_intro_contempt_4}They call me {LMMI_INTRO_NAME}. There — now we're acquainted. Pity.",
                "{=lmmi_intro_contempt_5}I'm {LMMI_INTRO_NAME}, and I've no wish to know who you are.",
                "{=lmmi_intro_contempt_6}{LMMI_INTRO_NAME}. Ask around about me — then stay away.",
            },
            ["intro_contempt_gang"] = new[] { "{=lmmi_intro_contempt_gang}{LMMI_INTRO_NAME}. People who cross me tend to end up in the river. Remember that.", "{=lmmi_intro_contempt_gang_2}{LMMI_INTRO_NAME}. The streets round here are mine. Walk them carefully.", "{=lmmi_intro_contempt_gang_3}I'm {LMMI_INTRO_NAME}. You've heard of me, or you will. Neither's good for you." },
            ["intro_contempt_merchant"] = new[] { "{=lmmi_intro_contempt_merchant}I'm {LMMI_INTRO_NAME}. And my goods are not for the likes of you.", "{=lmmi_intro_contempt_merchant_2}{LMMI_INTRO_NAME}. My prices just went up.", "{=lmmi_intro_contempt_merchant_3}I'm {LMMI_INTRO_NAME}, and I don't trade with every vagrant who wanders in." },
            ["intro_contempt_village"] = new[] { "{=lmmi_intro_contempt_village}{LMMI_INTRO_NAME}. This village doesn't want trouble, and you look like trouble.", "{=lmmi_intro_contempt_village_2}{LMMI_INTRO_NAME}. I speak for this village, and the village says go.", "{=lmmi_intro_contempt_village_3}I'm {LMMI_INTRO_NAME}. We've had our fill of outsiders here." },
            ["intro_contempt_artisan"] = new[] { "{=lmmi_intro_contempt_artisan}{LMMI_INTRO_NAME}. My workshop, my rules. You'll find no welcome in it.", "{=lmmi_intro_contempt_artisan_2}{LMMI_INTRO_NAME}. Touch nothing in my shop.", "{=lmmi_intro_contempt_artisan_3}I'm {LMMI_INTRO_NAME}. I make things with my hands. You look like someone who breaks them." },

            // ---- Word gets around: you helped someone else in town ----
            ["greet_heard_deeds"] = new[]
            {
                "{=lmmi_heard_1}Word is you helped {LMMI_HELPED}. That counts for something around here.",
                "{=lmmi_heard_2}You're the one who sorted out {LMMI_HELPED}'s troubles, aren't you? Everyone's talking.",
                "{=lmmi_heard_3}I hear you've done right by {LMMI_HELPED}. Good. What is it?",
                "{=lmmi_heard_4}So you're the one {LMMI_HELPED} keeps talking about.",
                "{=lmmi_heard_5}{LMMI_HELPED} says you're decent. I'll take their word — for now.",
                "{=lmmi_heard_6}Word travels. You helped {LMMI_HELPED}. That buys you a hearing.",
            },
            ["greet_heard_deeds_curt"] = new[] { "{=lmmi_heard_curt}You helped {LMMI_HELPED}. I heard. Speak.", "{=lmmi_heard_curt_2}{LMMI_HELPED} vouches for you. Speak." },
            ["greet_heard_deeds_ironic"] = new[] { "{=lmmi_heard_ironic}Ah, {LMMI_HELPED}'s savior. The whole street won't shut up about you.", "{=lmmi_heard_ironic_2}The hero of {LMMI_HELPED}'s little drama. Charmed." },
            ["greet_heard_deeds_earnest"] = new[] { "{=lmmi_heard_earnest}{LMMI_HELPED} speaks well of you. That's rare. What can I do for you?", "{=lmmi_heard_earnest_2}You did right by {LMMI_HELPED}. Thank you for that." },
            ["greet_heard_deeds_soft"] = new[] { "{=lmmi_heard_soft}I heard what you did for {LMMI_HELPED}. It was... kind.", "{=lmmi_heard_soft_2}{LMMI_HELPED} was so grateful. It was good of you." },

            // ---- You stood up for a local: prejudice meets a fact ----
            ["greet_stepped_up_foreign"] = new[]
            {
                "{=lmmi_stepup_1}Huh. Didn't expect this from a {LMMI_SLUR}. Word is you stood up for one of ours.",
                "{=lmmi_stepup_2}So you're the {LMMI_SLUR} everyone's talking about. Stepped in when our own wouldn't, they say.",
                "{=lmmi_stepup_3}A {LMMI_SLUR} who sticks up for our people. Strange times.",
                "{=lmmi_stepup_4}Last month I'd have spat at a {LMMI_SLUR}. Now they tell me you helped one of ours. Hm.",
                "{=lmmi_stepup_5}Never thought I'd thank a {LMMI_SLUR}. Don't make me regret it.",
                "{=lmmi_stepup_6}You're a {LMMI_SLUR}, but they say you've got a backbone. That's more than most have.",
            },
            ["greet_stepped_up_foreign_ironic"] = new[] { "{=lmmi_stepup_ironic}A {LMMI_SLUR} playing the hero. Now I've seen everything. ...It was well done, I'll grant you.", "{=lmmi_stepup_ironic_2}Well, well. A {LMMI_SLUR} with a conscience. Whatever next?" },
            ["greet_stepped_up_foreign_curt"] = new[] { "{=lmmi_stepup_curt}You. The {LMMI_SLUR} who helped. Hm. Speak, then.", "{=lmmi_stepup_curt_2}{LMMI_SLUR}. You did a decent thing. Noted." },
            ["greet_stepped_up_foreign_earnest"] = new[] { "{=lmmi_stepup_earnest_1}Maybe I misjudged your kind. You helped one of ours — thank you." },
            ["greet_stepped_up_foreign_soft"] = new[] { "{=lmmi_stepup_soft_1}They say you were kind to one of ours. I... didn't expect it." },
            ["greet_stepped_up_kin"] = new[] { "{=lmmi_stepup_kin}Word is you stood up for one of ours. Good. That's how it should be.", "{=lmmi_stepup_kin_2}One of our own, doing right by the town. Good to hear.", "{=lmmi_stepup_kin_3}They say you stepped in when it mattered. That's how our people are.", "{=lmmi_stepup_kin_4}Good on you, standing up for the neighbors. Sit, then." },

            // ---- Farewells (replace vanilla's pleasantries for those who don't mean them) ----
            ["farewell_contempt"] = new[]
            {
                "{=lmmi_bye_contempt_1}Good riddance.",
                "{=lmmi_bye_contempt_2}Don't come back.",
                "{=lmmi_bye_contempt_3}And stay gone.",
                "{=lmmi_bye_contempt_4}Off with you.",
                "{=lmmi_bye_contempt_5}Don't let the door hit you.",
                "{=lmmi_bye_contempt_6}Finally.",
                "{=lmmi_bye_contempt_7}Go and bother someone else.",
                "{=lmmi_bye_contempt_8}Out.",
                "{=lmmi_bye_contempt_9}And take your stink with you.",
            },
            ["farewell_contempt_ironic"] = new[] { "{=lmmi_bye_contempt_ironic}Leaving so soon? What a pity.", "{=lmmi_bye_contempt_ironic_2}Do come again. Or don't. Preferably don't.", "{=lmmi_bye_contempt_ironic_3}Such a delight. Truly." },
            ["farewell_contempt_curt"] = new[] { "{=lmmi_bye_contempt_curt}Go.", "{=lmmi_bye_contempt_curt_2}Leave.", "{=lmmi_bye_contempt_curt_3}Out. Now." },
            ["farewell_wary"] = new[] { "{=lmmi_bye_wary_1}Hm.", "{=lmmi_bye_wary_2}Go on, then.", "{=lmmi_bye_wary_3}Be on your way.", "{=lmmi_bye_wary_4}Mind how you go.", "{=lmmi_bye_wary_5}Right. Off you go.", "{=lmmi_bye_wary_6}We're done, then.", "{=lmmi_bye_wary_7}Until next time, I suppose.", "{=lmmi_bye_wary_8}Go carefully." },
            ["farewell_wary_earnest"] = new[] { "{=lmmi_bye_wary_earnest}Safe travels, I suppose.", "{=lmmi_bye_wary_earnest_2}Mind yourself out there.", "{=lmmi_bye_wary_earnest_3}Good day to you." },
            ["farewell_wary_curt"] = new[] { "{=lmmi_bye_wary_curt_1}Done." },
            ["farewell_wary_ironic"] = new[] { "{=lmmi_bye_wary_ironic_1}Off already? Shame." },
            ["farewell_wary_soft"] = new[] { "{=lmmi_bye_wary_soft_1}Take care, then." },
            ["farewell_syco"] = new[] { "{=lmmi_bye_syco_1}Always a pleasure, {?PLAYER.GENDER}my lady{?}my lord{\\?}! Do come again!", "{=lmmi_bye_syco_2}You honor us. Truly.", "{=lmmi_bye_syco_3}Come back soon, {?PLAYER.GENDER}my lady{?}my lord{\\?}! My door is always open.", "{=lmmi_bye_syco_4}Safe roads, {?PLAYER.GENDER}my lady{?}my lord{\\?}! I'll tell everyone you visited.", "{=lmmi_bye_syco_5}Such an honor. Such an honor!", "{=lmmi_bye_syco_6}Do remember me to your friends at court!" },
            ["farewell_syco_earnest"] = new[] { "{=lmmi_bye_syco_earnest_1}Come back soon! Please, come back soon." },

            // ---- Favor openers ----
            ["open_wary_foreign"] = new[]
            {
                "{=lmmi_open_wary_foreign_1}Favors cost coin, stranger. More for your kind.",
                "{=lmmi_open_wary_foreign_2}For a foreigner? It'll cost you. Go on.",
                "{=lmmi_open_wary_foreign_3}Your kind pays extra. That's just how it is.",
                "{=lmmi_open_wary_foreign_4}I'll think about it — for a price. A foreigner's price.",
                "{=lmmi_open_wary_foreign_5}Help a stranger? Coin first, then we'll talk.",
                "{=lmmi_open_wary_foreign_6}Nothing's free for outlanders. What do you want?",
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
