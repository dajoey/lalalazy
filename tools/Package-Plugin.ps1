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
  [string]$VersionOverride,
  # Re-package the SAME version already registered for this channel. Safe only while that
  # version's zip is unpushed - new bytes under a version Dalamud may have cached is the
  # stale-zip failure mode (see the v1.0.4.130 entry in GluttonyCombo's CHANGELOG).
  [switch]$Republish,
  # Opt in to the old behaviour: bump the csproj patch number and package that.
  [switch]$AutoBump,
  # Link the raw.githubusercontent.com copy instead of publishing a GitHub Release asset.
  # Downloads of such a build are not counted (tools/PackageRelease.ps1).
  [switch]$NoRelease
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
    # Explicit UTF-8: Get-Content on Windows PowerShell 5.1 decodes as the ANSI codepage
    # (Windows-1252 here), so any non-ASCII char in the csproj comes back as mojibake -
    # and -AutoBump writes the document straight back out with $csproj.Save(). Two csprojs
    # already carry non-ASCII (LazyCurrencySpender: U+2014, LazyCrafter: U+00A7).
    [xml]$csproj = [System.IO.File]::ReadAllText($csprojPath, [System.Text.Encoding]::UTF8)
    $targetGroup = $csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1
    $projVersion = if ($targetGroup) { $targetGroup.Version } else { $null }

    # Three-field version contract (2026-09-28, after the partial-bump incident): <Version>,
    # <AssemblyVersion> and <FileVersion> must carry the same value in a release commit
    # (cf. e0d02fd, 5154b127a). The version this script packages comes from the BUILT
    # manifest, whose AssemblyVersion is <AssemblyVersion> when that field is set - so a
    # csproj where only <Version> was bumped makes the run target the OLD version and the
    # GitHub Release upload replaces that old version's live asset. Absent fields are fine
    # (the SDK derives them from <Version>; most plugins here ship that way); fields that
    # are PRESENT and disagree refuse the run before anything is built, zipped or uploaded.
    $asmGroup = $csproj.Project.PropertyGroup | Where-Object { $_.AssemblyVersion } | Select-Object -First 1
    $fileGroup = $csproj.Project.PropertyGroup | Where-Object { $_.FileVersion } | Select-Object -First 1
    $asmVersion = if ($asmGroup) { $asmGroup.AssemblyVersion } else { $null }
    $fileVersion = if ($fileGroup) { $fileGroup.FileVersion } else { $null }
    if ($projVersion -and (($asmVersion -and $asmVersion -ne $projVersion) -or ($fileVersion -and $fileVersion -ne $projVersion))) {
        throw @"
csproj version fields disagree for ${PluginName}:
  <Version>          $projVersion
  <AssemblyVersion>  $(if ($asmVersion) { $asmVersion } else { '(absent: derived from <Version>)' })
  <FileVersion>      $(if ($fileVersion) { $fileVersion } else { '(absent: derived from <Version>)' })
The packaged version comes from the built manifest's AssemblyVersion, i.e. from
<AssemblyVersion> when it is set - a partial bump makes the run target the OLD version and
overwrites that version's live release asset (found live 2026-09-28). Release contract:
<Version>, <AssemblyVersion> and <FileVersion> move together. Set every present field to
the same four-part version in $csprojPath and re-run. No override exists: fixing the
csproj is always the right move.
"@
    }
    
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
            # This used to bump the csproj patch number here, silently, and carry on. That is a
            # trap: a second run of the same release - to correct a manifest, say - rewrote
            # <Version> underneath you and published a version number with no CHANGELOG.md
            # section, breaking the repo's hard changelog rule with nothing but a console line
            # to say so. Nothing gets rewritten unasked now; pick one deliberately.
            $parts = $projVersion.Split('.')
            $suggested = if ($parts.Count -eq 4) {
                "$($parts[0]).$($parts[1]).$($parts[2]).$([int]$parts[3] + 1)"
            } else { '<next version>' }

            if ($AutoBump) {
                if ($parts.Count -ne 4) { throw "-AutoBump needs a four-part version; csproj has '$projVersion'." }
                Write-Host "-AutoBump: $projVersion already published on '$Channel'; csproj -> $suggested." -ForegroundColor Yellow
                Write-Host "           Add a '## v$suggested (yyyy-MM-dd)' section to CHANGELOG.md before committing." -ForegroundColor Yellow
                $targetGroup.Version = $suggested
                # Three-field contract (see the guard above): -AutoBump rewrites <Version>,
                # so any explicit <AssemblyVersion>/<FileVersion> must move with it, or this
                # very run would package under the old version - the incident the guard refuses.
                if ($asmVersion) { $asmGroup.AssemblyVersion = $suggested }
                if ($fileVersion) { $fileGroup.FileVersion = $suggested }
                $csproj.Save($csprojPath)
            }
            elseif ($Republish) {
                Write-Host "-Republish: re-packaging $projVersion, already registered on '$Channel'." -ForegroundColor Yellow
                Write-Host "            Only safe while that zip is UNPUSHED." -ForegroundColor Yellow
            }
            else {
                throw @"
Version $projVersion is already registered as the '$Channel' version in pluginmaster.json.
Refusing to guess. Pick one:
  bump     set <Version> to $suggested in $csprojPath, add the matching '## v$suggested'
           section to CHANGELOG.md, re-run. (-AutoBump edits the csproj for you; the
           CHANGELOG entry is still yours to write.)
  re-cut   re-run with -Republish to package $projVersion again. Only if that version's
           zip has NOT been pushed yet.
"@
            }
        }
    }
}

# Parse CHANGELOG.md into the release changelog text.
# Runs HERE, before the payload is staged, because the SHIPPED manifest inside the zip
# needs this text too - not just pluginmaster.json. Parsing it down in the
# pluginmaster step meant the zip could only ever carry whatever the in-repo manifest
# template happened to say, which is how v1.0.4.130 shipped a production build whose
# embedded changelog read "[testing]".
$changelogPath = Join-Path $srcDir "CHANGELOG.md"
if (-not (Test-Path $changelogPath)) {
    # check parent dir for nested projects
    $changelogPath = Join-Path (Split-Path $srcDir) "CHANGELOG.md"
}

$changelogText = $null
$releaseNotes = $null
if (Test-Path $changelogPath) {
    Write-Host "Parsing CHANGELOG.md for metadata..."
    # Explicit UTF-8, NOT Get-Content (fix 2026-09-05, t_dd984b1e). Under Windows
    # PowerShell 5.1 - which is what runs on this host (5.1.26100.9278) - Get-Content
    # defaults to the ANSI codepage (Windows-1252), so a UTF-8 em dash (U+2014, bytes
    # E2 80 94) decoded to three chars (U+00E2 U+20AC U+201D) and shipped as mojibake into
    # BOTH the pluginmaster.json Changelog AND the zip's <Plugin>.json - i.e. every surface
    # the Dalamud plugin INSTALLER renders. The in-game "What's new" popup is NOT affected:
    # it reads the EMBEDDED CHANGELOG.md resource via ChangelogGate.ReadEmbedded, whose
    # `new StreamReader(stream)` defaults to UTF-8 (measured). So one release shipped the
    # same text correct in the popup and mangled in the installer listing. ReadAllLines
    # splits CRLF and bare LF alike and consumes a BOM if present; this repo has both line
    # endings and no BOMs on CHANGELOG.md.
    $lines = [System.IO.File]::ReadAllLines($changelogPath, [System.Text.Encoding]::UTF8)
    $formattedEntries = [System.Collections.Generic.List[string]]::new()
    $currentEntry = [System.Collections.Generic.List[string]]::new()
    
    foreach ($line in $lines) {
        $trimmed = $line.Trim()

        # Section header. TWO styles live in this repo and both must parse (fix 2026-08-02):
        #   Keep-a-Changelog:  ## [1.0.4.99] - some description
        #   dated:             ## v1.0.4.101 (2026-08-02) [testing]
        # Only the bracket style was recognised before, so every plugin on the dated style
        # (GluttonyCombo, ArmoireAutoFill, DagobertPriceMatcher, LazyFoodBuff,
        # LazyGearCollector, LazyOccultCrescent, LazySkywardTracker) published a Changelog
        # field frozen at whatever it happened to say when the entry was first created.
        $headerVersion = $null
        $headerDesc = $null
        if ($trimmed -match '^##\s+\[([^\]]+)\](?:\s*-\s*(.+))?$') {
            $headerVersion = $Matches[1]
            if ($Matches[2]) { $headerDesc = "- $($Matches[2])" }
        } elseif ($trimmed -match '^##\s+v([0-9][0-9A-Za-z.\-]*)\s*(.*)$') {
            $headerVersion = $Matches[1]
            $headerDesc = $Matches[2].Trim()
        }

        if ($headerVersion) {
            if ($currentEntry.Count -gt 0) {
                $entryStr = ($currentEntry -join "`n").Trim()
                if ($entryStr) {
                    $formattedEntries.Add($entryStr)
                }
                $currentEntry.Clear()
            }
            if ($headerDesc) {
                $currentEntry.Add("v$headerVersion $headerDesc")
            } else {
                $currentEntry.Add("v$headerVersion")
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
    # The newest entry alone is this version's note on its GitHub Release.
    $releaseNotes = $formattedEntries | Select-Object -First 1
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

# The changelog Dalamud shows in-game comes from THIS manifest, not from pluginmaster.json.
# It used to be whatever the in-repo template happened to say, which went stale the moment a
# release did not hand-edit it - shipping the previous version's notes, and once shipping
# "[testing]" on a production build (v1.0.4.130). Write it from CHANGELOG.md, same source the
# index uses, so the two can no longer disagree.
if ($changelogText) {
    if ($manifest.PSObject.Properties['Changelog']) {
        $manifest.Changelog = $changelogText
    } else {
        $manifest | Add-Member -NotePropertyName Changelog -NotePropertyValue $changelogText
    }

    # Keep the tracked template in sync too, so the next build starts correct and the repo
    # never disagrees with what shipped. Rewrite the single Changelog line in place rather
    # than a ConvertFrom/ConvertTo-Json round-trip, which would reformat the whole file and
    # bury the real diff. Line-scan, not a regex: the JSON string escapes its own newlines so
    # the value is always exactly one line, and a pattern that has to match escaped quotes is
    # a backslash-quoting trap in PowerShell (it ate one and threw "Not enough )'s").
    $srcManifestPath = Join-Path $srcDir "$PluginName.json"
    if (Test-Path $srcManifestPath) {
        $srcLines = [System.IO.File]::ReadAllLines($srcManifestPath, [System.Text.Encoding]::UTF8)
        $escaped = $changelogText | ConvertTo-Json
        $found = $false
        for ($i = 0; $i -lt $srcLines.Count; $i++) {
            if ($srcLines[$i] -match '^(\s*)"Changelog"\s*:') {
                $found = $true
                $indent = $Matches[1]
                $comma = if ($srcLines[$i].TrimEnd().EndsWith(',')) { ',' } else { '' }
                $rebuilt = $indent + '"Changelog": ' + $escaped + $comma
                if ($rebuilt -ne $srcLines[$i]) {
                    $srcLines[$i] = $rebuilt
                    [System.IO.File]::WriteAllLines($srcManifestPath, $srcLines, [System.Text.UTF8Encoding]::new($false))
                    Write-Host "Synced Changelog into the source manifest ($PluginName.json)."
                }
                break
            }
        }
        if (-not $found) {
            Write-Host "NOTE: $PluginName.json carries no Changelog field; only the shipped copy has one." -ForegroundColor Yellow
        }
    }
}

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

# 4b. Resolve the release-asset link (2026-09-25: GitHub counts Release-asset downloads,
# tools/PackageRelease.ps1). The URL is deterministic - tag name + asset name - so it can be
# committed into pluginmaster.json BEFORE the asset exists. The publish itself moved to
# step 6 (fix 2026-10-04): the release and its tag are created only at a commit that is
# already on origin/main. Until then the release was published from this unpushed worktree
# with target_commitish 'main', so GitHub resolved 'main' to the remote tip of the moment -
# always the PREVIOUS release's commit - and 81 tags named the wrong release
# (tools/check-release-tags.py is the gate; run it after every release).
$releaseUrl = $null
if ($NoRelease) {
    Write-Host "-NoRelease: linking the raw.githubusercontent.com copy; downloads of this build are not counted." -ForegroundColor Yellow
} else {
    . (Join-Path $PSScriptRoot 'PackageRelease.ps1')
    $releaseUrl = Get-ReleaseAssetUrl -PluginName $PluginName -Version $version -Channel $Channel
    Write-Host "Release asset (published in step 6 at the pushed release commit): $releaseUrl"
}

# 5. Synchronize pluginmaster.json automatically
Write-Host "Updating pluginmaster.json index..."

# $changelogText was parsed above, before staging, so the zip and the index agree.



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
        # Filled in below by Set-EntryDownloadLinks, the same policy as an existing entry.
        DownloadLinkInstall = ''
        DownloadLinkUpdate = ''
        DownloadLinkTesting = ''
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
    # Changelog was assigned only when creating a brand-new entry, so an existing plugin
    # kept the text it was first published with forever (fix 2026-08-02). Refresh it here,
    # but never blank a good field just because the CHANGELOG failed to parse.
    if ($changelogText) {
        if ($entry.PSObject.Properties['Changelog']) {
            $entry.Changelog = $changelogText
        } else {
            $entry | Add-Member -NotePropertyName Changelog -NotePropertyValue $changelogText
        }
    }
    
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

# Link policy lives in PackageLinks.ps1 (tested by tools/tests/Test-PackageLinks.ps1): the
# channel being published points at this build's Release asset (or its raw copy when there
# is none), the other channel's links stay, and a testing-exclusive plugin keeps
# install/update on its testing build (fix 2026-09-21 - these lines used to force
# latest.zip, which 404s before a first promote).
. (Join-Path $PSScriptRoot 'PackageLinks.ps1')
Set-EntryDownloadLinks -Entry $entry -PluginName $PluginName -Channel $Channel -ReleaseUrl $releaseUrl

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

# 6. Release commit, push, then publish (fix 2026-10-04). Everything this run wrote is
#    committed (release-owned paths only), pushed to origin/main, and the pushed SHA is
#    passed to Publish-PluginRelease -TargetCommit so the tag names exactly that commit.
#    A release tag never again lands on an unpushed worktree's notion of 'main'.
function Invoke-GitChecked([string[]]$GitArgs) {
    # PS 5.1 + $ErrorActionPreference='Stop' turns redirected native stderr into a terminating
    # error, and git writes routine progress to stderr (fetch, push, "From ..."): run under
    # 'Continue', merge the streams for the failure message, report via $LASTEXITCODE.
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = @(& git -C $RepoRoot @GitArgs 2>&1)
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $prev }
    if ($code -ne 0) {
        throw "git $($GitArgs -join ' ') failed (exit $code):`n$($out -join "`n")"
    }
    return $out
}
function Invoke-GitRaw([string[]]$GitArgs) {
    # Same stderr escape as Invoke-GitChecked, but returns the exit code instead of throwing,
    # for calls whose failure gets call-site-specific recovery instructions (push).
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = @(& git -C $RepoRoot @GitArgs 2>&1)
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $prev }
    return @{ Code = $code; Lines = @($out); Text = ($out -join "`n") }
}

$repoCheck = Invoke-GitRaw @('rev-parse', '--is-inside-work-tree')
if ($repoCheck.Code -ne 0) { throw "$RepoRoot is not a git work tree; the release-commit step (fix 2026-10-04) requires one." }

# The release commit may sweep only this release's own paths; anything else dirty refuses
# the run instead of being pushed unasked (a multi-plugin release commits every other
# plugin's prep before packaging).
$status = Invoke-GitRaw @('status', '--porcelain')
$dirtyLines = @($status.Lines | Where-Object { $_ })
$allowedPrefixes = @("src/$PluginName/", "plugins/$PluginName/")
$foreignPaths = @()
foreach ($line in $dirtyLines) {
    if ($line.Length -lt 4) { continue }
    foreach ($side in ($line.Substring(3) -split ' -> ')) {
        $p = ($side.Trim().Trim('"') -replace '\\', '/')
        $allowed = ($p -eq 'pluginmaster.json')
        foreach ($a in $allowedPrefixes) { if ($p.StartsWith($a)) { $allowed = $true } }
        if (-not $allowed) { $foreignPaths += $p }
    }
}
if ($foreignPaths.Count -gt 0) {
    $foreignList = ($foreignPaths | Sort-Object -Unique) -join ', '
    throw @"
Refusing to make the release commit: the worktree carries changes outside this release's own
paths: $foreignList
The release commit may sweep only src/$PluginName/, plugins/$PluginName/ and pluginmaster.json,
so unrelated work is never pushed by a packaging run. Commit or stash those paths first (for a
multi-plugin release, commit every other plugin's prep too), then re-run - add -Republish if
$version is already registered for '$Channel'.
"@
}

if ($dirtyLines.Count -gt 0) {
    Invoke-GitChecked @('add', '--', "src/$PluginName", "plugins/$PluginName", 'pluginmaster.json')
    Invoke-GitChecked @('commit', '-m', "release: $PluginName $version ($Channel) [packager]")
    $shortSha = [string](Invoke-GitChecked @('rev-parse', '--short', 'HEAD') | Select-Object -First 1)
    Write-Host "Release commit $shortSha : src/$PluginName, plugins/$PluginName, pluginmaster.json"
}

# Push the release commit; the release's tag will name exactly this SHA (step 6 publish).
$push = Invoke-GitRaw @('push', 'origin', 'HEAD:main')
if ($push.Code -ne 0) {
    $pushText = $push.Text
    throw @"
Push of the release commit to origin/main was rejected - origin/main moved ahead (another
release landed while this one was packaged), so this push is not a fast-forward:
$pushText
Nothing was published to GitHub and the feed on origin is untouched. Recover inside this
worktree:  git fetch origin ; git rebase origin/main
(keep BOTH releases' pluginmaster.json changes when resolving; a textual merge usually
succeeds), then re-run with -PluginName $PluginName -Channel $Channel -Republish.
"@
}
$pushedSha = [string](Invoke-GitChecked @('rev-parse', 'HEAD') | Select-Object -First 1)
$remoteMain = [string]((Invoke-GitChecked @('ls-remote', 'origin', 'refs/heads/main')) -split "`t" | Select-Object -First 1)
if ($remoteMain -ne $pushedSha) {
    throw "origin/main is $remoteMain but the release commit is $pushedSha; refusing to publish the release."
}

if (-not $NoRelease) {
    try {
        $uploadedUrl = Publish-PluginRelease -PluginName $PluginName -Version $version -Channel $Channel `
            -ZipPath $zipPath -DisplayName $manifest.Name -Notes $releaseNotes -TargetCommit $pushedSha -RepoRoot $RepoRoot
        if ($uploadedUrl -ne $releaseUrl) {
            throw "Published asset URL '$uploadedUrl' differs from the link committed into pluginmaster.json ('$releaseUrl')."
        }
        Write-Host "Release asset: $uploadedUrl" -ForegroundColor Green
        Write-Host "Tag $PluginName-v$version -> $pushedSha (on origin/main); verify: python tools/check-release-tags.py" -ForegroundColor Green
    } catch {
        # The release commit is already pushed with the asset link. Fall the feed back to the
        # raw copy (uncounted, but always resolves) and push that fix, so origin never serves
        # a link to an asset that does not exist - the same ship-anyway guarantee the old
        # pre-push publish had.
        Write-Host "WARNING: GitHub Release publish failed: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "         Falling pluginmaster.json back to the raw.githubusercontent.com copy and pushing that:" -ForegroundColor Yellow
        Write-Host "         the build ships, its downloads are not counted." -ForegroundColor Yellow
        $masterList2 = ([System.IO.File]::ReadAllText($masterPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json)
        $entry2 = $masterList2 | Where-Object { $_.InternalName -eq $PluginName }
        Set-EntryDownloadLinks -Entry $entry2 -PluginName $PluginName -Channel $Channel
        [System.IO.File]::WriteAllText($masterPath, ($masterList2 | ConvertTo-Json -Depth 100), [System.Text.UTF8Encoding]::new($false))
        Invoke-GitChecked @('add', '--', 'pluginmaster.json')
        Invoke-GitChecked @('commit', '-m', "fix: $PluginName $version raw download fallback (release publish failed) [packager]")
        $push2 = & git -C $RepoRoot push origin HEAD:main 2>&1
        if ($LASTEXITCODE -ne 0) {
            $push2Text = $push2 -join "`n"
            throw "Raw-fallback push failed after a publish failure: pluginmaster.json on disk points at the raw copy but origin still carries the asset link. Push manually from $RepoRoot. Output:`n$push2Text"
        }
        Write-Host "         Raw fallback pushed. For a counted link, fix the cause and re-run with -Republish." -ForegroundColor Yellow
    }
} else {
    Write-Host "Release commit pushed to origin/main ($pushedSha); the raw link serves once GitHub's CDN refreshes."
}

Write-Host "==> SUCCESS: $PluginName packaged and registered in pluginmaster.json!" -ForegroundColor Green
