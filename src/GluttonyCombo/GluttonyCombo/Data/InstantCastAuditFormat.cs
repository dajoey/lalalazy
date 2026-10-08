using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GluttonyCombo.Data;

/// <summary>
///     The pure half of the instant-cast audit line (v1.0.4.295): which actions are watched, the line
///     format and the per-action throttle, with no Dalamud/game types, so
///     <c>tests/GluttonyCombo.PhantomDualcastHarness</c> asserts the exact shape that ships.
///     <see cref="InstantCastAudit"/> is the live half that reads the game state and calls in here.
///     <para/>
///     Why it exists: a Swiftcast pressed while a Dualcast was up left no trace in the plugin log. The
///     combo telemetry only sees presses that go through a combo's button replacement; the auto-rez
///     press, and anything another plugin or the player sends, never appeared, so "who issued it" could
///     not be answered. This line is written at the one place every press passes - the
///     <c>UseAction</c> hook - and names the source.
/// </summary>
internal static class InstantCastAuditFormat
{
    /// <summary> Fixed, greppable line prefix: <c>message LIKE 'IC|%'</c> in ffxivdb. </summary>
    public const string Prefix = "IC|";

    /// <summary> Swiftcast. </summary>
    public const uint Swiftcast = 7561;

    /// <summary> Black Mage's Triplecast. </summary>
    public const uint Triplecast = 7421;

    /// <summary> Red Mage's Acceleration. </summary>
    public const uint Acceleration = 7518;

    /// <summary> Astrologian's Lightspeed. </summary>
    public const uint Lightspeed = 3606;

    /// <summary> Phantom Time Mage's Occult Quick. </summary>
    public const uint OccultQuick = 41625;

    /// <summary> Minimum gap between two lines for the same action (a held button re-asks every frame). </summary>
    public const long ThrottleMs = 400;

    /// <summary> Source label when nothing in the plugin claimed the press. </summary>
    public const string Unattributed = "unattributed (hotbar press, queued input or another plugin)";

    /// <summary> The actions that buy an instant cast and so get a line. </summary>
    public static bool IsWatched(uint actionId) =>
        actionId is Swiftcast or Triplecast or Acceleration or Lightspeed or OccultQuick;

    /// <summary>
    ///     Per-action throttle. Returns true (and records <paramref name="nowMs"/>) when a line for
    ///     <paramref name="actionId"/> may be written now.
    /// </summary>
    public static bool ShouldEmit(Dictionary<uint, long> lastEmit, uint actionId, long nowMs)
    {
        if (lastEmit.TryGetValue(actionId, out var last) && nowMs - last < ThrottleMs)
            return false;
        lastEmit[actionId] = nowMs;
        return true;
    }

    /// <summary> One status in the snapshot: its name, and remaining seconds (null = not held). </summary>
    internal readonly record struct Held(string Name, float? Remaining);

    /// <summary>
    ///     <c>IC|unixms|job|actionId|actionName|source|mode|name=remaining;name=-;...</c>
    ///     Remaining is seconds with one decimal; <c>-</c> means not held.
    /// </summary>
    internal static string BuildLine(
        long unixMs, string job, uint actionId, string source, string mode, IEnumerable<Held> held)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(160);
        sb.Append(Prefix)
          .Append(unixMs).Append('|')
          .Append(job).Append('|')
          .Append(actionId).Append('|')
          .Append(NameOf(actionId)).Append('|')
          .Append(source).Append('|')
          .Append(mode).Append('|');

        var first = true;
        foreach (var h in held)
        {
            if (!first)
                sb.Append(';');
            first = false;
            sb.Append(h.Name).Append('=');
            if (h.Remaining is { } r)
                sb.Append(r.ToString("F1", inv));
            else
                sb.Append('-');
        }

        return sb.ToString();
    }

    internal static string NameOf(uint actionId) => actionId switch
    {
        Swiftcast => "Swiftcast",
        Triplecast => "Triplecast",
        Acceleration => "Acceleration",
        Lightspeed => "Lightspeed",
        OccultQuick => "OccultQuick",
        _ => actionId.ToString(CultureInfo.InvariantCulture),
    };
}
