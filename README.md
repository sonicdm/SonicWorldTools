# SonicWorldTools

Client-side Valheim tools for precise world selection and building workflows, delivered through Infinity Hammer’s existing Hammer and Hoe menus.

| | |
| --- | --- |
| Version | 0.1.0 |
| GUID | `com.sonicdm.valheim.sonicworldtools` |
| Thunderstore hard deps | BepInEx, Jötunn |
| Runtime stack (via Infinity Hammer) | Server Devcommands, Infinity Hammer; World Edit Commands is a **hard dependency of Infinity Hammer** (transitive — install with IH, not listed again here) |

## Dependencies

| Package | Role |
| --- | --- |
| **BepInEx** Pack Valheim | Required (manifest). |
| **Jötunn** | Required (manifest). |
| **Server Devcommands** | Required by Infinity Hammer for build commands / admin tooling. Install with IH. |
| **Infinity Hammer** | Soft-detected at runtime. Precision Area Pipette YAML + hand-off need IH. Without IH the mod loads but the pipette stays disabled. |
| **World Edit Commands** | Soft-detected. Already required by current Infinity Hammer releases — do not add a redundant Thunderstore hard dependency unless IH drops it. |

Compatibility targets (no direct integration for MVP): DragNBuild, Build Camera, precision rotation on ghosts, Dismantleheim. See `docs/test-matrix.md` (§18.29–33) and `docs/integration/input-friction-notes.md`.

## Features (0.1.0)

- Owned Infinity Hammer tool YAML (`infinity_tools_sonicworldtools.yaml`) — never edits IH’s `infinity_tools.yaml`. Selection appears in the Hammer menu, terrain tools in the Hoe menu, and prop scatter in the Cultivator menu.
- Precision Area Pipette: three-corner oriented volume (A→B width, C depth/height), configurable selection padding, refine add/exclude, copy hand-off to IH placement ghost.
- Lower Terrain: direct WEC lowering from the Hoe menu, matching Infinity Hammer’s Raise tool controls for amount, smoothing and grid snapping.
- Curved Terrain Paths: choose grass, patches, dark grass, dirt, cultivated soil, paved, mossy paved, or dark paved from the Hoe menu. Place a multi-point route and hold the IH modifier 2 key (default Left Ctrl) on the final point to finish. Also hold modifier 1 (default Left Alt) on that final point for softer terrain transitions at path edges. WEC curves and levels the terrain along the route and supports undo/redo. Mouse wheel adjusts half-width; default half-width is 2 m.
- Scatter Props: on the Cultivator, middle-click up to eight prop types to sample/remove them; Shift+middle-click clears the list. Click to scatter the sampled mix, or the hovered prop if none are sampled. Mouse wheel adjusts radius; WEC undo/redo applies to each sampled type.
- Piece Repeater: on the Hammer, repeat the currently selected IH build piece along a path. Pieces follow bends. Preview placements before committing.
- Inputs only while the Sonic tool is active; switching Hammer tools or leaving place mode deactivates Sonic.

### Piece Repeater controls

Choose **Piece Repeater** from the IH Hammer menu, then middle-click a placed networked building piece to sample it. Click route waypoints and Ctrl+click to place. Q/E cycle Automatic and the source piece's build snap points for the first placement. That first piece stays at its clicked position and rotation. The route begins at whichever horizontal end is nearest the second waypoint. Free waypoints shape the route; with zero added gap each later EdgeChain piece connects to the previous piece's horizontal end joint, so a tight turn can cut across the drawn corner or overlap. Hold Right Alt while clicking to move a waypoint to the nearest measured piece joint. A short snapped leg can return to its previous joint. Wheel rotates each subsequent piece around its own position; Alt+wheel adds or removes a gap, starting at zero. Repeated profiles use build-piece surfaces beneath them when available, then terrain; ground props repeat at their configured default interval plus that gap and face the route tangent by default. The F7 panel can keep prop facing fixed instead. This surface search works with any piece collider shape, not just square blocks. Shift keeps placements at the first point's height. Press F7 while this tool is active to open its settings panel, which pauses placement and frees the cursor. The panel controls path interpolation (Linear or local Bezier), smoothing distance, added gap, height mode, and Tangent/Fixed prop facing. Press F7 again or click Close to resume. Escape exits the tool and opens the game menu. The F7 key and modifiers are configurable in the BepInEx config.

After a world loads, SonicWorldTools scans loaded build-piece tables and creates `BepInEx/config/SonicWorldTools/PrefabPlacement/PieceProfiles.yml`. It adds new entries without rewriting edited ones. Run `sonic_piece_catalog_refresh` after installing new building mods. Existing `VanillaPieces.yml` values remain readable as overrides. The generated file records `ProfileType` (`EdgeChain` or `GroundProp`), `Status` (`Ready` or disabled `NeedsReview`), `ShapeAxis`, local `JoinStart`/`JoinEnd`, `ShapeBottomOffset`, and `MeasurementSource`. Horizontal joints determine EdgeChain spacing; vertical placement resolves against any build-piece collider beneath the repeat, then terrain. Profiles can describe square, rounded, or irregular pieces through per-prefab join and offset values. Standard floor panels and modular 1×1 walls repeat along measured snap edges. Standalone vertical columns use GroundProp spacing and curve-facing behavior, like lamps. Free-standing ground props use the configurable 2 m shared spacing unless their YAML entry sets `DefaultSpacing`; `TangentYawOffset` corrects only their tangent-facing repeats without changing the first piece. Correct questionable entries and change `Status` to `Ready` before using them. `sonic_piece_geometry_dump` exports catalog, join, snap, and visible geometry measurements for calibration. YAML edits reload automatically.

In-game publish checks: `docs/test-matrix.md`. Offline selection/refinement tests: `dotnet test`.

## Dev

```powershell
.\build.ps1
..\tools\dev.ps1 deploy -Mod SonicWorldTools
```

## License

See repository license file when published.
