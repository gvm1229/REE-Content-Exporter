# REE-Content-Exporter AI CLI Reference

This document is accessible to human users, but it is intended primarily for AI agents and advanced maintainers. It contains command-line details, maintenance rules, and release notes that are too dense for the human-facing README.

## Architecture

REE-Content-Exporter is a Windows GUI, CLI, and legacy console-wizard wrapper around REE-Content-Editor and RE-Engine-Lib.

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

The project targets `net10.0-windows` with Windows Forms enabled. Keep the application output type as `Exe` so direct CLI invocations still write console output.

## GUI Wizard

Release packages contain two entry-point executables:

- `REE-Content-Exporter-GUI.exe` opens the Windows Forms wizard with no arguments.
- `REE-Content-Exporter-CLI.exe` prints command-line usage with no arguments and is intended for direct export commands. When launched by double-click from Explorer with no arguments, it waits for Enter after printing usage so the temporary console remains readable.

`--gui` opens the Windows Forms wizard explicitly from either executable.

The GUI stores paths and game configuration in the same config file as the legacy wizard. Once the `game` property is saved, the GUI disables the game dropdown and uses that saved game for exports. The user can clear the saved game with the Edit action or by deleting the `game` line from `config.json`.

The GUI wraps the direct exporter command. It provides:

- game dropdown and `.list` download through REE.PAK.Tool metadata
- folder/file pickers for extract root, export root, Blender path, primary mesh, animation sources, and output path
- downloaded-list search for primary mesh, MOTLIST folders/files, and raw `.mot.*` files
- dropdowns for output format and texture format
- animation source dropdown for `MOTLIST folder`, `MOTLIST files`, and `MOT files`; inactive source rows are disabled so the GUI mirrors the mutually exclusive CLI flow
- numeric FBX scale control
- export option mode dropdown with `Default` and `Custom`; `Default` shows disabled checkboxes using the legacy console wizard preferences, while `Custom` enables and persists GUI-only checkbox choices in `config.json`
- checkboxes for animations, split MOTLISTs, split animations, textures, LODs, occlusion, missing animation bone handling, and missing streaming buffers
- language dropdown for English/Korean GUI labels and messages, persisted in the shared `Language` config property
- command preview, percentage progress bar parsed from exporter progress output, log window, cancel button, and command-copy action
- larger dark downloaded-list search dialogs with horizontal scrolling, hover path previews, and unclipped Choose/Cancel actions for long asset paths

The GUI uses a dark visual style with a pale blue accent, rounded buttons, and visible hover/pressed button states. Path text boxes and list rows should expose full-path tooltips whenever the rendered text is too narrow to show the whole value.

The GUI currently runs the direct exporter process and captures stdout/stderr in the log window. The older script-generation and Blender Unreal-ready workflow remains available through the legacy console wizard.

GUI export option defaults intentionally mirror the legacy console wizard script defaults rather than raw direct-CLI defaults:

- MOTLIST folder and MOTLIST file modes default to `--split-motlists`.
- Raw MOT mode does not default to `--split-motlists`.
- Animation exports default to `--no-placeholder-animation-bones`.
- Texture export stays enabled, while LODs, occlusion, split animations, and missing streaming buffers stay disabled.

The GUI field labeled `Animation name filter` maps to `--animation-name <contains>`. It filters selected animation names after MOTLIST or MOT sources are chosen; it is not an animation source path.

The GUI field labeled `Scene actor` maps to `--scene-actor <actor-id>`. The `Allow mixed scene actors` checkbox maps to `--allow-mixed-scene-animations` and should be used only for diagnostics.

The GUI `Bone spacing repair` controls are enabled for animated FBX exports after a Blender path is configured. They map to `--bone-spacing-reference-fbx`, `--bone-spacing-reference-action`, and `--bone-spacing-allow-translation`; Blender must be configured because the repair runs during Unreal-ready FBX finalization.

Scene/minidemo MOTLIST files whose action names look like `<motlist>_<actor>_*` are actor-filtered by the direct CLI. When multiple actor prefixes are selected, the exporter infers the actor from the primary mesh name, or users can pass `--scene-actor <actor-id>`.

GUI and legacy wizard asset lookup accept both extracted layouts: indexed `natives\STM\...` paths and stripped flat paths. When one form is not present under the configured extract root, the picker tries the other before falling back to a displayed path.

## Universal Game Configuration

The wizard stores its config at:

```text
%LOCALAPPDATA%\REE-Content-Exporter\config.json
```

The selected game is stored in the JSON `game` property. The wizard does not reprompt for game selection while that property exists. To reselect a game, delete the `game` line and run the wizard again.

GUI-only custom export settings are stored as `guiExportOptionsMode` plus `guiSplitMotlists`, `guiSplitAnimations`, `guiNoTextures`, `guiIncludeLods`, `guiIncludeOcclusion`, `guiNoPlaceholderBones`, and `guiAllowMissingStreaming`. Empty or missing `guiExportOptionsMode` means the GUI uses Default mode.

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

Startup modes:

```powershell
REE-Content-Exporter-GUI.exe
REE-Content-Exporter-GUI.exe --config "<config.json>"
REE-Content-Exporter-GUI.exe --wizard
REE-Content-Exporter-CLI.exe --help
REE-Content-Exporter-CLI.exe --wizard
REE-Content-Exporter-CLI.exe --reset-config --wizard
REE-Content-Exporter-GUI.exe --config "<config.json>" --gui
```

No arguments on the GUI executable, `--gui` on either executable, or `REE-Content-Exporter-GUI.exe --config "<config.json>"` opens the Windows GUI. No arguments on the CLI executable prints usage; Explorer double-click no-arg launches pause for Enter before closing, while terminal/script invocations return immediately. `--wizard` opens the legacy console wizard. Direct export mode starts when `--mesh` and export arguments are supplied.

Direct export mode preflights the primary mesh, additional meshes, explicit streaming files, explicit MDF, MOTLIST inputs, raw MOT inputs, and output parent path before loading REE files. Missing required arguments return exit code `2`; file, directory, and export failures return exit code `1`. Normal CLI failures print a concise `ERROR:` line without a .NET stack trace unless `REE_CONTENT_EXPORTER_DEBUG_ERRORS` is set.

General usage:

```powershell
REE-Content-Exporter-CLI.exe `
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
| `--mot <path>` | Raw MOT animation source parsed through REE-Lib `MotFile`. Repeatable. |
| `--output <path>` | `.glb`, `.fbx`, or folder output target. |
| `--animation-name <text>` | Case-insensitive animation-name filter. |
| `--scene-actor <actor-id>` | Filters scene/minidemo MOTLIST actions by actor prefix, for example `ch0100`, `ch0000`, or `wp0900`. |
| `--allow-mixed-scene-animations` | Diagnostic escape hatch that allows multiple scene actor prefixes to export onto one armature. Not recommended for final character exports. |
| `--split-motlists` | One output file per non-empty MOTLIST. |
| `--split-animations` | One output file per selected animation. |
| `--batch-motlist` | Compatibility alias for older split-animation behavior. |
| `--skip-missing-animation-bones` | Skips animations that reference missing skeleton bones. |
| `--no-placeholder-animation-bones` | Keeps animations but drops channels for missing bones. |
| `--no-animations` | Mesh/skeleton export without animation stacks. |
| `--no-textures` | Disables texture export. |
| `--texture-format png|dds` | Texture output format. Default is `png`. |
| `--fbx-scale <scale>` | Source FBX scale. Unreal-ready scripts use `100`. |
| `--unreal-ready-fbx` | Runs Blender 4.5.9 after source FBX export and writes final `*_unreal.fbx` files. |
| `--blender <path>` | Blender executable for `--unreal-ready-fbx`; the GUI passes the saved Blender path automatically. |
| `--keep-source-fbx` | Keeps intermediate source FBX files after successful Unreal-ready Blender re-export. |
| `--bone-spacing-reference-fbx <path>` | Opt-in Blender-stage repair that clamps non-allowlisted pose-bone local translations to a reference FBX action. Requires `--unreal-ready-fbx`; intended for scene MOTLISTs such as `md10015` where the source animation stretches the rig by animating bone spacing. |
| `--bone-spacing-reference-action <text>` | Reference action name filter for `--bone-spacing-reference-fbx`. Defaults to `ch0100_General_0100_Stan_Loop`. |
| `--bone-spacing-allow-translation <bones>` | Comma-separated local-translation allowlist for spacing repair. Defaults to `root,Hip,Null_Offset`, preserving root/character motion while freezing normal bone spacing to the reference. |
| `--include-lods` | Includes LOD geometry where supported. |
| `--include-occlusion` | Includes occlusion geometry where supported. |
| `--allow-missing-streaming` | Diagnostic-only escape hatch for missing required streaming buffers. |

Wizard animation behavior:

- GUI direct export can use a MOTLIST folder, repeatable MOTLIST files, or repeatable raw MOT files.
- Legacy console script generation asks for the same source type when animations are enabled.
- When the legacy console wizard detects a skeletal mesh and the user includes animations, it searches the downloaded `.list` with the mesh stem and suggests inferred MOTLIST folders and raw MOT files before falling back to manual selection.
- Inferred MOTLIST folder mode keeps the existing `--motlist-dir --split-motlists` script behavior.
- Inferred selected MOTLIST files pass repeatable `--motlist` arguments and keep split-MOTLIST behavior.
- Inferred raw MOT files pass repeatable `--mot` arguments.
- Selecting all inferred MOTLIST folder and raw MOT candidates passes both `--motlist-dir` and `--mot` without `--split-motlists`, because the direct exporter rejects `--split-motlists` when raw MOT inputs are present.
- Raw MOT files are resolved from downloaded REE.PAK.Tool lists by `.mot.*` paths, excluding `.motlist.*`.
- Generated Unreal-ready scripts pass raw MOT files as `--mot` and keep them in a single source FBX for Blender re-export.

## Runtime Dependencies

Release artifacts must include:

```text
REE-Content-Exporter-GUI.exe
REE-Content-Exporter-CLI.exe
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
