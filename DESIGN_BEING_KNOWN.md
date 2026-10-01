# Being Known — design spec

Status (2026-09-25): everything in this document is implemented except items marked **(todo)** — helper escorts
(notables sending their people to walk you), vouch liability, and lords. None of it has been playtested beyond the
first slices; watch `lmmi.log`.
All numbers are starting points for tuning, not final.

## Where this fits

LMMI has two layers:

- **Knowing the way** (built): per-feature, binary, permanent discovery of places. The only thing that gates menus.
- **Being known** (this doc): how the people of a settlement regard you. Never gates menus. Changes how notables
  deal with you: favors, directions, recruits, quests, voice.

Decisions that shaped this (don't re-litigate without reason):

- **No time-in-scene as a progress source.** The campaign clock is paused in scenes, it's AFK-able, and the author
  rejected it publicly in 2024. Progress comes from discrete deeds only.
- **No single "familiarity" meter.** Vanilla already keeps the per-person number (relation). We interpret it,
  add a town-level ledger, and weigh it against who you are.
- **Prejudice may cost coin, time or a detour — and at Contempt it can close a notable's door — but there is always
  a way through**: deeds for others in town, a vouch, or rising in the world.
- **Power buys service, only relation buys loyalty** (sycophants vs friends).

## The disposition score

Computed on demand per notable. Nothing new is stored except the town ledger and vouches.

```
score = deeds + power + affinity − prejudice
```

| Level | Score | Meaning |
|---|---|---|
| Contempt | < −20 | Won't deal with you. Refuses favors, directions; won't offer quests |
| Wary | −20 … 5 | Deals for coin |
| Friendly | 5 … 25 | Does favors |
| Trusted | ≥ 25 | Does big favors — **if genuine** (see loyalty) |

### Deeds — what you've done

```
deeds = relation × honorWeight(notable.Honor) + 0.5 × townStanding(settlement)
honorWeight: +2 → 1.5, +1 → 1.25, 0 → 1, −1 → 0.85, −2 → 0.7   (honorable people judge you by deeds)
```

- **relation**: vanilla personal relation (−100…100). Already moved by quests, failures, raids, gifts, battles.
- **townStanding** (built, `TownStandingBehavior`): per-settlement ledger, −50…+50, saved. "Word gets around" — the reason a notable
  you never helped can still have heard of you.

| Deed | Hook | Change |
|---|---|---|
| Quest for any notable here completed | `OnIssueUpdatedEvent` (`IssueFinishedWithSuccess`, `IssueSettlement`) | +4 |
| Quest here failed / cancelled | same (`IssueFail`, `IssueCancel`) | −3 |
| Tournament won here | `TournamentFinished` (winner = player, town) | +3 |
| Hideout cleared, credited to nearest town | `OnPlayerBattleEndEvent` (won, `MapEventSettlement.IsHideout`) | +4 |
| Bandits beaten near here | `OnPlayerBattleEndEvent` (won vs bandits, within radius) | +1, max +3 / week |
| Defended the town or one of its villages | `OnPlayerBattleEndEvent` (player on defender side) | +6 |
| Raided one of its villages | `VillageLooted` / `RaidCompletedEvent` (player raider) | −15 |
| Took the town | `OnSiegeAftermathAppliedEvent` | show mercy −3, pillage −10, devastate −25 |
| Crime rating with owner faction | live value, not stored | −crime / 4 |

Decay: 1 point toward 0 every 7 days. Gratitude and grudges fade; knowing the way does not.

### Power — who you are

```
power = tierCurve[clanTier] × typeWeight × valorRespect + position
tierCurve   = [−8, −4, 0, 8, 18, 30, 45]   (retuned 2026-09-25 from −15/−8: nobodies are ignored, not despised)      // steep at the top: "can't ignore you anymore"
typeWeight  : merchant 1.0, artisan 0.8, headman/rural 0.7, gang leader 0.4
valorRespect: Valor ≠ 0 → 1.25 (brave respect renown, cowards fear it), else 1.0
```

| Position | Bonus |
|---|---|
| You own this settlement | +25 |
| Ruler of the owning realm | +25 |
| Vassal of the owning realm | +10 |
| Mercenary for the owning realm | +5 |
| At war with the owning realm | −15 |
| Party of 100+ / 200+ men (the army at the gates) | +3 / +6 |

### Prejudice — who you are not

```
prejudice = base(type) × culturePair(player, notable) × tolerance(Mercy)
            × (1 − min(1, fade[tier] × pragmatism(Calculating)))
            × occupier × vouched × steppedUp
base        : headman/rural 12, merchant/artisan 10, gang leader 4, other 8   (retuned from 25/15/5/10)
culturePair : same 0, kin 0.5, neutral 1.0, rival 1.5   — authored table, see below
tolerance   : Mercy +2 → 0.25, +1 → 0.6, 0 → 1, −1 → 1.3, −2 → 1.6
fade[tier]  : [0, 0, 0.2, 0.4, 0.6, 0.8, 0.9]
pragmatism  : Calculating +2 → 1.3, +1 → 1.15, 0 → 1, −1 → 0.8, −2 → 0.6   (impulsive grudges stick)
occupier    : 1.25 when the notable is native, the town's owner is of your culture, and yours ≠ theirs
vouched     : 0.25 once a notable they respect has vouched for you
steppedUp   : 0.6 for 60 days after you stood up for a local in that settlement (street events)
MCM         : × ForeignerPrejudicePercent
```

**Culture table (built — draft values, author: you).** Lives in `_Module/ModuleData/lmmi_cultures.xml` so it's editable without code.
Unlisted pairs are neutral. Directional is allowed (A can resent B more than B resents A). Draft:

| Pair | Relation | Why (lore sketch) |
|---|---|---|
| Sturgia ↔ Nord | kin | shared northern roots |
| Vlandia ↔ Battania | rival | Vlandians took the Battanian lowlands |
| Sturgia ↔ Khuzait | rival | steppe raids |
| Empire ↔ Battania | rival | old imperial conquest |
| Empire ↔ Aserai | rival | southern border wars |

### Affinity — the player's traits

People warm to kindred spirits, once they know who you are.

```
affinity = clamp(±15, fame(tier) × Σ_t notable[t] × player[t] × 2.5)    // t ∈ Mercy, Valor, Honor, Generosity, Calculating
fame(tier): 0–1 → 0 (nobody knows a nobody), 2 → 0.5, 3+ → 1
```

A cruel player and a merciful notable: −10. Two honorable people: +2.5 to +10. A ruthless player gets on with
ruthless gang leaders. The player's playstyle (vanilla trait XP) finally has social consequences.

### Loyalty — friend or sycophant

```
genuine = (deeds + affinity) ≥ power      // they like you, not your title
```

- **Sycophants**: flattering voice, small favors free, big favors refused ("I couldn't possibly, my lord").
- **Friends (genuine)**: big favors (crime, vouching) need genuine + Friendly or better.

### Generosity — the price of Wary

Wary gold costs and bribes × greed: Generosity +2 → 0.5, +1 → 0.75, 0 → 1, −1 → 1.5, −2 → 2.

## Vanilla levers (built)

- **Recruit slots**: postfix on the *active* volunteer model's `MaximumIndexHeroCanRecruitFromHero` when buyer is the
  player and seller a notable. Contempt −2, Wary −1, Friendly 0, Trusted +1. Vanilla already: relation < 0 → none,
  same realm +1, at war −1/−2. Patch the runtime model type so recruitment overhauls are covered.
- **Quest persuasion** (replaced the old greyed-out gate): at Contempt, vanilla's "I heard you may need some help"
  is hidden and ours takes its place. The notable says why they don't trust you ("Help? From a Sturgian?"); you
  answer with Honor, Calculating, Valor or Generosity. Chance: 35% + 20% per level of your trait + 10% if they share
  the virtue + Charm/300 (max +30%), clamped 5–95%, shown as Likely/Uncertain/Unlikely. Win: vanilla's `issue_offer`
  flow continues and they'll offer work for 7 days. Lose: 3 days before you can ask again.
- **Grudging compliance**: Wary/Contempt notables still sell recruits and the arrangement, at a price
  (disposition cost factor), and say so. Farewells follow disposition too — no "Good to meet you" from someone who
  just called you a horse fondler.

Not touched: relation gain rates (invisible, hits lords), prices (economy-mod conflicts), quest rewards.

## Vouching (built, with liability)

The way past Contempt that isn't grinding.

- Asked as a big favor of a notable: "Would you put in a word for me with someone?" → pick a notable in the same town.
- Requires the voucher to be **genuine** Friendly or better, and on decent terms with the target
  (vanilla hero-to-hero `GetRelation` ≥ 0). If they're rivals, the voucher refuses with a line that tells you so:
  "Him? My word would do you more harm than good." — the town's social web becomes readable.
- Effect on target: `vouched` flag (prejudice × 0.25) + relation +3 … +8 (scaled by voucher↔target relation).
- Cost: −3 relation with the voucher, 30-day cooldown.
- Liability (built): fail, abandon or betray the target's quest and the voucher loses 3 relation too.
- Saved as `targetId:voucherId` pairs.

## Directions and escorts (built)

**Who can show you the way (built):** notables know their own settlement; any other hero (companions, lords) only if
it's their home or birthplace, they govern it, or it's their clan's fief (lords). Everyone else says they're as lost as
you. Companions are never judged by notable disposition.

Notables don't walk you anywhere themselves; they send their people. Vanilla spawns them
(`NotableHelperCharacterCampaignBehavior`): 2 bodyguards per gang leader, a clerk ("notary") per merchant and artisan.
Pick the nearest escort-capable helper; fall back to "ask around". Risk: helpers spawn as fixed-location agents —
verify the escort can move them.

**As built:** helpers and notables are both pinned to fixed spots (they froze on the spot when asked to walk), so the
notable calls over a **runner** — a townsperson within reach — who does the walking ("You there! Take this one to the
tavern. There's a coin in it for you."; gang leaders are curter). No one nearby: "Ask around." A watchdog releases an
escort that hasn't moved for a few seconds and tells you to find your own way.

## Voice (built — `VoiceLines` line bank, `NotableVoice`)

Line matrix: situation × disposition (+ sycophant) × persona (vanilla `GetPersona()`: curt, earnest, ironic,
softspoken). Situations: greeting, favor refused, favor granted, quest refused, vouch given, vouch refused.
Start persona-agnostic (~30 lines), add persona variants over time. Culture slurs for foreign Contempt already exist.

## Hospitality and treachery (built)

Rule: every good or bad outcome comes from **personality + disposition**, is **telegraphed**, and has **counterplay**.
Otherwise it's random punishment.

**Hospitality** (Friendly and above):

| Gift | Who | Effect | API |
|---|---|---|---|
| Provisions | headman / rural notable, on arrival in their village | grain or food into your party | `ItemRoster.AddToCounts(DefaultItems.Grain, n)` |
| Healing | Trusted notable in town | some wounded troops healed, your HP restored | `TroopRoster.AddToCounts(.., woundedCount: −n)`, `Hero.HitPoints` |
| A night on the house | tavern, Friendly gang leader or merchant | party morale | `MobileParty.RecentEventsMorale` |
| Tribute | sycophants at tier 4+ | gold or goods on arrival — **with strings**: a petition follows, refuse it and relation drops | gold / roster |

**Treachery**, tied to traits: dishonorable (Honor ≤ −1) deceive, cruel (Mercy ≤ −1) turn violent, calculating scheme.

| Betrayal | Who | Effect | Cost |
|---|---|---|---|
| False trade tips | dishonorable Wary merchant | inflated prices; you find out on arrival | cheap |
| Takes your coin, rats you out | dishonorable sycophant gang leader, crime favor | gold gone, crime rating up | cheap |
| Bait hideout | dishonorable gang leader | a larger band waiting at the tip | medium |
| Purse lifted | cruel gang leader at Contempt, tavern district at night | gold loss (fight version later) | cheap |
| Road ambush | cruel notable with negative relation | thugs hunt your party after you leave; small parties only | `BanditPartyComponent.CreateBanditParty` + `SetMoveEngageParty`; medium-high |

**Counterplay:**
- Read people: encyclopedia traits; voice hints ("his smile doesn't reach his eyes").
- Skills: Roguery spots a swindle or bait before you commit (extra dialog line; the notable backs down or turns hostile),
  Charm sees through flattery, Scouting notices watchers before an ambush.
- Friends warn you: a genuine Trusted notable warns about a schemer in the same town.
- Size deters: no ambushes against large parties; walking with companions stops pickpockets.

**Limits:** at most one treachery per town per N days; MCM toggle "Treacherous notables"; never more than a fixed share
of your gold. Treachery must not stack with prejudice into a miserable foreign start.

Lords later: same model for audiences, false intel and slander — not in this pass.

### Arrival scenes (built) — people come to you

Hospitality and treachery are delivered **in the scene**, not as menu messages: when you walk into a village or town,
the right person walks up to you and starts talking.

| Event | Who | When | What happens |
|---|---|---|---|
| Welcome feast | any genuine Friendly+ notable | you return after 30+ days | "How we've missed you, my lord! Come, we've prepared a small feast." Accept: morale, food, some wounded healed, relation +2. Decline: nothing |
| Tribute | sycophant, tier 4+ | once a season | a gift; a petition follows on a later visit |
| Petition | a sycophant who gave tribute | later visit | asks something of you; refusing costs relation |
| A friend's warning | genuine Friendly+ (kept common on purpose: it's the main counterplay) | a schemer in town has you marked | "A word: don't trust {NAME}." — counterplay for treachery |
| Warned off | Contempt + cruel (Mercy ≤ −1) | you arrive | "Finish your business and leave." — telegraphs treachery |
| Lesser nobles | lords without a party in the lord's hall | you enter the hall | same model: flattery from sycophants, snubs from the contemptuous |

**Mechanics** — vanilla's own pattern (Prodigal Son quest):
`ScriptBehavior.AddTargetWithDelegate(agent, selectPlayer, null, onReached)` walks the agent to the player through its
`DailyBehaviorGroup` (the group our early escort attempts fought — this uses it as intended), then
`MissionConversationLogic.StartConversation(agent, false)` opens the dialog. A priority "start" line keyed to the
pending event supplies the greeting; afterwards the conversation can fall through to normal business.

**Limits:** one approach per visit; per-notable cooldown; never during an escort, quest scene or another scripted
conversation; skip agents without a navigator (scene mods); MCM toggle. "Missed you" needs a last-visit date per
settlement (store it with the town ledger on `SettlementEntered`). Check how the agent returns to its routine after the
conversation (the scripted behavior may need clearing).

## Tuning

- **Debug readout (built)**: MCM toggle that prints the breakdown when a notable conversation starts, e.g.
  `Qarais: Wary 4 = deeds 6 + power −8 + affinity 0 − prejudice 12 (rival ×1.5, Mercy −1 ×1.3, fade 0)`.
- **Target scenarios** — tune weights until these hold:

Retuned 2026-09-25 after the first foreign-start playtest (a tier-0 Sturgian was at Contempt with every village
notable, even with +3 relation). Simulated with the new numbers:

| Scenario | Now |
|---|---|
| Tier 0, neutral foreign culture: headman / merchant / gang leader | Wary −17.6 / −18 / −7.2 |
| Tier 0, cruel headman (Mercy −1) or rival culture | Contempt −21 to −24 |
| Tier 0 headman, relation 3, Honor +2 | Wary −13.1 |
| Local, tier 0 merchant, relation 20 | Friendly 12 |
| Tier 4 vassal: neutral merchant / rival headman | Friendly 24 / 15.4 |

Previous tuning (2026-09-24), kept for reference:

| Scenario | Expect | Simulated |
|---|---|---|
| Tier 0, rival culture, relation 0 | merchant & headman Contempt, gang leader Wary | −37.5 / −48 / −13.5 ✓ |
| Same + gang leader relation 15 + town standing 12 | gang leader Friendly & genuine → can vouch | 7.5 ✓ (needs the ledger; 1.5 Wary without it) |
| …then gang leader vouches with the merchant | merchant Wary: the door opens, not friendship | −14.6 ✓ |
| Tier 4 vassal of the realm, neutral culture | merchant & headman Friendly sycophants | 22 / 12.6 ✓ |
| Same, rival-culture headman in an occupied village | Wary (occupier ×1.25) | ≈ −5.5 ✓ |
| Tier 6 ruler, rival culture, cruel player | Trusted sycophants; merciful notables cooler | 67.8 vs 59.4 ✓ |
| Local, tier 0, relation 20 / tier 2 relation 30 | Friendly / Trusted, genuine | 5 / 30 ✓ |

## Roadmap (agreed 2026-09-30)

The scene content grew sideways (street events, ambushes, feasts, hired swords, jail) while the core idea — people know
who you are, and it shows — stayed under-served: the ledger all that content feeds was invisible. From here:

1. **Stabilize.** Work the test checklist; a test trigger per street event kind; tune against the target scenarios. No
   new *systems* until the existing ones hold.
2. **Make standing visible and worth having** — one ledger, surfaced:
   - shown: a line on entering a settlement ("Your name is well spoken of here" / "Muttering follows you"), and a
     message when you cross a band;
   - bands with concrete vanilla levers: **Despised** (−25) and **Disliked** (−10) — worse prices, heavier fines and
     sentences, hecklers; **Known** (+10), **Respected** (+25) — better prices, lighter fines and sentences;
     **Honored** (+40) — the town's lord hears of you.
3. **Lords**: audiences where standing and the realm's view both count; slander between lords (enemies talk, relations
   drift); bringing a grievance to a lord's court — which closes the hired-swords loop (accuse the sender before their
   liege, or hire your own).
4. **Word travels**: a share of standing spreads to nearby towns of the same culture — a regional reputation, where
   prejudice and fame meet.
5. **Then more scene content**, every piece under the same rules: it feeds the existing numbers (relation, standing,
   crime, disposition), it's visible in the scene, it has a "left alone" outcome, and it's never a dead end.

**Places should differ.** Towns: streets, trade, the watch. Villages: land, harvest, herds, feuds between neighbours.
Castles: the garrison — drill, sparring, the castellan, prisoners, couriers. Halls: court — petitions, judgments,
gossip. The camp: your own people.

### What players ask for (research, 2026-09-30)

Recurring requests on the Steam, TaleWorlds and Nexus communities:

- **Settlements feel lifeless**: NPCs with few animations who never talk to each other; silence; no routines. "There's
  no real reason to visit towns, castles or villages at all." Wanted: rudimentary Skyrim-style ambient talk and
  routines; dynamic events and intrigue that give a reason to walk the streets; the gang-alley idea extended to
  villages; nighttime raids; talkable townsfolk and guards with consequences; quests tied to the world's state
  (loyalty, recent raids, politics).
  ([Steam: "lifeless places"](https://steamcommunity.com/app/261550/discussions/0/2144217924394221366),
  [Steam: towns & villages rant](https://steamcommunity.com/app/261550/discussions/0/506216918921847298/))
- **Castles**: "useless", nothing to do; wanted: an NPC to deal with (garrison captain / recruiter), castles that look
  like what's been built, preparing defenses, a personal estate. Mods fill the gap with **castle notables** (elite /
  noble recruits from castles: *Elite Recruits In Castles*, *Settlements Change Culture*), **Hold Court** (subjects
  petition their ruler), *Castle Smugglers*, a granary to donate food to.
  ([Steam: point of owning a castle](https://steamcommunity.com/app/261550/discussions/0/2248931052753901951/),
  [Nexus: Elite Recruits In Castles](https://www.nexusmods.com/mountandblade2bannerlord/mods/5734),
  [Nexus: Hold Court](https://www.nexusmods.com/mountandblade2bannerlord/mods/5522))
- **Warband's camp** ("Take a walk around": your party in an empty map, to wander, talk, plan) is one of the most
  missed features. Mods (*Army March*, *Walk With Me*) show it's doable on battle terrain.
  ([Steam: "Camp?"](https://steamcommunity.com/app/261550/discussions/0/2264689848039962755/),
  [Warband: Take a walk around](https://steamcommunity.com/app/48700/discussions/0/133258092250552058/))

Vanilla still ships a **"Camp" mission and its view set** (`SandBoxMissions.OpenCampMission`,
`[ViewMethod("Camp")]`), unused — the engine side of a camp exists.

## Build order

1. ~~Disposition v2~~ — done.
2. ~~Vanilla levers + vouching~~ — done, with the greeting voice.
3. ~~Town standing + last-visit dates~~ — done.
4. ~~Arrival scenes, hospitality, treachery~~ — done: feast, warning, warned off, gratitude, tribute → petition,
   townsfolk (beggar, heckler, admirer); false tips, bait hideouts, crime double-cross; ambushes and purse lifting.
5. ~~More favors, loans, letters, persona voice, contemptuous introductions, foreigner's price~~ — done.
6. **Next:** playtest and tune (see Tuning). Then helper escorts (notables send their people),
   lords (audiences, snubs, slander), more errands in the spirit of letters.

## More systems (built 2026-09-25)

**Favors, full list** (`Favors.cs`; all face to face, cooldown per notable, Wary pays × Generosity × foreigner's 1.25):

| Favor | Who | Min level | Notes |
|---|---|---|---|
| Hideouts nearby | gang leader | Wary (300) | treacherous: bait — 2 extra bands sent to the hideout |
| Make the watch forget me | gang leader | Friendly + genuine | treacherous: offered at Wary (400) as bait; crime goes *up* |
| Who's in the dungeon | gang leader | Wary (100) | lords held in the keep |
| Trade tips | merchant | Wary (150) | treacherous: inflated prices |
| Loan | merchant | Trusted + genuine (10%, 30d) | treacherous: loan shark at Wary (40%, 20d), collectors on default |
| Provisions | headman, landowner | Wary (120) | grain for your column |
| Tend the wounded | headman, landowner, artisan | Friendly | half of each wounded stack |
| Vouch | anyone | Friendly + genuine | rivals refuse |
| Letter of introduction | anyone | Friendly + genuine | target: a Wary-or-worse notable in the 8 nearest towns, same trade first; 60 days; delivered in person = vouch + relation +5 |

**Treachery** = Honor ≤ −1 and not a genuine Friendly+ friend. Spotted (option greyed with the reason) by a friend's
warning about that notable or Roguery ≥ 60.

**Ambushes** (`AmbushBehavior`), rolled when you leave a settlement, one per departure, never against parties > 60:
overdue loan shark 60%, a warning you defied 50%, a cruel (Mercy ≤ −1) notable with relation ≤ −10 at Contempt 30%.
Looter party named "{NAME}'s Men", sized 8–35 from your party, told to engage you, telegraphed by a message;
disbanded after 3 days. The vanilla robbery threat is rewritten to name the sender. Purse lifting: tavern district
at night, cruel gang leader at Contempt, 50%, 5% of your gold (20–400), Roguery 60+ catches the hand.

**Loans** (`DebtsBehavior`), terms by disposition — nobody lends at Contempt:

| Terms | Who | Interest | Days | Cap | Default |
|---|---|---|---|---|---|
| Friend | merchant, Trusted + genuine | 10% | 30 | 1000 + 500/tier, ≤ 50% of their gold | −15 relation, −5 standing |
| Business | merchant, Friendly | 20% | 30 | 700 + 300/tier, ≤ 40% | −10, −5 |
| Stranger | merchant, Wary | 35% | 20 | 400 + 200/tier, ≤ 25% | −10, −5, crime +15 (the magistrate) |
| Shark | gang leader (Wary+), treacherous merchant | 40% | 20 | 800 + 400/tier, ≤ 60% | −10, collectors (AmbushBehavior) |

Late fee 10%. Repay any time the lender talks to you (+2 relation on time, +3 for a stranger's loan).

**Townsfolk directions** also follow town standing: at +10 the busy and foreign refusals halve in favor of help;
at −10 a third of the goodwill turns into refusals.

**Arrival scenes, full list** (one per scene, notables first): Warning > Gratitude > Petition > Feast > Tribute >
Warned off; then townsfolk: Heckler (foreign, standing ≤ 0, tier ≤ 3, 25% × resentment), Admirer (standing ≥ 15, 30%),
Beggar (towns, tier ≥ 2, 20%; alms raise standing, a shove lowers it).

**The lord's hall** (`ArrivalScenesBehavior.TryLordArrival`): lords lingering in the hall (not your clan, not the
realm's ruler) are judged by the same disposition model (default weights: power 0.7, prejudice base 10). At most one
crosses the hall to you, 50% per eligible lord, 20-day cooldown each: a snub at Contempt (answering back costs 2
relation), flattery from a sycophant (+1 if you accept the invitation), or a quiet word from a genuine Trusted friend.
Lords' own greetings stay vanilla.

## Sturgian playtest round (built 2026-09-25)

**Guards** (`GuardDialogsBehavior`): vanilla still decides whether they talk and whether you get in; only the words
change. Tone from the town's culture against yours (culture table × MCM %), your clan tier and town standing:
kin ("Welcome, kinsman"), kin of standing (tier 4+), grudging respect (foreign tier 4+ or neutral culture), word of
deeds (standing ≥ 10: "Even for a Sturgian. Go on, then."), contempt (culture-specific scorn). The lord's hall guard
turns a foreign nobody away with a joke at their people's expense (or about the ruler's bed); dungeon and keep gate
guards greet kin and sneer at foreigners. Your own fiefs: vanilla.

**Street events** (`StreetEventsBehavior`, rebuilt as staged scenes after the first test: a dialog-only brawl
"felt like nothing happened"). Rolled once per visit to a town/village centre, 20–50 s in, 30% chance, 1-day cooldown
per settlement. The actors are real townsfolk, directed through a custom scripted behavior (`LmmiStageBehavior`) in
the daily behavior group — the slot vanilla's `ScriptBehavior` uses: walk/run to a spot and hold, keep up with someone,
face someone, loop an animation (`act_scared_idle_1`, `act_conversation_threat_point`, `act_argue`). The requester
runs up to you; say yes and they lead you there, waiting if you fall more than 14 m behind.

| Event | Staging | At the spot | Outcomes |
|---|---|---|---|
| Rescue | a woman 18–60 m away; 2 men (3 at tier 2+, 4 at tier 4+) — the gang's own if two are about — close in around her | the lead man steps up: "Keep walking, {SLUR}." Fight · [Valor] intimidate (20% + 20%/Valor + 8%/tier − 15% vs gang) · pay 100 · back down | win/intimidate: stepped up +6/+4, her thanks, gang boss −3; pay: +2; lose: +2; back down or refuse: −2 |
| Brawl | two teenage boys, or two men of 18–20, within 30 m of each other (never mixed; vanilla's youths can fist-fight); one goes after the other and they really fight | the fight is stopped as you arrive; one lad squares up: [Valor] (30% + 20%/Valor + 5%/tier) · [Charm] (15% + Charm/250) · fists vs both · carry on | success/win: +3 stepped up; failed Valor → they both fight you; failed Charm → they go back at it (+1) |
| Thief | a street kid (vanilla's teenagers) or a beggar near a local, else a young man; he loiters until you've finished talking | "Stop, thief!": outlined red, he runs for a far point (another agent's spot 25–90 m off, away from you) at 70% speed; within 2.5 m in 45 s = caught: return (+3) · guard (+3; the nearest guard runs over, collars him, walks him off, then he fades) · [Roguery] split (+60 gold, no thanks) · [Roguery 60+] at the start: stare him down (+3) · 50 for the rent (+2) | escaped: she comes back; 50 for the rent (+2) or an apology |

**Aftermaths** — refuse, back down, shrug or walk off and the scene plays out on its own, visibly, outliving the
event: the men lead the girl away (the requester barks "Heaven help her"); the brawl goes on until one youth is
knocked out (10–15 s: he's made mortal on 2 hp) or the nearest guard runs over and breaks it up ("Oi! Break it
up!"), and if you shrugged, the requester says so ("Thanks for nothing"); the thief strolls off. Barks are vanilla's
portrait notifications (`MBInformationManager.AddQuickInformation`). Choices take effect on the first mission tick
after the conversation has closed — vanilla's `ConversationEndOneShot` runs *before* `MissionConversationLogic`
tidies up the conversation agents, and fading the thief out there crashed the game (access violation).

**Wading in** (`StreetHitWatcher`, a MissionLogic added to campaign missions in `SubModule.OnMissionBehaviorInitialize`):
land a blow on a brawler, one of the men around the girl, or the fleeing thief and it's your fight — the brawlers
lose their `Immortal` state (which only exists so they don't knock each other out before you arrive) and vanilla's
`StartCustomFight` takes over; a thump on the thief counts as catching him. Works on a brawl you walked away from
too (+2 stepped up if you win).

**Carriers**: a civilian carrying something (vanilla's `AgentNavigator.IsCarryingSomething`) bends down
(`act_pickup_down_begin`/`_end`), drops a held item and hides a shouldered one, and gets them back when released.

The youths' brawl uses vanilla's own fight machinery without the player: one lad on `PlayerEnemyTeam`, the other on
`PlayerAllyTeam` (enemies by side), alarmed with `FightBehavior` scripted (as `MissionFightHandler.ForceAgentForFight`
does), `MortalityState.Immortal` so it lasts until you arrive; stopped by restoring the old team (wrapped the way
`MissionFightHandler.ResetTeamsForFightAndDuel` does), `Patrolling` and `Mortal`. Your own fights are vanilla
`StartCustomFight`, fists only: weapons stripped (yours stashed and returned), their health × (0.8 + 0.04 × your
level, max 2.2) for the rescue, × (0.8 + 0.02 × level) for the lads. Checks roll for real in Test mode (odds shown).

**Stepped up** (`TownStandingBehavior.MarkSteppedUp`): 60 days in that settlement. Prejudice × 0.6, and each notable
remarks on it once, prejudice meeting a fact: "Huh. Didn't expect this from a frozen drunk. Word is you stood up for
one of ours." Kin hear "That's how it should be." It works like a vouch from the street.

**Test mode** (MCM → Testing): parties ignore you (no looters), arrival/favor/ambush cooldowns and chance rolls skip,
persuasion and street checks roll for real (odds and roll on screen — auto-success hid the mechanic), `[Test]` lines in conversations (relation ±10, town standing ±10,
"How do you see me?" — the disposition breakdown). Buttons: +10,000 gold, heal all, renown to the next tier,
relations +10 with everyone in the settlement, town standing ±10, discover this settlement, street event now, clear
all LMMI cooldowns.

## The welcome feast, live (built 2026-09-25)

Vanilla's cinematics (`SceneNotificationData`: wedding, births, coronation) are pre-authored scenes with fixed
character slots — a feast one would need the Modding Kit's scene editor. So the feast is staged live instead
(`FeastBehavior`), hidden behind vanilla's `ScreenFadeController` (fade out → build → fade in → ~75 s → fade out →
clear → fade in):

- **Spot**: open, level ground near the host — 12 waist-height rays clear to 3.5 m (else 2.6 m), no ground step
  over 35 cm. None: "held indoors", effects only.
- **Furniture — vanilla's authored work, not guesswork** (the first version spawned loose props sized from bounding
  boxes, which aren't computed yet right after spawning: tables came out rotated, guests clipped, and a sittable bench
  prefab's uninitialised `Chair` crashed vanilla's interaction focus). Now: `bd_table_c` (≈3 m × 1.1 m, top 0.797 m)
  with `bd_chair_c_tavern_set`s as `empire_interior_tavern_a` places them (two sets on one side of each table, each
  with one chair pulled in by a per-instance override — the prefab default overhangs the table end), mirrored onto the
  other side: 8 chairs per table, 1 table or 2 end to end (3.4 m apart) when you bring 4+ people and there's a 4.8 m
  clearing; the chairs are real `Chair`
  machines, instantiated with callbacks off, their `ChairUsePoint`s set to Eat/Drink (`act_sit_eating_1..4`,
  `act_sit_and_drink_idle`) and then `CallScriptCallbacks(true)` — exactly `Mission.CreateMissionObjectFromPrefab`.
  The dishes are a laid table copied from `empire_house_c_tavern_a` (stew pots, steak and grape plates, bread, bottle,
  mugs, candle), decoration only; any decoration prefab that turns out to carry scripts is refused.
- **Guests**: seated with vanilla's own chair logic — teleported to the sit point, `UseGameObject`, fast-forwarded
  with `SimulateTick` as `MissionAgentHandler.SimulateAgent` does at spawn — so they sit, eat, drink and talk to the
  neighbour with the chair's authored animations. Our scripted behavior only "occupies" (it replaces WalkingBehavior,
  the one thing that would make them leave after the chair's 50–320 s wait). 8 chairs: the host at the far end, then
  your companions (up to 3, reused if already following you) and best troops (up to 2) alternating with up to 3
  locals. At the end they stand up (the chair's leave action); chairs are removed the vanilla way
  (`RemoveAllChildren` + `Remove`), after everyone has stood.
- **Evening**: the host's toast, then portrait barks every 6–10 s from both sides; the host's dialog offers a toast
  (+1 relation) or leave. Effects on fade-out (or if you leave the scene).

## Vanilla persuasion (built 2026-09-27)

`Contacts/NativePersuasion` wires the game's persuasion into our conversations exactly as the Prodigal Son quest does:
`ConversationManager.StartPersuasion(goal, 1, 0, 2, 2, 0, difficulty)`, a `PersuasionTask` of up to four
`PersuasionOptionArgs`, player lines with the persuasion-option delegate (vanilla rolls them and shows the chance via
`PersuasionHelper.ShowSuccess`), a reaction line (`GetDefaultPersuasionOptionReaction`, critical failure blocks the
rest), and won / lost lines keyed on `GetPersuasionProgressSatisfied`. `AddLeave` adds a way out. Argument strength:
Normal, + the listener's level in that trait (±2), − prejudice (resentment > 0: −1, ≥ 1.25: −2), − 1 for a cruel
listener (Mercy ≤ −1), ± 1 for town standing ≥ 10 / ≤ −10, plus a per-case shift (gang men −1, youths +1, a witness +1,
your tier for intimidation). Vanilla's own `GetArgumentStrengthBasedOnTargetTraits` isn't used: it starts from +1, which
makes a single-trait argument "very easy".

| Where | Goal | Arguments | Difficulty |
|---|---|---|---|
| Quest gate (Contempt notable) | 2 | Honor/Charm, Calculating/Charm, Valor/Leadership, Generosity/Charm | Medium; MediumHard ≤ −35; Hard ≤ −55 |
| The men around the girl | 1 | Valor/Leadership, Calculating/Roguery, Mercy/Charm | Medium |
| The brawling youths | 1 | Valor/Leadership, Mercy/Charm, Calculating/Roguery | Medium |
| The watch | 1 | Honor/Charm, Valor/Leadership, Calculating/Roguery | Medium |
| The heckler | 1 | Valor/Leadership, Generosity/Charm, Mercy/Charm | Medium |
| The toughs (a setup) | 1 | Valor/Leadership, Calculating/Roguery, Generosity/Charm | Medium |
| A plea for your life (hired swords) | 2 | Calculating/Trade, Honor/Leadership, Mercy/Charm, Valor/Roguery | Hard; Very Hard ≤ −80 |

## Round 10 (built 2026-09-30): lords and the region

- **Word travels** (`TownStandingBehavior.Add`): |amount| ≥ 1 → +amount/4 to ≤ 4 towns/castles of the settlement's culture
  within 70 map units (the village's own town keeps its ½). Not recursive.
- **`LordsBehavior`**: weekly slander (relation ≤ −30, Honor ≤ 0, 25%) / praise (≥ 40, 25%) to two "friends" (the realm's
  clan leaders and own clan with relation ≥ 20 to the speaker), −1…−2 / +1, floors at −40 / caps at 40. Grievances are noted
  when hired swords name their sender (street greeting, road greeting, judgment); liege = sender's clan leader or ruler (a
  notable: their town's owner or ruler). `NativePersuasion("grievance")`, goal 2, Medium → Hard by the liege's relation with
  the accused; won → fine (½ to you), −10 sender↔player, −10 liege↔sender, +3 liege, `HiredSwordsBehavior.Blocked` 60 days.
  Revenge: gang leader at Wary+, top-3 enemies ≤ −20; resolved abstractly after 1–3 days (chance for lords 0.65 − men/250,
  notables 0.8; wounds via `HitPoints`, escort `WoundTroop`, purse −⅙); discovery 0.3/0.5 − broker relation/200 → −15.
  "What do they say of me": average relation of the realm's clan leaders, best/worst standing in the realm's fiefs, last
  slanderer (30 days).
- **`TavernLifeBehavior`**: a player line in vanilla's `town_or_village_player` (tavern, non-hero townsfolk/villager); 2d6 vs
  2d6, loaded dice (Roguery ≥ 30) win but risk 45% − Roguery/250; five wins a visit.
- **Hired swords**: `Warning()` on contract (companion/own Roguery 60+, a Trusted notable within 80); `WithinReach` 200 map
  units for both the street and the road.

## Round 9 (built 2026-09-30): places differ

- **Standing bands** (`TownStandingBehavior.BandOf`: ≤ −25 Despised, ≤ −10 Disliked, ≥ 10 Known, ≥ 25 Respected, ≥ 40
  Honored). Shown on `SettlementEntered` and on band changes in `Add`. Levers: `StandingPricePatch` (postfix on
  `DefaultTradeItemPriceFactorModel.GetTradePenalty`, main party only: ×0.8…×1.25), `FineFactor` (street fines), `SentenceDays`
  (added to the magistrate's days), weekly ±1 relation with the settlement's owner at Honored / Despised.
- **Village kinds** (`Dispute`, `Harvest`, `Raiders`; `KindsFor` keeps Debt and Shakedown in towns and these in villages).
  The harvest uses a timed follow-up (`Scene.Then`/`ThenAt`) during a fade; the raiders are spawned looters (`SceneSpawner`)
  in an armed `StartCustomFight` with the player Immortal and beaten down at 12% (`ArmedFight`). `Headman(sc, n)` is the
  village's relation lever; `Village.Hearth` moves ±1–3.
- **`CastleLifeBehavior`**: stages a master-at-arms (the garrison's best troop) and a castellan (the next best, civilian
  dress) near the entrance; bouts reuse `StreetEventsBehavior.Fistfight`; veterans come out of `GarrisonParty.MemberRoster`
  (tier 3+, ≤ 5, ≤ 1/8) at 2× `PartyWageModel.GetTroopRecruitmentCost`; vignettes (drill, prisoner, dice, courier) are
  spawned and faded. `News()` = nearest hostile lord party / army within 150.
- **`HallCourtBehavior`**: a petition per hall visit (daily per settlement): before a lord of the owner clan (ruled by
  Mercy/Generosity after 25 s unless you intercede — `NativePersuasion("court")` on the lord's conversation) or before you
  in your own hall (a conversation with the petitioner; rulings move `Town.Loyalty`/`Security`, gold, notables, standing).
  Gossip barks from world state every 40–70 s.
- **Camp** (`CampBehavior`, `CampMissionLogic`, `CampMissionViews`): Ctrl+T on the map (`SubModule.OnApplicationTick`)
  opens mission `"LmmiCamp"` on `SceneModel.GetBattleSceneForMapPatch(...)` with a minimal behavior set (campaign
  component, basic teams, leave, conversation, human AI, fight handler, boundaries). Our `[ViewCreatorModule]` supplies the
  view set (vanilla's village views minus name markers and barter) — the game scans mod assemblies that reference
  TaleWorlds.MountAndBlade for it. The camp is laid out around the walk-boundary centroid (level, walkable ground): culture
  tents, 1–3 fires, companions and ≤ 24 troops posed with `SetScriptedPositionAndDirection` + looping floor-sit /
  standing-talk animations (re-posed every 3 s), two armed sentries. Troops use `SimpleAgentOrigin` (no roster side
  effects). Dialogs key on `CampMissionLogic.Current.Men`.

## Round 8 (built 2026-09-27)

- **More pleas** (`Kind.Debt`, `Kind.LostChild`, `Kind.Shakedown`, cast in `SetupMore`). Debt and the lost child can be
  setups (the debtor is bait / the child is bait with a crew loitering near him); the crooked watchman never is. The lost
  child uses two steps of its own: `Searching` (the mother guides you only to within ~16 m; the child is outlined within
  14 m; found within 2.8 m — 6 m for a setup, where it springs) and `Returning` (he follows you; reunion within 3 m).
  Left alone: `Menace` (collectors / guard say their piece and walk off) and `Reunite` (she finds him).
- **Running from toughs** (`Step.Fleeing`): walking off or "[Back away and run]" after a reveal → they `Follow` you at a
  run; within 1.7 m it's a fight (`BeatOnly`); 30 m clear, a guard within 10 m, or 25 s → they give up, standing −1.
- **Street sentences** (`JailSentencePatch`, a prefix on `PlayerCaptivityCampaignBehavior.CheckCaptivityChange`):
  while a sentence is being served in the settlement it was handed down in, vanilla's check (release when not at war,
  ransom, escape) doesn't run; when it's up we switch to `lmmi_street_served` (release, incident crime refunded) or
  `lmmi_street_headsman` (`KillCharacterAction.ApplyByExecution`). Days: quietly 1 + crime/20; resisted 2 + crime/10
  + 2/downed + 5/killed; ≥ N kills (MCM, 3): headsman below clan tier 3 (unless execution is off), else ×2 and a blood
  price. Saved in `lmmi_street_sentence_v1`.
- **Hired swords in the street**: the daily roll now makes a `Contract` (fee paid up front). `OnMissionTick` stages it in
  the next town/village centre (20–60 s in): 3–6 soldiers spawned through `MissionAgentHandler` with battle gear, no horse,
  ranged weapons removed; the leader walks up and talks. Fight = `StartCustomFight` with weapons, the player Immortal
  and "beaten down" at 12% health (`EndFight` → lost). Lost/given in: Beat → fade, rob, fade in; Deliver/Hold →
  `EndMission`, then the `lmmi_hired_taken` menu leaves the settlement, spawns the band at the gate and
  `TakePrisonerAction` into it — the existing delivery and judgment take over. After 3 days without a street, the map
  band (the old version) comes instead.
- **Feast props**: each `ChairUsePoint` gets one fixed loop (25% `act_sit_eating_1` + ham, 50% `act_sit_and_drink_idle`
  + mug — that one is in `as_human_warrior`, so every set has it — 25% `act_sit_1`), with Eat/Drink off so vanilla's
  random pick can't choose "nothing". Hall feasts: chairs within 0.8 m of the host's floor only; seating teleports 5 cm
  above the sit point (a hall's floor is a mesh); a failed seat puts you back.

## Setups and hired swords (built 2026-09-27)

**Setups** (`StreetEventsBehavior`). The lure is no longer an event of its own: every event (rescue, brawl, thief, and
the genuine "my brother fell") rolls `Scene.Ambush` once, before staging (`AmbushChance`: MCM base + resentment ×0.1 up
to 0.25 + 0.03 per soft touch, − 0.05 per tier above 3, ×0.3 within 10 days of the last setup there; clamped
0.03–0.6; Test mode ≥ 0.5). The staging is identical; a setup adds a `Crew` of toughs loitering nearby (brawl
onlookers, the thief's friends at his escape point, the fallen man's companions). `Reveal` fires where the genuine
confrontation would (arrival; for the thief, catching him or coming within 9 m of his friends): the bait — the girl, the
thief, whoever asked — slips away with a bark, the toughs (`Actors` + `Crew`, or the crew alone for the thief) surround
you, and the toughs dialog runs (`TalkToughs`; genuine lines check `!Revealed`). Refusal penalties are skipped for a
setup (silently — standing changes never show). The Roguery hunch (0.6 setup / 0.15 genuine) and the "Why come to me?"
call are the only handles the player gets.

**Hired swords** (`HiredSwordsBehavior`). Daily candidates from `Hero.AllAliveHeroes` (lords within 100, notables within
60 map units; Honor ≥ 1 never; a grudge also needs Mercy < 1). The band is a looter-clan party (at war with everyone) with
a custom name, its roster replaced by the sender's culture's regulars (both trees, BFS over `UpgradeTargets`, nearest
the wanted tier, 30% a tier below), AI locked and engaging. The capture is vanilla's (a lost battle or surrender);
`HeroPrisonerTaken` tells us it's ours. Beat: a menu, then `EndCaptivityAction.ApplyByReleasedAfterBattle`.
Deliver/Hold: the band escorts the sender's party (or goes to the gate), `IgnoreByOtherPartiesTill` so nobody
intercepts; arrival (4 units) → a menu → `ApplyByReleasedByChoice` → band destroyed → a map conversation with the sender
(`CampaignMapConversation.OpenConversation`). Verdicts: Hold (`TakePrisonerAction` into their party or nearest clan
fortress; only at war, or vanilla releases you at once), Humiliate (20% of your purse to them), Doom (plea; execution
via `KillCharacterAction.ApplyByExecution(Hero.MainHero, sender, true, isForced: false)`, which routes through vanilla's
heir selection / game over). Everything after a conversation, and every menu switch, runs from
`SubModule.OnApplicationTick` (`CampaignEvents.TickEvent` doesn't fire while paused).

## Technical notes (learned the hard way)

- Hero locations come from `DefaultHeroAgentLocationModel`: all notables → `center`; wanderers → `tavern`;
  lords, ruler, spouse, party leaders → `lordshall`; prisoners → `prison`.
- Menu **Talk** fires `PlayerStartTalkFromMenu` and loads the scene next to the hero; **Quick Talk** is a map
  conversation with no location. "In person" = `CampaignMission.Current.Location != null`.
- Title mods (e.g. Titles) prepend to displayed names; match hero names by suffix.
- Under BLSE the game runs as `Bannerlord.BLSE.Launcher.exe`; check the DLL lock, not the process name, before deploying.
- Menu options live for the whole session and are shared by every settlement — wrap once, decide at evaluation time.
- `ConversationManager.EndConversation` runs `ConversationEndOneShot` before `ConversationEnd` (where
  `MissionConversationLogic` touches the conversation agents' visuals): never remove or fade an agent you were
  talking to from a one-shot — defer it a tick.
- Vanilla's teenagers (`culture.TownsmanTeenager` etc., child skeleton) can fist-fight; children can't.
- `MissionFightHandler.StartCustomFight(dropWeapons: true)` drops the *player's* weapons on the ground too; strip and
  restore them yourself (as vanilla's `StartFistFight` does) and pass `false`. Every fighter needs a
  `CampaignAgentComponent` with a navigator. Start fights from `ConversationEndOneShot`, as vanilla does.
