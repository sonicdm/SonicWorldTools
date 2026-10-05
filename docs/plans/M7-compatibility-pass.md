# M7 — Compatibility pass

**Roadmap:** [ROADMAP.md](../ROADMAP.md) · **Spec:** [build-plan.md](../build-plan.md) §11, §18 (29–33), §20–§21, §22 (M7), §25 (MVP)

## Goal

Verify SonicWorldTools does not regress Infinity Hammer, World Edit Commands, or common companion mods; confirm inactive-tool isolation and multiplayer expectations; complete MVP **0.1.0** checklist when combined with M0–M6.

## Deliverables

| Path | Purpose |
|------|---------|
| `docs/test-matrix.md` | In-game regression checklist mapped to §18 (all 33 items) and §17 test structure |
| `tests/SonicWorldTools.Tests/*` | All §16 offline tests still passing (volume, refinement, YAML installer) |
| Code fixes only as needed | Input isolation, controller deactivate on tool switch, no shared static state with other mods |
| `README.md` or mod manifest text | Dependencies: BepInEx, Server Devcommands, Infinity Hammer; note WEC transitive check (§11) |

**Test environments (§20):**

- Local world
- Dedicated server (authorized player using pipette)
- Optional: modded client placing while vanilla client observes result

**Compatibility targets (no direct integration required for MVP — §11):**

- Infinity Hammer
- World Edit Commands
- DragNBuild
- Build Camera
- Precision rotation (on placement ghost)
- Dismantleheim

## Acceptance criteria

From build plan §22 — **Milestone 7**:

- Test all listed integrations/environments.

**Acceptance:** **No regressions in existing tools** (§18 items 29–33):

- Switch to normal Pipette → Sonic inputs **deactivated**.
- DragNBuild → no conflict outside Sonic tool.
- Build Camera → preview remains usable.
- Precision rotation on ghost → no interference.
- Dismantleheim → no shared state or input leakage.

**MVP 0.1.0 (§25):** All ten MVP bullets satisfied, including:

- Middle-floor-only copy on three-story test building (items 9 + full flow).
- Automated tests for non-Unity selection/refinement logic (item 10).

## Non-goals / do-not

- Do not add integrations that duplicate other mods (§23, §27).
- Do not require other players to install SonicWorldTools to see placed builds (§20).
- Do not bypass wards, ownership, or devcommand permissions (§20).
- No release packaging unless user requests (`package.ps1` / `release.ps1`).

## Start-here checklist (implementing agent)

1. Build §17 regression structure in a test world (or document seed/coordinates).
2. Walk §18 acceptance tests 1–33; record pass/fail in `docs/test-matrix.md`.
3. With Sonic tool **inactive**, repeat IH pipette, WEC commands, DragNBuild, Build Camera smoke tests.
4. Run dedicated-server pick + place once with Server Devcommands permissions as expected.
5. Run `dotnet test` for entire `SonicWorldTools.Tests` suite.
6. Compare results to [build-plan.md §25](../build-plan.md#25-mvp-definition); if all pass, version **0.1.0** is MVP-complete per [ROADMAP.md](../ROADMAP.md).
7. Use [valheim-release-test skill](file:///e:/Scripts/Valheim%20Mods/.cursor/skills/valheim-release-test/SKILL.md) only when user asks to ship.
