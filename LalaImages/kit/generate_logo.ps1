<#
.SYNOPSIS
  Generate a lalalazy-style plugin logo via Venice AI img2img.

.DESCRIPTION
  Runs the canonical lalafell logo base through Venice's /image/edit
  endpoint, swapping only the contents of the thought bubble. Output is
  written as a 500x500 PNG to LalaImages/<ModName>-icon.png in the repo.

  The canonical base lives at LalaImages/kit/assets/canonical-base.png
  and must not be edited in place. The img2img approach preserves the
  lala face, hair, blanket, dark green background, and zZz letters
  pixel-faithfully across every logo — only what is inside the thought
  bubble changes per mod.

.PARAMETER ModName
  Short slug for the output filename. Lowercase, hyphen-separated.
  e.g. "tradecraft" -> LalaImages/tradecraft-icon.png

.PARAMETER Dream
  One-sentence pixel-icon description for what the lala is dreaming of.
  See LalaImages/kit/prompt-template.txt for the {DREAM} authoring guide.

.PARAMETER RepoRoot
  Repo root. Defaults to the parent of this script's grandparent so
  the script "just works" from anywhere inside the repo.

.PARAMETER Model
  Venice edit model. Default 'qwen-edit'. Alternatives:
    seedream-v5-lite-edit  (slightly higher quality, $0.05)
    flux-2-max-edit         (highest quality, $0.19)

.PARAMETER Open
  Open the resulting PNG when done.

.EXAMPLE
  .\generate_logo.ps1 -ModName tradecraft -Dream "a tiny stack of three pixel-art crafting tools — a hammer, a saw, and a screwdriver — in earthy tones"

.NOTES
  Requires the VENICE_API_KEY env var (User or Machine scope).
  See LalaImages/STYLE.md for the full style spec.
#>
param(
  [Parameter(Mandatory)][string]$ModName,
  [Parameter(Mandatory)][string]$Dream,
  [string]$RepoRoot = '',
  [string]$Model = 'qwen-edit',
  [switch]$Open
)
$ErrorActionPreference = 'Stop'

# Resolve repo root if not provided
if (-not $RepoRoot) {
  $here = Split-Path -Parent $MyInvocation.MyCommand.Definition
  $RepoRoot = Resolve-Path (Join-Path $here '..\..')
}
$lalaDir = Join-Path $RepoRoot 'LalaImages'
$basePath = Join-Path $lalaDir 'kit\assets\canonical-base.png'
if (-not (Test-Path $lalaDir)) { throw "LalaImages/ not found under $RepoRoot" }
if (-not (Test-Path $basePath)) { throw "Canonical base not found at $basePath" }

# Venice key
$key = [Environment]::GetEnvironmentVariable('VENICE_API_KEY','User')
if (-not $key) { $key = [Environment]::GetEnvironmentVariable('VENICE_API_KEY','Machine') }
if (-not $key) { throw 'VENICE_API_KEY env var not set. Get one at https://venice.ai/settings/api and: [Environment]::SetEnvironmentVariable("VENICE_API_KEY","<key>","User")' }
$headers = @{ 'Authorization' = "Bearer $key" }

# Build the prompt — KEEP IDENTICAL to LalaImages/kit/prompt-template.txt
$prompt = "Replace the crossed swords inside the white thought bubble with $Dream. Keep the sleeping lalafell face, brown bob hair, white duvet, dark green background, the zZz letters, and the white thought bubble outline EXACTLY the same as the original. Maintain the chunky retro pixel-art aesthetic."

# Base image -> data URI
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($basePath))
$body = [ordered]@{
  image        = "data:image/png;base64,$b64"
  prompt       = $prompt
  model        = $Model
  aspect_ratio = '1:1'
  safe_mode    = $true
}
$json = $body | ConvertTo-Json -Depth 6 -Compress

Write-Host "ModName=$ModName  Model=$Model"
Write-Host "Calling Venice /image/edit ..."
$t0 = Get-Date
$resp = Invoke-WebRequest -Uri 'https://api.venice.ai/api/v1/image/edit' `
  -Headers $headers -Method Post -Body $json -ContentType 'application/json' `
  -UseBasicParsing -TimeoutSec 180
$wall = [math]::Round((New-TimeSpan $t0 (Get-Date)).TotalSeconds, 1)
if ($resp.StatusCode -ne 200) { throw "Venice edit failed: HTTP $($resp.StatusCode)" }

# Write 1024 source, downscale to 500x500
$tmp1024 = Join-Path $env:TEMP ("lala-{0}-1024.png" -f $ModName)
[IO.File]::WriteAllBytes($tmp1024, $resp.Content)

Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile($tmp1024)
$dst = New-Object System.Drawing.Bitmap 500, 500
$g = [System.Drawing.Graphics]::FromImage($dst)
$g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::None
$g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.DrawImage($src, 0, 0, 500, 500)
$g.Dispose()
$src.Dispose()

$outPath = Join-Path $lalaDir ("{0}-icon.png" -f $ModName)
$dst.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$dst.Dispose()
Remove-Item $tmp1024 -ErrorAction SilentlyContinue

$size = (Get-Item $outPath).Length
Write-Host ("OK wall={0}s size={1}B -> {2}" -f $wall, $size, $outPath)
if ($Open) { Invoke-Item $outPath }
