using System;
using System.Collections.Generic;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using GluttonyCombo.Core;
using GluttonyCombo.CustomComboNS.Functions;
using Lalalazy.Telemetry;

namespace GluttonyCombo.Data;

/// <summary>
///     Live half of the instant-cast audit (v1.0.4.295). Called from the <c>UseAction</c> hook for every
///     Swiftcast / Triplecast / Acceleration / Lightspeed / Occult Quick press and writes one
///     <c>IC|...</c> line naming the source and the Dualcast state at that instant. See
///     <see cref="InstantCastAuditFormat"/> for why and for the format.
///     <para/>
///     Source attribution: a call site that presses one of these itself wraps the call in
///     <see cref="Issuing"/>; a combo that substituted one onto a button leaves a note via
///     <see cref="NoteComboSubstitution"/> (valid for two seconds); a press with neither is labelled
///     unattributed - which is itself the answer ("not Gluttony's rotation code").
/// </summary>
internal static class InstantCastAudit
{
    private const long ComboNoteWindowMs = 2000;

    private static readonly Dictionary<uint, long> LastEmit = new();
    private static string? _issuer;
    private static string? _comboNote;
    private static uint _comboNoteAction;
    private static long _comboNoteTick;

    /// <summary> Marks every watched press made until the returned scope is disposed as coming from <paramref name="site"/>. </summary>
    public static IssuerScope Issuing(string site)
    {
        var previous = _issuer;
        _issuer = site;
        return new IssuerScope(previous);
    }

    internal readonly struct IssuerScope(string? previous) : IDisposable
    {
        public void Dispose() => _issuer = previous;
    }

    /// <summary> A combo just substituted <paramref name="action"/> onto a button. </summary>
    public static void NoteComboSubstitution(string preset, uint action)
    {
        if (!InstantCastAuditFormat.IsWatched(action))
            return;
        _comboNote = preset;
        _comboNoteAction = action;
        _comboNoteTick = Environment.TickCount64;
    }

    /// <summary> Called by the <c>UseAction</c> hook with the adjusted action id. Never throws. </summary>
    public static void Observe(uint actionId, ActionManager.UseActionMode mode)
    {
        try
        {
            if (!InstantCastAuditFormat.IsWatched(actionId))
                return;

            var now = Environment.TickCount64;
            if (!InstantCastAuditFormat.ShouldEmit(LastEmit, actionId, now))
                return;

            var source = _issuer;
            if (source is null && _comboNote is not null && _comboNoteAction == actionId &&
                now - _comboNoteTick <= ComboNoteWindowMs)
                source = "combo:" + _comboNote;
            source ??= InstantCastAuditFormat.Unattributed;

            var line = InstantCastAuditFormat.BuildLine(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Player.Job.ToString(),
                actionId,
                source,
                mode.ToString(),
                Snapshot());
            Svc.Log.Information(line);
            LalaTelemetry.Record(line);
        }
        catch (Exception ex)
        {
            LalaTelemetry.Swallowed("telemetry.ic", ex);
        }
    }

    private static IEnumerable<InstantCastAuditFormat.Held> Snapshot()
    {
        yield return Held("dualcast1249", CustomComboFunctions.OccultInstantCast.PhantomRedMageDualcast);
        yield return Held("dualcast5438", CustomComboFunctions.OccultInstantCast.Dualcast);
        yield return Held("quick4260", CustomComboFunctions.OccultInstantCast.OccultQuick);
        yield return Held("swiftcast167", 167);
        yield return Held("triplecast1211", 1211);
        yield return new InstantCastAuditFormat.Held("incoming",
            CustomComboFunctions.OccultDualcastIncoming ? 1f : null);
        yield return new InstantCastAuditFormat.Held("moving", CustomComboFunctions.IsMoving() ? 1f : null);
    }

    private static InstantCastAuditFormat.Held Held(string name, ushort status) =>
        new(name, CustomComboFunctions.HasStatusEffect(status)
            ? CustomComboFunctions.GetStatusEffectRemainingTime(status)
            : null);
}
