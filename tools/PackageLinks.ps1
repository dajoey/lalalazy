# Download-link policy for an EXISTING pluginmaster entry, dot-sourced by Package-Plugin.ps1
# and by tools/tests/Test-PackageLinks.ps1.
function Set-EntryDownloadLinks {
    param(
        [Parameter(Mandatory)] $Entry,
        [Parameter(Mandatory)] [string] $PluginName,
        [Parameter(Mandatory)] [ValidateSet('testing', 'production')] [string] $Channel
    )
    $base = "https://raw.githubusercontent.com/dajoey/lalalazy/main/plugins/$PluginName"
    # A testing-exclusive plugin has never been promoted, so latest.zip does not exist:
    # install/update must stay on testing.zip until a -Channel production run (which
    # clears IsTestingExclusive) moves them.
    $exclusive = $Entry.PSObject.Properties['IsTestingExclusive'] -and $Entry.IsTestingExclusive
    $zip = if ($Channel -eq 'testing' -and $exclusive) { 'testing/testing.zip' } else { 'latest/latest.zip' }
    $Entry.DownloadLinkInstall = "$base/$zip"
    $Entry.DownloadLinkUpdate = "$base/$zip"
    $Entry.DownloadLinkTesting = "$base/testing/testing.zip"
}
