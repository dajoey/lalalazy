using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyOccultCrescent.ActionHelpers;

public static partial class Actions
{
    public static class Cannoneer
    {
        public static Action PhantomFire { get; private set; } = new(ActionType.GeneralAction, 31);

        public static Action HolyCannon { get; private set; } = new(ActionType.GeneralAction, 32);

        public static Action DarkCannon { get; private set; } = new(ActionType.GeneralAction, 33);

        public static Action ShockCannon { get; private set; } = new(ActionType.GeneralAction, 34);

        public static Action SilverCannon { get; private set; } = new(ActionType.GeneralAction, 35);
    }
}
