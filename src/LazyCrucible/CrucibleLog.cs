using ECommons.DalamudServices;

namespace LazyCrucible;

/// <summary>
///     The one place LazyCrucible writes to the Dalamud log. Telemetry lines (<c>PS|</c>, <c>PSP|</c>,
///     <c>XB|</c>, <c>XE|</c>, <c>XC|</c>) go through <see cref="Line"/>; failures through <see cref="Error"/>.
///     A shared error/report sink can be wired in here without touching the callers.
/// </summary>
internal static class CrucibleLog
{
    private const int MaxLine = 900;
    private static readonly Dictionary<string, DateTime> LastErrorAt = [];

    /// <summary> One telemetry line, truncated to the collector's 900-character limit. </summary>
    public static void Line(string line)
    {
        if (line.Length > MaxLine)
            line = line[..MaxLine];
        Svc.Log.Information(line);
    }

    /// <summary> A caught failure. The same location logs at most once a minute (these run every frame). </summary>
    public static void Error(Exception ex, string where)
    {
        var now = DateTime.UtcNow;
        if (LastErrorAt.TryGetValue(where, out var last) && now - last < TimeSpan.FromMinutes(1))
            return;
        LastErrorAt[where] = now;
        Svc.Log.Error(ex, $"[LazyCrucible] {where}");
    }

    /// <summary> A plain warning for the player-facing log (not telemetry). </summary>
    public static void Warning(string message) => Svc.Log.Warning($"[LazyCrucible] {message}");
}
