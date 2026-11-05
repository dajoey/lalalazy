// Fork (Fight Brain 1a): the forecast reader — what will hit the party in the next 3-30 s, built
// ONLY from code: stock BossMod Reborn's public Timeline/Hints IPC plus a plain cast scan of the
// enemies in view. It never runs in a Crucible board (that board has its own BH| reader, and the
// notebook page says no Crucible-specific work), and it never presses anything: it feeds the FB|
// log lines, the countdown strip, the cast-gate shadow check and the shadow mitigate question.
//
// Sources, most specific first (a cast hint re-times the same hit over a timeline state hint):
//   cast scan   an enemy's visible cast: exact landing time and action name (Specificity 0)
//   Hints.*     BMR's typed damage predictions: NextRaidwideDamageIn / NextTankbusterDamageIn (1)
//   Timeline.*  BMR's state-machine hints: NextRaidwideIn / NextTankbusterIn / NextKnockbackIn /
//               NextDowntimeIn (2)
// Merge/drop rules live in FightBrainForecastCore (harness-tested). IPC endpoints are those of
// BossmodReborn origin/main Framework/IPCProvider.cs; every read goes through IpcStallTrace so a
// slow BMR shows up in the BMT| trace. Absent/failing reads are neutral, never an exception per
// tick (the BossModHints latch pattern).

using Dalamud.Game.ClientState.Objects.Types;
using ECommons;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using GluttonyCombo.Extensions;
using GluttonyCombo.Services.IPC;
using Lalalazy.Crucible;
using Lalalazy.Telemetry;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace GluttonyCombo.AutoRotation;

internal static class FightBrainForecast
{
    /// <summary> One read of every source; <see cref="Empty"/> when off, silent or failing. </summary>
    internal readonly record struct Reading(bool Seen, IReadOnlyList<FightBrainHit> Hits, string Why);

    internal static readonly Reading Empty = new(false, Array.Empty<FightBrainHit>(), "off");

    /// <summary> An endpoint that threw is not asked again for this long. </summary>
    private const long RetryMs = 5000;

    /// <summary> The reader runs at most this often: 4 reads a second is far below one GCD and keeps the IPC idle cheap. </summary>
    internal const long ReadEveryMs = 250;

    /// <summary> How far out the reader keeps entries (the page's 3-30 s window; the near edge is the merge window). </summary>
    internal const float HorizonSeconds = 30f;

    private static long _retryAtMs;
    private static bool? _answering;

    // Timeline.* — BMR's state machine, seconds to the next state with each flag (float.MaxValue none).
    private static Dalamud.Plugin.Ipc.ICallGateSubscriber<float>? _tRaidwide;
    private static Dalamud.Plugin.Ipc.ICallGateSubscriber<float>? _tTankbuster;
    private static Dalamud.Plugin.Ipc.ICallGateSubscriber<float>? _tKnockback;
    private static Dalamud.Plugin.Ipc.ICallGateSubscriber<float>? _tDowntime;

    // Hints.* — BMR's typed damage predictions and the first forbidden-zone activation (cast gate).
    private static Dalamud.Plugin.Ipc.ICallGateSubscriber<float>? _hRaidwide;
    private static Dalamud.Plugin.Ipc.ICallGateSubscriber<float>? _hTankbuster;
    private static Dalamud.Plugin.Ipc.ICallGateSubscriber<Vector3, bool>? _isPositionSafe;

    /// <summary> The Fight Brain is code-only log/strip material and never runs in a Crucible board. </summary>
    internal static bool CrucibleBoard =>
        BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType) != 0;

    internal static Reading Read(long nowMs)
    {
        if (CrucibleBoard)
        {
            NoteAnswering(false, "crucible");
            return new Reading(false, Array.Empty<FightBrainHit>(), "crucible");
        }
        if (nowMs < _retryAtMs)
            return new Reading(false, Array.Empty<FightBrainHit>(), "retry");

        try
        {
            var pi = Svc.PluginInterface;
            _tRaidwide ??= pi.GetIpcSubscriber<float>("BossMod.Timeline.NextRaidwideIn");
            _tTankbuster ??= pi.GetIpcSubscriber<float>("BossMod.Timeline.NextTankbusterIn");
            _tKnockback ??= pi.GetIpcSubscriber<float>("BossMod.Timeline.NextKnockbackIn");
            _tDowntime ??= pi.GetIpcSubscriber<float>("BossMod.Timeline.NextDowntimeIn");
            _hRaidwide ??= pi.GetIpcSubscriber<float>("BossMod.Hints.NextRaidwideDamageIn");
            _hTankbuster ??= pi.GetIpcSubscriber<float>("BossMod.Hints.NextTankbusterDamageIn");
            _isPositionSafe ??= pi.GetIpcSubscriber<Vector3, bool>("BossMod.Hints.IsPositionSafe");

            var timelineOk = _tRaidwide.HasFunction && _tTankbuster.HasFunction;
            if (!timelineOk)
            {
                NoteAnswering(false, "missing");
                return new Reading(false, Array.Empty<FightBrainHit>(), "missing");
            }

            List<FightBrainHit> hits = [];
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Add(hits, FightBrainHitKind.Raidwide, "timeline", 2, ReadIn(_tRaidwide, "Timeline.NextRaidwideIn"), now);
            Add(hits, FightBrainHitKind.Tankbuster, "timeline", 2, ReadIn(_tTankbuster, "Timeline.NextTankbusterIn"), now);
            Add(hits, FightBrainHitKind.Knockback, "timeline", 2, ReadIn(_tKnockback, "Timeline.NextKnockbackIn"), now);
            Add(hits, FightBrainHitKind.Downtime, "timeline", 2, ReadIn(_tDowntime, "Timeline.NextDowntimeIn"), now);
            // The typed hints are more specific than the timeline guess for the same hit.
            Add(hits, FightBrainHitKind.Raidwide, "hints", 1, ReadIn(_hRaidwide, "Hints.NextRaidwideDamageIn"), now);
            Add(hits, FightBrainHitKind.Tankbuster, "hints", 1, ReadIn(_hTankbuster, "Hints.NextTankbusterDamageIn"), now);
            AddCastScan(hits, now);

            NoteAnswering(true, "ok");
            return new Reading(true, FightBrainForecastCore.Merge([], hits, now), "ok");
        }
        catch (Exception e)
        {
            _retryAtMs = nowMs + RetryMs;
            NoteAnswering(false, e.GetType().Name);
            return new Reading(false, Array.Empty<FightBrainHit>(), e.GetType().Name);
        }
    }

    /// <summary> The BMR answer for the danger zone under the player, for the cast-gate shadow check. </summary>
    /// <returns>Seconds until the first known zone activates (null when BMR cannot answer), and whether the player's
    /// current spot is already inside a drawn zone (IsPositionSafe on the player's own position).</returns>
    internal static (float? ZoneIn, bool StandingInZone) ZoneUnderPlayer()
    {
        if (CrucibleBoard)
            return (null, false);
        try
        {
            var pi = Svc.PluginInterface;
            _isPositionSafe ??= pi.GetIpcSubscriber<Vector3, bool>("BossMod.Hints.IsPositionSafe");
            if (_isPositionSafe is { } safe && safe.HasFunction && Player.Object is { } me)
            {
                var standing = safe.InvokeFunc(me.Position);
                return (ZoneNextActivationMs() is { } z ? (float)((z - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000.0) : null,
                        !standing);
            }
        }
        catch
        {
            // An absent or failing endpoint means the gate logs no would-block line this tick.
        }
        return (null, false);
    }

    private static long? ZoneNextActivationMs()
    {
        try
        {
            var pi = Svc.PluginInterface;
            var zone = pi.GetIpcSubscriber<float>("BossMod.Hints.ForbiddenZonesNextActivation");
            if (zone.HasFunction)
            {
                var s = IpcStallTrace.Time("Hints.ForbiddenZonesNextActivation", zone.InvokeFunc);
                if (s is > 0 and < float.MaxValue)
                    return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)(s * 1000);
            }
        }
        catch
        {
            // treated as no zone
        }
        return null;
    }

    private static float ReadIn(Dalamud.Plugin.Ipc.ICallGateSubscriber<float> sub, string name)
    {
        if (!sub.HasFunction)
            return float.MaxValue;
        var s = IpcStallTrace.Time(name, sub.InvokeFunc);
        return s is > 0 and < float.MaxValue && s <= HorizonSeconds ? s : float.MaxValue;
    }

    private static void Add(List<FightBrainHit> hits, FightBrainHitKind kind, string source, int specificity, float secondsIn, long now)
    {
        if (secondsIn >= float.MaxValue)
            return;
        hits.Add(new FightBrainHit(kind, now + (long)(secondsIn * 1000), source, specificity));
    }

    /// <summary> Enemy casts in view: exact landing time, named by the action sheet. Most specific source. </summary>
    private static void AddCastScan(List<FightBrainHit> hits, long now)
    {
        foreach (IBattleChara caster in Svc.Objects.GetBattleCharas())
        {
            if (caster is not { IsCasting: true, IsTargetable: true } || caster.IsDead || !caster.IsHostile())
                continue;
            var remaining = caster.TotalCastTime - caster.CurrentCastTime;
            if (remaining is < 0f or > HorizonSeconds)
                continue;
            uint id = caster.CastActionId;
            var name = Data.ActionWatching.ActionSheet.TryGetValue(id, out var sheet) && sheet.Name.ExtractText() is { Length: > 0 } n
                ? n.Replace('|', '/')
                : $"action {id}";
            hits.Add(new FightBrainHit(FightBrainHitKind.CastHit, now + (long)(remaining * 1000f), name, 0, id));
        }
    }

    private static void NoteAnswering(bool answering, string why)
    {
        if (_answering == answering)
            return;
        _answering = answering;
        FightBrain.Log($"forecast state={(answering ? "on" : "off")}|why={why}");
    }
}
