#!/usr/bin/env bash
# tools/sync-upstream-rotations.sh
#
# Pulls the latest PvP rotation files from RotationSolverReborn (upstream)
# into this fork. PvPSolver is a PvP-only fork; we share PvP rotation logic
# with upstream but diverge on infrastructure and intentionally do NOT pull
# upstream's base-class additions (which are mostly PvE evolution that this
# fork doesn't need and would likely break the build).
#
# Usage:
#   tools/sync-upstream-rotations.sh           # syncs from upstream/main
#   tools/sync-upstream-rotations.sh <branch>  # syncs from upstream/<branch>
#
# After running, review with `git diff` and commit the parts you want.
# If the new rotations reference action/status IDs the fork doesn't have,
# the build will break — pull just those symbols in manually from upstream.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

UPSTREAM_BRANCH="${1:-main}"

if ! git remote get-url upstream >/dev/null 2>&1; then
  echo "ERROR: 'upstream' remote not configured." >&2
  echo "Run: git remote add upstream https://github.com/FFXIV-CombatReborn/RotationSolverReborn.git" >&2
  exit 1
fi

echo "==> Fetching upstream/$UPSTREAM_BRANCH"
git fetch upstream "$UPSTREAM_BRANCH"

UPSTREAM_REF="upstream/$UPSTREAM_BRANCH"

# upstream_path|local_path
# Upstream has renamed this folder twice over its history:
#   BasicRotations/PVPRotations/        (oldest)
#   RebornRotations/PVPRotations/
#   RotationSolver/RebornRotations/PVPRotations/  (until 2026-11-17)
#   RotationSolver/Rotations/<Role>/<Job>/       (current, handled above)
# The [SourceCode] attribute inside each file often lags these renames.
SYNC_PAIRS=(
  "RotationSolver/RebornRotations/PVPRotations|src/PvPSolver/PvPSolver/RebornRotations/PVPRotations"
)

# Upstream reorganised again (2026-11-18, c170c9cd5d): the PvP rotations now sit beside the PvE
# ones as RotationSolver/Rotations/<Role>/<Job>/<JOB>_Default.PVP.cs (MCH: .PvP.cs). When that
# layout is present, copy just those files into the local <Role>/ folders.
NEW_ROOT="RotationSolver/Rotations"
LOCAL_PVP="src/PvPSolver/PvPSolver/RebornRotations/PVPRotations"
if git cat-file -e "$UPSTREAM_REF:$NEW_ROOT" 2>/dev/null; then
  echo "==> Syncing $NEW_ROOT/<Role>/<Job>/*_Default.PVP.cs -> $LOCAL_PVP/<Role>/"
  rm -rf "$LOCAL_PVP"
  mkdir -p "$LOCAL_PVP"
  count=0
  while IFS= read -r f; do
    rel="${f#"$NEW_ROOT"/}"
    role="${rel%%/*}"
    mkdir -p "$LOCAL_PVP/$role"
    git show "$UPSTREAM_REF:$f" > "$LOCAL_PVP/$role/${f##*/}"
    # upstream's namespace is now RotationSolver.Rotations.<Role>.<Job>; this fork keeps
    # RotationSolver.RebornRotations.PVPRotations.<Role> (the line carries no BOM-sensitive anchor).
    sed -i -E "s/namespace RotationSolver\.Rotations\.[A-Za-z]+\.[A-Za-z]+;/namespace RotationSolver.RebornRotations.PVPRotations.$role;/" "$LOCAL_PVP/$role/${f##*/}"
    count=$((count + 1))
  done < <(git ls-tree -r --name-only "$UPSTREAM_REF" -- "$NEW_ROOT" | grep -iE '^RotationSolver/Rotations/[^/]+/[^/]+/[^/]*_Default\.PVP\.cs$')
  echo "    copied $count PvP rotation files"
  SYNC_PAIRS=()
fi

for pair in "${SYNC_PAIRS[@]}"; do
  upstream_path="${pair%%|*}"
  local_path="${pair##*|}"

  echo "==> Syncing $upstream_path -> $local_path"

  if ! git cat-file -e "$UPSTREAM_REF:$upstream_path" 2>/dev/null; then
    echo "    SKIP: $upstream_path not present at $UPSTREAM_REF"
    echo "    (upstream may have moved this folder again — update SYNC_PAIRS)"
    continue
  fi

  # strip-components = number of components in upstream_path
  strip="$(echo "$upstream_path" | awk -F/ '{print NF}')"

  rm -rf "$local_path"
  mkdir -p "$local_path"
  git archive "$UPSTREAM_REF" -- "$upstream_path" \
    | tar -x --strip-components="$strip" -C "$local_path"
done

echo
echo "==> Done. Review with:"
echo "    git status"
echo "    git diff --stat"
echo
echo "Notes:"
echo "  - Namespaces stay as RotationSolver.RebornRotations.PVPRotations.* —"
echo "    this fork keeps that namespace deliberately to minimize merge friction."
echo "  - If the build breaks, upstream rotations may reference new ActionID/"
echo "    StatusID symbols not yet in this fork. Pull just those symbols from"
echo "    upstream's RotationSolver.Basic/ folder by hand."
echo "  - Base classes (RotationSolver.Basic/Rotations/Basic/*Rotation.cs) are"
echo "    intentionally NOT auto-synced; they accumulate PvE evolution this"
echo "    fork doesn't need."
