using System.Collections.Generic;

namespace GluttonyCombo.Combos.PvE;

// Crucible of the Unbroken (Beastmaster-only duty, 7.56). Pure data, no Dalamud types: compiled into
// tests/GluttonyCombo.BSTRotationHarness. The per-enemy panel table (BST_CrucibleData.Generated.cs) comes
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

    /// <summary>
    ///     Enemy buffs worth a dispel: the panel's dispellable buffs plus 1572 Might (Third Board golem, which
    ///     two guides dispel with Quelling Wave / Bloodcurdling Caw although its sheet flag is unset).
    /// </summary>
    /// <remarks> Lazy: static initializers in different files of a partial class run in no guaranteed order. </remarks>
    public static HashSet<uint> DispellableBuffs => _dispellableBuffs ??= [.. PanelDispellableBuffs, 1572];

    private static HashSet<uint>? _dispellableBuffs;

    /// <summary>
    ///     Crucible damage-immune states on enemies, on top of the plugin's general invulnerability check (BossmodReborn
    ///     Crucible modules + MagitekRoutine): 4175 Burning Ward (First Board ogre), 4410 Invincibility (First Master's
    ///     progenitrix), 2198 Vulnerability Down (Third Board ymir, Second Master's sphinx), 2413 Covered (an enemy the
    ///     guardia covers: hit the guardia first).
    /// </summary>
    public static readonly HashSet<uint> InvulnerableStatuses = [4175, 4410, 2198, 2413, 616];

    /// <summary> Directional Parry (Forward Guard). 680 is the named status everywhere. </summary>
    public static readonly HashSet<uint> ParryStatuses = [680];

    /// <summary>
    ///     2552 is an unnamed permanent status the First Board bone knight carries during Forward Guard, but Guttler and the
    ///     ice dragon carry it too: count it as a parry only on the bone knight.
    /// </summary>
    public const uint BoneKnightParryStatus = 2552;

    public static readonly HashSet<uint> BoneKnightNameIds = [14531, 14566];

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
    /// </summary>
    public static readonly HashSet<uint> Tankbusters =
    [
        46935, 46934, 46872, 46906, 46920,        // First Board: Cold Caress, Blood Sword, Skullsplinter, Deadly Thrust, Straight Punch
        48138, 48204, 48247,                      // Second Board: Deadly Hold, 100-tonze Slash, Void Paralyze
        48620, 48471, 48489, 50465, 48563,        // Third Board: Thunderbolt, Crushing Blade, Caustic Vomit, Flying Frenzy, Song of Torment
        48809, 48689, 48730,                      // First Master's: Toxic Vomit, Final Sting, Grim Fate
        49470, 49188, 49205, 49254,               // Second Master's: Thunderbolt, Erratic Blaster, Void Thunder III, Mangling Fang
    ];

    /// <summary> Seconds from the end of the castbar to the hit landing, where the hit is a later helper action. </summary>
    public static float TankbusterHitDelay(uint castId) => castId switch
    {
        49188 => 1.0f, // Erratic Blaster: 6.0 s castbar, hit 49189 at 7.0 s
        48809 => 1.5f, // Toxic Vomit: 3.5 s castbar, hit 1.5 s later
        _ => 0f,
    };

    /// <summary>
    ///     Enemies auto-targeting takes first whenever they are up: the adds the guides kill on sight (succubi, wisps before
    ///     they reach the centre, ahriman after the gaze, zombies, a woken Thanatos, the guardia that covers its allies) and
    ///     the bone bishop before the bone knight.
    /// </summary>
    public static readonly HashSet<uint> PriorityAdds = [14542, 14543, 14539, 14748, 14532, 14548, 14553, 14595, 14699, 14591];


    // ------------------------------------------------------------------ lookups

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
