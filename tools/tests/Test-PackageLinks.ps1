# Plain assertions (no Pester) for tools/PackageLinks.ps1. Run: powershell -NoProfile -File tools/tests/Test-PackageLinks.ps1 (no pwsh on DAJOEYROG)
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'PackageLinks.ps1')
$fail = 0
function Check($name, $actual, $expected) {
    if ($actual -ceq $expected) { Write-Host "PASS $name" } else { Write-Host "FAIL $name`n  expected $expected`n  actual   $actual"; $script:fail++ }
}
$b = 'https://raw.githubusercontent.com/dajoey/lalalazy/main/plugins/P'
function New-Entry($exclusive) {
    $e = [PSCustomObject]@{ DownloadLinkInstall = ''; DownloadLinkUpdate = ''; DownloadLinkTesting = '' }
    if ($null -ne $exclusive) { $e | Add-Member -NotePropertyName IsTestingExclusive -NotePropertyValue $exclusive }
    $e
}
# A testing-exclusive plugin has no latest.zip: every link must stay on testing.zip.
$e = New-Entry $true; Set-EntryDownloadLinks -Entry $e -PluginName P -Channel testing
Check 'exclusive+testing install' $e.DownloadLinkInstall "$b/testing/testing.zip"
Check 'exclusive+testing update'  $e.DownloadLinkUpdate  "$b/testing/testing.zip"
Check 'exclusive+testing testing' $e.DownloadLinkTesting "$b/testing/testing.zip"
# A promoted (non-exclusive) plugin keeps production links on a testing build.
$e = New-Entry $false; Set-EntryDownloadLinks -Entry $e -PluginName P -Channel testing
Check 'promoted+testing install' $e.DownloadLinkInstall "$b/latest/latest.zip"
Check 'promoted+testing update'  $e.DownloadLinkUpdate  "$b/latest/latest.zip"
# Older entries without the flag behave as promoted.
$e = New-Entry $null; Set-EntryDownloadLinks -Entry $e -PluginName P -Channel testing
Check 'noflag+testing install' $e.DownloadLinkInstall "$b/latest/latest.zip"
# A production promote of an exclusive plugin moves install/update to latest.zip.
$e = New-Entry $true; Set-EntryDownloadLinks -Entry $e -PluginName P -Channel production
Check 'exclusive+production install' $e.DownloadLinkInstall "$b/latest/latest.zip"
Check 'exclusive+production update'  $e.DownloadLinkUpdate  "$b/latest/latest.zip"
# --- Release links (2026-09-25): the channel being published points at its Release asset,
# the other channel's links are left alone.
$r = 'https://github.com/dajoey/lalalazy/releases/download'
function New-Released {
    [PSCustomObject]@{ IsTestingExclusive = $false
                       DownloadLinkInstall = "$r/P-v1.0.0.1/P.zip"; DownloadLinkUpdate = "$r/P-v1.0.0.1/P.zip"
                       DownloadLinkTesting = "$r/P-v1.0.0.1/P-testing.zip" }
}
$e = New-Released; Set-EntryDownloadLinks -Entry $e -PluginName P -Channel testing -ReleaseUrl "$r/P-v1.0.0.2/P-testing.zip"
Check 'release testing: testing link moves'      $e.DownloadLinkTesting "$r/P-v1.0.0.2/P-testing.zip"
Check 'release testing: production install stays' $e.DownloadLinkInstall "$r/P-v1.0.0.1/P.zip"
Check 'release testing: production update stays'  $e.DownloadLinkUpdate  "$r/P-v1.0.0.1/P.zip"
$e = New-Released; Set-EntryDownloadLinks -Entry $e -PluginName P -Channel production -ReleaseUrl "$r/P-v1.0.0.2/P.zip"
Check 'release production: install moves'  $e.DownloadLinkInstall "$r/P-v1.0.0.2/P.zip"
Check 'release production: update moves'   $e.DownloadLinkUpdate  "$r/P-v1.0.0.2/P.zip"
Check 'release production: testing stays'  $e.DownloadLinkTesting "$r/P-v1.0.0.1/P-testing.zip"
$e = New-Entry $true; Set-EntryDownloadLinks -Entry $e -PluginName P -Channel testing -ReleaseUrl "$r/P-v0.0.1.0/P-testing.zip"
Check 'release exclusive testing: install' $e.DownloadLinkInstall "$r/P-v0.0.1.0/P-testing.zip"
Check 'release exclusive testing: update'  $e.DownloadLinkUpdate  "$r/P-v0.0.1.0/P-testing.zip"
Check 'release exclusive testing: testing' $e.DownloadLinkTesting "$r/P-v0.0.1.0/P-testing.zip"
# A brand-new production entry has no testing link yet: it gets the production asset, not a 404.
$e = New-Entry $false; Set-EntryDownloadLinks -Entry $e -PluginName P -Channel production -ReleaseUrl "$r/P-v1.0.0.0/P.zip"
Check 'release new production: testing filled' $e.DownloadLinkTesting "$r/P-v1.0.0.0/P.zip"
# Publish failed (no -ReleaseUrl) on a testing build: the raw testing copy, and the counted
# production links are NOT reset to raw.
$e = New-Released; Set-EntryDownloadLinks -Entry $e -PluginName P -Channel testing
Check 'fallback testing: raw testing link'   $e.DownloadLinkTesting "$b/testing/testing.zip"
Check 'fallback testing: production kept'    $e.DownloadLinkInstall "$r/P-v1.0.0.1/P.zip"
# Publish failed on a production build: the raw latest copy committed by this run.
$e = New-Released; Set-EntryDownloadLinks -Entry $e -PluginName P -Channel production
Check 'fallback production: raw install'     $e.DownloadLinkInstall "$b/latest/latest.zip"
if ($fail) { Write-Host "$fail FAILED"; exit 1 } else { Write-Host 'ALL PASS' }
