<#
.SYNOPSIS
    Provision / verify the pinned Dalamud payload for the KamiToolKit consumers.

.DESCRIPTION
    The pin's source of truth is src/Shared/DalamudPin/DalamudPin.props (DalamudPinVersion +
    DalamudPinUrl). This script materializes the pinned payload in
    <profile>\.dalamud-refs\<version> so builds can point DALAMUD_HOME at it (see that props
    file). It never touches XIVLauncher's folders.

    Acquisition order: (1) cache already valid -> no network; (2) -Source <existing payload
    dir> (a run-scratch copy or another host's cache); (3) download -Url (the goatcorp staging
    distrib, which always serves the CURRENT staging build).

    Verification: the payload must contain Dalamud.dll and FFXIVClientStructs.dll and
    Dalamud.dll's ProductVersion prefix must equal -Version. A mismatch fails loudly naming
    both versions: staging has moved past the pin (bump DalamudPinVersion after re-verifying
    the consumers build, see tools/Test-DalamudPin.ps1) or -Source is not the pinned payload.

    Install is atomic-ish: extract/copy into .tmp-<pid>, verify, then rename into place; a
    concurrent run that loses the rename race verifies the winner and discards its own copy.

.PARAMETER Version
    Pinned Dalamud version, e.g. 15.0.3.9 (compare is on the ProductVersion prefix before '+').

.PARAMETER Url
    Payload zip to download when the cache and -Source are not usable.

.PARAMETER CacheRoot
    Cache root; default %USERPROFILE%\.dalamud-refs.

.PARAMETER Source
    Existing payload folder to seed from instead of downloading (its version is still verified).

.PARAMETER Force
    Re-acquire even when a cache folder exists (replaces it after the new copy verifies).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\Update-DalamudPin.ps1 -Version 15.0.3.9
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Url = 'https://goatcorp.github.io/dalamud-distrib/stg/latest.zip',
    [string]$CacheRoot,
    [string]$Source,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$tag = 'DalamudPin:'
$script:zipPath = $null
$script:tmpPath = $null

function Fail([string]$msg) {
    if ($script:zipPath -and (Test-Path $script:zipPath)) { Remove-Item -Force $script:zipPath -ErrorAction SilentlyContinue }
    if ($script:tmpPath -and (Test-Path $script:tmpPath)) { Remove-Item -Recurse -Force $script:tmpPath -ErrorAction SilentlyContinue }
    Write-Output "$tag FAILED: $msg"
    exit 1
}

function Get-PayloadVersion([string]$dir) {
    $dal = Join-Path $dir 'Dalamud.dll'
    if (-not (Test-Path $dal)) { return $null }
    if (-not (Test-Path (Join-Path $dir 'FFXIVClientStructs.dll'))) { return $null }
    try {
        $pv = (Get-Item $dal).VersionInfo.ProductVersion
    } catch { return $null }
    if (-not $pv) { return $null }
    return (($pv -split '\+')[0]).Trim()
}

function Write-Stamp([string]$dir, [string]$acquiredFrom) {
    $dalSha = (Get-FileHash (Join-Path $dir 'Dalamud.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    $fcsSha = (Get-FileHash (Join-Path $dir 'FFXIVClientStructs.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    $stamp = '{"version":"' + $Version + '","url":"' + $Url + '","acquired":"' + $acquiredFrom + '"' +
             ',"dalamud_sha256":"' + $dalSha + '","fcs_sha256":"' + $fcsSha + '"' +
             ',"created_utc":"' + (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') + '"}'
    # Compact JSON, first field exactly "version":"<Version>" - DalamudPin.props byte-matches it.
    [System.IO.File]::WriteAllText((Join-Path $dir 'payload-pin.json'), $stamp, (New-Object System.Text.UTF8Encoding($false)))
}

function Expand-PayloadZip([string]$zip, [string]$destDir) {
    # tar.exe (Windows 10 1803+ / any modern Linux) is much faster than Expand-Archive on big zips.
    $tar = Join-Path $env:SystemRoot 'System32\tar.exe'
    if (-not $tar) { $tar = 'tar' }
    $extracted = $false
    try {
        & $tar -xf $zip -C $destDir 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) { $extracted = $true }
    } catch { $extracted = $false }
    if (-not $extracted) {
        Expand-Archive -Path $zip -DestinationPath $destDir -Force
    }
    # Normalize: if the zip wrapped everything in one folder, descend into it.
    if (-not (Test-Path (Join-Path $destDir 'Dalamud.dll'))) {
        $children = @(Get-ChildItem $destDir -Directory)
        if ($children.Count -ge 1 -and (Test-Path (Join-Path $children[0].FullName 'Dalamud.dll'))) {
            Get-ChildItem $destDir | Move-Item -Destination $destDir -ErrorAction SilentlyContinue
            $inner = Join-Path $destDir $children[0].Name
            if (Test-Path $inner) { Remove-Item -Recurse -Force $inner }
        }
    }
}

if (-not $CacheRoot) {
    if ($env:USERPROFILE) { $CacheRoot = Join-Path $env:USERPROFILE '.dalamud-refs' }
    else { $CacheRoot = Join-Path $env:HOME '.dalamud-refs' }
}
$cache = Join-Path $CacheRoot $Version

# 1. Fast path: valid cache, nothing to do.
$haveVersion = Get-PayloadVersion $cache
if ($haveVersion -eq $Version -and -not $Force) {
    $stampFile = Join-Path $cache 'payload-pin.json'
    if (-not (Test-Path $stampFile)) { Write-Stamp $cache 'pre-existing' }
    Write-Output "$tag ok: pinned payload $Version at $cache"
    exit 0
}

# 2. Foreign or wrong-version cache folder: never build against it, never silently replace it.
if ($haveVersion -ne $null -and $haveVersion -ne $Version) {
    Fail ("cache $cache holds Dalamud $haveVersion but the pin wants $Version." +
          " Remove that folder or fix the pin in src/Shared/DalamudPin/DalamudPin.props" +
          " (verify the consumers build against the new payload first: tools/Test-DalamudPin.ps1).")
}

# 3. Acquire into a temp dir, verify, then swap into place.
if (-not (Test-Path $CacheRoot)) { New-Item -ItemType Directory -Force -Path $CacheRoot | Out-Null }
$script:tmpPath = Join-Path $CacheRoot ('.tmp-' + $PID + '-' + $Version)
if (Test-Path $script:tmpPath) { Remove-Item -Recurse -Force $script:tmpPath }
New-Item -ItemType Directory -Force -Path $script:tmpPath | Out-Null

if ($Source) {
    if (-not (Test-Path (Join-Path $Source 'Dalamud.dll'))) {
        Fail "-Source '$Source' has no Dalamud.dll at its root."
    }
    Write-Output "$tag seeding $Version from $Source"
    Copy-Item -Path (Join-Path $Source '*') -Destination $script:tmpPath -Recurse -Force
} else {
    Write-Output "$tag downloading $Url"
    $script:zipPath = Join-Path $CacheRoot ('dalamud-' + $Version + '.zip')
    $curl = 'curl.exe'
    if (-not (Get-Command $curl -ErrorAction SilentlyContinue)) { $curl = 'curl' }
    & $curl -fsSL --retry 3 -o $script:zipPath $Url
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $script:zipPath)) { Fail "download failed (curl exit $LASTEXITCODE): $Url" }
    if ((Get-Item $script:zipPath).Length -lt 1MB) { Fail "downloaded zip is suspiciously small ($((Get-Item $script:zipPath).Length) bytes): $Url" }
    Expand-PayloadZip $script:zipPath $script:tmpPath
}

$haveVersion = Get-PayloadVersion $script:tmpPath
if ($haveVersion -ne $Version) {
    if (-not $haveVersion) {
        Fail "acquired payload at $($script:tmpPath) is incomplete (no Dalamud.dll / FFXIVClientStructs.dll / unreadable version)."
    }
    Fail ("acquired payload is Dalamud $haveVersion but the pin wants $Version." +
          " The staging distrib serves the CURRENT staging build, so staging has moved past the pin:" +
          " bump DalamudPinVersion in src/Shared/DalamudPin/DalamudPin.props only after" +
          " tools/Test-DalamudPin.ps1 passes against the new payload, or seed this host from an" +
          " archived copy of the pinned payload with -Source <dir>.")
}

# 4. Swap into place (rename race: last writer wins the name, both copies verified equal).
if (Test-Path $cache) { Remove-Item -Recurse -Force $cache }
Rename-Item -Path $script:tmpPath -NewName $Version
$script:tmpPath = $null
if ($script:zipPath -and (Test-Path $script:zipPath)) { Remove-Item -Force $script:zipPath }
$script:zipPath = $null
Write-Stamp $cache $(if ($Source) { "source:$Source" } else { "download:$Url" })
Write-Output "$tag ok: pinned payload $Version at $cache"
exit 0
