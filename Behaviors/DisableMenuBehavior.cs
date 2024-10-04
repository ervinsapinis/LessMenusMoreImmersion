using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LessMenusMoreImmersion.Constants;
using SandBox.Conversation;
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
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using static TaleWorlds.CampaignSystem.Inventory.InventoryManager;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using Messages.FromClient.ToLobbyServer;

namespace LessMenusMoreImmersion.Behaviors
{
    public class DisableMenuBehavior : CampaignBehaviorBase
    {
        private static Dictionary<string, bool> settlementsWithAccess = new Dictionary<string, bool>();
        private CharacterObject? _localGuide;

        public override void RegisterEvents()
        {
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnGameStarted);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameStarted);
        }

        private void OnGameStarted(CampaignGameStarter campaignGameStarter)
        {
            InformationManager.DisplayMessage(new InformationMessage("Less menus more immersion loaded successfully."));
            AddVillageTraderDialogs(campaignGameStarter);
            AddGuideDialogs(campaignGameStarter);
            CampaignEvents.LocationCharactersAreReadyToSpawnEvent.AddNonSerializedListener(this, LocationCharactersAreReadyToSpawn);
            _localGuide = MBObjectManager.Instance.GetObject<CharacterObject>("local_guide");
        }

        private void LocationCharactersAreReadyToSpawn(Dictionary<string, int> dictionary)
        {
            if (Campaign.Current == null || Game.Current == null)
                return; // Ensure campaign is initialized

            var settlement = PlayerEncounter.LocationEncounter?.Settlement ?? Settlement.CurrentSettlement;

            if (settlement != null)
            {
                if (settlement.IsVillage)
                {
                    AddVillageTraderToLocation(settlement);
                }
                else if (CampaignMission.Current.Location.StringId == "tavern")
                {
                    AddGuideToTavern(settlement);
                }
            }
        }

        private void AddVillageTraderToLocation(Settlement settlement)
        {
            if (settlement?.Culture?.Merchant == null)
                return;

            Location location = settlement.LocationComplex?.GetLocationWithId("village_center");
            if (location == null)
                return;

            CharacterObject villageTrader = settlement.Culture.Merchant;
            Monster monsterWithSuffix = TaleWorlds.Core.FaceGen.GetMonsterWithSuffix(villageTrader.Race, "_settlement");

            if (monsterWithSuffix == null)
                return;

            LocationCharacter locationCharacter = new LocationCharacter(
                new AgentData(new SimpleAgentOrigin(villageTrader, -1, null, default))
                    .Monster(monsterWithSuffix)
                    .Age(MBRandom.RandomInt(56, 90)),
                SandBoxManager.Instance.AgentBehaviorManager.AddWandererBehaviors,
                "sp_rural_notable_notary", true, LocationCharacter.CharacterRelations.Neutral, null, true, false, null, false, false, true
            );

            location.AddCharacter(locationCharacter);
        }

        private void AddGuideToTavern(Settlement settlement)
        {
            Location tavernLocation = settlement.LocationComplex?.GetLocationWithId("tavern");
            if (tavernLocation == null)
                return;

            Monster monsterWithSuffix = TaleWorlds.Core.FaceGen.GetMonsterWithSuffix(_localGuide.Race, "_settlement");

            if (monsterWithSuffix == null)
                return;

            var agentData = new AgentData(
              new SimpleAgentOrigin(_localGuide, -1, null, default))
                  .Monster(monsterWithSuffix)
                  .Age(30);

            // Create the location character for the guide
            LocationCharacter guideLocationCharacter = new LocationCharacter(
                agentData,
                SandBoxManager.Instance.AgentBehaviorManager.AddWandererBehaviors,
                "tavernkeeper",
                true,
                LocationCharacter.CharacterRelations.Neutral,
                null,
                true,
                false,
                null,
                false,
                false,
                true
            );

            tavernLocation.AddCharacter(guideLocationCharacter);
        }

        protected void AddVillageTraderDialogs(CampaignGameStarter campaignGameStarter)
        {
            bool isConversationWithVillageMerchant() =>
                MobileParty.MainParty.CurrentSettlement != null &&
                MobileParty.MainParty.CurrentSettlement.IsVillage &&
                CharacterObject.OneToOneConversationCharacter == MobileParty.MainParty.CurrentSettlement.Culture.Merchant;

            campaignGameStarter.AddDialogLine("village_trader_greeting", "start", "village_trader",
                "Hail {?PLAYER.GENDER}m'lady{?}m'lord{\\?}, I bid thee welcome to our humble hamlet. I organize the trade here. Be thee interested in haggling?",
                isConversationWithVillageMerchant,
                null);

            // Player options
            campaignGameStarter.AddPlayerLine("village_trader_trade", "village_trader", "village_trader_trade_response",
                "Indeed, let's have a look.", null, () =>
                {
                    BeginTradeWithVillageTrader();
                });

            campaignGameStarter.AddPlayerLine("village_trader_arrangement", "village_trader", "village_trader_arrangement_response",
                "I want you to be ready to trade with me on a moment's notice when you see my banner approaching.",
                VillageTraderArrangementOnCondition,
                null);

            campaignGameStarter.AddPlayerLine("village_trader_end_conversation", "village_trader", "close_window",
                "Not at this time. Take care.", null, null);

            // Trader's response to trade
            campaignGameStarter.AddDialogLine("village_trader_trade_response", "village_trader_trade_response", "village_trader_options",
                "Very well, let's trade.", null, null);

            // Trader's response to arrangement
            campaignGameStarter.AddDialogLine("village_trader_arrangement_response", "village_trader_arrangement_response", "village_trader_arrangement_offer",
                "Ah, methinks for a humble price of {ARRANGEMENT_COST}{GOLD_ICON}, to pay the boys for running and gathering goods for thee, we shall be keen for such an arrangement. Does {?PLAYER.GENDER}m'lord{?}m'lady{\\?} concur?",
                null, null);

            // Player accepts arrangement
            campaignGameStarter.AddPlayerLine("village_trader_accept_arrangement", "village_trader_arrangement_offer", "village_trader_arrangement_accepted",
                "Yes, that sounds acceptable. [Pay {ARRANGEMENT_COST}{GOLD_ICON}]",
                VillageTraderAcceptArrangementOnCondition,
                () =>
                {
                    UnlockSettlementAccess(Settlement.CurrentSettlement, GetVillageArrangementCost());
                });

            // Trader acknowledges arrangement
            campaignGameStarter.AddDialogLine("village_trader_arrangement_accepted", "village_trader_arrangement_accepted", "close_window",
                "Splendid! We shall be at your service whenever you need us.", null, null);

            // Player declines arrangement
            campaignGameStarter.AddPlayerLine("village_trader_decline_arrangement", "village_trader_arrangement_offer", "close_window",
                "No, perhaps another time.", null, null);

            // Additional trade options after initial trade
            campaignGameStarter.AddPlayerLine("village_trader_trade_more", "village_trader_options", "village_trader_trade_response",
                "Yes, I am not done yet.", null, () =>
                {
                    BeginTradeWithVillageTrader();
                });

            campaignGameStarter.AddPlayerLine("village_trader_end_conversation", "village_trader_options", "close_window",
                "No. Thank you for your time.", null, null);

            // Set text variables
            MBTextManager.SetTextVariable("ARRANGEMENT_COST", GetVillageArrangementCost());
        }

        private bool VillageTraderArrangementOnCondition()
        {
            return !HasAccessToSettlement(Settlement.CurrentSettlement);
        }

        private bool VillageTraderAcceptArrangementOnCondition()
        {
            return Hero.MainHero.Gold >= GetVillageArrangementCost();
        }

        private int GetVillageArrangementCost()
        {
            return 200; // Fixed cost for village arrangement
        }

        private void BeginTradeWithVillageTrader()
        {
            var settlementComponent = Settlement.CurrentSettlement.SettlementComponent;
            if (settlementComponent != null)
            {
                OpenScreenAsTrade(Settlement.CurrentSettlement.ItemRoster, settlementComponent, InventoryCategoryType.None, null);

                // Correctly set the SETTLEMENT_NAME variable
                Settlement settlement = Settlement.CurrentSettlement;
                if (settlement != null)
                {
                    MBTextManager.SetTextVariable("SETTLEMENT_NAME", settlement.Name);
                }
            }
        }

        protected void AddGuideDialogs(CampaignGameStarter campaignGameStarter)
        {
            // Dialog for player who already has access
            campaignGameStarter.AddDialogLine("guide_already_paid_start", "start", "guide_talk",
                "Ah, I see thou hast already been shown the wonders of {SETTLEMENT_NAME}. There is naught more to see.",
                GuideAlreadyPaidStartOnCondition,
                null);

            campaignGameStarter.AddPlayerLine("guide_already_paid_end", "guide_talk", "close_window",
                "Indeed, I have seen all there is.",
                GuideAlreadyPaidEndOnCondition,
                null);

            // Dialog for initiating conversation with the guide
            campaignGameStarter.AddDialogLine("guide_start", "start", "guide_talk",
                "Ho there, sojourner. Thine puzzled face betrays thine nature. Fear thee not, as for the most modest sum, I shall show thee around {SETTLEMENT_NAME}, we shall leave no boulder unturned until thou knows't this corner of earth as thine own. Be thee interested?",
                GuideStartOnCondition,
                null);

            // Player accepts to pay the guide
            campaignGameStarter.AddPlayerLine("guide_accept", "guide_talk", "guide_agree",
                "Yes, I could use your help. [Pay {COST}{GOLD_ICON}]",
                GuideAcceptOnCondition,
                () =>
                {
                    UnlockSettlementAccess(Settlement.CurrentSettlement, GetGuideCost());
                });

            // Guide agrees to show around
            campaignGameStarter.AddDialogLine("guide_agree", "guide_agree", "close_window",
                "Splendid. Let me show thee the ins and outs of our {SETTLEMENT_NAME}.",
                GuideAgreeOnCondition,
                null);

            // Player declines to pay the guide
            campaignGameStarter.AddPlayerLine("guide_decline", "guide_talk", "close_window",
                "By heaven's Grace, what are you on about. Not interested.",
                GuideDeclineOnCondition,
                null);
        }

        private bool GuideAlreadyPaidStartOnCondition()
        {
            if (CharacterObject.OneToOneConversationCharacter != _localGuide)
                return false;

            MBTextManager.SetTextVariable("SETTLEMENT_NAME", Settlement.CurrentSettlement.Name);
            return HasAccessToSettlement(Settlement.CurrentSettlement);
        }

        private bool GuideAlreadyPaidEndOnCondition()
        {
            return HasAccessToSettlement(Settlement.CurrentSettlement);
        }

        private bool GuideStartOnCondition()
        {
            if (CharacterObject.OneToOneConversationCharacter != _localGuide)
                return false;

            MBTextManager.SetTextVariable("SETTLEMENT_NAME", Settlement.CurrentSettlement.Name);
            return !HasAccessToSettlement(Settlement.CurrentSettlement);
        }

        private bool GuideAcceptOnCondition()
        {
            if (HasAccessToSettlement(Settlement.CurrentSettlement))
                return false;

            MBTextManager.SetTextVariable("COST", GetGuideCost());
            MBTextManager.SetTextVariable("SETTLEMENT_NAME", Settlement.CurrentSettlement.Name);
            return Hero.MainHero.Gold >= GetGuideCost();
        }

        private bool GuideAgreeOnCondition()
        {
            MBTextManager.SetTextVariable("SETTLEMENT_NAME", Settlement.CurrentSettlement.Name);
            return true;
        }

        private bool GuideDeclineOnCondition()
        {
            return !HasAccessToSettlement(Settlement.CurrentSettlement);
        }

        private int GetGuideCost()
        {
            int clanTier = Clan.PlayerClan != null ? Clan.PlayerClan.Tier : 0;
            return 500 * (clanTier + 1);
        }

        private void UnlockSettlementAccess(Settlement settlement, int cost)
        {
            if (Hero.MainHero.Gold >= cost)
            {
                Hero.MainHero.ChangeHeroGold(-cost);
                var settlementId = settlement.Id.ToString();
                settlementsWithAccess[settlementId] = true;
                InformationManager.DisplayMessage(new InformationMessage($"You now know your way around {settlement.Name}."));
            }
            else
            {
                InformationManager.DisplayMessage(new InformationMessage("You don't have enough gold."));
            }
        }


        // Method to check if the player has access to the settlement
        public bool HasAccessToSettlement(Settlement settlement)
        {
            if (settlement.OwnerClan == Clan.PlayerClan)
            {
                // Player owns the settlement
                return true;
            }

            if (settlement.OwnerClan.Kingdom == Clan.PlayerClan.Kingdom && Clan.PlayerClan.Tier >= 3)
            {
                // Player shares kingdom
                return true;
            }

            if (Clan.PlayerClan.Tier >= 5)
            {
                // player very renowned, maps are given to him
                return true;
            }

            var settlementId = settlement.Id.ToString();
            // Check if the player has paid for access
            return settlementsWithAccess.ContainsKey(settlementId) && settlementsWithAccess[settlementId];
        }


        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("settlementsWithAccess", ref settlementsWithAccess);
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

        [HarmonyPatch(typeof(GameMenu), "AddOption", new Type[] {
            typeof(string),
            typeof(TextObject),
            typeof(GameMenuOption.OnConditionDelegate),
            typeof(GameMenuOption.OnConsequenceDelegate),
            typeof(int),
            typeof(bool),
            typeof(bool),
            typeof(object)
        })]
        public static class DisableSpecificMenuOptionsPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(ref GameMenuOption.OnConditionDelegate condition, string optionId, GameMenu __instance)
            {
                if (SettlementMenuOptions.AllOptions.Contains(optionId))
                {
                    var campaign = Campaign.Current;
                    if (campaign == null)
                    {
                        return true; // Allow the original method to proceed without modification
                    }

                    var behaviorInstance = campaign.GetCampaignBehavior<DisableMenuBehavior>();
                    var currentSettlement = GetCurrentSettlement(__instance);

                    if (behaviorInstance != null && currentSettlement != null)
                    {
                        var originalCondition = condition; // Preserve the original condition

                        condition = (MenuCallbackArgs args) =>
                        {
                            // Call the original condition
                            bool isOriginalConditionMet = originalCondition == null || originalCondition(args);

                            // Check if the player has access
                            bool hasAccess = behaviorInstance.HasAccessToSettlement(currentSettlement);

                            // Combine both conditions
                            args.IsEnabled = isOriginalConditionMet && hasAccess;
                            return args.IsEnabled;
                        };
                    }
                    else
                    {
                        // If behaviorInstance or currentSettlement is null, disable the option safely
                        condition = (MenuCallbackArgs args) =>
                        {
                            args.IsEnabled = false;
                            return false;
                        };
                    }
                }
                return true; // Allow the original method to proceed
            }

            private static Settlement GetCurrentSettlement(GameMenu gameMenu)
            {
                // Try to get the settlement associated with the current menu
                if (MobileParty.MainParty.CurrentSettlement != null)
                {
                    return MobileParty.MainParty.CurrentSettlement;
                }
                else if (Settlement.CurrentSettlement != null)
                {
                    return Settlement.CurrentSettlement;
                }
                // Fallback
                return null;
            }
        }
    }
}