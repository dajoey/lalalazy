<#
.SYNOPSIS
  Standardized script to build, package, and register any Dalamud plugin in the lalalazy monorepo.
.DESCRIPTION
  Standardizes the release pipeline. Cleans old build artifacts, compiles the plugin in Release,
  packages the zip to plugins/<PluginName>/latest/latest.zip (or testing/testing.zip),
  and automatically updates the global pluginmaster.json index with the latest metadata and download URLs.
.PARAMETER PluginName
  The name of the plugin directory in src/ (e.g. "LazyFATEAutomator", "PvPSolver").
.PARAMETER Channel
  The target release channel: "production" or "testing". Default is "production".
.PARAMETER VersionOverride
  Optional version string to override the version specified in the project/manifest.
.EXAMPLE
  .\Package-Plugin.ps1 -PluginName LazyFATEAutomator
#>
param(
  [Parameter(Mandatory=$true)][string]$PluginName,
  [ValidateSet('production', 'testing')][string]$Channel = 'production',
  [string]$VersionOverride
)

$ErrorActionPreference = 'Stop'

# Resolve paths
$here = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = Resolve-Path (Join-Path $here '..')
$srcDir = Join-Path $RepoRoot "src\$PluginName"
$masterPath = Join-Path $RepoRoot "pluginmaster.json"

if (-not (Test-Path $srcDir)) {
    throw "Plugin source directory not found: $srcDir"
}

# Support nested project folder (e.g. src/GluttonyCombo/GluttonyCombo)
$nestedDir = Join-Path $srcDir $PluginName
if (Test-Path $nestedDir) {
    $srcDir = $nestedDir
}

Write-Host "==> Standardized Packaging: $PluginName ($Channel)" -ForegroundColor Cyan

# Parse pluginmaster.json first to evaluate version status
if (-not (Test-Path $masterPath)) {
    throw "pluginmaster.json not found at $masterPath"
}
$masterJsonText = [System.IO.File]::ReadAllText($masterPath, [System.Text.Encoding]::UTF8)
$masterList = $masterJsonText | ConvertFrom-Json
$entry = $masterList | Where-Object { $_.InternalName -eq $PluginName }

# Check csproj version and perform auto-bump if deploying production with unchanged version
$csprojPath = Join-Path $srcDir "$PluginName.csproj"
if (Test-Path $csprojPath) {
    [xml]$csproj = Get-Content $csprojPath
    $targetGroup = $csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1
    $projVersion = if ($targetGroup) { $targetGroup.Version } else { $null }
    
    if ($projVersion -and $entry -and -not $VersionOverride) {
        # Compare against the channel we're publishing to: testing builds bump against
        # TestingAssemblyVersion so testing users are offered the update, production
        # builds against AssemblyVersion.
        $lastPublishedVersion = if ($Channel -eq 'testing' -and $entry.PSObject.Properties['TestingAssemblyVersion'] -and $entry.TestingAssemblyVersion) {
            $entry.TestingAssemblyVersion
        } else {
            $entry.AssemblyVersion
        }
        if ($projVersion -eq $lastPublishedVersion) {
            $parts = $projVersion.Split('.')
            if ($parts.Count -eq 4) {
                $patch = [int]$parts[3] + 1
                $newVersion = "$($parts[0]).$($parts[1]).$($parts[2]).$patch"
                Write-Host "WARNING: Version $projVersion is already registered in pluginmaster.json!" -ForegroundColor Yellow
                Write-Host "Auto-Bumping version in csproj to: $newVersion" -ForegroundColor Green
                
                $targetGroup.Version = $newVersion
                $csproj.Save($csprojPath)
            }
        }
    }
}

# 1. Clean old outputs
Write-Host "Cleaning build folders..."
$cleanPaths = @(
    (Join-Path $srcDir "bin"),
    (Join-Path $srcDir "obj")
)
foreach ($p in $cleanPaths) {
    if (Test-Path $p) {
        Remove-Item -Recurse -Force $p -ErrorAction SilentlyContinue
    }
}

# 2. Build the project
Write-Host "Building project in Release mode..."
$buildCmd = "dotnet build `"$srcDir`" --configuration Release --nologo --verbosity minimal"
Invoke-Expression $buildCmd

# 3. Locate built files
$releaseDir = Join-Path $srcDir "bin\Release"
if (-not (Test-Path $releaseDir)) {
    # Some projects build to net10.0-windows
    $subDirs = Get-ChildItem $releaseDir -Directory -ErrorAction SilentlyContinue
    if ($subDirs) {
        $releaseDir = $subDirs[0].FullName
    }
}

$manifestPath = Join-Path $releaseDir "$PluginName.json"
$dllPath = Join-Path $releaseDir "$PluginName.dll"

if (-not (Test-Path $manifestPath)) {
    throw "Built manifest not found: $manifestPath"
}
if (-not (Test-Path $dllPath)) {
    throw "Built DLL not found: $dllPath"
}

# Read local manifest (explicit UTF-8)
$manifestJsonText = [System.IO.File]::ReadAllText($manifestPath, [System.Text.Encoding]::UTF8)
$manifest = $manifestJsonText | ConvertFrom-Json
$version = if ($VersionOverride) { $VersionOverride } else { $manifest.AssemblyVersion }

if (-not $version) {
    throw "Could not resolve version from manifest."
}

# Setup target folders
$targetChannelName = if ($Channel -eq 'production') { 'latest' } else { 'testing' }
$targetDir = Join-Path $RepoRoot "plugins\$PluginName\$targetChannelName"
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

$zipPath = Join-Path $targetDir "$targetChannelName.zip"
Write-Host "Target Version: $version"
Write-Host "Target Zip: $zipPath"

# 4. Stage and compress payload
$stageDir = Join-Path $env:TEMP "lala-package-stage"
if (Test-Path $stageDir) { Remove-Item -Recurse -Force $stageDir }
New-Item -ItemType Directory -Path $stageDir | Out-Null

# Copy manifest and patch its version (UTF-8 without BOM)
$stagedManifestPath = Join-Path $stageDir "$PluginName.json"
$manifest.AssemblyVersion = $version
$manifestJson = $manifest | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($stagedManifestPath, $manifestJson, [System.Text.UTF8Encoding]::new($false))

# Copy primary DLL and ECommons.dll if present
Copy-Item $dllPath "$stageDir\"
$ecommonsPath = Join-Path $releaseDir "ECommons.dll"
if (Test-Path $ecommonsPath) {
    Copy-Item $ecommonsPath "$stageDir\"
}

# Copy other DLL dependencies (excluding system / dalamud DLLs)
Get-ChildItem $releaseDir -Filter "*.dll" | Where-Object {
    $_.Name -ne "$PluginName.dll" -and 
    $_.Name -ne "ECommons.dll" -and
    $_.Name -notmatch "Dalamud" -and
    $_.Name -notmatch "^Lumina(\.Excel)?\.dll$" -and
    $_.Name -notmatch "Newtonsoft"
} | ForEach-Object {
    Copy-Item $_.FullName "$stageDir\"
}

# Copy runtime resource files (e.g. icon PNGs marked CopyToOutputDirectory in the csproj)
$resDir = Join-Path $releaseDir "Resources"
if (Test-Path $resDir) {
    $resFiles = Get-ChildItem $resDir -File
    if ($resFiles) {
        New-Item -ItemType Directory -Path "$stageDir\Resources" -Force | Out-Null
        $resFiles | ForEach-Object { Copy-Item $_.FullName "$stageDir\Resources\" }
    }
}

# Copy content directories emitted by the build (e.g. Data\, Translations\).
# Plugins that ship runtime content reference it in the csproj as
# <None Include="..\Data\**"> with CopyToOutputDirectory, which lands it in
# bin\Release but NOT in this curated staging dir - so without this pass the zip
# silently ships a plugin whose data files are all missing.
#
# Two directories must never be copied: the folder DalamudPackager writes its own
# output into (named after the plugin), and Resources, staged above.
Get-ChildItem $releaseDir -Directory -ErrorAction SilentlyContinue | Where-Object {
    $_.Name -ne $PluginName -and
    $_.Name -ne "Resources" -and
    $_.Name -notmatch "^(runtimes|ref|refint)$"
} | ForEach-Object {
    Copy-Item $_.FullName "$stageDir\" -Recurse -Force
}

# Loose non-DLL runtime assets (icon.png and friends). The manifest is staged
# separately above; *.deps.json and *.pdb are build artifacts.
Get-ChildItem $releaseDir -File -ErrorAction SilentlyContinue | Where-Object {
    $_.Extension -notin @(".dll", ".pdb") -and
    $_.Name -ne "$PluginName.json" -and
    $_.Name -notlike "*.deps.json"
} | ForEach-Object {
    Copy-Item $_.FullName "$stageDir\" -Force
}

# Zip payload
if (Test-Path $zipPath) { Remove-Item $zipPath }
Compress-Archive -Path "$stageDir\*" -DestinationPath $zipPath -Force

# Copy zip to latest.zip in targetDir as well to guarantee zero stale zip mismatches
$altZipPath = Join-Path $targetDir "latest.zip"
if ($zipPath -ne $altZipPath) {
    Copy-Item $zipPath $altZipPath -Force
}

# Copy manifest to target dir alongside zip
Copy-Item $stagedManifestPath (Join-Path $targetDir "$PluginName.json") -Force

# Clean staging
Remove-Item -Recurse -Force $stageDir

# 5. Synchronize pluginmaster.json automatically
Write-Host "Updating pluginmaster.json index..."

# Parse CHANGELOG.md if present to populate Changelog field in pluginmaster.json
$changelogPath = Join-Path $srcDir "CHANGELOG.md"
if (-not (Test-Path $changelogPath)) {
    # check parent dir for nested projects
    $changelogPath = Join-Path (Split-Path $srcDir) "CHANGELOG.md"
}

$changelogText = $null
if (Test-Path $changelogPath) {
    Write-Host "Parsing CHANGELOG.md for metadata..."
    $lines = Get-Content $changelogPath
    $formattedEntries = [System.Collections.Generic.List[string]]::new()
    $currentEntry = [System.Collections.Generic.List[string]]::new()
    
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("## [")) {
            if ($currentEntry.Count -gt 0) {
                $entryStr = ($currentEntry -join "`n").Trim()
                if ($entryStr) {
                    $formattedEntries.Add($entryStr)
                }
                $currentEntry.Clear()
            }
            if ($trimmed -match '^##\s+\[([^\]]+)\](?:\s*-\s*(.+))?') {
                $ver = $Matches[1]
                $desc = $Matches[2]
                if ($desc) {
                    $currentEntry.Add("v$ver - $desc")
                } else {
                    $currentEntry.Add("v$ver")
                }
            }
        } elseif ($trimmed -like "###*") {
            continue
        } elseif ($trimmed.StartsWith("-") -or $trimmed.StartsWith("**") -or $trimmed -match '^\d+\.') {
            $currentEntry.Add($trimmed)
        } elseif ($trimmed -eq "" -and $currentEntry.Count -gt 0) {
            $currentEntry.Add("")
        }
    }
    if ($currentEntry.Count -gt 0) {
        $entryStr = ($currentEntry -join "`n").Trim()
        if ($entryStr) {
            $formattedEntries.Add($entryStr)
        }
    }
    $changelogText = ($formattedEntries | Select-Object -First 6) -join "`n`n"
}

# Reload pluginmaster.json in case version was modified on disk during prep
$masterJsonText = [System.IO.File]::ReadAllText($masterPath, [System.Text.Encoding]::UTF8)
$masterList = $masterJsonText | ConvertFrom-Json

# Find existing entry or create new one
$entry = $masterList | Where-Object { $_.InternalName -eq $PluginName }
$isNew = $false
if (-not $entry) {
    $isNew = $true
    $entry = [PSCustomObject]@{
        Author = $manifest.Author
        Name = $manifest.Name
        Punchline = $manifest.Punchline
        Description = $manifest.Description
        InternalName = $PluginName
        # Channel separation for a BRAND-NEW plugin (fix 2026-07-30b): a first release on
        # -Channel testing must not stamp the production pointer. Production stays at
        # 0.0.0.0 until an explicit -Channel production run promotes it, and the entry is
        # marked testing-exclusive so Dalamud never offers the not-yet-existing latest.zip.
        AssemblyVersion = $(if ($Channel -eq 'testing') { '0.0.0.0' } else { $version })
        TestingAssemblyVersion = $(if ($Channel -eq 'testing') { $version } else { $null })
        # Dalamud DISCARDS a testing version whose TestingDalamudApiLevel is missing
        # (log: "lacks an associated testing API"), so it must always accompany
        # TestingAssemblyVersion. Bug found 2026-08-01.
        TestingDalamudApiLevel = $(if ($Channel -eq 'testing') { $manifest.DalamudApiLevel } else { $null })
        Changelog = $changelogText
        RepoUrl = "https://github.com/dajoey/lalalazy/tree/main/src/$PluginName"
        ApplicableVersion = "any"
        DalamudApiLevel = $manifest.DalamudApiLevel
        IsHide = $false
        IsTestingExclusive = $(if ($Channel -eq 'testing') { $true } else { $false })
        DownloadLinkInstall = $(if ($Channel -eq 'testing') { "https://raw.githubusercontent.com/dajoey/lalalazy/main/plugins/$PluginName/testing/testing.zip" } else { "https://raw.githubusercontent.com/dajoey/lalalazy/main/plugins/$PluginName/latest/latest.zip" })
        DownloadLinkUpdate = $(if ($Channel -eq 'testing') { "https://raw.githubusercontent.com/dajoey/lalalazy/main/plugins/$PluginName/testing/testing.zip" } else { "https://raw.githubusercontent.com/dajoey/lalalazy/main/plugins/$PluginName/latest/latest.zip" })
        DownloadLinkTesting = "https://raw.githubusercontent.com/dajoey/lalalazy/main/plugins/$PluginName/testing/testing.zip"
        Tags = $manifest.Tags
        CategoryTags = $manifest.CategoryTags
        IconUrl = "https://raw.githubusercontent.com/dajoey/lalalazy/main/LalaImages/$($PluginName.ToLower())-icon.png"
        ImageUrls = @("")
        DownloadCount = [int]0
        LastUpdate = "0"
    }
} else {
    # Update existing fields
    $entry.Author = $manifest.Author
    $entry.Name = $manifest.Name
    $entry.Punchline = $manifest.Punchline
    $entry.Description = $manifest.Description
    $entry.DalamudApiLevel = $manifest.DalamudApiLevel
    $entry.Tags = $manifest.Tags
    $entry.CategoryTags = $manifest.CategoryTags
    $entry.IconUrl = "https://raw.githubusercontent.com/dajoey/lalalazy/main/LalaImages/$($PluginName.ToLower())-icon.png"
    $entry.DownloadLinkInstall = "https://raw.githubusercontent.com/dajoey/lalalazy/main/plugins/$PluginName/latest/latest.zip"
    $entry.DownloadLinkUpdate = "https://raw.githubusercontent.com/dajoey/lalalazy/main/plugins/$PluginName/latest/latest.zip"
    $entry.DownloadLinkTesting = "https://raw.githubusercontent.com/dajoey/lalalazy/main/plugins/$PluginName/testing/testing.zip"
    
    # Channel separation (2026-07-30): testing builds only move TestingAssemblyVersion;
    # the production pointer (AssemblyVersion) moves only on -Channel production, so a
    # test build can never break the production install.
    if ($Channel -eq 'testing') {
        if ($entry.PSObject.Properties['TestingAssemblyVersion']) {
            $entry.TestingAssemblyVersion = $version
        } else {
            $entry | Add-Member -NotePropertyName TestingAssemblyVersion -NotePropertyValue $version
        }
        # Required companion field: without it Dalamud logs "has a testing version
        # available, but it lacks an associated testing API" and drops the testing
        # version entirely, so the build is never offered. Bug found 2026-08-01.
        if ($entry.PSObject.Properties['TestingDalamudApiLevel']) {
            $entry.TestingDalamudApiLevel = $manifest.DalamudApiLevel
        } else {
            $entry | Add-Member -NotePropertyName TestingDalamudApiLevel -NotePropertyValue $manifest.DalamudApiLevel
        }
    } else {
        $entry.AssemblyVersion = $version
        # Promoting to production retires the testing-exclusive flag set by a first
        # testing-only release, so the plugin becomes visible to everyone.
        if ($entry.PSObject.Properties['IsTestingExclusive']) { $entry.IsTestingExclusive = $false }
    }
}

if ($isNew) {
    # Append to master list
    $tempList = [System.Collections.Generic.List[PSCustomObject]]::new()
    foreach ($m in $masterList) {
        $tempList.Add($m)
    }
    $tempList.Add($entry)
    $masterList = $tempList
}

# Save pluginmaster.json back with beautiful formatting (explicit UTF-8 without BOM)
$masterJsonOut = $masterList | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($masterPath, $masterJsonOut, [System.Text.UTF8Encoding]::new($false))

Write-Host "==> SUCCESS: $PluginName packaged and registered in pluginmaster.json!" -ForegroundColor Green
