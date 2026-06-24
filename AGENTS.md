# AGENTS.md

Operational rules for future AI agent sessions working in this repository.

This file captures the lessons learned while stabilizing the PRAGMATA `ch0000` and `ch0100` export scripts. Apply these rules when creating or modifying scripts for other model assets.


## Documentation is mandatory

Documentation must be kept current at all times.

When an AI agent changes behavior, scripts, CLI flags, dependencies, generated outputs, logging, reports, validation rules, or asset workflow assumptions, it must update the relevant documentation in the same work session.

Minimum expectations:

- Update `README.md` when public CLI flags, script parameters, dependencies, or expected output behavior change.
- Update `docs/CHANGELOG.md` when a meaningful feature, workflow, behavior, or verification milestone changes.
- Update or add focused docs under `docs/` when the change introduces a workflow users need to repeat.
- Update this `AGENTS.md` when a new durable rule is learned that should constrain future AI sessions.
- Do not leave documentation as a later task unless the user explicitly instructs not to document yet.
- If documentation is intentionally deferred because user verification is required, state that clearly and identify exactly which document needs the later update.

## Repository context

- This repository is `REE-Content-Exporter`, a wrapper around REE-Content-Editor / RE-Engine-Lib export functionality.
- The primary executable is built with:
  ```powershell
  dotnet build -c Release
  ```
- Convenience execution scripts live under:
  ```text
  export-scripts/
  ```
- Unreal-ready FBX export currently depends on Blender, not just the C# exporter.

## Git boundary rules

All git activity is strictly limited to this `REE-Content-Exporter` repository.

- Do not run `git status`, `git diff`, `git add`, `git commit`, `git push`, branch operations, log inspection, or any other git command in sibling repositories, parent directories, submodules, dependency checkouts, or source-reference checkouts.
- `REE-Content-Editor` and `RE-Engine-Lib` are sources of truth and dependency/reference code only. They may be read for comparison and implementation guidance, but their git state must not be inspected, modified, staged, committed, pushed, cleaned, reset, or otherwise managed from this exporter repo session.
- If a required fix appears to belong upstream, document the needed upstream change in this repository and ask the user how to proceed instead of performing git operations outside this repository.
- Git commands are allowed only when the working directory is the `REE-Content-Exporter` repository root or a path inside it.

## Release artifact dependency rules

Released builds must be functional immediately after download/extraction.

- Do not require users to install helper executables, native DLLs, converters, or other redistributable runtime tools separately.
- If the exporter or scripts need a redistributable runtime file, include it in the build/publish/release artifact and make build or publish fail when it cannot be included.
- The only dependency burden users should face is selecting or configuring paths to large external applications or user-owned assets that cannot reasonably be bundled, such as Blender or extracted game files.
- When adding a new runtime dependency, verify the published output or release archive contains it before calling the build usable.

## Do not treat streaming meshes as replacements for normal meshes

A normal `.mesh.*` file and its streaming `.mesh.*` file are complementary, not interchangeable.

- Normal mesh:
  - use as `--mesh` or `--additional-mesh`
  - contains skeleton, bones, material slots, metadata, draw calls, LOD structure, and buffer layout
  - is the authoritative source file for the asset
- Streaming mesh:
  - use only as `--streaming` or `--additional-streaming`
  - contains external geometry buffer data referenced by the normal mesh
  - must not replace the normal mesh path

Correct pattern:

```powershell
--mesh "<extract>\character\...\asset.mesh.version" `
--streaming "<extract>\streaming\character\...\asset.mesh.version"
```

Correct additional mesh pattern:

```powershell
--additional-mesh "<extract>\character\...\part.mesh.version" `
--additional-streaming "<extract>\character\...\part.mesh.version=<extract>\streaming\character\...\part.mesh.version"
```

Incorrect pattern:

```powershell
--mesh "<extract>\streaming\character\...\asset.mesh.version"
```

## Streaming asset rules for new scripts

When creating scripts for a new model asset:

1. Start with the normal mesh path under `character\...`.
2. Check whether a matching streaming mesh exists under `streaming\character\...`.
3. If the primary mesh has a matching streaming file, pass it explicitly with `--streaming`.
4. For each `--additional-mesh`, check whether a matching streaming file exists.
5. If an additional mesh has a matching streaming file, pass it explicitly with `--additional-streaming`.
6. If no streaming counterpart exists for an additional mesh, keep the normal `--additional-mesh` only.
7. Do not add guessed streaming paths that do not exist on disk.
8. Do not use `--allow-missing-streaming` in production scripts; reserve it for diagnosis only.

Before finishing a script change, verify the paths on disk:

```powershell
Test-Path -LiteralPath "<normal mesh path>"
Test-Path -LiteralPath "<streaming mesh path>"
```

## Blender dependency rules

Unreal-ready FBX scripts use Blender as a required re-export step.

- Required Blender version: `4.5.9 LTS`.
- Scripts must check the Blender version before exporting.
- Blender must be launched in background mode with factory startup:

```powershell
& $Blender --background --factory-startup --python $Py
```

Rationale:

- Blender re-bakes the source FBX into an Unreal-friendly FBX.
- `--factory-startup` prevents user-installed addons from affecting export behavior or polluting logs.
- A previously observed addon failure came from `io_model_semodel-master` calling removed Blender API `bpy.utils.unregister_module` during shutdown. This was not an exporter failure, but it polluted script output.

Do not remove `--factory-startup` unless the entire Blender execution strategy is re-verified.

## Unreal-ready FBX export rules

For Unreal-ready FBX scripts:

- Use source export scale:
  ```powershell
  --fbx-scale 100
  ```
- Use Blender export axis settings that were verified in Unreal:
  - Forward: `-Z`
  - Up: `Y`
- Preserve the normal mesh + streaming mesh relationship.
- Keep the Blender scene centimeter/unit handling already present in the scripts.
- Do not reintroduce root-roll normalization attempts unless explicitly requested and manually re-verified in Unreal.

Known acceptable state:

- Unreal skeletal mesh stands upright.
- Animation plays without wobble.
- Model scale imports correctly as `1.0` in Unreal.
- Root bone may still display a 90-degree roll/X rotation value. Previous investigation found this alone was not the cause of wobble or scale failure.

## Split MOTLIST export rules

For large animation sets, prefer per-MOTLIST export:

```powershell
--motlist-dir "<extract>\character\animation\...\motlist" `
--split-motlists
```

Rules:

- One Unreal-ready FBX should be produced per exportable MOTLIST.
- Empty/no-selected MOTLISTs are skipped by exporter logic and documented in:
  ```text
  skipped-motlists.md
  ```
- Some MOTLIST source FBXs can be generated successfully but import into Blender with zero actions. These must not fail the whole script.
- Zero-action Blender imports are skipped and documented in:
  ```text
  skipped-blender-motlists.md
  ```

Do not treat zero-action MOTLISTs as fatal unless the user specifically asks to debug that MOTLIST.

## Scene/minidemo MOTLIST actor rules

`gamedemo\minidemo\md*` MOTLIST files can contain scene actions for multiple actors, such as `md10015_ch0000_*`, `md10015_ch0100_*`, and `md10015_wp0900_*`.

- Do not export mixed scene actor actions onto one character skeleton as a final asset.
- Use `--scene-actor <actor-id>` when intentionally extracting one actor from a scene MOTLIST.
- If no `--scene-actor` is supplied, the CLI may infer the actor from the primary mesh name and filter mixed scene MOTLIST selections automatically.
- Use `--allow-mixed-scene-animations` only for diagnostics until a true multi-armature scene export path exists.
- If a filtered ch0100 scene export still reports only hashes `2184993242`, `3724475523`, `573179932`, and `386343545`, treat them as unresolved unnamed scene/helper/control channels, not as proof that the normal ch0100 body mesh set is missing parts.
## Standalone MOT loading rules

When loading individual `.mot.*` files through `--mot`, rely on REE-Lib's `MotFile.Read()` to load the standalone MOT bone table.

- Do not call `ReadBones(null)` again after `MotFile.Read()` for a normal standalone `.mot.*` path.
- `ReadBones(headerMot)` is only appropriate for embedded MOT entries that inherit or share bones from a MOTLIST/MOTPACK header MOT.
- Double-reading standalone MOT bones can duplicate bone/rest-pose data and make exported animation behavior diverge from REE-Content-Editor's direct MOT loader.

## GUI/CLI logic parity rules

Exporter behavior changes must be checked across every user-facing trigger that can reach that behavior.

- The Windows GUI must remain a trigger for the shared CLI/export pipeline, not a separate MOT, MOTLIST, mesh, texture, or FBX implementation.
- When changing CLI flags, export defaults, MOT/MOTLIST loading, animation filtering, missing-bone policy, streaming handling, texture behavior, or output naming, inspect and update all relevant argument builders:
  - GUI command construction in `GuiWizard.cs`
  - legacy console wizard and generated scripts in `Program.cs`
  - direct CLI parsing and execution in `Program.cs`
- Prefer refactoring shared command/option construction into common functions when the same behavior would otherwise be copied between GUI and CLI wizard paths.
- Before calling a logic fix complete, verify that the GUI trigger either invokes the same fixed CLI/export path or has been explicitly updated to preserve parity.
- For MOT and MOTLIST fixes specifically, confirm that GUI modes for MOTLIST folders, MOTLIST files, and raw MOT files produce the same CLI arguments that were used in focused CLI verification.

## Quaternion animation continuity rules

When exporting MOT rotations to FBX/GLB, normalize quaternion rotation keys and keep each bone channel on a continuous quaternion hemisphere before passing keys to Assimp. For FBX output, bake sparse MOT rotation tracks at integer source frames using the same shortest-path quaternion interpolation behavior as REE-Content-Editor playback.

- Adjacent quaternion keys that represent nearly identical poses can still have opposite signs because `q` and `-q` are the same rotation.
- Some downstream FBX importers can interpret that sign discontinuity as the long arc around the sphere, producing one-frame or two-frame rotation flukes even when REE-Content-Editor playback is smooth.
- Use the previous exported key for the same bone channel as the continuity reference; if `Quaternion.Dot(previous, current) < 0`, negate the current key before writing it.
- Do not leave sparse FBX rotation gaps for downstream importers to solve differently. If a source MOT has keys at frames 24 and 26, FBX output should include the sampled frame 25 rotation.
- Guard against zero-length quaternions by reusing the previous valid key, or identity for the first key.
- This is an export representation fix, not a MOT data edit. Do not mutate the source `MotFile` track arrays just to make FBX interpolation safer.
- The same rotation-continuity rule applies to standalone MOT and MOTLIST entries because both become exported animation channels through the shared Assimp scene path. A MOTLIST animation that plays correctly in REE-Content-Editor can still show FBX/Unreal flukes if sparse quaternion channels are not normalized, hemisphere-continuous, and baked at integer frames.
- After updating or rebasing REE-Content-Editor or RE-Engine-Lib, explicitly re-check `ContentEditor.App\CustomizedFileLoaders\MeshConversion\AssimpMeshExport.cs`: `AddMotToScene` must not write raw `clip.Rotation.rotations[i]` values directly to Assimp rotation keys for FBX export.
- When validating a suspected MOTLIST rotation fluke, prefer a scoped single-animation export, then audit both the source FBX and Blender re-exported FBX for the expected action, expected frame range, dense integer-frame animated rotation keys, and zero adjacent rotation jumps greater than pi radians.

## Intermediate source FBX handling

The scripts intentionally produce a temporary/source FBX first, then Blender re-exports the final Unreal-ready FBX.

Rules:

- Final Unreal-ready files should use concise names such as:
  ```text
  <asset>_<motlist>_unreal.fbx
  ```
- Intermediate source FBXs should be removed by default after successful Blender re-export.
- If a zero-action MOTLIST is skipped by Blender, remove its source FBX by default as well.
- Keep intermediate source FBXs only when the user passes `-KeepSourceFbx`.
- For current CLI/GUI exports, final Unreal-ready FBX means `--unreal-ready-fbx` produced `*_unreal.fbx`; do not treat a remaining `*_all_animations.fbx` or `*_source.fbx` as the golden Unreal import artifact unless the user explicitly asked for an intermediate.

This avoids confusion between the direct source FBX and the Unreal-ready Blender FBX.

## Logging rules

Execution scripts should write logs into the generated export folder.

- Successful run:
  ```text
  <script-name>-SUCCESS.log
  ```
- Failed run:
  ```text
  <script-name>-FAIL.log
  ```

The suffix allows the user to determine outcome without opening the log.

When adding new script-level skip behavior, document it inside the export folder with a Markdown report, similar to:

```text
skipped-motlists.md
skipped-blender-motlists.md
*.skipped-animation-bones.md
```

GUI exports must also write a persistent per-run debug log file. Do not leave the GUI's in-window log as the only record of an export. The GUI log should be created when the run starts, include the invoked CLI command and streamed exporter output, and end with an outcome-visible filename such as `*-GUI-SUCCESS__<timestamp>.log` or `*-GUI-FAIL__<timestamp>.log`.

## Progress output rules

Do not leave Blender's raw FBX exporter tuple output as the only progress signal, e.g.:

```text
(bpy.data.armatures['Armature'], 'POSE')
```

Scripts should print explicit progress messages similar to other exporter phases:

```text
BLENDER_PROGRESS Motlist 3/14 exporting animation 12/48: <animation-name>
```

This matters because large per-MOTLIST exports can take a long time.

## Texture rules

Texture loss has repeatedly been caused by path/source ambiguity.

Rules:

- Always verify that the generated export folder contains a non-empty `textures/` folder unless the script intentionally uses `--no-textures`.
- Do not silently accept a missing or empty texture folder in production scripts.
- Do not release a build that depends on users installing texture helper tools separately. PNG export requires `texconv.exe`; release artifacts must bundle it beside the exporter entry-point executables, and build/publish should fail if the converter cannot be included.
- Keep the dynamic path fallback behavior for old/new `natives\STM` and `re_chunk_000\streaming` style paths.
- Treat unsupported depth/3D TEX resources as warnings only; these textures are not needed for exported material files. Do not broaden this to missing texture sources or normal 2D texture read, DDS, or PNG conversion failures, which must remain fatal.
- If adding a new asset script, confirm its material/MDF lookup still resolves textures from the correct source mesh.

## Script execution rules for AI agents

- Do not run long export scripts unless the user explicitly asks.
- If the user asks to run scripts, prefer checking logs after completion rather than live tracking every line, unless live monitoring is specifically requested.
- Wizard choice prompts must require explicit input. Do not treat Enter as a default answer for yes/no or numbered choices; only path/text prompts with an intentional displayed default may accept Enter.
- For script-only edits, validate with:
  ```powershell
  [scriptblock]::Create((Get-Content -LiteralPath "<script.ps1>" -Raw))
  ```
- For C# logic changes, validate with:
  ```powershell
  dotnet build -c Release
  ```
- Do not commit unless the user explicitly asks for a commit.
- If there are pre-existing uncommitted changes, preserve them and report them clearly.

## Repo-local `ship` workflow

When the user prompt is exactly `ship`, treat it as a repo-exclusive release workflow trigger.

Required behavior:

1. Inspect `git status --short --branch` and the current diff before doing anything else.
2. Run smoke tests before committing:
   - always run `dotnet build -c Release`
   - if tracked PowerShell scripts changed, validate each changed script with:
     ```powershell
     [scriptblock]::Create((Get-Content -LiteralPath "<script.ps1>" -Raw))
     ```
   - do not run long asset export scripts unless the user explicitly asks
3. If smoke tests pass and there are changes, commit logical changes separately where practical, then push the current branch.
4. Do not silently stage ambiguous untracked artifacts such as logs, `dist/`, or local verification CSVs unless the ship request explicitly includes them.
5. After pushing, inspect GitHub releases with `gh release list` and `gh release view` to identify the latest release.
6. Ask the user whether to update the existing latest release or version up. If versioning up, ask for or infer the next patch version only after showing the latest version found.
7. After the user chooses the release target, rebuild the release zip, replace or upload the target release asset, and update that release description.
8. Immediately after a successful release upload/replacement, create a local test build folder in the repository parent directory for the user to run directly. Use a timestamped folder name such as:
   ```text
   ../REE-Content-Exporter-<version>-localbuild-<yyyyMMdd_HHmmss>/
   ```
   Publish with the same release profile, verify the folder contains the required runtime files listed below, and smoke-test `REE-Content-Exporter-CLI.exe --help` plus `REE-Content-Exporter-GUI.exe` startup from that local folder.

Release packaging defaults:

- Use the existing `win-x64-singlefile` publish profile.
- Verify the release archive contains:
  ```text
   REE-Content-Exporter-GUI.exe
   REE-Content-Exporter-CLI.exe
  texconv.exe
  DirectXTex.dll
  libGDeflate.dll
  assimp.dll
  ```

## Current known-good script patterns

Use these existing scripts as reference patterns when adding a new asset:

```text
export-scripts/export_ch0000_all_motlists_unreal_fbx.ps1
export-scripts/export_ch0100_all_motlists_unreal_fbx.ps1
export-scripts/template_mesh_only_unreal_fbx.ps1
```

For new assets, copy the structure, not the paths. Re-verify every mesh, streaming mesh, motlist, and texture path for the new asset.
