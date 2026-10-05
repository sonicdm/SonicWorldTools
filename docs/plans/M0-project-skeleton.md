# M0 — Project skeleton

**Roadmap:** [ROADMAP.md](../ROADMAP.md) · **Spec:** [build-plan.md](../build-plan.md) §15, §22 (M0), §13–§14, §21

## Goal

Establish the SonicWorldTools BepInEx plugin shell: solution layout per build plan, plugin loads in Valheim without errors, configuration and dependency detection stubs, consistent logging, and a unit-test project ready for M2/M4 logic tests.

## Deliverables

Create or align paths under `SonicWorldTools/`:

| Path | Purpose |
|------|---------|
| `SonicWorldTools.sln` | Solution including plugin + tests |
| `src/SonicWorldTools/Plugin.cs` | BepInEx entry: load config, run dependency detection, log startup |
| `src/SonicWorldTools/Config/PluginConfig.cs` | BepInEx `ConfigFile` bindings for [General], [PrecisionAreaPipette], [Preview], [Integration], [Debug] per build plan §13 |
| `src/SonicWorldTools/Core/DependencyDetector.cs` | Detect Infinity Hammer, World Edit Commands, Server Devcommands; expose flags for later milestones |
| `tests/SonicWorldTools.Tests/` | Test project (xUnit or match sibling mods in workspace) |
| `tests/SonicWorldTools.Tests/` (placeholder test) | At least one smoke test so CI runs |
| `assets/infinity_tools_sonicworldtools.yaml` | Template asset (content finalized in M1; file may exist as stub) |
| `build.ps1` / existing packaging | Ensure build outputs `SonicWorldTools.dll` to standard BepInEx plugins path via workspace conventions |

Wire `Plugin.cs` to:

- Log `[SonicWorldTools] Infinity Hammer detected.` or `[SonicWorldTools] Infinity Hammer not detected. Precision Area Pipette disabled.` (§14, §21) without crashing when IH is absent.
- Respect `[General] Enabled` — when false, skip module registration (modules added in later milestones).

Do **not** implement selection, YAML install, or Infinity Hammer bridge in M0.

## Acceptance criteria

From build plan §22 — **Milestone 0**:

- Solution exists; BepInEx plugin loads.
- Config, dependency detection, logging, and test project are in place.

**Acceptance:** Valheim starts with the plugin enabled and **no errors** in BepInEx log.

## Non-goals / do-not

- Do not install or modify Infinity Hammer YAML (M1).
- Do not implement `SelectionVolume`, preview, or input (M2–M3).
- Do not add Harmony patches unless required for plugin load (prefer none in M0).
- Do not duplicate dismantling, zooping, terrain, build camera, or blueprint features (§23).
- Do not hard-require World Edit Commands if Infinity Hammer already pulls it transitively — only detect and log (§11).

## Start-here checklist (implementing agent)

1. Read [build-plan.md](../build-plan.md) §2 (principles), §11 (dependencies), §15 (structure).
2. Read [valheim-modder skill](file:///e:/Scripts/Valheim%20Mods/.cursor/skills/valheim-modder/SKILL.md) and match existing workspace mod layout (`SonicPickupFilters`, `Dismantleheim`) for `build.ps1`, BepInEx metadata, and `tools/dev.ps1` deploy.
3. Restructure or add `src/SonicWorldTools/` per §15 if the repo root currently holds a flat `.csproj` — keep a single plugin assembly `SonicWorldTools.dll`.
4. Implement `PluginConfig` with all sections from §13 (defaults as documented).
5. Implement `DependencyDetector` using BepInEx `Chainloader.PluginInfos` (or equivalent) for Infinity Hammer and World Edit Commands.
6. Add `SonicWorldTools.Tests` project; reference plugin internals only for testable types (tests grow in M2).
7. Build: `.\build.ps1` from mod folder.
8. Deploy: `..\tools\dev.ps1 deploy` from mod folder (ScriptEngine); confirm Valheim boot and clean log.
9. Mark M0 complete in your notes; proceed to [M1-tool-installation.md](M1-tool-installation.md).
