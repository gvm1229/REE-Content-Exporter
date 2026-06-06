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
    export_ch0100_combined_glb.bat
    export_ch0100_all_motlists_unreal_fbx.ps1
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
    & "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" --background --python <generated-script.py>
    ```
    The current Unreal-ready FBX scripts are `export-scripts\export_ch0000_all_motlists_unreal_fbx.ps1` and `export-scripts\export_ch0100_all_motlists_unreal_fbx.ps1`. They expect Blender at `C:\Program Files\Blender Foundation\Blender 4.5\blender.exe`, import the source FBX, apply the Blender-side transform/unit fixes, create explicit NLA animation strips, and export one Unreal-ready FBX containing all MOTLIST animations for the character.
  - Detailed workflow notes are in `docs/ch0100_blender_45_fbx_unreal_workflow.md`. A macOS execution-script feasibility note is in `docs/macos_export_script_feasibility.md`.

- [DirectXTex texconv](https://github.com/microsoft/DirectXTex/wiki/Texconv)
  - Why: REE-Content-Exporter first asks REE-Lib to convert RE Engine TEX data to DDS. When `--texture-format png` is used, `texconv` converts those DDS files to PNG.
  - How: install `texconv.exe` and make it available on `PATH`, or install it through WinGet:
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

Choose the output format with the extension in `--output`:

- `.glb` exports GLB.
- `.fbx` exports FBX.

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
