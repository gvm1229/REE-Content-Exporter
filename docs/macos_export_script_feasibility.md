# macOS Export Script Port Feasibility Notes

This document records the current position on a possible macOS variant of the export execution scripts. It is intentionally only a feasibility note; no macOS execution script is currently maintained.

## Summary

A macOS port is possible in principle, but it is not a simple path rewrite. The current validated workflow is Windows-first and depends on several Windows-shaped assumptions:

- `REE-Content-Exporter.exe` from the .NET build output.
- A sibling patched `REE-Content-Editor` dependency that follows the current Windows development layout.
- Windows extract paths such as `D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000`.
- Blender 4.5.9 LTS at `C:\Program Files\Blender Foundation\Blender 4.5\blender.exe`.
- PNG texture conversion through `texconv.exe` / DirectXTex.

Because the original REE-Content-Editor workflow is also executable-oriented, a macOS script should not be added until the exporter, dependency, and texture conversion path are verified on macOS.

## Likely script shape

Two script styles are possible:

### PowerShell Core

```text
export-scripts/export_ch0100_all_motlists_unreal_fbx.macos.ps1
```

Pros:

- Similar structure to the Windows `.ps1` scripts.
- Easier to share the Blender Python generation logic.
- Good path and process handling.

Cons:

- Requires installing PowerShell Core on macOS.
- Still needs macOS-specific path, executable, and texture conversion handling.

### Bash or zsh

```text
export-scripts/export_ch0100_all_motlists_unreal_fbx.sh
```

Pros:

- Native to macOS.
- Easier for users who do not want PowerShell Core.

Cons:

- More duplication from the Windows PowerShell scripts.
- More fragile multiline Python generation unless carefully implemented.

## Main blockers to resolve

### 1. Blender executable path

Windows:

```text
C:\Program Files\Blender Foundation\Blender 4.5\blender.exe
```

Likely macOS path:

```text
/Applications/Blender.app/Contents/MacOS/Blender
```

The script must still enforce Blender `4.5.9 LTS`, because the workflow is version-sensitive.

### 2. RE Engine extract root path

Windows default:

```text
D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000
```

A macOS variant would need a POSIX path, for example:

```text
/Volumes/RE_EXTRACT/PRAG_EXTRACT/re_chunk_000
```

The exporter logic supports old STM and flat `re_chunk_000` layouts, but the execution script defaults still need platform-specific paths.

### 3. Exporter launch method

Windows scripts call:

```text
bin\Release\net10.0\REE-Content-Exporter.exe
```

On macOS, unless a self-contained macOS build is published, the likely command is:

```bash
dotnet bin/Release/net10.0/REE-Content-Exporter.dll
```

This must be verified because the project references the patched REE-Content-Editor dependency, and that dependency may carry Windows-specific assumptions.

### 4. Texture conversion

The current PNG texture workflow expects `texconv.exe` / DirectXTex. This is a major portability concern.

Possible macOS approaches:

- Keep texture output as DDS if the exporter can produce usable DDS without `texconv.exe`.
- Add a separate macOS-compatible converter path.
- Make PNG texture export optional for macOS until conversion is solved.

This should be tested before claiming macOS support.

### 5. Native dependencies and Assimp behavior

Even if .NET runs, the exporter and REE-Content-Editor stack may depend on native libraries or Assimp behavior that differs between platforms. The first macOS validation should be small and should avoid the Blender stage until the basic exporter works.

## Recommended validation order

1. Build the exporter on macOS.
2. Run a minimal mesh-only or single-animation export without PNG textures.
3. Confirm the source FBX is generated.
4. Add texture export and resolve converter issues.
5. Add Blender 4.5.9 headless re-export.
6. Verify the final FBX in Unreal, not only in Blender.

## Current decision

Do not add macOS execution scripts yet. Keep the possibility documented only until the exporter, patched dependency, texture conversion, Blender 4.5.9, and Unreal result are all verified on macOS.
