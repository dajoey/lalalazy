namespace LazyFateAutomation.Helpers.Extensions;

public static class TupleExtensions {
    public static Vector3 ToVector3(this (float X, float Y, float Z) t) => new(t.X, t.Y, t.Z);
}
