using System;
using System.Numerics;
using ECommons.DalamudServices;
using Dalamud.Game.ClientState.Conditions;

namespace LazyFATEAutomator;

public enum MoveStopReason
{
    None = 0,
    StuckRetry = 5,
    StuckTeleport = 6,
}

/// <summary>
/// Stuck detector using vnavmesh's actual movement state (IsPathing / IsPathfinding) instead
/// of just wall-clock + position delta. Ported from CBT's MoveTracker pattern.
///
/// Two paths:
///  1. vnav reports "running" but the player hasn't moved &gt;1.5y for 2s → StuckRetry.
///  2. vnav reports "idle" AND hasn't pathfound recently AND we're stationary → StuckRetry.
///  Two consecutive StuckRetry against the same position escalates to StuckTeleport.
/// </summary>
public class StuckTracker
{
    private readonly Plugin _plugin;

    private long _lastPathActivityTick;
    private long _lastProgressTick;
    private Vector3 _lastProgressPos = Vector3.Zero;
    private Vector3 _lastRetryPos = Vector3.Zero;
    private bool _retriedOnce;
    private bool _wasRunning;

    public StuckTracker(Plugin plugin) { _plugin = plugin; }

    public void Reset()
    {
        var now = Environment.TickCount64;
        _lastPathActivityTick = now;
        _lastProgressTick = now;
        _lastProgressPos = Vector3.Zero;
        _lastRetryPos = Vector3.Zero;
        _retriedOnce = false;
        _wasRunning = false;
    }

    public MoveStopReason Update(Vector3 currentPosition)
    {
        if (!_plugin.StateController.IsEnabled) return MoveStopReason.None;
        if (Plugin.Condition[ConditionFlag.InCombat] ||
            Plugin.Condition[ConditionFlag.BetweenAreas] ||
            Plugin.Condition[ConditionFlag.BetweenAreas51] ||
            Plugin.Condition[ConditionFlag.Casting])
        {
            _lastProgressTick = Environment.TickCount64;
            return MoveStopReason.None;
        }

        var now = Environment.TickCount64;
        var isPathing = _plugin.Navigation.IsPathing;
        var isPathfinding = _plugin.Navigation.IsPathfinding;

        if (isPathing || isPathfinding)
            _lastPathActivityTick = now;

        // Case 1: vnav is idle. Player should also be idle. If we're still here and not pathfinding
        // and it's been >1.5s since vnav last reported activity, vnav has given up — retry.
        if (!isPathing)
        {
            _wasRunning = false;
            _lastProgressPos = currentPosition;
            _lastProgressTick = now;

            if (!isPathfinding && (now - _lastPathActivityTick) >= 1500)
            {
                if (_retriedOnce && Vector3.Distance(currentPosition, _lastRetryPos) <= 3f)
                    return MoveStopReason.StuckTeleport;
                _lastRetryPos = currentPosition;
                _retriedOnce = true;
                _lastPathActivityTick = now; // back-off so we don't insta-fire next tick
                return MoveStopReason.StuckRetry;
            }
            return MoveStopReason.None;
        }

        // Case 2: vnav says it's running. Did the player actually move?
        if (!_wasRunning)
        {
            _wasRunning = true;
            _lastProgressPos = currentPosition;
            _lastProgressTick = now;
            return MoveStopReason.None;
        }

        if (Vector3.Distance(currentPosition, _lastProgressPos) > 1.5f)
        {
            _lastProgressPos = currentPosition;
            _lastProgressTick = now;
            if (_retriedOnce) { _retriedOnce = false; _lastRetryPos = Vector3.Zero; }
            return MoveStopReason.None;
        }

        if ((now - _lastProgressTick) < 2000) return MoveStopReason.None;

        if (_retriedOnce && Vector3.Distance(currentPosition, _lastRetryPos) <= 3f)
            return MoveStopReason.StuckTeleport;

        _lastRetryPos = currentPosition;
        _retriedOnce = true;
        _lastProgressTick = now;
        return MoveStopReason.StuckRetry;
    }
}
