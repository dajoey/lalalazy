using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.Loader;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.GameFunctions;
using ECommons.Logging;
using GluttonyCombo.Core;
using GluttonyCombo.CustomComboNS.Functions;
using GluttonyCombo.Services.IPC_Subscriber;
using Lalalazy.Telemetry;

namespace GluttonyCombo.Data;

/// <summary>
///     The 10 Hz movement recorder (fork, 2026-11-05; decision B of the Jev Fight Brain plan,
///     part 1). While a pull is on in a PvE duty it samples, a hundred times a second times ten
///     (10 Hz): Joey's position, facing and whether a movement key is held, every party member's
///     position and facing, every casting hostile (the telegraph signal), head markers, and the
///     self/BMR activity flags. Lines go to the relay's <c>/move/ingest</c> (ffxivdb, 365-day
///     retention) - never into dalamud.log at this rate, and never through any path that could
///     move the character: this class only reads.
/// </summary>
/// <remarks>
///     Boundaries, on purpose:
///     - PvE only: a hard <c>ClientState.IsPvP</c> stop, re-checked every tick.
///     - In combat only ("during pulls"); free-roam movement is not sampled in v1.
///     - Movement keys are read as WASD + arrows through Dalamud's key state (rebinds beyond
///       those are not read in v1; the position stream still carries the truth).
///     - <see cref="ForecastHint"/> is the one join point for the Stage 1a forecast: when the
///       Fight Brain forecast lands, a one-line setter feeds its "next mechanic" in here; until
///       then the planner works from cast bars alone.
/// </remarks>
internal static class MovementRecorder
{
    private const int SampleIntervalMs = 100; // 10 Hz
    private const float MovingThresholdYalms = 0.05f;

    private static readonly object Lock = new();

    private static bool _installed;
    private static long _lastSample;
    private static uint _pullId;
    private static uint _zoneId;
    private static bool _wasInCombat;
    private static long _combatStartMs;

    /// <summary> Last sampled position per slot, for the moving flag. Slot 0 = player. </summary>
    private static readonly Vector3[] LastPos = new Vector3[MovementFormat.MaxPartySlots];
    private static readonly bool[] HasLastPos = new bool[MovementFormat.MaxPartySlots];

    /// <summary> Hazard change-gate state, cleared on combat end. </summary>
    private static readonly Dictionary<(ulong Source, uint Action), int> HazardSteps = new();

    /// <summary> Casting hostiles of the current tick, handed to the planner. </summary>
    internal readonly record struct Hazard(
        ulong SourceId, uint CastActionId, float RemainS, float TotalS,
        ulong CastTargetId, Vector3 Pos, float Rot);

    /// <summary>
    ///     The Stage 1a join point: set this to a function returning the forecast's next
    ///     mechanic for "now" (or null). Until the forecast reader lands, nobody sets it and
    ///     the planner works from cast bars alone.
    /// </summary>
    public static Func<string?>? ForecastHint { get; set; }

    /// <summary> Tick counters for the status line. </summary>
    public static long Samples { get; private set; }
    public static uint Pulls { get; private set; }

    private static BossModIPC? _bmr;
    private static AssemblyLoadContext? _alc;

    public static void Install()
    {
        lock (Lock)
        {
            if (_installed)
                return;
            _installed = true;
        }

        Svc.Framework.Update += OnTick;
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
        // Safety net for a Dispose Dalamud never calls (same pattern as src/Shared/LalaTelemetry):
        // Unloading fires when Dalamud unloads the plugin's AssemblyLoadContext.
        _alc = AssemblyLoadContext.GetLoadContext(typeof(MovementRecorder).Assembly);
        if (_alc is not null && _alc != AssemblyLoadContext.Default)
            _alc.Unloading += OnUnloading;
        try
        {
            _bmr = new BossModIPC("BossModReborn", new Version(7, 2, 5, 90));
        }
        catch (Exception ex)
        {
            // BMR absent or the endpoint shape changed: IsDashSafe answers null and the planner
            // marks its options unchecked; the recorder itself does not need BMR at all.
            LalaTelemetry.Swallowed("movement.recorder.bmr", ex);
            _bmr = null;
        }
    }

    private static void OnUnloading(AssemblyLoadContext context) => Dispose();

    public static void Dispose()
    {
        lock (Lock)
        {
            if (!_installed)
                return;
            _installed = false;
        }

        try
        {
            Svc.Framework.Update -= OnTick;
            Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
            if (_alc is not null)
                _alc.Unloading -= OnUnloading;
        }
        catch (Exception ex)
        {
            LalaTelemetry.Swallowed("movement.recorder.dispose", ex);
        }
    }

    private static void OnTerritoryChanged(uint territoryId)
    {
        _zoneId = territoryId;
        _pullId = 0;
        _wasInCombat = false;
        lock (Lock)
        {
            HazardSteps.Clear();
        }

        for (var i = 0; i < MovementFormat.MaxPartySlots; i++)
            HasLastPos[i] = false;
    }

    private static void OnTick(IFramework framework)
    {
        try
        {
            if (!Service.Configuration.MovementRecorderOn)
                return;

            // The binding rule: automated movement is PvE-only, and so is the recorder. PvP is a
            // hard stop re-checked every tick, not a one-off at install.
            if (Svc.ClientState.IsPvP || Player.Object is null || !Svc.ClientState.IsLoggedIn)
                return;

            var now = Environment.TickCount64;
            if (now - _lastSample < SampleIntervalMs)
            {
                MovementUploader.Pump(now);
                return;
            }

            _lastSample = now;

            var inCombat = Svc.Condition[ConditionFlag.InCombat];
            if (inCombat && !_wasInCombat)
            {
                _pullId++;
                Pulls++;
                _combatStartMs = now;
                lock (Lock)
                    HazardSteps.Clear();
            }

            _wasInCombat = inCombat;
            if (!inCombat)
            {
                // Between pulls: still flush whatever a pull just finished, then idle.
                MovementUploader.Pump(now);
                return;
            }

            if (_zoneId == 0)
                _zoneId = Svc.ClientState.TerritoryType;

            var hazards = SampleHazards(now);
            SampleActors(now, inCombat);

            MovementPlanner.Tick(now, _pullId, _zoneId, hazards);
            MovementUploader.Pump(now);

            if ((now - _combatStartMs) % 30000 < SampleIntervalMs)
                MovementUploader.WarnIfDown();
        }
        catch (Exception ex)
        {
            // A recorder must never be able to break the frame. Rate-limited ER|, not every tick.
            LalaTelemetry.Swallowed("movement.recorder.tick", ex);
        }
    }

    private static List<Hazard> SampleHazards(long nowMs)
    {
        var list = new List<Hazard>(4);
        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (Lock)
        {
            foreach (var obj in Svc.Objects)
            {
                if (obj.ObjectKind is not (ObjectKind.BattleNpc or ObjectKind.EventNpc) || !obj.IsHostile())
                    continue;
                if (obj is not IBattleChara cast || cast.CastActionId == 0)
                    continue;

                var remain = cast.TotalCastTime - cast.CurrentCastTime;
                var hazard = new Hazard(
                    obj.GameObjectId, cast.CastActionId, remain, cast.TotalCastTime,
                    cast.CastTargetObjectId, obj.Position, obj.Rotation);
                list.Add(hazard);
                if (!MovementFormat.HazardShouldEmit(HazardSteps, hazard.SourceId, hazard.CastActionId, remain))
                    continue;

                MovementUploader.Enqueue(MovementFormat.BuildHazardLine(
                    unixMs, _pullId, _zoneId, hazard.SourceId, hazard.CastActionId,
                    hazard.RemainS, hazard.TotalS, hazard.CastTargetId,
                    hazard.Pos.X, hazard.Pos.Y, hazard.Pos.Z, hazard.Rot));
            }
        }

        return list;
    }

    private static void SampleActors(long nowMs, bool inCombat)
    {
        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var keys = MovementKeysHeld();
        var bmrQueued = false;
        try
        {
            bmrQueued = _bmr?.HasAutomaticActionsQueuedReborn() ?? false;
        }
        catch (Exception)
        {
            // BMR IPC hiccup: flag off this tick, nothing else.
        }

        // Slot 0: Joey.
        var self = Player.Object;
        var selfCasting = self is IBattleChara sc && sc.CastActionId != 0;
        var flags = MovementFormat.FlagInCombat
                    | (IsMoving(0, self.Position) ? MovementFormat.FlagMoving : 0)
                    | (keys ? MovementFormat.FlagKeyHeld : 0)
                    | (selfCasting ? MovementFormat.FlagSelfCasting : 0)
                    | (bmrQueued ? MovementFormat.FlagBmrQueued : 0);
        MovementUploader.Enqueue(MovementFormat.BuildTickLine(
            unixMs, _pullId, _zoneId, 0, (uint)Player.Job,
            self.Position.X, self.Position.Y, self.Position.Z, self.Rotation,
            flags, Target.GetNamePlateIconId(self)));

        // Slots 1..7: the party list, in its own order; cross-world members have no GameObject.
        var slot = 1;
        foreach (var member in Svc.Party)
        {
            if (slot >= MovementFormat.MaxPartySlots)
                break;
            var obj = member.GameObject;
            if (obj is null)
            {
                slot++;
                continue;
            }

            var mFlags = MovementFormat.FlagInCombat
                         | (IsMoving(slot, obj.Position) ? MovementFormat.FlagMoving : 0);
            MovementUploader.Enqueue(MovementFormat.BuildTickLine(
                unixMs, _pullId, _zoneId, slot, obj is IPlayerCharacter pc ? pc.ClassJob : 0,
                obj.Position.X, obj.Position.Y, obj.Position.Z, obj.Rotation,
                mFlags, Target.GetNamePlateIconId(obj)));
            slot++;
        }

        Samples++;
    }

    private static bool IsMoving(int slot, Vector3 pos)
    {
        var moved = !HasLastPos[slot]
                    || Vector3.DistanceSquared(pos, LastPos[slot]) > MovingThresholdYalms * MovingThresholdYalms;
        LastPos[slot] = pos;
        HasLastPos[slot] = true;
        return moved;
    }

    private static bool MovementKeysHeld()
    {
        try
        {
            return Svc.KeyState[VirtualKey.W] || Svc.KeyState[VirtualKey.A]
                   || Svc.KeyState[VirtualKey.S] || Svc.KeyState[VirtualKey.D]
                   || Svc.KeyState[VirtualKey.UP] || Svc.KeyState[VirtualKey.DOWN]
                   || Svc.KeyState[VirtualKey.LEFT] || Svc.KeyState[VirtualKey.RIGHT];
        }
        catch (Exception)
        {
            return false;
        }
    }
}
