using System.Collections.Generic;
using System.Numerics;
using LazyOccultCrescent.Enums;

namespace LazyOccultCrescent.Data.Traps;

public static partial class TrapData
{
    private static List<TrapGroup> PuzzleRoom { get; } =
    [
        // First hallway
        new([
            new TrapDatum(new Vector3(698.5183f, -500f, -295.7303f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(701.7409f, -500.00006f, -295.7303f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(701.8219f, -500f, -315.7228f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(698.399f, -500f, -315.7228f), OccultObjectType.Trap),
        ]),

        // Lobby big traps
        new([
            new TrapDatum(new Vector3(700f, -500f, -340f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(700f, -500f, -348f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(692f, -500.00003f, -348f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(684f, -500f, -348f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(708f, -500f, -348f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(716f, -500f, -348f), OccultObjectType.BigTrap),
        ]),

        // Left hallway
        new([
            new TrapDatum(new Vector3(652.0206f, -500f, -340.3084f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(652.1485f, -500f, -347.8252f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(652.1485f, -500f, -355.6916f), OccultObjectType.Trap),
        ]),

        // Left intersection normal traps
        new([
            new TrapDatum(new Vector3(618.5586f, -499.85f, -344.5586f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(618.5586f, -499.85004f, -351.3483f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(625.3483f, -499.85004f, -351.3483f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(625.3483f, -499.85f, -344.5586f), OccultObjectType.Trap),
        ]),

        // Left intersection big traps
        new([
            new TrapDatum(new Vector3(609.5416f, -499.842f, -341.3686f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(615.5901f, -499.89355f, -360.1894f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(633.7816f, -499.895f, -354.5076f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(629.0118f, -499.84f, -335.9709f), OccultObjectType.BigTrap),
        ]),

        // Right hallway
        new([
            new TrapDatum(new Vector3(747.9138f, -500.00003f, -355.8246f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(747.9138f, -500f, -348f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(747.9138f, -500.00003f, -340.2623f), OccultObjectType.Trap),
        ]),

        // Right intersection normal traps
        new([
            new TrapDatum(new Vector3(781.5257f, -499.85004f, -351.0338f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(781.5257f, -499.85004f, -344.8972f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(774.6306f, -499.85f, -344.8972f), OccultObjectType.Trap),
            new TrapDatum(new Vector3(774.6306f, -499.85f, -351.0338f), OccultObjectType.Trap),
        ]),

        // Right intersection big traps
        new([
            new TrapDatum(new Vector3(784.567f, -499.86926f, -360.1894f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(790.533f, -499.84003f, -341.3686f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(771.4991f, -499.88974f, -335.9709f), OccultObjectType.BigTrap),
            new TrapDatum(new Vector3(765.7463f, -499.8744f, -354.5076f), OccultObjectType.BigTrap),
        ]),
    ];
}
