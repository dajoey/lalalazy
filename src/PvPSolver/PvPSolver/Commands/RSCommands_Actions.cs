using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using RotationSolver.Basic.Configuration;
using RotationSolver.Updaters;

namespace RotationSolver.Commands
{
	public static partial class RSCommands
	{
		private static DateTime _lastClickTime = DateTime.MinValue;
		private static bool _lastState;
		private static bool started = false;
		internal static DateTime _lastUsedTime = DateTime.MinValue;
		internal static uint _lastActionID;
		private static float _lastCountdownTime = 0;
		private static Job _previousJob = Job.ADV;
		private static readonly Random random = Random.Shared;

		internal static IBaseAction? CurrentAction { get; set; } = null;

		public static void IncrementState()
		{
			if (!DataCenter.State) { DoStateCommandType(StateCommandType.Auto); return; }
			if (DataCenter.State && !DataCenter.IsManual && DataCenter.TargetingType == TargetingType.Big) { DoStateCommandType(StateCommandType.Auto); return; }
			if (DataCenter.State && !DataCenter.IsManual) { DoStateCommandType(StateCommandType.Manual); return; }
			if (DataCenter.State && DataCenter.IsManual) { DoStateCommandType(StateCommandType.Off); return; }
		}

		internal static bool CanDoAnAction(bool isGCD)
		{
			bool currentState = DataCenter.State;

			if (!_lastState || !currentState)
			{
				_lastState = currentState;
				return false;
			}
			_lastState = currentState;

			TimeSpan delayRange = TimeSpan.FromMilliseconds(random.Next(
				(int)(Service.Config.ClickingDelay.X * 1000),
				(int)(Service.Config.ClickingDelay.Y * 1000)));

			if (DateTime.Now - _lastClickTime < delayRange)
			{
				return false;
			}

			_lastClickTime = DateTime.Now;

			if (!isGCD && DataCenter.DefaultGCDRemain <= 0.5f && DataCenter.DefaultGCDRemain > 0f)
			{
				return false;
			}

			return isGCD || ActionUpdater.NextAction is not IBaseAction nextAction || !nextAction.Info.IsRealGCD;
		}

		private static StatusID[]? _cachedNoCastingStatusArray = null;
		private static HashSet<uint>? _cachedNoCastingStatusSet = null;

		public static void DoAction()
		{
			if (Player.Object != null && Player.Object.StatusList == null)
			{
				return;
			}

			IAction? nextAction = ActionUpdater.NextAction;
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
				// If this is an ability and not a Ninjutsu-type action, and GCD remaining is between 0 and 0.5s, skip using it
				if (baseAct.Info.IsAbility && !baseAct.Setting.IsMudra && DataCenter.DefaultGCDRemain <= 0.5f && DataCenter.DefaultGCDRemain > 0f)
				{
					return;
				}
			}

			HashSet<uint> noCastingStatus = OtherConfiguration.NoCastingStatus;
			if (noCastingStatus != null)
			{
				if (_cachedNoCastingStatusSet != noCastingStatus)
				{
					_cachedNoCastingStatusArray = new StatusID[noCastingStatus.Count];
					int index = 0;
					foreach (uint status in noCastingStatus)
					{
						_cachedNoCastingStatusArray[index++] = (StatusID)status;
					}
					_cachedNoCastingStatusSet = noCastingStatus;
				}
			}
			else
			{
				_cachedNoCastingStatusArray = [];
				_cachedNoCastingStatusSet = null;
			}
			StatusID[] noCastingStatusArray = _cachedNoCastingStatusArray!;

			float minStatusTime = float.MaxValue;
			int statusTimesCount = 0;
			if (Player.Object != null && !DataCenter.IsPvP)
			{
				foreach (float t in Player.Object.StatusTimes(false, noCastingStatusArray))
				{
					statusTimesCount++;
					if (t < minStatusTime)
					{
						minStatusTime = t;
					}
				}
			}

			if (statusTimesCount > 0 && Player.Object != null)
			{
				float remainingCastTime = Player.Object.TotalCastTime - Player.Object.CurrentCastTime;
				if (minStatusTime > remainingCastTime && minStatusTime < 3f)
				{
					return;
				}
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

			if (nextAction is BaseAction baseAct2)
			{
				if (baseAct2.Target.Target != null && baseAct2.Target.Target is IBattleChara target && target != Player.Object && (Service.Config.SwitchTargetFriendly2 || target.IsEnemy()))
				{
					DataCenter.HostileTarget = target;
					if (!DataCenter.IsManual &&
						(Service.Config.SwitchTargetFriendly2
						|| (Svc.Targets.Target?.IsEnemy() ?? true)
						|| (Svc.Targets.Target?.GetObjectKind() == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure)))
					{
						Svc.Targets.Target = target;
					}
				}
			}

			CurrentAction = nextAction as IBaseAction;

			if (nextAction.Use())
			{

				_lastActionID = nextAction.AdjustedID;
				_lastUsedTime = DateTime.Now;

				// If this action was the one intercepted by the user, clear intercepted state and end the intercept window early
				try
				{
					if (DataCenter.CurrentInterceptedAction != null && DataCenter.CurrentInterceptedAction.AdjustedID == nextAction.AdjustedID)
					{
						DataCenter.CurrentInterceptedAction = null;
						// End the special intercepting state without showing toast
						DoSpecialCommandType(SpecialCommandType.EndSpecial, false);
					}
				}
				catch (Exception ex)
				{
					PluginLog.Warning($"Failed to clear CurrentInterceptedAction after execution: {ex}");
				}

				if (nextAction is BaseAction finalAct)
				{
					if (Service.Config.KeyboardNoise)
					{
						PulseSimulation(nextAction.AdjustedID);
						if (Service.Config.EnableClickingCount)
						{
							OtherConfiguration.RotationSolverRecord.ClickingCount++;
						}
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

		private static void PulseSimulation(uint id)
		{
			if (started)
			{
				return;
			}

			started = true;
			try
			{
				int pulseCount = random.Next(Service.Config.KeyboardNoisePresses.X, Service.Config.KeyboardNoisePresses.Y);
				PulseAction(id, pulseCount);
			}
			catch (Exception ex)
			{
				PluginLog.Warning($"Pulse Failed!: {ex.Message}");
				BasicWarningHelper.AddSystemWarning($"Action bar failed to pulse because: {ex.Message}");
			}
			finally
			{
				started = false;
			}
		}

		private static void PulseAction(uint id, int remainingPulses)
		{
			if (remainingPulses <= 0)
			{
				started = false;
				return;
			}

			MiscUpdater.PulseActionBar(id);
			double time = Service.Config.ClickingDelay.X + (random.NextDouble() * (Service.Config.ClickingDelay.Y - Service.Config.ClickingDelay.X));
			_ = Svc.Framework.RunOnTick(() =>
			{
				PulseAction(id, remainingPulses - 1);
			}, TimeSpan.FromSeconds(time));
		}

		internal static void ResetSpecial()
		{
			DoSpecialCommandType(SpecialCommandType.EndSpecial, false);
		}

		internal static void CancelState()
		{
			DataCenter.ResetAllRecords();
			if (DataCenter.State)
			{
				DoStateCommandType(StateCommandType.Off);
			}
		}

		internal static void SetTargetWithDelay(IGameObject? candidate)
		{
			if (candidate == null)
			{
				return;
			}

			float min = Service.Config.TargetDelay.X;
			float max = Service.Config.TargetDelay.Y;
			double delay = Math.Max(0, min + (random.NextDouble() * Math.Max(0, max - min)));
			if (delay <= 0)
			{
				Svc.Targets.Target = candidate;
				return;
			}

			ulong initialTargetId = Svc.Targets.Target?.GameObjectId ?? 0;
			ulong candidateId = candidate.GameObjectId;

			_ = Svc.Framework.RunOnTick(() =>
			{
				try
				{
					var current = Svc.Targets.Target;
					ulong currentId = current?.GameObjectId ?? 0;

					if (currentId == initialTargetId)
					{
						IGameObject? cand = null;
						foreach (var obj in Svc.Objects)
						{
							if (obj != null && obj.GameObjectId == candidateId)
							{
								cand = obj;
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

			IAction? nextAction = ActionUpdater.NextAction;
			if (nextAction is BaseAction baseAct)
			{
				if (baseAct.Target.Target != null && baseAct.Target.Target is IBattleChara target && target != Player.Object && (Service.Config.SwitchTargetFriendly2 || target.IsEnemy()))
				{
					DataCenter.HostileTarget = target;
					if (!DataCenter.IsManual &&
						(Service.Config.SwitchTargetFriendly2 || ((Svc.Targets.Target?.IsEnemy() ?? true)
						|| Svc.Targets.Target?.GetObjectKind() == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure)))
					{
						Svc.Targets.Target = target;
					}
				}
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
					CancelState();
					if (Player.Job != _previousJob)
					{
						_previousJob = Player.Job;
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

	}
}