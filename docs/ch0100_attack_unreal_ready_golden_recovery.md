# ch0100 Attack Unreal-Ready Golden Path Recovery

## Summary

The 2026-06-07 file below is the golden reference for PRAGMATA `ch0100` Attack MOTLIST animation export:

```text
C:\Users\user\Downloads\PRAGMATA\Z_Extracted\ree-content-exporter\ch0100_00__20260607_083505__3bb902\ch0100_Attack_unreal.fbx
```

The later v0.6 GUI/direct export produced:

```text
C:\Users\user\Downloads\PRAGMATA\Z_Extracted\ree-content-exporter\ch0100_00__20260622_155010__cc4d4a\0000_ch0100_Attack_all_animations.fbx
```

That file was an intermediate/source FBX, not the final Unreal-ready FBX. Blender imported it with the armature object still carrying the source FBX 90-degree X rotation. The June 7 golden file had gone through the Blender finalization stage, which applies armature rotation/scale, creates explicit NLA strips, and bakes final Unreal-ready animation stacks.

The fix is surgical: keep current v0.6 functionality, keep standalone `.mot.*` compatibility, and make the current GUI/CLI route use the existing golden Blender finalization path for FBX animation exports.

## Cause

This was primarily a workflow regression, not a MOTLIST parser regression.

Current direct CLI source export had become easy to mistake for final output:

```text
*_all_animations.fbx
```

The golden path requires a second stage:

```text
source/intermediate FBX -> Blender 4.5.9 -> *_unreal.fbx
```

The current dense quaternion source-FBX fix remains useful for direct FBX diagnostics and standalone `.mot.*` compatibility, but it is not the same artifact class as the June 7 Unreal-ready FBX. For Unreal-ready exports, the source stage now preserves sparse FBX rotation tracks and lets Blender perform the final full transform/action bake, matching the June 7 workflow.

## Fix

New CLI flags:

```text
--unreal-ready-fbx
--blender <path-to-blender.exe>
--keep-source-fbx
```

Behavior:

- `--unreal-ready-fbx` runs Blender 4.5.9 after source FBX export and writes final `*_unreal.fbx` files.
- Source `*_all_animations.fbx` / `*_source.fbx` files are removed after successful Blender export unless `--keep-source-fbx` is used.
- GUI FBX animation exports automatically pass `--unreal-ready-fbx --blender <saved Blender path>` when Blender is configured.
- Direct FBX without `--unreal-ready-fbx` still exports source FBX and keeps the quaternion-continuity source-FBX safety path.
- Standalone `.mot.*` loading still uses `MotFile.Read()` only; the removed extra `ReadBones(null)` call was not reintroduced.

The REE bridge now has a scoped switch:

```text
ExportBakeFbxRotationTracks = true   -> direct source FBX safety path
ExportBakeFbxRotationTracks = false  -> Unreal-ready Blender staging path
```

This keeps direct-FBX quaternion continuity and the June 7 Blender-bake workflow from fighting each other.

## Verification

Build:

```powershell
dotnet build -c Release
```

Result:

```text
0 errors
0 warnings
```

Implemented-path export:

```powershell
REE-Content-Exporter.exe `
  --game pragmata `
  --mesh "<re_chunk_000>\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828" `
  --streaming "<re_chunk_000>\streaming\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828" `
  --additional-mesh "<re_chunk_000>\character\ch\ch01\ch0100\10\ch0100_10.mesh.251121828" `
  --additional-mesh "<re_chunk_000>\character\ch\ch01\ch0100\20\ch0100_20.mesh.251121828" `
  --additional-mesh "<re_chunk_000>\character\ch\ch01\ch0100\40\ch0100_40_neo.mesh.251121828" `
  --additional-streaming "<mesh40>=<stream40>" `
  --motlist "<re_chunk_000>\character\animation\ch\ch01\ch0100\motlist\ch0100_Attack.motlist.1057" `
  --fbx-scale 100 `
  --no-placeholder-animation-bones `
  --no-textures `
  --unreal-ready-fbx `
  --blender "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" `
  --output "<export-root>\ch0100_Attack.fbx"
```

Final output:

```text
C:\Users\user\Downloads\PRAGMATA\Z_Extracted\ree-content-exporter\golden_path_final_20260622_1648\ch0100_00__20260622_165251__2225fc\ch0100_Attack_unreal.fbx
```

The intermediate source FBX was removed:

```text
SOURCE_FBX_REMOVED=...\ch0100_Attack.fbx
```

Blender audit against the June 7 golden reference:

```text
Golden actions: 43
New actions: 43
Target action: Armature|Armature|ch0100_Attack_628_Clean_Up_Release_Lv1|
Frame range: 1-106
F-curves: 5439
Rotation curves: 2175
Rotation key histogram: 106:2175
Adjacent rotation jumps greater than pi: 0
Armature object rotation: approximately 0,0,0
```

The audit confirms the current implemented path produces the same final artifact class as the 2026-06-07 golden file: a Blender-baked Unreal-ready FBX, not a source/intermediate FBX.

## Notes

The Blender audit also found that exact sampled pose values are not byte-for-byte identical to the June 7 file. That is expected to remain a separate investigation if visual Unreal playback still differs, because the current fix only restores the golden export path and output class without rolling back later parser, dependency, GUI, texture, logging, or raw MOT changes.

The practical acceptance criteria for this recovery were:

- final file is `*_unreal.fbx`;
- source file is not left as the apparent final output;
- target action imports exactly once;
- target frame range and dense keying match the golden;
- armature object rotation is normalized by Blender finalization;
- no adjacent quaternion jump spikes are present.
