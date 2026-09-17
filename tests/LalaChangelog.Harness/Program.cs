// LalaChangelog harness + lint. No Dalamud. Exit 0 = every plugin's CHANGELOG parses, its
// newest entry matches the csproj <Version>, and no published changelog text names a person,
// character, retainer, or private host; exit 1 otherwise (prints one line per finding).
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

// ---- identity lint: changelogs are product notes, not a diary (2026-09-17) ----
// Changelog text is public (Dalamud installer, in-game "What's new"). It must never name the
// author, his character or retainers, or private hosts. Terms are stored as SHA-256 of the
// lowercased word or word pair, so this file does not republish what it blocks.
failures += Unit("identity lint flags a blocked term and passes the public owner handle", () =>
{
    Check(IdentityHits("- Fixed a stall Joey reported.").Count == 1, "blocked single word not flagged");
    Check(IdentityHits("- Source: https://github.com/dajoey/lalalazy (issue #3).").Count == 0, "public repo handle flagged");
    Check(IdentityHits("- A second run re-moved stock already on the correct retainer.").Count == 0, "neutral text flagged");
});
foreach (var dir in plugins)
{
    var name = Path.GetFileName(dir);
    var lines = File.ReadAllLines(Path.Combine(dir, "CHANGELOG.md"));
    for (var i = 0; i < lines.Length; i++)
        foreach (var hit in IdentityHits(lines[i]))
        {
            Console.WriteLine($"FAIL identity {name}/CHANGELOG.md:{i + 1}: names a person, character, retainer, or private host ({hit}) - describe the plugin change, not who/where");
            failures++;
        }
    foreach (var manifest in Directory.GetFiles(dir, name + ".json", SearchOption.AllDirectories))
        failures += JsonChangelogIdentity(manifest, Path.GetRelativePath(repoRoot, manifest));
}
failures += JsonChangelogIdentity(Path.Combine(repoRoot, "pluginmaster.json"), "pluginmaster.json");

Console.WriteLine(failures == 0 ? "OK" : $"FAILED ({failures})");
return failures == 0 ? 0 : 1;

static int JsonChangelogIdentity(string path, string label)
{
    if (!File.Exists(path)) return 0;
    var bad = 0;
    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    IEnumerable<JsonElement> items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray() : new[] { doc.RootElement };
    foreach (var item in items)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("Changelog", out var cl) || cl.ValueKind != JsonValueKind.String) continue;
        var who = item.TryGetProperty("InternalName", out var n) ? n.GetString() : "";
        foreach (var hit in IdentityHits(cl.GetString() ?? ""))
        {
            Console.WriteLine($"FAIL identity {label} {who} Changelog: names a person, character, retainer, or private host ({hit})");
            bad++;
        }
    }
    return bad;
}

static List<string> IdentityHits(string text)
{
    var words = Regex.Matches(text.ToLowerInvariant(), "[a-z0-9]+").Select(m => m.Value).ToList();
    var hits = new List<string>();
    for (var i = 0; i < words.Count; i++)
    {
        if (IdentityTerms.Blocked.Contains(Sha(words[i]))) hits.Add($"word {i + 1}");
        if (i + 1 < words.Count && IdentityTerms.Blocked.Contains(Sha(words[i] + " " + words[i + 1]))) hits.Add($"words {i + 1}-{i + 2}");
    }
    return hits;
}

static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

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

// SHA-256 of blocked lowercase words / word pairs: the author's name, his character, his
// retainers, and private host names. Add a term: sha256 of the lowercased term (a single space
// between words), e.g. `printf %s 'term' | sha256sum`.
static class IdentityTerms
{
    public static readonly HashSet<string> Blocked = new(StringComparer.Ordinal)
    {
        "029bcb7ba02a3e85e3cc031353acd826c259dbaad3847628b41d412ae367a272",
        "02af68f175ccabd81354fff1a8fe031eb8a97a4cf96fab332cfaebd91ee7c825",
        "1091fdf4abc7dcb04e7c93ef3429d84300c18831ef22a6898c6db49943299580",
        "145b0ce055c693374596c6f00d053aa1913c24e3d043e28bfabba8b18ab8fa6b",
        "1c391dad296c91407461772679bf80ed9a34bbb68aa478a920669f93ef7bdea2",
        "6ca6ef23c25a6fb0d683adec83a69981ababc3d366da47c2625d0bcc16167d3d",
        "6d00339b7bc82584ec0e0d5d9e6928795bf5795be6187df81e488d73640235a1",
        "7d3c39857c98056a30987c8d6b517b010e6cdb3c3955869f3b94ce5db092a7cf",
        "8233248e356eceb1176e517fa2fc2de6bd668fb4cdfc5e8acb5c706f8c4333a8",
        "b27a7334213bc672d427e9b2d25c26606bef553d4940ce7b50c6af4dc4683dce",
        "c661fa4762181cb3fe43567f8ffbbe05141f0b79b4dc0b4f5523a1f4f063ad63",
        "fceac367216ad8d1154e929a93a5706dd81f4355fa2d22107aa61e32b3ce5016",
    };
}
