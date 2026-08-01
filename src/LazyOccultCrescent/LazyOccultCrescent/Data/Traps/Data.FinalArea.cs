using System.Collections.Generic;
using System.Numerics;
using LazyOccultCrescent.Enums;

namespace LazyOccultCrescent.Data.Traps;

public static partial class TrapData
{
    private static List<TrapGroup> FinalArea { get; } =
    [
        // Intersection normal traps
        new([
            new TrapDatum(new Vector3(696.5f, -500f, -474.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(703.5f, -500f, -474.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(703.5f, -500f, -467.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(696.5f, -500f, -467.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(700f, -500f, -471f), OccultObjectType.Trap),
        ]),

        // Intersection big traps
        new([
            new TrapDatum(new Vector3(691.5f, -500f, -476f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(695f, -500f, -479.5f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(705f, -500f, -479.5f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(708.5f, -500f, -476f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(708.5f, -500f, -466f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(704.5f, -500f, -462.5f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(695f, -500f, -462.5f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(691.5f, -500f, -466f), OccultObjectType.BigTrap),
        ]),

        // Tangential hallways
        new([
            new TrapDatum(new Vector3(673f, -500f, -468.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(673f, -500f, -473.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(727f, -500f, -473.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(727f, -500f, -468.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(739f, -500f, -468.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(739f, -500f, -473.5f), OccultObjectType.Trap),
        ]),

        // Left treasure room
        new([
            new TrapDatum(new Vector3(630f, -500f, -467f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(630f, -500.00003f, -475f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(638f, -500f, -475f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(638f, -500f, -467f), OccultObjectType.Trap),
        ]),

        // Right curved hallway
        new([
            new TrapDatum(new Vector3(802.5f, -500.00006f, -477f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(806f, -500.00003f, -471f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(818f, -500f, -483.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(811.5f, -500f, -486.5f), OccultObjectType.Trap),
        ]),

        // Right treasure room
        new([
            new TrapDatum(new Vector3(817f, -500f, -520.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(811f, -500f, -522f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(809.5f, -500f, -528f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(811f, -500f, -534f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(817f, -500f, -535.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(823f, -500f, -534f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(824.5f, -500f, -528f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(823f, -500f, -522f), OccultObjectType.Trap),
        ], 2),


        //Stairs
        new([
            new TrapDatum(new Vector3(694.5f, -498.476f, -503.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(700f, -498.476f, -503.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(705.5f, -498.476f, -503.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(705.5f, -493.55603f, -524f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(700f, -493.55603f, -524f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(694.5f, -493.55603f, -524f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(694.5f, -488.75604f, -544f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(700f, -488.75604f, -544f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(705.5f, -488.75604f, -544f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(705.5f, -483.95605f, -564f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(700f, -483.95605f, -564f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(694.5f, -483.95605f, -564f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(694.5f, -479.15604f, -584f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(700f, -479.15604f, -584f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(705.5f, -479.15604f, -584f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(705.5f, -476f, -603.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(700f, -476f, -603.5f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(694.5f, -476f, -603.5f), OccultObjectType.Trap),
        ], 3),
    ];
}
