# Animation Quaternion Continuity

## Problem

MOT rotation tracks store bone rotations as quaternions. A quaternion and its negated value represent the same orientation:

```text
q == -q
```

That equivalence is harmless when the animation player interpolates with shortest-path quaternion logic, as REE-Content-Editor does during viewport playback. It can become harmful when an export pipeline hands sparse quaternion keys to a format writer or importer that converts them into FBX rotation curves without preserving shortest-path continuity or without sampling the same frames as the REE player.

The user-visible symptom is a one-frame or two-frame rotation fluke. In the `sm39_033_Close` test export, the reported problem frames were:

```text
Animation: Armature|sm39_033_Close|
Bone: _06_03
Frame: 22

Bone: _05_03
Frames: 25, 26
```

The source MOT plays correctly in REE-Content-Editor, so the source animation data is not inherently broken. The problem is the exported rotation representation.

## Cause

Before the fix, `AddMotToScene` wrote MOT rotation keys directly into Assimp:

```csharp
channel.RotationKeys.Add(new QuaternionKey(time, clip.Rotation.rotations[i]));
```

That preserves each sparse source quaternion value literally, but it leaves two importer-sensitive gaps:

- It does not guarantee that adjacent keys are on the same quaternion hemisphere. If a later key has a negative dot product against the previous key, the two keys are equivalent to a short pose delta but can be represented as a long spherical interpolation path.
- It leaves missing integer frames between sparse source keys up to the FBX importer. In `sm39_033_Close`, `_05_03` has source rotation keys at frames 24 and 26, so frame 25 is importer-interpolated unless the exporter bakes it.

Some importers normalize or repair this automatically. Others preserve the discontinuity, resample differently, or convert it into Euler/FBX curves that visibly spin the bone the long way for a frame.

The `sm39_033_Close` MOT did not contain adjacent source quaternion sign flips in the reported bones. The practical fix still includes hemisphere normalization as a guard, but the actual confirmed failure class for this test was sparse-key FBX interpolation: the frame that looked wrong in Unreal was between source MOT keys, so the importer was being asked to reconstruct a quaternion interpolation that REE-Content-Editor had already handled correctly during playback.

## Fix

The exporter now normalizes each rotation key and makes each bone channel hemisphere-continuous before passing it to Assimp:

```csharp
rotation = Quaternion.Normalize(rotation);
if (previousRotation != null && Quaternion.Dot(previousRotation.Value, rotation) < 0) {
    rotation = new Quaternion(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);
}
```

This does not change the represented pose. It changes only which equivalent quaternion representation is written so adjacent keys interpolate safely.

Zero-length rotation keys are guarded defensively:

```csharp
if (rotation.LengthSquared() < 1e-12f) {
    return previousRotation ?? Quaternion.Identity;
}
```

That avoids exporting NaNs if a malformed or unsupported track produces an invalid quaternion.

For FBX output, sparse rotation tracks are also sampled at each integer source MOT frame using the same shortest-path `Quaternion.Lerp` behavior used by REE-Content-Editor's playback path. That means a sparse source segment such as frames 24 to 26 gets an explicit exported key at frame 25 instead of leaving that frame to downstream FBX interpolation.

## Scope

This is an export-time representation fix:

- It applies per bone rotation channel.
- It applies to direct standalone MOT exports and MOTLIST-driven exports because both flow through `AddMotToScene`.
- FBX output receives integer-frame baked rotation keys; non-FBX formats keep sparse rotation keys after normalization.
- It does not mutate the source `MotFile` data.
- It preserves the existing frame times, translations, scales, names, and missing-bone policies.

## Verification

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
& "bin\Release\net10.0\REE-Content-Exporter.exe" `
  --mesh "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\environment\sm\sm3x\sm39\sm39_033\sm39_033_00.mesh.251121828" `
  --mot "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\environment\sm\sm3x\sm39\sm39_033\sm39_033_close.mot.993" `
  --output "C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter\sm39_033_close_quat_baked_test.fbx" `
  --fbx-scale 100 `
  --no-placeholder-animation-bones `
  --no-textures
```

Result:

```text
Loaded mot sm39_033_Close
Exported ...\sm39_033_close_quat_baked_test.fbx bytes=12520176
DONE
```

The skipped animation bone report said:

```text
No animation bone channels were skipped.
```

Blender 4.5.9 LTS import smoke test:

```text
FBX_IMPORT_CHECK actions=1 armatures=1 meshes=24 frames=[('Armature|sm39_033_Close|', 1, 111)]
```

The reported bones `_06_03` and `_05_03` imported with 111 quaternion keys each. Pose sampling around frames 18-30 stayed smooth after import. Blender is not a substitute for Unreal verification, but it confirms the exported FBX remains structurally valid and retains the expected action, frame range, armature, mesh data, and dense rotation samples.
