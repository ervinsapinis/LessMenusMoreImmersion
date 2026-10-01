# Changelog

All notable changes to **Less Menus, More Immersion** are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the
project loosely adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.7.0] - 2026-10-01 — "Being Known"

The second layer of the mod: after *knowing the way* around a settlement, *being known* by its people.
Player guide: `FEATURES.md`. Design and tuning notes: `DESIGN_BEING_KNOWN.md`.

### Added
- **Notable disposition.** Every notable judges you on four things: your deeds (their relation with you,
  weighted by their Honor, plus the town's opinion of you), your power (clan tier on a steep curve, your
  position in the realm, the army at your back), whether you're kindred spirits (your traits against theirs),
  and prejudice (culture, softened by their Mercy and fading with your rise — faster for the calculating).
  Levels: Contempt, Wary, Friendly, Trusted; and power-bought flattery is told apart from real friendship.
- **Culture table** (`ModuleData/lmmi_cultures.xml`): who resents whom, editable without code.
- **Notables speak their mind.** Greetings, "what is it?" prompts, first-meeting introductions and refusals
  follow disposition, with variants for vanilla's four speaking personas; contemptuous notables skip
  "I know your name". Foreigners pay a premium to Wary notables.
- **Favors, face to face:** gang leaders reveal hideouts, clear your crime rating, name the prisoners in the
  keep; merchants give trade tips and lend coin; headmen and landowners spare provisions; headmen, landowners
  and artisans tend your wounded. Every notable can vouch for you with another.
- **Loans, on terms that follow how they see you:** a Trusted friend lends at 10%, a Friendly merchant at 20%,
  a Wary one at 35% (and goes to the magistrate if you're late: crime rating); gang leaders and dishonorable
  merchants lend at a shark's 40% and send collectors. Nobody lends to someone they despise.
- **Vouching has weight:** let down someone a friend vouched to, and the friend loses face — and some regard for you.
- **Letters of introduction:** a friend writes to someone in a nearby town who wants nothing to do with you;
  deliver it in person (tracked on the map) and it counts as a vouch.
- **Town standing:** quests, tournaments, hideouts cleared, bandits beaten, villages defended — or raided, a town
  sacked, a loan defaulted — are remembered by the whole settlement and fade slowly.
- **People come to you:** on entering a town or village, a notable may walk up — a friend who missed you
  throws a feast, a friend warns you about a schemer, someone you helped thanks you, a sycophant brings
  tribute (and later a petition against a rival), someone cruel tells you to leave. Townsfolk too: beggars,
  hecklers who hate your kind, admirers who heard what you did.
- **Treachery:** dishonorable notables who aren't your friends may sell false trade tips, bait hideouts, or take
  your coin and tip off the watch. A friend's warning or Roguery 60+ lets you see it coming.
- **Grudges turn violent:** cruel notables with a grudge, whose warning you defied, or whose loan you left unpaid
  may send men after you on the road (never against parties over 60); purses get lifted in hostile tavern
  districts at night.
- **Vanilla levers:** disposition shifts recruit slots (Contempt −2, Wary −1, Trusted +1, on whichever volunteer
  model is loaded); the recruitment arrangement fee and recruit prices follow disposition too.
- **Talk your way into work:** a notable who despises you no longer greys out "I heard you may need some help".
  They tell you why they don't trust you, and you answer with your Honor, Calculating, Valor or Generosity
  (helped by Charm). Convince them and vanilla's quest offer follows; fail and they won't hear it for 3 days.
- **Grudging compliance:** Wary and contemptuous notables still sell you recruits and the recruitment arrangement,
  at a higher price, and they let you know how they feel about it. Farewells follow disposition too.
- **Guards have opinions:** street guards welcome a kinsman, tolerate a foreigner of standing and sneer at a
  foreign nobody; the lord's hall guard turns you away with a joke at your people's expense; keep and dungeon
  guards greet kin and needle foreigners. Vanilla still decides who gets in.
- **Street events, staged in the scene:** something is really happening a little way off, and a local runs up
  and leads you there (outlined, so you can follow them through the crowd). Men surround a frightened girl (fight
  them, face them down with Valor, pay them off, or back away; they get tougher as you level up). Two youths are
  (two boys, or two young men of 18–20) actually brawling (Valor, Charm, or your own fists; fail and they turn on you or go back at it). A street kid
  has lifted a purse and runs when you come for him, outlined in red (chase him down; hand him to a guard, who
  walks him off; or Roguery). Leave it, and it plays out: the girl is led away, someone gets knocked out or the
  watch breaks it up. Locals set down what they're carrying before they get involved. Step up and the town talks:
  for 60 days prejudice counts for less there, and each notable brings it up once ("Huh. Didn't expect this from
  a frozen drunk."). Hit anyone involved and it becomes your fight. MCM toggle.
- **The welcome feast, in the scene:** a friend's feast is now held where you stand. A table is laid in the square
  (bread, roast fowl, stew, ham, mugs), your companions and best men sit down among the host's people, the host
  toasts you; talk, toast back, leave when you like, and the evening fades out with the feast's effects.
- **Feasts, round two:** a seat is kept for you and you start the evening in it; passers-by are cleared from the
  table and kept clear; nothing else happens in the street meanwhile; everyone at the table has a word for you
  (companions and notables about the evening, locals and soldiers a toast) instead of errands; leaving early or
  walking off costs relation; the sun moves on in the scene; tables only go on walkable ground (no more crop fields).
- **The watch comes:** street events happen out of the guards' sight, but a passing guard (or one, if it drags on)
  breaks it up and questions you — Charm or Honor against the town's prejudice, a bribe, or a fine.
- **The watch's sentence:** fail to talk your way out and it's pay (double if he broke up your fight), go quietly to
  the cells (vanilla captivity), or resist arrest — the nearby guards come for you with weapons, and your only way out
  is to escape the town; every guard you put down adds crime.
- **Bystanders calm down** after a street fight instead of panicking for the rest of the visit.
- **Hecklers can be answered:** stare them down (Valor), buy them a drink (Charm), or settle it with fists.
- **Vanilla persuasion everywhere you argue:** talking a contemptuous notable into giving you work, talking down the
  men around a girl or two brawling youths, explaining yourself to the watch, answering a heckler — all use the game's
  own persuasion (progress bar, skill/trait arguments with their chances, critical success and failure). Arguments land
  better with a listener who shares the virtue, worse with prejudice and cruelty; town standing helps.
- **Setups:** any street event may be bait — the same plea and the same scene, until you get there: the girl walks off
  with a smirk, the brawlers stop and grin, the thief runs to his friends, the man who fell gets up. Toughs close in:
  pay, talk your way out (vanilla persuasion), fight, or run; some just want to beat a foreigner. Nothing gives it away
  (Roguery 60+ may give you a bad feeling, which is sometimes wrong); you can call it — right and they melt away, wrong
  and someone who needed help walks off. Likelier for a resented foreigner and a known soft touch, rarer for a famous
  name and just after one. MCM: base chance (15%).
- **"My brother fell!":** a man down in the street — set his leg (Medicine), help him up, or leave him to it.
- **Your name, shown and worth something:** town standing now has bands — honored, respected, known, a stranger,
  disliked, despised — announced when you walk in and when you cross one. Shops' markup (×0.8 to ×1.25), the watch's
  fines (×0.5 to ×1.5) and the magistrate's sentences (−2 to +2 days) follow it; an honored (or despised) name reaches
  the town's lord every week.
- **Village trouble:** a boundary dispute to judge (the old law, a split, a tenth of the crop — or sell your judgment),
  a harvest to bring in before the storm (come back with sacks of the crop), looters driving off the flock (a real fight
  with weapons). Towns keep the watch, the moneylenders and the crooked guard.
- **Castle life:** a master-at-arms (bouts with the garrison and wagers on them, training your companions, drilling your
  own garrison) and a castellan (news from the roads, veterans the lord can spare, your own castle's report); around
  them a sergeant drilling recruits, prisoners walked to the cells, dice by the wall, couriers with news.
- **Court in the lord's hall:** petitioners come before the lord — speak for them (vanilla persuasion) or back the lord;
  in your own hall they come to you, and your rulings move loyalty, security and your name. Lords in the hall gossip about
  wars, prisoners, armies — and you.
- **Camp (Ctrl+T):** Warband's "take a walk around" — make camp on the map, on that spot's battle terrain: tents, fires,
  your companions and men. Talk to them: a report, a cask of wine, a war story (Leadership), a bout, a drill. Tab breaks
  camp.
- **Test:** choose which street event "Street event now" stages.
- **Word travels:** standing spreads, at a quarter, to nearby towns and castles of the same people.
- **Lords talk:** enemies slander you at court and friends speak up for you (weekly); ask a lord what the court says of you;
  take hired swords' sender to their liege for judgment (a fine, half to you); or pay a gang leader to have an enemy of yours
  roughed up — and hope they never learn who paid.
- **Dice in taverns:** play any patron for 10, 50 or 100 — or bring your own dice (Roguery).
- **Hired swords:** a friend (or your own ear) may warn you of a price on your head; they only hunt within reach of whoever
  paid.
- **More street events:** a moneylender's collectors about to break a debtor's hands (pay, talk, fight — the gang boss
  notices); a mother whose little one has wandered off (look for him yourself, bring him back); one of the watch
  "fining" someone for nothing (pay, shame him, or put him on the ground — and he'll know your face).
- **Running from toughs:** they come after you — caught, it's a beating; get clear or reach the watch and they give up,
  but the street saw you run.
- **Street sentences:** the cells keep you until your time is served, even when you're not at war with the town's realm
  (a night for going quietly; days more for your crime rating and every guard you downed or killed). Kill three of the
  watch (MCM) and it's the headsman — unless your house is great enough to pay a blood price instead.
- **Hired swords:** a lord or notable who hates you (−25; no Honor or Mercy) may pay sellswords of their culture, as
  good as your clan tier, to find you in the street and beat you — or at −50 to carry you off to them; a cunning lord
  at war with you may pay to have you brought in. They walk up and say who sent them: fight them (weapons out), outbid
  them, or give in. Lose and it's a beating and a lighter purse, or you ride out of town bound and face whoever paid —
  held for ransom, humiliated, or, if they're cruel enough, a hard plea for your life (vanilla persuasion) before your
  execution (MCM toggle). Avoid the towns and they come for you on the road.
- **Street life:** quarrels, a preacher, drunks, the watch moving a beggar along, a northern dance — small scenes that
  play out around you without asking anything of you.
- **Someone to see you:** waiting in a town or village, a friend with a feast (or a warning, thanks, a petition) comes
  looking for you — meet them and they come straight to you in the square.
- **Dinner in the lord's hall:** a lord who's a genuine friend may ask you to stay and dine, at the hall's own tables.
- Feasts: a few guests eat (ham leg), most drink (a mug, the tavern drinking idle), some just talk — one fixed thing
  per seat instead of vanilla's coin-flip that left half the table empty-handed.
- Feasts: you sit beside the host; guests hold a ham leg or a mug (vanilla's props for those animations); the
  townsfolk around the table go about their business elsewhere while it's dark.
- People you helped in the street drop their prejudice for the rest of the visit.
- **Busier streets:** twice vanilla's ordinary townsfolk and villagers by default (MCM → General, 50–400%).
- **Notables send a runner:** asked to show you the way, a notable calls over someone nearby to take you, instead
  of walking you there themselves.
- **Debug options** (MCM → Testing): player immortal (hit, never down) and your damage (%) — both work without
  Test mode; "Feast now" and "Street event now" buttons.
- **Test mode** (MCM → Testing): no looters, no cooldowns, checks roll for real with the odds shown, `[Test]` conversation lines
  (relation, town standing, disposition breakdown) and buttons for gold, healing, renown, relations with everyone
  in the settlement, town standing, discovering the settlement, triggering a street event and clearing cooldowns.
- MCM "Notables" group: master toggle, foreigner prejudice %, favor cooldown %, arrival scenes, treachery,
  ambushes; Debug: disposition breakdown on screen.

### Changed
- **Playtest round, 2026-10-01** (see `BACKLOG.md`):
  - Streets: the lost child is far off, unmarked, heard only when close; toughs sprint you down; after looters the whole
    village calms and the one who fetched you comes back; "my men will see to it" is a real fight of 5+ of your troops
    (real dead and wounded; losing costs morale, relations and hearths); the harvest is a short cutscene (you swing a
    scythe, the light moves on 4 hours); separate thanks for paying a debt vs fighting it off.
  - The cells: vanilla escape and ransom work while serving; a sentence costs relation with the owning clan. The
    headsman: nobles (vassals or clan tier 3+) pay a blood price; for commoners the town's owner decides — cruel: the
    headsman; neither: blood money (2000 a guard) or the headsman; merciful or a friend: double time, a flogging and
    banishment. The captain warns you first.
  - Hired swords: buying them off costs a real share of your purse (more the more they hate you; some won't sell);
    paid off, they walk well away; the lord's face follows the judgment; their road band is left alone by other
    parties and never sent while you're in an army.
  - Prices follow town standing on whatever price model the game actually uses (up to ±15% on goods you'd keep, ~4% on
    trade goods). The tavern crowd setting no longer fills taverns. The tavern keeper serves you by your standing: a
    round for the house, news for sale, a room for the night — or the door.
  - Castles: the master-at-arms waits in the yard with three fighters of rising rank; fists or wooden weapons; beat all
    three and the yard won't take your wagers for a week. The castellan answers more (the lord's whereabouts, prisoners,
    bandits, the castle; in your own: double the watch, lay in stores). Garrison life uses soldiers already in the yard.
  - Hall court waits while a feast is on.
  - Camp: a Camp button on the map bar; once a day; a fuller report; **a taste of home** (troops like their own
    culture's food — a morale line); the cask once per camp, worth more after a battle; at most three bouts; the war
    story is a persuasion with the men gathered round; the drill is a cutscene (morale up after a quiet week, down
    otherwise).
  - Feasts: guards stand clear; single chairs, nobody in the table; eaters eat, drinkers drink; a maid with a jug,
    musicians and dancers by the host's standing; walking out early (or tabbing out) offends the host. Lords' halls: up
    to three tables — nearby lords, the notables, companions and the best soldiers; you sit two down from the host; the
    hall feasts on after you leave the table.
  - Arrivals: gifts are rare and small, and the giver wants something back later; "Meet them" starts the conversation
    at once.
- Settlement menu portraits: Talk/Visit need you to have met the person *and* to know the way to where they
  are (tavern, lord's hall, dungeon). Lords are now checked too (they weren't before), and names prefixed by
  title mods (e.g. Titles) are matched.
- Only heroes who plausibly know a place show you the way: notables in their own settlement; companions and
  lords only where it's their home, birthplace, governorship or fief.
- Notables' directions follow disposition (refuse, ask a bribe, help) instead of always helping.
- Townsfolk are warmer where your town standing is good, colder where it's bad.
- Prejudice is milder: a low-tier foreigner starts Wary with most notables, not in open contempt; cruel notables
  and rival cultures still despise you.

### Fixed
- Leaving a settlement after a served street sentence crashed the game (the town's menu was still open behind the
  release).
- Armed street fights couldn't hurt you at all ("immortal" blocks all damage in this game version) — or, with another
  mod resetting it, knocked you out with nothing following. You're now hurt but kept on your feet until beaten down.
- Lord's-hall feasts could seat people (and you) on a chair on another floor — a gallery, a stair, a cellar — and a seat
  that failed left you standing wherever the chair was. Only chairs on the host's floor now, and you go back to where
  you stood if the chair won't have you.
- Tab was refused during street fistfights (vanilla's fight handler); you can now run (it costs standing). Resisting
  arrest, Tab always works — whether you get away depends on how close the guards are.
- The watch stepped in before any fight had started; now only once your fight has begun.
- People carrying things kept their carrying pose after setting the load down.
- Feast guests mimed eating and drinking with empty hands.
- Handing a caught thief to the guard crashed the game (the thief was removed while vanilla was still closing the
  conversation with him). Choices now take effect once the conversation has fully closed.
- "Put in a word for me with..." listed every notable under the same name; each option now shows its own name and
  trade.
- Notables asked to show you the way froze on the spot (they're pinned to fixed positions); they now send a runner,
  and a stuck escort is released after a few seconds.
- Notables who had insulted you still said "Good to meet you" / "Fight well!" on parting.
- Menu options gated before discovery stayed disabled after discovering the location, and leaked into other
  settlements, until reload (since 1.6.0).
- Another dialogue mod (Dramalord) overwrote the notables' short-absence greeting; LMMI's conversation patches
  now run last.

## [1.5.0] - 2026-04-15

### Added
- **"Show me around" escort dialogs**: Ask any townsperson (or notable) in town center to escort you to key places — vanilla-style physical walk to the destination.
- **Escort arrival / en-route intercept dialogs** (priority 200) to replace buggy vanilla escort dialogs.
- **Dynamic discovery system.** Town sub-menus (tavern, arena, smithy, backstreet,
  keep, trade) now unlock individually the first time you actually walk through
  those locations or speak to the right notable/occupation inside the town.
  Paying the guide is still supported as a shortcut, but no longer required.
  - `Constants/SettlementMenuOptions.cs` now carries an `OptionFeatureMap` and a
    `LocationFeatureMap` so menu options and scene IDs resolve to discrete
    feature buckets (Keep / Arena / Backstreet / Trade / Smithy).
  - `DisableMenuBehavior` tracks `settlementsFeaturesAccessed` per-settlement and
    exposes `HasFeatureAccess`, `UnlockFeature`, `TryDiscoverFromLocation`, and
    `TryDiscoverFromConversationPartner`.
  - A silent discovery dialog hooks `start` at priority 1000 so any conversation
    with a tavernkeeper, gang leader, merchant, blacksmith, or weaponsmith
    discovers the relevant feature without interrupting normal dialog flow.
  - The Harmony `DisableSpecificMenuOptionsPatch` is now per-option: only
    features you haven't discovered yet get gated and tagged with the new
    `{=lmmi_undiscovered}You haven't discovered this part of the settlement
    yet.` tooltip.
  - `CustomSettlementAccessModel` routes `Trade` and `Craft` through
    `HasFeatureAccess` so the encyclopedia/menu badges agree with the new model.

- **File-based logging (`LmmiLog`).** New thread-safe logger writes to
  `Documents\Mount and Blade II Bannerlord\Logs\lmmi.log` with four levels
  (Info / Warning / Error / Debug). Rotates at 1 MB (keeps one `.old` copy),
  swallows all IO errors so it can never crash the game, and exposes a
  `VerboseLoggingQuery` delegate so `Debug` output is gated by the user's
  MCM setting without the logger taking a compile-time dependency on MCM.

- **Per-feature ALT overlay**: X-ray nameplates now unlock per-location instead of all-or-nothing; discovering the tavern shows tavern highlights immediately.
- **Per-feature location gating**: Settlement sub-location tooltips now reflect individual feature discovery rather than full settlement access.

- **Persistent citizen refusals** (per-NPC, per-settlement visit): if an NPC refuses (TooBusy / Foreigner / AlreadyHelped), later conversations repeat the exact same refusal line and immediately close.
- **Night-time closure handling** for closed destinations (marketplace/smithy/arena/barber): refuses at night instead of starting escort.

- **MCM integration as a soft dependency.** New `LmmiSettings` (MCMv5
  `AttributeGlobalSettings`) exposes:
  - `EnableDynamicDiscovery` (bool, default true)
  - `ShowDiscoveryMessages` (bool, default true)
  - `GuideCostBase` (int 0-5000, default 500) — scales with clan tier
  - `VillageArrangementCost` (int 0-2000, default 200)
  - `FullAccessClanTier` (int 0-6, default 5) — auto-unlock at/above this tier
  - `KingdomAccessClanTier` (int 0-6, default 3) — auto-unlock in kingdom
    settlements at/above this tier
  - `VerboseLogging` (bool, default false)
  - A `LmmiSettingsProvider` shim uses AppDomain assembly scanning plus
    `[MethodImpl(NoInlining)]` JIT isolation so the mod runs cleanly whether
    or not Bannerlord.MCM is installed. Compile-time the package is pulled in
    with `IncludeAssets="compile"`, so MCMv5.dll is never bundled in the mod.
  - `SubModule.xml` declares `Bannerlord.MBOptionScreen` with
    `optional="true"` in the BUTR community dependency metadata.

- **Version bump to 1.5.0** in `LessMenusMoreImmersion.csproj`; `SubModule`
  now reads its version via reflection and logs it at load.

### Changed
- Guide cost, village arrangement cost, and auto-unlock clan tiers are now
  driven by MCM settings (with hard-coded fallbacks when MCM is absent).
- `DisableMenuBehavior.HasAccessToSettlement` no longer hard-codes the
  `Tier >= 5` / `Tier >= 3` thresholds — it reads them from
  `LmmiSettingsProvider`.
- Tooltips on gated options now use the `{=lmmi_undiscovered}` key so the new
  "you haven't discovered this yet" phrasing is consistent across menus and
  the access model.

- Vanilla **"I'm looking for someone."** is no longer available as a top-level option in town-center civilian conversations; it is reachable only through the mod's directions dialog flow.
- The injected **"I'm looking for someone..."** option is routed into the vanilla `player_ask_hero_location` flow (not `hero_main_options`) to avoid clan/hero condition evaluation in civilian conversations.
- Tavern / backstreet directions option restored as a single destination, without reintroducing alley/waterfront/clearing directions options.
- Backstreet discovery no longer auto-unlocks Alley/Waterfront/Clearing.

### Fixed
- **Crash**: `System.NullReferenceException` in `SandBox.CampaignBehaviors.ClanMemberRolesCampaignBehavior.clan_member_dont_follow_me_on_condition()` caused by routing civilian conversations into `hero_main_options`.
- Vanilla street opener (e.g. `town_or_village_start_postrumor`) no longer appears after a cached refusal; the refusal line is repeated and conversation closes.
- **Latent NullReferenceException** in
  `CustomRecruitmentMenuBehavior.ShouldAutoUnlockRecruitment`: the method
  dereferenced `Clan.PlayerClan` without a null check. It now guards both
  the settlement and the player clan.
- `DisableMenuBehavior.OnGameStarted` now tolerates a missing `local_guide`
  NPC: it logs a warning and skips dialog registration instead of throwing.
- `CustomVillageMenuBehavior.VillageBuyGoodsCondition` now null-checks
  `CurrentSettlement` and `.Village` before dereferencing them.
- `CustomVillageMenuBehavior.RemoveMenuOption` replaces the old "popup on
  failure" with a proper `LmmiLog.Warning` so a Bannerlord API change doesn't
  spam players with on-screen error messages.

### Hardened (error handling / logging)
Every behavior and patch that previously swallowed exceptions silently now
routes through `LmmiLog`:
- `CustomVillageMenuBehavior` — wraps `OnSessionLaunched`, condition,
  consequence, and reflection-based menu removal.
- `CustomRecruitmentMenuBehavior` — wraps dialog registration, recruitment
  arrangement accept consequence, cost calculation, bulk recruitment handling,
  bulk recruitment execution (with slot-index bounds check), recruit
  enumeration, and bulk cost calculation.
- `PortraitActionBlocker` — `Postfix`, `GetHeroFromWidget`, `HasPlayerMetHero`,
  and the margin apply/reset paths now log instead of silently failing.
- `AltOverlayBlockerBehavior` / `NameMarkerViewPatch` — `ShouldBlockHighlighting`
  and the reflection-based `TargetMethod` lookup now log on failure.
- `DisableMenuBehavior` — `UnlockSettlementAccess`, feature unlock, dialog
  registration, and guide cost calculation all log their outcomes.

### Developer notes
- New folders: `Logging/` (contains `LmmiLog.cs`) and `Settings/` (contains
  `LmmiSettings.cs` and `LmmiSettingsProvider.cs`).
- Bannerlord.MCM 5.11.3 is pulled from nuget.org. The BUTR source is not
  required for this build.
- The mod keeps building against both `net472` and `net6` as before.

## [1.6.0] - 2026-08-31

### Changed
- Updated for Bannerlord 1.5 beta compatibility.

### Fixed
- **Harbor/Port locked with Warsails DLC**: The mod's access model called the base game's `CanMainHeroAccessLocation` for ports, which always returns "Door is locked!" — bypassing Warsails' own port access. Ports now return accessible directly instead of delegating to base. (Thanks to lofflof for the report and fix.)
- **Only one recruit per notable**: `MaximumIndexHeroCanRecruitFromHero` was called with `-1` instead of `-101`, limiting recruitment to a single troop per notable interaction. (Thanks to lofflof for the report and fix.)

## [1.5.1] - 2026-04-24

### Fixed
- **Village trader in-person trade**: the "Very well, let's trade" conversation now actually opens the village trade screen.
- **Local guide dialog restored**: local guide now uses the existing localized dialog strings from `_Module/ModuleData/Languages/std_module_strings_xml.xml` instead of falling back to mercenary-style tavern talk.
- **Unlock message localization**: fixed the settlement-name placeholder so the "You now know your way around …" message substitutes the town name correctly.
- **Missing translations**: added all `{=...}` string keys referenced by the mod’s dialogs/tooltips to `std_module_strings_xml.xml`.
- **Local guide no longer treated as mercenary**: changed the `local_guide` NPC occupation from `CaravanGuard` to `Townsfolk`.

### Changed
- Village trading arrangement cost now respects `LmmiSettingsProvider.VillageArrangementCost` (MCM setting) instead of scaling with clan tier.
