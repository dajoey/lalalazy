using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using RotationSolver.Commands;

namespace RotationSolver.Updaters;

internal static class MovingUpdater
{
	internal static unsafe void UpdateCanMove(bool doNextAction)
	{
		if (Player.Object == null)
		{
			return;
		}

		// Special state.
		if (Svc.Condition?[ConditionFlag.OccupiedInEvent] == true)
		{
			Service.CanMove = true;
			return;
		}

		// Casting the action in list.
		if (Svc.Condition?[ConditionFlag.Casting] == true)
		{
			Service.CanMove = ActionBasicInfo.ActionsNoNeedCasting.Contains(Player.Object?.CastActionId ?? 0);
			return;
		}

		// Action
		ActionID action;
		if (DateTime.Now - RSCommands._lastUsedTime < TimeSpan.FromMilliseconds(100f))
		{
			action = (ActionID)RSCommands._lastActionID;
		}
		else if (doNextAction)
		{
			action = (ActionID)(ActionUpdater.NextAction?.AdjustedID ?? 0);
		}
		else
		{
			action = 0;
		}

		// Casting actions
		bool specialActions = ActionManager.GetAdjustedCastTime(ActionType.Action, (uint)action) > 0;

		Service.CanMove = !specialActions;
	}
}