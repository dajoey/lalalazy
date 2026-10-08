namespace RotationSolver.Decisions;

/// <summary>
///     Fork-only decision core (no Dalamud or game types, compiles standalone): which targeting type
///     <c>DataCenter.TargetingType</c> resolves to. Targeting types travel as their <c>TargetingType</c> enum
///     values (ints) so this file needs no game types.
/// </summary>
public static class TargetingChoice
{
    /// <summary>
    ///     The list <c>DataCenter.TargetingType</c> writes into the configuration when it finds the list empty:
    ///     LowHP, HighHP, Small, Big (as <c>(int)TargetingType</c>: 3, 2, 1, 0).
    /// </summary>
    public static readonly IReadOnlyList<int> DefaultList = [3, 2, 1, 0];

    /// <summary>
    ///     A forced value always wins. Otherwise the entry of <paramref name="list"/> selected by
    ///     <paramref name="index"/> (wrapped into the list); an empty list resolves against
    ///     <see cref="DefaultList"/>, which is what the getter fills the configuration with.
    /// </summary>
    public static int Resolve(int? forced, IReadOnlyList<int> list, int index)
    {
        if (forced.HasValue)
        {
            return forced.Value;
        }

        IReadOnlyList<int> entries = list.Count == 0 ? DefaultList : list;
        int i = index % entries.Count;
        if (i < 0)
        {
            i += entries.Count;
        }

        return entries[i];
    }
}
