# SonicWorldTools roadmap

**MVP version:** `0.1.0` — ship when [build plan §25](build-plan.md#25-mvp-definition) acceptance is met (all milestones M0–M7 complete per their acceptance criteria in [§22](build-plan.md#22-implementation-milestones)).

**Authoritative spec:** [build-plan.md](build-plan.md) (Precision Area Pipette & Selection Refinement).

Implement milestones in order. Each milestone has a self-contained Cursor implementation plan under `docs/plans/`.

| Milestone | Plan | Summary |
|-----------|------|---------|
| **M0** — Project skeleton | [plans/M0-project-skeleton.md](plans/M0-project-skeleton.md) | BepInEx plugin, config, dependency detection, logging, test project |
| **M1** — Tool installation | [plans/M1-tool-installation.md](plans/M1-tool-installation.md) | Infinity Hammer YAML install, safe upgrades, menu entry + command |
| **M2** — Selection engine | [plans/M2-selection-engine.md](plans/M2-selection-engine.md) | `SelectionVolume`, normalization, queries, state; offline tests |
| **M3** — In-game box selection | [plans/M3-ingame-box-selection.md](plans/M3-ingame-box-selection.md) | Two-corner UX, live box, highlights, count (no copy yet) |
| **M4** — Selection refinement | [plans/M4-selection-refinement.md](plans/M4-selection-refinement.md) | Manual add/exclude, reset, resize persistence, tests |
| **M5** — Infinity Hammer hand-off | [plans/M5-infinity-hammer-handoff.md](plans/M5-infinity-hammer-handoff.md) | Research-first bridge → placement ghost (highest risk) |
| **M6** — UX hardening | [plans/M6-ux-hardening.md](plans/M6-ux-hardening.md) | Controls, HUD, cancel/reset, config, input conflicts |
| **M7** — Compatibility pass | [plans/M7-compatibility-pass.md](plans/M7-compatibility-pass.md) | IH, WEC, DragNBuild, Build Camera, rotation, Dismantleheim, dedicated server |

## MVP checklist (0.1.0)

From [build-plan.md §25](build-plan.md#25-mvp-definition):

1. Precision Area Pipette in Infinity Hammer’s Hammer menu (M1).
2. Two-point rectangular prism selection (M2, M3).
3. Origin-based inclusion inside the prism (M2, M3).
4. Visually accurate selection (M3, M4).
5. Per-piece add and exclude (M4).
6. Exact refined set → Infinity Hammer placement ghost (M5).
7. Standard Infinity Hammer placement after hand-off (M5).
8. No behavior change when Sonic tool inactive (M7).
9. Three-story building: copy middle floor only (M3 primary success; full flow M5).
10. Automated tests for non-Unity selection/refinement logic (M2, M4).

## Out of scope (all milestones)

See [build-plan.md §23](build-plan.md#23-explicit-non-goals). Dismantle/delete, terrain editing, zoop-style tools, and prop scatter were dropped from the non-goals and are now in scope. Do not implement features in roadmap docs-only tasks; implementation follows the plans above.
