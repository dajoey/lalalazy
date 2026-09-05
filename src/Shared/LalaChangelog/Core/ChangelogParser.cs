// Shared source (NOT a shared DLL) - see ChangelogModels.cs. Dalamud-free on purpose.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Lalalazy.Changelog;

/// <summary>
/// Parses the lalalazy CHANGELOG.md format:
///   ## vX.Y.Z.N (YYYY-MM-DD)        (also tolerates `## [X.Y.Z.N] - YYYY-MM-DD`, a missing date, and a trailing `[testing]` tag)
///   ### Added / Changed / Fixed / Removed / Notes   (any other heading is kept as-is)
///   - bullet text, hard-wrapped continuation lines are joined into one bullet
/// Inline markdown (`code`, **bold**) is stripped so the in-game window is not a wall of markup.
/// </summary>
public static class ChangelogParser
{
    private static readonly Regex VersionHeader = new(
        @"^##\s+\[?v?(?<ver>\d+(?:\.\d+){1,3})\]?\s*(?:\((?<date>[^)]*)\))?(?:\s*-\s*(?<date2>\d{4}-\d{2}-\d{2}))?(?<tag>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SectionHeader = new(@"^###\s+(?<name>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^\s*[-*]\s+(?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex InlineCode = new(@"`([^`]*)`", RegexOptions.Compiled);
    private static readonly Regex Bold = new(@"\*\*([^*]+)\*\*", RegexOptions.Compiled);
    private static readonly Regex MdLink = new(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.Compiled);

    public static List<ChangelogEntry> Parse(string markdown)
    {
        var entries = new List<ChangelogEntry>();
        if (string.IsNullOrWhiteSpace(markdown)) return entries;

        ChangelogEntry? entry = null;
        ChangelogSection? section = null;
        StringBuilder? bullet = null;

        void FlushBullet()
        {
            if (bullet is null) return;
            var text = CleanInline(bullet.ToString());
            if (text.Length > 0)
            {
                if (entry is not null && section is null)
                {
                    section = new ChangelogSection { Name = "Notes", Kind = ChangelogSectionKind.Notes };
                    entry.Sections.Add(section);
                }
                section?.Bullets.Add(text);
            }
            bullet = null;
        }

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();

            var vm = VersionHeader.Match(line);
            if (vm.Success)
            {
                FlushBullet();
                var verText = vm.Groups["ver"].Value;
                var date = vm.Groups["date"].Success ? vm.Groups["date"].Value.Trim()
                         : vm.Groups["date2"].Success ? vm.Groups["date2"].Value.Trim() : null;
                entry = new ChangelogEntry
                {
                    Version = NormalizeVersion(verText),
                    VersionText = verText,
                    Date = string.IsNullOrEmpty(date) ? null : date,
                };
                entries.Add(entry);
                section = null;
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal) && !line.StartsWith("## ", StringComparison.Ordinal))
            {
                // Top-level title ("# Changelog") - ignore.
                FlushBullet();
                continue;
            }

            if (entry is null) continue; // preamble before the first version

            var sm = SectionHeader.Match(line);
            if (sm.Success)
            {
                FlushBullet();
                var name = sm.Groups["name"].Value.Trim();
                section = new ChangelogSection { Name = name, Kind = KindOf(name) };
                entry.Sections.Add(section);
                continue;
            }

            var bm = Bullet.Match(line);
            if (bm.Success)
            {
                FlushBullet();
                bullet = new StringBuilder(bm.Groups["text"].Value.Trim());
                continue;
            }

            if (line.Length == 0)
            {
                FlushBullet();
                continue;
            }

            // Continuation of a hard-wrapped bullet, or a bare paragraph under a section.
            if (bullet is not null)
            {
                bullet.Append(' ').Append(line.Trim());
            }
            else
            {
                bullet = new StringBuilder(line.Trim());
            }
        }

        FlushBullet();

        // Drop empty sections (e.g. an empty "### Notes" heading).
        foreach (var e in entries)
            e.Sections.RemoveAll(s => s.Bullets.Count == 0);

        return entries;
    }

    /// <summary>Entries with Version in (from, to], newest first. from == null means "everything up to and including to".</summary>
    public static List<ChangelogEntry> Between(IEnumerable<ChangelogEntry> entries, Version? from, Version to)
    {
        var list = new List<ChangelogEntry>();
        foreach (var e in entries)
        {
            if (e.Version > to) continue;
            if (from is not null && e.Version <= from) continue;
            list.Add(e);
        }
        list.Sort((a, b) => b.Version.CompareTo(a.Version));
        return list;
    }

    /// <summary>Pad to four components so 1.2 == 1.2.0.0 and comparisons never go undefined (-1 parts).</summary>
    public static Version NormalizeVersion(string text)
    {
        if (!Version.TryParse(text.Trim().TrimStart('v', 'V'), out var v)) return new Version(0, 0, 0, 0);
        return NormalizeVersion(v);
    }

    public static Version NormalizeVersion(Version v) =>
        new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));

    public static string CleanInline(string s)
    {
        s = MdLink.Replace(s, "$1");
        s = Bold.Replace(s, "$1");
        s = InlineCode.Replace(s, "$1");
        return s.Trim();
    }

    private static ChangelogSectionKind KindOf(string name) => name.ToLowerInvariant() switch
    {
        "added" => ChangelogSectionKind.Added,
        "changed" => ChangelogSectionKind.Changed,
        "fixed" => ChangelogSectionKind.Fixed,
        "removed" => ChangelogSectionKind.Removed,
        "notes" or "note" => ChangelogSectionKind.Notes,
        _ => ChangelogSectionKind.Other,
    };
}
