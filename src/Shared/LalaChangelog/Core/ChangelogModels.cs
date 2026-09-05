// Shared source (NOT a shared DLL): compiled into every lalalazy plugin via
//   <Compile Include="..\Shared\LalaChangelog\**\*.cs" />
// This Core/ folder is Dalamud-free so tests/LalaChangelog.Harness can compile it standalone.
using System;
using System.Collections.Generic;

namespace Lalalazy.Changelog;

/// <summary>One `## vX.Y.Z.N (YYYY-MM-DD)` block of a plugin CHANGELOG.md.</summary>
public sealed class ChangelogEntry
{
    public Version Version { get; init; } = new(0, 0, 0, 0);
    public string VersionText { get; init; } = string.Empty;
    public string? Date { get; init; }
    public List<ChangelogSection> Sections { get; } = new();

    public int BulletCount
    {
        get
        {
            var n = 0;
            foreach (var s in Sections) n += s.Bullets.Count;
            return n;
        }
    }
}

/// <summary>One `### Added|Changed|Fixed|Removed|Notes` block. Bullets are joined single lines.</summary>
public sealed class ChangelogSection
{
    public string Name { get; init; } = string.Empty;
    public ChangelogSectionKind Kind { get; init; } = ChangelogSectionKind.Other;
    public List<string> Bullets { get; } = new();
}

public enum ChangelogSectionKind
{
    Added,
    Changed,
    Fixed,
    Removed,
    Notes,
    Other,
}
