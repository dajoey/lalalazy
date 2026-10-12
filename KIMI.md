# KIMI.md: rules for Kimi's pull requests

This file is for the Kimi agent. Read it at the start of every session; it changes over time. Where it differs from anything said in chat, this file wins.

Other AI agents already work on lalalazy every day. My orchestrator (Werkrby) fixes bugs and ships builds, and a nightly job merges upstream changes into the forked plugins. Releases are built, packaged and pushed only from a Windows build host you cannot reach. Your lane is improvement work delivered as pull requests. Fleet agents pick up every PR from a `kimi/` branch, build it, review it, merge the good ones and ship them on the testing channel. Rejected PRs get a review comment saying why. Follow these rules so your work lands instead of colliding with theirs.

## 1. Read first
- Read CLAUDE.md at the repo root in full. Its rules on code, presets, Auto-Market, changelog voice and the landing site bind you. Its Release Checklist, Version Management steps and "Push host" rule are the fleet's job: sections 2 and 3 below replace them for you, and you never run them.
- Read the plugin's folder and its tests before you change it. Ignore deploy, version-bump, release and contact steps in any AGENTS.md, README or CONTRIBUTING file inside `src/` (some are stale or come from upstream). Never run `redeploy.sh` or any script that copies into a game folder.

## 2. How your work gets in
- Every change is a pull request against `main` from a branch named `kimi/<plugin>-<short-topic>`. Never push to `main`, never merge your own PR, never create tags or GitHub Releases. Open PRs ready for review, not as drafts.
- One PR = one plugin, one change. Small and reviewable beats big.
- At most 3 of your PRs open at once. When 3 are open, keep researching and queue the next one in your notes.
- Start every branch from the current `main`. To update a branch, merge `main` into it. Never force-push, never amend, never rebase a pushed branch.
- When a PR gets a review comment, push fix commits to the same branch. Each new push is reviewed again.
- If you cannot push or open a PR from your sandbox, give me a `git bundle` (or a `git format-patch` series) plus the finished PR text, and I will hand it over.
- If I give you a GitHub token, use it only for git and the PR API. Never write it into a file, commit, PR, log or reply.

## 3. Off limits
- **Release files:** `pluginmaster.json`, anything under `plugins/`, any `CHANGELOG.md`, and the version fields in any `.csproj`. The release agent writes the changelog entry together with the version bump. Put your proposed changelog bullets in the PR description.
- **Fleet tooling:** anything under `tools/` and `.github/`, plus `scheduled_merge_prompt.md`. Running `python3 tools/check-preset-ids.py .` is fine.
- **Shared and generated code:** `src/Shared/**` (it compiles into every plugin), `docs/crucible/**` and `tests/CruciblePlanner.Golden/**`.
- **Vendored upstream libraries:** `src/GluttonyCombo/ECommons`, `src/GluttonyCombo/PunishLib`, `src/GluttonyCombo/WrathCombo.API`, `src/LazyWTMath/KamiToolKit`, `src/PvPSolver/PvPSolver.Basic`, `src/PvPSolver/PvPSolver.SourceGenerators`.
- **Contracts other tools depend on:** telemetry log-line prefixes and formats (`ER|`, `RP|`, `FZ|`, `CT|` and the rest, see `docs/telemetry-lines.md`), config key names, slash-command names, IPC names, `PackageReference` versions, the `Sdk` version, `TargetFramework` and `DalamudApiLevel`.
- **Presets:** do not add, remove or renumber GluttonyCombo or PvPSolver presets, and never edit the `// Last value` comments. If an idea needs a new preset, leave it out and list it in your reply to me as a proposal.
- **Plugins themselves:** do not add or remove a plugin.
- **This file:** do not edit `KIMI.md`. Suggest changes in your reply instead.
- **Outside the repo:** never touch game install folders or plugin config files.

## 4. Stay out of the fleet's way
- Before starting a change, run `git log --since="3 days ago" --oneline -- <paths you plan to touch>`. Recent commits mean a fleet agent is working there: pick another target, or keep the change surgical and say so in the PR.
- Check open PRs and any `auto/merge-*` branches (the nightly upstream merges). Do not touch files they touch.
- GluttonyCombo, PvPSolver and LazyWTMath are forks of upstream plugins and take upstream merges every night. Everything in their folders is upstream-owned except new files and lines already marked `// Fork`. In upstream-owned files: no reformatting, renaming, reordering or style cleanup. Change only the lines the fix needs, and mark each divergence with a `// Fork:` comment saying what changed and why. Put new fork-only code in new files where you can. Never remove the LalaChangelog wiring (the `<Compile>`/`<EmbeddedResource>` csproj block, the `ChangelogGate` setup and the `changelog` command) from any plugin.
- LazyMarketCompanion: follow CLAUDE.md's Auto-Market safety doctrine exactly. Any change to vendor, list or pull decisions adds a fixture case to `tests/LazyMarketCompanion.Harness` that fails on the old code first.
- Rotation changes (GluttonyCombo, PvPSolver) need a cited source (URL plus the quoted rule) in the PR description, or a defect you can demonstrate. No retuning on opinion. PvP targeting, Guard, Purify and Limit Break timing change only on a stated source rule.

## 5. Build and test
These plugins compile on plain Linux against Dalamud's public reference assemblies. Try this first:

```
curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
bash dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p "$HOME/dalamud"
curl -fsSL https://goatcorp.github.io/dalamud-distrib/latest.zip -o /tmp/dalamud.zip
unzip -q -o /tmp/dalamud.zip -d "$HOME/dalamud"
export DALAMUD_HOME="$HOME/dalamud/"

# build one plugin: most projects are src/<Plugin>/<Plugin>.csproj;
# AutoPotion, GluttonyCombo, LazyFoodBuff, LazyHub, LazyRetainerLive, LazyWTMath and PvPSolver
# are one level deeper at src/<Plugin>/<Plugin>/<Plugin>.csproj
dotnet build <path to .csproj> -c Release -p:DalamudLibPath="$HOME/dalamud/"
# run a test harness (plain console apps under tests/, no game needed)
dotnet run --project tests/<Harness> -c Release -p:DalamudLibPath="$HOME/dalamud/"
python3 tools/check-preset-ids.py .
```

- Bug fixes: first write a harness case that fails on current `main`, then fix the cause, then show it passing. Put new logic in a pure, testable core where you can; the existing `tests/*Harness` projects show the pattern.
- Run the harnesses for every plugin you touch. Warnings are fine; errors and failures are not.
- If your sandbox cannot build (no network, no SDK), write "Not built: <reason>" in the PR. Never claim a build or test you did not run.
- You cannot run the game. Every PR names the exact thing to check in game.

## 6. Public repo: what never goes in
Code, comments, tests, commit messages, PR text and changelog bullets never contain: my name, character or retainer names, machine or host names, IP addresses, file paths from my machines, internal task or ticket ids, secrets or tokens. Changelog bullets are impersonal: they describe what the plugin does and never say "you" or "your" (CLAUDE.md, "Changelog voice").

## 7. Every PR description has these sections
- **What and why:** 2 to 4 plain sentences.
- **Files touched:** the plugin, the files, and any upstream-owned files in a fork.
- **Build and tests:** exact commands and results, or "Not built: <reason>".
- **Proposed changelog bullets:** impersonal, written for the player.
- **In-game check:** the exact thing to look at in game to confirm it works.
- **Risk:** is the new behavior on by default, and what happens if it is wrong?

## 8. Where to spend your effort
- Best value, least collision: tests for untested logic and the bugs they find; settings UI clarity and wording; crash and edge-case guards; the landing site under `docs/` (except `docs/crucible/`; follow CLAUDE.md's Pages rules, absolute icon URLs).
- Prefer plugins with little recent commit activity (check `git log`). The busiest areas, usually GluttonyCombo rotations, LazyMarketCompanion Auto-Market and LazyCrucible, get small, well-tested PRs only.
- Do not open GitHub issues, comment on issues, or contact anyone.

## Each session
Start by reading this file and checking the state of your open PRs and any review comments on them. Fix those before starting anything new.
