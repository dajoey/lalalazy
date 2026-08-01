using System.Collections.Generic;
using System.Numerics;
using LazyOccultCrescent.Enums;

namespace LazyOccultCrescent.Data.Traps;

public static partial class TrapData
{
    private static List<TrapGroup> LeftHallway { get; } =
    [
        // First room small traps
        new([
            new TrapDatum(new Vector3(655.6104f, -500.192f, -85.59458f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(673.7584f, -500.0001f, -56.48542f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(668.2469f, -500.0001f, -56.54494f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(655.73f, -500.192f, -78.54089f), OccultObjectType.Trap),
        ]),

        // First room big traps
        new([
            new TrapDatum(new Vector3(681.6731f, -500.08502f, -76.61f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(678.53204f, -500.08502f, -80.284f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(672.7961f, -500.085f, -72.422f), OccultObjectType.BigTrap),
        ]),

        // After first Geomancer room
        new([
            new TrapDatum(new Vector3(624.4809f, -492.00003f, -5.53789f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(619.4255f, -492f, -5.511589f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(597.7759f, -489f, 4.60101f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(597.776f, -489.0001f, 7.265193f), OccultObjectType.Trap),
        ]),

        // In front of thief room
        new([
            new TrapDatum(new Vector3(583.5754f, -489f, -45.56033f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(581.2959f, -489f, -39.9053f), OccultObjectType.Trap),
        ]),


        // In front of mob guarded treasure room (one)
        new([
            new TrapDatum(new Vector3(625.8799f, -489f, -101.8446f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(629.6578f, -489.00003f, -103.9902f), OccultObjectType.Trap),
        ]),

        // Before first add pack
        new([
            new TrapDatum(new Vector3(595.7722f, -489f, 48.17526f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(590.3928f, -489f, 49.87876f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(585.2714f, -489.00006f, 51.71358f), OccultObjectType.Trap),
        ]),

        // big traps in nooks
        new([
            new TrapDatum(new Vector3(588.8248f, -489f, 75.44753f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(591.0157f, -489.00003f, 78.81108f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(631.0574f, -489f, 115.4413f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(634.2026f, -489.00003f, 117.5371f), OccultObjectType.BigTrap),
        ]),

        // Outside second treasure room (2)
        new([
            new TrapDatum(new Vector3(551.283f, -489f, 43.72131f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(553.133f, -489f, 49.61197f), OccultObjectType.Trap),
        ]),

        // Around second Geomancer trap
        new([
            new TrapDatum(new Vector3(590.7065f, -489f, 92.89005f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(586.7617f, -489f, 97.58625f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(583.0048f, -489.00003f, 102.0946f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(615.8428f, -489f, 130.0462f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(619.2331f, -489f, 125.9692f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(623.2242f, -489f, 121.1198f), OccultObjectType.Trap),
        ]),

        // Before button
        new([
            new TrapDatum(new Vector3(656.0482f, -489f, 119.4878f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(657.8513f, -489.00006f, 114.2046f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(659.6758f, -489f, 108.9721f), OccultObjectType.Trap),
        ]),
    ];
}
