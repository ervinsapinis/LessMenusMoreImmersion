using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LessMenusMoreImmersion.Contacts;
using LessMenusMoreImmersion.Logging;
using LessMenusMoreImmersion.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// "Word gets around." A per-settlement ledger of what you've done there — quests for its notables,
    /// tournaments won, hideouts cleared and bandits beaten nearby, its villages defended or raided, the town
    /// taken — fading slowly toward nothing. Feeds every notable's disposition (half weight next to their own
    /// relation), so the people of a town you've served have heard of you even if you never met them.
    /// Knowing the way is permanent; gratitude and grudges are not.
    /// </summary>
    /// <summary>How a settlement speaks of you (town standing, crime included).</summary>
    public enum StandingBand { Despised, Disliked, Unknown, Known, Respected, Honored }

    public class TownStandingBehavior : CampaignBehaviorBase
    {
        private const float Cap = 50f;
        private const float DecayPerDay = 1f / 7f;
        private const float BanditWeeklyCap = 3f;

        [NonSerialized] private Dictionary<string, float> _standing = new Dictionary<string, float>();
        // settlement StringId -> hero StringId of the notable you most recently helped there (for "word is you helped...")
        [NonSerialized] private Dictionary<string, string> _lastHelped = new Dictionary<string, string>();
        // notable StringIds who owe you thanks for a finished quest (arrival scene)
        [NonSerialized] private HashSet<string> _owesThanks = new HashSet<string>();
        // settlement StringId -> standing gained from beating bandits nearby this week
        [NonSerialized] private Dictionary<string, float> _banditCredit = new Dictionary<string, float>();
        [NonSerialized] private int _banditWeek = -1;
        // settlement StringId -> campaign hour you last stepped up for a local (rescue, brawl, purse)
        [NonSerialized] private Dictionary<string, double> _steppedUp = new Dictionary<string, double>();
        // "heroId@settlementId" — notables who already remarked on it
        [NonSerialized] private HashSet<string> _remarked = new HashSet<string>();
        private const float SteppedUpDays = 60f;

        private static TownStandingBehavior? Instance => Campaign.Current?.GetCampaignBehavior<TownStandingBehavior>();

        /// <summary>Stored standing plus the live penalty for your crime rating with the settlement's realm.</summary>
        public static float Get(Settlement? settlement)
        {
            if (settlement == null) return 0f;
            var self = Instance;
            float stored = self != null && self._standing.TryGetValue(settlement.StringId, out var v) ? v : 0f;
            float crime = settlement.MapFaction?.MainHeroCrimeRating ?? 0f;
            return stored - crime / 4f - WarPenalty(settlement);
        }

        private const float WarPenaltyAmount = 10f;

        /// <summary>
        /// Computed, never stored: while your clan sits in a kingdom (vassal or mercenary) at war with the settlement's
        /// realm, its people think worse of you. Applied once, here, so every band/price/fine/dialog reading sees it.
        /// </summary>
        private static float WarPenalty(Settlement settlement)
        {
            try
            {
                var mine = Clan.PlayerClan?.Kingdom;
                var theirs = settlement.MapFaction;
                if (mine == null || theirs == null || theirs == mine || settlement.OwnerClan == Clan.PlayerClan) return 0f;
                return mine.IsAtWarWith(theirs) ? WarPenaltyAmount : 0f;
            }
            catch { return 0f; }
        }

        public static StandingBand BandOf(float standing) =>
            standing <= -25f ? StandingBand.Despised
            : standing <= -10f ? StandingBand.Disliked
            : standing >= 40f ? StandingBand.Honored
            : standing >= 25f ? StandingBand.Respected
            : standing >= 10f ? StandingBand.Known
            : StandingBand.Unknown;

        public static StandingBand Band(Settlement? settlement) => BandOf(Get(settlement));

        /// <summary>What shopkeepers charge on top: the spread between buying and selling, eased or worsened.</summary>
        public static float PriceFactor(Settlement? settlement) => Band(settlement) switch
        {
            StandingBand.Honored => 0.8f,
            StandingBand.Respected => 0.88f,
            StandingBand.Known => 0.95f,
            StandingBand.Disliked => 1.12f,
            StandingBand.Despised => 1.25f,
            _ => 1f,
        };

        /// <summary>The watch and the magistrate: how hard they come down on you here.</summary>
        public static float FineFactor(Settlement? settlement) => Band(settlement) switch
        {
            StandingBand.Honored => 0.5f,
            StandingBand.Respected => 0.75f,
            StandingBand.Known => 0.9f,
            StandingBand.Disliked => 1.25f,
            StandingBand.Despised => 1.5f,
            _ => 1f,
        };

        public static int SentenceDays(Settlement? settlement) => Band(settlement) switch
        {
            StandingBand.Honored => -2,
            StandingBand.Respected => -1,
            StandingBand.Disliked => 1,
            StandingBand.Despised => 2,
            _ => 0,
        };

        public static TextObject BandName(StandingBand band) => new TextObject(band switch
        {
            StandingBand.Honored => "{=lmmi_band_honored}honored",
            StandingBand.Respected => "{=lmmi_band_respected}respected",
            StandingBand.Known => "{=lmmi_band_known}known",
            StandingBand.Disliked => "{=lmmi_band_disliked}disliked",
            StandingBand.Despised => "{=lmmi_band_despised}despised",
            _ => "{=lmmi_band_unknown}a stranger",
        });

        /// <summary>You stood up for a local here recently: the town talks, and prejudice counts for less.</summary>
        public static bool SteppedUp(Settlement? settlement)
        {
            var self = Instance;
            return settlement != null && self != null && self._steppedUp.TryGetValue(settlement.StringId, out var at)
                   && CampaignTime.Now.ToHours - at < SteppedUpDays * CampaignTime.HoursInDay;
        }

        public static void MarkSteppedUp(Settlement? settlement, float standing, string why)
        {
            var self = Instance;
            if (self == null || settlement == null) return;
            self._steppedUp[settlement.StringId] = CampaignTime.Now.ToHours;
            self._remarked.RemoveWhere(k => k.EndsWith("@" + settlement.StringId));
            self.Add(settlement, standing, why);
        }

        /// <summary>True the first time a notable brings up your latest good deed in their town.</summary>
        public static bool RemarkOnce(Hero notable)
        {
            var self = Instance;
            if (self == null || notable.CurrentSettlement == null || !SteppedUp(notable.CurrentSettlement)) return false;
            return self._remarked.Add(notable.StringId + "@" + notable.CurrentSettlement.StringId);
        }

        /// <summary>For other systems' deeds (e.g. a defaulted debt).</summary>
        public static void Adjust(Settlement? settlement, float amount, string why) => Instance?.Add(settlement, amount, why);

        public static Hero? LastHelped(Settlement settlement)
        {
            var self = Instance;
            if (self == null || !self._lastHelped.TryGetValue(settlement.StringId, out var id)) return null;
            return Hero.FindFirst(h => h.StringId == id);
        }

        /// <summary>True once per finished quest: the notable you helped may come to thank you.</summary>
        public static bool OwesThanks(Hero hero) => Instance?._owesThanks.Contains(hero.StringId) == true;
        public static void Thanked(Hero hero) => Instance?._owesThanks.Remove(hero.StringId);

        public override void RegisterEvents()
        {
            CampaignEvents.OnIssueUpdatedEvent.AddNonSerializedListener(this, OnIssueUpdated);
            CampaignEvents.TournamentFinished.AddNonSerializedListener(this, OnTournamentFinished);
            CampaignEvents.OnPlayerBattleEndEvent.AddNonSerializedListener(this, OnPlayerBattleEnd);
            CampaignEvents.VillageLooted.AddNonSerializedListener(this, OnVillageLooted);
            CampaignEvents.OnSiegeAftermathAppliedEvent.AddNonSerializedListener(this, OnSiegeAftermath);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        [NonSerialized] private bool _startRolled;

        /// <summary>Once per save: every town and castle starts with a feel for you — kin warmer, rivals colder, the rest by chance.</summary>
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try
            {
                if (_startRolled) return;
                var culture = Hero.MainHero?.Culture;
                if (culture == null) return;
                int set = 0, kin = 0, rival = 0;
                foreach (var s in Settlement.All)
                {
                    if (!(s.IsTown || s.IsCastle)) continue;
                    if (_standing.TryGetValue(s.StringId, out var cur) && Math.Abs(cur) > 0.001f) continue;
                    float v = MBRandom.RandomInt(-4, 5);
                    if (s.Culture == culture) { v += 3f; kin++; }
                    else if (CultureRelations.Multiplier(s.Culture?.StringId, culture.StringId) >= 1.5f) { v -= 3f; rival++; }
                    v = Math.Max(-24f, Math.Min(24f, v));
                    if (Math.Abs(v) < 0.001f) continue;
                    _standing[s.StringId] = v;
                    set++;
                }
                _startRolled = true;
                LmmiLog.Info($"Town standing: starting standing rolled for {set} towns and castles ({kin} kin, {rival} rival culture).");
            }
            catch (Exception ex) { LmmiLog.Error("TownStandingBehavior.OnSessionLaunched threw", ex); }
        }

        /// <summary>Walking in, you can tell how they speak of you here.</summary>
        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            try
            {
                if (party != MobileParty.MainParty || settlement == null || settlement.IsHideout) return;
                if (settlement.OwnerClan == Clan.PlayerClan) return;   // your own: they know you
                var band = Band(settlement);
                if (WarPenalty(settlement) > 0f && band < BandOf(Get(settlement) + WarPenaltyAmount))
                    InformationManager.DisplayMessage(new InformationMessage(
                        Flavor.Pick("{=lmmi_standing_war_enter}They know whose banner you ride under.",
                            "{=lmmi_standing_war_enter_2}Hard eyes follow your banner through {SETTLEMENT}. They know who you serve.",
                            "{=lmmi_standing_war_enter_3}Word of the war has come before you. {SETTLEMENT} knows whose colors you wear.")
                            .SetTextVariable("SETTLEMENT", settlement.Name).ToString(), Colors.Red));
                if (band == StandingBand.Unknown) return;
                var line = (band switch
                {
                    StandingBand.Honored => Flavor.Pick("{=lmmi_standing_enter_honored}In {SETTLEMENT}, people stop to greet you by name. You're honored here.",
                        "{=lmmi_standing_enter_honored_2}In {SETTLEMENT}, children run to see you pass. You're honored here.",
                        "{=lmmi_standing_enter_honored_3}{SETTLEMENT} greets you like one of its own. You're honored here."),
                    StandingBand.Respected => Flavor.Pick("{=lmmi_standing_enter_respected}Your name is well spoken of in {SETTLEMENT}.",
                        "{=lmmi_standing_enter_respected_2}People nod to you in {SETTLEMENT}. Your name carries weight here.",
                        "{=lmmi_standing_enter_respected_3}In {SETTLEMENT} they speak well of you."),
                    StandingBand.Known => Flavor.Pick("{=lmmi_standing_enter_known}In {SETTLEMENT}, a few faces turn your way — they've heard of you.",
                        "{=lmmi_standing_enter_known_2}A few people in {SETTLEMENT} seem to know your face.",
                        "{=lmmi_standing_enter_known_3}Your name's getting around in {SETTLEMENT}."),
                    StandingBand.Disliked => Flavor.Pick("{=lmmi_standing_enter_disliked}Muttering follows you through the gates of {SETTLEMENT}.",
                        "{=lmmi_standing_enter_disliked_2}Cold looks greet you in {SETTLEMENT}.",
                        "{=lmmi_standing_enter_disliked_3}People mutter as you ride into {SETTLEMENT}."),
                    _ => Flavor.Pick("{=lmmi_standing_enter_despised}In {SETTLEMENT}, doors close as you pass. They know what you've done here.",
                        "{=lmmi_standing_enter_despised_2}{SETTLEMENT} hates you. Shutters bang closed as you pass.",
                        "{=lmmi_standing_enter_despised_3}Spit lands near your boots as you enter {SETTLEMENT}. They haven't forgotten."),
                });
                line.SetTextVariable("SETTLEMENT", settlement.Name);
                InformationManager.DisplayMessage(new InformationMessage(line.ToString(),
                    band >= StandingBand.Known ? Colors.Green : Colors.Red));
            }
            catch (Exception ex) { LmmiLog.Error("TownStandingBehavior.OnSettlementEntered threw", ex); }
        }

        /// <summary>An honored name reaches the town's lord; so does a despised one.</summary>
        private void OnWeeklyTick()
        {
            try
            {
                foreach (var key in _standing.Keys.ToList())
                {
                    var settlement = Settlement.Find(key);
                    var lord = settlement?.OwnerClan?.Leader;
                    if (settlement == null || lord == null || lord == Hero.MainHero || !lord.IsAlive || settlement.OwnerClan == Clan.PlayerClan) continue;
                    var band = Band(settlement);
                    int delta = band == StandingBand.Honored ? 1 : band == StandingBand.Despised ? -1 : 0;
                    if (delta == 0) continue;
                    if (delta > 0 && lord.GetRelationWithPlayer() >= 30f) continue;
                    ChangeRelationAction.ApplyPlayerRelation(lord, delta, affectRelatives: false, showQuickNotification: false);
                    var msg = (delta > 0
                        ? Flavor.Pick("{=lmmi_standing_lord_up}Word of how {SETTLEMENT} speaks of you has reached {LORD}.",
                        "{=lmmi_standing_lord_up_2}{LORD} has heard how {SETTLEMENT} speaks of you.",
                        "{=lmmi_standing_lord_up_3}{SETTLEMENT}'s good opinion of you has reached {LORD}.")
                        : Flavor.Pick("{=lmmi_standing_lord_down}{LORD} has heard what {SETTLEMENT} thinks of you.",
                        "{=lmmi_standing_lord_down_2}{SETTLEMENT}'s grumbling about you has reached {LORD}.",
                        "{=lmmi_standing_lord_down_3}{LORD} has heard the complaints from {SETTLEMENT}."));
                    msg.SetTextVariable("SETTLEMENT", settlement.Name);
                    msg.SetTextVariable("LORD", lord.Name);
                    InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), delta > 0 ? Colors.Green : Colors.Red));
                    LmmiLog.Info($"Town standing: {lord.Name} hears of your {band} name in {settlement.Name} ({(delta > 0 ? "+" : "")}{delta}).");
                }
            }
            catch (Exception ex) { LmmiLog.Error("TownStandingBehavior.OnWeeklyTick threw", ex); }
        }

        private void Add(Settlement? settlement, float amount, string why)
        {
            if (settlement == null || amount == 0f) return;
            var before = Band(settlement);
            _standing.TryGetValue(settlement.StringId, out var current);
            float next = Math.Max(-Cap, Math.Min(Cap, current + amount));
            _standing[settlement.StringId] = next;
            LmmiLog.Info($"Town standing: {settlement.Name} {current:0.#} -> {next:0.#} ({(amount > 0 ? "+" : "")}{amount:0.#}, {why}).");
            var after = Band(settlement);
            if (after != before && settlement.OwnerClan != Clan.PlayerClan)
            {
                // Crossing a line: they speak of you differently now.
                var msg = (after > before
                    ? Flavor.Pick("{=lmmi_standing_band_up}Word gets around {SETTLEMENT}: you're {BAND} there now.",
                        "{=lmmi_standing_band_up_2}{SETTLEMENT} is talking about you: you're {BAND} there now.",
                        "{=lmmi_standing_band_up_3}Your name's rising in {SETTLEMENT}. You're {BAND} there now.")
                    : Flavor.Pick("{=lmmi_standing_band_down}Word gets around {SETTLEMENT}, and not kindly: you're {BAND} there now.",
                        "{=lmmi_standing_band_down_2}{SETTLEMENT} has turned against you: you're {BAND} there now.",
                        "{=lmmi_standing_band_down_3}Your name's sinking in {SETTLEMENT}. You're {BAND} there now."));
                msg.SetTextVariable("SETTLEMENT", settlement.Name);
                msg.SetTextVariable("BAND", BandName(after));
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), after > before ? Colors.Green : Colors.Red));
            }

            // Word travels from a village to its town, at half strength.
            Settlement? bound = null;
            if (settlement.IsVillage && settlement.Village?.Bound != null)
            {
                bound = settlement.Village.Bound;
                _standing.TryGetValue(bound.StringId, out var t);
                _standing[bound.StringId] = Math.Max(-Cap, Math.Min(Cap, t + amount / 2f));
            }

            // ...and to the towns and castles of the same people nearby, at a quarter — a regional name.
            if (Math.Abs(amount) >= 1f && settlement.Culture != null && LmmiSettingsProvider.EnableWordTravels)
            {
                var near = Settlement.All
                    .Where(o => o != settlement && o != bound && (o.IsTown || o.IsCastle) && o.Culture == settlement.Culture
                                && o.GatePosition.Distance(settlement.GatePosition) < WordRange)
                    .OrderBy(o => o.GatePosition.Distance(settlement.GatePosition)).Take(4).ToList();
                foreach (var o in near)
                {
                    _standing.TryGetValue(o.StringId, out var t);
                    _standing[o.StringId] = Math.Max(-Cap, Math.Min(Cap, t + amount / 4f));
                }
                if (near.Count > 0)
                {
                    LmmiLog.Info($"Town standing: word travels to {string.Join(", ", near.Select(o => o.Name))} ({amount / 4f:+0.#;-0.#}).");
                    if (Math.Abs(amount) >= 3f)
                    {
                        var msg = new TaleWorlds.Localization.TextObject("{=lmmi_standing_travels}Word travels: {PLACES} will hear of it too.");
                        msg.SetTextVariable("PLACES", string.Join(", ", near.Select(o => o.Name.ToString())));
                        InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Gray));
                    }
                }
            }
        }

        /// <summary>How far word of your deeds carries among a people (map units).</summary>
        private const float WordRange = 70f;

        // ---- Deeds ----

        private void OnIssueUpdated(IssueBase issue, IssueBase.IssueUpdateDetails details, Hero issueSolver)
        {
            try
            {
                if (issue == null) return;
                bool byPlayer = issueSolver == Hero.MainHero;
                var owner = issue.IssueOwner;
                var settlement = issue.IssueSettlement ?? owner?.CurrentSettlement ?? owner?.HomeSettlement;
                if (settlement == null) return;

                switch (details)
                {
                    case IssueBase.IssueUpdateDetails.IssueFinishedWithSuccess when byPlayer:
                    case IssueBase.IssueUpdateDetails.SentTroopsFinishedQuest:
                        Add(settlement, 4f, $"helped {owner?.Name}");
                        if (owner != null && owner.IsNotable)
                        {
                            _lastHelped[settlement.StringId] = owner.StringId;
                            _owesThanks.Add(owner.StringId);
                        }
                        break;
                    case IssueBase.IssueUpdateDetails.IssueFinishedWithBetrayal when byPlayer:
                        Add(settlement, -6f, $"betrayed {owner?.Name}");
                        ShameVoucher(owner);
                        break;
                    case IssueBase.IssueUpdateDetails.IssueFail when byPlayer:
                    case IssueBase.IssueUpdateDetails.SentTroopsFailedQuest:
                    case IssueBase.IssueUpdateDetails.IssueCancel when byPlayer:
                    case IssueBase.IssueUpdateDetails.IssueTimedOut when byPlayer:
                        Add(settlement, -3f, $"let {owner?.Name} down");
                        ShameVoucher(owner);
                        break;
                }
            }
            catch (Exception ex) { LmmiLog.Error("TownStandingBehavior.OnIssueUpdated threw", ex); }
        }

        /// <summary>A friend who vouched for you with this notable loses face when you let them down.</summary>
        private static void ShameVoucher(Hero? owner)
        {
            if (owner == null) return;
            var voucher = ContactsBehavior.VoucherFor(owner);
            if (voucher == null || !voucher.IsAlive) return;
            ChangeRelationAction.ApplyPlayerRelation(voucher, -3, affectRelatives: false);
            var msg = new TaleWorlds.Localization.TextObject("{=lmmi_voucher_shamed}{VOUCHER} vouched for you with {OWNER}. Word of how that turned out has reached {VOUCHER}.");
            msg.SetTextVariable("VOUCHER", voucher.Name);
            msg.SetTextVariable("OWNER", owner.Name);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));
            LmmiLog.Info($"Vouch liability: {voucher.Name} loses face over {owner.Name} (-3).");
        }

        private void OnTournamentFinished(CharacterObject winner, MBReadOnlyList<CharacterObject> participants, Town town, ItemObject prize)
        {
            try
            {
                if (winner != null && winner == Hero.MainHero?.CharacterObject && town != null)
                    Add(town.Settlement, 3f, "won the tournament");
            }
            catch (Exception ex) { LmmiLog.Error("TownStandingBehavior.OnTournamentFinished threw", ex); }
        }

        private void OnPlayerBattleEnd(MapEvent mapEvent)
        {
            try
            {
                if (mapEvent == null || !mapEvent.HasWinner || mapEvent.WinningSide != mapEvent.PlayerSide) return;

                var place = mapEvent.MapEventSettlement;
                var enemySide = mapEvent.PlayerSide == BattleSideEnum.Attacker ? mapEvent.DefenderSide : mapEvent.AttackerSide;
                bool enemyWereBandits = enemySide.Parties.Any(p => p.Party?.MobileParty?.IsBandit == true);

                if (mapEvent.IsHideoutBattle && place != null)
                {
                    Add(NearestTown(place.GetPosition2D), 4f, "cleared a hideout nearby");
                }
                else if (place != null && (place.IsTown || place.IsVillage || place.IsCastle) && mapEvent.PlayerSide == BattleSideEnum.Defender)
                {
                    Add(place, 6f, "defended it");
                }
                else if (place == null && enemyWereBandits)
                {
                    var town = NearestTown(mapEvent.Position.ToVec2());
                    if (town == null) return;
                    int week = (int)(CampaignTime.Now.ToDays / 7);
                    if (week != _banditWeek) { _banditWeek = week; _banditCredit.Clear(); }
                    _banditCredit.TryGetValue(town.StringId, out var credit);
                    if (credit >= BanditWeeklyCap) return;
                    _banditCredit[town.StringId] = credit + 1f;
                    Add(town, 1f, "cleared bandits from its roads");
                }
            }
            catch (Exception ex) { LmmiLog.Error("TownStandingBehavior.OnPlayerBattleEnd threw", ex); }
        }

        private void OnVillageLooted(Village village)
        {
            try
            {
                if (village?.Settlement?.LastAttackerParty == MobileParty.MainParty)
                    Add(village.Settlement, -15f, "you raided it");
            }
            catch (Exception ex) { LmmiLog.Error("TownStandingBehavior.OnVillageLooted threw", ex); }
        }

        private void OnSiegeAftermath(MobileParty attacker, Settlement settlement, SiegeAftermathAction.SiegeAftermath aftermath,
            Clan previousOwner, Dictionary<MobileParty, float> contributions)
        {
            try
            {
                bool playerTookPart = attacker == MobileParty.MainParty || (contributions?.ContainsKey(MobileParty.MainParty) ?? false);
                if (!playerTookPart || settlement == null) return;
                switch (aftermath)
                {
                    case SiegeAftermathAction.SiegeAftermath.ShowMercy: Add(settlement, -3f, "you took it, and showed mercy"); break;
                    case SiegeAftermathAction.SiegeAftermath.Pillage: Add(settlement, -10f, "you took it and let it be pillaged"); break;
                    case SiegeAftermathAction.SiegeAftermath.Devastate: Add(settlement, -25f, "you took it and devastated it"); break;
                }
            }
            catch (Exception ex) { LmmiLog.Error("TownStandingBehavior.OnSiegeAftermath threw", ex); }
        }

        private void OnDailyTick()
        {
            foreach (var key in _standing.Keys.ToList())
            {
                float v = _standing[key];
                float next = v > 0 ? Math.Max(0f, v - DecayPerDay) : Math.Min(0f, v + DecayPerDay);
                if (Math.Abs(next) < 0.01f) _standing.Remove(key);
                else _standing[key] = next;
            }
        }

        private static Settlement? NearestTown(Vec2 position) =>
            Town.AllTowns.Select(t => t.Settlement)
                .OrderBy(s => s.GetPosition2D.Distance(position))
                .FirstOrDefault();

        // ---- Save / load ----

        public override void SyncData(IDataStore dataStore)
        {
            string data = dataStore.IsLoading ? string.Empty : BuildSaveString();
            dataStore.SyncData("lmmi_standing_v1", ref data);
            if (dataStore.IsLoading) LoadSaveString(data);
        }

        /// <summary>Entries separated by '|': "S;settlementId;value", "H;settlementId;heroId", "T;heroId".</summary>
        private string BuildSaveString()
        {
            var parts = new List<string>();
            parts.AddRange(_standing.Select(kv => "S;" + kv.Key + ";" + kv.Value.ToString("R", CultureInfo.InvariantCulture)));
            parts.AddRange(_lastHelped.Select(kv => "H;" + kv.Key + ";" + kv.Value));
            parts.AddRange(_owesThanks.Select(id => "T;" + id));
            parts.AddRange(_steppedUp.Select(kv => "U;" + kv.Key + ";" + kv.Value.ToString("R", CultureInfo.InvariantCulture)));
            parts.AddRange(_remarked.Select(k => "K;" + k));
            if (_startRolled) parts.Add("I;1");
            return string.Join("|", parts);
        }

        private void LoadSaveString(string data)
        {
            _standing = new Dictionary<string, float>();
            _lastHelped = new Dictionary<string, string>();
            _owesThanks = new HashSet<string>();
            _steppedUp = new Dictionary<string, double>();
            _remarked = new HashSet<string>();
            _startRolled = false;
            if (string.IsNullOrEmpty(data)) return;
            foreach (var entry in data.Split('|'))
            {
                var p = entry.Split(';');
                if (p.Length == 3 && p[0] == "S" && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    _standing[p[1]] = v;
                else if (p.Length == 3 && p[0] == "H")
                    _lastHelped[p[1]] = p[2];
                else if (p.Length == 2 && p[0] == "T")
                    _owesThanks.Add(p[1]);
                else if (p.Length == 3 && p[0] == "U" && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var at))
                    _steppedUp[p[1]] = at;
                else if (p.Length == 2 && p[0] == "K")
                    _remarked.Add(p[1]);
                else if (p.Length == 2 && p[0] == "I")
                    _startRolled = true;
            }
        }
    }
}
