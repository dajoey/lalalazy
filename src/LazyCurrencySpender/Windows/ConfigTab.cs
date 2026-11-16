using CurrencySpender.Classes;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace CurrencySpender.Windows;

internal class ConfigTab
{
    internal static void Draw()
    {
        CurrencyChrome.TwoColumns("settings",
            () =>
            {
                CurrencyChrome.Card("spending_guide_settings", () =>
                {
                    CurrencyChrome.Section("Spending guide", "Choose the tables shown for each currency.");
                    CurrencyChrome.Toggle("Show ventures", ref C.ShowVentures,
                        "Show ventures that can be bought with the selected currency.");
                    CurrencyChrome.Toggle("Show collectables", ref C.ShowCollectables,
                        "Show selected collectables that are not yet registered.");
                    if (C.ShowCollectables)
                        CurrencyChrome.Toggle("Show missing collectables in the main window", ref C.ShowMissingCollectables,
                            "Add a missing-collectable count to each currency row.");
                    CurrencyChrome.Toggle("Show items eligible for sale", ref C.ShowSellables,
                        "Show marketable items and their latest sales data.");

                    ImGui.SetNextItemWidth(MathF.Min(180, ImGui.GetContentRegionAvail().X));
                    ImGui.InputInt("Minimum sales", ref C.MinSales);
                    Widgets.Caption("Minimum weekly sales for the market table; 0 disables the filter.");
                });

                if (C.ShowCollectables)
                    CurrencyChrome.Card("collectable_types", () =>
                    {
                        CurrencyChrome.Section("Collectable types",
                            "Only selected types appear in spending guides and missing-count calculations.",
                            C.SelectedCollectableTypes.Count + " selected");
                        foreach (CollectableType type in Enum.GetValues(typeof(CollectableType)))
                        {
                            if (type == CollectableType.None || type == CollectableType.Container)
                                continue;
                            DrawCollectableType(type);
                        }
                    });
            },
            () =>
            {
                CurrencyChrome.Card("display_startup", () =>
                {
                    CurrencyChrome.Section("Display & startup");
                    string[] separators = ["None", "Dot (.)", "Comma (,)"];
                    if (ImGui.BeginCombo("Thousands separator", separators[C.Separator]))
                    {
                        for (var i = 0; i < separators.Length; i++)
                        {
                            var isSelected = C.Separator == i;
                            if (ImGui.Selectable(separators[i], isSelected))
                                C.Separator = i;
                            if (isSelected)
                                ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }
                    CurrencyChrome.Toggle("Open automatically with the Currency window", ref C.OpenAutomatically,
                        "Open the main window whenever the game Currency window opens.");
                });

                CurrencyChrome.Card("setup_diagnostics", () =>
                {
                    CurrencyChrome.Section("Setup & diagnostics");
                    Widgets.SecondaryWrapped("Review options introduced in earlier versions.");
                    if (CurrencyChrome.Button("open_config_wizard", "Open configuration wizard"))
                        VersionHelper.OpenConfigWizard();
                    CurrencyChrome.Toggle("Debug Mode", ref C.Debug,
                        "Expose raw diagnostic data. Enable only when diagnosing a problem.");
                });
            });
    }

    internal static void DrawCollectableType(CollectableType type)
    {
        var label = CollectableTypeLabels.TryGetValue(type, out var displayName) ? displayName : type.ToString();
        var isSelected = C.SelectedCollectableTypes.Contains(type);
        if (!CurrencyChrome.Toggle(label, ref isSelected, id: "collectable_" + type))
            return;
        if (isSelected)
            C.SelectedCollectableTypes.Add(type);
        else
            C.SelectedCollectableTypes.Remove(type);
        P.spendingWindow.UpdateData();
        MainTab.update(true);
    }
}
