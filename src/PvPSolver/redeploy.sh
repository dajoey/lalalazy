#!/usr/bin/env bash
set -euo pipefail
PROJ="$HOME/PvPSolver"
DEST="$HOME/.xlcore/installedPlugins/PvPSolver/0.1.0.0"
cd "$PROJ"

# Must clean obj to avoid stale source generator cache
rm -rf "$PROJ"/PvPSolver.Basic/obj "$PROJ"/PvPSolver/obj "$PROJ"/PvPSolver.SourceGenerators/obj "$PROJ"/PvPSolver.SourceGenerators/bin

~/.dotnet/dotnet build --configuration Release

PLUGIN_DLL="$PROJ/PvPSolver/bin/Release/PvPSolver.dll"
BASIC_DLL="$PROJ/PvPSolver.Basic/bin/x64/Release/net10.0-windows10.0.26100.0/PvPSolver.Basic.dll"
FALLBACK_BASIC_DLL="$PROJ/PvPSolver/bin/Release/PvPSolver.Basic.dll"

mkdir -p "$DEST"
cp -f "$PLUGIN_DLL" "$DEST/"
if [ -f "$BASIC_DLL" ]; then
    cp -f "$BASIC_DLL" "$DEST/"
elif [ -f "$FALLBACK_BASIC_DLL" ]; then
    cp -f "$FALLBACK_BASIC_DLL" "$DEST/"
fi
cp -f "$PROJ/manifest.json" "$DEST/PvPSolver.json"
echo "Deployed $(date) to $DEST"
ls -la "$DEST/"
echo "Reload in /xlplugins (Disable then Enable) or relaunch the game."
