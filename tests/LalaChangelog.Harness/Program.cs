// LalaChangelog harness + lint. No Dalamud. Exit 0 = every plugin's CHANGELOG parses and its
// newest entry matches the csproj <Version>; exit 1 otherwise (prints one line per plugin).
using System.Text.RegularExpressions;
using Lalalazy.Changelog;

var repoRoot = args.Length > 0 ? args[0] : FindRepoRoot(Directory.GetCurrentDirectory());
if (repoRoot is null) { Console.Error.WriteLine("FAIL cannot find repo root (pass it as arg 1)"); return 1; }

var failures = 0;

// ---- unit checks on the parser itself ----
failures += Unit("header+sections+joined bullet", () =>
{
    var md = "# Changelog\n\n## v1.2.3.4 (2026-09-05)\n\n### Added\n- **Bold** thing with `code`\n  continued on next line.\n- second\n\n### Notes\n\n## v1.2.3.3 (2026-09-01)\n\n### Fixed\n- fix one\n";
    var e = ChangelogParser.Parse(md);
    Check(e.Count == 2, $"2 entries, got {e.Count}");
    Check(e[0].Version == new Version(1, 2, 3, 4), $"newest version {e[0].Version}");
    Check(e[0].Date == "2026-09-05", $"date {e[0].Date}");
    Check(e[0].Sections.Count == 1, $"empty Notes dropped, sections={e[0].Sections.Count}");
    Check(e[0].Sections[0].Kind == ChangelogSectionKind.Added, "kind Added");
    Check(e[0].Sections[0].Bullets[0] == "Bold thing with code continued on next line.", $"joined+cleaned: '{e[0].Sections[0].Bullets[0]}'");
    Check(e[0].Sections[0].Bullets.Count == 2, "two bullets");
    Check(e[1].Sections[0].Kind == ChangelogSectionKind.Fixed, "kind Fixed");
});
failures += Unit("Between(from,to] newest-first", () =>
{
    var md = "## v0.1.3.0 (x)\n- a\n## v0.1.2.0 (x)\n- b\n## v0.1.1.0 (x)\n- c\n## v0.1.0.0 (x)\n- d\n";
    var all = ChangelogParser.Parse(md);
    var r = ChangelogParser.Between(all, new Version(0, 1, 1, 0), new Version(0, 1, 3, 0));
    Check(r.Count == 2 && r[0].Version == new Version(0, 1, 3, 0) && r[1].Version == new Version(0, 1, 2, 0), $"got {string.Join(",", r.Select(x => x.VersionText))}");
    var everything = ChangelogParser.Between(all, null, new Version(0, 1, 3, 0));
    Check(everything.Count == 4, $"null from = all, got {everything.Count}");
});
failures += Unit("version normalisation", () =>
{
    Check(ChangelogParser.NormalizeVersion("v1.2") == new Version(1, 2, 0, 0), "pad 1.2");
    Check(ChangelogParser.NormalizeVersion(new Version(1, 2, 3)) == new Version(1, 2, 3, 0), "pad Version(1,2,3)");
    Check(ChangelogParser.NormalizeVersion("garbage") == new Version(0, 0, 0, 0), "garbage -> 0.0.0.0");
});
failures += Unit("keep-a-changelog header tolerated", () =>
{
    var e = ChangelogParser.Parse("## [1.0.4.99] - 2026-01-01\n- x\n");
    Check(e.Count == 1 && e[0].Version == new Version(1, 0, 4, 99) && e[0].Date == "2026-01-01", "bracket header");
});
failures += Unit("trailing [testing] tag and nested sub-bullets", () =>
{
    var e = ChangelogParser.Parse("## v1.0.4.167 (2026-09-03) [testing]\n\n### Fixed\n- top\n  - nested one\n  - nested two\n## v1.0.4.166 (2026-09-01) [testing]\n- x\n");
    Check(e.Count == 2, $"2 entries, got {e.Count}");
    Check(e[0].Version == new Version(1, 0, 4, 167) && e[0].Date == "2026-09-03", $"v/date {e[0].VersionText} {e[0].Date}");
    Check(e[0].Sections[0].Bullets.Count == 3, $"nested bullets are their own bullets: {e[0].Sections[0].Bullets.Count}");
});
failures += Unit("bare paragraph under a section becomes a bullet", () =>
{
    var e = ChangelogParser.Parse("## v1.0.0.0 (d)\n\n### Initial Release\n\nJust text here.\n");
    Check(e[0].Sections.Count == 1 && e[0].Sections[0].Bullets.Count == 1 && e[0].Sections[0].Bullets[0] == "Just text here.", "paragraph");
});

// ---- repo lint: every src/*/CHANGELOG.md vs its csproj <Version> ----
var srcDir = Path.Combine(repoRoot, "src");
var versionRx = new Regex(@"<Version>\s*([^<\s]+)\s*</Version>", RegexOptions.Compiled);
var plugins = Directory.GetDirectories(srcDir).Where(d => File.Exists(Path.Combine(d, "CHANGELOG.md"))).OrderBy(d => d).ToList();
Console.WriteLine($"lint: {plugins.Count} plugin CHANGELOGs under {srcDir}");
foreach (var dir in plugins)
{
    var name = Path.GetFileName(dir);
    var csproj = Path.Combine(dir, name + ".csproj");
    if (!File.Exists(csproj)) csproj = Path.Combine(dir, name, name + ".csproj");
    if (!File.Exists(csproj)) { Console.WriteLine($"FAIL {name}: csproj not found"); failures++; continue; }

    var csVer = versionRx.Match(File.ReadAllText(csproj)).Groups[1].Value;
    var csV = ChangelogParser.NormalizeVersion(csVer);
    var entries = ChangelogParser.Parse(File.ReadAllText(Path.Combine(dir, "CHANGELOG.md")));
    if (entries.Count == 0) { Console.WriteLine($"FAIL {name}: CHANGELOG.md yields 0 versions"); failures++; continue; }

    var newest = entries.OrderByDescending(e => e.Version).First();
    var ok = newest.Version == csV;
    var bullets = newest.BulletCount;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}: csproj {csVer}, newest CHANGELOG v{newest.VersionText} ({entries.Count} versions, newest has {bullets} bullet(s))");
    if (!ok) failures++;
    if (ok && bullets == 0) { Console.WriteLine($"FAIL {name}: newest entry v{newest.VersionText} has no bullets - nothing to show in the popup"); failures++; }
}

Console.WriteLine(failures == 0 ? "OK" : $"FAILED ({failures})");
return failures == 0 ? 0 : 1;

static int Unit(string name, Action body)
{
    try { body(); Console.WriteLine($"PASS unit: {name}"); return 0; }
    catch (Exception ex) { Console.WriteLine($"FAIL unit: {name}: {ex.Message}"); return 1; }
}
static void Check(bool cond, string msg) { if (!cond) throw new Exception(msg); }
static string? FindRepoRoot(string start)
{
    var d = new DirectoryInfo(start);
    while (d is not null)
    {
        if (File.Exists(Path.Combine(d.FullName, "pluginmaster.json"))) return d.FullName;
        d = d.Parent;
    }
    return null;
}
