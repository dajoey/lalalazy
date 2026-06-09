#region

using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using System;
using System.Numerics;
using GluttonyCombo.Combos.PvE;
using GluttonyCombo.CustomComboNS.Functions;
using GluttonyCombo.Data.Conflicts;
using GluttonyCombo.Services.IPC_Subscriber;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;
using static GluttonyCombo.CustomComboNS.Functions.Jobs;

#endregion

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     Moves melee DPS players to the correct positional (flank/rear) during
///     autorotation when BossMod AI is not handling movement.
/// </summary>
internal static class PositionalMover
{
    /// <summary>
    ///     The desired positional for the current melee job's next GCD.
    /// </summary>
    internal enum DesiredPositional
    {
        None,
        Flank,
        Rear
    }

    /// <summary>
    ///     Offset from the target's hitbox edge to stand at.
    ///     Keeps the player within melee range but not clipping the hitbox.
    /// </summary>
    private const float HitboxOffset = 0.5f;

    /// <summary>
    ///     Attempts to move the player to the correct positional for the current
    ///     melee DPS job. No-ops if any guard condition is met.
    /// </summary>
    public static unsafe void MoveToPositional(IGameObject? target)
    {
        // --- Guard clauses ---
        if (target is null || target is not IBattleChara battleTarget)
            return;

        // Don't move if vnavmesh is not available
        if (!NavmeshIPC.CanPathfind)
            return;

        // Don't move if BossMod AI is actively handling movement
        if (IsBossModAIActive())
            return;

        // Don't move if player has True North (positionals don't matter)
        if (HasStatusEffect(RoleActions.Melee.Buffs.TrueNorth))
            return;

        // Don't move if target doesn't need positionals (omnidirectional)
        if (!TargetNeedsPositionals(target))
            return;

        // Don't move if not in melee range
        if (!InMeleeRange(target))
            return;

        // Don't move if the player is providing movement input
        if (MovementHook.Instance != null && MovementHook.Instance->Moved == 1)
        {
            // Player is actively moving — cancel any vnavmesh path and bail
            if (NavmeshIPC.IsRunningFunc is not null && NavmeshIPC.IsRunningFunc())
                NavmeshIPC.Stop?.Invoke();
            return;
        }

        // Throttle pathfind requests to avoid spamming
        if (!EzThrottler.Throttle("PositionalMover", 250))
            return;

        // --- Determine desired positional ---
        var desired = GetDesiredPositional();
        if (desired is DesiredPositional.None)
            return;

        // Already at the correct positional
        var currentAngle = AngleToTarget(target);
        if (desired is DesiredPositional.Rear && currentAngle is CustomComboFunctions.AttackAngle.Rear)
            return;
        if (desired is DesiredPositional.Flank && currentAngle is CustomComboFunctions.AttackAngle.Flank)
            return;

        // --- Calculate destination ---
        var dest = CalculatePositionalPoint(battleTarget, desired);
        if (dest == Vector3.Zero)
            return;

        NavmeshIPC.PathfindAndMoveTo(dest);
    }

    /// <summary>
    ///     Stops any active positional movement. Called when autorotation stops
    ///     or the player provides input.
    /// </summary>
    internal static void Cancel()
    {
        if (NavmeshIPC.IsRunningFunc is not null && NavmeshIPC.IsRunningFunc())
            NavmeshIPC.Stop?.Invoke();
    }

    /// <summary>
    ///     Determines the desired positional for the current melee job based on
    ///     buff state and combo progression.
    /// </summary>
    private static DesiredPositional GetDesiredPositional()
    {
        var job = Player.Job;

        return job switch
        {
            ECommons.ExcelServices.Job.RPR => GetRPRPositional(),
            ECommons.ExcelServices.Job.SAM => GetSAMPositional(),
            ECommons.ExcelServices.Job.NIN or ECommons.ExcelServices.Job.ROG => GetNINPositional(),
            ECommons.ExcelServices.Job.DRG or ECommons.ExcelServices.Job.LNC => GetDRGPositional(),
            ECommons.ExcelServices.Job.MNK or ECommons.ExcelServices.Job.PGL => GetMNKPositional(),
            ECommons.ExcelServices.Job.VPR => GetVPRPositional(),
            _ => DesiredPositional.None
        };
    }

    #region Job Positional Lookups

    /// <summary> RPR: EnhancedGibbet → Gibbet (flank), EnhancedGallows/neither → Gallows (rear). </summary>
    private static DesiredPositional GetRPRPositional()
    {
        if (!HasStatusEffect(RPR.Buffs.SoulReaver) && !HasStatusEffect(RPR.Buffs.Executioner))
            return DesiredPositional.None;

        return HasStatusEffect(RPR.Buffs.EnhancedGibbet)
            ? DesiredPositional.Flank
            : DesiredPositional.Rear;
    }

    /// <summary> SAM: Gekko (rear), Kasha (flank) based on combo state. </summary>
    private static DesiredPositional GetSAMPositional()
    {
        if (ComboTimer <= 0)
            return DesiredPositional.None;

        // Jinpu path → Gekko (rear)
        if (ComboAction is SAM.Jinpu or SAM.Hakaze or SAM.Gyofu)
        {
            if (ComboAction is SAM.Jinpu)
                return DesiredPositional.Rear;
        }

        // Shifu path → Kasha (flank)
        if (ComboAction is SAM.Shifu)
            return DesiredPositional.Flank;

        return DesiredPositional.None;
    }

    /// <summary> NIN: AeolianEdge (rear), ArmorCrush (flank) based on combo state and Kazematoi gauge. </summary>
    private static DesiredPositional GetNINPositional()
    {
        if (ComboTimer <= 0)
            return DesiredPositional.None;

        // After GustSlash, the next will be AeolianEdge (rear) or ArmorCrush (flank)
        if (ComboAction is NIN.GustSlash)
        {
            var gauge = GetJobGauge<Dalamud.Game.ClientState.JobGauge.Types.NINGauge>();
            return gauge.Kazematoi switch
            {
                0 => DesiredPositional.Flank,     // Need ArmorCrush (flank)
                >= 4 => DesiredPositional.Rear,    // Need AeolianEdge (rear)
                _ => DesiredPositional.None         // Either works, don't force movement
            };
        }

        return DesiredPositional.None;
    }

    /// <summary> DRG: Positionals were removed in Dawntrail. </summary>
    private static DesiredPositional GetDRGPositional() => DesiredPositional.None;

    /// <summary>
    ///     MNK: Form-based positionals (Coeurl form only in Dawntrail).
    ///     Demolish (rear), SnapPunch/PouncingCoeurl (flank).
    /// </summary>
    private static DesiredPositional GetMNKPositional()
    {
        // Only Coeurl form has positionals in Dawntrail
        if (HasStatusEffect(MNK.Buffs.CoeurlForm))
        {
            var gauge = GetJobGauge<Dalamud.Game.ClientState.JobGauge.Types.MNKGauge>();

            // CoeurlStacks 0 → Demolish (rear), otherwise → SnapPunch (flank)
            return gauge.CoeurlFury is 0
                ? DesiredPositional.Rear
                : DesiredPositional.Flank;
        }

        return DesiredPositional.None;
    }

    /// <summary> VPR: Positionals based on combo state. </summary>
    private static DesiredPositional GetVPRPositional()
    {
        // Flanksting (flank) and Flanksbane (flank) vs Hindsting (rear) and Hindsbane (rear)
        if (HasStatusEffect(VPR.Buffs.FlankstungVenom) || HasStatusEffect(VPR.Buffs.FlanksbaneVenom))
            return DesiredPositional.Flank;

        if (HasStatusEffect(VPR.Buffs.HindstungVenom) || HasStatusEffect(VPR.Buffs.HindsbaneVenom))
            return DesiredPositional.Rear;

        // Default opener: first combo hit goes rear
        if (ComboTimer > 0)
            return DesiredPositional.Rear;

        return DesiredPositional.None;
    }

    #endregion

    /// <summary>
    ///     Calculates a point at the desired positional relative to the target.
    /// </summary>
    private static Vector3 CalculatePositionalPoint(IBattleChara target, DesiredPositional positional)
    {
        var targetPos = target.Position;
        var targetRot = target.Rotation;
        var hitboxRadius = target.HitboxRadius;
        var distance = hitboxRadius + HitboxOffset;

        float angle;
        switch (positional)
        {
            case DesiredPositional.Rear:
                // Directly behind: target rotation + π
                angle = targetRot + MathF.PI;
                break;

            case DesiredPositional.Flank:
                // Pick the closer side (left flank or right flank)
                var playerPos = Player.Object!.Position;
                var leftAngle = targetRot + (MathF.PI / 2f);
                var rightAngle = targetRot - (MathF.PI / 2f);

                var leftPoint = targetPos + new Vector3(MathF.Sin(leftAngle), 0, MathF.Cos(leftAngle)) * distance;
                var rightPoint = targetPos + new Vector3(MathF.Sin(rightAngle), 0, MathF.Cos(rightAngle)) * distance;

                var leftDist = Vector3.Distance(playerPos, leftPoint);
                var rightDist = Vector3.Distance(playerPos, rightPoint);

                angle = leftDist <= rightDist ? leftAngle : rightAngle;
                break;

            default:
                return Vector3.Zero;
        }

        var dest = targetPos + new Vector3(MathF.Sin(angle), 0, MathF.Cos(angle)) * distance;

        // Use target Y position for the destination (stay on the same plane)
        dest.Y = targetPos.Y;

        return dest;
    }

    /// <summary>
    ///     Checks if BossMod or BossModReborn AI is actively controlling movement.
    /// </summary>
    private static bool IsBossModAIActive()
    {
        try
        {
            if (ConflictingPluginsChecks.BossMod.TargetingSettingConflicted)
                return true;
            if (ConflictingPluginsChecks.BossModReborn.TargetingSettingConflicted)
                return true;
        }
        catch
        {
            // Conflict checks not initialized yet
        }

        return false;
    }
}
