using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyOccultCrescent.ActionHelpers;

public static partial class Actions
{
    public static class Thief
    {
        public static Action OccultSprint { get; private set; } = new(ActionType.GeneralAction, 31);

        public static Action Steal { get; private set; } = new(ActionType.GeneralAction, 32);

        public static Action Vigilance { get; private set; } = new(ActionType.GeneralAction, 33);

        public static Action TrapDetection { get; private set; } = new(ActionType.GeneralAction, 34);

        public static Action PilferWeapon { get; private set; } = new(ActionType.GeneralAction, 35);
    }
}
