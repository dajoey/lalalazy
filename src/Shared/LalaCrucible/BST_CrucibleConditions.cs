using System.Collections.Generic;

namespace Lalalazy.Crucible;

// Conditional target rules and status reads the rotation keys on (2026-10-13 research pass). Pure data, shared SOURCE.
// Ids: 7.56 Status / Action sheets (xivapi v2, fetched 2026-10-13), the recorded runs (ffxivdb status_events / action_events,
// 2026-09-08 .. 10-11) and stock BossmodReborn's Crucible modules (origin/main be8ff771c), cited per set.
internal static partial class BST_CrucibleData
{
    // ------------------------------------------------------------------ poisons on the familiar (Parting Blow sheds them)

    /// <summary>
    ///     Poisons a familiar sheds by leaving: a Parting Blow right after the hit takes the damage over time away with it (the
    ///     post-hotfix answer the B1 guides give for Banemite's Deadly Thrust 46906 and Pas de Seul's Cold Caress 46935, both one hit
    ///     that leaves 5140 Poison, 18 s: familiar 9 and 15 times in the recorded runs) and Borgny's Toxicosis (5183, 11.7 s; 5553 /
    ///     5554 are its other Toxicosis rows). <see cref="PoisonStringStatuses"/> marks the ones applied by a multi-hit string.
    /// </summary>
    public static readonly HashSet<uint> PoisonShedStatuses = [5140, 5183, 5553, 5554];

    /// <summary>
    ///     Poisons put on by a string of hits on the enmity holder: Toxic Vomit (48809) is followed by three more hits (48810) at
    ///     +3.2 / +6.3 / +9.4 s, each on whoever holds the enemy and each refreshing Toxicosis; when the familiar left mid-string the
    ///     remaining hits landed on the character (2026-10-02 15:55, 10-03 11:05). A shed waits until the string has ended: the
    ///     status has not been refreshed for <see cref="PoisonStringGapSeconds"/>, i.e. its time left is at most
    ///     <see cref="PoisonStringFullSeconds"/> minus that gap.
    /// </summary>
    public static readonly HashSet<uint> PoisonStringStatuses = [5183, 5553, 5554];

    /// <summary>
    ///     The casts whose poison a familiar sheds: Deadly Thrust 46906, Cold Caress 46935, Toxic Vomit 48809 and its follow-up hits 48810.
    ///     A poison only counts when one of these was being cast just before it landed on the familiar: 5140 also comes from the Chimera
    ///     and other enemies, and those are not shed.
    /// </summary>
    public static readonly HashSet<uint> PoisonShedCasts = [46906, 46935, 48809, 48810];

    /// <summary> Toxicosis as applied (11.7 s measured, 11.8 s on some rows): the full duration a refresh restores. </summary>
    public const float PoisonStringFullSeconds = 11.7f;

    /// <summary> The string's hits land 3.1-3.2 s apart: a Toxicosis not refreshed for this long has no hit of the string left. </summary>
    public const float PoisonStringGapSeconds = 3.5f;

    // ------------------------------------------------------------------ knockback battles

    /// <summary>
    ///     (board, battle) of every battle with a known knockback cast: the battles of the core lane's <c>BST.CrucibleKnockbacks.Casts</c>
    ///     (BST_Helper.cs, from stock BMR's Crucible knockback components). Vileskin (knockback immunity) is kept for the push there, never
    ///     spent on a predicted hit. Kept as a battle list here so this rule needs nothing from that file; on merge it can read
    ///     <c>BST.CrucibleKnockbacks.BattleHasKnockback</c> instead.
    /// </summary>
    public static readonly HashSet<(int Board, int Battle)> KnockbackBattles =
    [
        (2, 3), (2, 5), (3, 0), (3, 1), (3, 2), (3, 5), (4, 0), (4, 1), (4, 2), (4, 4), (4, 5), (4, 7), (4, 9), (5, 0), (5, 3), (5, 4), (5, 6),
    ];

    // ------------------------------------------------------------------ the character's own protection

    /// <summary> Blink (Tome of the Impervious): "impervious to most physical attacks; each nullified attack reduces one stack". </summary>
    public const uint BlinkStatus = 4849;

    /// <summary> Paralysis: "deadened nerves are sometimes preventing the execution of actions" (15 to 24 s; 19 adds on familiars in the recorded runs, Thunderbolt and Lauda). </summary>
    public const uint ParalysisStatus = 5388;

    /// <summary> Poison resistance on the character: Poison Resistance Up (4961, the coblyn's Vulcanize) and Spirit of the Breathtaker (2322, Potion of Breathtaking Swiftness). </summary>
    public static readonly HashSet<uint> PoisonResistStatuses = [4961, 2322];

    /// <summary> Poisons seen on the character in the Crucible (the 18 Poison of open-world enemies, Banemite / Pas de Seul / Chimera 5140, Borgny's Toxicosis rows). </summary>
    public static readonly HashSet<uint> PoisonStatuses = [18, 5140, 5183, 5553, 5554];

    /// <summary>
    ///     Registered hard hits whose damage is physical (Action sheet AttackType 1-3; for a cast whose own row has none, the type of its
    ///     hit id): Skullsplinter 46872, Deadly Thrust 46906, Deadly Hold 48138, 100-tonze Slash 48204, Crushing Blade 48471, Salivous
    ///     Snap 48822, Final Sting 48689, Grim Fate 48730 (hit 48731), Sweeping Evisceration 48717 (hit 50933), Mangling Fang 49254,
    ///     Touchdown 48815 (hit 48816). Blink nullifies these, so no guard potion goes out for them while it is up.
    /// </summary>
    public static readonly HashSet<uint> PhysicalCasts = [46872, 46906, 48138, 48204, 48471, 48822, 48689, 48730, 48717, 49254, 48815];

    // ------------------------------------------------------------------ conditional do-not-attack

    /// <summary> The Second Master's Drake fight: the Abaddon eats the Morphos; it is not attacked while any Morpho lives (BMR DrakePiece.cs CalculateModuleAIHints, guide "Abaddon last, after every Morpho is eaten"). </summary>
    public const uint AbaddonNameId = 14655;

    public const uint MorphoNameId = 14656;

    /// <summary> Gigantis Piece (Second Master's battle 10) carries this status with the club's element in its param: 0x499 lightning, 0x49A fire (BMR GigantisPiece.cs SID.WeaponElement). </summary>
    public const uint GigantisClubStatus = 2056;

    public const uint GigantisClubLightning = 0x499;
    public const uint GigantisClubFire = 0x49A;

    /// <summary> The big gels by BNpcBase (the small ones, 0x4D06 / 0x4D08, share the names and are never held back): Congealed Lightning 0x4D05, Congealed Kindling 0x4D07. </summary>
    public const uint CongealedLightningBase = 0x4D05;

    public const uint CongealedKindlingBase = 0x4D07;
}
