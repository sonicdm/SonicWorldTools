# M6 — UX hardening

**Roadmap:** [ROADMAP.md](../ROADMAP.md) · **Spec:** [build-plan.md](../build-plan.md) §5, §8, §12–§14, §18 (workflow), §22 (M6)

## Goal

Make the Precision Area Pipette workflow **usable without console commands**: playtest-finalized controls, clear cancel/reset flows, config-driven HUD and preview, and resolution of input conflicts with vanilla Hammer, Infinity Hammer, and common build mods.

## Deliverables

| Path | Purpose |
|------|---------|
| `src/SonicWorldTools/Modules/PrecisionAreaPipette/PrecisionAreaPipetteInput.cs` | Final bindings; all custom actions configurable via BepInEx (§12) |
| `src/SonicWorldTools/Config/PluginConfig.cs` | Complete §13 keys; bindable input entries if workspace pattern supports it |
| `src/SonicWorldTools/Modules/PrecisionAreaPipette/PrecisionAreaPipetteController.cs` | Cancel (Escape), reset corners, reset refinement, clear state on tool deactivate |
| `src/SonicWorldTools/Modules/PrecisionAreaPipette/PrecisionAreaPipettePreview.cs` | HUD: tool title, count, `7.0m × 10.0m × 3.0m` style dimensions (§5) |
| Logging | Normal + verbose paths per §14 |

**Input targets (validate in playtest — §12):**

| Action | Initial concept |
|--------|-----------------|
| Set corner / confirm | Left click |
| Add piece | Modifier + left click |
| Exclude piece | Modifier + middle click |
| Hammer menu | Right click preserved |
| Cancel precision session | Escape |

Determine what IH, vanilla Hammer, and DragNBuild consume; adjust defaults and document in config description strings.

**Tool scope:** Custom bindings apply **only** while Precision Area Pipette is active (§12).

Validate `instant: true` on YAML tool if menu behavior was awkward in M1 (§9).

## Acceptance criteria

From build plan §22 — **Milestone 6**:

- Finalized controls, dimensions HUD, cancel/reset flow, config, input conflict fixes.

**Acceptance:** **Workflow is usable without console commands** — a player can:

- Enter tool, select volume, refine, pick up, and place using only in-game inputs.
- Cancel or reset without stale highlights or stuck controller state.
- Toggle preview/HUD features via config.

## Non-goals / do-not

- Full compatibility matrix with every mod (M7).
- New features (rotation, blueprints, dismantle) per §23.
- Changing Infinity Hammer defaults or other mods’ configs.

## Start-here checklist (implementing agent)

1. Confirm M5 hand-off works end-to-end once; note input friction from playtest.
2. Test with DragNBuild installed — note intercepted clicks (inform M7).
3. Implement Escape → clear corners, refinement, preview; deactivate cleanly.
4. Expose all Sonic-specific binds in config with sensible defaults from §12.
5. Ensure right-click still opens normal Hammer menu when appropriate.
6. Wire `[Preview]` and `[PrecisionAreaPipette]` toggles without restart if possible.
7. Add verbose logging for corners and selection counts (§14); no per-object spam in normal mode.
8. Update or create `docs/test-matrix.md` rows for UX cases if mod uses release-test skill.
9. Proceed to [M7-compatibility-pass.md](M7-compatibility-pass.md).
