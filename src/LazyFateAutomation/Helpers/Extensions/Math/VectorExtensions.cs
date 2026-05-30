namespace LazyFateAutomation.Helpers.Extensions;

public static class VectorExtensions {
    public static Vector3 RandomPoint(this Vector3 center, float radius) {
        var random = new Random();
        var angle = random.NextFloat(0, 1) * 2f * MathF.PI;
        var distance = random.NextFloat(0, radius);
        return center + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * distance;
    }

    public static Vector3 OnMesh(this Vector3 position) {
        var floor = Svc.Navmesh.NearestPointReachable(position, 5, 5);
        if (floor is null)
            Svc.Log.PrintWarning($"Failed to find point on mesh near {position}");
        return floor ?? position;
    }

    public static Vector3 OnMesh(this Vector2 position) {
        var floor = Svc.Navmesh.PointOnFloor(new Vector3(position.X, 1024, position.Y));
        if (floor is null)
            Svc.Log.PrintWarning($"Failed to find point on floor from {position}");
        return floor ?? new Vector3(position.X, Svc.Objects.LocalPlayer?.Position.Y ?? 0, position.Y);
    }

    public static Vector3 RotatePoint(this Vector3 p, float cx, float cy, float angle) {
        if (angle == 0f) return p;
        var s = (float)Math.Sin(angle);
        var c = (float)Math.Cos(angle);

        // translate point back to origin:
        p.X -= cx;
        p.Z -= cy;

        // rotate point
        var xnew = p.X * c - p.Z * s;
        var ynew = p.X * s + p.Z * c;

        // translate point back:
        p.X = xnew + cx;
        p.Z = ynew + cy;
        return p;
    }

    public static Vector3 Add(this Vector3 x, Vector3 y) => x + y;
    public static Vector3 AddX(this Vector3 v, float x) => v + new Vector3(x, 0f, 0f);
    public static Vector3 AddY(this Vector3 v, float y) => v + new Vector3(0f, y, 0f);
    public static Vector3 AddZ(this Vector3 v, float z) => v + new Vector3(0f, 0f, z);

    public static Vector2 ToVector2(this Vector3 v) => new(v.X, v.Z);
    public static Vector3 ToVector3(this Vector2 v) => new(v.X, 0, v.Y);
}
