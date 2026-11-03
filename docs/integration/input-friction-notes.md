# Input friction notes (M6 → M7)

Short notes for the M7 compatibility pass. Not a full matrix.

## Defaults (Precision Area Pipette active only)

| Action | Default |
|--------|---------|
| Set Corner A / B / C | LMB (`Mouse0` / Attack) |
| Add hovered piece | Alt + LMB |
| Exclude hovered piece | Alt + MMB |
| Pick up (IH copy ghost) | Shift + MMB (same as IH sample/copy) |
| Dismantle selection (refunds) | Ctrl + Delete (no crafting-station range gate; ward + CanBeRemoved still apply) |
| Permanent delete selection (no drops) | Shift + Delete |
| Cancel selection | Escape |
| New box (reset corners) | Home |
| Reset refinement | Backspace |
| Hammer menu | RMB — not bound by Sonic |

Plain **Delete** (no modifier) is left for Dismantleheim `ActivateKey`. Dismantleheim ignores ActivateKey while Ctrl/Shift/Alt/Cmd are held so pipette Ctrl+Del / Shift+Del can fire. Plain LMB while **Locked** does nothing — use Home / Esc to clear.

All of the above are BepInEx-configurable under `[PrecisionAreaPipette]`.

## Undo

- **Infinity Hammer / World Edit Commands** provide `undo` (often Keypad7). Pipette dismantle/delete records removals into that undo stack when WEC is present.
- **Dismantleheim** does not have its own undo.

## Oriented box

- A→B = width edge on XZ (direction preserved; no 180° yaw flip).
- C = depth on either side of that edge + height span (min/max Y of A,B,C).
- Origin shifts so the box always covers A→B and C.
- `PrecisionAreaPipette.SelectionPadding` expands all six faces of the effective volume and the rendered box together (default 0.1 m; set to 0 for exact corners; clamped to 0–2 m). Corner markers remain at the actual aim points. The separate 2 m broad-phase search padding only gathers nearby candidates; it does not change selection membership.
- Selection membership is based on each piece's ZDO origin. Padding can include origins just outside the aimed surfaces; increase it cautiously near adjacent floors or walls.

## Infinity Hammer

- Tool entry uses `instant: true` so the YAML command runs without holding a ghost.
- Activate on BuildUi **commit** + IH `ToolSelection` / Instant command.
- Suppress `PlayerAttackInput` / place / `RemovePiece` / `Interact` while active.
- Selection is origin-mode: ZDO world position vs the oriented box.

## DragNBuild / Vanilla

- DragNBuild can steal build clicks — disable or rebind `SetCornerKey` if needed.
- Escape cancels Sonic selection only; does not unequip the Hammer. While a selection is in progress (past idle Corner A), Escape does not open the pause menu — press Esc again once cleared, or deactivate the tool.
