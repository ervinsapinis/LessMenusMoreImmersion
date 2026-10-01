# Test checklist

Build under test: 2026-10-01. Log: `Modules\RickLauncherData\Mount & Blade II Bannerlord_270d4e21\Documents\Logs\lmmi.log`
(every feature logs what it does — send it with any bug).

## Setup

- MCM → **Testing → Test mode ON**: cooldowns and "does it happen" rolls are skipped, checks roll for real and show their
  odds, parties ignore you on the map (except hired swords).
- **Player immortal** (optional) for fights — note: it also disables "beaten down" in armed fights.
- **Clear LMMI cooldowns** between repeats.
- To make someone hate you: in Test mode, talk to them and use **[Test] Relation -10** (repeat).
- **Street event to stage** picks which street event **Street event now** triggers.

## 1. This round (2026-10-01) — everything from your last list

Each worker's log lines start with its feature name (Street:, Castle:, Feast:, Camp:, Hired:, Tavern:…).

| # | Test | How | Expect |
|---|---|---|---|
| 1 | Lost child | stage **LostChild** | child 60–110 m off, no outline; the mother stops ~35 m short; crying only within 25 m, no portrait |
| 2 | Run from toughs | stage until a setup → run | they sprint the whole way (tell me if they catch you too easily) |
| 3 | Looters, yourself | stage **Raiders** → fight | blows hurt; beaten down at ~12%, not killed; after: the village calms, the one who fetched you comes back to thank you |
| 4 | Looters, "my men" | 5+ regular troops → "my men will see to it" | two lines face off at the outskirts, then fight (watch or join); party screen shows real dead/wounded; lose → morale −5, notables −3, hearth −5 |
| 5 | Harvest | stage **Harvest** → lend a hand / my men | you swing a scythe beside villagers, light moves on 4 h, controls return; leave mid-scene → no black screen; "my men" costs morale |
| 6 | Debt | pay vs fight | different thanks |
| 7 | Cells | go quietly while at peace | you stay until served; escape/ransom offers can come; relation message with the owner's clan |
| 8 | Headsman | as a commoner kill 2+ guards resisting, get caught — cruel / neutral / merciful owners | warning line first; cruel: headsman; neutral: blood-money option (2000 a guard, once); merciful or relation 30+: double time, leave at ~15% health, standing Despised. As a vassal/tier 3+: −10 with the owner |
| 9 | Castle yard | enter a castle | master-at-arms + 3 fighters already round a ring; "Put me in the ring": fists or wooden; stake none/100/300; no deaths; lose → tomorrow; beat all 3 → champion (+2 standing, no wagers 7 days) |
| 10 | Castellan | ask everything | lord's whereabouts, prisoners, bandits (marks a lair), the castle; own castle: double the watch, lay in stores |
| 11 | Garrison life | stay 2 min | drill/dice/prisoner/courier use soldiers already there; nobody pops in in view |
| 12 | Hired swords, street | *Hired swords: a beating* | you take real damage; knocked out by a fall → our menu, never vanilla's |
| 13 | Buy-off | outbid them | a big share of your purse; cruel + hates you → "this one's personal"; paid → they walk 60–100 m before vanishing |
| 14 | Plea | *Hired swords: judgment* | the lord's face/gestures match |
| 15 | Road band | contract, stay out of towns | others leave it alone; in an army → nothing comes |
| 16 | Prices | trade screen at different standings | gear ±4…15%; trade goods ~4% (log names the price model it patched) |
| 17 | Taverns | crowd 200% | the tavern isn't packed |
| 18 | Tavern keeper | each standing band | a round (weekly), news for sale, a room (heals); Despised → shown the door |
| 19 | Camp button | map bar, after Kingdom | greyed with the reason at sea / in a settlement / second time today |
| 20 | Camp report | "How are the men keeping?" | three lines: spirits, the last fight + problems, food and what's missing |
| 21 | Taste of home | morale tooltip on the map | "A taste of home" (up to +5) or "Nothing from home in the stores" (−1) |
| 22 | Cask / bouts | camp | cask once per camp, victory cries, crowd murmur with 10+; 4th bout refused |
| 23 | War story | camp | men gather in a half-circle; persuasion; cheers or yawns |
| 24 | Drill | camp | ranks, weapons out and back, back to the fires; morale ±2 by the 7-day rule |
| 25 | Square feast | near a guard post | guards step out; no one in the table; ham eats, mug drinks; the table blocks you (log: "physics body"); maid pours and talks; musicians (host Power 100+) play; dancers (200+) |
| 26 | Walk out | tab out mid-feast, no goodbye | −2 with the host, effects once |
| 27 | Hall feast | dinner in a busy town's hall | up to 3 tables: host at the head, you two seats down; lords nearby and notables brought in; soldiers at the foot; goes on after your goodbye; a cramped hall falls back to fewer |
| 28 | Court vs feast | feast while a petition is pending | the petitioner waits, then carries on |
| 29 | Gifts | clan tier 4+ | rare, small gifts; days later they come back with a favour to ask; ignore it → −4 |
| 30 | Meet them | wait in a town for "Someone to see you" | the scene opens beside them, mid-conversation |

## 2. Street events (towns and villages)

Stage each kind; for each, try helping, refusing, and walking off.

| Kind | Check |
|---|---|
| Rescue | talk down / fight / pay / back down; left alone, the girl is led off |
| Brawl | talk down / fists / carry on; left alone, a knockout or the watch |
| Thief | chase (red outline, catchable), return / split / call the guard; Roguery 60+ stare-down |
| Injured | [Medicine] roll shown in Test mode; help up; they limp off together |
| Debt (towns) | pay the debt / talk (persuasion) / fight / walk away; gang boss relation changes if they were his men |
| Shakedown (towns) | pay 40 / talk / fistfight the guard (crime +5 if you win, 100 fine if you lose) / back down |
| **Setups** | in Test mode ~½ are setups: the plea looks identical; at the spot the bait walks off and toughs close in: pay / talk / fight / run |
| "Why come to me?" | on a setup: they melt away (Roguery xp); on a genuine plea: it plays out without you (−1) |
| Roguery hunch | with Roguery 60+: a "sets your teeth on edge" message sometimes (also on genuine pleas) |

## 3. Village events (villages only)

| Kind | Check |
|---|---|
| Dispute | old law / [Charm] split (fail → they brawl) / a tenth / [Roguery] bribe (+40, −2) / walk off (they brawl) |
| Harvest | "lend a hand" → guided to a field hand → fade → sacks of the village's crop in your inventory, sun moved on; "my men will help" (9+ men) |
| Raiders | 3–5 looters at the village edge; armed fight (you're beaten down at ~12%, not killed); win: purse + thanks; refuse / lose: flock gone |

## 4. The watch and the cells

| Test | How | Expect |
|---|---|---|
| Watch interrupts | start any fistfight and keep it going 90 s (or near a guard) | a guard breaks it up and questions you |
| Go quietly | fail the watch persuasion → "I'll go quietly" | menu shows the sentence ("a night…" / "N days"); you stay in the cells **even at peace** with that realm; after the time, "Your time is served" → released |
| Resist | "You'll have to take me." | guards fight you with weapons; knock one down (+10 crime), kill one (+25); Tab away (escaped) or get caught (cells, longer sentence) |
| Headsman | set **Guards killed for the headsman** to 1, kill a guard while resisting, get knocked out | "At dawn they bring you up into the square…" → execution (clan tier < 3; at 3+: double sentence + blood price) |
| Save/load | save while serving a sentence, load | still held until served |

## 5. Town standing

| Test | How | Expect |
|---|---|---|
| Bands | *Town standing +10* (Testing) a few times | messages "you're known / respected / honored there now"; entering the town later shows its line |
| Prices | compare a shop's buy/sell prices at 0 and at +25 or more (or −25 or less) | the gap narrows when well liked, widens when disliked |
| Fines | get fined by the watch at different standings | 100 → 50…150 |
| Word travels | +10 in one town | the log (and at 3+, a message) names up to 4 nearby towns/castles of the same people that heard of it |
| The lord hears | be Honored in a town, wait a week | "Word of how X speaks of you has reached {lord}" (+1) |

## 6. Castles (courtyard)

| Test | How | Expect |
|---|---|---|
| The cast | enter any castle courtyard | a master-at-arms (hails you within 7 m) and a castellan (a little further in) |
| Bout | master-at-arms → "Put me against one of your men" | fistfight with a garrison soldier, guards gather and cheer; win: xp + standing +1 |
| Wager | "a hundred says I put him down" | win: 100 back plus the odds; lose: 100 gone |
| Train companions | with companions in the party | 100 each; +150 xp in their best combat skill; 3-day cooldown |
| Drill garrison | in **your own** castle | garrison xp message; weekly |
| News | castellan → "Any news from the roads?" | the nearest hostile lord/army and a place name, or "quiet" |
| Veterans | castellan, someone else's castle (same realm, or the lord likes you, or you're Known there) | up to 5 tier-3+ men offered at a price; they leave the garrison and join you |
| Report | castellan in your own castle | garrison, prisoners, food days, loyalty, security |
| Garrison life | stay ~2 minutes | a sergeant drilling recruits, a prisoner walked across (if the castle holds prisoners), dice by the wall, a courier (you overhear news) |
| No street events | stay a while | no street events or quarrels/preachers in castle yards |

## 7. Lords' halls

| Test | How | Expect |
|---|---|---|
| Petition before the lord | enter a hall where the owner's clan has a lord (Test mode: every visit) | a petitioner walks to the lord and pleads (bark); ~25 s later the lord rules (bark) and they leave |
| Intercede | talk to the lord while the petition is pending → "Speak for the petitioner" | persuasion (3 arguments); win: granted, +3 standing; lose: −2 with the lord |
| Back the lord | same → "Justice must be seen to be done" | +1 lord, −1 standing |
| Your own court | a hall in **your own** town/castle | a petitioner comes to you; each ruling shows loyalty/security changes; "Not now" costs 1 loyalty |
| Gossip | stand near lords in a hall for a minute | wars, prisoners, the biggest army — and you, if the town has a name for you |

## 8. Camp (Ctrl+T)

| Test | How | Expect |
|---|---|---|
| Make camp | on the map (not in a settlement/battle/at sea/someone else's army), Ctrl+T | battle terrain of that spot; tents, 1–3 fires, companions and up to 24 men sitting/standing around, 2 sentries |
| Refusals | Ctrl+T inside a town menu / at sea / in another's army | a message why not |
| Talk to a man | walk up, press the talk key | greeting follows morale |
| Report | "How are the men keeping?" | morale word, food days, wounded |
| Wine | "Break out a cask" | gold down (2/man), morale +5, men cheer; daily |
| Story | "[Leadership] Gather round…" | Test mode shows the roll; success: morale +3, cheer |
| Bout | "Fancy a bout?" | fistfight; afterwards he returns to his seat |
| Drill | "On your feet — drill" | xp for the party, morale −2; daily |
| Companions | talk to one | their normal conversations work |
| Leave | Tab | back to the map, nothing left behind |

## 9. Hired swords

| Test | How | Expect |
|---|---|---|
| Street, fight | *Hired swords: a beating* → town centre → "Come and try it" | armed fight; lose at ~12% health: beaten, purse −25%, fade back in; win: half the fee from their leader, standing +2 |
| Street, outbid | same → outbid (2× fee) | they walk off |
| Street, give in | "Get it over with" / "I'll come quietly" | beating / carried off (see re-test 1) |
| Judgment variants | *Hired swords: war capture* (needs a lord at war) | held prisoner (vanilla captivity) |
| Humiliated | a sender who isn't cruel (no button: lower a merciful-ish lord to −50 with [Test] lines and wait) | "crawl back to your people", a fifth of your purse |
| Warning | a companion with Roguery 60+ in your party when a contract is made | "{companion} pulls you aside…" (yellow) |
| Range | a contract, then travel 200+ map units from the sender | they don't come for you there |
| Road fallback | a contract, then stay out of town centres 3 days | a "Hired Swords" party on the map |
| Save/load | save with a pending contract, load | still pending |

## 10. Lords

| Test | How | Expect |
|---|---|---|
| Slander / praise | a non-honorable lord at −30 or worse (or a friend at 40+) → *Lords talk now* | "Word reaches you: X has been speaking ill/well of you" |
| Reputation | talk to a lord who's at 0 or better → "What do they say of me at court?" | realm opinion + best/worst town + the last slanderer |
| Grievance | after hired swords named their sender, talk to the sender's clan leader or ruler | "I've a grievance against…" → persuasion; win: fine (half to you) |
| Revenge | a gang leader who'll deal with you → "I need some men to deal with someone" | pick one of your 3 worst enemies; pay; Test mode: word within the hour (done/failed, maybe found out) |

## 11. Taverns

| Test | How | Expect |
|---|---|---|
| Dice | talk to a tavern patron → "Fancy a game of dice?" | stakes 10/50/100, rolls shown, gold changes |
| Loaded dice | Roguery 30+ | you win unless caught (pay double, −2 standing) |
| Refusals | five wins in a visit, or as a resented foreigner with no standing | they won't play |
