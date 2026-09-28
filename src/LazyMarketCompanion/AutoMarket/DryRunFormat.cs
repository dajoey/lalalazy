using System;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// Deterministic string formatting for Auto-Market dry-run log lines (0.2.1.0, design §6 pin).
/// While dry-run is on, Auto-Market computes every listing, pulling and vendoring decision
/// and logs it via these methods without executing anything.
/// </summary>
public static class DryRunFormat
{
  public static string WouldList(uint itemId, bool hq, int quantity, StockOrigin origin, int targetSlot)
    => $"[AM][dry-run] would list {itemId}{(hq ? " HQ" : "")} x{quantity} from {origin} into market#{targetSlot}";

  public static string WouldPull(int slot, uint itemId, bool hq, int quantity, long listedPrice, long thresholdGil, PullTarget target)
    => $"[AM][dry-run] would pull market#{slot} item {itemId}{(hq ? " HQ" : "")} x{quantity} (listed at {listedPrice:N0} gil, under the {thresholdGil:N0} gil net threshold) back to {(target == PullTarget.PlayerBags ? "player inventory" : "retainer inventory")}";

  public static string WouldVendor(string containerName, int slot, uint itemId, bool hq, int quantity, long estGil)
    => $"[AM][dry-run] would vendor {containerName}:{slot} item {itemId}{(hq ? " HQ" : "")} x{quantity} (est {estGil:N0} gil)";
}
