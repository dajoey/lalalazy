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
///     8 Bind, 9 Heavy, 10 Doom.
/// </summary>
public readonly record struct CrucibleEnemy(
    uint NameId,
    byte Board,
    byte Battle,
    byte Sub,
    CrucibleWeakness Weakness,
    ushort Vulnerable,
    CrucibleNeeds Needs,
    string Name);

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

    /// <summary> Directional Parry on the First Board bone knight (Forward Guard): 2552, plus the generic 680. </summary>
    public static readonly HashSet<uint> ParryStatuses = [2552, 680];

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

    /// <summary> Single-target hits the familiar should take (Snarl) when it is healthy. </summary>
    public static HashSet<uint> SnarlHits => _snarlHits ??= [.. PanelSingleTargetHits];

    private static HashSet<uint>? _snarlHits;

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
