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

## 0.3.0 — material, texture, FBX, and batch export

Completed: 2026-06-04 01:21:43 +09:00

### What was attempted

After 0.2.0 proved that the CLI should use REE Content Editor's native export pipeline, 0.3.0 focused on closing the remaining practical exporter gaps:

- MDF material discovery and loading.
- Material texture slot injection into GLB/FBX output.
- TEX to DDS/PNG texture export.
- Direct FBX output.
- Batch MOTLIST export.

### Result

The exporter became a usable PRAGMATA content pipeline rather than only a mesh/animation smoke test.

New command-line options:

- `--mdf`
- `--batch-motlist`
- `--no-textures`
- `--texture-format png|dds`

The exporter also auto-detects sibling MDF files such as:

- `_mat.mdf2.*`
- `_Mat.mdf2.*`
- `.mdf2.*`
- `_00.mdf2.*`

### REE-Content-Editor hook

0.3.0 introduced a small patch to `CommonMeshResource` in REE-Content-Editor:

- `ExportTextureFormat`
- `SetImportedMaterials(...)`
- `ApplyMaterialTextureSlots(...)`

This allows the CLI to load MDF material data and make GLB/FBX material slots point to exported texture files.

### What we learned

- MDF loading should use REE's `MdfFile` and `MaterialGroupWrapper`, not a separate Tyrant-style material parser.
- Texture export should keep REE TEX parsing as the first step.
- Batch MOTLIST export is feasible by reusing the same mesh resource and exporting one selected motion per output file.

### Improvements over 0.2.0

- Added material loading.
- Added texture export manifest.
- Added PNG/DDS output modes.
- Added DirectXTex `texconv` PNG conversion after REE TEX-to-DDS output.
- Added GLB/FBX material texture references.
- Added selected batch MOTLIST export.

### Verification evidence

- `dotnet build -c Release` succeeded.
- GLB + PNG export generated 54 PNG files.
- GLB JSON contained 23 images, 23 textures, and 14 materials with no missing PNG URIs.
- Blender imported the GLB with 56 objects, 21 meshes, 1 armature, 231 bones, 1 action, and 790 f-curves.
- Blender imported the FBX with 56 objects, 21 meshes, 1 armature, 231 bones, 1 action, and 1090 f-curves.
- Batch export with `--batch-motlist --animation-name 0320` produced `0000_ch0000_General_0320_Walk_Loop_VerA.glb`.

### Known problem

The PNG files were structurally valid, but many PRAGMATA material textures were still noise-filled because GDeflate-compressed TEX payloads were not decompressed before `SaveAsDDS()`.

### Package evidence

- Package: `REE-Content-Exporter.PRAGMATA-poc-0.3.0.zip`
- SHA256: `D70F9760765575CF5F2F889373F766CBD00D861315AD90FEB395AA9893A3F585`
