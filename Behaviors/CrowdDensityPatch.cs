using System;
using HarmonyLib;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using SandBox.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements.Locations;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Busier streets: scales how many common townsfolk and villagers vanilla puts in a town or village centre
    /// (men, women, carriers, children, youths, beggars, dancers — not notables, guards or shopkeepers). Only the
    /// open streets: the tavern, the keep and the other indoor locations keep vanilla's numbers (a tavern at 200%
    /// was wall-to-wall people). Vanilla still caps them by the scene's spawn points and the game's own
    /// civilian-count option, so asking for more than a scene can hold just fills it.
    /// </summary>
    [HarmonyPatch(typeof(Location), nameof(Location.AddLocationCharacters))]
    internal static class CrowdDensityPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Location __instance, CreateLocationCharacterDelegate createDelegate, ref int count)
        {
            try
            {
                int percent = LmmiSettingsProvider.CrowdDensityPercent;
                if (percent == 100 || count <= 0 || createDelegate == null) return;
                var where = __instance?.StringId;
                if (where != "center" && where != "village_center") return;
                var owner = createDelegate.Method?.DeclaringType;
                if (owner != typeof(CommonTownsfolkCampaignBehavior) && owner != typeof(CommonVillagersCampaignBehavior)) return;
                count = Math.Max(0, (int)Math.Round(count * percent / 100f));
            }
            catch (Exception ex) { LmmiLog.Error("CrowdDensityPatch threw", ex); }
        }
    }
}
