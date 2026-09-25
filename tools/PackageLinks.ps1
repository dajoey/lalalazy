# Download-link policy for an EXISTING pluginmaster entry, dot-sourced by Package-Plugin.ps1
# and by tools/tests/Test-PackageLinks.ps1.
#
# Since 2026-09-25 a build's zip is also published as a GitHub Release asset
# (tools/PackageRelease.ps1) and the link for the channel being published points at that
# asset, because GitHub counts Release downloads and counts nothing on raw.githubusercontent.com.
# Release links name one version, so a run only moves the links of its own channel; the other
# channel's links are left as they are. Without a -ReleaseUrl (-NoRelease, or the publish failed)
# the channel gets the raw copy committed under plugins/ - the pre-2026-09-25 links, which
# always resolve to that channel's newest committed zip.
function Set-EntryDownloadLinks {
    param(
        [Parameter(Mandatory)] $Entry,
        [Parameter(Mandatory)] [string] $PluginName,
        [Parameter(Mandatory)] [ValidateSet('testing', 'production')] [string] $Channel,
        [string] $ReleaseUrl
    )
    $base = "https://raw.githubusercontent.com/dajoey/lalalazy/main/plugins/$PluginName"
    $built = if ($ReleaseUrl) { $ReleaseUrl }
             elseif ($Channel -eq 'testing') { "$base/testing/testing.zip" }
             else { "$base/latest/latest.zip" }
    # A testing-exclusive plugin has never been promoted, so latest.zip does not exist:
    # install/update must stay on the testing build until a -Channel production run (which
    # clears IsTestingExclusive) moves them.
    $exclusive = $Entry.PSObject.Properties['IsTestingExclusive'] -and $Entry.IsTestingExclusive
    if ($Channel -eq 'testing') {
        $Entry.DownloadLinkTesting = $built
        if ($exclusive) {
            $Entry.DownloadLinkInstall = $built
            $Entry.DownloadLinkUpdate = $built
        } else {
            # Production links belong to the production run; only fill them if missing.
            if (-not $Entry.DownloadLinkInstall) { $Entry.DownloadLinkInstall = "$base/latest/latest.zip" }
            if (-not $Entry.DownloadLinkUpdate) { $Entry.DownloadLinkUpdate = "$base/latest/latest.zip" }
        }
    } else {
        $Entry.DownloadLinkInstall = $built
        $Entry.DownloadLinkUpdate = $built
        # Dalamud only follows the testing link while TestingAssemblyVersion is newer than
        # AssemblyVersion, so the testing run's link stays; fill it only if missing.
        if (-not $Entry.DownloadLinkTesting) { $Entry.DownloadLinkTesting = $built }
    }
}
