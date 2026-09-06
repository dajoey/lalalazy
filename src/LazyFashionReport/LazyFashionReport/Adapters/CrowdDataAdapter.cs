using LazyFashionReport.Core;

namespace LazyFashionReport.Adapters;

/// <summary>
/// CrowdData over the two live datasets:
/// - xivstats Categories map hint CATEGORY ROW ID -> {itemId: votes}.
/// - xivstats WeeklyDyes map week -> [{Id: equip-slot code, Dyes: {stainId: {Count,Pct}}}].
///   Slot codes (verified): 1=weapon 34=head 35=body 37=hands 36=legs 38=feet.
/// - fashionreportxiv report-state supplies this week's exact plus2 dye name per slot; we
///   resolve that NAME to a stain id via the live Stain sheet (adapter-supplied lookup).
/// Category resolution: the FashionCheckThemeCategory sheet maps row id -> hint name; live
/// hint text comes from the addon, so we match category by NAME, not by row id.
/// </summary>
public sealed class CrowdDataAdapter : CrowdData
{
    private readonly RemoteDataSource.XivStatsRoot? _xiv;
    private readonly RemoteDataSource.ReportState? _state;
    private readonly IReadOnlyDictionary<string, uint> _dyeNameToStain;   // lowercase name -> stain id
    private readonly IReadOnlyDictionary<string, int> _categoryNameToRow; // hint name -> category row id
    private readonly IReadOnlyDictionary<uint, string> _itemNameById;

    public CrowdDataAdapter(
        RemoteDataSource.XivStatsRoot? xiv,
        RemoteDataSource.ReportState? state,
        IReadOnlyDictionary<string, uint> dyeNameToStain,
        IReadOnlyDictionary<string, int> categoryNameToRow,
        IReadOnlyDictionary<uint, string> itemNameById)
    {
        _xiv = xiv;
        _state = state;
        _dyeNameToStain = dyeNameToStain;
        _categoryNameToRow = categoryNameToRow;
        _itemNameById = itemNameById;
    }

    private static readonly IReadOnlyDictionary<int, FashionSlot> SlotCodeToSlot = new Dictionary<int, FashionSlot>
    {
        [1] = FashionSlot.Weapon, [34] = FashionSlot.Head, [35] = FashionSlot.Body,
        [37] = FashionSlot.Hands, [36] = FashionSlot.Legs, [38] = FashionSlot.Feet,
    };

    public IReadOnlyList<CandidateItem> CandidatesFor(FashionWeek week, FashionSlot slot, IReadOnlySet<uint>? owned)
    {
        if (_xiv is null) return Array.Empty<CandidateItem>();
        if (!week.IsHinted(slot)) return Array.Empty<CandidateItem>();
        var hint = week.Hints[(int)slot]!;
        if (!_categoryNameToRow.TryGetValue(Normalize(hint), out var catRow)) return Array.Empty<CandidateItem>();
        if (!_xiv.Categories.TryGetValue(catRow.ToString(), out var items) || items.Count == 0)
            return Array.Empty<CandidateItem>();

        var ranked = items
            .Select(kv => (ItemId: uint.Parse(kv.Key), Votes: kv.Value))
            .Where(x => x.ItemId != 0)
            .OrderByDescending(x => x.Votes)
            .ToList();

        IEnumerable<(uint ItemId, int Votes)> seq = ranked;
        if (owned is not null)
            seq = seq.Where(x => owned.Contains(x.ItemId));

        return seq.Take(200)
            .Select(x => new CandidateItem
            {
                Slot = slot,
                ItemId = x.ItemId,
                Name = _itemNameById.GetValueOrDefault(x.ItemId, $"item {x.ItemId}"),
                Votes = x.Votes,
                Owned = owned is null || owned.Contains(x.ItemId),
            })
            .ToList();
    }

    public IReadOnlySet<uint> GoldIdsFor(FashionWeek week, FashionSlot slot)
    {
        if (_xiv is null || !week.IsHinted(slot)) return new HashSet<uint>();
        var hint = week.Hints[(int)slot]!;
        if (!_categoryNameToRow.TryGetValue(Normalize(hint), out var catRow)) return new HashSet<uint>();
        if (!_xiv.Categories.TryGetValue(catRow.ToString(), out var items)) return new HashSet<uint>();
        return items.Keys.Select(uint.Parse).ToHashSet();
    }
    // exact plus2 dye per left-side slot: fashionreportxiv name -> stain id (highest-confidence
    // source), falling back to the week's top-voted crowd dye for that slot when frxiv is stale.
    public uint PreferredStainFor(FashionWeek week, FashionSlot slot)
    {
        if (!slot.IsLeftSide()) return 0;

        // 1. fashionreportxiv exact dye
        if (_state?.DyeData is { } dd)
        {
            var key = SlotKey(slot);
            if (dd.TryGetValue(key, out var entry) && !string.IsNullOrWhiteSpace(entry.Plus2))
            {
                if (_dyeNameToStain.TryGetValue(Normalize(entry.Plus2), out var stain) && stain != 0)
                    return stain;
            }
        }

        // 2. top crowd dye for this week + slot
        if (_xiv?.WeeklyDyes is { } wd && wd.TryGetValue(week.Week.ToString(), out var slots))
        {
            foreach (var s in slots)
            {
                if (SlotCodeToSlot.GetValueOrDefault(s.Id) != slot) continue;
                if (s.Dyes is null || s.Dyes.Count == 0) continue;
                var best = s.Dyes
                    .Where(kv => uint.TryParse(kv.Key, out var id) && id != 0)
                    .OrderByDescending(kv => kv.Value.Count)
                    .Select(kv => uint.Parse(kv.Key))
                    .FirstOrDefault();
                return best;
            }
        }

        return 0;
    }

    private static string SlotKey(FashionSlot slot) => slot switch
    {
        FashionSlot.Weapon => "weapon",
        FashionSlot.Head => "head",
        FashionSlot.Body => "body",
        FashionSlot.Hands => "hands",
        FashionSlot.Legs => "legs",
        FashionSlot.Feet => "feet",
        _ => "",
    };

    private static string Normalize(string s) => s.Trim().ToLowerInvariant();
}
