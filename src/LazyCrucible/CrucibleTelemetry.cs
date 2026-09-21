using System;
using System.Collections.Generic;
using System.Text;
using Lalalazy.Telemetry;

namespace LazyCrucible;

/// <summary>
///     LazyCrucible's use of the shared error reporting (src/Shared/LalaTelemetry, 2026-09-21), the Dalamud-free
///     part. PURE: compiled into tests/LazyCrucible.Harness. The live side is <see cref="CrucibleLog"/> (the one
///     emission seam) and <see cref="Plugin"/> (install, breakers, report command).
/// </summary>
internal static class CrucibleTelemetry
{
    /// <summary>
    ///     Report ring size for this plugin (the shared default is 200). Smaller, so that a report carrying the
    ///     full captures of the visible XBM screens (<see cref="MaxCaptureRecords"/>) stays inside the same
    ///     200 KB worst case as the other plugins' reports.
    /// </summary>
    public const int RingCapacity = 120;

    /// <summary> Most <c>addon</c> records the full XBM captures may use in one report. </summary>
    public const int MaxCaptureRecords = 32;

    /// <summary> Value characters per capture record (under ReportBuilder.MaxAddonValueChars, so no record is cut). </summary>
    public const int CaptureChunkChars = 1400;

    /// <summary> Name prefix of a full capture record; the generic 24-value record keeps the plain addon name. </summary>
    public const string CapturePrefix = "full:";

    /// <summary>
    ///     Stable ER| area (<c>a=</c>) for a <see cref="CrucibleLog.Error"/> call site: the site's description
    ///     lower-cased, anything but letters and digits folded to single dashes ("ReceiveEvent resolve failed" ->
    ///     "receiveevent-resolve-failed").
    /// </summary>
    public static string Area(string where)
    {
        var sb = new StringBuilder(where.Length);
        var dash = false;
        foreach (var c in where)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                if (dash && sb.Length > 0)
                    sb.Append('-');
                dash = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                dash = true;
            }
        }
        return sb.Length == 0 ? "unknown" : sb.ToString();
    }

    /// <summary>
    ///     Splits full AtkValue dumps (<c>idx:type=value;</c>, the XB| grammar) into report <c>addon</c> records of
    ///     at most <paramref name="chunkChars"/> characters, cut only between values so every record parses on its
    ///     own: <c>full:Name</c>, <c>full:Name+1</c>, ... Smaller screens go first so every visible screen is
    ///     represented; a screen cut by the <paramref name="maxRecords"/> budget ends with a
    ///     <c>full:Name+cut</c> record whose values are <c>shown=k/total</c> (records written / records needed).
    ///     Screens that no longer fit at all are left to the report's visible-addon list. PURE.
    /// </summary>
    public static List<AddonCapture> SplitCaptures(
        IEnumerable<(string Name, int ValueCount, string Values)> screens,
        int maxRecords = MaxCaptureRecords,
        int chunkChars = CaptureChunkChars)
    {
        var ordered = new List<(string Name, int ValueCount, string Values)>(screens);
        ordered.Sort((a, b) =>
        {
            var byLength = a.Values.Length.CompareTo(b.Values.Length);
            return byLength != 0 ? byLength : string.CompareOrdinal(a.Name, b.Name);
        });

        var result = new List<AddonCapture>();
        foreach (var (name, count, values) in ordered)
        {
            var remaining = maxRecords - result.Count;
            if (remaining <= 0)
                break;

            var chunks = Chunk(values, chunkChars);
            if (chunks.Count <= remaining)
            {
                for (var i = 0; i < chunks.Count; i++)
                    result.Add(new AddonCapture(RecordName(name, i), count, chunks[i]));
                continue;
            }

            // Cut: as many records as fit, keeping one slot for the marker.
            var shown = remaining - 1;
            for (var i = 0; i < shown; i++)
                result.Add(new AddonCapture(RecordName(name, i), count, chunks[i]));
            result.Add(new AddonCapture(CapturePrefix + name + "+cut", count, $"shown={shown}/{chunks.Count}"));
        }
        return result;
    }

    private static string RecordName(string name, int part) =>
        part == 0 ? CapturePrefix + name : CapturePrefix + name + "+" + part;

    /// <summary> Pieces of at most <paramref name="max"/> characters, each ending on a value separator when one exists. </summary>
    internal static List<string> Chunk(string values, int max)
    {
        var chunks = new List<string>();
        if (string.IsNullOrEmpty(values))
        {
            chunks.Add(string.Empty);
            return chunks;
        }

        var start = 0;
        while (start < values.Length)
        {
            var len = Math.Min(max, values.Length - start);
            if (start + len < values.Length)
            {
                var cut = values.LastIndexOf(';', start + len - 1, len);
                if (cut >= start)
                    len = cut - start + 1; // keep the separator with its value
            }
            chunks.Add(values.Substring(start, len));
            start += len;
        }
        return chunks;
    }
}

/// <summary>
///     Which formatted telemetry lines reach the in-memory report ring (they are always written to the log as
///     before). The screen recorder can write dozens of lines a second, so without a cap it would push the
///     familiar-selection decisions out of a 120-line ring within seconds:
///     <list type="bullet">
///         <item><c>PS|</c> (selection decisions): always.</item>
///         <item><c>PSP|</c> (pet-party / notebook events): at most 5/s, burst 40.</item>
///         <item><c>XB+|</c> (continuation chunks of a screen dump): never; the first <c>XB|</c> chunk marks the
///             change, and a report carries the full values of every visible XBM screen anyway.</item>
///         <item>everything else (<c>XV| XB| XC| XR| XE| XA| XO| XK|</c> and notes): at most 3/s, burst 30.</item>
///     </list>
///     Single-threaded: framework-thread ticks and game-thread hooks. PURE.
/// </summary>
internal sealed class CrucibleRingPolicy
{
    private TokenBucket _probe = new(40, 5);
    private TokenBucket _screens = new(30, 3);

    public long Kept { get; private set; }
    public long Skipped { get; private set; }

    public bool ShouldRecord(string line, long nowMs)
    {
        bool keep;
        if (line.StartsWith("PS|", StringComparison.Ordinal))
            keep = true;
        else if (line.StartsWith("XB+|", StringComparison.Ordinal))
            keep = false;
        else if (line.StartsWith("PSP|", StringComparison.Ordinal))
            keep = _probe.TryTake(nowMs);
        else
            keep = _screens.TryTake(nowMs);

        if (keep)
            Kept++;
        else
            Skipped++;
        return keep;
    }
}
