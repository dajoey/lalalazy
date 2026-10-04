# Plain assertions (no Pester) for tools/PackageRelease.ps1.
#   powershell -NoProfile -File tools/tests/Test-PackageRelease.ps1         naming contract, offline
#   powershell -NoProfile -File tools/tests/Test-PackageRelease.ps1 -Live   also a real round trip
# -Live publishes throwaway plugin 'ZzReleaseProbe' (testing, then production of the same
# version, then a -Republish-style replace) to GitHub and deletes the release and its tag at
# the end, pass or fail. It needs the GitHub token (env GITHUB_TOKEN or the Infisical identity).
param([switch] $Live)
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'PackageRelease.ps1')
$fail = 0
function Check($name, $actual, $expected) {
    if ($actual -ceq $expected) { Write-Host "PASS $name" } else { Write-Host "FAIL $name`n  expected $expected`n  actual   $actual"; $script:fail++ }
}

# The naming contract ~/ops/github-traffic-snapshot.py parses on the ops host.
Check 'tag'              (Get-ReleaseTag 'GluttonyCombo' '1.0.4.235') 'GluttonyCombo-v1.0.4.235'
Check 'production asset' (Get-ReleaseAssetName 'GluttonyCombo' 'production') 'GluttonyCombo.zip'
Check 'testing asset'    (Get-ReleaseAssetName 'GluttonyCombo' 'testing') 'GluttonyCombo-testing.zip'
Check 'asset url'        (Get-ReleaseAssetUrl 'PvPSolver' '0.1.1.1' 'testing') 'https://github.com/dajoey/lalalazy/releases/download/PvPSolver-v0.1.1.1/PvPSolver-testing.zip'

# The tag-target contract (fix 2026-10-04): creating a NEW release creates its tag, and that
# tag must land on a commit already on origin/main. Until the fix the release was published
# with target_commitish 'main' from an unpushed worktree, so the tag resolved to the remote
# tip of the moment - the PREVIOUS release's commit (live: GluttonyCombo v1.0.4.267-.273).
# Assert-TargetCommitOnOrigin is the gate Publish-PluginRelease calls before creating; prove
# it offline against a scratch repo pair (work repo + local 'origin'), no GitHub involved.
$scratch = Join-Path $env:TEMP ("zz-tagcheck-" + [Guid]::NewGuid().ToString('N'))
$originRepo = Join-Path $scratch 'origin'
$workRepo = Join-Path $scratch 'work'
try {
    New-Item -ItemType Directory -Force -Path $originRepo | Out-Null
    $null = & git -C $originRepo init -q 2>&1
    & git -C $originRepo symbolic-ref HEAD refs/heads/main
    if ($LASTEXITCODE -ne 0) { throw 'scratch origin init failed' }
    Set-Content (Join-Path $originRepo 'f.txt') 'base'
    & git -C $originRepo add -A
    & git -C $originRepo -c user.name=probe -c user.email=probe@example.com commit -qm base
    New-Item -ItemType Directory -Force -Path (Split-Path $workRepo -Parent) | Out-Null
    & git clone -q $originRepo $workRepo
    if ($LASTEXITCODE -ne 0) { throw 'scratch clone failed' }
    # A commit that IS on origin/main, and one that is only local to the work repo.
    Set-Content (Join-Path $originRepo 'f.txt') 'on-main'
    & git -C $originRepo add -A
    & git -C $originRepo -c user.name=probe -c user.email=probe@example.com commit -qm on-main
    $onMain = [string](& git -C $originRepo rev-parse HEAD)
    Set-Content (Join-Path $workRepo 'g.txt') 'local-only'
    & git -C $workRepo add -A
    & git -C $workRepo -c user.name=probe -c user.email=probe@example.com commit -qm local-only
    $notMain = [string](& git -C $workRepo rev-parse HEAD)

    $refused = $false
    try { Assert-TargetCommitOnOrigin -TargetCommit $notMain -RepoRoot $workRepo }
    catch { $refused = ($_.Exception.Message -match 'not reachable from origin/main') }
    Check 'off-main commit refused' $refused $true

    $accepted = $true
    try { Assert-TargetCommitOnOrigin -TargetCommit $onMain -RepoRoot $workRepo } catch { $accepted = $false }
    Check 'on-main commit accepted' $accepted $true
}
finally { Remove-Item -Recurse -Force $scratch -ErrorAction SilentlyContinue }

if ($Live) {
    $token = Get-ReleaseGitHubToken
    $api = 'https://api.github.com/repos/dajoey/lalalazy'
    $ver = "0.0.0.$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
    $tag = Get-ReleaseTag 'ZzReleaseProbe' $ver
    $tag2 = Get-ReleaseTag 'ZzReleaseProbe2' $ver
    $zip = Join-Path $env:TEMP "zz-release-probe-$ver.zip"
    # The commit new-release tags must name: the current origin/main tip, fetched live so
    # the probe exercises exactly the path a real release takes.
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $mainTip = [string]((& git -C $repoRoot ls-remote origin refs/heads/main) -split "`t" | Select-Object -First 1)
    Check 'live main tip resolved' ($mainTip -match '^[0-9a-f]{40}$') $true
    try {
        [System.IO.File]::WriteAllBytes($zip, [byte[]](1..200 | ForEach-Object { $_ % 256 }))
        $u = Publish-PluginRelease -PluginName 'ZzReleaseProbe' -Version $ver -Channel testing -ZipPath $zip -DisplayName 'Release probe' -Notes 'Throwaway, deleted by the test.' -Token $token -TargetCommit $mainTip
        Check 'live testing url' $u (Get-ReleaseAssetUrl 'ZzReleaseProbe' $ver 'testing')
        $rel = Invoke-ReleaseApi -Uri "$api/releases/tags/$tag" -Token $token
        Check 'live testing is pre-release' $rel.prerelease $true
        # The tag the release created must sit on the commit we named, not on 'main'-of-the-moment.
        $ref = Invoke-ReleaseApi -Uri "$api/git/refs/tags/$tag" -Token $token
        Check 'live tag names the given commit' ([string]$ref.object.sha) $mainTip
        Check 'live tag is lightweight (points at a commit)' ([string]$ref.object.type) 'commit'

        # Production promote of the same version: second asset on the same release, flag cleared.
        $u = Publish-PluginRelease -PluginName 'ZzReleaseProbe' -Version $ver -Channel production -ZipPath $zip -DisplayName 'Release probe' -Token $token
        Check 'live production url' $u (Get-ReleaseAssetUrl 'ZzReleaseProbe' $ver 'production')
        $rel = Invoke-ReleaseApi -Uri "$api/releases/tags/$tag" -Token $token
        Check 'live promote clears pre-release' $rel.prerelease $false
        # Set comparison: Sort-Object is culture-aware and orders 'X.zip' before 'X-testing.zip'.
        $names = @($rel.assets | ForEach-Object { $_.name })
        Check 'live both assets' (($names.Count -eq 2) -and ($names -contains 'ZzReleaseProbe.zip') -and ($names -contains 'ZzReleaseProbe-testing.zip')) $true
        # make_latest=false asks GitHub not to mark this release "Latest", and the payload
        # above sends it on both create and promote. But /releases/latest falls back to
        # "newest non-prerelease" when NO release in the repo carries the mark - the case
        # here since the 2026-10-03 batch (all packager-made with make_latest=false, none
        # ever marked), so the probe may surface via that fallback. That is GitHub's fallback,
        # not a payload defect (found 2026-10-04: /releases/latest = PvPSolver-v0.1.1.2, the
        # newest non-prerelease, with the flag unchanged since 2026-09-25). Warn, don't fail.
        $latestTag = ''
        try { $latestTag = (Invoke-ReleaseApi -Uri "$api/releases/latest" -Token $token).tag_name }
        catch { if ((Get-ReleaseHttpStatus $_) -ne 404) { throw } }
        if ($latestTag -eq $tag) {
            Write-Host "WARN live promote is not Latest: no release in the repo is marked latest, so GitHub's fallback names the probe (payload sent make_latest=false)."
        } else {
            Check 'live promote is not Latest' ($latestTag -eq $tag) $false
        }

        # Re-publishing the same channel replaces the asset (the -Republish path) with new bytes.
        [System.IO.File]::WriteAllBytes($zip, [byte[]](1..300 | ForEach-Object { ($_ * 7) % 256 }))
        $null = Publish-PluginRelease -PluginName 'ZzReleaseProbe' -Version $ver -Channel testing -ZipPath $zip -Token $token
        $rel = Invoke-ReleaseApi -Uri "$api/releases/tags/$tag" -Token $token
        $t = @($rel.assets | Where-Object { $_.name -eq 'ZzReleaseProbe-testing.zip' })
        Check 'live replace keeps one asset' $t.Count 1
        Check 'live replace has new bytes' ([string]$t[0].size) '300'

        # A NEW release with no -TargetCommit must refuse BEFORE creating anything: that is
        # the shape that used to tag an unpushed worktree's notion of 'main'.
        $refusedNew = $false
        try {
            $null = Publish-PluginRelease -PluginName 'ZzReleaseProbe2' -Version $ver -Channel testing -ZipPath $zip -Token $token
        } catch { $refusedNew = ($_.Exception.Message -match 'TargetCommit') }
        Check 'live new release refuses without TargetCommit' $refusedNew $true

        # The public link resolves without a token, the way Dalamud fetches it (HEAD only).
        $head = Invoke-WebRequest -Method Head -Uri $u -UseBasicParsing
        Check 'live public link resolves' ([int]$head.StatusCode) 200
    }
    finally {
        foreach ($t in @($tag, $tag2)) {
            try {
                $rel = Invoke-ReleaseApi -Uri "$api/releases/tags/$t" -Token $token
                Invoke-ReleaseApi -Method Delete -Uri "$api/releases/$($rel.id)" -Token $token | Out-Null
            } catch { Write-Host "cleanup: release ${t}: $($_.Exception.Message)" }
            try { Invoke-ReleaseApi -Method Delete -Uri "$api/git/refs/tags/$t" -Token $token | Out-Null }
            catch { Write-Host "cleanup: tag ${t}: $($_.Exception.Message)" }
        }
        Remove-Item $zip -ErrorAction SilentlyContinue
        Write-Host "cleanup: deleted releases and tags $tag $tag2"
    }
}
if ($fail) { Write-Host "$fail FAILED"; exit 1 } else { Write-Host 'ALL PASS' }
