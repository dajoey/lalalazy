using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyOccultCrescent.ActionHelpers;

public static partial class Actions
{
    public static class Geomancer
    {
        public static Action BattleBell { get; private set; } = new(ActionType.GeneralAction, 31);
    }
}
