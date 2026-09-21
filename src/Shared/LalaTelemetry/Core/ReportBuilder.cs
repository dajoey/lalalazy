// Shared source (NOT a shared DLL). Core/ is Dalamud-free.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace Lalalazy.Telemetry;

/// <summary>
/// Builds the <c>RP|</c> problem-report block (grammar: docs/telemetry-lines.md). The block is several
/// physical lines written by ONE log call; every line is a complete <c>RP|unixms|section|id=...</c> record,
/// so it parses whether the harvester folds the continuation lines into one row (it does) or not.
/// </summary>
public static class ReportBuilder
{
    public const string Prefix = "RP|";

    /// <summary>Bumped only on an incompatible change to the section grammar.</summary>
    public const int FormatVersion = 1;

    public const int MaxTextChars = 1000;
    public const int MaxRingLineChars = 600;
    public const int MaxErrorLineChars = 2000;
    public const int MaxErrorLines = 16;
    public const int MaxSummaryChars = 4000;
    public const int MaxAddonValueChars = 1500;

    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static int _counter;

    /// <summary>
    /// Short, speakable report id: Crockford base32 of the unix second (7 chars until the year 3058) plus one
    /// char from a per-load counter, so two reports in the same second still differ. e.g. <c>1NP3K7QA</c>.
    /// </summary>
    public static string NewId(long unixMs)
    {
        var seconds = Math.Max(0, unixMs / 1000);
        var sb = new StringBuilder(8);
        for (var i = 6; i >= 0; i--)
            sb.Append(Crockford[(int)((seconds >> (5 * i)) & 31)]);
        sb.Append(Crockford[Interlocked.Increment(ref _counter) & 31]);
        return sb.ToString();
    }

    public static List<string> BuildLines(ReportInput r)
    {
        var lines = new List<string>(r.Ring.Count + r.Errors.Count + r.Addons.Count + 12);
        var t = r.UnixMs;
        var id = r.Id;
        var inv = CultureInfo.InvariantCulture;

        // Placeholder: the begin line carries the final line count, filled in at the end.
        lines.Add(string.Empty);

        lines.Add(New(t, "text", id).Field("t", r.Text, MaxTextChars).ToString());

        var g = r.Game;
        var game = New(t, "game", id)
            .Field("src", g.Source)
            .Field("at", g.CapturedUnixMs)
            .Field("tt", g.TerritoryId)
            .Field("j", g.ClassJobId)
            .Field("ja", g.JobAbbr, 8)
            .Field("lv", g.Level)
            .Field("cb", g.InCombat)
            .Field("du", g.BoundByDuty)
            .Field("li", g.LoggedIn)
            .Field("pos", g.X is not { } x ? string.Empty
                : string.Create(inv, $"{x:0.00},{g.Y ?? 0f:0.00},{g.Z ?? 0f:0.00}"))
            .Field("tgt", g.Target, 120)
            .Field("cond", string.Join(",", g.Conditions), 1000);
        lines.Add(game.ToString());

        lines.Add(New(t, "state", id).Field("s", r.State, MaxSummaryChars).ToString());
        lines.Add(New(t, "config", id).Field("s", r.Config, MaxSummaryChars).ToString());

        // Newest errors win if there are more than MaxErrorLines.
        var errStart = Math.Max(0, r.Errors.Count - MaxErrorLines);
        lines.Add(New(t, "errs", id)
            .Field("n", r.Errors.Count - errStart)
            .Field("held", r.Errors.Count)
            .Field("seen", r.ErrorTotal).ToString());
        for (var i = errStart; i < r.Errors.Count; i++)
        {
            lines.Add(New(t, "err", id)
                .Field("i", i - errStart)
                .Field("at", r.Errors[i].UnixMs)
                .Field("l", r.Errors[i].Line, MaxErrorLineChars).ToString());
        }

        lines.Add(New(t, "rings", id)
            .Field("n", r.Ring.Count)
            .Field("seen", r.RingTotal).ToString());
        for (var i = 0; i < r.Ring.Count; i++)
        {
            lines.Add(New(t, "ring", id)
                .Field("i", i)
                .Field("at", r.Ring[i].UnixMs)
                .Field("l", r.Ring[i].Line, MaxRingLineChars).ToString());
        }

        lines.Add(New(t, "addons", id)
            .Field("n", r.VisibleAddons.Count)
            .Field("visible", string.Join(",", r.VisibleAddons), 2000).ToString());
        foreach (var a in r.Addons)
        {
            lines.Add(New(t, "addon", id)
                .Field("name", a.Name, 64)
                .Field("n", a.ValueCount)
                .Field("vals", a.Values, MaxAddonValueChars).ToString());
        }

        foreach (var e in r.CollectionErrors)
            lines.Add(New(t, "note", id).Field("m", e, 300).ToString());

        var total = lines.Count + 1; // + the end line
        lines[0] = New(t, "begin", id)
            .Field("fmt", FormatVersion)
            .Field("p", r.Identity.Plugin)
            .Field("v", r.Identity.Version)
            .Field("ch", r.Identity.Channel)
            .Field("c", r.Identity.Commit)
            .Field("lines", total).ToString();
        lines.Add(New(t, "end", id).Field("lines", total).ToString());
        return lines;
    }

    private static TelemetryLineBuilder New(long unixMs, string section, string id)
        => new TelemetryLineBuilder(Prefix, unixMs, section).Field("id", id, 16);
}
