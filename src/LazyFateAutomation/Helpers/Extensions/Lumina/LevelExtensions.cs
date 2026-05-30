using Lumina.Excel.Sheets;

namespace LazyFateAutomation.Helpers.Extensions;

public static class LevelExtensions {
    public static Vector3 ToVector3(this Level row) => new(row.X, row.Y, row.Z);
}
