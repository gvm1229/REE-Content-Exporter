# REE-Content-Exporter

REE-Content-Exporter is a small command-line exporter built on top of the REE-Content-Editor / RE-Engine-Lib codebase.

The project goal is to make RE Engine content easier to move into standard DCC/game-engine workflows by wrapping the readers and exporters already developed in REE-Content-Editor. It started as a PRAGMATA proof of concept for exporting mesh and animation data to Blender-readable formats.

## Relationship to REE-Content-Editor

This project is not a replacement for REE-Content-Editor. It depends on that project and follows its implementation patterns where possible.

Expected local layout for development:

```text
parent-folder/
  REE-Content-Editor/
  REE-Content-Exporter/
```

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
