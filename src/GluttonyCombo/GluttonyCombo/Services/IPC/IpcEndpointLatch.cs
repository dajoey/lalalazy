// Fork (bst1013): per-endpoint "missing in this BossMod Reborn build" latch. Pure, deliberately free of
// Dalamud and ECommons types: tests/GluttonyCombo.BossModTargetingHarness compiles THIS file, so any game
// type leaking in breaks that build, which is the point.
//
// Why: stock BossMod Reborn does not register every endpoint Gluttony subscribes to (Hints.PriorityTarget
// never existed upstream; Configuration.SetTransient arrived in 7.5.6.34). Dalamud answers a call to an
// unregistered endpoint by throwing IpcNotReadyError, and the boss-mod targeting path made that call at the
// 50 ms targeting throttle all session. The latch turns the first such failure into one log line and stops
// calling until BossMod Reborn reloads (a new plugin instance or version is a new generation).

using System; // explicit: the plugin csproj does not enable ImplicitUsings (the harness does)

namespace GluttonyCombo.Services.IPC;

internal sealed class IpcEndpointLatch
{
    internal enum State : byte
    {
        /// <summary>Not called yet for this generation of the provider plugin.</summary>
        Unknown = 0,
        /// <summary>The last call for this generation answered.</summary>
        Present,
        /// <summary>The provider does not register the endpoint (or registers an incompatible one).</summary>
        Missing,
    }

    private string? _generation;
    private State _state;

    /// <summary>What is known about the endpoint for this provider generation (version + instance).</summary>
    internal State StateFor(string generation) =>
        string.Equals(generation, _generation, StringComparison.Ordinal) ? _state : State.Unknown;

    /// <summary>False only while the endpoint is latched missing for this generation.</summary>
    internal bool ShouldCall(string generation) => StateFor(generation) != State.Missing;

    internal void RecordSuccess(string generation)
    {
        _generation = generation;
        _state = State.Present;
    }

    /// <summary>
    ///     Records a failed call. Only an endpoint-missing error latches; anything else (an exception thrown
    ///     inside the provider, a value it could not convert) is transient and the next call goes through.
    /// </summary>
    /// <returns><c>true</c> exactly when this failure newly latched the endpoint (the caller logs once).</returns>
    internal bool RecordFailure(string generation, Exception error)
    {
        if (!IsMissingEndpointError(error))
            return false;

        var newly = StateFor(generation) != State.Missing;
        _generation = generation;
        _state = State.Missing;
        return newly;
    }

    /// <summary>
    ///     Whether a throttled re-check is due: <paramref name="lastCheckedMs"/> null means never checked (due at
    ///     once); otherwise due once <paramref name="intervalMs"/> has passed on the same millisecond clock.
    /// </summary>
    /// <remarks>
    ///     "Never" is an explicit null, not a <c>long.MinValue</c> sentinel: <c>now - long.MinValue</c> overflows to
    ///     a negative number, so a sentinel start made the throttle never pass and the settings UI never saw the
    ///     endpoint as missing.
    /// </remarks>
    internal static bool RecheckDue(long? lastCheckedMs, long nowMs, long intervalMs) =>
        lastCheckedMs is not { } last || nowMs - last >= intervalMs;

    /// <summary>
    ///     Dalamud's errors for "nothing usable is registered under this name": not registered at all
    ///     (IpcNotReadyError), or registered with other generic types or arity (IpcTypeMismatchError,
    ///     IpcLengthMismatchError). Matched by type name so this file needs no Dalamud reference; reflection
    ///     and task wrappers are unwrapped first.
    /// </summary>
    internal static bool IsMissingEndpointError(Exception error)
    {
        var e = error;
        while (e is System.Reflection.TargetInvocationException or AggregateException && e.InnerException is not null)
            e = e.InnerException;
        return e.GetType().Name is "IpcNotReadyError" or "IpcTypeMismatchError" or "IpcLengthMismatchError";
    }
}
