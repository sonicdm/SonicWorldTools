# SonicWorldTools — Precision Area Pipette & Selection Refinement Build Plan

**Project status:** Planning  
**Primary implementation target:** Precision Area Pipette  
**Secondary implementation target:** Selection Refinement  
**Audience:** Cursor / developer implementation  
**Platform:** Valheim + BepInEx 5  
**Primary integration:** Infinity Hammer + World Edit Commands  
**Out of scope:** Dismantling (handled by Dismantleheim), zooping, terrain editing, precision rotation, build camera, blueprint systems already covered by existing mods

---

## 1. Project Goal

SonicWorldTools is a small, compatibility-first Valheim building utility mod that fills gaps left by Infinity Hammer and World Edit Commands without replacing them.

The first release should solve one specific problem extremely well:

> Select an exact subsection of a structure — especially a multi-story structure — preview exactly what is included, refine the selection, then hand that selection into an Infinity Hammer-style copy / pipette placement workflow.

The mod must avoid duplicating features already provided adequately by Infinity Hammer, World Edit Commands, DragNBuild, Build Camera, precision rotation mods, and Dismantleheim.

---

## 2. Core Design Principles

### Compatibility first

Preferred integration order:

1. Public commands / stable command behavior
2. Extra Infinity Hammer YAML tools
3. Public/shared classes exposed by dependencies
4. Small, isolated Harmony patches against Valheim
5. Patches against another mod only as a last resort

If a feature can be achieved with a World Edit Commands command or Infinity Hammer YAML entry, use that instead of reimplementing it.

### One physical Hammer

If Infinity Hammer is installed, SonicWorldTools tools should appear in its existing Hammer menus. The player should not need a separate custom Hammer item, multiple Hammer variants in inventory, or a second build-menu system.

The plugin should automatically place its own tool definitions into:

```text
BepInEx/config/tools/infinity_tools_sonicworldtools.yaml
```

Do not modify Infinity Hammer's default `infinity_tools.yaml`.

### Modular features

Initial modules:

```text
SonicWorldTools.dll
├── Core
│   ├── DependencyDetection
│   ├── ToolInstaller
│   ├── SelectionEngine
│   ├── PreviewRenderer
│   └── Config
├── PrecisionAreaPipette
└── SelectionRefinement
```

Each module must be independently configurable.

---

## 3. Existing Functionality We Must Reuse

### Infinity Hammer

Infinity Hammer already provides:

- Hammer-based custom tools
- YAML tool definitions
- `infinity_tools*.yaml` loading
- hot-reload of tool YAML
- area ruler UI
- width / depth / height values
- area highlighting
- object ghosting
- pipette-style copying
- building pipette
- area pipette
- `pick`
- `freeze`
- point capture placeholders
- build-menu integration
- placement preview
- normal placement confirmation
- existing snapping behavior

Do not recreate these unless required for the new selection workflow.

Current source confirms:

- `ToolManager` loads any `infinity_tools*.yaml`
- additional files are additive
- duplicate tool names should be avoided
- tools support `<w>`, `<d>`, `<h>`, `<x>`, `<y>`, `<z>`
- tools support captured points such as `<p1>`, `<p2>`, etc.
- Area Pipette currently uses:

```yaml
command: hammer pick=<mod1> freeze=<mod2> height=<h> <area>
```

- `<area>` expands into center/radius/rectangle parameters rather than automatically deriving a box from two arbitrary opposite corners

### World Edit Commands

World Edit Commands already supports rectangular and height-limited selection parameters:

```text
from=<x>,<z>,<y>
rect=<width>,<depth>
angle=<angle>
height=<height>
id=<include>
ignore=<ignore>
```

The selector already supports nearby-object queries for rectangular volumes and vertical limits. Do not build a second generic world-query language.

---

## 4. Feature 1 — Precision Area Pipette

### User problem

Infinity Hammer's Area Pipette is useful, but its ruler-centered area selection is awkward for precise work on an existing building.

Typical failure case:

- player wants the second floor of a building,
- current area spans floors above or below,
- adjusting width/depth/height manually is tedious,
- exact building boundaries are difficult to line up,
- selection cannot easily be refined piece-by-piece.

The Precision Area Pipette should make this spatially explicit.

---

## 5. Primary UX — Two-Corner Volume Selection

### Entry point

Infinity Hammer Tools menu entry:

```text
Precision Area Pipette
```

If there is a clean way to expose it alongside the built-in pipettes without patch fragility, that can be added later. Initial implementation should target the normal Infinity Hammer Tools menu.

### Selection flow

1. **Activate tool** — selecting Precision Area Pipette activates SonicWorldTools selection mode. Nothing is modified.
2. **Select first corner** — left-click sets Corner A and shows a visible marker.
3. **Select opposite corner** — moving the cursor shows a live box; left-click sets Corner B.
4. **Candidate selection** — objects whose canonical positions fall inside the volume are highlighted.
5. **Refine** — add/exclude individual pieces and optionally resize/reset corners.
6. **Pick up selection** — hand the exact refined object set into Infinity Hammer's normal placement ghost workflow.

HUD example:

```text
Precision Area Pipette
42 objects selected
7.0m × 10.0m × 3.0m
```

Once hand-off succeeds, Infinity Hammer owns ghost rendering, placement, freezing, rotation, snapping, and final placement.

---

## 6. Selection Semantics

### Coordinate model

The box is defined by two world-space points:

```text
A = (x1, y1, z1)
B = (x2, y2, z2)
```

Normalize:

```text
minX = min(x1, x2)
maxX = max(x1, x2)
minY = min(y1, y2)
maxY = max(y1, y2)
minZ = min(z1, z2)
maxZ = max(z1, z2)
```

MVP uses an axis-aligned rectangular prism. Rotated boxes are future scope.

### Inclusion rule

MVP: an object is selected if its canonical world position lies inside the normalized volume.

Use the same position source used by World Edit Commands / Infinity Hammer where possible, ideally ZDO position for networked objects.

Do not initially attempt full collider-volume intersection.

Future optional setting:

```text
SelectionIntersectionMode = Origin | Collider
```

Default: `Origin`.

### Object classes

Initial target should mimic what Infinity Hammer's current area pipette can already copy. Do not broaden selection to arbitrary creatures, vegetation, or unrelated world objects unless Infinity Hammer already supports them in the same workflow.

---

## 7. Selection Refinement Module

Selection refinement should be reusable infrastructure, not pipette-specific hacks.

### State model

```csharp
HashSet<ZDOID> BaseSelection
HashSet<ZDOID> AddedSelection
HashSet<ZDOID> ExcludedSelection
```

Effective selection:

```text
(BaseSelection ∪ AddedSelection) - ExcludedSelection
```

Do not store only scene-object references because objects can unload/reload. Use stable network identifiers where practical.

### Remove individual piece

When refinement mode is active, targeting a selected object and triggering subtract moves it into `ExcludedSelection`, immediately updates highlight, and updates count.

### Add individual piece

Targeting an unselected valid object and triggering add moves it into `AddedSelection`. It may be outside the original box; that is intentional.

### Recompute behavior

Changing Corner A or B recomputes `BaseSelection`.

- manual exclusions remain excluded when possible
- manual additions remain included when possible
- explicit `Reset Refinement` clears both manual sets

---

## 8. Preview Requirements

Render:

- Corner A marker
- Corner B marker
- box edges
- selected object highlights
- selected count
- dimensions

Do not use opaque geometry that blocks the building from view.

Suggested visual states:

```text
current volume selection
manual addition
manual exclusion
hovered target
```

The exact selection shown in the preview must be the exact selection handed off for copying. Do not recompute with different rules during final hand-off.

---

## 9. Infinity Hammer Integration

### YAML installation

On startup:

1. Detect Infinity Hammer.
2. Determine expected tools directory.
3. Ensure SonicWorldTools YAML exists.
4. Create/update only the SonicWorldTools-owned file.
5. Never overwrite Infinity Hammer defaults.
6. Never modify unrelated user YAML.

Suggested YAML:

```yaml
hammer:
  - name: Precision Area Pipette
    description: |-
      Select an exact rectangular volume using two corners.
      Refine the selection before copying.
    icon: hammer
    command: sonic_precision_pipette
    instant: true
```

Whether `instant: true` is correct must be validated in-game.

### Generated file ownership/versioning

Include:

```yaml
# Generated by SonicWorldTools
# Template-Version: 1
```

Upgrade logic:

- create if missing
- replace if an untouched generated file is old
- preserve user-modified file
- write a `.new` template when conflict exists

---

## 10. Hand-Off to Infinity Hammer

This is the highest-risk technical item.

We need the cleanest supported way to convert an exact list of selected network objects into Infinity Hammer's normal multi-object placement ghost.

Preferred options, in order:

1. Reuse a public Infinity Hammer selection constructor / command.
2. Invoke an Infinity Hammer command that accepts an exact object set.
3. Build a small compatibility adapter against Infinity Hammer types.
4. Use isolated reflection in `InfinityHammerBridge.cs`.
5. Reimplement multi-object placement only if every reuse path fails.

Do not build a custom placement engine before this investigation is complete.

Cursor should inspect:

```text
ObjectSelection
HammerSelect
ToolSelection
BaseSelection
Selector
```

and document:

1. constructor/API candidate
2. required object type
3. how `pick` removes originals
4. how normal copy differs from pick
5. how ghost placement is activated
6. whether originals can remain in place
7. whether undo is involved
8. whether the path can be used without patching Infinity Hammer internals

---

## 11. Dependency Strategy

Expected dependencies:

- BepInEx
- Server Devcommands
- Infinity Hammer
- World Edit Commands

Validate whether World Edit Commands is already transitively required by the installed Infinity Hammer version. Avoid redundant hard dependencies.

Compatibility targets:

- DragNBuild
- Build Camera
- precision rotation mods
- Dismantleheim

No direct integration should be necessary for MVP.

---

## 12. Input Design

Do not finalize bindings before playtesting.

The tool runs only while Precision Area Pipette is active.

Initial concepts only:

```text
Left Click              Set corner / confirm current corner
Modifier + Left Click   Add individual object
Modifier + Middle Click Exclude individual object
Right Click             Preserve normal Hammer menu behavior
Escape                  Cancel current precision selection
```

Testing must determine:

- which inputs Infinity Hammer consumes
- which inputs vanilla Hammer consumes
- whether DragNBuild intercepts them
- whether refinement actions feel natural

All custom bindings must be configurable.

---

## 13. Configuration

Suggested config:

```ini
[General]
Enabled = true

[PrecisionAreaPipette]
Enabled = true
SelectionMode = Origin
ShowDimensions = true
ShowSelectedCount = true
PersistRefinementsAfterResize = true

[Preview]
ShowBox = true
ShowCornerMarkers = true
ShowHighlights = true

[Integration]
InstallInfinityHammerTools = true

[Debug]
VerboseLogging = false
DryRunSelection = false
```

---

## 14. Logging

Normal logging:

```text
[SonicWorldTools] Infinity Hammer detected.
[SonicWorldTools] Installed Precision Area Pipette tool definition.
```

Verbose logging:

```text
[SonicWorldTools] Corner A = 123.4, 45.6, 789.0
[SonicWorldTools] Corner B = 130.4, 48.6, 799.0
[SonicWorldTools] Base selection = 42
[SonicWorldTools] Added manually = 2
[SonicWorldTools] Excluded manually = 3
[SonicWorldTools] Effective selection = 41
```

Do not spam one line per object unless verbose mode is enabled.

---

## 15. Project Structure

```text
SonicWorldTools/
├── SonicWorldTools.sln
├── src/
│   └── SonicWorldTools/
│       ├── Plugin.cs
│       ├── Config/
│       │   └── PluginConfig.cs
│       ├── Core/
│       │   ├── DependencyDetector.cs
│       │   ├── ToolInstaller.cs
│       │   ├── SelectionState.cs
│       │   ├── SelectionVolume.cs
│       │   └── SelectionQuery.cs
│       ├── Modules/
│       │   └── PrecisionAreaPipette/
│       │       ├── PrecisionAreaPipetteController.cs
│       │       ├── PrecisionAreaPipetteInput.cs
│       │       ├── PrecisionAreaPipettePreview.cs
│       │       └── PrecisionAreaPipetteCommand.cs
│       ├── Refinement/
│       │   ├── SelectionRefinement.cs
│       │   └── RefinementInput.cs
│       ├── Integration/
│       │   ├── InfinityHammerBridge.cs
│       │   └── WorldEditBridge.cs
│       └── Rendering/
│           ├── BoxRenderer.cs
│           └── ObjectHighlightRenderer.cs
├── tests/
│   └── SonicWorldTools.Tests/
│       ├── SelectionVolumeTests.cs
│       ├── SelectionStateTests.cs
│       ├── RefinementTests.cs
│       └── ToolInstallerTests.cs
└── assets/
    └── infinity_tools_sonicworldtools.yaml
```

---

## 16. Offline Tests

### Selection volume

Test:

- A/B ordering does not matter
- negative coordinates
- exact boundary inclusion
- zero-width box
- zero-height box
- very large box
- points just outside each face

### Refinement

Test:

```text
base = A,B,C
exclude B
effective = A,C
```

```text
base = A,B,C
add D
effective = A,B,C,D
```

```text
base recomputes to A,C,E
excluded B remains harmless
added D persists
effective = A,C,D,E
```

### YAML installer

Test:

- first install
- already-current generated file
- old untouched generated file
- user-modified file
- missing tools folder
- duplicate execution
- no Infinity Hammer installed

---

## 17. In-Game Regression Structure

Create a disposable test structure with:

- 3 floors
- walls aligned vertically
- overlapping roof pieces
- furniture
- crafting stations
- portal
- chest
- decorative build pieces
- pieces crossing box edges
- nearby unrelated building

Retain this structure for regression testing.

---

## 18. Acceptance Tests

### Tool installation

1. Install SonicWorldTools with Infinity Hammer.
2. Launch game.
3. Confirm no new physical Hammer item is required.
4. Confirm Precision Area Pipette appears in Hammer tool menu.
5. Confirm Infinity Hammer's normal Pipette, Building Pipette, and Area Pipette still work.

### Corner selection

6. Select Precision Area Pipette.
7. Set Corner A.
8. Confirm marker is visible.
9. Move cursor.
10. Confirm live box preview updates.
11. Set Corner B.
12. Confirm box locks.

### Multi-story precision

13. Box only the second floor of the test building.
14. Confirm first-floor pieces are not selected.
15. Confirm third-floor pieces are not selected.
16. Confirm selected count matches visible highlight.

This is the primary success criterion.

### Refinement

17. Exclude one selected wall.
18. Confirm it leaves the selection.
19. Add one adjacent wall outside the box.
20. Confirm it joins selection.
21. Resize box.
22. Confirm configured refinement persistence behavior.

### Pick-up

23. Trigger pick/copy.
24. Confirm exactly the highlighted objects become the placement ghost.
25. Confirm excluded object is absent.
26. Confirm manually added object is present.
27. Place copied selection elsewhere.
28. Confirm placement uses normal Infinity Hammer controls.

### Compatibility

29. Switch to normal Pipette; Sonic inputs must deactivate.
30. Use DragNBuild; no conflict outside Sonic tool.
31. Use Build Camera; preview remains usable.
32. Use precision rotation on placement ghost; no interference.
33. Use Dismantleheim; no shared state or input leakage.

---

## 19. Performance Requirements

Target selections:

```text
Normal: 1–200 objects
Large:  200–1000 objects
Stress: 1000+
```

Do not perform full-world searches every frame.

Candidate query should be spatially bounded.

Preview may use:

```text
10–20 Hz candidate refresh
60 Hz box visual movement
```

Do not allocate new highlight materials every frame; cache reusable render resources.

---

## 20. Multiplayer

Initial expectation: SonicWorldTools is client-side tooling used by an authorized player.

The resulting placed structures are normal Valheim network objects. Other players should not need SonicWorldTools just to see/use the result.

Do not bypass:

- ward restrictions
- server authority
- ownership constraints
- Infinity Hammer / Server Devcommands permission rules

Test on:

- local world
- dedicated server
- modded client + vanilla client observer

---

## 21. Error Handling

If Infinity Hammer is missing:

```text
[SonicWorldTools] Infinity Hammer not detected. Precision Area Pipette disabled.
```

Do not crash.

If the bridge fails because Infinity Hammer changed:

```text
[SonicWorldTools] Infinity Hammer integration unavailable for this version.
Precision Area Pipette selection is disabled.
```

Do not attempt a destructive fallback.

If one selected object unloads before pick-up, remove it from effective selection, update count, and continue if the remaining selection is valid.

---

## 22. Implementation Milestones

### Milestone 0 — Project skeleton

Deliver:

- solution
- BepInEx plugin loads
- config
- dependency detection
- logging
- test project

Acceptance: Valheim starts with plugin and no errors.

### Milestone 1 — Tool installation

Deliver:

- generated Infinity Hammer YAML
- safe update logic
- tool appears in Hammer menu
- command activates controller

Acceptance: tool can be selected repeatedly without duplicates.

### Milestone 2 — Pure selection engine

Deliver:

- `SelectionVolume`
- corner normalization
- candidate inclusion
- selection state
- automated tests

Acceptance: all offline selection tests pass.

### Milestone 3 — In-game box selection

Deliver:

- Corner A/B input
- live box
- object highlights
- selected count

No copying yet.

Acceptance: second-floor-only selection works reliably.

### Milestone 4 — Selection refinement

Deliver:

- manual add
- manual exclude
- reset refinement
- persistence through resize
- tests

Acceptance: effective selection exactly matches highlights.

### Milestone 5 — Infinity Hammer hand-off

Deliver:

- convert effective selection into Infinity Hammer placement ghost
- retain standard Infinity Hammer placement behavior

Acceptance: exact refined selection can be copied and placed.

This is the highest-risk milestone.

### Milestone 6 — UX hardening

Deliver:

- finalized controls
- dimensions HUD
- cancel/reset flow
- config
- input conflict fixes

Acceptance: workflow is usable without console commands.

### Milestone 7 — Compatibility pass

Test:

- Infinity Hammer
- World Edit Commands
- DragNBuild
- Build Camera
- precision rotation
- Dismantleheim
- dedicated server

Acceptance: no regressions in existing tools.

---

## 23. Explicit Non-Goals

Do not add:

- dismantling
- mass deletion
- terrain editing
- zooping
- arbitrary object rotation
- build camera
- blueprint library
- structure saving system
- replacement Hammer
- replacement build menu
- generic world editor
- creature manipulation
- vegetation editing

If an existing installed mod reasonably provides the functionality, SonicWorldTools should not duplicate it.

---

## 24. First Research Task for Cursor

Before writing the Infinity Hammer bridge, inspect the current source for:

```text
ObjectSelection
HammerSelect
ToolSelection
BaseSelection
Selector
```

Goal: determine the smallest stable path for creating an Infinity Hammer multi-object placement selection from an exact list of existing `ZNetView` / ZDO objects.

Cursor must document the result before implementing a custom placement engine.

---

## 25. MVP Definition

Version 0.1 is complete when:

1. Precision Area Pipette appears in Infinity Hammer's existing Hammer menu.
2. User can define a rectangular prism with two points.
3. Only pieces whose positions fall inside that prism are selected.
4. The selection is visibly accurate.
5. Individual pieces can be added and excluded.
6. The exact refined selection can be converted into an Infinity Hammer placement ghost.
7. Normal Infinity Hammer placement controls continue working.
8. No other building mod behavior changes when the Sonic tool is inactive.
9. A three-story test building can have only its middle floor copied without selecting the floors above or below.
10. Automated tests cover non-Unity selection/refinement logic.

---

## 26. Source References

Current upstream repositories:

- Infinity Hammer: https://github.com/JereKuusela/valheim-infinity_hammer
- World Edit Commands: https://github.com/JereKuusela/valheim-world_edit_commands

Important source areas reviewed during planning:

```text
InfinityHammer/commands/ToolData.cs
InfinityHammer/commands/ToolManager.cs
InfinityHammer/selection/ToolSelection.cs
InfinityHammer/commands/hammer/Hammer.cs
WorldEditCommands/Object/ObjectParameters.cs
WorldEditCommands/Object/ObjectCommand.cs
```

Treat current upstream source as authoritative when implementation details conflict with this plan.

---

## 27. Guiding Rule

When deciding whether to implement something new, ask:

> Does Infinity Hammer, World Edit Commands, DragNBuild, Build Camera, a precision-rotation mod, or Dismantleheim already do this reasonably well?

If yes, integrate with it or leave it alone.

SonicWorldTools should exist only to fill the gaps between those tools.
