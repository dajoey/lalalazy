using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyOccultCrescent.ActionHelpers;

public static partial class Actions
{
    public static class Chemist
    {
        public static Action Potion { get; private set; } = new(ActionType.GeneralAction, 31);

        public static Action Ether { get; private set; } = new(ActionType.GeneralAction, 32);

        public static Action Revive { get; private set; } = new(ActionType.GeneralAction, 33);

        public static Action Elixir { get; private set; } = new(ActionType.GeneralAction, 34);
    }
}
