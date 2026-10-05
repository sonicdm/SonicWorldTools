# M4 — Selection refinement

**Roadmap:** [ROADMAP.md](../ROADMAP.md) · **Spec:** [build-plan.md](../build-plan.md) §7, §8, §16, §18 (17–22), §22 (M4)

## Goal

Add reusable **Selection Refinement** infrastructure: manual add/exclude by piece, reset refinement, distinct visual states, and persistence behavior when corners resize — effective selection must match highlights exactly.

## Deliverables

| Path | Purpose |
|------|---------|
| `src/SonicWorldTools/Refinement/SelectionRefinement.cs` | `BaseSelection`, `AddedSelection`, `ExcludedSelection` as `HashSet<ZDOID>`; effective = (Base ∪ Added) − Excluded (§7) |
| `src/SonicWorldTools/Refinement/RefinementInput.cs` | Modifier + left add; modifier + middle exclude (initial §12); configurable later in M6 |
| `src/SonicWorldTools/Core/SelectionState.cs` | On corner change: recompute `BaseSelection`; apply persistence rules (§7) |
| `src/SonicWorldTools/Modules/PrecisionAreaPipette/PrecisionAreaPipettePreview.cs` | Visual states: volume base, manual add, manual exclude, hover (§8) |
| `tests/SonicWorldTools.Tests/RefinementTests.cs` | §16 refinement scenarios |

**Behavior (fixed):**

- Subtract: selected target → `ExcludedSelection`; update highlight + count immediately (§7).
- Add: valid unselected target → `AddedSelection` (may be outside box) (§7).
- Corner A/B change → recompute `BaseSelection`; keep exclusions/additions where ZDOIDs still valid (§7).
- `Reset Refinement` clears Added + Excluded (§7).
- `[PrecisionAreaPipette] PersistRefinementsAfterResize` controls resize persistence (§13).

Preview and any future hand-off must use **the same effective set** (§8).

## Acceptance criteria

From build plan §22 — **Milestone 4**:

- Manual add, manual exclude, reset refinement, persistence through resize, tests.

**Acceptance:** **Effective selection exactly matches highlights** (§18 items 17–22):

- Exclude one selected wall → leaves selection.
- Add adjacent wall outside box → joins selection.
- Resize box → behavior matches config for refinement persistence.

**Automated:** All §16 refinement tests pass in `RefinementTests.cs`.

## Non-goals / do-not

- No Infinity Hammer ghost / pick (M5).
- Do not store only `GameObject` references — use `ZDOID` (§7).
- No dismantling or deletion flows (§23).
- Do not spam per-object logs unless verbose (§14).

## Start-here checklist (implementing agent)

1. Extract refinement from pipette-specific code into `Refinement/` module (§2 modular features).
2. Implement effective-set API used by preview and (later) bridge: `GetEffectiveSelection(): IReadOnlySet<ZDOID>`.
3. Wire `RefinementInput` only when pipette active and corners locked (or per design after B set).
4. Implement reset (key or action — finalize in M6).
5. Run `RefinementTests` for all §16 scenarios including recompute with excluded B and added D.
6. In-game verify §18 refinement cases on §17 test building.
7. Proceed to [M5-infinity-hammer-handoff.md](M5-infinity-hammer-handoff.md).
