namespace ArmoireAutoFill.Data.Shopping;

// User-facing instructions for importing an Armoire copy-code into Knightshopper.
// Pure file: also compiled by the offline ShoppingListHarness, which pins the wording
// against the control labels verified in Knightshopper 1.0.1.6 (decompiled from its
// released dll plus its en localization files):
//   - main window, left sidebar, category "Currencies" (General.json "Category.Currencies");
//     one sidebar entry per currency named exactly as in General.json "_name.*UI",
//     e.g. "Gil", "The Hunt", "MGP", "Tomestones";
//   - top of each currency page: a shopping-list dropdown showing the current list's
//     name (first widget in the panel body, next to the Buy All button);
//   - inside that dropdown, below the saved lists, the row
//     [ "New list name..." input ] [ + add button ] [ clipboard icon button ]:
//     the last one is the import button, ImGui id "##pasteShoppingList",
//     tooltip "Paste" (ItemUI.json "Paste", FontAwesome clipboard-list);
//   - success prints to the game chat as "[Knightshopper] Imported the shopping list
//     from the clipboard." (ItemUI.json "ImportedList" via TaskErrorLog.Info);
//   - a code for a different currency is refused (CurrencyConfigurationStore.json
//     "WrongCurrency": "This is a {0} list. Import it from the {0} window.",
//     also printed to chat as a warning).
public static class KnightshopperInstructions
{
    public static string CopiedMessage(byte currencyId, int itemCount, string listName)
    {
        var window = CurrencyNames.For(currencyId);
        return $"Copied {itemCount} item(s) for {window} — the import code is on your clipboard.\n"
             + $"In Knightshopper: click {window} in the left sidebar (under Currencies), then click the "
             + "shopping-list dropdown at the top of the window — it shows the current list's name, next to the Buy All button.\n"
             + "In the open dropdown, click the clipboard icon at the right end of the 'New list name...' row; "
             + "its tooltip is 'Paste'.\n"
             + $"That imports a new list named '{listName}', selects it, and keeps your existing lists. "
             + "Knightshopper confirms it in your chat log: 'Imported the shopping list from the clipboard.'\n"
             + $"Each code imports only in its own currency's window: {window} codes do not import anywhere else — pasted in a "
             + "different currency's window, Knightshopper refuses it and tells you which window to use.\n"
             + $"If Knightshopper refuses the code itself, it names only the first item it does not recognise "
             + $"('Item N is not available from its shared {window} vendor.') — note that number; the rest of the list is not checked past it.";
    }

    // Shown under the shopping list: what the codes were checked against, and what was not.
    // The game version is the install the offline check ran on (tools/KnightshopperGroundTruth);
    // the player's game may be on a newer patch.
    public const string CatalogCheckVersion = "game version 2026.08.05";

    public static string CatalogCheckNote() =>
        "The vendors and shops in these codes were checked offline against Knightshopper 1.0.1.6's own vendor catalog "
        + $"({CatalogCheckVersion}). Not yet confirmed in-game, and newer patches are not covered.";

    public static string NotLoadedMessage() =>
        "Knightshopper is not installed or not loaded — the codes below still copy to your clipboard; "
        + "once it is loaded, import each code in its currency's window: open the shopping-list dropdown "
        + "at the top of the page and click the clipboard icon labelled 'Paste' next to the 'New list name...' box.";
}
