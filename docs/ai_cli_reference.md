# REE-Content-Exporter AI CLI Reference

This document is accessible to human users, but it is intended primarily for AI agents and advanced maintainers. It contains command-line details, maintenance rules, and release notes that are too dense for the human-facing README.

## Architecture

REE-Content-Exporter is a CLI/wizard wrapper around REE-Content-Editor and RE-Engine-Lib.

Expected development layout:

```text
parent-folder/
  REE-Content-Editor/
  REE-Content-Exporter/
```

Prepare the pinned dependency with:

```powershell
.\scripts\setup-content-editor-dependency.ps1 -Force
```

Build:

```powershell
dotnet build -c Release
```

Publish:

```powershell
dotnet publish -c Release -p:PublishProfile=win-x64-singlefile
```

## Universal Game Configuration

The wizard stores its config at:

```text
%LOCALAPPDATA%\REE-Content-Exporter\config.json
```

The selected game is stored in the JSON `game` property. The wizard does not reprompt for game selection while that property exists. To reselect a game, delete the `game` line and run the wizard again.

Downloaded REE.PAK.Tool lists are cached beside the config:

```text
%LOCALAPPDATA%\REE-Content-Exporter\lists\<list-file>.list
```

List source:

```text
https://raw.githubusercontent.com/Ekey/REE.PAK.Tool/refs/heads/main/Projects/
```

Supported wizard game IDs:

| Game ID | REE-Lib GameName | REE.PAK.Tool list |
| --- | --- | --- |
| `re2` | `re2` | `RE2_STM_Release.list` |
| `re2rt` | `re2rt` | `RE2_RT_STM_Release.list` |
| `re3` | `re3` | `RE3_STM_Release.list` |
| `re3rt` | `re3rt` | `RE3_RT_STM_Release.list` |
| `re4` | `re4` | `RE4_STM_Release.list` |
| `re7` | `re7` | `RE7_STM_Release.list` |
| `re7rt` | `re7rt` | `RE7_RT_STM_Release.list` |
| `re8` | `re8` | `RE8_STM_Release.list` |
| `re9` | `re9` | `RE9_STM_Release.list` |
| `dmc5` | `dmc5` | `DMC5_STM_Release.list` |
| `mhrise` | `mhrise` | `MHR_STM_Release.list` |
| `sf6` | `sf6` | `SF6_STM_Release.list` |
| `dd2` | `dd2` | `DD2_STM_Release.list` |
| `gtrick` | `gtrick` | `GTPD_STM_Release.list` |
| `apollo` | `apollo` | `AJ_AAT_STM_Release.list` |
| `drdr` | `drdr` | `DRDR_STM_Release.list` |
| `kunitsu` | `kunitsu` | `KGPG_STM_Release.list` |
| `oni2` | `oni2` | `O2_SD_STM_Release.list` |
| `mhwilds` | `mhwilds` | `MHWs_STM_Release.list` |
| `pragmata` | `pragmata` | `P_STM_Release.list` |
| `mhsto3` | `mhsto3` | `MHS3_TR_STM_Demo.list` |

## Command Line

General usage:

```powershell
REE-Content-Exporter.exe `
  --game <game-id> `
  --mesh "<primary.mesh.version>" `
  [mesh/material options] `
  [animation options] `
  [output/export options] `
  --output "<output.fbx|output.glb|output-folder>"
```

If `--game` is omitted, the exporter uses the saved wizard config when available. If neither exists, the export runs with `GameName.unknown`; pass `--game` for deterministic game-specific behavior.

Core options:

| Option | Behavior |
| --- | --- |
| `--game <game-id>` | Selects the REE-Lib game version used by the export resource. |
| `--mesh <path>` | Primary `.mesh.*` input. |
| `--additional-mesh <path>` | Adds extra mesh parts. Repeatable. |
| `--streaming <path>` | Explicit streaming buffer for the primary mesh. |
| `--additional-streaming <mesh=streaming>` | Explicit streaming buffer for an additional mesh. Repeatable. |
| `--mdf <path>` | Explicit MDF for primary mesh materials. |
| `--motlist <path>` | MOTLIST animation source. Repeatable. |
| `--motlist-dir <folder>` | Recursively uses all `*.motlist*` files under a folder. |
| `--mot <path>` | Raw MOT animation source. Repeatable. |
| `--output <path>` | `.glb`, `.fbx`, or folder output target. |
| `--animation-name <text>` | Case-insensitive animation-name filter. |
| `--split-motlists` | One output file per non-empty MOTLIST. |
| `--split-animations` | One output file per selected animation. |
| `--batch-motlist` | Compatibility alias for older split-animation behavior. |
| `--skip-missing-animation-bones` | Skips animations that reference missing skeleton bones. |
| `--no-placeholder-animation-bones` | Keeps animations but drops channels for missing bones. |
| `--no-animations` | Mesh/skeleton export without animation stacks. |
| `--no-textures` | Disables texture export. |
| `--texture-format png|dds` | Texture output format. Default is `png`. |
| `--fbx-scale <scale>` | Source FBX scale. Unreal-ready scripts use `100`. |
| `--include-lods` | Includes LOD geometry where supported. |
| `--include-occlusion` | Includes occlusion geometry where supported. |
| `--allow-missing-streaming` | Diagnostic-only escape hatch for missing required streaming buffers. |

## Runtime Dependencies

Release artifacts must include:

```text
REE-Content-Exporter.exe
texconv.exe
DirectXTex.dll
libGDeflate.dll
assimp.dll
```

Release builds fail if `texconv.exe` cannot be found and copied. Source builds can pass:

```powershell
dotnet publish -c Release -p:PublishProfile=win-x64-singlefile -p:TexconvPath="C:\tools\texconv.exe"
```

## Unreal-Ready FBX Workflow

The wizard-generated PowerShell scripts first write source FBX files through REE-Content-Exporter, then re-export through Blender 4.5.9 LTS.

Blender must be launched in background mode with factory startup:

```powershell
& $Blender --background --factory-startup --python $Py
```

Verified Blender FBX export axes:

```text
Forward: -Z
Up: Y
```

For Unreal-ready scripts, use:

```text
--fbx-scale 100
```

## Validation

For C# changes:

```powershell
dotnet build -c Release
```

For tracked PowerShell script changes:

```powershell
[scriptblock]::Create((Get-Content -LiteralPath "<script.ps1>" -Raw))
```

Do not run long game export scripts unless the user explicitly asks.
