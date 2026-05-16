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

			string currentMessage = $" {EntryString}";
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
			DoOneCommandType((type, role) => type.ToStateString(role), role =>
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
				UpdateState(stateType, role);

				if (!DataCenter.AutoFaceTargetOnActionSetting() && DataCenter.MoveModeSetting() == 1)
				{
					Svc.GameConfig.UiControl.Set(UiControlOption.AutoFaceTargetOnAction.ToString(), 1);
				}
				return stateType;
			});
		}

		public static void DoAutodutyStateCommandType(StateCommandType stateType, TargetingType targetingType)
		{
			DoOneCommandType((type, role) => type.ToStateString(role), role =>
			{
				AutodutyUpdateState(stateType, role, targetingType);
				return stateType;
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
			int count = Service.Config.TargetingTypes.Count;
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

		public static void UpdateState(StateCommandType stateType, JobRole role)
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
					DataCenter.TargetingTypeOverride = TargetingType.LowHP;
					if (Service.Config.ShowToggledSettingInChat) { Svc.Chat.Print($"Targeting : PvP"); }
					break;
			}

			_stateString = stateType.ToStateString(role);
			UpdateToast();
		}

		public static void AutodutyUpdateState(StateCommandType stateType, JobRole role, TargetingType targetingType)
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
					DataCenter.TargetingTypeOverride = TargetingType.LowHP;
					if (Service.Config.ShowToggledSettingInChat) { Svc.Chat.Print($"Targeting : PvP"); }
					break;
			}

			_stateString = stateType == StateCommandType.AutoDuty
				? $"{stateType.ToStateString(role)} ({targetingType})"
				: stateType.ToStateString(role);
			UpdateToast();
		}

		public static void DoSpecialCommandType(SpecialCommandType specialType, bool sayout = true)
		{
			DoOneCommandType((type, role) => type.ToSpecialString(role), role =>
			{
				_specialString = specialType.ToSpecialString(role);
				DataCenter.SpecialType = specialType;
				if (sayout)
				{
					UpdateToast();
				}

				return specialType;
			});
		}

		private static void DoOneCommandType<T>(Func<T, JobRole, string> sayout, Func<JobRole, T> doingSomething)
			where T : struct, Enum
		{
			JobRole role = Player.Object?.ClassJob.Value.GetJobRole() ?? JobRole.None;

			if (role == JobRole.None)
			{
				return;
			}

			_ = doingSomething(role);
		}
	}
}