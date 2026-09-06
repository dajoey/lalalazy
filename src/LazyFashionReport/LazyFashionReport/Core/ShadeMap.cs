namespace LazyFashionReport.Core;

/// <summary>
/// Dye shade families (9), resolved from the dye ITEM's ICON id — NOT the Stain.Shade column
/// (verified live 2026-09-06: Ash Grey carries Shade=2 like Snow White, and Metallic Silver
/// carries Shade=10 like Jet Black, so that column is a sub-ordering, not the scoring family).
///
/// The stain-id -> family resolution is DATA-DRIVEN at runtime: the adapter walks the live
/// Stain sheet (each row links its dye Items), reads each item's Icon, and maps it through
/// the verified table below. Nothing here is name-guessed.
///
/// Icon map (verified, from the fashion-report-facts reference):
/// 22811/22820/22817 = White (incl. Metallic Silver), 22808 = Grey, 22807/22816 = Black,
/// 22805/22814 = Red, 22809/22818 = Brown, 22806/22815 = Yellow, 22810/22819 = Green,
/// 22804/22813 = Blue, 22812/22821 = Purple.
/// </summary>
public static class ShadeMap
{
    public const string White = "white";
    public const string Grey = "grey";
    public const string Black = "black";
    public const string Red = "red";
    public const string Brown = "brown";
    public const string Yellow = "yellow";
    public const string Green = "green";
    public const string Blue = "blue";
    public const string Purple = "purple";

    /// <summary>All nine family names, lowercase.</summary>
    public static readonly IReadOnlyList<string> Families =
        new[] { White, Grey, Black, Red, Brown, Yellow, Green, Blue, Purple };

    /// <summary>Dye item icon id -> shade family. Any icon not listed here is not a scored dye.</summary>
    public static readonly IReadOnlyDictionary<uint, string> ByIcon = new Dictionary<uint, string>
    {
        [22811] = White, [22820] = White, [22817] = White,
        [22808] = Grey,
        [22807] = Black, [22816] = Black,
        [22805] = Red, [22814] = Red,
        [22809] = Brown, [22818] = Brown,
        [22806] = Yellow, [22815] = Yellow,
        [22810] = Green, [22819] = Green,
        [22804] = Blue, [22813] = Blue,
        [22812] = Purple, [22821] = Purple,
    };

    /// <summary>
    /// Build the stain-id -> family view the scorer uses. The adapter supplies
    /// stainId -> dye item icon id (from the live Stain/Item sheets); anything unresolvable
    /// is simply absent, which scores as "no shade bonus" rather than guessing.
    /// </summary>
    public static IReadOnlyDictionary<uint, string> BuildStainFamilies(
        IReadOnlyDictionary<uint, uint> stainToDyeItemIcon)
    {
        var result = new Dictionary<uint, string>();
        foreach (var (stainId, icon) in stainToDyeItemIcon)
        {
            if (ByIcon.TryGetValue(icon, out var family))
                result[stainId] = family;
        }
        return result;
    }

    /// <summary>
    /// Evaluate the dye on one equipped item against the week's preferred dye for that slot.
    /// Exact stain id match wins (+2); otherwise same family (+1); otherwise nothing.
    /// </summary>
    public static DyeState Evaluate(
        uint equippedStain0, uint equippedStain1,
        uint? preferredStain,
        IReadOnlyDictionary<uint, string> stainFamilies)
    {
        var hasDye = equippedStain0 != 0 || equippedStain1 != 0;
        if (!hasDye || preferredStain is null || preferredStain == 0)
            return new DyeState { SlotHasDye = hasDye, IsExact = false, IsSameShade = false };

        var isExact = equippedStain0 == preferredStain || equippedStain1 == preferredStain;
        if (isExact)
            return new DyeState { SlotHasDye = true, IsExact = true, IsSameShade = true };

        bool SameAs(uint stain)
        {
            if (!stainFamilies.TryGetValue(stain, out var fam)) return false;
            return stainFamilies.TryGetValue(preferredStain.Value, out var pref) && fam == pref;
        }

        var sameShade = SameAs(equippedStain0) || (equippedStain1 != 0 && SameAs(equippedStain1));
        return new DyeState { SlotHasDye = true, IsExact = false, IsSameShade = sameShade };
    }
}
