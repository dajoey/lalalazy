<#
.SYNOPSIS
  One-off (2026-09-25): publish each plugin's CURRENT zips as GitHub Release assets and point
  pluginmaster.json at them. From then on Package-Plugin.ps1 publishes every build itself.
.DESCRIPTION
  For every pluginmaster entry with source under src/ (ARControlPRTest2 has none and keeps its links):
    production  plugins/<P>/latest/latest.zip   -> release <P>-v<AssemblyVersion>, asset <P>.zip
    testing     plugins/<P>/testing/testing.zip -> release <P>-v<TestingAssemblyVersion>, asset <P>-testing.zip
  A zip is published only if the <P>.json inside it carries exactly the version it goes out
  under, so a stale zip can never be linked under the wrong version. Only DownloadLink values
  change in pluginmaster.json (a textual edit: the rest of the file keeps its bytes), and every
  edit is proven to match exactly one line before anything is published.
  Re-runnable: a link that already points at a Release asset is left alone.

  -Check publishes nothing: every Release link must name the version its entry advertises for
  that channel. Run it after any merge that touched pluginmaster.json - a merge can pair one
  commit's new version with another commit's old link, and Dalamud would then install a zip
  whose manifest disagrees with the index (the stale-zip failure).
.EXAMPLE
  powershell -NoProfile -File tools\Migrate-ToReleases.ps1 -WhatIf
  powershell -NoProfile -File tools\Migrate-ToReleases.ps1
  powershell -NoProfile -File tools\Migrate-ToReleases.ps1 -Check
#>
param([switch] $WhatIf, [switch] $Check)
$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$masterPath = Join-Path $RepoRoot 'pluginmaster.json'
. (Join-Path $PSScriptRoot 'PackageRelease.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem

$rawBase = 'https://raw.githubusercontent.com/dajoey/lalalazy/main/plugins'
$relPrefix = 'https://github.com/dajoey/lalalazy/releases/download/'
$linkFields = @('DownloadLinkInstall', 'DownloadLinkUpdate', 'DownloadLinkTesting')

function Test-ReleaseLink([string] $Url) { $Url -and $Url.StartsWith($relPrefix) }

function Get-LinkMismatches($List) {
    # A Release link's tag must carry the version of the channel its asset belongs to.
    $bad = @()
    foreach ($e in $List) {
        foreach ($f in $linkFields) {
            $url = [string]$e.$f
            if (-not (Test-ReleaseLink $url)) { continue }
            $parts = $url.Substring($relPrefix.Length).Split('/')
            $tagVersion = $parts[0] -replace ('^' + [regex]::Escape($e.InternalName) + '-v'), ''
            $want = if ($parts[1] -like '*-testing.zip') { $e.TestingAssemblyVersion } else { $e.AssemblyVersion }
            if ($parts[0] -notlike "$($e.InternalName)-v*" -or $tagVersion -ne $want) {
                $bad += "$($e.InternalName) $f -> $($parts[0])/$($parts[1]) but the entry advertises $want"
            }
        }
    }
    return $bad
}

function Get-ZipManifestVersion([string] $ZipPath, [string] $PluginName) {
    $z = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entry = $z.Entries | Where-Object { $_.FullName -eq "$PluginName.json" } | Select-Object -First 1
        if (-not $entry) { return $null }
        $r = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
        try { return ($r.ReadToEnd() | ConvertFrom-Json).AssemblyVersion } finally { $r.Dispose() }
    } finally { $z.Dispose() }
}

function Get-ChangelogSection([string] $PluginName, [string] $Version) {
    foreach ($rel in @("src\$PluginName\$PluginName\CHANGELOG.md", "src\$PluginName\CHANGELOG.md")) {
        $path = Join-Path $RepoRoot $rel
        if (-not (Test-Path $path)) { continue }
        $out = New-Object System.Collections.Generic.List[string]
        $on = $false
        foreach ($line in [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)) {
            if ($line -match '^##\s') {
                if ($on) { break }
                if ($line -match ('^##\s+\[?v?' + [regex]::Escape($Version) + '\b')) { $on = $true }
            }
            if ($on) { $out.Add($line) }
        }
        if ($out.Count) { return ($out -join "`n").Trim() }
    }
    return "Release notes: src/$PluginName/CHANGELOG.md"
}

$text = [System.IO.File]::ReadAllText($masterPath, [System.Text.Encoding]::UTF8)
$list = $text | ConvertFrom-Json

if ($Check) {
    $bad = @(Get-LinkMismatches $list)
    $n = @($list | ForEach-Object { $e = $_; $linkFields | Where-Object { Test-ReleaseLink ([string]$e.$_) } }).Count
    if ($bad.Count) { $bad | ForEach-Object { Write-Host "MISMATCH $_" -ForegroundColor Red }; exit 1 }
    Write-Host "OK: $n Release links, every one names the version its entry advertises."
    exit 0
}

# 1. Plan: which zips go up, and the exact line each one's link replaces.
$plan = New-Object System.Collections.Generic.List[object]
foreach ($e in $list) {
    $p = $e.InternalName
    if (-not (Test-Path (Join-Path $RepoRoot "src\$p"))) { Write-Host "skip $p - no source under src/, links left as they are"; continue }
    $exclusive = $e.PSObject.Properties['IsTestingExclusive'] -and $e.IsTestingExclusive
    $jobs = @()
    if (-not $exclusive -and $e.AssemblyVersion -and $e.AssemblyVersion -ne '0.0.0.0' -and -not (Test-ReleaseLink $e.DownloadLinkInstall)) {
        $jobs += @{ Channel = 'production'; Version = $e.AssemblyVersion; Zip = "plugins\$p\latest\latest.zip"
                    Old = "$rawBase/$p/latest/latest.zip"; Fields = @('DownloadLinkInstall', 'DownloadLinkUpdate') }
    }
    if ($e.PSObject.Properties['TestingAssemblyVersion'] -and $e.TestingAssemblyVersion -and -not (Test-ReleaseLink $e.DownloadLinkTesting)) {
        $fields = @('DownloadLinkTesting')
        if ($exclusive) { $fields += @('DownloadLinkInstall', 'DownloadLinkUpdate') }
        $jobs += @{ Channel = 'testing'; Version = $e.TestingAssemblyVersion; Zip = "plugins\$p\testing\testing.zip"
                    Old = "$rawBase/$p/testing/testing.zip"; Fields = $fields }
    }
    foreach ($j in $jobs) {
        $zip = Join-Path $RepoRoot $j.Zip
        if (-not (Test-Path $zip)) { Write-Host "SKIP $p $($j.Channel) - $($j.Zip) is missing, left on raw" -ForegroundColor Yellow; continue }
        $zv = Get-ZipManifestVersion $zip $p
        if ($zv -ne $j.Version) { Write-Host "SKIP $p $($j.Channel) - the zip carries $zv, the entry advertises $($j.Version); left on raw" -ForegroundColor Yellow; continue }
        foreach ($f in $j.Fields) {
            $hits = [regex]::Matches($text, '"' + $f + '":\s*"' + [regex]::Escape($j.Old) + '"').Count
            if ($hits -ne 1) { throw "$p ${f}: expected exactly one line with $($j.Old), found $hits. Nothing was published." }
        }
        $plan.Add([PSCustomObject]@{ Plugin = $p; Name = $e.Name; Channel = $j.Channel; Version = $j.Version
                                     Zip = $zip; Old = $j.Old; Fields = $j.Fields; Url = $null })
    }
}

$plan | ForEach-Object { Write-Host ("{0,-22} {1,-10} {2,-12} -> {3}" -f $_.Plugin, $_.Channel, $_.Version, (Get-ReleaseAssetUrl $_.Plugin $_.Version $_.Channel)) }
if ($WhatIf) { Write-Host "WhatIf: $($plan.Count) zips would be published; nothing published, pluginmaster.json untouched."; exit 0 }
if (-not $plan.Count) { Write-Host 'Nothing to migrate.'; exit 0 }

# 2. Publish. A failure stops before pluginmaster.json is touched; re-running replaces any
#    asset that went up (unreferenced assets are unpublished by definition).
$token = Get-ReleaseGitHubToken
foreach ($item in $plan) {
    $item.Url = Publish-PluginRelease -PluginName $item.Plugin -Version $item.Version -Channel $item.Channel `
        -ZipPath $item.Zip -DisplayName $item.Name -Notes (Get-ChangelogSection $item.Plugin $item.Version) -Token $token
}

# 3. Rewrite only the planned link values, then prove nothing else moved.
$new = $text
foreach ($item in $plan) {
    foreach ($f in $item.Fields) {
        $new = [regex]::Replace($new, '("' + $f + '":\s*")' + [regex]::Escape($item.Old) + '"', ('${1}' + $item.Url + '"'))
    }
}
$after = $new | ConvertFrom-Json
function Get-Stripped($l) { ($l | Select-Object -Property * -ExcludeProperty $linkFields | ConvertTo-Json -Depth 20 -Compress) }
if ((Get-Stripped $after) -ne (Get-Stripped $list)) { throw 'pluginmaster.json changed outside the DownloadLink fields; not written.' }
$bad = @(Get-LinkMismatches $after)
if ($bad.Count) { $bad | ForEach-Object { Write-Host "MISMATCH $_" -ForegroundColor Red }; throw 'Version/link mismatch after rewrite; not written.' }
[System.IO.File]::WriteAllText($masterPath, $new, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Published $($plan.Count) zips and pointed their links at them. Run -Check after any merge before pushing."
