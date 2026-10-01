using System;
using System.Globalization;
using LessMenusMoreImmersion.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace LessMenusMoreImmersion.Contacts
{
    internal enum Disposition
    {
        Contempt,   // won't deal with you
        Wary,       // deals for coin
        Friendly,   // does favors
        Trusted     // does big favors, if genuine
    }

    /// <summary>
    /// Every term behind a notable's disposition, kept so it can be logged and shown for tuning.
    /// score = deeds + power + affinity − prejudice. See DESIGN_BEING_KNOWN.md.
    /// </summary>
    internal sealed class DispositionBreakdown
    {
        public float Relation;
        public float HonorWeight;
        public float TownStanding;
        public float Deeds;

        public float TierPower;
        public float Position;
        public float Power;

        public float Affinity;

        public float BasePrejudice;
        public float CulturePair;
        public float Tolerance;
        public float Fade;
        public float Occupier;
        public bool Vouched;
        public bool SteppedUp;
        public float Prejudice;

        public float Score;
        public Disposition Level;

        /// <summary>They like you for what you've done and who you are, not for your title.</summary>
        public bool Genuine;

        public bool IsSycophant => !Genuine && Level >= Disposition.Friendly;

        public string Describe(Hero notable) => string.Format(CultureInfo.InvariantCulture,
            "{0}: {1} {2:0} ({3}) = deeds {4:0} [rel {5:0} × honor {6:0.##} + ½ town standing {23:0.#}] + power {7:0} [tier {8:0} + position {9:0}] " +
            "+ affinity {10:0} − prejudice {11:0} [base {12:0} × culture {13:0.##} × mercy {14:0.##} × (1 − fade {15:0.##}) × occupier {16:0.##} × vouched {22:0.##} × stepped up {24:0.##}] " +
            "| traits M{17} V{18} H{19} G{20} C{21}",
            notable.Name, Level, Score, IsSycophant ? "sycophant" : Genuine ? "genuine" : "indifferent",
            Deeds, Relation, HonorWeight, Power, TierPower, Position,
            Affinity, Prejudice, BasePrejudice, CulturePair, Tolerance, Fade, Occupier,
            notable.GetTraitLevel(DefaultTraits.Mercy), notable.GetTraitLevel(DefaultTraits.Valor),
            notable.GetTraitLevel(DefaultTraits.Honor), notable.GetTraitLevel(DefaultTraits.Generosity),
            notable.GetTraitLevel(DefaultTraits.Calculating), Vouched ? 0.25f : 1f, TownStanding, SteppedUp ? 0.6f : 1f);
    }

    /// <summary>
    /// How a notable regards the player: what you've done (deeds), who you are (power), whether you're
    /// kindred spirits (affinity), and who you are not (prejudice) — each shaped by the notable's personality.
    /// </summary>
    internal static class NotableDisposition
    {
        // Retuned 2026-09-25: a tier-0 foreigner was Contempt everywhere. Nobodies are ignored, not despised.
        private static readonly float[] TierCurve = { -8f, -4f, 0f, 8f, 18f, 30f, 45f };
        private static readonly float[] FadeByTier = { 0f, 0f, 0.2f, 0.4f, 0.6f, 0.8f, 0.9f };

        // Not cached: DefaultTraits hands out new objects per campaign.
        private static TraitObject[] PersonalityTraits => new[]
        {
            DefaultTraits.Mercy, DefaultTraits.Valor, DefaultTraits.Honor, DefaultTraits.Generosity, DefaultTraits.Calculating
        };

        public static Disposition Get(Hero notable) => Evaluate(notable).Level;

        public static DispositionBreakdown Evaluate(Hero notable)
        {
            var b = new DispositionBreakdown();
            var player = Hero.MainHero;
            int tier = Math.Max(0, Math.Min(6, Clan.PlayerClan?.Tier ?? 0));

            // Deeds: honorable people judge you by what you've done.
            b.Relation = notable.GetRelationWithPlayer();
            b.HonorWeight = ByTrait(notable, DefaultTraits.Honor, 0.7f, 0.85f, 1f, 1.25f, 1.5f);
            // Word gets around: what you've done for the rest of the town counts at half weight.
            b.TownStanding = Behaviors.TownStandingBehavior.Get(notable.CurrentSettlement);
            b.Deeds = b.Relation * b.HonorWeight + 0.5f * b.TownStanding;

            // Power: your renown and your place in the realm. The brave respect it, cowards fear it.
            float valorRespect = notable.GetTraitLevel(DefaultTraits.Valor) != 0 ? 1.25f : 1f;
            b.TierPower = TierCurve[tier] * PowerWeight(notable) * valorRespect;
            b.Position = PositionBonus(notable.CurrentSettlement);
            b.Power = b.TierPower + b.Position;

            // Affinity: kindred spirits warm to each other — once your name is known.
            float fame = tier <= 1 ? 0f : tier == 2 ? 0.5f : 1f;
            float likeness = 0f;
            if (player != null)
                foreach (var trait in PersonalityTraits)
                    likeness += notable.GetTraitLevel(trait) * player.GetTraitLevel(trait);
            b.Affinity = Clamp(fame * likeness * 2.5f, -15f, 15f);

            // Prejudice: how much your culture counts against you, softened by mercy and, for the
            // pragmatic, by your rise in the world.
            b.BasePrejudice = BasePrejudice(notable);
            b.CulturePair = CultureRelations.Multiplier(notable.Culture?.StringId, player?.Culture?.StringId);
            b.Tolerance = ByTrait(notable, DefaultTraits.Mercy, 1.6f, 1.3f, 1f, 0.6f, 0.25f);
            float pragmatism = ByTrait(notable, DefaultTraits.Calculating, 0.6f, 0.8f, 1f, 1.15f, 1.3f);
            b.Fade = Math.Min(1f, FadeByTier[tier] * pragmatism);
            b.Occupier = IsOccupier(notable) ? 1.25f : 1f;
            b.Vouched = Behaviors.ContactsBehavior.IsVouchedFor(notable);
            // You stood up for one of theirs: the town talks, and your people count for less against you.
            b.SteppedUp = Behaviors.TownStandingBehavior.SteppedUp(notable.CurrentSettlement);
            b.Prejudice = b.BasePrejudice * b.CulturePair * b.Tolerance * (1f - b.Fade) * b.Occupier * (b.Vouched ? 0.25f : 1f) * (b.SteppedUp ? 0.6f : 1f)
                          * LmmiSettingsProvider.ForeignerPrejudicePercent / 100f;

            b.Score = b.Deeds + b.Power + b.Affinity - b.Prejudice;
            b.Level = FromScore(b.Score);
            b.Genuine = b.Deeds + b.Affinity >= b.Power;
            return b;
        }

        public static bool IsForeigner(Hero notable) =>
            Hero.MainHero != null && notable.Culture != Hero.MainHero.Culture;

        /// <summary>What being Wary costs you with this notable: the generous ask less, the closefisted gouge.</summary>
        public static float Greed(Hero notable) =>
            ByTrait(notable, DefaultTraits.Generosity, 2f, 1.5f, 1f, 0.75f, 0.5f);

        private static float PowerWeight(Hero notable) =>
            notable.IsMerchant ? 1f :                             // merchants deal with people of means
            notable.IsArtisan ? 0.8f :
            notable.IsGangLeader ? 0.4f :                         // a gang leader cares whether you're useful
            0.7f;                                                 // headmen, rural notables, others

        private static float BasePrejudice(Hero notable) =>
            notable.IsHeadman || notable.IsRuralNotable ? 12f :   // villages are insular
            notable.IsMerchant || notable.IsArtisan ? 10f :       // guilds protect their own
            notable.IsGangLeader ? 4f :                           // crime has no borders
            8f;

        private static float PositionBonus(Settlement? settlement)
        {
            var clan = Clan.PlayerClan;
            if (settlement == null || clan == null) return 0f;

            float bonus = 0f;
            if (settlement.OwnerClan == clan)
                bonus += 25f;

            var realm = settlement.MapFaction;
            if (clan.Kingdom != null && realm == clan.Kingdom)
            {
                if (clan.Kingdom.RulingClan == clan) bonus += 25f;
                else if (clan.IsUnderMercenaryService) bonus += 5f;
                else bonus += 10f;
            }
            else if (realm != null && clan.MapFaction != null && clan.MapFaction.IsAtWarWith(realm))
            {
                bonus -= 15f;
            }

            // The army at the gates.
            int men = MobileParty.MainParty?.MemberRoster?.TotalManCount ?? 0;
            if (men >= 200) bonus += 6f;
            else if (men >= 100) bonus += 3f;

            return bonus;
        }

        /// <summary>A native of a town ruled by the player's culture resents that culture a little more.</summary>
        private static bool IsOccupier(Hero notable)
        {
            var settlement = notable.CurrentSettlement;
            var playerCulture = Hero.MainHero?.Culture;
            return settlement != null && playerCulture != null
                   && notable.Culture == settlement.Culture
                   && notable.Culture != playerCulture
                   && settlement.OwnerClan?.Culture == playerCulture;
        }

        private static Disposition FromScore(float score) =>
            score < -20f ? Disposition.Contempt :
            score < 5f ? Disposition.Wary :
            score < 25f ? Disposition.Friendly :
            Disposition.Trusted;

        /// <summary>Picks a value by trait level −2 … +2.</summary>
        private static float ByTrait(Hero hero, TraitObject trait, float minus2, float minus1, float zero, float plus1, float plus2)
        {
            switch (Math.Max(-2, Math.Min(2, hero.GetTraitLevel(trait))))
            {
                case -2: return minus2;
                case -1: return minus1;
                case 1: return plus1;
                case 2: return plus2;
                default: return zero;
            }
        }

        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
    }
}
