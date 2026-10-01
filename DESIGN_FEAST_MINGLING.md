# Feast mingling (B9) — design

Status: design only, nothing built. Written 2026-10-01 against `FeastBehavior.cs`, `FeastHall.cs`, `FeastStaff.cs`,
`Contacts/NativePersuasion.cs` and the patterns in `HallCourtBehavior.cs` / `CampBehavior.cs`.

The idea (from the user): after the first round of food and drink, guests get up and mix — pairs talk, someone raises
a cup — and some come over to the player for a short **dialog minigame**. What it's worth depends on who they are:

| Who | Reward | Lever already in the code |
|---|---|---|
| a lord or lady | relation | `ChangeRelationAction.ApplyPlayerRelation` |
| a notable | town standing (and a little relation) | `TownStandingBehavior.Adjust` |
| one of your soldiers | party morale | `MobileParty.MainParty.RecentEventsMorale` |
| a companion | trait xp (Honor, Mercy, Valor, Generosity, Calculating) | vanilla `PlayerTraitDeveloper.AddTraitXp` — **not used anywhere in the repo yet; verify it exists in 1.4.8** |
| a local (square feasts) | a little standing | `TownStandingBehavior.Adjust` |

## What exists today (the hooks)

- **Phases** (`FeastBehavior.Phase`): `Pending → Darkening → Feasting → Ending`, and for a hall `Feasting → Lingering`
  (`EndInHall`): the effects are applied, the player is free, everyone stays at the table until the mission ends.
- **Timing**: square feast `FeastSeconds` 75 s (early < 40 s); hall `HallFeastSeconds` 150 s (early < 70 s); Lingering is
  open-ended. The host's toast is the first `Chatter` bark (`f.Toasted`), ~1.6 s after the lights come up; then a bark
  every 6–10 s (14–18 s while Lingering).
- **Who's at the table**: `f.Yours` / `f.Theirs` (agents), `f.Spawned` (brought in, faded at the end), `f.Borrowed`
  (already in the scene, released at the end), `f.Staff` (maid, musicians, dancers), `f.Held` (the watch, kept off).
- **Seating**: `SitOnChair` takes an agent over with `LmmiStageBehavior` (`Occupy`), **teleports** them to the chair's
  point (`PointFor`), faces them (`FacePoint`), `UseGameObject`, then fast-forwards the chair (`SimulateTick` ×50) so they
  are already seated. `Redress` sets each of *our* chair points to eat (ham), drink (mug) or sit. **Nobody records which
  chair an agent sat in.**
- **Geometry**: `f.Center`, `f.Along`, `f.HalfTable`, `f.Radius` (the cleared area), `ChairOut` 1.32 m; hall tables come
  from `FitTables` (a 2 m navmesh grid within 25 m of the host). The band stands 6–7.5 m from every seat
  (`FindBandSpot`), dancers ≥ 4 m from every seat; the maid at the table end nearest the host.
- **Talking**: the generic `lmmi_feast_guest` start line (priority 1250) answers anyone in `Yours`/`Theirs` with a
  canned line + "Cheers!"; the host has his own (1100) while `Feasting`; the maid (1300).
- **Elsewhere**: `FeastBehavior.IsBusy` is true from `Begin` until the mission ends — including Lingering — so hall
  court petitions stay paused (C6) and gossip waits for the whole visit once a hall feast has started.
- `FeastBehavior.SyncData` is empty: feasts are transient.

## Subtasks

### B9a — Standing up: pairs and toasts (M)

After the first round (see pacing, B9d), every 12–20 s one or two guests get up from the table and do something for
~20–30 s, then sit back down:

- **a pair**: two guests (prefer a lord + a notable, two soldiers, a companion + a local) walk to a **mingling spot**
  and talk, facing each other (`act_conversation_*_loop`: warrior / confident / hip / closed — all already used in the
  repo), with a bark or two (`FeastBehavior.Bark` → `MBInformationManager.AddQuickInformation`).
- **a toast**: one guest stands at their place, raises a cup (`act_cheer_1..4`), a bark ("To the harvest!"), and the
  nearest few cheer from their seats (barks only — seated agents keep their chair loop).

Implementation:

1. **Record seats**: a `Dictionary<Agent, Chair> SeatOf` on `Feast`, filled where `Build`/`BuildInHall` call
   `SitOnChair` successfully. Everything below depends on it.
2. **Mingling spots**, computed once at build time (not per tick: they're raycasts): hall — reuse `FitTables`' floor
   grid, keep points ≥ `ChairOut` + 1.2 m from the table rectangle, ≥ 2 m from `f.Staff.BandAt`, ≥ 1.5 m from dancers and
   the maid, `ClearAround(1.0)`, and `CastleStaging.ClearLine` from the guest's chair point (so they don't walk through a
   table — our tables have a physics body but are **not cut out of the navmesh**). Square — the same, inside `f.Radius`
   (which is only `span/2 + 3.2` m: there may be no room; then no pairs, toasts only).
3. **Up**: `StreetEventsBehavior.Direct(agent)` (already stops the chair use) → `GoTo(spot, run: false, face: partner,
   loop: …)`.
4. **Back down — the hard part.** `SitOnChair` as written teleports and fast-forwards: fine in the dark, a visible pop
   in the light. Add a `ReturnToChair(agent, chair)` that walks them to `PointFor(chair)` (`GoTo`), and only when they've
   arrived calls `FacePoint` + `UseGameObject` **without** the fast-forward, letting vanilla's sit-down animation play;
   re-`Redress` our chair point first (standing drops the prop). Fall back to the teleport version if they haven't sat
   within ~6 s, preferably when the player isn't looking (`CastleStaging.OutOfSight`).
5. Who never gets up: the host while `Feasting`, the player's chair, staff, anyone the player is talking to, anyone
   whose chair point is occupied by someone else.

### B9b — Guests come to you (M)

At most one approach at a time. A chosen guest (B9d says who and when) gets up, walks to the player
(`Direct(...).Follow(Agent.Main, 1.8f, run: false)`), and — once within ~3 m, the player not in a conversation, no
`_afterTalk` queued — starts the conversation the way the own-court petitioner does
(`MissionConversationLogic.StartConversation(agent, setActionsInstantly: false)`, `HallCourtBehavior.UpdatePetition`).
Before that, a bark announces it ("A word, if you've a moment?"), so it isn't a surprise.

- If the player walks off, sits in another chair, or doesn't stop within ~25 s, the guest gives up (a bark) and goes
  back to their chair (B9a step 4) — no penalty.
- **Open question — VERIFY in game:** what `StartConversation` does when the player is **seated**. If it stands the player
  up or misbehaves, approach only a standing player, and have a seated player's approacher stand by the chair and bark
  instead (the player answers by talking to them: same dialog).
- A new start line (`lmmi_feast_mingle_open`, priority above the 1250 guest line and the 1300 maid line) that only
  answers the agent with a pending approach; everyone else keeps the canned guest line.

### B9c — The minigame, per guest (L)

Short: one exchange, one roll at most, an answer either way. Two shapes:

- **A test (lords, notables, soldiers, locals): vanilla persuasion**, one success (`NativePersuasion`, goal 1, Easy or
  Medium). One shared instance (`new NativePersuasion("feast_mingle")`, `Register` once) started with three arguments
  built per guest with `NativePersuasion.Argument(skill, trait, line, listener, resentment, standing)` — so the listener's
  own traits, prejudice (`CultureRelations`) and town standing already shape the odds. Plus a polite way out
  (`AddLeave`), which costs nothing.
  - *A lord sounds you out* — the war, a rival house, a marriage rumour: Calculating/Leadership, Honor/Charm,
    Valor/Tactics. Won: relation +2 (+1 more if they share the trait); lost: 0, or −1 on a critical failure.
  - *A notable takes your measure* — a favour floated, your manners tested: Generosity/Charm, Calculating/Trade,
    Honor/Leadership. Won: standing +2 and relation +1; lost: standing −1.
  - *One of your men raises a cup to you* — answer the toast: Leadership/Valor, Charm/Generosity, Roguery/Calculating
    (a joke at the sergeant's expense). Won: morale +2 and a cheer from your end of the table; lost: laughter, +0.
  - *A local who resents your people* (square, or a notable with prejudice) comes to needle you — the mod's prejudice,
    face to face: Valor/Leadership, Mercy/Charm, Calculating/Roguery. Won: standing +2 and that person's prejudice drops for
    the visit (`StreetEventsBehavior.MarkGrateful`, as the heckler does); lost: standing −1.
- **A choice (companions): no roll.** A companion takes you aside with something on their mind — a man they let go, a
  debt they won't collect, a fight they walked away from — and asks what you'd have done. Each answer is one trait
  (Mercy / Honor / Valor / Generosity / Calculating) and gives the player a little xp in it (~+20; verify the API), and
  the companion +1 relation if it matches their own trait, −1 if it's against it. This fits "trait xp" better than a
  skill check: traits are about what you choose, not how well you talk.
- Rewards are applied in an `_afterTalk` action after the conversation has fully closed (below), never in the dialog
  consequence itself.
- Text: ~6–8 openers per guest type plus 3 arguments each and won/lost lines: ~80–100 new `lmmi_feast_mingle_*` ids.
  This is most of the subtask's size.

### B9d — Limits and pacing (S)

- **When**: not before the first round is over — `f.Toasted` and at least 25 s into `Feasting` (hall) — and never in a
  square feast's last 20 s. In `Lingering`, any time.
- **How much**: hall — up to 3 approaches while `Feasting`, 2 more while `Lingering`; square — at most 1 (the evening
  is 75 s: recommend none in v1, see below). At least 25 s between approaches, 12–20 s between pairs/toasts, at most 3
  guests up at once.
- **Who**: each guest approaches at most once per feast; not someone at relation < −10 unless it's the needling variant;
  companions only if you have one at the table; soldiers only yours. Weight: lords > notables > companions > soldiers in a
  hall; locals and soldiers in a square.
- **Caps**: morale from mingling ≤ +4 per feast (the feast itself gives +10); relation per hero ≤ +3 per 7 days;
  companion trait xp once per companion per 7 days.
- **Saved state**: the only thing that outlives the scene is those per-hero 7-day cooldowns — a
  `Dictionary<string, double>` "mingle:heroId → ready-at hours" synced as one string (`"key;hours|…"`, the
  `HallCourtBehavior.SyncData` format) under a new key (`lmmi_feast_mingle_v1`). Everything else is per feast, in `Feast`.
- **Switch**: an MCM toggle ("Feast mingling", default on) would belong in `Settings/` — out of scope for this write-up;
  until then a `const bool` like `CampFoodMorale.Enabled`.

## Hooking into the phases

| Phase | Square feast | Hall feast |
|---|---|---|
| `Darkening` (`Build` / `BuildInHall`) | record `SeatOf`; compute mingling spots inside `f.Radius` (likely few) | record `SeatOf`; spots from the `FitTables` grid |
| `Feasting` | v1: nothing (or one approach after 30 s if spots exist) | `TickMingle(f, mission)` beside `Shoo` / `Chatter` / `TickStaff`, after the first round |
| `EndInHall` → `Lingering` | — | keep mingling; the effects are already applied, no early-leave risk — **the safest place to start** |
| `Ending` (fade out) / `Cleanup` | stop everything; anyone standing is handled by `Cleanup` as today (spawned fade, borrowed released) | — (`Lingering` ends with the mission) |
| `Abandon` / `OnMissionEnded` | drop the mingle state; nothing to restore | same |

`TickMingle` must skip while `mission.Mode == MissionMode.Conversation`, while a conversation is in progress, and while
`_afterTalk` (a new list on `FeastBehavior`) isn't empty — then drain `_afterTalk` first, exactly as
`HallCourtBehavior.OnMissionTick` does.

## What could go wrong

- **Seat restore.** No record of who sat where (fix: `SeatOf`). Our chairs' points are re-dressed per guest; standing up
  drops the prop and the point may keep the old loop/item — re-`Redress` before sitting back. A guard's or soldier's
  action set may lack the chair's eating loop (`Fits`/`HasAction` already handle that at build time; keep the same chair).
  The fast-forward sit in plain light is a pop (B9a step 4). The hall's **own** chairs (the fallback layout) have their
  own animation points — don't re-dress those.
- **Pathing.** Spawned tables have collision but aren't navmesh obstacles: a guest pathing straight through a table
  clips into it (the B3 problem). Choose spots with a clear line from the chair point, and only from the side of the
  table the chair is on. Halls have stairs and galleries: spots on the host's floor only (`FitTables` already filters
  by level). Squares: keep inside `f.Radius`, or the shoo logic (`Shoo`) and passers-by mix with standing guests.
- **Conversations.** **Never fade, re-team, remove or re-seat agents inside a conversation-end callback, a dialog
  consequence or a `NativePersuasion` `onWon`/`onLost`** — vanilla is still closing the conversation (the thief-hand-off
  crash). Set a flag, queue the action on `_afterTalk`, run it from the tick once the conversation has fully closed. The
  same goes for sending the guest back to their chair after the talk.
- **Dialog clashes.** `lmmi_feast_guest` (1250) answers anyone at the table; another mod's high-priority start line can
  win over ours (as Dramalord did with greetings). Give the mingle line a higher priority and a tight condition (this
  agent, an approach pending), and fall back to the canned line if the approach state is gone.
- **The seated player** and `StartConversation` (B9b open question).
- **Animations.** No `ActionIndexCache` in static fields or static initializers (the folded-characters bug); cache lazily
  at first use, as `CampMissionLogic.Act` does. No Harmony patches on `Agent`; nothing here needs one.
- **Staff.** The band and dancers are placed with distances to *seats*, not to standing people: keep spots clear of
  `BandAt` and the dancers, or the band ends up in a crowd.
- **Hall court.** Unchanged (`IsBusy` already holds through `Lingering`), but it means a hall feast ends court for that
  visit; mingling adds no new interaction there.
- **Performance.** Spot finding is raycast-heavy: once per feast, at build time, in the dark.

## Sizes and order

| Subtask | Size | Notes |
|---|---|---|
| B9a pairs and toasts | M | `SeatOf`, spots, `ReturnToChair` — the technical risk |
| B9b approaches | M | the petitioner pattern; the seated-player question |
| B9c minigames | L | mostly content (~80–100 ids); companions' choice dialog; one VERIFY (trait xp API) |
| B9d limits, cooldowns, save | S | one dictionary, one sync key |

Recommended order:

1. **B9a spike in hall `Lingering` only**: one guest up, a pair spot, back to the chair with a real sit-down. If the
   return can't be made to look right, everything else changes shape (approachers stand by their chair instead of
   walking over), so prove it first.
2. **B9b in `Lingering`**, with the soldier variant of B9c only (the simplest reward: morale) — the whole loop, end to end.
3. **B9d** limits and the 7-day cooldowns before anything else ships.
4. **B9c** the other guest types (lords, notables, the needling local, companions' choice).
5. Move the start into the hall's **`Feasting`** phase after the first round.
6. **Square feasts last, if at all.** 75 seconds and a tight cleared radius leave little room; either lengthen the
   square feast when mingling is on (and re-check the early-leave thresholds) or leave the square as it is. My
   recommendation: leave it — the hall is where a Warband-style evening belongs.
