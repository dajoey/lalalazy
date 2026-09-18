#region

using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GluttonyCombo.API.Enum;
using GluttonyCombo.Combos.PvE;
using GluttonyCombo.CustomComboNS.Functions;
using GluttonyCombo.Data;
using GluttonyCombo.Services.IPC_Subscriber;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;
using NativeBattleChara = FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara;

#endregion

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     Dalamud half of Smart Movement v2 (2026-09 rebuild): builds the
///     <see cref="SmartMoverCore.MoverWorld"/> snapshot from live game state
///     every <see cref="SmartMoverCore.TickMs"/>, hands it to the pure planner,
///     and exposes the steering direction the <see cref="MovementHook"/> writes
///     into the game's movement input every frame. vnavmesh is used only for a
///     long approach (target outside the planning grid).
/// </summary>
/// <remarks>
///     Danger zones are DERIVED from enemy cast data (see <see cref="DangerZoneModel"/>);
///     BossMod is never required. When BossMod Reborn's AI is actively steering,
///     or vnavmesh is following a path this mover did not issue, the mover stands
///     down (dec=ext). Ticked from <c>AutoRotationController.Run()</c>.
/// </remarks>
internal static class SmartMover
{
    private static readonly SmartMoverCore.MoverState State = new();
    private static readonly List<DangerZoneModel.Zone> Zones = new();
    private static readonly DangerZoneModel.LingeringZones Lingering = new();

    // cast tracking: actor id -> (zone, zone id, resolve time) for ground-shape linger and MZ add/del lines
    private static readonly Dictionary<ulong, (DangerZoneModel.Zone Zone, ulong ZoneId, double ResolveAt, bool Ground)> Tracked = new();
    private static readonly HashSet<ulong> LoggedZoneIds = new();

    private static byte _lastReason;
    private static MovementTelemetryFormat.EmitKey? _lastKey;
    private static long _lastEmitMs;
    private static long _lastTickFailMs;
    private static bool _headerLogged;
    private static bool _ownNavIssued;
    private static bool _legacyMode;
    private static long _legacyCheckedMs;
    private static float _playerY;
    private static float _targetY;

    /// <summary> NPC casts resolve about this much later than the reported cast time (BossMod measurement). </summary>
    internal const float NpcFinishDelaySec = 0.3f;

    /// <summary> Longest cast the rotation may start right now (seconds); float.MaxValue when nothing threatens or the mover is off. </summary>
    internal static float MaxCastTime { get; private set; } = float.MaxValue;

    /// <summary>
    ///     Per-action shape overrides for casts whose sheet row cannot be
    ///     trusted (CastType 6 custom shapes, wrong ranges). Empty until a cast
    ///     has been measured from its hit positions. Key: action id.
    /// </summary>
    internal static readonly Dictionary<uint, (byte CastType, float EffectRange, float XAxisModifier)> Overrides = new();

    private static float DesiredRangeFor(Job job)
    {
        var role = Jobs.GetRoleFromJob(job);
        if (job == Job.SGE) return 5f;
        return role switch
        {
            Jobs.JobRole.MeleeDPS => 3f,
            Jobs.JobRole.Tank => 3f,
            Jobs.JobRole.Healer => 15f,
            Jobs.JobRole.RangedDPS => 20f,
            Jobs.JobRole.MagicalDPS => 20f,
            _ => 3f,
        };
    }

    /// <summary> Called every framework tick; plans every <see cref="SmartMoverCore.TickMs"/>. </summary>
    internal static void Tick()
    {
        try
        {
            if (!EzThrottler.Throttle("SmartMover", SmartMoverCore.TickMs))
                return;

            var cfgOn = AutoRotationController.cfg?.DPSSettings.SmartMover ?? false;
            IBattleChara? player = Player.Available ? Player.Object as IBattleChara : null;
            if (!cfgOn || player is null || player.GetRole() is CombatRole.Tank)
            {
                Reset();
                return;
            }

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (nowMs - _legacyCheckedMs > 1000)
            {
                _legacyCheckedMs = nowMs;
                _legacyMode = MovementHook.ReadLegacyMode();
            }
            if (!_headerLogged && (AutoRotationController.cfg?.DPSSettings.MovementTelemetry ?? false))
            {
                _headerLogged = true;
                var build = typeof(SmartMover).Assembly.GetName().Version?.ToString() ?? "?";
                Svc.Log.Information(MovementTelemetryFormat.BuildHeader(nowMs, build, CushionSec(), SmartMoverCore.RunSpeed));
            }

            var world = BuildWorld(player, nowMs);
            var decision = SmartMoverCore.Decide(world, State);
            MaxCastTime = world.Enabled && world.InCombat ? decision.MaxCastTime : float.MaxValue;
            Execute(decision);
            Emit(world, decision, nowMs);
            _lastReason = decision.Reason;
        }
        catch (Exception e)
        {
            if (AutoRotationController.cfg?.DPSSettings.MovementTelemetry ?? false)
            {
                var failMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (failMs - _lastTickFailMs > 60000)
                {
                    _lastTickFailMs = failMs;
                    Svc.Log.Information($"[SmartMover] tick failed: {e}");
                }
            }
        }
    }

    /// <summary> Per-frame steering direction (world XZ unit vector) for the movement hook, or null. </summary>
    internal static Vector2? CurrentSteer()
    {
        if (!(AutoRotationController.cfg?.DPSSettings.SmartMover ?? false))
            return null;
        if (!Player.Available)
            return null;
        var p = Player.Position;
        return SmartMoverCore.SteerDirection(State, new Vector2(p.X, p.Z));
    }

    internal static bool LegacyMode => _legacyMode;

    private static void Reset()
    {
        if (_ownNavIssued)
            StopNav();
        _ownNavIssued = false;
        State.Reset();
        Zones.Clear();
        Tracked.Clear();
        Lingering.Clear();
        LoggedZoneIds.Clear();
        MaxCastTime = float.MaxValue;
    }

    internal static void Shutdown()
    {
        Reset();
        _headerLogged = false;
    }

    /// <summary> Whether the mover is actively dodging (its last decision was a dodge) - dash gate input. </summary>
    internal static bool IsDodging =>
        (AutoRotationController.cfg?.DPSSettings.SmartMover ?? false) &&
        _lastReason is SmartMoverCore.ReasonDodgeCode or SmartMoverCore.ReasonEscapeCode;

    /// <summary>
    ///     Whether a dash landing at <paramref name="point"/> about half a
    ///     second from now lands in a telegraph that will have resolved by then
    ///     (time-aware, v2). With the mover off every point answers safe.
    /// </summary>
    internal static bool ZonesUnsafe(Vector3 point)
    {
        var buffer = AutoRotationController.cfg?.DPSSettings.SmartMoverDangerBufferY ?? 1f;
        var nowSec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        return SmartMoverCore.UnsafeAtTime(new Vector2(point.X, point.Z), Zones, buffer, nowSec, 0.5f, CushionSec());
    }

    private static float CushionSec()
    {
        var c = AutoRotationController.cfg?.DPSSettings.SmartMoverCushionSec ?? SmartMoverCore.DefaultActivationCushionSec;
        return Math.Clamp(c, 0.3f, 3f);
    }

    private static void StopNav()
    {
        try
        {
            if (NavmeshIPC.IsRunningFunc is not null && NavmeshIPC.IsRunningFunc())
                NavmeshIPC.Stop?.Invoke();
        }
        catch { /* vnavmesh gone - nothing to stop */ }
    }

    private static bool VnavRunning()
    {
        try { return NavmeshIPC.IsRunningFunc is not null && NavmeshIPC.IsRunningFunc(); }
        catch { return false; }
    }

    private static SmartMoverCore.MoverWorld BuildWorld(IBattleChara player, long nowMs)
    {
        _playerY = player.Position.Y;
        var nowSec = nowMs / 1000.0;

        IBattleChara? target = null;
        try
        {
            var mode = AutoRotationController.cfg?.DPSRotationMode ?? DPSRotationMode.Manual;
            target = AutoRotationController.AutoRotationHelper.GetSingleTarget(mode);
        }
        catch { /* targeting helpers can throw mid-zone-change */ }
        _targetY = target?.Position.Y ?? _playerY;

        Zones.Clear();
        try { CollectZones(player, nowSec, nowMs); }
        catch { /* a bad object row must not kill the tick */ }

        var casting = player.IsCasting;
        var castRemaining = casting && player.TotalCastTime > 0f ? MathF.Max(0f, player.TotalCastTime - player.CurrentCastTime) : 0f;
        var (posWanted, isRear) = PositionalWant(player, target);

        var vnavRunning = VnavRunning();
        if (_ownNavIssued && !vnavRunning)
            _ownNavIssued = false; // our approach finished or was cancelled
        var external = (vnavRunning && !_ownNavIssued) || SafeIsBmrNavigating();

        var mounted = Svc.Condition[ConditionFlag.Mounted] || Svc.Condition[ConditionFlag.InFlight] || Svc.Condition[ConditionFlag.Diving]
                      || Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent];
        var speed = HasStatusEffect(50) ? SmartMoverCore.SprintSpeed : SmartMoverCore.RunSpeed; // 50 = Sprint

        return new SmartMoverCore.MoverWorld(
            PlayerPos: new Vector2(player.Position.X, player.Position.Z),
            DesiredRange: DesiredRangeFor(Player.Job),
            PositionalWanted: posWanted,
            PositionalIsRear: isRear,
            TrueNorth: HasStatusEffect(RoleActions.Melee.Buffs.TrueNorth),
            TargetPos: target is not null ? new Vector2(target.Position.X, target.Position.Z) : default,
            TargetRotation: target?.Rotation ?? 0f,
            TargetHitboxRadius: target?.HitboxRadius ?? 0f,
            TargetEngaged: target is not null && target.IsTargetable && !target.IsDead,
            Zones: Zones,
            ManualInput: MovementHook.UserMoving,
            Casting: casting,
            CastRemainingSec: castRemaining,
            ExternalMover: external,
            NavReady: NavmeshIPC.CanPathfind,
            Enabled: true,
            InCombat: InCombat(),
            NowSec: nowSec,
            Speed: speed,
            Mounted: mounted,
            KnockbackPending: false,
            ActivationCushionSec: CushionSec(),
            IsPointWalkable: NavmeshIPC.CanPathfind ? WalkableAt : null,
            SteeringUnavailable: !MovementHook.SteeringAvailable);
    }

    /// <summary>
    ///     Collects telegraphed zones from every enemy currently casting (with
    ///     activation = now + remaining + NPC finish delay), then folds in
    ///     resolved ground fields that linger for one second.
    /// </summary>
    private static unsafe void CollectZones(IBattleChara player, double nowSec, long nowMs)
    {
        var playerId = player.GameObjectId;
        var seen = new HashSet<ulong>();
        var buffer = AutoRotationController.cfg?.DPSSettings.SmartMoverDangerBufferY ?? 1f;
        var telemetry = AutoRotationController.cfg?.DPSSettings.MovementTelemetry ?? false;

        foreach (var obj in Svc.Objects)
        {
            if (obj is not IBattleChara bc || bc.IsDead || !bc.IsCasting || !IsDangerCaster(bc))
                continue;

            var actionId = bc.CastActionId;
            if (actionId == 0 || !ActionWatching.ActionSheet.TryGetValue(actionId, out var sheet))
                continue;

            var castType = (byte)sheet.CastType;
            var effectRange = (float)sheet.EffectRange;
            var xAxis = (float)sheet.XAxisModifier;
            if (Overrides.TryGetValue(actionId, out var ov))
                (castType, effectRange, xAxis) = ov;

            var native = (NativeBattleChara*)bc.Address;
            var castInfo = native->GetCastInfo();
            var targetId = bc.CastTargetObjectId;
            var snap = castInfo->TargetLocation;

            // anchor: a target-anchored cast on an ACTOR lands on that actor (live position);
            // a location cast uses the snapshot; otherwise the caster.
            IGameObject? tgtObj = targetId != 0 && targetId != bc.GameObjectId ? Svc.Objects.FirstOrDefault(x => x.GameObjectId == targetId) : null;
            Vector2 castLoc;
            if (castType is 2 or 10 or 11 or 12 && tgtObj is not null)
                castLoc = new Vector2(tgtObj.Position.X, tgtObj.Position.Z);
            else if (snap.X != 0f || snap.Y != 0f || snap.Z != 0f)
                castLoc = new Vector2(snap.X, snap.Z);
            else if (tgtObj is not null)
                castLoc = new Vector2(tgtObj.Position.X, tgtObj.Position.Z);
            else
                castLoc = new Vector2(bc.Position.X, bc.Position.Z);

            // a charge aimed at the player cannot be dodged by pathing (BossMod rule)
            if (castType == 8 && targetId == playerId)
                continue;

            var omenPath = sheet.Omen.ValueNullable?.Path.ToString();
            var remaining = bc.TotalCastTime > 0f ? MathF.Max(0f, bc.TotalCastTime - bc.CurrentCastTime) : 0.5f;
            var prim = new DangerZoneModel.CastPrimitive(
                CastType: castType,
                EffectRange: effectRange,
                XAxisModifier: xAxis,
                HitboxRadius: bc.HitboxRadius,
                CasterPos: new Vector2(bc.Position.X, bc.Position.Z),
                CastTargetLoc: castLoc,
                CastTargetId: targetId,
                PlayerId: playerId,
                RemainingSec: remaining,
                ConeHalfAngleDeg: OmenVfxModel.CastConeHalfDeg(omenPath),
                DonutInnerYalms: OmenVfxModel.CastDonutInner(omenPath, effectRange),
                AimRotation: DangerZoneModel.GameRotationToMath(native->CastRotation));

            if (DangerZoneModel.BuildZone(prim) is not { } z)
                continue;

            var activation = nowSec + remaining + NpcFinishDelaySec;
            var castStartMs = nowMs - (long)(bc.CurrentCastTime * 1000);
            var zoneId = (bc.GameObjectId & 0xFFFFFFFF) ^ ((ulong)actionId << 32) ^ ((ulong)(castStartMs / 500) << 48);
            z = z with
            {
                Radius = z.Radius + buffer,
                InnerRadius = MathF.Max(0f, z.InnerRadius - buffer),
                HalfWidth = z.HalfWidth + buffer,
                HalfAngle = z.HalfAngle + (buffer / MathF.Max(z.Radius, 1f)),
                ActivationSec = activation,
                Source = bc.GameObjectId,
                ActionId = actionId,
            };
            Zones.Add(z);
            seen.Add(bc.GameObjectId);

            var ground = castType is 2 or 10 or 11 or 12;
            if (!Tracked.TryGetValue(bc.GameObjectId, out var prev) || prev.ZoneId != zoneId)
            {
                if (prev.ZoneId != 0 && telemetry)
                    LogZoneDel(nowMs, prev.ZoneId, "recast");
                if (telemetry)
                    LogZoneAdd(nowMs, zoneId, z, (long)(activation * 1000));
            }
            Tracked[bc.GameObjectId] = (z, zoneId, activation, ground);
        }

        // casts that ended: resolved ground shapes linger 1 s; interrupted ones are dropped
        foreach (var key in Tracked.Keys.Except(seen).ToList())
        {
            var t = Tracked[key];
            var resolved = nowSec >= t.ResolveAt - NpcFinishDelaySec - 0.5;
            if (resolved && t.Ground)
                Lingering.Add(t.Zone with { ActivationSec = 0, RemainingSec = 0f }, nowSec);
            if (telemetry)
                LogZoneDel(nowMs, t.ZoneId, resolved ? "end" : "cancel");
            Tracked.Remove(key);
        }

        Lingering.Sweep(nowSec);
        Lingering.AppendTo(Zones, nowSec);
    }

    /// <summary>
    ///     Whether a casting object's telegraphs are danger: battle NPCs of sub
    ///     kind 5 (enemy) or 11 (helper), untargetable or hostile. Pets, party
    ///     NPCs and friendly soldiers never count.
    /// </summary>
    private static unsafe bool IsDangerCaster(IBattleChara bc)
    {
        if (bc.ObjectKind != ObjectKind.BattleNpc)
            return bc.IsHostile();
        var subKind = ((NativeBattleChara*)bc.Address)->SubKind;
        if (subKind is not (5 or 11))
            return false;
        return !bc.IsTargetable || bc.IsHostile();
    }

    /// <summary>
    ///     Floor probe for the planner's coarse mask: vnavmesh PointOnFloor
    ///     asked from well ABOVE the ground so uphill terrain within the map is
    ///     found (the v1 probe at the player's own height read whole slopes as
    ///     off-mesh). Fails open.
    /// </summary>
    private static bool WalkableAt(Vector2 p)
    {
        try
        {
            if (NavmeshIPC.PointOnFloorFunc is null)
                return true;
            var y = MathF.Max(_playerY, _targetY) + 8f;
            return NavmeshIPC.PointOnFloorFunc(new Vector3(p.X, y, p.Y), false, 1f) is not null;
        }
        catch { return true; }
    }

    private static bool SafeIsBmrNavigating()
    {
        try
        {
            var f = BossModHintsIPC.IsNavigating;
            return f is not null && f();
        }
        catch { return false; }
    }

    /// <summary> Positional want, reusing PositionalMover's per-job phase logic. </summary>
    private static (bool wanted, bool isRear) PositionalWant(IBattleChara player, IBattleChara? target)
    {
        if (target is null || Jobs.GetRoleFromJob(Player.Job) is not Jobs.JobRole.MeleeDPS)
            return (false, false);
        // a target facing us turns with us; positionals are only worth chasing while it is busy (BossMod rule)
        if (target.TargetObjectId == player.GameObjectId && !target.IsCasting)
            return (false, false);
        var desired = PositionalMover.GetDesiredPositional();
        return desired switch
        {
            PositionalMover.DesiredPositional.Rear => (true, true),
            PositionalMover.DesiredPositional.Flank => (true, false),
            _ => (false, false),
        };
    }

    private static void Execute(SmartMoverCore.MoveDecision d)
    {
        switch (d.Kind)
        {
            case SmartMoverCore.Decision.Stop:
                if (_ownNavIssued)
                {
                    StopNav();
                    _ownNavIssued = false;
                }
                break;

            case SmartMoverCore.Decision.NavTo:
                try
                {
                    if (NavmeshIPC.PathfindingInProgress)
                    {
                        State.OwnNavActive = false; // retry next tick
                        break;
                    }
                    var dest = new Vector3(d.Dest.X, _targetY, d.Dest.Y);
                    if (NavmeshIPC.PathfindAndMoveTo(dest))
                        _ownNavIssued = true;
                    else
                        State.OwnNavActive = false;
                }
                catch { State.OwnNavActive = false; }
                break;

            case SmartMoverCore.Decision.Steer:
                if (_ownNavIssued)
                {
                    StopNav();
                    _ownNavIssued = false;
                }
                break;
        }
    }

    private static void LogZoneAdd(long nowMs, ulong zoneId, DangerZoneModel.Zone z, long activationMs)
    {
        try
        {
            if (!LoggedZoneIds.Add(zoneId))
                return;
            Svc.Log.Information(MovementTelemetryFormat.BuildZoneAdd(nowMs, zoneId, z.Source, z.ActionId, z.Kind.ToString().ToLowerInvariant(),
                z.Origin.X, z.Origin.Y, z.Radius, z.InnerRadius, z.HalfWidth, z.HalfAngle * 180f / MathF.PI, z.Rotation * 180f / MathF.PI, activationMs));
        }
        catch { /* never break the tick */ }
    }

    private static void LogZoneDel(long nowMs, ulong zoneId, string why)
    {
        try
        {
            LoggedZoneIds.Remove(zoneId);
            Svc.Log.Information(MovementTelemetryFormat.BuildZoneDel(nowMs, zoneId, why));
        }
        catch { /* never break the tick */ }
    }

    private static void Emit(SmartMoverCore.MoverWorld w, SmartMoverCore.MoveDecision d, long nowMs)
    {
        if (!(AutoRotationController.cfg?.DPSSettings.MovementTelemetry ?? false))
            return;
        try
        {
            var reason = SmartMoverCore.ReasonString(d.Reason);
            if (reason is "off")
                return;

            float? dstX = d.Kind is SmartMoverCore.Decision.Steer or SmartMoverCore.Decision.NavTo ? d.Dest.X : null;
            float? dstZ = d.Kind is SmartMoverCore.Decision.Steer or SmartMoverCore.Decision.NavTo ? d.Dest.Y : null;
            var distPast = 0f;
            if (w.TargetEngaged)
                distPast = Vector2.Distance(w.PlayerPos, w.TargetPos) - w.TargetHitboxRadius - w.DesiredRange;

            var key = MovementTelemetryFormat.KeyOf(reason, dstX, dstZ, w.Zones.Count);
            var isDodgeStart = d.Reason is SmartMoverCore.ReasonDodgeCode or SmartMoverCore.ReasonEscapeCode &&
                               _lastReason is not (SmartMoverCore.ReasonDodgeCode or SmartMoverCore.ReasonEscapeCode);
            if (!MovementTelemetryFormat.ShouldEmit(_lastKey, _lastEmitMs, nowMs, key, isDodgeStart, w.Zones.Count > 0))
                return;

            var line = MovementTelemetryFormat.BuildLine(nowMs, (byte)Player.Job, reason, w.TargetEngaged ? TargetDataId() : 0u, distPast,
                w.Zones.Count, dstX, dstZ, w.PlayerPos.X, w.PlayerPos.Y, w.Speed, d.LeewaySec, d.StartMaxG, State.LastPlanSteps);
            Svc.Log.Information(line);
            _lastKey = key;
            _lastEmitMs = nowMs;
        }
        catch { /* never break the tick */ }
    }

    private static uint TargetDataId()
    {
        try
        {
            var mode = AutoRotationController.cfg?.DPSRotationMode ?? DPSRotationMode.Manual;
            return AutoRotationController.AutoRotationHelper.GetSingleTarget(mode)?.DataId ?? 0u;
        }
        catch { return 0u; }
    }

    internal static string ReasonString(byte code) => SmartMoverCore.ReasonString(code);

    /// <summary> Telemetry reset (toggle-on) so the current state reports immediately. </summary>
    internal static void ResetTelemetry()
    {
        _lastKey = null;
        _lastEmitMs = 0;
        _headerLogged = false;
        LoggedZoneIds.Clear();
    }
}
