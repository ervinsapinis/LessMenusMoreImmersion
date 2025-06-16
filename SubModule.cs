using HarmonyLib;
using LessMenusMoreImmersion.Behaviors;
using LessMenusMoreImmersion.Models;
using System;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion
{
    public class SubModule : MBSubModuleBase
    {
        private Harmony _harmony;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            Harmony.DEBUG = true;                       // forward Harmony’s own internal logs
            _harmony = new Harmony("LessMenusMoreImmersion");
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            var ver = typeof(Harmony).Assembly.GetName().Version;
            InformationManager.DisplayMessage(new InformationMessage($"[LM] Harmony {ver} initialised"));
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);

            if (game.GameType is Campaign)
            {
                var starter = (CampaignGameStarter)gameStarterObject;

                starter.AddBehavior(new DisableMenuBehavior());
                starter.AddBehavior(new CustomVillageMenuBehavior());
                starter.AddBehavior(new CustomRecruitmentMenuBehavior());
                starter.AddModel(new CustomSettlementAccessModel());
            }
        }
    }
}
