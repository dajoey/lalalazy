using ECommons.GameHelpers;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using RotationSolver.Basic.Tweaks;
using System.Diagnostics;

namespace RotationSolver.Basic.Helpers;

/// <summary>
/// Extended ActionManager functionality with animation lock and cooldown delay tweaks.
/// This class provides enhanced timing control for action execution.
/// </summary>
public sealed unsafe class ActionManagerEx : IDisposable
{
	private static ActionManagerEx? _instance;

	/// <summary>
	/// Gets the singleton instance of <see cref="ActionManagerEx"/>.
	/// </summary>
	public static ActionManagerEx Instance => _instance ??= new ActionManagerEx();

	private readonly AnimationLockTweak _animLockTweak = new();
	private readonly CooldownDelayTweak _cooldownTweak = new();

	/// <summary>
	/// Gets the <see cref="AnimationLockTweak"/> instance used for animation lock timing adjustments.
	/// </summary>
	public AnimationLockTweak AnimationLockTweak => _animLockTweak;

	/// <summary>
	/// Gets the <see cref="CooldownDelayTweak"/> instance used for cooldown delay adjustments.
	/// </summary>
	public CooldownDelayTweak CooldownDelayTweak => _cooldownTweak;

	private float _lastAnimationLock;
	private float _frameDeltaSec;
	private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

	/// <summary>
	/// Updates the tweaks with current frame information.
	/// Should be called every frame to maintain accurate timing.
	/// </summary>
	public void UpdateTweaks()
	{
		if (ActionManager.Instance() == null) return;

		var dt = (float)_stopwatch.Elapsed.TotalSeconds;
		_frameDeltaSec = Math.Clamp(dt, 0f, 0.25f);
		_stopwatch.Restart();

		var currentAnimLock = Player.AnimationLock;

		_lastAnimationLock = currentAnimLock;
	}

	/// <summary>
	/// Enhanced UseAction with timing tweaks applied.
	/// </summary>
	/// <param name="actionType">The type of action to use</param>
	/// <param name="actionId">The ID of the action to use</param>
	/// <param name="targetId">The target object ID</param>
	/// <returns>True if the action was successfully used</returns>
	public bool UseActionWithTweaks(ActionType actionType, uint actionId, ulong targetId)
	{
		var actionManager = ActionManager.Instance();
		if (actionManager == null) return false;

		// Record current state for tweaks
		var prevAnimLock = Player.AnimationLock;
		var prevCooldown = GetRemainingCooldown(actionType, actionId);

		// Start cooldown adjustment if enabled
		if (Service.Config.RemoveCooldownDelay)
		{
			_cooldownTweak.StartAdjustment(prevAnimLock, prevCooldown, _frameDeltaSec);
		}

		// Determine the expected sequence for the upcoming action
		var expectedSequence = actionManager->LastUsedActionSequence + 1;

		// Execute the action
		var result = actionManager->UseAction(actionType, actionId, targetId);

		if (result)
		{
			// Record the initial animation lock bump after a successful use
			if (Service.Config.RemoveAnimationLockDelay)
			{
				var initAnimLock = Player.AnimationLock;
				if (initAnimLock > prevAnimLock)
				{
					_animLockTweak.RecordRequest((uint)expectedSequence, initAnimLock);
				}
			}
			// Apply tweaks after successful action use
			ApplyPostActionTweaks((uint)expectedSequence, prevAnimLock, prevCooldown);
		}
		else
		{
			// Stop adjustments if action failed
			_cooldownTweak.StopAdjustment();
		}

		return result;
	}

	/// <summary>
	/// Enhanced UseActionLocation with timing tweaks applied.
	/// </summary>
	/// <param name="actionType">The type of action to use</param>
	/// <param name="actionId">The ID of the action to use</param>
	/// <param name="targetId">The target object ID</param>
	/// <param name="location">The target location</param>
	/// <returns>True if the action was successfully used</returns>
	public bool UseActionLocationWithTweaks(ActionType actionType, uint actionId, ulong targetId, Vector3* location)
	{
		var actionManager = ActionManager.Instance();
		if (actionManager == null || location == null) return false;

		// Record current state for tweaks
		var prevAnimLock = Player.AnimationLock;
		var prevCooldown = GetRemainingCooldown(actionType, actionId);

		// Start cooldown adjustment if enabled
		if (Service.Config.RemoveCooldownDelay)
		{
			_cooldownTweak.StartAdjustment(prevAnimLock, prevCooldown, _frameDeltaSec);
		}

		// Determine the expected sequence for the upcoming action
		var expectedSequence = actionManager->LastUsedActionSequence + 1;

		// Execute the action
		var result = actionManager->UseActionLocation(actionType, actionId, targetId, location);

		if (result)
		{
			// Record the initial animation lock bump after a successful use
			if (Service.Config.RemoveAnimationLockDelay)
			{
				var initAnimLock = Player.AnimationLock;
				if (initAnimLock > prevAnimLock)
				{
					_animLockTweak.RecordRequest((uint)expectedSequence, initAnimLock);
				}
			}
			// Apply tweaks after successful action use
			ApplyPostActionTweaks((uint)expectedSequence, prevAnimLock, prevCooldown);
		}
		else
		{
			// Stop adjustments if action failed
			_cooldownTweak.StopAdjustment();
		}

		return result;
	}

	/// <summary>
	/// Apply timing tweaks after action execution
	/// </summary>
	private void ApplyPostActionTweaks(uint expectedSequence, float prevAnimLock, float prevCooldown)
	{
		var actionManager = ActionManager.Instance();
		if (actionManager == null) return;

		// Apply animation lock reduction
		if (Service.Config.RemoveAnimationLockDelay && prevAnimLock > 0)
		{
			var currentAnimLock = Player.AnimationLock;
			var reduction = _animLockTweak.Apply(expectedSequence, prevAnimLock, currentAnimLock, currentAnimLock, currentAnimLock, out var delay);

			// Apply the reduction to the current animation lock
			if (reduction > 0)
			{
				actionManager->AnimationLock = Math.Max(0, currentAnimLock - reduction);
				if (Service.Config.InDebug)
				{
					PluginLog.Debug($"[ActionManagerEx] Reduced animation lock by {reduction:f3}s (delay: {delay:f3}s)");
				}
			}
		}

		// Apply cooldown adjustment
		if (Service.Config.RemoveCooldownDelay && _cooldownTweak.Adjustment > 0)
		{
			// Note: In a full implementation, we would need to adjust specific action cooldowns
			// This is a simplified version that demonstrates the concept
			if (Service.Config.InDebug)
			{
				PluginLog.Debug($"[ActionManagerEx] Cooldown adjustment: {_cooldownTweak.Adjustment:f3}s");
			}
			_cooldownTweak.StopAdjustment();
		}
	}

	/// <summary>
	/// Get the remaining cooldown for a specific action
	/// </summary>
	private float GetRemainingCooldown(ActionType actionType, uint actionId)
	{
		var actionManager = ActionManager.Instance();
		if (actionManager == null) return 0;

		var recastTime = actionManager->GetRecastTime(actionType, actionId);
		var elapsedTime = actionManager->GetRecastTimeElapsed(actionType, actionId);
		return Math.Max(0, recastTime - elapsedTime);
	}

	/// <summary>
	/// Get animation lock delay estimate for rotation timing
	/// </summary>
	public float GetAnimationLockDelayEstimate() => _animLockTweak.DelayEstimate;

	/// <summary>
	/// Get current cooldown adjustment
	/// </summary>
	public float GetCooldownAdjustment() => _cooldownTweak.Adjustment;

	/// <summary>
	/// Releases all resources used by the <see cref="ActionManagerEx"/> instance.
	/// </summary>
	public void Dispose()
	{
		// Clean up resources if needed
		_cooldownTweak.StopAdjustment();
		_instance = null;
	}
}
