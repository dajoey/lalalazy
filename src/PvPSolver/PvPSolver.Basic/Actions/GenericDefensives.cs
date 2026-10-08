using Dalamud.Game.ClientState.Objects.SubKinds;
using RotationSolver.Basic.Configuration;
using RotationSolver.Decisions;

namespace RotationSolver.Basic.Actions;

/// <summary>
///     Fork-only stage of the generic PvP defensives. <see cref="TryFire"/> is called once from the base
///     <c>EmergencyAbility</c> PvP block, after the Guard branch and before Recuperate; <see cref="RecuperateGate"/>
///     adds the HP condition to the existing Recuperate calls. The rows, defaults and rules are in
///     <see cref="DefensiveTable"/>, <see cref="DefensiveTrigger"/>, <see cref="DefensiveChaining"/> and
///     <see cref="DefensiveSilence"/>; this class only reads the game and calls the action.
/// </summary>
internal static class GenericDefensives
{
    // Environment.TickCount64 (ms) until which no generic row is used after the last use.
    private static long _lockoutUntilMs;

    /// <summary>
    ///     Uses the first enabled Fire row of the current job whose HP test passes, on the character itself. Rows of
    ///     mode Param or Gate are never used here. Returns false (and a null action) when nothing is used, which
    ///     is always the case while every row is off.
    /// </summary>
    public static bool TryFire(CustomRotation r, out IAction? act)
    {
        act = null;
        Configs config = Service.Config;
        if (!config.PvpDefensivesMaster || !DataCenter.IsPvP)
        {
            return false;
        }

        IPlayerCharacter? player = ECommons.GameHelpers.Player.Object;
        if (player == null)
        {
            return false;
        }

        IReadOnlyList<DefensiveRow> rows = DefensiveTable.ForJob(DataCenter.Job.ToString());
        if (!AnyFireRowInForce(rows, config))
        {
            return false;
        }

        if (DefensiveSilence.Silenced(CustomRotation.HasPVPGuard, HasAny(DefensiveTable.SilenceStatuses),
                DataCenter.HasHostilesInMaxRange, player.TimeAlive()))
        {
            return false;
        }

        long now = Environment.TickCount64;
        float health = player.GetHealthRatio();
        foreach (DefensiveRow row in rows)
        {
            if (row.Mode != DefensiveMode.Fire)
            {
                continue;
            }

            // The setting test comes first: a row that is off is never even asked CanUse (no target search, no cooldown read).
            if (!config.DefensiveActive(row))
            {
                continue;
            }

            if (!DefensiveTrigger.ShouldUse(true, health, config.DefensivePercent(row)))
            {
                continue;
            }

            if (HasAny(row.ProvidesStatus) || HasAny(row.SuppressWhileStatus))
            {
                continue;
            }

            if (DefensiveChaining.Blocked(row.Kind, now, _lockoutUntilMs, SiblingsOf(rows, row, config)))
            {
                continue;
            }

            IBaseAction? action = Find(r, row.ActionId);
            if (action == null || action.Info.IsRealGCD)
            {
                continue;
            }

            if (action.CanUse(out IAction used, usedUp: row.UsedUp, skipTargetStatusNeedCheck: row.SkipTargetStatusNeed, targetOverride: TargetType.Self))
            {
                _lockoutUntilMs = now + DefensiveChaining.LockoutMs;
                act = used;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     The extra condition on the existing <c>RecuperatePvP.CanUse</c> calls. True (a no-op) while the Recuperate
    ///     row is not in force; otherwise true only while the character's HP ratio is below the row's percentage.
    /// </summary>
    public static bool RecuperateGate(IBattleChara? player)
    {
        Configs config = Service.Config;
        DefensiveRow row = DefensiveTable.Recuperate;
        return DefensiveGate.RecuperateAllows(config.DefensiveActive(row), player?.GetHealthRatio() ?? float.NaN, config.DefensivePercent(row));
    }

    private static bool AnyFireRowInForce(IReadOnlyList<DefensiveRow> rows, Configs config)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Mode == DefensiveMode.Fire && config.DefensiveActive(rows[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAny(IReadOnlyList<uint> statusIds)
    {
        for (int i = 0; i < statusIds.Count; i++)
        {
            if (StatusHelper.PlayerHasStatus(true, (StatusID)(ushort)statusIds[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static DefensiveSibling[] SiblingsOf(IReadOnlyList<DefensiveRow> rows, DefensiveRow candidate, Configs config)
    {
        List<DefensiveSibling> siblings = new(rows.Count);
        foreach (DefensiveRow other in rows)
        {
            if (ReferenceEquals(other, candidate) || other.Mode == DefensiveMode.Gate)
            {
                continue;
            }

            siblings.Add(new DefensiveSibling(other.Kind, config.DefensiveActive(other), HasAny(other.ProvidesStatus)));
        }

        return [.. siblings];
    }

    // The row's action from the rotation's own action list (the list is built once per rotation instance).
    private static IBaseAction? Find(CustomRotation r, uint actionId)
    {
        foreach (IBaseAction candidate in r.AllBaseActions)
        {
            if (candidate.ID == actionId)
            {
                return candidate;
            }
        }

        return null;
    }
}
