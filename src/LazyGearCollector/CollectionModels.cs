using System.Collections.Generic;
using System.Linq;

namespace LazyGearCollector;

/// <summary>A single "N of item X" line in a shop trade.</summary>
public sealed record CostLine(uint ItemId, string ItemName, uint Quantity);

/// <summary>One concrete way to obtain a specific item, as published by a SpecialShop row.</summary>
public sealed class AcquisitionPath
{
    public uint ShopId;
    public string ShopName = "";
    public List<CostLine> Costs = new();

    /// <summary>Set when one of the costs is the tier directly below this one in the same chain.</summary>
    public uint UpgradeFromItemId;

    /// <summary>Set when one of the costs is equipment from a *different* family (e.g. Arcanaut's).</summary>
    public uint ExchangeFromItemId;

    /// <summary>Costs excluding the consumed predecessor piece - i.e. the materials/currency you actually spend.</summary>
    public IEnumerable<CostLine> MaterialCosts =>
        Costs.Where(c => c.ItemId != UpgradeFromItemId && c.ItemId != ExchangeFromItemId);
}

/// <summary>One tier of one piece (base, +1, +2, +3).</summary>
public sealed class TierNode
{
    public int Tier;
    public uint ItemId;
    public string Name = "";
    public List<AcquisitionPath> Paths = new();
}

/// <summary>A single equipment slot within a role, across all of its tiers.</summary>
public sealed class PieceChain
{
    public string Role = "";
    public string PieceName = "";
    public uint EquipSlot;
    public string SlotName = "";
    public List<TierNode> Tiers = new();

    public TierNode? Tier(int t) => Tiers.FirstOrDefault(x => x.Tier == t);
    public int MaxTier => Tiers.Count == 0 ? 0 : Tiers.Max(x => x.Tier);
}

/// <summary>A whole trackable collection (one content family).</summary>
public sealed class GearCollection
{
    public string Id = "";
    public string DisplayName = "";
    public string SourceNote = "";
    public List<PieceChain> Pieces = new();

    /// <summary>Currency/material items referenced by any acquisition path, in first-seen order.</summary>
    public List<uint> Currencies = new();

    public IEnumerable<string> Roles => Pieces.Select(p => p.Role).Distinct();
    public IEnumerable<PieceChain> ForRole(string role) => Pieces.Where(p => p.Role == role);
}

/// <summary>What it will take to get one piece from where it is now to the target tier.</summary>
public sealed class PiecePlan
{
    public PieceChain Piece = null!;
    public int OwnedTier = -1;              // -1 = own nothing in this chain
    public int TargetTier;
    public bool Complete => OwnedTier >= TargetTier;

    /// <summary>Aggregated remaining material/currency cost, itemId to quantity.</summary>
    public Dictionary<uint, long> Remaining = new();

    /// <summary>Free-text hints, e.g. a trade-up you already qualify for.</summary>
    public List<string> Notes = new();

    /// <summary>Set when an owned item from another family can be exchanged straight in.</summary>
    public bool HasShortcut;
}
