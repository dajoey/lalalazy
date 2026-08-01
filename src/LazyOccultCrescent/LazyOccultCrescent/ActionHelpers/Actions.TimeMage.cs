using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyOccultCrescent.ActionHelpers;

public static partial class Actions
{
    public static class TimeMage

    {
        public static Action Slowga { get; private set; } = new(ActionType.GeneralAction, 31);

        public static Action Comet { get; private set; } = new(ActionType.GeneralAction, 32);

        public static Action MageMasher { get; private set; } = new(ActionType.GeneralAction, 33);

        public static Action Dispel { get; private set; } = new(ActionType.GeneralAction, 34);

        public static Action Quick { get; private set; } = new(ActionType.GeneralAction, 35);
    }
}
