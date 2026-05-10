#!/usr/bin/env bash
# tools/package-pvpsolver.sh
#
# Builds PvPSolver and packages it for the lalalazy custom plugin repo.
# Produces a Dalamud-compatible plugin zip with the same file shape as the
# existing production build (PvPSolver.dll, PvPSolver.Basic.dll, ECommons.dll,
# PvPSolver.json — without .pdb/.xml/.deps.json to keep the zip small).
#
# Usage:
#   tools/package-pvpsolver.sh testing            # → plugins/PvPSolver/testing/testing.zip
#   tools/package-pvpsolver.sh production         # → plugins/PvPSolver/latest/latest.zip
#   tools/package-pvpsolver.sh testing 0.2.0.3    # override AssemblyVersion in the zip's manifest
#
# After running, commit + push, then in-game switch between production and
# testing via /xlplugins → toggle "Test plugin builds" on PvP Solver.
#
# Prereqs: .NET 10 SDK on PATH, NuGet sources configured (nuget.org), python3.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$REPO_ROOT/src/PvPSolver"
MASTER="$REPO_ROOT/pluginmaster.json"

CHANNEL="${1:-}"
OVERRIDE_VERSION="${2:-}"

case "$CHANNEL" in
  testing)
    OUT_DIR="$REPO_ROOT/plugins/PvPSolver/testing"
    OUT_ZIP="$OUT_DIR/testing.zip"
    VERSION_FIELD="TestingAssemblyVersion"
    ;;
  production)
    OUT_DIR="$REPO_ROOT/plugins/PvPSolver/latest"
    OUT_ZIP="$OUT_DIR/latest.zip"
    VERSION_FIELD="AssemblyVersion"
    ;;
  *)
    echo "Usage: $0 {testing|production} [version-override]" >&2
    exit 1
    ;;
esac

if [ -n "$OVERRIDE_VERSION" ]; then
  VERSION="$OVERRIDE_VERSION"
else
  VERSION="$(python3 -c "
import json,sys
with open(sys.argv[1]) as f: m = json.load(f)
e = next(p for p in m if p['InternalName']=='PvPSolver')
print(e.get('${VERSION_FIELD}', e['AssemblyVersion']))" "$MASTER")"
fi

if [ -z "$VERSION" ]; then
  echo "ERROR: could not resolve version (no $VERSION_FIELD or AssemblyVersion in pluginmaster.json)" >&2
  exit 1
fi

echo "==> Channel:  $CHANNEL"
echo "==> Version:  $VERSION"
echo "==> Output:   $OUT_ZIP"
echo

rm -rf \
  "$SRC/PvPSolver.Basic/obj" \
  "$SRC/PvPSolver/obj" \
  "$SRC/PvPSolver.SourceGenerators/obj"

echo "==> Building..."
(cd "$SRC" && dotnet build --configuration Release --nologo --verbosity minimal)

# Stage payload from build outputs
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

# Locate built manifest
BUILT_MANIFEST=""
for cand in "$SRC/bin/Release/PvPSolver/PvPSolver.json" "$SRC/PvPSolver/bin/Release/PvPSolver.json"; do
  if [ -f "$cand" ]; then BUILT_MANIFEST="$cand"; break; fi
done
if [ -z "$BUILT_MANIFEST" ]; then
  echo "ERROR: built PvPSolver.json not found" >&2
  exit 1
fi
cp "$BUILT_MANIFEST" "$STAGE/PvPSolver.json"

# Locate built DLLs
for dll in PvPSolver.dll PvPSolver.Basic.dll ECommons.dll; do
  src_dll=""
  for cand in \
      "$SRC/bin/Release/$dll" \
      "$SRC/PvPSolver/bin/Release/$dll" \
      "$SRC/PvPSolver.Basic/bin/x64/Release/net10.0-windows10.0.26100.0/$dll"; do
    if [ -f "$cand" ]; then src_dll="$cand"; break; fi
  done
  if [ -z "$src_dll" ]; then
    echo "ERROR: could not locate built $dll" >&2
    exit 1
  fi
  cp "$src_dll" "$STAGE/$dll"
done

# Patch AssemblyVersion in staged manifest
python3 - "$STAGE/PvPSolver.json" "$VERSION" <<'PY'
import json, sys
path, version = sys.argv[1], sys.argv[2]
with open(path) as f:
    data = json.load(f)
data['AssemblyVersion'] = version
with open(path, 'w') as f:
    json.dump(data, f, indent=2)
PY

mkdir -p "$OUT_DIR"
rm -f "$OUT_ZIP"

# Zip via Python (Git Bash on Windows has no `zip`)
python3 - "$OUT_ZIP" "$STAGE" <<'PY'
import os, sys, zipfile
out, stage = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as zf:
    for name in ['PvPSolver.dll', 'PvPSolver.Basic.dll', 'ECommons.dll', 'PvPSolver.json']:
        zf.write(os.path.join(stage, name), arcname=name)
PY

cp "$STAGE/PvPSolver.json" "$OUT_DIR/PvPSolver.json"

echo
echo "==> Done."
ls -la "$OUT_DIR"
echo
echo "==> Inside zip:"
python3 -c "
import zipfile, sys
with zipfile.ZipFile(sys.argv[1]) as z:
    for i in z.infolist(): print(f'{i.file_size:>10}  {i.filename}')" "$OUT_ZIP"
