# Latest REE Dependency Quaternion Continuity Regression

## Summary

After updating the local REE dependencies to current upstream commits, ch0100 FBX animation exports started twitching again. This was the same documented sparse quaternion and FBX interpolation problem that had already been fixed before, but the latest REE dependency update restored a raw rotation-key write path in the patched `AddMotToScene` bridge code.

The fix does not change standalone `.mot.*` loading. It preserves the standalone MOT compatibility correction where `MotFile.Read()` remains the only normal standalone MOT load step. The corrected behavior is entirely in export-time rotation key writing after MOT or MOTLIST data has already been loaded into `MotFile` objects.

## What Happened

The current exporter depends on a sibling `REE-Content-Editor` checkout and the nested `RE-Engine-Lib` checkout. Updating those dependencies is useful, but it can overwrite or drift from exporter-specific bridge behavior in:

```text
C:\Users\user\Downloads\PRAGMATA\Tools\REE-Content-Editor\ContentEditor.App\CustomizedFileLoaders\MeshConversion\AssimpMeshExport.cs
```

The problematic code path was the direct write of sparse source quaternions into Assimp rotation keys:

```csharp
channel.RotationKeys.Add(new QuaternionKey(time, clip.Rotation.rotations[i]));
```

That is risky for FBX because REE-Content-Editor playback evaluates sparse quaternion tracks with shortest-path interpolation, while downstream FBX importers may reconstruct sparse frames differently. The source animation can play correctly in REE-Content-Editor and still twitch in Unreal, Blender re-export, or another importer if the exported FBX leaves sparse quaternion gaps or sign-discontinuous equivalent quaternions.

This is an export representation regression, not proof that the source MOTLIST data is corrupt.

## What Did Not Change

Standalone `.mot.*` compatibility remains intact:

- `Program.cs` still loads raw MOT inputs with `MotFile.Read()`.
- The removed extra `mot.ReadBones(null)` call was not reintroduced.
- MOTLIST loading still uses REE-Lib's embedded/header MOT behavior.
- The fix does not mutate source `MotFile` tracks, MOTLIST entries, or standalone MOT arrays.

This matters because the earlier standalone MOT bug and this twitch regression can look similar after import, but they live in different layers:

| Issue | Layer | Correct fix |
| --- | --- | --- |
| Standalone MOT duplicate bone/rest-pose data | Raw `.mot.*` load step | Use only `MotFile.Read()` for normal standalone MOT paths |
| Twitching sparse rotation FBX export | `AddMotToScene` export key writing | Normalize, hemisphere-correct, and FBX-bake rotation keys |

## Fix

The bridge patch was restored in `AddMotToScene`:

- Raw `clip.Rotation.rotations[i]` writes were replaced with helper-based export.
- Every exported quaternion is normalized.
- Zero, NaN, infinite, or otherwise invalid quaternions fall back to the previous valid rotation for that bone channel, or identity for the first key.
- If `Quaternion.Dot(previous, current) < 0`, the current quaternion is negated before export so adjacent keys stay on a continuous hemisphere.
- FBX output samples sparse source rotation tracks at every integer source frame from frame `0` through `mot.Header.endFrame` inclusive.
- FBX sampling uses `Animator.FindFrames`-equivalent frame lookup plus `Quaternion.Lerp`, matching REE-Content-Editor playback semantics.
- Non-FBX formats keep sparse rotation tracks, but still receive normalized and hemisphere-continuous keys.

The final local bridge patch hash after this fix is:

```text
ae6f7b1bc253838273e579655acd4681ae75784969c90a5d01b93e40a75b967d
```

## Dependency Snapshot

The final local verification build reports:

```text
Recorded UTC: 2026-06-22T06:34:14Z
Exporter commit: f0e65cf23598b3e3eaebda3a7d6870d6ccae54e6
REE-Content-Editor commit: 711c509affb0362c7af1e5343e8c57d32a1ad27d
REE-Content-Editor status: origin/master plus exporter bridge patch
REE bridge patch SHA-256: ae6f7b1bc253838273e579655acd4681ae75784969c90a5d01b93e40a75b967d
RE-Engine-Lib commit: 867daf1b0361a67e24bc82ef1391e01cc33d524a
RE-Engine-Lib status: origin/master clean
```

The usable local build folder is:

```text
C:\Users\user\Downloads\PRAGMATA\Tools\REE-Content-Exporter-fresh-quatfix2-ree-711c509-rel-867daf1-20260622_1537
```

The published folder contains the expected runtime files:

```text
REE-Content-Exporter-GUI.exe
REE-Content-Exporter-CLI.exe
texconv.exe
DirectXTex.dll
libGDeflate.dll
assimp.dll
```

## Build Verification

Commands:

```powershell
dotnet build -c Release
dotnet publish -c Release -p:PublishProfile=win-x64-singlefile
```

Result:

```text
Build succeeded.
0 warnings
0 errors

Publish succeeded.
```

Dependency version smoke test:

```powershell
& "C:\Users\user\Downloads\PRAGMATA\Tools\REE-Content-Exporter-fresh-quatfix2-ree-711c509-rel-867daf1-20260622_1537\REE-Content-Exporter-CLI.exe" --dependency-versions
```

The command printed the dependency snapshot listed above.

## Focused Regression Export

The known focused MOTLIST case was regenerated with the final build:

```text
MOTLIST: C:\Users\user\Downloads\PRAGMATA\Tools\REtool\re_chunk_000\character\animation\ch\ch01\ch0100\motlist\ch0100_other.motlist.1057
Filter: 5215
Selected animation: ch0100_Other_5215_Turn_Walk_L180
Textures: disabled for animation-only verification
Missing bone policy: --no-placeholder-animation-bones
```

Generated source FBX:

```text
C:\Users\user\Downloads\PRAGMATA\Z_Extracted\ree-content-exporter\quatfix_verification_20260622_1537\ch0100_00__20260622_153714__39af0b\ch0100_other_5215_quatfix2_source.fbx
```

Result:

```text
Exported bytes=99216720
```

The expected missing helper/runtime bone hashes were still reported and skipped per channel:

```text
2184993242
3724475523
573179932
386343545
```

Those skipped channels are separate from the quaternion continuity issue.

## Focused Blender Audit

Blender version:

```text
Blender 4.5.9 LTS
```

Audit result:

```text
AUDIT_IMPORT armatures=1 meshes=46 actions=1
AUDIT_ACTION name='Armature|ch0100_Other_5215_Turn_Walk_L180|' frame_range=1-149 fcurves=770 rotation_curves=308
AUDIT_ROTATION_KEY_HIST 1:84, 149:224
AUDIT_ROTATION_JUMPS_GT_PI=0
AUDIT_MAX_ADJACENT_ROTATION_ANGLE=0.269601
```

Interpretation:

- 224 imported rotation curves are animated and dense across the full 149-frame action.
- 84 imported rotation curves are static one-key curves.
- No evaluated adjacent integer-frame rotation jump exceeded pi radians.

This is the crisp regression check: the previous raw sparse export path could leave importer-sensitive gaps, while the restored bridge patch produces dense animated FBX rotation samples for this MOTLIST action.

## Minidemo MOTLIST Regression Export

The current `gamedemo\minidemo\md*` MOTLIST set was exported with animation names filtered to `ch0100`.

Inputs:

```text
MOTLIST files scanned: 57
Non-empty filtered MOTLIST exports: 22
Textures: disabled for animation-only verification
Missing bone policy: --no-placeholder-animation-bones
Split MOTLIST output: enabled
```

Output root:

```text
C:\Users\user\Downloads\PRAGMATA\Z_Extracted\ree-content-exporter\ch0100_minidemo_ch0100_anims_quatfix2_20260622_1537\ch0100_00__20260622_153804__1797b5
```

Result:

```text
Generated FBX count: 22
Zero-byte FBX count: 0
```

Generated FBX files:

```text
0000_md08001_all_animations.fbx
0001_md09000_all_animations.fbx
0002_md10006_all_animations.fbx
0003_md10012_all_animations.fbx
0004_md10014_all_animations.fbx
0005_md10015_all_animations.fbx
0006_md10016_all_animations.fbx
0007_md10017_all_animations.fbx
0008_md10024_all_animations.fbx
0009_md10029_all_animations.fbx
0010_md10030_all_animations.fbx
0011_md11005_all_animations.fbx
0012_md13000_all_animations.fbx
0013_md14000_all_animations.fbx
0014_md15001_all_animations.fbx
0015_md16001_all_animations.fbx
0016_md16003_all_animations.fbx
0017_md16004_all_animations.fbx
0018_md16005_all_animations.fbx
0019_md41050_all_animations.fbx
0020_md41051_all_animations.fbx
0021_md41052_all_animations.fbx
```

## Representative Minidemo Blender Audit

Representative file:

```text
C:\Users\user\Downloads\PRAGMATA\Z_Extracted\ree-content-exporter\ch0100_minidemo_ch0100_anims_quatfix2_20260622_1537\ch0100_00__20260622_153804__1797b5\0005_md10015_all_animations.fbx
```

Import result:

```text
AUDIT_IMPORT armatures=1 meshes=46 actions=14
```

Every imported action had zero adjacent integer-frame rotation jumps greater than pi:

| Action | Frame range | Jumps > pi | Max adjacent angle |
| --- | ---: | ---: | ---: |
| md10015_ch0100_Reinforce_FocusDana_Hack_VerA | 1-281 | 0 | 0.285095 |
| md10015_ch0100_Reinforce_FocusDana_Hack_VerB | 1-276 | 0 | 0.390425 |
| md10015_ch0100_Reinforce_FocusDana_Loop | 1-1048 | 0 | 0.090778 |
| md10015_ch0100_Reinforce_FocusDana_Start_FocusGun | 1-81 | 0 | 0.140603 |
| md10015_ch0100_Reinforce_FocusDana_Start | 1-81 | 0 | 0.205559 |
| md10015_ch0100_Reinforce_FocusGun_Loop | 1-528 | 0 | 0.033447 |
| md10015_ch0100_Reinforce_FocusGun_Start_FocusDana | 1-81 | 0 | 0.191740 |
| md10015_ch0100_Reinforce_FocusGun_Start_FocusPL | 1-71 | 0 | 0.146343 |
| md10015_ch0100_Reinforce_FocusPL_Gun_VerA | 1-281 | 0 | 0.299769 |
| md10015_ch0100_Reinforce_FocusPL_Loop | 1-893 | 0 | 0.035136 |
| md10015_ch0100_Reinforce_FocusPL_Start_FocusGun | 1-86 | 0 | 0.075000 |
| md10015_ch0100_Reinforce_FocusPL_Start | 1-81 | 0 | 0.097898 |
| md10015_ch0100_Reinforce_FocusPL_Suit_VerA | 1-251 | 0 | 0.302774 |
| md10015_ch0100_Reinforce_Start | 1-131 | 0 | 0.231016 |

Blender compacted a few constant or near-constant imported component curves, so the imported f-curve histogram is not exclusively one-key-or-full-length for every component in this multi-action file. The continuity audit evaluates the reconstructed quaternion at every integer frame, and all 14 actions stayed below the pi-radian jump threshold.

## Future Regression Checks

After any REE-Content-Editor or RE-Engine-Lib update:

1. Confirm standalone MOT loading still uses `MotFile.Read()` only for normal raw `.mot.*` inputs.
2. Inspect `AddMotToScene` and confirm it does not directly write `clip.Rotation.rotations[i]` to `QuaternionKey`.
3. Recompute the bridge patch SHA-256 and update `DependencyVersions.cs`.
4. Run `dotnet build -c Release`.
5. Publish a fresh local build and verify `REE-Content-Exporter-CLI.exe --dependency-versions`.
6. Export `ch0100_other.motlist.1057` with `--animation-name 5215`.
7. Audit the generated source FBX in Blender for:
   - expected action name;
   - frame range `1-149`;
   - dense animated rotation curves;
   - `AUDIT_ROTATION_JUMPS_GT_PI=0`.
8. Export the `gamedemo\minidemo\md*` MOTLIST set filtered to `ch0100`.
9. Confirm all generated FBXs are nonzero and audit a representative multi-action file such as `md10015`.

## Conclusion

The twitching regression was caused by the export-time FBX rotation representation drifting back to raw sparse quaternion key writing after dependency updates. Restoring normalized, hemisphere-continuous, integer-frame-baked FBX rotation export fixes the documented regression without voiding the standalone MOT compatibility correction.
