using System;
using System.Numerics;
using ECommons.DalamudServices;

namespace LazyFATEAutomator;

public class StuckTracker
{
    private readonly Plugin _plugin;

    private Vector3 _lastPosition = Vector3.Zero;
    private DateTime _lastMoveTime = DateTime.Now;
    private int _stuckRetryCount = 0;
    private DateTime _lastRetryTime = DateTime.MinValue;

    public bool IsStuck { get; private set; } = false;

    public StuckTracker(Plugin plugin)
    {
        _plugin = plugin;
    }

    /// <summary>
    /// Resets all stuck tracking counters. Call this when successfully starting pathing or transitioning FATEs.
    /// </summary>
    public void Reset()
    {
        _lastPosition = Vector3.Zero;
        _lastMoveTime = DateTime.Now;
        _stuckRetryCount = 0;
        IsStuck = false;
    }

    /// <summary>
    /// Evaluates the player's current position to detect stuck states.
    /// </summary>
    public MoveStopReason Update(Vector3 currentPosition)
    {
        if (!_plugin.StateController.IsEnabled) return MoveStopReason.None;

        // Skip tracking if player is in combat or loading/teleporting
        if (Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat] ||
            Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas] ||
            Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas51])
        {
            _lastMoveTime = DateTime.Now;
            return MoveStopReason.None;
        }

        // Initialize position on first tick
        if (_lastPosition == Vector3.Zero)
        {
            _lastPosition = currentPosition;
            _lastMoveTime = DateTime.Now;
            return MoveStopReason.None;
        }

        // Compare horizontal distance to detect stationary states
        float distance = Vector2.Distance(new Vector2(currentPosition.X, currentPosition.Z), new Vector2(_lastPosition.X, _lastPosition.Z));
        if (distance > 1.0f)
        {
            // Player moved successfully
            _lastPosition = currentPosition;
            _lastMoveTime = DateTime.Now;
            if (IsStuck)
            {
                Plugin.PluginLog.Information("Character is moving again, stuck state resolved.");
                IsStuck = false;
                _stuckRetryCount = 0;
            }
            return MoveStopReason.None;
        }

        // If player has been stationary
        double timeStationary = (DateTime.Now - _lastMoveTime).TotalSeconds;

        if (timeStationary >= 15.0)
        {
            // STUCK TELEPORT FALLBACK: Teleport to nearest in-zone Aetheryte to reset position
            Plugin.PluginLog.Warning("Stuck threshold exceeded 15 seconds. Triggering StuckTeleport unstuck recovery.");
            IsStuck = true;
            return MoveStopReason.StuckTeleport;
        }
        else if (timeStationary >= 3.0)
        {
            // STUCK RETRY: Re-mesh and re-route via vnavmesh
            if ((DateTime.Now - _lastRetryTime).TotalSeconds >= 4.0)
            {
                _stuckRetryCount++;
                _lastRetryTime = DateTime.Now;
                IsStuck = true;
                Plugin.PluginLog.Warning($"Stuck detected ({timeStationary:F1}s stationary). Triggering StuckRetry local pathing rebuild (Retry #{_stuckRetryCount}).");
                return MoveStopReason.StuckRetry;
            }
        }

        return MoveStopReason.None;
    }
}

public enum MoveStopReason
{
    None = 0,
    FateInvalid = 1,
    FatePending = 2,
    HigherPriority = 3,
    NpcLoaded = 4,
    StuckRetry = 5,
    StuckTeleport = 6
}
