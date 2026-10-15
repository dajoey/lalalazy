using System.Collections.Generic;

namespace Lalalazy.Crucible;

// Who may be given the enemy, per piece (round 4, snarl lane). Pure data, shared SOURCE (GluttonyCombo + LazyCrucible + both harnesses).

internal static partial class BST_CrucibleData
{
    /// <summary>
    ///     The largest single hit the logs put on whoever held the enemy in a piece, at Standard, per (board, battle). A familiar may be
    ///     given the enemy (the pull Snarl, the Provision swap) only when its HP is at least this hit, scaled to the board's degree
    ///     (<see cref="CrucibleDegree.FamiliarHitScale"/>) and with the cover margin (<see cref="CrucibleDegree.CoverMargin"/>): it then
    ///     survives the largest hit that piece is known to land on a tank. A piece that is not here was not measured and gets no pull Snarl.
    ///     <para>
    ///         How the numbers were taken (ffxivdb <c>action_events</c>, 2026-09-04 to 2026-10-04, the Standard period before the First Degree
    ///         loop; encounters of the job Bst on a Board with at least 20 s; enemy sources only; piece = the encounter title mapped to its
    ///         panel battle; at least 4 encounters): the larger of (a) the largest hit on a familiar and (b) 1.8 x the largest hit on the
    ///         character. (b) is there because a hit that follows enmity landed on the character in the Standard runs, where the familiar
    ///         never held the enemy: the recorded familiar-to-character ratio is 1.27 (Erratic Blaster), 1.65 (Sweeping Evisceration) and
    ///         1.81 (Evisceration at First Degree), and Strix's Darkness, 1,290 on the character at Standard, put 2,045 on a familiar at
    ///         First Degree. Rounded up to 10. Cleave-auto bosses (Pas de Seul, Guttler, Siren) are left out: the pull Snarl never fires there.
    ///     </para>
    ///     <para>
    ///         The Flauros Piece (5,1) is listed for completeness; its fight is owned by the cover Snarl and <see cref="ChallengeBackCasts"/>.
    ///         The Durga Piece (5,6) is listed so that it is visibly never given: Atomic Ray took 6,386 from the character.
    ///     </para>
    /// </summary>
    public static readonly IReadOnlyDictionary<(int Board, int Battle), int> FamiliarHitCeilings = new Dictionary<(int Board, int Battle), int>
    {
        // First Board (L30)
        [(1, 1)] = 160,   // bone knight: 18 encounters
        [(1, 2)] = 190,   // arch demon piece: 8
        [(1, 3)] = 270,   // piscodemon piece: 11
        [(1, 4)] = 280,   // banemite piece: 19
        [(1, 5)] = 240,   // ogre piece: 19
        // Second Board (L40)
        [(2, 0)] = 130,   // Loosefrox Inkyjots: 11
        [(2, 1)] = 380,   // manticore piece: 14
        [(2, 3)] = 380,   // wyvern piece: 12
        [(2, 4)] = 540,   // voidmancer piece: 14
        [(2, 5)] = 990,   // elder / younger tablitaur piece: 12
        // Third Board (L50)
        [(3, 1)] = 1110,  // cavalier piece: 38
        [(3, 2)] = 1130,  // ymir piece: 35
        [(3, 4)] = 1140,  // catoblepas piece: 29
        [(3, 5)] = 1030,  // lakhamu piece: 28
        // First Master's Board
        [(4, 0)] = 1840,  // Borgny the Venomous: 38
        [(4, 1)] = 2330,  // strix piece: 71 (Darkness 1,290 on the character x 1.8)
        [(4, 3)] = 1820,  // corpse flower / queen hawk piece: 50
        [(4, 5)] = 2620,  // gargoyle piece: 45 (Sweeping Evisceration 2,616 on a familiar)
        [(4, 8)] = 1450,  // treant / sapling piece: 59
        // Second Master's Board
        [(5, 0)] = 2360,  // Lauda the Spellcleaver: 7
        [(5, 1)] = 3330,  // flauros piece: 17 (Erratic Blaster 1,846 on the character x 1.8)
        [(5, 3)] = 1660,  // boogyman piece: 7 (Clearout 941 on a familiar, Veil of Darkness 921 on the character)
        [(5, 6)] = 11500, // durga piece: 10 (Atomic Ray 6,386 on the character x 1.8)
        [(5, 9)] = 1410,  // sphinx piece: 5
        [(5, 10)] = 840,  // gigantis piece: 6
    };

    /// <summary> The measured ceiling for a piece, 0 when the piece was not measured. </summary>
    public static int FamiliarHitCeiling(int board, int battle) =>
        FamiliarHitCeilings.TryGetValue((board, battle), out var ceiling) ? ceiling : 0;

    /// <summary>
    ///     Provision (Boogyman Piece, board 5 battle 3): the boss swaps club and sword, and its auto-attacks then leave the opposite weapon's
    ///     vulnerability (Blunt / Slashing Resistance Down, 30 s). The guide's answer is to swap who tanks at every swap. 49215 and 49216 are the
    ///     two casts (24 and 14 casts in 7 runs).
    /// </summary>
    public static readonly HashSet<uint> ProvisionCasts = [49215, 49216];

    /// <summary>
    ///     Pieces where the character takes the enemy back with Challenge once the cover Snarl's hit has landed, with the cast the cover is for
    ///     (a row of <see cref="HeavyCasts"/>): the Flauros Piece's Erratic Blaster, which hits the holder for 1,378 (character) / 1,752 (familiar).
    /// </summary>
    public static readonly IReadOnlyDictionary<(int Board, int Battle), uint> ChallengeBackCasts = new Dictionary<(int Board, int Battle), uint>
    {
        [(5, 1)] = 49188,
    };
}
