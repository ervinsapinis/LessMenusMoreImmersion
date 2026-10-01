using System;
using HarmonyLib;
using LessMenusMoreImmersion.Logging;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Beaten down, never knocked out: while you fight hired swords in the street, no single blow (nor a fall) can take
    /// the last of your health — you're worn down until <see cref="HiredSwordsBehavior"/> calls the fight at 12%.
    ///
    /// In this game version Agent.MortalityState.Immortal zeroes all damage (Agent.HandleBlow), so it can't be used for
    /// "hurt but not out"; and other mods (MasterStrikes) reset the player's mortality mid-fight anyway. Agent.HandleBlow
    /// is the one place every blow lands (Agent.RegisterBlow → HandleBlow), so the blow is trimmed there.
    ///
    /// Not applied by PatchAll: patching an Agent method while the module loads runs Agent's static initializer
    /// (DefaultTauntActions → ActionIndexCache) before the game has loaded its actions, so every cached action index
    /// is wrong for the session — characters in menus and the encyclopedia come out folded. It's applied at the
    /// first mission instead (<see cref="ApplyLate"/>), when the actions are long loaded.
    /// </summary>
    internal static class HiredBeatDownPatch
    {
        private static bool _applied;

        /// <summary>Patches Agent.HandleBlow once; call when a mission starts, never while the module loads.</summary>
        internal static void ApplyLate(Harmony? harmony)
        {
            if (_applied || harmony == null) return;
            _applied = true;
            try
            {
                var original = AccessTools.Method(typeof(Agent), "HandleBlow");
                var prefix = AccessTools.Method(typeof(HiredBeatDownPatch), nameof(Prefix));
                if (original == null || prefix == null)
                {
                    LmmiLog.Info("HiredBeatDownPatch: Agent.HandleBlow not found — fights can knock you out.");
                    return;
                }
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));
                LmmiLog.Info("HiredBeatDownPatch: Agent.HandleBlow patched (late, at the first mission).");
            }
            catch (Exception ex) { LmmiLog.Error("HiredBeatDownPatch: patching Agent.HandleBlow failed", ex); }
        }

        /// <summary>The agent who can be hurt but not put down (the player, in a fight with hired swords).</summary>
        internal static Agent? Guarded;

        /// <summary>Others who can be hurt but not put down (street brawlers, "bruises, not knockouts"). Whoever adds an agent removes it.</summary>
        internal static readonly System.Collections.Generic.HashSet<Agent> AlsoGuarded = new System.Collections.Generic.HashSet<Agent>();

        private static void Prefix(Agent __instance, ref Blow b)
        {
            if (__instance == null || (!ReferenceEquals(__instance, Guarded) && !AlsoGuarded.Contains(__instance))) return;
            try
            {
                int allowed = (int)Math.Floor(__instance.Health) - 1;
                if (b.InflictedDamage > allowed) b.InflictedDamage = Math.Max(0, allowed);
            }
            catch (Exception ex) { LmmiLog.Error("HiredBeatDownPatch threw", ex); }
        }
    }
}
