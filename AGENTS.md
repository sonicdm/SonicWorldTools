# AGENTS.md — SonicWorldTools

Guidance for Cursor agents (and humans) working in this repo.

## What this is

Valheim **BepInEx** mod: world-building utilities delivered as tools inside Infinity Hammer's menus. Current tools: Line Tool, Shape Tool, Precision Select (Hammer); Surface Paths, Lower Terrain (Hoe); Scatter Props (Cultivator).

| | |
| --- | --- |
| GUID | `com.sonicdm.valheim.sonicworldtools` |
| Assembly | `SonicWorldTools.dll` |
| Source | `src/SonicWorldTools/` |

## Product rules

- **One Hammer** — register tools only via our owned YAML fragment (`assets/infinity_tools_sonicworldtools.yaml`) merged by Infinity Hammer at install time. Do not ship separate hammer items.
- **Scope** — the original non-goals (dismantle/delete, terrain editing, zoop-style repeat tools, vegetation/prop scatter) are dropped: these tools are not available in other mods in the form we need. Still avoid duplicating what Infinity Hammer, WEC, or Dismantleheim already do well, and still never ship a replacement Hammer, build menu, or generic world editor.
- **Never edit `infinity_tools.yaml`** — that file belongs to Infinity Hammer. Only maintain `infinity_tools_sonicworldtools.yaml` and the installer bridge (M1+).
- **Dependencies** — Infinity Hammer is a **hard** dependency: `[BepInDependency(InfinityHammerGuid)]` on the plugin (GUID `infinity_hammer`) and listed in `manifest.json`. World Edit Commands and Server Devcommands are Thunderstore package dependencies but are still detected at runtime; their GUIDs are confirmed from the BepInEx log (`world_edit_commands`, `server_devcommands`), so a hard `[BepInDependency]` is possible, but they stay soft until a feature truly cannot work without them. Keep `InfinityHammerBridge` reflection guarded: a hard dependency guarantees load order, not API compatibility across IH versions.
- **Client-only** — `[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]`. Skip headless-only work until features need it; no custom server RPCs in M0.
- **Harmony** — prefer Jötunn hooks. If patches are added, unpatch **only this plugin's Harmony id** on shutdown (`UnpatchSelf` / targeted unpatch). Never `UnpatchAll()`.

## Valheim references (local only)

```text
E:\Scripts\Valheim Mods\Reqs
```

Override with `-LibDir` on `build.ps1` / `package.ps1` / `release.ps1`.

Refresh that folder with `..\tools\sync-refs.ps1` (copies from the game's Managed folder and the Gale profile; `-DryRun` previews). On a machine without the game, `-From NuGet` pulls the same pinned packages CI uses.

- Never commit those DLLs or the Reqs folder.
- CI compiles against pinned public Valheim/Jötunn refs. Tag publish uploads Hexium/Thunderstore after verify + all-pass `docs/test-matrix.md`.

## Build habits

```powershell
.\build.ps1
..\tools\dev.ps1 deploy -Mod SonicWorldTools
```

After code changes, verify with `.\build.ps1` before finishing. Prefer **build and ScriptEngine deploy** while iterating — do not package/release unless asked.

## Version bumps

Stay on **`0.1.x`** while iterating. Align `PluginVersion`, csproj `<Version>`, `manifest.json`, README, and `CHANGELOG.md` when shipping.

## Install

Do **not** hand-copy the DLL. `..\tools\dev.ps1 deploy -Mod SonicWorldTools` for ScriptEngine testing. Build output: `bin\Release\SonicWorldTools.dll`.

## Troubleshooting

Player BepInEx log (Gale / r2modman Default profile):

```text
C:\Users\Allan\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Default\BepInEx\LogOutput.log
```

On crash / tool issues, read the end of that file first (search `SonicWorldTools`, `Exception`).
