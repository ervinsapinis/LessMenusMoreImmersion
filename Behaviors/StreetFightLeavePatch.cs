using System;
using HarmonyLib;
using LessMenusMoreImmersion.Logging;
using SandBox.Missions.MissionLogics;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Vanilla's MissionFightHandler won't let you leave a scene while a fight is on ("Your fight has not ended yet!").
    /// For our street fistfights you may run — it costs you standing in town (StreetEventsBehavior.OnMissionEnded).
    /// Vanilla's own fights (alleys, quests) are untouched.
    /// </summary>
    [HarmonyPatch(typeof(MissionFightHandler), nameof(MissionFightHandler.OnEndMissionRequest))]
    internal static class StreetFightLeavePatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref InquiryData __result, ref bool canPlayerLeave)
        {
            try
            {
                if (!StreetEventsBehavior.StreetFightActive && !HiredSwordsBehavior.FightActive) return;
                canPlayerLeave = true;
                __result = null!;
            }
            catch (Exception ex) { LmmiLog.Error("StreetFightLeavePatch threw", ex); }
        }
    }
}
