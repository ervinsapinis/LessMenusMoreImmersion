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
    /// <summary>
    /// Custom behavior to modify village menus, replacing the "Buy Products" option with a custom version.
    /// </summary>
    public class CustomVillageMenuBehavior : CampaignBehaviorBase
    {
        /// <summary>
        /// Registers campaign events for this behavior.
        /// </summary>
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.AfterGameMenuOpenedEvent.AddNonSerializedListener(this, OnGameMenuOpened);
        }

        /// <summary>
        /// Synchronizes data when saving or loading the game.
        /// </summary>
        public override void SyncData(IDataStore dataStore)
        {
            // No data to sync
        }

        /// <summary>
        /// Called when the campaign session is launched. Adds custom menu options.
        /// </summary>
        /// <param name="campaignGameStarter">The campaign game starter.</param>
        private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
        {
            // Add a custom "Buy Products" option to the village menu
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

        /// <summary>
        /// Condition method for the custom "Buy Products" menu option.
        /// Determines whether the option is enabled or disabled based on access.
        /// </summary>
        /// <param name="args">Menu callback arguments.</param>
        /// <returns>True to display the option; otherwise, false.</returns>
        private bool VillageBuyGoodsCondition(MenuCallbackArgs args)
        {
            Village village = Settlement.CurrentSettlement.Village;

            if (village.VillageState == Village.VillageStates.BeingRaided)
            {
                return false;
            }

            args.optionLeaveType = GameMenuOption.LeaveType.Trade;

            // Custom access condition
            var behaviorInstance = Campaign.Current.GetCampaignBehavior<DisableMenuBehavior>();
            if (behaviorInstance != null && !behaviorInstance.HasAccessToSettlement(Settlement.CurrentSettlement))
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject("{=K9l0M1n2O}The trader moves around, maybe you could arrange regular trading with this village.");
                return true; // Display disabled option
            }

            // Original conditions to enable the option
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

        /// <summary>
        /// Consequence method for the custom "Buy Products" menu option.
        /// Opens the trade screen.
        /// </summary>
        /// <param name="args">Menu callback arguments.</param>
        private void VillageBuyGoodsConsequence(MenuCallbackArgs args)
        {
            InventoryManager.OpenScreenAsTrade(
                Settlement.CurrentSettlement.ItemRoster,
                Settlement.CurrentSettlement.Village,
                InventoryManager.InventoryCategoryType.None,
                null
            );
        }

        /// <summary>
        /// Removes a menu option from the specified menu context using reflection.
        /// </summary>
        /// <param name="menuContext">The menu context.</param>
        /// <param name="optionId">The ID of the option to remove.</param>
        public static void RemoveMenuOption(MenuContext menuContext, string optionId)
        {
            var gameMenu = menuContext.GameMenu;
            if (gameMenu == null)
                return;

            // Access the private field '_menuItems' using reflection
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

        /// <summary>
        /// Event handler called after a game menu is opened. Removes the original "Buy Products" option.
        /// </summary>
        /// <param name="args">Menu callback arguments.</param>
        private void OnGameMenuOpened(MenuCallbackArgs args)
        {
            if (args.MenuContext.GameMenu.StringId == "village")
            {
                RemoveMenuOption(args.MenuContext, "trade");
            }
        }
    }
}
