using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LessMenusMoreImmersion.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Shopkeepers treat you as the town speaks of you. In a settlement's market (the trade screen) you buy cheaper and
    /// sell dearer the better your name there — honored 12%, respected 8%, known 4% — and the other way round when
    /// you're disliked (8%) or despised (15%).
    ///
    /// Applied to whatever trade price model the campaign actually runs (another mod may replace vanilla's), on its
    /// GetPrice — the call the trade screen makes for every number it shows (InventoryLogic.GetItemPrice →
    /// IMarketData.GetPrice → TradeItemPriceFactorModel.GetPrice). Patched once the models are final, from
    /// <see cref="TavernLifeBehavior"/>'s session launch.
    ///
    /// A good name never makes a market pay you more for a thing than it would charge you for it: where the shop's own
    /// margin is thin (trade goods), the discount shrinks so that buying and selling back straight away always loses at
    /// least 4%.
    /// </summary>
    internal static class StandingPricePatch
    {
        private const float MinSpread = 1.04f;

        private static readonly HashSet<MethodBase> Patched = new HashSet<MethodBase>();
        private static Harmony? _harmony;
        [ThreadStatic] private static bool _busy;

        /// <summary>How much better (positive) or worse (negative) the market treats you here.</summary>
        internal static float Edge(StandingBand band) => band switch
        {
            StandingBand.Honored => 0.12f,
            StandingBand.Respected => 0.08f,
            StandingBand.Known => 0.04f,
            StandingBand.Disliked => -0.08f,
            StandingBand.Despised => -0.15f,
            _ => 0f,
        };

        /// <summary>Patch the campaign's trade price model (idempotent; call once the campaign's models are set).</summary>
        internal static void Apply()
        {
            try
            {
                var model = Campaign.Current?.Models?.TradeItemPriceFactorModel;
                if (model == null) { LmmiLog.Warning("Standing prices: no trade price model to patch."); return; }
                var type = model.GetType();
                var method = AccessTools.Method(type, nameof(TradeItemPriceFactorModel.GetPrice), new[]
                {
                    typeof(EquipmentElement), typeof(MobileParty), typeof(PartyBase), typeof(bool), typeof(float), typeof(float), typeof(float),
                });
                if (method == null || method.IsAbstract)
                {
                    LmmiLog.Warning($"Standing prices: {type.FullName} has no GetPrice to patch — town standing won't touch prices.");
                    return;
                }
                if (Patched.Contains(method)) return;
                _harmony ??= new Harmony("LessMenusMoreImmersion.StandingPrices");
                _harmony.Patch(method, postfix: new HarmonyMethod(typeof(StandingPricePatch), nameof(Postfix)));
                Patched.Add(method);
                LmmiLog.Info($"Standing prices: patched {method.DeclaringType?.FullName}.GetPrice (the campaign's model is {type.FullName}).");
            }
            catch (Exception ex) { LmmiLog.Error("Standing prices: patching the trade price model failed", ex); }
        }

        // __0..__6 by position: another mod's override may name its parameters differently.
        private static void Postfix(TradeItemPriceFactorModel __instance, EquipmentElement __0, MobileParty __1, PartyBase __2, bool __3,
            float __4, float __5, float __6, ref int __result)
        {
            if (_busy) return;
            try
            {
                if (__1 == null || __1 != MobileParty.MainParty || __2 == null || !__2.IsSettlement) return;
                float edge = Edge(TownStandingBehavior.Band(__2.Settlement));
                if (edge == 0f) return;
                bool selling = __3;

                if (edge > 0f)
                {
                    // Never better than a minimum spread between what they'd charge and what they'd pay.
                    int other;
                    _busy = true;
                    try { other = __instance.GetPrice(__0, __1, __2, !selling, __4, __5, __6); }
                    finally { _busy = false; }
                    float buy = selling ? other : __result;
                    float sell = selling ? __result : other;
                    if (sell > 0f && buy > 0f)
                    {
                        float k = MinSpread * sell / buy;
                        float max = k >= 1f ? 0f : (1f - k) / (1f + k);
                        edge = Math.Min(edge, max);
                    }
                    if (edge <= 0f) return;
                }

                int price = selling
                    ? (int)Math.Floor(__result * (1f + edge))
                    : (int)Math.Ceiling(__result * (1f - edge));
                __result = Math.Max(1, price);
            }
            catch (Exception ex) { LmmiLog.Error("StandingPricePatch threw", ex); }
        }
    }
}
