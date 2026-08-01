using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyOccultCrescent.ActionHelpers;

public static partial class Actions
{
    public static class Knight
    {
        public static Action Guard { get; private set; } = new(ActionType.GeneralAction, 31);

        public static Action Pray { get; private set; } = new(ActionType.GeneralAction, 32);

        public static Action Heal { get; private set; } = new(ActionType.GeneralAction, 33);

        public static Action Pledge { get; private set; } = new(ActionType.GeneralAction, 34);
    }
}
