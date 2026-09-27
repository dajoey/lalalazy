namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Exercised by tests/LazyMarketCompanion.Harness (case 134).
//
// 0.2.0.0 (docs/AutoMarket-Design.md §5, invariant V4): no market slot is left silently
// empty after a withdrawal pass. The listing plan that follows a pull is built from a FRESH
// market snapshot, and every slot that is still empty after the plan is either targeted by
// a planned listing or named here with the reason. The note is the recorded hold decision;
// the orchestrator logs it with the plan so a run can always answer "why is slot #7 empty?".
public static class SlotAccounting
{
  /// <summary>
  /// One line naming every slot the market snapshot shows empty that the listing plan does not
  /// fill, with the reason. Empty when the plan fills every free slot (the ordinary case).
  /// </summary>
  /// <param name="market">The FRESH post-withdrawal market snapshot the plan was built against.</param>
  /// <param name="plannedTargetSlots">Target slots of the listing plan's ops.</param>
  /// <param name="slotCount">The retainer's market slot count (20).</param>
  public static string EmptySlotNote(IReadOnlyList<MarketSlot> market, IReadOnlyList<int> plannedTargetSlots, int slotCount)
  {
    var planned = new HashSet<int>(plannedTargetSlots);
    var unfilled = new List<int>();
    for (var i = 0; i < slotCount; i++)
    {
      var occupied = i < market.Count && market[i].ItemId != 0 && market[i].Quantity > 0;
      if (!occupied && !planned.Contains(i))
        unfilled.Add(i);
    }
    if (unfilled.Count == 0)
      return string.Empty;
    return $"plan: {unfilled.Count} empty market slot(s) left empty this pass (no eligible stock, or held by the value gate): #{string.Join(", #", unfilled)}";
  }
}
