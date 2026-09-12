namespace LazyFashionReport.Core;

/// <summary>One slot of the assembled 80+ plan (P4 planner half, v0.4.0.0).</summary>
public sealed record OutfitPiece
{
    public required FashionSlot Slot { get; init; }
    /// <summary>Chosen item name; "any item" for unhinted slots where anything scores base.</summary>
    public required string ItemName { get; init; }
    public uint ItemId { get; init; }
    /// <summary>True when the chosen item is a crowd gold for this slot's hint (+8/+6).</summary>
    public bool SatisfiesHint { get; init; }
    /// <summary>Points this slot contributes in the plan.</summary>
    public int Score { get; init; }
    /// <summary>Where the piece sits for the player (P3 note; "" when in bags).</summary>
    public string LocationNote { get; init; } = "";
    /// <summary>Dye instruction for left-side slots: "apply Jet Black (+2)", "Jet Black not owned - Snow White owned (+1)", "no matching dye owned", or "" when no dye preference.</summary>
    public string DyeNote { get; init; } = "";
}

/// <summary>The best-achievable outfit from what the player owns (P4 planner).</summary>
public sealed record OutfitAssembly
{
    public required IReadOnlyList<OutfitPiece> Pieces { get; init; }
    public int Total => Pieces.Sum(p => p.Score);
    public bool Reaches80 => Total >= 80;
    /// <summary>Plain-language blockers when the plan cannot reach 80.</summary>
    public IReadOnlyList<string> Gaps { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Pure 80+ outfit planner (P4, planner half): composes the best outfit from OWNED pieces
/// plus owned dyes and says exactly what to wear and dye, slot by slot, with the predicted
/// total. The physical equip/dye application (executor half) is deliberately NOT here —
/// it moves real gear and consumes real dye items, and ships only after live pin
/// verification. Offline-harness-tested.
/// </summary>
public static class OutfitAssembler
{
    /// <param name="week">Current week (hints + preferred dyes).</param>
    /// <param name="crowd">Crowd golds per slot.</param>
    /// <param name="owned">The per-location owned catalog (P3).</param>
    /// <param name="itemName">Item id -> display name.</param>
    /// <param name="plusTwoStain">Left-side slot -> this week's exact +2 stain id (0 unknown).</param>
    /// <param name="ownedStains">Stain ids the player can apply right now (owns the dye item).</param>
    /// <param name="stainName">Stain id -> display name.</param>
    /// <param name="stainFamilies">Stain id -> shade family (the scoring family).</param>
    public static OutfitAssembly Build(
        FashionWeek week,
        CrowdData? crowd,
        OwnedCatalog? owned,
        Func<uint, string> itemName,
        IReadOnlyDictionary<FashionSlot, uint> plusTwoStain,
        IReadOnlySet<uint> ownedStains,
        IReadOnlyDictionary<uint, string> stainName,
        IReadOnlyDictionary<uint, string> stainFamilies)
    {
        var pieces = new List<OutfitPiece>(ScoreMath.TotalSlots);
        var gaps = new List<string>();

        foreach (var i in Enum.GetValues<FashionSlot>())
        {
            var slot = i;
            var hinted = week.IsHinted(slot);
            var isAcc = slot.IsAccessory();

            string name = "any item";
            uint itemId = 0;
            bool satisfies = false;
            var location = "";

            if (hinted)
            {
                // Best owned crowd candidate first (votes order = CandidatesFor order).
                var best = crowd?.CandidatesFor(week, slot, owned?.Ids()).FirstOrDefault();
                if (best is { } b)
                {
                    name = b.Name;
                    itemId = b.ItemId;
                    satisfies = crowd!.GoldIdsFor(week, slot).Contains(b.ItemId);
                    location = owned?.LocationNote(b.ItemId) ?? "";
                }
                else
                {
                    gaps.Add($"{slot.DisplayName()}: no owned candidate for \"{week.Hints[(int)slot]}\" - any item still scores {ScoreMath.HintedSlotBase}");
                }
            }

            // Dye instruction for left-side slots with a known exact dye.
            var dyeNote = "";
            if (slot.IsLeftSide() && plusTwoStain.TryGetValue(slot, out var stain) && stain != 0)
            {
                var stainNm = stainName.GetValueOrDefault(stain, $"stain {stain}");
                if (ownedStains.Contains(stain))
                {
                    dyeNote = $"apply {stainNm} (+2)";
                }
                else
                {
                    var family = stainFamilies.GetValueOrDefault(stain, "");
                    var alt = ownedStains
                        .Where(s => s != stain && stainFamilies.GetValueOrDefault(s, "") != "" && stainFamilies.GetValueOrDefault(s, "") == family)
                        .Select(s => stainName.GetValueOrDefault(s, $"stain {s}"))
                        .FirstOrDefault();
                    dyeNote = alt is { Length: > 0 } a
                        ? $"{stainNm} not owned - {a} owned (+1)"
                        : $"no matching dye owned ({stainNm} +2, same-shade +1)";
                }
            }

            var score = ScoreMath.SlotScore(isAcc, hinted, satisfies, dyeNote.StartsWith("apply ") ? new DyeState { SlotHasDye = true, IsExact = true, IsSameShade = true } : null);
            pieces.Add(new OutfitPiece
            {
                Slot = slot,
                ItemName = name,
                ItemId = itemId,
                SatisfiesHint = satisfies,
                Score = score,
                LocationNote = location,
                DyeNote = dyeNote,
            });
        }

        return new OutfitAssembly { Pieces = pieces, Gaps = gaps };
    }
}
