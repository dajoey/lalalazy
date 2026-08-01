using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyOccultCrescent.ActionHelpers;

public static partial class Actions
{
    public static class Oracle
    {
        public static Action Predict { get; private set; } = new(ActionType.GeneralAction, 31);

        public static Action Recuperation { get; private set; } = new(ActionType.GeneralAction, 32);

        public static Action Doom { get; private set; } = new(ActionType.GeneralAction, 33);

        public static Action Rejuvenation { get; private set; } = new(ActionType.GeneralAction, 34);

        public static Action Invulnerability { get; private set; } = new(ActionType.GeneralAction, 35);
    }
}
