using Dalamud.Game.Config;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using RotationSolver.IPC;
using RotationSolver.Updaters;

namespace RotationSolver.Commands
{
	public static partial class RSCommands
	{
		public static string _stateString = "Off", _specialString = string.Empty;

		internal static string EntryString => $"{_stateString}{(DataCenter.SpecialTimeLeft < 0 ? string.Empty : $" - {_specialString}: {DataCenter.SpecialTimeLeft:F2}s")}";

		private static string _lastToastMessage = string.Empty;

		private static void UpdateToast()
		{
			if (!Service.Config.ShowInfoOnToast)
			{
				return;
			}

			var currentMessage = $" {EntryString}";
			if (currentMessage == _lastToastMessage)
			{
				return;
			}

			Svc.Toasts.ShowQuest(currentMessage, new Dalamud.Game.Gui.Toast.QuestToastOptions
			{
				IconId = 101,
			});

			_lastToastMessage = currentMessage;
		}

		public static void DoStateCommandType(StateCommandType stateType, int index = -1)
		{
			DoOneCommandType(role =>
			{
				if (!DataCenter.State && stateType != StateCommandType.Off && !DataCenter.IsPvP)
				{
					// Silently ignore — PvP Solver only activates in PvP zones.
					return stateType;
				}

				// PvP-only: force PvP state when toggling on in PvP
				if (!DataCenter.State && DataCenter.IsPvP)
				{
					stateType = StateCommandType.PvP;
				}

				if (DataCenter.State)
				{
					stateType = AdjustStateType(stateType, ref index);
				}
				else if (stateType != StateCommandType.Off && DataCenter.IsInDutyReplay())
				{
					stateType = StateCommandType.Off;
				}
				else if (index != -1 && stateType is StateCommandType.Auto or StateCommandType.TargetOnly)
				{
					UpdateTargetingIndex(ref index);
				}
				UpdateState(stateType, role);

				if (!DataCenter.AutoFaceTargetOnActionSetting() && DataCenter.MoveModeSetting() == 1)
				{
					Svc.GameConfig.UiControl.Set(UiControlOption.AutoFaceTargetOnAction.ToString(), 1);
				}
			});
		}

		public static void SetStateCommandType(StateCommandType stateType)
		{
			DoOneCommandType(role =>
			{
				if (stateType != StateCommandType.Off)
				{
					if (DataCenter.IsInDutyReplay())
					{
						stateType = StateCommandType.Off;
					}
					else if (DataCenter.IsPvP && Service.Config.PvpStateControl)
					{
						stateType = StateCommandType.PvP;
					}
				}

				UpdateState(stateType, role);

				if (!DataCenter.AutoFaceTargetOnActionSetting() && DataCenter.MoveModeSetting() == 1)
				{
					Svc.GameConfig.UiControl.Set(UiControlOption.AutoFaceTargetOnAction.ToString(), 1);
				}
			});
		}

		public static void SetTargetingIndex(int index)
		{
			var count = Service.Config.TargetingTypes.Count;
			if (count == 0)
			{
				return;
			}

			Service.Config.TargetingIndex = ((index % count) + count) % count;

			if (!DataCenter.State || DataCenter.IsManual || DataCenter.IsAutoDuty || DataCenter.IsPvPStateEnabled)
			{
				return;
			}

			SetStateCommandType(DataCenter.IsTargetOnly ? StateCommandType.TargetOnly : StateCommandType.Auto);
		}

		public static void DoAutodutyStateCommandType(StateCommandType stateType, TargetingType targetingType)
		{
			DoOneCommandType(role =>
			{
				AutodutyUpdateState(stateType, role, targetingType);
			});
		}

		private static StateCommandType AdjustStateType(StateCommandType stateType, ref int index)
		{
			if (DataCenter.IsInDutyReplay())
			{
				return StateCommandType.Off;
			}

			// PvP-only: one state � PvP (on) or Off
			return StateCommandType.Off;
		}

		// PvP-only: all cycle methods toggle between Off and PvP
		public static void CycleStateManualAuto()
		{
			if (!DataCenter.State)
				DoStateCommandType(StateCommandType.PvP);
			else
				DoStateCommandType(StateCommandType.Off);
		}

		public static void CycleStateAuto()
		{
			if (!DataCenter.State)
				DoStateCommandType(StateCommandType.PvP);
			else
				DoStateCommandType(StateCommandType.Off);
		}

		public static void CycleStateManual()
		{
			if (!DataCenter.State)
				DoStateCommandType(StateCommandType.PvP);
			else
				DoStateCommandType(StateCommandType.Off);
		}

		public static void CycleStateWithAllTargetTypes()
		{
			if (!DataCenter.State)
				DoStateCommandType(StateCommandType.PvP);
			else
				DoStateCommandType(StateCommandType.Off);
		}

		public static void CycleStateWithOneTargetTypes()
		{
			if (!DataCenter.State)
				DoStateCommandType(StateCommandType.PvP);
			else
				DoStateCommandType(StateCommandType.Off);
		}

		private static void UpdateTargetingIndex(ref int index)
		{
			var count = Service.Config.TargetingTypes.Count;
			if (count == 0)
			{
				index = 0;
				Service.Config.TargetingIndex = 0;
				return;
			}

			if (index == -1)
			{
				index = Service.Config.TargetingIndex + 1;
			}
			index %= count;
			Service.Config.TargetingIndex = index;
		}

		public static void UpdateState(StateCommandType stateType, JobRole role, IpcStateOverrides? ipcOverrides = null)
		{
			if (stateType == StateCommandType.PvP && !DataCenter.IsPvP)
			{
				// Silently ignore — PvP Solver only activates in PvP zones.
				return;
			}

			switch (stateType)
			{
				case StateCommandType.Off:
					DataCenter.State = false;
					DataCenter.IsManual = false;
					DataCenter.IsTargetOnly = false;
					DataCenter.IsAutoDuty = false;
					DataCenter.IsHenched = false;
					DataCenter.IsPvPStateEnabled = false;
					DataCenter.ResetAllRecords();
					ActionUpdater.NextAction = ActionUpdater.NextGCDAction = null;
					DataCenter.TargetingTypeOverride = null;
					if (Service.Config.ShowToggledSettingInChat) { Svc.Chat.Print($"Targeting : Off"); }
					break;

				case StateCommandType.Auto:
					DataCenter.State = true;
					DataCenter.IsManual = false;
					DataCenter.IsTargetOnly = false;
					DataCenter.IsAutoDuty = false;
					DataCenter.IsHenched = false;
					DataCenter.IsPvPStateEnabled = false;
					ActionUpdater.AutoCancelTime = DateTime.MinValue;
					DataCenter.TargetingTypeOverride = null;
					if (Service.Config.ShowToggledSettingInChat) { Svc.Chat.Print($"Auto Targeting : {DataCenter.TargetingType}"); }
					break;

				case StateCommandType.TargetOnly:
					DataCenter.State = true;
					DataCenter.IsManual = false;
					DataCenter.IsTargetOnly = true;
					DataCenter.IsAutoDuty = false;
					DataCenter.IsHenched = false;
					DataCenter.IsPvPStateEnabled = false;
					ActionUpdater.AutoCancelTime = DateTime.MinValue;
					DataCenter.TargetingTypeOverride = null;
					if (Service.Config.ShowToggledSettingInChat) { Svc.Chat.Print($"Auto Targeting Only : {DataCenter.TargetingType}"); }
					break;

				case StateCommandType.Manual:
					DataCenter.State = true;
					DataCenter.IsManual = true;
					DataCenter.IsTargetOnly = false;
					DataCenter.IsAutoDuty = false;
					DataCenter.IsHenched = false;
					DataCenter.IsPvPStateEnabled = false;
					ActionUpdater.AutoCancelTime = DateTime.MinValue;
					DataCenter.TargetingTypeOverride = null;
					if (Service.Config.ShowToggledSettingInChat) { Svc.Chat.Print($"Targeting : Manual"); }
					break;

				case StateCommandType.AutoDuty:
					DataCenter.State = true;
					DataCenter.IsManual = false;
					DataCenter.IsTargetOnly = false;
					DataCenter.IsAutoDuty = true;
					DataCenter.IsHenched = false;
					DataCenter.IsPvPStateEnabled = false;
					ActionUpdater.AutoCancelTime = DateTime.MinValue;
					DataCenter.TargetingTypeOverride = null;
					if (Service.Config.ShowToggledSettingInChat) { Svc.Chat.Print($"Targeting : AutoDuty"); }
					break;

				case StateCommandType.Henched:
					DataCenter.State = true;
					DataCenter.IsManual = true;
					DataCenter.IsTargetOnly = false;
					DataCenter.IsAutoDuty = false;
					DataCenter.IsHenched = true;
					DataCenter.IsPvPStateEnabled = false;
					ActionUpdater.AutoCancelTime = DateTime.MinValue;
					DataCenter.TargetingTypeOverride = null;
					if (Service.Config.ShowToggledSettingInChat) { Svc.Chat.Print($"Targeting : Henched"); }
					break;

				case StateCommandType.PvP:
					DataCenter.State = true;
					DataCenter.IsManual = false;
					DataCenter.IsTargetOnly = false;
					DataCenter.IsAutoDuty = false;
					DataCenter.IsHenched = false;
					DataCenter.IsPvPStateEnabled = true;
					ActionUpdater.AutoCancelTime = DateTime.MinValue;
					DataCenter.TargetingTypeOverride = null;
					if (Service.Config.ShowToggledSettingInChat) { Svc.Chat.Print($"Targeting : PvP"); }
					break;
			}

			_stateString = stateType.ToStateString(role);
			UpdateToast();
		}

		public static void AutodutyUpdateState(StateCommandType stateType, JobRole role, TargetingType targetingType, IpcStateOverrides? ipcOverrides = null)
		{
			switch (stateType)
			{
				case StateCommandType.Off:
					DataCenter.State = false;
					DataCenter.IsManual = false;
					DataCenter.IsTargetOnly = false;
					DataCenter.IsAutoDuty = false;
					DataCenter.IsHenched = false;
					DataCenter.IsPvPStateEnabled = false;
					DataCenter.ResetAllRecords();
					ActionUpdater.NextAction = ActionUpdater.NextGCDAction = null;
					DataCenter.TargetingTypeOverride = null;
					if (Service.Config.ShowToggledSettingInChat) { Svc.Chat.Print($"Targeting : Off"); }
					break;

				case StateCommandType.Auto:
					DataCenter.State = true;
					DataCenter.IsManual = false;
					DataCenter.IsTargetOnly = false;
					DataCenter.IsAutoDuty = false;
					DataCenter.IsHenched = false;
					DataCenter.IsPvPStateEnabled = false;
					ActionUpdater.AutoCancelTime = DateTime.MinValue;
					DataCenter.TargetingTypeOverride = null;
					if (Service.Config.ShowToggledSettingInChat) { Svc.Chat.Print($"Auto Targeting : {DataCenter.TargetingType}"); }
					break;

				case StateCommandType.TargetOnly:
					DataCenter.State = true;
					DataCenter.IsManual = false;
					DataCenter.IsTargetOnly = true;
					DataCenter.IsAutoDuty = false;
					DataCenter.IsHenched = false;
					DataCenter.IsPvPStateEnabled = false;
					ActionUpdater.AutoCancelTime = DateTime.MinValue;
					DataCenter.TargetingTypeOverride = targetingType;
					if (Service.Config.ShowToggledSettingInChat) { Svc.Chat.Print($"Auto Targeting Only : {DataCenter.TargetingType}"); }
					break;

				case StateCommandType.Manual:
					DataCenter.State = true;
					DataCenter.IsManual = true;
					DataCenter.IsTargetOnly = false;
					DataCenter.IsAutoDuty = false;
					DataCenter.IsHenched = false;
					DataCenter.IsPvPStateEnabled = false;
					ActionUpdater.AutoCancelTime = DateTime.MinValue;
					DataCenter.TargetingTypeOverride = null;
					if (Service.Config.ShowToggledSettingInChat) { Svc.Chat.Print($"Targeting : Manual"); }
					break;

				case StateCommandType.AutoDuty:
					DataCenter.State = true;
					DataCenter.IsManual = false;
					DataCenter.IsTargetOnly = false;
					DataCenter.IsAutoDuty = true;
					DataCenter.IsHenched = false;
					DataCenter.IsPvPStateEnabled = false;
					ActionUpdater.AutoCancelTime = DateTime.MinValue;
					DataCenter.TargetingTypeOverride = targetingType;
					if (Service.Config.ShowToggledSettingInChat) { Svc.Chat.Print($"Targeting : AutoDuty"); }
					break;

				case StateCommandType.Henched:
					DataCenter.State = true;
					DataCenter.IsManual = true;
					DataCenter.IsTargetOnly = false;
					DataCenter.IsAutoDuty = false;
					DataCenter.IsHenched = true;
					DataCenter.IsPvPStateEnabled = false;
					ActionUpdater.AutoCancelTime = DateTime.MinValue;
					DataCenter.TargetingTypeOverride = null;
					if (Service.Config.ShowToggledSettingInChat) { Svc.Chat.Print($"Targeting : Henched"); }
					break;

				case StateCommandType.PvP:
					DataCenter.State = true;
					DataCenter.IsManual = false;
					DataCenter.IsTargetOnly = false;
					DataCenter.IsAutoDuty = false;
					DataCenter.IsHenched = false;
					DataCenter.IsPvPStateEnabled = true;
					ActionUpdater.AutoCancelTime = DateTime.MinValue;
					DataCenter.TargetingTypeOverride = null;
					if (Service.Config.ShowToggledSettingInChat) { Svc.Chat.Print($"Targeting : PvP"); }
					break;
			}

			_stateString = stateType == StateCommandType.AutoDuty
				? $"{stateType.ToStateString(role)} ({targetingType})"
				: stateType.ToStateString(role);
			UpdateToast();
		}

		private static void ApplyState(StateCommandType stateType, JobRole role, TargetingType? autodutyTargeting, IpcStateOverrides? ipcOverrides)
		{
			if (DataCenter.PvPAutomationBlocked && stateType != StateCommandType.Off)
			{
				Svc.Chat.PrintError("Rotation Solver Reborn: Autorotation is blocked in PvP while 'Auto PVP Series Grind' is enabled. It is an AI generated plugin and has been breaking RSR installations.");
				stateType = StateCommandType.Off;
			}

			DataCenter.State = stateType != StateCommandType.Off;
			DataCenter.IsManual = stateType is StateCommandType.Manual or StateCommandType.Henched;
			DataCenter.IsTargetOnly = stateType == StateCommandType.TargetOnly;
			DataCenter.IsAutoDuty = stateType == StateCommandType.AutoDuty;
			DataCenter.IsHenched = stateType == StateCommandType.Henched;
			DataCenter.IsPvPStateEnabled = stateType == StateCommandType.PvP;
			DataCenter.TargetingTypeOverride = stateType switch
			{
				StateCommandType.PvP => TargetingType.LowHP,
				StateCommandType.TargetOnly or StateCommandType.AutoDuty => autodutyTargeting,
				_ => null,
			};

			if (DataCenter.State)
			{
				ActionUpdater.AutoCancelTime = DateTime.MinValue;
			}
			else
			{
				DataCenter.ResetAllRecords();
				Wrath_IPCSubscriber.Release();
				ActionUpdater.NextAction = ActionUpdater.NextGCDAction = null;
				Service.CanMove = true;
			}

			if (Service.Config.ShowToggledSettingInChat)
			{
				Svc.Chat.Print(stateType switch
				{
					StateCommandType.Auto => $"Auto Targeting : {DataCenter.TargetingType}",
					StateCommandType.TargetOnly => $"Auto Targeting Only : {DataCenter.TargetingType}",
					_ => $"Targeting : {stateType}",
				});
			}

			DataCenter.IpcOverrides = DataCenter.State ? ipcOverrides : null;
			_stateString = stateType == StateCommandType.AutoDuty && autodutyTargeting.HasValue
				? $"{stateType.ToStateString()} ({autodutyTargeting})"
				: stateType.ToStateString();
			UpdateToast();
		}
		public static void DoSpecialCommandType(SpecialCommandType specialType, bool sayout = true)
		{
			DoOneCommandType(role =>
			{
				_specialString = specialType.ToSpecialString();
				DataCenter.SpecialType = specialType;
				if (sayout)
				{
					UpdateToast();
				}
			});
		}

		private static void DoOneCommandType(Action<JobRole> doingSomething)
		{
			var role = Player.Object?.ClassJob.Value.GetJobRole() ?? JobRole.None;

			if (role == JobRole.None)
			{
				return;
			}

			doingSomething(role);
		}
	}
}