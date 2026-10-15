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
    ///     the siren wave; 4.0 Wriggling Phlegm; 4.8 the treant waves; 5.4 / 5.5 / 5.6 / 5.9 Atomos wave, Barbmoles, the rooks (Summon 49269,
    ///     3 or more up in 228 recorded ticks), the Sphinx's riddle adds.
    /// </summary>
    public static readonly HashSet<(int Board, int Battle)> PackBattles =
    [
        (1, 0), (1, 4), (1, 5), (2, 2), (2, 4), (3, 1), (3, 6), (4, 0), (4, 8), (5, 4), (5, 5), (5, 6), (5, 9),
    ];

    /// <summary>
    ///     Battles whose panel has no ability need but whose damage is physical, so the guide answer is a Beastkin's Beastskin (-25% physical
    ///     taken) armed before the pull: 4.5 Gargoyle (14608: no weakness, Magic Res 5 makes Quelling Wave useless, 42 hits on the character).
    /// </summary>
    public static readonly HashSet<(int Board, int Battle)> BeastskinBattles = [(4, 5)];

    /// <summary> Hostile enemies up at which an add pack counts as on the field (logs: every pack peaked at 3+, the boss alone is 1). </summary>
    public const int PackMinEnemies = 3;

    /// <summary>
    ///     How long a familiar's Tempered Release is held for a window (seconds since the summon). The release is the summon's one
    ///     use of One with Nature, so a hold that never ends wastes it; 30 s is a chosen bound, not a measured one (the next run's
    ///     <c>sh=</c> lines say how long the windows really took to open).
    /// </summary>
    public const float WindowHoldSeconds = 30f;

    /// <summary>
    ///     Self-destruct (Golem Piece, 4.7): the 20 s arena-wide cast. Both recorded runs (2026-09-28, 10-01) saw it begin at +172 s and
    ///     +177 s into the battle with the Golem at 85% and 90%; in both the telemetry stops 16-17 s into the cast, 2.9 s and 3.4 s before
    ///     it ends, with the highest enemy at 9% and 12%. The cast is a deadline the normal rotation did not meet in 2 of 2 runs.
    /// </summary>
    public static readonly HashSet<uint> SelfDestructCasts = [48766];

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

    // ------------------------------------------------------------------ Third Board battle 2: Ymir / Sahagin (r4-third2)

    /// <summary> Sahagin Piece (14571): its Water (48626, instant, 100-155) hits the enmity holder 923 times in the logs; a Snarl keeps it on the familiar. </summary>
    public static readonly HashSet<uint> WaterCasterNameIds = [14571];

    /// <summary>
    ///     Casts the dossier answers with Soul Crush first and the cover Snarl only when that interrupt cannot go out (Dreadwash, 48485: on the panel's
    ///     interruptible list; 24 of 87 casts were interrupted in the logs).
    /// </summary>
    public static readonly HashSet<uint> InterruptFirstCasts = [48485];

    /// <summary> Paralyzing Spikes (50532, 3.0 s cast): the counter stance (5434, 40 s) lands on the Sahagin when the cast ends. </summary>
    public static readonly HashSet<uint> StanceCasts = [50532];

    /// <summary>
    ///     A stance cast with this much castbar left or less counts as the stance for the hold: a press made now resolves after the buff lands
    ///     (recorded: an Axeblade Bite 0.0 s and a combo press 1.8 s after the buff, both counted as hits into the spikes).
    /// </summary>
    public const float StanceCastLeadSeconds = 1.0f;

    /// <summary>
    ///     How far from the target an AoE press (Shield Charge) may land before it hits an enemy in a counter stance (yalms): the egg reach, the plugin's
    ///     worst case for a player AoE. 2026-09-30 and 2026-10-07: a dash at the Ymir Shell hit the stanced Sahagin for 505 and 577 (log_type 22).
    /// </summary>
    public const float StanceAoeReach = 10f;

    /// <summary>
    ///     Battles where a held Quelling Wave is pressed at a stanced enemy: Icy Veins says the wave "does not reflect the paralysis" of Paralyzing Spikes
    ///     (3.2). One source and no log of a wave on a stanced Sahagin, so it is limited to the one battle the claim is about.
    /// </summary>
    public static readonly HashSet<(int Board, int Battle)> QuellingStanceBattles = [(3, 2)];
}
