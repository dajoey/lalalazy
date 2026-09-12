#region

using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Numerics;
using GluttonyCombo.API.Enum;
using GluttonyCombo.Combos.PvE;
using GluttonyCombo.CustomComboNS.Functions;
using GluttonyCombo.Data;
using GluttonyCombo.Services.IPC_Subscriber;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;

#endregion

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     Dalamud half of SmartMover (fork, t_356159a8, v1.0.4.191): builds the
///     <see cref="SmartMoverCore.MoverWorld"/> snapshot from live game state,
///     hands it to the pure engine, and drives vnavmesh with the verdict.
/// </summary>
/// <remarks>
///     Danger zones are DERIVED from enemy cast data (see <see cref="DangerZoneModel"/>);
///     BMR is never required. When BMR is present its live navigation flag is an
///     input only. The mover never stands down because BMR is installed or its
///     AI is on - the only BMR-driven pause is an ACTIVE navigation in progress,
///     which resolves in seconds and is telemetry-visible as dec=bmr.
///     Ticked from <c>AutoRotationController.Run()</c> BEFORE ProcessAutoActions
///     so movement survives rotation-gated ticks.
/// </remarks>
internal static class SmartMover
{
    private static readonly SmartMoverCore.Hysteresis Hys = new();
    private static readonly List<DangerZoneModel.Zone> Zones = new();

    // v1.0.4.193: ground-danger linger. Target-anchored telegraphs (ground
    // circles, donuts, crosses, location rects) usually leave a damaging
    // field after the cast resolves; TrackedGroundCasts remembers each such
    // cast per caster, and when the cast disappears (resolved or cancelled)
    // a resolved one is handed to Lingering for GroundLingerSec more danger.
    private static readonly Dictionary<ulong, (DangerZoneModel.Zone Zone, double ResolveAt)> TrackedGroundCasts = new();
    private static readonly DangerZoneModel.LingeringZones Lingering = new();
    private static byte lastReason;

    // Telemetry state
    private static MovementTelemetryFormat.EmitKey? _lastKey;
    private static long _lastEmitMs;

    /// <summary> Desired standing range (edge-to-edge) per role - mirrors the fork's BMR push table (GluttonyCombo.cs UpdateCaches). </summary>
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

    /// <summary> Called every framework tick (self-throttled to SmartMoverCore.TickMs). </summary>
    internal static void Tick()
    {
        try
        {
            if (!EzThrottler.Throttle("SmartMover", SmartMoverCore.TickMs))
                return;

            var cfgOn = AutoRotationController.cfg?.DPSSettings.SmartMover ?? false;

            IBattleChara? player = Player.Available ? Player.Object as IBattleChara : null;
            if (!cfgOn || player is null)
            {
                Reset();
                return;
            }

            // Tanks position bosses - the mover is for the other roles.
            if (player.GetRole() is CombatRole.Tank)
            {
                Reset();
                return;
            }

            var world = BuildWorld(player);
            var decision = SmartMoverCore.Decide(world, Hys);
            Execute(decision, world);
            Emit(world, decision);
            lastReason = decision.Reason;
        }
        catch (Exception e)
        {
            Svc.Log.Debug($"[SmartMover] tick failed: {e}");
        }
    }

    private static void Reset()
    {
        if (Hys.HasLastDest)
            StopNav();
        Hys.Reset();
        Zones.Clear();
        TrackedGroundCasts.Clear();
        Lingering.Clear();
    }

    internal static void Shutdown()
    {
        StopNav();
        Hys.Reset();
        Zones.Clear();
        TrackedGroundCasts.Clear();
        Lingering.Clear();
    }

    /// <summary>
    ///     Policy A export (t_8d711ea6): whether the mover is actively dodging -
    ///     its last decision was a dodge and the mover is still enabled. The
    ///     rotation side must not fire movement abilities mid-dodge: the dodge
    ///     destination is safety-chosen and a dash would override it.
    /// </summary>
    internal static bool IsDodging =>
        (AutoRotationController.cfg?.DPSSettings.SmartMover ?? false) &&
        lastReason == SmartMoverCore.ReasonDodgeCode;

    /// <summary>
    ///     Policy A export (t_8d711ea6): whether <paramref name="point"/> lies
    ///     inside any live danger zone plus the configured buffer. The zone list
    ///     only populates while the mover is on; with the mover off every point
    ///     answers safe.
    /// </summary>
    internal static bool ZonesUnsafe(Vector3 point)
    {
        var buffer = AutoRotationController.cfg?.DPSSettings.SmartMoverDangerBufferY ?? 1f;
        return SmartMoverCore.UnsafeAt(new Vector2(point.X, point.Z), Zones, buffer) is not null;
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

    private static SmartMoverCore.MoverWorld BuildWorld(IBattleChara player)
    {
        // --- DPS target: the autorotation's own choice, independent of the hard target ---
        IBattleChara? target = null;
        try
        {
            var mode = AutoRotationController.cfg?.DPSRotationMode ?? DPSRotationMode.Manual;
            target = AutoRotationController.AutoRotationHelper.GetSingleTarget(mode);
        }
        catch { /* targeting helpers can throw mid-zone-change */ }

        // --- danger zones from live enemy casts ---
        Zones.Clear();
        var nowSec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        try { CollectZones(player, nowSec); }
        catch { /* a bad object row must not kill the tick */ }

        var casting = player.IsCasting;
        var castRemaining = casting && player.TotalCastTime > 0f
            ? MathF.Max(0f, player.TotalCastTime - player.CurrentCastTime)
            : 0f;

        var (posWanted, isRear) = PositionalWant(player, target);

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
            ManualInput: ManualMovementInput(),
            Casting: casting,
            CastRemainingSec: castRemaining,
            BmrNavigating: SafeIsBmrNavigating(),
            NavReady: NavmeshIPC.CanPathfind,
            Enabled: AutoRotationController.cfg?.DPSSettings.SmartMover ?? false,
            InCombat: InCombat(),
            DeltaSec: SmartMoverCore.TickMs / 1000f,
            NowSec: nowSec);
    }

    /// <summary> Collects telegraphed zones from every hostile currently casting, then folds in resolved ground fields that are still dangerous. </summary>
    private static void CollectZones(IBattleChara player, double nowSec)
    {
        var playerId = player.GameObjectId;
        var seenGroundCasters = new HashSet<ulong>();
        const float fallbackHalfAngleDeg = 45f; // generous, matching AIHintsBuilder's conservative cone fallback

        foreach (var obj in Svc.Objects)
        {
            if (obj is not IBattleChara bc || !bc.IsHostile() || bc.IsDead || !bc.IsCasting)
                continue;

            var actionId = bc.CastActionId;
            if (actionId == 0 || !ActionWatching.ActionSheet.TryGetValue(actionId, out var sheet))
                continue;

            // Cast target: the resolved object if any, else the caster's own position.
            var targetId = bc.CastTargetObjectId;
            IGameObject? tgtObj = null;
            if (targetId != 0)
                tgtObj = Svc.Objects.FirstOrDefault(x => x.GameObjectId == targetId);
            var castLoc = tgtObj is not null
                ? new Vector2(tgtObj.Position.X, tgtObj.Position.Z)
                : new Vector2(bc.Position.X, bc.Position.Z);

            var prim = new DangerZoneModel.CastPrimitive(
                CastType: (byte)sheet.CastType,
                EffectRange: sheet.EffectRange,
                XAxisModifier: sheet.XAxisModifier,
                HitboxRadius: bc.HitboxRadius,
                CasterPos: new Vector2(bc.Position.X, bc.Position.Z),
                CastTargetLoc: castLoc,
                CastTargetId: targetId,
                PlayerId: playerId,
                RemainingSec: bc.TotalCastTime > 0f ? MathF.Max(0f, bc.TotalCastTime - bc.CurrentCastTime) : 0.5f,
                ConeFallbackDeg: fallbackHalfAngleDeg,
                DonutInnerYalms: 0f); // unknown inner radius -> conservative full circle

            if (DangerZoneModel.BuildZone(prim) is { } z && z.RemainingSec > 0.25f)
            {
                // Dilate by the configured danger buffer so the kept margin is real.
                var buffer = AutoRotationController.cfg?.DPSSettings.SmartMoverDangerBufferY ?? 1f;
                z = z with { Radius = z.Radius + buffer, InnerRadius = MathF.Max(0f, z.InnerRadius - buffer), HalfWidth = z.HalfWidth + buffer, HalfAngle = z.HalfAngle + (buffer / MathF.Max(z.Radius, 1f)) };
                Zones.Add(z);

                // Ground-anchored telegraphs leave a field after resolution -
                // remember them so the linger feed fires when the cast ends.
                if (sheet.CastType is 2 or 10 or 11 or 12)
                {
                    TrackedGroundCasts[bc.GameObjectId] = (z with { RemainingSec = 0f }, nowSec + z.RemainingSec);
                    seenGroundCasters.Add(bc.GameObjectId);
                }
            }
        }

        // Casters tracked last tick but no longer casting that ground zone:
        // resolved ones linger as danger, interrupted ones are dropped.
        foreach (var key in TrackedGroundCasts.Keys.Except(seenGroundCasters).ToList())
        {
            var t = TrackedGroundCasts[key];
            if (nowSec >= t.ResolveAt - 0.5)
                Lingering.Add(in t.Zone, nowSec);
            TrackedGroundCasts.Remove(key);
        }

        Lingering.Sweep(nowSec);
        Lingering.AppendTo(Zones, nowSec);
    }

    /// <summary> Manual movement: keyboard wishdir via MovementHook (gamepad included). </summary>
    private static unsafe bool ManualMovementInput()
    {
        try
        {
            return MovementHook.Instance != null &&
                   (MovementHook.Instance->Wishdir_Horizontal != 0 || MovementHook.Instance->Wishdir_Vertical != 0);
        }
        catch { return false; }
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

    /// <summary>
    ///     Positional want, reusing PositionalMover's per-job phase logic.
    ///     True North suppression is handled by the engine (TrueNorth flag).
    /// </summary>
    private static (bool wanted, bool isRear) PositionalWant(IBattleChara player, IBattleChara? target)
    {
        if (target is null || Jobs.GetRoleFromJob(Player.Job) is not Jobs.JobRole.MeleeDPS)
            return (false, false);

        // Same condition as PositionalMover: don't fight a target that faces us.
        if (target.TargetObjectId == player.GameObjectId)
            return (false, false);

        var desired = PositionalMover.GetDesiredPositional();
        return desired switch
        {
            PositionalMover.DesiredPositional.Rear => (true, true),
            PositionalMover.DesiredPositional.Flank => (true, false),
            _ => (false, false),
        };
    }

    private static void Execute(SmartMoverCore.MoveDecision d, SmartMoverCore.MoverWorld w)
    {
        switch (d.Kind)
        {
            case SmartMoverCore.Decision.Stop:
                StopNav();
                break;

            case SmartMoverCore.Decision.Move:
                var y = Player.Object?.Position.Y ?? 0f;
                var dest = new Vector3(d.Dest.X, y, d.Dest.Y);

                // Danger-aware routing when a live zone sits near the straight line.
                if (AvoidCircleFor(dest, w) is { } ac && NavmeshIPC.PathfindAvoidFunc is not null)
                {
                    var from = new Vector3(w.PlayerPos.X, y, w.PlayerPos.Y);
                    List<Vector3>? path = null;
                    try { path = NavmeshIPC.PathfindAvoidFunc(from, dest, false, ac.Center, ac.Radius); }
                    catch { /* fall back to simple move */ }
                    if (path is { Count: > 0 })
                    {
                        try { NavmeshIPC.MoveToFunc?.Invoke(path, false); }
                        catch { /* fall back to simple move */ }
                        return;
                    }
                }

                NavmeshIPC.PathfindAndMoveTo(dest);
                break;
        }
    }

    /// <summary> The live zone nearest the straight player->dest corridor, as an avoid circle. </summary>
    private static (Vector3 Center, float Radius)? AvoidCircleFor(Vector3 dest, SmartMoverCore.MoverWorld w)
    {
        if (w.Zones.Count == 0)
            return null;

        var y = dest.Y;
        var from = new Vector3(w.PlayerPos.X, 0f, w.PlayerPos.Y);
        var dir = dest - from;
        var len = dir.Length();
        if (len < 0.01f) return null;
        dir /= len;

        (Vector3, float)? best = null;
        var bestDist = float.MaxValue;
        foreach (var z in w.Zones)
        {
            var c = new Vector3(z.Origin.X, 0f, z.Origin.Y);
            var to = c - from;
            var along = Vector3.Dot(to, dir);
            if (along < -2f || along > len + 2f) continue;
            var lateral = (to - dir * along).Length();
            var radius = ZoneRadius(z);
            if (lateral > radius + 4f) continue;
            if (lateral < bestDist) { bestDist = lateral; best = (new Vector3(c.X, y, c.Z), radius + 1f); }
        }
        return best;
    }

    private static float ZoneRadius(DangerZoneModel.Zone z) => z.Kind switch
    {
        DangerZoneModel.ShapeKind.Circle => z.Radius,
        DangerZoneModel.ShapeKind.Donut => z.Radius,
        DangerZoneModel.ShapeKind.Rect => MathF.Max(z.Radius, z.HalfWidth),
        DangerZoneModel.ShapeKind.ChargeRect => z.HalfWidth,
        DangerZoneModel.ShapeKind.Cone => z.Radius,
        DangerZoneModel.ShapeKind.Cross => MathF.Max(z.Radius, z.HalfWidth),
        _ => 3f,
    };

    private static void Emit(SmartMoverCore.MoverWorld w, SmartMoverCore.MoveDecision d)
    {
        if (!(AutoRotationController.cfg?.DPSSettings.MovementTelemetry ?? false))
            return;

        try
        {
            var reason = ReasonString(d.Reason);
            if (reason is "off" or "nav")
                return; // plugin-off states are not decisions

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            float? dstX = d.Kind == SmartMoverCore.Decision.Move ? d.Dest.X : null;
            float? dstZ = d.Kind == SmartMoverCore.Decision.Move ? d.Dest.Y : null;

            var distPast = 0f;
            if (w.TargetEngaged)
            {
                var dist = Vector2.Distance(w.PlayerPos, w.TargetPos) - w.TargetHitboxRadius;
                distPast = dist - w.DesiredRange;
            }

            var key = MovementTelemetryFormat.KeyOf(reason, dstX, dstZ);
            var isDodgeStart = reason == "ddg" && ReasonString(lastReason) != "ddg";

            if (!MovementTelemetryFormat.ShouldEmit(_lastKey, _lastEmitMs, nowMs, key, isDodgeStart))
                return;

            var job = (byte)Player.Job;
            var tgtId = w.TargetEngaged ? TargetDataId() : 0u;
            var line = MovementTelemetryFormat.BuildLine(nowMs, job, reason, tgtId, distPast, w.Zones.Count, dstX, dstZ);
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

    internal static string ReasonString(byte code) => code switch
    {
        SmartMoverCore.ReasonOffCode => "off",
        SmartMoverCore.ReasonNavCode => "nav",
        SmartMoverCore.ReasonManualCode => "man",
        SmartMoverCore.ReasonCastCode => "cast",
        SmartMoverCore.ReasonBmrCode => "bmr",
        SmartMoverCore.ReasonDodgeCode => "ddg",
        SmartMoverCore.ReasonEngageCode => "eng",
        SmartMoverCore.ReasonSettleCode => "stl",
        _ => "stl",
    };

    /// <summary> Telemetry reset (toggle-on) so the current state reports immediately. </summary>
    internal static void ResetTelemetry()
    {
        _lastKey = null;
        _lastEmitMs = 0;
    }
}
