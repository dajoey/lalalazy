namespace LazyCrucible;

/// <summary>
///     What each familiar has eaten this run, so the shop can plan a feed before the feed picker opens. PURE. Filled
///     from every feed-picker / campsite read (both screens list satiety and the feeds eaten per familiar) and from the
///     plugin's own confirmed feeds; empty at the start of a run (feed lasts one run). Satiety caps come from the
///     XBMPet sheet.
/// </summary>
internal sealed class FeedMemory
{
    private readonly Dictionary<int, (int Used, List<int> Eaten)> _byRow = [];

    public void Reset() => _byRow.Clear();

    public void Update(PetPartyScreen screen)
    {
        foreach (var f in screen.Familiars)
            _byRow[f.Row] = (f.SatietyUsed, [.. f.FeedsEaten]);
    }

    public void RecordFed(int familiarRow, int feedRow)
    {
        var (used, eaten) = _byRow.TryGetValue(familiarRow, out var e) ? e : (0, new List<int>());
        if (!eaten.Contains(feedRow))
            eaten.Add(feedRow);
        _byRow[familiarRow] = (used + 1, eaten);
    }

    /// <summary> The run roster as feed candidates (index = position in <paramref name="rosterRows"/>). </summary>
    public List<FamiliarState> Familiars(IReadOnlyList<int> rosterRows, IReadOnlyDictionary<int, int> hpByRow)
    {
        var list = new List<FamiliarState>(rosterRows.Count);
        for (var i = 0; i < rosterRows.Count; i++)
        {
            var row = rosterRows[i];
            var max = row > 0 && row < CrucibleItems.SatietyMax.Length ? CrucibleItems.SatietyMax[row] : 0;
            var (used, eaten) = _byRow.TryGetValue(row, out var e) ? e : (0, new List<int>());
            list.Add(new FamiliarState(row, i, hpByRow.TryGetValue(row, out var hp) ? hp : 100, used, max, eaten));
        }
        return list;
    }
}
