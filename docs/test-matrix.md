# SonicWorldTools publish test matrix

Manual checks before a `v*` tag. Everyday edits stop at `.\build.ps1` and ScriptEngine deploy.
Procedures stay the same across versions. Copy this section forward for a new version and reset results to `pending`.

## How to run

1. `.\build.ps1` then `..\tools\dev.ps1 deploy -Mod SonicWorldTools` (ScriptEngine). F9 reload after each fix.
2. Work the checks below in order. Use ValheimMCP for log/console/render rows while you play.
3. Logs: `C:\Users\Allan\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Default\BepInEx\LogOutput.log`.
4. After ScriptEngine passes: `..\tools\dev.ps1 deploy -Mod SonicWorldTools -Plugins`, restart Valheim, re-check S01–S03.
5. Mark results `pass` / `fail`. Tag only when every result under the version heading is `pass`.

### §17 regression structure (test world)

Keep a disposable three-story build with: vertically aligned walls, overlapping roof pieces, furniture, crafting stations, portal, chest, decorative pieces, edge-crossing pieces, and a nearby unrelated building. Reuse for S13 / A13–A16 / MVP-9.

### Initial in-game session (2026-10-05)

- Offline gates: `dotnet test` **42 passed**; deploy to ScriptEngine **ok**.
- ValheimMCP **up** at menu (`127.0.0.1:8731`); world-dependent rows wait until loaded world + F9.
- Pre-world / log checks: S02–S05 marked **pass** below; S01 confirmed after world load.

### Phase F / M7 session note (2026-10-05)

- Code isolation added for §18.29 (deactivate on leave place-mode or IH placement ghost takeover).
- Live: Ctrl+Del / Shift+Del + Dismantleheim plain-Del coexistence verified. S10–S13 / S15–S18 pass.

## Procedures

### S01 — BepInEx load line
**Rule:** Plugin loads for this version. **Maps:** load hygiene (pre-§18).
1. Deploy and enter a world.
2. Check logs for `SonicWorldTools`.
3. **Pass:** load line `SonicWorldTools 0.1.0 loaded.`
4. **Fail:** missing load or immediate exception.

### S02 — No exception in Gale logs
**Rule:** Clean log for this mod after the session. **Maps:** load hygiene.
1. Search Gale logs for SonicWorldTools / exceptions.
2. **Pass:** no exception from this mod.
3. **Fail:** any exception from this mod.

### S03 — Optional dependency detect lines
**Rule:** IH/WEC detection logs without throwing when absent. **Maps:** §11 soft deps.
1. Note whether Infinity Hammer / World Edit Commands are installed.
2. **Pass:** info lines for detected or not detected; no error.
3. **Fail:** exception during `DependencyDetector`.

### S04 — Config file created
**Rule:** BepInEx config binds on first run. **Maps:** §13.
1. Confirm `BepInEx\config\com.sonicdm.valheim.sonicworldtools.cfg` exists after load.
2. **Pass:** `General.Enabled`, `PrecisionAreaPipette` keys (incl. keybinds and `SelectionPadding`), `Preview.*`, `Integration.InstallInfinityHammerTools`, and `Debug.VerboseLogging` / `Debug.DryRunSelection` present.
3. **Fail:** missing config or bind errors in log.

### S05 — Owned Infinity Hammer YAML install (M1)
**Rule:** With Infinity Hammer present, install only owned tools YAML (never `infinity_tools.yaml`). **Maps:** §18.1–2, §9.
1. Enter a world with Infinity Hammer loaded; deploy / F9.
2. Confirm log: `[SonicWorldTools] Installed Precision Area Pipette tool definition.`
3. Confirm file: `BepInEx\config\tools\infinity_tools_sonicworldtools.yaml` with marker `sonicworldtools-tool-v1` and command `sonic_precision_pipette`.
4. Confirm `infinity_tools.yaml` was not modified by this mod.
5. **Pass:** owned file present; shared default untouched; soft-fail log only if IH missing.
6. **Fail:** write to `infinity_tools.yaml`, crash, or missing owned file when IH is present.

### S06 — Hammer menu hot-reload (M1)
**Rule:** IH tools watcher picks up YAML without quitting Valheim. **Maps:** §18.4.
1. After S05 install (or F9 rewrite), open Hammer tools menu **without restarting Valheim**.
2. **Pass:** `Precision Select` appears under the Hammer **Tools** tab and `Lower Terrain` plus the terrain path variants appear under the Hoe **Tools** tab; no duplicate entries after F9.
3. **Fail:** tool missing until full restart, or duplicates after reload.

### S06p — Line Tool settings hotkey
**Rule:** The settings key belongs to Line Tool only while that tool is selected.
1. Select another Hammer tool and press F7. **Pass:** no Line Tool panel opens.
2. Select Line Tool and press F7. **Pass:** one panel opens, the cursor is free, and the key does not activate another game action.
3. Change corner mode, radius, gap, height, and ground-prop facing. **Pass:** the preview updates, and clicks in the panel place no pieces.
4. Press F7 again, then reopen and press Escape. **Pass:** F7 closes the panel and resumes the tool; Escape exits the tool and opens the game menu.
5. Re-equip the Hammer and select Line Tool again. **Pass:** settings persist and the hotkey works once, without duplicate panels or handlers.

### S06s — Shape Tool closed outlines
**Rule:** Shape Tool shares Line Tool sampling and placement safety while cycling closed outline layouts.
1. Select Shape Tool, sample a placed building piece with MMB, and verify the target remains intact.
2. Click a center, move the cursor, then cycle Circle → Square → Rectangle with Q. **Pass:** the preview follows each outline and Q does not change the IH ruler shape.
3. Hold the configured place modifier and click. **Pass:** pieces are placed around the shown outline and IH undo reverses the batch.
4. Switch to the build hammer and place a normal piece. **Pass:** Shape Tool no longer captures the build action.

### S06a — Curved Terrain Path Surface Variants (WEC / IH multi-point tool)
**Rule:** The custom tools draw WEC curves on the selected surface, level the route, and finish reliably.
1. In a disposable area, equip the Hoe and choose a path surface variant from the Hoe Tools tab.
2. Click at least three route points; hold IH modifier 2 (default Left Ctrl) while clicking the final point. Also verify clicking the previous point again finishes the route.
3. Repeat with at least one soil/grass surface and one paved variant. Compare final-point placement with and without modifier 1 (default Left Alt), while holding modifier 2 to finish. **Pass:** modifier 1 softens the cross-slope transition at path edges; changing the mouse-wheel size changes route half-width; WEC undo restores the terrain.
4. **Fail:** tool executes before finishing, ignores points/width, or leaves terrain changes that WEC cannot undo.

### S06b — Lower Terrain
**Rule:** Lowering terrain works directly from the Hoe menu using WEC and can be undone.
1. In a disposable area, equip the Hoe and select `Lower Terrain`.
2. Apply a small lowering amount to a terrain-grid area, then repeat while holding modifier 1 to verify smoothing. Hold modifier 2 to verify free placement.
3. **Pass:** terrain is lowered without first digging; WEC undo restores the original terrain.
4. **Fail:** command raises or resets terrain, snapping controls do not match the existing Raise tool, or undo fails.

### S07 — Precision Area Pipette activate (M1)
**Rule:** Selecting the tool runs `sonic_precision_pipette` and activates the controller. **Maps:** §18.5–6.
1. Select Precision Area Pipette from the Hammer tools menu (or run `sonic_precision_pipette`).
2. **Pass:** log `Precision Area Pipette active.`; built-in Pipette / Building / Area pipettes still work; repeated select does not stack handlers.
3. **Fail:** no activate log, exception, or broken stock IH pipettes.

### S08 — SettingChanged hot-reload (M1)
**Rule:** `Enabled` / `InstallInfinityHammerTools` re-run install or deactivate without deleting YAML.
1. Toggle `InstallInfinityHammerTools` or `Enabled` false in cfg / Configuration Manager.
2. **Pass:** controller deactivates if active; log that owned YAML remains on disk; file not deleted.
3. Re-enable both; confirm install log again.
4. **Fail:** YAML deleted automatically, or no SettingChanged reaction.

### S09 — Plugins-folder restart
**Rule:** Release-style install still loads.
1. `deploy -Mod SonicWorldTools -Plugins`, restart game, enter world.
2. **Pass:** same as S01–S02 (and S05 if IH present).
3. **Fail:** load failure after restart.

### S10 — Corner A marker (M3 / §18.6–8)
**Rule:** Activating Precision Area Pipette shows a live green aim reticle immediately; left-click sets Corner A. **Maps:** A06–A08.
1. Activate Precision Area Pipette (`sonic_precision_pipette` or Hammer tools menu).
2. **Pass (pre-click):** HUD armed + green aim crosshair follows look aim (no need to click first); IH Place/Remove ghost not competing.
3. Aim at a build piece; left-click.
4. **Pass:** log/HUD shows Corner A; green marker stays at that point; no world modification.
5. **Fail:** no aim until first click, no marker, exception, or pieces deleted/copied.

### S11 — Live width preview (M3 / §18.9–10)
**Rule:** After Corner A, cursor motion updates a non-opaque live AABB ribbon (width edge A→aim). **Maps:** A09–A10.
1. From S10, move the camera/cursor across the structure.
2. **Pass:** cyan wire box follows aim along the width edge; building remains visible through the preview.
3. **Fail:** no box, opaque blocker, or freeze/exception.

### S12 — Corner B then Corner C lock (M3 / §18.11–12)
**Rule:** Second left-click sets Corner B (width); third left-click sets Corner C and locks a Y-up oriented box. **Maps:** A11–A12.
1. Left-click opposite end of the width edge (Corner B).
2. **Pass (B):** amber Corner B marker; HUD asks for Corner C (depth/height); magenta aim follows look; oriented wire box updates with aim.
3. Left-click the far depth/height corner (Corner C).
4. **Pass (C):** magenta Corner C marker; box stops following cursor; HUD shows locked selection count/dims; oriented = true in log.
5. **Fail:** locks after only two clicks, no Corner C phase, box keeps drifting, or selection clears unexpectedly.

### S13 — Second-floor-only selection (M3 / §18.13–16) — primary gate
**Rule:** Origin-mode volume can select only the second floor of a multi-story test build. **Maps:** A13–A16 / MVP-9.
1. Use / build a 3-floor structure (§17).
2. Box only the second floor (corners on second-floor surfaces; Y band excludes floor 1 and 3).
3. **Pass:** only second-floor pieces highlighted; first/third floors not selected; selected count matches visible highlights.
4. **Fail:** other floors included, count mismatch, or no highlights.

### S14 — HUD count and dimensions (M3)
**Rule:** With `ShowSelectedCount` / `ShowDimensions` true, HUD shows count and W×D×H.
1. Complete a locked selection with both config flags true.
2. **Pass:** HUD lines for object count and meters; toggling flags hides the corresponding line after reload/SettingChanged.
3. **Fail:** missing HUD or wrong dimensions vs box.

### S15 — Escape cancels selection (M3)
**Rule:** Escape clears the current precision selection without leaving a dirty preview or opening pause.
1. Set Corner A (and optionally B); press Escape.
2. **Pass:** markers/box/highlights clear; tool remains armed for a new Corner A; log notes cancel; pause menu does **not** open.
3. Press Escape again at idle Corner A.
4. **Pass:** pause menu may open (Esc no longer owned by pipette).
5. **Fail:** visuals stuck on, hard deactivate, or pause opens on the cancel press.

### S16 — Exclude selected piece (M4 / §18.17–18)
**Rule:** Alt+MMB excludes one selected piece from the effective set. **Maps:** A17–A18.
1. Lock a volume selection that includes at least one wall.
2. Hover that wall; Alt+MMB.
3. **Pass:** piece leaves effective selection (count drops); red exclude tint; highlights match count; no IH Remove / sit.
4. **Fail:** still counted/highlighted as selected, Remove fires, or exception.

### S17 — Add adjacent piece outside box (M4 / §18.19–20)
**Rule:** Alt+LMB adds a valid unselected piece (may be outside the box). **Maps:** A19–A20.
1. From a locked selection, hover an adjacent wall outside the box.
2. Alt+LMB.
3. **Pass:** piece joins effective selection (green add tint); count increases; still highlighted after release.
4. **Fail:** no add, wrong piece, count/highlight mismatch, or locked box wiped into a new Corner A.

### S18 — Persist refinements on resize (M4 / §18.21–22)
**Rule:** With `PersistRefinementsAfterResize=true`, a new volume keeps valid add/exclude. **Maps:** A21–A22.
1. From S16–S17 (added + excluded present), press **Home** (reset corners — plain LMB while Locked does not resize), then set new A→B→C.
2. **Pass:** excluded ZDO still excluded if present in world; added ZDO still added if valid; HUD count matches highlights.
3. Optionally set `PersistRefinementsAfterResize=false`, repeat: refinements clear; base-only selection.
4. **Fail:** refinements wiped when config is true, or persist when config is false.

### S19 — Pick-up / copy hand-off (M5 / §18.23–24)
**Rule:** Locked effective selection becomes Infinity Hammer multi-object placement ghost (copy). **Maps:** A23–A24.
1. Lock a refined selection (S13–S17).
2. Press **Shift+MMB** (same as IH copy; or run `sonic_precision_pipette_pickup`).
3. **Pass:** log `Handed off N object(s) to Infinity Hammer placement ghost`; Sonic pipette deactivates; IH placement ghost shows; originals remain in place (copy).
4. **Fail:** no ghost, Sonic stays fighting placement, originals deleted, or exception.

### S20 — Ghost matches highlights (M5 / §18.24)
**Rule:** Ghost children match exactly the highlighted effective set. **Maps:** A24.
1. From S19, compare ghost object count / identities to pre-hand-off highlights.
2. **Pass:** same pieces as were highlighted (count and membership).
3. **Fail:** extra / missing ghost pieces vs highlights.

### S21 — Excluded absent from ghost (M5 / §18.25)
**Rule:** Manually excluded pieces are not in the IH ghost. **Maps:** A25.
1. Exclude one piece (S16), then pick up (Shift+MMB / `sonic_precision_pipette_pickup`).
2. **Pass:** excluded piece remains in world and is not part of the placement ghost.
3. **Fail:** excluded piece appears in ghost.

### S22 — Manually added present in ghost (M5 / §18.26)
**Rule:** Manually added pieces are in the IH ghost. **Maps:** A26.
1. Add an outside-box piece (S17), then pick up.
2. **Pass:** added piece is included in the placement ghost.
3. **Fail:** added piece missing from ghost.

### S23 — Place elsewhere (M5 / §18.27)
**Rule:** Copied selection can be placed with normal IH placement. **Maps:** A27.
1. After S19 hand-off, aim elsewhere and place with IH (vanilla place / IH place binds).
2. **Pass:** copy appears at new location; no Sonic preview leftovers.
3. **Fail:** cannot place, wrong location, or Sonic re-takes control.

### S24 — Normal Infinity Hammer controls (M5 / §18.28)
**Rule:** After hand-off, freeze / rotate / snap behave as stock IH. **Maps:** A28.
1. With the ghost from S19, exercise IH freeze, rotation, and snapping.
2. **Pass:** controls match normal multi-object IH selection; no Sonic input conflicts.
3. **Fail:** freeze/rotate/snap broken or Sonic intercepts keys.

### S25 — Console-free workflow (M6)
**Rule:** Full pipette flow works from Hammer tools menu + binds only (no console commands).
1. Select **Precision Select** from the IH tools menu.
2. Set Corner A/B with LMB, refine with Alt+LMB / Alt+MMB, pick up with Enter, place with IH.
3. **Pass:** complete select → refine → pick up → place without typing console commands.
4. **Fail:** must use `sonic_precision_pipette` / `_pickup` to finish the flow.

### S26 — Reset corners (M6)
**Rule:** `ResetCornersKey` (default Home) clears volume and returns to Corner A.
1. Set Corner A (and optionally B / Locked); press R.
2. **Pass:** markers/box/highlights clear; HUD prompts Corner A; tool stays armed; log notes corners reset.
3. **Fail:** stale preview or hard deactivate.

### S27 — Reset refinement (M6)
**Rule:** `ResetRefinementKey` (default Backspace) clears add/exclude only while Locked.
1. Lock a selection; add one piece and exclude one; press X.
2. **Pass:** count returns to base-only; corners/box remain; add/exclude tints cleared.
3. **Fail:** corners cleared, or refinements persist.

### S28 — Preview/HUD SettingChanged (M6)
**Rule:** Preview and HUD toggles apply without restart.
1. With an active locked selection, toggle `Preview.ShowHighlights` / `ShowBox` / `ShowCornerMarkers` and `ShowDimensions` / `ShowSelectedCount` in cfg or Configuration Manager.
2. **Pass:** overlays and HUD lines update immediately (no F9 / restart).
3. **Fail:** change ignored until reload.

### S29 — DragNBuild / IH input friction note (M6 → M7)
**Rule:** Friction documented; full DragNBuild smoke is A30.
1. Read `docs/integration/input-friction-notes.md`.
2. If DragNBuild is installed and corner clicks feel stolen, note it for M7 (rebind or disable DragNBuild).
3. **Pass:** note exists; RMB still opens Hammer menu while pipette is active.
4. **Fail:** Sonic binds RMB, or no friction note for M7.

### A01 — Install with Infinity Hammer (§18.1)
**Rule:** SonicWorldTools installs alongside Infinity Hammer without conflict.
1. Profile has BepInEx + Server Devcommands + Infinity Hammer (+ WEC as required by IH) + SonicWorldTools.
2. **Pass:** all load; no hard dependency crash from Sonic.
3. **Fail:** missing hard dep or load failure unique to this stack.

### A02 — Launch game (§18.2)
**Rule:** Game reaches main menu / world with Sonic loaded.
1. Launch Gale profile; enter a local world.
2. **Pass:** S01 load line present; world usable.
3. **Fail:** crash before world or Sonic exception on boot.

### A03 — No new physical Hammer (§18.3)
**Rule:** Sonic does not add a separate Hammer item.
1. Check inventory / piece tables for a new Sonic hammer item.
2. **Pass:** only Infinity Hammer’s existing Hammer equipment; tool is a menu entry only.
3. **Fail:** new craftable/equipable hammer from this mod.

### A04 — Precision Area Pipette in menu (§18.4)
**Rule:** Tool appears in Hammer tool menu. Same evidence as S06 when IH present.
1. Open Hammer tools menu.
2. **Pass:** `Precision Select` listed.
3. **Fail:** missing entry.

### A05 — Stock IH pipettes still work (§18.5)
**Rule:** Normal Pipette, Building Pipette, Area Pipette unchanged when Sonic inactive.
1. With Sonic **inactive**, exercise stock IH pipettes.
2. **Pass:** each stock pipette behaves as before Sonic was installed.
3. **Fail:** regression in stock pipettes.

### A06 — Select Precision Area Pipette (§18.6)
**Maps:** S07 / S10 start. Activate from menu; log `Precision Area Pipette active.`

### A07 — Set Corner A (§18.7)
**Maps:** S10.

### A08 — Corner A marker visible (§18.8)
**Maps:** S10.

### A09 — Move cursor (§18.9)
**Maps:** S11.

### A10 — Live box updates (§18.10)
**Maps:** S11.

### A11 — Set Corner B then Corner C (§18.11)
**Maps:** S12. B = width edge; C = depth/height (locks oriented box).

### A12 — Box locks after Corner C (§18.12)
**Maps:** S12.

### A13 — Box second floor only (§18.13)
**Maps:** S13. Primary success criterion.

### A14 — First floor not selected (§18.14)
**Maps:** S13.

### A15 — Third floor not selected (§18.15)
**Maps:** S13.

### A16 — Count matches highlights (§18.16)
**Maps:** S13 / S14.

### A17 — Exclude one wall (§18.17)
**Maps:** S16.

### A18 — Excluded leaves selection (§18.18)
**Maps:** S16.

### A19 — Add adjacent wall outside box (§18.19)
**Maps:** S17.

### A20 — Added joins selection (§18.20)
**Maps:** S17.

### A21 — Resize box (§18.21)
**Maps:** S18.

### A22 — Refinement persistence (§18.22)
**Maps:** S18.

### A23 — Trigger pick/copy (§18.23)
**Maps:** S19.

### A24 — Ghost equals highlights (§18.24)
**Maps:** S19–S20.

### A25 — Excluded absent from ghost (§18.25)
**Maps:** S21.

### A26 — Added present in ghost (§18.26)
**Maps:** S22.

### A27 — Place copied selection (§18.27)
**Maps:** S23.

### A28 — Normal IH placement controls (§18.28)
**Maps:** S24.

### A29 — Switch to normal Pipette deactivates Sonic (§18.29)
**Rule:** Sonic inputs stop when switching away from Precision Area Pipette.
1. Activate Sonic; set at least Corner A (markers visible).
2. Select Infinity Hammer’s normal **Pipette** (or another non-Sonic tool that creates a placement ghost).
3. **Pass:** log notes deactivate / tool inactive; Sonic markers/highlights clear; LMB/Alt refine no longer drive Sonic selection.
4. **Fail:** Sonic keeps consuming clicks or leaves stale preview.

### A30 — DragNBuild no conflict when Sonic inactive (§18.30)
**Rule:** DragNBuild works normally outside Sonic tool.
1. Deactivate / leave Sonic pipette (A29 or Escape then unequip).
2. Use DragNBuild place/drag as usual.
3. **Pass:** DragNBuild behavior matches pre-Sonic install when Sonic is inactive. (While Sonic is active, see S29 / friction notes.)
4. **Fail:** DragNBuild broken even with Sonic inactive.

### A31 — Build Camera preview usable (§18.31)
**Rule:** Build Camera does not break Sonic preview or stock building when Sonic inactive.
1. With Build Camera available, enter build camera; activate Sonic briefly and confirm box preview still readable; leave Sonic inactive and place a piece.
2. **Pass:** preview usable; no stuck Sonic overlays after deactivate.
3. **Fail:** camera/preview unusable or overlays stuck.

### A32 — Precision rotation on ghost (§18.32)
**Rule:** Precision rotation mods on IH placement ghost after hand-off.
1. Complete S19 hand-off; use precision rotation on the ghost.
2. **Pass:** rotation works; Sonic does not intercept rotation binds.
3. **Fail:** rotation broken or Sonic steals input.

### A33 — Dismantleheim no shared state (§18.33)
**Rule:** Dismantleheim has no shared static/input leakage with Sonic.
1. With Dismantleheim installed, deactivate Sonic; use Dismantleheim flow.
2. Activate Sonic briefly, deactivate, use Dismantleheim again.
3. **Pass:** Dismantleheim unaffected; no Sonic highlights/input while inactive.
4. **Fail:** shared state, stolen input, or stuck Sonic visuals.

### MP01 — Dedicated server pick + place (§20)
**Rule:** Authorized client can run pipette → hand-off → place on a dedicated server without requiring Sonic on other clients.
1. Join dedicated server as admin/authorized (Server Devcommands permissions as required by IH).
2. Run S25-style flow (select → refine → pick up → place).
3. Optional: vanilla observer client confirms placed pieces appear.
4. **Pass:** place succeeds; wards/ownership/devcommand rules still apply; observers see result without Sonic installed.
5. **Fail:** server reject unique to Sonic, or other clients cannot see placed build.

### Offline — Automated §16 suite
**Rule:** Non-Unity selection/refinement/YAML installer tests pass. **Maps:** MVP-10 / §16.
1. From mod root: `dotnet test SonicWorldTools.sln`
2. **Pass:** all tests green (see version table).
3. **Fail:** any failing test.

## 0.1.0

| ID | Procedure | §18 / notes | Result |
| --- | --- | --- | --- |
| Offline | `dotnet test` full suite (41 + PluginAudit) | MVP-10 | pass |
| S01 | BepInEx load line | hygiene | pass |
| S02 | No exception in Gale logs | hygiene | pass |
| S03 | Optional dependency detect lines | §11 | pass |
| S04 | Config file created | §13 | pass |
| S05 | Owned Infinity Hammer YAML install (M1) | A01–A02 / §9 | pass |
| S06 | Hammer menu hot-reload (M1) | A04 | pass |
| S06a | Curved Terrain Path Surface Variants | WEC / IH multi-point tool | pending |
| S06b | Lower Terrain | WEC / IH Hoe tool | pending |
| S07 | Precision Area Pipette activate (M1) | A05–A06 | pass |
| S08 | SettingChanged hot-reload (M1) | config | pending |
| S09 | Plugins-folder restart | release install | pending |
| S10 | Corner A marker (M3) | A06–A08 | pass |
| S11 | Live box preview (M3) | A09–A10 | pass |
| S12 | Corner B then C lock (M3) | A11–A12 | pass |
| S13 | Second-floor-only selection (M3) | A13–A16 | pass |
| S14 | HUD count and dimensions (M3) | A16 | pending |
| S15 | Escape cancels selection (M3) | UX | pass |
| S16 | Exclude selected piece (M4) | A17–A18 | pass |
| S17 | Add adjacent piece outside box (M4) | A19–A20 | pass |
| S18 | Persist refinements on resize (M4) | A21–A22 | pass |
| S19 | Pick-up / copy hand-off (M5) | A23–A24 | pending |
| S20 | Ghost matches highlights (M5) | A24 | pending |
| S21 | Excluded absent from ghost (M5) | A25 | pending |
| S22 | Manually added present in ghost (M5) | A26 | pending |
| S23 | Place elsewhere (M5) | A27 | pending |
| S24 | Normal Infinity Hammer controls (M5) | A28 | pending |
| S25 | Console-free workflow (M6) | workflow | pending |
| S26 | Reset corners (M6) | UX | pending |
| S27 | Reset refinement (M6) | UX | pending |
| S28 | Preview/HUD SettingChanged (M6) | UX | pending |
| S29 | DragNBuild / IH input friction note | → A30 | pending |
| A01 | Install with Infinity Hammer | §18.1 | pending |
| A02 | Launch game | §18.2 | pending |
| A03 | No new physical Hammer | §18.3 | pending |
| A04 | Precision Area Pipette in menu | §18.4 | pending |
| A05 | Stock IH pipettes still work | §18.5 | pending |
| A06 | Select Precision Area Pipette | §18.6 → S07 | pending |
| A07 | Set Corner A | §18.7 → S10 | pass |
| A08 | Corner A marker visible | §18.8 → S10 | pass |
| A09 | Move cursor | §18.9 → S11 | pass |
| A10 | Live box updates | §18.10 → S11 | pass |
| A11 | Set Corner B | §18.11 → S12 | pass |
| A12 | Box locks | §18.12 → S12 | pass |
| A13 | Box second floor only | §18.13 → S13 | pass |
| A14 | First floor not selected | §18.14 → S13 | pass |
| A15 | Third floor not selected | §18.15 → S13 | pass |
| A16 | Count matches highlights | §18.16 → S13 | pass |
| A17 | Exclude one wall | §18.17 → S16 | pass |
| A18 | Excluded leaves selection | §18.18 → S16 | pass |
| A19 | Add adjacent wall outside box | §18.19 → S17 | pass |
| A20 | Added joins selection | §18.20 → S17 | pass |
| A21 | Resize box | §18.21 → S18 | pass |
| A22 | Refinement persistence | §18.22 → S18 | pass |
| A23 | Trigger pick/copy | §18.23 → S19 | pending |
| A24 | Ghost equals highlights | §18.24 → S19–S20 | pending |
| A25 | Excluded absent from ghost | §18.25 → S21 | pending |
| A26 | Added present in ghost | §18.26 → S22 | pending |
| A27 | Place copied selection | §18.27 → S23 | pending |
| A28 | Normal IH placement controls | §18.28 → S24 | pending |
| A29 | Switch to Pipette deactivates Sonic | §18.29 | pending |
| A30 | DragNBuild when Sonic inactive | §18.30 | pending |
| A31 | Build Camera preview usable | §18.31 | pending |
| A32 | Precision rotation on ghost | §18.32 | pending |
| A33 | Dismantleheim no shared state | §18.33 | pending |
| MP01 | Dedicated server pick + place | §20 | pending |

## §25 MVP checklist (0.1.0)

| # | MVP bullet | Status |
| --- | --- | --- |
| 1 | Precision Area Pipette in IH Hammer menu | **Code-complete** — in-game verify pending (S06/A04) |
| 2 | Oriented rectangular prism via three points | **pass** (S10–S12, 2026-10-05) |
| 3 | Only pieces inside prism selected | **pass** (S13, 2026-10-05) |
| 4 | Selection visibly accurate | **pass** (S13; S14 HUD dims still pending) |
| 5 | Add / exclude individual pieces | **pass** (S16–S17, 2026-10-05) |
| 6 | Refined set → IH placement ghost | **Code-complete** — pending (S19–S22) |
| 7 | Normal IH placement controls after hand-off | **Code-complete** — pending (S23–S24) |
| 8 | No other mod behavior change when Sonic inactive | **Isolation coded** (tool-switch deactivate) — pending (A29–A33) |
| 9 | Middle floor only on three-story test build | **pass** selection (S13); copy flow still S19+ |
| 10 | Automated tests for non-Unity selection/refinement | **pass** (`dotnet test`, 2026-10-05) |

**Verdict:** Offline / code path for MVP 0.1.0 is in place. **Not release-ready** until in-game rows (and MP01 if shipping multiplayer claims) are marked `pass`. Do not run `release.ps1` / tag until the version table is all-pass.
