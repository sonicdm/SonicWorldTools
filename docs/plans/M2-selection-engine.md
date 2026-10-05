# M2 — Pure selection engine

**Roadmap:** [ROADMAP.md](../ROADMAP.md) · **Spec:** [build-plan.md](../build-plan.md) §6, §15, §16, §19, §22 (M2)

## Goal

Implement **offline-testable** core selection math and state: axis-aligned box from two corners, origin-based inclusion (MVP), bounded candidate queries, and ZDOID-backed state — with **no** in-game input or rendering yet.

## Deliverables

| Path | Purpose |
|------|---------|
| `src/SonicWorldTools/Core/SelectionVolume.cs` | Normalize corners A/B to `minX..maxX`, `minY..maxY`, `minZ..maxZ`; dimensions helper |
| `src/SonicWorldTools/Core/SelectionState.cs` | Holds corner points, `BaseSelection`, refinement sets (structure for M4; base recompute here) |
| `src/SonicWorldTools/Core/SelectionQuery.cs` | Given volume + world object set, return candidates whose **canonical position** is inside (Origin mode) |
| `src/SonicWorldTools/Integration/WorldEditBridge.cs` | Reuse WEC/IH position semantics where practical (ZDO position); no command parsing DSL (§3) |
| `tests/SonicWorldTools.Tests/SelectionVolumeTests.cs` | §16 volume cases |
| `tests/SonicWorldTools.Tests/SelectionStateTests.cs` | Base selection recompute hooks |

**Semantics (fixed — no Option branches):**

- Axis-aligned rectangular prism only (§6).
- Inclusion: object selected iff canonical world position ∈ normalized volume (§6, `SelectionMode = Origin` default in config).
- Object class filter: same practical set as Infinity Hammer area pipette would copy — do not broaden to creatures/vegetation (§6).
- Query must be **spatially bounded** — never full-world scan per operation (§19).

**SelectionQuery design notes:**

- Accept enumerable of `(ZDOID, Vector3 position)` or test doubles for unit tests.
- For later Unity integration, adapter loads positions from `ZNetView` / ZDO consistent with World Edit Commands (§6, §26).

## Acceptance criteria

From build plan §22 — **Milestone 2**:

- `SelectionVolume`, corner normalization, candidate inclusion, selection state.
- Automated tests.

**Acceptance:** **All offline selection tests pass** (§16):

- A/B ordering irrelevant; negative coordinates; boundary inclusion; zero-width/zero-height boxes; very large box; points just outside each face.

Run: `dotnet test` on `tests/SonicWorldTools.Tests/`.

## Non-goals / do-not

- No in-game input, `BoxRenderer`, or highlights (M3).
- No refinement add/exclude logic beyond storing empty sets (M4).
- No `SelectionIntersectionMode = Collider` (future; §6).
- No Infinity Hammer bridge (M5).
- No per-frame allocation patterns — design query API for batch refresh (§19).

## Start-here checklist (implementing agent)

1. Confirm M1 done (not required for unit tests, but solution should build).
2. Implement `SelectionVolume` with pure methods; 100% covered by `SelectionVolumeTests`.
3. Implement `SelectionState`: set corners → recompute `BaseSelection` via `SelectionQuery`.
4. Add `WorldEditBridge` as thin helper to read position from game types when integrating in M3 — keep tests on pure data.
5. Wire `PluginConfig` `[PrecisionAreaPipette] SelectionMode` — only `Origin` implemented; ignore or log if Collider requested.
6. Run full §16 volume matrix; fix edge cases at box faces (inclusive boundaries — document choice in test names).
7. Proceed to [M3-ingame-box-selection.md](M3-ingame-box-selection.md).
