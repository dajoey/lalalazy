using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyOccultCrescent.ActionHelpers;

public static partial class Actions
{
    public static class Berserker
    {
        public static Action Rage { get; private set; } = new(ActionType.GeneralAction, 31);

        public static Action DeadlyBlow { get; private set; } = new(ActionType.GeneralAction, 32);
    }
}
