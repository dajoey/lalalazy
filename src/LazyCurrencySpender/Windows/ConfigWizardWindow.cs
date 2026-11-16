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
    private readonly CurrencyChrome.ShellControls _controls;

    private static readonly Dictionary<string, Action<int>> VersionSteps = new()
    {
        { "1.1.0", DrawVersion1_1_0Steps },
        { "1.1.2", DrawVersion1_1_2Steps },
        { "1.2.2", DrawVersion1_2_2Steps },
        { "1.2.3", DrawVersion1_2_3Steps },
        { "1.2.4", DrawVersion1_2_4Steps },
    };

    public ConfigWizardWindow() : base("ConfigWizardWindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _controls = new(this, new Vector2(520, 420), new Vector2(float.MaxValue, float.MaxValue));
        CalculateSteps();
        P.ws.AddWindow(this);
    }

    public override void PreDraw()
    {
        WindowName = $"{P.Name} {P.Version} - Configuration Wizard###ConfigWizardWindow";
    }

    public override void Draw()
    {
        var minimized = _controls.PrepareDraw();
        CurrencyChrome.PushFrame();
        try
        {
            CurrencyChrome.TopBar("Configuration wizard", UiState.NeedsSetup, controls: _controls);
            if (minimized)
                return;
            // Fork: the page header, progress track and step content scroll together in one shared body child; the footer is a fixed band.
            var footerHeight = FooterHeight(ImGui.GetWindowWidth());
            CurrencyChrome.BeginBody("##StepContent", footerHeight);
            if (_resetScroll)
            {
                ImGui.SetScrollY(0);
                _resetScroll = false;
            }
            CurrencyChrome.PageHeader(Step == 0 ? "Welcome" : "Configuration updates",
                "Review options introduced since the last configured version; every option remains available in settings.",
                Step == 0 ? $"{MaxSteps} steps" : $"Step {Step} of {MaxSteps}");
            CurrencyChrome.ProgressTrack(MaxSteps == 0 ? 0 : Step / (float)MaxSteps);
            DrawStep();
            CurrencyChrome.EndBody();
            DrawFooter(footerHeight);
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
                CurrencyChrome.Muted(
                    "This wizard presents options added since the last configured version. Skip the setup to keep the current values, or review each step and change only what is needed."); // Fork: secondary size
                CurrencyChrome.Muted("Every option can also be changed later in Settings.");
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

    // Fork: full-bleed footer band at the suite geometry (60 px, status block at gutter + 16, 36 px buttons).
    private List<(string Id, string Label, bool Primary, Action Action)> FooterButtons()
    {
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
        return buttons;
    }

    private static string FooterDetail() => Step == 0 ? $"{MaxSteps} steps to review" : $"Step {Step} of {MaxSteps}";

    private static string FooterStatus() => Step == 0 ? "Ready to review" : Step == MaxSteps ? "Ready to finish" : "Reviewing changes";

    private bool FooterNarrow(float width)
    {
        var gutter = CurrencyChrome.Gutter(width);
        var buttons = FooterButtons();
        var buttonsWidth = buttons.Sum(button => CurrencyChrome.ActionWidth(96, button.Label)) + ((buttons.Count - 1) * 8);
        return buttonsWidth + CurrencyChrome.TextWidth(FooterStatus()) + 16 + 16 + (2 * gutter) > width;
    }

    private float FooterHeight(float width)
    {
        var textBlock = CurrencyChrome.LineHeight() + 4 + CurrencyChrome.LineHeight(Theme.SecondaryScale);
        return FooterNarrow(width) ? 12 + textBlock + 12 + CurrencyChrome.Control + 12 : 12 + MathF.Max(textBlock, CurrencyChrome.Control) + 12;
    }

    private void DrawFooter(float height)
    {
        var winPos = ImGui.GetWindowPos();
        var width = ImGui.GetWindowWidth();
        var gutter = CurrencyChrome.Gutter(width);
        var top = winPos.Y + ImGui.GetWindowHeight() - height;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(winPos, winPos + ImGui.GetWindowSize(), false);
        draw.AddRectFilled(new Vector2(winPos.X, top), winPos + ImGui.GetWindowSize(), Theme.Pack(Theme.ShellBar));
        draw.AddLine(new Vector2(winPos.X, top), new Vector2(winPos.X + width, top), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        var buttons = FooterButtons();
        var narrow = FooterNarrow(width);
        var gap = 8f;
        var widths = buttons.Select(button => CurrencyChrome.ActionWidth(96, button.Label)).ToArray();
        var buttonsWidth = widths.Sum() + ((buttons.Count - 1) * gap);
        var textBlock = CurrencyChrome.LineHeight() + 4 + CurrencyChrome.LineHeight(Theme.SecondaryScale);
        var buttonY = narrow ? top + 12 + textBlock + 12 : top + ((height - CurrencyChrome.Control) / 2f);
        var x = winPos.X + width - gutter - buttonsWidth;
        for (var i = 0; i < buttons.Count; i++)
        {
            var button = buttons[i];
            if (CurrencyChrome.OutlineButton(draw, button.Id, button.Label, new Vector2(x, buttonY), new Vector2(widths[i], CurrencyChrome.Control), button.Primary))
                button.Action();
            x += widths[i] + gap;
        }
        var textY = narrow ? top + 12 : top + ((height - textBlock) / 2f);
        var textX = winPos.X + gutter + 16;
        draw.AddCircleFilled(new Vector2(winPos.X + gutter + 4, textY + (CurrencyChrome.LineHeight() / 2f)), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(UiState.NeedsSetup)));
        var room = narrow ? width - gutter - textX : winPos.X + width - gutter - buttonsWidth - 16 - textX;
        CurrencyChrome.DrawText(draw, new Vector2(textX, textY), CurrencyChrome.FitText(FooterStatus(), room), Theme.TextPrimary);
        CurrencyChrome.DrawText(draw, new Vector2(textX, textY + CurrencyChrome.LineHeight() + 4), CurrencyChrome.FitText(FooterDetail(), room, Theme.SecondaryScale),
            Theme.TextSecondary, Theme.SecondaryScale);
        ImGui.SetCursorScreenPos(new Vector2(winPos.X, top + height));
        ImGui.Dummy(Vector2.Zero);
    }

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
                    CurrencyChrome.Divider(); // Fork: separate the sub-section from the toggles above
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
            CurrencyChrome.IntField("Minimum sales", ref C.MinSales, // Fork: framed field with its label beside it
                "Minimum weekly sales for the market table; 0 disables the filter.");
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
