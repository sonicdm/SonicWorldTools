# M3 — In-game box selection

**Roadmap:** [ROADMAP.md](../ROADMAP.md) · **Spec:** [build-plan.md](../build-plan.md) §5, §8, §12 (concepts), §17–§18 (6–16), §19, §22 (M3)

## Goal

While **Precision Area Pipette** is active, implement two-corner volume selection in the world: Corner A/B input, live box preview, object highlights, and HUD count/dimensions — **no copying or Infinity Hammer hand-off** yet.

## Deliverables

| Path | Purpose |
|------|---------|
| `src/SonicWorldTools/Modules/PrecisionAreaPipette/PrecisionAreaPipetteInput.cs` | Left-click set corners; escape cancel (initial bindings per §12) |
| `src/SonicWorldTools/Modules/PrecisionAreaPipette/PrecisionAreaPipettePreview.cs` | Orchestrates preview state with `SelectionState` + M2 query |
| `src/SonicWorldTools/Modules/PrecisionAreaPipette/PrecisionAreaPipetteController.cs` | Full tool lifecycle: idle → corner A → live box → corner B → locked selection |
| `src/SonicWorldTools/Rendering/BoxRenderer.cs` | Corner markers + box edges; non-opaque (§8) |
| `src/SonicWorldTools/Rendering/ObjectHighlightRenderer.cs` | Highlight effective/base selection; cache materials (§19) |
| `Plugin.cs` | Register controller only when IH present and pipette enabled |

**UX flow (§5):**

1. Activate tool — no world modification.
2. Left-click → Corner A + marker.
3. Cursor move → live box; left-click → Corner B; box locks.
4. Objects inside volume highlighted; count + `W × D × H` in HUD (§5, §8).

**Performance (§19):**

- Candidate refresh ~10–20 Hz; box follow cursor ~60 Hz.
- Spatially bounded query via M2 `SelectionQuery`.
- No new highlight materials per frame.

**Regression structure:** Build or use disposable test building per §17 (3 floors, edge cases).

## Acceptance criteria

From build plan §22 — **Milestone 3**:

- Corner A/B input, live box, object highlights, selected count.
- **No copying yet.**

**Acceptance:** **Second-floor-only selection works reliably** — primary success criterion (§18 items 13–16):

- Box only second floor of test building.
- First and third floors **not** selected.
- Selected count matches visible highlight.

Also satisfy §18 items 6–12 (corner selection flow and markers).

## Non-goals / do-not

- No manual add/exclude (M4).
- No pick/copy or `InfinityHammerBridge` (M5).
- Do not finalize all bindings — playtest-driven tweaks belong in M6 (§12).
- Do not use opaque preview geometry blocking the build (§8).
- Do not recompute selection with different rules at hand-off time (preview = truth early; M5 must preserve this).

## Start-here checklist (implementing agent)

1. Confirm M2 tests green; integrate `SelectionState` + `SelectionQuery` into controller.
2. Implement raycast / point capture for corners on build pieces (match IH hammer targeting feel).
3. Wire `BoxRenderer` + `ObjectHighlightRenderer`; respect `[Preview]` config flags.
4. Show HUD: tool name, object count, dimensions when `[PrecisionAreaPipette] ShowDimensions` / `ShowSelectedCount` true.
5. Deploy: `..\tools\dev.ps1 deploy`; verify with [valheim-mcp skill](file:///e:/Scripts/Valheim%20Mods/.cursor/skills/valheim-mcp/SKILL.md) or in-game on §17 structure.
6. Tune query radius to building scale; confirm multi-story precision.
7. Proceed to [M4-selection-refinement.md](M4-selection-refinement.md).
