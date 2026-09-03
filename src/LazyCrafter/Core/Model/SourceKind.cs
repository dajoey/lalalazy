namespace LazyCrafter.Core.Model;

/// <summary>Where a missing ingredient can come from (Scope §3.2). Order is not the tier.</summary>
public enum SourceKind
{
    OnHand,
    SubCraft,
    GilVendor,
    SpecialShop,
    RegularNode,
    TimedNode,
    Fish,
    Venture,
    Market,
    Drop,
    Unknown,
}
