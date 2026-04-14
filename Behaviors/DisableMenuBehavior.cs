using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LessMenusMoreImmersion.Constants;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
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
using TaleWorlds.ObjectSystem;
using static Helpers.InventoryScreenHelper;
using Helpers;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.MountAndBlade;
using SandBox;
using SandBox.Conversation;

namespace LessMenusMoreImmersion.Behaviors
{
    public class DisableMenuBehavior : CampaignBehaviorBase
    {
        [NonSerialized] private Dictionary<string, bool> settlementsWithAccess = new Dictionary<string, bool>();
        [NonSerialized] private Dictionary<string, List<string>> settlementsFeaturesAccessed = new Dictionary<string, List<string>>();
        [NonSerialized] private CharacterObject? _localGuide;
        [NonSerialized] private bool _spawnListenerRegistered;

        private EscortBehavior _escortBehavior = new EscortBehavior();

        private static readonly Dictionary<Occupation, string> OccupationFeatureMap = new Dictionary<Occupation, string>
        {
            { Occupation.Tavernkeeper, SettlementMenuOptions.Features.Backstreet },
            { Occupation.TavernWench, SettlementMenuOptions.Features.Backstreet },
            { Occupation.TavernGameHost, SettlementMenuOptions.Features.Backstreet },
            { Occupation.GangLeader, SettlementMenuOptions.Features.Backstreet },
            { Occupation.Gangster, SettlementMenuOptions.Features.Backstreet },
            { Occupation.Merchant, SettlementMenuOptions.Features.Trade },
            { Occupation.GoodsTrader, SettlementMenuOptions.Features.Trade },
            { Occupation.ShopWorker, SettlementMenuOptions.Features.Trade },
        };

        public override void RegisterEvents()
        {
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnGameStarted);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameStarted);
            CampaignEvents.MissionTickEvent.AddNonSerializedListener(this, OnMissionTick);
        }

        private void OnGameStarted(CampaignGameStarter campaignGameStarter)
        {
            LmmiLog.Info("DisableMenuBehavior: OnGameStarted - registering dialogs and events. [v3.1.0]");
            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=Eji4qI4xg}Less menus more immersion loaded successfully [v3.1.0].").ToString()));

            try
            {
                AddVillageTraderDialogs(campaignGameStarter);
                AddGuideDialogs(campaignGameStarter);
                AddDiscoveryDialog(campaignGameStarter);
                AddTownDirectionsDialogs(campaignGameStarter);
            }
            catch (Exception ex)
            {
                LmmiLog.Error("Failed to register dialogs", ex);
            }

            if (!_spawnListenerRegistered)
            {
                CampaignEvents.LocationCharactersAreReadyToSpawnEvent.AddNonSerializedListener(this, LocationCharactersAreReadyToSpawn);
                _spawnListenerRegistered = true;
                LmmiLog.Debug("LocationCharactersAreReadyToSpawn listener registered.");
            }

            _localGuide = MBObjectManager.Instance.GetObject<CharacterObject>("local_guide");
            if (_localGuide == null)
                LmmiLog.Warning("local_guide character object not found - guide tavern spawns will be skipped.");

            _escortBehavior.OnFeatureUnlocked += OnFeatureUnlocked;
        }

        private void OnFeatureUnlocked(string feature, Settlement settlement)
        {
            if (settlement == null || string.IsNullOrEmpty(feature)) return;
            if (HasAccessToSettlement(settlement)) return;

            var settlementId = settlement.Id.ToString();
            if (settlementsFeaturesAccessed == null) settlementsFeaturesAccessed = new Dictionary<string, List<string>>();
            if (!settlementsFeaturesAccessed.TryGetValue(settlementId, out var features))
            {
                features = new List<string>();
                settlementsFeaturesAccessed[settlementId] = features;
            }

            if (features.Contains(feature)) return;
            features.Add(feature);

            LmmiLog.Debug($"Discovered feature '{feature}' in {settlement.Name} (id={settlementId}).");

            if (LmmiSettingsProvider.ShowDiscoveryMessages)
            {
                var displayName = SettlementMenuOptions.GetFeatureDisplayName(feature);
                var message = new TextObject("{=lmmi_feature_discovered}You've found {FEATURE} of {SETTLEMENT}.");
                message.SetTextVariable("FEATURE", displayName);
                message.SetTextVariable("SETTLEMENT", settlement.Name);
                InformationManager.DisplayMessage(new InformationMessage(message.ToString()));
            }
        }

        private void LocationCharactersAreReadyToSpawn(Dictionary<string, int> dictionary)
        {
            if (Campaign.Current == null || Game.Current == null)
                return;

            var settlement = PlayerEncounter.LocationEncounter?.Settlement ?? Settlement.CurrentSettlement;
            if (settlement == null)
                return;

            var locationId = CampaignMission.Current?.Location?.StringId;

            if (settlement.IsVillage)
            {
                AddVillageTraderToLocation(settlement);
            }
            else if (locationId == "tavern")
            {
                AddGuideToTavern(settlement);
            }

            if (!string.IsNullOrEmpty(locationId))
            {
                TryDiscoverFromLocation(settlement, locationId!);
            }
        }

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

        private void AddGuideToTavern(Settlement settlement)
        {
            if (_localGuide == null)
                return;

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
            bool isConversationWithVillageMerchant()
            {
                var currentSettlement = MobileParty.MainParty.CurrentSettlement;
                var isVillage = currentSettlement?.IsVillage ?? false;
                var currentChar = CharacterObject.OneToOneConversationCharacter;
                var expectedChar = currentSettlement?.Culture?.Merchant;

                bool result = currentSettlement != null &&
                              isVillage &&
                              currentChar == expectedChar;

                return result;
            }

            campaignGameStarter.AddDialogLine(
                "village_trader_greeting",
                "start",
                "village_trader",
                "{=village_trader_greeting}Hail {?PLAYER.GENDER}m'lady{?}m'lord{\\?}, I bid thee welcome to our humble hamlet. I am village trader here. How may I serve thee?",
                isConversationWithVillageMerchant,
                null
            );

            campaignGameStarter.AddPlayerLine(
                "village_trader_trade",
                "village_trader",
                "village_trader_trade_response",
                "{=MmNpGwNT9}Indeed, let's have a look.",
                null,
                null
            );

            campaignGameStarter.AddPlayerLine(
                "village_trader_arrangement",
                "village_trader",
                "village_trader_arrangement_response",
                "{=1qSPbCkBo}I want you to be ready to trade with me on a moment's notice when you see my banner approaching.",
                VillageTraderArrangementOnCondition,
                null
            );

            campaignGameStarter.AddPlayerLine(
                "village_trader_end_conversation",
                "village_trader",
                "close_window",
                "{=OeZhSF3K0}Not at this time. Take care.",
                null,
                null
            );

            campaignGameStarter.AddDialogLine(
                "village_trader_trade_response",
                "village_trader_trade_response",
                "village_trader_options",
                "{=iZHsKXxU6}Very well, let's trade.",
                null,
                null
            );

            campaignGameStarter.AddDialogLine(
                "village_trader_arrangement_response",
                "village_trader_arrangement_response",
                "village_trader_arrangement_offer",
                "{=kOPidaq6F}Ah, methinks for a humble price of {ARRANGEMENT_COST}{GOLD_ICON}, to pay boys for running and gathering goods for thee, we shall be keen for such an arrangement. Furthermore, I shall telle thee of about our hamlet and esteemed folk therein. Does {?PLAYER.GENDER}m'lady{?}m'lord{\\?} concur?",
                null,
                null
            );

            campaignGameStarter.AddPlayerLine(
                "village_trader_accept_arrangement",
                "village_trader_arrangement_offer",
                "village_trader_arrangement_accepted",
                "{=vt6FfbaMf}Yes, that sounds acceptable. [Pay {ARRANGEMENT_COST}{GOLD_ICON}]",
                VillageTraderAcceptArrangementOnCondition,
                () => UnlockSettlementAccess(Settlement.CurrentSettlement, GetVillageArrangementCost())
            );

            campaignGameStarter.AddDialogLine(
                "village_trader_arrangement_accepted",
                "village_trader_arrangement_accepted",
                "close_window",
                "{=p12b9Nsi9}Splendid! We shall be at your service whenever you need us.",
                null,
                null
            );

            campaignGameStarter.AddPlayerLine(
                "village_trader_decline_arrangement",
                "village_trader_arrangement_offer",
                "close_window",
                "{=ojZrYpAXh}No, perhaps another time.",
                null,
                null
            );

            campaignGameStarter.AddPlayerLine(
                "village_trader_trade_more",
                "village_trader_options",
                "village_trader_trade_response",
                "{=A1b2C3d4E}Yes, I am not done yet.",
                null,
                null
            );

            campaignGameStarter.AddPlayerLine(
                "village_trader_end_conversation",
                "village_trader_options",
                "close_window",
                "{=F5g6H7i8J}No. Thank you for your time.",
                null,
                null
            );

            MBTextManager.SetTextVariable("ARRANGEMENT_COST", GetVillageArrangementCost());
        }

        private int GetVillageArrangementCost()
        {
            int clanTier = Clan.PlayerClan != null ? Clan.PlayerClan.Tier : 0;
            int baseCost = 100;
            return baseCost * (clanTier + 1);
        }

        private bool VillageTraderArrangementOnCondition()
        {
            return !HasAccessToSettlement(Settlement.CurrentSettlement);
        }

        private bool VillageTraderAcceptArrangementOnCondition()
        {
            return Hero.MainHero.Gold >= GetVillageArrangementCost();
        }

        protected void AddGuideDialogs(CampaignGameStarter campaignGameStarter)
        {
            LmmiLog.Debug("AddGuideDialogs: Guide dialogs not yet implemented.");
        }

        private void UnlockSettlementAccess(Settlement settlement, int cost)
        {
            if (settlement == null)
            {
                LmmiLog.Warning("UnlockSettlementAccess called with null settlement.");
                return;
            }

            if (Hero.MainHero.Gold >= cost)
            {
                Hero.MainHero.ChangeHeroGold(-cost);
                var settlementId = settlement.Id.ToString();
                if (settlementsWithAccess == null) settlementsWithAccess = new Dictionary<string, bool>();
                settlementsWithAccess[settlementId] = true;
                LmmiLog.Info($"Full settlement access unlocked for '{settlement.Name}' (id={settlementId}) for {cost} gold.");
                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=P3q4R5s6T}You now know your way around {settlement.Name}.").ToString()));
            }
            else
            {
                LmmiLog.Debug($"Player tried to unlock '{settlement.Name}' but lacked gold ({Hero.MainHero.Gold}/{cost}).");
                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=Z1a2B3c4D}You don't have enough gold.").ToString()));
            }
        }

        public bool HasAccessToSettlement(Settlement settlement)
        {
            if (settlement == null) return true;

            var playerClan = Clan.PlayerClan;
            if (playerClan == null) return true;

            if (settlement.OwnerClan == playerClan)
                return true;

            int fullTier = LmmiSettingsProvider.FullAccessClanTier;
            if (playerClan.Tier >= fullTier)
                return true;

            int kingdomTier = LmmiSettingsProvider.KingdomAccessClanTier;
            if (settlement.OwnerClan?.Kingdom != null &&
                playerClan.Kingdom != null &&
                settlement.OwnerClan.Kingdom == playerClan.Kingdom &&
                playerClan.Tier >= kingdomTier)
                return true;

            var settlementId = settlement.Id.ToString();
            if (settlementsWithAccess == null) return false;
            return settlementsWithAccess.ContainsKey(settlementId) && settlementsWithAccess[settlementId];
        }

        public bool HasFeatureAccess(Settlement settlement, string feature)
        {
            if (settlement == null) return true;
            if (HasAccessToSettlement(settlement)) return true;

            var settlementId = settlement.Id.ToString();
            if (settlementsFeaturesAccessed == null) return false;
            if (!settlementsFeaturesAccessed.TryGetValue(settlementId, out var features))
                return false;

            return features.Contains(feature);
        }

        private void TryDiscoverFromLocation(Settlement settlement, string locationId)
        {
            if (!LmmiSettingsProvider.EnableDynamicDiscovery) return;
            if (settlement == null || string.IsNullOrEmpty(locationId)) return;

            if (SettlementMenuOptions.LocationFeatureMap.TryGetValue(locationId, out var feature))
            {
                LmmiLog.Debug($"Location '{locationId}' maps to feature '{feature}' - attempting discovery in {settlement.Name}.");
                OnFeatureUnlocked(feature, settlement);
            }
        }

        private void TryDiscoverFromConversationPartner()
        {
            if (!LmmiSettingsProvider.EnableDynamicDiscovery) return;

            var settlement = Settlement.CurrentSettlement;
            if (settlement == null || settlement.IsVillage) return;

            var partner = CharacterObject.OneToOneConversationCharacter;
            if (partner == null) return;

            LmmiLog.Debug($"DEBUG: TryDiscoverFromConversationPartner called. Occupation: {partner.Occupation}");

            if (OccupationFeatureMap.TryGetValue(partner.Occupation, out var feature))
            {
                LmmiLog.Debug($"DEBUG: Occupation '{partner.Occupation}' maps to feature '{feature}' in {settlement.Name}.");
                OnFeatureUnlocked(feature, settlement);
            }
            else
            {
                LmmiLog.Debug($"DEBUG: Occupation '{partner.Occupation}' has NO mapping.");
            }
        }

        protected void AddDiscoveryDialog(CampaignGameStarter campaignGameStarter)
        {
            campaignGameStarter.AddDialogLine(
                "lmmi_discovery_check",
                "start",
                "lmmi_discovery_skip",
                string.Empty,
                () => {
                    TryDiscoverFromConversationPartner();
                    return false;
                },
                null,
                1000
            );
        }

        // ===================== Escort State =====================
        [NonSerialized] private string? _pendingNavLocationId;
        [NonSerialized] private string? _pendingNavFeature;
        [NonSerialized] private Agent? _pendingNpcAgent;
        [NonSerialized] private Settlement? _pendingSettlement;
        [NonSerialized] private bool _performEscortSetupPending;

        private bool IsInTownCenter()
        {
            var s = Settlement.CurrentSettlement;
            if (s == null || !s.IsTown) return false;
            return CampaignMission.Current?.Location?.StringId == "center";
        }

        // ===================== Escort Dialog Conditions =====================

        /// <summary>
        /// Fires when escort NPC has arrived at destination — shows "Here we are!" dialog.
        /// Priority 200 prevents vanilla's crashing escort dialog from running.
        /// </summary>
        private bool IsEscortArrivalConversation()
        {
            if (!_escortBehavior.IsActive || !_escortBehavior.IsArrived) return false;
            var convAgent = ConversationMission.OneToOneConversationAgent;
            if (convAgent == null || convAgent != _escortBehavior.NpcAgent) return false;

            // Set destination display name for the dialog text
            string feature = _escortBehavior.TargetFeature ?? string.Empty;
            string destName = SettlementMenuOptions.GetFeatureDisplayName(feature);
            MBTextManager.SetTextVariable("ESCORT_DESTINATION", destName);
            return true;
        }

        /// <summary>
        /// Fires when player interrupts the escort NPC mid-journey.
        /// Priority 200 prevents vanilla's crashing escort dialog from running.
        /// </summary>
        private bool IsEscortInProgressConversation()
        {
            if (!_escortBehavior.IsActive || _escortBehavior.IsArrived) return false;
            var convAgent = ConversationMission.OneToOneConversationAgent;
            if (convAgent == null || convAgent != _escortBehavior.NpcAgent) return false;

            // Pick location-specific flavor text
            string feature = _escortBehavior.TargetFeature ?? string.Empty;
            float rand = MBRandom.RandomFloat;
            string text;

            if (feature == SettlementMenuOptions.Features.Backstreet)
                text = rand < 0.5f ? "The tavern is just around the corner..." : "Nearly at the tavern now...";
            else if (feature == SettlementMenuOptions.Features.Trade)
                text = rand < 0.5f ? "The marketplace is just ahead..." : "We're almost at the marketplace...";
            else if (feature == SettlementMenuOptions.Features.Arena)
                text = rand < 0.5f ? "The arena is close by, I can hear the crowds..." : "Nearly at the arena...";
            else if (feature == SettlementMenuOptions.Features.Smithy)
                text = rand < 0.5f ? "The smithy is nearby, can you smell the forge?" : "Almost at the smithy...";
            else if (feature == SettlementMenuOptions.Features.Keep)
                text = rand < 0.5f ? "The lord's hall is just ahead..." : "We're nearly at the lord's hall...";
            else
                text = "It's just over there...";

            MBTextManager.SetTextVariable("ESCORT_ENROUTE_TEXT", text);
            return true;
        }

        /// <summary>
        /// Called from arrival dialog — unlocks the feature and finishes the escort.
        /// </summary>
        private void CompleteEscortArrival()
        {
            LmmiLog.Info("CompleteEscortArrival: Player acknowledged arrival.");
            _escortBehavior.CompleteEscortArrival();
        }

        /// <summary>
        /// Called when player cancels escort mid-journey.
        /// </summary>
        private void CancelEscort()
        {
            LmmiLog.Info("CancelEscort: Player cancelled escort.");
            _escortBehavior.Cancel();
        }

        // ===================== Direction Dialogs =====================

        protected void AddTownDirectionsDialogs(CampaignGameStarter starter)
        {
            // === ESCORT ARRIVAL DIALOG (priority 200) ===
            // Fires BEFORE vanilla's escort dialog which crashes on TargetAgent.Character when TargetAgent is null.
            starter.AddDialogLine(
                "lmmi_escort_arrived",
                "start",
                "lmmi_escort_arrived_resp",
                "{=lmmi_arrived}Here we are! This is the {ESCORT_DESTINATION}.",
                () => IsEscortArrivalConversation(),
                null,
                200
            );
            starter.AddPlayerLine(
                "lmmi_escort_arrived_thanks",
                "lmmi_escort_arrived_resp",
                "close_window",
                "{=lmmi_arrived_thanks}Thank you, I appreciate it.",
                null,
                () => CompleteEscortArrival()
            );

            // === ESCORT EN-ROUTE INTERCEPT (priority 200) ===
            // Fires when player talks to escorting NPC before reaching destination.
            starter.AddDialogLine(
                "lmmi_escort_enroute",
                "start",
                "lmmi_escort_enroute_resp",
                "{=lmmi_enroute}{ESCORT_ENROUTE_TEXT}",
                () => IsEscortInProgressConversation(),
                null,
                200
            );
            starter.AddPlayerLine(
                "lmmi_escort_keep_going",
                "lmmi_escort_enroute_resp",
                "close_window",
                "{=lmmi_keep_going}Let's keep going.",
                null,
                null
            );
            starter.AddPlayerLine(
                "lmmi_escort_cancel_opt",
                "lmmi_escort_enroute_resp",
                "close_window",
                "{=lmmi_cancel_esc}Never mind, I'll find it myself.",
                null,
                () => CancelEscort()
            );

            // === DIRECTION REQUEST DIALOGS ===
            starter.AddPlayerLine(
                "lmmi_dirs_townsfolk_ask",
                "town_or_village_player",
                "lmmi_dirs_where_resp",
                "{=lmmi_dirs_ask}Can you show me way somewhere?",
                () =>
                {
                    if (!IsInTownCenter()) return false;
                    var partner = CharacterObject.OneToOneConversationCharacter;
                    if (partner == null || partner == _localGuide) return false;
                    var s = Settlement.CurrentSettlement;
                    return s != null && HasAnyUndiscoveredFeature(s);
                },
                null
            );

            starter.AddPlayerLine(
                "lmmi_dirs_notable_ask",
                "hero_main_options",
                "lmmi_dirs_where_resp",
                "{=lmmi_dirs_ask}Can you show me way somewhere?",
                () =>
                {
                    if (!IsInTownCenter()) return false;
                    var partner = CharacterObject.OneToOneConversationCharacter;
                    if (partner == null || partner == _localGuide) return false;
                    var s = Settlement.CurrentSettlement;
                    return s != null && HasAnyUndiscoveredFeature(s);
                },
                null
            );

            starter.AddDialogLine(
                "lmmi_dirs_where_resp_line",
                "lmmi_dirs_where_resp",
                "lmmi_dirs_choices",
                "{=lmmi_dirs_where}Where would you like to go?",
                null,
                null
            );

            starter.AddPlayerLine("lmmi_dirs_to_tavern", "lmmi_dirs_choices", "lmmi_dirs_follow",
                "{=lmmi_dirs_tavern}The tavern.",
                () => !HasFeatureAccess(Settlement.CurrentSettlement, SettlementMenuOptions.Features.Backstreet),
                () => { _pendingNavLocationId = "tavern"; _pendingNavFeature = SettlementMenuOptions.Features.Backstreet; });
            starter.AddPlayerLine("lmmi_dirs_to_market", "lmmi_dirs_choices", "lmmi_dirs_follow",
                "{=lmmi_dirs_market}The marketplace.",
                () => !HasFeatureAccess(Settlement.CurrentSettlement, SettlementMenuOptions.Features.Trade),
                () => { _pendingNavLocationId = "center"; _pendingNavFeature = SettlementMenuOptions.Features.Trade; });
            starter.AddPlayerLine("lmmi_dirs_to_smithy", "lmmi_dirs_choices", "lmmi_dirs_follow",
                "{=lmmi_dirs_smithy}The smithy.",
                () => !HasFeatureAccess(Settlement.CurrentSettlement, SettlementMenuOptions.Features.Smithy),
                () => { _pendingNavLocationId = "smithy"; _pendingNavFeature = SettlementMenuOptions.Features.Smithy; });
            starter.AddPlayerLine("lmmi_dirs_to_arena", "lmmi_dirs_choices", "lmmi_dirs_follow",
                "{=lmmi_dirs_arena}The arena.",
                () => !HasFeatureAccess(Settlement.CurrentSettlement, SettlementMenuOptions.Features.Arena),
                () => { _pendingNavLocationId = "arena"; _pendingNavFeature = SettlementMenuOptions.Features.Arena; });
            starter.AddPlayerLine("lmmi_dirs_to_keep", "lmmi_dirs_choices", "lmmi_dirs_follow",
                "{=lmmi_dirs_keep}The lord's hall.",
                () => !HasFeatureAccess(Settlement.CurrentSettlement, SettlementMenuOptions.Features.Keep),
                () => { _pendingNavLocationId = "lordshall"; _pendingNavFeature = SettlementMenuOptions.Features.Keep; });
            starter.AddPlayerLine("lmmi_dirs_nevermind", "lmmi_dirs_choices", "close_window",
                "{=lmmi_dirs_never}Never mind.", null, null);

            starter.AddDialogLine(
                "lmmi_dirs_follow_line",
                "lmmi_dirs_follow",
                "close_window",
                "{=lmmi_dirs_followme}Follow me!",
                null,
                () => BeginEscort()
            );
        }

        private bool HasAnyUndiscoveredFeature(Settlement settlement)
        {
            if (HasAccessToSettlement(settlement)) return false;
            return !HasFeatureAccess(settlement, SettlementMenuOptions.Features.Backstreet)
                || !HasFeatureAccess(settlement, SettlementMenuOptions.Features.Trade)
                || !HasFeatureAccess(settlement, SettlementMenuOptions.Features.Smithy)
                || !HasFeatureAccess(settlement, SettlementMenuOptions.Features.Arena)
                || !HasFeatureAccess(settlement, SettlementMenuOptions.Features.Keep);
        }

        private void BeginEscort()
        {
            // NOTE: Do NOT clear _pendingNavLocationId/_pendingNavFeature here.
            // PerformEscortSetup() reads them from OnMissionTick after dialog closes.
            var locationId = _pendingNavLocationId;
            var feature = _pendingNavFeature;

            if (string.IsNullOrEmpty(locationId) || string.IsNullOrEmpty(feature)) return;

            var settlement = Settlement.CurrentSettlement;
            if (settlement == null) return;

            // Capture agent NOW while conversation is still open
            var npcAgent = ConversationMission.OneToOneConversationAgent;
            LmmiLog.Info($"BeginEscort: Starting escort to '{locationId}' for '{feature}'. NPC={(npcAgent != null ? npcAgent.Name + " id=" + npcAgent.Index : "NULL")}");

            if (npcAgent == null)
            {
                LmmiLog.Warning("BeginEscort: OneToOneConversationAgent is null — cannot escort.");
                return;
            }

            _pendingNpcAgent = npcAgent;
            _pendingSettlement = settlement;

            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=lmmi_escort_start}Follow me to the {LOCATION}!")
                    .SetTextVariable("LOCATION", feature).ToString()));

            _performEscortSetupPending = true;
        }

        private void PerformEscortSetup()
        {
            var locationId = _pendingNavLocationId;
            var feature = _pendingNavFeature;
            var npcAgent = _pendingNpcAgent;
            var settlement = _pendingSettlement;

            _pendingNavLocationId = null;
            _pendingNavFeature = null;
            _pendingNpcAgent = null;
            _pendingSettlement = null;
            _performEscortSetupPending = false;

            LmmiLog.Info($"PerformEscortSetup: loc='{locationId}' feat='{feature}' npc={(npcAgent != null ? npcAgent.Name + " id=" + npcAgent.Index : "NULL")} settlement={(settlement != null ? settlement.Name.ToString() : "NULL")}");

            if (string.IsNullOrEmpty(locationId) || string.IsNullOrEmpty(feature))
            {
                LmmiLog.Warning("PerformEscortSetup: No pending location/feature.");
                return;
            }
            if (npcAgent == null || !npcAgent.IsActive())
            {
                LmmiLog.Warning("PerformEscortSetup: NPC agent null or inactive.");
                return;
            }
            if (settlement == null)
            {
                LmmiLog.Warning("PerformEscortSetup: No settlement.");
                return;
            }

            Vec3? destinationPos = null;

            if (locationId == "center")
            {
                // Marketplace: navigate to a shop worker NPC (not a Notable merchant hero)
                Agent? merchantAgent = EscortBehavior.FindMerchantAgent(npcAgent);
                if (merchantAgent != null)
                {
                    destinationPos = merchantAgent.Position;
                    LmmiLog.Info($"PerformEscortSetup: Using shop worker agent at {destinationPos}");
                }
                else
                {
                    LmmiLog.Warning("PerformEscortSetup: No shop worker found for 'center'.");
                }
            }
            else if (locationId == "smithy")
            {
                // Smithy: navigate to the blacksmith NPC in the town center
                Agent? blacksmithAgent = EscortBehavior.FindBlacksmithAgent(npcAgent);
                if (blacksmithAgent != null)
                {
                    destinationPos = blacksmithAgent.Position;
                    LmmiLog.Info($"PerformEscortSetup: Using blacksmith agent at {destinationPos}");
                }
                else
                {
                    // Fallback: try passage just in case
                    destinationPos = EscortBehavior.FindPassagePosition(locationId);
                    if (destinationPos.HasValue)
                        LmmiLog.Info($"PerformEscortSetup: Smithy fallback passage at {destinationPos}");
                    else
                        LmmiLog.Warning("PerformEscortSetup: No blacksmith agent or passage found for 'smithy'.");
                }
            }
            else
            {
                destinationPos = EscortBehavior.FindPassagePosition(locationId);
                if (destinationPos.HasValue)
                    LmmiLog.Info($"PerformEscortSetup: Passage at {destinationPos}");
                else
                    LmmiLog.Warning($"PerformEscortSetup: FindPassagePosition null for '{locationId}'.");
            }

            if (!destinationPos.HasValue)
            {
                LmmiLog.Warning($"PerformEscortSetup: No destination for '{locationId}' — aborted.");
                return;
            }

            LmmiLog.Info($"PerformEscortSetup: StartEscort NPC={npcAgent.Name} dest={destinationPos.Value} feat={feature}");
            _escortBehavior.StartEscort(npcAgent, destinationPos.Value, feature, settlement);
        }

        private void OnMissionTick(float dt)
        {
            if (_performEscortSetupPending && Mission.Current != null && Mission.Current.Mode != MissionMode.Conversation)
            {
                LmmiLog.Info("OnMissionTick: Conversation ended - performing escort setup.");
                _performEscortSetupPending = false;
                PerformEscortSetup();
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_settlementsWithAccess", ref settlementsWithAccess);
            dataStore.SyncData("_settlementsFeaturesAccessed", ref settlementsFeaturesAccessed);
        }

        public static class HarmonyPatches
        {
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
                    if (!SettlementMenuOptions.OptionFeatureMap.TryGetValue(optionId, out var requiredFeature))
                        return true;

                    var campaign = Campaign.Current;
                    if (campaign == null)
                        return true;

                    var behaviorInstance = campaign.GetCampaignBehavior<DisableMenuBehavior>();
                    if (behaviorInstance == null)
                        return true;

                    var originalCondition = condition;

                    condition = (MenuCallbackArgs args) =>
                    {
                        var currentSettlement = MobileParty.MainParty?.CurrentSettlement ?? Settlement.CurrentSettlement;

                        bool isOriginalConditionMet = originalCondition == null || originalCondition(args);

                        if (currentSettlement == null)
                            return isOriginalConditionMet;

                        bool hasFeature = behaviorInstance.HasFeatureAccess(currentSettlement, requiredFeature);
                        bool finalEnabled = isOriginalConditionMet && hasFeature;
                        args.IsEnabled = finalEnabled;

                        if (!hasFeature)
                        {
                            args.Tooltip = new TextObject("{=lmmi_undiscovered}You haven't discovered this part of settlement yet.");
                        }

                        return args.IsEnabled;
                    };

                    return true;
                }
            }

            [HarmonyPatch(typeof(PlayerTownVisitCampaignBehavior))]
            [HarmonyPatch("game_menu_recruit_volunteers_on_condition")]
            public static class VillageRecruitmentPatch
            {
                [HarmonyPrefix]
                public static bool Prefix(MenuCallbackArgs args, ref bool __result)
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Recruit;

                    if (Settlement.CurrentSettlement == null)
                        return true;

                    if (Settlement.CurrentSettlement.IsVillage)
                    {
                        if (Settlement.CurrentSettlement.Village.VillageState != Village.VillageStates.Normal)
                        {
                            __result = false;
                            return false;
                        }

                        bool disableOption;
                        TextObject disabledText;
                        bool canPlayerDo = Campaign.Current.Models.SettlementAccessModel.CanMainHeroDoSettlementAction(
                            Settlement.CurrentSettlement,
                            SettlementAccessModel.SettlementAction.RecruitTroops,
                            out disableOption,
                            out disabledText);

                        __result = MenuHelper.SetOptionProperties(args, canPlayerDo, disableOption, disabledText);
                        return false;
                    }

                    return true;
                }
            }
        }
    }
}
