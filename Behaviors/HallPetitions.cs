using System;
using System.Collections.Generic;
using System.Linq;
using LessMenusMoreImmersion.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// What the petitioners say, how lords rule on it, and what your own rulings do. Every kind has a few pleas (one is
    /// picked at random, the same one heard as a bark before a lord or spoken to you in your own hall) and a grant and a
    /// refusal for a lord to give. In your own court each ruling moves something real: loyalty, security, prosperity,
    /// a notable's opinion of you, a village's militia or hearths, your purse, the garrison, how the town speaks of you.
    /// Which case comes up is weighted by the place, the times and custom (<see cref="PickKind"/>).
    /// </summary>
    public partial class HallCourtBehavior
    {
        private static readonly Dictionary<PetitionKind, string[]> Pleas = new Dictionary<PetitionKind, string[]>
        {
            [PetitionKind.SeedGrain] = new[]
            {
                "{=lmmi_court_plea_grain}{LORD}, the tax collector took our seed grain along with the tax. Without it there's no crop next year — we'll starve before spring.",
                "{=lmmi_court_plea_grain_2}{LORD}, I come from {VILLAGE}. Your steward's men emptied the seed store with the tax. If we eat what's left we can't sow; if we sow it, we starve this winter.",
                "{=lmmi_court_plea_grain_3}{LORD}, they took the seed corn. All of it. My father farmed that land and his father before him, and we've never once failed to sow. Please.",
            },
            [PetitionKind.Pressed] = new[]
            {
                "{=lmmi_court_plea_pressed}{LORD}, your men took my son for the garrison. He's all I have to work the land — please, send him home.",
                "{=lmmi_court_plea_pressed_2}{LORD}, the recruiting sergeant came through {VILLAGE} and took my boy at spearpoint. He's fourteen. He can barely lift a shield.",
                "{=lmmi_court_plea_pressed_3}{LORD}, my husband's lame and my son was the only pair of hands we had. Your garrison took him. Who'll bring the harvest in?",
            },
            [PetitionKind.Poacher] = new[]
            {
                "{=lmmi_court_plea_poacher}{LORD}, it was one deer — one! My children hadn't eaten in three days. Have mercy!",
                "{=lmmi_court_plea_poacher_2}{LORD}, I set a snare for rabbits — rabbits! — and the forester says it's a hanging matter because it was in your wood.",
                "{=lmmi_court_plea_poacher_3}{LORD}, the deer were eating our barley down to the root. I only killed what was killing us.",
            },
            [PetitionKind.Widow] = new[]
            {
                "{=lmmi_court_plea_widow}{LORD}, my husband's not a month in the ground and his brother's claimed our field. Where am I to go?",
                "{=lmmi_court_plea_widow_2}{LORD}, my man fell in your service. Now the headman of {VILLAGE} says a widow can't hold a plough-land, and gives ours to his own nephew.",
                "{=lmmi_court_plea_widow_3}{LORD}, I've three children and a strip of land my husband cleared with his own hands. His brother wants it. I want justice.",
            },
            [PetitionKind.Quartered] = new[]
            {
                "{=lmmi_court_plea_quartered}{LORD}, your soldiers are quartered in my house. They've eaten my stores bare, and one of them won't keep his hands off my daughter.",
                "{=lmmi_court_plea_quartered_2}{LORD}, six of your soldiers sleep in my workshop. My tools are broken, my stock is drunk, and they laugh when I ask to be paid.",
                "{=lmmi_court_plea_quartered_3}{LORD}, the sergeant billets his men on our street and calls it our duty. They pay for nothing, and the night watch drinks with them.",
            },
            [PetitionKind.Feud] = new[]
            {
                "{=lmmi_court_plea_feud}{LORD}, {A} and {B} are at each other's throats over a debt — the whole street's taking sides. Only you can settle it.",
                "{=lmmi_court_plea_feud_2}{LORD}, {A} says {B} cheated on a cargo of wool, and {B} says the wool was wet. Their men have been brawling in the market for a week.",
                "{=lmmi_court_plea_feud_3}{LORD}, {A} swears {B} moved the boundary stones by night. Now neither will sell to the other's kin, and half the market stands shut.",
            },
            [PetitionKind.CartSeized] = new[]
            {
                "{=lmmi_court_plea_cart_1}{LORD}, the guards at the gate seized my cart — they say I owe a toll I've never paid in twenty years of trading here. That cart is my living.",
                "{=lmmi_court_plea_cart_2}{LORD}, your gate sergeant says my salt is contraband and has kept the cart, the mule and the salt. I have my papers! He won't even read them.",
                "{=lmmi_court_plea_cart_3}{LORD}, they took my cart at the gate, and now the sergeant's brother-in-law is selling my cloth in the market. I'm not a fool.",
            },
            [PetitionKind.Apprentice] = new[]
            {
                "{=lmmi_court_plea_apprentice_1}{LORD}, this boy is bound to me for seven years, and he's run off twice. I've fed him, housed him, taught him a trade — I want what I paid for.",
                "{=lmmi_court_plea_apprentice_2}{LORD}, my apprentice ran off to follow the soldiers. I dragged him back. Tell him his indenture holds, or every boy in the guild will try it.",
                "{=lmmi_court_plea_apprentice_3}{LORD}, the boy says I beat him. A master corrects his apprentice — that's the way of it. He's bound to me, and I want him bound.",
            },
            [PetitionKind.BridePrice] = new[]
            {
                "{=lmmi_court_plea_bride_1}{LORD}, my daughter married into a family from {VILLAGE}. They promised a yoke of oxen for her. We've had one ox — and a lame one.",
                "{=lmmi_court_plea_bride_2}{LORD}, the groom's father swears the bride price was paid in full. It wasn't! Half, and the rest 'after the harvest' — and the harvest's long in.",
                "{=lmmi_court_plea_bride_3}{LORD}, they took my girl and kept the price. I've no sons. That price was to keep her mother and me in our old age.",
            },
            [PetitionKind.Bandits] = new[]
            {
                "{=lmmi_court_plea_bandits_1}{LORD}, bandits came down on {VILLAGE} three nights running. They took our sheep, our grain, and the miller's daughter. We need soldiers.",
                "{=lmmi_court_plea_bandits_2}{LORD}, there's a band of cutthroats camped in the woods above {VILLAGE}. We can't take the carts to market without paying them a toll.",
                "{=lmmi_court_plea_bandits_3}{LORD}, we pay our taxes for your protection. Where was it when the bandits burned the barn at {VILLAGE}?",
            },
            [PetitionKind.Debtor] = new[]
            {
                "{=lmmi_court_plea_debtor_1}{LORD}, I owe the treasury two years' rent. The harvest failed, then my wife fell sick. Give me one more season and I'll pay every coin.",
                "{=lmmi_court_plea_debtor_2}{LORD}, the bailiffs are coming for my loom on market day. Without the loom I can never pay. With it — give me till spring.",
                "{=lmmi_court_plea_debtor_3}{LORD}, I borrowed against the harvest to pay your tax. The harvest rotted. Now I owe both. I'm begging you for time.",
            },
            [PetitionKind.Monopoly] = new[]
            {
                "{=lmmi_court_plea_monopoly_1}{LORD}, I'm the best smith in this town, and every tinker with a hammer undercuts me. Grant me the sole right to forge here, and the treasury will have its share.",
                "{=lmmi_court_plea_monopoly_2}{LORD}, outsiders sell shoddy blades in our market — they'll fail your soldiers in a siege. Let our guild alone forge for the town. We'll pay well for the charter.",
                "{=lmmi_court_plea_monopoly_3}{LORD}, a charter, that's all I ask: mine the only forge within the walls. You'd have good steel and a fat fee every year.",
            },
            [PetitionKind.Bloodshed] = new[]
            {
                "{=lmmi_court_plea_blood_1}{LORD}, my son killed a man in a tavern brawl. He didn't mean it — one blow, and the man hit his head. They'll hang him on market day. He's all I have.",
                "{=lmmi_court_plea_blood_2}{LORD}, the other man drew a knife first! My boy only defended himself. Ask anyone who was there — but nobody will speak against the dead man's family.",
                "{=lmmi_court_plea_blood_3}{LORD}, take my house, take everything — just don't take my son. We'll pay the blood price, whatever they ask.",
            },
            [PetitionKind.Bribes] = new[]
            {
                "{=lmmi_court_plea_bribes_1}{LORD}, this guard takes coin from the smugglers and fines honest folk for their trouble. I've seen it with my own eyes, and so has half the street.",
                "{=lmmi_court_plea_bribes_2}{LORD}, he wanted a silver from me to let my cart through the gate — and when I wouldn't pay, he broke my wheel. Look at him smirk.",
                "{=lmmi_court_plea_bribes_3}{LORD}, the watch on our street is bought and paid for, this one most of all. Thieves walk free and we pay for the privilege.",
            },
        };

        private static readonly Dictionary<PetitionKind, (string Grant, string Deny)> Rulings = new Dictionary<PetitionKind, (string, string)>
        {
            [PetitionKind.SeedGrain] = ("{=lmmi_court_grant_grain}Give them back their seed. A field that isn't sown pays no tax.",
                "{=lmmi_court_deny_grain}The tax is the tax. If I spare you, I spare every village in the valley."),
            [PetitionKind.Pressed] = ("{=lmmi_court_grant_pressed}Send the boy home. The garrison can spare one farmer's son.",
                "{=lmmi_court_deny_pressed}He serves. We're at war, and every man counts."),
            [PetitionKind.Poacher] = ("{=lmmi_court_grant_poacher}Let him go. A hungry man isn't a thief — this once.",
                "{=lmmi_court_deny_poacher}The law is the law. Take his hand."),
            [PetitionKind.Widow] = ("{=lmmi_court_grant_widow}The field is hers while she lives. Tell the brother he'll answer to me.",
                "{=lmmi_court_deny_widow}The land passes to the brother. That's custom."),
            [PetitionKind.CartSeized] = ("{=lmmi_court_grant_cart}Give the man his cart and his goods. And send the gate sergeant to me.",
                "{=lmmi_court_deny_cart}The toll is owed. Pay it, and you'll have your cart back."),
            [PetitionKind.Apprentice] = ("{=lmmi_court_grant_apprentice}The indenture holds. The boy goes back, and serves out his years.",
                "{=lmmi_court_deny_apprentice}A boy doesn't run from a good master twice. He's free of you. Find another."),
            [PetitionKind.BridePrice] = ("{=lmmi_court_grant_bride}The price was promised; the price will be paid. In full, by the next market day.",
                "{=lmmi_court_deny_bride}What passes between two families over a cup is no business of this hall."),
            [PetitionKind.Bandits] = ("{=lmmi_court_grant_bandits}Send twenty riders. I want those woods clean by the new moon.",
                "{=lmmi_court_deny_bandits}Every village wants soldiers. Build a palisade and post your own watch."),
            [PetitionKind.Debtor] = ("{=lmmi_court_grant_debtor}One more season. Not a day after.",
                "{=lmmi_court_deny_debtor}A debt is a debt. The bailiffs will do their work."),
            [PetitionKind.Monopoly] = ("{=lmmi_court_grant_monopoly}You'll have your charter — and the treasury will have its fee, every year, on the day.",
                "{=lmmi_court_deny_monopoly}A free market keeps prices honest. Forge better, and they'll buy from you."),
            [PetitionKind.Bloodshed] = ("{=lmmi_court_grant_blood}He'll not hang. Blood money to the dead man's kin, and a year on the walls.",
                "{=lmmi_court_deny_blood}A man is dead. The law takes a life for a life."),
            [PetitionKind.Bribes] = ("{=lmmi_court_grant_bribes}Strip him of his post, and give the coin back to those he robbed.",
                "{=lmmi_court_deny_bribes}A guard's word against a townsman's grudge? Bring me proof, or don't waste my time."),
        };

        private static int VariantsOf(PetitionKind kind) => Pleas.TryGetValue(kind, out var v) ? v.Length : 1;

        /// <summary>
        /// Which case comes before the hall. A town brings its markets, guilds, debts and watch; a castle its villages and
        /// woods. Hard times weigh in (a lawless street, a war, a lair nearby), so does custom, and in your own hall a case
        /// whose notable isn't there to care comes up less. Pressed men come before a lord only in wartime (he says so).
        /// Never the same kind twice running.
        /// </summary>
        private static PetitionKind PickKind(Settlement s, bool yours, List<Hero> notables, PetitionKind? last)
        {
            bool town = s.IsTown;
            bool villages = s.BoundVillages.Any(v => v?.Settlement != null);
            bool lawless = (s.Town?.Security ?? 50f) < 40f;
            var faction = s.MapFaction;
            bool atWar = faction != null && Kingdom.All.Any(k => !k.IsEliminated && !ReferenceEquals(k, faction) && FactionManager.IsAtWarAgainstFaction(k, faction));
            var gate = s.GatePosition;
            bool lairNear = villages && Settlement.All.Any(x => x.IsHideout && x.Hideout != null && x.Hideout.IsInfested && x.Position.Distance(gate) < 120f);
            string culture = s.Culture?.StringId ?? string.Empty;

            var weights = new List<(PetitionKind Kind, float Weight)>
            {
                (PetitionKind.SeedGrain, town ? 1f : 1.5f),
                (PetitionKind.Pressed, !atWar && !yours ? 0f : (town ? 1f : 1.2f) * (atWar ? 1.4f : 0.6f)),
                (PetitionKind.Poacher, town ? 0.8f : 1.5f),
                (PetitionKind.Widow, 1f),
                (PetitionKind.CartSeized, town ? 1.2f : 0.3f),
                (PetitionKind.Apprentice, town ? 1f : 0f),
                (PetitionKind.BridePrice, town ? 0.8f : 1.2f),
                (PetitionKind.Bandits, !villages ? 0f : (town ? 0.8f : 1.5f) * (lairNear ? 1.6f : 0.8f) * (lawless ? 1.3f : 1f)),
                (PetitionKind.Debtor, town ? 1f : 0.6f),
                (PetitionKind.Monopoly, town ? 0.8f : 0f),
                (PetitionKind.Bloodshed, (town ? 1f : 0.6f) * (lawless ? 1.3f : 1f)),
                (PetitionKind.Bribes, (town ? 1f : 0.5f) * (lawless ? 1.5f : 1f)),
            };
            if (yours)
            {
                weights.Add((PetitionKind.Quartered, (town ? 1f : 0.6f) * (atWar ? 1.4f : 1f)));
                if (notables.Count >= 2) weights.Add((PetitionKind.Feud, 1f));
            }

            float total = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                var (kind, w) = weights[i];
                w *= CultureWeight(culture, kind);
                if (yours && NotableMissing(kind, notables)) w *= 0.5f;
                if (last.HasValue && kind == last.Value) w = 0f;
                weights[i] = (kind, w);
                total += w;
            }
            if (total <= 0f) return last == PetitionKind.Widow ? PetitionKind.SeedGrain : PetitionKind.Widow;
            float roll = MBRandom.RandomFloat * total;
            foreach (var (kind, w) in weights)
            {
                if (roll < w) return kind;
                roll -= w;
            }
            return weights.Last(x => x.Weight > 0f).Kind;
        }

        /// <summary>What reaches the hall follows custom: imperial guilds and debts, Aserai caravans and bride-gifts, Khuzait herds and raiders, Vlandian forest law and levies, Sturgian blood feuds, Battanian clan law and woods.</summary>
        private static float CultureWeight(string culture, PetitionKind kind) => (culture, kind) switch
        {
            ("empire", PetitionKind.Monopoly) => 1.5f,
            ("empire", PetitionKind.Debtor) => 1.4f,
            ("empire", PetitionKind.Apprentice) => 1.4f,
            ("empire", PetitionKind.Bribes) => 1.3f,
            ("aserai", PetitionKind.CartSeized) => 1.6f,
            ("aserai", PetitionKind.BridePrice) => 1.5f,
            ("aserai", PetitionKind.Debtor) => 1.3f,
            ("khuzait", PetitionKind.BridePrice) => 1.5f,
            ("khuzait", PetitionKind.Bandits) => 1.4f,
            ("khuzait", PetitionKind.Poacher) => 0.5f,
            ("khuzait", PetitionKind.Monopoly) => 0.5f,
            ("khuzait", PetitionKind.Apprentice) => 0.6f,
            ("vlandia", PetitionKind.Poacher) => 1.6f,
            ("vlandia", PetitionKind.Pressed) => 1.3f,
            ("vlandia", PetitionKind.Widow) => 1.3f,
            ("vlandia", PetitionKind.Apprentice) => 1.2f,
            ("sturgia", PetitionKind.Bloodshed) => 1.5f,
            ("sturgia", PetitionKind.Feud) => 1.4f,
            ("sturgia", PetitionKind.Widow) => 1.2f,
            ("sturgia", PetitionKind.Bandits) => 1.2f,
            ("battania", PetitionKind.Poacher) => 1.4f,
            ("battania", PetitionKind.Bloodshed) => 1.4f,
            ("battania", PetitionKind.BridePrice) => 1.3f,
            ("battania", PetitionKind.CartSeized) => 0.7f,
            ("battania", PetitionKind.Monopoly) => 0.6f,
            _ => 1f,
        };

        /// <summary>The case touches a kind of notable the town doesn't have (no merchant for a seized cart, no guild for a charter, no gang for a bought guard).</summary>
        private static bool NotableMissing(PetitionKind kind, List<Hero> notables)
        {
            switch (kind)
            {
                case PetitionKind.CartSeized: return !notables.Any(h => h.IsMerchant);
                case PetitionKind.Apprentice:
                case PetitionKind.Monopoly: return !notables.Any(h => h.IsArtisan);
                case PetitionKind.Bribes: return !notables.Any(h => h.IsGangLeader);
                default: return false;
            }
        }

        private static TextObject PleaLine(Petition p, Hero? judge)
        {
            var text = Pleas.TryGetValue(p.Kind, out var v) ? v[Math.Min(p.Variant, v.Length - 1)] : "{=lmmi_court_plea_generic}{LORD}, I beg you to hear me.";
            var line = new TextObject(text);
            line.SetTextVariable("LORD", Lord(judge));
            line.SetTextVariable("VILLAGE", p.Village?.Settlement?.Name ?? new TextObject("{=lmmi_court_our_village}our village"));
            line.SetTextVariable("A", p.A?.Name ?? TextObject.GetEmpty());
            line.SetTextVariable("B", p.B?.Name ?? TextObject.GetEmpty());
            return line;
        }

        private static TextObject RulingLine(Petition p, bool granted)
        {
            if (Rulings.TryGetValue(p.Kind, out var r)) return new TextObject(granted ? r.Grant : r.Deny);
            return new TextObject(granted ? "{=lmmi_court_grant_generic}Granted." : "{=lmmi_court_deny_generic}Refused.");
        }

        /// <summary>How a lord rules depends on the case and on the lord: mercy for the desperate, honour for a wrong done, and so on.</summary>
        private static bool LordWouldGrant(Hero lord, PetitionKind kind)
        {
            int mercy = lord.GetTraitLevel(DefaultTraits.Mercy), generosity = lord.GetTraitLevel(DefaultTraits.Generosity);
            int honor = lord.GetTraitLevel(DefaultTraits.Honor), valor = lord.GetTraitLevel(DefaultTraits.Valor), calc = lord.GetTraitLevel(DefaultTraits.Calculating);
            switch (kind)
            {
                case PetitionKind.CartSeized:
                case PetitionKind.Bribes:
                case PetitionKind.BridePrice:
                    if (honor >= 1) return true;
                    if (honor <= -1) return false;
                    break;
                case PetitionKind.Bandits:
                    if (valor >= 1 || honor >= 1) return true;
                    if (calc >= 1 && mercy <= 0) return false;
                    break;
                case PetitionKind.Monopoly:
                    if (calc >= 1 || generosity <= -1) return true;
                    if (generosity >= 1) return false;
                    break;
                case PetitionKind.Apprentice:
                    if (mercy >= 1) return false;
                    if (honor >= 1 || mercy <= -1) return true;
                    break;
                default:
                    if (mercy >= 1 || generosity >= 1) return true;
                    if (mercy <= -1) return false;
                    break;
            }
            return MBRandom.RandomFloat < 0.5f;
        }

        /// <summary>
        /// Who has to be speaking for the plea to make sense: a widow, a mother, a poacher, a master, a smith — and a few
        /// single pleas ("my husband's lame", "my wife fell sick", "her mother and me"). Null: either will do.
        /// </summary>
        private static bool? SpeakerIsFemale(PetitionKind kind, int variant)
        {
            switch (kind)
            {
                case PetitionKind.Widow:
                case PetitionKind.Bloodshed:
                    return true;
                case PetitionKind.Poacher:
                case PetitionKind.Apprentice:
                case PetitionKind.Monopoly:
                    return false;
                case PetitionKind.Pressed:
                    return variant == 2 ? true : (bool?)null;
                case PetitionKind.Debtor:
                    return variant == 0 ? false : (bool?)null;
                case PetitionKind.BridePrice:
                    return variant == 2 ? false : (bool?)null;
                default:
                    return null;
            }
        }

        /// <summary>The petitioner's people: villagers for the village's troubles (and in a castle, where there's no town), townsfolk for the town's.</summary>
        private static CharacterObject? PetitionerFor(PetitionKind kind, int variant, Settlement s)
        {
            var c = s.Culture;
            if (c == null) return null;
            bool rural = !s.IsTown || kind == PetitionKind.SeedGrain || kind == PetitionKind.Pressed || kind == PetitionKind.Poacher
                         || kind == PetitionKind.Bandits || kind == PetitionKind.BridePrice;
            CharacterObject? man = rural ? c.Villager ?? c.Townsman : c.Townsman ?? c.Villager;
            CharacterObject? woman = rural ? c.VillageWoman ?? c.Townswoman : c.Townswoman ?? c.VillageWoman;
            bool female = SpeakerIsFemale(kind, variant) ?? MBRandom.RandomInt(2) == 0;
            return female ? woman ?? man : man ?? woman;
        }

        /// <summary>Who in the town the case touches: the merchants for a seized cart, the guild for a charter or an apprentice, the gangs for a bought guard, a village's headman for its bandits.</summary>
        private static Hero? NotableFor(PetitionKind kind, Settlement s, Village? village)
        {
            Func<Hero, bool>? pick = kind switch
            {
                PetitionKind.CartSeized => h => h.IsMerchant,
                PetitionKind.Apprentice => h => h.IsArtisan,
                PetitionKind.Monopoly => h => h.IsArtisan,
                PetitionKind.Bribes => h => h.IsGangLeader,
                _ => null,
            };
            if (kind == PetitionKind.Bandits)
                return village?.Settlement?.Notables.Where(h => h.IsAlive && (h.IsHeadman || h.IsRuralNotable)).OrderBy(_ => MBRandom.RandomFloat).FirstOrDefault();
            if (pick == null) return null;
            return s.Notables.Where(h => h.IsAlive && pick(h)).OrderBy(_ => MBRandom.RandomFloat).FirstOrDefault();
        }

        private static void Relation(Hero? who, int amount)
        {
            if (who == null || !who.IsAlive || amount == 0) return;
            ChangeRelationAction.ApplyPlayerRelation(who, amount, affectRelatives: false);
        }

        /// <summary>The nearest bandit lair, marked on your map (vanilla's own spotting): where your riders went.</summary>
        private static void MarkNearestLair(Settlement s)
        {
            try
            {
                var at = s.GatePosition;
                var lair = Settlement.All.Where(x => x.IsHideout && x.Hideout != null && x.Hideout.IsInfested && !x.Hideout.IsSpotted && x.Position.Distance(at) < 120f)
                    .OrderBy(x => x.Position.Distance(at)).FirstOrDefault();
                if (lair == null) return;
                lair.Hideout.IsSpotted = true;
                lair.IsVisible = true;
                CampaignEventDispatcher.Instance?.OnHideoutSpotted(MobileParty.MainParty.Party, lair.Party);
                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=lmmi_court_lair_marked}Your riders find where the bandits lair, and mark it on your map.").ToString(), Colors.Yellow));
                LmmiLog.Info($"Court: riders marked {lair.Name} on your map.");
            }
            catch (Exception ex) { LmmiLog.Error("Court: marking a lair threw", ex); }
        }

        /// <summary>The new kinds of petition, ruled in your own hall.</summary>
        private void AddMoreRulings(CampaignGameStarter starter)
        {
            // A merchant's cart seized at the gate.
            AddRuling(starter, "lmmi_court_cart_return", "{=lmmi_court_cart_return}Give him his cart. The toll was never owed — and the gate sergeant answers to me.", PetitionKind.CartSeized,
                "{=lmmi_court_cart_return_resp}Thank you! I'll tell every trader on the road this town deals fairly again.",
                p => { Relation(p.Notable, 3); Move(p.Settlement, security: -1f, prosperity: 20f, standing: 2f, why: "returned a merchant's seized cart"); });
            AddRuling(starter, "lmmi_court_cart_toll", "{=lmmi_court_cart_toll}Pay the toll, and you'll have your cart back.", PetitionKind.CartSeized,
                "{=lmmi_court_cart_toll_resp}...Fine. Fine. Here.",
                p => { Hero.MainHero.ChangeHeroGold(50); Move(p.Settlement, standing: -1f, why: "made a merchant pay the gate toll"); });
            AddRuling(starter, "lmmi_court_cart_forfeit", "{=lmmi_court_cart_forfeit}The goods are forfeit. The cart too.", PetitionKind.CartSeized,
                "{=lmmi_court_cart_forfeit_resp}You're no better than your sergeant!",
                p => { Hero.MainHero.ChangeHeroGold(200); Relation(p.Notable, -5); Move(p.Settlement, loyalty: -1f, prosperity: -25f, standing: -3f, why: "kept a merchant's seized goods"); });

            // A runaway apprentice.
            AddRuling(starter, "lmmi_court_apprentice_back", "{=lmmi_court_apprentice_back}The indenture holds. The boy goes back to his master.", PetitionKind.Apprentice,
                "{=lmmi_court_apprentice_back_resp}Thank you. He'll learn his trade if it kills him.",
                p => { Relation(p.Notable, 3); Move(p.Settlement, standing: -1f, why: "sent a runaway apprentice back to his master"); });
            AddRuling(starter, "lmmi_court_apprentice_terms", "{=lmmi_court_apprentice_terms}He serves out his years — but the beatings stop, or you answer to me.", PetitionKind.Apprentice,
                "{=lmmi_court_apprentice_terms_resp}...As you say. No more beatings.",
                p => { Relation(p.Notable, 1); Move(p.Settlement, loyalty: 1f, standing: 1f, why: "held an apprentice to his indenture, and his master to decency"); });
            AddRuling(starter, "lmmi_court_apprentice_free", "{=lmmi_court_apprentice_free}The boy is free — and you'll pay him for the years he worked.", PetitionKind.Apprentice,
                "{=lmmi_court_apprentice_free_resp}Free?! The guild will hear of this!",
                p => { Relation(p.Notable, -4); Move(p.Settlement, loyalty: 1f, prosperity: -5f, standing: 2f, why: "freed a beaten apprentice"); });

            // A bride-price dispute.
            AddRuling(starter, "lmmi_court_bride_full", "{=lmmi_court_bride_full}The price was promised. The groom's family pays in full, by the next market day.", PetitionKind.BridePrice,
                "{=lmmi_court_bride_full_resp}Thank you! I knew the hall would see it straight.",
                p => Move(p.Settlement, loyalty: 1f, standing: 1f, why: "made a family pay the bride price it promised"));
            AddRuling(starter, "lmmi_court_bride_half", "{=lmmi_court_bride_half}What's paid is paid. Make your peace with your in-laws.", PetitionKind.BridePrice,
                "{=lmmi_court_bride_half_resp}...Peace. With those thieves. As you say.",
                p => Move(p.Settlement, standing: -1f, why: "let a bride price go half-paid"));
            AddRuling(starter, "lmmi_court_bride_pay", "{=lmmi_court_bride_pay}I'll pay the difference myself — and I expect a cup at the first child's naming. [150{GOLD_ICON}]", PetitionKind.BridePrice,
                "{=lmmi_court_bride_pay_resp}You — you'd do that? The whole village will drink your health!",
                p => Move(p.Settlement, loyalty: 2f, standing: 3f, why: "paid a bride price out of their own purse"), 150);

            // A village beset by bandits.
            AddRuling(starter, "lmmi_court_bandits_riders", "{=lmmi_court_bandits_riders}I'll send riders after them, and pay them well for it. [150{GOLD_ICON}]", PetitionKind.Bandits,
                "{=lmmi_court_bandits_riders_resp}Bless you! We'll show your men where they camp.",
                p =>
                {
                    Relation(p.Notable, 2);
                    Move(p.Settlement, loyalty: 1f, security: 5f, standing: 2f, why: "sent riders after a village's bandits");
                    MarkNearestLair(p.Village?.Settlement ?? p.Settlement);
                }, 150);
            AddRuling(starter, "lmmi_court_bandits_militia", "{=lmmi_court_bandits_militia}Arm your own men — here's silver for spears. [100{GOLD_ICON}]", PetitionKind.Bandits,
                "{=lmmi_court_bandits_militia_resp}Spears! We'll make them think twice.",
                p =>
                {
                    var vs = p.Village?.Settlement;
                    if (vs != null)
                    {
                        vs.Militia += 8f;   // VERIFY: Settlement.Militia has a public setter in 1.4.8 (no other use in the repo)
                        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=lmmi_court_militia_up}{VILLAGE}: Militia +8").SetTextVariable("VILLAGE", vs.Name).ToString(), Colors.Green));
                    }
                    Relation(p.Notable, 1);
                    Move(p.Settlement, loyalty: 1f, standing: 1f, why: "armed a village's militia");
                }, 100);
            AddRuling(starter, "lmmi_court_bandits_no", "{=lmmi_court_bandits_no}The garrison guards the walls, not every goat-pen in the valley.", PetitionKind.Bandits,
                "{=lmmi_court_bandits_no_resp}...Then we'll bury our own.",
                p =>
                {
                    if (p.Village != null) p.Village.Hearth = Math.Max(0f, p.Village.Hearth - 10f);
                    Relation(p.Notable, -2);
                    Move(p.Settlement, loyalty: -2f, standing: -2f, why: "left a village to its bandits");
                });

            // A debtor begging for time.
            AddRuling(starter, "lmmi_court_debtor_forgive", "{=lmmi_court_debtor_forgive}The debt is forgiven. Go home.", PetitionKind.Debtor,
                "{=lmmi_court_debtor_forgive_resp}Forgiven? ...I'll pray for you every day I live.",
                p => Move(p.Settlement, loyalty: 2f, security: -1f, standing: 2f, why: "forgave a debtor his arrears"));
            AddRuling(starter, "lmmi_court_debtor_season", "{=lmmi_court_debtor_season}One more season. Not a day after.", PetitionKind.Debtor,
                "{=lmmi_court_debtor_season_resp}Thank you — thank you. You'll have it.",
                p => Move(p.Settlement, loyalty: 1f, standing: 1f, why: "gave a debtor another season"));
            AddRuling(starter, "lmmi_court_debtor_collect", "{=lmmi_court_debtor_collect}Pay now, or the bailiffs take your tools.", PetitionKind.Debtor,
                "{=lmmi_court_debtor_collect_resp}...Then take them. Take everything.",
                p => { Hero.MainHero.ChangeHeroGold(120); Move(p.Settlement, loyalty: -2f, prosperity: -10f, standing: -3f, why: "sent the bailiffs to a debtor"); });

            // A smith asking for a monopoly.
            AddRuling(starter, "lmmi_court_monopoly_grant", "{=lmmi_court_monopoly_grant}You'll have your charter — for a fee to the treasury.", PetitionKind.Monopoly,
                "{=lmmi_court_monopoly_grant_resp}You won't regret it! The fee will be on your table by nightfall.",
                p => { Hero.MainHero.ChangeHeroGold(300); Relation(p.Notable, 5); Move(p.Settlement, prosperity: -30f, standing: -2f, why: "sold a smith the sole right to forge"); });
            AddRuling(starter, "lmmi_court_monopoly_refuse", "{=lmmi_court_monopoly_refuse}No. Any smith may forge in this town. Forge better.", PetitionKind.Monopoly,
                "{=lmmi_court_monopoly_refuse_resp}...We'll see how the town likes cheap steel in a siege.",
                p => { Relation(p.Notable, -3); Move(p.Settlement, prosperity: 10f, standing: 1f, why: "kept the town's forges open to all"); });

            // A mother whose son killed a man in a brawl.
            AddRuling(starter, "lmmi_court_blood_hang", "{=lmmi_court_blood_hang}A man is dead. Your son hangs.", PetitionKind.Bloodshed,
                "{=lmmi_court_blood_hang_resp}No — no, please, he's just a boy—",
                p => Move(p.Settlement, loyalty: -1f, security: 2f, standing: -3f, why: "hanged a boy for a death in a brawl"));
            AddRuling(starter, "lmmi_court_blood_money", "{=lmmi_court_blood_money}Blood money to the dead man's kin, and he goes free.", PetitionKind.Bloodshed,
                "{=lmmi_court_blood_money_resp}We'll pay it. Somehow. Thank you.",
                p => Move(p.Settlement, loyalty: 1f, security: -1f, standing: 1f, why: "let a brawler pay blood money"));
            AddRuling(starter, "lmmi_court_blood_walls", "{=lmmi_court_blood_walls}He'll not hang. He serves on the walls instead — a year, and he earns his life back.", PetitionKind.Bloodshed,
                "{=lmmi_court_blood_walls_resp}On the walls... he'll live. That's all I ask.",
                p =>
                {
                    var garrison = p.Settlement.Town?.GarrisonParty;
                    var troop = p.Settlement.Culture?.BasicTroop;
                    if (garrison != null && troop != null)
                    {
                        garrison.MemberRoster.AddToCounts(troop, 1);
                        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=lmmi_court_garrison_up}{TROOP} joins the garrison of {PLACE}.")
                            .SetTextVariable("TROOP", troop.Name).SetTextVariable("PLACE", p.Settlement.Name).ToString(), Colors.Green));
                    }
                    Move(p.Settlement, security: 1f, standing: 1f, why: "sent a brawler to serve on the walls");
                }, 0, () => _petition?.Settlement.Town?.GarrisonParty != null);
            AddRuling(starter, "lmmi_court_blood_pay", "{=lmmi_court_blood_pay}I'll pay the blood price myself. Let it end here. [300{GOLD_ICON}]", PetitionKind.Bloodshed,
                "{=lmmi_court_blood_pay_resp}You... I don't know what to say. Bless you.",
                p => Move(p.Settlement, loyalty: 2f, standing: 4f, why: "paid a blood price out of their own purse"), 300);

            // A guard accused of taking bribes.
            AddRuling(starter, "lmmi_court_bribes_punish", "{=lmmi_court_bribes_punish}Strip him of his post, and he pays back every coin.", PetitionKind.Bribes,
                "{=lmmi_court_bribes_punish_resp}At last! Someone in this hall who listens.",
                p => { Relation(p.Notable, -3); Move(p.Settlement, loyalty: 1f, security: 3f, standing: 2f, why: "threw a bribe-taking guard out of the watch"); });
            AddRuling(starter, "lmmi_court_bribes_fine", "{=lmmi_court_bribes_fine}He keeps his post, but the coin comes to my treasury. Every bit of it.", PetitionKind.Bribes,
                "{=lmmi_court_bribes_fine_resp}...So the coin goes from his purse to yours. I see.",
                p => { Hero.MainHero.ChangeHeroGold(80); Move(p.Settlement, security: 1f, standing: -1f, why: "fined a bribe-taking guard into the treasury"); });
            AddRuling(starter, "lmmi_court_bribes_dismiss", "{=lmmi_court_bribes_dismiss}Where's your proof? Get out, and stop slandering my watch.", PetitionKind.Bribes,
                "{=lmmi_court_bribes_dismiss_resp}...Of course. Silly of me to think the hall would care.",
                p => { Relation(p.Notable, 2); Move(p.Settlement, security: -2f, standing: -2f, why: "dismissed a complaint against a bought guard"); });
        }

        /// <summary>What you say for the petitioner depends on the case: mercy for the desperate, justice for a wrong, sense for a bargain.</summary>
        private static (SkillObject Skill, TraitObject Trait, TextObject Text)[] InterceptArguments(PetitionKind kind)
        {
            switch (kind)
            {
                case PetitionKind.CartSeized:
                case PetitionKind.Bribes:
                case PetitionKind.BridePrice:
                case PetitionKind.Bandits:
                case PetitionKind.Apprentice:
                    return new[]
                    {
                        (DefaultSkills.Leadership, DefaultTraits.Honor, new TextObject("{=lmmi_court_arg_justice_honor}If you let this stand, every man in the town learns it can be done.")),
                        (DefaultSkills.Trade, DefaultTraits.Calculating, new TextObject("{=lmmi_court_arg_justice_calc}It's cheaper to set this right today than to pay for it next season.")),
                        (DefaultSkills.Charm, DefaultTraits.Generosity, new TextObject("{=lmmi_court_arg_justice_generous}They came to you because there's no one else. Be the lord they hoped for.")),
                    };
                case PetitionKind.Monopoly:
                    return new[]
                    {
                        (DefaultSkills.Trade, DefaultTraits.Calculating, new TextObject("{=lmmi_court_arg_monopoly_calc}A fee every year, and good steel for your walls. That's a bargain, not a favour.")),
                        (DefaultSkills.Leadership, DefaultTraits.Honor, new TextObject("{=lmmi_court_arg_monopoly_honor}A master who's given this town his life's work deserves better than to be undercut by tinkers.")),
                        (DefaultSkills.Charm, DefaultTraits.Generosity, new TextObject("{=lmmi_court_arg_monopoly_generous}Reward the ones who build your town up. The others will learn.")),
                    };
                default:
                    return new[]
                    {
                        (DefaultSkills.Charm, DefaultTraits.Mercy, new TextObject("{=lmmi_court_arg_mercy}They've nothing left to give. Mercy costs you less than their despair will.")),
                        (DefaultSkills.Trade, DefaultTraits.Calculating, new TextObject("{=lmmi_court_arg_calc}A ruined farmer pays no taxes next year. This is the cheaper ruling.")),
                        (DefaultSkills.Leadership, DefaultTraits.Honor, new TextObject("{=lmmi_court_arg_honor}Your people are watching how you judge the least of them. So is your name.")),
                    };
            }
        }
    }
}
