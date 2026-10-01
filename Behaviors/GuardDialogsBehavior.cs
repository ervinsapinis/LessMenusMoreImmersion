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
                                "{=lmmi_guard_kin_2}One of ours. Good. Mind the cutpurses near the market.");
                    break;
                case GuardTone.KinOfStanding:
                    line = Pick("{=lmmi_guard_kin_lord}Welcome to {SETTLEMENT}, my {?PLAYER.GENDER}lady{?}lord{\\?}. Good to see one of our own doing well.");
                    break;
                case GuardTone.GrudgingRespect:
                    line = Pick("{=lmmi_guard_grudging_1}My {?PLAYER.GENDER}lady{?}lord{\\?}. Welcome to {SETTLEMENT}. We'll... keep an eye out for you.",
                                "{=lmmi_guard_grudging_2}Welcome to {SETTLEMENT}. Behave, and we'll get along.");
                    break;
                case GuardTone.WordOfDeeds:
                    line = Pick("{=lmmi_guard_deeds}Word is you've done right by this town. Even for a {DEMONYM}. Go on, then.");
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
            switch (Hero.MainHero.Culture?.StringId)
            {
                case "khuzait": return Pick(generic, "{=lmmi_guard_scorn_khuzait}Keep your horse out of the fountain, {SLUR}.");
                case "sturgia": return Pick(generic, "{=lmmi_guard_scorn_sturgia}No brawling, northerner. If there's a broken head tonight, I'll know whose it was.");
                case "aserai": return Pick(generic, "{=lmmi_guard_scorn_aserai}Hands where I can see them, Aserai. Your kind have quick fingers.");
                case "battania": return Pick(generic, "{=lmmi_guard_scorn_battania}Leave the woad and the war cries at the gate, Battanian.");
                case "vlandia": return Pick(generic, "{=lmmi_guard_scorn_vlandia}Another Vlandian. Try not to swear fealty to anything while you're here.");
                case "empire": return Pick(generic, "{=lmmi_guard_scorn_empire}An imperial. Remember whose town this is now.");
                case "nord": return Pick(generic, "{=lmmi_guard_scorn_nord}Keep that axe sheathed, Nord. This isn't a beach you're raiding.");
                default: return Pick(generic);
            }
        }

        // ---- Lord's hall and dungeon ----

        private static TextObject? HallDenial(Settlement settlement)
        {
            TextObject line;
            switch (Tone(settlement))
            {
                case GuardTone.Kin:
                    line = Pick("{=lmmi_hall_deny_kin}Sorry, {?PLAYER.GENDER}kinswoman{?}kinsman{\\?}, but we don't know you. Make a name for yourself first. (Not enough renown)");
                    break;
                case GuardTone.Contempt:
                    line = MBRandom.RandomFloat < 0.4f
                        ? Pick("{=lmmi_hall_deny_ruler}Hah! And I want to share {RULER}'s bed, for all the good wanting does me. Move along, {DEMONYM}. (Not enough renown)")
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
                case "aserai": return Pick("{=lmmi_hall_deny_aserai}Right, of course you do, Aserai. No goats to fondle in there, though. The animal pens are that way. Now scram. (Not enough renown)");
                case "khuzait": return Pick("{=lmmi_hall_deny_khuzait}The lord's hall isn't a stable, nomad. Try the one out back. (Not enough renown)");
                case "battania": return Pick("{=lmmi_hall_deny_battania}No trees to hug in there, Battanian. Off you go. (Not enough renown)");
                case "sturgia": return Pick("{=lmmi_hall_deny_sturgia}The hall's not a mead-house, northerner. Sober up and try again. Or don't. (Not enough renown)");
                case "vlandia": return Pick("{=lmmi_hall_deny_vlandia}A Vlandian wants to see the lord. To swear another oath you'll break? No. (Not enough renown)");
                case "empire": return Pick("{=lmmi_hall_deny_empire}Imperial airs won't open this door, friend. Not anymore. (Not enough renown)");
                case "nord": return Pick("{=lmmi_hall_deny_nord}No longboats to beach in there, Nord. Move along. (Not enough renown)");
                default: return Pick("{=lmmi_hall_deny_generic}We don't know you, stranger, and I don't much like the look of you. No. (Not enough renown)");
            }
        }

        private static TextObject? GateGreeting(Settlement settlement, bool prison)
        {
            TextObject line;
            switch (Tone(settlement))
            {
                case GuardTone.Kin:
                    line = prison ? Pick("{=lmmi_prison_kin}{?PLAYER.GENDER}Kinswoman{?}Kinsman{\\?}. What brings you down to the cells?")
                                  : Pick("{=lmmi_castle_kin}{?PLAYER.GENDER}Kinswoman{?}Kinsman{\\?}. What is it?");
                    break;
                case GuardTone.Contempt:
                    line = prison ? Pick("{=lmmi_prison_scorn}What does a {DEMONYM} want at the dungeon? Visiting relatives?")
                                  : Pick("{=lmmi_castle_scorn}A {DEMONYM} at the keep. What do you want? Make it quick.");
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
