// Fork (1.0.4.283): BossMod Reborn IPC stall trace. Pure timing core, deliberately free of
// Dalamud and ECommons types: the offline harness
// (tests/GluttonyCombo.IpcStallTraceHarness) compiles THIS file, so any game type leaking in
// breaks that build, which is the point. GluttonyCombo.cs wires Emit to the plugin log at
// initialize and clears it at dispose; while unwired nothing is written.
//
// Why: intermittent multi-second main-thread freezes inside BossMod Reborn's draw cannot name
// their own cause. These lines make the next one do that: a `BMT|` line for every BossMod
// endpoint call over 100 ms (endpoint, duration, thread, tick) and one hitch line per
// framework-tick gap over 1 s that contains BossMod IPC activity, so a freeze can be
// attributed to a wait inside BossMod, to a slow GluttonyCombo call, or to neither.

using System; // explicit: the plugin csproj does not enable ImplicitUsings (the harness does)

namespace GluttonyCombo.Services.IPC;

internal static class IpcStallTrace
{
    /// <summary>A BossMod endpoint call slower than this writes one BMT line. Fast calls write nothing.</summary>
    internal const int SlowMsThreshold = 100;

    /// <summary>A framework-tick gap over this counts as a stall for the once-per-stall hitch line.</summary>
    internal const int HitchMsThreshold = 1000;

    /// <summary>Log sink, wired to the plugin log at initialize. Null writes nothing.</summary>
    internal static Action<string>? Emit;

    /// <summary>Milliseconds clock for tick-gap measurement. The default is the process uptime clock; the harness swaps it.</summary>
    internal static Func<long> Clock = static () => Environment.TickCount64;

    // Endpoint calls and the framework tick all run on the same thread in game, so each field
    // has a single writer and the hitch check reads its own thread's writes. No lock: this runs
    // between every tick.
    private static bool _started;        // the first OnTickStart only sets the baseline
    private static long _lastTickStart;  // Clock() at the previous framework-tick entry
    private static long _tick;           // monotonic tick counter carried on every BMT line
    private static bool _hitchOpen;      // a stall window not yet broken by a normal tick
    private static long _lastIpcEnd;     // Clock() when the most recent BossMod endpoint call finished
    private static string _lastIpcName = string.Empty; // that call's endpoint name (the hitch line names it)

    /// <summary>
    ///     First call of the framework-tick handler. Measures the gap since the previous entry;
    ///     a gap over <see cref="HitchMsThreshold"/> whose window contains a finished BossMod call
    ///     writes exactly one BMT hitch line, and nothing more until a normal tick re-arms it.
    /// </summary>
    internal static void OnTickStart()
    {
        var now = Clock();
        if (!_started)
        {
            _started = true;
            _lastTickStart = now;
            return;
        }

        var previous = _lastTickStart;
        _lastTickStart = now;
        _tick++;

        if (now - previous <= HitchMsThreshold)
        {
            _hitchOpen = false;
            return;
        }

        if (_hitchOpen || _lastIpcEnd < previous)
            return;

        _hitchOpen = true;
        Emit?.Invoke(
            $"BMT|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|ipc={_lastIpcName}|hitch|" +
            $"ms={now - previous:0}|thread={Environment.CurrentManagedThreadId}|tick={_tick}");
    }

    /// <summary>
    ///     Times one BossMod endpoint call. The value and any exception surface exactly as an
    ///     untimed call; a call over <see cref="SlowMsThreshold"/> additionally writes one BMT line.
    /// </summary>
    internal static T Time<T>(string name, Func<T> call)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return call();
        }
        finally
        {
            stopwatch.Stop();
            _lastIpcEnd = Clock();
            _lastIpcName = name;
            if (stopwatch.Elapsed.TotalMilliseconds > SlowMsThreshold)
                Emit?.Invoke(
                    $"BMT|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|ipc={name}|" +
                    $"ms={stopwatch.Elapsed.TotalMilliseconds:0}|thread={Environment.CurrentManagedThreadId}|tick={_tick}");
        }
    }

    /// <summary>Harness-only: restores pristine static state between cases. Never called in game.</summary>
    internal static void ResetForTest()
    {
        _started = false;
        _lastTickStart = 0;
        _tick = 0;
        _hitchOpen = false;
        _lastIpcEnd = 0;
        _lastIpcName = string.Empty;
        Emit = null;
        Clock = static () => Environment.TickCount64;
    }
}
