using System.Collections.Generic;

namespace Lalalazy.Crucible;

// The tactics the fight research names that cost a familiar's Tempered Release or a Parting Blow (add-pack AoE, a window held for
// a named cast, a stun at the Ymir). Pure data, shared SOURCE (GluttonyCombo + harnesses). Task
// tasks-20261002-crucible-add-pack-and-window-tactics-01 priced them from the recorded runs (2026-09-24 .. 10-01, CR| telemetry
// and ffxivdb action / status events); the numbers behind each constant are in the notebook page
// "Projects/BST Rebuild 2026-09/Crucible Research To Behavior" and in the table's log column.
internal static partial class BST_CrucibleData
{
    /// <summary>
    ///     Battles whose guide answer is "AoE the add pack": (board, battle). The pack is on the field when
    ///     <see cref="PackMinEnemies"/> hostile enemies are up (every logged pack peaked at 3 or more; the boss alone is 1).
    ///     1.0 / 1.4 / 1.5 Pas de Seul, Banemite and the Ogre's wisps; 2.2 / 2.4 Ahriman and zombies; 3.1 / 3.6 Bone Bishops and
    ///     the siren wave; 4.0 Wriggling Phlegm; 4.8 the treant waves; 5.4 / 5.5 / 5.9 Atomos wave, Barbmoles, the Sphinx's riddle adds.
    /// </summary>
    public static readonly HashSet<(int Board, int Battle)> PackBattles =
    [
        (1, 0), (1, 4), (1, 5), (2, 2), (2, 4), (3, 1), (3, 6), (4, 0), (4, 8), (5, 4), (5, 5), (5, 9),
    ];

    /// <summary> Hostile enemies up at which an add pack counts as on the field (logs: every pack peaked at 3+, the boss alone is 1). </summary>
    public const int PackMinEnemies = 3;

    /// <summary>
    ///     How long a familiar's Tempered Release is held for a window (seconds since the summon). The release is the summon's one
    ///     use of One with Nature, so a hold that never ends wastes it; 30 s is a chosen bound, not a measured one (the next run's
    ///     <c>sh=</c> lines say how long the windows really took to open).
    /// </summary>
    public const float WindowHoldSeconds = 30f;

    /// <summary>
    ///     Self-destruct casts the rotation bursts (the release goes first, then a Parting Blow recall while the cast is young):
    ///     the Golem Piece (4.7) heart's 20 s arena-wide cast - both recorded runs (2026-09-28, 10-01) saw it begin at +172 s and
    ///     +177 s with the Golem at 85% and 90%, and the telemetry stopped 3 s before it ended with the Golem at 9% / 12% - and
    ///     the Bomb Piece (5.3) 49211, an 8 s room-wide blast a familiar cannot step out of (2026-09-30 01:21:59Z: the bomb casts
    ///     with 7.7 s left and the Damselfly eats it at 79%, dec=gcdchain throughout). 48792 (the 4.9 grenade pack) is in the sheet
    ///     but never seen cast; it is covered anyway.
    /// </summary>
    public static readonly HashSet<uint> SelfDestructCasts = [48766, 49211, 48792];

    /// <summary> Seconds of a Self-destruct cast that must remain for a Parting Blow, a resummon and the new familiar's release to fit (a chosen bound, not a measured one). </summary>
    public const float BurstPartingMinCastLeft = 7f;

    /// <summary>
    ///     Enemies whose damage-immune shell breaks into a window the guide wants a stun or bind in: the Ymir Piece (3.2). The shell is
    ///     Vulnerability Down (status 2198, an <see cref="InvulnerableStatuses"/> member) on the Ymir; 39 applications in the recorded
    ///     runs, each lasting 8-60 s.
    /// </summary>
    public static readonly HashSet<uint> ShellTargets = [14569];

    /// <summary>
    ///     Familiars whose release stuns or binds: every beast with the crowd-control trait, plus the slime, whose release applied Bind+ (5429)
    ///     to the Ymir 6 times in the recorded runs although its trait row does not say so.
    /// </summary>
    public static bool ReleaseBinds(BeastmasterBeast beast) =>
        (beast.Release & BeastmasterReleaseTraits.CrowdControl) != 0 || beast.Row == 17;

    /// <summary> Whether the guide's window for <paramref name="battle"/> is a pack, a shell break or neither (hold is only for those). </summary>
    public static bool IsShellBattle(int board, int battle) => board == 3 && battle == 2;
}
