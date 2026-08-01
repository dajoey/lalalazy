using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyOccultCrescent.ActionHelpers;

public static partial class Actions
{
    public static Action MountRoulette { get; private set; } = new(ActionType.GeneralAction, 9);

    public static Action Mount(uint id)
    {
        return new Action(ActionType.Mount, id);
    }

    public static Action Unmount { get; private set; } = new(ActionType.Mount, 0);

    public static void TryUnmount()
    {
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            Unmount.Cast();
        }
    }
}
