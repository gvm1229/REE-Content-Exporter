# Dependency setup

`REE-Content-Exporter` references `REE-Content-Editor` as a sibling source dependency:

```xml
<ProjectReference Include="..\REE-Content-Editor\ContentEditor.App\ContentEditor.App.csproj" />
```

The exporter requires a small custom patch on top of upstream `REE-Content-Editor`. Do not rely on an untracked hand-edited sibling checkout.

To recreate the dependency from scratch:

```powershell
.\scripts\setup-content-editor-dependency.ps1 -Force
```

The script:

1. Clones `https://github.com/kagenocookie/REE-Content-Editor.git` if `..\REE-Content-Editor` is missing.
2. Checks out pinned upstream commit `7db72c1`.
3. Initializes submodules.
4. Applies `patches\ree-content-editor-commonmeshresource-material-textures.patch`.

The resulting `..\REE-Content-Editor` checkout will intentionally show local modifications. Those modifications are reproducible from the patch stored in this exporter repository.

Current patch scope:

- Material texture export support.
- Multiple imported material groups for additional meshes.
- Export progress hooks.
- Root node/mesh name controls used by the wrapper.
- Missing animation bone skip/no-placeholder behavior.
- Single-file publish-safe version lookup for `ContentEditor.AppConfig`.

The setup script also applies:

```text
patches\ree-lib-single-file-version.patch
```

That submodule patch makes REE-Lib's remote resource user-agent version lookup safe when assemblies are bundled into a single-file executable.

The removed experimental FBX root-rotation normalization is **not** part of this patch.
