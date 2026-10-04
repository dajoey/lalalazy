# Publish a plugin zip as a GitHub Release asset. Dot-sourced by Package-Plugin.ps1 and
# tools/tests/Test-PackageRelease.ps1.
#
# Why (2026-09-25): Dalamud downloaded every zip from raw.githubusercontent.com, where GitHub
# counts nothing, so there was no install count anywhere. Release assets carry a
# download_count; ~/ops/github-traffic-snapshot.py on the ops host records it daily.
#
# Naming contract - github-traffic-snapshot.py parses exactly this; change both together:
#   tag    <Plugin>-v<version>        one release per plugin version, shared by both channels
#   asset  <Plugin>.zip               the production zip of that version
#          <Plugin>-testing.zip       the testing zip of that version
# A testing build creates the release as a pre-release. The production promote of the same
# version adds <Plugin>.zip to it and clears the pre-release flag. No release is ever marked
# "Latest": the repo holds many plugins, so a single latest release would mislead.
#
# Uploads are verified by the SHA-256 digest GitHub returns, never by downloading the asset,
# so the packager adds nothing to the download counts.
# Windows PowerShell 5.1 runs this (no pwsh on the build host): no ternaries, no ?? operators.

$script:ReleaseRepo = 'dajoey/lalalazy'

function Get-ReleaseTag([string] $PluginName, [string] $Version) { "$PluginName-v$Version" }

function Get-ReleaseAssetName([string] $PluginName, [string] $Channel) {
    if ($Channel -eq 'testing') { "$PluginName-testing.zip" } else { "$PluginName.zip" }
}

function Get-ReleaseAssetUrl([string] $PluginName, [string] $Version, [string] $Channel) {
    "https://github.com/$script:ReleaseRepo/releases/download/$(Get-ReleaseTag $PluginName $Version)/$(Get-ReleaseAssetName $PluginName $Channel)"
}

function Assert-TargetCommitOnOrigin {
    <#
      Verify $TargetCommit is reachable from origin/main before any tag is created at it.
      A tag made at an unpushed worktree state names whatever origin/main happened to be at
      that moment - in practice the PREVIOUS release's commit, because the release commit
      only exists once the packager's output is committed and pushed (found live 2026-10-04:
      81 tags, GluttonyCombo v1.0.4.236-.273 among them, every one naming the previous
      release; tools/check-release-tags.py is the gate). Fetch first so the check reads
      origin as it is now, not as the last fetch saw it. -RepoRoot defaults to the caller's
      current directory (tools/tests/Test-PackageRelease.ps1 drives a scratch repo pair).
    #>
    param([Parameter(Mandatory)] [string] $TargetCommit, [string] $RepoRoot = (Get-Location).Path)
    # PS 5.1 + $ErrorActionPreference='Stop' turns any redirected stderr record from a native
    # command into a terminating error, and git writes routine progress ("From ...") to
    # stderr - so the calls run under 'Continue' and report through $LASTEXITCODE instead.
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $null = & git -C $RepoRoot fetch -q origin 2>&1
        $fetchCode = $LASTEXITCODE
        & git -C $RepoRoot merge-base --is-ancestor $TargetCommit origin/main
        $ancestorCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $prev }
    if ($fetchCode -ne 0) { throw "git fetch origin failed in $RepoRoot (exit $fetchCode); cannot verify the target commit is on origin/main." }
    if ($ancestorCode -ne 0) {
        throw @"
Refusing to create a tag at ${TargetCommit}: that commit is not reachable from origin/main.
A release tag must name its own release commit ON origin - a tag created at an unpushed
worktree state lands on whatever origin/main tip is current, i.e. the previous release's
commit (found live 2026-10-04). Push the release commit to origin/main first, then pass
its SHA as -TargetCommit (Package-Plugin.ps1 does this itself; see step 6 there).
"@
    }
}

function Get-ReleaseGitHubToken {
    # GITHUB_TOKEN from the environment wins; otherwise fetch /infra/GITHUB_TOKEN from
    # Infisical with this host's read-only agent identity (wiki APIs/Infisical).
    if ($env:GITHUB_TOKEN) { return $env:GITHUB_TOKEN }
    $idFile = Join-Path $env:USERPROFILE '.credentials\infisical-agents.env'
    if (-not (Test-Path $idFile)) { throw "No GITHUB_TOKEN in the environment and no Infisical identity at $idFile." }
    $cfg = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($idFile)) {
        if ($line -match '^\s*(?:export\s+)?([A-Z_]+)\s*=\s*(.*)$') { $cfg[$Matches[1]] = $Matches[2].Trim().Trim('"').Trim("'") }
    }
    $envName = if ($cfg['INFISICAL_ENV']) { $cfg['INFISICAL_ENV'] } else { 'prod' }
    $login = Invoke-RestMethod -Method Post -Uri "$($cfg['INFISICAL_URL'])/api/v1/auth/universal-auth/login" `
        -ContentType 'application/json' `
        -Body (@{ clientId = $cfg['INFISICAL_CLIENT_ID']; clientSecret = $cfg['INFISICAL_CLIENT_SECRET'] } | ConvertTo-Json)
    $q = "workspaceId=$($cfg['INFISICAL_PROJECT_ID'])&environment=$envName&secretPath=%2Finfra"
    $sec = Invoke-RestMethod -Uri "$($cfg['INFISICAL_URL'])/api/v3/secrets/raw/GITHUB_TOKEN?$q" `
        -Headers @{ Authorization = "Bearer $($login.accessToken)" }
    return $sec.secret.secretValue
}

function Invoke-ReleaseApi {
    param([string] $Method = 'Get', [Parameter(Mandatory)] [string] $Uri, $Body, [string] $InFile,
          [string] $ContentType = 'application/json', [Parameter(Mandatory)] [string] $Token)
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $p = @{ Method = $Method; Uri = $Uri; ContentType = $ContentType
            Headers = @{ Authorization = "Bearer $Token"; Accept = 'application/vnd.github+json'
                         'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'lalalazy-packager' } }
    if ($null -ne $Body) { $p.Body = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 5)) }
    if ($InFile) { $p.InFile = $InFile }
    Invoke-RestMethod @p
}

function Get-ReleaseHttpStatus($ErrorRecord) {
    $r = $ErrorRecord.Exception.Response
    if ($r) { return [int]$r.StatusCode }
    return 0
}

function Publish-PluginRelease {
    <#
      Upload $ZipPath as the $Channel asset of release <Plugin>-v<Version>, creating the release
      if needed, and return the asset's public download URL. An existing asset of the same name
      is replaced: Package-Plugin.ps1 refuses to re-package a registered version unless
      -Republish (unpushed), so the only assets replaced here are unpublished ones.
    #>
    param(
        [Parameter(Mandatory)] [string] $PluginName,
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [ValidateSet('testing', 'production')] [string] $Channel,
        [Parameter(Mandatory)] [string] $ZipPath,
        [string] $DisplayName,
        [string] $Notes,
        # The exact commit (already on origin/main) the release's tag must land on. Required
        # whenever this call CREATES the release - creating a release creates its tag.
        [string] $TargetCommit,
        # Repo root for the origin/main verification (defaults to the caller's cwd, which
        # git resolves to the enclosing worktree).
        [string] $RepoRoot,
        [string] $Token
    )
    if (-not (Test-Path $ZipPath)) { throw "Zip not found: $ZipPath" }
    if (-not $Token) { $Token = Get-ReleaseGitHubToken }
    $api = "https://api.github.com/repos/$script:ReleaseRepo"
    $tag = Get-ReleaseTag $PluginName $Version
    $assetName = Get-ReleaseAssetName $PluginName $Channel
    if (-not $DisplayName) { $DisplayName = $PluginName }

    $rel = $null
    try { $rel = Invoke-ReleaseApi -Uri "$api/releases/tags/$tag" -Token $Token }
    catch { if ((Get-ReleaseHttpStatus $_) -ne 404) { throw } }
    if (-not $rel) {
        # Creating the release creates its tag. Refuse to guess where: target_commitish
        # 'main' resolved to the remote tip of the moment while the release commit sat
        # unpushed in a worktree, so every tag named the PREVIOUS release (2026-10-04,
        # 81 tags). The tag goes to an explicit pushed commit and nowhere else.
        if (-not $TargetCommit) {
            throw @"
Refusing to create release ${tag}: creating a release creates its tag, and no -TargetCommit was given.
Pass the release commit's SHA once it is pushed to origin/main (Package-Plugin.ps1 pushes the
release commit and passes that SHA). A tag created without one lands on the current remote
'main' tip - the previous release's commit (found live 2026-10-04: 81 mis-tagged releases).
"@
        }
        Assert-TargetCommitOnOrigin -TargetCommit $TargetCommit -RepoRoot $(if ($RepoRoot) { $RepoRoot } else { (Get-Location).Path })
        $body = @{ tag_name = $tag; target_commitish = $TargetCommit; name = "$DisplayName $Version"
                   body = $(if ($Notes) { $Notes } else { '' }); prerelease = ($Channel -eq 'testing'); make_latest = 'false' }
        $rel = Invoke-ReleaseApi -Method Post -Uri "$api/releases" -Body $body -Token $Token
        Write-Host "Created release $tag$(if ($Channel -eq 'testing') { ' (pre-release)' }) at commit $($TargetCommit.Substring(0,9))."
    }

    foreach ($old in @($rel.assets | Where-Object { $_.name -eq $assetName })) {
        Invoke-ReleaseApi -Method Delete -Uri "$api/releases/assets/$($old.id)" -Token $Token | Out-Null
        Write-Host "Replaced the unpublished $assetName on $tag."
    }

    $uploadUri = "https://uploads.github.com/repos/$script:ReleaseRepo/releases/$($rel.id)/assets?name=$([uri]::EscapeDataString($assetName))"
    $up = Invoke-ReleaseApi -Method Post -Uri $uploadUri -InFile $ZipPath -ContentType 'application/zip' -Token $Token

    $local = (Get-FileHash -Algorithm SHA256 -Path $ZipPath).Hash.ToLowerInvariant()
    $size = (Get-Item $ZipPath).Length
    if ($up.state -ne 'uploaded') { throw "Upload of $assetName to $tag ended in state '$($up.state)'." }
    if ($up.digest) {
        if ($up.digest -ne "sha256:$local") { throw "Upload of $assetName to $tag is corrupt: GitHub has $($up.digest), local sha256:$local." }
    } elseif ([int64]$up.size -ne $size) {
        throw "Upload of $assetName to $tag is $($up.size) bytes, local zip is $size."
    }

    if ($Channel -eq 'production' -and $rel.prerelease) {
        Invoke-ReleaseApi -Method Patch -Uri "$api/releases/$($rel.id)" -Body @{ prerelease = $false; make_latest = 'false' } -Token $Token | Out-Null
        Write-Host "Promoted release $tag from pre-release."
    }
    return $up.browser_download_url
}
