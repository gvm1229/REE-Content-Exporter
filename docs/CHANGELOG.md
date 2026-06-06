# CHANGELOG

## 0.7.0 - Unreal-ready FBX pipeline, scripting hardening, and interactive export workflow

Completed: 2026-06-07

### What was attempted

After `0.6.0`, split MOTLIST GLB export was stable enough for Blender inspection, but the Unreal import path still had several practical failures:

- direct FBX exports could wobble in Unreal even when Blender played the same animations correctly;
- Blender re-export was needed, but its version, axis, scale, and addon behavior had to be made deterministic;
- source FBX files and final Unreal FBX files were easy to confuse;
- large all-animation FBX files were slow to test and import;
- MOTLISTs that generated source FBXs but imported into Blender with zero actions could abort whole batch runs;
- texture output could silently disappear when paths or source folders were wrong;
- streaming mesh usage needed to be explicit for both primary and additional meshes;
- project documentation and future-agent scripting rules had fallen behind the actual workflow;
- users needed an interactive way to discover and select export inputs instead of hand-writing every command.

### Result

The current Unreal-ready FBX workflow is a two-stage export:

1. `REE-Content-Exporter` writes source FBX files from RE Engine mesh/MOTLIST data.
2. Blender `4.5.9 LTS` imports those source FBXs, applies the verified unit/axis/export settings, creates explicit NLA strips, and writes final Unreal-ready FBX files.

The verified Unreal path now uses:

- `--fbx-scale 100` during source FBX export;
- Blender scene/unit handling for centimeter-correct Unreal scale;
- Blender export axis `-Z Forward`, `Y Up`;
- Blender background execution with `--factory-startup` to isolate user addons;
- per-MOTLIST Unreal FBX output instead of one huge all-animation FBX;
- concise final filenames such as `<asset>_<motlist>_unreal.fbx`;
- source FBX cleanup by default after successful Blender re-export.

The known-good Unreal outcome is:

- skeletal mesh stands upright;
- animations play without wobble;
- model scale imports correctly as `1.0` in Unreal;
- textures are present in the export folder;
- the root bone may still display a 90-degree roll/X value, but that was not the cause of the wobble or scale failures.

### Exporter CLI changes

- Added `--fbx-scale <scale>` to control the scale passed into the REE/Assimp FBX export stage.
- Added explicit additional mesh streaming support:
  ```text
  --additional-streaming <additional-mesh-path>=<streaming-mesh-path>
  ```
- Preserved the correct relationship between normal and streaming meshes:
  - normal mesh remains `--mesh` / `--additional-mesh`;
  - streaming mesh is only passed through `--streaming` / `--additional-streaming`.
- Added path fallback support for flat `re_chunk_000` layouts as well as older `natives\STM` style layouts.
- Added dynamic streaming-file discovery for `re_chunk_000\streaming\...` sibling paths.
- Added stronger validation around split export output creation and missing files.
- Added support for skipped MOTLIST reporting in split-MOTLIST mode.
- Added single-file publish/dependency version lookup fixes for the interactive workflow.

### Unreal FBX script changes

- Replaced one-off FBX scripts with final per-character Unreal-ready scripts:
  - `export-scripts\export_ch0000_all_motlists_unreal_fbx.ps1`
  - `export-scripts\export_ch0100_all_motlists_unreal_fbx.ps1`
- Moved execution scripts into `export-scripts\`.
- Kept GLB scripts for each character:
  - `export-scripts\export_ch0000_all_motlists_glb.bat`
  - `export-scripts\export_ch0100_all_motlists_glb.bat`
- Added a mesh-only Unreal FBX template:
  - `export-scripts\template_mesh_only_unreal_fbx.ps1`
- Added per-MOTLIST Unreal FBX generation to reduce import/test size and avoid one huge FBX.
- Added Blender progress output so long FBX exports show current MOTLIST/action progress instead of only raw Blender tuple output.
- Added export logs into each generated job folder.
- Added `-SUCCESS.log` / `-FAIL.log` suffixes so the result is visible from the filename.
- Added zero-action Blender MOTLIST detection. If Blender imports a source FBX with zero actions, the MOTLIST is skipped, recorded in `skipped-blender-motlists.md`, and the whole run continues.
- Added source FBX cleanup by default after Blender re-export or zero-action skip; `-KeepSourceFbx` keeps intermediates for debugging.
- Added texture folder validation so missing/empty textures are treated as export failures.
- Added `--factory-startup` to Blender invocations to prevent user-installed addons from affecting exports or polluting logs.
- Updated scripts to explicitly pass streaming buffers where matching streaming assets exist:
  - ch0000 primary mesh now passes `--streaming`;
  - ch0100 primary mesh passes `--streaming`;
  - ch0100 additional mesh part `40` passes `--additional-streaming`;
  - ch0100 parts `10` and `20` remain normal additional meshes because matching streaming files were not present in the extract.

### Dependency and repository reproducibility changes

- Reworked the REE-Content-Editor dependency setup so required custom dependency patches are reproducible instead of living only in an untracked local dependency folder.
- Added dependency setup documentation under `docs/dependency_setup.md`.
- Added patch files for the REE/Content Editor dependency changes needed by this exporter.
- Added `pragmata.list` to the repository as a tracked asset/file reference list.

### Documentation changes

- Documented Blender as a required dependency for Unreal-ready FBX export.
- Specified Blender `4.5.9 LTS` as the required and verified version.
- Documented the Blender-to-Unreal workflow in detail.
- Unified project documentation language to English.
- Documented all exporter flags in the README, including behavior and expected outcomes.
- Documented skipped MOTLIST behavior and skipped Blender MOTLIST behavior.
- Documented macOS execution-script feasibility as a note only, because the current REE-Content-Editor dependency uses Windows executable assumptions.
- Added `AGENTS.md` to encode future AI-agent rules for asset scripting, streaming mesh handling, Blender usage, logging, texture verification, and commit discipline.

### Interactive export workflow changes

- Added an interactive export wizard to reduce manual command construction.
- Added a Windows single-file publish profile.
- Updated README guidance for interactive usage.
- Fixed dependency version lookup for single-file publishing.

### Improvements over 0.6.0

- Unreal-ready FBX export is now the documented path for Unreal imports.
- Blender re-export is deterministic and isolated from user addons.
- Per-MOTLIST Unreal FBX output makes large animation sets practical to test.
- Zero-action MOTLISTs no longer abort full-character runs.
- Logs and Markdown reports now make export failures/skips auditable after completion.
- Streaming mesh usage is explicit and documented for both primary and additional meshes.
- Source-vs-final FBX confusion is reduced by concise final names and source cleanup.
- Texture output is validated instead of silently accepted when missing.
- Future AI sessions now have repo-local guidance in `AGENTS.md`.
- Interactive export discovery/selection is available on the feature branch.

### Verification evidence

- `dotnet build -c Release` succeeded after the major script/CLI changes.
- PowerShell syntax validation succeeded for the Unreal FBX scripts and mesh-only template.
- Blender `--factory-startup` smoke test ran without loading the problematic user addon.
- A failed `*_Tree_*` source FBX was directly inspected in Blender and confirmed to import with an armature/meshes but zero actions, validating the zero-action skip logic.
- The current ch0000/ch0100 scripts were checked against the extracted file tree for existing normal and streaming mesh paths.

## 0.6.0 - split MOTLIST exports and batch hardening

Completed: 2026-06-04

### What was attempted

Full-character combined exports can become too large for Assimp's GLB writer, especially for characters such as `ch0100` where multiple mesh parts and many MOTLIST files are involved. A single all-animation GLB was not reliable enough for practical use.

The exporter also needed clearer progress during long writes, better diagnostics when Assimp failed to produce an output file, and safer batch files that keep the console open on errors.

### Result

Added `--split-motlists`. When used with `--motlist-dir`, every MOTLIST in the directory is exported as its own GLB/FBX, while all files are placed in one shared export job folder. This keeps each file smaller than a full all-MOTLIST export while avoiding per-animation clutter.

The long-running final write status now includes file progress for multi-file modes. For example:

```text
Writing GLB2 file 3/14: ch0100_General
```

Assimp export failures are now checked directly. If `ExportFile(...)` returns failure or produces no output file, the exporter raises a clear error instead of crashing later while trying to normalize a missing GLB.

The character batch files were refined so only one combined script remains per character:

- `export-scripts\export_ch0000_all_motlists_glb.bat`
- `export-scripts\export_ch0100_all_motlists_glb.bat`

Both scripts now use `--motlist-dir` and `--split-motlists`.

### Improvements over 0.5.3

- Added `--split-motlists`.
- Added shared job-folder output for per-MOTLIST GLB/FBX exports.
- Added file-count context to final `Writing GLB2 file` progress in multi-file exports.
- Added direct Assimp output-file validation.
- Added pause-on-error handling to batch files so CMD windows remain open after failures.
- Removed redundant ch0000 single-MOTLIST batch file.
- Avoided fragile batch caret line continuations after CMD could misread `--output` as a separate `utput` command and return exit code `9009` after a successful export.

### Current batch behavior

`export-scripts\export_ch0000_all_motlists_glb.bat` exports every MOTLIST under:

```text
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\character\animation\ch\ch00\ch0000\motlist
```

`export-scripts\export_ch0100_all_motlists_glb.bat` exports every MOTLIST under:

```text
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\character\animation\ch\ch01\ch0100\motlist
```

and includes ch0100 mesh folders `00`, `10`, `20`, and `40`, while intentionally excluding `15` and `45`.

### Verification evidence

- `dotnet build -c Release -p:UseAppHost=false -o .omx\build-check` succeeded with 0 warnings and 0 errors.
- A split-MOTLIST smoke test exported two ch0000 MOTLISTs into one shared job folder as two separate GLBs.

## 0.5.3 - per-job output folders

Completed: 2026-06-04

### What was attempted

Exporter products were written directly beside the requested GLB/FBX path: the model file, `textures/`, and any Markdown reports. Running multiple jobs in the same parent output folder could overwrite texture folders or reports from earlier jobs.

### Result

Every export job now writes into a dedicated subfolder. The folder name uses the primary mesh name, the job timestamp, and a short hash suffix for uniqueness and path safety. The requested GLB/FBX filename is preserved inside that job folder.

### Improvements over 0.5.2

- Added automatic per-job output folder creation.
- Folder names use `<mesh-name>__<yyyyMMdd_HHmmss>__<6-char-hash>`.
- Texture folders and skipped-animation reports now stay isolated per export job.
- Split-animation exports also create separate job folders per animation output.

### Verification evidence

- `dotnet build -c Release` succeeded with 0 warnings and 0 errors.
- Smoke export wrote `smoke.glb` under `.omx/tmp-job-folder-smoke/ch0000_00_playergame__<hash>/`.

## 0.5.2 - no-placeholder animation bone mode

Completed: 2026-06-04

### What was attempted

The strict `--skip-missing-animation-bones` behavior skipped entire animations even when only helper/runtime bone channels were missing. This skipped useful animations such as `ch0000_General_0320_Walk_Loop_VerA`, which had previously been verified as usable.

### Result

`--skip-missing-animation-bones` keeps its strict behavior: any animation that references a missing bone is excluded entirely. A new `--no-placeholder-animation-bones` flag keeps the animation, skips only the channels that target missing bones, and avoids creating placeholder `hash...` bones.

When `--no-placeholder-animation-bones` is active, the exporter writes `<output-name>.skipped-animation-bones.md` next to the GLB/FBX file.

### Improvements over 0.5.1

- Added `--no-placeholder-animation-bones`.
- Preserved current strict `--skip-missing-animation-bones` behavior.
- Added per-channel missing-bone report generation.
- Updated batch files to use `--no-placeholder-animation-bones` instead of strict animation skipping.

### Verification evidence

- `dotnet build -c Release` succeeded with 0 warnings and 0 errors.

## 0.5.1 - missing animation bone skip report

Completed: 2026-06-04

### What was attempted

Full MOTLIST exports can include animations that reference helper, runtime, weapon, or interaction bones absent from the exported mesh skeleton. Previously, REE Content Editor's export path added placeholder `hash...` bones for those missing references.

### Result

The CLI now supports `--skip-missing-animation-bones`. When enabled, animations that reference bones missing from the exported mesh skeleton are excluded instead of creating placeholder bones. A sidecar Markdown report is generated next to the GLB/FBX output and lists every skipped animation plus the missing bone hashes that caused the skip.

### Improvements over 0.5.0

- Added `--skip-missing-animation-bones` CLI option.
- Added pre-export missing-bone detection for MOT animations.
- Added `<output-name>.skipped-animations.md` report generation.
- Preserved default behavior for users who still want placeholder bones and maximum animation inclusion.

### Verification evidence

- `dotnet build -c Release` succeeded with 0 warnings and 0 errors.

## 0.5.0 ? additional mesh export support

Completed: 2026-06-04

### What was attempted

Some RE Engine characters are split across several mesh files. The exporter previously accepted only one `--mesh`, which was enough for `ch0000` but not for characters such as `ch0100` where specific mesh parts need to be selected manually.

### Result

The CLI now supports repeated `--additional-mesh <mesh.path>` arguments. The primary mesh and all additional meshes are exported through one REE Content Editor scene, sharing one exported armature and one animation set. Additional meshes use the same automatic streaming mesh lookup as the primary mesh and can contribute their own auto-detected sibling MDF material data.

### Improvements over 0.4.1

- Added repeated `--additional-mesh` CLI option.
- Preserved manual control over exactly which mesh parts are included.
- Added automatic streaming data lookup for each additional mesh.
- Added automatic sibling MDF lookup and texture/material slot contribution for each additional mesh.
- Kept output naming normalization: one `Armature` root and simplified mesh object names.

### Verification evidence

- `dotnet build -c Release` succeeded with 0 warnings and 0 errors.

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
