# Plain assertions (no Pester) for the release-commit guard's sealed pre-dirt exemption in
# tools/Package-Plugin.ps1. nightly-gates.sh preflight seals the worktree's pre-existing
# dirt verbatim (`git status --porcelain` lines) in .lalalazy-sync\_run-state\pre-dirty.txt;
# the guard must let exactly those paths stay uncommitted instead of refusing the release,
# while any NON-sealed foreign path still refuses and sealed paths stay out of the commit.
# Runs the REAL packager against a scratch git repo (stubbed dotnet, bare local origin,
# -NoRelease), so the guard is exercised end-to-end: release commit made and pushed to the
# bare origin, or refused before any commit. No network, no writes outside the scratch dirs
# (the seal override LALA_PREDIRT_SEAL points at a sibling dir - never inside the worktree,
# where it would itself be foreign dirt).
# Run: powershell -NoProfile -File tools/tests/Test-PackagePredirtSeal.ps1  (no pwsh on DAJOEYROG)
$ErrorActionPreference = 'Stop'

$repoTools = Split-Path $PSScriptRoot -Parent
$realScript = Join-Path $repoTools 'Package-Plugin.ps1'
$realLinks  = Join-Path $repoTools 'PackageLinks.ps1'
$fail = 0
function Check($name, $actual, $expected) {
    if ("$actual" -ceq "$expected") { Write-Host "PASS $name" } else { Write-Host "FAIL $name`n  expected $expected`n  actual   $actual"; $script:fail++ }
}
function CheckTrue($name, $cond) {
    if ($cond) { Write-Host "PASS $name" } else { Write-Host "FAIL $name"; $script:fail++ }
}

# Scratch layout, rebuilt per case so git state never leaks between cases:
#   <scratch>\                      the worktree (main, one initial commit, bare origin)
#   <scratch>-origin.git\           bare remote the packager can really push to (sibling)
#   <scratch>\stub\dotnet.cmd       fake dotnet: writes the built manifest + dll, builds nothing
#   <scratch>-seal\pre-dirty.txt    the gates' seal, OUTSIDE the worktree on purpose
$scratchList = @()
function New-ScratchRepo {
    $scratch = Join-Path ([IO.Path]::GetTempPath()) ("lala-predirt-test-" + [IO.Path]::GetRandomFileName())
    $script:scratchList += $scratch
    New-Item -ItemType Directory -Force -Path (Join-Path $scratch 'tools\tests'), (Join-Path $scratch 'src\GuardProbe'), (Join-Path $scratch 'stub') | Out-Null
    Copy-Item $realScript (Join-Path $scratch 'tools\Package-Plugin.ps1')
    Copy-Item $realLinks (Join-Path $scratch 'tools\PackageLinks.ps1')

    # tools/ and stub/ are ignored: only release-owned paths may ever be dirty here.
    [IO.File]::WriteAllText((Join-Path $scratch '.gitignore'), ".vs/`nbin/`nobj/`nlib/`nstub/`ntools/`n", [Text.UTF8Encoding]::new($false))
    # Two entries so the master list stays a JSON array through the packager's round-trip.
    # Every field the packager assigns on an existing entry must be present (a PSCustomObject
    # from ConvertFrom-Json refuses plain assignment to a property it does not have - real
    # pluginmaster entries always carry these).
    [IO.File]::WriteAllText((Join-Path $scratch 'pluginmaster.json'),
        '[' +
        '{"InternalName":"OtherPlugin","AssemblyVersion":"0.0.1.0","Author":"t","Name":"OtherPlugin","Punchline":"p","Description":"d","DalamudApiLevel":10,"Tags":[],"CategoryTags":[],"IconUrl":"","DownloadLinkInstall":"","DownloadLinkUpdate":"","DownloadLinkTesting":""},' +
        '{"InternalName":"GuardProbe","AssemblyVersion":"0.0.1.0","Author":"t","Name":"GuardProbe","Punchline":"p","Description":"d","DalamudApiLevel":10,"Tags":[],"CategoryTags":[],"IconUrl":"","DownloadLinkInstall":"","DownloadLinkUpdate":"","DownloadLinkTesting":""}]',
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $scratch 'README.md'), "# scratch`n", [Text.UTF8Encoding]::new($false))
    # A tracked file under plugins/ keeps git from collapsing the whole untracked plugins/
    # tree to '?? plugins/' in porcelain (real repos always carry tracked release dirs).
    New-Item -ItemType Directory -Force -Path (Join-Path $scratch 'plugins\OtherPlugin\latest') | Out-Null
    [IO.File]::WriteAllText((Join-Path $scratch 'plugins\OtherPlugin\latest\OtherPlugin.json'), '{"AssemblyVersion":"0.0.1.0"}', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $scratch 'src\GuardProbe\GuardProbe.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Version>0.0.1.0</Version>
  </PropertyGroup>
</Project>
"@, [Text.UTF8Encoding]::new($false))

    git -C $scratch init | Out-Null
    git -C $scratch config user.name 'Predirt Seal Test'
    git -C $scratch config user.email 'predirt-seal-test@example.invalid'
    git -C $scratch config core.autocrlf false
    git -C $scratch add -- .gitignore pluginmaster.json README.md src/GuardProbe/GuardProbe.csproj plugins/OtherPlugin/latest/OtherPlugin.json
    if ($LASTEXITCODE -ne 0) { throw "scratch git add failed" }
    git -C $scratch commit -q -m 'initial'
    if ($LASTEXITCODE -ne 0) { throw "scratch git commit failed" }
    git -C $scratch branch -M main
    # Bare origin as a SIBLING of the worktree - inside it, it would be foreign dirt itself.
    $origin = "$scratch-origin.git"
    git init --bare --quiet $origin
    if ($LASTEXITCODE -ne 0) { throw "scratch bare origin failed" }
    git -C $scratch remote add origin $origin
    git -C $scratch push --quiet origin main
    if ($LASTEXITCODE -ne 0) { throw "scratch initial push failed" }

    # The release shape under test: version bumped in the worktree, uncommitted (the
    # release commit sweeps it), while pluginmaster still registers 0.0.1.0.
    [IO.File]::WriteAllText((Join-Path $scratch 'src\GuardProbe\GuardProbe.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Version>0.0.2.0</Version>
  </PropertyGroup>
</Project>
"@, [Text.UTF8Encoding]::new($false))

    # dotnet stub: creates the built manifest + dll the packager needs, builds nothing.
    $stubPs1 = Join-Path $scratch 'stub\dotnet-build.ps1'
    [IO.File]::WriteAllText($stubPs1, @"
`$out = Join-Path '$scratch' 'src\GuardProbe\bin\Release'
New-Item -ItemType Directory -Force -Path `$out | Out-Null
`$m = @{ AssemblyVersion = '0.0.2.0'; Author = 'Test Author'; Name = 'GuardProbe'; Punchline = 'probe'; Description = 'probe'; DalamudApiLevel = 10 }
[IO.File]::WriteAllText((Join-Path `$out 'GuardProbe.json'), (`$m | ConvertTo-Json), [Text.UTF8Encoding]::new(`$false))
Set-Content -Path (Join-Path `$out 'GuardProbe.dll') -Value 'stub dll'
"@)
    [IO.File]::WriteAllText((Join-Path $scratch 'stub\dotnet.cmd'), "@echo off`r`npowershell -NoProfile -ExecutionPolicy Bypass -File `"$stubPs1`"`r`nexit /b %ERRORLEVEL%`r`n")
    # The stub wins command resolution: prepended, so `dotnet build` inside the packager
    # invokes it instead of the real SDK (same trick as Test-PackageVersionGuard.ps1).
    $env:Path = "$(Join-Path $scratch 'stub');$env:Path"
    return $scratch
}

# Invoke the REAL packager in the scratch repo. $SealLines: array of raw porcelain lines to
# write into the sibling seal file, $null for no seal file at all, empty array for an empty
# (zero-byte) seal. $Foreign: untracked files to drop in the repo root.
function Invoke-Packager([string]$Scratch, $SealLines, [string[]]$Foreign) {
    foreach ($f in $Foreign) { Set-Content -Path (Join-Path $Scratch $f) -Value "foreign dirt: $f" }
    $sealPath = "$Scratch-seal\pre-dirty.txt"   # SIBLING dir: outside the worktree, or it would be foreign dirt itself
    if ($null -ne $SealLines) {
        New-Item -ItemType Directory -Force -Path (Split-Path $sealPath -Parent) | Out-Null
        if ($SealLines.Count -gt 0) {
            [IO.File]::WriteAllLines($sealPath, $SealLines, [Text.UTF8Encoding]::new($false))
        } else {
            [IO.File]::WriteAllText($sealPath, '', [Text.UTF8Encoding]::new($false))
        }
    }
    # Point the packager at a seal that does not exist when $SealLines is $null.
    $env:LALA_PREDIRT_SEAL = if ($null -eq $SealLines) { "$Scratch-no-such-run-state\pre-dirty.txt" } else { $sealPath }
    $script:caught = $null
    try { & (Join-Path $Scratch 'tools\Package-Plugin.ps1') -PluginName GuardProbe -Channel testing -NoRelease } catch { $script:caught = $_ }
}

Write-Host '=== Case A: sealed untracked pre-dirt does not block the release commit ==='
$s = New-ScratchRepo
Invoke-Packager -Scratch $s -SealLines @('?? h2acc.txt', '?? h2e2e.txt') -Foreign @('h2acc.txt', 'h2e2e.txt')
CheckTrue 'A: run completed without refusal' ($null -eq $script:caught)
if ($script:caught) { Write-Host "  error: $($script:caught.Exception.Message)" }
Check 'A: pushed: origin/main == HEAD' `
    "$(git -C $s rev-parse HEAD) $(git -C $s rev-parse origin/main)" `
    "$(git -C $s rev-parse HEAD) $(git -C $s rev-parse HEAD)"
$commitFiles = @(git -C $s diff-tree --no-commit-id --name-only -r HEAD)
CheckTrue 'A: sealed paths absent from the release commit' `
    (-not ($commitFiles | Where-Object { $_ -match '^h2(e2e|acc)\.txt$' }))
CheckTrue 'A: release commit carries pluginmaster.json' ($commitFiles -contains 'pluginmaster.json')
CheckTrue 'A: release commit sweeps the release src' ($commitFiles -contains 'src/GuardProbe/GuardProbe.csproj')
Check 'A: commit message is the packager release commit' `
    $(if ((git -C $s log -1 --format=%s) -match '^release: GuardProbe 0\.0\.2\.0 \(testing\) \[packager\]$') { 'True' } else { 'False' }) 'True'
CheckTrue 'A: sealed file still on disk' (Test-Path (Join-Path $s 'h2acc.txt'))
Check 'A: sealed file still untracked' `
    $(if ((git -C $s status --porcelain -- h2acc.txt) -match '^\?\? h2acc\.txt$') { 'True' } else { 'False' }) 'True'

Write-Host '=== Case B: a NON-sealed foreign path still refuses (same message) ==='
$s = New-ScratchRepo
$initialSha = git -C $s rev-parse HEAD
Invoke-Packager -Scratch $s -SealLines @('?? h2acc.txt') -Foreign @('stray.txt')
$msg = if ($script:caught) { $script:caught.Exception.Message } else { '' }
CheckTrue 'B: run refused'        ($msg -match 'Refusing to make the release commit')
CheckTrue 'B: refusal names the worktree'  ($msg -match "outside this release's own")
CheckTrue 'B: refusal names stray.txt'     ($msg -match 'stray\.txt')
CheckTrue 'B: refusal names ONLY the unsealed path' (-not ($msg -match 'h2acc'))
CheckTrue 'B: refusal precedes any commit' ($msg -notmatch 'Push of the release commit')
Check 'B: origin/main untouched' "$(git -C $s rev-parse origin/main)" $initialSha
Check 'B: no release commit made' "$(git -C $s rev-list --count HEAD)" '1'

Write-Host '=== Case C: absent seal file still refuses (tolerates absence) ==='
$s = New-ScratchRepo
Invoke-Packager -Scratch $s -SealLines $null -Foreign @('stray.txt')
$msg = if ($script:caught) { $script:caught.Exception.Message } else { '' }
CheckTrue 'C: run refused' ($msg -match 'Refusing to make the release commit')
CheckTrue 'C: refusal names stray.txt' ($msg -match 'stray\.txt')

Write-Host '=== Case D: empty (zero-byte) seal file still refuses ==='
$s = New-ScratchRepo
Invoke-Packager -Scratch $s -SealLines @() -Foreign @('stray.txt')
$msg = if ($script:caught) { $script:caught.Exception.Message } else { '' }
CheckTrue 'D: run refused' ($msg -match 'Refusing to make the release commit')
CheckTrue 'D: refusal names stray.txt' ($msg -match 'stray\.txt')

Write-Host '=== Case E: mixed - sealed path exempt, unsealed path named ==='
$s = New-ScratchRepo
Invoke-Packager -Scratch $s -SealLines @('?? h2acc.txt') -Foreign @('h2acc.txt', 'stray.txt')
$msg = if ($script:caught) { $script:caught.Exception.Message } else { '' }
CheckTrue 'E: run refused' ($msg -match 'Refusing to make the release commit')
CheckTrue 'E: refusal names stray.txt' ($msg -match 'stray\.txt')
CheckTrue 'E: sealed path not named'   (-not ($msg -match 'h2acc'))

Remove-Item Env:\LALA_PREDIRT_SEAL -ErrorAction SilentlyContinue
foreach ($s in $scratchList) {
    Remove-Item -Recurse -Force @($s, "$s-origin.git", "$s-seal", "$s-no-such-run-state") -ErrorAction SilentlyContinue
}
if ($fail) { Write-Host "$fail FAILED"; exit 1 } else { Write-Host 'ALL PASS' }
