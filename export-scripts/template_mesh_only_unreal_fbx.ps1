param(
    [Parameter(Mandatory = $true)]
    [string]$Mesh,
    [string[]]$AdditionalMesh = @(),
    [string]$Streaming = "",
    [string]$OutputName = "mesh_unreal.fbx",
    [string]$ExportRoot = "C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter",
    [string]$Blender = "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe",
    [switch]$KeepSourceFbx
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
$Exporter = Join-Path $RepoRoot "bin\Release\net10.0-windows\REE-Content-Exporter.exe"

if (!(Test-Path $Exporter)) { throw "Missing exporter: $Exporter. Build with dotnet build -c Release." }
if (!(Test-Path $Blender)) { throw "Missing Blender 4.5.9 executable: $Blender" }
if (!(Test-Path $ExportRoot)) { New-Item -ItemType Directory -Force -Path $ExportRoot | Out-Null }
if ([System.IO.Path]::GetExtension($OutputName) -ne ".fbx") { throw "OutputName must end in .fbx" }

$BlenderVersionLine = (& $Blender --version 2>&1 | Select-Object -First 1)
if ($LASTEXITCODE -ne 0) { throw "Could not query Blender version from: $Blender" }
if ($BlenderVersionLine -notmatch 'Blender\s+4\.5\.9') {
    throw "Expected Blender 4.5.9 LTS, but found: $BlenderVersionLine"
}

$sourceName = [System.IO.Path]::GetFileNameWithoutExtension($OutputName) + "_source.fbx"
$OutputRequest = Join-Path $ExportRoot $sourceName
$Start = Get-Date

$argsList = @(
    "--mesh", $Mesh,
    "--no-animations",
    "--texture-format", "png",
    "--fbx-scale", "100",
    "--output", $OutputRequest
)
if (![string]::IsNullOrWhiteSpace($Streaming)) {
    $argsList = @("--streaming", $Streaming) + $argsList
}
foreach ($path in $AdditionalMesh) {
    $argsList += @("--additional-mesh", $path)
}

& $Exporter @argsList
if ($LASTEXITCODE -ne 0) { throw "Exporter failed with exit code $LASTEXITCODE" }

$Source = Get-ChildItem $ExportRoot -Recurse -File -Filter $sourceName |
  Where-Object { $_.LastWriteTime -ge $Start.AddMinutes(-2) } |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1
if (!$Source) { throw "Could not find generated source FBX under $ExportRoot" }

$OutDir = Split-Path $Source.FullName -Parent
$TextureDir = Join-Path $OutDir "textures"
if (!(Test-Path $TextureDir)) { throw "Texture folder missing after export: $TextureDir" }
$TextureCount = (Get-ChildItem $TextureDir -File -ErrorAction Stop | Measure-Object).Count
if ($TextureCount -le 0) { throw "Texture folder exists but is empty: $TextureDir" }

$BlenderOut = Join-Path $OutDir $OutputName
$Py = Join-Path $env:TEMP "blender_mesh_only_unreal_cm_units.py"
@"
import bpy
from pathlib import Path
src = Path(r'$($Source.FullName)')
out = Path(r'$BlenderOut')

print('BLENDER_PROGRESS 1/4 clearing scene', flush=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()
for datablocks in (bpy.data.actions, bpy.data.armatures, bpy.data.meshes):
    for datablock in list(datablocks):
        datablocks.remove(datablock, do_unlink=True)

bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 0.01

print('BLENDER_PROGRESS 2/4 importing mesh-only source FBX', flush=True)
bpy.ops.import_scene.fbx(
    filepath=str(src),
    use_anim=False,
    automatic_bone_orientation=False,
    ignore_leaf_bones=False,
    force_connect_children=False,
)

armatures = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
print(f'IMPORTED armatures={len(armatures)} meshes={len(meshes)} actions={len(bpy.data.actions)}')
if not armatures:
    raise RuntimeError('No armature imported from source FBX')

for arm_index, arm in enumerate(armatures, start=1):
    print(f'BLENDER_PROGRESS 3/4 applying armature transform {arm_index}/{len(armatures)}: {arm.name}', flush=True)
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True, properties=True)

print('BLENDER_PROGRESS 4/4 exporting mesh-only Unreal FBX', flush=True)
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
    bake_anim=False,
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
print(f'EXPORTED {out} size={out.stat().st_size if out.exists() else 0}')
"@ | Set-Content -Encoding UTF8 $Py

& $Blender --background --factory-startup --python $Py
if ($LASTEXITCODE -ne 0) { throw "Blender re-export failed with exit code $LASTEXITCODE" }
if (!(Test-Path $BlenderOut)) { throw "Missing Blender output: $BlenderOut" }

if ($KeepSourceFbx) {
    Write-Host "SOURCE_FBX=$($Source.FullName)"
} else {
    Remove-Item -LiteralPath $Source.FullName -Force
    Write-Host "SOURCE_FBX_REMOVED=$($Source.FullName)"
}
Write-Host "BLENDER_FBX=$BlenderOut"
Write-Host "TEXTURE_DIR=$TextureDir"
Write-Host "TEXTURE_COUNT=$TextureCount"
