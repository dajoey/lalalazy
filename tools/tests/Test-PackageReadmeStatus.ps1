# Plain assertions (no Pester) for the README status step of tools/Package-Plugin.ps1's release
# commit. Runs the packager's own text (its git helpers and the README step through the release
# commit, cut out of the real script) against a scratch git repo: no build, no network, no push.
# Run: powershell -NoProfile -File tools/tests/Test-PackageReadmeStatus.ps1   (or pwsh)
$ErrorActionPreference = 'Stop'

$toolsDir   = Split-Path $PSScriptRoot -Parent
$realScript = Join-Path $toolsDir 'Package-Plugin.ps1'
$text       = [IO.File]::ReadAllText($realScript)

# The packager's git helpers, by AST (the same definitions the release step calls).
$ast = [System.Management.Automation.Language.Parser]::ParseFile($realScript, [ref]$null, [ref]$null)
$helpers = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                          $n.Name -in @('Invoke-GitChecked', 'Invoke-GitRaw') }, $true)
if (@($helpers).Count -ne 2) { throw "expected Invoke-GitChecked and Invoke-GitRaw in Package-Plugin.ps1" }
foreach ($h in $helpers) { . ([scriptblock]::Create($h.Extent.Text)) }

# The README step through the release commit.
$start = $text.IndexOf("# The README plugin table's Status column follows the feed")
$end   = $text.IndexOf("# Push the release commit;")
if ($start -lt 0 -or $end -le $start) { throw "README step markers not found in Package-Plugin.ps1" }
$releaseStep = [scriptblock]::Create($text.Substring($start, $end - $start))

$failures = 0
function Assert($cond, [string]$what) {
    if ($cond) { Write-Host "ok   $what" } else { Write-Host "FAIL $what" -ForegroundColor Red; $script:failures++ }
}

$utf8 = [Text.UTF8Encoding]::new($false)
$readme = "# t`n`n## Plugins`n`n| Plugin | Description | Status |`n|---|---|---|`n| **Probe Plugin** | probe | Testing |`n"
function New-ScratchRepo {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ("lala-readme-test-" + [IO.Path]::GetRandomFileName())
    New-Item -ItemType Directory -Force -Path (Join-Path $dir 'tools'), (Join-Path $dir 'src/Probe') | Out-Null
    Copy-Item (Join-Path $toolsDir 'readme_status.py') (Join-Path $dir 'tools/readme_status.py')
    [IO.File]::WriteAllText((Join-Path $dir 'README.md'), $readme, $utf8)
    [IO.File]::WriteAllText((Join-Path $dir 'pluginmaster.json'),
        '[{"Name":"Probe Plugin","InternalName":"Probe","AssemblyVersion":"0.0.0.0","TestingAssemblyVersion":"0.1.0.0"}]', $utf8)
    [IO.File]::WriteAllText((Join-Path $dir 'src/Probe/a.txt'), "a", $utf8)
    & git -C $dir init -q -b main 2>&1 | Out-Null
    & git -C $dir -c user.name=t -c user.email=t@t add -A 2>&1 | Out-Null
    & git -C $dir -c user.name=t -c user.email=t@t commit -q -m init 2>&1 | Out-Null
    & git -C $dir config user.name t
    & git -C $dir config user.email t@t
    return $dir
}

function Invoke-ReleaseStep([string]$repo) {
    $script:RepoRoot = $repo
    $script:PluginName = 'Probe'
    $script:version = '0.1.0.0'
    $script:Channel = 'production'
    $status = Invoke-GitRaw @('status', '--porcelain')
    $script:dirtyLines = @($status.Lines | Where-Object { $_ })
    . $releaseStep
}

# 1. A production release: the feed changes, the README row follows it inside the same commit.
$repo = New-ScratchRepo
try {
    [IO.File]::WriteAllText((Join-Path $repo 'pluginmaster.json'),
        '[{"Name":"Probe Plugin","InternalName":"Probe","AssemblyVersion":"0.1.0.0","TestingAssemblyVersion":"0.1.0.0"}]', $utf8)
    [IO.File]::WriteAllText((Join-Path $repo 'src/Probe/a.txt'), "b", $utf8)
    Invoke-ReleaseStep $repo
    $files = @(& git -C $repo show --name-only --format= HEAD)
    Assert ($files -contains 'README.md') "release commit carries README.md"
    Assert ($files -contains 'pluginmaster.json') "release commit carries pluginmaster.json"
    $msg = (& git -C $repo log -1 --format=%s)
    Assert ($msg -eq 'release: Probe 0.1.0.0 (production) [packager]') "release commit message unchanged ($msg)"
    $body = [IO.File]::ReadAllText((Join-Path $repo 'README.md'))
    Assert ($body.Contains('| **Probe Plugin** | probe | Production 0.1.0.0 |')) "README row reads Production 0.1.0.0"
    Assert (-not (& git -C $repo status --porcelain)) "worktree clean after the release commit"
} finally { Remove-Item -Recurse -Force $repo }

# 2. A release that does not change the README status: README is not part of the commit.
$repo = New-ScratchRepo
try {
    & python3 (Join-Path $repo 'tools/readme_status.py') --repo $repo 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { & py -3 (Join-Path $repo 'tools/readme_status.py') --repo $repo 2>&1 | Out-Null }
    & git -C $repo commit -q -am "current readme" 2>&1 | Out-Null
    [IO.File]::WriteAllText((Join-Path $repo 'src/Probe/a.txt'), "c", $utf8)
    Invoke-ReleaseStep $repo
    $files = @(& git -C $repo show --name-only --format= HEAD)
    Assert (-not ($files -contains 'README.md')) "an unchanged README stays out of the release commit"
    Assert ($files -contains 'src/Probe/a.txt') "release paths still committed"
} finally { Remove-Item -Recurse -Force $repo }

if ($failures) { Write-Host "$failures failure(s)" -ForegroundColor Red; exit 1 }
Write-Host "all README status packager checks passed"
exit 0
