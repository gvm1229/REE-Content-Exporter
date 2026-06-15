# Standalone MOT Bone Loading Fix

## Summary

Standalone `.mot.*` exports used a wrapper-only bone-loading step that did not match REE-Content-Editor's direct MOT loader. The exporter constructed a `MotFile`, called `MotFile.Read()`, and then called `ReadBones(null)` again for every `--mot` input.

That second call was wrong for normal standalone MOT files. `MotFile.Read()` already loads the standalone MOT bone table when the file path is a `.mot.*` path. Calling `ReadBones(null)` a second time can append the same MOT bone records again and duplicate rest-pose/bone-name data before the FBX/GLB animation export path evaluates channels.

## User-visible symptom

The issue was found with:

```text
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\environment\sm\sm3x\sm39\sm39_033\sm39_033_00.mesh.251121828
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\environment\sm\sm3x\sm39\sm39_033\sm39_033_close.mot.993
```

The `.mot` file itself could be read and exported, and Unreal could import the resulting FBX, so the failure did not look like a parse error. The imported animation was broken even though REE-Content-Editor could open the same `.mot` file and play it correctly.

That difference pointed at wrapper logic around MOT loading rather than the underlying REE-Lib parser.

## Root cause

REE-Lib separates standalone MOT files from embedded MOT entries inside MOTLIST/MOTPACK containers.

For a normal standalone `.mot.*` file:

```csharp
var mot = new MotFile(new FileHandler(motPath));
mot.Read();
```

`MotFile.Read()` detects that the source path is not a MOTLIST/MOTPACK path and calls `ReadBones(null)` internally. This is the same effective behavior used by REE-Content-Editor's default MOT file loader.

For embedded MOT entries inside a MOTLIST/MOTPACK, the reader may need a header/source MOT:

```csharp
motFile.Read();
motFile.ReadBones(headerMot);
```

That embedded path is different because later MOT entries may inherit or share the bone table from the first/header MOT in the list. `ReadBones(headerMot)` is appropriate there.

The exporter mixed these two cases. It loaded standalone `--mot` files with `MotFile.Read()` and then manually called:

```csharp
mot.ReadBones(null);
```

Because the standalone read had already populated `mot.Bones` and `mot.RootBones`, the second call could duplicate the same bone records and make downstream rest-pose and channel-name lookup diverge from REE-Content-Editor.

## Fix

The standalone `--mot` path now only does this:

```csharp
using var motHandler = new FileHandler(motPath);
var mot = new MotFile(motHandler);
if (!mot.Read()) throw new Exception("REE-Lib failed to read mot");
```

The exporter no longer calls `ReadBones(null)` after `MotFile.Read()`.

MOTLIST behavior was intentionally left unchanged. MOTLIST reading still uses REE-Lib's existing embedded-MOT logic, including `ReadBones(headerMot)` where the MOTLIST reader requires it.

## Verification

Build verification:

```powershell
dotnet build -c Release
```

Result:

```text
0 warnings
0 errors
```

Scoped export verification:

```powershell
& "bin\Release\net10.0\REE-Content-Exporter.exe" `
  --mesh "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\environment\sm\sm3x\sm39\sm39_033\sm39_033_00.mesh.251121828" `
  --mot "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\environment\sm\sm3x\sm39\sm39_033\sm39_033_close.mot.993" `
  --output "C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter\sm39_033_close_mot_fix_test.fbx" `
  --fbx-scale 100 `
  --no-placeholder-animation-bones `
  --no-textures
```

Result:

```text
Loaded mot sm39_033_Close
Exported ...\sm39_033_close_mot_fix_test.fbx bytes=12478880
DONE
```

The generated skipped-animation-bones report said:

```text
No animation bone channels were skipped.
```

Blender import smoke test:

```powershell
& "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" `
  --background `
  --factory-startup `
  --python-expr "<import the generated FBX and count actions>"
```

Result:

```text
FBX_IMPORT_CHECK actions=1 armatures=1 meshes=24 frames=[('Armature|sm39_033_Close|', 1, 111)]
```

## Future rule

Do not call `ReadBones(null)` manually after `MotFile.Read()` for normal standalone `.mot.*` files. Only use explicit `ReadBones(headerMot)` in embedded MOTLIST/MOTPACK contexts where REE-Lib requires a source/header MOT.
