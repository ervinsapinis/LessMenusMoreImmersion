using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Behaviors
{
    public class CustomVillageMenuBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.AfterGameMenuOpenedEvent.AddNonSerializedListener(this, OnGameMenuOpened);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // No data to sync
        }

        private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
        {
            campaignGameStarter.AddGameMenuOption(
                "village",            
                "trade_custom",        
                "{=VN4ctHIU}Buy products",
                VillageBuyGoodsCondition,
                VillageBuyGoodsConsequence,
                false,
                -1,
                false,
                null
            );
        }

        private bool VillageBuyGoodsCondition(MenuCallbackArgs args)
        {
            Village village = Settlement.CurrentSettlement.Village;

            if (village.VillageState == Village.VillageStates.BeingRaided)
            {
                return false;
            }

            args.optionLeaveType = GameMenuOption.LeaveType.Trade;

            var behaviorInstance = Campaign.Current.GetCampaignBehavior<DisableMenuBehavior>();
            if (behaviorInstance != null && !behaviorInstance.HasAccessToSettlement(Settlement.CurrentSettlement))
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject("The trader moves around, maybe you could arrange regular trading with this village.");
                return true;
            }

            if (village.VillageState == Village.VillageStates.Normal && village.Owner.ItemRoster.Count > 0)
            {
                return true;
            }

            if (village.Gold > 0)
            {
                args.Tooltip = new TextObject("{=FbowXAC0}There are no available products right now.");
                return true;
            }

            args.IsEnabled = false;
            args.Tooltip = new TextObject("{=bmfo7CaO}Village shop is not available right now.");
            return true;
        }

        private void VillageBuyGoodsConsequence(MenuCallbackArgs args)
        {
            InventoryManager.OpenScreenAsTrade(
                Settlement.CurrentSettlement.ItemRoster,
                Settlement.CurrentSettlement.Village,
                InventoryManager.InventoryCategoryType.None,
                null
            );
        }

        public static void RemoveMenuOption(MenuContext menuContext, string optionId)
        {
            var gameMenu = menuContext.GameMenu;
            if (gameMenu == null)
                return;

            var menuOptionsField = typeof(GameMenu).GetField("_menuItems", BindingFlags.Instance | BindingFlags.NonPublic);
            if (menuOptionsField == null)
            {
                InformationManager.DisplayMessage(new InformationMessage("menuOptionsField is null."));
                return;
            }

            var menuOptions = menuOptionsField.GetValue(gameMenu) as List<GameMenuOption>;
            if (menuOptions != null)
            {
                var optionToRemove = menuOptions.FirstOrDefault(o => o.IdString == optionId);
                if (optionToRemove != null)
                {
                    menuOptions.Remove(optionToRemove);
                }
            }
        }

        private void OnGameMenuOpened(MenuCallbackArgs args)
        {
            if (args.MenuContext.GameMenu.StringId == "village")
            {
                RemoveMenuOption(args.MenuContext, "trade");
            }
        }
    }
}
