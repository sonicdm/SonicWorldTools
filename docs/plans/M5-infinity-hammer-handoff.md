# M5 — Infinity Hammer hand-off

**Roadmap:** [ROADMAP.md](../ROADMAP.md) · **Spec:** [build-plan.md](../build-plan.md) §10, §24, §18 (23–28), §21, §22 (M5)

## Goal

Convert the **exact effective refined selection** (same as preview highlights) into Infinity Hammer’s normal **multi-object placement ghost**, then exit Sonic selection mode so IH owns placement, freeze, rotation, snapping, and confirmation.

**This is the highest-risk milestone.** Follow integration order from §2 and §10.

---

## RESEARCH FIRST (mandatory before implementation)

**Do not build a custom placement engine until this research is complete and written down.**

Per [build-plan.md §24](../build-plan.md#24-first-research-task-for-cursor) and §10, inspect current **Infinity Hammer** source (authoritative: [valheim-infinity_hammer](https://github.com/JereKuusela/valheim-infinity_hammer)) for:

```text
ObjectSelection
HammerSelect
ToolSelection
BaseSelection
Selector
```

Also review §26 references:

```text
InfinityHammer/commands/ToolData.cs
InfinityHammer/commands/ToolManager.cs
InfinityHammer/selection/ToolSelection.cs
InfinityHammer/commands/hammer/Hammer.cs
```

**Deliverable before bridge code:** `docs/integration/infinity-hammer-handoff-research.md` (create in this milestone) documenting:

1. Constructor/API candidate for creating a multi-object selection from an exact list.
2. Required object types (`ZNetView`, ZDO, etc.).
3. How `pick` removes originals vs copy-only behavior.
4. How normal copy differs from pick.
5. How ghost placement is activated.
6. Whether originals can remain in place.
7. Whether undo is involved.
8. Whether the path works **without patching** Infinity Hammer internals.

**Preferred integration path (use first that works — §10):**

1. Public IH selection constructor / command.
2. IH command accepting exact object set.
3. Small compatibility adapter against IH public types.
4. Isolated reflection in `InfinityHammerBridge.cs`.
5. Reimplement multi-object placement **only if** 1–4 fail.

---

## Deliverables (after research doc approved)

| Path | Purpose |
|------|---------|
| `docs/integration/infinity-hammer-handoff-research.md` | **First** — findings from §24 checklist |
| `src/SonicWorldTools/Integration/InfinityHammerBridge.cs` | Hand-off implementation per chosen path |
| `src/SonicWorldTools/Modules/PrecisionAreaPipette/PrecisionAreaPipetteController.cs` | “Pick up selection” trigger → bridge → deactivate Sonic tool |
| Error handling in `Plugin.cs` / bridge | §21 messages if IH missing or API incompatible |

**Hand-off rules:**

- Input set = `GetEffectiveSelection()` resolved to live `ZNetView`/ZDO where possible.
- If an object unloaded before pick-up: drop from effective set, update count, continue if remainder valid (§21).
- On bridge failure: log `[SonicWorldTools] Infinity Hammer integration unavailable for this version.` and disable pipette selection — **no destructive fallback** (§21).
- After success: IH ghost rendering and controls only — Sonic does not fight IH (§5).

## Acceptance criteria

From build plan §22 — **Milestone 5**:

- Convert effective selection into Infinity Hammer placement ghost.
- Retain standard Infinity Hammer placement behavior.

**Acceptance:** **Exact refined selection can be copied and placed** (§18 items 23–28):

- Pick/copy → ghost contains exactly highlighted objects.
- Excluded absent; manually added present.
- Place elsewhere with normal IH controls.

## Non-goals / do-not

- **Do not** implement custom multi-object ghost/placement before research doc exists.
- **Do not** patch Infinity Hammer as first resort (§2).
- Do not change IH YAML beyond Sonic-owned file (M1).
- Do not bypass ward/server authority/permissions (§20).
- Do not finalize all key bindings (M6).

## Start-here checklist (implementing agent)

1. **STOP:** Read §10 and §24 in [build-plan.md](../build-plan.md).
2. Clone or browse Infinity Hammer source at commit compatible with your Valheim/IH versions.
3. Trace area pipette / `pick` / `freeze` / `hammer pick=...` flow (§3).
4. Write `docs/integration/infinity-hammer-handoff-research.md` with answers to all eight §24 questions and a **single chosen approach**.
5. Review with build plan §2 compatibility order; reject approaches that require fragile internal patches if a public path exists.
6. Implement `InfinityHammerBridge` minimally; dry-run via `[Debug] DryRunSelection` if useful (§13).
7. Wire pick-up from pipette controller; verify preview set == ghost set.
8. In-game: full §18 pick-up tests; dedicated server smoke if available (§20).
9. Proceed to [M6-ux-hardening.md](M6-ux-hardening.md).
