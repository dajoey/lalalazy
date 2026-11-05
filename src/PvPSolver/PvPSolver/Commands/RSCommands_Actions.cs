using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using RotationSolver.Basic.Configuration;
using RotationSolver.Helpers;
using RotationSolver.Updaters;

namespace RotationSolver.Commands
{
	public static partial class RSCommands
	{
		private static DateTime _lastClickTime = DateTime.MinValue;
		private static TimeSpan _clickDelay = TimeSpan.Zero;
		private static bool _lastState;
		private static bool started = false;
		internal static DateTime _lastUsedTime = DateTime.MinValue;
		internal static uint _lastActionID;
		private static float _lastCountdownTime = 0;
		private static Job _previousJob = Job.ADV;
		private static readonly Random random = Random.Shared;
		private static readonly List<MiscUpdater.ActionBarSlotRef> _pulseSlots = [];

		internal static IBaseAction? CurrentAction { get; set; } = null;

		public static void IncrementState()
		{
			if (!DataCenter.State)
			{ DoStateCommandType(StateCommandType.Auto); return; }
			if (DataCenter.State && !DataCenter.IsManual && DataCenter.TargetingType == TargetingType.Big)
			{ DoStateCommandType(StateCommandType.Auto); return; }
			if (DataCenter.State && !DataCenter.IsManual)
			{ DoStateCommandType(StateCommandType.Manual); return; }
			if (DataCenter.State && DataCenter.IsManual)
			{ DoStateCommandType(StateCommandType.Off); return; }
		}

		internal static bool CanDoAnAction(bool isGCD)
		{
			var currentState = DataCenter.State;

			if (!_lastState || !currentState)
			{
				_lastState = currentState;
				return false;
			}
			_lastState = currentState;

			if (DateTime.Now - _lastClickTime < _clickDelay)
			{
				return false;
			}

			_lastClickTime = DateTime.Now;
			_clickDelay = RandomSeconds(Service.Config.ClickingDelay.X, Service.Config.ClickingDelay.Y);

			if (!isGCD && DataCenter.DefaultGCDRemain <= 0.5f && DataCenter.DefaultGCDRemain > 0f)
			{
				return false;
			}

			return isGCD || ActionUpdater.NextAction is not IBaseAction nextAction || !nextAction.Info.IsRealGCD;
		}

		public static void DoAction()
		{
			if (Player.Object != null && Player.Object.StatusList == null)
			{
				return;
			}

			var nextAction = ActionUpdater.NextAction;
			if (nextAction == null)
			{
				return;
			}

			if (DataCenter.AnimationLock > 0f)
			{
				return;
			}

			if (nextAction is BaseAction baseAct)
			{
				if (baseAct.Info.IsAbility && !baseAct.Setting.IsMudra && DataCenter.DefaultGCDRemain <= 0.5f && DataCenter.DefaultGCDRemain > 0f)
				{
					return;
				}
			}

			var player = Player.Object;
			if (player != null && !DataCenter.IsPvP
				&& NoCastingStatusHelper.PlayerHasNoCastingStatus(out var minStatusTime))
			{
				var remainingCastTime = player.TotalCastTime - player.CurrentCastTime;
				if (minStatusTime > remainingCastTime && minStatusTime < NoCastingStatusHelper.ResolveWindow)
				{
					return;
				}
			}

			if (DataCenter.BMRSpecialModeType == SpecialMode.Pyretic)
			{
				PluginLog.Verbose("Player has Pyretic special mode active, skipping action use to avoid potential issues.");
				return;
			}

			if (StatusHelper.PlayerHasStatus(false, StatusID.MotionTracker))
			{
				PluginLog.Verbose("Player has Motion Tracker status, skipping action use to avoid potential issues.");
				return;
			}

			if (StatusHelper.PlayerHasStatus(false, StatusID.Transcendent))
			{
				return;
			}

			if (StatusHelper.PlayerHasStatus(false, StatusID.WaningNocturne))
			{
				return;
			}

#if DEBUG
			// if (nextAction is BaseAction debugAct)
			//     PluginLog.Debug($"Will Do {debugAct}");
#endif

			SwitchTargetForAction(nextAction);

			CurrentAction = nextAction as IBaseAction;

			var pulse = CapturePulseSlots(nextAction);

			if (nextAction.Use())
			{

				_lastActionID = nextAction.AdjustedID;
				_lastUsedTime = DateTime.Now;

				try
				{
					if (DataCenter.CurrentInterceptedAction != null && DataCenter.CurrentInterceptedAction.AdjustedID == nextAction.AdjustedID)
					{
						DataCenter.CurrentInterceptedAction = null;
						DoSpecialCommandType(SpecialCommandType.EndSpecial, false);
					}
				}
				catch (Exception ex)
				{
					PluginLog.Warning($"Failed to clear CurrentInterceptedAction after execution: {ex}");
				}

				if (nextAction is BaseAction finalAct)
				{
					if (Service.Config.EnableClickingCount)
					{
						OtherConfiguration.RotationSolverRecord.ClickingCount++;
					}

					if (pulse)
					{
						PulseSimulation();
					}

					if (finalAct.Setting.EndSpecial)
					{
						ResetSpecial();
					}
				}
			}
			else if (Service.Config.InDebug)
			{
				PluginLog.Verbose($"Failed to use the action {nextAction} ({nextAction.AdjustedID})");
			}
		}

		private static TimeSpan RandomSeconds(float min, float max)
		{
			return TimeSpan.FromSeconds(Math.Min(min, max) + (random.NextDouble() * Math.Abs(max - min)));
		}

		private static bool CapturePulseSlots(IAction action)
		{
			if (!Service.Config.KeyboardNoise || started || action is not BaseAction)
			{
				return false;
			}

			try
			{
				MiscUpdater.FindActionBarSlots(action.AdjustedID, _pulseSlots);
				return _pulseSlots.Count > 0;
			}
			catch (Exception ex)
			{
				PluginLog.Warning($"Pulse Failed!: {ex.Message}");
				return false;
			}
		}

		private static void PulseSimulation()
		{
			started = true;
			PulseAction(random.Next(2, 4));
		}

		private static void PulseAction(int remainingPulses)
		{
			if (remainingPulses <= 0)
			{
				started = false;
				return;
			}

			try
			{
				MiscUpdater.PulseActionBarSlots(_pulseSlots);
				_ = Svc.Framework.RunOnTick(() => PulseAction(remainingPulses - 1),
					RandomSeconds(Service.Config.ClickingDelay.X, Service.Config.ClickingDelay.Y));
			}
			catch (Exception ex)
			{
				started = false;
				PluginLog.Warning($"Pulse Failed!: {ex.Message}");
				BasicWarningHelper.AddSystemWarning($"Action bar failed to pulse because: {ex.Message}");
			}
		}

		internal static void ResetSpecial()
		{
			DoSpecialCommandType(SpecialCommandType.EndSpecial, false);
		}

		internal static void CancelState()
		{
			DataCenter.ResetAllRecords();
			DataCenter.IpcOverrides = null;
			if (DataCenter.State)
			{
				DoStateCommandType(StateCommandType.Off);
			}
		}

		internal static void SetTargetWithDelay(IBattleChara? candidate)
		{
			if (candidate == null)
			{
				return;
			}

			var min = Service.Config.TargetDelay.X;
			var max = Service.Config.TargetDelay.Y;
			var delay = Math.Max(0, min + (random.NextDouble() * Math.Max(0, max - min)));
			if (delay <= 0)
			{
				Svc.Targets.Target = candidate;
				return;
			}

			var initialTargetId = Svc.Targets.Target?.GameObjectId ?? 0;
			var candidateId = candidate.GameObjectId;

			_ = Svc.Framework.RunOnTick(() =>
			{
				try
				{
					var current = Svc.Targets.Target;
					var currentId = current?.GameObjectId ?? 0;

					if (currentId == initialTargetId)
					{
						IBattleChara? cand = null;
						foreach (var obj in Svc.Objects)
						{
							if (obj != null && obj.GameObjectId == candidateId)
							{
								cand = obj as IBattleChara;
								break;
							}
						}

						if (cand != null && cand.IsTargetable)
						{
							Svc.Targets.Target = cand;
						}
					}
				}
				catch
				{
					// Intentionally swallow; candidate may have despawned
				}
			}, TimeSpan.FromSeconds(delay));
		}

		public static void UpdateTargetFromNextAction()
		{
			if (Player.Object == null)
			{
				return;
			}

			SwitchTargetForAction(ActionUpdater.NextAction);
		}

		private static void SwitchTargetForAction(IAction? action)
		{
			if (action is not BaseAction baseAct || baseAct.Target.Target is not IBattleChara target)
			{
				return;
			}

			var isEnemy = target.IsEnemy();
			if (target.GameObjectId == Player.Object?.GameObjectId || !(Service.Config.SwitchTargetFriendly2 || isEnemy))
			{
				return;
			}

			if (isEnemy)
			{
				DataCenter.HostileTarget = target;
			}

			if (!DataCenter.IsManual &&
				(Service.Config.SwitchTargetFriendly2
				|| (Svc.Targets.Target?.IsEnemy() ?? true)
				|| Svc.Targets.Target?.GetObjectKind() == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure))
			{
				Svc.Targets.Target = target;
			}
		}

		internal static void UpdateRotationState()
		{
			try
			{
				if (Player.Object == null)
				{
					return;
				}

				if (ActionUpdater.AutoCancelTime != DateTime.MinValue &&
					(!DataCenter.State || DataCenter.InCombat))
				{
					ActionUpdater.AutoCancelTime = DateTime.MinValue;
				}

			// PvP-only: auto-off conditions
				if (Svc.Condition[ConditionFlag.LoggingOut] ||
					(Service.Config.AutoOffPvPMatchEnd && Svc.Condition[ConditionFlag.PvPDisplayActive]) ||
					(Service.Config.AutoOffCutScene && !DataCenter.IsAutoDuty && Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]) ||
					(Service.Config.AutoOffSwitchClass && Player.Job != _previousJob) ||
					(ActionUpdater.AutoCancelTime != DateTime.MinValue && DateTime.Now > ActionUpdater.AutoCancelTime) || false)
				{
					if (DataCenter.State)
					{
						CancelState();
					}

					ActionUpdater.AutoCancelTime = DateTime.MinValue;
					return;
				}

				// PvP-only: auto-on when match starts (loading screen)
				if (Service.Config.AutoOnPvPMatchStart &&
					Svc.Condition[ConditionFlag.BetweenAreas] &&
					Svc.Condition[ConditionFlag.BoundByDuty] &&
					!DataCenter.State &&
					(DataCenter.Territory?.IsPvP ?? false))
				{
					DoStateCommandType(StateCommandType.PvP);
					return;
				}
			}
			catch (Exception ex)
			{
				PluginLog.Error($"Exception in UpdateRotationState: {ex.Message}");
			}
		}

		private static readonly HashSet<ulong> _targetedIds = [];

		private static HashSet<ulong> CollectHostileTargetObjectIds()
		{
			_targetedIds.Clear();
			foreach (var hostile in DataCenter.AllHostileTargets)
			{

				if (hostile == null || !hostile.IsValid() || hostile.Address == nint.Zero)
				{
					continue;
				}

				if (hostile.TargetObjectId != 0)
				{
					_ = _targetedIds.Add(hostile.TargetObjectId);
				}
			}
			return _targetedIds;
		}

		private static bool AnyInCombatOrTargeted(List<IBattleChara> members, HashSet<ulong> targetedIds)
		{
			foreach (var member in members)
			{
				if (member != null && (member.InCombat() || targetedIds.Contains(member.GameObjectId)))
				{
					return true;
				}
			}
			return false;
		}
	}
}