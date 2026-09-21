namespace LazyCrucible;

/// <summary> XBMItem.Type: 1 beast gear, 2 Crucible item, 3 beast feed. </summary>
public enum CrucibleItemType : byte
{
    None = 0,
    Gear = 1,
    Item = 2,
    Feed = 3,
}

/// <summary> Statuses an item resists or cures (and a fight can inflict). </summary>
[Flags]
public enum CrucibleStatus : byte
{
    None = 0,
    Poison = 1 << 0,
    Paralysis = 1 << 1,
    Blind = 1 << 2,
    Petrify = 1 << 3,
    Sleep = 1 << 4,
    Doom = 1 << 5,
    Stun = 1 << 6,
}

/// <summary> Drawbacks written in an item's tooltip. </summary>
[Flags]
public enum ItemHazard : byte
{
    None = 0,
    MaxHpDown = 1 << 0,
    Slow = 1 << 1,
    SelfDot = 1 << 2,
    /// <summary> A chance (or certainty) to inflict a status on the one who carries or eats it. </summary>
    SelfStatus = 1 << 3,
    MoveSpeedDown = 1 << 4,
}

/// <summary> Special tooltip effects the policies act on. </summary>
[Flags]
public enum ItemFlags : byte
{
    None = 0,
    /// <summary> Beast gear whose effects also apply to the summoned familiar. </summary>
    PetGear = 1 << 0,
    /// <summary> Lily Simular: feed effects survive a campsite rest. </summary>
    KeepsFeedAtCamp = 1 << 1,
    /// <summary> Lassi Simular: completely restores HP at campsites. </summary>
    FullHealAtCamp = 1 << 2,
}

/// <summary> What an item is for (assigned by hand from its name and tooltip in the generator). </summary>
public enum ItemRole : byte
{
    None,
    Gear,
    Feed,
    Heal,
    HealParty,
    AutoHeal,
    Cure,
    AutoCure,
    Serum,
    SerumParty,
    SwiftPoisonResist,
    Revive,
    SelfRevive,
    Reraise,
    Rewind,
    Flee,
    Mitigation,
    MaxHpBuff,
    AfterBattleHeal,
    CampPotion,
    ExtraTreasure,
    Fang,
    FangDispel,
    Absorb,
    Offense,
    Feral,
    Score,
}

/// <summary>
///     One XBMItem row. Percentages are as the tooltip states them; <see cref="DamageTaken"/>, <see cref="PhysVuln"/>
///     and <see cref="MagicVuln"/> are reductions (positive = less damage taken). <see cref="Kins"/> has bit
///     (1 &lt;&lt; kin) set for each kin the feed is "suitable for" (0 for gear and items).
/// </summary>
public readonly record struct CrucibleItem(
    int Row,
    CrucibleItemType Type,
    string Name,
    int SellPrice,
    ItemRole Role,
    int MaxHp,
    int DamageTaken,
    int PhysVuln,
    int MagicVuln,
    int Heal,
    int DamageDealt,
    int PhysDamage,
    int MagicDamage,
    CrucibleStatus Resists,
    CrucibleStatus Cures,
    ItemHazard Hazards,
    ushort Kins,
    ItemFlags Flags)
{
    public bool IsDefined => Row > 0;

    /// <summary> Whether a familiar of <paramref name="kin"/> may eat this feed (sheet "Suitable for" list). </summary>
    public bool SuitsKin(Lalalazy.Crucible.BeastmasterKinType kin) => (Kins & (1 << (int)kin)) != 0;
}

/// <summary> Pure lookups over the generated XBMItem table. </summary>
internal static partial class CrucibleItems
{
    /// <summary> The item for an XBMItem row, or an undefined default. </summary>
    public static CrucibleItem Get(int row) => row > 0 && row < All.Length ? All[row] : default;

    public static string NameOf(int row)
    {
        var item = Get(row);
        return item.IsDefined ? item.Name : $"item {row}";
    }

    public static bool IsHealing(int row) => Get(row).Role is ItemRole.Heal or ItemRole.HealParty or ItemRole.AutoHeal;
}
