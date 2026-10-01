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
                Harmony.DEBUG = true;
                _harmony = new Harmony("LessMenusMoreImmersion");

                InformationManager.DisplayMessage(new InformationMessage("[LMMI] Applying Harmony patches..."));
                LmmiLog.Info("Applying Harmony patches...");

                _harmony.PatchAll(Assembly.GetExecutingAssembly());

                var ver = typeof(Harmony).Assembly.GetName().Version;
                var patchedMethods = _harmony.GetPatchedMethods();
                int patchCount = 0;
                foreach (var m in patchedMethods) patchCount++;

                InformationManager.DisplayMessage(new InformationMessage($"[LMMI] Harmony {ver} — {patchCount} methods patched"));
                LmmiLog.Info($"Harmony {ver} initialized — {patchCount} methods patched.");

                foreach (var m in _harmony.GetPatchedMethods())
                {
                    LmmiLog.Info($"  Patched: {m.DeclaringType?.FullName}.{m.Name}");
                    InformationManager.DisplayMessage(new InformationMessage($"[LMMI] Patched: {m.DeclaringType?.Name}.{m.Name}"));
                }
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

        /// <summary>Every frame, paused or not: the hired swords' menus and judgment.</summary>
        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            if (Campaign.Current != null)
            {
                HiredSwordsBehavior.OnApplicationTick();
                CampBehavior.OnApplicationTick();
            }
        }

        /// <summary>Campaign missions get the street events' hit watcher (wading into a scene with your fists).</summary>
        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            HiredBeatDownPatch.ApplyLate(_harmony);
            try
            {
                if (Campaign.Current != null) mission.AddMissionBehavior(new StreetHitWatcher());
                mission.AddMissionBehavior(new TestImmortalityWatcher());
            }
            catch (Exception ex) { LmmiLog.Error("SubModule: adding StreetHitWatcher failed", ex); }
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
                    starter.AddBehavior(new ContactsBehavior());
                    starter.AddBehavior(new ArrivalScenesBehavior());
                    starter.AddBehavior(new TownStandingBehavior());
                    starter.AddBehavior(new DebtsBehavior());
                    starter.AddBehavior(new AmbushBehavior());
                    starter.AddBehavior(new HiredSwordsBehavior());
                    starter.AddBehavior(new LettersBehavior());
                    starter.AddBehavior(new TestModeBehavior());
                    starter.AddBehavior(new QuestPersuasionBehavior());
                    starter.AddBehavior(new GuardDialogsBehavior());
                    starter.AddBehavior(new StreetEventsBehavior());
                    starter.AddBehavior(new FeastBehavior());
                    starter.AddBehavior(new AmbientStreetLifeBehavior());
                    starter.AddBehavior(new CastleLifeBehavior());
                    starter.AddBehavior(new HallCourtBehavior());
                    starter.AddBehavior(new CampBehavior());
                    starter.AddBehavior(new LordsBehavior());
                    starter.AddBehavior(new TavernLifeBehavior());

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
