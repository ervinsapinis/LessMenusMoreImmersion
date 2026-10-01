using System;
using System.Linq;
using HarmonyLib;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using SandBox.View.Map.Navigation;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.TwoDimension;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// "Make camp" on the map bar, between Party and Quests. The bar is data-driven: <c>MapNavigationVM</c> makes one button per
    /// element of <see cref="MapNavigationHandler"/> (the War Sails DLC adds its fleet button the same way), so a button
    /// needs no prefab patch and no UIExtenderEx — only an element, and brush layers for its background and icon
    /// (looked up by its id in <c>MapBar.Left.Button.Backgrounds</c> / <c>MapBar.Left.Icons</c>).
    /// </summary>
    internal sealed class CampNavigationElement : MapNavigationElementBase
    {
        public const string Id = "lmmi_camp";

        public CampNavigationElement(MapNavigationHandler handler) : base(handler) { }

        public override string StringId => Id;
        public override bool IsActive => false;
        public override bool IsLockingNavigation => false;
        public override bool HasAlert => false;

        protected override NavigationPermissionItem GetPermission()
        {
            if (!LmmiSettingsProvider.EnableCamp)
                return new NavigationPermissionItem(false, new TextObject("{=lmmi_camp_bar_off}Making camp is turned off in the mod's options."));
            if (!MapNavigationHelper.IsNavigationBarEnabled(_handler)) return new NavigationPermissionItem(false, null);
            if (!(_game?.GameStateManager?.ActiveState is MapState)) return new NavigationPermissionItem(false, null);
            return CampBehavior.CanCampNow(out var why) ? new NavigationPermissionItem(true, null) : new NavigationPermissionItem(false, why);
        }

        protected override TextObject GetTooltip()
        {
            var text = new TextObject("{=lmmi_camp_bar_hint}Make camp");
            var line = GameTexts.FindText("str_hotkey_with_hint");
            line.SetTextVariable("TEXT", text.ToString());
            line.SetTextVariable("HOTKEY", "Ctrl+T");
            return line;
        }

        protected override TextObject GetAlertTooltip() => TextObject.GetEmpty();

        public override void OpenView()
        {
            if (Permission.IsAuthorized) CampBehavior.TryMakeCamp();
        }

        public override void OpenView(params object[] parameters) => OpenView();

        public override void GoToLink() { }
    }

    /// <summary>
    /// Adds the camp to the map bar's elements (vanilla's handler and the DLC's, which builds on it), as a regular slot
    /// right after Party (and after the DLC's fleet button, if it sits there) so the Kingdom button keeps the bar's end cap.
    /// </summary>
    [HarmonyPatch(typeof(MapNavigationHandler), "OnCreateElements")]
    internal static class CampMapBarPatch
    {
        [HarmonyPostfix]
        private static void Postfix(MapNavigationHandler __instance, ref INavigationElement[] __result)
        {
            try
            {
                if (!LmmiSettingsProvider.EnableCamp || __result == null || Campaign.Current == null) return;
                if (__result.Any(e => e?.StringId == CampNavigationElement.Id)) return;
                CampMapBarIcon.Ensure();
                var list = __result.ToList();
                int at = list.FindIndex(e => e?.StringId == "party");
                if (at >= 0)
                {
                    at++;
                    if (at < list.Count && list[at]?.StringId == "manage_fleet") at++;
                }
                else
                {
                    at = list.FindIndex(e => e?.StringId == "kingdom");
                    if (at < 0) at = list.Count;
                }
                list.Insert(at, new CampNavigationElement(__instance));
                __result = list.ToArray();
                LmmiLog.Info("Camp: 'Make camp' added to the map bar at slot " + at + ".");
            }
            catch (Exception ex) { LmmiLog.Error("Camp: adding the map bar button threw", ex); }
        }
    }

    /// <summary>
    /// The button's look: the vanilla backgrounds/icon brushes get a layer named after the element, copied from a regular
    /// middle slot (the Party background — the one the DLC's fleet button reuses — and the Inventory icon layer's
    /// placement), with a vanilla game-menu sprite as the icon (from an always-loaded sprite category; nothing is created
    /// natively at runtime). Idempotent; cheap enough to call every frame on the map.
    /// </summary>
    internal static class CampMapBarIcon
    {
        private const string BackgroundTemplate = "party";
        private const string IconTemplate = "inventory";
        private const string IconSprite = @"SPGeneral\GameMenu\wait_icon";
        private const string FallbackSprite = @"General\Icons\Morale@2x";
        private static bool _loggedFailure;

        public static void Ensure()
        {
            try
            {
                var brushes = UIResourceManager.BrushFactory;
                if (brushes == null) return;
                Layer(brushes.GetBrush("MapBar.Left.Button.Backgrounds"), BackgroundTemplate, null);
                Layer(brushes.GetBrush("MapBar.Left.Icons"), IconTemplate, IconSprite);
            }
            catch (Exception ex)
            {
                if (_loggedFailure) return;
                _loggedFailure = true;
                LmmiLog.Error("Camp: the map bar icon threw", ex);
            }
        }

        private static void Layer(Brush? brush, string template, string? spriteName)
        {
            if (brush == null || brush.GetLayer(CampNavigationElement.Id) != null) return;
            var source = brush.GetLayer(template);
            if (source == null) return;
            var layer = new BrushLayer();
            layer.FillFrom(source);
            layer.Name = CampNavigationElement.Id;
            if (spriteName != null)
            {
                var sprite = UIResourceManager.SpriteData?.GetSprite(spriteName) ?? UIResourceManager.SpriteData?.GetSprite(FallbackSprite);
                if (sprite != null) layer.Sprite = sprite;
            }
            brush.AddLayer(layer);
        }
    }
}
