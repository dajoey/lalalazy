using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using RotationSolver.Commands;
using RotationSolver.IPC;
using RotationSolver.UI.HighlightTeachingMode;
using RotationSolver.Basic.Helpers;
using static FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule;

namespace RotationSolver.Updaters;

internal static class MajorUpdater
{
	private static TimeSpan _timeSinceUpdate = TimeSpan.Zero;
	private static bool _rotationsLoaded;
	private static bool _bmrDataLive;

	private static readonly List<VfxNewData> _vfxRemaining = [];
	private static readonly List<string> _expiredWarnings = [];

	public static bool IsValid
	{
		get
		{
			if (!Player.Available)
			{
				return false;
			}

			return !Svc.Condition[ConditionFlag.BetweenAreas] && !Svc.Condition[ConditionFlag.BetweenAreas51] && !Svc.Condition[ConditionFlag.LoggingOut];
		}
	}

	private static string? _lastExceptionKey;

	public static void Enable()
	{
		PluginLog.Information("MajorUpdater.Enable() called, registering framework handlers");
		Svc.Framework.Update += RSRGateUpdate;
		Svc.Framework.Update += RSRTeachingClearUpdate;
		Svc.Framework.Update += RSRInvalidUpdate;
		Svc.Framework.Update += RSRActivatedCoreUpdate;
		Svc.Framework.Update += RSRActivatedHighlightUpdate;
		Svc.Framework.Update += RSRCommonUpdate;
		Svc.Framework.Update += RSRCleanupUpdate;
		Svc.Framework.Update += RSRRotationAndStateUpdate;
		Svc.Framework.Update += RSRMiscAndTargetFreelyUpdate;
		Svc.Framework.Update += RSRResetUpdate;
	}

	private static void OnUpdate(IFramework framework)
	{
		if (!ShouldRunThisCycle(framework, out var isValid))
		{
			return;
		}

		if (Service.Config.TeachingMode)
		{
			Run("HotbarHighlightManager.HotbarIDs.Clear", HotbarHighlightManager.HotbarIDs.Clear);
		}

		if (!isValid)
		{
			Run("RSRInvalidUpdate", InvalidUpdate);
			return;
		}

		Run("ActionUpdater.UpdateCombatInfo", ActionUpdater.UpdateCombatInfo);
		Run("RotationUpdater.UpdateRotation", RotationUpdater.UpdateRotation);

		var isActivated = DataCenter.IsActivated();
		ActivatedCoreUpdate(isActivated);
		ActivatedHighlightUpdate(isActivated);
		CommonUpdate();
		Run("CleanupUpdate", CleanupUpdate);
		StateUpdate();
		Run("Secondary RSRUpdate", MiscAndTargetFreelyUpdate);
	}

	private static bool ShouldRunThisCycle(IFramework framework, out bool isValid)
	{
		isValid = false;
		try
		{
			_timeSinceUpdate += framework.UpdateDelta;

			if (Service.Config.MinUpdatingTime > 0f && _timeSinceUpdate < TimeSpan.FromSeconds(Service.Config.MinUpdatingTime))
			{
				return false;
			}

			_timeSinceUpdate = TimeSpan.Zero;
			_isValidThisCycle = IsValid;
			_isActivatedThisCycle = DataCenter.IsActivated();
			_shouldRunThisCycle = true;
			if (!Service.Config.TutorialDone)
			{
				PvPSolverPlugin.OpenFirstStartTutorial();
			}

			if (isValid)
			{
				PluginLog.Information($"MajorUpdater: first valid cycle, IsPvP={DataCenter.IsPvP}, TerritoryId={DataCenter.Territory?.Id}, PlayerAvail={Player.Available}");
				PluginLog.Information("MajorUpdater: triggering rotation load...");
				RotationUpdater.LoadBuiltInRotations();
				_rotationsLoaded = true;
			}

			return true;
		}
		catch (Exception ex)
		{
			LogOnce("GateUpdate Exception", ex);
			return false;
		}
	}

	private static void InvalidUpdate()
	{
		if (Svc.Condition[ConditionFlag.BetweenAreas])
		{
			ActionUpdater.MarkAreaChange();
		}

		RSCommands.UpdateRotationState();
		ActionUpdater.ClearNextAction();
		MiscUpdater.UpdateEntry();
	}

	private static void ActivatedCoreUpdate(bool isActivated)
	{
		var autoOnEnabled = Service.Config.AutoOnYes && (Service.Config.StartOnAllianceIsInCombat2
			|| Service.Config.StartOnAttackedBySomeone2
			|| Service.Config.StartOnFieldOpInCombat2
			|| Service.Config.StartOnPartyIsInCombat2) && !DataCenter.IsInDutyReplay();

		if (autoOnEnabled || isActivated)
		{
			Run("(RSRActivatedCore): TargetUpdater.UpdateTargets", TargetUpdater.UpdateTargets);
		}

		if (!isActivated)
		{
			ResetBmrDataIfLive();
			return;
		}

			// Through the presence latch, so an unload seen here resets the BMR timings once (BMRTimeline_IPCSubscriber).
			if (BMRTimeline_IPCSubscriber.IsEnabled)
			{
				BossModUpdater.Update();
			}

		if (DataCenter.BMREnabled)
		{
			_bmrDataLive = true;
			Run("(RSRActivatedCore): BossModUpdater.Update", BossModUpdater.Update);
			Run("(RSRActivatedCore): BMRPlanUpdater.Update", BMRPlanUpdater.Update);
		}
		else
		{
			ResetBmrDataIfLive();
		}

		Run("(RSRActivatedCore): StateUpdater.UpdateState", StateUpdater.UpdateState);
		Run("(RSRActivatedCore): AutoAttackUpdater.Update", AutoAttackUpdater.Update);
		Run("(RSRActivatedCore): ActionUpdater.UpdateNextAction", ActionUpdater.UpdateNextAction);

		var canDoAction = false;
		try
		{
			canDoAction = ActionUpdater.CanDoAction();
		}
		catch (Exception ex)
		{
			LogOnce("(RSRActivatedCore): ActionUpdater.CanDoAction Exception", ex);
		}

		try
		{
			MovingUpdater.UpdateCanMove(canDoAction);
		}
		catch (Exception ex)
		{
			LogOnce("(RSRActivatedCore): MovingUpdater.UpdateCanMove Exception", ex);
		}

		if (canDoAction)
		{
			Run("(RSRActivatedCore): RSCommands.DoAction", RSCommands.DoAction);
		}

		if (DataCenter.IsTargetOnly)
		{
			Run("(RSRActivatedCore): RSCommands.UpdateTargetFromNextAction (TargetOnly)", RSCommands.UpdateTargetFromNextAction);
		}

			// PvPSolver is PvP-only, Wrath Combo handles PvE — no conflict, no IPC needed

		}

		Run("(RSRActivatedCore): Wrath_IPCSubscriber.DisableAutoRotation", Wrath_IPCSubscriber.DisableAutoRotation);
	}

	private static void ResetBmrDataIfLive()
	{
		if (!_bmrDataLive)
		{
			return;
		}

		_bmrDataLive = false;
		DataCenter.ResetBmrData();
		DataCenter.ResetBmrPlanData();
	}

	private static void ActivatedHighlightUpdate(bool isActivated)
	{
		if (!isActivated)
		{
			HotbarHighlightManager.ClearElements();

			if (HotbarDisabledColor.HasTint)
			{
				Run("Hotbar Disabled Redden", HotbarDisabledColor.Reset);
			}
			return;
		}

		if (Service.Config.TeachingMode && ActionUpdater.NextAction is not null)
		{
			Run("Hotbar Highlighting", () =>
			{
				var hotbar = HotbarAddonHelper.GetHotbarID(ActionUpdater.NextAction);
				if (hotbar.HasValue)
				{
					_ = HotbarHighlightManager.HotbarIDs.Add(hotbar.Value);
				}
			});

			Run("Hotbar Highlight Update", HotbarHighlightManager.UpdateElements);
		}
		else
		{
			HotbarHighlightManager.ClearElements();
		}

		if (Service.Config.ReddenDisabledHotbarActions || HotbarDisabledColor.HasTint)
		{
			Run("Hotbar Disabled Redden", HotbarDisabledColor.ApplyFrame);
		}
	}

	private static void CommonUpdate()
	{
		Run("CommonUpdate", PvPSolverPlugin.UpdateDisplayWindow);

		Run("HotbarKeybindHelper.Update", HotbarKeybindHelper.Update);
	}

	private static void CleanupUpdate()
	{
		if (DataCenter.SystemWarnings.Count > 0)
		{
			var expiry = DateTime.Now - TimeSpan.FromMinutes(10);
			_expiredWarnings.Clear();
			foreach (var kvp in DataCenter.SystemWarnings)
			{
				if (kvp.Value < expiry)
				{
					_expiredWarnings.Add(kvp.Key);
				}
			}
			foreach (var key in _expiredWarnings)
			{
				_ = DataCenter.SystemWarnings.Remove(key);
			}
		}

		if (DataCenter.VfxDataQueue.IsEmpty)
		{
			return;
		}

		_vfxRemaining.Clear();
		while (DataCenter.VfxDataQueue.TryDequeue(out var vfx))
		{
			try
			{
				var keepFor = vfx.Duration >= 0.5f ? vfx.Duration : 5.0;
				if (vfx.TimeDuration.TotalSeconds <= keepFor)
				{
					_vfxRemaining.Add(vfx);
				}
			}
			catch
			{
				_vfxRemaining.Add(vfx);
			}
		}

		foreach (var item in _vfxRemaining)
		{
			DataCenter.VfxDataQueue.Enqueue(item);
		}
	}

	private static void StateUpdate()
	{
		Run("RotationAndStateUpdate", RSCommands.UpdateRotationState);

			// Update displaying the additional UI windows
			PvPSolverPlugin.UpdateDisplayWindow();
		}
		catch (Exception ex)
		{
			Run("HotbarHighlightManager.UpdateSettings", HotbarHighlightManager.UpdateSettings);
		}
	}

	private static void MiscAndTargetFreelyUpdate()
	{
		MiscUpdater.UpdateMisc();

		if (!DataCenter.TargetFreelyEnabled || DataCenter.IsPvP || !DataCenter.State || !DataCenter.InCombat
			|| ActionUpdater.NextAction != null || Player.Object == null || Svc.Targets.Target != null)
		{
			return;
		}

		IBattleChara? closestEnemy = null;
		var minDistance = float.MaxValue;

		foreach (var enemy in DataCenter.AllHostileTargets)
		{
			if (enemy == null || !enemy.IsEnemy() || enemy == Player.Object)
			{
				continue;
			}

			var distance = Vector3.Distance(Player.Object.Position, enemy.Position);
			if (distance < minDistance)
			{
				minDistance = distance;
				closestEnemy = enemy;
			}
		}

		if (closestEnemy == null)
		{
			return;
		}

		if (!Service.Config.TargetDelayEnable)
		{
			// Change loaded rotation based on job
			RotationUpdater.UpdateRotation();

			// Change RS state
			RSCommands.UpdateRotationState();

			// "Let Jev call the shots": at most one relay call a second, never awaited here.
			JevCaller.Update();

			if (Service.Config.TeachingMode)
			{
				try
				{
					HotbarHighlightManager.UpdateSettings();
				}
				catch (Exception ex)
				{
					LogOnce("HotbarHighlightManager.UpdateSettings Exception", ex);
				}
			}
		}
		else
		{
			RSCommands.SetTargetWithDelay(closestEnemy);
		}
	}

	private static void Run(string context, Action action)
	{
		try
		{
			if (DataCenter.State && !DataCenter.IsPvP && Service.Config.ForlornPriority && DataCenter.IsInFate && Player.Object != null)
			{
				IBattleChara? forlorn = null;
				foreach (var obj in Svc.Objects)
				{
					if (obj is IBattleChara enemy && enemy.IsForlorn() && enemy.IsTargetable && !enemy.IsDead)
					{
						forlorn = enemy;
						break;
					}
				}

				if (forlorn != null && Svc.Targets.Target?.GameObjectId != forlorn.GameObjectId)
				{
					if (!Service.Config.TargetDelayEnable)
					{
						Svc.Targets.Target = forlorn;
					}
					else
					{
						RSCommands.SetTargetWithDelay(forlorn);
					}
				}
			}

			MiscUpdater.UpdateMisc();

			if (Service.Config.TargetFreely && !DataCenter.IsPvP && DataCenter.State)
			{
				IAction? nextAction2 = ActionUpdater.NextAction;
				if (nextAction2 == null)
				{
					if (Player.Object != null && Svc.Targets.Target == null)
					{
						// Try to find the closest enemy and target it
						IBattleChara? closestEnemy = null;
						float minDistance = float.MaxValue;

						foreach (var enemy in DataCenter.AllHostileTargets)
						{
							if (enemy == null || !enemy.IsEnemy() || enemy == Player.Object)
								continue;

							float distance = Vector3.Distance(Player.Object.Position, enemy.Position);
							if (distance < minDistance)
							{
								minDistance = distance;
								closestEnemy = enemy;
							}
						}

						if (closestEnemy != null)
						{
							if (!Service.Config.TargetDelayEnable)
							{
								Svc.Targets.Target = closestEnemy;
							}
							else
							{
								// Respect TargetDelay before auto-targeting the closest enemy
								RSCommands.SetTargetWithDelay(closestEnemy);
							}
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			LogOnce(context + " Exception", ex);
		}
	}

	private static void LogOnce(string context, Exception ex)
	{
		var key = $"{context}|{ex.GetType().FullName}|{ex.Message}";
		if (_loggedErrors.Count >= MaxLoggedErrors)
		{
			_loggedErrors.Clear();
		}

		return _generalActionLookup.TryGetValue(baseAction.ID, out uint generalActionRowId)
			? new HotbarID(HotbarSlotType.GeneralAction, generalActionRowId)
			: null;
	}

	private static void LogOnce(string context, Exception ex)
	{
		var key = $"{context}:{ex.GetType().Name}:{ex.Message}";
		if (_lastExceptionKey == key)
		{
			return;
		}

		_lastExceptionKey = key;
		PluginLog.Error($"{context}: {ex.Message}");
		if (Service.Config.InDebug)
		{
			_ = BasicWarningHelper.AddSystemWarning(context);
		}
	}

	public static void Dispose()
	{
		Svc.Framework.Update -= OnUpdate;

		MiscUpdater.Dispose();
		ActionUpdater.ClearNextAction();
		AutoAttackUpdater.Disable();
	}
}
