# Plain assertions (no Pester) for tools/PackageLinks.ps1. Run: pwsh -File tools/tests/Test-PackageLinks.ps1
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
if ($fail) { Write-Host "$fail FAILED"; exit 1 } else { Write-Host 'ALL PASS' }
