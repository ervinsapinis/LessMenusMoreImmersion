using HarmonyLib;
using LessMenusMoreImmersion.Behaviors;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Models;
using LessMenusMoreImmersion.Settings;
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
        private Harmony? _harmony;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            // Initialize the file logger first so everything that follows can be traced.
            LmmiLog.Initialize();
            LmmiSettingsProvider.AttachVerboseQueryToLogger();
            LmmiLog.Info($"SubModule loading (Version {GetModVersion()}).");

            try
            {
                Harmony.DEBUG = false;
                _harmony = new Harmony("LessMenusMoreImmersion");
                _harmony.PatchAll(Assembly.GetExecutingAssembly());

                var ver = typeof(Harmony).Assembly.GetName().Version;
                InformationManager.DisplayMessage(new InformationMessage($"[LM] Harmony {ver} initialised"));
                LmmiLog.Info($"Harmony {ver} initialized and patches applied.");
            }
            catch (Exception ex)
            {
                LmmiLog.Error("Failed to apply Harmony patches", ex);
                InformationManager.DisplayMessage(new InformationMessage("[LMMI] Failed to apply patches — see lmmi.log"));
            }
        }

        /// <summary>
        /// Called after every module has been loaded but before the main menu appears.
        /// This is the correct time to poke MCM: all mods (including MCMv5 itself, if present)
        /// are now in the AppDomain, so the availability check can give a definitive answer.
        /// </summary>
        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            LmmiSettingsProvider.TryRegisterMcm();
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);

            if (game.GameType is Campaign)
            {
                try
                {
                    var starter = (CampaignGameStarter)gameStarterObject;

                    starter.AddBehavior(new DisableMenuBehavior());
                    starter.AddBehavior(new CustomVillageMenuBehavior());
                    starter.AddBehavior(new CustomRecruitmentMenuBehavior());
                    starter.AddModel(new CustomSettlementAccessModel());
                    starter.AddBehavior(new AltOverlayBlockerBehavior());

                    LmmiLog.Info("Campaign behaviors and models registered.");
                }
                catch (Exception ex)
                {
                    LmmiLog.Error("Failed to register campaign behaviors/models", ex);
                }
            }
        }

        private static string GetModVersion()
        {
            try
            {
                return typeof(SubModule).Assembly.GetName().Version?.ToString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
