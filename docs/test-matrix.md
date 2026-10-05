# SonicWorldTools publish test matrix

Manual checks before a `v*` tag. Everyday edits stop at `.\build.ps1` and ScriptEngine deploy.
Procedures stay the same across versions. Copy this section forward for a new version and reset results to `pending`.

## How to run

1. `.\build.ps1` then `..\tools\dev.ps1 deploy -Mod SonicWorldTools` (ScriptEngine). F9 reload after each fix.
2. Work the checks below in order. Use ValheimMCP for log/console/render rows while you play.
3. Logs: `C:\Users\Allan\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Default\BepInEx\LogOutput.log`.
4. After ScriptEngine passes: `..\tools\dev.ps1 deploy -Mod SonicWorldTools -Plugins`, restart Valheim, re-check S01–S03.
5. Mark results `pass` / `fail`. Tag only when every result under the version heading is `pass`.

## Procedures

### S01 — BepInEx load line
**Rule:** Plugin loads for this version.
1. Deploy and enter a world.
2. Check logs for `SonicWorldTools`.
3. **Pass:** load line `SonicWorldTools 0.1.0 loaded.`
4. **Fail:** missing load or immediate exception.

### S02 — No exception in Gale logs
**Rule:** Clean log for this mod after the session.
1. Search Gale logs for SonicWorldTools / exceptions.
2. **Pass:** no exception from this mod.
3. **Fail:** any exception from this mod.

### S03 — Optional dependency detect lines
**Rule:** IH/WEC detection logs without throwing when absent.
1. Note whether Infinity Hammer / World Edit Commands are installed.
2. **Pass:** info lines for detected or not detected; no error.
3. **Fail:** exception during `DependencyDetector`.

### S04 — Config file created
**Rule:** BepInEx config binds on first run.
1. Confirm `BepInEx\config\com.sonicdm.valheim.sonicworldtools.cfg` exists after load.
2. **Pass:** `General.Enabled` and `General.DebugLogging` present.
3. **Fail:** missing config or bind errors in log.

### S05 — Tool install deferred (M0)
**Rule:** M0 does not merge YAML into IH yet.
1. Check log for deferred M1 tool install message.
2. **Pass:** message present; no write to `infinity_tools.yaml`.
3. **Fail:** unexpected YAML merge or IH errors.

### S06 — Plugins-folder restart
**Rule:** Release-style install still loads.
1. `deploy -Mod SonicWorldTools -Plugins`, restart game, enter world.
2. **Pass:** same as S01–S02.
3. **Fail:** load failure after restart.

## 0.1.0

| ID | Procedure | Result |
| --- | --- | --- |
| S01 | BepInEx load line | pending |
| S02 | No exception in Gale logs | pending |
| S03 | Optional dependency detect lines | pending |
| S04 | Config file created | pending |
| S05 | Tool install deferred (M0) | pending |
| S06 | Plugins-folder restart | pending |
