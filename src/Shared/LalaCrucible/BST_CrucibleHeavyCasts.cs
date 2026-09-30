using System.Collections.Generic;

namespace Lalalazy.Crucible;

// First / Second Master's Board heavy casts, MEASURED from the recorded runs of 2026-09-26 .. 09-30 (the plugin's CR|
// telemetry, the IINACT network logs and ffxivdb action events; 30 heavy hits across 10 of the 24 battles). Pure data,
// shared SOURCE (GluttonyCombo + LazyCrucible + both harnesses). Nothing here is a guide number: a battle that was never
// fought in those runs is absent from <see cref="BST_CrucibleData.MeasuredBattles"/> and its casts stay guide-derived.

/// <summary> What a heavy cast does to whoever it hits, as the logs show it. </summary>
public enum CrucibleHitKind : byte
{
    /// <summary>
    ///     A single-target cast that hits whoever holds enmity when it lands (the victim was the holder in every logged cast): Snarl ->
    ///     Parting Blow dodges it.
    /// </summary>
    Tankbuster,

    /// <summary> Hits the character and the familiar together: recorded, never a Snarl rule. </summary>
    PartyWide,

    /// <summary> An area, line or ground hit centred on the caster or the ground: it hits whoever stands in the shape, not whoever holds enmity. Position answers it. </summary>
    Positional,

    /// <summary> Hit the character although the familiar held enmity (line of sight / mitigation answers it): a warning, never a Snarl rule. </summary>
    CastOnly,
}

/// <summary>
///     One measured heavy cast, keyed by the cast id the plugin reads from the target (<c>TargetCastId</c>).
///     <see cref="CastSeconds"/> is the plugin's own total castbar; <see cref="HitDelay"/> is when the damage lands relative to the
///     moment the plugin's remaining reaches 0 (negative = before). <see cref="MaxOnCharacter"/> is the largest single hit on the
///     character in the logs (max HP on these boards is about 4,060); <see cref="Casts"/> / <see cref="Hits"/> say how many logged
///     casts there were and how many of them hurt someone.
/// </summary>
public readonly record struct CrucibleHeavyCast(
    uint CastId,
    string Name,
    byte Board,
    byte Battle,
    float CastSeconds,
    float HitDelay,
    CrucibleHitKind Kind,
    int MaxOnCharacter,
    int MaxOnFamiliar,
    int Casts,
    int Hits,
    bool Warn,
    string Evidence);

internal static partial class BST_CrucibleData
{
    /// <summary>
    ///     Battles fought in the recorded runs (board 4 = First Master's, 5 = Second Master's; battle 0 is the boss). A cast of any other
    ///     battle in these two boards has no measurement behind it: guide-derived at best.
    /// </summary>
    public static readonly HashSet<(int Board, int Battle)> MeasuredBattles =
    [
        (4, 0), (4, 1), (4, 3), (4, 5), (4, 7), (4, 8),
        (5, 1), (5, 3), (5, 5), (5, 6),
    ];

    /// <summary> Whether a battle of the two Master's Boards was actually fought in the recorded runs. </summary>
    public static bool IsMeasuredBattle(int board, int battle) => MeasuredBattles.Contains((board, battle));

    /// <summary>
    ///     Second Master's Tankbusters no recorded run ever saw cast (49205 Void Thunder III, 49470 Thunderbolt): kept from the guides and
    ///     BossmodReborn, NOT measured. Every other Master's Board entry in <see cref="Tankbusters"/> has a row in <see cref="HeavyCasts"/>.
    /// </summary>
    public static readonly HashSet<uint> GuideBoundTankbusters = [49205, 49470];

    public static readonly CrucibleHeavyCast[] HeavyCasts =
    [
        // ---- First Master's Board, single-target casts that follow the enmity holder -------------------------------------------
        new(48809, "Toxic Vomit", 4, 0, 4.7f, -1.2f, CrucibleHitKind.Tankbuster, 517, 675, 15, 15, false,
            "Borgny. The plugin shows 4.7 s at cast start; the hit lands 3.5 s after the start, 1.2 s before the plugin's remaining reaches 0 (15 of 15). Cast line names the holder 15 of 15; three more ticks (48810, 400-517 each) follow 3 s apart."),
        new(48822, "Salivous Snap", 4, 0, 6.7f, 0.3f, CrucibleHitKind.Tankbuster, 875, 1077, 5, 4, false,
            "Borgny. Cast line names the holder 5 of 5; victim was the holder in 4 of 4 (character 3, familiar 1 after Snarl)."),
        new(48689, "Final Sting", 4, 3, 4.7f, 0.3f, CrucibleHitKind.Tankbuster, 616, 0, 7, 4, false,
            "Queen Hawk Piece. Cast line names the holder 6 of 7; victim was the holder in 4 of 4."),
        new(48717, "Sweeping Evisceration", 4, 5, 7.6f, 1.3f, CrucibleHitKind.Tankbuster, 1225, 2021, 20, 18, false,
            "Gargoyle Piece, hit id 50933. Victim was the holder in 18 of 18 (character 14, familiar 4: 1,225 / 2,021); the hit lands 1.3 s after the castbar."),
        new(48730, "Grim Fate", 4, 5, 4.7f, 1.3f, CrucibleHitKind.Tankbuster, 212, 212, 7, 6, false,
            "Gargoyle Piece, hit id 48731 is a FIVE-hit string of 121-212 each, 645-1,030 per cast; cast line names the holder 7 of 7; first hit 1.3 s after the castbar."),
        new(50649, "Obliterate", 4, 7, 4.7f, 0.3f, CrucibleHitKind.Tankbuster, 1349, 1112, 2, 2, false,
            "Golem Piece. Victim was the holder in 2 of 2 (character 1,349, familiar 1,112); only two casts logged."),
        new(48669, "On the Properties of Darkness", 4, 1, 7.7f, 0.3f, CrucibleHitKind.Tankbuster, 1265, 0, 13, 10, false,
            "Strix Piece. Hit the character in 10 of 10 hits and never the familiar, which the guide's 'room-wide, cannot be avoided' contradicts; the character held enmity in every logged cast, so following the holder is consistent but not proven against a familiar holding it."),

        // ---- Second Master's Board ---------------------------------------------------------------------------------------------
        new(49188, "Erratic Blaster", 5, 1, 6.7f, 0.3f, CrucibleHitKind.Tankbuster, 1378, 1752, 6, 6, false,
            "Flauros Piece, hit id 49189. Cast line names the holder 6 of 6; 1,378 on the character, 1,752 on the familiar (2 hits)."),
        new(49254, "Mangling Fang", 5, 5, 4.7f, 0.4f, CrucibleHitKind.Tankbuster, 678, 0, 2, 2, false,
            "Drake Piece. Cast line names the holder 2 of 2; only two casts logged."),

        // ---- Not a Snarl rule ------------------------------------------------------------------------------------------------------
        new(48690, "Rotten Stench", 4, 3, 5.7f, -0.5f, CrucibleHitKind.PartyWide, 773, 534, 13, 8, false,
            "Corpse Flower Piece, hit id 48691: character and familiar hit in the same burst in 6 of 8 bursts."),
        new(49272, "Atomic Ray", 5, 6, 12.7f, 0.3f, CrucibleHitKind.CastOnly, 4782, 0, 2, 2, true,
            "Durga Piece. Both casts began while the familiar held enmity and both hit the CHARACTER for 3,998 and 4,782 (max HP about 4,060); the character died twice in one visit."),
        new(48721, "Rippling Evisceration", 4, 5, 7.7f, 0.3f, CrucibleHitKind.Positional, 1244, 0, 22, 1, false,
            "Gargoyle Piece: ground circle/donut, 1 of 22 casts hurt anyone."),
        new(48729, "Sea of Pitch", 4, 5, 2.7f, 0.3f, CrucibleHitKind.Positional, 774, 0, 29, 1, false,
            "Gargoyle Piece: ground puddles (cast line names the ground), 1 of 29 casts hurt anyone."),
        new(48815, "Touchdown", 4, 0, 3.7f, 0.3f, CrucibleHitKind.Positional, 927, 0, 9, 7, false,
            "Borgny, hit id 48816: centred on the caster; hit the character in 7 of 9, never the familiar."),
        new(49266, "Grounding Jolt", 5, 6, 4.9f, 0.3f, CrucibleHitKind.Positional, 1167, 0, 7, 1, false,
            "Durga Piece: two circles by hand (hit ids 49265 at 1,167 and 49266 at 890, both from this one cast), 1 of 7 casts hurt anyone; the sibling cast 49265 (21 casts) hurt nobody."),
    ];

    private static Dictionary<uint, CrucibleHeavyCast>? _heavyByCast;

    /// <summary> The measured row for a cast id, or null when no recorded run measured it. </summary>
    public static CrucibleHeavyCast? HeavyCast(uint castId)
    {
        if (_heavyByCast is null)
        {
            var map = new Dictionary<uint, CrucibleHeavyCast>(HeavyCasts.Length);
            foreach (var h in HeavyCasts)
                map[h.CastId] = h;
            _heavyByCast = map;
        }

        return _heavyByCast.TryGetValue(castId, out var row) ? row : null;
    }

    /// <summary> The measured casts worth a warning in the fight guide (hits the character whatever the familiar does). </summary>
    public static IEnumerable<CrucibleHeavyCast> WarnCasts()
    {
        foreach (var h in HeavyCasts)
            if (h.Warn)
                yield return h;
    }
}
