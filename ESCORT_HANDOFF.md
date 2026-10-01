# Escort System Implementation — Handoff Document

## Goal

When a player asks a townsperson "show me the way to [tavern/smithy/keep/marketplace]", the NPC must **physically walk them there** — identical to the vanilla "take me to notable X" escort. The feature (e.g. Backstreet, Trade, Smithy, Keep) unlocks ONLY when the player reaches the destination, NOT instantly on dialog.

## Current State (2026-04-13)

The dialog system works. The deferred-setup pipeline works. The passage lookup works. **The NPC does not move.** Every approach to make the NPC walk has failed.

---

## What Works

### Dialog System (fully working)
- Player option "Can you show me the way somewhere?" appears in vanilla `town_or_village_player` hub (alongside "I'm looking for someone")
- Also in `hero_main_options` hub for notables
- NPC responds "Where would you like to go?" with choices: tavern, marketplace, smithy, lord's hall, never mind
- Each choice hidden once already discovered
- Dialog consequence calls `BeginEscort()` which saves state and sets `_escortPending = true`
- File: `DisableMenuBehavior.cs`, method `AddTownDirectionsDialogs()`, ~line 790

### Deferred Setup (fully working)
- `BeginEscort()` saves: `_escortNpcCharacter`, `_escortTargetLocationId`, `_escortFeature`, `_escortSettlement`, `_escortPending = true`
- `OnMissionTick()` waits for `Mission.Current.Mode != MissionMode.Conversation`, then calls `PerformEscortSetup()`
- Confirmed by logs: conversation end is detected correctly, setup runs after dialog closes

### Passage Lookup (fully working)
- `MissionAgentHandler.TownPassageProps` contains `List<UsableMachine>` — these are `SandBox.Objects.Usables.Passage` objects
- `Passage.ToLocation.StringId` matches location IDs ("tavern", "lordshall", etc.)
- `Passage.PilotStandingPoint.GameEntity.GetGlobalFrame().origin` gives the door position
- `MissionAgentHandler.DisabledPassages` also checked (for undiscovered locations)
- **Smithy has no passage** in the scenes tested (Lycaron, Phycaon, Danustica) — falls back to immediate unlock
- File: `DisableMenuBehavior.cs`, method `FindPassageForLocation()`, ~line 977

### NPC Agent Resolution (working)
- `CharacterObject.OneToOneConversationCharacter` saved during dialog
- After conversation ends, agent found via `Mission.Current.Agents.FirstOrDefault(a => a.IsActive() && a.Character == savedCharacter)`
- This succeeds — the agent is found

### Feature Unlock System (working)
- `UnlockFeature(settlement, feature)` adds feature to `settlementsFeaturesAccessed` dictionary
- `HasFeatureAccess()` checks this dictionary
- `SyncData()` persists across saves
- Discovery messages shown via `InformationManager.DisplayMessage`
- Menu options gated via Harmony patch on `GameMenu.AddOption`
- Location access gated via `CustomSettlementAccessModel.CanMainHeroAccessLocation`

---

## What Failed — NPC Movement

### Attempt 1: EscortAgentBehavior (vanilla escort class)
**Approach:** Replicate the vanilla `AddEscortAgentBehavior` pattern:
```csharp
var navigator = npcAgent.GetComponent<CampaignAgentComponent>().AgentNavigator;
var group = navigator.GetBehaviorGroup<InterruptingBehaviorGroup>();
var escort = group.AddBehavior<EscortAgentBehavior>();
group.SetScriptedBehavior<EscortAgentBehavior>();
escort.Initialize(Agent.Main, (UsableMachine)passage, OnTargetReached);
```
**Result:** NPC did not move. Behavior was added but never became the active behavior.
**Why:** The `InterruptingBehaviorGroup` never took priority over `DailyBehaviorGroup`. After setup, `navigator.GetActiveBehavior()` returned `null` and `GetActiveBehaviorGroup()` stayed as `DailyBehaviorGroup`.

### Attempt 2: EscortAgentBehavior + ActivateGroup (private, via reflection)
**Approach:** Same as above, plus:
```csharp
var activateMethod = navigator.GetType().GetMethod("ActivateGroup", BindingFlags.NonPublic | BindingFlags.Instance);
activateMethod.Invoke(navigator, new object[] { group });
navigator.ForceThink(0f);
```
**Result:** NPC did not move. After `ActivateGroup` + `ForceThink`, `GetActiveBehavior()` still returned `null`.
**Log output:**
```
PrepareAgent: before — behavior=WalkingBehavior, group=DailyBehaviorGroup, usingObj=False
SetupEscort: after — behavior=null, target=passage 'tavern'.
```

### Attempt 3: navigator.SetTarget() every tick
**Approach:** Every tick in `OnMissionTick`:
```csharp
navigator.SetTarget(passage, false, Agent.AIScriptedFrameFlags.None);
```
**Result:** NPC did not move. The behavior system's `WalkingBehavior` in `DailyBehaviorGroup` overrides the target every frame during `AgentNavigator.Tick()`.

### Attempt 4: Agent.SetScriptedPosition every tick
**Approach:** Bypass navigator entirely:
```csharp
var wp = new WorldPosition(Mission.Current.Scene, _escortTargetPos);
_escortAgent.SetScriptedPosition(ref wp, false, Agent.AIScriptedFrameFlags.GoToPosition);
```
**Result:** NOT YET TESTED as of handoff. This is the current code deployed.

### Attempt 5 (not tried): StopUsingGameObject + DisableScriptedMovement first
The NPC might have a scripted position already set (from their spawn point / standing point). Calling `DisableScriptedMovement()` before `SetScriptedPosition()` might be needed.

### Attempt 6 (not tried): Remove/disable the DailyBehaviorGroup
```csharp
navigator.RemoveBehaviorGroup<DailyBehaviorGroup>();
```
Then the navigator has no behavior group to override our movement commands.

### Attempt 7 (not tried): Agent.SetMovementDirection + Agent.SetMaximumSpeedLimit
Lower-level movement control. Set direction vector toward target each frame.

### Attempt 8 (not tried): Decompile vanilla "take me to notable" flow
The vanilla "take me to [notable name]" escort DOES work. Decompiling the exact code path that handles it would reveal what's different. Look in `SandBox` for the dialog consequence that fires when you select "take me to X" — it likely calls `EscortAgentBehavior.AddEscortAgentBehavior` but may also do additional setup we're missing.

---

## Key Types and Where to Find Them

### SandBox.dll (`Modules\SandBox\bin\Win64_Shipping_Client\SandBox.dll`)
| Type | Purpose |
|------|---------|
| `SandBox.Objects.Usables.Passage` | Passage door UsableMachine. Has `ToLocation` property (→ Location). Extends `UsableMachine`. |
| `SandBox.Missions.MissionLogics.MissionAgentHandler` | Has `TownPassageProps` (List\<UsableMachine\>) and `DisabledPassages`. Get via `Mission.Current.GetMissionBehavior<MissionAgentHandler>()`. |
| `SandBox.Missions.AgentBehaviors.EscortAgentBehavior` | Vanilla escort behavior. Has `Initialize(Agent, UsableMachine, OnTargetReachedDelegate)` overload. Static `AddEscortAgentBehavior(Agent ownerAgent, Agent targetAgent, callback)`. |
| `SandBox.Missions.AgentBehaviors.InterruptingBehaviorGroup` | Behavior group that should interrupt other groups. Has `AddBehavior<T>()`, `SetScriptedBehavior<T>()`. |
| `SandBox.AgentNavigator` | Attached to agents via `CampaignAgentComponent.AgentNavigator`. Has `SetTarget()`, `ClearTarget()`, `ForceThink()`, `GetActiveBehavior()`, `GetActiveBehaviorGroup()`, `ActivateGroup()` (PRIVATE). |
| `SandBox.Missions.AgentBehaviors.ChangeLocationBehavior` | How NPCs walk to passage doors. Has `SelectADoor()` → `Passage`, `_selectedDoor` field. Uses `Navigator.SetTarget(door, false, 0)` in its `Tick()`. |

### TaleWorlds.MountAndBlade.dll
| Type | Purpose |
|------|---------|
| `Agent` | Game agent. Has `SetScriptedPosition()`, `DisableScriptedMovement()`, `IsUsingGameObject`, `StopUsingGameObject()`, `Position`, `IsActive()`. |
| `Agent.AIScriptedFrameFlags` | Enum: None, GoToPosition, NoAttack, ConsiderRotation, NeverSlowDown, DoNotRun, InConversation, etc. |
| `WorldPosition` | Constructor: `WorldPosition(Scene, Vec3)`. Used with `SetScriptedPosition`. |
| `UsableMachine` | Base class for Passage and other interactables. |

### TaleWorlds.CampaignSystem.dll
| Type | Purpose |
|------|---------|
| `CampaignAgentComponent` | Agent component with `AgentNavigator` property (returns `SandBox.AgentNavigator`). |
| `CampaignMission.Current.Location` | Current location. `StringId` = "center", "tavern", etc. |

---

## What to Decompile Next

1. **The vanilla "take me to notable" dialog consequence** — Find the dialog in SandBox that handles "take me to [notable name]". Search for `EscortAgentBehavior.AddEscortAgentBehavior` call sites. The consequence likely does more than just add the behavior — there may be additional state changes needed.

2. **`AgentNavigator.HandleBehaviorGroups()`** — This is what decides which behavior group is active each tick. Understanding this reveals why `InterruptingBehaviorGroup` never takes priority.

3. **`InterruptingBehaviorGroup`** — Its `Tick()` and any override logic. How does it decide whether to interrupt the current group?

4. **`DailyBehaviorGroup`** — Its `Tick()` and `WalkingBehavior`. What does it do with the navigator target each frame?

5. **`ChangeLocationBehavior.Tick()`** — This successfully makes NPCs walk to passage doors. What does it do differently from our approach?

---

## File Structure

```
C:\mods\LessMenusMoreImmersion\
  Behaviors\
    DisableMenuBehavior.cs      -- Main file. Dialog, escort, discovery, menu gating.
    AltOverlayBlockerBehavior.cs -- ALT highlight filtering (separate issue)
    CustomRecruitmentBehavior.cs -- Notable recruitment system
    CustomVillageMenuBehavior.cs -- Village trade menu
    PortraitActionBlocker.cs     -- Portrait click blocking
  Constants\
    SettlementMenuOptions.cs     -- Feature names, LocationFeatureMap, OptionFeatureMap
  Logging\
    LmmiLog.cs                   -- Log to Documents\Mount and Blade II Bannerlord\Logs\lmmi.log
  Models\
    CustomSettlementAccessModel.cs -- Location/action access gating
  Settings\
    LmmiSettingsProvider.cs      -- MCM Fluent Builder settings
  SubModule.cs                   -- Entry point
  _Module\SubModule.xml          -- Module definition
```

## BL Location StringIds
- `"center"` = town center (NOT "town_center")
- `"tavern"` = tavern
- `"lordshall"` = lord's hall / keep
- `"smithy"` = smithy (may not have a passage in all towns)
- `"arena"` = arena
- `"prison"` = prison

## Key Observations
- Townsfolk NPCs use `Occupation.Townsfolk` (NOT `Occupation.Wanderer`)
- Townsfolk agents have `DailyBehaviorGroup` with `WalkingBehavior` as their default
- `Location.LocationsOfPassages` gives passage indices (tavern=0, arena=1, lordshall=2, etc.)
- Scene entities are NOT tagged with `sp_passage_N` — passages are `Passage` objects found via `MissionAgentHandler.TownPassageProps`
- `CustomSettlementAccessModel.CanMainHeroAccessLocation` passes everything through when `Mission.Current.Scene != null` (player is physically in the settlement) — physical doors are always usable, the discovery system only gates MENU shortcuts
- The NPC's `WalkingBehavior` in `DailyBehaviorGroup` actively controls their movement every frame, overriding external SetTarget/SetScriptedPosition calls

## Game Version
- Bannerlord 1.3.15
- .NET Framework 4.7.2
- Harmony 2.4.2
- MCM 5.11.3 (Fluent Builder API, compile-only reference)
