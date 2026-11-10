using LazyMarketCompanion.AutoMarket;

// Offline case 175: the mannequin listing gate (0.2.8.19, ported from upstream Dagobert
// 811a5cddb's AutoPinch.IsItemMannequin). The check used to match a hardcoded EN/DE/FR list of
// the context menu's "adjust price" label, so on a client in any other language a mannequin
// listing was never detected and the automation clicked "adjust price" on it. The port compares
// the menu entries against the client's own localized label (Addon sheet row 930); these cases
// pin that contract in every situation upstream's own MannequinDetectionTests cover, including
// the culture independence of the comparison. The sheet-lookup wiring itself (fetching the label
// in ClickAdjustPrice) is Dalamud-side glue and is exercised on the live path.
internal static class MannequinCases
{
  public static void Run(Action<string, bool, string> check)
  {
    // 175a. A real listing shows the price action under ANY client language: not a mannequin.
    foreach (var label in new[] { "Adjust Price", "Preis ändern", "価格を変更する", "Changer le prix", "调整价格", "Another localized price action" })
      check($"175 mannequin: a '{label}' menu entry means a real listing",
        !MannequinGate.IsMannequin(new[] { label, "Remove" }, label), "");

    // 175b. Menus without the price action stay protected (mannequin), in every phrasing.
    foreach (var entry in new[] { "Display on mannequin", "Remove from mannequin", "Do not Adjust Price" })
      check($"175 mannequin: '{entry}' without the price action stays mannequin",
        MannequinGate.IsMannequin(new[] { entry }, "Adjust Price"), "");

    // 175c. An empty menu is a mannequin.
    check("175 mannequin: an empty menu stays mannequin",
      MannequinGate.IsMannequin(Array.Empty<string>(), "調整价格"), "");

    // 175d. A blank label (nothing usable from the sheet) never matches an entry: protected.
    foreach (var label in new string?[] { null, "", " " })
      check("175 mannequin: a blank localized label never matches an entry",
        MannequinGate.IsMannequin(new[] { "", " " }, label), "");

    // 175e. The comparison ignores case and is culture-independent (OrdinalIgnoreCase).
    check("175 mannequin: the label comparison is case-insensitive and culture-independent",
      !MannequinGate.IsMannequin(new[] { "ADJUST PRICE" }, "Adjust Price")
      && !MannequinGate.IsMannequin(new[] { "adjust price" }, "ADJUST PRICE"), "");
  }
}
