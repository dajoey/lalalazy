using System.Globalization;

namespace LazyFateAutomation.Helpers.Extensions;

public static class IntExtensions {
    public static Vector2 Vec2(this int i) => new(i);
    public static Vector3 Vec3(this int i) => new(i);
    public static int Hex(this int i) => int.Parse(i.ToString("X"), NumberStyles.HexNumber);
}
