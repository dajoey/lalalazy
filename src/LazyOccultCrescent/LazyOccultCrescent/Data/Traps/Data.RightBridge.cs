using System.Collections.Generic;
using System.Numerics;
using LazyOccultCrescent.Enums;

namespace LazyOccultCrescent.Data.Traps;

public static partial class TrapData
{
    private static List<TrapGroup> RightBridge { get; } =
    [
        // Set 1
        new([
            new TrapDatum(new Vector3(-566f, -852f, 322f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(-566f, -852f, 329f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(-566f, -852f, 336f), OccultObjectType.Trap),
        ]),

        // Set 2
        new([
            new TrapDatum(new Vector3(-538f, -852f, 322f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(-538f, -852f, 329f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(-538f, -852f, 336f), OccultObjectType.Trap),
        ], 2),

        // Set 3
        new([
            new TrapDatum(new Vector3(-503f, -852f, 322f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(-503f, -852f, 329f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(-503f, -852f, 336f), OccultObjectType.Trap),
        ], 2),

        // Set 4
        new([
            new TrapDatum(new Vector3(-470f, -852f, 336f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(-470f, -852f, 322f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(-470f, -852f, 322f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(-470f, -852f, 336f), OccultObjectType.Trap),
        ], 2),

        // Set 5
        new([
            new TrapDatum(new Vector3(-433.9653f, -852f, 322f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(-434f, -852.00006f, 329f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(-433.9653f, -852f, 336f), OccultObjectType.BigTrap),
        ]),
    ];
}
