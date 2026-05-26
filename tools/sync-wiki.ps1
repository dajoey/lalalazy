# sync-wiki.ps1
# Automates updating the GitHub Wiki pages with release information, version numbers, download links, and changelogs from pluginmaster.json.

$ErrorActionPreference = "Stop"

# Path configuration
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoPath = Split-Path -Parent $scriptPath
$wikiPath = Join-Path (Split-Path -Parent $repoPath) "lalalazy.wiki"
$manifestPath = Join-Path $repoPath "pluginmaster.json"

Write-Host "Syncing lalalazy Wiki..." -ForegroundColor Cyan
Write-Host "Repository Path: $repoPath" -ForegroundColor DarkGray
Write-Host "Wiki Path: $wikiPath" -ForegroundColor DarkGray
Write-Host "Manifest Path: $manifestPath" -ForegroundColor DarkGray

if (-not (Test-Path $manifestPath)) {
    throw "Manifest file not found at $manifestPath"
}
if (-not (Test-Path $wikiPath)) {
    throw "Wiki folder not found at $wikiPath. Please ensure the wiki repo is cloned next to the lalalazy repo."
}

# Parse manifest
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

# Mapping of InternalName to Wiki File Name
$nameMapping = @{
    "PvPSolver"            = "PvP-Solver.md"
    "DagobertPriceMatcher" = "Dagobert-Price-Matcher.md"
    "AutoPotion"           = "AutoPotion.md"
    "ArmoireAutoFill"      = "Armoire-Auto-Fill.md"
    "GluttonyCombo"        = "Gluttony-Combo.md"
    "LazyWTMath"           = "Lazy-WT-Math.md"
    "LazySightseeing"      = "Lazy-Sightseeing.md"
    "LazyCurrencySpender"  = "Lazy-Currency-Spender.md"
}

foreach ($plugin in $manifest) {
    $internalName = $plugin.InternalName
    if (-not $nameMapping.ContainsKey($internalName)) {
        Write-Warning "No wiki mapping configured for plugin: $internalName"
        continue
    }

    $wikiFile = $nameMapping[$internalName]
    $wikiFilePath = Join-Path $wikiPath $wikiFile

    $version = $plugin.AssemblyVersion
    if ($plugin.TestingAssemblyVersion -and $plugin.IsTestingExclusive) {
        $version = $plugin.TestingAssemblyVersion
    }
    
    $changelog = $plugin.Changelog
    if (-not $changelog) {
        $changelog = "No recent changelog documented."
    }

    # Format the release info block
    $releaseBlock = @(
        "<!-- RELEASE_INFO_START -->",
        "## Release Information",
        "",
        "| Attribute | Value |",
        "|---|---|",
        "| **Latest Version** | ``v$version`` |",
        "| **Dalamud API** | ``API $($plugin.DalamudApiLevel)`` |",
        "| **Punchline** | $($plugin.Punchline) |",
        "| **Direct Install Zip** | [Download Zip]($($plugin.DownloadLinkInstall)) |",
        "",
        "### Recent Changelog",
        '```text',
        $changelog.Trim(),
        '```',
        "<!-- RELEASE_INFO_END -->"
    ) -join "`r`n"

    if (Test-Path $wikiFilePath) {
        $content = Get-Content $wikiFilePath -Raw
        $pattern = "(?s)<!-- RELEASE_INFO_START -->.*?<!-- RELEASE_INFO_END -->"

        if ($content -match $pattern) {
            # Replace existing block
            $newContent = $content -replace $pattern, $releaseBlock
            Write-Host "Updating release info in $wikiFile" -ForegroundColor Yellow
        } else {
            # Prepend block if markers not found
            $newContent = $releaseBlock + "`r`n`r`n" + $content
            Write-Host "Prepending release info to $wikiFile" -ForegroundColor Green
        }
        Set-Content -Path $wikiFilePath -Value $newContent -NoNewline
    } else {
        # Create new stub file if it doesn't exist
        $initialContent = @(
            "# $($plugin.Name)",
            "",
            $plugin.Description,
            "",
            $releaseBlock,
            "",
            "## How to Use",
            "",
            "Detailed usage instructions for $($plugin.Name) will go here.",
            "",
            "### Commands",
            "- None documented yet."
        ) -join "`r`n"
        
        Write-Host "Creating new wiki page: $wikiFile" -ForegroundColor Green
        Set-Content -Path $wikiFilePath -Value $initialContent -NoNewline
    }
}

Write-Host "Wiki Sync Completed Successfully!" -ForegroundColor Green
