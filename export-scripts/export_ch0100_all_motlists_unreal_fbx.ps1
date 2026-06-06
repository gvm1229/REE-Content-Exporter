param(
    [string]$Root = "D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000",
    [string]$ExportRoot = "C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter",
    [string]$Blender = "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe",
    [switch]$KeepSourceFbx
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
$Exporter = Join-Path $RepoRoot "bin\Release\net10.0\REE-Content-Exporter.exe"

if (!(Test-Path $Exporter)) { throw "Missing exporter: $Exporter. Build with dotnet build -c Release." }
if (!(Test-Path $Blender)) { throw "Missing Blender 4.5.9 executable: $Blender" }
if (!(Test-Path $ExportRoot)) { New-Item -ItemType Directory -Force -Path $ExportRoot | Out-Null }

$RunStamp = Get-Date -Format "yyyyMMdd_HHmmss"
$LogTemp = Join-Path $env:TEMP "ch0100_all_motlists_unreal_export__$RunStamp.log"
$FinalLogFileName = "ch0100_all_motlists_unreal_export.log"
$TranscriptStarted = $false
$LogCompleted = $false
$OutDir = $null

function Complete-ExportLog {
    if ($script:LogCompleted) { return }
    $script:LogCompleted = $true

    if ($script:TranscriptStarted) {
        Stop-Transcript | Out-Null
        $script:TranscriptStarted = $false
    }

    if (Test-Path $script:LogTemp) {
        if ($script:OutDir -and (Test-Path $script:OutDir)) {
            $finalLog = Join-Path $script:OutDir $script:FinalLogFileName
            Move-Item -LiteralPath $script:LogTemp -Destination $finalLog -Force
            Write-Host "EXPORT_LOG=$finalLog"
        } else {
            Write-Host "EXPORT_LOG_TEMP=$script:LogTemp"
        }
    }
}

trap {
    Write-Host "SCRIPT_STATUS=FAILED"
    Write-Host "SCRIPT_ERROR=$($_.Exception.Message)"
    Complete-ExportLog
    break
}

Start-Transcript -Path $LogTemp -Force | Out-Null
$TranscriptStarted = $true
Write-Host "SCRIPT=export_ch0100_all_motlists_unreal_fbx.ps1"
Write-Host "EXPORT_LOG_TEMP=$LogTemp"
Write-Host "ROOT=$Root"
Write-Host "EXPORT_ROOT=$ExportRoot"
Write-Host "BLENDER=$Blender"
Write-Host "KEEP_SOURCE_FBX=$KeepSourceFbx"

$BlenderVersionLine = (& $Blender --version 2>&1 | Select-Object -First 1)
if ($LASTEXITCODE -ne 0) { throw "Could not query Blender version from: $Blender" }
if ($BlenderVersionLine -notmatch 'Blender\s+4\.5\.9') {
    throw "Expected Blender 4.5.9 LTS, but found: $BlenderVersionLine"
}
Write-Host "BLENDER_VERSION=$BlenderVersionLine"

$SourceFileName = "ch0100_all_motlists_source.fbx"
$FinalFbxFileName = "ch0100_all_motlists_unreal.fbx"
$ReportFileName = "ch0100_all_motlists.skipped-animation-bones.md"
$OutputRequest = Join-Path $ExportRoot $SourceFileName
$Start = Get-Date

& $Exporter `
  --mesh "$Root\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828" `
  --streaming "$Root\streaming\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828" `
  --additional-mesh "$Root\character\ch\ch01\ch0100\10\ch0100_10.mesh.251121828" `
  --additional-mesh "$Root\character\ch\ch01\ch0100\20\ch0100_20.mesh.251121828" `
  --additional-mesh "$Root\character\ch\ch01\ch0100\40\ch0100_40_neo.mesh.251121828" `
  --motlist-dir "$Root\character\animation\ch\ch01\ch0100\motlist" `
  --no-placeholder-animation-bones `
  --texture-format png `
  --fbx-scale 100 `
  --output $OutputRequest
if ($LASTEXITCODE -ne 0) { throw "Exporter failed with exit code $LASTEXITCODE" }

$Source = Get-ChildItem $ExportRoot -Recurse -Filter $SourceFileName |
  Where-Object { $_.LastWriteTime -ge $Start.AddMinutes(-1) } |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1
if (!$Source) { throw "Could not find generated source FBX under $ExportRoot" }

$OutDir = Split-Path $Source.FullName -Parent
$TextureDir = Join-Path $OutDir "textures"
if (!(Test-Path $TextureDir)) { throw "Texture folder missing after export: $TextureDir" }
$TextureCount = (Get-ChildItem $TextureDir -File -ErrorAction Stop | Measure-Object).Count
if ($TextureCount -le 0) { throw "Texture folder exists but is empty: $TextureDir" }

$SourceBaseName = [System.IO.Path]::GetFileNameWithoutExtension($Source.Name)
$SourceReport = Join-Path $OutDir "$SourceBaseName.skipped-animation-bones.md"
$FinalReport = Join-Path $OutDir $ReportFileName
if (Test-Path $SourceReport) {
    Move-Item -LiteralPath $SourceReport -Destination $FinalReport -Force
}

$BlenderOut = Join-Path $OutDir $FinalFbxFileName
$Py = Join-Path $env:TEMP "blender_ch0100_all_motlists_unreal_cm_units.py"
@"
import bpy
import builtins
from pathlib import Path
src = Path(r'$($Source.FullName)')
out = Path(r'$BlenderOut')

def log_progress(message):
    print(f'BLENDER_PROGRESS {message}', flush=True)

def install_fbx_pose_progress(action_names):
    real_print = builtins.print
    state = {'pose_count': 0}
    total = max(1, len(action_names))

    def progress_print(*args, **kwargs):
        # Blender 4.5.9's FBX exporter prints noisy tuples such as
        # (bpy.data.armatures['Armature'], 'POSE') while baking animation
        # stacks. Convert those into actionable indexed progress lines.
        if len(args) == 1 and isinstance(args[0], tuple) and len(args[0]) >= 2 and args[0][1] == 'POSE':
            state['pose_count'] += 1
            index = state['pose_count']
            if index <= len(action_names):
                real_print(f'BLENDER_PROGRESS Exporting animation {index}/{total}: {action_names[index - 1]}', flush=True)
            else:
                real_print(f'BLENDER_PROGRESS Exporting additional FBX pose data after {total} animation stack(s): event {index}', flush=True)
            return
        real_print(*args, **kwargs)

    builtins.print = progress_print
    return real_print

log_progress('1/6 clearing scene')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()
for datablocks in (bpy.data.actions, bpy.data.armatures, bpy.data.meshes):
    for datablock in list(datablocks):
        datablocks.remove(datablock, do_unlink=True)

# Use centimeter scene units so Unreal does not need import scale 100.
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 0.01

log_progress('2/6 importing source FBX')
bpy.ops.import_scene.fbx(
    filepath=str(src),
    use_anim=True,
    automatic_bone_orientation=False,
    ignore_leaf_bones=False,
    force_connect_children=False,
)

armatures = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
print(f'IMPORTED armatures={len(armatures)} meshes={len(meshes)} actions={len(bpy.data.actions)}')
if not armatures:
    raise RuntimeError('No armature imported from source FBX')
if not bpy.data.actions:
    raise RuntimeError('No actions imported from source FBX')

for arm_index, arm in enumerate(armatures, start=1):
    log_progress(f'3/6 applying armature transform {arm_index}/{len(armatures)}: {arm.name}')
    print('BEFORE', arm.name, 'rot', [round(v, 6) for v in arm.rotation_euler], 'scale', [round(v, 6) for v in arm.scale])
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True, properties=True)
    print('AFTER_APPLY_ROT_SCALE', arm.name, 'rot', [round(v, 6) for v in arm.rotation_euler], 'scale', [round(v, 6) for v in arm.scale])

actions = list(bpy.data.actions)
action_names = [action.name for action in actions]
for arm in armatures:
    arm.animation_data_create()
    arm.animation_data.action = None
    for track in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(track)
    for action_index, action in enumerate(actions, start=1):
        log_progress(f'4/6 preparing NLA strip {action_index}/{len(actions)}: {action.name}')
        start, end = action.frame_range
        track = arm.animation_data.nla_tracks.new()
        track.name = action.name
        strip = track.strips.new(action.name, 0, action)
        strip.name = action.name
        strip.action_frame_start = start
        strip.action_frame_end = end
        strip.frame_start = 0
        strip.frame_end = max(1, end - start)
        strip.blend_type = 'REPLACE'
        strip.extrapolation = 'NOTHING'
for action in bpy.data.actions:
    action.use_fake_user = True

max_frame = 1
for action in bpy.data.actions:
    if action.frame_range:
        max_frame = max(max_frame, int(action.frame_range[1] - action.frame_range[0]))
bpy.context.scene.frame_start = 0
bpy.context.scene.frame_end = max_frame
bpy.context.scene.render.fps = 60

log_progress(f'5/6 exporting FBX with {len(actions)} animation stack(s)')
real_print = install_fbx_pose_progress(action_names)
try:
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
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=True,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
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
finally:
    builtins.print = real_print
log_progress('6/6 Blender FBX export complete')
print(f'EXPORTED {out} size={out.stat().st_size if out.exists() else 0}')
"@ | Set-Content -Encoding UTF8 $Py

& $Blender --background --python $Py
if ($LASTEXITCODE -ne 0) { throw "Blender re-export failed with exit code $LASTEXITCODE" }
if (!(Test-Path $BlenderOut)) { throw "Missing Blender output: $BlenderOut" }

if ($KeepSourceFbx) {
    Write-Host "SOURCE_FBX=$($Source.FullName)"
} else {
    Remove-Item -LiteralPath $Source.FullName -Force
    Write-Host "SOURCE_FBX_REMOVED=$($Source.FullName)"
}
Write-Host "BLENDER_FBX=$BlenderOut"
if (Test-Path $FinalReport) { Write-Host "SKIPPED_BONE_REPORT=$FinalReport" }
Write-Host "TEXTURE_DIR=$TextureDir"
Write-Host "TEXTURE_COUNT=$TextureCount"
Write-Host "SCRIPT_STATUS=SUCCESS"
Complete-ExportLog
