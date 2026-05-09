#!/usr/bin/env bash
set -euo pipefail
PROJ="$HOME/ArmoireAutoFill"
DEST="$HOME/.xlcore/installedPlugins/ArmoireAutoFill/0.1.0.0"
cd "$PROJ"
~/.dotnet/dotnet build --configuration Release
SRC="$PROJ/bin/Release"
mkdir -p "$DEST"
cp -f "$SRC/ArmoireAutoFill.dll" "$DEST/"
cp -f "$SRC/ArmoireAutoFill.deps.json" "$DEST/"
cp -f "$PROJ/ArmoireAutoFill.json" "$DEST/"
cp -f "$SRC/ECommons.dll" "$DEST/" 2>/dev/null || true
echo "Deployed to $DEST"
ls -la "$DEST/"
echo "Reload in /xlplugins (Disable then Enable) or relaunch the game."
