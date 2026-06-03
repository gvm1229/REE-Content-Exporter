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

## Dependencies

- [REE-Content-Editor](https://github.com/kagenocookie/REE-Content-Editor)
  - Why: this exporter is a CLI wrapper around REE-Content-Editor's mesh export pipeline. It uses `ContentEditor.App` classes such as `CommonMeshResource` instead of reimplementing mesh/skeleton/animation export logic.
  - How: clone it next to this repository, then apply this repository's patch before building:
    ```powershell
    git clone https://github.com/kagenocookie/REE-Content-Editor.git ..\REE-Content-Editor
    git -C ..\REE-Content-Editor apply ..\REE-Content-Exporter\patches\ree-content-editor-commonmeshresource-material-textures.patch
    ```

- [RE-Engine-Lib](https://github.com/kagenocookie/REE-Content-Editor/tree/master/RE-Engine-Lib)
  - Why: this is the file-format library used by REE-Content-Editor. The exporter relies on it for RE Engine mesh, material, texture, MOT, and MOTLIST parsing.
  - How: it is included inside the REE-Content-Editor repository. No separate clone is required when using the expected sibling-folder layout.

- [.NET SDK](https://dotnet.microsoft.com/download)
  - Why: the exporter and REE-Content-Editor projects are C#/.NET projects and must be built with `dotnet`.
  - How: install the SDK, then build from this repository with:
    ```powershell
    dotnet build -c Release
    ```

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

## Usage

Example PRAGMATA export with one selected walk-loop animation from a RETool extract rooted at `<path-to-RETool-extract>\re_chunk_000`:

```powershell
REE-Content-Exporter.exe `
  --mesh "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\ch\ch00\ch0000\00\ch0000_00_playergame.mesh.251121828" `
  --motlist "<path-to-RETool-extract>\re_chunk_000\natives\STM\character\animation\ch\ch00\ch0000\motlist\ch0000_general.motlist.1057" `
  --animation-name 0320 `
  --texture-format png `
  --output "<path-to-output>\ch0000_00_playergame_0320.glb"
```

Use `--batch-motlist` with a folder output to export selected MOTLIST entries as separate files.


