// Fork (bst1013): the per-probe runner and the named failure line of the conflicting-plugins scan. Pure,
// deliberately free of Dalamud and ECommons types: tests/GluttonyCombo.ConflictCheckHarness compiles THIS file
// together with the real LogEmitGate, so any game type leaking in breaks that build, which is the point.
//
// Why: the periodic scan wrote "[ConflictingPlugins] Periodic check failed (async plugin?)" about every 8 s for
// weeks (≈20,000 failures since 09-19) without saying which check failed; the name was only at Verbose. One
// failing BossModReborn probe also aborted every probe after it, the HasEntries combo check included.

using System;

namespace GluttonyCombo.Data.Conflicts;

internal static class ConflictProbeFailure
{
    /// <summary> Kept from the old line so existing log searches still find it. </summary>
    internal const string LinePrefix = "[ConflictingPlugins] Periodic check failed";

    /// <summary>
    ///     The throttled failure line. It names the check and the probe and carries only the exception's type, so
    ///     each distinct failing probe gets its own throttle window (LogEmitGate keys on the text) and a varying
    ///     exception message cannot defeat the gate. The message itself goes to Verbose.
    /// </summary>
    internal static string Line(string check, string? probe, Exception error) =>
        $"{LinePrefix}: {check}{(string.IsNullOrEmpty(probe) ? "" : "." + probe)} threw {error.GetType().Name}";

    /// <summary>
    ///     Runs one probe so its failure can neither abort the probes after it nor go unnamed: the exception is
    ///     handed to <paramref name="onFailure"/> with the probe's name and the probe reads <c>false</c>.
    /// </summary>
    internal static bool Run(string probe, Func<bool> check, Action<string, Exception> onFailure)
    {
        try
        {
            return check();
        }
        catch (Exception e)
        {
            onFailure(probe, e);
            return false;
        }
    }
}
