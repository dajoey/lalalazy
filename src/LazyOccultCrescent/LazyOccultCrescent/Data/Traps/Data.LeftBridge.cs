using System.Collections.Generic;
using System.Numerics;
using LazyOccultCrescent.Enums;

namespace LazyOccultCrescent.Data.Traps;

public static partial class TrapData
{
    private static List<TrapGroup> LeftBridge { get; } =
    [
        // Set 1
        new([
            new TrapDatum(new Vector3(-566f, -852f, 292f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(-566f, -852f, 299f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(-566f, -852f, 306f), OccultObjectType.Trap),
        ]),

        // Set 2
        new([
            new TrapDatum(new Vector3(-538f, -852.00006f, 292f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(-538f, -852f, 299f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(-538f, -852f, 306f), OccultObjectType.Trap),
        ], 2),

        // Set 3
        new([
            new TrapDatum(new Vector3(-503f, -852f, 292f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(-503f, -852f, 299f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(-503f, -852f, 306f), OccultObjectType.Trap),
        ], 2),

        // Set 4
        new([
            new TrapDatum(new Vector3(-469.9653f, -852f, 292f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(-469.9653f, -852f, 306f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(-469.9653f, -852f, 306f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(-469.9653f, -852f, 292f), OccultObjectType.BigTrap),
        ], 2),

        // Set 5
        new([
            new TrapDatum(new Vector3(-434f, -852f, 292f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(-434f, -852f, 299f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(-434f, -852f, 306f), OccultObjectType.BigTrap),
        ]),
    ];
}
