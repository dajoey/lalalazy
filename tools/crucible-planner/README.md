# Crucible Planner — patch-refresh runbook

The web planner (`docs/crucible`) and the in-game LazyCrucible advisor share one
data pipeline. When Square Enix ships a Beastmaster / Crucible balance pass, re-run
this chain and commit the regenerated artifacts together.

Pinned baseline: FFXIV **7.56** (`f5af21155b99a524`). Use `--version latest` (or the
new game-version key from `https://v2.xivapi.com/api/version`) only when you intend
to absorb a real sheet change.

Expected runtime on jobunthree (xivapi rate-limit ~0.3 s/call): **~30–60 s** for the
two fetches, then a few seconds for gen / golden / export / node test.

## Commands (in order)

From the repo root, on branch `feat/crucible-planner` (or `main` after merge):

```bash
# 1. Head — xivapi -> raw sheet dumps used by build.py
python3 tools/crucible-planner/sheets/fetch_sheets.py
# optional scratch proof:
#   python3 tools/crucible-planner/sheets/fetch_sheets.py --out /tmp/crucible-raw
#   (copy or symlink raw/ into a scratch build dir, run build.py there)

# 2. Rebuild the research map from raw/ and diff against the committed map
python3 tools/crucible-planner/sheets/build.py
git diff --exit-code tools/crucible-planner/sheets/crucible_map.json

# 3. Advisor / web slices (same version pin)
python3 tools/bst-crucible/fetch_slices.py
git diff --exit-code \
  tools/bst-crucible/crucible_sheets_7.56.json \
  tools/bst-crucible/crucible_beasts_7.56.json

# 4. Tail — C# tables -> golden -> web data -> node test
python3 tools/bst-crucible/gen_crucible_data.py
~/.dotnet/dotnet run -c Release --project tests/CruciblePlanner.Golden -- \
  tests/CruciblePlanner.Golden/golden.json
python3 tools/crucible-planner/export_planner_data.py
node --test docs/crucible/test/advisor.test.mjs

# 5. Tree should be clean except docs/crucible/data/version.json (exportedAt)
git status
```

CI (`.github/workflows/crucible-planner.yml`) already re-runs the tail on every push.

## When a diff is real (balance patch landed)

1. Stop. Do not force the old numbers back.
2. Bump the slice filename suffix (`7.56` → the new patch, e.g. `7.56x1`) in
   `fetch_slices.py --suffix`, `gen_crucible_data.py`, and
   `export_planner_data.py`.
3. Review enemy / beast diffs (stars, resists, weakness, familiar stats).
4. Re-grade the advisor on changed fights with the LazyCrucible harness.
5. Commit **slices + generated C# + golden.json + docs/crucible/data/** together.
6. Merge; the GitHub Pages site republishes from `docs/crucible`.

## Files this runbook owns

| Path | Role |
|---|---|
| `tools/crucible-planner/sheets/fetch_sheets.py` | xivapi → `raw/*.json` + `VERSION.json` |
| `tools/crucible-planner/sheets/build.py` | `raw/` → `crucible_map.json` / enums / detection |
| `tools/bst-crucible/fetch_slices.py` | xivapi → `crucible_sheets_*.json` + `crucible_beasts_*.json` |
| `tools/bst-crucible/gen_crucible_data.py` | slices → `BST_CrucibleData.Generated.cs` |
| `tests/CruciblePlanner.Golden` | C# advisor → `golden.json` |
| `tools/crucible-planner/export_planner_data.py` | slices → `docs/crucible/data/*.json` |
