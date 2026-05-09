#!/usr/bin/env bash
# Rebuild AutoPotion and copy the artifacts into the installed-plugin folder.
# After running this you must reload the plugin in /xlplugins (Disable -> Enable)
# or relaunch FFXIV/Dalamud.

set -euo pipefail

PROJ="$HOME/AutoPotion"
DEST="$HOME/.xlcore/installedPlugins/AutoPotion/0.1.1.0"

cd "$PROJ"
~/.dotnet/dotnet build --configuration Release

SRC="$PROJ/AutoPotion/bin/x64/Release"
mkdir -p "$DEST"
cp -f "$SRC/AutoPotion.dll" "$DEST/"
cp -f "$SRC/AutoPotion.deps.json" "$DEST/"
cp -f "$SRC/AutoPotion/AutoPotion.json" "$DEST/"

echo
echo "Deployed to $DEST"
echo "Reload the plugin in /xlplugins (Disable then Enable) or relaunch the game."
