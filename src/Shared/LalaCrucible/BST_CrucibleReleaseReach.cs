using System.Collections.Generic;

namespace Lalalazy.Crucible;

// How far each familiar's area Tempered Release reaches and where its area is centred, for the do-not-attack guard (Zu eggs hatch
// when any hit lands on them, AoE included). Pure data, shared SOURCE. From the 7.56 Action sheet (xivapi v2, fetched 2026-10-13:
// /api/sheet/Action?rows=<every BST_Beasts ReleaseSkillId>&fields=Name,Range,EffectRange,CastType): CastType 2 = circle, 12 = line,
// 13 = cone. A release with Range 0 is a circle around the familiar; a cone or a line starts at the familiar and runs EffectRange
// toward the target, so it is held to a circle of EffectRange around the familiar (it can reach no further than that); a ranged
// circle (Range above 0, CastType 2) lands on the target. Only releases with the AoE trait are listed: a single-target or party-buff
// release hits nothing beside its target.
internal static partial class BST_CrucibleData
{
    /// <summary> One area release: its radius (yalms) and whether the area is centred on the familiar (true) or on the target (false). </summary>
    internal readonly record struct ReleaseReach(float Radius, bool FromFamiliar);

    /// <summary> XBMPet row -> the area its Tempered Release covers (rows with the AoE release trait only). </summary>
    public static readonly Dictionary<int, ReleaseReach> ReleaseReaches = new()
    {
        [8] = new(16f, true),   // diremite: Silkscreen, line 16
        [9] = new(9f, true),    // megalocrab: Bubble Shower, cone 9
        [11] = new(8f, true),   // vulture: Bloodcurdling Caw, circle 8 around the familiar
        [12] = new(8f, true),   // mandragora: Heirloom Scream, circle 8
        [14] = new(8f, true),   // puk: Tail Chase, circle 8
        [16] = new(8f, true),   // mantis: Eerie Soundwave, circle 8
        [17] = new(4f, false),  // slime: Syrup, circle 4 on the target
        [19] = new(8f, true),   // bat: Ultrasonics, circle 8
        [20] = new(5f, false),  // flying trap: Necrotic Nectar, circle 5 on the target
        [21] = new(8f, true),   // ziz: Petribreath, cone 8
        [23] = new(8f, true),   // golem: Rockslide, line 8
        [26] = new(15f, true),  // buffalo: War Cry, circle 15
        [27] = new(6f, true),   // uragnite: Gas Shell, cone 6
        [28] = new(12f, true),  // worm: Bottomless Desert, circle 12
        [30] = new(6f, true),   // goobbue: Moldy Sneeze, cone 6
        [33] = new(7f, true),   // coeurl: Charged Whisker, circle 7
        [34] = new(12f, true),  // raptor: Foul Breath, cone 12
        [36] = new(10f, true),  // treant: Arboreal Storm, circle 10
        [38] = new(9f, true),   // chimera: the Ram's Voice, circle 9
        [39] = new(12f, true),  // morbol: Bad Breath, cone 12
        [40] = new(8f, true),   // ghost: Curse, circle 8
        [41] = new(8f, true),   // salamander: Peculiar Light, circle 8
        [43] = new(15f, true),  // hydra: White Breath, cone 15
        [44] = new(8f, true),   // damselfly: Venom, cone 8
        [45] = new(20f, true),  // rotting goobbue: Inhale, cone 20
        [46] = new(12f, true),  // zu: Breath Wing, circle 12
        [48] = new(12f, false), // Karlabos: Tail Screw, circle 12 on the target
        [49] = new(12f, true),  // rafflesia: Blighted Bouquet, circle 12
        [50] = new(18f, false), // behemoth: Meteor, circle 18 on the target
    };
}
