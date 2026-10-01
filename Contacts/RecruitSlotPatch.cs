using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LessMenusMoreImmersion.Logging;
using TaleWorlds.CampaignSystem;

namespace LessMenusMoreImmersion.Contacts
{
    /// <summary>
    /// How many of a notable's volunteer slots you can recruit from. Vanilla derives it from relation
    /// (below 0: none), realm (+1) and war (−1/−2); disposition shifts it: Contempt −2, Wary −1, Trusted +1.
    /// Patched on whichever volunteer model is active at runtime, so recruitment overhauls that replace
    /// the model are covered as long as they keep the method.
    /// </summary>
    internal static class RecruitSlotPatch
    {
        private static readonly Harmony Harmony = new Harmony("LessMenusMoreImmersion.RecruitSlots");
        private static readonly HashSet<MethodBase> Patched = new HashSet<MethodBase>();
        private static readonly Dictionary<string, string> LastLogged = new Dictionary<string, string>();

        public static void Apply()
        {
            try
            {
                var model = Campaign.Current?.Models?.VolunteerModel;
                if (model == null) return;

                var target = AccessTools.Method(model.GetType(), "MaximumIndexHeroCanRecruitFromHero",
                    new[] { typeof(Hero), typeof(Hero), typeof(int) });
                if (target == null)
                {
                    LmmiLog.Warning($"RecruitSlotPatch: {model.GetType().FullName} has no MaximumIndexHeroCanRecruitFromHero — recruit slots unaffected.");
                    return;
                }
                if (!Patched.Add(target)) return;

                Harmony.Patch(target, postfix: new HarmonyMethod(typeof(RecruitSlotPatch), nameof(Postfix)));
                LmmiLog.Info($"RecruitSlotPatch: patched {target.DeclaringType?.FullName}.{target.Name}.");
            }
            catch (Exception ex)
            {
                LmmiLog.Error("RecruitSlotPatch: failed to patch the volunteer model", ex);
            }
        }

        // Positional arguments: replacement models may name their parameters differently.
        private static void Postfix(Hero __0, Hero __1, ref int __result)
        {
            try
            {
                if (__0 != Hero.MainHero || __1 == null || !__1.IsNotable) return;
                if (!Settings.LmmiSettingsProvider.EnableNotableDisposition) return;

                int shift;
                var level = NotableDisposition.Get(__1);
                switch (level)
                {
                    case Disposition.Contempt: shift = -2; break;
                    case Disposition.Wary: shift = -1; break;
                    case Disposition.Trusted: shift = 1; break;
                    default: return;
                }
                int vanilla = __result;
                __result = Math.Max(-1, __result + shift);

                var stamp = (int)CampaignTime.Now.ToHours + ":" + vanilla + ">" + __result;
                if (!LastLogged.TryGetValue(__1.StringId, out var last) || last != stamp)
                {
                    LastLogged[__1.StringId] = stamp;
                    LmmiLog.Info($"Recruit slots: {__1.Name} ({level}) vanilla max index {vanilla} -> {__result}.");
                }
            }
            catch (Exception ex)
            {
                LmmiLog.Error("RecruitSlotPatch.Postfix threw", ex);
            }
        }
    }
}
