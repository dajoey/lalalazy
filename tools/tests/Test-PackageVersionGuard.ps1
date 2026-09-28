# Plain assertions (no Pester) for the three-field version guard in tools/Package-Plugin.ps1
# (2026-09-28, after the partial-bump incident where a <Version>-only bump made the packager
# target the OLD version and overwrite that version's live GitHub Release asset).
# Runs the REAL packager against a scratch repo layout with a stubbed dotnet, so the guard is
# exercised end-to-end: no build, no network, no writes outside the scratch dir. Pass-through
# cases prove the run got PAST the guard by failing later at the built-manifest check.
# Run: powershell -NoProfile -File tools/tests/Test-PackageVersionGuard.ps1  (no pwsh on DAJOEYROG)
$ErrorActionPreference = 'Stop'

$realScript = Join-Path (Split-Path $PSScriptRoot -Parent) 'Package-Plugin.ps1'
$scratch    = Join-Path ([IO.Path]::GetTempPath()) ("lala-guard-test-" + [IO.Path]::GetRandomFileName())
$stubDir    = Join-Path $scratch 'stub'
New-Item -ItemType Directory -Force -Path (Join-Path $scratch 'tools\tests'), (Join-Path $scratch 'src\GuardProbe'), $stubDir | Out-Null
Copy-Item $realScript (Join-Path $scratch 'tools\Package-Plugin.ps1')

# dotnet stub: the pass-through cases only need to prove the guard let them past it; the run
# then fails at the built-manifest check, offline and instantly.
[IO.File]::WriteAllText((Join-Path $stubDir 'dotnet.cmd'), "@echo dotnet stub (Test-PackageVersionGuard): not building`r`n@exit /b 1`r`n")
$env:Path = "$stubDir;$env:Path"

[IO.File]::WriteAllText((Join-Path $scratch 'pluginmaster.json'),
    '[{"InternalName":"GuardProbe","AssemblyVersion":"0.0.1.0"}]', [Text.UTF8Encoding]::new($false))
$probeCsproj = Join-Path $scratch 'src\GuardProbe\GuardProbe.csproj'

function Invoke-Packager([string]$csprojText, [switch]$AutoBump) {
    [IO.File]::WriteAllText($probeCsproj, $csprojText, [Text.UTF8Encoding]::new($false))
    $script:caught = $null
    $p = @{ PluginName = 'GuardProbe'; Channel = 'production' }
    if ($AutoBump) { $p.AutoBump = $true }
    try { & (Join-Path $scratch 'tools\Package-Plugin.ps1') @p } catch { $script:caught = $_ }
}

$fail = 0
function Check($name, $actual, $expected) {
    if ($actual -ceq $expected) { Write-Host "PASS $name" } else { Write-Host "FAIL $name`n  expected $expected`n  actual   $actual"; $script:fail++ }
}
function CheckTrue($name, $cond) {
    if ($cond) { Write-Host "PASS $name" } else { Write-Host "FAIL $name"; $script:fail++ }
}

# --- Case 1: the incident shape. <Version> bumped, <AssemblyVersion>/<FileVersion> stale.
# The guard must refuse BEFORE any build/zip/upload, naming all three fields and both values.
Invoke-Packager @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Version>0.0.2.0</Version>
    <AssemblyVersion>0.0.1.0</AssemblyVersion>
    <FileVersion>0.0.1.0</FileVersion>
  </PropertyGroup>
</Project>
"@
$msg = if ($caught) { $caught.Exception.Message } else { '' }
CheckTrue 'disagree: run refused'      ($msg -match 'version fields disagree')
CheckTrue 'disagree: names <Version>'          ($msg -match [regex]::Escape('<Version>'))
CheckTrue 'disagree: names <AssemblyVersion>'  ($msg -match [regex]::Escape('<AssemblyVersion>'))
CheckTrue 'disagree: names <FileVersion>'      ($msg -match [regex]::Escape('<FileVersion>'))
CheckTrue 'disagree: shows both values'        ($msg -match '0\.0\.2\.0' -and $msg -match '0\.0\.1\.0')
CheckTrue 'disagree: refusal precedes the pipeline' ($msg -notmatch 'Built manifest not found')

# --- Case 2: all three equal (the 0.2.4.0-style repaired state) - no behavior change.
# Proof the guard passed: the run proceeds and stops later at the built-manifest check.
Invoke-Packager @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Version>0.0.2.0</Version>
    <AssemblyVersion>0.0.2.0</AssemblyVersion>
    <FileVersion>0.0.2.0</FileVersion>
  </PropertyGroup>
</Project>
"@
Check 'match: got past the guard (stops at the build step)' `
    $(if ($caught) { $caught.Exception.Message -match 'Built manifest not found' } else { $false }) 'True'

# --- Case 3: <Version>-only (GluttonyCombo/PvPSolver/ArmoireAutoFill shape). Absent fields
# are derived by the SDK, so this is a correct run and must NOT be refused.
Invoke-Packager @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Version>0.0.2.0</Version>
  </PropertyGroup>
</Project>
"@
Check 'version-only: got past the guard (stops at the build step)' `
    $(if ($caught) { $caught.Exception.Message -match 'Built manifest not found' } else { $false }) 'True'

# --- Case 4: -AutoBump must move every PRESENT field together; bumping <Version> alone
# would recreate the incident state mid-run (old <AssemblyVersion> -> old packaged version).
Invoke-Packager -AutoBump @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Version>0.0.1.0</Version>
    <AssemblyVersion>0.0.1.0</AssemblyVersion>
    <FileVersion>0.0.1.0</FileVersion>
  </PropertyGroup>
</Project>
"@
$after = [IO.File]::ReadAllText($probeCsproj)
Check 'auto-bump: <Version> moved'          ($after -match [regex]::Escape('<Version>0.0.1.1</Version>'))          'True'
Check 'auto-bump: <AssemblyVersion> moved'  ($after -match [regex]::Escape('<AssemblyVersion>0.0.1.1</AssemblyVersion>')) 'True'
Check 'auto-bump: <FileVersion> moved'      ($after -match [regex]::Escape('<FileVersion>0.0.1.1</FileVersion>'))      'True'

Remove-Item -Recurse -Force $scratch
if ($fail) { Write-Host "$fail FAILED"; exit 1 } else { Write-Host 'ALL PASS' }
