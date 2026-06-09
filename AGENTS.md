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
- Do not release a build that depends on users installing texture helper tools separately. PNG export requires `texconv.exe`; release artifacts must bundle it beside `REE-Content-Exporter.exe`, and build/publish should fail if the converter cannot be included.
- Keep the dynamic path fallback behavior for old/new `natives\STM` and `re_chunk_000\streaming` style paths.
- If adding a new asset script, confirm its material/MDF lookup still resolves textures from the correct source mesh.

## Script execution rules for AI agents

- Do not run long export scripts unless the user explicitly asks.
- If the user asks to run scripts, prefer checking logs after completion rather than live tracking every line, unless live monitoring is specifically requested.
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

## Current known-good script patterns

Use these existing scripts as reference patterns when adding a new asset:

```text
export-scripts/export_ch0000_all_motlists_unreal_fbx.ps1
export-scripts/export_ch0100_all_motlists_unreal_fbx.ps1
export-scripts/template_mesh_only_unreal_fbx.ps1
```

For new assets, copy the structure, not the paths. Re-verify every mesh, streaming mesh, motlist, and texture path for the new asset.
