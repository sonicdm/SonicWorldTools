# Infinity Hammer hand-off research

**Milestone:** M5 research (Phase C — C-Research only)  
**Spec:** [M5-infinity-hammer-handoff.md](../plans/M5-infinity-hammer-handoff.md), [build-plan.md](../build-plan.md) §10 / §24  
**Date:** 2026-10-05  
**Status:** Research complete — **no bridge code in this doc’s phase**

---

## Source inspected

| Source | Detail |
|--------|--------|
| Upstream git | [JereKuusela/valheim-infinity_hammer](https://github.com/JereKuusela/valheim-infinity_hammer) `main` @ `ce2737f` (2026-10-04) — plugin `VERSION` **1.87**, GUID `infinity_hammer` |
| Supporting | [valheim-world_edit_commands](https://github.com/JereKuusela/valheim-world_edit_commands) (undo / `UndoHelper`); [valheim-dev `Service/Selector.cs`](https://github.com/JereKuusela/valheim-dev/blob/master/ServerDevcommands/Service/Selector.cs) |
| Local package sample | Thunderstore zip **1.86.0** under ValheimModManager backups (DLL present; slightly behind inspected `main`) |
| Local clone (agent scratch) | `SonicWorldTools/.tmp-ih-src/` — **do not commit** |

**Blockers:** None. IH source was available and sufficient for §24.

---

## Class map (requested symbols)

| Type | Assembly / namespace | Role for Sonic hand-off |
|------|---------------------|-------------------------|
| **`ObjectSelection`** | `InfinityHammer` | **Primary API.** Multi-object ctor takes exact `IEnumerable<ZNetView>` and builds the placement ghost hierarchy. |
| **`Selection`** | `InfinityHammer` | Static owner: `CreateGhost(BaseSelection)` → `Clear` → `Activate` → `SetupPlacementGhost()`. |
| **`BaseSelection`** | `InfinityHammer` | Base for object/tool/location/room selections; holds `SingleUse`, ghost prefab, place hooks. |
| **`HammerSelect`** | `InfinityHammer` | Registers console command `hammer` (hover / connect / circle / rect / prefab id). **No exact ZDOID list parameter.** |
| **`Hammer`** | `InfinityHammer` | Equip / empty piece slot / infinity hammer helpers. `SelectEmpty()` clears build-menu piece so ghost selection wins. |
| **`ToolSelection`** | `InfinityTools` | YAML **tool** ghost (ruler + command), not object copy. Used by Area Pipette *before* it runs `hammer …`. |
| **`ToolData` / `ToolManager`** | `InfinityTools` | YAML tool install/reload. Relevant to M1 menu entry, **not** refined-set hand-off. |
| **`Selector`** | `Service` (Server Devcommands) | Hover / area / connected queries used by `hammer`. Sonic already has its own volume query; **do not re-select via `Selector`**. |

---

## Preferred path for Phase D

### **Chosen: Path 1 — public IH selection constructor**

Call Infinity Hammer’s public multi-object constructor, then activate the normal ghost pipeline — same sequence `HammerSelect` uses after it has a `ZNetView[]`:

```csharp
// Conceptual Phase D flow (do not treat as production code)
HammerHelper.Init(); // EnabledCheck + Hammer.Equip()
var selection = new ObjectSelection(
    views,           // exact live ZNetViews from effective ZDOIDs
    singleUse: false, // false = copy; true mirrors pick's SingleUse flag
    scale: null,
    extraData: null);
var ghost = Selection.CreateGhost(selection);
Hammer.SelectEmpty();
// Optional pick (remove originals) — see §24 Q3:
// UndoHelper.BeginAction();
// foreach view: UndoHelper.AddRemoveAction(zdo); HammerHelper.RemoveZDO(zdo);
// UndoHelper.EndAction();
```

**Why Path 1 wins**

1. Constructor is **`public`**: `ObjectSelection(IEnumerable<ZNetView> views, bool singleUse, Vector3? scale, DataEntry? extraData, …)`.
2. Ghost activation is **`public`**: `Selection.CreateGhost(BaseSelection)`.
3. This is exactly what Area Pipette / `hammer` already do after gathering views — Sonic only needs to supply a **better** view list (refined ZDOID set), not a custom placement engine.
4. No Harmony patch of Infinity Hammer required.

**Why not 2–5**

| Path | Verdict |
|------|---------|
| **2. Command with exact object set** | **Fails.** `hammer` accepts hover, `connect`, `circle`/`rect`, or a **prefab id** — not an arbitrary list of world instance IDs / ZDOIDs. Re-running area geometry would discard Sonic’s add/exclude refinement. |
| **3. Small adapter on public types** | Redundant if Path 1 is callable directly. Keep only a thin Sonic wrapper (`InfinityHammerBridge`) that resolves ZDOID→`ZNetView` and calls Path 1. |
| **4. Isolated reflection** | Reserve if compile-time `InfinityHammer.dll` cannot be referenced in CI/Reqs. Target the **same** public members as Path 1 (`ObjectSelection` multi ctor + `Selection.CreateGhost`). Prefer compile-time `Reference` (`Private=false`, not shipped) when the DLL is available locally. |
| **5. Custom placement engine** | **Not needed.** Paths 1–4 do not fail; Path 1 is viable. |

### Phase D implementation notes (contract)

- Input: `GetEffectiveSelection()` → resolve each `ZDOID` to a live `ZNetView` via `ZNetScene` instances; drop unloaded IDs and continue if remainder non-empty (§21).
- Empty set → soft-fail; do not call multi ctor (uses `views.First()`).
- Reject / skip `Player` views (`ObjectSelection` single-view ctor throws on `Player`).
- Default MVP behavior: **copy** (`singleUse: false`, no remove loop) so originals remain; optional pick can mirror IH later.
- After success: deactivate Sonic pipette so IH owns freeze / rotate / snap / place.
- Soft detect GUID `infinity_hammer` (already in bridge detect heuristics). Per `AGENTS.md`, avoid hard `[BepInDependency]` unless packaging policy changes; fail soft with §21 message if missing or API mismatch.
- `DataEntry` comes from Server Devcommands `Data` namespace — pass `null` unless overriding health/text/etc.
- Pick path also needs `WorldEditCommands.UndoHelper` + `HammerHelper.RemoveZDO` (WEC is already an IH hard dependency upstream).

---

## §24 checklist answers

### 1. Constructor / API candidate for multi-object selection from an exact list

**Primary**

```text
InfinityHammer.ObjectSelection(
  IEnumerable<ZNetView> views,
  bool singleUse,
  Vector3? scale,
  DataEntry? extraData,
  TerrainHeight? terrainHeightInfo = null,
  TerrainPaint? terrainPaintInfo = null)
```

Then:

```text
InfinityHammer.Selection.CreateGhost(selection) → GameObject (player.m_placementGhost)
InfinityHammer.Hammer.SelectEmpty()
```

Optional prelude (same as `hammer` command): `HammerHelper.Init()` (throws if IH disabled; equips a hammer).

Related single-object overloads exist (`ZNetView`, `Piece`, blueprint) but the **multi** ctor is the one that matches a refined set.

### 2. Required object types

| Need | Type |
|------|------|
| Hand-off input | Live **`ZNetView`** instances (ZDO-backed). Prefab-only views from `ZNetScene.GetPrefab` are for menu/id pick, not world copies. |
| Data source | Each view’s **`ZDO`** — multi ctor merges `new DataEntry(view.GetZDO())` into ghost child state. |
| Sonic side | Effective **`ZDOID`** set → resolve to views; drop missing. |
| Optional | `DataEntry` overrides; terrain height/paint snapshots (Area Pipette / blueprint only — **not required** for MVP piece copy). |

### 3. How `pick` removes originals

In `HammerSelect` (`commands/hammer/Hammer.cs`), when `pars.Pick` is true **after** the ghost is created:

1. `UndoHelper.BeginAction()` (World Edit Commands).
2. For each selected view: `UndoHelper.AddRemoveAction(view.GetZDO())`.
3. For each view: `HammerHelper.RemoveZDO(view.GetZDO())` (take ownership if needed, destroy scene instance / ZDO).
4. `UndoHelper.EndAction()`.

`pars.Pick` is **also** passed as `singleUse` into `ObjectSelection`. `SingleUse` clears the hammer selection after one place (`PlacePiece` finalizer → `Hammer.SelectRepair()`), separate from world deletion.

Console / YAML: `hammer pick` or `pick=<mod1>` (Area Pipette).

### 4. How normal copy differs from pick

| | Copy (default pipette) | Pick |
|--|------------------------|------|
| Ghost | Same `ObjectSelection` + `CreateGhost` | Same |
| `singleUse` | `false` | `true` |
| World objects | **Remain in place** | **Removed** (with undo) |
| Place again | Can place repeatedly until cleared | Selection clears after one place |

Copy = duplicate elsewhere. Pick = move (ghost + delete originals).

### 5. How ghost placement is activated

`Selection.CreateGhost`:

1. `Clear()` previous selection for current tool key.
2. `selection.Activate()`.
3. Store selection under `HammerHelper.GetTool()` (e.g. `infinity_hammer`).
4. `Player.SetupPlacementGhost()`.
5. Return `player.m_placementGhost`.

Harmony `PieceTable.GetSelectedPiece` returns `Selection.Get().GetSelectedPiece()` so the ghost piece drives vanilla / IH placement. Multi-object place uses `UsesSelectionRoot` + `AfterPlace` → `HandleMultiple` to spawn each child with ZDO data (undo sub-actions).

### 6. Whether originals can remain in place

**Yes.** Omit the pick remove loop and pass `singleUse: false` (default copy). That is the normal pipette behavior and the recommended MVP hand-off.

### 7. Whether undo is involved

| Operation | Undo |
|-----------|------|
| Copy ghost creation | No world remove; placement later uses `UndoHelper.BeginSubAction` / `EndSubAction` inside `ObjectSelection.GetPrefab` / `AfterPlace`. |
| Pick remove | Explicit `UndoHelper` remove actions around `RemoveZDO`. |
| Place multi | Per-child spawn tracked via WEC undo helpers. |

Sonic should use the same undo helpers if implementing pick; for copy-only, rely on IH’s existing place undo.

### 8. Whether the path works without patching Infinity Hammer internals

**Yes for Path 1.** All required types/methods are **public**. No need to patch IH, mark pieces via `HammerMark`, or reimplement ghost/place. Soft-fail if types/signatures change across IH versions (reflection binder or missing method → §21 message).

---

## Trace: Area Pipette → ghost (reference)

YAML (`ToolData` / `InitialData`):

```yaml
- name: Area pipette
  command: hammer pick=<mod1> freeze=<mod2> height=<h> <area>
```

Flow:

1. `ToolSelection` + ruler collect area parameters.
2. Command expands to `hammer … circle=` / `rect=` …
3. `HammerSelect` → `Selector.GetNearby(...)` → `ZNetView[]`.
4. `new ObjectSelection(views, pars.Pick, …)` + `Selection.CreateGhost` + optional pick delete / freeze.

Sonic replaces steps 1–3 with its own volume + refinement, then joins at step 4 with an exact view list.

---

## Risks / version pins

- Inspected source is **1.87** (`main`); local Thunderstore sample was **1.86.0**. Phase D should smoke-test against the installed profile IH version; soft-fail on ctor / `CreateGhost` signature drift.
- Multi ctor requires **≥1** view and valid ZDOs; first view anchors layout before root is zeroed.
- Player / invalid objects: filter before hand-off.
- Compile vs soft-dep: keep IH as runtime soft dependency; optional local `Reqs\InfinityHammer.dll` reference for Path 1 compile. If CI has no IH DLL, implement Path 1 via a **narrow reflection binder** (still not Path 5).
- Do not use `hammer_mark` / `HammerMark.MarkedPieces` as the primary hand-off — WearNTear-centric and not ZDOID-exact.

---

## Phase D gate summary

| Item | Result |
|------|--------|
| Research doc | This file |
| Chosen path | **1** |
| Custom placement engine | **Not authorized** — Path 1 succeeds |
| Next | Implement `InfinityHammerBridge` hand-off only after coordinator/user approval of this path |

**Do not mark `phase-c-m4-m5res` complete from this research alone** — Phase C also requires M4 refinement core + wire.
