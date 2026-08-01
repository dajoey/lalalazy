using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyOccultCrescent.ActionHelpers;

public static partial class Actions
{
    public static class Monk
    {
        public static Action Kick { get; private set; } = new(ActionType.GeneralAction, 31);

        public static Action Counter { get; private set; } = new(ActionType.GeneralAction, 32);

        public static Action Counterstance { get; private set; } = new(ActionType.GeneralAction, 33);

        public static Action Chakra { get; private set; } = new(ActionType.GeneralAction, 34);
    }
}
