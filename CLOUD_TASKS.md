# Cloud session brief — LMMI remaining work

You are continuing work on **Less Menus, More Immersion (LMMI)**, a Mount & Blade II: Bannerlord v1.4.8 mod
(C#, Harmony, MCM v5). The mod is a campaign-side immersion layer: people in towns, villages, castles, lords' halls and
the player's camp behave like people (street events, feasts, hired swords, town standing, prejudice, petitions…).

## What you can and can't do here — read first

- **You cannot build or run anything.** The project compiles against the game's DLLs, which aren't in this repo.
  The lead (me, on the user's machine) does the build + a compile-fix pass afterwards. So:
  - **Only use APIs you can see used elsewhere in this repo, or that the drafts below already use.** Never invent a
    game API. Where you are not sure a member exists, mark it `// VERIFY: <what to check>` and keep it isolated.
  - Keep new code in **new files** with minimal edits to existing ones, so a compile error is easy to find and fix.
  - Do **not** add Harmony patches on engine types (`Agent`, etc.) and do **not** create `ActionIndexCache` in static
    initializers: that corrupted character models (folded characters in menus) earlier. Patches belong on
    campaign-behavior methods; engine-type patches must be applied late (see `HiredBeatDownPatch.ApplyLate`).
- Work on a **new branch** (`cloud/c5-docs`), small commits. Don't merge into `dynamicDiscovery`; the user decides.
- **Do not edit** `_Module/ModuleData/Languages/std_module_strings_xml.xml` (the lead extracts new text ids with
  `tools/extract_localization_strings.py` and a sync script), `SubModule.cs`, `Settings/*`, the `.csproj`, or any file
  not named in a task below.

## Conventions (match the surrounding code)

- User-visible text: `new TextObject("{=lmmi_<area>_<name>}English text")`. Every id unique and prefixed `lmmi_`; never
  reuse or add vanilla ids. In C# strings the gender conditional is `{?PLAYER.GENDER}her{?}him{\\?}` (escaped
  backslash). Changed wording ⇒ new id (the XML would otherwise keep the old text).
- `LmmiLog.Info(...)` for what happens; `try { … } catch (Exception ex) { LmmiLog.Error("…", ex); }` around handlers.
- Vanilla tone: short, a bit gruff, period-appropriate; consequences through relation, standing, loyalty/security,
  prosperity, gold, notable power. Always a "left alone" / decline outcome. Prejudice by culture is part of the mod.
- Existing helpers: `StreetEventsBehavior` (Direct/Release/Bark/…), `NativePersuasion` (vanilla persuasion wrapper),
  `TownStandingBehavior` (Get/Adjust/Band), `NotableDisposition`, `CultureRelations`.

## Task 1 — C5: more petitions in lords' halls (the main job)

Files: `Behaviors/HallCourtBehavior.cs` (current; `partial class`), and **your new file** `Behaviors/HallPetitions.cs`.

Background: halls show petitions in two modes — a petitioner pleads **before a lord** (the player may intercede via
`NativePersuasion("court")` or back the lord) and, in a hall the **player owns**, the petitioner pleads to the player
and the ruling moves real numbers (loyalty / security / prosperity / notable relations / gold / town standing).
The current code has six kinds: `SeedGrain, Pressed, Poacher, Widow, Quartered, Feud`.

A previous worker already drafted the full thing but it was pulled from the build before it compiled:

- `drafts/C5/HallPetitions.cs.txt` — plea variants (2–3 per kind), ruling lines, and own-court effects for **12 new
  kinds** plus extra variants for the old ones (a `partial class HallCourtBehavior`).
- `drafts/C5/HallCourtBehavior.cs.txt` — an older `HallCourtBehavior.cs` with the new kinds wired in. **It is stale**:
  the current `Behaviors/HallCourtBehavior.cs` has since gained the "feast wins" logic (petitions pause while
  `FeastBehavior.IsBusy` and resume after) which must be kept.

Do this:
1. Diff the draft `HallCourtBehavior.cs.txt` against the current `Behaviors/HallCourtBehavior.cs` and **port only the
   wiring for the new kinds** into the current file (enum values, kind selection, plea/ruling lookups, own-court
   handling). Do not undo the feast-postponement code or anything else that's in the current file and not in the draft.
2. Move the draft content into `Behaviors/HallPetitions.cs` (drop the `.txt`), fixing anything that references members
   that don't exist in the current `HallCourtBehavior.cs` (check names/signatures by reading the current file).
3. Kinds to cover — the user's own list first, then the draft's extras: a merchant's cart seized at the gate; a runaway
   apprentice; a bride-price dispute; a village asking the lord to deal with bandits; a debtor begging more time; a
   smith asking for a monopoly; a mother whose son killed a man in a brawl; a guard accused of taking bribes.
   Each: 2–3 plea variants (random), a grant line and a refusal line for lords, and for the own-court mode an effect
   per choice using levers that already appear in `HallCourtBehavior.cs` (`Move(loyalty, security, standing)`, notable
   relation, gold, prosperity). More plea variants for the six existing kinds are welcome.
4. Selection: weight kinds sensibly by settlement (town vs castle vs village-owned hall), culture, and whether it's the
   player's own hall; don't repeat the same kind twice in a row.
5. Every new `TextObject` has a unique `lmmi_court_*` id. List the new ids at the end of your report.

Definition of done: code is internally consistent (read it through as a compiler would: names, types, usings,
`partial` declarations match), nothing from the draft left half-wired, every uncertain API marked `// VERIFY:`.

## Task 2 — Docs: bring FEATURES.md and DESIGN_BEING_KNOWN.md up to date

`CHANGELOG.md` (the "Playtest round, 2026-10-01" entry), `BACKLOG.md` and `TESTING.md` already describe this round.
`FEATURES.md` (player-facing guide) and `DESIGN_BEING_KNOWN.md` (design + tuning notes, has "Round 9/10" sections) do not.

- `FEATURES.md`: add/refresh sections for what this round built — the cells and the headsman rules (see
  `StreetEventsBehavior*.cs`), looters/"my men", the harvest, castle yard bouts and the castellan (`Castle*.cs`), the
  camp (`Camp*.cs`: button, once-a-day, report, taste of home, cask needing wine/beer, story persuasion, drill),
  feasts (`Feast*.cs`: staff, hall feasts), the tavern keeper (`Tavern*.cs`), price effects of town standing
  (`StandingPricePatch.cs`), hired swords changes (`HiredSwordsBehavior.cs`), arrivals/gifts/obligations
  (`Arrival*.cs`). **Describe only what the code does**: read the relevant file for numbers (costs, thresholds,
  cooldowns); if code and CHANGELOG disagree, trust the code and note the difference in your report.
- `DESIGN_BEING_KNOWN.md`: add a "Round 11 (2026-10-01)" section in the same terse style as Rounds 9–10: mechanisms,
  numbers, files, open questions.
- Don't reformat existing text; add and refresh.

## Task 3 — B9 design write-up: feast mingling (design only, no code)

Create `DESIGN_FEAST_MINGLING.md`. Idea from the user: after the first round of food and drink at a feast, guests stand
up and interact with one another; some approach the player directly for a short **dialog minigame** that raises town
standing, relations, morale, or a player trait depending on who they are (a lord → relation; a notable → town standing;
the player's soldier → party morale; a companion → trait xp such as Honor/Mercy/Valor…).
Read `Behaviors/FeastBehavior.cs`, `FeastHall.cs`, `FeastStaff.cs` and `Contacts/NativePersuasion.cs`, then write:
subtasks (B9a stand-up and pairs/toasts; B9b guests approaching; B9c the per-guest dialog minigame; B9d limits and
pacing), how each would hook into the existing feast phases (square vs hall, the "hall feasts on after you leave"
phase), which existing helpers to reuse, what could go wrong (seating restore, pathing, conversation callbacks —
remember: never fade/re-team/remove agents inside a conversation-end callback), saved-state needs, estimated size per
subtask (S/M/L), and a recommended order. No code.

## Report back (short)

Per task: done / partly / skipped; files changed or created; the list of new text ids (Task 1); the list of every
`// VERIFY:` you left; anything you changed outside the files named above (should be nothing).
