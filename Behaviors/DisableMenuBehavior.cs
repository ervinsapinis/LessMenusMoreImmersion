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
using TaleWorlds.CampaignSystem.Conversation;
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

        [NonSerialized] private EscortBehavior _escortBehavior = new EscortBehavior();

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
            { Occupation.Blacksmith, SettlementMenuOptions.Features.Smithy },
            { Occupation.Armorer, SettlementMenuOptions.Features.Smithy },
            { Occupation.Weaponsmith, SettlementMenuOptions.Features.Smithy },
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

            // Tavern district (Backstreet) discovery implies you now also know how to reach
            // the common areas. We treat these as bundled to avoid repetitive / buggy escorting.
            if (feature == SettlementMenuOptions.Features.Backstreet)
            {
                // Add silently (no extra discovery popups)
                if (!features.Contains(SettlementMenuOptions.Features.Alley))
                    features.Add(SettlementMenuOptions.Features.Alley);
                if (!features.Contains(SettlementMenuOptions.Features.Waterfront))
                    features.Add(SettlementMenuOptions.Features.Waterfront);
                if (!features.Contains(SettlementMenuOptions.Features.Clearing))
                    features.Add(SettlementMenuOptions.Features.Clearing);

                LmmiLog.Debug($"Backstreet discovered -> also unlocking Alley/Waterfront/Clearing in {settlement.Name}.");
            }

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

        // ===================== Citizen Willingness State =====================
        private enum CitizenWillingness
        {
            Undetermined,
            AlreadyHelped,
            TooBusy,
            Foreigner,
            Glad,
            Greedy
        }

        private static string PickOne(params string[] options)
        {
            if (options == null || options.Length == 0)
                return string.Empty;

            return options[MBRandom.RandomInt(options.Length)];
        }

        private static string GetForeignerInsultText()
        {
            string cultureId = Hero.MainHero?.Culture?.StringId ?? string.Empty;
            cultureId = cultureId?.ToLowerInvariant() ?? string.Empty;

            // Sturgia ruler name (for one of the Sturgia variants)
            string sturgiaRuler = "their king";
            try
            {
                var sturgia = Kingdom.All?.FirstOrDefault(k => k?.Culture?.StringId == "sturgia");
                var rulerName = sturgia?.RulingClan?.Leader?.Name?.ToString();
                if (!string.IsNullOrEmpty(rulerName))
                    sturgiaRuler = rulerName;
            }
            catch
            {
                // ignore; keep fallback
            }

            // Culture-specific bigotry variants
            if (cultureId == "empire")
            {
                return PickOne(
                    "Ugh, imperials... haven't you polluted enough of the world? Get lost.",
                    "Another imperial thinking they own the place. We bow to no emperor here.",
                    "Take your imperial arrogance elsewhere. You're not wanted."
                );
            }
            if (cultureId == "aserai")
            {
                return PickOne(
                    "Crawl back to whatever sandhole you came out of, dog.",
                    "We don't serve desert rats here. Go peddle your wares elsewhere.",
                    "The stink of the Nahasa follows you, stranger. Move along."
                );
            }
            if (cultureId == "khuzait")
            {
                return PickOne(
                    "We have no love for horse fondlers here, stranger. Go back to your decrepit steppe.",
                    "A Khuzait? In our town? Go back to your yurt, nomad.",
                    "I can smell the horse dung from here. Off with you."
                );
            }
            if (cultureId == "nord")
            {
                return PickOne(
                    "How about I call the guards and have you thrown out? Go back to the sea, you are not welcome here.",
                    "Another sea raider crawled ashore, did they? Get lost.",
                    "Take your longboat and shove off, Nord. We've nothing for you."
                );
            }
            if (cultureId == "battania")
            {
                return PickOne(
                    "Sure... wait, what is that accent... We do not tolerate filthy barbarians here. Leave, while you can.",
                    "A forest dweller? In civilized lands? Go hug a tree somewhere else.",
                    "I don't deal with painted savages. Away with you."
                );
            }
            if (cultureId == "sturgia")
            {
                return PickOne(
                    $"Hahaha, what? Your wits really have frozen, stranger. Crawl back to {sturgiaRuler}'s lap.",
                    "You Sturgians are all the same — half-drunk and lost. Find your own way.",
                    "Go back to your frozen wasteland, snowman. We don't help your kind."
                );
            }
            if (cultureId == "vlandia")
            {
                return PickOne(
                    "Ahh, a passionate Vlandian... I'll be sure to give my regards to the next goat I see. Now leave before I call the guards.",
                    "A Vlandian, eh? Don't you have some peasants to tax or a field to plow? Off with you.",
                    "I've had enough of Vlandian 'knights' stumbling through our streets. Find your own way."
                );
            }

            return PickOne(
                "I don't help outsiders around here. Move along.",
                "You're not from around here. Figure it out yourself.",
                "We don't take kindly to strangers. Best be on your way."
            );
        }

        [NonSerialized] private CitizenWillingness _currentWillingness = CitizenWillingness.Undetermined;
        [NonSerialized] private int _bribeAmount;
        [NonSerialized] private Dictionary<int, CitizenWillingness> _agentWillingnessMap = new Dictionary<int, CitizenWillingness>();
        [NonSerialized] private Dictionary<int, int> _agentBribeMap = new Dictionary<int, int>();
        // Refusal persistence (per settlement visit)
        [NonSerialized] private Dictionary<int, CitizenWillingness> _agentRefusalTypeMap = new Dictionary<int, CitizenWillingness>();
        [NonSerialized] private Dictionary<int, string> _agentRefusalTextMap = new Dictionary<int, string>();
        [NonSerialized] private Settlement? _lastWillingnessSettlement;

        private void EnsureWillingnessCacheForSettlement(Settlement? settlement)
        {
            if (settlement == null) return;

            // Reset cache if we entered a new settlement
            if (_lastWillingnessSettlement != settlement)
            {
                _agentWillingnessMap.Clear();
                _agentBribeMap.Clear();
                _agentRefusalTypeMap.Clear();
                _agentRefusalTextMap.Clear();
                _lastWillingnessSettlement = settlement;
            }
        }

        private bool ShouldRepeatStoredRefusal(out string refusalText)
        {
            refusalText = string.Empty;

            if (!IsInTownCenter()) return false;
            if (_escortBehavior.IsActive) return false; // don't steal escort intercepts

            var partner = CharacterObject.OneToOneConversationCharacter;
            if (partner == null || partner.IsHero) return false;

            var settlement = Settlement.CurrentSettlement;
            if (settlement == null || !settlement.IsTown) return false;
            EnsureWillingnessCacheForSettlement(settlement);

            var agent = ConversationMission.OneToOneConversationAgent;
            if (agent == null) return false;

            if (!_agentRefusalTextMap.TryGetValue(agent.Index, out var storedText))
                return false;

            if (string.IsNullOrEmpty(storedText))
                return false;

            refusalText = storedText;
            return true;
        }

        // ===================== Escort State =====================
        [NonSerialized] private string? _pendingNavLocationId;
        [NonSerialized] private string? _pendingNavFeature;
        [NonSerialized] private Agent? _pendingNpcAgent;
        [NonSerialized] private Settlement? _pendingSettlement;
        [NonSerialized] private bool _performEscortSetupPending;

        private void CalculateWillingness()
        {
            var partner = CharacterObject.OneToOneConversationCharacter;
            var agent = ConversationMission.OneToOneConversationAgent;
            var settlement = Settlement.CurrentSettlement;
            var player = Hero.MainHero;

            if (partner == null || settlement == null || player == null || agent == null)
            {
                _currentWillingness = CitizenWillingness.Glad;
                return;
            }

            EnsureWillingnessCacheForSettlement(settlement);

            int agentId = agent.Index;

            // Did we already roll for this NPC?
            if (_agentWillingnessMap.TryGetValue(agentId, out var existingWillingness))
            {
                _currentWillingness = existingWillingness;
                if (existingWillingness == CitizenWillingness.Greedy && _agentBribeMap.TryGetValue(agentId, out var existingBribe))
                {
                    _bribeAmount = existingBribe;
                    MBTextManager.SetTextVariable("BRIBE_AMOUNT", _bribeAmount);
                }
                return;
            }

            // Owner or kingdom member? Always glad to help.
            if (settlement.OwnerClan == player.Clan ||
                (player.Clan.Kingdom != null && player.Clan.Kingdom == settlement.OwnerClan.Kingdom))
            {
                _currentWillingness = CitizenWillingness.Glad;
                _agentWillingnessMap[agentId] = _currentWillingness;
                return;
            }

            // Roll for it
            int busyWeight = 0, foreignerWeight = 0, gladWeight = 0, greedyWeight = 0;
            switch (player.Clan.Tier)
            {
                case 0:
                case 1: busyWeight = 30; foreignerWeight = 25; gladWeight = 25; greedyWeight = 20; break;
                case 2: busyWeight = 20; foreignerWeight = 15; gladWeight = 40; greedyWeight = 25; break;
                default: busyWeight = 0; foreignerWeight = 0; gladWeight = 60; greedyWeight = 40; break;
            }

            // Same culture bonus
            if (partner.Culture == player.Culture)
            {
                gladWeight += foreignerWeight;
                foreignerWeight = 0;
            }

            int total = busyWeight + foreignerWeight + gladWeight + greedyWeight;
            int roll = MBRandom.RandomInt(total);

            if ((roll -= busyWeight) < 0) _currentWillingness = CitizenWillingness.TooBusy;
            else if ((roll -= foreignerWeight) < 0) _currentWillingness = CitizenWillingness.Foreigner;
            else if ((roll -= gladWeight) < 0) _currentWillingness = CitizenWillingness.Glad;
            else _currentWillingness = CitizenWillingness.Greedy;

            if (_currentWillingness == CitizenWillingness.Greedy)
            {
                _bribeAmount = (int)(player.Gold * 0.01f) + (10 * player.Clan.Tier) + 10;
                _bribeAmount = Math.Min(_bribeAmount, 500); // Cap at 500
                if (partner.Culture == player.Culture)
                    _bribeAmount /= 2; // Same culture discount
                
                MBTextManager.SetTextVariable("BRIBE_AMOUNT", _bribeAmount);
                _agentBribeMap[agentId] = _bribeAmount;
            }

            _agentWillingnessMap[agentId] = _currentWillingness;
        }

        private void CalculateWillingnessForNotable()
        {
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null)
            {
                _currentWillingness = CitizenWillingness.Glad;
                return;
            }

            EnsureWillingnessCacheForSettlement(settlement);

            var agent = ConversationMission.OneToOneConversationAgent;
            if (agent == null)
            {
                _currentWillingness = CitizenWillingness.Glad;
                return;
            }

            int agentId = agent.Index;

            // If we already have a cached state (e.g. AlreadyHelped), respect it.
            if (_agentWillingnessMap.TryGetValue(agentId, out var existingWillingness))
            {
                _currentWillingness = existingWillingness;
                return;
            }

            // Notables are always helpful the first time.
            _currentWillingness = CitizenWillingness.Glad;
            _agentWillingnessMap[agentId] = _currentWillingness;
        }

        private bool IsInTownCenter()
        {
            var s = Settlement.CurrentSettlement;
            if (s == null || !s.IsTown) return false;
            return CampaignMission.Current?.Location?.StringId == "center";
        }

        private static List<Hero> HeroesToLookForInScene()
        {
            var result = new List<Hero>();

            try
            {
                var convoAgent = ConversationMission.OneToOneConversationAgent;
                if (convoAgent == null) return result;
                if (Mission.Current == null) return result;

                Vec3 position = convoAgent.Position;
                foreach (Agent agent in Mission.Current.Agents)
                {
                    if (agent == null || !agent.IsHuman || !agent.IsHero || agent.State != AgentState.Active)
                        continue;

                    var hero = ((CharacterObject)agent.Character).HeroObject;
                    if (hero != null && !hero.IsLord && position.Distance(agent.Position) > 6f)
                    {
                        result.Add(hero);
                    }
                }
            }
            catch (Exception ex)
            {
                LmmiLog.Error("HeroesToLookForInScene: failed", ex);
            }

            return result;
        }

        private static bool IsMainAgentBeingEscortedVanillaSafe()
        {
            try
            {
                if (Mission.Current == null) return false;
                if (Agent.Main == null || !Agent.Main.IsActive()) return false;

                foreach (Agent agent in Mission.Current.Agents)
                {
                    if (agent == null) continue;
                    if (!agent.IsActive()) continue;
                    if (SandBox.Missions.AgentBehaviors.EscortAgentBehavior.CheckIfAgentIsEscortedBy(agent, Agent.Main))
                        return true;
                }
            }
            catch
            {
                // swallow; treat as not escorted
            }

            return false;
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
            MBTextManager.SetTextVariable("ESCORT_ARRIVAL_TEXT", PickOne(
                $"Here we are! This is the {destName}.",
                $"We've arrived. The {destName}, as promised.",
                $"There you go — the {destName}. You can't miss it."
            ));
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
            string text;

            if (feature == SettlementMenuOptions.Features.Backstreet)
                text = PickOne("The tavern is just around the corner...", "Nearly at the tavern now...", "This way — the tavern won't be far.");
            else if (feature == SettlementMenuOptions.Features.Trade)
                text = PickOne("The marketplace is just ahead...", "We're almost at the marketplace...", "Keep your eyes open — the market is nearby.");
            else if (feature == SettlementMenuOptions.Features.Arena)
                text = PickOne("The arena is close by, I can hear the crowds...", "Nearly at the arena...", "Hear that roar? That's the arena — we're close.");
            else if (feature == SettlementMenuOptions.Features.Smithy)
                text = PickOne("The smithy is nearby, can you smell the forge?", "Almost at the smithy...", "The hammering should guide us — the smithy is close.");
            else if (feature == SettlementMenuOptions.Features.Keep)
                text = PickOne("The lord's hall is just ahead...", "We're nearly at the lord's hall...", "Stay sharp. The keep is close.");
            else
                text = PickOne("It's just over there...", "Not much further...", "We're close now...");

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
            // === REPEAT STORED REFUSAL (priority 1000) ===
            // If an NPC already refused to help (TooBusy / Foreigner / AlreadyHelped),
            // repeat the exact same refusal line and close immediately.
            // This also suppresses vanilla street opener lines.
            starter.AddDialogLine(
                "lmmi_repeat_refusal",
                "start",
                "close_window",
                "{=lmmi_repeat_refusal}{LMMI_REPEAT_REFUSAL_TEXT}",
                () => {
                    if (!ShouldRepeatStoredRefusal(out var refusalText)) return false;
                    MBTextManager.SetTextVariable("LMMI_REPEAT_REFUSAL_TEXT", refusalText);
                    return true;
                },
                null,
                1000
            );

            // === ESCORT ARRIVAL DIALOG (priority 200) ===
            // Fires BEFORE vanilla's escort dialog which crashes on TargetAgent.Character when TargetAgent is null.
            starter.AddDialogLine(
                "lmmi_escort_arrived",
                "start",
                "lmmi_escort_arrived_resp",
                "{=lmmi_arrived}{ESCORT_ARRIVAL_TEXT}",
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

            // === DIRECTION REQUEST DIALOG (entry point) ===
            // This is the player's opening line. On consequence, we roll for citizen willingness.
            starter.AddPlayerLine(
                "lmmi_dirs_ask",
                "town_or_village_player",
                "lmmi_dirs_willingness_check",
                "{=lmmi_dirs_ask}Can you show me the way somewhere?",
                () => {
                    if (!IsInTownCenter()) return false;
                    var partner = CharacterObject.OneToOneConversationCharacter;
                    if (partner == null || partner.IsHero || partner == _localGuide) return false;
                    var s = Settlement.CurrentSettlement;
                    return s != null && HasAnyUndiscoveredFeature(s);
                },
                () => CalculateWillingness()
            );

            // Add for notables as well
             starter.AddPlayerLine(
                "lmmi_dirs_notable_ask",
                "hero_main_options",
                "lmmi_dirs_willingness_check",
                "{=lmmi_dirs_ask}Can you show me the way somewhere?",
                 () => {
                    if (!IsInTownCenter()) return false;
                    var partner = CharacterObject.OneToOneConversationCharacter;
                    if (partner == null || !partner.IsHero || partner == _localGuide) return false;
                    var s = Settlement.CurrentSettlement;
                    return s != null && HasAnyUndiscoveredFeature(s);
                },
                () => CalculateWillingnessForNotable()
            );

            // === WILLINGNESS CHECK & DIVERGENCE ===
            // This is a dummy state that immediately diverges based on the roll.
            starter.AddDialogLine("lmmi_dirs_willingness_check_nonverbal", "lmmi_dirs_willingness_check", "close_window", string.Empty, () => false, null);

            // OUTCOME 1: Too Busy
            starter.AddDialogLine("lmmi_dirs_busy_resp", "lmmi_dirs_willingness_check", "close_window",
                "{=lmmi_busy}{LMMI_DIRS_BUSY_TEXT}",
                () => {
                    if (_currentWillingness != CitizenWillingness.TooBusy) return false;

                    var settlement = Settlement.CurrentSettlement;
                    EnsureWillingnessCacheForSettlement(settlement);

                    var agent = ConversationMission.OneToOneConversationAgent;
                    if (agent != null)
                    {
                        if (!_agentRefusalTextMap.TryGetValue(agent.Index, out var storedText) || string.IsNullOrEmpty(storedText))
                        {
                            storedText = PickOne(
                                "Can't you see I'm busy? Find someone else.",
                                "I have things to do. Bother someone else.",
                                "Not now. I have somewhere to be."
                            );
                            _agentRefusalTypeMap[agent.Index] = CitizenWillingness.TooBusy;
                            _agentRefusalTextMap[agent.Index] = storedText;
                        }
                        MBTextManager.SetTextVariable("LMMI_DIRS_BUSY_TEXT", storedText);
                    }
                    else
                    {
                        MBTextManager.SetTextVariable("LMMI_DIRS_BUSY_TEXT", "Can't you see I'm busy? Find someone else.");
                    }
                    return true;
                }, null);

            // OUTCOME 1.5: Already helped (NPC won't escort twice)
            starter.AddDialogLine("lmmi_dirs_already_helped_resp", "lmmi_dirs_willingness_check", "close_window",
                "{=lmmi_already_helped}{LMMI_DIRS_ALREADY_HELPED_TEXT}",
                () => {
                    if (_currentWillingness != CitizenWillingness.AlreadyHelped) return false;

                    var settlement = Settlement.CurrentSettlement;
                    EnsureWillingnessCacheForSettlement(settlement);

                    var agent = ConversationMission.OneToOneConversationAgent;
                    if (agent != null)
                    {
                        if (!_agentRefusalTextMap.TryGetValue(agent.Index, out var storedText) || string.IsNullOrEmpty(storedText))
                        {
                            storedText = PickOne(
                                "I've already shown you around. Ask someone else.",
                                "I helped you once already. I have my own business to attend to.",
                                "Find another guide, friend. I've done my part."
                            );
                            _agentRefusalTypeMap[agent.Index] = CitizenWillingness.AlreadyHelped;
                            _agentRefusalTextMap[agent.Index] = storedText;
                        }
                        MBTextManager.SetTextVariable("LMMI_DIRS_ALREADY_HELPED_TEXT", storedText);
                    }
                    else
                    {
                        MBTextManager.SetTextVariable("LMMI_DIRS_ALREADY_HELPED_TEXT", "I've already shown you around. Ask someone else.");
                    }
                    return true;
                }, null);

            // OUTCOME 2: Foreigner
            starter.AddDialogLine("lmmi_dirs_foreigner_resp", "lmmi_dirs_willingness_check", "close_window",
                "{=lmmi_foreigner}{LMMI_DIRS_FOREIGNER_TEXT}",
                () => {
                    if (_currentWillingness != CitizenWillingness.Foreigner) return false;

                    var settlement = Settlement.CurrentSettlement;
                    EnsureWillingnessCacheForSettlement(settlement);

                    var agent = ConversationMission.OneToOneConversationAgent;
                    if (agent != null)
                    {
                        if (!_agentRefusalTextMap.TryGetValue(agent.Index, out var storedText) || string.IsNullOrEmpty(storedText))
                        {
                            storedText = GetForeignerInsultText();
                            _agentRefusalTypeMap[agent.Index] = CitizenWillingness.Foreigner;
                            _agentRefusalTextMap[agent.Index] = storedText;
                        }
                        MBTextManager.SetTextVariable("LMMI_DIRS_FOREIGNER_TEXT", storedText);
                    }
                    else
                    {
                        MBTextManager.SetTextVariable("LMMI_DIRS_FOREIGNER_TEXT", GetForeignerInsultText());
                    }
                    return true;
                }, null);

            // OUTCOME 3: Glad
            starter.AddDialogLine("lmmi_dirs_glad_resp", "lmmi_dirs_willingness_check", "lmmi_dirs_choices",
                "{=lmmi_glad}{LMMI_DIRS_GLAD_TEXT}",
                () => {
                    if (_currentWillingness != CitizenWillingness.Glad) return false;
                    MBTextManager.SetTextVariable("LMMI_DIRS_GLAD_TEXT", PickOne(
                        "Of course! Where would you like to go?",
                        "Happy to help! What are you looking for?",
                        "Sure thing! Where do you need to get to?"
                    ));
                    return true;
                }, null);

            // OUTCOME 4: Greedy
            starter.AddDialogLine("lmmi_dirs_greedy_resp", "lmmi_dirs_willingness_check", "lmmi_dirs_greedy_options",
                "{=lmmi_greedy}{LMMI_DIRS_GREEDY_TEXT}",
                () => {
                    if (_currentWillingness != CitizenWillingness.Greedy) return false;
                    MBTextManager.SetTextVariable("LMMI_DIRS_GREEDY_TEXT", PickOne(
                        "I know this town well... every back alley and shortcut. For {BRIBE_AMOUNT}{GOLD_ICON}, I'll take you wherever you need to go.",
                        "Directions? Nothing's free here. {BRIBE_AMOUNT}{GOLD_ICON} and I'll show you personally.",
                        "You look lost, friend. For {BRIBE_AMOUNT}{GOLD_ICON}, I could help... for a small fee."
                    ));
                    return true;
                }, null);

            starter.AddPlayerLine("lmmi_dirs_greedy_pay", "lmmi_dirs_greedy_options", "lmmi_dirs_choices_paid",
                "{=lmmi_greedy_pay}Agreed. Here is the coin.",
                () => Hero.MainHero.Gold >= _bribeAmount,
                () => { 
                    Hero.MainHero.ChangeHeroGold(-_bribeAmount); 
                    _agentWillingnessMap[ConversationMission.OneToOneConversationAgent.Index] = CitizenWillingness.Glad; 
                });

            starter.AddPlayerLine("lmmi_dirs_greedy_refuse", "lmmi_dirs_greedy_options", "close_window",
                "{=lmmi_greedy_refuse}I'll find my own way.", null, null);

            starter.AddDialogLine("lmmi_dirs_greedy_paid_resp", "lmmi_dirs_choices_paid", "lmmi_dirs_choices",
                "{=lmmi_greedy_paid_resp}{LMMI_DIRS_GREEDY_PAID_TEXT}",
                () => {
                    MBTextManager.SetTextVariable("LMMI_DIRS_GREEDY_PAID_TEXT", PickOne(
                        "Excellent. Where to?",
                        "A pleasure doing business. Now, where are we headed?",
                        "Coin well spent. Where do you need to go?"
                    ));
                    return true;
                }, null);

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

            starter.AddPlayerLine("lmmi_dirs_to_barber", "lmmi_dirs_choices", "lmmi_dirs_follow",
                "{=lmmi_dirs_barber}The barber.",
                () => !HasFeatureAccess(Settlement.CurrentSettlement, SettlementMenuOptions.Features.Barber),
                () => { _pendingNavLocationId = "barber"; _pendingNavFeature = SettlementMenuOptions.Features.Barber; });

            // Inject vanilla "I'm looking for someone" option inside our willingness check
            // IMPORTANT: do NOT route to hero_main_options; that triggers hero/clan dialog conditions
            // in a civilian conversation and can cause NREs (e.g. clan_member_dont_follow_me_on_condition).
            // Instead, route into the vanilla flow state "player_ask_hero_location" and populate repeat objects.
            starter.AddPlayerLine("lmmi_dirs_looking_for_someone", "lmmi_dirs_choices", "player_ask_hero_location",
                "{=X8R11a00}I'm looking for someone...",
                () => !IsMainAgentBeingEscortedVanillaSafe() && HeroesToLookForInScene().Count > 0,
                () => ConversationSentence.SetObjectsToRepeatOver(HeroesToLookForInScene(), 5));

            starter.AddPlayerLine("lmmi_dirs_nevermind", "lmmi_dirs_choices", "close_window",
                "{=lmmi_dirs_never}Never mind.", null, null);

            starter.AddDialogLine(
                "lmmi_dirs_follow_line",
                "lmmi_dirs_follow",
                "close_window",
                "{=lmmi_dirs_followme}{LMMI_DIRS_FOLLOW_TEXT}",
                () => {
                    // Do not start escort if the selected destination is closed at night.
                    if (CampaignTime.Now.IsNightTime)
                    {
                        bool isClosed = _pendingNavFeature == SettlementMenuOptions.Features.Trade ||
                                        _pendingNavFeature == SettlementMenuOptions.Features.Smithy ||
                                        _pendingNavFeature == SettlementMenuOptions.Features.Arena ||
                                        _pendingNavFeature == SettlementMenuOptions.Features.Barber;
                        if (isClosed) return false;
                    }

                    MBTextManager.SetTextVariable("LMMI_DIRS_FOLLOW_TEXT", PickOne(
                        "Follow me!",
                        "This way — stay close.",
                        "Right, let's go. Keep up."
                    ));
                    return true;
                },
                () => BeginEscort()
            );
            
            // Nighttime refusal dialog (for closed locations)
            starter.AddDialogLine(
                "lmmi_dirs_closed",
                "lmmi_dirs_follow",
                "lmmi_dirs_choices",
                "{=lmmi_dirs_closed}{LMMI_DIRS_CLOSED_TEXT}",
                () => {
                    if (!CampaignTime.Now.IsNightTime) return false;
                    bool isClosed = _pendingNavFeature == SettlementMenuOptions.Features.Trade ||
                                    _pendingNavFeature == SettlementMenuOptions.Features.Smithy ||
                                    _pendingNavFeature == SettlementMenuOptions.Features.Arena ||
                                    _pendingNavFeature == SettlementMenuOptions.Features.Barber;
                    if (!isClosed) return false;

                    MBTextManager.SetTextVariable("LMMI_DIRS_CLOSED_TEXT", PickOne(
                        "It's empty there right now. Come back during the day.",
                        "Nothing to see there at night. Try again in daylight.",
                        "They'll be closed at this hour. Come back tomorrow."
                    ));
                    return true;
                },
                () => { _pendingNavLocationId = null; _pendingNavFeature = null; }
            );
        }

        private bool HasAnyUndiscoveredFeature(Settlement settlement)
        {
            if (HasAccessToSettlement(settlement)) return false;
            // Only features that can be discovered via the directions/escort system.
            // Backstreet + common areas are discovered through exploration / tavern discovery.
            return !HasFeatureAccess(settlement, SettlementMenuOptions.Features.Trade)
                || !HasFeatureAccess(settlement, SettlementMenuOptions.Features.Smithy)
                || !HasFeatureAccess(settlement, SettlementMenuOptions.Features.Arena)
                || !HasFeatureAccess(settlement, SettlementMenuOptions.Features.Keep)
                || !HasFeatureAccess(settlement, SettlementMenuOptions.Features.Barber);
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

            // This NPC won't escort again during this settlement visit.
            _agentWillingnessMap[npcAgent.Index] = CitizenWillingness.AlreadyHelped;

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
            else if (locationId == "barber")
            {
                // Barber: navigate to the barber NPC in the town center
                Agent? barberAgent = EscortBehavior.FindBarberAgent(npcAgent);
                if (barberAgent != null)
                {
                    destinationPos = barberAgent.Position;
                    LmmiLog.Info($"PerformEscortSetup: Using barber agent at {destinationPos}");
                }
                else
                {
                    // Fallback: try common area just in case
                    destinationPos = EscortBehavior.FindPassagePosition(locationId);
                    if (destinationPos.HasValue)
                        LmmiLog.Info($"PerformEscortSetup: Barber fallback common area at {destinationPos}");
                    else
                        LmmiLog.Warning("PerformEscortSetup: No barber agent or common area found.");
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
            // Serialize ONLY as a plain string — avoids Bannerlord's container graph walker
            // which crashes on nested types (Dictionary<string, List<string>>) and
            // non-serializable objects (Agent, Settlement refs inside EscortBehavior).
            string savedData = string.Empty;
            if (!dataStore.IsLoading)
                savedData = BuildSaveString();

            dataStore.SyncData("lmmi_data_v1", ref savedData);

            if (dataStore.IsLoading)
                LoadSaveString(savedData);
        }

        /// <summary>
        /// Encodes all discovery state as a plain string safe for Bannerlord's save system.
        /// Format: entries separated by '|'
        ///   Full access: "F:{settlementId}"
        ///   Feature access: "D:{settlementId}:{feat1},{feat2}"
        /// </summary>
        private string BuildSaveString()
        {
            var parts = new List<string>();

            if (settlementsWithAccess != null)
                foreach (var kv in settlementsWithAccess)
                    if (kv.Value)
                        parts.Add("F:" + kv.Key);

            if (settlementsFeaturesAccessed != null)
                foreach (var kv in settlementsFeaturesAccessed)
                    if (kv.Value != null && kv.Value.Count > 0)
                        parts.Add("D:" + kv.Key + ":" + string.Join(",", kv.Value));

            var result = string.Join("|", parts);
            LmmiLog.Debug($"SyncData Save: {parts.Count} entries encoded.");
            return result;
        }

        /// <summary>
        /// Restores discovery state from a saved string.
        /// </summary>
        private void LoadSaveString(string savedData)
        {
            settlementsWithAccess = new Dictionary<string, bool>();
            settlementsFeaturesAccessed = new Dictionary<string, List<string>>();

            if (string.IsNullOrEmpty(savedData))
            {
                LmmiLog.Debug("SyncData Load: No saved data found.");
                return;
            }

            int loaded = 0;
            foreach (var part in savedData.Split('|'))
            {
                if (string.IsNullOrEmpty(part)) continue;

                if (part.StartsWith("F:"))
                {
                    var id = part.Substring(2);
                    if (!string.IsNullOrEmpty(id))
                    {
                        settlementsWithAccess[id] = true;
                        loaded++;
                    }
                }
                else if (part.StartsWith("D:"))
                {
                    var rest = part.Substring(2);
                    int colonIdx = rest.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        var id = rest.Substring(0, colonIdx);
                        var featureStr = rest.Substring(colonIdx + 1);
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(featureStr))
                        {
                            settlementsFeaturesAccessed[id] = new List<string>(featureStr.Split(','));
                            loaded++;
                        }
                    }
                }
            }

            LmmiLog.Debug($"SyncData Load: {loaded} entries restored.");
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

            [HarmonyPatch(typeof(CampaignGameStarter), "AddPlayerLine")]
            public static class BlockVanillaLookingForSomeonePatch
            {
                [HarmonyPrefix]
                public static bool Prefix(string id, string inputToken, string outputToken, string text, ref ConversationSentence.OnConditionDelegate conditionDelegate, ConversationSentence.OnConsequenceDelegate consequenceDelegate, int priority, ConversationSentence.OnClickableConditionDelegate clickableConditionDelegate = null, ConversationSentence.OnPersuasionOptionDelegate persuasionOptionDelegate = null)
                {
                    // We only want to intercept the vanilla "I'm looking for someone." player line:
                    // CommonVillagersCampaignBehavior registers it as:
                    //   id="player_ask_hero_location"
                    //   inputToken="town_or_village_player"
                    //   outputToken="player_ask_hero_location"
                    if (id == "player_ask_hero_location" && inputToken == "town_or_village_player" && outputToken == "player_ask_hero_location")
                    {
                        var originalCondition = conditionDelegate;

                        conditionDelegate = () =>
                        {
                            // Hide top-level "looking for someone" for town-center civilians.
                            // Must be reachable via our custom flow.
                            try
                            {
                                if (CampaignMission.Current?.Location?.StringId == "center")
                                {
                                    var s = Settlement.CurrentSettlement;
                                    if (s != null && s.IsTown)
                                    {
                                        var partner = CharacterObject.OneToOneConversationCharacter;
                                        if (partner != null && !partner.IsHero)
                                            return false;
                                    }
                                }

                                return originalCondition == null || originalCondition();
                            }
                            catch (Exception ex)
                            {
                                // Fail closed and log, instead of crashing the game.
                                LmmiLog.Error("BlockVanillaLookingForSomeonePatch: condition delegate threw", ex);
                                return false;
                            }
                        };
                    }
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