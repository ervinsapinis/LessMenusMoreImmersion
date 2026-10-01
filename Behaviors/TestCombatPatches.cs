using System;
using HarmonyLib;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ComponentInterfaces;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Debug suite: your damage multiplier. Patched on the damage model's base CalculateDamage — the one
    /// non-virtual entry point every damage model (vanilla's or another mod's) goes through.
    /// </summary>
    [HarmonyPatch(typeof(AgentApplyDamageModel), nameof(AgentApplyDamageModel.CalculateDamage))]
    internal static class TestDamagePatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref float __result, object[] __args)
        {
            try
            {
                int percent = LmmiSettingsProvider.TestDamagePercent;
                if (percent == 100 || __args == null || __args.Length == 0) return;
                if (__args[0] is AttackInformation info && info.AttackerAgent != null && info.AttackerAgent == Agent.Main)
                    __result *= percent / 100f;
            }
            catch (Exception ex) { LmmiLog.Error("TestDamagePatch threw", ex); }
        }
    }

    /// <summary>Debug suite: keeps the player immortal (hit, never down) while the option is on.</summary>
    public class TestImmortalityWatcher : MissionLogic
    {
        private bool _applied;

        public override void OnMissionTick(float dt)
        {
            try
            {
                var main = Agent.Main;
                if (main == null || !main.IsActive()) { _applied = false; return; }
                if (LmmiSettingsProvider.TestImmortal)
                {
                    if (main.CurrentMortalityState != Agent.MortalityState.Immortal)
                    {
                        main.SetMortalityState(Agent.MortalityState.Immortal);
                        _applied = true;
                    }
                }
                else if (_applied)
                {
                    main.SetMortalityState(Agent.MortalityState.Mortal);
                    _applied = false;
                }
            }
            catch (Exception ex) { LmmiLog.Error("TestImmortalityWatcher threw", ex); }
        }
    }
}
