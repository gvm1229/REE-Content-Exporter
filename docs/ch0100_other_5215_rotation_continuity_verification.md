# ch0100 Other 5215 MOTLIST Rotation Continuity Verification

## Summary

The `ch0100_other.motlist.1057` animation `5215` was verified after the MOT rotation-continuity export fix.

The confirmed issue class is the same as the `sm39_033_Close` rotation anomaly: FBX export representation and downstream sparse-key interpolation could create one-frame or two-frame rotation flukes even when REE-Content-Editor playback was smooth.

It was not the standalone `.mot.*` bone-table double-load bug. This test uses a `.motlist.*` file, and MOTLIST animations already used the correct inherited/header MOT bone loading path. The relevant shared path is `AddMotToScene`, where both standalone MOT and MOTLIST entries become exported animation channels.

## Tested Asset

Primary mesh:

```text
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828
```

Additional meshes:

```text
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\character\ch\ch01\ch0100\10\ch0100_10.mesh.251121828
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\character\ch\ch01\ch0100\20\ch0100_20.mesh.251121828
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\character\ch\ch01\ch0100\40\ch0100_40_neo.mesh.251121828
```

Verified streaming counterparts:

```text
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\streaming\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\streaming\character\ch\ch01\ch0100\40\ch0100_40_neo.mesh.251121828
```

No streaming counterpart was present for `ch0100_10` or `ch0100_20`, so none was passed for those additional meshes.

Animation source:

```text
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\character\animation\ch\ch01\ch0100\motlist\ch0100_other.motlist.1057
```

Selected animation:

```text
ch0100_Other_5215_Turn_Walk_L180
```

The exporter selected exactly one animation from the MOTLIST:

```text
Loaded motlist ch0100_Other: total=142 selected=1
```

## Cause

The original MOTLIST animation data was not confirmed to be corrupt. REE-Content-Editor playback was the source of truth and did not show the user-visible fluke.

The exporter failure mode was representation loss at FBX export time:

- MOT and MOTLIST rotation channels store quaternions.
- `q` and `-q` represent the same pose, but downstream FBX importers may not preserve REE-Content-Editor's shortest-path interpolation if sparse keys are handed off literally.
- Sparse MOT rotation tracks can leave intermediate integer frames for the FBX importer or Unreal importer to reconstruct.
- If the importer converts sparse quaternion keys into Euler/FBX curves differently, the visible result can be a one-frame long-way rotation even though the source animation plays correctly in REE-Content-Editor.

This is the same practical failure class as `sm39_033_Close`: the source animation was playable, but the exported FBX representation left too much interpolation choice to the next importer.

## Fix Being Verified

The fix is shared by standalone MOT and MOTLIST export because both flow through the same animation channel construction.

For every bone rotation channel:

1. Normalize each quaternion key.
2. Keep adjacent keys on a continuous quaternion hemisphere by negating the current key when its dot product with the previous exported key is negative.
3. Guard invalid zero-length quaternions by reusing the previous valid key, or identity for the first key.
4. For FBX output, sample sparse MOT rotation tracks at every integer source frame using the same shortest-path interpolation behavior used by REE-Content-Editor playback.

The fourth step is important for MOTLIST animations too. It prevents an animation with sparse source keys from asking Unreal or another importer to invent a frame that REE-Content-Editor already knows how to evaluate.

## Export Command

Build:

```powershell
dotnet build -c Release
```

Result:

```text
0 warnings
0 errors
```

Scoped export:

```powershell
& ".\bin\Release\net10.0-windows\REE-Content-Exporter.exe" `
  --game pragmata `
  --mesh "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828" `
  --streaming "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\streaming\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828" `
  --additional-mesh "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\character\ch\ch01\ch0100\10\ch0100_10.mesh.251121828" `
  --additional-mesh "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\character\ch\ch01\ch0100\20\ch0100_20.mesh.251121828" `
  --additional-mesh "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\character\ch\ch01\ch0100\40\ch0100_40_neo.mesh.251121828" `
  --additional-streaming "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\character\ch\ch01\ch0100\40\ch0100_40_neo.mesh.251121828=D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\streaming\character\ch\ch01\ch0100\40\ch0100_40_neo.mesh.251121828" `
  --motlist "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\character\animation\ch\ch01\ch0100\motlist\ch0100_other.motlist.1057" `
  --animation-name 5215 `
  --output "C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter\ch0100_other_5215_test.fbx" `
  --fbx-scale 100 `
  --no-placeholder-animation-bones `
  --no-textures
```

Output source FBX:

```text
C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter\ch0100_00__20260616_062909__561e37\ch0100_other_5215_test.fbx
```

Size:

```text
99,216,720 bytes
```

## Missing Bone Channels

The export used `--no-placeholder-animation-bones`, so missing animation channels were skipped instead of creating placeholder `hash...` bones.

Skipped hashes:

```text
2184993242
3724475523
573179932
386343545
```

Report:

```text
C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter\ch0100_00__20260616_062909__561e37\ch0100_other_5215_test.skipped-animation-bones.md
```

These skipped helper/runtime channels are separate from the quaternion continuity issue. The main animation action was kept and exported.

## Blender Source FBX Audit

Blender version:

```text
Blender 4.5.9 LTS
```

Import result:

```text
Armatures: 1
Meshes: 46
Actions: 1
Action: Armature|ch0100_Other_5215_Turn_Walk_L180|
Frame range: 1-149
F-curves: 770
Rotation curves: 308
Adjacent integer-frame rotation jumps greater than pi radians: 0
```

Rotation key density:

```text
Rotation curve key count histogram:
1 key: 84 curves
149 keys: 224 curves
```

The 149-key curves are animated rotation channels baked at every integer frame in the action. The remaining one-key curves are static bones such as helper, weapon, and toe bones.

This confirms the source FBX no longer leaves animated sparse rotation gaps for the next importer to solve.

## Blender Unreal-Style Re-export Audit

The source FBX was re-exported through Blender with the established Unreal-ready settings:

```text
axis_forward = -Z
axis_up = Y
bake_anim = true
bake_anim_step = 1.0
bake_anim_simplify_factor = 0.0
apply_unit_scale = true
apply_scale_options = FBX_SCALE_ALL
```

Output Unreal-style FBX:

```text
C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter\ch0100_00__20260616_062909__561e37\ch0100_other_5215_test_unreal.fbx
```

Size:

```text
46,247,868 bytes
```

Re-import audit:

```text
Armatures: 1
Meshes: 46
Actions: 1
Action: Armature|Armature|ch0100_Other_5215_Turn_Walk_L180|
Frame range: 1-149
F-curves: 5439
Rotation curves: 2175
Rotation curve key count histogram: 149 keys on all 2175 rotation curves
Adjacent integer-frame rotation jumps greater than pi radians: 0
```

This final file is structurally suitable for the same Unreal import path used by the current Blender re-export workflow.

## Conclusion

The successful user-side inspection confirms that `ch0100_Other_5215_Turn_Walk_L180` is fixed by the same quaternion-continuity and integer-frame FBX baking strategy used for the earlier standalone MOT anomaly.

The distinction is:

- `sm39_033_close.mot.993` had two relevant fixes in the investigation history: standalone MOT bone loading had to stop double-reading bones, and FBX rotation export needed continuity/baking.
- `ch0100_other.motlist.1057` animation `5215` did not depend on the standalone MOT bone-loading correction. It verifies that the shared rotation export fix also protects MOTLIST entries.

For future suspected MOTLIST flukes, run a similarly scoped one-animation export, then audit both the source FBX and Blender re-exported FBX for:

- exactly the expected selected action;
- expected frame range;
- dense integer-frame rotation keys on animated channels;
- zero adjacent rotation jumps greater than pi radians after import.
