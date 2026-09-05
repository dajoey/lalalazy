# Config-migration regression proof for the DagobertAfterCraft -> PriceMatchAfterCraft rename (card t_89a7ebec).
# Compiles the REAL Configuration.cs (linked, not copied) against stubs + real Newtonsoft, then asserts
# a pre-rename v4 config round-trips without losing the setting.
Write-Output "== build =="
dotnet build tests\LazyCrafter.ConfigMigrate\LazyCrafter.ConfigMigrate.csproj -c Release
if ($LASTEXITCODE -ne 0) { Write-Output 'BUILD FAILED'; exit 1 }
Write-Output "== run =="
dotnet tests\LazyCrafter.ConfigMigrate\bin\Release\net10.0\LazyCrafter.ConfigMigrate.dll
exit $LASTEXITCODE
