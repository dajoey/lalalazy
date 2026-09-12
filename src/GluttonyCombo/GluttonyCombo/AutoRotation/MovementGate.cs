#region

using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using GluttonyCombo.CustomComboNS.Functions;
using System;
using System.Numerics;

#endregion

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     Dalamud half of the movement-ability safety gate (Policy A, t_8d711ea6):
///     resolves the live inputs - SmartMover dodging state, the dash byte, the
///     danger-zone oracle, the computed landing point - and defers to the pure
///     <see cref="MovementGateCore"/>. Every auto-fired dash/gap-closer passes
///     <see cref="Allowed"/> before its per-job logic may fire.
/// </summary>
internal static class MovementGate
{
    /// <summary>
    ///     Whether the picked policy allows firing <paramref name="actionId"/> right
    ///     now, given the point the ability would deposit the character at. Gate
    ///     disabled (or config unavailable) passes everything through untouched.
    ///     The gate abstains only - it never stands the rotation down.
    /// </summary>
    internal static bool Allowed(uint actionId, Vector3 landingPoint)
    {
        var cfg = AutoRotationController.cfg;
        if (!(cfg?.DPSSettings.MovementSafetyGate ?? true))
            return true;

        var allowed = MovementGateCore.Allowed(
            true,
            SmartMover.IsDodging,
            CustomComboFunctions.IsDashing(),
            SmartMover.ZonesUnsafe(landingPoint));

        if (!allowed && EzThrottler.Throttle("MovementGateLog", 2000))
            Svc.Log.Debug($"[MovementGate] held {actionId}: dodging={SmartMover.IsDodging} " +
                $"dashing={CustomComboFunctions.IsDashing()} unsafe={SmartMover.ZonesUnsafe(landingPoint)}");

        return allowed;
    }

    /// <summary>
    ///     Where a gap-closer fired right now would deposit the character: the
    ///     target's melee ring along the player-&gt;target line. Gap-closers stop at
    ///     melee range, not on the target's hitbox centre, so checking the centre
    ///     would over-report danger; the ring is the honest landing approximation.
    /// </summary>
    internal static Vector3 GapCloserLanding()
    {
        var player = Player.Object;
        if (player is null)
            return default;

        var target = CustomComboFunctions.CurrentTarget;
        if (target is null)
            return player.Position;

        var toPlayer = player.Position - target.Position;
        var len = new Vector2(toPlayer.X, toPlayer.Z).Length();
        if (len < 0.01f)
            return target.Position;

        var ring = target.HitboxRadius + 0.5f;
        var scale = Math.Min(1f, ring / len);
        return new Vector3(
            target.Position.X + toPlayer.X * scale,
            target.Position.Y,
            target.Position.Z + toPlayer.Z * scale);
    }
}

