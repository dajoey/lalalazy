# lalalazy — Claude Development Rules

## Version Management (MANDATORY — read before ANY version change)

All four version locations MUST match in every release commit:

1. `src/GluttonyCombo/GluttonyCombo/GluttonyCombo.csproj` `<Version>`
2. `pluginmaster.json` → GluttonyCombo `AssemblyVersion`
3. `plugins/GluttonyCombo/latest/GluttonyCombo.json` `AssemblyVersion` (inside the zip AND the standalone copy)
4. `src/GluttonyCombo/CHANGELOG.md`

### Rules

- **Read the current version from the csproj BEFORE setting any version.** Never assume the version from conversation context.
- **If the version you're about to write is LOWER than what's there, STOP.** That's a regression. Dalamud won't offer downgrades — users get stranded.
- **After updating pluginmaster.json, READ IT BACK and verify the version actually changed.** The file has a UTF-8 BOM and inconsistent whitespace — regex replacements silently fail. Always verify with a post-write read.
- **After building, verify the manifest inside the zip matches.** Extract `GluttonyCombo.json` from the zip and confirm `AssemblyVersion`.
- **Never use `git push --force` or `git commit --amend` on this repo.**
- **Never touch game files** (XIVLauncher installedPlugins, pluginConfigs, etc.) — only work on the repo and push. The game downloads from GitHub.

### Release Checklist

```
1. Read current csproj version
2. Increment to next version
3. Update csproj
4. Build
5. Stage DLLs + manifest into zip
6. Patch manifest version in staged copy
7. Create zip
8. Update pluginmaster.json
9. ** VERIFY: read pluginmaster.json back, confirm version matches **
10. ** VERIFY: extract manifest from zip, confirm version matches **
11. ** VERIFY: all four locations show the same version **
12. git add, commit, push
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
