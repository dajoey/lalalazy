using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.MJI;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace clib.Extensions;

public static unsafe class ActionManagerExtensions {
    public static bool IsCasting(ActionType actionType, uint actionId) {
        if (Control.GetLocalPlayer() is not null and var player) {
            var info = player->GetCastInfo();
            return info is not null && info->IsCasting && info->ActionType == (byte)actionType && info->ActionId == actionId;
        }
        return false;
    }

    public static bool UseAction(ActionType actionType, uint actionId) {
        var am = ActionManager.Instance();
        return am is not null && am->UseAction(actionType, actionId);
    }

    public static bool IsActionInUse(ActionType type, uint itemId) {
        var am = ActionManager.Instance();
        return am is not null && am->GetActionStatus(type, itemId) != 0;
    }

    public static bool Teleport(uint aetheryteId, byte subIndex = 0) => UIState.Instance()->Telepo.Teleport(aetheryteId, subIndex);

    public static bool Sprint() {
        const uint SprintId = 4;
        const ushort SprintStatus = 50;
        const uint StellarSprintId = 43357;
        const ushort StellarSprintStatus = 4398;
        const uint IslandSprintId = 31314;

        foreach (var s in Control.GetLocalPlayer()->StatusManager.Status) {
            if (s.StatusId is StellarSprintStatus && s.RemainingTime < 5)
                return ActionManager.Instance()->UseAction(ActionType.Action, StellarSprintId);
            if (s.StatusId is SprintStatus) {
                if (MJIManager.Instance()->IsPlayerInSanctuary && s.RemainingTime < 5)
                    return ActionManager.Instance()->UseAction(ActionType.Action, IslandSprintId);
            }
        }

        if (GameMain.Instance()->CurrentTerritoryIntendedUseId is FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse.CosmicExploration)
            return ActionManager.Instance()->UseAction(ActionType.Action, StellarSprintId);
        else if (MJIManager.Instance()->IsPlayerInSanctuary)
            return ActionManager.Instance()->UseAction(ActionType.Action, IslandSprintId);
        else
            return ActionManager.Instance()->UseAction(ActionType.GeneralAction, SprintId);
    }
}
