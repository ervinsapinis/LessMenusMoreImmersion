using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LessMenusMoreImmersion.Constants;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using static TaleWorlds.CampaignSystem.Inventory.InventoryManager;
using TaleWorlds.ObjectSystem;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Custom behavior to manage menu options and settlement access within the LessMenusMoreImmersion mod.
    /// </summary>
    public class DisableMenuBehavior : CampaignBehaviorBase
    {
        private static Dictionary<string, bool> settlementsWithAccess = new Dictionary<string, bool>();
        private CharacterObject? _localGuide;

        /// <summary>
        /// Registers campaign events for this behavior.
        /// </summary>
        public override void RegisterEvents()
        {
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnGameStarted);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameStarted);
            InformationManager.DisplayMessage(new InformationMessage("[LM] DisableMenuBehavior events registered"));
        }

        /// <summary>
        /// Initializes the behavior when the game starts.
        /// </summary>
        /// <param name="campaignGameStarter">The campaign game starter.</param>
        private void OnGameStarted(CampaignGameStarter campaignGameStarter)
        {
            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=Eji4qI4xg}Less menus more immersion loaded successfully.").ToString()));
            AddVillageTraderDialogs(campaignGameStarter);
            AddGuideDialogs(campaignGameStarter);
            AddRecruitmentDialogs(campaignGameStarter); // Add recruitment dialogs
            CampaignEvents.LocationCharactersAreReadyToSpawnEvent.AddNonSerializedListener(this, LocationCharactersAreReadyToSpawn);
            _localGuide = MBObjectManager.Instance.GetObject<CharacterObject>("local_guide");
            InformationManager.DisplayMessage(new InformationMessage("[LM] DisableMenuBehavior fully initialized"));
        }

        /// <summary>
        /// Event handler called when location characters are ready to spawn.
        /// Adds traders or guides to the appropriate locations.
        /// </summary>
        /// <param name="dictionary">A dictionary of character counts.</param>
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

        /// <summary>
        /// Adds a village trader to the specified settlement's location.
        /// </summary>
        /// <param name="settlement">The settlement to add the trader to.</param>
        private void AddVillageTraderToLocation(Settlement settlement)
        {
            if (settlement?.Culture?.Merchant == null)
                return;

            Location? location = settlement.LocationComplex?.GetLocationWithId("village_center");
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

        /// <summary>
        /// Adds a guide to the tavern location of the specified settlement.
        /// </summary>
        /// <param name="settlement">The settlement to add the guide to.</param>
        private void AddGuideToTavern(Settlement settlement)
        {
            Location? tavernLocation = settlement.LocationComplex?.GetLocationWithId("tavern");
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

        /// <summary>
        /// Adds dialog lines and player options for the village trader, utilizing localized strings.
        /// </summary>
        /// <param name="campaignGameStarter">The campaign game starter used to add dialogs.</param>
        protected void AddVillageTraderDialogs(CampaignGameStarter campaignGameStarter)
        {
            /// <summary>
            /// Determines if the current conversation is with the village merchant.
            /// </summary>
            /// <returns>True if the conversation is with the village merchant; otherwise, false.</returns>
            bool isConversationWithVillageMerchant() =>
                MobileParty.MainParty.CurrentSettlement != null &&
                MobileParty.MainParty.CurrentSettlement.IsVillage &&
                CharacterObject.OneToOneConversationCharacter == MobileParty.MainParty.CurrentSettlement.Culture.Merchant;

            // Trader greeting
            campaignGameStarter.AddDialogLine(
                "village_trader_greeting",
                "start",
                "village_trader",
                "{=village_elder_greeting}Hail {?PLAYER.GENDER}m'lady{?}m'lord{\\?}, I bid thee welcome to our humble hamlet. I am the village elder here. How may I serve thee?",
                isConversationWithVillageMerchant,
                null
            );

            // Player option: Trade
            campaignGameStarter.AddPlayerLine(
                "village_trader_trade",
                "village_trader",
                "village_trader_trade_response",
                "{=MmNpGwNT9}Indeed, let's have a look.",
                null,
                () =>
                {
                    BeginTradeWithVillageTrader();
                }
            );

            // Player option: Arrangement
            campaignGameStarter.AddPlayerLine(
                "village_trader_arrangement",
                "village_trader",
                "village_trader_arrangement_response",
                "{=1qSPbCkBo}I want you to be ready to trade with me on a moment's notice when you see my banner approaching.",
                VillageTraderArrangementOnCondition,
                null
            );

            // Player option: End Conversation
            campaignGameStarter.AddPlayerLine(
                "village_trader_end_conversation",
                "village_trader",
                "close_window",
                "{=OeZhSF3K0}Not at this time. Take care.",
                null,
                null
            );

            // Trader's response to Trade
            campaignGameStarter.AddDialogLine(
                "village_trader_trade_response",
                "village_trader_trade_response",
                "village_trader_options",
                "{=iZHsKXxU6}Very well, let's trade.",
                null,
                null
            );

            // Trader's response to Arrangement
            campaignGameStarter.AddDialogLine(
                "village_trader_arrangement_response",
                "village_trader_arrangement_response",
                "village_trader_arrangement_offer",
                "{=kOPidaq6F}Ah, methinks for a humble price of {ARRANGEMENT_COST}{GOLD_ICON}, to pay the boys for running and gathering goods for thee, we shall be keen for such an arrangement. Does {?PLAYER.GENDER}m'lord{?}m'lady{\\?} concur?",
                null,
                null
            );

            // Player option: Accept Arrangement
            campaignGameStarter.AddPlayerLine(
                "village_trader_accept_arrangement",
                "village_trader_arrangement_offer",
                "village_trader_arrangement_accepted",
                "{=vt6FfbaMf}Yes, that sounds acceptable. [Pay {ARRANGEMENT_COST}{GOLD_ICON}]",
                VillageTraderAcceptArrangementOnCondition,
                () =>
                {
                    UnlockSettlementAccess(Settlement.CurrentSettlement, GetVillageArrangementCost());
                }
            );

            // Trader acknowledges arrangement
            campaignGameStarter.AddDialogLine(
                "village_trader_arrangement_accepted",
                "village_trader_arrangement_accepted",
                "close_window",
                "{=p12b9Nsi9}Splendid! We shall be at your service whenever you need us.",
                null,
                null
            );

            // Player option: Decline Arrangement
            campaignGameStarter.AddPlayerLine(
                "village_trader_decline_arrangement",
                "village_trader_arrangement_offer",
                "close_window",
                "{=ojZrYpAXh}No, perhaps another time.",
                null,
                null
            );

            // Player option: Trade More
            campaignGameStarter.AddPlayerLine(
                "village_trader_trade_more",
                "village_trader_options",
                "village_trader_trade_response",
                "{=A1b2C3d4E}Yes, I am not done yet.",
                null,
                () =>
                {
                    BeginTradeWithVillageTrader();
                }
            );

            // Player option: End Conversation Again
            campaignGameStarter.AddPlayerLine(
                "village_trader_end_conversation",
                "village_trader_options",
                "close_window",
                "{=F5g6H7i8J}No. Thank you for your time.",
                null,
                null
            );

            // Set text variables
            MBTextManager.SetTextVariable("ARRANGEMENT_COST", GetVillageArrangementCost());
        }

        /// <summary>
        /// Adds dialog lines and player options for the guide, utilizing localized strings.
        /// </summary>
        /// <param name="campaignGameStarter">The campaign game starter used to add dialogs.</param>
        protected void AddGuideDialogs(CampaignGameStarter campaignGameStarter)
        {
            // Dialog for player who already has access
            campaignGameStarter.AddDialogLine(
                "guide_already_paid_start",
                "start",
                "guide_talk",
                "{=E5f6G7h8I}Ah, I see thou hast already been shown the wonders of {SETTLEMENT_NAME}. There is naught more to see.",
                GuideAlreadyPaidStartOnCondition,
                null
            );

            // Player option: End Conversation
            campaignGameStarter.AddPlayerLine(
                "guide_already_paid_end",
                "guide_talk",
                "close_window",
                ("{=J9k0L1m2N}Indeed, I have seen all there is."),
                GuideAlreadyPaidEndOnCondition,
                null
            );

            // Dialog for initiating conversation with the guide
            campaignGameStarter.AddDialogLine(
                "guide_start",
                "start",
                "guide_talk",
                "{=O3p4Q5r6S}Ho there, sojourner. Thine puzzled face betrays thine nature. Fear thee not, as for the most modest sum, I shall show thee around {SETTLEMENT_NAME}, we shall leave no boulder unturned until thou knows't this corner of earth as thine own. Be thee interested?",
                GuideStartOnCondition,
                null
            );

            // Player option: Accept to Pay the Guide
            campaignGameStarter.AddPlayerLine(
                "guide_accept",
                "guide_talk",
                "guide_agree",
                "{=T7u8V9w0X}Yes, I could use your help. [Pay {COST}{GOLD_ICON}]",
                GuideAcceptOnCondition,
                () =>
                {
                    UnlockSettlementAccess(Settlement.CurrentSettlement, GetGuideCost());
                }
            );

            // Guide agrees to show around
            campaignGameStarter.AddDialogLine(
                "guide_agree",
                "guide_agree",
                "close_window",
                "{=Y1z2A3b4C}Splendid. Let me show thee the ins and outs of our {SETTLEMENT_NAME}.",
                 GuideAgreeOnCondition,
                null
            );

            // Player option: Decline to Pay the Guide
            campaignGameStarter.AddPlayerLine(
                "guide_decline",
                "guide_talk",
                "close_window",
                "{=D5e6F7g8H}By heaven's Grace, what are you on about. Not interested.",
                GuideDeclineOnCondition,
                null
            );
        }

        /// <summary>
        /// Adds proper recruitment dialogs that exit cleanly when refused
        /// </summary>
        /// <param name="campaignGameStarter">The campaign game starter used to add dialogs.</param>
        protected void AddRecruitmentDialogs(CampaignGameStarter campaignGameStarter)
        {
            // Block the recruitment organizer dialog that gets stuck in loops
            bool isRecruitmentOrganizerWithoutAccess() =>
                Settlement.CurrentSettlement != null &&
                !HasAccessToSettlement(Settlement.CurrentSettlement);

            // Intercept the "organize willing recruits" dialog
            campaignGameStarter.AddDialogLine(
                "recruitment_organizer_blocked",
                "party_recruit_confirm",
                "recruitment_organizer_refuse",
                "{=recruitment_organizer_blocked}I don't know you well enough to organize that kind of arrangement, stranger.",
                isRecruitmentOrganizerWithoutAccess,
                null,
                200 // Very high priority to intercept
            );

            // Force exit option for organizer
            campaignGameStarter.AddPlayerLine(
                "recruitment_organizer_exit",
                "recruitment_organizer_refuse",
                "close_window",
                "{=recruitment_organizer_exit}I understand.",
                null,
                () => {
                    InformationManager.DisplayMessage(new InformationMessage("[LM] Recruitment organizer dialog closed"));
                }
            );

            // Also block the general recruitment conversation
            bool isRecruitmentWithoutAccess() =>
                CharacterObject.OneToOneConversationCharacter != null &&
                CharacterObject.OneToOneConversationCharacter.Occupation == Occupation.Soldier &&
                Settlement.CurrentSettlement != null &&
                !HasAccessToSettlement(Settlement.CurrentSettlement);

            // Intercept normal recruitment at start
            campaignGameStarter.AddDialogLine(
                "recruitment_blocked_start",
                "start",
                "recruitment_blocked_options",
                "{=recruitment_blocked}I don't know you, stranger. You'll need to get someone to vouch for you before I'll consider joining your cause.",
                isRecruitmentWithoutAccess,
                null,
                100 // High priority
            );

            // Exit option that actually works
            campaignGameStarter.AddPlayerLine(
                "recruitment_exit_clean",
                "recruitment_blocked_options",
                "close_window",
                "{=recruitment_exit_clean}Perhaps another time.",
                null,
                () => {
                    InformationManager.DisplayMessage(new InformationMessage("[LM] Recruitment dialog properly closed"));
                }
            );

            // Explanation option
            campaignGameStarter.AddPlayerLine(
                "recruitment_ask_how",
                "recruitment_blocked_options",
                "recruitment_explain_how",
                "{=recruitment_ask_how}How can I gain your trust?",
                null,
                null
            );

            // Explain and then exit
            campaignGameStarter.AddDialogLine(
                "recruitment_explain_how_response",
                "recruitment_explain_how",
                "close_window",
                "{=recruitment_explain_how_response}Find someone who knows the settlement to introduce you. Pay for a guide, maybe.",
                null,
                () => {
                    InformationManager.DisplayMessage(new InformationMessage("[LM] Recruitment explanation completed"));
                }
            );
        }

        // ==== UTILITY METHODS ====

        /// <summary>
        /// Determines whether the player can arrange trading with the village trader.
        /// </summary>
        /// <returns>True if the player does not have access to the settlement.</returns>
        private bool VillageTraderArrangementOnCondition()
        {
            return !HasAccessToSettlement(Settlement.CurrentSettlement);
        }

        /// <summary>
        /// Determines whether the player can accept the arrangement with the village trader.
        /// </summary>
        /// <returns>True if the player has enough gold to pay for the arrangement.</returns>
        private bool VillageTraderAcceptArrangementOnCondition()
        {
            return Hero.MainHero.Gold >= GetVillageArrangementCost();
        }

        /// <summary>
        /// Gets the cost for arranging trading with the village trader.
        /// </summary>
        /// <returns>The cost amount.</returns>
        private int GetVillageArrangementCost()
        {
            return 200; // Fixed cost for village arrangement
        }

        /// <summary>
        /// Initiates trading with the village trader.
        /// </summary>
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

        /// <summary>
        /// Condition to check if the guide dialog should start for players who have already paid.
        /// </summary>
        /// <returns>True if the player has access to the settlement.</returns>
        private bool GuideAlreadyPaidStartOnCondition()
        {
            if (CharacterObject.OneToOneConversationCharacter != _localGuide)
                return false;

            MBTextManager.SetTextVariable("SETTLEMENT_NAME", Settlement.CurrentSettlement.Name);
            return HasAccessToSettlement(Settlement.CurrentSettlement);
        }

        /// <summary>
        /// Condition to check if the player has already paid the guide.
        /// </summary>
        /// <returns>True if the player has access to the settlement.</returns>
        private bool GuideAlreadyPaidEndOnCondition()
        {
            return HasAccessToSettlement(Settlement.CurrentSettlement);
        }

        /// <summary>
        /// Condition to check if the guide dialog should start for players who have not paid.
        /// </summary>
        /// <returns>True if the player does not have access to the settlement.</returns>
        private bool GuideStartOnCondition()
        {
            if (CharacterObject.OneToOneConversationCharacter != _localGuide)
                return false;

            MBTextManager.SetTextVariable("SETTLEMENT_NAME", Settlement.CurrentSettlement.Name);
            return !HasAccessToSettlement(Settlement.CurrentSettlement);
        }

        /// <summary>
        /// Condition to check if the player can accept paying the guide.
        /// </summary>
        /// <returns>True if the player has enough gold to pay the guide.</returns>
        private bool GuideAcceptOnCondition()
        {
            if (HasAccessToSettlement(Settlement.CurrentSettlement))
                return false;

            MBTextManager.SetTextVariable("COST", GetGuideCost());
            MBTextManager.SetTextVariable("SETTLEMENT_NAME", Settlement.CurrentSettlement.Name);
            return Hero.MainHero.Gold >= GetGuideCost();
        }

        /// <summary>
        /// Condition to check if the guide agrees to show around.
        /// </summary>
        /// <returns>Always true.</returns>
        private bool GuideAgreeOnCondition()
        {
            MBTextManager.SetTextVariable("SETTLEMENT_NAME", Settlement.CurrentSettlement.Name);
            return true;
        }

        /// <summary>
        /// Condition to check if the player declines to pay the guide.
        /// </summary>
        /// <returns>True if the player does not have access to the settlement.</returns>
        private bool GuideDeclineOnCondition()
        {
            return !HasAccessToSettlement(Settlement.CurrentSettlement);
        }

        /// <summary>
        /// Gets the cost for hiring the guide based on the player's clan tier.
        /// </summary>
        /// <returns>The cost amount.</returns>
        private int GetGuideCost()
        {
            int clanTier = Clan.PlayerClan != null ? Clan.PlayerClan.Tier : 0;
            return 500 * (clanTier + 1);
        }

        /// <summary>
        /// Unlocks access to the specified settlement by deducting gold and updating access records.
        /// </summary>
        /// <param name="settlement">The settlement to unlock access for.</param>
        /// <param name="cost">The cost to unlock access.</param>
        private void UnlockSettlementAccess(Settlement settlement, int cost)
        {
            if (Hero.MainHero.Gold >= cost)
            {
                Hero.MainHero.ChangeHeroGold(-cost);
                var settlementId = settlement.Id.ToString();
                settlementsWithAccess[settlementId] = true;
                InformationManager.DisplayMessage(new InformationMessage($"[LM] Unlocked access to {settlement.Name}"));
                InformationManager.DisplayMessage(new InformationMessage("{=P3q4R5s6T}You now know your way around {settlement.Name}."));

                // Log that access has been granted for debugging
                InformationManager.DisplayMessage(new InformationMessage($"[LM] Settlement access granted - menu options should now be available"));
            }
            else
            {
                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=Z1a2B3c4D}You don't have enough gold.").ToString()));
            }
        }

        /// <summary>
        /// Checks if the player has access to the specified settlement.
        /// </summary>
        /// <param name="settlement">The settlement to check access for.</param>
        /// <returns>True if the player has access; otherwise, false.</returns>
        public bool HasAccessToSettlement(Settlement settlement)
        {
            if (settlement == null) return true;

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
                // Player very renowned, maps are given to him
                return true;
            }

            var settlementId = settlement.Id.ToString();
            // Check if the player has paid for access
            return settlementsWithAccess.ContainsKey(settlementId) && settlementsWithAccess[settlementId];
        }

        /// <summary>
        /// Synchronizes data when saving or loading the game.
        /// </summary>
        /// <param name="dataStore">The data store.</param>
        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("settlementsWithAccess", ref settlementsWithAccess);
        }

        /// <summary>
        /// Contains Harmony patches for the DisableMenuBehavior.
        /// </summary>
        public static class HarmonyPatches
        {
            private static Harmony? harmonyInstance;

            /// <summary>
            /// Applies all Harmony patches defined in this class.
            /// </summary>
            public static void ApplyPatches()
            {
                if (harmonyInstance == null)
                {
                    harmonyInstance = new Harmony("LessMenusMoreImmersion");
                    harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
                }
            }
        }

        /// <summary>
        /// Harmony patch to disable specific menu options based on settlement access.
        /// </summary>
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
            private static int _patchCallCount = 0;

            /// <summary>
            /// Prefix method that modifies the condition delegate for certain menu options.
            /// </summary>
            /// <param name="condition">The original condition delegate.</param>
            /// <param name="optionId">The ID of the menu option being added.</param>
            /// <param name="__instance">The GameMenu instance.</param>
            /// <returns>True to allow the original method to proceed.</returns>
            [HarmonyPrefix]
            public static bool Prefix(ref GameMenuOption.OnConditionDelegate condition, string optionId, GameMenu __instance)
            {
                // Only modify specific menu options that we know should be controlled
                var restrictedOptions = new[] {
                    "town_arena", "town_trade", "castle_trade", "village_trade",
                    "town_backstreet", "town_keep", "castle_dungeon", "wait"
                };

                if (restrictedOptions.Contains(optionId))
                {
                    _patchCallCount++;
                    if (_patchCallCount <= 5) // Reduced logging
                    {
                        InformationManager.DisplayMessage(new InformationMessage($"[LM] Menu patch applied to: {optionId}"));
                    }

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
                            bool finalEnabled = isOriginalConditionMet && hasAccess;
                            args.IsEnabled = finalEnabled;

                            // Minimal logging for critical issues only
                            if (!hasAccess && _patchCallCount <= 3)
                            {
                                InformationManager.DisplayMessage(new InformationMessage($"[LM] {optionId} disabled: no settlement access"));
                            }
                            else if (hasAccess && _patchCallCount <= 3)
                            {
                                InformationManager.DisplayMessage(new InformationMessage($"[LM] {optionId} enabled: has access"));
                            }

                            return args.IsEnabled;
                        };
                    }
                    else
                    {
                        // If behaviorInstance or currentSettlement is null, use original condition
                        InformationManager.DisplayMessage(new InformationMessage($"[LM] {optionId}: using original condition (behavior/settlement null)"));
                    }
                }
                return true; // Allow the original method to proceed
            }

            /// <summary>
            /// Retrieves the current settlement associated with the GameMenu.
            /// </summary>
            /// <param name="gameMenu">The GameMenu instance.</param>
            /// <returns>The current settlement, if any; otherwise, null.</returns>
            private static Settlement? GetCurrentSettlement(GameMenu gameMenu)
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