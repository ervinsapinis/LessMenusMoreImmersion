using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LessMenusMoreImmersion.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Vanilla lets a prisoner go the moment their captors aren't at war with them (unless their crime there is
    /// moderate or worse — and always, in their own kingdom). A street sentence overrides only that: while it's being
    /// served, vanilla's captivity check still runs — the chance to escape, the jailer's ransom offers — but its "not
    /// at war, so they let you go" release is held off. When the time is up our own menu lets you out, or brings you to
    /// the headsman (StreetEventsBehavior.CaptivityCheck); escape or ransom through vanilla ends the sentence.
    /// <para>
    /// How: the check's one call to FactionManager.IsAtWarAgainstFaction is rerouted to <see cref="AtWarOrHeld"/>, which
    /// answers "at war" while a sentence holds you. That covers the crime-rating branch and the own-kingdom branch alike,
    /// whatever crime model other mods install. If the call can't be found (a game update), the guard isn't installed
    /// and the prefix falls back to skipping vanilla's check while the sentence runs (no escape, no ransom).
    /// </para>
    /// </summary>
    [HarmonyPatch(typeof(PlayerCaptivityCampaignBehavior), nameof(PlayerCaptivityCampaignBehavior.CheckCaptivityChange))]
    internal static class JailSentencePatch
    {
        /// <summary>The release guard was woven into vanilla's check.</summary>
        internal static bool ReleaseGuardInstalled { get; private set; }

        [HarmonyPrefix]
        private static bool Prefix()
        {
            try { return StreetEventsBehavior.CaptivityCheck(ReleaseGuardInstalled); }
            catch (Exception ex)
            {
                LmmiLog.Error("JailSentencePatch threw", ex);
                return true;
            }
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            try
            {
                var atWar = AccessTools.Method(typeof(FactionManager), nameof(FactionManager.IsAtWarAgainstFaction), new[] { typeof(IFaction), typeof(IFaction) });
                var guard = AccessTools.Method(typeof(JailSentencePatch), nameof(AtWarOrHeld));
                int replaced = 0;
                if (atWar != null && guard != null)
                {
                    foreach (var ci in list)
                    {
                        if (!ci.Calls(atWar)) continue;
                        ci.opcode = System.Reflection.Emit.OpCodes.Call;
                        ci.operand = guard;
                        replaced++;
                    }
                }
                ReleaseGuardInstalled = replaced > 0;
                if (replaced == 1) LmmiLog.Info("JailSentencePatch: vanilla's 'not at war, let them go' release is guarded while a street sentence runs.");
                else if (replaced == 0) LmmiLog.Info("JailSentencePatch: couldn't find vanilla's at-war check — a sentence will skip vanilla's captivity check instead (no escape or ransom while it runs).");
                else LmmiLog.Info($"JailSentencePatch: guarded {replaced} at-war checks in vanilla's captivity check (expected 1).");
            }
            catch (Exception ex)
            {
                ReleaseGuardInstalled = false;
                LmmiLog.Error("JailSentencePatch: transpiler threw — falling back to skipping vanilla's check during a sentence", ex);
            }
            return list;
        }

        /// <summary>Vanilla's question, "are the captors at war with you?" — yes, as far as release goes, while a sentence holds you.</summary>
        public static bool AtWarOrHeld(IFaction faction1, IFaction faction2)
        {
            if (FactionManager.IsAtWarAgainstFaction(faction1, faction2)) return true;
            try { return StreetEventsBehavior.HoldsSentence(); }
            catch (Exception ex)
            {
                LmmiLog.Error("JailSentencePatch: HoldsSentence threw", ex);
                return false;
            }
        }
    }
}
