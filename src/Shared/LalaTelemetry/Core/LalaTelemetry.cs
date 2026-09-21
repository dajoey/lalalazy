// Shared source (NOT a shared DLL). Core/ is Dalamud-free.
//
// Wiring (see src/GluttonyCombo/GluttonyCombo/GluttonyCombo.cs and src/LazyMarketCompanion/Plugin.cs):
//   _telemetry = DalamudTelemetry.Install(new DalamudTelemetry.Options { ... });   // FIRST thing in the ctor
//   catch (Exception ex) { LalaTelemetry.Error("area", ex); }                       // top-level catch points
//   catch (Exception ex) { LalaTelemetry.Swallowed("area", ex); }                   // deliberate continue-past
//   LalaTelemetry.Record(line);                                                     // every XX| line formatted
//   var guard = LalaTelemetry.CreateGuard("tick", "the per-frame update");          // per-frame handlers
//   `/<cmd> report <text>` -> _telemetry.FileReport(text);   Dispose -> _telemetry.Dispose() LAST.
// Grammar of every line: docs/telemetry-lines.md.
using System;
using System.Threading;

namespace Lalalazy.Telemetry;

/// <summary>
/// Static entry point, so static classes deep inside a plugin can report without plumbing. Each plugin
/// compiles its own copy of this source and loads in its own AssemblyLoadContext, so the static state here
/// is per plugin and per load. Every call is a no-op while no hub is installed.
/// </summary>
public static class LalaTelemetry
{
    private static TelemetryHub? _hub;

    public static TelemetryHub? Hub
    {
        get => Volatile.Read(ref _hub);
        set => Volatile.Write(ref _hub, value);
    }

    /// <summary>Keeps an already-formatted telemetry line in the in-memory ring (never logs it).</summary>
    public static void Record(string? line) => Hub?.Record(line);

    /// <summary>A failure at a top-level catch point: one rate-limited ER| line at ERR (<paramref name="detail"/> -&gt; <c>d=</c>).</summary>
    public static void Error(string area, Exception ex, string? detail = null) => Hub?.Error(area, ex, detail);

    /// <summary>A failure deliberately continued past: one rate-limited ER| line at WRN (<paramref name="detail"/> -&gt; <c>d=</c>).</summary>
    public static void Swallowed(string area, Exception ex, string? detail = null) => Hub?.Swallowed(area, ex, detail);

    /// <summary>A guard bound to the current hub (or to whichever hub is installed later).</summary>
    public static TelemetryGuard CreateGuard(string area, string label, BreakerOptions? options = null)
        => Hub?.CreateGuard(area, label, options) ?? new TelemetryGuard(null, area, label, options);
}
