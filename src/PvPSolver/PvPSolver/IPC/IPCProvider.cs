using ECommons.EzIpcManager;
using ECommons.Logging;
using RotationSolver.Commands;

namespace RotationSolver.IPC
{
	/// <summary>
	/// Provides IPC methods for external plugins or tools to interact with RotationSolver.
	/// All public methods marked with <see cref="EzIPCAttribute"/> are exposed for IPC calls.
	/// </summary>
	internal sealed class IPCProvider : IDisposable
	{
		private readonly EzIPCDisposalToken[] _disposalTokens;
		private bool _disposed;

		/// <summary>
		/// Initializes the IPC provider and registers IPC methods with the specified prefix.
		/// </summary>
		internal IPCProvider()
		{
			_disposalTokens = EzIPC.Init(this, prefix: "PvPSolver");
		}

		/// <summary>
		/// Disposes the IPC provider, unregistering every IPC method this instance registered.
		/// </summary>
		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}
			_disposed = true;
			foreach (var token in _disposalTokens)
			{
				try
				{
					token?.Dispose();
				}
				catch (Exception ex)
				{
					PluginLog.Error($"Error while unregistering IPC: {ex}");
				}
			}
		}

		/// <summary>
		/// IPC method for testing connectivity and logging a debug message.
		/// </summary>
		/// <param name="param">A string parameter to include in the debug log for verification.</param>
		[EzIPC]
		public void Test(string param)
		{
			PluginLog.Debug($"IPC Test! Param:{param}");
		}

		/// <summary>
		/// Adds a name ID to the prioritized list via IPC.
		/// </summary>
		/// <param name="nameId">The unique identifier to add to the prioritized name IDs collection.</param>
		[EzIPC]
		public void AddPriorityNameID(uint nameId)
		{
			lock (DataCenter._nameIdLock)
			{
				if (DataCenter.PrioritizedNameIds != null)
				{
					if (!DataCenter.PrioritizedNameIds.Contains(nameId))
					{
						DataCenter.PrioritizedNameIds.Add(nameId);
					}
					PluginLog.Debug($"IPC AddPriorityNameID was called. NameID:{nameId}");
				}
				else
				{
					PluginLog.Error("DataCenter.PrioritizedNameIds is null.");
				}
			}
		}

		/// <summary>
		/// Removes a name ID from the prioritized list via IPC.
		/// </summary>
		/// <param name="nameId">The unique identifier to remove from the prioritized name IDs collection.</param>
		[EzIPC]
		public void RemovePriorityNameID(uint nameId)
		{
			lock (DataCenter._nameIdLock)
			{
				if (DataCenter.PrioritizedNameIds != null)
				{
					if (DataCenter.PrioritizedNameIds.RemoveAll(id => id == nameId) > 0)
					{
						PluginLog.Debug($"IPC RemovePriorityNameID was called. NameID:{nameId}");
					}
					else
					{
						PluginLog.Warning($"IPC RemovePriorityNameID was called but NameID:{nameId} was not found.");
					}
				}
				else
				{
					PluginLog.Error("DataCenter.PrioritizedNameIds is null.");
				}
			}
		}

		/// <summary>
		/// Adds a name ID to the blacklist via IPC, preventing actions for this identifier.
		/// </summary>
		/// <param name="nameId">The unique identifier to add to the blacklist collection.</param>
		[EzIPC]
		public void AddBlacklistNameID(uint nameId)
		{
			lock (DataCenter._nameIdLock)
			{
				if (DataCenter.BlacklistedNameIds != null)
				{
					if (!DataCenter.BlacklistedNameIds.Contains(nameId))
					{
						DataCenter.BlacklistedNameIds.Add(nameId);
					}
					PluginLog.Debug($"IPC AddBlacklistNameID was called. NameID:{nameId}");
				}
				else
				{
					PluginLog.Error("DataCenter.BlacklistedNameIds is null.");
				}
			}
		}

		/// <summary>
		/// Removes a name ID from the blacklist via IPC, allowing actions for this identifier.
		/// </summary>
		/// <param name="nameId">The unique identifier to remove from the blacklist collection.</param>
		[EzIPC]
		public void RemoveBlacklistNameID(uint nameId)
		{
			lock (DataCenter._nameIdLock)
			{
				if (DataCenter.BlacklistedNameIds != null)
				{
					if (DataCenter.BlacklistedNameIds.RemoveAll(id => id == nameId) > 0)
					{
						PluginLog.Debug($"IPC RemoveBlacklistNameID was called. NameID:{nameId}");
					}
					else
					{
						PluginLog.Warning($"IPC RemoveBlacklistNameID was called but NameID:{nameId} was not found.");
					}
				}
				else
				{
					PluginLog.Error("DataCenter.BlacklistedNameIds is null.");
				}
			}
		}

		/// <summary>
		/// Changes the operating mode of the plugin via IPC.
		/// </summary>
		/// <param name="stateCommand">
		/// The <see cref="StateCommandType"/> value specifying the desired operating mode, such as Off, Auto, or Manual.
		/// </param>
		[EzIPC]
		public void ChangeOperatingMode(StateCommandType stateCommand)
		{
			RSCommands.UpdateState(stateCommand, (JobRole)DataCenter.Job);
			PluginLog.Debug($"IPC ChangeOperatingMode was called. StateCommand:{stateCommand}");
		}

		/// <summary>
		/// Triggers a special state in the plugin via IPC, such as healing, defense, or movement.
		/// </summary>
		/// <param name="specialCommand">
		/// The <see cref="SpecialCommandType"/> value representing the special state to activate (e.g., HealArea, DefenseSingle, Burst).
		/// </param>
		[EzIPC]
		public void TriggerSpecialState(SpecialCommandType specialCommand)
		{
			DataCenter.SpecialType = specialCommand;
			PluginLog.Debug($"IPC TriggerSpecialState was called. SpecialCommand:{specialCommand}");
		}

		/// <summary>
		/// Changes a plugin setting or triggers an auxiliary command via IPC.
		/// </summary>
		/// <param name="otherType">
		/// The <see cref="OtherCommandType"/> category of command to execute, such as DoActions, ToggleActions, Settings, NextAction, or Rotations.
		/// </param>
		/// <param name="str">
		/// The string value representing the modification or parameter for the command.
		/// </param>
		[EzIPC]
		public void OtherCommand(OtherCommandType otherType, string str)
		{
			RSCommands.DoOtherCommand(otherType, str);
			PluginLog.Debug($"IPC DoOtherCommand was called. OtherCommandType:{otherType}, String:{str},");
		}

		/// <summary>
		/// Executes an action command via IPC, allowing external plugins or tools to trigger a specific action with a timing parameter.
		/// </summary>
		/// <param name="action">
		/// The name of the action to execute. This needs to be the exact name of the action.
		/// </param>
		/// <param name="time">
		/// The time value that the window to use the action is open (typcially 5 seconds).
		/// </param>
		[EzIPC]
		public void ActionCommand(string action, float time)
		{
			RSCommands.DoActionCommand((action ?? string.Empty).Trim(), Math.Round(time, 3));
			PluginLog.Debug($"IPC ActionCommand was called. Action Name:{action}, Time:{time}");
		}
	}
}