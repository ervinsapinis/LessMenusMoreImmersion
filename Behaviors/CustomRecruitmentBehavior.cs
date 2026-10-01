using System;
using System.Collections.Generic;
using System.Linq;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using LessMenusMoreImmersion.Contacts;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Behaviors
{
    public class CustomRecruitmentMenuBehavior : CampaignBehaviorBase
    {
        private static Dictionary<string, bool> settlementsWithRecruitmentOrganizer = new Dictionary<string, bool>();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("settlementsWithRecruitmentOrganizer", ref settlementsWithRecruitmentOrganizer);
        }

        private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
        {
            try
            {
                // Add notable recruitment dialogs
                AddNotableRecruitmentDialogs(campaignGameStarter);
                AddNotableDirectRecruitmentDialogs(campaignGameStarter);
                LmmiLog.Debug("CustomRecruitmentMenuBehavior: notable recruitment dialogs registered.");
            }
            catch (Exception ex)
            {
                LmmiLog.Error("CustomRecruitmentMenuBehavior.OnSessionLaunched failed", ex);
            }
        }

        /// <summary>
        /// Adds recruitment arrangement dialogs for any notable
        /// </summary>
        /// <param name="campaignGameStarter">The campaign game starter used to add dialogs.</param>
        protected void AddNotableRecruitmentDialogs(CampaignGameStarter campaignGameStarter)
        {
            // Check if current character is a notable in current settlement
            bool isNotableInCurrentSettlement() =>
                CharacterObject.OneToOneConversationCharacter != null &&
                CharacterObject.OneToOneConversationCharacter.IsHero &&
                Settlement.CurrentSettlement != null &&
                Settlement.CurrentSettlement.Notables.Contains(CharacterObject.OneToOneConversationCharacter.HeroObject);

            // Player option: Ask about recruitment arrangement (available to any notable)
            campaignGameStarter.AddPlayerLine(
                "notable_recruitment_arrangement_ask",
                "hero_main_options",
                "notable_recruitment_arrangement_response",
                "{=notable_recruitment_ask}Can you help organize willing recruits for my company?",
                isNotableInCurrentSettlement,
                null
            );

            // Notable explains the arrangement
            campaignGameStarter.AddDialogLine(
                "notable_recruitment_arrangement_response",
                "notable_recruitment_arrangement_response",
                "notable_recruitment_arrangement_offer",
                "{=notable_recruitment_response}Organizing recruits requires cooperation between all the notables here. We'd need to coordinate with everyone - some are... more expensive to convince than others. For {RECRUITMENT_COST}{GOLD_ICON}, I can arrange it so willing volunteers will be ready whenever you visit. Interested?",
                null,
                () => {
                    // Set the recruitment cost variable
                    int recruitmentCost = GetRecruitmentArrangementCost();
                    MBTextManager.SetTextVariable("RECRUITMENT_COST", recruitmentCost);
                }
            );

            // Player option: Accept recruitment arrangement
            campaignGameStarter.AddPlayerLine(
                "notable_recruitment_accept",
                "notable_recruitment_arrangement_offer",
                "notable_recruitment_accepted",
                "{=notable_recruitment_accept}Yes, arrange it. [Pay {RECRUITMENT_COST}{GOLD_ICON}]",
                () => {
                    int recruitmentCost = GetRecruitmentArrangementCost();
                    return Hero.MainHero.Gold >= recruitmentCost;
                },
                () => {
                    try
                    {
                        var settlement = Settlement.CurrentSettlement;
                        if (settlement == null)
                        {
                            LmmiLog.Warning("Recruitment arrangement consequence fired with no current settlement — aborting.");
                            return;
                        }

                        int cost = GetRecruitmentArrangementCost();
                        Hero.MainHero.ChangeHeroGold(-cost);

                        // ONLY mark recruitment as arranged - NO settlement access!
                        var settlementId = settlement.Id.ToString();
                        settlementsWithRecruitmentOrganizer[settlementId] = true;

                        LmmiLog.Info($"Recruitment arrangement unlocked in '{settlement.Name}' (id={settlementId}) for {cost} gold.");
                        InformationManager.DisplayMessage(new InformationMessage("You've arranged for willing recruits to be available when you visit."));
                    }
                    catch (Exception ex)
                    {
                        LmmiLog.Error("Recruitment arrangement consequence threw", ex);
                    }
                }
            );

            // Notable confirms arrangement
            campaignGameStarter.AddDialogLine(
                "notable_recruitment_accepted",
                "notable_recruitment_accepted",
                "close_window",
                "{=notable_recruitment_accepted}Excellent! I'll speak with the other notables. From now on, you'll find willing volunteers ready when you need them.",
                null,
                null
            );

            // Player option: Decline recruitment arrangement
            campaignGameStarter.AddPlayerLine(
                "notable_recruitment_decline",
                "notable_recruitment_arrangement_offer",
                "close_window",
                "{=notable_recruitment_decline}Perhaps another time.",
                null,
                null
            );
        }

        /// <summary>
        /// Gets the cost for arranging recruitment with notables based on their relations.
        /// </summary>
        /// <returns>The cost amount.</returns>
        private int GetRecruitmentArrangementCost()
        {
            try
            {
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null)
                {
                    LmmiLog.Debug("GetRecruitmentArrangementCost: no current settlement, falling back to 500.");
                    return 500; // Fallback cost
                }

                int totalCost = 0;
                var parts = new List<string>();

                foreach (var notable in settlement.Notables ?? Enumerable.Empty<Hero>())
                {
                    if (notable == null) continue;

                    int baseCostPerNotable = 250; // Base cost per notable
                    int relation = (int)notable.GetRelationWithPlayer();

                    // Calculate cost for this notable
                    int notableCost = baseCostPerNotable;

                    if (relation > 0)
                    {
                        // Positive relation: discount
                        notableCost -= (relation * 30);
                    }
                    else if (relation < 0)
                    {
                        // Negative relation: premium
                        notableCost += (Math.Abs(relation) * 75);
                    }

                    // Ensure minimum cost of 50g per notable
                    notableCost = Math.Max(notableCost, 50);

                    // "Some are more expensive to convince than others": scorn and suspicion cost extra.
                    float factor = DispositionCostFactor(notable);
                    notableCost = (int)Math.Round(notableCost * factor);
                    totalCost += notableCost;
                    parts.Add($"{notable.Name} {notableCost} (rel {relation}, x{factor:0.##})");
                }

                var stamp = settlement.StringId + ":" + (int)CampaignTime.Now.ToHours + ":" + totalCost;
                if (stamp != _lastCostLog)
                {
                    _lastCostLog = stamp;
                    LmmiLog.Info($"Recruitment arrangement cost in {settlement.Name}: {totalCost} = {string.Join(", ", parts)}");
                }
                return totalCost;
            }
            catch (Exception ex)
            {
                LmmiLog.Error("GetRecruitmentArrangementCost threw", ex);
                return 500;
            }
        }

        private static string? _lastCostLog;

        private static float BulkPriceFactor(Hero? notable)
        {
            if (notable == null || !LmmiSettingsProvider.EnableNotableDisposition) return 0.95f;
            switch (NotableDisposition.Get(notable))
            {
                case Disposition.Contempt: return 1.5f;
                case Disposition.Wary: return 1.25f;
                case Disposition.Trusted: return 0.85f;
                default: return 0.95f;
            }
        }

        /// <summary>How the notable you're talking to regards you, or null when the system is off.</summary>
        private static Disposition? CurrentDisposition()
        {
            var n = Hero.OneToOneConversationHero;
            if (n == null || !n.IsNotable || !LmmiSettingsProvider.EnableNotableDisposition) return null;
            return NotableVoice.ForConversation(n).Level;
        }

        private static bool IsSameCulture() =>
            Hero.OneToOneConversationHero?.Culture == Hero.MainHero?.Culture;

        /// <summary>
        /// Contempt and Wary notables still deal — coin is coin — but grudgingly, and say so.
        /// Higher priority than the default lines; the same consequences run.
        /// </summary>
        private void AddGrudgingRecruitmentLines(CampaignGameStarter starter)
        {
            void SetArrangementCost() => MBTextManager.SetTextVariable("RECRUITMENT_COST", GetRecruitmentArrangementCost());

            // Arrangement offer
            starter.AddDialogLine("lmmi_arrange_offer_contempt", "notable_recruitment_arrangement_response", "notable_recruitment_arrangement_offer",
                "{=!}{LMMI_ARRANGE_CONTEMPT}",
                () => CurrentDisposition() == Disposition.Contempt && Flavor.Say("LMMI_ARRANGE_CONTEMPT",
                        "{=lmmi_arrange_offer_contempt}Organize recruits — for you? ...Coin is coin. But the others will want paying, and more for the likes of you. {RECRUITMENT_COST}{GOLD_ICON}. Take it or leave it.",
                        "{=lmmi_arrange_offer_contempt_2}Recruit for you? Your coin's no better than anyone's, but it spends. The others will want more, from your sort. {RECRUITMENT_COST}{GOLD_ICON}.",
                        "{=lmmi_arrange_offer_contempt_3}...Fine. For coin, and a good deal of it. Your kind pays double. {RECRUITMENT_COST}{GOLD_ICON}."), SetArrangementCost, 110);
            starter.AddDialogLine("lmmi_arrange_offer_wary", "notable_recruitment_arrangement_response", "notable_recruitment_arrangement_offer",
                "{=!}{LMMI_ARRANGE_WARY}",
                () => CurrentDisposition() == Disposition.Wary && Flavor.Say("LMMI_ARRANGE_WARY",
                        "{=lmmi_arrange_offer_wary}It can be arranged. It won't be cheap — the others don't know you any better than I do. {RECRUITMENT_COST}{GOLD_ICON}.",
                        "{=lmmi_arrange_offer_wary_2}I can arrange it, but it'll cost you. Nobody here knows you. {RECRUITMENT_COST}{GOLD_ICON}.",
                        "{=lmmi_arrange_offer_wary_3}Possible. Expensive, though — trust costs money. {RECRUITMENT_COST}{GOLD_ICON}."), SetArrangementCost, 110);

            // Arrangement accepted
            starter.AddDialogLine("lmmi_arrange_done_contempt", "notable_recruitment_accepted", "close_window",
                "{=!}{LMMI_ARRANGE_DONE_C}",
                () => CurrentDisposition() == Disposition.Contempt && Flavor.Say("LMMI_ARRANGE_DONE_C",
                        "{=lmmi_arrange_done_contempt}Fine. The volunteers will be there. Don't expect anyone to smile about it.",
                        "{=lmmi_arrange_done_contempt_2}Done. They'll come. Don't expect them to like it.",
                        "{=lmmi_arrange_done_contempt_3}The lads will be ready. Grudgingly."), null, 110);
            starter.AddDialogLine("lmmi_arrange_done_wary", "notable_recruitment_accepted", "close_window",
                "{=!}{LMMI_ARRANGE_DONE_W}",
                () => CurrentDisposition() == Disposition.Wary && Flavor.Say("LMMI_ARRANGE_DONE_W",
                        "{=lmmi_arrange_done_wary}Done. Don't make me regret it.",
                        "{=lmmi_arrange_done_wary_2}Arranged. Pay well and we'll get along.",
                        "{=lmmi_arrange_done_wary_3}It's done. Keep your end of things."), null, 110);

            // Bulk offer
            starter.AddDialogLine("lmmi_recruits_offer_contempt", "notable_recruits_response", "notable_recruits_offer",
                "{=lmmi_recruits_offer_contempt}{TROOP_LIST}. {TOTAL_COST}{GOLD_ICON}, and not a coin less. I don't haggle with your kind.",
                () => CurrentDisposition() == Disposition.Contempt,
                () => HandleBulkRecruitment(Hero.OneToOneConversationHero), 110);
            starter.AddDialogLine("lmmi_recruits_offer_wary", "notable_recruits_response", "notable_recruits_offer",
                "{=lmmi_recruits_offer_wary}I've got {TROOP_LIST}. {TOTAL_COST}{GOLD_ICON}. Take it or leave it.",
                () => CurrentDisposition() == Disposition.Wary,
                () => HandleBulkRecruitment(Hero.OneToOneConversationHero), 110);

            // Bulk accepted
            starter.AddDialogLine("lmmi_recruits_done_contempt", "notable_recruits_accepted", "hero_main_options",
                "{=lmmi_recruits_done_contempt}{LMMI_RECRUITS_SCORN}",
                () =>
                {
                    if (CurrentDisposition() != Disposition.Contempt) return false;
                    MBTextManager.SetTextVariable("LMMI_RECRUITS_SCORN", (IsSameCulture()
                        ? Flavor.Pick("{=lmmi_recruits_scorn_kin}Take them. Try not to get them all killed.",
                            "{=lmmi_recruits_scorn_kin_2}They're yours. Bring them back alive, if you can.",
                            "{=lmmi_recruits_scorn_kin_3}Go on, take them. Feed them properly.")
                        : Flavor.Pick("{=lmmi_recruits_scorn_foreign}Take them and go. Fools, following a foreigner — but that's their business, not mine.",
                            "{=lmmi_recruits_scorn_foreign_2}Take them. Following a foreigner — their mothers will weep.",
                            "{=lmmi_recruits_scorn_foreign_3}Off you go. Fools, all of them, to follow your sort.")));
                    return true;
                }, null, 110);
            starter.AddDialogLine("lmmi_recruits_done_wary", "notable_recruits_accepted", "hero_main_options",
                "{=!}{LMMI_RECRUITS_DONE_W}",
                () => CurrentDisposition() == Disposition.Wary && Flavor.Say("LMMI_RECRUITS_DONE_W",
                        "{=lmmi_recruits_done_wary}They're yours. Pay them on time and they'll follow you well enough.",
                        "{=lmmi_recruits_done_wary_2}Yours now. Treat them fairly.",
                        "{=lmmi_recruits_done_wary_3}They'll serve. Pay them and they'll stay."), null, 110);
        }

        private static float DispositionCostFactor(Hero notable)
        {
            if (!LmmiSettingsProvider.EnableNotableDisposition) return 1f;
            switch (NotableDisposition.Get(notable))
            {
                case Disposition.Contempt: return 2f;
                case Disposition.Wary: return 1.5f * NotableDisposition.Greed(notable);
                case Disposition.Trusted: return 0.75f;
                default: return 1f;
            }
        }

        /// <summary>
        /// Checks if the settlement has a paid recruitment organizer or should be auto-unlocked
        /// </summary>
        public bool HasRecruitmentOrganizer(Settlement settlement)
        {
            if (settlement == null) return false;

            // Check for auto-unlock first
            if (ShouldAutoUnlockRecruitment(settlement))
            {
                return true;
            }

            // Check if organizer was paid
            var settlementId = settlement.Id.ToString();
            return settlementsWithRecruitmentOrganizer.ContainsKey(settlementId) &&
                   settlementsWithRecruitmentOrganizer[settlementId];
        }

        /// <summary>
        /// Checks if player should automatically have recruitment access
        /// </summary>
        private bool ShouldAutoUnlockRecruitment(Settlement settlement)
        {
            if (settlement == null) return false;

            var playerClan = Clan.PlayerClan;
            if (playerClan == null) return false;

            // Auto-unlock conditions
            if (playerClan.Tier >= 5 || settlement.OwnerClan == playerClan)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Adds recruitment dialog options to existing notable conversations
        /// </summary>
        protected void AddNotableDirectRecruitmentDialogs(CampaignGameStarter campaignGameStarter)
        {
            AddGrudgingRecruitmentLines(campaignGameStarter);

            // Check if this notable has recruits available
            bool hasRecruitsAvailable() =>
                CharacterObject.OneToOneConversationCharacter?.HeroObject != null &&
                GetAvailableRecruits(CharacterObject.OneToOneConversationCharacter.HeroObject).Count > 0;

            // Player asks about direct recruitment
            campaignGameStarter.AddPlayerLine(
                "notable_ask_recruits",
                "hero_main_options",
                "notable_recruits_response",
                "{=ask_notable_recruits}What recruits do you have available?",
                hasRecruitsAvailable,
                null
            );

            // Notable responds with available recruits
            campaignGameStarter.AddDialogLine(
                "notable_recruits_response",
                "notable_recruits_response",
                "notable_recruits_offer",
                "{=notable_recruits_list}I can offer you: {TROOP_LIST} for {TOTAL_COST}{GOLD_ICON} total.",
                null,
                () => {
                    HandleBulkRecruitment(CharacterObject.OneToOneConversationCharacter.HeroObject);
                }
            );

            // Player accepts bulk recruitment
            campaignGameStarter.AddPlayerLine(
                "notable_accept_recruits",
                "notable_recruits_offer",
                "notable_recruits_accepted",
                "{=accept_bulk_recruits}I'll take them all. [Pay {TOTAL_COST}{GOLD_ICON}]",
                () => {
                    var notable = CharacterObject.OneToOneConversationCharacter?.HeroObject;
                    if (notable != null)
                    {
                        var availableTroops = GetAvailableRecruits(notable);
                        var totalCost = CalculateBulkRecruitmentCost(availableTroops, notable);
                        return Hero.MainHero.Gold >= totalCost;
                    }
                    return false;
                },
                () => {
                    ExecuteBulkRecruitment(CharacterObject.OneToOneConversationCharacter.HeroObject);
                }
            );

            // Notable confirms recruitment
            campaignGameStarter.AddDialogLine(
                "notable_recruits_accepted",
                "notable_recruits_accepted",
                "hero_main_options",
                "{=bulk_recruitment_success}Excellent! The lads are yours now. Fight well!",
                null,
                null
            );

            // Player declines bulk recruitment
            campaignGameStarter.AddPlayerLine(
                "notable_decline_recruits",
                "notable_recruits_offer",
                "notable_recruitment_declined",
                "{=decline_bulk_recruits}Perhaps another time.",
                null,
                null
            );

            // Add a transition back to main options
            campaignGameStarter.AddDialogLine(
                "notable_recruitment_declined_response",
                "notable_recruitment_declined",
                "hero_main_options",
                "{=notable_recruitment_ok}Very well. Perhaps when you're ready.",
                null,
                null
            );
        }

        /// <summary>
        /// Handles direct bulk recruitment from a notable
        /// </summary>
        private void HandleBulkRecruitment(Hero notable)
        {
            try
            {
                if (notable == null)
                {
                    LmmiLog.Warning("HandleBulkRecruitment called with null notable.");
                    return;
                }

                var availableTroops = GetAvailableRecruits(notable);
                if (availableTroops.Count == 0)
                {
                    InformationManager.DisplayMessage(new InformationMessage("I have no willing recruits at the moment."));
                    return;
                }

                var totalCost = CalculateBulkRecruitmentCost(availableTroops, notable);
                var troopDescription = GenerateTroopDescription(availableTroops);

                LmmiLog.Debug($"Bulk recruitment offer from '{notable.Name}': {availableTroops.Count} slots, total {totalCost} gold.");

                // Set variables for dialog
                MBTextManager.SetTextVariable("TROOP_LIST", troopDescription);
                MBTextManager.SetTextVariable("TOTAL_COST", totalCost);
            }
            catch (Exception ex)
            {
                LmmiLog.Error("HandleBulkRecruitment threw", ex);
            }
        }

        /// <summary>
        /// Executes the bulk recruitment transaction (sets slots to null like vanilla)
        /// </summary>
        private void ExecuteBulkRecruitment(Hero notable)
        {
            try
            {
                if (notable == null)
                {
                    LmmiLog.Warning("ExecuteBulkRecruitment called with null notable.");
                    return;
                }

                var availableTroops = GetAvailableRecruits(notable);
                var totalCost = CalculateBulkRecruitmentCost(availableTroops, notable);

                if (Hero.MainHero.Gold >= totalCost)
                {
                    Hero.MainHero.ChangeHeroGold(-totalCost);

                    // Add troops to player party and clear slots (like vanilla)
                    foreach (var (troop, count, slotIndex) in availableTroops)
                    {
                        if (troop == null) continue;
                        MobileParty.MainParty.MemberRoster.AddToCounts(troop, count);

                        if (notable.VolunteerTypes != null && slotIndex >= 0 && slotIndex < notable.VolunteerTypes.Length)
                        {
                            notable.VolunteerTypes[slotIndex] = null; // Clear the slot
                        }
                    }

                    LmmiLog.Info($"Bulk-recruited {availableTroops.Count} troops from '{notable.Name}' for {totalCost} gold.");
                    InformationManager.DisplayMessage(new InformationMessage("All recruits have joined your party!"));
                }
                else
                {
                    LmmiLog.Debug($"Player lacked gold for bulk recruitment ({Hero.MainHero.Gold}/{totalCost}).");
                    InformationManager.DisplayMessage(new InformationMessage("You don't have enough gold."));
                }
            }
            catch (Exception ex)
            {
                LmmiLog.Error("ExecuteBulkRecruitment threw", ex);
            }
        }

        /// <summary>
        /// Gets available recruits from a notable (exactly what vanilla shows)
        /// </summary>
        private List<(CharacterObject troop, int count, int slotIndex)> GetAvailableRecruits(Hero notable)
        {
            var recruits = new List<(CharacterObject, int, int)>();

            try
            {
                if (notable == null || notable.VolunteerTypes == null)
                    return recruits;

                // Check maximum slot index player can access (vanilla eligibility)
                int maxIndex = Campaign.Current.Models.VolunteerModel.MaximumIndexHeroCanRecruitFromHero(
                    Hero.MainHero, notable, -101);

                // Check each slot up to the max allowed
                for (int i = 0; i <= maxIndex && i < notable.VolunteerTypes.Length; i++)
                {
                    if (notable.VolunteerTypes[i] != null)
                    {
                        // Each slot has exactly 1 troop (not multiple)
                        recruits.Add((notable.VolunteerTypes[i], 1, i));
                    }
                }
            }
            catch (Exception ex)
            {
                LmmiLog.Error($"GetAvailableRecruits for '{notable?.Name}' threw", ex);
            }

            return recruits;
        }

        /// <summary>
        /// Calculates bulk recruitment cost with smaller discount
        /// </summary>
        private int CalculateBulkRecruitmentCost(List<(CharacterObject troop, int count, int slotIndex)> troops, Hero? notable)
        {
            try
            {
                int totalCost = 0;
                foreach (var (troop, count, slotIndex) in troops)
                {
                    if (troop == null) continue;

                    // Use proper recruitment cost calculation like vanilla
                    int individualCost = (int)Campaign.Current.Models.PartyWageModel.GetTroopRecruitmentCost(
                        troop, Hero.MainHero, false).ResultNumber;
                    totalCost += individualCost * count;
                }

                // Bulk discount for those who like you; a surcharge from those who don't, but they still sell.
                return (int)(totalCost * BulkPriceFactor(notable));
            }
            catch (Exception ex)
            {
                LmmiLog.Error("CalculateBulkRecruitmentCost threw", ex);
                return 0;
            }
        }

        /// <summary>
        /// Generates description of available troops (groups identical types)
        /// </summary>
        private string GenerateTroopDescription(List<(CharacterObject troop, int count, int slotIndex)> troops)
        {
            // Group identical troops
            var groupedTroops = troops
                .GroupBy(t => t.troop.StringId)
                .Select(g => new {
                    Troop = g.First().troop,
                    TotalCount = g.Sum(x => x.count)
                })
                .ToList();

            var descriptions = new List<string>();
            foreach (var group in groupedTroops)
            {
                if (group.TotalCount == 1)
                {
                    descriptions.Add($"1 {group.Troop.Name}");
                }
                else
                {
                    descriptions.Add($"{group.TotalCount} {group.Troop.Name}s"); // Add 's' for plural
                }
            }

            return string.Join(", ", descriptions);
        }
    }
}