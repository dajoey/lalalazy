# Plain assertions (no Pester) for tools/PackageRelease.ps1.
#   powershell -NoProfile -File tools/tests/Test-PackageRelease.ps1         naming contract, offline
#   powershell -NoProfile -File tools/tests/Test-PackageRelease.ps1 -Live   also a real round trip
# -Live publishes a throwaway plugin 'ZzReleaseProbe' (testing, then production of the same
# version, then a -Republish-style replace) to GitHub and deletes the release and its tag at
# the end, pass or fail. It needs the GitHub token (env GITHUB_TOKEN or the Infisical identity).
param([switch] $Live)
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'PackageRelease.ps1')
$fail = 0
function Check($name, $actual, $expected) {
    if ($actual -ceq $expected) { Write-Host "PASS $name" } else { Write-Host "FAIL $name`n  expected $expected`n  actual   $actual"; $script:fail++ }
}

# The naming contract ~/ops/github-traffic-snapshot.py parses on jobunthree.
Check 'tag'              (Get-ReleaseTag 'GluttonyCombo' '1.0.4.235') 'GluttonyCombo-v1.0.4.235'
Check 'production asset' (Get-ReleaseAssetName 'GluttonyCombo' 'production') 'GluttonyCombo.zip'
Check 'testing asset'    (Get-ReleaseAssetName 'GluttonyCombo' 'testing') 'GluttonyCombo-testing.zip'
Check 'asset url'        (Get-ReleaseAssetUrl 'PvPSolver' '0.1.1.1' 'testing') 'https://github.com/dajoey/lalalazy/releases/download/PvPSolver-v0.1.1.1/PvPSolver-testing.zip'

if ($Live) {
    $token = Get-ReleaseGitHubToken
    $api = 'https://api.github.com/repos/dajoey/lalalazy'
    $ver = "0.0.0.$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
    $tag = Get-ReleaseTag 'ZzReleaseProbe' $ver
    $zip = Join-Path $env:TEMP "zz-release-probe-$ver.zip"
    try {
        [System.IO.File]::WriteAllBytes($zip, [byte[]](1..200 | ForEach-Object { $_ % 256 }))
        $u = Publish-PluginRelease -PluginName 'ZzReleaseProbe' -Version $ver -Channel testing -ZipPath $zip -DisplayName 'Release probe' -Notes 'Throwaway, deleted by the test.' -Token $token
        Check 'live testing url' $u (Get-ReleaseAssetUrl 'ZzReleaseProbe' $ver 'testing')
        $rel = Invoke-ReleaseApi -Uri "$api/releases/tags/$tag" -Token $token
        Check 'live testing is pre-release' $rel.prerelease $true

        # Production promote of the same version: second asset on the same release, flag cleared.
        $u = Publish-PluginRelease -PluginName 'ZzReleaseProbe' -Version $ver -Channel production -ZipPath $zip -DisplayName 'Release probe' -Token $token
        Check 'live production url' $u (Get-ReleaseAssetUrl 'ZzReleaseProbe' $ver 'production')
        $rel = Invoke-ReleaseApi -Uri "$api/releases/tags/$tag" -Token $token
        Check 'live promote clears pre-release' $rel.prerelease $false
        # Set comparison: Sort-Object is culture-aware and orders 'X.zip' before 'X-testing.zip'.
        $names = @($rel.assets | ForEach-Object { $_.name })
        Check 'live both assets' (($names.Count -eq 2) -and ($names -contains 'ZzReleaseProbe.zip') -and ($names -contains 'ZzReleaseProbe-testing.zip')) $true
        # make_latest=false: a promoted plugin release must not become the repo's "Latest".
        $latestTag = ''
        try { $latestTag = (Invoke-ReleaseApi -Uri "$api/releases/latest" -Token $token).tag_name }
        catch { if ((Get-ReleaseHttpStatus $_) -ne 404) { throw } }
        Check 'live promote is not Latest' ($latestTag -eq $tag) $false

        # Re-publishing the same channel replaces the asset (the -Republish path) with new bytes.
        [System.IO.File]::WriteAllBytes($zip, [byte[]](1..300 | ForEach-Object { ($_ * 7) % 256 }))
        $null = Publish-PluginRelease -PluginName 'ZzReleaseProbe' -Version $ver -Channel testing -ZipPath $zip -Token $token
        $rel = Invoke-ReleaseApi -Uri "$api/releases/tags/$tag" -Token $token
        $t = @($rel.assets | Where-Object { $_.name -eq 'ZzReleaseProbe-testing.zip' })
        Check 'live replace keeps one asset' $t.Count 1
        Check 'live replace has new bytes' ([string]$t[0].size) '300'

        # The public link resolves without a token, the way Dalamud fetches it (HEAD only).
        $head = Invoke-WebRequest -Method Head -Uri $u -UseBasicParsing
        Check 'live public link resolves' ([int]$head.StatusCode) 200
    }
    finally {
        try {
            $rel = Invoke-ReleaseApi -Uri "$api/releases/tags/$tag" -Token $token
            Invoke-ReleaseApi -Method Delete -Uri "$api/releases/$($rel.id)" -Token $token | Out-Null
        } catch { Write-Host "cleanup: release: $($_.Exception.Message)" }
        try { Invoke-ReleaseApi -Method Delete -Uri "$api/git/refs/tags/$tag" -Token $token | Out-Null }
        catch { Write-Host "cleanup: tag: $($_.Exception.Message)" }
        Remove-Item $zip -ErrorAction SilentlyContinue
        Write-Host "cleanup: deleted release and tag $tag"
    }
}
if ($fail) { Write-Host "$fail FAILED"; exit 1 } else { Write-Host 'ALL PASS' }
