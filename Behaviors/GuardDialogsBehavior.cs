using System;
using System.Reflection;
using HarmonyLib;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using SandBox.CampaignBehaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Guards have opinions too. Street guards greet a kinsman, tolerate a foreigner of standing, and sneer at a
    /// foreign nobody; the lord's hall guard turns you away with a joke at your people's expense; the dungeon guard
    /// asks what your kind wants down there. Vanilla's conditions still decide *whether* they talk and let you in —
    /// only the words change. Their temper: the town's culture against yours (culture table, MCM prejudice), your
    /// clan tier, and your standing in town.
    /// </summary>
    public class GuardDialogsBehavior : CampaignBehaviorBase
    {
        private enum GuardTone { Vanilla, Kin, KinOfStanding, GrudgingRespect, WordOfDeeds, Contempt }

        private static readonly MethodInfo? CastleGuardStart = AccessTools.Method(typeof(GuardsCampaignBehavior), "conversation_castle_guard_start_on_condition");
        private static readonly MethodInfo? PrisonGuardStart = AccessTools.Method(typeof(GuardsCampaignBehavior), "conversation_prison_guard_start_on_condition");
        private static readonly MethodInfo? HallNobodyInside = AccessTools.Method(typeof(GuardsCampaignBehavior), "conversation_castle_guard_nobody_inside_condition");
        private static readonly MethodInfo? HallCanEnter = AccessTools.Method(typeof(GuardsCampaignBehavior), "conversation_castle_guard_player_can_enter_lordshall_condition");

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, AddDialogs);
        }

        public override void SyncData(IDataStore dataStore) { }

        private static bool Vanilla(MethodInfo? condition)
        {
            var guards = Campaign.Current?.GetCampaignBehavior<GuardsCampaignBehavior>();
            if (guards == null || condition == null) return false;
            try { return (bool)condition.Invoke(guards, null); }
            catch (Exception ex) { LmmiLog.Error($"GuardDialogs: vanilla condition {condition.Name} threw", ex); return false; }
        }

        private static GuardTone Tone(Settlement? settlement)
        {
            var player = Hero.MainHero;
            if (!LmmiSettingsProvider.EnableNotableDisposition || settlement == null || player == null) return GuardTone.Vanilla;
            if (settlement.OwnerClan == Clan.PlayerClan || settlement.MapFaction?.Leader == player) return GuardTone.Vanilla;

            int tier = Clan.PlayerClan?.Tier ?? 0;
            if (settlement.Culture == player.Culture) return tier >= 4 ? GuardTone.KinOfStanding : GuardTone.Kin;

            float resentment = CultureRelations.Multiplier(settlement.Culture?.StringId, player.Culture?.StringId)
                               * LmmiSettingsProvider.ForeignerPrejudicePercent / 100f;
            if (tier >= 4 || resentment <= 0f) return GuardTone.GrudgingRespect;
            if (TownStandingBehavior.Get(settlement) >= 10f) return GuardTone.WordOfDeeds;
            return GuardTone.Contempt;
        }

        private static TextObject Pick(params string[] lines) => new TextObject(lines[MBRandom.RandomInt(lines.Length)]);

        private static TextObject WithWords(TextObject line, Settlement settlement)
        {
            line.SetTextVariable("DEMONYM", CultureWords.Demonym(Hero.MainHero.Culture));
            line.SetTextVariable("SLUR", CultureWords.Slur(Hero.MainHero.Culture));
            line.SetTextVariable("SETTLEMENT", settlement.Name);
            line.SetTextVariable("RULER", settlement.MapFaction?.Leader?.Name ?? new TextObject("{=lmmi_guard_the_lord}the lord"));
            return line;
        }

        // ---- Street guards ----

        internal static TextObject? StreetComment(Settlement settlement)
        {
            TextObject line;
            switch (Tone(settlement))
            {
                case GuardTone.Kin:
                    line = Pick("{=lmmi_guard_kin_1}Welcome, {?PLAYER.GENDER}kinswoman{?}kinsman{\\?}. Keep your nose clean and you'll have no trouble here.",
                                "{=lmmi_guard_kin_2}One of ours. Good. Mind the cutpurses near the market.",
                                "{=lmmi_guard_kin_3}Good to hear our own tongue at the gate. Go on in, {?PLAYER.GENDER}kinswoman{?}kinsman{\\?}.",
                                "{=lmmi_guard_kin_4}One of ours, back from the road. Watch the dice players by the well — they cheat.",
                                "{=lmmi_guard_kin_5}Welcome home, near enough. Keep the peace and we'll keep the thieves off you.",
                                "{=lmmi_guard_kin_6}Ah, a familiar accent. Mind yourself in the tavern; there's foreigners about.");
                    break;
                case GuardTone.KinOfStanding:
                    line = Pick("{=lmmi_guard_kin_lord}Welcome to {SETTLEMENT}, my {?PLAYER.GENDER}lady{?}lord{\\?}. Good to see one of our own doing well.",
                                "{=lmmi_guard_kin_lord_2}My {?PLAYER.GENDER}lady{?}lord{\\?}. {SETTLEMENT} is proud to see one of its own people rise so high.",
                                "{=lmmi_guard_kin_lord_3}Make way, there! Welcome, my {?PLAYER.GENDER}lady{?}lord{\\?}. Should any trouble find you, call for the watch.");
                    break;
                case GuardTone.GrudgingRespect:
                    line = Pick("{=lmmi_guard_grudging_1}My {?PLAYER.GENDER}lady{?}lord{\\?}. Welcome to {SETTLEMENT}. We'll... keep an eye out for you.",
                                "{=lmmi_guard_grudging_2}Welcome to {SETTLEMENT}. Behave, and we'll get along.",
                                "{=lmmi_guard_grudging_3}My {?PLAYER.GENDER}lady{?}lord{\\?}. Your name's known even here. Keep the peace and you'll have no quarrel with us.",
                                "{=lmmi_guard_grudging_4}A {DEMONYM} of rank. Welcome to {SETTLEMENT}... I suppose.",
                                "{=lmmi_guard_grudging_5}Welcome. The lord says folk of your standing are to be treated civil. So I'm being civil.",
                                "{=lmmi_guard_grudging_6}You've a name, even if it's a foreign one. Go on through.");
                    break;
                case GuardTone.WordOfDeeds:
                    line = Pick("{=lmmi_guard_deeds}Word is you've done right by this town. Even for a {DEMONYM}. Go on, then.",
                                "{=lmmi_guard_deeds_2}You're the {DEMONYM} who helped our people. Didn't think I'd say it, but — welcome.",
                                "{=lmmi_guard_deeds_3}They talk about you in the barracks. The good kind of talk, mostly. Go on.",
                                "{=lmmi_guard_deeds_4}A {DEMONYM}, but a decent one, they say. Don't prove them wrong.");
                    break;
                case GuardTone.Contempt:
                    line = ContemptForCulture();
                    break;
                default:
                    return null;
            }
            return WithWords(line, settlement);
        }

        private static TextObject ContemptForCulture()
        {
            string generic = "{=lmmi_guard_scorn_generic}Ahh... a {DEMONYM}. Ugh. You're lucky our lord lets foreign scum walk around untouched.";
            string generic2 = "{=lmmi_guard_scorn_generic_2}A {DEMONYM}. Keep your hands to yourself and your coin in sight. I'll be watching.";
            string generic3 = "{=lmmi_guard_scorn_generic_3}Another {SLUR}. Step out of line once and you'll see the inside of our cells.";
            switch (Hero.MainHero.Culture?.StringId)
            {
                case "khuzait": return Pick(generic, generic2, generic3, "{=lmmi_guard_scorn_khuzait}Keep your horse out of the fountain, {SLUR}.",
                    "{=lmmi_guard_scorn_khuzait_2}No arrows in the street, {SLUR}. And keep away from the stables.",
                    "{=lmmi_guard_scorn_khuzait_3}Khuzait. I'll be counting the horses in the stable after you've gone.",
                    "{=lmmi_guard_scorn_khuzait_4}Walk on, nomad. You'll find no grass to graze in here.");
                case "sturgia": return Pick(generic, generic2, generic3, "{=lmmi_guard_scorn_sturgia}No brawling, northerner. If there's a broken head tonight, I'll know whose it was.",
                    "{=lmmi_guard_scorn_sturgia_2}Off with you, {SLUR}. If I find you face-down in the gutter, you're spending the night in the cells.",
                    "{=lmmi_guard_scorn_sturgia_3}Sturgian. I can smell the mead from here. Keep it quiet.",
                    "{=lmmi_guard_scorn_sturgia_4}Northerner. Your axe stays on your belt, or it goes in the river.");
                case "aserai": return Pick(generic, generic2, generic3, "{=lmmi_guard_scorn_aserai}Hands where I can see them, Aserai. Your kind have quick fingers.",
                    "{=lmmi_guard_scorn_aserai_2}Leave your desert manners at the gate, {SLUR}.",
                    "{=lmmi_guard_scorn_aserai_3}Aserai. The heat's got to your head if you think you're welcome here.",
                    "{=lmmi_guard_scorn_aserai_4}Mind yourself, desert-born. Your sultan's word counts for nothing in this town.");
                case "battania": return Pick(generic, generic2, generic3, "{=lmmi_guard_scorn_battania}Leave the woad and the war cries at the gate, Battanian.",
                    "{=lmmi_guard_scorn_battania_2}One raid on our herds and you'll hang from the gate, {SLUR}.",
                    "{=lmmi_guard_scorn_battania_3}Battanian. No war-paint in the market, and no singing.",
                    "{=lmmi_guard_scorn_battania_4}Hillman. Stay away from the cattle pens.");
                case "vlandia": return Pick(generic, generic2, generic3, "{=lmmi_guard_scorn_vlandia}Another Vlandian. Try not to swear fealty to anything while you're here.",
                    "{=lmmi_guard_scorn_vlandia_2}Leave the goats alone, {SLUR}. The farmers have complained before.",
                    "{=lmmi_guard_scorn_vlandia_3}Vlandian. Sell your sword elsewhere — we've no use for it.",
                    "{=lmmi_guard_scorn_vlandia_4}Mind your manners, Vlandian. Your knights aren't in charge here.");
                case "empire": return Pick(generic, generic2, generic3, "{=lmmi_guard_scorn_empire}An imperial. Remember whose town this is now.",
                    "{=lmmi_guard_scorn_empire_2}Nobody here cares who your grandfather was, {SLUR}.",
                    "{=lmmi_guard_scorn_empire_3}Imperial. Spare us the speeches about the old days.",
                    "{=lmmi_guard_scorn_empire_4}The purple doesn't impress anyone here, imperial. Move along.");
                case "nord": return Pick(generic, generic2, generic3, "{=lmmi_guard_scorn_nord}Keep that axe sheathed, Nord. This isn't a beach you're raiding.",
                    "{=lmmi_guard_scorn_nord_2}If anything goes missing near the harbor tonight, {SLUR}, I'll know where to look.",
                    "{=lmmi_guard_scorn_nord_3}Nord. You're a long way from your boats. Keep it that way.",
                    "{=lmmi_guard_scorn_nord_4}Sheathe it and keep it sheathed, sea-raider.");
                default: return Pick(generic, generic2, generic3);
            }
        }

        // ---- Lord's hall and dungeon ----

        private static TextObject? HallDenial(Settlement settlement)
        {
            TextObject line;
            switch (Tone(settlement))
            {
                case GuardTone.Kin:
                    line = Pick("{=lmmi_hall_deny_kin}Sorry, {?PLAYER.GENDER}kinswoman{?}kinsman{\\?}, but we don't know you. Make a name for yourself first. (Not enough renown)",
                                "{=lmmi_hall_deny_kin_2}Easy, {?PLAYER.GENDER}kinswoman{?}kinsman{\\?}. The lord sees people of name. Earn one and come back. (Not enough renown)",
                                "{=lmmi_hall_deny_kin_3}I'd let you in if it were up to me. It isn't. Do something worth talking about first. (Not enough renown)");
                    break;
                case GuardTone.Contempt:
                    line = MBRandom.RandomFloat < 0.4f
                        ? Pick("{=lmmi_hall_deny_ruler}Hah! And I want to share {RULER}'s bed, for all the good wanting does me. Move along, {DEMONYM}. (Not enough renown)",
                               "{=lmmi_hall_deny_ruler_2}Hah! See the lord? I'd like to see the inside of the treasury, myself. Move along, {DEMONYM}. (Not enough renown)",
                               "{=lmmi_hall_deny_ruler_3}{RULER} doesn't receive every {SLUR} who wanders up the steps. Off with you. (Not enough renown)")
                        : HallJokeForCulture();
                    break;
                default:
                    return null;   // vanilla's "Sorry, but we don't know you."
            }
            return WithWords(line, settlement);
        }

        private static TextObject HallJokeForCulture()
        {
            switch (Hero.MainHero.Culture?.StringId)
            {
                case "aserai": return Pick("{=lmmi_hall_deny_aserai}Right, of course you do, Aserai. No goats to fondle in there, though. The animal pens are that way. Now scram. (Not enough renown)",
                    "{=lmmi_hall_deny_aserai_2}The lord's hall is no caravanserai, Aserai. Off. (Not enough renown)",
                    "{=lmmi_hall_deny_aserai_3}Go and pitch a tent in the market, desert-born. The hall's not for you. (Not enough renown)");
                case "khuzait": return Pick("{=lmmi_hall_deny_khuzait}The lord's hall isn't a stable, nomad. Try the one out back. (Not enough renown)",
                    "{=lmmi_hall_deny_khuzait_2}You'd only try to ride through the door, Khuzait. No. (Not enough renown)",
                    "{=lmmi_hall_deny_khuzait_3}The lord's not buying horses today, nomad. Move along. (Not enough renown)");
                case "battania": return Pick("{=lmmi_hall_deny_battania}No trees to hug in there, Battanian. Off you go. (Not enough renown)",
                    "{=lmmi_hall_deny_battania_2}The lord doesn't hold court for painted hillmen. Out. (Not enough renown)",
                    "{=lmmi_hall_deny_battania_3}Go back to your standing stones, Battanian. They'll talk to you. (Not enough renown)");
                case "sturgia": return Pick("{=lmmi_hall_deny_sturgia}The hall's not a mead-house, northerner. Sober up and try again. Or don't. (Not enough renown)",
                    "{=lmmi_hall_deny_sturgia_2}There's no bear to wrestle in there, northerner. Shove off. (Not enough renown)",
                    "{=lmmi_hall_deny_sturgia_3}The lord's wine is not for Sturgian throats. Away. (Not enough renown)");
                case "vlandia": return Pick("{=lmmi_hall_deny_vlandia}A Vlandian wants to see the lord. To swear another oath you'll break? No. (Not enough renown)",
                    "{=lmmi_hall_deny_vlandia_2}The lord doesn't need another Vlandian sellsword. Try the tavern. (Not enough renown)",
                    "{=lmmi_hall_deny_vlandia_3}No fiefs handed out today, Vlandian. Go home. (Not enough renown)");
                case "empire": return Pick("{=lmmi_hall_deny_empire}Imperial airs won't open this door, friend. Not anymore. (Not enough renown)",
                    "{=lmmi_hall_deny_empire_2}The lord doesn't need lectures on how the Empire did things. Away. (Not enough renown)",
                    "{=lmmi_hall_deny_empire_3}Your senate has no say here, imperial. Nor do you. (Not enough renown)");
                case "nord": return Pick("{=lmmi_hall_deny_nord}No longboats to beach in there, Nord. Move along. (Not enough renown)",
                    "{=lmmi_hall_deny_nord_2}The last Nord in that hall walked off with the candlesticks. You're not going in. (Not enough renown)",
                    "{=lmmi_hall_deny_nord_3}Leave your axe at the door — actually, leave altogether. (Not enough renown)");
                default: return Pick("{=lmmi_hall_deny_generic}We don't know you, stranger, and I don't much like the look of you. No. (Not enough renown)",
                    "{=lmmi_hall_deny_generic_2}Never seen you before, and the lord hasn't either. Off. (Not enough renown)",
                    "{=lmmi_hall_deny_generic_3}The hall's for people the lord knows. That's not you. (Not enough renown)");
            }
        }

        private static TextObject? GateGreeting(Settlement settlement, bool prison)
        {
            TextObject line;
            switch (Tone(settlement))
            {
                case GuardTone.Kin:
                    line = prison ? Pick("{=lmmi_prison_kin}{?PLAYER.GENDER}Kinswoman{?}Kinsman{\\?}. What brings you down to the cells?",
                                       "{=lmmi_prison_kin_2}Down to the cells, {?PLAYER.GENDER}kinswoman{?}kinsman{\\?}? Mind the stairs, they're slick.",
                                       "{=lmmi_prison_kin_3}One of ours, down in this pit? What is it you need?")
                                  : Pick("{=lmmi_castle_kin}{?PLAYER.GENDER}Kinswoman{?}Kinsman{\\?}. What is it?",
                                       "{=lmmi_castle_kin_2}{?PLAYER.GENDER}Kinswoman{?}Kinsman{\\?}. The keep's busy today. What do you want?",
                                       "{=lmmi_castle_kin_3}Ah, one of our own. Speak up — what brings you to the keep?");
                    break;
                case GuardTone.Contempt:
                    line = prison ? Pick("{=lmmi_prison_scorn}What does a {DEMONYM} want at the dungeon? Visiting relatives?",
                                       "{=lmmi_prison_scorn_2}A {SLUR} at the dungeon door. Here to visit, or to stay?",
                                       "{=lmmi_prison_scorn_3}Come to see the cells, {DEMONYM}? There's a spare one, if you're asking.")
                                  : Pick("{=lmmi_castle_scorn}A {DEMONYM} at the keep. What do you want? Make it quick.",
                                       "{=lmmi_castle_scorn_2}What's a {SLUR} doing at the keep? Speak, and be quick about it.",
                                       "{=lmmi_castle_scorn_3}A {DEMONYM}. The keep's not a market. What do you want?");
                    break;
                default:
                    return null;
            }
            return WithWords(line, settlement);
        }

        private static void AddDialogs(CampaignGameStarter starter)
        {
            try
            {
                starter.AddDialogLine("lmmi_castle_guard_start", "start", "castle_guard_talk",
                    "{=lmmi_castle_guard_start}{LMMI_GATE_GREETING}",
                    () => SetIf("LMMI_GATE_GREETING", Vanilla(CastleGuardStart) ? GateGreeting(Settlement.CurrentSettlement, prison: false) : null),
                    null, 105);

                starter.AddDialogLine("lmmi_prison_guard_start", "start", "prison_guard_talk",
                    "{=lmmi_prison_guard_start}{LMMI_GATE_GREETING}",
                    () => SetIf("LMMI_GATE_GREETING", Vanilla(PrisonGuardStart) ? GateGreeting(Settlement.CurrentSettlement, prison: true) : null),
                    null, 105);

                // Same place in the flow as vanilla's "Sorry, but we don't know you" — only when neither
                // "nobody inside" nor "you may enter" applies, so the bribe option follows as usual.
                starter.AddDialogLine("lmmi_hall_deny", "player_ask_permission_to_lords_hall", "permisson_for_lords_hall",
                    "{=lmmi_hall_deny}{LMMI_HALL_DENIAL}",
                    () =>
                    {
                        if (Settlement.CurrentSettlement == null || Vanilla(HallNobodyInside) || Vanilla(HallCanEnter)) return false;
                        return SetIf("LMMI_HALL_DENIAL", HallDenial(Settlement.CurrentSettlement));
                    },
                    null, 105);
            }
            catch (Exception ex) { LmmiLog.Error("GuardDialogsBehavior: failed to register dialogs", ex); }
        }

        private static bool SetIf(string variable, TextObject? text)
        {
            if (text == null) return false;
            MBTextManager.SetTextVariable(variable, text);
            LmmiLog.Info($"Guard: \"{text}\"");
            return true;
        }

        /// <summary>Street guards' comment — vanilla chooses it here; we replace it unless you own the place.</summary>
        [HarmonyPatch(typeof(GuardsCampaignBehavior), "conversation_guard_start_on_condition")]
        private static class StreetGuardComment
        {
            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(bool __result)
            {
                try
                {
                    if (!__result || Settlement.CurrentSettlement == null) return;
                    var comment = StreetComment(Settlement.CurrentSettlement);
                    if (comment == null) return;
                    MBTextManager.SetTextVariable("GUARD_COMMENT", comment);
                    LmmiLog.Info($"Guard: \"{comment}\"");
                }
                catch (Exception ex) { LmmiLog.Error("GuardDialogsBehavior.StreetGuardComment threw", ex); }
            }
        }
    }
}
