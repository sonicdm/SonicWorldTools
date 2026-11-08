# SonicWorldTools

**Draw the route. Preview the build. Place the whole line.**

SonicWorldTools adds building and terrain tools to Infinity Hammer's familiar Hammer, Hoe, and Cultivator menus. Line a winding road with fences or lamps, paint a curved path beneath it, scatter the surrounding props, and select an area when you want to move or remove a larger build.

> **The tools live where you already build:** Line Tool, Shape Tool, and Precision Select under Hammer, terrain paths and Lower Terrain under Hoe, and Scatter Props under Cultivator.

## What you can make

| Tool | Try it for… |
| --- | --- |
| **Line Tool** | Fence lines, curved walls, rows of lamps, posts, floors, and other supported build pieces along clicked waypoints. |
| **Shape Tool** | Closed circles, squares, or rectangles made from a sampled build piece. |
| **Surface Paths** | Curved dirt, grass, cultivated, and paved routes with adjustable width and edge smoothing. No lava. |
| **Scatter Props** | A mixed spread of rocks, plants, and other networked props sampled from the world. |
| **Precision Select** | Copying, dismantling, or deleting a three-corner build area, with individual pieces added or excluded. |
| **Lower Terrain** | Lowering ground by a chosen amount, with optional smoothing. |

### Line a path in four steps

1. Select **Line Tool** in the Infinity Hammer Hammer tools menu.
2. Point at a placed build piece and **middle-click** to sample it.
3. **Click** waypoints along the route. The preview updates as you go.
4. **Shift+click** to place the previewed pieces. Use Infinity Hammer's undo to reverse the batch.

The first piece uses Valheim's build snap behavior, including **Q/E** snap-point cycling. Repeated pieces follow the route using editable per-piece profiles. A bend may deliberately overlap solid parts of neighboring pieces to close an outside gap; short or very tight turns may still need another waypoint or a spacing adjustment.

| Line Tool control | Default |
| --- | --- |
| Sample a placed piece | Middle-click |
| Add a waypoint / place the batch | Click / Shift+click |
| Snap a waypoint to a repeat joint | Right Alt+click |
| Rotate repeated pieces / adjust gap | Wheel / Alt+wheel |
| Hold one height | Alt |
| Route settings / sampled-piece offsets | F7 / Shift+F7 |
| Exit the tool | Esc |

These bindings are configurable. The tool pane and generated Infinity Hammer description show the configured keys. Open **Line Tool settings** with F7 to switch curve interpolation. **A: Current** preserves the existing tangent-limited curve; **B: Centripetal** uses WEC-style centripetal Catmull–Rom. The switch updates the preview immediately, and the smoothing-distance control applies only to A.

### Place a shape outline

Select **Shape Tool** in the Infinity Hammer Hammer menu and middle-click a placed build piece to sample it. Click the outline center, move the cursor to size the preview, and press **Q** to cycle Circle, Square, and Rectangle. **Shift+click** places the outline. This version places the perimeter; interior infill can be added later.

### Shape the ground and surroundings

Under **Hoe**, choose a surface path and click its route points. Hold **Shift** on the last click to finish (Infinity Hammer's `<alt>` key, default Shift); also hold **Alt** (Infinity Hammer's modifier 1) there for softer edges. Scroll to change half-width. **Lower Terrain** lowers by the selected amount and can smooth with modifier 1.

Under **Cultivator**, choose **Scatter Props**. Middle-click networked props to add or remove them from your sample mix; Shift+middle-click clears it. Click to scatter the mix. With no samples, the tool uses the prop under your cursor. Alt+scroll changes the number spawned per click by one (1–200); the current count appears in the tool description. Scatter radius continues to follow Infinity Hammer’s radius-wheel bind.

With **Precision Select**, click three corners to define a volume. Alt+left-click adds an individual piece, Alt+middle-click excludes one, and Shift+middle-click hands the selection to Infinity Hammer's copy placement. Ctrl+Delete dismantles; Shift+Delete permanently removes. The selection preview and padding are configurable.

## Install

Install through your mod manager, or put `SonicWorldTools.dll` in `BepInEx/plugins`. Enter a world, then look in Infinity Hammer's Hammer, Hoe, and Cultivator menus.

**Requirements:** BepInEx and Jötunn load the plugin. Infinity Hammer, Server Devcommands, and World Edit Commands provide its menus and world-editing commands; install compatible versions together. The plugin can load without Infinity Hammer, but its menu tools will be unavailable.

SonicWorldTools creates its own `infinity_tools_sonicworldtools.yaml` under `BepInEx/config/tools/`. It does not edit Infinity Hammer's `infinity_tools.yaml`.

## Tune it

The main settings file is `BepInEx/config/com.sonicdm.valheim.sonicworldtools.cfg`.

| Section | Controls |
| --- | --- |
| `LineTool` | Keys, gap, curve sampler A/B, A-only smoothing distance, height and prop facing, default prop spacing, optional diagnostics. Previous `PieceRepeater` settings migrate on startup. |
| `TerrainPaths`, `LowerTerrain` | Terrain path width, edge smoothing, finish key and smooth key; initial lowering amount and smoothing. Path key changes regenerate the owned Infinity Hammer YAML. |
| `ScatterProps` | Sample and clear keys, palette limit, objects per click, initial radius. |
| `PrecisionAreaPipette`, `Preview` | Selection keys, padding, and visual feedback. |
| `Integration`, `Debug` | Infinity Hammer tool installation and optional diagnostics. Detailed logging defaults to off. |

Changes to relevant settings refresh the generated tool YAML. Infinity Hammer may need you to reselect the tool or restart the game before its menu shows the new description. If you edit that generated YAML yourself, the plugin keeps your changes and writes its newer template to `BepInEx/config/infinity_tools_sonicworldtools.yaml.new` for comparison.

Line Tool profiles live in `BepInEx/config/SonicWorldTools/PrefabPlacement/VanillaPieces.yml`. They hold each piece's repeat geometry, ground offset, and enabled status. Existing edits survive catalog refreshes; newly discovered build pieces are appended. A `NeedsReview` profile stays disabled until its geometry is corrected. Profile YAML edits reload automatically. Use `sonic_piece_catalog_refresh` after adding building mods, or `sonic_piece_geometry_dump` to inspect a piece's measured geometry. The embedded release defaults come from [the source catalog](src/SonicWorldTools/Resources/PrefabPlacement/VanillaPieces.yml).

## Remove or reset

Removing the DLL leaves the Infinity Hammer tool definition and your profile edits on disk. For a clean reset, close Valheim and remove the plugin's files from the profile:

```text
BepInEx/plugins/SonicWorldTools.dll
BepInEx/scripts/SonicWorldTools.dll       (DEV ScriptEngine installs only)
BepInEx/config/com.sonicdm.valheim.sonicworldtools.cfg
BepInEx/config/SonicWorldTools/
BepInEx/config/tools/infinity_tools_sonicworldtools.yaml
BepInEx/config/infinity_tools_sonicworldtools.yaml      (older installs)
BepInEx/config/infinity_tools_sonicworldtools.yaml.new  (if present)
```

Back up `VanillaPieces.yml` first if you tuned piece profiles. If enabled, Line Tool diagnostics are in `BepInEx/logs/SonicWorldTools-LineTool.log`.

## Development

Build with `./build.ps1`; run tests with `dotnet test tests/SonicWorldTools.Tests/SonicWorldTools.Tests.csproj`. The DEV ScriptEngine workflow is `../tools/dev.ps1 deploy -Mod SonicWorldTools -ProfileBepInEx <DEV profile BepInEx path>`. See the [in-game test matrix](docs/test-matrix.md).
