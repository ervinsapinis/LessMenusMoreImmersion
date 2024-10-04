using LessMenusMoreImmersion.Behaviors;
using LessMenusMoreImmersion.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;


namespace LessMenusMoreImmersion
{
    public class SubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            InformationManager.DisplayMessage(new InformationMessage("LessMenusMoreImmersion loaded!"));
        }

        protected override void OnSubModuleUnloaded()
        {
            base.OnSubModuleUnloaded();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);

            if (game.GameType is Campaign)
            {
                CampaignGameStarter campaignGameStarter = gameStarterObject as CampaignGameStarter;
                if (campaignGameStarter != null)
                {
                    // Add DisableMenuBehavior first
                    campaignGameStarter.AddBehavior(new DisableMenuBehavior());

                    campaignGameStarter.AddBehavior(new CustomVillageMenuBehavior());

                    // Then add the CustomSettlementAccessModel
                    campaignGameStarter.AddModel(new CustomSettlementAccessModel());
                }
            }
        }

    }
}