using System.Collections.Generic;
using System.Linq;

namespace Lalalazy.Crucible;

// Crucible of the Unbroken (Beastmaster-only duty, 7.56). Pure data, no Dalamud types: shared SOURCE
// (GluttonyCombo + LazyCrucible + both harnesses). The per-enemy panel table (BST_CrucibleData.Generated.cs) comes
// straight from the game's own XBM sheets; the sets in this file are hand-authored from the Crucible
// guides (Icy Veins, consolegameswiki, nettoge, asellog) and a public reaction set, with status ids
// confirmed against the 7.56 Status sheet.

/// <summary> The weakness shown on an enemy's Crucible panel (XBMElement row). </summary>
public enum CrucibleWeakness : byte
{
    None = 0,
    Fire = 1,
    Wind = 2,
    Earth = 3,
    Lightning = 4,
    Ice = 5,
    Water = 6,
    Blunt = 7,
    Piercing = 8,
    Slashing = 9,
}

/// <summary> What an enemy's panel casts call for. </summary>
[System.Flags]
public enum CrucibleNeeds : byte
{
    None = 0,
    /// <summary> A cast can be interrupted (Soul Crush). </summary>
    Interrupt = 1 << 0,
    /// <summary> A cast grants the enemy a buff that can be dispelled (Quelling Wave, Bloodcurdling Caw). </summary>
    Dispel = 1 << 1,
    /// <summary> A cast inflicts a debuff that can be cleansed (Scouring Ash). </summary>
    Cleanse = 1 << 2,
}

/// <summary> How Snarl / Challenge automation behaves. </summary>
public enum CrucibleAggroMode : byte
{
    Off = 0,
    /// <summary> Decide, but only record the choice in telemetry. </summary>
    Shadow = 1,
    On = 2,
}

/// <summary> Board space type of a battle (XBMContentStageEvent). </summary>
public enum CrucibleRole : byte
{
    Enemy = 0,
    EliteEnemy = 1,
    Boss = 2,
}

/// <summary> One battle (XBMContentBattle row = board, subrow = battle; battle 0 is the boss). </summary>
public readonly record struct CrucibleBattleInfo(byte Board, byte Battle, CrucibleRole Role, bool RandomOnly);

/// <summary>
///     A familiar's Crucible profile (XBMPet). <see cref="Stats"/> is STR, INT, PHY R, MAG R, CON at beast ranks
///     5, 10, 15, 20, 25, flattened rank-first (25 values).
/// </summary>
public readonly record struct CrucibleBeastProfile(byte Row, CrucibleWeakness AutoElement, bool AutoMagic, ushort Inflicts, ushort[] Stats)
{
    public const int Str = 0, Int = 1, PhysRes = 2, MagRes = 3, Con = 4;

    /// <summary> A stat at the rank sync of board 1-5 (ranks 5/10/15/20/25). </summary>
    public int StatAtBoard(int board, int stat) =>
        Stats is null || board is < 1 or > 5 ? 0 : Stats[(board - 1) * 5 + stat];
}

/// <summary> One Crucible board (XBMContent row). </summary>
public readonly record struct CrucibleBoard(
    byte Board,
    uint Territory,
    uint ContentFinderCondition,
    byte Level,
    ushort ItemLevel,
    byte BeastRank,
    byte Roster,
    byte Battles,
    string Name);

/// <summary>
///     One enemy from a Crucible panel (XBMBattleDetail). <see cref="Vulnerable"/> bit i set = the enemy is
///     vulnerable to: 0 Slow, 1 Petrify, 2 Paralysis, 3 Interrupt, 4 Blind, 5 Poison, 6 Stun, 7 Sleep,
///     8 Bind, 9 Heavy, 10 Doom. <see cref="Stars"/> packs the panel's 1-5 star ratings, 3 bits each:
///     STR, INT, PHY R, MAG R, CON (high to low bits).
/// </summary>
public readonly record struct CrucibleEnemy(
    uint NameId,
    byte Board,
    byte Battle,
    byte Sub,
    CrucibleWeakness Weakness,
    ushort Vulnerable,
    CrucibleNeeds Needs,
    ushort Stars,
    string Name)
{
    public int StarStr => (Stars >> 12) & 7;
    public int StarInt => (Stars >> 9) & 7;
    public int StarPhysRes => (Stars >> 6) & 7;
    public int StarMagRes => (Stars >> 3) & 7;
    public int StarCon => Stars & 7;
}

internal static partial class BST_CrucibleData
{
    /// <summary> TerritoryType of the First Board; boards 1-5 are 1339-1343 (the only territories with intended use 62). </summary>
    public const uint FirstTerritory = 1339;

    /// <summary> Board 1-5 for a TerritoryType, 0 outside the Crucible. </summary>
    public static int BoardOfTerritory(uint territory) =>
        territory is >= FirstTerritory and < FirstTerritory + 5 ? (int)(territory - FirstTerritory + 1) : 0;

    // ------------------------------------------------------------------ statuses

    /// <summary>
    ///     Counter / reflect stances: attacking the enemy while it has one hurts the attacker.
    ///     5434 Paralyzing Spikes (Third Board Sahagin), 2528 Ice Spikes (First Master's snoll),
    ///     5465 Blaze Spikes (Second Master's drake), 5145 Needles Out (Second Master's spinemoles).
    /// </summary>
    public static readonly HashSet<uint> StanceStatuses = [5434, 2528, 5465, 5145];

    /// <summary> One research dispel row: the fight, the buff's status id, and what the id rests on. </summary>
    internal readonly record struct DispelRow(int Board, int Battle, uint StatusId, string Buff, string Basis);

    /// <summary>
    ///     Every dispel counter in the fight research (CrucibleGuide.json, 13 rows) with the status id the game puts on the enemy.
    ///     The rotation dispels a buff whose id is here (and on the panel's list), on whichever enemy carries it. Basis:
    ///     "panel" = the enemy panel flags the cast's buff dispellable; "live" = the id was recorded on the enemy in a real
    ///     run (ffxivdb status_events) and the guides say to dispel it, but the panel does not flag it; "guide" = the guides
    ///     say so and the id comes from the panel's cast table, never seen on an enemy in a run. A "live" / "guide" id has never been dispelled in a run, so
    ///     the rotation gives up on it after <see cref="BST_CrucibleLogic.DispelMaxTries"/> dispels that left it standing.
    ///     The test in BSTRotationHarness asserts one row per guide counter, so a new dispel need cannot ship without an id.
    /// </summary>
    public static readonly DispelRow[] DispelRows =
    [
        new(1, 0, 1225, "Damage Up on the boss from Fanaticism", "panel"),
        new(1, 1, 2074, "Physical Damage Up from Ossify (Bone Knight)", "panel"),
        new(1, 3, 1225, "Clear Mind (Damage Up on the Piscodemon)", "panel"),
        new(2, 0, 5423, "Popoto Skin from Kinborrow (Loosefrox Inkyjots)", "panel"),
        new(2, 5, 1225, "Damage Up on the Elder from Rallying Cheer", "panel"),
        new(3, 5, 1572, "Might on the Golem", "live"),
        new(4, 1, 5020, "Ultimate Focus (Magic Damage Up, Strix Piece)", "panel"),
        new(4, 8, 390, "Growing from Grab and Grow (Sapling Piece)", "live"),
        new(4, 9, 2528, "Ice Spikes (Snoll Piece)", "panel"),
        new(5, 5, 5465, "Blaze Spikes (Drake Piece)", "panel"),
        new(5, 5, 989, "Regen (Rehabilitation) on the Abaddon from eaten Morphos", "live"),
        new(5, 7, 3129, "Impassion Damage Up (Medusa Piece)", "guide"),
        new(5, 11, 1225, "Spirit of Pompetition (Kinged Swordsmog)", "panel"),
    ];

    /// <summary>
    ///     Enemy buffs worth a dispel: the panel's dispellable buffs plus every status id a research dispel row names
    ///     (<see cref="DispelRows"/>). 989 Regen was missing until 2026-10-03: the Abaddon Piece fight needed the dispel for
    ///     428 of 430 ticks with Quelling Wave held and the target flag never rose.
    /// </summary>
    /// <remarks> Lazy: static initializers in different files of a partial class run in no guaranteed order. </remarks>
    public static HashSet<uint> DispellableBuffs => _dispellableBuffs ??= [.. PanelDispellableBuffs, .. DispelRows.Select(r => r.StatusId)];

    private static HashSet<uint>? _dispellableBuffs;

    /// <summary>
    ///     The enemy panel itself flags this buff dispellable. Those are the ones the game is known to honour; every other
    ///     dispellable id (the guides' "live" / "guide" rows) is unproven until a dispel is seen to take it off.
    /// </summary>
    public static bool PanelFlagsDispellable(uint statusId) => PanelDispellableBuffs.Contains(statusId);

    /// <summary>
    ///     Crucible damage-immune states on enemies, on top of the plugin's general invulnerability check (BossmodReborn
    ///     Crucible modules + MagitekRoutine): 4175 Burning Ward (First Board ogre), 4410 Invincibility (First Master's
    ///     progenitrix), 2198 Vulnerability Down (Third Board ymir, Second Master's sphinx), 2413 Covered (an enemy the
    ///     guardia covers: hit the guardia first).
    /// </summary>
    public static readonly HashSet<uint> InvulnerableStatuses = [4175, 4410, 2198, 2413, 616];

    /// <summary>
    ///     Whether a status makes a Crucible enemy immune to damage. The native actor
    ///     invincibility flag does not cover these encounter-specific statuses.
    /// </summary>
    public static bool IsDamageImmunityStatus(uint statusId) =>
        InvulnerableStatuses.Contains(statusId);

    /// <summary> Directional Parry (Forward Guard). 680 is the named status everywhere. </summary>
    public static readonly HashSet<uint> ParryStatuses = [680];

    /// <summary>
    ///     2552 is an unnamed permanent status the First Board bone knight carries during Forward Guard, but Guttler and the
    ///     ice dragon carry it too: count it as a parry only on the bone knight.
    /// </summary>
    public const uint BoneKnightParryStatus = 2552;

    public static readonly HashSet<uint> BoneKnightNameIds = [14531, 14566];

    /// <summary>
    ///     Forward Guard casts (Bone Knight): directional parry that blocks hits from the front.
    ///     Parting Blow recalls the familiar before the guard lands so pets are not locked or killed on it.
    /// </summary>
    public static readonly HashSet<uint> ForwardGuardCasts = [46864];

    /// <summary>
    ///     Curtains for Rank 5 (King Ahriman): a 6.0 s cast that KOs the familiar whatever its HP. The game starts it as TWO simultaneous
    ///     casts, 49428 and 49429, and the target's castbar reads 49428 (live 2026-10-03: 47.6 s and 164.3 s, both familiars died to the
    ///     effect because the rule only knew 49429). Parting Blow recalls the familiar before either resolves.
    /// </summary>
    public static readonly HashSet<uint> CurtainsCasts = [49428, 49429];

    /// <summary> Physical Vulnerability Up from the mantis's Eerie Soundwave: Final Sting lands inside this window. </summary>
    public const uint PhysicalVulnerabilityUp = 5180;

    /// <summary> Familiars whose Tempered Release sets up burst (vulnerability up / resistance down): summoned before the others. </summary>
    public static readonly HashSet<int> SetUpBeasts = [16, 20, 41, 9, 23, 33, 34, 38, 46];

    /// <summary> Bat: Ultrasonics removes a detrimental effect from nearby party members. </summary>
    public const int BatRow = 19;

    // ------------------------------------------------------------------ objects

    /// <summary>
    ///     Enemies that must not be attacked, with how far from the target an AoE may land before it hits them
    ///     (yalms; float.MaxValue = anywhere in the arena).
    ///     Zu eggs 14575/14576 hatch when hit, AoE included (Third Board). Morpho pieces 14656 set off
    ///     room-wide confusion when they die (Second Master's Board).
    /// </summary>
    public static readonly Dictionary<uint, float> DoNotAttack = new()
    {
        [14575] = 10f,
        [14576] = 10f,
        [14656] = float.MaxValue,
    };

    /// <summary>
    ///     Pairs that must die together (killing one first enrages the other): elder / younger tablitaur (Second
    ///     Board elite), Loosefrox Inkyjots / Chewchum Popoto (Second Board boss).
    /// </summary>
    public static readonly (uint A, uint B)[] Pairs = [(14555, 14556), (14561, 14562)];

    /// <summary>
    ///     Hard hits on the character (or the highest-enmity target) worth dodging with Snarl -> Parting Blow: castbar
    ///     action ids bound from BossmodReborn's Crucible modules and the guides (two sources, or one real log), 2026-09-17.
    ///     Master's Board entries are the casts the recorded runs measured as single-target hits that follow the enmity holder
    ///     (<see cref="HeavyCasts"/>, kind Tankbuster: victim == holder in every logged cast); the two Second Master's ids in
    ///     <see cref="GuideBoundTankbusters"/> were never seen cast and stay guide-derived.
    /// </summary>
    public static readonly HashSet<uint> Tankbusters =
    [
        46935, 46934, 46872, 46906, 46920,        // First Board: Cold Caress, Blood Sword, Skullsplinter, Deadly Thrust, Straight Punch
        48138, 48204, 48247,                      // Second Board: Deadly Hold, 100-tonze Slash, Void Paralyze
        48620, 48471, 48489, 50465, 48563,        // Third Board: Thunderbolt, Crushing Blade, Caustic Vomit, Flying Frenzy, Song of Torment
        48809, 48822, 48689, 48730,               // First Master's (measured): Toxic Vomit, Salivous Snap, Final Sting, Grim Fate (five-hit string)
        48717, 50649, 48669,                      // First Master's (measured): Sweeping Evisceration, Obliterate, On the Properties of Darkness
        49188, 49254,                             // Second Master's (measured): Erratic Blaster, Mangling Fang
        49470, 49205,                             // Second Master's (guide only, never seen cast): Thunderbolt, Void Thunder III
    ];

    /// <summary>
    ///     Seconds from the moment the plugin's castbar remaining reaches 0 to the hit landing (negative = the hit lands first). Measured
    ///     for the Master's Board casts in <see cref="HeavyCasts"/>; 0 for every other id (no measurement: Boards 1-3 and the guide-only
    ///     Master's ids).
    /// </summary>
    public static float TankbusterHitDelay(uint castId) =>
        HeavyCast(castId) is { Kind: CrucibleHitKind.Tankbuster } row ? row.HitDelay : 0f;

    /// <summary>
    ///     Enemies auto-targeting takes first whenever they are up: the adds the guides kill on sight (succubi, wisps before
    ///     they reach the centre, ahriman after the gaze, zombies, a woken Thanatos, the guardia that covers its allies) and
    ///     the bone bishop before the bone knight, plus every panel add priority across B1-B3, M1 and M2. Identities are
    ///     pinned to the 7.56 sheet decode (wiki Crucible Game Data); the BSTRotationHarness asserts this exact set.
    /// </summary>
    public static readonly HashSet<uint> PriorityAdds =
    [
        // Board 1: bone bishop, miteling, wisp, great wisp, succubus mage + knight
        14532, 14537, 14539, 14748, 14542, 14543,
        // Board 2: ahriman, zombie, evil weapon, devilet
        14548, 14553, 14558, 14559,
        // Board 3: bone bishop, cockerel, pullet, golem, shambling, crawling, flowertender, guardia, Thanatos
        14567, 14573, 14574, 14581, 14585, 14586, 14589, 14591, 14595,
        // M1: queen hawk, ice sprite, administrator, biloko, sapling, diremite, grenade, bomb, toxic mass,
        //     Strix Plume (the add that casts the 12 s Aero III knockback; no cast of Aero III was ever flagged interruptible in
        //     five logged casts, so killing the add is the only counter - live 2026-10-01 it stood at 100% beside the boss while
        //     the cast went off in three runs)
        14605, 14607, 14610, 14622, 14620, 14621, 14624, 14625, 14629, 14598,
        // M2: lightning sprite, deepeye, bomb, atomos wave adds (gremlins, puddings, bavarois, flan, vodoriga, dahak),
        //     barbmole (drake-fight spinemole), spinner-rook, lamia, cyclops x2 (medusa + gigantis), congealed gels,
        //     moogle officers (kinged casters, kinged swordmog, melomog, mogmugger), hapalit, dirty eye, Thanatus statues
        14632, 14639, 14640, 14641, 14643, 14644, 14645, 14646, 14647, 14648, 14649, 14653,
        14659, 14661, 14662, 14671, 14672, 14673, 14676, 14677, 14680, 14681, 14683, 14691, 14692, 14699,
        // M2 King Ahriman: the Final Hourglass the Roulette spawns (live 2026-10-03: with Hapalit and Dirty Eye up it was never attacked
        // and its Death killed the character through Doom; broken within ~12 s in the two roulettes with no adds up)
        14689,
    ];

    /// <summary>
    ///     Bosses whose auto-attacks are a frontal cone that also hits the familiar (guides, two independent sources:
    ///     "the boss of board 3 has this too", "siren autos are a frontal cleave that also hits the pet",
    ///     "Guttler's autos are a frontal cleave that hits the pet", Lauda's 120° 8y cone drawn by the public reaction set).
    ///     Rule (Crucible-wide, C2): the player holds enmity with Challenge so the enemy faces away from the familiar;
    ///     Snarl only to cover a known tankbuster cast, then Challenge back when it resolves.
    /// </summary>
    public static readonly HashSet<uint> CleaveAutoBosses = [14541, 14583, 14592, 14693];

    /// <summary>
    ///     Documented kill orders for priority adds that spawn together: each list is one fight's order, each inner array a
    ///     tier of enemies killed together, most dangerous tier first. Of the members of a list that are up, only the first
    ///     tier present stays targetable until it is dead (see <c>BST_CrucibleLogic.AllowedTargets</c>), so a wave is cleared
    ///     in the guides' order instead of nearest-first (live 2026-09-26: the siren wave's shamblings were attacked ~5 s
    ///     before the crawling piece whose touch breaks Unbeastable). Orders, from the fight guide's kill orders (the
    ///     research-to-behaviour audit of 2026-10-02 checked all 26): siren wave, crawling piece before the shamblings; treant
    ///     wave "Biloko &gt; Sapling &gt; Diremite &gt; Slug / Treant" (the biloko's Natural Nurture heals the wave, the sapling's
    ///     Grab and Grow buffs an ally); the woken Thanatos before its guardia; the progenitrix's grenade before its bombs; the
    ///     boogyman's self-destructing bomb, the deepeye, then the light sprite; Pas de Seul's Succubus Mage before the Knights;
    ///     the ogre's small wisps before the great wisp; the pullet (Caustic Vomit) before the cockerel; the cactuar pack's
    ///     flowertender before the guardia; the Atomos summons bavarois, then pudding / flan / dahak, then gremlins and
    ///     vodoriga; the moogle finale's Mogmugger, then the Melomogs, then the kinged healer and caster, then the Kinged
    ///     Swordsmog. A priority add absent from every list is unranked: a wave of only unranked adds keeps the whole tier
    ///     targetable. An order only constrains the members of its own list that are up.
    /// </summary>
    public static readonly uint[][][] PriorityAddOrder =
    [
        [[14586], [14585]],                                    // siren wave: crawling first
        [[14622], [14620], [14621]],                           // treant wave: biloko, sapling, diremite
        [[14595], [14591]],                                    // Thanatos before the guardia that covers it
        [[14624], [14625]],                                    // progenitrix wave: grenade first
        [[14640], [14639], [14641]],                           // boogyman wave: self-destructing bomb, deepeye, light sprite
        [[14683], [14681], [14676, 14677], [14680]],           // moogle finale: Mogmugger, Melomogs, kinged healer and caster, Kinged Swordsmog
        [[14542], [14543]],                                    // Pas de Seul: Succubus Mage before the Knights
        [[14539], [14748]],                                    // ogre: small wisps before the great wisp
        [[14574], [14573]],                                    // zu pack: pullet (Caustic Vomit) before the cockerel
        [[14589], [14591]],                                    // cactuar pack: flowertender before the guardia
        [[14646], [14645, 14647, 14649], [14644, 14643, 14648]], // Atomos summons: bavarois; pudding, flan, dahak; gremlins, vodoriga
        [[14689], [14691, 14692]],                             // King Ahriman: the Final Hourglass before Hapalit and Dirty Eye
    ];

    /// <summary>
    ///     Casts whose caster is killed while it casts, because the guide's answer to them is "kill it before the cast ends": the Bone Bishop's
    ///     Soul Douse (50693: "kill the casting Bishop within 5 s") and the Deepeye's Oogle (49214: "kill the Deepeye during the cast").
    ///     A candidate casting one of these is the target before any other (an armed interrupt still comes first).
    /// </summary>
    public static readonly HashSet<uint> KillTheCaster = [50693, 49214];

    private static Dictionary<uint, (int Wave, int Tier)[]>? _priorityAddRanks;

    private static Dictionary<uint, (int Wave, int Tier)[]> PriorityAddMap()
    {
        if (_priorityAddRanks == null)
        {
            var map = new Dictionary<uint, List<(int, int)>>();
            for (var w = 0; w < PriorityAddOrder.Length; w++)
                for (var t = 0; t < PriorityAddOrder[w].Length; t++)
                    foreach (var id in PriorityAddOrder[w][t])
                    {
                        if (!map.TryGetValue(id, out var list))
                            map[id] = list = [];
                        list.Add((w, t));
                    }

            _priorityAddRanks = map.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        }

        return _priorityAddRanks;
    }

    /// <summary> Tier of a priority add inside the documented order <paramref name="wave"/> (0 = kill first); <see cref="int.MaxValue"/> when it is not a member of that order. </summary>
    public static int PriorityAddTier(uint nameId, int wave)
    {
        if (PriorityAddMap().TryGetValue(nameId, out var memberships))
            foreach (var (w, t) in memberships)
                if (w == wave)
                    return t;
        return int.MaxValue;
    }

    /// <summary> Tier of a priority add in its (first) documented order; <see cref="int.MaxValue"/> when unranked. </summary>
    public static int PriorityAddRank(uint nameId) =>
        PriorityAddMap().TryGetValue(nameId, out var r) ? r[0].Tier : int.MaxValue;

    /// <summary> The first documented order a priority add belongs to, or -1 when unranked. </summary>
    public static int PriorityAddWave(uint nameId) =>
        PriorityAddMap().TryGetValue(nameId, out var r) ? r[0].Wave : -1;


    // ------------------------------------------------------------------ lookups

    /// <summary>
    ///     XBMContentBattle.BattleDetail → (board, battle). Sheet-derived (7.56); battle 0 is the boss.
    ///     AgentXBMStageDetailList EntryType-3 rows carry this id as BattleDetailId.
    /// </summary>
    private static readonly Dictionary<uint, (byte Board, byte Battle)> BattleByDetailId = new()
    {
        [1] = (1, 1),
        [2] = (1, 2),
        [3] = (1, 3),
        [4] = (1, 4),
        [5] = (1, 5),
        [6] = (1, 0),
        [7] = (2, 1),
        [8] = (2, 2),
        [9] = (2, 3),
        [10] = (2, 4),
        [11] = (2, 5),
        [12] = (2, 6),
        [13] = (2, 0),
        [14] = (3, 1),
        [15] = (3, 2),
        [16] = (3, 3),
        [17] = (3, 4),
        [18] = (3, 5),
        [19] = (3, 6),
        [20] = (3, 7),
        [21] = (3, 0),
        [22] = (4, 1),
        [23] = (4, 2),
        [24] = (4, 3),
        [25] = (4, 4),
        [26] = (4, 5),
        [27] = (4, 6),
        [28] = (4, 7),
        [29] = (4, 8),
        [30] = (4, 9),
        [31] = (4, 0),
        [32] = (5, 1),
        [33] = (5, 2),
        [34] = (5, 3),
        [35] = (5, 4),
        [36] = (5, 5),
        [37] = (5, 6),
        [38] = (5, 7),
        [39] = (5, 8),
        [40] = (5, 9),
        [41] = (5, 10),
        [42] = (5, 11),
        [43] = (5, 12),
        [44] = (5, 13),
        [45] = (5, 0),
    };

    /// <summary> Resolve an XBMBattleDetail row id to (board, battle). False when unknown. </summary>
    public static bool TryBattleOfDetail(uint battleDetailId, out int board, out int battle)
    {
        if (BattleByDetailId.TryGetValue(battleDetailId, out var pair))
        {
            board = pair.Board;
            battle = pair.Battle;
            return true;
        }
        board = 0;
        battle = -1;
        return false;
    }

    private static Dictionary<uint, CrucibleEnemy>? _byNameId;

    /// <summary> The panel enemy for a BNpcName id, or null. Match by id, never by name: six names are reused. </summary>
    public static CrucibleEnemy? Enemy(uint nameId)
    {
        if (_byNameId is null)
        {
            var map = new Dictionary<uint, CrucibleEnemy>(Enemies.Length);
            foreach (var e in Enemies)
                map[e.NameId] = e;
            _byNameId = map;
        }

        return _byNameId.TryGetValue(nameId, out var enemy) ? enemy : null;
    }

    /// <summary> Union of the panel needs of every enemy in one battle. </summary>
    public static CrucibleNeeds BattleNeeds(int board, int battle)
    {
        var needs = CrucibleNeeds.None;
        foreach (var e in Enemies)
            if (e.Board == board && e.Battle == battle)
                needs |= e.Needs;
        return needs;
    }

    /// <summary> The battle's first panel enemy name (subrow 0; for battle 0 that is the boss). </summary>
    public static string BattleLabel(int board, int battle)
    {
        foreach (var e in Enemies)
            if (e.Board == board && e.Battle == battle && e.Sub == 0)
                return e.Name;
        return "?";
    }
}
