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

Do not treat the `90°` X rotation or `0.010` scale as an error for this workflow. Leave them as Blender imported them unless you intentionally want to author a different transform.

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

- **Scale**: `1.00`
- **Apply Scalings**: `FBX All`
- **Forward**: `-Z Forward`
- **Up**: `Y Up`
- **Apply Unit**: On
- **Use Space Transform**: On
- **Apply Transform**: Off

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
- **NLA Strips**: Off for this imported multi-action workflow
- **All Actions**: On
- **Force Start/End Keying**: On
- **Sampling Rate / Step**: `1.0`
- **Simplify**: `0.0`

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
   - `bake_anim_use_nla_strips=False`
   - `bake_anim_use_all_actions=True`
   - `bake_anim_simplify_factor=0.0`
   - `axis_forward='-Z'`
   - `axis_up='Y'`
   - `apply_scale_options='FBX_SCALE_ALL'`
   - `bake_space_transform=False`

## 6. Retry artifact from 2026-06-06

A Blender 4.5.9 headless re-export was generated here:

```text
C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter\ch0100_00__20260606_021807__c9b97b\ch0100_attack_all_animations_blender45_unreal_retry.fbx
```

Verification from the generated FBX:

- FBX version: `7400` as written by Blender.
- Animation stack count: `43`.
- First stack: `ch0100_Attack_0100_Hacking_Start`.
- Source import into Blender reported one armature, 46 meshes, and 43 actions.
