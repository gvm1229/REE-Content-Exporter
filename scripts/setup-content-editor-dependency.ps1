param(
    [string]$DependencyPath = "..\REE-Content-Editor",
    [string]$RemoteUrl = "https://github.com/kagenocookie/REE-Content-Editor.git",
    [string]$Commit = "7db72c1",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Invoke-Git {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$GitArgs
    )

    & git @GitArgs
    if ($LASTEXITCODE -ne 0) {
        throw "git $($GitArgs -join ' ') failed with exit code $LASTEXITCODE"
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$dependencyFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $DependencyPath))
$patchPath = Join-Path $repoRoot "patches\ree-content-editor-commonmeshresource-material-textures.patch"

if (-not (Test-Path $patchPath)) {
    throw "Missing dependency patch: $patchPath"
}

if (Test-Path $dependencyFullPath) {
    $gitDir = Join-Path $dependencyFullPath ".git"
    if (-not (Test-Path $gitDir)) {
        throw "Dependency path exists but is not a git repository: $dependencyFullPath"
    }

    $status = & git -C $dependencyFullPath status --porcelain
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect dependency git status: $dependencyFullPath"
    }
    if ($status -and -not $Force) {
        throw "Dependency repo has local changes. Re-run with -Force to reset it, or back it up first: $dependencyFullPath"
    }

    if ($Force) {
        Invoke-Git -C $dependencyFullPath reset --hard
        Invoke-Git -C $dependencyFullPath clean -fdx
    }

    Invoke-Git -C $dependencyFullPath fetch origin
} else {
    $parent = Split-Path $dependencyFullPath -Parent
    if (-not (Test-Path $parent)) {
        New-Item -ItemType Directory -Force $parent | Out-Null
    }
    Invoke-Git clone --recursive $RemoteUrl $dependencyFullPath
}

Invoke-Git -C $dependencyFullPath checkout $Commit
Invoke-Git -C $dependencyFullPath submodule update --init --recursive

# Apply from a clean pinned base. The resulting dependency intentionally has local changes;
# those changes are tracked by this exporter repo's patch file.
Invoke-Git -C $dependencyFullPath reset --hard
Invoke-Git -C $dependencyFullPath clean -fdx
Invoke-Git -C $dependencyFullPath apply --whitespace=nowarn $patchPath

Write-Host "Prepared patched REE-Content-Editor dependency at: $dependencyFullPath"
Write-Host "Pinned upstream commit: $Commit"
Write-Host "Patch: $patchPath"
Invoke-Git -C $dependencyFullPath status --short --branch
