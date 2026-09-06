namespace LazyFashionReport.Core;

/// <summary>Slot order used everywhere in this plugin: matches the FashionCheck addon's
/// AtkValues enum order (weapon, head, body, hands, legs, feet, ears, neck, wrist, ringL, ringR).</summary>
public enum FashionSlot
{
    Weapon = 0, Head = 1, Body = 2, Hands = 3, Legs = 4, Feet = 5,
    Ears = 6, Neck = 7, Wrist = 8, RingL = 9, RingR = 10,
}

public static class FashionSlotExtensions
{
    public static bool IsLeftSide(this FashionSlot slot) => (int)slot < ScoreMath.MainSlotCount;
    public static bool IsAccessory(this FashionSlot slot) => (int)slot >= ScoreMath.MainSlotCount;

    public static string DisplayName(this FashionSlot slot) => slot switch
    {
        FashionSlot.Weapon => "Weapon",
        FashionSlot.Head => "Head",
        FashionSlot.Body => "Body",
        FashionSlot.Hands => "Hands",
        FashionSlot.Legs => "Legs",
        FashionSlot.Feet => "Feet",
        FashionSlot.Ears => "Ears",
        FashionSlot.Neck => "Neck",
        FashionSlot.Wrist => "Wrist",
        FashionSlot.RingL => "Ring (left)",
        FashionSlot.RingR => "Ring (right)",
        _ => slot.ToString(),
    };
}

/// <summary>One week's Fashion Report: theme, per-slot hints, and the crowd data that answers them.</summary>
public sealed record FashionWeek
{
    public required int Week { get; init; }
    public required string Theme { get; init; }

    /// <summary>Hint text per slot; null/empty = no hint on that slot this week.</summary>
    public required IReadOnlyList<string?> Hints { get; init; }

    /// <summary>xivstats category row id per hinted slot (the crowdsourced DB key), where known.</summary>
    public IReadOnlyDictionary<FashionSlot, int> HintCategories { get; init; } =
        new Dictionary<FashionSlot, int>();

    /// <summary>Exact +2 dye name per left-side slot (from fashionreportxiv), where known.</summary>
    public IReadOnlyDictionary<FashionSlot, string> PlusTwoDyes { get; init; } =
        new Dictionary<FashionSlot, string>();

    /// <summary>+1 shade family name per left-side slot (from fashionreportxiv), where known.</summary>
    public IReadOnlyDictionary<FashionSlot, string> PlusOneShades { get; init; } =
        new Dictionary<FashionSlot, string>();

    public bool IsHinted(FashionSlot slot) =>
        (int)slot < Hints.Count && !string.IsNullOrWhiteSpace(Hints[(int)slot]);

    /// <summary>The computed weekly base (68/70/... depending on where the hints landed).</summary>
    public int BaseScore
    {
        get
        {
            var hinted = new bool[ScoreMath.TotalSlots];
            for (var i = 0; i < ScoreMath.TotalSlots; i++)
                hinted[i] = IsHinted((FashionSlot)i);
            return ScoreMath.WeeklyBase(hinted);
        }
    }
}

/// <summary>The player's equipped item on one slot, as read live.</summary>
public sealed record EquippedItem
{
    public required FashionSlot Slot { get; init; }
    /// <summary>Glamour-visible item id (glamour plate appearance wins over the physical item).</summary>
    public required uint ItemId { get; init; }
    public string Name { get; init; } = "";
    public uint Stain0Id { get; init; }
    public uint Stain1Id { get; init; }
}

/// <summary>A candidate item for a hinted slot, from the crowdsourced DB, filtered by ownership.</summary>
public sealed record CandidateItem
{
    public required FashionSlot Slot { get; init; }
    public required uint ItemId { get; init; }
    public required string Name { get; init; }
    /// <summary>Submissions counting this item as gold for this hint (xivstats CategoryData).</summary>
    public int Votes { get; init; }
    public bool Owned { get; init; }
}

/// <summary>Full per-slot scoring snapshot for the UI.</summary>
public sealed record SlotReport
{
    public required FashionSlot Slot { get; init; }
    public string? Hint { get; init; }
    public string? PlusTwoDye { get; init; }
    public string? PlusOneShade { get; init; }
    public EquippedItem? Equipped { get; init; }
    /// <summary>Item matches the hint's gold set (from crowdsourced votes or exact-dye logic).</summary>
    public bool ItemSatisfiesHint { get; init; }
    public DyeState? Dye { get; init; }
    public int Score { get; init; }
    public IReadOnlyList<CandidateItem> Candidates { get; init; } = Array.Empty<CandidateItem>();
}

/// <summary>Score predictor result for the current outfit.</summary>
public sealed record OutfitReport
{
    public required FashionWeek Week { get; init; }
    public required IReadOnlyList<SlotReport> Slots { get; init; }
    public int Total => Slots.Sum(s => s.Score);
    /// <summary>Score if every empty slot were filled with base-value items and exact dyes elsewhere.</summary>
    public int AchievableIfFilled => Slots.Sum(s => s.Score > 0
        ? s.Score
        : ScoreMath.SlotScore(s.Slot.IsAccessory(), s.Hint is not null, false, null));
    public bool FullMgp => Total >= 80;
    /// <summary>Plain-language readout for the status line.</summary>
    public string StatusLine
    {
        get
        {
            var total = Total;
            if (total >= 100) return $"scores {total} - perfect";
            if (total >= 80) return $"scores {total} - full 50k MGP";
            var need = 80 - total;
            return $"scores {total} - needs +{need} for 80";
        }
    }
}
