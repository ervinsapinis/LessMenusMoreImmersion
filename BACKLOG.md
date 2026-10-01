# Backlog — playtest feedback, 2026-09-30

Status: `[ ]` open · `[x]` done — built and installed 2026-10-01, not yet playtested. C5 draft: scratchpad C5_draft (for a cloud session). A8 redesign proposal awaits a decision.

## Fixed directly

- [x] **Crash leaving a settlement after a street sentence was served** — released at the gate, but the town menu stayed on
  the stack; "Leave" had no current settlement. The served menu now closes the stale menu. (StreetEventsBehavior)
- [x] Headsman default: more than one guard killed (2) → execution below clan tier 3. (Settings)

## A — Streets, villages, the cells (StreetEventsBehavior, JailSentencePatch, SceneSpawner)

- [x] A1 Lost child: harder to find (further; the mother only knows roughly), **no outline** (the crying is the clue); the
  cry's portrait shows a random cavalryman — fix (no portrait, or the right one).
- [x] A2 Running from toughs: they walk when close and are slow — they must run, all the way.
- [x] A3 Looters: the requester runs off in fear and doesn't come back to thank you; the village keeps panicking after
  the fight — calm the whole village and bring the requester back.
- [x] A4 Looters, "my men will see to it": a real fight — some of your men charge the looters in the scene (actual combat;
  deaths and wounds are real), riskier; sheep may be lost.
- [x] A5 Harvest: a small cutscene — you (and the villagers) working the field with the farming animation and a tool prop;
  make the passage of time visible. "My men will help": a cutscene of your men in the field, and it costs morale.
- [x] A6 Debt: the debtor's thanks says "I'll pay it back" even after you paid the debt — separate lines for paid vs
  fought/talked down.
- [x] A7 Cells: allow vanilla's captivity events (escape, ransom offers) while serving — only the "released because not at
  war" must be blocked. Being jailed lowers relation with the owning clan's members.
- [x] A8 Headsman: below clan tier 3, killing more than one guard = execution (setting default changed to 2).

## B — Feasts (FeastBehavior)

- [x] B1 Guards (and other non-townsfolk) not cleared from the table area; avoid spots next to guard posts.
- [x] B2 The spawned table has no collision.
- [x] B3 NPCs still clip inside tables.
- [x] B4 Props must match animations: mugs → drinking, ham → eating.
- [x] B5 A tavern maid with a jug, musicians and dancers — by the host's power for notables; always for lords' hall feasts.
  Some dialog for them.
- [x] B6 Lord's-hall feasts: fill the hall — local notables, lords in the settlement, companions, the best soldiers of both
  parties; the player in the 2nd/3rd seat; lasts longer; carries on after the player leaves the table (Warband-like).
- [x] B7 Tab out mid-feast: no conclusion today — the host should be upset if you left early without a word.
- [x] B8 Feasts and hall court events must not overlap. (feast wins: petitions wait — done in C6)

## C — Castles and halls (CastleLifeBehavior, HallCourtBehavior)

- [x] C1 Master-at-arms: no fighters appearing out of thin air — he stands with 3 fighters of rising rank; beat all three
  and the yard is closed to your wagers for a week.
- [x] C2 Sparring with wooden weapons (like the campaign tutorial's training field), besides bare hands.
- [x] C3 Castellan: more (and more interesting) options.
- [x] C4 Garrison life: dice players and drill recruits appear out of thin air near guards — use soldiers already in the
  yard (walk them over) instead.
- [ ] C5 Petitions: more kinds and more flavor text (they're fun).
- [x] C6 Hall court must not run during a feast (see B8).

## D — Camp (CampBehavior, CampMissionLogic, CampMissionViews, new files)

- [x] D1 A **Camp** button on the map bar (bottom), besides Ctrl+T.
- [x] D2 Once per day: "You've already made camp today."
- [x] D3 "How are the men keeping?": much more flavor — morale, recent battles, issues, food stocks and variety.
- [x] D4 **Culture food preferences**: troops of a culture like certain foods; having them in the stores lifts morale
  (a new morale factor, shown in the morale tooltip).
- [x] D5 A cask: once per camp (or steeply diminishing), small gain, less if morale is already high; more after a recent
  battle, most after a lost one. Cheers use real battle victory cries. Ambient crowd chatter when 10+ in camp.
- [x] D6 Bouts: at most three per camp ("Come on, captain, calm down — who'll strike camp at this rate?").
- [x] D7 The war story: a persuasion-style minigame (vanilla persuasion) with the men gathered round — not a dice roll.
- [x] D8 Drill: an animated cutscene; raises morale if no battle in 7 days, costs morale otherwise.

## E — Hired swords, trade, taverns (HiredSwordsBehavior, StandingPricePatch, CrowdDensityPatch, TavernLifeBehavior)

- [x] E1 Knocked out during the street fight with hired swords (or by the guards) — nothing happens; it must count as a
  loss (beaten / carried off).
- [x] E2 Buying them off is far too cheap (4k at max clan tier) — scale with your wealth and how much the sender hates you;
  sometimes they can't be bought at all.
- [x] E3 Bought off, they must walk much further before fading out.
- [x] E4 Emotion animations on the lord while you plead for your life.
- [x] E5 War capture: the band can be intercepted and beaten by other lords — make it a quest-like party nobody attacks;
  never sent while you're in an army.
- [x] E6 Town standing doesn't visibly change prices — make it work (probably another model is in use) and noticeable.
- [x] E7 Taverns are overcrowded — the crowd density setting shouldn't apply there.
- [x] E8 Tavern keeper: more important and follows town standing (research and review what he offers).

## F — Arrivals (ArrivalScenesBehavior)

- [x] F1 Sycophants ("pay their respects") come too often and just shower money — rarer, and they want something back
  (a favor, a request; consequences for refusing).
- [x] F2 "X has come to pay their respects" (wait-menu prompt): accepting should work like visiting a notable — straight
  into the scene and into their conversation.

## Research / later

- Town standing: more uses (after more testing).
- Slander/praise: more testing.
