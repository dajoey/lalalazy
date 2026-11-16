// Game/runtime boundary stubs for the LazyWTMath UI harness. Everything else the window draws is the real source.
using System.Runtime.InteropServices;

// Stand-in for the one game-memory type the window and the solver read. The real type lives in the game's client
// structs; the harness does not reference that assembly, so the same name is declared here.
namespace FFXIVClientStructs.FFXIV.Client.Game.UI
{
    public unsafe struct PlayerState
    {
        private static PlayerState* _memory;
        private static bool _loggedIn;

        public byte WeeklyBingoNumPlacedStickers;
        private fixed byte _placed[16];

        /// <summary>Null while no character is logged in, like the real accessor.</summary>
        public static PlayerState* Instance() => _loggedIn ? _memory : null;

        public bool IsWeeklyBingoStickerPlaced(int index) => _placed[index] != 0;

        /// <summary>Harness control: log a character in with the given journal state, or out.</summary>
        internal static void Set(bool loggedIn, int stickerCount, IReadOnlyCollection<int> placedCells)
        {
            if (_memory == null)
                _memory = (PlayerState*)NativeMemory.AllocZeroed((nuint)sizeof(PlayerState));
            _loggedIn = loggedIn;
            _memory->WeeklyBingoNumPlacedStickers = (byte)stickerCount;
            for (var i = 0; i < 16; i++)
                _memory->_placed[i] = (byte)(placedCells.Contains(i) ? 1 : 0);
        }
    }
}

// The plugin's static holder (System.cs) also names the native-addon controller from the vendored toolkit, which cannot
// be compiled here; the window only reads PerfectTails from it. Same class name so the real code resolves it unchanged.
namespace WondrousTailsSolver
{
#pragma warning disable CS8618
    public static class System
    {
        public static PerfectTails PerfectTails { get; set; }
    }
#pragma warning restore CS8618
}
