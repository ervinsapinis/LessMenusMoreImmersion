using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LessMenusMoreImmersion.Constants;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Overlay;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using static TaleWorlds.CampaignSystem.Inventory.InventoryManager;

namespace LessMenusMoreImmersion.Behaviors
{
    public class DisableMenuBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.LocationCharactersAreReadyToSpawnEvent.AddNonSerializedListener(this, LocationCharactersAreReadyToSpawn);
        }

        private void LocationCharactersAreReadyToSpawn(Dictionary<string, int> dictionary)
        {
            Location location = Settlement.CurrentSettlement.LocationComplex.GetLocationWithId("village_center");
            Settlement settlement = PlayerEncounter.LocationEncounter.Settlement;

            if (settlement != null && settlement.IsVillage)
            {
                CharacterObject villageTrader = settlement.Culture.Merchant;
                Monster monsterWithSuffix = FaceGen.GetMonsterWithSuffix(villageTrader.Race, "_settlement");

                LocationCharacter locationCharacter = new LocationCharacter(
                    new AgentData(new SimpleAgentOrigin(villageTrader, -1, null, default))
                        .Monster(monsterWithSuffix)
                        .Age(MBRandom.RandomInt(56, 90)),
                    SandBoxManager.Instance.AgentBehaviorManager.AddWandererBehaviors,
                    "sp_rural_notable_notary", true, LocationCharacter.CharacterRelations.Neutral, null, true, false, null, false, false, true
                );

                location.AddCharacter(locationCharacter);
            }
        }

        protected void AddVillageTraderDialogs(CampaignGameStarter campaignGameStarter)
        {
            bool isConversationWithVillageMerchant() =>
                MobileParty.MainParty.CurrentSettlement != null &&
                MobileParty.MainParty.CurrentSettlement.IsVillage &&
                CharacterObject.OneToOneConversationCharacter == MobileParty.MainParty.CurrentSettlement.Culture.Merchant;

            campaignGameStarter.AddDialogLine("village_trader_talk", "start", "village_trader",
                "Hail {?PLAYER.GENDER}m'lady{?}m'lord{\\?}, I bid thee welcome to our humble hamlet. I organize the trade here. Be thee interested in haggling?",
                isConversationWithVillageMerchant,
                null,
                100, null);

            campaignGameStarter.AddPlayerLine("village_trader_trade", "village_trader", "village_trader_start_trade",
                "Indeed, let's have a look.", null, () =>
                {
                    BeginTradeWithVillageTrader();
                });

            campaignGameStarter.AddDialogLine("village_trader_start_trade", "village_trader_start_trade", "village_trader_options",
                "Would {?PLAYER.GENDER}m'lady{?}m'lord{\\?} like to barter further?", null, null);

            campaignGameStarter.AddPlayerLine("village_trader_end_conversation", "village_trader", "village_trader_end",
                "Not at this time. Take care.", null, null);

            campaignGameStarter.AddPlayerLine("village_trader_end_conversation", "village_trader_options", "village_trader_end",
                "No. Thank you for your time.", null, null);

            campaignGameStarter.AddPlayerLine("village_trader_trade", "village_trader_options", "village_trader_start_trade",
                "Yes, I am not done yet.", null, () =>
                {
                    BeginTradeWithVillageTrader();
                });

            campaignGameStarter.AddDialogLine("village_trader_end", "village_trader_end", "close_window",
                "Fare thee well.", null, null);
        }

        private void BeginTradeWithVillageTrader()
        {
            var settlementComponent = Settlement.CurrentSettlement.SettlementComponent;
            if (settlementComponent != null)
            {
                OpenScreenAsTrade(Settlement.CurrentSettlement.ItemRoster, settlementComponent, InventoryCategoryType.None, null);
                Settlement? settlement = ConversationSentence.CurrentProcessedRepeatObject as Settlement;
                if (settlement != null)
                {
                    ConversationSentence.SelectedRepeatLine.SetTextVariable("SETTLEMENT_NAME", settlement.Name);
                }
            }
        }

        public static class HarmonyPatches
        {
            private static Harmony? harmonyInstance;

            public static void ApplyPatches()
            {
                if (harmonyInstance == null)
                {
                    harmonyInstance = new Harmony("LessMenusMoreImmersion");
                    harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
                }
            }
        }

        [HarmonyPatch(typeof(GameMenu), "AddOption", new Type[] { typeof(string), typeof(TextObject), typeof(GameMenuOption.OnConditionDelegate), typeof(GameMenuOption.OnConsequenceDelegate), typeof(int), typeof(bool), typeof(bool), typeof(object) })]
        public static class DisableSpecificMenuOptionsPatch
        {
            [HarmonyPrefix]
            public static void Prefix(ref GameMenuOption.OnConditionDelegate condition, string optionId)
            {
                if (SettlementMenuOptions.AllOptions.Contains(optionId))
                {
                    condition = (MenuCallbackArgs args) =>
                    {
                        args.IsEnabled = false;
                        return false;
                    };
                }
            }
        }


        private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
        {
            HarmonyPatches.ApplyPatches();
            InformationManager.DisplayMessage(new InformationMessage("Less menus more immersion loaded successfuly."));
            AddVillageTraderDialogs(campaignGameStarter);
        }

        public override void SyncData(IDataStore dataStore) { }
    }
}
