# ch0100 Attack FBX Blender 4.5.9 Re-export Workflow

This guide documents the manual Blender round-trip used when the direct REE/Assimp FBX imports and plays correctly in Blender, but behaves incorrectly in Unreal.

## Current known-good source behavior

When the non-normalized exporter FBX is imported into Blender 4.5.9:

- The character stands upright.
- The attack animations play correctly in Blender.
- The armature/object transform shown by Blender is expected to be:
  - X Rotation: `90°`
  - Y Rotation: `0°`
  - Z Rotation: `0°`
  - Uniform Scale: `0.010`

Do not treat the `90°` X rotation or `0.010` scale as an error at the Blender import stage. They are Blender's interpretation of the source FBX. For Unreal export, compensate through the FBX export settings below rather than by using Unreal's import-scale override.

## 1. Export the source FBX from REE-Content-Exporter

Use the non-normalized export path. Do **not** use the removed/experimental root-rotation normalization flag.

Example command:

```powershell
$exporter = "C:\Users\hojin\Downloads\PRAG_PROJ\REE-Content-Exporter\bin\Release\net10.0\REE-Content-Exporter.exe"
$root = "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000"
$out = "C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter\ch0100_attack_all_animations_source.fbx"

& $exporter `
  --mesh "$root\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828" `
  --streaming "$root\streaming\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828" `
  --additional-mesh "$root\character\ch\ch01\ch0100\10\ch0100_10.mesh.251121828" `
  --additional-mesh "$root\character\ch\ch01\ch0100\20\ch0100_20.mesh.251121828" `
  --additional-mesh "$root\character\ch\ch01\ch0100\40\ch0100_40_neo.mesh.251121828" `
  --motlist "$root\character\animation\ch\ch01\ch0100\motlist\ch0100_attack.motlist.1057" `
  --no-placeholder-animation-bones `
  --texture-format png `
  --fbx-scale 100 `
  --output $out
```

Notes:

- The exporter now tries both layouts:
  - Old STM layout: `...\natives\STM\...`
  - Flat extracted layout: `...\re_chunk_000\...`
- If a mesh requires streaming data, the exporter first tries the old `natives\STM\streaming` path, then the flat `re_chunk_000\streaming` path.
- Texture lookup also tries both old and flat layouts.

## 2. Import into Blender 4.5.9

In Blender 4.5.9:

1. Start with a clean scene.
2. Go to **File > Import > FBX (.fbx)**.
3. Select the source FBX exported above.
4. Import with the default FBX import settings unless you have a specific reason to change them.
5. After import, verify:
   - One armature exists.
   - The expected attack actions are present.
   - The model is upright.
   - Animation playback is correct.
   - Armature/object transform is approximately X rotation `90°`, uniform scale `0.010`.

If the animations are correct in Blender, continue. If they are already wrong in Blender, the issue is upstream of Unreal and the FBX should not be exported onward yet.

## 3. Export from Blender 4.5.9 for Unreal

Go to **File > Export > FBX (.fbx)**.

Recommended Blender 4.5.9 export options:

### Include

- **Selected Objects**: Off, unless you intentionally selected only the character mesh and armature.
- **Object Types**: enable only:
  - Armature
  - Mesh

### Transform

- **Scale**: `100.00`
- **Apply Scalings**: `FBX All`
- **Forward**: `-Y Forward`
- **Up**: `Z Up`
- **Apply Unit**: On
- **Use Space Transform**: On
- **Apply Transform**: Off

Important: do **not** use `-Z Forward / Y Up` for Unreal here. That writes a Maya-style/Y-up FBX and produced a character laid on its back in Unreal during testing. `-Y Forward / Z Up` keeps the exported FBX in Unreal's expected Z-up orientation.

### Geometry

- **Apply Modifiers**: On
- Smoothing/tangent options can be left at Blender defaults unless Unreal reports tangent/normal issues.

### Armature

- **Primary Bone Axis**: `Y Axis`
- **Secondary Bone Axis**: `X Axis`
- **Add Leaf Bones**: Off
- **Only Deform Bones**: Off for maximum compatibility with the imported skeleton hierarchy. If Unreal imports too many helper bones, retry with this On.

### Bake Animation

- **Bake Animation**: On
- **Key All Bones**: On
- **NLA Strips**: On, if each action has been pushed/stashed as its own NLA strip
- **All Actions**: Off when using the NLA-strip workflow
- **Force Start/End Keying**: On
- **Sampling Rate / Step**: `1.0`
- **Simplify**: `0.0`

For a small manual test with only one active action, `All Actions` can work. For many actions, the safer workflow is to create one NLA strip per imported action, then export with `NLA Strips` enabled. This avoids relying on Blender's action-compatibility scan and produced explicit animation stacks in the generated test FBX.

Then export the FBX.

## 4. Unreal import notes

In Unreal:

1. Import the Blender-exported FBX.
2. Import mesh and skeleton on the first import.
3. Make sure animations are imported from all FBX takes/stacks.
4. If testing several generated FBX variants, import into a clean test folder to avoid Unreal reusing stale skeleton or animation assets.
5. If an animation still wobbles, test one animation at a time so it is clear whether the issue is:
   - the skeleton asset,
   - the individual animation sequence,
   - root motion/import settings,
   - or the FBX data itself.

## 5. Headless Blender re-export command

The same workflow can be automated with Blender's Python API. This is the command shape used for the retry export:

```powershell
& "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" --background --python blender_reexport_ch0100_unreal.py
```

The script should:

1. Delete the default scene.
2. Import the source FBX with animation enabled.
3. Verify that actions were imported.
4. Export FBX with:
   - `object_types={'ARMATURE', 'MESH'}`
   - `add_leaf_bones=False`
   - `primary_bone_axis='Y'`
   - `secondary_bone_axis='X'`
   - `bake_anim=True`
   - `bake_anim_use_all_bones=True`
   - Create one NLA strip per imported action before export
   - `bake_anim_use_nla_strips=True`
   - `bake_anim_use_all_actions=False`
   - `bake_anim_simplify_factor=0.0`
   - `axis_forward='-Y'`
   - `axis_up='Z'`
   - `global_scale=100.0`
   - `apply_scale_options='FBX_SCALE_ALL'`
   - `bake_space_transform=False`

## 6. Retry artifact from 2026-06-06

A Blender 4.5.9 headless re-export was generated here with the corrected Unreal settings:

```text
C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter\ch0100_00__20260606_021807__c9b97b\ch0100_attack_all_animations_blender45_unreal_zup_scale100_nla.fbx
```

Verification from the generated FBX:

- FBX version: `7400` as written by Blender.
- Animation stack count: `43`.
- First stack: `ch0100_Attack_0100_Hacking_Start`.
- Source import into Blender reported one armature, 46 meshes, and 43 actions.
- Export axis: `-Y Forward / Z Up`.
- Export scale: `100.0`.
- Export animation mode: one NLA strip per imported action.

Small single-animation test sample:

```text
C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter\ch0100_00__20260606_024544__7b83da\ch0100_attack_0110_hacking_loop_blender45_unreal_zup_scale100.fbx
```

Verification from the sample FBX:

- FBX version: `7400`.
- Animation stack count: `1`.
- Stack: `ch0100_Attack_0110_Hacking_Loop`.
- Export axis: `-Y Forward / Z Up`.
- Export scale: `100.0`.
