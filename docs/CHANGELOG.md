# CHANGELOG

## 0.4.1 ? combined multi-MOTLIST export default

Completed: 2026-06-04

### What was attempted

The first multi-MOTLIST command example used `--batch-motlist`, which generated one GLB per animation. That was not suitable for Unreal Engine import because it produced too many separate files and unnecessary storage clutter.

### Result

Multiple MOT/MOTLIST sources now default to one combined export containing all selected animations. This applies to repeated `--motlist`, repeated `--mot`, and `--motlist-dir` usage. If a folder path is passed to `--output` for a combined export, the exporter writes `<mesh-name>_all_animations.glb` inside that folder.

### Improvements over 0.4.0

- Multiple animation sources now imply one combined GLB/FBX export by default.
- Added explicit `--split-animations` for users who really want one file per animation.
- Kept `--batch-motlist` as a compatibility alias for split export when only one animation source is supplied.
- Updated README examples to avoid accidentally producing one file per animation for multi-MOTLIST exports.

### Verification evidence

- `dotnet build -c Release` succeeded with 0 warnings and 0 errors.
- Published executable `--help` includes `--batch-motlist|--split-animations`.

## 0.4.0 — DCC naming cleanup and multi-MOTLIST batch export

Completed: 2026-06-04

### What was attempted

This update addressed workflow polish found after the 0.3.2 exporter was usable in Blender:

- exported armature/root naming needed to be stable instead of deriving from the RE mesh name;
- exported mesh names needed to keep only the useful `Group_*_sub*__Material` portion;
- batch export needed to handle whole MOTLIST files and multiple MOTLIST inputs, not only one filtered selection;
- README usage needed to explain repository contents, batch behavior, and GLB/FBX selection.

### Result

The exporter now sets the exported root/armature name to `Armature`, strips the RE mesh basename from mesh object names, and post-processes GLB skin names to `Armature` so Blender imports use predictable armature naming.

The CLI now accepts repeated `--motlist` and `--mot` options. It also supports `--motlist-dir <folder>` to recursively collect `*.motlist*` files. With `--batch-motlist`, omitting `--animation-name` exports every selected motion as a separate file.

### Improvements over 0.3.2

- Added stable exported armature/root name: `Armature`.
- Added simplified mesh names such as `Group_0_sub0__ch0000_00_NThruster`.
- Added repeated `--motlist` support.
- Added repeated `--mot` support.
- Added `--motlist-dir` recursive MOTLIST discovery.
- Updated README with Git-tracked file hierarchy and format selection guidance.

### Verification evidence

- `dotnet build -c Release` succeeded with 0 warnings and 0 errors.
- Published executable `--help` includes repeated MOTLIST/MOT and `--motlist-dir` usage.

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

## 0.3.1 — GDeflate texture noise fix

Completed: 2026-06-04 01:36:55 +09:00

### What was attempted

0.3.0 exported PNG files, but many were filled with random-looking RGB noise. The next investigation compared the exporter path with REE Content Editor's texture preview path.

### Result

The cause was found in the texture loading sequence. REE Content Editor's `TextureLoader` decompresses GDeflate-compressed TEX files before using them, while the exporter called `TexFile.SaveAsDDS()` immediately after `TexFile.Read()`.

For PRAGMATA `.tex.251111100`, many material textures are GDeflate-compressed. Exporting them without decompression produced valid PNG files containing compressed payload noise.

### Fix

The exporter now mirrors REE Content Editor's texture loader behavior:

- if `tex.MustBeCompressed && tex.IsCompressed`, call `tex.DecompressGDeflate(...)`;
- use `GDeflateNet.GDeflate.Decompress(...)` for each mip;
- preserve warnings if a mip cannot be decompressed;
- apply the same step to streaming TEX files before DDS/PNG conversion.

### What we learned

- REE's texture parser alone is not enough for modern PRAGMATA textures; the loader's decompression step is part of the correct read path.
- Texture export should follow the same sequence as REE Content Editor preview: read, decompress if required, then convert/export.

### Improvements over 0.3.0

- PNG textures changed from compressed noise to coherent UV textures.
- The texture fix remained REE-native by using `GDeflateNet` and `TexFile.DecompressGDeflate(...)`.

### Verification evidence

Output folder: `C:/Users/hojin/Downloads/PRAG_PROJ/ree_exporter/texture_attempt1`

- PNG count: 54
- `ch0000_00_Hand_ALBD.png` changed from random RGB noise to coherent UV texture.
- Contact sheet: `texture_attempt1/attempt1_albd_contact_sheet.png`
- Neighbor correlation sanity check:
  - `ch0000_00_Hand_ALBD.png`: before `0.2293`, after `0.9280`
  - `ch0000_00_Head_ALBD.png`: before `0.2685`, after `0.8936`
  - `ch0000_00_Fabric_4MNewTexture_ALBD.png`: before `0.2508`, after `0.8719`
  - `ch0000_00_NRegArmor_4MNewTexture_ALBD.png`: before `0.2543`, after `0.8802`
- `dotnet build -c Release` succeeded.

### Package evidence

- Package: `REE-Content-Exporter.PRAGMATA-poc-0.3.1.zip`
- SHA256: `EAA8EA231CD519F73B0252B1EBC68624AAB678B30DF417D6E7E1B3CD8DD8A381`

## 0.3.2 — original RE mesh naming and animation verification

Completed: 2026-06-04 02:03:06 +09:00

### What was attempted

After the texture fix, Blender import showed that exported armature and mesh object names were derived from the absolute filesystem path, for example starting with:

```text
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\charact
```

That was not useful for DCC workflows. Object names should be derived from the original RE Engine mesh name.

During the same check, an output folder without animation was identified. That folder was from a texture-only verification run that intentionally used `--no-animations`, not from the normal full-export path.

### Result

The exporter now uses the RE mesh filename stem for the `CommonMeshResource` name.

### Fix

Changed resource naming from:

```csharp
PathUtils.GetFilepathWithoutExtensionOrVersion(meshPath)
```

to:

```csharp
PathUtils.GetFilenameWithoutExtensionOrVersion(meshPath)
```

For `ch0000_00_playergame.mesh.251121828`, Blender now imports:

- armature: `ch0000_00_playergame`
- mesh objects: `ch0000_00_playergame_Group_...`

### What we learned

- REE Content Editor's exporter uses the resource name as the scene/root naming seed.
- The CLI should pass a game-asset name, not a host filesystem path.
- Animation is preserved as long as `--motlist` or `--mot` is supplied and `--no-animations` is not used.

### Improvements over 0.3.1

- Clean Blender object names.
- No absolute path fragments in imported object names.
- Confirmed full export path still includes animation after the texture fix.

### Verification evidence

Output:

```text
C:/Users/hojin/Downloads/PRAG_PROJ/ree_exporter/name_anim_attempt1/ch0000_00_playergame_0320.glb
```

Blender 3.6 import:

- objects: 56
- mesh objects: 21
- armatures: 1
- armature name: `ch0000_00_playergame`
- mesh sample: `ch0000_00_playergame_Group_0_sub0__ch0000_00_NThruster`
- actions: 1
- action name: `ch0000_General_0320_Walk_Loop_VerA_ch0000_00_playergame`
- f-curves: 790
- action range: `[0.0, 52.0]`
- bad path-named objects: 0
- PNG textures: 54

### Package evidence

- Package: `REE-Content-Exporter.PRAGMATA-poc-0.3.2.zip`
- SHA256: `2A4AFE53AE6CB74E75E33B83B2C35D238FFCA18864540EDCA1A1CF28EED15AF5`

