using FFXIVClientStructs.FFXIV.Client.Game;

namespace LazyFateAutomation.Helpers.Extensions;

public static unsafe class GameMainExtensions {
    extension(GameMain) {
        public static bool IsTerritoryLoaded => GameMain.Instance()->TerritoryLoadState == 2;

        public static bool ExecuteCommand(CommandFlag command, int param1 = 0, int param2 = 0, int param3 = 0, int param4 = 0)
            => GameMain.ExecuteCommand((int)command, param1, param2, param3, param4);
        public static bool ExecuteLocationCommand(LocationCommandFlag command, Vector3 position, int param1 = 0, int param2 = 0, int param3 = 0, int param4 = 0)
            => GameMain.ExecuteLocationCommand((int)command, &position, param1, param2, param3, param4);
        public static bool ExecuteLocationCommand(int command, Vector3 position, int param1 = 0, int param2 = 0, int param3 = 0, int param4 = 0)
            => GameMain.ExecuteLocationCommand(command, &position, param1, param2, param3, param4);
    }
}
