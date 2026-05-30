using System.Globalization;

namespace LazyFateAutomation.Helpers.Extensions;

public static class UintExtensions {
    public static uint Hex(this uint i) => uint.Parse(i.ToString("X"), NumberStyles.HexNumber);
    public static uint Reverse(this uint value)
        => ((value & 0x000000FFu) << 24) | ((value & 0x0000FF00u) << 8) |
            ((value & 0x00FF0000u) >> 8) | ((value & 0xFF000000u) >> 24);
}
