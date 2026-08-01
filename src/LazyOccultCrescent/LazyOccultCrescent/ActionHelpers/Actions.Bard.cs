using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyOccultCrescent.ActionHelpers;

public static partial class Actions
{
    public static class Bard
    {
        public static Action OffensiveAria { get; private set; } = new(ActionType.GeneralAction, 31);

        public static Action RomeosBallad { get; private set; } = new(ActionType.GeneralAction, 32);

        public static Action MightyMarch { get; private set; } = new(ActionType.GeneralAction, 33);

        public static Action HerosRime { get; private set; } = new(ActionType.GeneralAction, 34);
    }
}
