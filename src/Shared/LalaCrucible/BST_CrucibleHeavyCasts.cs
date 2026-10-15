using System.Collections.Generic;

namespace Lalalazy.Crucible;

// First / Second Master's Board heavy casts, MEASURED from the recorded runs (the plugin's CR|
// telemetry, the IINACT network logs and ffxivdb action events; the Master's Board rows from the 2026-09-26 .. 09-30 runs,
// the Second/Third Board tankbuster rows re-derived 2026-10-15 over the 09-21 .. 10-14 action events). Pure data,
// shared SOURCE (GluttonyCombo + LazyCrucible + both harnesses). Nothing here is a guide number: a battle that was never
// fought in those runs is absent from <see cref="BST_CrucibleData.MeasuredBattles"/> and its casts stay guide-derived.
// Cast SECONDS for the Second/Third Board rows come from the 7.56 sheet and crucible-bindings (the plugin logged no c= castbar
// for those ids in the database window); every hit number on them is measured.

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
    ///     Battles fought in the recorded runs (board 4 = First Master's, 5 = Second Master's; 2026-10-15: the Second/Third Board
    ///     battles whose tankbusters got measured rows; battle 0 is the boss). A cast of any other battle has no measurement behind
    ///     it: guide-derived at best.
    /// </summary>
    public static readonly HashSet<(int Board, int Battle)> MeasuredBattles =
    [
        (4, 0), (4, 1), (4, 3), (4, 5), (4, 7), (4, 8),
        (5, 1), (5, 3), (5, 5), (5, 6),
        (3, 2),   // r4-third2: Third Board battle 2 (Ymir / Sahagin), 51 recorded runs: the Dreadwash row below
        (2, 1),
        (2, 5),
        (3, 1),
        (2, 6),
    ];

    /// <summary> Whether a battle of the two Master's Boards was actually fought in the recorded runs. </summary>
    public static bool IsMeasuredBattle(int board, int battle) => MeasuredBattles.Contains((board, battle));

    /// <summary>
    ///     Tankbusters no recorded run ever saw cast (49205 Void Thunder III and 49470 Thunderbolt on the Second Master's Board, 48517 Goblin Rush on
    ///     the Second Board — R4 row 2.0-07): kept from the guides and BossmodReborn, NOT measured. Every other Master's Board entry in
    ///     <see cref="Tankbusters"/> has a row in <see cref="HeavyCasts"/>.
    /// </summary>
    public static readonly HashSet<uint> GuideBoundTankbusters = [49205, 49470, 48517];

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
            "Golem Piece. Victim was the holder in 2 of 2 (character 1,349, familiar 1,112); only two casts logged. Re-checked 2026-10-13 over 7 casts (ffxivdb to 10-11): the holder at the hit took it 7 of 7 (familiar 1, character 6, one of them after a Parting Blow recalled the familiar); BMR's knockback typing does not change who is hit."),
        new(48669, "On the Properties of Darkness", 4, 1, 7.7f, 0.3f, CrucibleHitKind.Tankbuster, 1265, 0, 13, 10, false,
            "Strix Piece. Hit the character in 10 of 10 hits and never the familiar, which the guide's 'room-wide, cannot be avoided' contradicts; the character held enmity in every logged cast, so following the holder is consistent but not proven against a familiar holding it. Settled 2026-10-13 over 139 casts (ffxivdb to 10-11): a familiar still holding after a Snarl took it 2 of 2 (2,004 / 2,045, degree not recorded, so not in MaxOnFamiliar); with a Parting Blow during the cast, the character 39 of 39. It follows the holder; BMR's room-wide typing is wrong about the target."),

        // ---- Third Board, battle 2 (r4-third2) ----------------------------------------------------------------------------------
        new(48485, "Dreadwash", 3, 2, 8.0f, 0.0f, CrucibleHitKind.Tankbuster, 910, 948, 87, 64, false,
            "Sahagin Piece, ffxivdb 2026-09-24 to 2026-10-07: 87 cast starts, 64 effects 7.96-8.00 s after the start (lands at the castbar's end). Each completed cast hit one target, the enmity holder: 33 on the character (479-910), 31 on a familiar (424-948). Hysteria (296) followed 28 of the 33 character hits."),
        // ---- Second Board, single-target casts that follow the enmity holder ----------------------------------------------------
        new(48138, "Deadly Hold", 2, 1, 5.0f, 0f, CrucibleHitKind.Tankbuster, 206, 227, 52, 24, false,
            "Battle-1 manticore trash (R4 row 2.1-02). Cast 5.0 s (7.56 sheet, crucible-bindings C2); HitDelay not measured (Boards 1-3 stay 0). Re-derived 2026-10-15 from ffxivdb action_events: 52 rows, 24 with damage; character max 206 (20 hits), familiars Diremite 227, Colibri 226, Treant 206, Puk 189."),
        new(48204, "100-tonze Slash", 2, 5, 9.0f, 0f, CrucibleHitKind.Tankbuster, 549, 614, 31, 16, false,
            "The tablitaur pair, battle 5 (R4 row 2.5-04). Cast 9.0 s (crucible-bindings C2: the Elder's TB-icon line at the player, tether 260 from the Younger); HitDelay not measured (Boards 1-3 stay 0). Re-derived 2026-10-15 from ffxivdb action_events: 31 rows, 16 with damage; character max 549 (5 hits), familiars Drake 614, Bat 588, Colibri 552, Gigantoad 521, Puk 520, Antling 505, Vulture 349, Uragnite 250."),

        // ---- Third Board, single-target casts that follow the enmity holder -----------------------------------------------------
        new(48471, "Crushing Blade", 3, 1, 5.0f, 0f, CrucibleHitKind.Tankbuster, 455, 0, 177, 88, false,
            "Cavalier Piece, battle 1 (R4 row 3.1-08a). Cast 5.0 s (7.56 sheet, crucible-bindings); HitDelay not measured (Boards 1-3 stay 0). Re-derived 2026-10-15 from ffxivdb action_events: 177 rows, 88 with damage, all 88 on the character, max 455; never hit a familiar, so the cover gate uses EstimatedTankbusterHits[48471] (455 x 1.65 = 751). The cast is also the battle's 15 y knockback (CrucibleKnockbacks)."),

        // ---- Second Master's Board ---------------------------------------------------------------------------------------------
        new(49188, "Erratic Blaster", 5, 1, 6.7f, 0.3f, CrucibleHitKind.Tankbuster, 1378, 1752, 6, 6, false,
            "Flauros Piece, hit id 49189. Cast line names the holder 6 of 6; 1,378 on the character, 1,752 on the familiar (2 hits)."),
        new(49254, "Mangling Fang", 5, 5, 4.7f, 0.4f, CrucibleHitKind.Tankbuster, 678, 691, 2, 2, false,
            "Drake Piece. Cast line names the holder 2 of 2; only two casts logged. 2026-10-15 re-derivation (ffxivdb 09-24..10-15, R4 row 5.5-06): the largest familiar hit is Damselfly 691 (Uragnite 545), so the cover gate uses 691."),

        // ---- Not a Snarl rule ------------------------------------------------------------------------------------------------------
        new(48222, "Void Fire III", 2, 6, 10.0f, 0f, CrucibleHitKind.PartyWide, 66, 263, 13, 6, false,
            "Devilet Piece, battle 6 (R4 row 2.6-06): a 10 s room-wide cast the plugin answers by killing the caster (KillTheCaster). 13 casts in the one recorded run, 6 hit: the character 66 once, familiars 65-263 (Worm max 263). HitDelay not measured (Boards 1-3 stay 0); the cast is also the battle's kill-the-caster target."),
        new(48690, "Rotten Stench", 4, 3, 5.7f, -0.5f, CrucibleHitKind.PartyWide, 773, 534, 13, 8, false,
            "Corpse Flower Piece, hit id 48691: character and familiar hit in the same burst in 6 of 8 bursts."),
        new(48724, "Fivefold Fallout", 4, 5, 3.0f, 0.3f, CrucibleHitKind.Tankbuster, 620, 645, 409, 124, false,
            "Gargoyle Piece, battle 5 (R4 row 4.5). The castbar id; the damage arrives under hit id 48725 (the same helper's 20 y knockback cast, already in CrucibleKnockbacks): 409 casts with no direct 48724 damage, 48725 hit the character 124 times (max 620) and familiars 76 times (Sabotender 645, Bat 627, Diremite 580). Hit lands 0.3 s after the castbar (measured 0.23-0.26 on three cast-end pairs; BMR models the helper cast at 3.0 s, the largest plugin remaining seen was 2.7)."),
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
