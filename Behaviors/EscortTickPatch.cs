using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using SandBox.Missions.AgentBehaviors;
using LessMenusMoreImmersion.Logging;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Patches the vanilla EscortAgentBehavior.Tick() which has a bug: it aborts
    /// (_state = NotEscorting) when _targetAgent is null, even when _targetPosition
    /// or _targetMachine is set. This patch fixes that for position-based escorts.
    /// Vanilla agent-to-agent escorts are unaffected (targetAgent != null path).
    /// </summary>
    internal static class EscortTickPatch
    {
        private static readonly FieldInfo FEscortedAgent = AccessTools.Field(typeof(EscortAgentBehavior), "_escortedAgent");
        private static readonly FieldInfo FTargetAgent = AccessTools.Field(typeof(EscortAgentBehavior), "_targetAgent");
        private static readonly FieldInfo FTargetPosition = AccessTools.Field(typeof(EscortAgentBehavior), "_targetPosition");
        private static readonly FieldInfo FTargetMachine = AccessTools.Field(typeof(EscortAgentBehavior), "_targetMachine");
        private static readonly FieldInfo FState = AccessTools.Field(typeof(EscortAgentBehavior), "_state");
        private static readonly MethodInfo MControlMovement = AccessTools.Method(typeof(EscortAgentBehavior), "ControlMovement");

        [HarmonyPatch(typeof(EscortAgentBehavior), "Tick")]
        private static class TickPrefix
        {
            [HarmonyPrefix]
            public static bool Prefix(EscortAgentBehavior __instance)
            {
                try
                {
                    Agent targetAgent = (Agent)FTargetAgent.GetValue(__instance);

                    // If targetAgent is set, let vanilla Tick handle it normally (agent-to-agent escort)
                    if (targetAgent != null)
                        return true;

                    // Position-based or machine-based escort — fix the broken null check
                    Vec3? targetPos = (Vec3?)FTargetPosition.GetValue(__instance);
                    UsableMachine targetMach = (UsableMachine)FTargetMachine.GetValue(__instance);

                    // No target at all — let vanilla abort
                    if (targetPos == null && targetMach == null)
                        return true;

                    Agent escorted = (Agent)FEscortedAgent.GetValue(__instance);

                    // Null escorted agent — go to NotEscorting state
                    if (escorted == null || !escorted.IsActive())
                    {
                        FState.SetValue(__instance, 0); // State.NotEscorting
                        return false;
                    }

                    int state = (int)FState.GetValue(__instance);
                    if (state != 0) // State.NotEscorting
                    {
                        MControlMovement.Invoke(__instance, null);
                    }
                    return false; // Skip original Tick
                }
                catch (Exception ex)
                {
                    LmmiLog.Error("EscortTickPatch.TickPrefix exception", ex);
                    return true; // Fall through to vanilla on error
                }
            }
        }
    }
}
