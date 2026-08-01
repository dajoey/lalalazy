using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyOccultCrescent.ActionHelpers;

public static partial class Actions
{
    public static class Samurai
    {
        public static Action Mineuchi { get; private set; } = new(ActionType.GeneralAction, 31);

        public static Action Shirahadori { get; private set; } = new(ActionType.GeneralAction, 32);

        public static Action Iainuki { get; private set; } = new(ActionType.GeneralAction, 33);

        public static Action Zeninage { get; private set; } = new(ActionType.GeneralAction, 34);
    }
}
