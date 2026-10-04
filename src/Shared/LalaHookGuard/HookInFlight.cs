using System;
using System.Diagnostics;
using System.Threading;

namespace Lalalazy.HookGuard;

/// <summary>
///     Counts the detours of one hook module that are running right now, so teardown can wait for them
///     before it disposes the hooks. A Dalamud hot-reload disposes a plugin's hooks while the game may still
///     be inside one of its detours on another thread; a detour that then reaches a nulled or disposed hook
///     throws out of native code and takes the game down (crash 2026-10-03: NullReferenceException escaping
///     a VFX update detour during an auto-update reload).
///     Detour: <c>using var inFlight = InFlight.Enter();</c> as the first statement, read the hook into a local,
///     fail soft when it is gone. Teardown: disable every hook, <see cref="Drain" />, then dispose and null.
///     Shared SOURCE (never a shared DLL); each plugin keeps its own copy in its own load context.
/// </summary>
public sealed class HookInFlight
{
    private int _count;
    private int _warned;

    /// <summary> Detours currently inside the module. </summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary> Mark a detour as running; dispose the token when it leaves (use <c>using var</c>). </summary>
    public Token Enter()
    {
        Interlocked.Increment(ref _count);
        return new Token(this);
    }

    /// <summary>
    ///     Wait until no detour is running, at most <paramref name="capMs" />. Returns false when the cap was
    ///     hit (a detour is stuck, or Dispose runs inside one); <paramref name="warn" /> then gets one line,
    ///     once per module, and its failures are swallowed (teardown must never throw).
    /// </summary>
    public bool Drain(string module, Action<string>? warn = null, int capMs = 1000)
    {
        var sw = Stopwatch.StartNew();
        while (Volatile.Read(ref _count) > 0)
        {
            if (sw.ElapsedMilliseconds >= capMs)
            {
                if (Interlocked.Exchange(ref _warned, 1) == 0)
                {
                    try
                    {
                        warn?.Invoke($"{module}: {Count} hook detour(s) still running after {capMs} ms, disposing anyway");
                    }
                    catch
                    {
                        // ignored: teardown path
                    }
                }

                return false;
            }

            Thread.Sleep(1);
        }

        return true;
    }

    public readonly struct Token : IDisposable
    {
        private readonly HookInFlight _owner;

        internal Token(HookInFlight owner) => _owner = owner;

        public void Dispose() => Interlocked.Decrement(ref _owner._count);
    }
}
