# CHANGELOG

## 0.1.0 — manual GLB proof of concept

Completed: 2026-06-04 00:17:43 +09:00

### What was attempted

The first exporter proof of concept tried to skip Tyrant's `.semodel` / `.seanim` intermediate files and export PRAGMATA content directly from REE-Lib data.

The initial command-line tool accepted:

- `--mesh`
- `--motlist` or `--mot`
- `--output`
- `--animation-name`
- `--no-animations`
- `--include-lods`

It read PRAGMATA mesh and MOT/MOTLIST files through REE-Lib and then exported either:

- FBX through Assimp, or
- GLB through a custom `ManualGltf` writer.

### Result

The proof of concept showed that REE-Lib could read the relevant PRAGMATA files and that the exporter could generate Blender-readable output with skeleton and animation data.

However, the implementation still made too many assumptions about PRAGMATA mesh layout. In particular, the manual GLB writer recreated mesh slicing and indexing logic outside of the original REE Content Editor export path.

### What we learned

- REE-Lib already knows far more about modern RE Engine files than Tyrant's hand-written PRAGMATA guesses.
- A direct exporter is viable.
- But duplicating mesh export logic is risky: PRAGMATA mesh group/submesh/index rules are easy to misread.

### Limitations found

- Geometry could become scrambled.
- The manual GLB path was not the same as REE Content Editor's preview/export path.
- Streaming mesh data was not handled yet.
- Material and texture export were not integrated.

### Package evidence

- Package: `REE-Content-Exporter.PRAGMATA-poc-0.1.0.zip`
- SHA256: `4F18E85F165731045A838CA5F396B722E16059772504A43E3CDF9A86171FD454`

## 0.2.0 — switch to REE Content Editor native export path

Completed: 2026-06-04 00:46:26 +09:00

### What was attempted

The first proof of concept proved that direct export was possible, but it also showed that a hand-written GLB writer was the wrong place to solve PRAGMATA mesh layout. Version 0.2.0 removed the manual mesh export path and wrapped the original REE Content Editor export path instead.

The exporter now used:

- `MeshFile.Read()`
- `MeshFile.LoadStreamingData()`
- `CommonMeshResource.ExportToFile()`

### Result

This was the major correctness jump for mesh export. The exporter stopped trying to recreate REE mesh slicing and let REE Content Editor's Assimp scene exporter build the output.

It also added automatic discovery for loose RETool streaming mesh data. For example:

```text
natives/STM/character/ch/ch00/ch0000/00/ch0000_00_playergame.mesh.251121828
```

is paired with:

```text
natives/STM/streaming/character/ch/ch00/ch0000/00/ch0000_00_playergame.mesh.251121828
```

### What we learned

- PRAGMATA `ch0000_00_playergame.mesh.251121828` requires streaming mesh buffers.
- Resident mesh data alone can produce broken geometry.
- REE Content Editor already had the right mesh export path; the CLI should expose it, not duplicate it.

### Improvements over 0.1.0

- Removed `ManualGltf` mesh writer.
- Removed custom `Exporter` Assimp scene construction.
- Added `--streaming`.
- Added automatic streaming candidate lookup.
- Added `--include-occlusion`.
- Added `--allow-missing-streaming`, while failing by default when required streaming data is absent.

### Package evidence

- Package: `REE-Content-Exporter.PRAGMATA-poc-0.2.0.zip`
- SHA256: `6AC4FB976A633EF28A56C6206C942F9546ACC02B04137F68570FD550DF5992A7`
