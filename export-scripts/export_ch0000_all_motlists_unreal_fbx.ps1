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
$LogTemp = Join-Path $env:TEMP "ch0000_motlists_unreal_export__$RunStamp.log"
$FinalLogBaseName = "ch0000_motlists_unreal_export"
$TranscriptStarted = $false
$LogCompleted = $false
$OutDir = $null
$BlenderSkippedMotlists = New-Object System.Collections.Generic.List[object]

function Complete-ExportLog {
    param(
        [ValidateSet("SUCCESS", "FAIL")]
        [string]$Status
    )

    if ($script:LogCompleted) { return }
    $script:LogCompleted = $true

    if ($script:TranscriptStarted) {
        Stop-Transcript | Out-Null
        $script:TranscriptStarted = $false
    }

    if (Test-Path $script:LogTemp) {
        $finalLogName = "{0}-{1}.log" -f $script:FinalLogBaseName, $Status
        if ($script:OutDir -and (Test-Path $script:OutDir)) {
            $finalLog = Join-Path $script:OutDir $finalLogName
            Move-Item -LiteralPath $script:LogTemp -Destination $finalLog -Force
            Write-Host "EXPORT_LOG=$finalLog"
        } else {
            $finalTempLog = Join-Path ([System.IO.Path]::GetDirectoryName($script:LogTemp)) ("{0}-{1}__{2}.log" -f $script:FinalLogBaseName, $Status, $script:RunStamp)
            Move-Item -LiteralPath $script:LogTemp -Destination $finalTempLog -Force
            Write-Host "EXPORT_LOG_TEMP=$finalTempLog"
        }
    }
}

function Get-FinalBaseName {
    param([System.IO.FileInfo]$Source)
    $base = [System.IO.Path]::GetFileNameWithoutExtension($Source.Name)
    $base = $base -replace '^\d{4}_', ''
    $base = $base -replace '_all_animations$', ''
    return $base
}


function Write-BlenderSkippedMotlistReport {
    param(
        [string]$JobDir,
        [object[]]$SkippedMotlists
    )

    if (!$JobDir -or !(Test-Path $JobDir)) { return }

    $ReportPath = Join-Path $JobDir "skipped-blender-motlists.md"
    $Lines = New-Object System.Collections.Generic.List[string]
    $Lines.Add("# Skipped Blender MOTLIST Re-exports")
    $Lines.Add("")
    $Lines.Add("This report lists split MOTLIST source FBX files that were created by REE-Content-Exporter but intentionally skipped during the Blender Unreal re-export phase.")
    $Lines.Add("")
    $Lines.Add("These entries usually represent MOTLIST files that contain resource data, but Blender imports zero animation actions from the generated source FBX. They are not treated as fatal export failures because there is no animation stack for Blender to re-export.")
    $Lines.Add("")

    if (!$SkippedMotlists -or $SkippedMotlists.Count -eq 0) {
        $Lines.Add("No MOTLIST source FBX files were skipped by Blender re-export.")
    } else {
        $Lines.Add("| Source FBX | Intended Unreal FBX | Reason | Imported Actions |")
        $Lines.Add("| --- | --- | --- | --- |")
        foreach ($Skipped in $SkippedMotlists) {
            $sourceName = [System.IO.Path]::GetFileName($Skipped.Source)
            $targetName = [System.IO.Path]::GetFileName($Skipped.Target)
            $reason = ($Skipped.Reason -replace '\|', '\/')
            $Lines.Add("| $sourceName | $targetName | $reason | $($Skipped.ActionCount) |")
        }
    }

    $Lines | Set-Content -Encoding UTF8 $ReportPath
    Write-Host "BLENDER_SKIPPED_MOTLIST_REPORT=$ReportPath"
}

function Invoke-BlenderReexport {
    param(
        [System.IO.FileInfo]$Source,
        [string]$BlenderOut,
        [int]$Index,
        [int]$Total,
        [string]$StatusPath
    )

    $Py = Join-Path $env:TEMP "blender_ch0000_motlist_unreal_cm_units.py"
    @"
import bpy
import builtins
from pathlib import Path
src = Path(r'$($Source.FullName)')
out = Path(r'$BlenderOut')
status_path = Path(r'$StatusPath')
index = $Index
total = $Total

def write_status(status, reason='', action_count=0):
    status_path.write_text(f'STATUS={status}\nREASON={reason}\nACTION_COUNT={action_count}\n', encoding='utf-8')

def log_progress(message):
    print(f'BLENDER_PROGRESS {message}', flush=True)

def install_fbx_pose_progress(action_names):
    real_print = builtins.print
    state = {'pose_count': 0}
    total_actions = max(1, len(action_names))

    def progress_print(*args, **kwargs):
        if len(args) == 1 and isinstance(args[0], tuple) and len(args[0]) >= 2 and args[0][1] == 'POSE':
            state['pose_count'] += 1
            pose_index = state['pose_count']
            if pose_index <= len(action_names):
                real_print(f'BLENDER_PROGRESS Motlist {index}/{total} exporting animation {pose_index}/{total_actions}: {action_names[pose_index - 1]}', flush=True)
            else:
                real_print(f'BLENDER_PROGRESS Motlist {index}/{total} exporting additional FBX pose data after {total_actions} animation stack(s): event {pose_index}', flush=True)
            return
        real_print(*args, **kwargs)

    builtins.print = progress_print
    return real_print

log_progress(f'Motlist {index}/{total} 1/6 clearing scene')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()
for datablocks in (bpy.data.actions, bpy.data.armatures, bpy.data.meshes):
    for datablock in list(datablocks):
        datablocks.remove(datablock, do_unlink=True)

bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 0.01

log_progress(f'Motlist {index}/{total} 2/6 importing source FBX')
bpy.ops.import_scene.fbx(
    filepath=str(src),
    use_anim=True,
    automatic_bone_orientation=False,
    ignore_leaf_bones=False,
    force_connect_children=False,
)

armatures = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
print(f'IMPORTED motlist={index}/{total} armatures={len(armatures)} meshes={len(meshes)} actions={len(bpy.data.actions)}')
if not armatures:
    write_status('FAILED', 'No armature imported from source FBX', len(bpy.data.actions))
    raise RuntimeError('No armature imported from source FBX')
if not bpy.data.actions:
    reason = 'No actions imported from source FBX'
    write_status('SKIPPED', reason, 0)
    print(f'BLENDER_SKIP motlist={index}/{total} reason={reason} source={src.name}', flush=True)
    raise SystemExit(0)

for arm_index, arm in enumerate(armatures, start=1):
    log_progress(f'Motlist {index}/{total} 3/6 applying armature transform {arm_index}/{len(armatures)}: {arm.name}')
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
        log_progress(f'Motlist {index}/{total} 4/6 preparing NLA strip {action_index}/{len(actions)}: {action.name}')
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

log_progress(f'Motlist {index}/{total} 5/6 exporting FBX with {len(actions)} animation stack(s)')
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
log_progress(f'Motlist {index}/{total} 6/6 Blender FBX export complete')
write_status('EXPORTED', '', len(actions))
print(f'EXPORTED {out} size={out.stat().st_size if out.exists() else 0}')
"@ | Set-Content -Encoding UTF8 $Py

    Remove-Item -LiteralPath $StatusPath -Force -ErrorAction SilentlyContinue
    & $Blender --background --factory-startup --python $Py
    if ($LASTEXITCODE -ne 0) { throw "Blender re-export failed for $($Source.Name) with exit code $LASTEXITCODE" }
    if (!(Test-Path $StatusPath)) { throw "Missing Blender status file for $($Source.Name): $StatusPath" }

    $Status = @{}
    foreach ($Line in Get-Content -LiteralPath $StatusPath) {
        $Parts = $Line -split '=', 2
        if ($Parts.Count -eq 2) { $Status[$Parts[0]] = $Parts[1] }
    }

    $ActionCount = 0
    if ($Status.ContainsKey('ACTION_COUNT')) { [void][int]::TryParse($Status['ACTION_COUNT'], [ref]$ActionCount) }
    $Reason = if ($Status.ContainsKey('REASON')) { $Status['REASON'] } else { '' }

    if ($Status['STATUS'] -eq 'SKIPPED') {
        return [pscustomobject]@{ Status = 'SKIPPED'; Reason = $Reason; ActionCount = $ActionCount }
    }
    if ($Status['STATUS'] -ne 'EXPORTED') {
        if (!$Reason) { $Reason = "Unexpected Blender status: $($Status['STATUS'])" }
        throw $Reason
    }
    if (!(Test-Path $BlenderOut)) { throw "Missing Blender output: $BlenderOut" }
    return [pscustomobject]@{ Status = 'EXPORTED'; Reason = $Reason; ActionCount = $ActionCount }
}

trap {
    if ($script:OutDir -and (Test-Path $script:OutDir)) {
        Write-BlenderSkippedMotlistReport -JobDir $script:OutDir -SkippedMotlists $script:BlenderSkippedMotlists.ToArray()
    }
    Write-Host "SCRIPT_STATUS=FAILED"
    Write-Host "SCRIPT_ERROR=$($_.Exception.Message)"
    Complete-ExportLog -Status "FAIL"
    break
}

Start-Transcript -Path $LogTemp -Force | Out-Null
$TranscriptStarted = $true
Write-Host "SCRIPT=export_ch0000_all_motlists_unreal_fbx.ps1"
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

$OutputRequest = Join-Path $ExportRoot "ch0000_motlists_source.fbx"
$Start = Get-Date

& $Exporter `
  --mesh "$Root\character\ch\ch00\ch0000\00\ch0000_00_playergame.mesh.251121828" `
  --motlist-dir "$Root\character\animation\ch\ch00\ch0000\motlist" `
  --split-motlists `
  --no-placeholder-animation-bones `
  --texture-format png `
  --fbx-scale 100 `
  --output $OutputRequest
if ($LASTEXITCODE -ne 0) { throw "Exporter failed with exit code $LASTEXITCODE" }

$RecentSources = Get-ChildItem $ExportRoot -Recurse -File -Filter "*_all_animations.fbx" |
  Where-Object { $_.LastWriteTime -ge $Start.AddMinutes(-2) } |
  Sort-Object FullName
if (!$RecentSources -or $RecentSources.Count -eq 0) { throw "Could not find split MOTLIST source FBX files under $ExportRoot" }

$OutDir = ($RecentSources | Sort-Object LastWriteTime -Descending | Select-Object -First 1).DirectoryName
$SourceFiles = Get-ChildItem $OutDir -File -Filter "*_all_animations.fbx" | Sort-Object Name
if (!$SourceFiles -or $SourceFiles.Count -eq 0) { throw "No source FBX files found in split MOTLIST job folder: $OutDir" }
Write-Host "SPLIT_MOTLIST_SOURCE_COUNT=$($SourceFiles.Count)"
Write-Host "JOB_DIR=$OutDir"

$TextureDir = Join-Path $OutDir "textures"
if (!(Test-Path $TextureDir)) { throw "Texture folder missing after export: $TextureDir" }
$TextureCount = (Get-ChildItem $TextureDir -File -ErrorAction Stop | Measure-Object).Count
if ($TextureCount -le 0) { throw "Texture folder exists but is empty: $TextureDir" }

for ($i = 0; $i -lt $SourceFiles.Count; $i++) {
    $Source = $SourceFiles[$i]
    $finalBase = Get-FinalBaseName -Source $Source
    $BlenderOut = Join-Path $OutDir "$($finalBase)_unreal.fbx"
    $SourceReport = Join-Path $OutDir "$([System.IO.Path]::GetFileNameWithoutExtension($Source.Name)).skipped-animation-bones.md"
    $FinalReport = Join-Path $OutDir "$finalBase.skipped-animation-bones.md"

    Write-Host "MOTLIST_SOURCE=$($Source.FullName)"
    Write-Host "MOTLIST_TARGET=$BlenderOut"
    $StatusPath = Join-Path $env:TEMP ("{0}_blender_status.txt" -f ([System.IO.Path]::GetFileNameWithoutExtension($Source.Name)))
    $BlenderResult = Invoke-BlenderReexport -Source $Source -BlenderOut $BlenderOut -Index ($i + 1) -Total $SourceFiles.Count -StatusPath $StatusPath

    if ($BlenderResult.Status -eq 'SKIPPED') {
        $script:BlenderSkippedMotlists.Add([pscustomobject]@{
            Source = $Source.FullName
            Target = $BlenderOut
            Reason = $BlenderResult.Reason
            ActionCount = $BlenderResult.ActionCount
        })
        Write-Host "MOTLIST_SKIPPED_BY_BLENDER=$($Source.FullName)"
        Write-Host "MOTLIST_SKIP_REASON=$($BlenderResult.Reason)"
        if ($KeepSourceFbx) {
            Write-Host "SOURCE_FBX=$($Source.FullName)"
        } else {
            Remove-Item -LiteralPath $Source.FullName -Force
            Write-Host "SOURCE_FBX_REMOVED=$($Source.FullName)"
            if (Test-Path $SourceReport) {
                Remove-Item -LiteralPath $SourceReport -Force
                Write-Host "SOURCE_SKIPPED_BONE_REPORT_REMOVED=$SourceReport"
            }
        }
        Write-Host "MOTLIST_DONE=$($i + 1)/$($SourceFiles.Count)"
        continue
    }

    if (Test-Path $SourceReport) {
        Move-Item -LiteralPath $SourceReport -Destination $FinalReport -Force
        Write-Host "SKIPPED_BONE_REPORT=$FinalReport"
    }

    if ($KeepSourceFbx) {
        Write-Host "SOURCE_FBX=$($Source.FullName)"
    } else {
        Remove-Item -LiteralPath $Source.FullName -Force
        Write-Host "SOURCE_FBX_REMOVED=$($Source.FullName)"
    }
    Write-Host "BLENDER_FBX=$BlenderOut"
    Write-Host "MOTLIST_DONE=$($i + 1)/$($SourceFiles.Count)"
}

Write-BlenderSkippedMotlistReport -JobDir $OutDir -SkippedMotlists $BlenderSkippedMotlists.ToArray()
Write-Host "TEXTURE_DIR=$TextureDir"
Write-Host "TEXTURE_COUNT=$TextureCount"
Write-Host "SCRIPT_STATUS=SUCCESS"
Complete-ExportLog -Status "SUCCESS"
