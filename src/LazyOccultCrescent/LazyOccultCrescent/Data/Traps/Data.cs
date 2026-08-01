using System;
using System.Collections.Generic;
using LazyOccultCrescent.Modules.Data;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace LazyOccultCrescent.Data.Traps;

public static partial class TrapData
{
    public readonly static List<TrapGroup> Groups;

    static TrapData()
    {
        Groups =
        [
            ..LeftHallway,
            ..RightHallway,
            ..HallwayJoin,
            ..LeftBridge,
            ..RightBridge,
            ..PuzzleRoom,
            ..FinalArea,
        ];
    }

    // Built once. The old lookup walked 189 entries calling GetKey() on both
    // sides of every comparison - GetKey() interpolates a string, so a single
    // miss allocated ~378 strings - and then threw for any trap not in the
    // Forked Tower tables, which describes every trap in the overworld.
    private readonly static Dictionary<string, TrapGroup> GroupByTrapKey = BuildIndex();

    private static Dictionary<string, TrapGroup> BuildIndex()
    {
        var index = new Dictionary<string, TrapGroup>();

        foreach (var group in Groups)
        {
            foreach (var trap in group.Traps)
            {
                index[trap.GetKey()] = group;
            }
        }

        return index;
    }

    // Null means "not a trap we have mapped", which is a normal answer, not an error.
    public static TrapGroup? GetGroup(IEventObj obj)
    {
        return GroupByTrapKey.GetValueOrDefault(obj.GetKey());
    }
}
