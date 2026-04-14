using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LessMenusMoreImmersion.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using Helpers;
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
            CampaignEvents.GameMenuOpened.AddNonSerializedListener(this, OnGameMenuOpened);
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
            try
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
                LmmiLog.Debug("CustomVillageMenuBehavior: registered 'trade_custom' option on 'village' menu.");
            }
            catch (Exception ex)
            {
                LmmiLog.Error("CustomVillageMenuBehavior.OnSessionLaunched failed", ex);
            }
        }

        /// <summary>
        /// Condition method for the custom "Buy Products" menu option.
        /// Determines whether the option is enabled or disabled based on access.
        /// </summary>
        /// <param name="args">Menu callback arguments.</param>
        /// <returns>True to display the option; otherwise, false.</returns>
        private bool VillageBuyGoodsCondition(MenuCallbackArgs args)
        {
            try
            {
                var currentSettlement = Settlement.CurrentSettlement;
                if (currentSettlement == null || currentSettlement.Village == null)
                {
                    LmmiLog.Debug("VillageBuyGoodsCondition: no current settlement or not a village.");
                    return false;
                }

                Village village = currentSettlement.Village;

                if (village.VillageState == Village.VillageStates.BeingRaided)
                {
                    return false;
                }

                args.optionLeaveType = GameMenuOption.LeaveType.Trade;

                // Custom access condition
                var behaviorInstance = Campaign.Current.GetCampaignBehavior<DisableMenuBehavior>();
                if (behaviorInstance != null && !behaviorInstance.HasAccessToSettlement(currentSettlement))
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
            catch (Exception ex)
            {
                LmmiLog.Error("VillageBuyGoodsCondition threw", ex);
                return false;
            }
        }

        /// <summary>
        /// Consequence method for the custom "Buy Products" menu option.
        /// Opens the trade screen.
        /// </summary>
        /// <param name="args">Menu callback arguments.</param>
        private void VillageBuyGoodsConsequence(MenuCallbackArgs args)
        {
            try
            {
                var settlement = Settlement.CurrentSettlement;
                if (settlement?.Village == null)
                {
                    LmmiLog.Warning("VillageBuyGoodsConsequence: no current settlement/village; aborting trade screen.");
                    return;
                }

                LmmiLog.Debug($"Opening village trade screen for '{settlement.Name}'.");
                InventoryScreenHelper.OpenScreenAsTrade(
                    settlement.ItemRoster,
                    settlement.Village,
                    InventoryScreenHelper.InventoryCategoryType.None,
                    null
                );
            }
            catch (Exception ex)
            {
                LmmiLog.Error("VillageBuyGoodsConsequence threw while opening trade screen", ex);
            }
        }

        /// <summary>
        /// Removes a menu option from the specified menu context using reflection.
        /// </summary>
        /// <param name="menuContext">The menu context.</param>
        /// <param name="optionId">The ID of the option to remove.</param>
        public static void RemoveMenuOption(MenuContext menuContext, string optionId)
        {
            try
            {
                var gameMenu = menuContext.GameMenu;
                if (gameMenu == null)
                    return;

                // Access the private field '_menuItems' using reflection
                var menuOptionsField = typeof(GameMenu).GetField("_menuItems", BindingFlags.Instance | BindingFlags.NonPublic);
                if (menuOptionsField == null)
                {
                    LmmiLog.Warning("RemoveMenuOption: reflection field 'GameMenu._menuItems' not found — BL API may have changed.");
                    return;
                }

                var menuOptions = menuOptionsField.GetValue(gameMenu) as List<GameMenuOption>;
                if (menuOptions != null)
                {
                    var optionToRemove = menuOptions.FirstOrDefault(o => o.IdString == optionId);
                    if (optionToRemove != null)
                    {
                        menuOptions.Remove(optionToRemove);
                        LmmiLog.Debug($"Removed menu option '{optionId}' from '{gameMenu.StringId}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                LmmiLog.Error($"RemoveMenuOption failed for '{optionId}'", ex);
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
