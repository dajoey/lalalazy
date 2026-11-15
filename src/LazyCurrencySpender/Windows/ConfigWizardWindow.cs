using CurrencySpender.Classes;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace CurrencySpender.Windows;

internal class ConfigWizardWindow : Window
{
    private static int Step = 0;
    private static int MaxSteps = 0;
    public static string Version = "0.0.0";
    private bool _resetScroll;

    private static readonly Dictionary<string, Action<int>> VersionSteps = new()
    {
        { "1.1.0", DrawVersion1_1_0Steps },
        { "1.1.2", DrawVersion1_1_2Steps },
        { "1.2.2", DrawVersion1_2_2Steps },
        { "1.2.3", DrawVersion1_2_3Steps },
        { "1.2.4", DrawVersion1_2_4Steps },
    };

    public ConfigWizardWindow() : base("ConfigWizardWindow")
    {
        SizeConstraints = new()
        {
            MinimumSize = new Vector2(520, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        CalculateSteps();
        P.ws.AddWindow(this);
    }

    public override void PreDraw()
    {
        WindowName = $"{P.Name} {P.Version} - Configuration Wizard###ConfigWizardWindow";
    }

    public override void Draw()
    {
        CurrencyChrome.PushFrame();
        try
        {
            CurrencyChrome.TopBar("Configuration wizard", UiState.NeedsSetup);
            CurrencyChrome.PageHeader(Step == 0 ? "Welcome" : "Configuration updates",
                "Review options introduced since the last configured version; every option remains available in settings.",
                Step == 0 ? $"{MaxSteps} steps" : $"Step {Step} of {MaxSteps}");
            Widgets.ProgressBar(MaxSteps == 0 ? 0 : Step / (float)MaxSteps);

            var footerHeight = FooterHeight(ImGui.GetContentRegionAvail().X);
            var available = ImGui.GetContentRegionAvail();
            ImGui.BeginChild("##StepContent", new Vector2(available.X, MathF.Max(40, available.Y - footerHeight)), false);
            if (_resetScroll)
            {
                ImGui.SetScrollY(0);
                _resetScroll = false;
            }
            DrawStep();
            ImGui.EndChild();
            DrawFooter();
        }
        finally
        {
            CurrencyChrome.PopFrame();
        }
    }

    private void DrawStep()
    {
        if (Step == 0)
        {
            CurrencyChrome.Card("wizard_welcome", () =>
            {
                CurrencyChrome.Section("Review configuration updates");
                Widgets.SecondaryWrapped(
                    "This wizard presents options added since the last configured version. Skip the setup to keep the current values, or review each step and change only what is needed.");
                Widgets.SecondaryWrapped("Every option can also be changed later in Settings.");
            });
            return;
        }

        var cumulativeSteps = 0;
        foreach (var (version, drawSteps) in VersionSteps)
        {
            if (!VersionHelper.LowerVersionThan(version, Version))
                continue;
            var versionStepCount = GetVersionStepCount(version);
            if (Step > cumulativeSteps && Step <= cumulativeSteps + versionStepCount)
            {
                CurrencyChrome.Section($"Changed in Version {version}");
                drawSteps(Step - cumulativeSteps);
                break;
            }
            cumulativeSteps += versionStepCount;
        }
    }

    private void DrawFooter()
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = FooterHeight(width);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));

        var buttons = new List<(string Id, string Label, bool Primary, Action Action)>
        {
            ("wizard_skip", "Skip", false, () => P.configWizard.IsOpen = false),
        };
        if (Step > 0)
            buttons.Add(("wizard_back", "Back", false, () => { Step--; _resetScroll = true; }));
        if (Step == MaxSteps)
            buttons.Add(("wizard_finish", "Finish", true, () => { P.configWizard.IsOpen = false; Step = 0; _resetScroll = true; }));
        else
            buttons.Add(("wizard_next", Step == 0 ? "Start" : "Next", true, () => { Step++; _resetScroll = true; }));

        var gap = 2 * Theme.Unit;
        var widths = buttons.Select(button => MathF.Max(82, ImGui.CalcTextSize(button.Label).X + 30)).ToArray();
        var buttonsWidth = widths.Sum() + ((buttons.Count - 1) * gap);
        var status = Step == 0 ? "Ready to review" : $"Step {Step}/{MaxSteps}";
        var narrow = buttonsWidth + ImGui.CalcTextSize(status).X + 48 > width;
        var buttonY = narrow ? 56 : 15;
        var x = width - buttonsWidth;
        for (var i = 0; i < buttons.Count; i++)
        {
            var button = buttons[i];
            var pos = origin + new Vector2(x, buttonY);
            if (CurrencyChrome.OutlineButton(draw, button.Id, button.Label, pos, new Vector2(widths[i], 34), button.Primary))
                button.Action();
            x += widths[i] + gap;
        }
        var textPos = origin + new Vector2(4, narrow ? 17 : 24);
        draw.AddCircleFilled(textPos + new Vector2(0, -3), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(UiState.NeedsSetup)));
        draw.AddText(textPos + new Vector2(12, -ImGui.GetTextLineHeight() / 2f), Theme.Pack(Theme.TextSecondary), status);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
    }

    private static float FooterHeight(float width) => width < 520 ? 104 : 64;

    private static void DrawVersion1_1_0Steps(int step)
    {
        switch (step)
        {
            case 1:
                ConfigCurrenciesTab.Draw();
                break;
            case 2:
                CurrencyChrome.Card("wizard_collectables", () =>
                {
                    CurrencyChrome.Toggle("Show collectables", ref C.ShowCollectables,
                        "Show selected collectables that are not yet registered.");
                    if (!C.ShowCollectables)
                        return;
                    CurrencyChrome.Toggle("Show missing collectables in the main window", ref C.ShowMissingCollectables,
                        "Add a missing-collectable count to each currency row.");
                    CurrencyChrome.Section("Collectable types", "Select the item types treated as collectables.");
                    foreach (CollectableType type in Enum.GetValues(typeof(CollectableType)))
                    {
                        if (type == CollectableType.None)
                            continue;
                        ConfigTab.DrawCollectableType(type);
                    }
                });
                break;
        }
    }

    private static void DrawVersion1_1_2Steps(int step)
    {
        if (step != 1)
            return;
        CurrencyChrome.Card("wizard_market", () =>
        {
            CurrencyChrome.Toggle("Show items eligible for sale", ref C.ShowSellables,
                "Show marketable items and their latest sales data.");
        });
        CurrencyChrome.Card("wizard_mahjong", () =>
        {
            CurrencyChrome.Section("Collectable type");
            foreach (CollectableType type in Enum.GetValues(typeof(CollectableType)))
            {
                if (type == CollectableType.Mahjong)
                    ConfigTab.DrawCollectableType(type);
            }
        });
        CurrencyChrome.Card("wizard_cowries", () =>
        {
            CurrencyChrome.Section("Currencies");
            foreach (var cur in P.Currencies.Where(cur => !cur.Child && cur.Enabled && (cur.ItemId == 37549 || cur.ItemId == 37550)).ToList())
                ConfigCurrenciesTab.DrawCurrencyToggle(cur);
        });
    }

    private static void DrawVersion1_2_2Steps(int step)
    {
        if (step != 1)
            return;
        CurrencyChrome.Card("wizard_cosmocredit", () =>
        {
            CurrencyChrome.Section("Currency");
            foreach (var cur in P.Currencies.Where(cur => !cur.Child && cur.Enabled && cur.ItemId == 45690).ToList())
                ConfigCurrenciesTab.DrawCurrencyToggle(cur);
        });
    }

    private static void DrawVersion1_2_3Steps(int step)
    {
        if (step != 1)
            return;
        CurrencyChrome.Card("wizard_auto_open", () =>
            CurrencyChrome.Toggle("Open automatically with the Currency window", ref C.OpenAutomatically,
                "Open the main window whenever the game Currency window opens."));
    }

    private static void DrawVersion1_2_4Steps(int step)
    {
        if (step != 1)
            return;
        CurrencyChrome.Card("wizard_minimum_sales", () =>
        {
            CurrencyChrome.Section("Market filter");
            ImGui.SetNextItemWidth(MathF.Min(180, ImGui.GetContentRegionAvail().X));
            ImGui.InputInt("Minimum sales", ref C.MinSales);
            Widgets.Caption("Minimum weekly sales for the market table; 0 disables the filter.");
        });
        CurrencyChrome.Card("wizard_credits", () =>
        {
            CurrencyChrome.Section("Currencies");
            foreach (var cur in P.Currencies.Where(cur => !cur.Child && cur.Enabled && (cur.ItemId == 45691 || cur.ItemId == 48146)).ToList())
                ConfigCurrenciesTab.DrawCurrencyToggle(cur);
        });
        CurrencyChrome.Card("wizard_master_recipes", () =>
        {
            CurrencyChrome.Section("Collectable type");
            foreach (CollectableType type in Enum.GetValues(typeof(CollectableType)))
            {
                if (type == CollectableType.MasterRecipes)
                    ConfigTab.DrawCollectableType(type);
            }
        });
    }

    private static void CalculateSteps()
    {
        MaxSteps = 0;
        foreach (var version in VersionSteps.Keys)
            if (VersionHelper.LowerVersionThan(version, Version))
                MaxSteps += GetVersionStepCount(version);
    }

    private static int GetVersionStepCount(string version) => version switch
    {
        "1.1.0" => 2,
        "1.1.2" => 1,
        "1.2.2" => 1,
        "1.2.3" => 1,
        "1.2.4" => 1,
        _ => 0,
    };

    public void SetVersion(string version)
    {
        Version = version;
        CalculateSteps();
    }
}
