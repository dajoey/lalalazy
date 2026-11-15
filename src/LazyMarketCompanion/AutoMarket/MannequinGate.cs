using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Exercised by tests/LazyMarketCompanion.Harness (case 175, MannequinCases.cs).
//
// Ported 2026-11-10 from SHOEGAZEssb/Dagobert 811a5cddb (AutoPinch.IsItemMannequin), watched by
// the .lalalazy-sync/Dagobert.json marker: the absorbed engine's mannequin check matched a
// hardcoded EN/DE/FR string list ("adjust price", "preis ändern", "changer le prix"), so on a
// client in any other language a mannequin listing was never detected and the automation clicked
// "adjust price" on it. Upstream now compares the menu entries against the client's own localized
// "Adjust Price" label (the Addon sheet menu-action row); this gate carries that comparison and
// MarketAutomation supplies the label and the entry names.
public static class MannequinGate
{
  /// <summary>Addon sheet row of the client-localized "Adjust Price" context-menu action label. Row 930 is the price window title, whose translation can differ from the menu action.</summary>
  public const uint AdjustPriceMenuTextRowId = 959;

  /// <summary>
  /// True when the context menu belongs to a mannequin listing: the localized "adjust price"
  /// entry is absent. A blank label means nothing usable came back from the sheet lookup -
  /// treat the menu as a mannequin (skip) rather than clicking an unknown entry.
  /// </summary>
  public static bool IsMannequin(IEnumerable<string?> entries, string? adjustPriceText)
  {
    return string.IsNullOrWhiteSpace(adjustPriceText) ||
      !entries.Any(name => string.Equals(name, adjustPriceText, StringComparison.OrdinalIgnoreCase));
  }
}
