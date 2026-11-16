using CurrencySpender.Classes;
using LalaUiKit;

namespace CurrencySpender.Windows;

internal class ConfigCurrenciesTab
{
    internal static void Draw()
    {
        var currencies = P.Currencies.Where(cur => !cur.Child && cur.Enabled).ToList();
        CurrencyChrome.TwoColumns("currency_selection",
            () => CurrencyChrome.Card("available_currencies", () =>
            {
                CurrencyChrome.Section("Available currencies",
                    "Deselected currencies stay out of the main window.",
                    $"{C.SelectedCurrencies.Count} selected");
                if (currencies.Count == 0)
                {
                    Widgets.InfoBox("Empty — no currencies are currently available.", UiState.Empty);
                    return;
                }
                foreach (var cur in currencies)
                    DrawCurrencyToggle(cur);
            }),
            () => CurrencyChrome.Card("currency_display", () =>
            {
                CurrencyChrome.Section("Display");
                CurrencyChrome.Toggle("Hide currencies you currently don't own", ref C.HideEmptyCurrencies,
                    "Hide zero-balance currencies without changing the selected set.");
                Widgets.SecondaryWrapped($"{currencies.Count} currencies are available; {C.SelectedCurrencies.Count} are selected.");
            }));
    }

    internal static void DrawCurrencyToggle(TrackedCurrency cur)
    {
        var isSelected = C.SelectedCurrencies.Contains(cur.ItemId);
        if (!CurrencyChrome.Toggle(cur.Name, ref isSelected, id: "currency_" + cur.ItemId))
            return;
        if (isSelected)
            C.SelectedCurrencies.Add(cur.ItemId);
        else
            C.SelectedCurrencies.Remove(cur.ItemId);
        P.spendingWindow.UpdateData();
        MainTab.update(true);
    }
}
