#!/usr/bin/env bash
set -euo pipefail
PROJ="$HOME/Dagobert-PriceMatcher"
DEST="$HOME/.xlcore/installedPlugins/DagobertPriceMatcher/0.1.0.0"
cd "$PROJ"
~/.dotnet/dotnet build --configuration Release
SRC="$PROJ/Dagobert/bin/x64/Release"
mkdir -p "$DEST"
cp -f "$SRC/DagobertPriceMatcher.dll" "$DEST/"
cp -f "$SRC/DagobertPriceMatcher.deps.json" "$DEST/"
cp -f "$PROJ/Dagobert/DagobertPriceMatcher.json" "$DEST/"
cp -f "$SRC/ECommons.dll" "$DEST/"
cp -f "$SRC/System.Speech.dll" "$DEST/" 2>/dev/null || true
echo "Deployed to $DEST"
echo "Reload in /xlplugins (Disable then Enable) or relaunch the game."
