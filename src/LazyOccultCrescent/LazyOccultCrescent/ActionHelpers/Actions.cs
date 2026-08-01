using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyOccultCrescent.ActionHelpers;

public static partial class Actions
{
    public static Action Sprint { get; private set; } = new(ActionType.GeneralAction, 4);

    public static Action Return { get; private set; } = new(ActionType.GeneralAction, 8);
}
