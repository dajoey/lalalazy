using System;
using System.Numerics;
using LazyOccultCrescent.Enums;

namespace LazyOccultCrescent.Data.Traps;

public class TrapDatum(Vector3 position, OccultObjectType type)
{
    public readonly Vector3 Position = position;

    public readonly OccultObjectType Type = type;

    public string GetKey()
    {
        var x = (float)Math.Round(Position.X, 2);
        var y = (float)Math.Round(Position.Y, 2);
        var z = (float)Math.Round(Position.Z, 2);

        return $"{(uint)Type}:{x:F2},{y:F2},{z:F2}";
    }

    public TrapGroup GetGroup()
    {
        foreach (var group in TrapData.Groups)
        {
            foreach (var trap in group.Traps)
            {
                if (GetKey() == trap.GetKey())
                {
                    return group;
                }
            }
        }

        throw new Exception("Trap group not found");
    }
}
