# Changelog

## Unreleased

- Scatter Props previews the next scatter at the Infinity Hammer cursor: translucent ghosts of the sampled props plus a marker post at each spot. A click spawns exactly the previewed layout, then rolls a new one. `ScatterProps.ShowPreview` hides the ghosts.
- Each Scatter Props click is now a single World Edit Commands undo step, even with several sampled prop types.
- Scatter variation: random size (`ScaleMin`/`ScaleMax`, default 0.9–1.1), optional tilt to the ground slope (`AlignToSlope`, `MaxSlopeTilt`), minimum spacing between props (`MinSpacing`), and per-sample pick weights (`sonic_scatter weight <prefab> <n>`). The preview shows all of it.
- Scatter Props sample notices (sampled, removed, cleared, palette full) show in the centre of the screen so they no longer queue behind undo and spawn messages; the per-click summary goes to the console only.
- Line Tool trace wall: look at an existing wall, open the settings (F7) and press Trace wall to lay the sampled piece in a parallel row along your side of it, following the wall's curve. Shift+wheel or the panel nudges the offset; Next row steps out one piece depth. Optional `TraceKey` (unbound by default). Removed the unused "both sides" path code it replaces.
- Scatter Props sets props on terrain, or on a rock or build floor within 2 m of it, instead of the first solid surface (which could be a tree canopy).
- Infinity Hammer is now a hard BepInEx dependency; World Edit Commands and Server Devcommands stay runtime-detected.
- Split the Line/Shape Tool controller into partial files by concern (no behaviour change).

## 0.1.1

- Declare Infinity Hammer, World Edit Commands, and Server Devcommands as package dependencies, using the versions installed in the DEV test profile.

## 0.1.0

- Add Line Tool for previewing and placing build-piece paths, with editable piece profiles and in-game settings.
- Add Shape Tool for previewing and placing sampled pieces around circle, square, and rectangle outlines; Q cycles shapes.
- Add Shape Tool infill toggle and fit outline side lengths to the sampled repeat spacing for cleaner corners.
- Arrange Circle infill in concentric rings rather than staggered rows.
- Add curved terrain surface paths, Lower Terrain, Scatter Props, and Precision Area Pipette tools to Infinity Hammer's menus.
- Generate tool definitions and a default piece catalog on first run while preserving profile edits.
- Expose tool controls in the BepInEx config and keep optional Line Tool diagnostics off by default.
