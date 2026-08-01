using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyOccultCrescent.ActionHelpers;

public static partial class Actions
{
    public static class Ranger
    {
        public static Action Aim { get; private set; } = new(ActionType.GeneralAction, 31);

        public static Action Featherfoot { get; private set; } = new(ActionType.GeneralAction, 32);

        public static Action Falcon { get; private set; } = new(ActionType.GeneralAction, 33);

        public static Action Unicorn { get; private set; } = new(ActionType.GeneralAction, 34);
    }
}
