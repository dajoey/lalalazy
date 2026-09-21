using ECommons.DalamudServices;
using Lalalazy.Telemetry;

namespace LazyCrucible;

/// <summary>
///     The one place LazyCrucible writes to the Dalamud log. Telemetry lines (<c>PS|</c>, <c>PSP|</c>,
///     <c>XV|</c>, <c>XB|</c>, <c>XC|</c>, <c>XR|</c>, <c>XE|</c>, <c>XA|</c>, <c>XO|</c>, <c>XK|</c>) go through
///     <see cref="Line"/>; failures through <see cref="Error"/>. Both feed the shared error reporting
///     (src/Shared/LalaTelemetry, 2026-09-21): lines reach the report ring (capped, <see cref="CrucibleRingPolicy"/>)
///     and failures become rate-limited <c>ER|</c> lines.
/// </summary>
internal static class CrucibleLog
{
    private const int MaxLine = 900;
    private static readonly Dictionary<string, DateTime> LastErrorAt = [];

    /// <summary> What reaches the in-memory report ring (every line is still logged). </summary>
    internal static readonly CrucibleRingPolicy Ring = new();

    /// <summary>
    ///     Breaker on the agent probe's event log (<see cref="AgentProbe"/>). The familiar-selection edit latch runs
    ///     before it and is never skipped. Replaced by a hub-registered guard in <see cref="CreateGuards"/>.
    /// </summary>
    internal static TelemetryGuard ProbeHook { get; private set; } = new(null, "hook.probe", "the familiar-screen event log");

    /// <summary> Breaker on the screen recorder's hooks (callbacks, clicks, condition changes); log-only code. </summary>
    internal static TelemetryGuard RecorderHook { get; private set; } = new(null, "hook.recorder", "the screen recorder's click log");

    /// <summary> Called once, right after the error reporting is installed, so the hook breakers show in reports. </summary>
    internal static void CreateGuards()
    {
        ProbeHook = LalaTelemetry.CreateGuard("hook.probe", "the familiar-screen event log");
        RecorderHook = LalaTelemetry.CreateGuard("hook.recorder", "the screen recorder's click log");
    }

    /// <summary> One telemetry line, truncated to the collector's 900-character limit. </summary>
    public static void Line(string line)
    {
        if (line.Length > MaxLine)
            line = line[..MaxLine];
        Svc.Log.Information(line);
        if (Ring.ShouldRecord(line, Environment.TickCount64))
            LalaTelemetry.Record(line);
    }

    /// <summary>
    ///     A caught failure that the code continues past: one rate-limited <c>ER|...|kind=swallowed</c> line (WRN)
    ///     with area <see cref="CrucibleTelemetry.Area"/>(<paramref name="where"/>). Without the error reporting
    ///     (it failed to start) the same location logs at most once a minute, as before.
    /// </summary>
    public static void Error(Exception ex, string where)
    {
        if (LalaTelemetry.Hub is not null)
        {
            LalaTelemetry.Swallowed(CrucibleTelemetry.Area(where), ex);
            return;
        }

        var now = DateTime.UtcNow;
        if (LastErrorAt.TryGetValue(where, out var last) && now - last < TimeSpan.FromMinutes(1))
            return;
        LastErrorAt[where] = now;
        Svc.Log.Error(ex, $"[LazyCrucible] {where}");
    }

    /// <summary> A plain warning for the player-facing log (not telemetry). </summary>
    public static void Warning(string message) => Svc.Log.Warning($"[LazyCrucible] {message}");
}
