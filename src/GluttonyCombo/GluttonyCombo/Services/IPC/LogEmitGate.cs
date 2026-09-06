using System;
using System.Collections.Generic;

namespace GluttonyCombo.Services.IPC;

/// <summary>
///     Pure, Dalamud-free emit gate for the IPC log channel.
/// </summary>
/// <remarks>
///     <para>
///         Exists because an exception thrown inside a per-frame ImGui draw path logs once
///         per rendered frame. On 2026-09-05 a single bad
///         <see cref="AutoRotationConfigOption" /> name did exactly that and wrote
///         <b>4,257</b> identical <c>[ERR]</c> lines in ~26 minutes - peaking at 3,461 lines
///         in one minute - which drowns the log this fleet greps for release evidence.
///     </para>
///     <para>
///         The gate allows <see cref="Burst" /> lines per <see cref="Window" /> per distinct
///         message, and reports how many identical lines it dropped on the first emit of the
///         next window, so the frequency is still visible without the volume. The level is
///         deliberately NOT demoted: the line is wanted, the rate is the bug.
///     </para>
///     <para>
///         Kept free of Dalamud and of <see cref="DateTime" />.Now so
///         <c>tests/GluttonyCombo.IpcLogGateHarness</c> can replay a measured frame stream
///         through the exact code that ships and assert the resulting line count.
///     </para>
/// </remarks>
internal sealed class LogEmitGate(TimeSpan window, int burst, int maxTrackedKeys = 512)
{
    /// <summary>Hard cap on the identifying prefix of a message.</summary>
    private const int KeyLength = 200;

    /// <summary>Message lines that form the identity of a message.</summary>
    /// <remarks>
    ///     Line 1 is the call site's own text and line 2 is the exception's summary - which is
    ///     what carries the offending value, keeping
    ///     <c>Requested value 'IncludeShields' was not found</c> distinct from
    ///     <c>...'TankbustersBeyondParty'...</c>. Everything from line 3 on is stack frames and
    ///     other per-occurrence detail; including it would let varying trailing text make every
    ///     line a unique key and defeat the gate entirely - i.e. reproduce the very bug this
    ///     class exists to prevent. The harness asserts that property.
    /// </remarks>
    private const int KeyLines = 2;

    private readonly Dictionary<string, KeyState> _keys = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <summary>Lines allowed per <see cref="Window" />, per distinct message.</summary>
    public int Burst { get; } = burst < 1 ? 1 : burst;

    /// <summary>Length of the rate-limiting window.</summary>
    public TimeSpan Window { get; } = window;

    /// <summary>Number of distinct messages currently tracked.</summary>
    public int TrackedKeys
    {
        get
        {
            lock (_lock) return _keys.Count;
        }
    }

    /// <summary>
    ///     Decides whether <paramref name="message" /> should be written now.
    /// </summary>
    /// <param name="message">The message about to be logged.</param>
    /// <param name="nowUtc">Caller-supplied clock, so this is replayable offline.</param>
    /// <param name="suppressed">
    ///     How many identical lines were dropped since this message last emitted. Non-zero
    ///     only on the first emit after a run of drops; the caller appends it to the line.
    /// </param>
    /// <returns><c>true</c> to write the line, <c>false</c> to drop it.</returns>
    public bool ShouldEmit(string message, DateTime nowUtc, out int suppressed)
    {
        suppressed = 0;
        var key = KeyFor(message);

        lock (_lock)
        {
            if (!_keys.TryGetValue(key, out var state))
            {
                // A pathological number of distinct messages must not grow the map without
                // bound. Dropping the whole map is fine: worst case one extra burst is let
                // through, which is far cheaper than leaking memory from a logging path.
                if (_keys.Count >= maxTrackedKeys)
                    _keys.Clear();

                _keys[key] = new KeyState(nowUtc, 1, 0);
                return true;
            }

            // Window still open: emit while under burst, otherwise count the drop.
            if (nowUtc - state.WindowStart < Window)
            {
                if (state.EmittedInWindow < Burst)
                {
                    _keys[key] = state with { EmittedInWindow = state.EmittedInWindow + 1 };
                    return true;
                }

                _keys[key] = state with { SuppressedSinceEmit = state.SuppressedSinceEmit + 1 };
                return false;
            }

            // Window rolled over: emit, and hand back everything dropped meanwhile.
            suppressed = state.SuppressedSinceEmit;
            _keys[key] = new KeyState(nowUtc, 1, 0);
            return true;
        }
    }

    /// <summary>
    ///     Renders the note appended to a line that follows suppressed duplicates.
    ///     Empty when nothing was dropped, so the common case is untouched.
    /// </summary>
    public string SuppressedNote(int suppressed) =>
        suppressed <= 0
            ? string.Empty
            : $" [+{suppressed} identical line(s) suppressed in the previous " +
              $"{Window.TotalSeconds:0}s]";

    /// <summary>
    ///     Reduces a message to its identity: the first <see cref="KeyLines" /> lines, capped
    ///     at <see cref="KeyLength" /> characters.
    /// </summary>
    private static string KeyFor(string message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        var end = message.Length < KeyLength ? message.Length : KeyLength;
        var newlines = 0;
        for (var i = 0; i < end; i++)
        {
            if (message[i] != '\n')
                continue;
            if (++newlines < KeyLines)
                continue;
            end = i;
            break;
        }

        return message[..end];
    }

    private readonly record struct KeyState(
        DateTime WindowStart,
        int EmittedInWindow,
        int SuppressedSinceEmit);
}
