# REE-Content-Exporter

REE-Content-Exporter is a small command-line exporter built on top of the [REE-Content-Editor](https://github.com/kagenocookie/REE-Content-Editor) / [RE-Engine-Lib](https://github.com/kagenocookie/REE-Content-Editor/tree/master/RE-Engine-Lib) codebase.

The project goal is to make RE Engine content easier to move into standard DCC/game-engine workflows by wrapping the readers and exporters already developed in REE-Content-Editor. It started as a PRAGMATA proof of concept for exporting mesh and animation data to Blender-readable formats.

## Relationship to REE-Content-Editor

This project is not a replacement for [REE-Content-Editor](https://github.com/kagenocookie/REE-Content-Editor). It depends on that project and follows its implementation patterns where possible.

Expected local layout for development:

```text
parent-folder/
  REE-Content-Editor/
  REE-Content-Exporter/
```

## Repository file hierarchy

Files included in Git:

```text
REE-Content-Exporter/
  .gitignore
  LICENSE
  Program.cs
  README.md
  REE-Content-Exporter.csproj
  export-scripts/
    export_ch0000_all_motlists_glb.bat
    export_ch0000_all_motlists_unreal_fbx.ps1
    export_ch0100_all_motlists_glb.bat
    export_ch0100_all_motlists_unreal_fbx.ps1
    template_mesh_only_unreal_fbx.ps1
  docs/
    CHANGELOG.md
  patches/
    ree-content-editor-commonmeshresource-material-textures.patch
```

Generated or local folders such as build output are intentionally not listed here.

## Dependencies

- [REE-Content-Editor](https://github.com/kagenocookie/REE-Content-Editor)
  - Why: this exporter is a CLI wrapper around REE-Content-Editor's mesh export pipeline. It uses `ContentEditor.App` classes such as `CommonMeshResource` instead of reimplementing mesh/skeleton/animation export logic.
  - How: prepare the pinned, patched sibling dependency with the setup script:
    ```powershell
    .\scripts\setup-content-editor-dependency.ps1 -Force
    ```
    See `docs/dependency_setup.md` for details. The patch is required; an unmodified upstream `REE-Content-Editor` checkout will not build this exporter.

- [RE-Engine-Lib](https://github.com/kagenocookie/REE-Content-Editor/tree/master/RE-Engine-Lib)
  - Why: this is the file-format library used by REE-Content-Editor. The exporter relies on it for RE Engine mesh, material, texture, MOT, and MOTLIST parsing.
  - How: it is included inside the REE-Content-Editor repository. No separate clone is required when using the expected sibling-folder layout.

- [.NET SDK](https://dotnet.microsoft.com/download)
  - Why: the exporter and REE-Content-Editor projects are C#/.NET projects and must be built with `dotnet`.
  - How: install the SDK, then build from this repository with:
    ```powershell
    dotnet build -c Release
    ```

- [Blender 4.5.9 LTS](https://www.blender.org/)
  - Required version: `4.5.9 LTS`. This workflow is version-sensitive; different Blender FBX importer/exporter behavior may change axis, scale, or animation baking results. Do not assume another 4.5.x or newer Blender version is equivalent until it is re-verified in Unreal.
  - Why: Blender is required for the current Unreal-ready FBX workflow. The direct Assimp-authored FBX can import into Unreal, but its animation curves wobble in Unreal. Blender imports the same FBX correctly, then re-bakes/re-exports the animation into an Unreal-friendly FBX.
  - Scope: Blender is not required for the basic exporter build, nor for producing the first source FBX. It is required when using the Unreal-ready sample/export scripts that perform the Blender bake/re-export step.
  - How this repository accesses Blender: the Unreal sample script calls Blender as an external executable in background mode and passes it a generated Python script:
    ```powershell
    & "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" --background --factory-startup --python <generated-script.py>
    ```
    The current Unreal-ready FBX scripts are `export-scripts\export_ch0000_all_motlists_unreal_fbx.ps1` and `export-scripts\export_ch0100_all_motlists_unreal_fbx.ps1`. They launch Blender with `--factory-startup` so user-installed addons cannot affect export behavior or pollute logs. They expect Blender at `C:\Program Files\Blender Foundation\Blender 4.5\blender.exe`, export one source FBX per non-empty MOTLIST, import each source FBX through Blender, apply the Blender-side transform/unit fixes, create explicit NLA animation strips, and export one Unreal-ready FBX per MOTLIST. Intermediate source FBX files are deleted after successful Blender re-export unless the script is run with `-KeepSourceFbx`. If Blender imports a source FBX with zero animation actions, that MOTLIST source is skipped rather than treated as a fatal failure and documented in `skipped-blender-motlists.md`. Each run also writes an export log into the generated job folder with a `-SUCCESS.log` or `-FAIL.log` suffix. For mesh-only exports, use `export-scripts\template_mesh_only_unreal_fbx.ps1`.
  - Detailed workflow notes are in `docs/ch0100_blender_45_fbx_unreal_workflow.md`. A macOS execution-script feasibility note is in `docs/macos_export_script_feasibility.md`.

- [DirectXTex texconv](https://github.com/microsoft/DirectXTex/wiki/Texconv)
  - Why: REE-Content-Exporter first asks REE-Lib to convert RE Engine TEX data to DDS. When `--texture-format png` is used, `texconv` converts those DDS files to PNG.
  - PNG fallback behavior: the exporter first lets `texconv` use its default decoded output. If PNG writing fails for a format such as BC5/two-channel data, the exporter retries that texture as `R8G8B8A8_UNORM` so the batch can continue without forcing every texture through RGBA. Details and verification notes are in `docs/png_texture_conversion_fallback.md`.
  - Release builds: `texconv.exe` is bundled beside `REE-Content-Exporter.exe`, so users who download a release package do not need to install it separately.
  - Source builds: install `texconv.exe` on the build machine or pass `-p:TexconvPath="<path>\texconv.exe"` when building/publishing. WinGet installs are auto-detected:
    ```powershell
    winget install --id Microsoft.DirectXTex.Texconv
    ```

- [RETool](https://www.patreon.com/posts/retool-modding-36746173)
  - Why: the exporter expects loose RE Engine files on disk, such as `.mesh`, `.mdf2`, `.tex`, `.mot`, and `.motlist`. RETool is used first to extract those files from RE Engine PAK archives.
  - How: extract the game PAKs with RETool, then pass paths under the extracted `re_chunk_000` folder to this exporter.

## License

REE-Content-Exporter follows the same MIT license as REE-Content-Editor. See `LICENSE`.

## History

Detailed development history is tracked in `docs/CHANGELOG.md`.

## REE-Content-Editor patch

Starting with 0.3.0, this exporter needs a small REE-Content-Editor-side hook so the CLI can inject loaded MDF material data and write material texture slots into the Assimp scene before export.

Apply:

```powershell
git -C ..\REE-Content-Editor apply ..\REE-Content-Exporter\patches\ree-content-editor-commonmeshresource-material-textures.patch
```


## Output layout

Each export job writes into its own subfolder under the requested output location. The subfolder name is derived from the mesh and animation source filenames used for that export, with a short hash suffix to keep names unique and path-safe.

For example, if `--output` is:

```text
C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter\ch0000_all_motlists_all_animations.glb
```

then the actual files are written under a job folder such as:

```text
C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter\ch0000_00_playergame__ch0000_damage__ch0000_damage_tree__ch0000_develop__ch0000_emp_backarm__plus25__<hash>\
  ch0000_all_motlists_all_animations.glb
  textures\
    materials.textures.json
    ...
  ch0000_all_motlists_all_animations.skipped-animation-bones.md
```

This prevents GLB/FBX files, texture folders, and Markdown reports from overwriting one another when different export jobs share the same parent output folder.

## Usage

### Interactive wizard

Run the executable with no arguments to launch the interactive Unreal-ready FBX wizard:

```powershell
.\bin\Release\net10.0\REE-Content-Exporter.exe
```

On first setup, the wizard asks for a language:

```text
1. English
2. Korean
```

The selected language is saved in the wizard config and used for future interactive prompts and validation messages. Existing config files created before this setting will ask only for the language once on the next wizard launch, then keep that language until the config file is edited, reset, or deleted.

The wizard saves its setup in:

```text
%LOCALAPPDATA%\REE-Content-Exporter\config.json
```

It accepts a game extract root, a folder inside the extract, or a full asset path such as:

```text
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828
```

The resolver can also accept a bare filename such as `ch0100_00.mesh.251121828`. It uses `pragmata.list` as an index, then probes common extract layouts including `natives\stm\character\...`, direct `character\...`, and their matching `streaming\...` counterparts. Both classic `MESH` files and MPLY-backed `.mesh.*` files are supported; MPLY files are converted into the normal mesh export path before FBX generation.

After setup, wizard v0.2 asks whether to export one mesh or import a CSV for batch mesh export. Choice prompts require explicit input, so pressing Enter is rejected for yes/no and numbered choices instead of selecting a default. Accepted prompt answers are followed by a visible separator so long interactive sessions are easier to scan. If CSV batch mode is selected, the wizard then asks how skeletal meshes should be handled: either prompt for animations when each skeletal mesh is found, or auto-export skeletal meshes without prompting for animations. CSV batch files must contain exactly one column of primary mesh names or paths. A header row is optional when the first cell is `mesh`, `mesh_name`, or `name`; otherwise every non-empty row is treated as a mesh query. Path prompts accept pasted Windows-style paths with surrounding double quotes, such as `"./all_meshes.csv"`. The wizard rejects blank rows, duplicate mesh entries, and files with extra columns before generating a script.

In CSV batch mode, static meshes are queued without extra prompts. When the prompt-for-animations policy is selected, each skeletal mesh gets its own animation prompt and reuses the same MOTLIST folder/file selection flow as single-mesh mode. When the no-animation-prompt policy is selected, skeletal meshes are exported with their skeletons but without animation stacks. Rows that cannot be resolved or inspected are skipped during preflight and written to the final summary instead of crashing the wizard. The generated batch script writes one top-level folder named like `wizard_batch_<timestamp>` under the selected export root, with each mesh placed under its own child folder and a `batch-summary.md` report listing exported, skipped, and failed rows.

Batch scripts are resumable from previous successful exports. In batch mode, the wizard asks whether to auto-scan existing batch exports or use a designated folder. Auto-scan searches sibling `wizard_batch_*` folders in the selected export root. The designated-folder mode accepts a pasted path, including paths surrounded by double quotes, and scans only that folder plus its immediate `wizard_batch_*` children. Before running each row, the script looks for that mesh's child folder, a previous `ree_export_wizard-SUCCESS.log`, and a non-empty `*_unreal.fbx`; matching rows are skipped as existing successful exports and recorded in the new summary. Every batch row also gets a log under `wizard_batch_<timestamp>\batch-job-logs\`, including preflight skips, existing-success skips, exported rows, and failed rows. If any row fails, the script continues through the remaining rows, writes the summary and per-row logs, prints the failed row details and log paths, exits with code `1`, and waits for Enter before closing so the error information remains visible.

Generated Unreal-ready scripts write Blender re-export diagnostics next to the temporary status file in `%TEMP%` as `*.blender.log`. If Blender fails during the Python import/export phase, the script reports that log path in the error message. Pressing Enter at the Blender path prompt accepts the displayed default path.

Useful wizard options:

```powershell
.\bin\Release\net10.0\REE-Content-Exporter.exe --wizard
.\bin\Release\net10.0\REE-Content-Exporter.exe --reset-config
.\bin\Release\net10.0\REE-Content-Exporter.exe --config "C:\path\to\config.json"
```

To publish a self-contained Windows package:

```powershell
dotnet publish -c Release -p:PublishProfile=win-x64-singlefile
```

Release and publish outputs include `texconv.exe` beside `REE-Content-Exporter.exe` so PNG texture export works from the downloaded package without a separate DirectXTex install. If the build machine cannot find `texconv.exe`, Release build/publish fails instead of producing an incomplete package. Pass an explicit converter path when needed:

```powershell
dotnet publish -c Release -p:PublishProfile=win-x64-singlefile -p:TexconvPath="C:\tools\texconv.exe"
```

The publish output is written under:

```text
bin\Release\net10.0\win-x64\publish\
```

Choose the output format with the extension in `--output`:

- `.glb` exports GLB.
- `.fbx` exports FBX.

### CLI option and flag reference

General usage shape:

```powershell
REE-Content-Exporter.exe `
  --mesh "<primary.mesh.version>" `
  [mesh/material options] `
  [animation options] `
  [output/export options] `
  --output "<output.fbx|output.glb|output-folder>"
```

Options can be passed in any order. Options marked as repeatable can be supplied more than once.

#### Required input/output options

| Option | Value | Behavior | Expected outcome | How to use |
| --- | --- | --- | --- | --- |
| `--mesh` | Path to a `.mesh.*` file | Loads the primary RE Engine mesh. | Creates the main skeleton, mesh groups, material slots, and export job folder name. | Always pass exactly one primary mesh: `--mesh "<extract>\character\...\ch0100_00.mesh.251121828"`. |
| `--output` | `.glb` file, `.fbx` file, or folder path | Selects export format from the extension and controls where the export job folder is created. | `.glb` writes GLB, `.fbx` writes FBX. If no extension is supplied, the value is treated as a folder and a default GLB filename is used for single-output mode. | Use a file path for a predictable output filename: `--output "C:\out\ch0100_attack.fbx"`. Use a folder path for default naming. |

#### Mesh, streaming, and material options

| Option | Value | Behavior | Expected outcome | How to use |
| --- | --- | --- | --- | --- |
| `--additional-mesh` | Path to another `.mesh.*` file. Repeatable. | Adds extra mesh parts into the same export scene and armature. | Multi-part characters such as ch0100 can export body, gear, accessories, and additional parts together. | Repeat once per extra part: `--additional-mesh "<extract>\character\...\10\ch0100_10.mesh.251121828"`. |
| `--streaming` | Path to a streaming `.mesh.*` buffer | Explicitly supplies streaming geometry for the primary mesh. | Required vertex/index data is loaded when the mesh needs a streaming buffer. | Use when auto-detection is not enough: `--streaming "<extract>\streaming\character\...\ch0100_00.mesh.251121828"`. |
| `--additional-streaming` | `<additional-mesh-path>=<streaming-mesh-path>`. Repeatable. | Explicitly supplies streaming geometry for a matching `--additional-mesh` path. The key must exactly match one supplied additional mesh path. | Additional mesh parts with separate streaming buffers load deterministically instead of relying only on auto-detection. | Use for ch0100 part `40`: `--additional-streaming "<extract>\character\...\40\ch0100_40_neo.mesh.251121828=<extract>\streaming\character\...\40\ch0100_40_neo.mesh.251121828"`. |
| `--allow-missing-streaming` | Flag | Allows export to continue if a required streaming buffer is missing. | Useful for diagnosis, but output may have incomplete geometry. | Add the flag only for troubleshooting: `--allow-missing-streaming`. |
| `--mdf` | Path to an `.mdf2.*` file | Explicitly supplies material data for the primary mesh. | Material slots and texture references are taken from the specified MDF instead of auto-discovery. | Use when auto MDF lookup chooses the wrong file: `--mdf "<extract>\character\...\ch0100_00_mat.mdf2.51"`. |
| `--no-textures` | Flag | Disables texture export and material texture file writing. | Faster export with no `textures\` folder. Material texture reconnection data may be absent. | Add for geometry/animation-only tests: `--no-textures`. |
| `--texture-format` | `png` or `dds` | Selects exported texture file format. Default is `png`. PNG conversion uses the bundled `texconv.exe` first, preserves texconv's default decoded format when possible, then retries with PNG-compatible RGBA output for formats such as BC5/two-channel maps that cannot be written directly. | `png` is easier to inspect and use in DCC tools; `dds` avoids PNG conversion. Texture export failures are fatal when material texture export is actually produced, except unsupported depth/3D TEX resources remain warnings because they are not needed for exported material files. | Use `--texture-format png` for normal workflows, or `--texture-format dds` only when DDS output is intentionally desired. |

#### Animation source options

| Option | Value | Behavior | Expected outcome | How to use |
| --- | --- | --- | --- | --- |
| `--motlist` | Path to a `.motlist.*` file. Repeatable. | Reads one MOTLIST and all MOT files referenced by it. | Selected animations from each supplied MOTLIST are exported. | Use once for one MOTLIST or repeat for several: `--motlist "<extract>\...\ch0100_attack.motlist.1057"`. |
| `--motlist-dir` | Folder path | Recursively finds `*.motlist*` files under the folder. | All MOTLIST files in the folder become animation sources. Empty/no-selected MOTLISTs are skipped in `--split-motlists` mode and documented in `skipped-motlists.md`. | Use for full character animation coverage: `--motlist-dir "<extract>\character\animation\ch\ch01\ch0100\motlist"`. |
| `--mot` | Path to a `.mot.*` file. Repeatable. | Reads individual MOT files directly without a MOTLIST. | Supplied MOT animations are exported. | Use for targeted tests when a raw MOT path is known: `--mot "<extract>\...\motion.mot.78"`. |
| `--animation-name` | Case-insensitive substring | Filters MOT/MOTLIST animations by name. | Only animations whose names contain the substring are selected. In split mode, MOTLISTs with zero selected animations are skipped. | Use to export a smaller test set: `--animation-name 0110_Hacking_Loop` or `--animation-name Attack`. |
| `--no-animations` | Flag | Disables animation loading/export even if MOT/MOTLIST options are supplied. | Mesh-only export with skeleton and mesh data but no animation stacks. | Use for skeletal mesh tests: `--no-animations`. |

#### Animation splitting and batching flags

| Option | Value | Behavior | Expected outcome | How to use |
| --- | --- | --- | --- | --- |
| `--split-motlists` | Flag | With `--motlist-dir` or repeated `--motlist`, exports one file per non-empty MOTLIST. Empty/no-selected MOTLISTs are skipped and listed in `skipped-motlists.md`. Cannot be combined with `--mot` or `--split-animations`. | Produces manageable per-MOTLIST files such as `0000_ch0100_Attack_all_animations.fbx` instead of one huge all-animation file. | Use for Unreal-ready script source generation or large characters: `--motlist-dir "<motlist-folder>" --split-motlists`. |
| `--split-animations` | Flag | Exports one file per selected animation. Cannot be combined with `--split-motlists`. | Produces many small files, each containing one animation. | Use for isolated animation debugging: `--split-animations --animation-name 0110`. |
| `--batch-motlist` | Flag | Compatibility alias for split-per-animation behavior when only one MOT/MOTLIST source is supplied. With multiple animation sources, the exporter keeps combined-output behavior unless `--split-animations` is used. | Older batch workflows still work. | Prefer `--split-animations` for new commands; use `--batch-motlist` only for older scripts. |

#### Missing animation bone handling

| Option | Value | Behavior | Expected outcome | How to use |
| --- | --- | --- | --- | --- |
| `--skip-missing-animation-bones` | Flag | Skips entire animations that reference bones missing from the exported skeleton. | Output has fewer animations but no placeholder bones for those skipped animations. A `*.skipped-animations.md` report is written. | Use when you want a clean animation set and can discard incompatible animations. |
| `--no-placeholder-animation-bones` | Flag | Keeps animations but skips only channels that target missing bones. | Output keeps more animations while avoiding generated `hash...` placeholder bones. A `*.skipped-animation-bones.md` report is written. | Use for the current PRAGMATA Unreal workflow: `--no-placeholder-animation-bones`. |

Do not use `--skip-missing-animation-bones` and `--no-placeholder-animation-bones` together for normal exports; they represent different policies for missing animation bone channels.

#### Geometry/export detail options

| Option | Value | Behavior | Expected outcome | How to use |
| --- | --- | --- | --- | --- |
| `--fbx-scale` | Positive number | Sets the scale passed into the REE/Assimp FBX export stage. Default is `1`. | Direct source FBX size changes. For the current Blender-to-Unreal workflow, `100` is required before Blender converts to centimeter scene units. | Use `--fbx-scale 100` for Unreal-ready FBX scripts. |
| `--include-lods` | Flag | Includes mesh LODs where supported by the underlying exporter. | Larger output with additional LOD geometry. | Add only when LODs are needed: `--include-lods`. |
| `--include-occlusion` | Flag | Includes occlusion-related mesh data where supported. | Larger output with occlusion data included. | Add only when testing or needing occlusion data: `--include-occlusion`. |

#### Help flag

| Option | Value | Behavior | Expected outcome | How to use |
| --- | --- | --- | --- | --- |
| `--help` | Flag | Prints the command summary and exits. | No export is run. | `REE-Content-Exporter.exe --help` |

### PowerShell execution script parameters

The scripts under `export-scripts\` are convenience wrappers around the CLI. They build the source FBX first, then optionally run Blender 4.5.9 for Unreal-ready output.

#### `export_ch0000_all_motlists_unreal_fbx.ps1`

| Parameter | Value | Behavior | Expected outcome | How to use |
| --- | --- | --- | --- | --- |
| `-Root` | Extracted `re_chunk_000` path | Sets the loose-file extract root. | ch0000 mesh and MOTLIST paths are resolved under this root. | `.\export-scripts\export_ch0000_all_motlists_unreal_fbx.ps1 -Root "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000"` |
| `-ExportRoot` | Output parent folder | Selects where the generated job folder is created. | Per-MOTLIST Unreal FBXs, textures, reports, and log are written under one new job folder. | `-ExportRoot "C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter"` |
| `-Blender` | Path to `blender.exe` | Selects the Blender executable. The script requires Blender `4.5.9 LTS`. | Blender imports each source FBX and writes Unreal-ready per-MOTLIST FBX files. | `-Blender "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe"` |
| `-KeepSourceFbx` | Switch | Keeps the intermediate per-MOTLIST source FBX files after Blender succeeds or after a zero-action source is skipped. | Useful for debugging; otherwise source FBXs are deleted to avoid confusion. | `.\export-scripts\export_ch0000_all_motlists_unreal_fbx.ps1 -KeepSourceFbx` |

#### `export_ch0100_all_motlists_unreal_fbx.ps1`

| Parameter | Value | Behavior | Expected outcome | How to use |
| --- | --- | --- | --- | --- |
| `-Root` | Extracted `re_chunk_000` path | Sets the loose-file extract root. | ch0100 mesh parts `00`, `10`, `20`, `40`, streaming data, and MOTLIST paths are resolved under this root. | `.\export-scripts\export_ch0100_all_motlists_unreal_fbx.ps1 -Root "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000"` |
| `-ExportRoot` | Output parent folder | Selects where the generated job folder is created. | Per-MOTLIST Unreal FBXs, textures, reports, and log are written under one new job folder. | `-ExportRoot "C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter"` |
| `-Blender` | Path to `blender.exe` | Selects the Blender executable. The script requires Blender `4.5.9 LTS`. | Blender imports each source FBX and writes Unreal-ready per-MOTLIST FBX files. | `-Blender "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe"` |
| `-KeepSourceFbx` | Switch | Keeps the intermediate per-MOTLIST source FBX files after Blender succeeds or after a zero-action source is skipped. | Useful for debugging; otherwise source FBXs are deleted to avoid confusion. | `.\export-scripts\export_ch0100_all_motlists_unreal_fbx.ps1 -KeepSourceFbx` |

For the per-MOTLIST Unreal scripts, `skipped-motlists.md` is written by the exporter when a MOTLIST has no selected animation entries before source-FBX generation. `skipped-blender-motlists.md` is written by the PowerShell/Blender phase when a source FBX exists but Blender imports zero animation actions from it. The latter is expected for some tree/control MOTLIST resources and does not make the run fail.

#### `template_mesh_only_unreal_fbx.ps1`

| Parameter | Value | Behavior | Expected outcome | How to use |
| --- | --- | --- | --- | --- |
| `-Mesh` | Path to a `.mesh.*` file | Required primary mesh input. | Mesh-only source FBX is created before Blender re-export. | `-Mesh "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828"` |
| `-AdditionalMesh` | Array of extra mesh paths | Adds extra mesh parts into the same mesh-only export. | Multi-part skeletal mesh output without animations. | `-AdditionalMesh "...\10\ch0100_10.mesh.251121828","...\20\ch0100_20.mesh.251121828"` |
| `-Streaming` | Streaming mesh buffer path | Explicitly supplies streaming geometry for the primary mesh. | Required streaming data is available during mesh-only export. | `-Streaming "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\streaming\character\...\ch0100_00.mesh.251121828"` |
| `-OutputName` | Final `.fbx` filename | Sets the final Blender-reexported mesh-only FBX filename. | Output uses the requested concise filename. | `-OutputName "ch0100_mesh_unreal.fbx"` |
| `-ExportRoot` | Output parent folder | Selects where the generated job folder is created. | Mesh-only Unreal FBX and textures are written under one new job folder. | `-ExportRoot "C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter"` |
| `-Blender` | Path to `blender.exe` | Selects the Blender executable. The script requires Blender `4.5.9 LTS`. | Blender writes an Unreal-ready mesh-only FBX. | `-Blender "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe"` |
| `-KeepSourceFbx` | Switch | Keeps the intermediate source FBX after Blender succeeds. | Useful for debugging; otherwise source FBX is deleted. | `.\export-scripts\template_mesh_only_unreal_fbx.ps1 -Mesh "<mesh>" -KeepSourceFbx` |

Example PRAGMATA export with one selected walk-loop animation from a RETool extract rooted at `<path-to-RETool-extract>\re_chunk_000`:

```powershell
REE-Content-Exporter.exe `
  --mesh "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\ch\ch00\ch0000\00\ch0000_00_playergame.mesh.251121828" `
  --motlist "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\animation\ch\ch00\ch0000\motlist\ch0000_general.motlist.1057" `
  --animation-name 0320 `
  --texture-format png `
  --output "<path-to-output>\ch0000_00_playergame_0320.glb"
```

To export a whole MOTLIST into one GLB/FBX, omit `--animation-name` and use a file path for `--output`:

```powershell
REE-Content-Exporter.exe `
  --mesh "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\ch\ch00\ch0000\00\ch0000_00_playergame.mesh.251121828" `
  --motlist "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\animation\ch\ch00\ch0000\motlist\ch0000_general.motlist.1057" `
  --texture-format png `
  --output "<path-to-output>\ch0000_general_all_animations.glb"
```

Pass `--motlist` more than once, or use `--motlist-dir <folder>`, to export multiple MOTLIST files into one GLB/FBX containing all selected animations:

```powershell
REE-Content-Exporter.exe `
  --mesh "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\ch\ch00\ch0000\00\ch0000_00_playergame.mesh.251121828" `
  --motlist "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\animation\ch\ch00\ch0000\motlist\ch0000_general.motlist.1057" `
  --motlist "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\animation\ch\ch00\ch0000\motlist\ch0000_combat.motlist.1057" `
  --texture-format png `
  --output "<path-to-output>\ch0000_multi_motlist_all_animations.glb"
```

```powershell
REE-Content-Exporter.exe `
  --mesh "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\ch\ch00\ch0000\00\ch0000_00_playergame.mesh.251121828" `
  --motlist-dir "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\animation\ch\ch00\ch0000\motlist" `
  --texture-format png `
  --output "<path-to-output>\ch0000_all_motlists_all_animations.glb"
```


To export a character assembled from multiple mesh files into one outliner armature, pass one primary `--mesh` and repeat `--additional-mesh` for each extra part you want included. Additional meshes use automatic streaming-buffer lookup and automatic sibling MDF lookup:

```powershell
REE-Content-Exporter.exe `
  --mesh "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828" `
  --additional-mesh "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\ch\ch01\ch0100\10\ch0100_10.mesh.251121828" `
  --additional-mesh "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\ch\ch01\ch0100\20\ch0100_20.mesh.251121828" `
  --motlist-dir "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\animation\ch\ch01\ch0100\motlist" `
  --texture-format png `
  --output "<path-to-output>\ch0100_combined_all_animations.glb"
```

If `--output` has no `.glb` or `.fbx` extension, it is treated as a folder and the single combined export is written as `<mesh-name>_all_animations.glb` inside it.

Use `--split-animations` to force one output file per animation. `--batch-motlist` remains as a compatibility alias for split export when only one MOT/MOTLIST source is supplied.


Use `--skip-missing-animation-bones` to skip entire animations that reference bones missing from the exported mesh skeleton. When this flag is active, skipped animations are documented in a sidecar Markdown report next to the output file:

```powershell
REE-Content-Exporter.exe `
  --mesh "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\ch\ch00\ch0000\00\ch0000_00_playergame.mesh.251121828" `
  --motlist-dir "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\animation\ch\ch00\ch0000\motlist" `
  --skip-missing-animation-bones `
  --texture-format png `
  --output "<path-to-output>\ch0000_all_motlists_all_animations.glb"
```

For that example, the report path is:

```text
<path-to-output>\ch0000_all_motlists_all_animations.skipped-animations.md
```

Use `--no-placeholder-animation-bones` when you want to keep animations but avoid adding placeholder `hash...` bones. This skips only the missing-bone channels and writes a sidecar channel report:

```powershell
REE-Content-Exporter.exe `
  --mesh "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\ch\ch00\ch0000\00\ch0000_00_playergame.mesh.251121828" `
  --motlist-dir "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\animation\ch\ch00\ch0000\motlist" `
  --no-placeholder-animation-bones `
  --texture-format png `
  --output "<path-to-output>\ch0000_all_motlists_all_animations.glb"
```

For that example, the channel report path is:

```text
<path-to-output>\ch0000_all_motlists_all_animations.skipped-animation-bones.md
```

Export naming is normalized for DCC import:

- armature object/root node: `Armature`
- GLB skin/skeleton name: `Armature`
- mesh names: `Group_<group>_sub<sub>__<material>`, without the RE mesh basename prefix
