# Less Menus, More Immersion — player guide

What the mod does, from the player's side: what people think of you, what you can ask of them, what can go
wrong, and what the numbers are. For the design reasoning see `DESIGN_BEING_KNOWN.md`; for version history,
`CHANGELOG.md`.

---

## 1. Knowing the way

- Town menus (tavern, arena, smithy, backstreets, keep, market) unlock only once you've **been there** or talked
  to someone who works there. A guide can still show you around for a fee.
- Anyone in the street can show you the way. Townsfolk help, ask for a coin, or refuse, depending on your
  standing in town and whether you're a foreigner.
- **Notables** don't walk you there themselves; they call someone nearby to take you ("You there! Take this one
  to the tavern."). If nobody's around, they tell you to ask around.
- Companions and lords only know their home, birthplace, governorship or fief.
- Portraits in the settlement menu: **Talk/Visit** needs you to have met the person *and* know the way to where
  they are.
- **Crowds** (MCM → General): towns and villages hold twice vanilla's ordinary townsfolk by default (50–400%).
  Scenes still cap it by their spawn points and the game's own civilian-count option (Options → Performance).

## 2. What notables think of you (disposition)

Every notable sizes you up on four things. Turn on MCM → Debug → *disposition breakdown* to see the sums.

| | What counts |
|---|---|
| **Deeds** | Their relation with you (counts more for honorable notables), plus your **town standing** |
| **Power** | Your clan tier (a steep curve: nobody at tier 0–1, somebody at 3+), your place in the realm |
| **Kindred spirits** | Your traits against theirs (Mercy, Valor, Honor, Generosity, Calculating), once you're known (tier 2+) |
| **Prejudice** | Your culture against theirs (see the culture table), softened by their Mercy, fading as you rise — faster for calculating notables |

**Levels:** Contempt (< −20) · Wary (< 5) · Friendly (< 25) · Trusted.
**Friend or flatterer:** if what they like about you is your power rather than your deeds, they're a
*sycophant*: warm words, small favors, but they won't stick their neck out.

**Prejudice gets smaller when:**

- someone they respect **vouches** for you (prejudice × 0.25);
- you **stepped up** for a local in that settlement in the last 60 days (× 0.6; see street events);
- you rise in tier.

**Town standing** (per settlement, −50…+50, fades toward 0 about 1 point a week):

| Raises it | Lowers it |
|---|---|
| Quests for its notables (+4) | Failing or betraying them (−3 / −6) |
| Clearing hideouts, beating bandits nearby | Raiding its villages, sacking it (−3 to −25) |
| Tournaments, alms, street events | Defaulting on a loan (−5), refusing help in the street |

## 3. How it shows

- **Greetings, prompts, introductions, farewells** follow disposition and the notable's personality: kin are
  warm, foreigners at Contempt get insulted by name ("horse fondler", "frozen drunk", "wood hugger"...), and a
  notable who insulted you won't wish you well on the way out.
- **Recruits:** Contempt −2 slots, Wary −1, Trusted +1. Wary and contemptuous notables still sell, at a higher
  price, and let you know what they think of it.
- **Work (quests):** a notable at Contempt won't simply hand you their problem. You have to talk them into it —
  with **vanilla's persuasion** (the progress bar, each argument's skill and trait with its chance, critical success and
  failure). Four arguments — Honor, Calculating, Valor (Leadership), Generosity — and you need two successes (a
  critical counts double). An argument lands better with a notable who shares that virtue, worse the more they resent
  your people or the crueller they are; your standing in their town helps; the deeper their contempt, the harder the
  persuasion. Win and they offer work for 7 days; lose and they won't hear it for 3.
- **Guards:** street guards welcome kin, tolerate a foreigner of standing, sneer at a foreign nobody. The lord's
  hall guard turns a foreign nobody away with a joke at their people's expense. Who gets in is still vanilla.

### Your name in town (town standing)

Every town and village keeps a ledger of what you've done there (−50 to +50, fading about a point a week; your crime
rating with the realm counts against it). It now has **names**, you see them, and they matter:

| Band | Standing | Walking in | Shops | The watch & magistrate | And |
|---|---|---|---|---|---|
| **Honored** | 40+ | "people stop to greet you by name" | markup ×0.8 | fines ×0.5, sentences −2 days | the town's lord hears of you: +1 relation a week (up to 30) |
| **Respected** | 25+ | "your name is well spoken of" | ×0.88 | fines ×0.75, −1 day | |
| **Known** | 10+ | "a few faces turn your way" | ×0.95 | fines ×0.9 | |
| a stranger | −10 to 10 | — | — | — | |
| **Disliked** | −10 or worse | "muttering follows you through the gates" | ×1.12 | fines ×1.25, +1 day | hecklers |
| **Despised** | −25 or worse | "doors close as you pass" | ×1.25 | fines ×1.5, +2 days | the town's lord hears of it: −1 relation a week |

("Markup" is vanilla's trade penalty — the gap between what a shop sells for and what it pays.)

**Word travels:** whatever you gain or lose in one place spreads, at a quarter, to up to four towns and castles of the same
people within a day's ride (a village's own town still gets half). Big deeds (3+) tell you where the word went. Crossing
into a new band is announced ("Word gets around Ocs Hall: you're respected there now"). It also still feeds every notable's opinion of
you at half weight, townsfolk's directions, guards' tone, admirers and persuasions — see above and below.

## 4. Favors

Ask any notable: *"I'd like to ask a favor."* Wary notables want coin (× your Generosity, × 1.25 if you're a
foreigner); Friendly and Trusted do it free. Each favor has its own cooldown per notable.

| Favor | Who | Needs | What you get |
|---|---|---|---|
| **Trade tips** — "What's worth buying here, and where does it sell?" | merchant | Wary (150) | Up to 3 goods **actually in stock here**, with this town's buying price and the best selling price among the 6 nearest towns, only if the margin is worth it (at least 10%). They go into your trade rumors for 15 days. **It's real market data** — unless the merchant is treacherous (below) |
| **Hideouts nearby** | gang leader | Wary (300) | 2 hideouts marked on the map |
| **Make the watch forget me** | gang leader | Friendly + genuine | Crime rating −30 (costs 5 relation) |
| **Who's in the dungeon** | gang leader | Wary (100) | The lords held in the keep |
| **Provisions** | headman, landowner | Wary (120) | 4–30 grain, by party size |
| **Tend the wounded** | headman, landowner, artisan | Friendly | Half of each wounded stack healed |
| **Loan** | merchant, gang leader | see §5 | Coin, on terms |
| **Put in a good word** | anyone | Friendly + genuine | A vouch with another notable in town (below) |
| **Letter of introduction** | anyone | Friendly + genuine | A letter to someone in a nearby town who dislikes you; deliver it in person within 60 days = a vouch there + 5 relation |

**Vouching:** the voucher must be on decent terms with the target. Rivals refuse ("My word would do you more harm
than good"), which tells you who hates whom. A vouch cuts the target's prejudice to a quarter and adds 3–8
relation; it costs you 3 relation with the voucher and 30 days before they'll do it again. Let the target down
(fail their quest) and the voucher loses face too.

### Can I get scammed?

Yes. A notable is **treacherous** if they're dishonorable (Honor −1 or lower) and not a genuine friend of yours.
They'll still do the favor, and cheat you:

| Favor | The swindle |
|---|---|
| Trade tips | Real goods, a real town, a **made-up selling price** (1.5–1.9× what you pay). You find out when you get there |
| Hideouts | Bait: extra bandit bands are sent to the hideout they name |
| Make the watch forget me | Offered to a Wary stranger (400) — and your crime rating goes **up**: they tipped off the watch |
| Loan | Loan-shark terms and collectors on the road if you're late |

**Seeing it coming:** **Roguery 60+**, or a **friend's warning** (a friend may walk up in town and tell you
"don't trust X") greys the favor out with the reason. Without either, you can't tell. At relation 0, a merchant's
tips are honest unless that merchant is dishonorable: check their traits in the encyclopedia.

## 5. Loans

Merchants and gang leaders lend; the terms follow how they see you. Nobody lends to someone they despise.

| Terms | Who | Interest | Due | Amount (at most) | If you're late |
|---|---|---|---|---|---|
| **A friend's** | merchant, Trusted + genuine | 10% | 30 days | 1,000 + 500/tier | −15 relation, −5 town standing |
| **Business** | merchant, Friendly | 20% | 30 days | 700 + 300/tier | −10 relation, −5 standing |
| **A stranger's** | merchant, Wary | 35% | 20 days | 400 + 200/tier | −10 relation, −5 standing, **+15 crime** (they go to the magistrate) |
| **A shark's** | any gang leader; a dishonorable merchant | 40% | 20 days | 800 + 400/tier | −10 relation, and **collectors come after you on the road** |

Also capped by how much coin the lender has. A late loan adds a 10% fee. Repay any time you talk to the lender;
paying a stranger's loan on time earns +3 relation (+2 for the others).

## 6. People come to you

On entering a town or village, at most one person may walk up to you:

| Who | When | What |
|---|---|---|
| A friend | you've been away 30+ days | a welcome feast, held right there (below) |
| A friend | someone in town has you marked | a warning: "Don't trust X" |
| Someone you helped | you did their quest | thanks and a gift |
| A sycophant | tier 4+ | tribute — and later a petition against a rival |
| A cruel notable at Contempt | you arrive | "Finish your business and leave." Ignore it at your peril |
| Townsfolk | — | beggars, admirers who heard what you did, and **hecklers** who hate your kind (below) |
| Lords in the hall | — | a snub, flattery, or a quiet word from a real friend |

### The welcome feast

Accept and the screen goes dark. When it comes back a tavern table stands in the square, laid the way vanilla's
taverns lay one, with real chairs: the host at the far end, your companions (up to 4) and best men (up to 4) seated
among the host's people — a few tucking into a ham leg, most nursing a mug, some just talking. Eight seats per table; bring four or more of your own and
there are two tables end to end (16 seats), room permitting. Empty seats are filled with locals, and **the seat at the
foot of the table is yours** — you start the evening sitting in it (stand up to walk around). Passers-by are kept
clear of the table, and nothing else happens in the street while you feast.

Talk to anyone at the table: a word and a cup ("Cheers!"), no errands; companions and notables have something to
say about the evening and can still be asked about other things. Toast the host (+1 relation). Tell the host you're
going, or stay about a minute, and the evening fades out — the sun has moved on when it comes back. **Leave too
soon** (under ~40 s) and the host takes it badly (−1 relation); **walk off** more than 20 m from the table and the
feast ends, and if it was early, that's −2 and noticed. Effects: morale +10, grain, a quarter of your wounded tended,
your own health restored, relation +2. The table only goes on open, walkable ground (not in crop fields); if there's
none nearby, the feast is held indoors (effects only).

### Someone to see you (while you wait)

Waiting in a town or village (the vanilla "wait here" menu)? If someone would come looking for you — a friend with a
feast, a warning, thanks, tribute, a petition — a prompt tells you who. **Meet them** and you walk out into the square,
where they come straight to you. (At most once every 12 hours per settlement; nobody comes to your door just to
threaten you.)

### Dinner in the lord's hall

A lord who's a genuine friend (the one who crosses the hall because they're glad to see you) may ask you to stay and
dine. The feast is held at the hall's own tables — nothing is laid out or cleared away: the host sits nearest, **you sit
beside them**, the lords and ladies in the hall join, and your companions come in. Same evening as a feast in the
square (toast, talk, leave when you like).

### Hecklers

A local who resents your people may walk up and say so. Your answer counts:

| Answer | How | Outcome |
|---|---|---|
| **Answer him** | vanilla persuasion, one success needed: "Watch your tongue" (Valor/Leadership), "let me buy you a drink" (Generosity/Charm), "judge me by what I do here" (Mercy/Charm); harder the more the town resents your people | won over (standing +2) — or laughed at (−1) |
| **Say that again. To my face.** | a fistfight | win: the town's loudmouths keep quiet for 30 days (standing −1, you did beat up a local); lose: −2 |
| Walk past | — | nothing |

(Townsfolk refusing to give directions — "I'll call the guards and have you thrown out" — is only talk.)

## 7. Street events

In a town or village centre, sometimes (about 1 visit in 3, once a day per settlement at most) something is
happening a little way off, and a local runs up to you for help. Say yes and **they lead you there**: they're
outlined in gold so you can follow them through the crowd, and they wait if you fall behind. If you don't deal with
what you find, it plays out without you. **But any of them may be a setup** (see below). People carrying things put them down
before they get involved. **Hit anyone involved and it's your fight**: wade into a brawl (even one you walked away
from), punch one of the men around the girl, or knock the fleeing thief down.

| Event | What you see | Your options | If you leave it |
|---|---|---|---|
| **A girl cornered** | 2–4 men surrounding a frightened woman (the gang's men if any are about) | **Fight** (fists: your weapons are put away and given back; the men get tougher as you level up) · **talk them down** (vanilla persuasion: Valor/Leadership, Calculating/Roguery or Mercy/Charm; fail and it's a fight; gang men are harder) · **pay them off** (100) · back down | The men lead her away |
| **Two youths brawling** | two teenage boys, or two young men of 18–20 (never a man against a boy), really fighting in the street | **talk them down** (vanilla persuasion: Valor, Mercy or Calculating; fail and they go back at it) · knock their heads together yourself (fistfight with both) · let them carry on | One gets knocked out, or the watch runs over and breaks it up; whoever asked you lets you know what they think |
| **"My brother fell!"** | a man sitting in the street, his leg gone under him | **[Medicine]** look at the leg (chance 20% + Medicine/125, up to 95%): set it and the town talks (+3, Medicine xp); fail and you still help him home (+1) · help him up (+2) · "fetch a physician" | whoever fetched you gets him up and they limp off together |
| **"They'll break his hands!"** | a moneylender's collectors (the gang's own if any are about) crowding a man against a wall | **pay his debt** (100–250) · **talk them down** (vanilla persuasion: Calculating/Trade, Mercy/Charm, Valor/Leadership) · **fight** · walk away (−2). The gang boss notices: fight or talk them down −1 to −3 relation, pay +1 | they take what they came for and walk off; he's left with nothing |
| **"Have you seen my little one?"** | a frantic mother (always a woman); her child has wandered off, 45–95 m away | say yes and she leads you to where she *thinks* he went, then it's up to you: you'll hear him crying within 30 m, and he's outlined within 14 m — frightened, cowering. Talk to him (a foreign face may scare him: *"Mama says not to talk to…"*), and he follows you back to her: +3, her thanks, and the two of them go home together. Leave him crying and it's −1 | she finds him herself, gives him an earful, and they go home together |
| **The crooked watchman** | one of the watch "fining" someone for nothing, with no other guard in sight | **pay the "fine"** (40) · **talk him down** (Honor/Leadership, Calculating/Roguery, Mercy/Charm; fail and he carries on, +1 for trying) · **"Hands off him."** — a fistfight with the watch: win and he slinks off (crime +5; he won't report it, but he'll know your face), lose and it's a 100 fine or crime · back down | he pockets the coin |
| **A cutpurse** | a street kid or a beggar who's just lifted a purse, loitering nearby | **Chase him**: he runs only once you've finished talking, outlined in red. Catch him and return the purse, call the guard (a guard walks him off), or [Roguery] split it with him · **[Roguery 60+]** stare him down and he drops it · cover her rent (50) · shrug | He strolls off with it |

### Village trouble

Villages get their own kind of trouble (and the watch, the moneylenders and the crooked guard stay in town):

| Event | What you see | Your options | If you leave it |
|---|---|---|---|
| **The boundary stone** | two neighbours at each other's throats over a stone moved in the night | **the old law** (stone goes back: +2, headman +1) · **[Charm] split the strip** (20% + Charm/150: +3 — or they won't hear of it and come to blows) · **a tenth of the crop** to the other (+2, headman +2) · **[Roguery] sell your judgment** (+40 gold, −2) · walk away (−1) | they settle it with their fists |
| **The harvest** | a storm's coming and half the crop is still standing | **lend a hand**: they lead you to the field; the screen goes dark, the sun moves on, and you come back with sacks of the village's crop (2, or 4 with 20+ men), Athletics xp, +3 and headman +2 · **your men help** (9+ fit men: +2, a sack) · "not today" (no shame — it's work) | — |
| **Looters at the flock** | 3–5 looters at the edge of the village, driving off the sheep | **fight** — with weapons (they have clubs and knives; you're beaten down at about 12% health, never killed): win and it's +5, headman +2, a dropped purse, and the village grows · **send your men** (9+: +3) · refuse (−1) | half the flock is gone (the village shrinks) |

The dispute and the harvest can be setups like any plea; the looters are bandits already.

### Setups

**Any plea may be bait.** Whether it is, is decided before anything is staged, and nothing in the plea, the guide or
the scene gives it away — the same words, the same girl surrounded by the same kind of men, the same two lads really
swinging at each other. You find out when you get there:

| Plea | The twist |
|---|---|
| A girl cornered | she's one of them: *"Sorry, love. Nothing personal."* — she walks off and the men turn on you |
| Two youths brawling | the fight is for show: they stop, grin, and a couple of the onlookers weren't just looking |
| A cutpurse | he runs to where his friends are waiting; catch him, or get close to them, and they close in |
| "My brother fell!" | he's fine — he gets up, and the men standing around him close in |
| "They'll break his hands!" | he doesn't owe them a coin — *"You do."* |
| A lost child | the little one is bait, and the men loitering near him aren't there by chance |

(The crooked watchman is never a setup: the watch doesn't do those.)

Whoever brought you slips away, and the toughs (2–4, more as your clan grows; the gang's own if any are about)
surround you. **Pay** (15% of your purse, 50–400) · **talk your way out** (vanilla persuasion: Valor, Calculating,
Generosity) · **come and take it** (fistfight) · **run** — and they come after you: caught, and it's a beating with no
more talk (and the robbery if you lose); get 30 m clear, or to within 10 m of a guard, or last 25 s, and they give up —
but half the street saw you run (standing −1; leaving the scene with them at your heels counts too). A foreigner in a
town that resents your people may get no talk at all — just the beating. Lose the fight and they take half again as much; win and the street hears you're no easy prey
(and the gang leader, if they were his, won't like it). If the watch turns up mid-fight, "it was a setup" is your story.

- **Odds:** 15% base (MCM, 0–50%), + up to 25% for a foreigner where your people are resented, + 3% for each time you've
  stepped up in that town since the last setup (word gets round who stops to help, up to +15%), − 5% per clan tier
  above 3; a town that just tried it on you is quiet for 10 days (×0.3). Test mode: at least 50%.
- **A bad feeling:** with Roguery 60+, as the one asking reaches you, you may get a hunch — 60% of the time when it's a
  setup, but 15% of the time when it isn't.
- **Call it:** every plea has *"(Stay where you are) Why come to me? There are people closer."* Right, and they melt back
  into the crowd (Roguery xp). Wrong, and someone who needed help goes to find someone who cares, and it plays out
  without you (standing −1).
- Refusing a plea that was a setup costs nothing — nobody was really in trouble — but nothing tells you that either.

**The watch.** Street events are staged out of the guards' sight (no guard within 25 m). But **once your fistfight has
started**, a patrolling guard passing within 12 m — or any guard, if it goes on for 90 seconds — comes over, breaks it
up and wants an explanation. **Explain yourself** with vanilla persuasion (your account — Charm/Honor; "I'd do
it again" — Leadership/Valor; "let's not make paperwork" — Roguery/Calculating): harder the more the town resents your
people, easier with good standing and with the person you helped standing right there. Or pay 50 "for his trouble", or
take the fine (100). Talk your way out and you still get the credit (and the thanks). **Fail, and he sentences you:**

| Choice | What happens |
|---|---|
| Pay | 100 denars — **200 if he had to break up your fight** |
| Go quietly | the watch walks you to the cells in that town (a village's lord's town or castle) |
| "You'll have to take me." | resisting arrest (crime +15): up to four guards come for you, weapons out. The only way out is **escape** — you can't just walk off the scene with a guard within 12 m; get clear, then leave. Every guard you knock down is +10 crime, every one you kill +25. Get away and you're out of the town with the watch at your heels; go down (or get caught at the gate) and you wake in the cells |

**The cells.** Vanilla would let you go at once if you're not at war with the town's realm; a street sentence keeps
you in until it's served (no release, ransom or escape meanwhile). The magistrate's sentence:

- **Went quietly:** a night, plus a day per 20 crime rating you have with that realm.
- **Resisted:** 2 days, plus a day per 10 crime rating, **+2 per guard you knocked down, +5 per guard you killed**
  (up to 90 days).
- **Killed 3 or more of the watch** (MCM, 1–10): **the headsman** at dawn — your heir carries on. A great house (clan tier
  3+) buys its neck instead: double the sentence and a blood price of 500 per man to the town's lord. With
  **"You can be executed"** off, it's the double sentence.
- Served: you're shoved out at the gate, and the crime from that incident is wiped.

After any street fight, the bystanders it frightened calm down and go back to their day. **Leaving mid-fight** (Tab)
is allowed: you ran, and the street will talk (standing −1). Resisting arrest, Tab always works — but with a guard
within 12 m, a hand lands on your collar at the gate (the cells, crime +10); otherwise you got away.

**People you helped** — the one you rescued, whoever asked you, a heckler you won over — drop their prejudice for the
rest of your visit: no more refusals, glad to show you the way. Locals carrying something (a basket, a sack) set it down
before they get involved — arms and all — and pick it back up after.

### Street life

Every minute or so, something happens a little way off whether you take part or not: two locals **quarrelling** over
short weight or a stall, a **preacher** with a few listeners, two **drunks** propping each other up, the **watch moving a
beggar along**, a **dance** in a northern town. Half a minute each; a line or two if you're close enough to hear. (How hard opponents block
and how they close in is vanilla's fistfight AI — it follows their skill; the mod only gives them more health as you
level.)

**Stepping up** — winning, talking them down, returning the purse — raises your town standing (+2 to +6), and
for 60 days prejudice counts for less in that settlement. Each notable there brings it up once: *"Huh. Didn't
expect this from a frozen drunk. Word is you stood up for one of ours."* Refusing to help or backing down costs
a little standing.

## 8. Castles, halls and camp

### Castle life

A castle is a garrison, not a market. In the courtyard:

- **The master-at-arms** (the best of the garrison) hails you as you come near:
  - **a bout** with one of his men — bare-handed, first to fall, an opponent as good as your level (Athletics and
    One-Handed xp; +1 standing if you win);
  - **a wager** on it (100: the odds follow the man's tier, 1.7–2 to 1);
  - **train your companions** (100 each; +150 xp in their best combat skill; every 3 days);
  - in **your own castle, drill the garrison** (40 + Leadership/2 xp per man, weekly).
- **The castellan**:
  - **news from the roads** — the nearest hostile lord or army and where it was seen;
  - in someone else's castle, **veterans the lord can spare**: up to five tier-3+ men from the garrison (never more than
    an eighth of it), at twice their recruiting cost, half of it to the lord — if you're of the same realm, the lord's
    friend (10+) or known in the castle; never if you're at war (a resented foreigner is turned away); weekly;
  - in **your own castle, a report**: garrison, prisoners, food, loyalty, security.
- **The garrison's day** around you, every minute or so: a sergeant bawling at recruits, a prisoner walked across the yard,
  men dicing by the wall, a courier running in with news (you overhear it).

(Town street events and street life don't happen in castle yards.)

### Court in the lord's hall

In a town's or castle's hall, now and then (daily at most):

- **Before the lord** (their house's lord present): a petitioner comes in — seed grain taken with the tax, a son pressed
  into the garrison, a poacher dragged in by the watch, a widow's field claimed by her brother-in-law — and pleads. The
  lord rules in about 25 seconds as their nature bids (the merciful or generous grant it, the cruel refuse, the rest
  either way). Before then you can **talk to the lord**: **speak for the petitioner** (vanilla persuasion: Mercy/Charm,
  Calculating/Trade, Honor/Leadership — win: granted, +3 standing, +1 with a lord who isn't cruel; lose: −2 with the
  lord), or **back the lord** (+1 relation, −1 standing).
- **In your own hall** they come to you, and you rule: seed grain (pay 200 / return half / refuse), the pressed son
  (send him home / keep him), the poacher (hang / fine / pardon), the widow (hers / custom), soldiers quartered on a
  household (pay 150 and discipline / tell him to feed them), a feud between two notables (for one / for the other /
  [Charm] make them share it). Each moves the town's **loyalty and security**, your purse, notables' relations and
  standing; turning them away costs a point of loyalty.
- **Gossip**: lords in the hall talk every minute or so — a war dragging on, a lord rotting in someone's dungeon, the
  biggest army in the field — and, if the town has a name for you, about you.

### Dice in taverns

Ask any tavern patron: **"Fancy a game of dice?"** Two dice each, high roll takes 10, 50 or 100 (ties roll again).
**[Roguery 30+] use your own dice**: you win — unless you're caught (45% − Roguery/250, at least 5%): then you pay double
and the town hears of it (−2). Win five times in one evening and the room won't play with you. In a town that resents your
people, and where you've done nothing for it, some won't dice with your kind at all.

### Lords talk

- **Slander and praise** (weekly): an enemy lord at −30 or worse who isn't honorable may speak ill of you to two friends at
  court (their relation with you −1 or −2, not below −40); a friend at 40+ may speak up for you (+1). Word reaches you either
  way.
- **"What do they say of me at court?"** — any lord of a realm who doesn't dislike you will tell you: how the realm's lords
  regard you on average, where in the realm your name is honored or spat at, and who's been talking.
- **A grievance**: once hired swords have named who sent them, you can take it to that person's liege (their clan's leader or
  their realm's ruler; for a notable, the lord of their town) — **"I've a grievance against …"**. Vanilla persuasion, two
  successes (Honor/Charm, Calculating/Leadership, Generosity/Charm), harder the better the liege likes the accused. Win: a fine
  of 1500 + 500 per clan tier (what they can pay), half of it to you; the accused loses face with the liege (−10) and sends no
  more swords for 60 days. Lose: the liege resents the accusation (−3). Once per accused; grievances lapse after 90 days.
- **Your own hired swords**: a gang leader who'll deal with you (Wary or better) will find men to rough up one of your
  three worst enemies (relation −20 or worse) — 900 for a notable, 1500 + 20 per man in a lord's party (×1.5 if merely Wary).
  A day to three later, word comes: done (they lose a sixth of their purse and are left badly wounded; a lord's escort takes
  wounds too) or not (a lord's guard can see them off). There's a chance they find out who paid (−15, and it spreads to their
  family).

### Camp (Ctrl+T)

Warband's "take a walk around", back. On the map (not in a settlement, battle, an army someone else leads, or at sea),
**Ctrl+T makes camp** where you stand, on that spot's own battle terrain: tents in your people's style, one to three
fires, your companions and up to 24 of your men around them (sitting, standing, talking among themselves — about the
food if it's short, the march if morale's low, the last fight if it's high), two sentries at the edge. Time doesn't pass
(it never does in a scene). Walk among them and talk to any of your men:

- **How are the men?** — morale, food, the wounded.
- **A cask of wine** (2 per man, at least 20): morale +5 and a cheer; daily.
- **[Leadership] a war story** (30% + Leadership/200): morale +3 and Leadership xp — or someone starts snoring; daily.
- **A bout** with him, fists only: Athletics xp for you, xp for him, morale +1.
- **Drill** (everyone, 20 + Leadership/5 xp per man; morale −2); daily.

Companions are there too, with their usual conversations. **Tab** breaks camp.

## 9. When grudges turn violent

| Who | When | What |
|---|---|---|
| A loan shark | your loan is overdue | 60% when you leave town: "X's Men" come for you on the road |
| Someone who warned you off | you stayed anyway | 50% |
| A cruel notable at Contempt, relation −10 or worse | — | 30% |
| A cruel gang leader who despises you | tavern district, at night | 50%: your purse is lifted (5% of your gold); Roguery 60+ catches the hand |

Never against parties over 60; one per departure; the party is telegraphed by a message and gives up after 3 days.

### Hired swords

Someone who truly hates you — and has neither **Honor** nor **Mercy** to hold them back — may pay sellswords to deal
with you. Once a day, each such lord (within 100 map units of you) or notable (within 60) may hire them:

| Who | Chance a day | What they're paid for |
|---|---|---|
| Relation −25 to −49 | 3–5% (×1.5 if cruel) | **a beating** |
| Relation −50 or worse | 5–9% (×1.5 if cruel) | **to drag you before them** |
| A **cunning** (Calculating) lord at war with you, whatever they think of you | 2% | **to bring you in** as a prisoner |

They must be able to afford it (the fee comes from their own purse). One contract at a time, at most one a week, and
the same enemy at most once a month.

**Warnings:** a companion with Roguery 60+, a notable friend (Trusted) in a nearby town, or your own Roguery 60+ may
tip you off when a price is put on your head — who, but not where. And they hunt within about 200 map units of whoever paid:
wander far enough and they won't find you (though the contract still has ten days to run).

**In the street.** They find you in the next town or village centre you walk (20–60 s in): **3–6 soldiers of the
sender's culture** (3 for a small clan, up to 6 at tier 5+), **tier 2 for a nobody, 3 at clan tier 2–3, 4 at tier 4,
5 beyond**, armed but on foot and without bows, walking straight up to you. Their leader says who sent them and what
for. **"Come and try it."** — a real fight with weapons (you have whatever you walk the streets with; you're beaten down
at about 12% health, never killed) · **outbid** whoever paid (twice the fee: sellswords are loyal to coin) · **give in**
("Get it over with." / "I'll come quietly."). Walk out of the conversation and they take that as an answer. Win and their
leader's purse holds half the fee (standing +2; the sender −5). Run (Tab) and they'll try again tomorrow.

**On the road.** Stay out of towns for 3 days after they're hired and they come for you on the map instead: a
"Hired Swords" party about as big as yours (never against more than 100, an army, or at sea), same choices. The
contract lapses after 10 days.

**If you lose (or give in):**

- **A beating:** they take a quarter of your purse "for their trouble" and leave you in the street (or the road).
- **Carried off:** you come to bound across a saddle, riding out of town as their prisoner, to whoever paid (a lord's
  party, or the gate of the town they're in; nobody gets in the way). Then you face them, in person:
  - a lord **at war** with you **holds you for ransom** (vanilla captivity from there);
  - otherwise they **humiliate** you, take a fifth of your purse, and let you go;
  - a **cruel** one (Mercy below 0) at **−60 or worse** wants you **dead**. **Plead for your life** — vanilla persuasion,
    Hard (Very Hard at −80), two successes needed: a ransom (Trade/Calculating), what killing a bound captive says of
    them (Leadership/Honor), mercy (Charm/Mercy — hopeless with the merciless), or a threat (Roguery/Valor). Win and
    you're held or sent off poorer (relation +5); lose and you're executed — your heir carries on, as with any death.
    Or **"Get it over with."**: a brave enemy (Valor) may respect it, half the time. MCM **You can be executed** off:
    they hold you or strip your purse instead.
  - If whoever paid is dead or captured, or it takes more than 6 days, the band empties your purse and cuts you loose.
- Escape on the way (vanilla's chances), and the band scatters.

## 10. Test mode (MCM → Testing)

- **Test mode:**
  - Parties ignore you, so no looters.
  - Cooldowns and "does it happen" rolls are skipped.
  - Persuasions roll **for real** (vanilla's odds); each argument's result is also shown on screen.
  - `[Test]` lines in conversations: relation ±10, town standing ±10, and "How do you see me?" (prints the full
    breakdown).
- **Player immortal:** you take hits but never go down, in any scene or battle.
- **Your damage (%):** scales the damage you deal, fists and weapons (100 = normal).
- **Buttons:**
  - +10,000 gold
  - Heal all
  - Renown to the next tier
  - Relations +10 with everyone in the settlement
  - Town standing ±10
  - Discover this settlement
  - Street event now — pick which one in **Street event to stage** (Any, or a kind; town-only and village-only kinds
    need the right place). At least half are setups in Test mode.
  - Lords talk now (this week's slander and praise at once)
  - Hired swords: a beating / judgment (forced to the plea for your life) / war capture — paid by your worst enemy
    within reach (or anyone); they walk up to you as soon as you're in a town or village centre
  - Feast now (the nearest notable throws you one)
  - Clear all mod cooldowns
