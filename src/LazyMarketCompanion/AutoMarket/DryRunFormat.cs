using System;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// Deterministic string formatting for the OFFLINE HARNESS's dry-run simulation log lines
/// (0.2.4.0: the in-game dry-run gate is removed — testing builds list live by default).
/// This is the simulation formatter the harness uses to render "what a recorded pass would
/// do" against the recorded incident fixtures (design §9) — SC4's dry-run evidence path.
/// No in-game code path calls into this file.
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

