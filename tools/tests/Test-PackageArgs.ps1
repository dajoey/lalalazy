# Plain assertions (no Pester) for the argument contract of tools/Package-Plugin.ps1:
#   1. no positional binding - a stray token on the command line is a binding error, never a value
#   2. -VersionOverride accepts only a dotted numeric version of two to four parts
# The script is inspected through its AST and is NEVER executed by the static checks, because a
# run with a bad argument would build, commit, push and publish. The one behavioural case runs
# only after the static checks pass and uses a plugin name that does not exist, so even a
# regression there stops at the missing source directory instead of publishing.
# Run: powershell -NoProfile -File tools/tests/Test-PackageArgs.ps1  (Windows PowerShell 5.1)
$ErrorActionPreference = 'Stop'

$realScript = Join-Path (Split-Path $PSScriptRoot -Parent) 'Package-Plugin.ps1'

$fail = 0
function CheckTrue($name, $cond) {
    if ($cond) { Write-Host "PASS $name" } else { Write-Host "FAIL $name"; $script:fail++ }
}

$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($realScript, [ref]$tokens, [ref]$errors)
CheckTrue 'script parses without errors' (@($errors).Count -eq 0)
$paramBlock = $ast.ParamBlock
CheckTrue 'script has a param block' ($null -ne $paramBlock)

# --- Case 1: CmdletBinding(PositionalBinding = $false)
$cb = @($paramBlock.Attributes | Where-Object { $_.TypeName.GetReflectionType() -eq [System.Management.Automation.CmdletBindingAttribute] })
CheckTrue 'param block carries CmdletBinding' ($cb.Count -eq 1)
$posOff = $false
if ($cb.Count -eq 1) {
    foreach ($na in $cb[0].NamedArguments) {
        if ($na.ArgumentName -eq 'PositionalBinding' -and -not $na.ExpressionOmitted -and $na.Argument.Extent.Text -eq '$false') { $posOff = $true }
    }
}
CheckTrue 'CmdletBinding sets PositionalBinding = $false' $posOff

# --- Case 2: VersionOverride validation pattern
$vo = $paramBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'VersionOverride' }
CheckTrue 'VersionOverride parameter exists' ($null -ne $vo)
$pattern = $null
if ($vo) {
    $vp = @($vo.Attributes | Where-Object { $_.TypeName.GetReflectionType() -eq [System.Management.Automation.ValidatePatternAttribute] })
    if ($vp.Count -eq 1 -and @($vp[0].PositionalArguments).Count -ge 1) {
        $pattern = [string]$vp[0].PositionalArguments[0].SafeGetValue()
    }
}
CheckTrue 'VersionOverride carries a ValidatePattern' ($null -ne $pattern)
foreach ($v in '1.2.3', '0.1.9.23', '1.0') {
    CheckTrue "pattern accepts '$v'" ($null -ne $pattern -and $v -cmatch $pattern)
}
foreach ($v in '.', '', '1', 'v1.2.3', '1.2.3.4.5', '1.2.x', '1..2', '1.2.', "1.2.3`n", '1.2.3 ') {
    $shown = $v -replace "`n", '\n'
    CheckTrue "pattern rejects '$shown'" ($null -ne $pattern -and $v -cnotmatch $pattern)
}

# --- Case 3 (behavioural, only if the static checks passed): a stray token is a binding error and
# the script body does not run. ZzArgProbe does not exist, so even if binding wrongly succeeded the
# run would stop at the missing source directory, before anything is built or written. The repo
# tree is listed (size + timestamp) before and after and must be identical.
if ($fail -eq 0) {
    $repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    function Get-Listing {
        $items = @()
        foreach ($rel in 'pluginmaster.json', 'plugins', 'src') {
            $p = Join-Path $repoRoot $rel
            if (Test-Path $p) {
                $items += Get-ChildItem -LiteralPath $p -Recurse -Force -File | ForEach-Object { "$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" }
            }
        }
        ($items | Sort-Object) -join "`n"
    }
    $before = Get-Listing
    $caught = $null
    try { & $realScript -PluginName ZzArgProbe -Channel testing . } catch { $caught = $_ }
    $msg = if ($caught) { $caught.Exception.Message } else { '' }
    CheckTrue 'stray token: run refused' ($null -ne $caught)
    CheckTrue 'stray token: parameter-binding error' ($caught -and $caught.FullyQualifiedErrorId -match 'PositionalParameterNotFound|ParameterBinding')
    CheckTrue 'stray token: body did not run' ($msg -notmatch 'Plugin source directory not found')
    CheckTrue 'stray token: tree unchanged' ((Get-Listing) -ceq $before)
    $caught = $null
    try { & $realScript -PluginName ZzArgProbe -Channel testing -VersionOverride . } catch { $caught = $_ }
    CheckTrue 'bad version: parameter-binding error' ($caught -and $caught.FullyQualifiedErrorId -match 'ParameterArgumentValidationError|ParameterBinding' -and $caught.Exception.Message -notmatch 'Plugin source directory not found')
} else {
    Write-Host 'SKIP behavioural cases (static checks failed; the unfixed script is never run)'
}

if ($fail -gt 0) { Write-Host "$fail check(s) FAILED"; exit 1 }
Write-Host 'All checks passed'
exit 0
