using LalaUiKit;

namespace CurrencySpender.Windows;

internal class InstructionsTab
{
    internal static void Draw()
    {
        Step(1, "Choose a currency",
            "Select the desired currency and use Spend it to open its spending guide.");
        Step(2, "Review the tables",
            "Collectables, ventures, marketable items, tracked items, and equipment exchanges appear when available.");
        Step(3, "Adjust the visible tables",
            "Ventures, collectables, marketable items, and tracked items can be toggled in settings.");
        Step(4, "Travel to a vendor",
            "Flag opens the map at the vendor. TP teleports to the nearest aetheryte and also places a flag marker on the map.");
        CurrencyChrome.Card("shared_fate_help", () =>
        {
            CurrencyChrome.Section("Shared FATE ranks");
            Widgets.SecondaryWrapped(
                "If the current Shared FATE rank cannot be retrieved, a warning appears in the main window. Open the shared FATE rank screen from that warning so the plugin can read the required information.");
        }, CurrencyCardTone.Attention);
    }

    private static void Step(int number, string title, string body)
    {
        CurrencyChrome.Card("instruction_" + number, () =>
        {
            CurrencyChrome.Section($"{number}. {title}");
            Widgets.SecondaryWrapped(body);
        });
    }
}
