namespace LazyFashionReport.Core;

/// <summary>
/// Pure Fashion Report scoring math. Game-assembly-free by design: the offline harness replays
/// week 449 (base 70; golds body/hands/feet/neck; easy100 = those 4 items; easy80 =
/// Brand-new Gloves + Abyssal Blue on head) and every number must reproduce exactly.
///
/// Verified rules (consolegameswiki + week 449 live data, 2026-09-06):
/// - Unhinted slot base: 10 (main gear), 8 (accessories).
/// - Hinted slot base drops to 2; a correct item adds +8 (main) / +6 (accessory).
/// - Dyes (left side only: weapon/head/body/hands/legs/feet): exact dye +2, same shade +1.
/// - The weekly base is COMPUTED, never hardcoded: 68 when all four hints are main slots,
///   70 when one hint is an accessory (verified week 449).
/// </summary>
public static class ScoreMath
{
    public const int MainSlotCount = 6;       // weapon..feet (indices 0..5)
    public const int AccessorySlotCount = 5;  // ears..ringR (indices 6..10)
    public const int TotalSlots = MainSlotCount + AccessorySlotCount;

    /// <summary>Base points an UNHINTED slot awards when filled.</summary>
    public static int BaseFor(bool isAccessory) => isAccessory ? 8 : 10;

    /// <summary>Base a HINTED slot awards when filled with anything.</summary>
    public const int HintedSlotBase = 2;

    /// <summary>Points a correct item adds in a hinted slot.</summary>
    public static int CorrectItemBonus(bool isAccessory) => isAccessory ? 6 : 8;

    public const int ExactDyeBonus = 2;
    public const int SameShadeBonus = 1;

    /// <summary>True for slot indices 6..10 (ears/neck/wrist/ringL/ringR).</summary>
    public static bool IsAccessorySlot(int slotIndex) => slotIndex >= MainSlotCount;

    /// <summary>
    /// The week's score when all 11 slots are filled but no hint is satisfied and no dye applied:
    /// every hinted slot contributes 2 instead of its 10/8 base. 100 with no hints,
    /// 68 when all four hints are main slots, 70 when one is an accessory.
    /// </summary>
    public static int WeeklyBase(IReadOnlyList<bool> hinted)
    {
        var total = 0;
        for (var i = 0; i < TotalSlots; i++)
        {
            var isAcc = IsAccessorySlot(i);
            var slotBase = BaseFor(isAcc);
            total += i < hinted.Count && hinted[i] ? HintedSlotBase : slotBase;
        }
        return total;
    }

    /// <summary>
    /// Score one slot. A slot not filled contributes 0 — the caller decides that by not
    /// invoking this; an equipped-but-unjudged call is a defect, not a 10.
    /// </summary>
    /// <param name="isAccessory">ears/neck/wrist/ringL/ringR.</param>
    /// <param name="hinted">this slot has a hint this week.</param>
    /// <param name="itemSatisfiesHint">the equipped item satisfies the hint (gold-tier).</param>
    /// <param name="dyeState">weapon/head/body/hands/legs/feet only; null for accessories.</param>
    public static int SlotScore(bool isAccessory, bool hinted, bool itemSatisfiesHint, DyeState? dyeState)
    {
        var score = hinted ? HintedSlotBase : BaseFor(isAccessory);
        if (hinted && itemSatisfiesHint) score += CorrectItemBonus(isAccessory);
        score += dyeState?.Points ?? 0;
        return score;
    }
}

/// <summary>Dye evaluation for one left-side slot.</summary>
public sealed record DyeState
{
    public required bool SlotHasDye { get; init; }
    public required bool IsExact { get; init; }
    public required bool IsSameShade { get; init; }

    /// <summary>+2 exact, +1 same shade, +0 otherwise (or when the slot has no dye).</summary>
    public int Points => !SlotHasDye ? 0 : IsExact ? ScoreMath.ExactDyeBonus : IsSameShade ? ScoreMath.SameShadeBonus : 0;
}
