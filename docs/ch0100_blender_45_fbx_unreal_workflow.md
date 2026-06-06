# ch0100 Attack FBX to Blender 4.5.9 to Unreal Workflow

This document describes the currently validated FBX workflow for using PRAGMATA `ch0100` Attack MOTLIST animations in Unreal. The core pipeline is: **REE-Content-Exporter creates a source FBX, then Blender 4.5.9 imports, bakes, and re-exports the animation into an Unreal-friendly FBX**.

## Current conclusions

- **Blender is required** for Unreal-ready FBX output.
- The direct Assimp-authored FBX can import and play correctly in Blender, but Unreal may interpret its animation curves with severe wobble.
- Importing through Blender 4.5.9, applying the armature object rotation/scale, creating explicit NLA strips, and re-exporting baked animation removes the Unreal wobble.
- The Unreal-validated axis and scale combination is:
  - Blender scene unit: Metric, `scale_length = 0.01`
  - Blender FBX export axis: `-Z Forward`, `Y Up`
  - Blender FBX export `global_scale = 1.0`
  - `apply_unit_scale = True`
  - `apply_scale_options = FBX_SCALE_ALL`
- Keep `--fbx-scale 100` in the **REE-Content-Exporter source FBX stage**.
- In the Blender stage, use centimeter scene units and `global_scale=1.0`. This keeps the final Unreal placement scale correct without using Unreal's import scale override, and Unreal displays scale as `1.0`.
- The Skeleton root bone Roll/X rotation value may still display as `90°`. Do **not** normalize it in the exporter or scripts. Unreal composes FBX axis conversion, pre-rotation, and reference pose data during import; trying to force only the displayed value to `0` can desynchronize the skeleton basis from the animation basis.

## Dependencies

### Required

- .NET SDK
- Loose RE Engine files extracted with RETool
- `texconv.exe` or a DirectXTex installation capable of PNG conversion
- Patched `REE-Content-Editor` sibling checkout
- Blender `4.5.9 LTS`

Expected development layout:

```text
parent-folder/
  REE-Content-Editor/
  REE-Content-Exporter/
```

Recreate the patched dependency:

```powershell
.\scripts\setup-content-editor-dependency.ps1 -Force
```

Build:

```powershell
dotnet build -c Release
```

The current scripts call Blender from:

```text
C:\Program Files\Blender Foundation\Blender 4.5\blender.exe
```

Blender is invoked in headless/background mode:

```powershell
& "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" --background --python <generated-script.py>
```

## Current recommended FBX scripts

The maintained Unreal-ready FBX scripts are one script per character. Both use `--motlist-dir` and `--split-motlists` to produce **one Unreal-ready FBX per non-empty MOTLIST**. Empty MOTLISTs, or MOTLISTs with zero selected animations after filtering, are detected by the exporter and skipped before Blender is invoked.

```powershell
.\export-scripts\export_ch0000_all_motlists_unreal_fbx.ps1
.\export-scripts\export_ch0100_all_motlists_unreal_fbx.ps1
```

Default input root:

```text
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000
```

ch0100 included meshes:

```text
character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828
character\ch\ch01\ch0100\10\ch0100_10.mesh.251121828
character\ch\ch01\ch0100\20\ch0100_20.mesh.251121828
character\ch\ch01\ch0100\40\ch0100_40_neo.mesh.251121828
```

ch0100 MOTLIST directory:

```text
character\animation\ch\ch01\ch0100\motlist
```

ch0100 script output:

- Unreal-ready FBX per non-empty MOTLIST, for example: `ch0100_Attack_unreal.fbx`
- Export log: `ch0100_motlists_unreal_export-SUCCESS.log` or `ch0100_motlists_unreal_export-FAIL.log`
- Texture directory: `textures\`
- Skipped MOTLIST report: `skipped-motlists.md`
- Skipped bone channel report per MOTLIST, if needed: `ch0100_Attack.skipped-animation-bones.md`

Temporary split source FBX files use names like `0000_ch0100_Attack_all_animations.fbx` while Blender is running. Each source FBX is deleted after its successful Blender re-export to avoid leaving two similar FBX files in the output folder. Run the script with `-KeepSourceFbx` only when debugging the Blender round trip.

The script starts a PowerShell transcript before the exporter call and moves it into the final job folder at completion. The log captures the exporter output, Blender output, progress lines, final artifact paths, and failure status if the script stops early. Successful runs write `*-SUCCESS.log`; failed runs write `*-FAIL.log`, making the outcome visible without opening the file. If the script fails before a job folder is known, it prints `EXPORT_LOG_TEMP=...` so the temporary `*-FAIL__<timestamp>.log` can still be inspected.

## REE-Content-Exporter stage

The ch0100 script runs the exporter with this command shape:

```powershell
& $Exporter `
  --mesh "$Root\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828" `
  --streaming "$Root\streaming\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828" `
  --additional-mesh "$Root\character\ch\ch01\ch0100\10\ch0100_10.mesh.251121828" `
  --additional-mesh "$Root\character\ch\ch01\ch0100\20\ch0100_20.mesh.251121828" `
  --additional-mesh "$Root\character\ch\ch01\ch0100\40\ch0100_40_neo.mesh.251121828" `
  --motlist-dir "$Root\character\animation\ch\ch01\ch0100\motlist" `
  --split-motlists `
  --no-placeholder-animation-bones `
  --texture-format png `
  --fbx-scale 100 `
  --output $OutputRequest
```

Important details:

- Use `--no-placeholder-animation-bones`. Missing animation bone channels are skipped per channel instead of creating placeholder bones.
- Use `--texture-format png` to generate a texture folder that is easier to inspect and reconnect in Blender/Unreal.
- Do not remove `--fbx-scale 100`. It is needed in the source FBX stage to bring the model size into the expected centimeter-oriented range.
- Do not apply `global_scale=100` again in Blender; doing so can over-scale the final FBX.

## Texture path fallback

The recurring missing-texture issue came from two possible RE Engine extraction layouts:

```text
<extract>\re_chunk_000\natives\STM\...
<extract>\re_chunk_000\...
```

The exporter now tries both the old STM layout and the flat `re_chunk_000` layout for streaming buffers and loose texture lookup.

Examples:

```text
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\character\...
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\streaming\character\...
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\character\...
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\streaming\character\...
```

The sample execution scripts verify immediately after source export that `textures\` exists and contains at least one file. If the texture folder is missing or empty, the script fails.

## Blender stage

The script generates a temporary Python file and runs Blender in background mode.

### 1. Reset the scene

- Delete the default scene.
- Remove previous action, armature, and mesh datablocks.

### 2. Set centimeter units

```python
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 0.01
```

This is important because Unreal uses centimeters while Blender uses meters. Handling the unit conversion here avoids needing Unreal import scale `100`.

### 3. Import the source FBX

```python
bpy.ops.import_scene.fbx(
    filepath=str(src),
    use_anim=True,
    automatic_bone_orientation=False,
    ignore_leaf_bones=False,
    force_connect_children=False,
)
```

### 4. Apply armature object transform

```python
bpy.ops.object.transform_apply(
    location=False,
    rotation=True,
    scale=True,
    properties=True,
)
```

This stabilizes the Blender object-level rotation and scale before export. It is not intended to remove the displayed Unreal Skeleton root Roll value.

### 5. Convert actions into explicit NLA strips

The script creates one NLA track/strip for each action.

Reasons:

- Avoid relying on Blender's `All Actions` compatibility scan.
- Make FBX animation stacks/takes explicit.
- Reduce cases where only some animations import or some imported animations are static.

### 6. Export FBX from Blender

Current validated settings:

```python
bpy.ops.export_scene.fbx(
    filepath=str(out),
    check_existing=False,
    use_selection=False,
    object_types={'ARMATURE', 'MESH'},
    use_mesh_modifiers=True,
    add_leaf_bones=False,
    primary_bone_axis='Y',
    secondary_bone_axis='X',
    use_armature_deform_only=False,
    bake_anim=True,
    bake_anim_use_all_bones=True,
    bake_anim_use_nla_strips=True,
    bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True,
    bake_anim_step=1.0,
    bake_anim_simplify_factor=0.0,
    axis_forward='-Z',
    axis_up='Y',
    global_scale=1.0,
    apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_ALL',
    use_space_transform=True,
    bake_space_transform=False,
    path_mode='AUTO',
    embed_textures=False,
)
```

Notes:

- The earlier experimental `-Y Forward / Z Up`, `global_scale=100` combination is not the current recommendation.
- The current Unreal-validated combination is `-Z Forward / Y Up`, centimeter scene units, and `global_scale=1.0`.
- `bake_space_transform=True` did not resolve the root Roll display issue and is not used.

## Unreal import checklist

Import into a fresh Unreal test folder. Existing skeleton or animation assets can contaminate variant testing if Unreal reuses them.

Check:

1. The Skeletal Mesh stands upright.
2. Animations play while the model remains upright.
3. Animation playback has no wobble.
4. The Skeletal Mesh has correct size when placed in a level/map.
5. No Unreal import scale override is required.
6. Unreal asset/component scale displays as `1.0`.
7. The texture folder exists and contains PNG/manifest files needed for material reconnection.
8. If root motion is needed, enable root motion on the animation sequence and verify capsule movement direction and distance in Unreal.

## Current policy for root Roll/X rotation 90°

Unreal may display the Skeleton root bone Roll/X rotation as `90°`. This value remains a known limitation and is intentionally not normalized.

Reasons:

- The important runtime results have already been achieved: upright model, upright animations, no wobble, and scale `1.0`.
- Attempts to force only the displayed root Roll value to `0` changed the FBX transform layers in ways that separated the skeleton basis from the animation basis, causing animations to play with the model lying down.
- Moving the value into FBX `PreRotation` did not change Unreal's displayed result; Unreal composed it back into the imported skeleton transform.
- Unreal composes FBX axis conversion, pre-rotation, and reference pose data during import. Editing raw FBX properties does not guarantee that Unreal's Skeleton Editor will display `0`.

Current rules:

- Do not add root Roll 90 normalization code.
- Do not add a roll-0 normalization flag to the exporter or scripts.
- Validate root motion by actual Unreal movement/capsule behavior.
- Prioritize keeping the skeleton reference pose and animation root track in the same basis over making the displayed Roll value look clean.

## Failed approaches and lessons learned

### 1. Exporter-level normalization flag

An early plan tried to make root/object Roll display as `0` through a normalization flag. The actual Unreal blocker was animation wobble, and wobble was not caused by normalization; it came from Unreal's interpretation of the direct Assimp FBX animation data.

Conclusion: the normalization flag was removed.

### 2. Blender `bake_space_transform=True`

This did not change the root Roll display value.

Conclusion: do not use it.

### 3. Armature scale-only / roll-0 candidate

Some internal FBX values moved closer to `0`, but Unreal played animations with the model lying down.

Conclusion: discard this approach because it can desynchronize skeleton and animation coordinate bases.

### 4. FBX `PreRotation` patch

Moving FBX `Lcl Rotation` into `PreRotation` looked plausible at the file-property level, but Unreal composed it back into the imported skeleton transform.

Conclusion: this did not solve the Unreal-displayed Roll value.

## Local verification procedure

Minimum verification after code or script changes:

```powershell
dotnet build -c Release
.\export-scripts\export_ch0100_all_motlists_unreal_fbx.ps1
```

Success criteria:

- Build: 0 warnings, 0 errors.
- Exporter exit code: 0.
- Blender exit code: 0.
- Temporary source FBX files generated per non-empty MOTLIST, then removed unless `-KeepSourceFbx` was used.
- Blender Unreal-ready FBX files generated per non-empty MOTLIST, e.g. `ch0100_Attack_unreal.fbx`.
- Export log generated with a status suffix, e.g. `ch0100_motlists_unreal_export-SUCCESS.log`.
- `textures\` folder generated.
- Texture file count greater than 0.
- Animation stack count matches the test target:
  - all-MOTLIST script: all selected animations from the target character's MOTLIST folder.

Expected final output folder shape:

```text
<job-folder>\
  ch0100_Attack_unreal.fbx
  ch0100_General_unreal.fbx
  ch0100_motlists_unreal_export-SUCCESS.log
  skipped-motlists.md
  ch0100_Attack.skipped-animation-bones.md   # only if missing animation bone channels were skipped
  textures\
    materials.textures.json
    ...
```

## All animation MOTLIST export

The final FBX scripts already remove the `--animation-name` filter and use `--motlist-dir` plus `--split-motlists`. For ch0100, this means every non-empty MOTLIST in the motlist folder becomes its own Unreal-ready FBX.

Keep these settings for many-animation export:

- Create one NLA strip per action.
- `bake_anim_use_nla_strips=True`
- `bake_anim_use_all_actions=False`
- `bake_anim_simplify_factor=0.0`
- In Unreal, verify that all FBX takes/stacks become animation sequences.

## Quick diagnosis

- Animation is wrong in Blender: likely exporter/source FBX problem.
- Animation is correct in Blender but wobbles in Unreal: Blender re-bake/re-export is needed or the Blender export settings are wrong.
- Model lies down in Unreal: axis settings are wrong, or skeleton and animation bases are mismatched.
- Model is too small in Unreal: check Blender scene units and export scale. Current recommendation is `scale_length=0.01`, `global_scale=1.0`.
- Texture folder is missing: source path layout fallback or texture export failed. Current scripts fail if the texture folder is missing or empty.
- Only root Roll 90 remains: this is a known limitation. Validate actual root motion behavior in Unreal instead of treating the displayed value alone as a failure.
