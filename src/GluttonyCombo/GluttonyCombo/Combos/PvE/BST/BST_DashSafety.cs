// Fork (bst1013): Shield Charge landing verdict from BossMod Reborn's dash-safety answers, and the SG| dh= label
// for why the gap-close did not fire. Pure, deliberately free of Dalamud and ECommons types (and of the rotation
// engine's types): tests/GluttonyCombo.DashSafetyHarness compiles THIS file alone, so any game type leaking in
// breaks that build, which is the point. BST_CrucibleLive.ReadDashLanding and BST.ReadState are the live halves.
//
// Why: the landing used to need BOTH the enemy's centre and the landing point at its edge to pass stock
// Hints.IsDashSafe. BMR's predicate (ActionQueue/ActionPredicate.cs IsDashSafe) rejects any point inside any
// forbidden zone whatever its activation time, and an enemy's centre usually sits inside that enemy's own zones,
// so the centre test held dashes whose landing was fine. The 2026-10-03 cases the check was added for (a dash
// into an enemy outside the arena; a landing in a puddle) are both decided at the landing point: BMR also rejects
// a point outside the arena bounds, and on a custom-shaped arena a path that crosses its wall. Stock BMR judges its
// own gap-closers at this same point (ActionPredicate.AllowDashToTarget: src + dir * DistanceToHitbox(target), i.e.
// the target's position less both hitbox radii, which is how ReadDashLanding places the landing). The centre answer
// stays as a diagnostic (dh=danger-enemy and the DS| line). Separately, overcap-only mode never gap-closes, so its
// range stalls were labelled "danger" although no dash would have fired: it now reads "overcap-only".

using System; // explicit: the plugin csproj does not enable ImplicitUsings (the harness does)

namespace GluttonyCombo.Combos.PvE;

internal static class BST_DashSafety
{
    /// <summary> Mirrors <c>BST_RotationLogic.DashLanding</c>; the live half maps it by name. </summary>
    internal enum Landing : byte
    {
        Safe = 0,
        Danger,
        Unreachable,
        Unknown,
    }

    /// <summary>
    ///     The landing verdict from BMR's two answers: the landing point (where the dash really ends) decides; the
    ///     enemy-centre answer is a diagnostic only. Null = BMR could not answer.
    /// </summary>
    internal static Landing Judge(bool? atEnemy, bool? atLanding) => atLanding switch
    {
        null => Landing.Unknown,
        true => Landing.Safe,
        false => Landing.Danger,
    };

    /// <summary> The enemy's centre is in a danger zone while the landing is not: the case the old rule held. </summary>
    internal static bool EnemyCentreOnlyDanger(bool? atEnemy, bool? atLanding) => atEnemy == false && atLanding == true;

    /// <summary>
    ///     Why the gap-close did not fire this tick (<c>SG|..|dh=</c>), ranked like the rotation's Shield Charge
    ///     gate; null when it could. <c>overcap-only</c>: the dash is configured never to gap-close.
    ///     <c>danger-landing</c>: BMR rejects the landing point (the hold). <c>danger-enemy</c>: nothing holds the
    ///     dash, but the enemy's centre is in a danger zone (diagnostic; this tick the old rule would have held).
    ///     <c>hit-imminent</c> / <c>cast-ending</c> / <c>knockback</c> / <c>settling</c> (dash-hold lane 2026-10-14): a mechanic is about to
    ///     go off or has only just gone off, so the dash waits (<c>BST_RotationLogic.DashMechanicHold</c>, passed in as
    ///     <paramref name="mechanicHold"/>); ranked after the landing verdicts, before the centre-only diagnostic.
    /// </summary>
    internal static string? HoldLabel(
        bool levelOk, bool useShieldCharge, bool overcap, bool ready, int charges, float targetDistance,
        bool protectedNear, Landing landing, bool enemyCentreOnlyDanger, string? mechanicHold = null) =>
        !levelOk ? "lvl" :
        !useShieldCharge && !overcap ? "off" :
        !useShieldCharge ? "overcap-only" :
        !ready ? (charges > 0 ? "recast" : "charges") :
        targetDistance > 20f ? "far" :
        protectedNear ? "protected" :
        landing == Landing.Danger ? "danger-landing" :
        landing == Landing.Unreachable ? "unreachable" :
        landing == Landing.Unknown ? "unknown" :
        mechanicHold is not null ? mechanicHold :
        enemyCentreOnlyDanger ? "danger-enemy" :
        null;

    /// <summary>
    ///     Whether the landing read must be made: only while a dash is actually on the table. Gap-close mode on
    ///     needs it whenever a charge is ready; overcap-only mode only at full charges, where its dash can fire.
    /// </summary>
    internal static bool LandingReadNeeded(bool useShieldCharge, bool overcap, int charges, int maxCharges) =>
        useShieldCharge || (overcap && charges >= maxCharges);

    /// <summary>
    ///     The <c>DS|</c> diagnostic line: both BMR answers for the current target. Written on change only (see
    ///     <see cref="DiagnosticKey"/>); 1 = safe, 0 = danger, x = no answer.
    /// </summary>
    internal static string DiagnosticLine(long unixMs, uint targetNameId, bool? atEnemy, bool? atLanding) =>
        $"DS|{unixMs}|t={targetNameId}|enemy={Bit(atEnemy)}|landing={Bit(atLanding)}|v={Judge(atEnemy, atLanding).ToString().ToLowerInvariant()}";

    /// <summary> The change key for <see cref="DiagnosticLine"/>: target and both answers. </summary>
    internal static string DiagnosticKey(uint targetNameId, bool? atEnemy, bool? atLanding) =>
        $"{targetNameId}|{Bit(atEnemy)}|{Bit(atLanding)}";

    private static string Bit(bool? answer) => answer switch { null => "x", true => "1", false => "0" };
}
