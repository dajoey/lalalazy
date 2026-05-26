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

Write-Host "==> Standardized Packaging: $PluginName ($Channel)" -ForegroundColor Cyan

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
    $_.Name -notmatch "Lumina" -and
    $_.Name -notmatch "Newtonsoft"
} | ForEach-Object {
    Copy-Item $_.FullName "$stageDir\"
}

# Zip payload
if (Test-Path $zipPath) { Remove-Item $zipPath }
Compress-Archive -Path "$stageDir\*" -DestinationPath $zipPath -Force

# Copy manifest to target dir alongside zip
Copy-Item $stagedManifestPath (Join-Path $targetDir "$PluginName.json") -Force

# Clean staging
Remove-Item -Recurse -Force $stageDir

# 5. Synchronize pluginmaster.json automatically
Write-Host "Updating pluginmaster.json index..."
if (-not (Test-Path $masterPath)) {
    throw "pluginmaster.json not found at $masterPath"
}

# Read pluginmaster.json (explicit UTF-8)
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
        AssemblyVersion = $version
        RepoUrl = "https://github.com/dajoey/lalalazy/tree/main/src/$PluginName"
        ApplicableVersion = "any"
        DalamudApiLevel = $manifest.DalamudApiLevel
        IsHide = $false
        IsTestingExclusive = $false
        DownloadLinkInstall = "https://raw.githubusercontent.com/dajoey/lalalazy/main/plugins/$PluginName/latest/latest.zip"
        DownloadLinkUpdate = "https://raw.githubusercontent.com/dajoey/lalalazy/main/plugins/$PluginName/latest/latest.zip"
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
    
    if ($Channel -eq 'production') {
        $entry.AssemblyVersion = $version
    } else {
        $entry.TestingAssemblyVersion = $version
    }
}

if ($isNew) {
    # Append to master list
    $tempList = [System.Collections.Generic.List[PSCustomObject]]::new($masterList)
    $tempList.Add($entry)
    $masterList = $tempList
}

# Save pluginmaster.json back with beautiful formatting (explicit UTF-8 without BOM)
$masterJsonOut = $masterList | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($masterPath, $masterJsonOut, [System.Text.UTF8Encoding]::new($false))

Write-Host "==> SUCCESS: $PluginName packaged and registered in pluginmaster.json!" -ForegroundColor Green
