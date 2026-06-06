# lalalazy — Claude Development Rules

## Version Management (MANDATORY — read before ANY version change)

All four version locations MUST match in every release commit for any plugin `<PluginName>`:

1. `src/<PluginName>/<PluginName>.csproj` (or `src/<PluginName>/<PluginName>/<PluginName>.csproj`) `<Version>`
2. `pluginmaster.json` → `<PluginName>` `AssemblyVersion`
3. `plugins/<PluginName>/latest/<PluginName>.json` `AssemblyVersion` (inside the zip AND the standalone copy)
4. `src/<PluginName>/CHANGELOG.md`

### Rules

- **Read the current version from the `.csproj` file BEFORE setting any version.** Never assume the version from conversation context or from `pluginmaster.json` alone.
- **If the version you're about to write is LOWER than or EQUAL to what's there, STOP.** That's a regression. Dalamud won't offer downgrades — users get stranded.
- **After running the packaging script, check the `git diff` of `pluginmaster.json` and verify the version actually increased and did not regress.** Always perform a manual or command-line diff inspection before staging.
- **After building, verify the manifest inside the zip matches.** Extract `<PluginName>.json` from the zip and confirm `AssemblyVersion`.
- **Never use `git push --force` or `git commit --amend` on this repo.**
- **Never touch game files** (XIVLauncher installedPlugins, pluginConfigs, etc.) — only work on the repo and push. The game downloads from GitHub.

### Release Checklist

```
1. Read current csproj version
2. Increment to next version (or verify it matches the planned release version)
3. Update csproj
4. Run packaging script (e.g. Package-Plugin.ps1)
5. ** VERIFY: Run `git diff` of pluginmaster.json and confirm version has increased and did not regress **
6. ** VERIFY: extract manifest from zip, confirm version matches **
7. ** VERIFY: all four locations show the same version **
8. git add, commit, push
```

## Build

```bash
dotnet build src/GluttonyCombo/GluttonyCombo.slnx -c Release
```

- Build log is UTF-16 on Windows — use `Select-String "error CS"` (NOT grep) to check for errors.
- Stage: GluttonyCombo.dll, GluttonyCombo.json, ECommons.dll, PunishLib.dll, System.Speech.dll, WrathCombo.API.dll
- Zip with 7z: `7z a -tzip plugins/GluttonyCombo/latest/latest.zip stage/*`

## pluginmaster.json Encoding

This file has a **UTF-8 BOM**. When writing it:
- PowerShell: use `[IO.File]::WriteAllBytes` with BOM prefix, or `-Encoding UTF8` (which adds BOM in PS 5.1)
- Direct string replacement (not regex) is more reliable than regex for this file
- Always do a post-write verification read

## Test Machine

Joey tests on **dajoeybaz** (192.168.10.7, Linux/Wine) — NOT dajoeyrog. dajoeyrog is the build/repo host only.

## BLU Autorotation

- Source: `src/GluttonyCombo/GluttonyCombo/Combos/PvE/BLU/BLU_Helper.cs`
- Debug log: writes to `System.IO.Path.GetTempPath()/blu-debug.log`
- Labeled ALPHA — BLU-specific breakage is acceptable during dev; other jobs must stay untouched
- Full spell catalog in BLU_Helper.cs — every damaging BLU ability must be represented

## Landing site (`docs/`) & design system — maintenance rules

The repo ships a public landing site at **https://dajoey.github.io/lalalazy/**, served by **GitHub Pages from `main` → `/docs`** (repo Settings → Pages → Deploy from a branch → `main` / `/docs`). The green pixel-art "sleeping lalafell" look is the brand — keep it consistent.

### CRITICAL — the GitHub Pages path gotcha (do not regress this)

Because Pages serves from `/docs`, **`docs/` is the web root**. Anything *outside* `docs/` is NOT reachable by a relative path. The icons live in `LalaImages/` at the **repo root**, which Pages does **not** publish. So every icon in `docs/` is referenced by **absolute URL**:

```
https://raw.githubusercontent.com/dajoey/lalalazy/main/LalaImages/<slug>-icon.png
```

**Never** use `../LalaImages/…`, `../../LalaImages/…`, or `assets/icons/…` inside `docs/` — they 404 under Pages. (This exact bug shipped 2026-06-06 and was fixed by switching to absolute `raw.githubusercontent.com` URLs.) CSS/JS that live *inside* `docs/` stay relative.

### Layout

- `docs/index.html` — landing (hero, install box, plugin grid). The grid is generated from `docs/plugins.js` (the `PLUGINS` array).
- `docs/mods/<slug>.html` — one page per plugin. The bottom prev/next nav is a **loop in the same order as the `PLUGINS` array**.
- `docs/site.css` — layout. `docs/colors_and_type.css` — design tokens. **Do not delete `colors_and_type.css`**: `site.css` reads `--font-ui` (Noto Sans), `--font-pixel` (Pixelify Sans), `--font-mono` (JetBrains Mono), and `--gil-gold: #E8C148` from it.

### Icons / logos — the contract is `LalaImages/STYLE.md`

- Every icon: **500×500 PNG**, pixel-art sleeping lalafell on `#244A3A`, a thought bubble holding the per-mod "dream" icon, chunky pixels, 3–4 colors, **no anti-aliasing / no photoreal**. `repo-icon.png` is the bare base (no bubble).
- Generate with the kit: `LalaImages/kit/generate_logo.sh <slug> "<dream description>"` (img2img off `kit/assets/canonical-base.png`). Don't hand-drop photoreal or AI-illustrated art.
- **Size sanity check:** themed icons run ~225–415 KB. A 600 KB+ file is a red flag it's off-theme — that's how the bad Currency Spender icon was caught (a 719 KB photoreal coin purse; replaced 2026-06-06 with a 378 KB themed render).
- Single source of truth = `LalaImages/`. Both `pluginmaster.json` `IconUrl` and the landing site point at the same `raw.githubusercontent.com/.../LalaImages/<slug>-icon.png`. After replacing an icon, the raw CDN can serve the old image for a few minutes — that's cache lag, not a failed push.

### Keep the landing in sync with `pluginmaster.json`

Every shipping plugin should have a `PLUGINS` entry in `docs/plugins.js` and a `docs/mods/<slug>.html`. The slug must equal the icon basename `<slug>-icon.png`.

**Add a plugin** — touch all of: `src/<Plugin>/`, `plugins/<Plugin>/`, `pluginmaster.json` entry, `LalaImages/<slug>-icon.png` (per STYLE.md), `README.md` (table + build list), `docs/plugins.js` (`PLUGINS` entry), `docs/mods/<slug>.html`, the nav-loop neighbors, and `tools/sync-wiki.ps1` (name mapping).

**Remove a plugin** — reverse all of the above. (Done for **LazySightseeing** on 2026-06-06: removed from `src/`, `plugins/`, `pluginmaster.json`, its `LalaImages` icon, the `README` build line, and `tools/sync-wiki.ps1`.)

### Current roster (9 plugins, as of 2026-06-06)

GluttonyCombo, PvPSolver, DagobertPriceMatcher, AutoPotion, ArmoireAutoFill, LazyWTMath, LazyCurrencySpender, LazyFateAutomation, LazySkywardTracker.
