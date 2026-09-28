using System;
using System.Collections.Generic;
using System.Text;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Everything in this file is exercised by tests/LazyMarketCompanion.Harness.

/// <summary>
/// Formats the plugin-log line that names the FULL held-unpriced set with item ids (and
/// names where the Item sheet resolves them), 0.2.5.0. Before this, the gate announced only
/// a COUNT ("54 item(s) have no confirmed market price") - the per-item diagnosis the
/// 2026-09-28 round needed had to be reconstructed indirectly from the priced set. One
/// greppable line per pass; unresolvable names fall back to the bare id.
/// </summary>
public static class HeldSetAnnounce
{
  /// <param name="held">Every held rule as (itemId, resolved name or empty string).</param>
  public static string FormatLog(IReadOnlyList<(uint ItemId, string Name)> held)
  {
    var sb = new StringBuilder("gate: held unpriced item(s) (no confirmed market price): ");
    for (var i = 0; i < held.Count; i++)
    {
      if (i > 0)
        sb.Append(", ");
      sb.Append(held[i].ItemId);
      if (!string.IsNullOrEmpty(held[i].Name))
        sb.Append(" (").Append(held[i].Name).Append(')');
    }
    return sb.ToString();
  }
}
