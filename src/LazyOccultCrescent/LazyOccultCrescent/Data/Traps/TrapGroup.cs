using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ECommons.GameHelpers;

namespace LazyOccultCrescent.Data.Traps;

public class TrapGroup(List<TrapDatum> traps, uint max = 1)
{
    public readonly List<TrapDatum> Traps = traps;

    public readonly uint MaxInGroup = max;

    public Vector3 GetCenter()
    {
        if (Traps.Count == 0)
        {
            return Vector3.Zero;
        }

        var sum = Vector3.Zero;
        foreach (var trap in Traps)
        {
            sum += trap.Position;
        }

        return sum / Traps.Count;
    }

    public float GetDistance()
    {
        if (Traps.Count == 0)
        {
            return float.MaxValue;
        }

        return Traps.Min(trap => Player.DistanceTo(trap.Position));
    }

    public string GetKey()
    {
        if (Traps.Count == 0)
        {
            return "0:-:-:-";
        }

        var first = Traps.First().Position;

        var x = (float)Math.Round(first.X, 2);
        var y = (float)Math.Round(first.Y, 2);
        var z = (float)Math.Round(first.Z, 2);

        return $"{Traps.Count}:{x:F2},{y:F2},{z:F2}";
    }

    public TrapGroup Clone()
    {
        var clonedTraps = Traps
            .Select(t => new TrapDatum(t.Position, t.Type))
            .ToList();

        return new TrapGroup(clonedTraps, MaxInGroup);
    }
}
