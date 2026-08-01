using System.Numerics;

namespace LazyOccultCrescent.Modules.Data;

public struct Position
{
    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }


    public static Position Create(Vector3 vector3)
    {
        return new Position
        {
            X = vector3.X,
            Y = vector3.Y,
            Z = vector3.Z,
        };
    }
}
