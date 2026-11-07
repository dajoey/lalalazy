<#
.SYNOPSIS
    Gate: the KamiToolKit consumers must compile against the Dalamud payload they are pinned to.

.DESCRIPTION
    Bug-fix gate for the 2026-11-07 promote failure (LazyWTMath / LazyHub packaging died with
    33 CS1061 because the tree targets staging Dalamud APIs newer than the build host's
    installed dev payload). Run this before releasing either consumer - it fails at the gate,
    naming the payload version, instead of mid-package.

    Checks, in order:
      1. Pin parse: DalamudPinVersion + DalamudPinUrl present in src/Shared/DalamudPin/DalamudPin.props.
      2. Installed dev payload version (report only - a pin != dev is the situation the pin exists for).
      3. Payload provision/verify via tools/Update-DalamudPin.ps1 (skipped in -Unpinned mode).
      4. Release builds of src/LazyWTMath/KamiToolKit/KamiToolKit.csproj,
         src/LazyWTMath/LazyWTMath/LazyWTMath.csproj and src/LazyHub/LazyHub/LazyHub.csproj.
         Any error fails the gate naming the payload that produced it.

    -Unpinned builds with the pin disabled and no DALAMUD_HOME, i.e. exactly the pre-fix
    behavior: against the installed dev payload. Expect the CS1061 set there; it is the
    diagnostic mode for "did the tree drift ahead of its pin?" investigations.

    Exit 0 = green.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\Test-DalamudPin.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\Test-DalamudPin.ps1 -Unpinned
#>
param(
    [string]$RepoRoot,
    [switch]$Unpinned
)

$ErrorActionPreference = 'Continue'
$tag = 'DalamudPinGate:'

if (-not $RepoRoot) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
if (-not (Test-Path (Join-Path $RepoRoot 'src/Shared/DalamudPin/DalamudPin.props'))) {
    Write-Output "$tag FAILED: pin props not found under $RepoRoot (run from the repo root or pass -RepoRoot)"
    exit 1
}

function Get-InstalledDevPayloadVersion {
    $candidates = @(
        (Join-Path $env:APPDATA 'XIVLauncher/addon/Hooks/dev'),
        (Join-Path $env:HOME '.xlcore/dalamud/Hooks/dev')
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path (Join-Path $c 'Dalamud.dll'))) {
            try {
                $pv = (Get-Item (Join-Path $c 'Dalamud.dll')).VersionInfo.ProductVersion
                if ($pv) { return @{ Path = $c; Version = (($pv -split '\+')[0]).Trim() } }
            } catch {}
        }
    }
    return $null
}

function Invoke-ConsumerBuild([string]$project, [string]$label, [switch]$UnpinnedBuild) {
    $dotnetArgs = @('build', $project, '-c', 'Release', '--nologo', '-v', 'minimal')
    if ($UnpinnedBuild) { $dotnetArgs += '-p:DalamudPinEnabled=false' }
    $out = & dotnet $dotnetArgs 2>&1
    $code = $LASTEXITCODE
    $errors = @{}
    foreach ($line in $out) {
        if ($line -match '^\s*(.+?)\((\d+),\d+\): error ([A-Z]+\d+):') {
            $errors["$($Matches[1]):$($Matches[2]):$($Matches[3])"] = $line.Trim()
        }
    }
    return @{ Exit = $code; UniqueErrors = $errors; Output = $out }
}

# --- 1. Pin parse -----------------------------------------------------------
$propsText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot 'src/Shared/DalamudPin/DalamudPin.props'))
$pinVersion = $null
$pinUrl = $null
if ($propsText -match '<DalamudPinVersion[^>]*>([^<]+)<') { $pinVersion = $Matches[1].Trim() }
if ($propsText -match '<DalamudPinUrl[^>]*>([^<]+)<') { $pinUrl = $Matches[1].Trim() }
if (-not $pinVersion -or -not $pinUrl) {
    Write-Output "$tag FAILED: could not parse DalamudPinVersion/DalamudPinUrl from src/Shared/DalamudPin/DalamudPin.props"
    exit 1
}
Write-Output "$tag pin: $pinVersion ($pinUrl)"

# --- 2. Installed dev payload (informational) --------------------------------
$dev = Get-InstalledDevPayloadVersion
if ($dev) {
    Write-Output "$tag installed dev payload: $($dev.Version) at $($dev.Path)"
    if ($Unpinned) {
        Write-Output "$tag mode: UNPINNED - builds run against the installed dev payload above (pre-fix behavior)"
        $effective = "installed dev $($dev.Version) at $($dev.Path)"
        $envEffectiveVersion = $dev.Version
    } else {
        $envEffectiveVersion = $pinVersion
    }
} else {
    Write-Output "$tag installed dev payload: not found (continuing)"
    $envEffectiveVersion = $pinVersion
}

# --- 3. Provision / verify the pinned payload --------------------------------
if (-not $Unpinned) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot 'tools/Update-DalamudPin.ps1') -Version $pinVersion -Url $pinUrl
    if ($LASTEXITCODE -ne 0) {
        Write-Output "$tag FAILED: pinned payload $pinVersion is not provisioned (see DalamudPin output above)"
        exit 1
    }
    $cache = Join-Path (Join-Path $env:USERPROFILE '.dalamud-refs') $pinVersion
    if (-not (Test-Path (Join-Path $cache 'Dalamud.dll')) -and $env:HOME) {
        $cache = Join-Path (Join-Path $env:HOME '.dalamud-refs') $pinVersion
    }
    $effective = "pinned $pinVersion at $cache"
} elseif ($dev) {
    $cache = $dev.Path
} else {
    $cache = '<no payload resolved>'
}

# --- 4. The consumer builds ---------------------------------------------------
$projects = @(
    'src/LazyWTMath/KamiToolKit/KamiToolKit.csproj',
    'src/LazyWTMath/LazyWTMath/LazyWTMath.csproj',
    'src/LazyHub/LazyHub/LazyHub.csproj'
)
$failed = $false
foreach ($p in $projects) {
    $full = Join-Path $RepoRoot $p
    if (-not (Test-Path $full)) {
        Write-Output "$tag FAILED: project missing: $p"
        $failed = $true
        continue
    }
    $result = Invoke-ConsumerBuild $full $p -UnpinnedBuild:$Unpinned
    if ($result.Exit -ne 0) {
        $failed = $true
        Write-Output "$tag FAILED: $p exited $($result.Exit) against $effective"
        $shown = 0
        foreach ($e in $result.UniqueErrors.Values) {
            Write-Output "    $e"
            $shown++
            if ($shown -ge 5) { Write-Output "    ... $($result.UniqueErrors.Count) distinct errors total (each CS line prints twice via stdout+stderr)"; break }
        }
    } else {
        Write-Output "$tag PASS: $p (0 errors) against $effective"
    }
}

if ($failed) {
    Write-Output "$tag RESULT: FAIL - KamiToolKit consumers do not compile against $effective"
    Write-Output "$tag (pin: $pinVersion; installed dev: $(if ($dev) { $dev.Version } else { 'not found' }); fix the payload or the source, then re-run)"
    exit 1
}
Write-Output "$tag RESULT: PASS - KamiToolKit consumers compile against $effective"
exit 0
