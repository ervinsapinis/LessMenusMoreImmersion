using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LessMenusMoreImmersion.Constants;
using LessMenusMoreImmersion.Contacts;
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

        /// <summary>An escort is walking the player somewhere (or about to) — other scripted scenes should wait.</summary>
        public bool IsEscortActive => _escortBehavior.IsActive || _performEscortSetupPending;

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
            CampaignEvents.GameMenuOpened.AddNonSerializedListener(this, OnGameMenuOpened);
        }

        private static readonly FieldInfo? _menuItemsField =
            typeof(GameMenu).GetField("_menuItems", BindingFlags.Instance | BindingFlags.NonPublic);

        [NonSerialized] private readonly HashSet<GameMenuOption> _gatedOptions = new HashSet<GameMenuOption>();

        private void OnGameMenuOpened(MenuCallbackArgs args)
        {
            try
            {
                var gameMenu = args.MenuContext?.GameMenu;
                if (gameMenu == null)
                {
                    LmmiLog.Debug("OnGameMenuOpened: gameMenu is null, skipping.");
                    return;
                }

                LmmiLog.Debug($"OnGameMenuOpened: menu='{gameMenu.StringId}'");

                var settlement = MobileParty.MainParty?.CurrentSettlement ?? Settlement.CurrentSettlement;
                if (settlement == null)
                {
                    LmmiLog.Debug("OnGameMenuOpened: no settlement context, skipping.");
                    return;
                }

                if (_menuItemsField == null)
                {
                    LmmiLog.Warning("OnGameMenuOpened: '_menuItems' field not found — BL API may have changed.");
                    return;
                }

                var menuItems = _menuItemsField.GetValue(gameMenu) as List<GameMenuOption>;
                if (menuItems == null)
                {
                    LmmiLog.Debug("OnGameMenuOpened: _menuItems returned null.");
                    return;
                }

                LmmiLog.Debug($"OnGameMenuOpened: {menuItems.Count} items in menu '{gameMenu.StringId}' at {settlement.Name}");

                int gatedCount = 0;
                foreach (var option in menuItems)
                {
                    if (option == null) continue;
                    if (!SettlementMenuOptions.OptionFeatureMap.TryGetValue(option.IdString, out var requiredFeature))
                        continue;

                    bool hasAccess = HasFeatureAccess(settlement, requiredFeature);
                    LmmiLog.Debug($"  option='{option.IdString}' feature='{requiredFeature}' hasAccess={hasAccess}");
                    if (!hasAccess) gatedCount++;

                    // Menu options live for the whole session and are shared by every settlement,
                    // so wrap each one once and decide at evaluation time for the current settlement.
                    // A wrapper that always disables would keep the option locked after discovery.
                    if (!_gatedOptions.Add(option)) continue;

                    var originalCondition = option.OnCondition;
                    option.OnCondition = (MenuCallbackArgs a) =>
                    {
                        bool original = originalCondition == null || originalCondition(a);
                        var current = MobileParty.MainParty?.CurrentSettlement ?? Settlement.CurrentSettlement;
                        if (current != null && !HasFeatureAccess(current, requiredFeature))
                        {
                            a.IsEnabled = false;
                            a.Tooltip = new TextObject("{=lmmi_undiscovered}You haven't discovered this part of the settlement yet.");
                        }
                        return original;
                    };
                }

                if (gatedCount > 0)
                    LmmiLog.Info($"OnGameMenuOpened: gated {gatedCount} options in '{gameMenu.StringId}' at {settlement.Name}");
            }
            catch (Exception ex)
            {
                LmmiLog.Error("OnGameMenuOpened threw", ex);
            }
        }

        private void OnGameStarted(CampaignGameStarter campaignGameStarter)
        {
            var version = typeof(DisableMenuBehavior).Assembly.GetName().Version?.ToString() ?? "unknown";
            LmmiLog.Info($"DisableMenuBehavior: OnGameStarted - registering dialogs and events. [v{version}]");
            // Must initialize the guide character BEFORE registering guide dialogs.
            _localGuide = MBObjectManager.Instance.GetObject<CharacterObject>("local_guide");
            if (_localGuide == null)
                LmmiLog.Warning("local_guide character object not found - guide tavern spawns will be skipped.");

            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=Eji4qI4xg}Less menus more immersion loaded successfully.").ToString() + $" [v{version}]"));

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
            // NOTE: This used to bundle common areas (alley/waterfront/clearing) as well,
            // but we want only the tavern/backstreet itself to be discoverable via directions.

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
                () =>
                {
                    try
                    {
                        var settlement = Settlement.CurrentSettlement;
                        if (settlement?.Village == null)
                        {
                            LmmiLog.Warning("Village trader trade consequence fired with no current settlement/village — aborting trade screen.");
                            return;
                        }

                        LmmiLog.Debug($"Opening village trade screen (dialog) for '{settlement.Name}'.");
                        InventoryScreenHelper.OpenScreenAsTrade(
                            settlement.ItemRoster,
                            settlement.Village,
                            InventoryCategoryType.None,
                            null
                        );
                    }
                    catch (Exception ex)
                    {
                        LmmiLog.Error("Village trader trade consequence threw while opening trade screen", ex);
                    }
                }
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

        private int GetVillageArrangementCost() => LmmiSettingsProvider.VillageArrangementCost;

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
            // Guard: guide NPC missing in the XML/object DB
            if (_localGuide == null)
            {
                LmmiLog.Warning("AddGuideDialogs: _localGuide is null — skipping guide dialog registration.");
                return;
            }

            bool IsTalkingToLocalGuide()
            {
                try
                {
                    var partner = CharacterObject.OneToOneConversationCharacter;
                    if (partner == null) return false;
                    if (partner != _localGuide) return false;

                    // Prefer to only fire in tavern (where we spawn the guide), but don't be overly strict.
                    // This also ensures we override generic tavern/mercenary openers.
                    var locId = CampaignMission.Current?.Location?.StringId;
                    if (!string.IsNullOrEmpty(locId) && locId != "tavern")
                        return false;

                    return Settlement.CurrentSettlement?.IsTown == true;
                }
                catch
                {
                    return false;
                }
            }

            int GetGuideCost()
            {
                int tier = Clan.PlayerClan?.Tier ?? 0;
                int baseCost = LmmiSettingsProvider.GuideCostBase;
                return baseCost * (tier + 1);
            }

            // Use existing localized strings from _Module/ModuleData/Languages/std_module_strings_xml.xml
            // so dialog text stays in one place.

            bool HasFullAccess() => HasAccessToSettlement(Settlement.CurrentSettlement);

            void SetGuideVars()
            {
                var s = Settlement.CurrentSettlement;
                MBTextManager.SetTextVariable("SETTLEMENT_NAME", s?.Name ?? new TextObject("this town"));
                MBTextManager.SetTextVariable("COST", GetGuideCost());
            }

            // Entry line: if already unlocked, show a short “nothing more to show” line.
            campaignGameStarter.AddDialogLine(
                "lmmi_local_guide_already_unlocked",
                "start",
                "lmmi_local_guide_already_unlocked_player",
                "{=E5f6G7h8I}Ah, I see thou hast already been shown the wonders of {SETTLEMENT_NAME}. There is naught more to see.",
                () => IsTalkingToLocalGuide() && HasFullAccess(),
                () => MBTextManager.SetTextVariable("SETTLEMENT_NAME", Settlement.CurrentSettlement?.Name ?? new TextObject("this town")),
                1000
            );
            campaignGameStarter.AddPlayerLine(
                "lmmi_local_guide_already_unlocked_ack",
                "lmmi_local_guide_already_unlocked_player",
                "close_window",
                "{=J9k0L1m2N}Indeed, I have seen all there is.",
                null,
                null
            );

            // Entry line: offer to reveal the town.
            campaignGameStarter.AddDialogLine(
                "lmmi_local_guide_offer_start",
                "start",
                "lmmi_local_guide_offer_player",
                "{=O3p4Q5r6S}Ho there, sojourner. Thine puzzled face betrays thine nature. Fear thee not, as for the most modest sum, I shall show thee around {SETTLEMENT_NAME}, we shall leave no boulder unturned until thou knows't this corner of earth as thine own. Be thee interested?",
                () => IsTalkingToLocalGuide() && !HasFullAccess(),
                () => SetGuideVars(),
                1000
            );
            campaignGameStarter.AddPlayerLine(
                "lmmi_local_guide_accept",
                "lmmi_local_guide_offer_player",
                "lmmi_local_guide_accept_resp",
                "{=T7u8V9w0X}Yes, I could use your help. [Pay {COST}{GOLD_ICON}]",
                () => Hero.MainHero.Gold >= GetGuideCost(),
                () =>
                {
                    SetGuideVars();
                    UnlockSettlementAccess(Settlement.CurrentSettlement, GetGuideCost());
                }
            );
            campaignGameStarter.AddPlayerLine(
                "lmmi_local_guide_decline",
                "lmmi_local_guide_offer_player",
                "close_window",
                "{=D5e6F7g8H}By heaven's Grace, what are you on about. Not interested.",
                null,
                null
            );
            campaignGameStarter.AddDialogLine(
                "lmmi_local_guide_accept_resp",
                "lmmi_local_guide_accept_resp",
                "close_window",
                "{=Y1z2A3b4C}Splendid. Let me show thee the ins and outs of our {SETTLEMENT_NAME}.",
                null,
                () => MBTextManager.SetTextVariable("SETTLEMENT_NAME", Settlement.CurrentSettlement?.Name ?? new TextObject("this town"))
            );
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

                var msg = new TextObject("{=P3q4R5s6T}You now know your way around {SETTLEMENT_NAME}.");
                msg.SetTextVariable("SETTLEMENT_NAME", settlement.Name);
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString()));
            }
            else
            {
                LmmiLog.Debug($"Player tried to unlock '{settlement.Name}' but lacked gold ({Hero.MainHero.Gold}/{cost}).");
                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=Z1a2B3c4D}You don't have enough gold.").ToString()));
            }
        }

        /// <summary>Test tool: know every part of this settlement.</summary>
        public void GrantFullAccess(Settlement settlement)
        {
            if (settlementsWithAccess == null) settlementsWithAccess = new Dictionary<string, bool>();
            settlementsWithAccess[settlement.Id.ToString()] = true;
        }

        // This runs many times a frame while menus are drawn: log only when the answer changes.
        [NonSerialized] private string? _lastAccessLog;

        private void AccessLog(string message)
        {
            if (message == _lastAccessLog) return;
            _lastAccessLog = message;
            LmmiLog.Debug(message);
        }

        public bool HasAccessToSettlement(Settlement settlement)
        {
            if (settlement == null) { AccessLog("HasAccessToSettlement: settlement null → true"); return true; }

            var playerClan = Clan.PlayerClan;
            if (playerClan == null) { AccessLog("HasAccessToSettlement: playerClan null → true"); return true; }

            if (settlement.OwnerClan == playerClan)
            {
                AccessLog($"HasAccessToSettlement: player owns {settlement.Name} → true");
                return true;
            }

            int fullTier = LmmiSettingsProvider.FullAccessClanTier;
            if (playerClan.Tier >= fullTier)
            {
                AccessLog($"HasAccessToSettlement: clan tier {playerClan.Tier} >= fullTier {fullTier} → true");
                return true;
            }

            int kingdomTier = LmmiSettingsProvider.KingdomAccessClanTier;
            if (settlement.OwnerClan?.Kingdom != null &&
                playerClan.Kingdom != null &&
                settlement.OwnerClan.Kingdom == playerClan.Kingdom &&
                playerClan.Tier >= kingdomTier)
            {
                AccessLog($"HasAccessToSettlement: same kingdom, tier {playerClan.Tier} >= {kingdomTier} → true");
                return true;
            }

            var settlementId = settlement.Id.ToString();
            if (settlementsWithAccess == null) { AccessLog($"HasAccessToSettlement: {settlement.Name} no access data → false"); return false; }
            bool result = settlementsWithAccess.ContainsKey(settlementId) && settlementsWithAccess[settlementId];
            AccessLog($"HasAccessToSettlement: {settlement.Name} lookup → {result}");
            return result;
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
            Greedy,
            Unfamiliar   // a hero who doesn't know this place any better than you (e.g. a companion)
        }

        private static string PickOne(params string[] options)
        {
            if (options == null || options.Length == 0)
                return string.Empty;

            return options[MBRandom.RandomInt(options.Length)];
        }

        /// <summary>A line written the usual way ("{=id}text"), resolved now.</summary>
        private static string T(string line) => new TextObject(line).ToString();

        private static string L(string id, string fallback)
        {
            // Include fallback text to keep English defaults even if XML is missing.
            // Translators can override via _Module/ModuleData/Languages/*.xml.
            return new TextObject("{=" + id + "}" + fallback).ToString();
        }

        private static string L(string id, string fallback, string var1Name, TextObject var1Value)
        {
            var t = new TextObject("{=" + id + "}" + fallback);
            t.SetTextVariable(var1Name, var1Value);
            return t.ToString();
        }

        private static string L(string id, string fallback, string var1Name, string var1Value)
        {
            var t = new TextObject("{=" + id + "}" + fallback);
            t.SetTextVariable(var1Name, var1Value);
            return t.ToString();
        }

        internal static string GetForeignerInsultText()
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
                    L("lmmi_dirs_foreigner_empire_1", "Ugh, imperials... haven't you polluted enough of the world? Get lost."),
                    L("lmmi_dirs_foreigner_empire_2", "Another imperial thinking they own the place. We bow to no emperor here."),
                    L("lmmi_dirs_foreigner_empire_3", "Take your imperial arrogance elsewhere. You're not wanted."),
                    L("lmmi_dirs_foreigner_empire_4", "Another imperial come to tell us how things were done in the old days? Spare me."),
                    L("lmmi_dirs_foreigner_empire_5", "The Empire is dead, and you're what's left of it. Keep walking."),
                    L("lmmi_dirs_foreigner_empire_6", "Keep your purple airs to yourself, imperial."),
                    T("{=lmmi_dirs_foreigner_empire_7}Imperial, are you? Go and count something, and leave the rest of us alone."),
                    T("{=lmmi_dirs_foreigner_empire_8}Your emperors bled this land for three hundred years. Don't expect a smile."),
                    T("{=lmmi_dirs_foreigner_empire_9}An imperial asking a favor. The old days really are over."),
                    T("{=lmmi_dirs_foreigner_empire_10}Go and find a senator to carry your purse, imperial."),
                    T("{=lmmi_dirs_foreigner_empire_11}We had a tax collector like you once. We threw him down the well."),
                    T("{=lmmi_dirs_foreigner_empire_12}Your legions are gone, imperial. So is your welcome."),
                    T("{=lmmi_dirs_foreigner_empire_13}Three emperors squabbling over one corpse, and you want my help? Walk."),
                    T("{=lmmi_dirs_foreigner_empire_14}Purple cloak, empty purse, big mouth. Imperial, then."),
                    T("{=lmmi_dirs_foreigner_empire_15}I'd sooner help a mule. At least it doesn't lecture me."),
                    T("{=lmmi_dirs_foreigner_empire_16}Go back to your marble ruins and argue about who's emperor this week."),
                    T("{=lmmi_dirs_foreigner_empire_17}Imperials. Always looking down your noses. Look down this street instead — and keep walking."),
                    T("{=lmmi_dirs_foreigner_empire_18}My grandfather paid your Empire's taxes in blood. I owe you nothing."),
                    T("{=lmmi_dirs_foreigner_empire_19}Run along to your bathhouse, imperial. There's a fat senator waiting to scrub your back."),
                    T("{=lmmi_dirs_foreigner_empire_20}Your emperors married their own cousins, and it shows on your face."),
                    T("{=lmmi_dirs_foreigner_empire_21}An imperial. All purple cloak, and nothing much underneath it."),
                    T("{=lmmi_dirs_foreigner_empire_22}You smell like a senator's armpit — perfume on top, rot underneath."),
                    T("{=lmmi_dirs_foreigner_empire_23}Go and lie on a couch and have someone feed you grapes, imperial. It's all your kind are good for."),
                    T("{=lmmi_dirs_foreigner_empire_24}Did your mother powder her face for the legions too? I hear they all did.")
                );
            }
            if (cultureId == "aserai")
            {
                return PickOne(
                    L("lmmi_dirs_foreigner_aserai_1", "Crawl back to whatever sandhole you came out of, dog."),
                    L("lmmi_dirs_foreigner_aserai_2", "We don't serve desert rats here. Go peddle your wares elsewhere."),
                    L("lmmi_dirs_foreigner_aserai_3", "The stink of the Nahasa follows you, stranger. Move along."),
                    L("lmmi_dirs_foreigner_aserai_4", "The desert sent us another one. Keep walking."),
                    L("lmmi_dirs_foreigner_aserai_5", "Go sell your spices to someone who cares, Aserai."),
                    L("lmmi_dirs_foreigner_aserai_6", "Your kind haggles over everything. Not here, and not with me."),
                    T("{=lmmi_dirs_foreigner_aserai_7}The Nahasa's a long way off. Not far enough, if you ask me."),
                    T("{=lmmi_dirs_foreigner_aserai_8}Go back to the Nahasa and haggle with the sand."),
                    T("{=lmmi_dirs_foreigner_aserai_9}Whatever you're here for, we don't want it. Away with you."),
                    T("{=lmmi_dirs_foreigner_aserai_10}The sun's cooked your brain, desert-born. We don't help your sort."),
                    T("{=lmmi_dirs_foreigner_aserai_11}Your caravans undercut every honest trader in this town. Find your own way."),
                    T("{=lmmi_dirs_foreigner_aserai_12}Banu this, Banu that. I can't keep your clans straight and I don't care to."),
                    T("{=lmmi_dirs_foreigner_aserai_13}You smell of dust and spices. Wherever you're going, it's not my problem."),
                    T("{=lmmi_dirs_foreigner_aserai_14}The desert's that way. Keep walking until you're home."),
                    T("{=lmmi_dirs_foreigner_aserai_15}An Aserai this far north? The desert must finally have had enough of you."),
                    T("{=lmmi_dirs_foreigner_aserai_16}Your sultan's riders raid our borders, and you want my help? Hah."),
                    T("{=lmmi_dirs_foreigner_aserai_17}I'd help you, but then you'd stay. Go on, off with you."),
                    T("{=lmmi_dirs_foreigner_aserai_18}Shoo. Go and find an oasis to drink dry."),
                    T("{=lmmi_dirs_foreigner_aserai_19}Go and kiss a scorpion, desert-born. It's the warmest welcome you'll get here."),
                    T("{=lmmi_dirs_foreigner_aserai_20}The sun's boiled your brains to porridge. Off."),
                    T("{=lmmi_dirs_foreigner_aserai_21}You've sand in places decent people don't mention, I'd wager. Go and shake it out somewhere else."),
                    T("{=lmmi_dirs_foreigner_aserai_22}I'd sooner share a bed with a scorpion than a table with you."),
                    T("{=lmmi_dirs_foreigner_aserai_23}Back to your dunes, and take your flies with you."),
                    T("{=lmmi_dirs_foreigner_aserai_24}You look like something the desert chewed up and spat out. Twice.")
                );
            }
            if (cultureId == "khuzait")
            {
                return PickOne(
                    L("lmmi_dirs_foreigner_khuzait_1", "We have no love for horse fondlers here, stranger. Go back to your decrepit steppe."),
                    L("lmmi_dirs_foreigner_khuzait_2", "A Khuzait? In our town? Go back to your yurt, nomad."),
                    L("lmmi_dirs_foreigner_khuzait_3", "I can smell the horse dung from here. Off with you."),
                    L("lmmi_dirs_foreigner_khuzait_4", "Another steppe rider. Where's your horse? Or did you eat it?"),
                    L("lmmi_dirs_foreigner_khuzait_5", "Khuzaits burn our villages, and then they come asking for favors. Leave."),
                    L("lmmi_dirs_foreigner_khuzait_6", "You smell of mare's milk and smoke. Off with you."),
                    T("{=lmmi_dirs_foreigner_khuzait_7}Your khan's riders burned my cousin's village. Ride on, nomad."),
                    T("{=lmmi_dirs_foreigner_khuzait_8}No horse to carry you there? Shame. Walk, then — far away."),
                    T("{=lmmi_dirs_foreigner_khuzait_9}Khuzait. Do you even know what a street is?"),
                    T("{=lmmi_dirs_foreigner_khuzait_10}You people drink mare's milk and call it a meal. Off with you."),
                    T("{=lmmi_dirs_foreigner_khuzait_11}Go back to the grass sea, steppe rat."),
                    T("{=lmmi_dirs_foreigner_khuzait_12}A Khuzait on foot. Somebody's horse must have finally had enough."),
                    T("{=lmmi_dirs_foreigner_khuzait_13}Your kind only come to a town to burn it. Not today."),
                    T("{=lmmi_dirs_foreigner_khuzait_14}I can hear the arrows already. Go, before your friends come looking for you."),
                    T("{=lmmi_dirs_foreigner_khuzait_15}We pay good coin for walls because of people like you. Move along."),
                    T("{=lmmi_dirs_foreigner_khuzait_16}Ask your horse. You two seem close."),
                    T("{=lmmi_dirs_foreigner_khuzait_17}The steppe's big enough for you, isn't it? Then why are you here?"),
                    T("{=lmmi_dirs_foreigner_khuzait_18}Felt tents, dung fires and stolen cattle. No, thank you."),
                    T("{=lmmi_dirs_foreigner_khuzait_19}Your mother was a mare, and your father wasn't fussy."),
                    T("{=lmmi_dirs_foreigner_khuzait_20}Go and sleep with your horse, nomad. I hear your lot do."),
                    T("{=lmmi_dirs_foreigner_khuzait_21}You smell like the back end of a horse. The wrong end."),
                    T("{=lmmi_dirs_foreigner_khuzait_22}Did your horse throw you out of the yurt? Can't say I blame it."),
                    T("{=lmmi_dirs_foreigner_khuzait_23}Mare's milk and dung smoke. You reek of both."),
                    T("{=lmmi_dirs_foreigner_khuzait_24}Your kind kiss their horses goodnight. On the mouth, I'm told.")
                );
            }
            if (cultureId == "nord")
            {
                return PickOne(
                    L("lmmi_dirs_foreigner_nord_1", "How about I call the guards and have you thrown out? Go back to the sea, you are not welcome here."),
                    L("lmmi_dirs_foreigner_nord_2", "Another sea raider crawled ashore, did they? Get lost."),
                    L("lmmi_dirs_foreigner_nord_3", "Take your longboat and shove off, Nord. We've nothing for you."),
                    L("lmmi_dirs_foreigner_nord_4", "A Nord. Did the sea spit you out, or did you swim?"),
                    L("lmmi_dirs_foreigner_nord_5", "We know what Nords do to coastal towns. Keep your axe where I can see it."),
                    L("lmmi_dirs_foreigner_nord_6", "Go back to your cold fjords and your colder gods."),
                    T("{=lmmi_dirs_foreigner_nord_7}Nords. You smell the sea on them before you see them. Move along."),
                    T("{=lmmi_dirs_foreigner_nord_8}Go and row somewhere, Nord. Somewhere far."),
                    T("{=lmmi_dirs_foreigner_nord_9}The last Nords through here left with the harbor-master's silver. Get out."),
                    T("{=lmmi_dirs_foreigner_nord_10}Did your longboat sink, or did your friends leave you behind?"),
                    T("{=lmmi_dirs_foreigner_nord_11}I don't help sea wolves. Swim home."),
                    T("{=lmmi_dirs_foreigner_nord_12}Pray to your sea-gods for directions, Nord. Not to me."),
                    T("{=lmmi_dirs_foreigner_nord_13}Axe on your belt, salt in your beard and a favor on your lips. No."),
                    T("{=lmmi_dirs_foreigner_nord_14}A Nord on dry land. Like a fish on a plank — and about as welcome."),
                    T("{=lmmi_dirs_foreigner_nord_15}There's a watch on the harbor for your kind. Shall I call them?"),
                    T("{=lmmi_dirs_foreigner_nord_16}Herring and plunder, that's all your lot know. Not here."),
                    T("{=lmmi_dirs_foreigner_nord_17}The tide brought you in. Let it take you out again."),
                    T("{=lmmi_dirs_foreigner_nord_18}Every spring your ships come, and every spring we bury someone. Leave."),
                    T("{=lmmi_dirs_foreigner_nord_19}Go and kiss a herring, Nord. It's the only lips you'll get here."),
                    T("{=lmmi_dirs_foreigner_nord_20}You smell like a fish died in your beard. Twice."),
                    T("{=lmmi_dirs_foreigner_nord_21}I hear Nords court seals. Lucky seals."),
                    T("{=lmmi_dirs_foreigner_nord_22}Your longboat leaks, and so does your skull."),
                    T("{=lmmi_dirs_foreigner_nord_23}Back to the sea, Nord. The fish miss you, and the walruses miss you more."),
                    T("{=lmmi_dirs_foreigner_nord_24}Salt in the beard and lice in the furs. Lovely.")
                );
            }
            if (cultureId == "battania")
            {
                return PickOne(
                    L("lmmi_dirs_foreigner_battania_1", "Sure... wait, what is that accent... We do not tolerate filthy barbarians here. Leave, while you can."),
                    L("lmmi_dirs_foreigner_battania_2", "A forest dweller? In civilized lands? Go hug a tree somewhere else."),
                    L("lmmi_dirs_foreigner_battania_3", "I don't deal with painted savages. Away with you."),
                    L("lmmi_dirs_foreigner_battania_4", "Battanian. Go back to your woods and your standing stones."),
                    L("lmmi_dirs_foreigner_battania_5", "Painted face, empty head. Move along."),
                    L("lmmi_dirs_foreigner_battania_6", "Your people stole cattle from my grandfather. I haven't forgotten."),
                    T("{=lmmi_dirs_foreigner_battania_7}Woad and wolf-skins. Go and paint yourself somewhere else."),
                    T("{=lmmi_dirs_foreigner_battania_8}Battanian. Go and talk to your trees — they might listen."),
                    T("{=lmmi_dirs_foreigner_battania_9}Your clans can't stop raiding each other, and now you want my help? Ha."),
                    T("{=lmmi_dirs_foreigner_battania_10}Back to the forest, woad raider. The town isn't for you."),
                    T("{=lmmi_dirs_foreigner_battania_11}I know that accent. My father lost three cows to it."),
                    T("{=lmmi_dirs_foreigner_battania_12}Do your druids not give directions? Go and ask a stone."),
                    T("{=lmmi_dirs_foreigner_battania_13}Hill folk. All mud and long knives. Move on."),
                    T("{=lmmi_dirs_foreigner_battania_14}Another Battanian looking for a fight. You'll not find one here — or anything else."),
                    T("{=lmmi_dirs_foreigner_battania_15}Your kind sing songs about burning towns like ours. Keep walking."),
                    T("{=lmmi_dirs_foreigner_battania_16}Go and hide in your glens, Battanian."),
                    T("{=lmmi_dirs_foreigner_battania_17}The woods are that way. Don't let the gate hit you on the way out."),
                    T("{=lmmi_dirs_foreigner_battania_18}Moss on your boots and blood on your hands. No help here."),
                    T("{=lmmi_dirs_foreigner_battania_19}Go and hump a tree, woad raider. It's the only thing that'll have you."),
                    T("{=lmmi_dirs_foreigner_battania_20}Your druids marry oak trees, and the trees still say no."),
                    T("{=lmmi_dirs_foreigner_battania_21}Painted blue all over? Even there? Don't answer that."),
                    T("{=lmmi_dirs_foreigner_battania_22}I can smell the woods on you — and whatever you were doing in them."),
                    T("{=lmmi_dirs_foreigner_battania_23}Back to your glen. The squirrels must be missing you terribly."),
                    T("{=lmmi_dirs_foreigner_battania_24}Your sort sleep in the mud with the pigs and call it a clan gathering.")
                );
            }
            if (cultureId == "sturgia")
            {
                return PickOne(
                    L("lmmi_dirs_foreigner_sturgia_1", "Hahaha, what? Your wits really have frozen, stranger. Crawl back to {STURGIA_RULER}'s lap.", "STURGIA_RULER", sturgiaRuler),
                    L("lmmi_dirs_foreigner_sturgia_2", "You Sturgians are all the same — half-drunk and lost. Find your own way."),
                    L("lmmi_dirs_foreigner_sturgia_3", "Go back to your frozen wasteland, snowman. We don't help your kind."),
                    L("lmmi_dirs_foreigner_sturgia_4", "Sturgian. Drunk already, or just born that way?"),
                    L("lmmi_dirs_foreigner_sturgia_5", "Go back to your snow and your mead-halls."),
                    L("lmmi_dirs_foreigner_sturgia_6", "Your princes couldn't hold their own borders. Why should I help you?"),
                    T("{=lmmi_dirs_foreigner_sturgia_7}Sturgian. You've had a few already, haven't you? Find it yourself."),
                    T("{=lmmi_dirs_foreigner_sturgia_8}Go and wrestle a bear, snowman. Leave honest folk alone."),
                    T("{=lmmi_dirs_foreigner_sturgia_9}Your princes squabble while their people starve. And here you are, begging favors."),
                    T("{=lmmi_dirs_foreigner_sturgia_10}You smell like a mead barrel fell on you. Away."),
                    T("{=lmmi_dirs_foreigner_sturgia_11}Sturgians. Brave as bears, and about as clever. Off you go."),
                    T("{=lmmi_dirs_foreigner_sturgia_12}The north wind blew you in. It can blow you out again."),
                    T("{=lmmi_dirs_foreigner_sturgia_13}Is it true your lot bathe once a winter? It smells true."),
                    T("{=lmmi_dirs_foreigner_sturgia_14}Go back to Balgard and freeze."),
                    T("{=lmmi_dirs_foreigner_sturgia_15}I'd help you, but you'd forget it by your next cup."),
                    T("{=lmmi_dirs_foreigner_sturgia_16}Fur coat, red nose, empty head. Sturgian, aren't you?"),
                    T("{=lmmi_dirs_foreigner_sturgia_17}Your kind drink till they fall down and call it a feast. Not my concern."),
                    T("{=lmmi_dirs_foreigner_sturgia_18}Find your own way, northerner. Follow the smell of ale."),
                    T("{=lmmi_dirs_foreigner_sturgia_19}Your mother wrestled a bear once. Lost, I hear — then married it."),
                    T("{=lmmi_dirs_foreigner_sturgia_20}You northerners bathe once a year, whether you need it or not."),
                    T("{=lmmi_dirs_foreigner_sturgia_21}There's more vomit than beard on that chin, Sturgian."),
                    T("{=lmmi_dirs_foreigner_sturgia_22}Go and warm your bed with a bear, snowman. I hear it's a family tradition."),
                    T("{=lmmi_dirs_foreigner_sturgia_23}Back to your mead-hall and your lice. They'll be lonely without you."),
                    T("{=lmmi_dirs_foreigner_sturgia_24}You smell like a wet dog that drank a barrel of mead and slept in it.")
                );
            }
            if (cultureId == "vlandia")
            {
                return PickOne(
                    L("lmmi_dirs_foreigner_vlandia_1", "Ahh, a passionate Vlandian... I'll be sure to give my regards to the next goat I see. Now leave before I call the guards."),
                    L("lmmi_dirs_foreigner_vlandia_2", "A Vlandian, eh? Don't you have some peasants to tax or a field to plow? Off with you."),
                    L("lmmi_dirs_foreigner_vlandia_3", "I've had enough of Vlandian 'knights' stumbling through our streets. Find your own way."),
                    L("lmmi_dirs_foreigner_vlandia_4", "A Vlandian. Everyone, hands on your purses."),
                    L("lmmi_dirs_foreigner_vlandia_5", "Go pay homage to your lord somewhere else, Vlandian."),
                    L("lmmi_dirs_foreigner_vlandia_6", "Your knights trample our fields and then expect courtesy. No."),
                    T("{=lmmi_dirs_foreigner_vlandia_7}Vlandian. Mind the goats, everyone."),
                    T("{=lmmi_dirs_foreigner_vlandia_8}Go and find a horse to sit on and feel important, Vlandian."),
                    T("{=lmmi_dirs_foreigner_vlandia_9}Your knights burn a village and call it chivalry. Away."),
                    T("{=lmmi_dirs_foreigner_vlandia_10}A Vlandian asking nicely. That's new."),
                    T("{=lmmi_dirs_foreigner_vlandia_11}Crossbows and goat jokes, that's all your lot are. Move."),
                    T("{=lmmi_dirs_foreigner_vlandia_12}Your king takes a tenth of everything. Go and ask him for help."),
                    T("{=lmmi_dirs_foreigner_vlandia_13}Go and polish your armor, Vlandian. It's the only thing about you that shines."),
                    T("{=lmmi_dirs_foreigner_vlandia_14}Another Vlandian sellsword. Who's paying you to be here?"),
                    T("{=lmmi_dirs_foreigner_vlandia_15}Your sort came over the sea with swords and never left. Leave now."),
                    T("{=lmmi_dirs_foreigner_vlandia_16}Vlandians. Iron on the outside, turnip on the inside."),
                    T("{=lmmi_dirs_foreigner_vlandia_17}I don't help people who'd sell their own mothers to the highest bidder."),
                    T("{=lmmi_dirs_foreigner_vlandia_18}Back to Pravend with you, and take your goats."),
                    T("{=lmmi_dirs_foreigner_vlandia_19}Run home to your goats, Vlandian. They'll be missing you. One of them especially."),
                    T("{=lmmi_dirs_foreigner_vlandia_20}Your knights ride horses and court goats. Or is it the other way round?"),
                    T("{=lmmi_dirs_foreigner_vlandia_21}You've the face of a goat and the manners of its back end."),
                    T("{=lmmi_dirs_foreigner_vlandia_22}Lock up the goats, everyone — there's a Vlandian in town."),
                    T("{=lmmi_dirs_foreigner_vlandia_23}Your king swears oaths the way your mother did: often, and to anyone."),
                    T("{=lmmi_dirs_foreigner_vlandia_24}Go and woo a nanny-goat, Vlandian. She might even say yes.")
                );
            }

            return PickOne(
                L("lmmi_dirs_foreigner_generic_1", "I don't help outsiders around here. Move along."),
                L("lmmi_dirs_foreigner_generic_2", "You're not from around here. Figure it out yourself."),
                L("lmmi_dirs_foreigner_generic_3", "We don't take kindly to strangers. Best be on your way."),
                    L("lmmi_dirs_foreigner_generic_4", "I don't know where you're from, and I don't care to. Move along."),
                    L("lmmi_dirs_foreigner_generic_5", "Your kind always brings trouble. Not today."),
                    L("lmmi_dirs_foreigner_generic_6", "Strangers. Always wanting something. Go away."),
                    T("{=lmmi_dirs_foreigner_generic_7}Strangers bring disease and debts. Neither's welcome."),
                    T("{=lmmi_dirs_foreigner_generic_8}I don't know you, and I'd like to keep it that way."),
                    T("{=lmmi_dirs_foreigner_generic_9}Ask someone who's paid to care."),
                    T("{=lmmi_dirs_foreigner_generic_10}Whatever you're looking for, it isn't here."),
                    T("{=lmmi_dirs_foreigner_generic_11}Foreigners. Every one of them lost, and every one of them my problem. Not today."),
                    T("{=lmmi_dirs_foreigner_generic_12}Go back the way you came. That road I can point you to."),
                    T("{=lmmi_dirs_foreigner_generic_13}We look after our own here. You're not our own."),
                    T("{=lmmi_dirs_foreigner_generic_14}No. And don't bother asking anyone else on this street either."),
                    T("{=lmmi_dirs_foreigner_generic_15}I've got work. You've got legs. Use them."),
                    T("{=lmmi_dirs_foreigner_generic_16}A stranger asking favors. Next you'll want a bed and a hot meal."),
                    T("{=lmmi_dirs_foreigner_generic_17}The last stranger I helped stole my mule."),
                    T("{=lmmi_dirs_foreigner_generic_18}You'll find nothing but closed doors here, outsider."),
                    T("{=lmmi_dirs_foreigner_generic_19}You've the look of someone who sleeps with the pigs, and the smell to prove it."),
                    T("{=lmmi_dirs_foreigner_generic_20}Your mother should have kept you in the barn."),
                    T("{=lmmi_dirs_foreigner_generic_21}Go and scratch your fleas somewhere else."),
                    T("{=lmmi_dirs_foreigner_generic_22}I've stepped in nicer things than you on market day."),
                    T("{=lmmi_dirs_foreigner_generic_23}Off, before the dogs start sniffing you."),
                    T("{=lmmi_dirs_foreigner_generic_24}Whatever rock you crawled out from under wants you back.")
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
        [NonSerialized] private Mission? _lastWillingnessMission;
        // A notable doesn't walk you anywhere: they send a passerby (notables are pinned to their spot and freeze).
        [NonSerialized] private Agent? _escortRunner;
        // Watchdog: an escort that hasn't moved after a few seconds is stuck and gets called off.
        [NonSerialized] private Agent? _watchAgent;
        [NonSerialized] private Vec3 _watchStartPos;
        [NonSerialized] private float _watchTime = -1f;

        private void EnsureWillingnessCacheForSettlement(Settlement? settlement)
        {
            if (settlement == null) return;

            // Reset the cache for a new settlement or a new scene: agent indices are reused in every scene.
            if (_lastWillingnessSettlement != settlement || _lastWillingnessMission != Mission.Current)
            {
                _lastWillingnessMission = Mission.Current;
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
            if (StreetEventsBehavior.IsGrateful(agent)) { _agentRefusalTextMap.Remove(agent.Index); return false; }

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

            // Someone you stood up for (or won over) this visit has no quarrel with your people any more.
            if (StreetEventsBehavior.IsGrateful(agent))
            {
                _currentWillingness = CitizenWillingness.Glad;
                _agentWillingnessMap[agentId] = _currentWillingness;
                _agentRefusalTextMap.Remove(agentId);
                return;
            }

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

            // Word gets around: common folk warm to someone who's done right by their town, and cool on one who hasn't.
            float standing = TownStandingBehavior.Get(settlement);
            if (standing >= 10f)
            {
                gladWeight += busyWeight / 2 + foreignerWeight / 2;
                busyWeight /= 2;
                foreignerWeight /= 2;
            }
            else if (standing <= -10f)
            {
                int shift = gladWeight / 3;
                gladWeight -= shift;
                if (partner.Culture == player.Culture) busyWeight += shift;
                else foreignerWeight += shift;
            }

            int total = busyWeight + foreignerWeight + gladWeight + greedyWeight;
            int roll = MBRandom.RandomInt(total);

            if ((roll -= busyWeight) < 0) _currentWillingness = CitizenWillingness.TooBusy;
            else if ((roll -= foreignerWeight) < 0) _currentWillingness = CitizenWillingness.Foreigner;
            else if ((roll -= gladWeight) < 0) _currentWillingness = CitizenWillingness.Glad;
            else _currentWillingness = CitizenWillingness.Greedy;

            if (_currentWillingness == CitizenWillingness.Greedy)
                RollBribe(agentId, sameCulture: partner.Culture == player.Culture);

            _agentWillingnessMap[agentId] = _currentWillingness;
        }

        private void RollBribe(int agentId, bool sameCulture, float greed = 1f)
        {
            var player = Hero.MainHero;
            _bribeAmount = (int)(player.Gold * 0.01f) + (10 * player.Clan.Tier) + 10;
            _bribeAmount = Math.Min(_bribeAmount, 500); // Cap at 500
            if (sameCulture)
                _bribeAmount /= 2; // Same culture discount
            _bribeAmount = (int)Math.Round(_bribeAmount * greed);

            MBTextManager.SetTextVariable("BRIBE_AMOUNT", _bribeAmount);
            _agentBribeMap[agentId] = _bribeAmount;
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

            // A notable who already walked you somewhere won't do it twice this visit. Everything else is
            // judged afresh — a vouch or a letter delivered minutes ago should count.
            if (_agentWillingnessMap.TryGetValue(agentId, out var existingWillingness) && existingWillingness == CitizenWillingness.AlreadyHelped)
            {
                _currentWillingness = existingWillingness;
                return;
            }

            var notable = Hero.OneToOneConversationHero;

            // Only someone who plausibly knows this place can show you around it. Your companions
            // don't know every town you drag them into.
            if (notable != null && !KnowsSettlement(notable, settlement))
            {
                _currentWillingness = CitizenWillingness.Unfamiliar;
                _agentWillingnessMap[agentId] = _currentWillingness;
                LmmiLog.Info($"Directions from {notable.Name}: doesn't know {settlement.Name} (not from here).");
                return;
            }

            // Notables judge a stranger the same way they judge a favor: standing, culture, and
            // what you've done for them. Contempt refuses, Wary wants coin, anyone warmer helps.
            // Other heroes who know the place (a companion from here, the town's lord) just help.
            if (notable == null || !notable.IsNotable || !LmmiSettingsProvider.EnableNotableDisposition)
            {
                _currentWillingness = CitizenWillingness.Glad;
                if (notable != null && !notable.IsNotable)
                    LmmiLog.Info($"Directions from {notable.Name}: knows {settlement.Name} (home, birthplace, governor or fief) -> Glad");
            }
            else
            {
                bool foreigner = NotableDisposition.IsForeigner(notable);
                switch (NotableVoice.ForConversation(notable).Level)
                {
                    case Disposition.Contempt:
                        _currentWillingness = foreigner ? CitizenWillingness.Foreigner : CitizenWillingness.TooBusy;
                        // Same voice as the greeting: no second culture insult if they already gave you one.
                        _agentRefusalTypeMap[agentId] = _currentWillingness;
                        _agentRefusalTextMap[agentId] = NotableVoice.Dismissal(notable);
                        break;
                    case Disposition.Wary:
                        _currentWillingness = CitizenWillingness.Greedy;
                        RollBribe(agentId, sameCulture: !foreigner, greed: NotableDisposition.Greed(notable));
                        break;
                    default:
                        _currentWillingness = CitizenWillingness.Glad;
                        break;
                }
                LmmiLog.Info($"Directions from notable {notable.Name}: {NotableVoice.ForConversation(notable).Level} -> {_currentWillingness}" +
                             (_currentWillingness == CitizenWillingness.Greedy ? $" (bribe {_bribeAmount})" : ""));
            }

            _agentWillingnessMap[agentId] = _currentWillingness;
        }

        /// <summary>Notables know their own settlement; anyone else only if it's home, birthplace, governed or their clan's fief.</summary>
        private static bool KnowsSettlement(Hero hero, Settlement settlement)
        {
            if (hero.IsNotable) return hero.CurrentSettlement == settlement;
            if (hero.HomeSettlement == settlement || hero.BornSettlement == settlement) return true;
            if (settlement.Town != null && hero.GovernorOf == settlement.Town) return true;
            return hero.IsLord && hero.Clan != null && settlement.OwnerClan == hero.Clan;
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
            var destNameText = SettlementMenuOptions.GetFeatureDisplayNameText(feature);
            MBTextManager.SetTextVariable("ESCORT_ARRIVAL_TEXT", PickOne(
                L("lmmi_escort_arrival_1", "Here we are! This is the {DESTINATION}.", "DESTINATION", destNameText),
                L("lmmi_escort_arrival_2", "We've arrived. The {DESTINATION}, as promised.", "DESTINATION", destNameText),
                L("lmmi_escort_arrival_3", "There you go — the {DESTINATION}. You can't miss it.", "DESTINATION", destNameText)
            ));
            MBTextManager.SetTextVariable("ESCORT_DESTINATION", destNameText);
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
                text = PickOne(
                    L("lmmi_escort_enroute_backstreet_1", "The tavern is just around the corner..."),
                    L("lmmi_escort_enroute_backstreet_2", "Nearly at the tavern now..."),
                    L("lmmi_escort_enroute_backstreet_3", "This way — the tavern won't be far.")
                );
            else if (feature == SettlementMenuOptions.Features.Trade)
                text = PickOne(
                    L("lmmi_escort_enroute_trade_1", "The marketplace is just ahead..."),
                    L("lmmi_escort_enroute_trade_2", "We're almost at the marketplace..."),
                    L("lmmi_escort_enroute_trade_3", "Keep your eyes open — the market is nearby.")
                );
            else if (feature == SettlementMenuOptions.Features.Arena)
                text = PickOne(
                    L("lmmi_escort_enroute_arena_1", "The arena is close by, I can hear the crowds..."),
                    L("lmmi_escort_enroute_arena_2", "Nearly at the arena..."),
                    L("lmmi_escort_enroute_arena_3", "Hear that roar? That's the arena — we're close.")
                );
            else if (feature == SettlementMenuOptions.Features.Smithy)
                text = PickOne(
                    L("lmmi_escort_enroute_smithy_1", "The smithy is nearby, can you smell the forge?"),
                    L("lmmi_escort_enroute_smithy_2", "Almost at the smithy..."),
                    L("lmmi_escort_enroute_smithy_3", "The hammering should guide us — the smithy is close.")
                );
            else if (feature == SettlementMenuOptions.Features.Keep)
                text = PickOne(
                    L("lmmi_escort_enroute_keep_1", "The lord's hall is just ahead..."),
                    L("lmmi_escort_enroute_keep_2", "We're nearly at the lord's hall..."),
                    L("lmmi_escort_enroute_keep_3", "Stay sharp. The keep is close.")
                );
            else
                text = PickOne(
                    L("lmmi_escort_enroute_generic_1", "It's just over there..."),
                    L("lmmi_escort_enroute_generic_2", "Not much further..."),
                    L("lmmi_escort_enroute_generic_3", "We're close now...")
                );

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
                                L("lmmi_dirs_busy_1", "Can't you see I'm busy? Find someone else."),
                                L("lmmi_dirs_busy_2", "I have things to do. Bother someone else."),
                                L("lmmi_dirs_busy_3", "Not now. I have somewhere to be.")
                            );
                            _agentRefusalTypeMap[agent.Index] = CitizenWillingness.TooBusy;
                            _agentRefusalTextMap[agent.Index] = storedText;
                        }
                        MBTextManager.SetTextVariable("LMMI_DIRS_BUSY_TEXT", storedText);
                    }
                    else
                    {
                        MBTextManager.SetTextVariable("LMMI_DIRS_BUSY_TEXT", L("lmmi_dirs_busy_1", "Can't you see I'm busy? Find someone else."));
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
                                L("lmmi_dirs_already_helped_1", "I've already shown you around. Ask someone else."),
                                L("lmmi_dirs_already_helped_2", "I helped you once already. I have my own business to attend to."),
                                L("lmmi_dirs_already_helped_3", "Find another guide, friend. I've done my part.")
                            );
                            _agentRefusalTypeMap[agent.Index] = CitizenWillingness.AlreadyHelped;
                            _agentRefusalTextMap[agent.Index] = storedText;
                        }
                        MBTextManager.SetTextVariable("LMMI_DIRS_ALREADY_HELPED_TEXT", storedText);
                    }
                    else
                    {
                        MBTextManager.SetTextVariable("LMMI_DIRS_ALREADY_HELPED_TEXT", L("lmmi_dirs_already_helped_1", "I've already shown you around. Ask someone else."));
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

            // OUTCOME 2.5: Doesn't know the place (companions, visiting lords)
            starter.AddDialogLine("lmmi_dirs_unfamiliar_resp", "lmmi_dirs_willingness_check", "hero_main_options",
                "{=lmmi_dirs_unfamiliar}{LMMI_DIRS_UNFAMILIAR_TEXT}",
                () => {
                    if (_currentWillingness != CitizenWillingness.Unfamiliar) return false;
                    MBTextManager.SetTextVariable("LMMI_DIRS_UNFAMILIAR_TEXT", PickOne(
                        L("lmmi_dirs_unfamiliar_1", "Me? I've never set foot here before either. Ask a local."),
                        L("lmmi_dirs_unfamiliar_2", "I know this town about as well as you do. Which is to say, not at all."),
                        L("lmmi_dirs_unfamiliar_3", "Don't look at me. I'm as lost as you are.")
                    ));
                    return true;
                }, null);

            // OUTCOME 3: Glad
            starter.AddDialogLine("lmmi_dirs_glad_resp", "lmmi_dirs_willingness_check", "lmmi_dirs_choices",
                "{=lmmi_glad}{LMMI_DIRS_GLAD_TEXT}",
                () => {
                    if (_currentWillingness != CitizenWillingness.Glad) return false;
                    MBTextManager.SetTextVariable("LMMI_DIRS_GLAD_TEXT", PickOne(
                        L("lmmi_dirs_glad_1", "Of course! Where would you like to go?"),
                        L("lmmi_dirs_glad_2", "Happy to help! What are you looking for?"),
                        L("lmmi_dirs_glad_3", "Sure thing! Where do you need to get to?")
                    ));
                    return true;
                }, null);

            // OUTCOME 4: Greedy
            starter.AddDialogLine("lmmi_dirs_greedy_resp", "lmmi_dirs_willingness_check", "lmmi_dirs_greedy_options",
                "{=lmmi_greedy}{LMMI_DIRS_GREEDY_TEXT}",
                () => {
                    if (_currentWillingness != CitizenWillingness.Greedy) return false;
                    MBTextManager.SetTextVariable("LMMI_DIRS_GREEDY_TEXT", PickOne(
                        L("lmmi_dirs_greedy_1", "I know this town well... every back alley and shortcut. For {BRIBE_AMOUNT}{GOLD_ICON}, I'll take you wherever you need to go."),
                        L("lmmi_dirs_greedy_2", "Directions? Nothing's free here. {BRIBE_AMOUNT}{GOLD_ICON} and I'll show you personally."),
                        L("lmmi_dirs_greedy_3", "You look lost, friend. For {BRIBE_AMOUNT}{GOLD_ICON}, I could help... for a small fee.")
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
                        L("lmmi_dirs_greedy_paid_1", "Excellent. Where to?"),
                        L("lmmi_dirs_greedy_paid_2", "A pleasure doing business. Now, where are we headed?"),
                        L("lmmi_dirs_greedy_paid_3", "Coin well spent. Where do you need to go?")
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

            // Tavern / backstreet (escort through tavern passage)
            starter.AddPlayerLine("lmmi_dirs_to_tavern", "lmmi_dirs_choices", "lmmi_dirs_follow",
                "{=lmmi_dirs_tavern}The tavern.",
                () => !HasFeatureAccess(Settlement.CurrentSettlement, SettlementMenuOptions.Features.Backstreet),
                () => { _pendingNavLocationId = "tavern"; _pendingNavFeature = SettlementMenuOptions.Features.Backstreet; });

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

                    _escortRunner = null;
                    var notableGuide = Hero.OneToOneConversationHero;
                    if (notableGuide != null && notableGuide.IsNotable)
                    {
                        _escortRunner = FindRunner(ConversationMission.OneToOneConversationAgent);
                        if (_escortRunner == null) return false;   // "lmmi_dirs_no_runner" answers instead
                        MBTextManager.SetTextVariable("LMMI_DIRS_FOLLOW_TEXT", notableGuide.IsGangLeader
                            ? L("lmmi_dirs_runner_gang", "You there — show this one to the {LOCATION}. Don't dawdle, and don't get lost.", "LOCATION", SettlementMenuOptions.GetFeatureDisplayNameText(_pendingNavFeature ?? ""))
                            : L("lmmi_dirs_runner", "You there! Take this one to the {LOCATION}. There's a coin in it for you.", "LOCATION", SettlementMenuOptions.GetFeatureDisplayNameText(_pendingNavFeature ?? "")));
                        return true;
                    }

                    MBTextManager.SetTextVariable("LMMI_DIRS_FOLLOW_TEXT", PickOne(
                        L("lmmi_dirs_follow_1", "Follow me!"),
                        L("lmmi_dirs_follow_2", "This way — stay close."),
                        L("lmmi_dirs_follow_3", "Right, let's go. Keep up.")
                    ));
                    return true;
                },
                () => BeginEscort()
            );

            // A notable with nobody around to send.
            starter.AddDialogLine(
                "lmmi_dirs_no_runner",
                "lmmi_dirs_follow",
                "hero_main_options",
                "{=lmmi_dirs_no_runner}My people are all busy. Ask around — somebody out there will point you the right way.",
                () =>
                {
                    var notableGuide = Hero.OneToOneConversationHero;
                    if (notableGuide == null || !notableGuide.IsNotable) return false;
                    if (CampaignTime.Now.IsNightTime && (_pendingNavFeature == SettlementMenuOptions.Features.Trade
                        || _pendingNavFeature == SettlementMenuOptions.Features.Smithy
                        || _pendingNavFeature == SettlementMenuOptions.Features.Arena
                        || _pendingNavFeature == SettlementMenuOptions.Features.Barber)) return false;   // the "closed" line answers
                    return FindRunner(ConversationMission.OneToOneConversationAgent) == null;
                },
                null, 105);
            
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
                        L("lmmi_dirs_closed_1", "It's empty there right now. Come back during the day."),
                        L("lmmi_dirs_closed_2", "Nothing to see there at night. Try again in daylight."),
                        L("lmmi_dirs_closed_3", "They'll be closed at this hour. Come back tomorrow.")
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
            return !HasFeatureAccess(settlement, SettlementMenuOptions.Features.Backstreet)
                || !HasFeatureAccess(settlement, SettlementMenuOptions.Features.Trade)
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

            // Capture agent NOW while conversation is still open. A notable sends a runner instead of walking.
            var talker = ConversationMission.OneToOneConversationAgent;
            var npcAgent = _escortRunner != null && _escortRunner.IsActive() ? _escortRunner : talker;
            if (talker != null && npcAgent != talker)
            {
                _agentWillingnessMap[talker.Index] = CitizenWillingness.AlreadyHelped;
                LmmiLog.Info($"BeginEscort: {Hero.OneToOneConversationHero?.Name} sends {npcAgent.Name} (id={npcAgent.Index}) to walk you.");
            }
            _escortRunner = null;
            LmmiLog.Info($"BeginEscort: Starting escort to '{locationId}' for '{feature}'. NPC={(npcAgent != null ? npcAgent.Name + " id=" + npcAgent.Index : "NULL")}");

            if (npcAgent == null)
            {
                LmmiLog.Warning("BeginEscort: OneToOneConversationAgent is null — cannot escort.");
                return;
            }

            // Scene mods (e.g. Alive Scenes) can spawn agents that cannot be controlled by vanilla EscortAgentBehavior.
            // In that case, don't consume the interaction / don't mark them as "already helped".
            if (!EscortBehavior.IsAgentEscortCapable(npcAgent))
            {
                LmmiLog.Warning($"BeginEscort: Agent '{npcAgent.Name}' (id={npcAgent.Index}) is not escort-capable. Aborting escort.");
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=lmmi_escort_unavailable}Sorry, I can't guide you there. Ask someone else.").ToString()));
                return;
            }

            // This NPC won't escort again during this settlement visit.
            _agentWillingnessMap[npcAgent.Index] = CitizenWillingness.AlreadyHelped;

            _pendingNpcAgent = npcAgent;
            _pendingSettlement = settlement;

            InformationManager.DisplayMessage(new InformationMessage(npcAgent != talker
                ? new TextObject("{=lmmi_escort_runner_start}A local will take you to the {LOCATION}. Follow them.")
                    .SetTextVariable("LOCATION", SettlementMenuOptions.GetFeatureDisplayNameText(feature)).ToString()
                : new TextObject("{=lmmi_escort_start}Follow me to the {LOCATION}!")
                    .SetTextVariable("LOCATION", SettlementMenuOptions.GetFeatureDisplayNameText(feature)).ToString()));

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

            if (!EscortBehavior.IsAgentEscortCapable(npcAgent))
            {
                LmmiLog.Warning($"PerformEscortSetup: Agent '{npcAgent.Name}' (id={npcAgent.Index}) is not escort-capable. Aborting escort.");
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
            if (!_escortBehavior.StartEscort(npcAgent, destinationPos.Value, feature, settlement))
            {
                LmmiLog.Warning("PerformEscortSetup: StartEscort returned false.");
            }
        }

        private void OnMissionTick(float dt)
        {
            if (_performEscortSetupPending && Mission.Current != null && Mission.Current.Mode != MissionMode.Conversation)
            {
                LmmiLog.Info("OnMissionTick: Conversation ended - performing escort setup.");
                _performEscortSetupPending = false;
                PerformEscortSetup();
                _watchAgent = _escortBehavior.IsActive ? _escortBehavior.NpcAgent : null;
                _watchTime = _watchAgent != null ? 0f : -1f;
                if (_watchAgent != null) _watchStartPos = _watchAgent.Position;
            }

            WatchForStuckEscort(dt);
        }

        /// <summary>An escort that hasn't moved after 8 seconds is stuck (pinned agent, blocked path): call it off.</summary>
        private void WatchForStuckEscort(float dt)
        {
            if (_watchTime < 0f || _watchAgent == null) return;
            if (!_escortBehavior.IsActive || _escortBehavior.NpcAgent != _watchAgent || !_watchAgent.IsActive())
            {
                _watchTime = -1f;
                _watchAgent = null;
                return;
            }
            if (Mission.Current?.Mode == MissionMode.Conversation) return;

            _watchTime += dt;
            if (_watchTime < 8f) return;

            if (_watchAgent.Position.Distance(_watchStartPos) < 1.5f)
            {
                LmmiLog.Warning($"Escort: {_watchAgent.Name} hasn't moved in 8s — stuck, calling it off.");
                _escortBehavior.Cancel();
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=lmmi_escort_stuck}Your guide can't seem to find a way through. Better ask someone else.").ToString()));
            }
            _watchTime = -1f;
            _watchAgent = null;
        }

        /// <summary>A passerby near the notable who can walk you somewhere: ordinary townsfolk, not busy, able to move.</summary>
        private Agent? FindRunner(Agent? near)
        {
            if (Mission.Current == null || near == null) return null;
            return Mission.Current.Agents
                .Where(a => a.IsActive() && a.IsHuman && a != Agent.Main && a != near
                            && a.Character is CharacterObject co && !co.IsHero && co.Occupation == Occupation.Townsfolk
                            && a.Position.Distance(near.Position) < 30f
                            && !(_agentWillingnessMap.TryGetValue(a.Index, out var w) && w == CitizenWillingness.AlreadyHelped)
                            && EscortBehavior.IsAgentEscortCapable(a))
                .OrderBy(a => a.Position.Distance(near.Position))
                .FirstOrDefault();
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
                public static bool Prefix(string optionId, GameMenu __instance)
                {
                    LmmiLog.Debug($"GameMenu.AddOption: optionId='{optionId}', menu='{__instance?.StringId}'");
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