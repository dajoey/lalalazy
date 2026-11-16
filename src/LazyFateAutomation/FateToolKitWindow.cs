using System.Reflection;
using LazyFateAutomation.Helpers.ImGuiHelpers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using LalaUiKit;
using System.Globalization;

namespace LazyFateAutomation;

public class FateToolKitWindow : MinimisableWindow
{
    private const string LivePage = "fates.live";
    private const string SelectionPage = "settings.selection";
    private const string CurrencyPage = "settings.currency";
    private const string ZonesPage = "settings.zones";
    private const string DisplayPage = "settings.display";
    private const string BrandName = "LazyFateAutomation";

    private sealed record FatePage(string Id, string Group, string Title, string Purpose);

    private static readonly FatePage[] Pages =
    [
        new(LivePage, "FATEs", "Live FATEs", "Tracks real FATEs in the current zone and keeps the run controls close."),
        new(SelectionPage, "Settings", "FATE selection", "Chooses which FATE types the automation may start and target."),
        new(CurrencyPage, "Settings", "Currency focus", "Steers zone swaps toward the selected FATE reward currency."),
        new(ZonesPage, "Settings", "Swap zones", "Controls the real zone pool used by automatic zone rotation."),
        new(DisplayPage, "Settings", "Sorting & display", "Sets priority order, saved name formatting, and the server bar entry."),
    ];

    private static readonly (FateRule Rule, string Label)[] FateRuleLabels =
    [
        (FateRule.Normal, "Kill & boss FATEs"),
        (FateRule.Collect, "Item collection (turn-in)"),
        (FateRule.Escort, "Escort"),
        (FateRule.Defend, "Defend"),
        (FateRule.EventFate, "Seasonal event"),
        (FateRule.Chase, "Chase"),
        (FateRule.ConcertedWorks, "Firmament: Concerted Works"),
        (FateRule.Fete, "Firmament: Fete"),
    ];

    private readonly FateToolKit _tweak;
    private string _currentPage = LivePage;
    private string _lastSettingsPage = SelectionPage;
    private string _zoneFilter = "";
    private readonly Dictionary<string, (Vector2 Pos, Vector2 Size)> _buttonRects = new();

    public FateToolKitWindow(FateToolKit tweak) : base($"Fate Tracker##{nameof(FateToolKitWindow)}", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _tweak = tweak;
        Size = new Vector2(1120, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    protected override Vector2 MinimisedSize => new(700, 140);

    public override bool DrawConditions() => Player.Available;

    /// <summary>The shell draws full-bleed bands, so the window itself carries no padding.</summary>
    public override void PreDraw() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    public override void PostDraw() => ImGui.PopStyleVar();

    private FatePage Current => Pages.First(page => page.Id == _currentPage);

    protected override void DrawContent(bool minimised)
    {
        _tweak.SyncRunningState();
        Theme.PushFrame();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(Theme.ItemSpacing.X, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, FateChrome.Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, FateChrome.Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, FateChrome.Radius);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent with { W = 0.14f });
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.24f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Accent with { W = 0.30f });
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        try
        {
            if (minimised)
            {
                DrawMinimised();
                return;
            }

            var (state, action) = OverallState();
            List<(PublicEvent Fate, bool IsAvailable)>? fates = _currentPage == LivePage
                ? _tweak.GetOrderedFates().ToList()
                : null;

            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            var gutter = FateChrome.Gutter(winSize.X);

            FateChrome.FooterTopY = winPos.Y + winSize.Y - FooterHeight(winSize.X);
            var y = DrawTopBar(state, winPos, winSize.X, gutter);
            if (Current.Group == "Settings")
                y += DrawFeatureBar(y, winPos, winSize.X, gutter);
            y += DrawPageHeader(y, fates, winPos, winSize.X, gutter);

            var footerHeight = FooterHeight(winSize.X);
            DrawBody(y, winSize.X, MathF.Max(60, winSize.Y - footerHeight - y), gutter, fates);
            DrawFooter(state, action, winPos, winSize, gutter);
        }
        finally
        {
            FateChrome.ResetFrame();
            ImGui.PopStyleColor(8);
            ImGui.PopStyleVar(4);
            Theme.PopFrame();
        }
    }

    private void DrawBody(float top, float width, float height, float gutter, List<(PublicEvent Fate, bool IsAvailable)>? fates)
    {
        ImGui.SetCursorPos(new Vector2(0, top));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(gutter, 16));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8f);
        ImGui.PushStyleColor(ImGuiCol.Border, 0u);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Theme.SurfaceBorder);
        try
        {
            ImGui.BeginChild("##fate_body", new Vector2(width, height), true);
            try
            {
                FateChrome.PushRight(ImGui.GetWindowPos().X + width - gutter);
                switch (_currentPage)
                {
                    case LivePage:
                        DrawLivePage(fates!);
                        break;
                    case SelectionPage:
                        DrawFateSelectionPage();
                        break;
                    case CurrencyPage:
                        DrawCurrencyFocusPage();
                        break;
                    case ZonesPage:
                        DrawZonesPage();
                        break;
                    case DisplayPage:
                        DrawDisplayPage();
                        break;
                }
                FateChrome.PopRight();

                if (ImGui.GetScrollMaxY() > 0 && ImGui.GetScrollY() > 1)
                {
                    var topDraw = ImGui.GetWindowDrawList();
                    var topPos = ImGui.GetWindowPos();
                    var topSolid = ImGui.GetColorU32(ImGuiCol.WindowBg);
                    var topClear = topSolid & 0x00FFFFFFu;
                    topDraw.AddRectFilledMultiColor(topPos, topPos + new Vector2(ImGui.GetWindowSize().X - 8, 16),
                        topSolid, topSolid, topClear, topClear);
                }

                if (ImGui.GetScrollMaxY() > 0 && ImGui.GetScrollY() < ImGui.GetScrollMaxY() - 1)
                {
                    var childDraw = ImGui.GetWindowDrawList();
                    var childPos = ImGui.GetWindowPos();
                    var childSize = ImGui.GetWindowSize();
                    var solid = ImGui.GetColorU32(ImGuiCol.WindowBg);
                    var clear = solid & 0x00FFFFFFu;
                    childDraw.AddRectFilledMultiColor(childPos + new Vector2(0, childSize.Y - 28), childPos + childSize - new Vector2(8, 0),
                        clear, clear, solid, solid);
                }
            }
            finally
            {
                ImGui.EndChild();
            }
        }
        finally
        {
            ImGui.PopStyleColor(4);
            ImGui.PopStyleVar(4);
        }
    }

    private void ToggleSettings()
    {
        if (_currentPage == LivePage)
            _currentPage = _lastSettingsPage;
        else
            _currentPage = LivePage;
    }

    private (UiState State, string Action) OverallState()
    {
        if (!_tweak.Running)
            return (UiState.Idle, "Automation is stopped");
        if (_tweak.PendingStopWhenSafe)
            return (UiState.Running, "Stopping when the current FATE ends safely");
        return (UiState.Running, _tweak.CurrentState.Equals("Idle", StringComparison.OrdinalIgnoreCase)
            ? "Waiting for an eligible FATE"
            : FateChrome.Humanize(_tweak.CurrentState));
    }

    // ---- shell: header band, sub-navigation, page header, footer ---------------------------------------------------

    private float DrawTopBar(UiState state, Vector2 winPos, float width, float gutter)
    {
        var draw = ImGui.GetWindowDrawList();
        const float gap = 8;
        var groups = Pages.Select(page => page.Group).Distinct().ToArray();
        var navWidths = groups.Select(group => MathF.Max(72, FateChrome.TextWidth(group) + 32)).ToArray();
        var navTotal = navWidths.Sum() + (gap * (groups.Length - 1));
        var minimiseSize = new Vector2(FateChrome.Control);
        var pillSize = new Vector2(FateChrome.PillWidth(), FateChrome.Control);
        var closeSize = new Vector2(FateChrome.Control);
        var rightWidth = minimiseSize.X + gap + pillSize.X + gap + closeSize.X;
        var brandWidth = FateChrome.BrandWidth(BrandName);
        var single = width >= gutter + brandWidth + 16 + navTotal + 16 + rightWidth + gutter;
        brandWidth = MathF.Min(brandWidth, MathF.Max(120, width - (2 * gutter) - rightWidth - 16));
        var bar = single ? 68f : 14 + 40 + 12 + FateChrome.Control + 14;

        draw.AddRectFilled(winPos, winPos + new Vector2(width, bar), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, bar), winPos + new Vector2(width, bar), Theme.Pack(Theme.SurfaceBorder));

        if (FateChrome.Brand(draw, winPos + new Vector2(gutter, 14), brandWidth, BrandName))
            _currentPage = LivePage;

        var closePos = new Vector2(width - gutter - closeSize.X, 16);
        var pillPos = new Vector2(closePos.X - gap - pillSize.X, 16);
        var minimisePos = new Vector2(pillPos.X - gap - minimiseSize.X, 16);

        var navX = gutter + brandWidth + 16;
        var navY = 16f;
        if (!single)
        {
            navY = 14 + 40 + 12;
            navX = gutter;
            var share = (width - (2 * gutter) - (gap * (groups.Length - 1))) / groups.Length;
            navWidths = groups.Select(_ => share).ToArray();
        }

        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            var destination = group == "FATEs" ? LivePage : _lastSettingsPage;
            if (FateChrome.NavPill(draw, group, group, winPos + new Vector2(navX, navY), new Vector2(navWidths[i], FateChrome.Control), Current.Group == group))
                _currentPage = destination;
            navX += navWidths[i] + gap;
        }

        if (FateChrome.HeaderIconButton(draw, "##fate_header_minimise", winPos + minimisePos, minimiseSize, "Minimise", cross: false))
            ToggleMinimised();
        FateChrome.StatusPill(draw, winPos + pillPos, pillSize, state);
        if (FateChrome.CloseButton(draw, winPos + closePos, closeSize))
            IsOpen = false;
        return bar + 1;
    }

    private bool DrawSettingsButton(string id, Vector2 pos, Vector2 size)
    {
        var showSettings = _currentPage == LivePage;
        var label = showSettings ? "Settings" : "FATEs";
        var clicked = FateChrome.OutlineButton(ImGui.GetWindowDrawList(), id, label, pos, size);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(showSettings ? "Open settings" : "Return to live FATEs");
        if (clicked)
            ToggleSettings();
        return clicked;
    }

    private float DrawFeatureBar(float top, Vector2 winPos, float width, float gutter)
    {
        var draw = ImGui.GetWindowDrawList();
        var features = Pages.Where(page => page.Group == "Settings").ToArray();
        var widths = features.Select(feature => FateChrome.FeaturePillWidth(feature.Title)).ToArray();
        var inner = width - (2 * gutter);
        var fits = widths.Sum() + (12 * (features.Length - 1)) <= inner;
        var band = fits ? 12 + 32 + 12 : 12 + FateChrome.Control + 12;
        draw.AddRectFilled(winPos + new Vector2(0, top), winPos + new Vector2(width, top + band), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, top + band), winPos + new Vector2(width, top + band), Theme.Pack(Theme.SurfaceBorder));

        if (fits)
        {
            var x = gutter;
            for (var i = 0; i < features.Length; i++)
            {
                var feature = features[i];
                if (FateChrome.FeaturePill(draw, feature.Id, feature.Title, winPos + new Vector2(x, top + 12), new Vector2(widths[i], 32), _currentPage == feature.Id))
                {
                    _currentPage = feature.Id;
                    _lastSettingsPage = feature.Id;
                }
                x += widths[i] + 12;
            }
            return band + 1;
        }

        var fieldPos = winPos + new Vector2(gutter, top + 12);
        var fieldSize = new Vector2(inner, FateChrome.Control);
        if (FateChrome.OutlineButton(draw, "feature_menu", Current.Title, fieldPos, fieldSize, true))
            ImGui.OpenPopup("###FateFeatureMenu");
        FateChrome.Chevron(draw, fieldPos + new Vector2(fieldSize.X - 20, fieldSize.Y / 2f));
        ImGui.SetNextWindowPos(fieldPos + new Vector2(0, fieldSize.Y + 4), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(fieldSize.X, 0));
        FateChrome.LimitPopupHeight(fieldPos.Y + fieldSize.Y + 4);
        using var style = FateChrome.PopupStyle();
        using var popup = ImRaii.Popup("###FateFeatureMenu");
        if (popup)
        {
            foreach (var feature in features)
            {
                if (!FateChrome.PopupRow("feature_" + feature.Id, feature.Title, _currentPage == feature.Id))
                    continue;
                _currentPage = feature.Id;
                _lastSettingsPage = feature.Id;
                ImGui.CloseCurrentPopup();
            }
        }
        return band + 1;
    }

    private float DrawPageHeader(float top, List<(PublicEvent Fate, bool IsAvailable)>? fates, Vector2 winPos, float width, float gutter)
    {
        var page = Current;
        var count = LiveCount(fates);
        var subtext = LiveSubtext();
        var draw = ImGui.GetWindowDrawList();
        const float tile = 44;
        var wide = width >= 760;
        var textX = gutter + 60;
        var originY = top + 20;
        var origin = winPos + new Vector2(gutter, originY);
        draw.AddRectFilled(origin, origin + new Vector2(tile), Theme.Pack(Theme.Accent with { W = 0.14f }), FateChrome.Radius);
        FateChrome.Diamond(draw, origin + new Vector2(tile / 2f), 10, Theme.Pack(Theme.Accent));

        var titleHeight = FateChrome.LineHeight(1.35f);
        var descHeight = FateChrome.LineHeight(Theme.SecondaryScale);
        var metaHeight = descHeight;
        float block;
        if (wide)
        {
            var metaMax = width * 0.4f;
            var countText = FateChrome.Fit(count, metaMax, Theme.SecondaryScale);
            var subText = FateChrome.Fit(subtext, metaMax, Theme.SecondaryScale);
            var metaWidth = MathF.Max(FateChrome.TextWidth(countText, Theme.SecondaryScale), FateChrome.TextWidth(subText, Theme.SecondaryScale));
            var textWidth = width - gutter - textX - metaWidth - 16;
            FateChrome.TextWithTip(draw, "page_title", winPos + new Vector2(textX, originY), page.Title, textWidth, Theme.TextPrimary, 1.35f);
            var descTop = originY + titleHeight + 4;
            FateChrome.TextWithTip(draw, "page_purpose", winPos + new Vector2(textX, descTop), page.Purpose, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
            var right = winPos.X + width - gutter;
            FateChrome.TextRight(draw, right, winPos.Y + originY + ((titleHeight - metaHeight) / 2f), countText, Theme.TextSecondary, Theme.SecondaryScale);
            FateChrome.TextRight(draw, right, winPos.Y + descTop, subText, Theme.TextSecondary, Theme.SecondaryScale);
            block = titleHeight + 4 + descHeight;
        }
        else
        {
            var textWidth = width - gutter - textX;
            FateChrome.TextWithTip(draw, "page_title", winPos + new Vector2(textX, originY), page.Title, textWidth, Theme.TextPrimary, 1.35f);
            var descTop = originY + titleHeight + 4;
            FateChrome.Wrapped(draw, winPos + new Vector2(textX, descTop), page.Purpose, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
            var descBlock = FateChrome.WrappedHeight(page.Purpose, textWidth, Theme.SecondaryScale);
            var metaTop = descTop + descBlock + 4;
            FateChrome.TextWithTip(draw, "page_meta", winPos + new Vector2(textX, metaTop), $"{count} · {subtext}", textWidth, Theme.TextSecondary, Theme.SecondaryScale);
            block = titleHeight + 4 + descBlock + 4 + metaHeight;
        }
        return 20 + MathF.Max(tile, block) + 8;
    }

    private string LiveCount(List<(PublicEvent Fate, bool IsAvailable)>? fates)
    {
        if (_currentPage == LivePage)
            return $"{fates!.Count} tracked · {fates.Count(row => row.IsAvailable)} eligible";
        if (_currentPage == SelectionPage)
            return $"{FateRuleLabels.Count(row => !_tweak.Config.ExcludedFateRules.Contains(row.Rule))} of {FateRuleLabels.Length} types enabled";
        if (_currentPage == CurrencyPage)
            return _tweak.Config.CurrencyFocus == FateCurrency.None ? "No currency focus" : FateToolKit.CurrencyName(_tweak.Config.CurrencyFocus);
        if (_currentPage == ZonesPage)
        {
            var (allowed, total) = ZonePoolCounts();
            return $"{allowed} of {total} zones allowed";
        }
        return $"{_tweak.Config.SortOrder.Count} sort rules";
    }

    private string LiveSubtext()
    {
        if (_currentPage == LivePage)
            return $"{_tweak.GetCurrentMode().DisplayName} mode · {FateChrome.Humanize(_tweak.CurrentState)}";
        if (_currentPage == SelectionPage)
            return $"Forlorn priority {(_tweak.Config.PrioritizeForlornMaidens ? "on" : "off")}";
        if (_currentPage == CurrencyPage)
            return _tweak.Config.CurrencyFocus == FateCurrency.None ? "Normal zone selection" : CurrencyFallbackLabel(_tweak.Config.CurrencyFocusFallback);
        if (_currentPage == ZonesPage)
            return ZoneSourceLabel();
        return $"Server bar {(_tweak.Config.ShowServerBarEntry ? "on" : "off")}";
    }

    private static float FooterTextBlock() => FateChrome.LineHeight() + 4 + FateChrome.LineHeight(Theme.SecondaryScale);

    private static float FooterHeight(float width)
    {
        var block = FooterTextBlock();
        return width < 620 ? 12 + block + 12 + FateChrome.Control + 12 : 12 + MathF.Max(block, FateChrome.Control) + 12;
    }

    private Vector2 RunButtonSize() => new(FateChrome.ActionWidth(96, "Start", "Stop", "Stopping"), FateChrome.Control);

    private void DrawFooter(UiState state, string action, Vector2 winPos, Vector2 winSize, float gutter)
    {
        var width = winSize.X;
        var height = FooterHeight(width);
        var top = winSize.Y - height;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(winPos + new Vector2(0, top), winPos + winSize, false);
        draw.AddRectFilled(winPos + new Vector2(0, top), winPos + winSize, Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, top), winPos + new Vector2(width, top), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        var narrow = width < 620;
        var buttonSize = RunButtonSize();
        if (narrow)
            buttonSize.X = width - (2 * gutter);
        var block = FooterTextBlock();
        var buttonPos = narrow
            ? new Vector2(gutter, top + 12 + block + 12)
            : new Vector2(width - gutter - buttonSize.X, top + ((height - FateChrome.Control) / 2f));
        var textX = gutter + 16;
        var textRight = narrow ? width - gutter : buttonPos.X - 16;
        var textTop = narrow ? top + 12 : top + ((height - block) / 2f);
        var lineHeight = FateChrome.LineHeight();
        var primary = $"{Copy.Word(state)} · {action}";
        var secondary = _tweak.Running
            ? $"Mode: {_tweak.GetCurrentMode().DisplayName} · {_tweak.CompletedCount} completed"
            : $"Mode: {_tweak.GetCurrentMode().DisplayName} · {_tweak.Config.SortOrder.Count} sort rules";
        draw.AddCircleFilled(winPos + new Vector2(gutter + 4, textTop + (lineHeight / 2f)), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(state)));
        FateChrome.TextWithTip(draw, "footer_primary", winPos + new Vector2(textX, textTop), primary, textRight - textX, Theme.TextPrimary);
        FateChrome.TextWithTip(draw, "footer_secondary", winPos + new Vector2(textX, textTop + lineHeight + 4), secondary, textRight - textX, Theme.TextSecondary, Theme.SecondaryScale);
        DrawRunButton("footer_primary_button", winPos + buttonPos, buttonSize, false);
    }

    // ---- live page --------------------------------------------------------------------------------------------------

    private void DrawLivePage(List<(PublicEvent Fate, bool IsAvailable)> fates)
    {
        var eligible = fates.Count(row => row.IsAvailable);
        var target = _tweak.RunUntilCompleted is { } runTarget && runTarget > 0 ? runTarget : (int?)null;
        FateChrome.StatCards(
            new FateChrome.Stat("Tracked FATEs", fates.Count.ToString(), fates.Count == 0 ? "No FATEs in this zone" : "All visible FATEs"),
            new FateChrome.Stat("Eligible now", eligible.ToString(), eligible == 0 ? "None currently eligible" : "Pass every automation rule",
                eligible / (float)Math.Max(1, fates.Count), $"/ {fates.Count}"),
            new FateChrome.Stat("Completed this run", _tweak.CompletedCount.ToString(),
                _tweak.RemainingUntilCompleted is { } remaining ? $"{remaining} remaining" : "No completion target",
                target is { } t ? _tweak.CompletedCount / (float)t : null, target is { } cap ? $"/ {cap}" : null));

        DrawRunSetup();

        if (fates.Count == 0)
        {
            FateChrome.Notice("No FATEs in this zone", "Move to a zone with active FATEs or wait for one to spawn.", UiState.Empty);
            return;
        }

        FateChrome.Card("fates_in_zone", () =>
        {
            FateChrome.Section("FATEs in this zone", $"{eligible} eligible of {fates.Count}");
            FateChrome.Paragraph("Click a FATE to pathfind there, or click again while navigation is active to stop. Right-click toggles its blacklist entry; hover for eligibility details.");
            FateChrome.Space(12);
            for (var i = 0; i < fates.Count; i++)
                DrawFateCard(fates[i].Fate, fates[i].IsAvailable, i == fates.Count - 1);
        });
    }

    private void DrawRunSetup()
    {
        FateChrome.Card("run_setup", () =>
        {
            FateChrome.Section("Run setup", _tweak.Running ? Service.Automation.Status : "Automation stopped");
            FateChrome.Paragraph($"{_tweak.CompletedCount} FATEs completed this run. {ModeProgressText()}");
            FateChrome.Space(12);
            DrawRunControls("run");
        });
    }

    private string ModeProgressText()
    {
        var modeRemaining = _tweak.GetCurrentMode().GetRemainingDisplay(_tweak);
        if (!string.IsNullOrEmpty(modeRemaining))
            return $"Mode progress: {modeRemaining.Replace("fates", "FATEs")}.";
        return _tweak.RemainingUntilCompleted is { } remaining
            ? $"Completion target remaining: {remaining}."
            : "No completion target is set.";
    }

    private float ModeButtonWidth()
        => FateGrindModes.All.Max(mode => FateChrome.TextWidth($"Mode: {mode.DisplayName}")) + 32;

    private void DrawRunControls(string id)
    {
        var width = FateChrome.Avail();
        var origin = ImGui.GetCursorScreenPos();
        const float gap = 8;
        var runSize = RunButtonSize();
        var zoneSize = new Vector2(FateChrome.ActionWidth(96, "Swap zones"), FateChrome.Control);
        var modeSize = new Vector2(MathF.Min(ModeButtonWidth(), width), FateChrome.Control);
        var modeLabel = $"Mode: {_tweak.GetCurrentMode().DisplayName}";
        Vector2 modePos;
        Vector2 zonePos;
        float height;
        if (runSize.X + modeSize.X + zoneSize.X + (2 * gap) <= width)
        {
            modePos = origin + new Vector2(runSize.X + gap, 0);
            zonePos = modePos + new Vector2(modeSize.X + gap, 0);
            height = FateChrome.Control;
        }
        else
        {
            modePos = origin + new Vector2(0, FateChrome.Control + gap);
            zonePos = modePos + new Vector2(modeSize.X + gap, 0);
            height = (2 * FateChrome.Control) + gap;
            if (zonePos.X + zoneSize.X > origin.X + width)
            {
                zonePos = origin + new Vector2(0, 2 * (FateChrome.Control + gap));
                height = (3 * FateChrome.Control) + (2 * gap);
            }
        }

        DrawRunButton(id + "_primary", origin, runSize, true);
        DrawModeButton(id + "_mode", modePos, modeSize, modeLabel);
        DrawZoneButton(id + "_zones", zonePos, zoneSize);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    private void DrawRunButton(string id, Vector2 pos, Vector2 size, bool accent)
    {
        var draw = ImGui.GetWindowDrawList();
        var clicked = _tweak.Running
            ? FateChrome.StopButton(draw, id, _tweak.PendingStopWhenSafe ? "Stopping" : "Stop", pos, size, _tweak.PendingStopWhenSafe)
            : FateChrome.OutlineButton(draw, id, "Start", pos, size, accent);
        var hovered = ImGui.IsItemHovered();
        if (!clicked)
        {
            if (hovered)
                ImGui.SetTooltip(_tweak.Running ? "Click to stop now. Ctrl+click to stop after the current FATE ends safely." : "Start FATE automation.");
            return;
        }

        if (_tweak.Running)
        {
            if (ImGui.GetIO().KeyCtrl)
            {
                _tweak.PendingStopWhenSafe = true;
            }
            else
            {
                _tweak.ToggleRunning();
                Service.Navmesh.Stop();
            }
        }
        else
        {
            _tweak.ToggleRunning();
        }
    }

    private void DrawModeButton(string id, Vector2 pos, Vector2 size, string label)
    {
        if (FateChrome.OutlineButton(ImGui.GetWindowDrawList(), id, label, pos, size))
            ImGui.OpenPopup("###GrindModePopup");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Grind mode: {_tweak.GetCurrentMode().DisplayName}");

        ImGui.SetNextWindowPos(new Vector2(pos.X, pos.Y + size.Y + 4), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(FateChrome.PopupWidth(FateGrindModes.All.Select(mode => mode.DisplayName), MathF.Max(size.X, 240)), 0));
        FateChrome.LimitPopupHeight(pos.Y + size.Y + 4);
        using var style = FateChrome.PopupStyle();
        using var popup = ImRaii.Popup("###GrindModePopup");
        if (!popup)
            return;
        FateChrome.PopupTitle("Grind mode");
        foreach (var mode in FateGrindModes.All)
        {
            if (!FateChrome.PopupRow("mode_" + mode.DisplayName, mode.DisplayName, mode.DisplayName == _tweak.SelectedModeId))
                continue;
            _tweak.SelectedModeId = mode.DisplayName;
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawZoneButton(string id, Vector2 pos, Vector2 size)
    {
        using var disabled = ImRaii.Disabled(_tweak.ModeSuppliesSwapZones);
        if (FateChrome.OutlineButton(ImGui.GetWindowDrawList(), id, "Swap zones", pos, size, _tweak.HasSelectedSwapZones, _tweak.ModeSuppliesSwapZones))
            _tweak.OpenZoneSelector();
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            return;
        if (_tweak.ModeSuppliesSwapZones)
            ImGui.SetTooltip("Zone list is defined by the current grind mode. Switch to None to select zones manually.");
        else if (_tweak.HasSelectedSwapZones)
            ImGui.SetTooltip($"Swap zones: {_tweak.SelectedSwapZones.Count}");
        else
            ImGui.SetTooltip("Swap zones (uses default swap behaviour if none selected)");
    }

    private void DrawMinimised()
    {
        var (state, action) = OverallState();
        var winPos = ImGui.GetWindowPos();
        var width = ImGui.GetWindowWidth();
        var gutter = FateChrome.Gutter(width);
        var draw = ImGui.GetWindowDrawList();
        const float gap = 8;
        const float bar = 56;
        var settingsSize = new Vector2(FateChrome.ActionWidth(96, "Settings"), FateChrome.Control);
        var restoreSize = new Vector2(FateChrome.ActionWidth(96, "Restore"), FateChrome.Control);
        var pillSize = new Vector2(FateChrome.PillWidth(), FateChrome.Control);
        var closeSize = new Vector2(FateChrome.Control);
        var rightWidth = settingsSize.X + gap + restoreSize.X + gap + pillSize.X + gap + closeSize.X;
        var brandWidth = MathF.Min(FateChrome.BrandWidth(BrandName), MathF.Max(120, width - (2 * gutter) - rightWidth - 16));

        draw.AddRectFilled(winPos, winPos + new Vector2(width, bar), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, bar), winPos + new Vector2(width, bar), Theme.Pack(Theme.SurfaceBorder));
        if (FateChrome.Brand(draw, winPos + new Vector2(gutter, 8), brandWidth, BrandName))
            _currentPage = LivePage;

        var closePos = new Vector2(width - gutter - closeSize.X, 10);
        var pillPos = new Vector2(closePos.X - gap - pillSize.X, 10);
        var restorePos = new Vector2(pillPos.X - gap - restoreSize.X, 10);
        var settingsPos = new Vector2(restorePos.X - gap - settingsSize.X, 10);
        if (DrawSettingsButton("compact_settings", winPos + settingsPos, settingsSize) && Minimised)
            ToggleMinimised();
        if (FateChrome.OutlineButton(draw, "compact_restore", "Restore", winPos + restorePos, restoreSize))
            ToggleMinimised();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(ExpandTooltipText);
        FateChrome.StatusPill(draw, winPos + pillPos, pillSize, state);
        if (FateChrome.CloseButton(draw, winPos + closePos, closeSize))
            IsOpen = false;

        var rowY = bar + 12;
        var runSize = RunButtonSize();
        var zoneSize = new Vector2(FateChrome.ActionWidth(96, "Swap zones"), FateChrome.Control);
        var completed = $"{_tweak.CompletedCount} completed";
        var completedWidth = FateChrome.TextWidth(completed) + 8;
        var modeWidth = MathF.Max(120, MathF.Min(ModeButtonWidth(), width - (2 * gutter) - runSize.X - completedWidth - zoneSize.X - (3 * gap)));
        var x = gutter;
        DrawRunButton("compact_primary", winPos + new Vector2(x, rowY), runSize, true);
        x += runSize.X + gap;
        FateChrome.Text(draw, winPos + new Vector2(x + 4, rowY + ((FateChrome.Control - FateChrome.LineHeight()) / 2f)), completed, Theme.TextSecondary);
        x += completedWidth + gap;
        DrawModeButton("compact_mode", winPos + new Vector2(x, rowY), new Vector2(modeWidth, FateChrome.Control), $"Mode: {_tweak.GetCurrentMode().DisplayName}");
        x += modeWidth + gap;
        DrawZoneButton("compact_zones", winPos + new Vector2(x, rowY), zoneSize);

        var statusY = rowY + FateChrome.Control + 10;
        var statusHeight = FateChrome.LineHeight(Theme.SecondaryScale);
        draw.AddCircleFilled(winPos + new Vector2(gutter + 4, statusY + (statusHeight / 2f)), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(state)));
        FateChrome.TextWithTip(draw, "compact_status", winPos + new Vector2(gutter + 16, statusY), $"{Copy.Word(state)} · {action}",
            width - (2 * gutter) - 16, Theme.TextSecondary, Theme.SecondaryScale);
        ImGui.SetCursorPos(Vector2.Zero);
        ImGui.Dummy(new Vector2(width, statusY + statusHeight + 8));
    }

    private void DrawFateCard(PublicEvent fate, bool isAvailable, bool last)
    {
        FateChrome.Card($"fate_{fate.Id}", () =>
        {
            var origin = ImGui.GetCursorScreenPos();
            var width = FateChrome.Avail();
            var draw = ImGui.GetWindowDrawList();
            var displayName = FormatDisplayName(fate);
            var isBlacklisted = _tweak.IsBlacklisted(fate);
            var automated = isAvailable && !isBlacklisted;
            var rowState = automated ? UiState.Ready : UiState.Blocked;
            var stateLabel = Copy.Word(rowState);
            var nameHeight = FateChrome.LineHeight();
            var metaHeight = FateChrome.LineHeight(Theme.SecondaryScale);
            var rowHeight = nameHeight + 4 + metaHeight + 8 + Theme.BarHeight;

            ImGui.SetCursorScreenPos(origin - new Vector2(FateChrome.PadX, FateChrome.RowPadY));
            ImGui.InvisibleButton("##fate_open", new Vector2(width + (2 * FateChrome.PadX), rowHeight + (2 * FateChrome.RowPadY)));
            var hovered = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                if (Svc.Navmesh.IsRunning())
                    Svc.Navmesh.Stop();
                else
                    Svc.Navmesh.PathfindAndMoveTo(fate.Position.RandomPoint(fate.Radius * 0.5f).OnMesh(), Svc.Condition[ConditionFlag.InFlight]);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                _tweak.ToggleBlacklist(fate);
            if (hovered)
                DrawFateTooltip(fate, displayName, isBlacklisted);

            var textLeft = origin.X + 26;
            FateChrome.Diamond(draw, new Vector2(origin.X + 6, origin.Y + (nameHeight / 2f)), 5,
                Theme.Pack(automated ? Theme.Accent : Theme.Accent with { W = 0.4f }));
            var stateWidth = FateChrome.TextWidth(stateLabel);
            FateChrome.Text(draw, new Vector2(textLeft, origin.Y),
                FateChrome.Fit(displayName, width - 26 - stateWidth - 12), automated ? Theme.TextPrimary : Theme.TextSecondary);
            FateChrome.TextRight(draw, origin.X + width, origin.Y, stateLabel, Copy.MarkColor(rowState));

            var time = FormatTimeRemaining(fate.TimeRemaining);
            var distance = Player.Available ? Player.DistanceTo(fate.Position).ToString("F1") + " y" : "?";
            var bonus = fate.HasBonus ? " · Bonus" : "";
            var metadata = $"{time} · {distance} · {fate.State}{bonus}";
            var progressLabel = $"{fate.Progress}%";
            var metaTop = origin.Y + nameHeight + 4;
            FateChrome.Text(draw, new Vector2(textLeft, metaTop),
                FateChrome.Fit(metadata, width - 26 - FateChrome.TextWidth(progressLabel, Theme.SecondaryScale) - 12, Theme.SecondaryScale),
                Theme.TextSecondary, Theme.SecondaryScale);
            FateChrome.TextRight(draw, origin.X + width, metaTop, progressLabel, Theme.TextSecondary, Theme.SecondaryScale);

            var fraction = Math.Clamp(fate.Progress / 100f, 0f, 1f);
            var barMin = new Vector2(textLeft, metaTop + metaHeight + 8);
            var barMax = new Vector2(origin.X + width, barMin.Y + Theme.BarHeight);
            draw.AddRectFilled(barMin, barMax, Theme.Pack(Theme.SurfaceBorder), 2f);
            if (fraction > 0)
            {
                var barColour = FateChrome.ProgressColour(_tweak.Config.BarColour);
                var fill = automated ? barColour : barColour with { W = 0.45f };
                draw.AddRectFilled(barMin, new Vector2(barMin.X + ((barMax.X - barMin.X) * fraction), barMax.Y), Theme.Pack(fill), 2f);
            }
        }, row: true, gap: last ? 0 : 8, bottomOverlap: FateChrome.RowPadY);
    }

    private static readonly PropertyInfo[] TooltipProperties = [.. typeof(PublicEvent)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .OrderBy(p => p.Name)];

    private void DrawFateTooltip(PublicEvent fate, string displayName, bool isBlacklisted)
    {
        const float tipWidth = 320;
        const float pad = 12;
        var inner = tipWidth - (2 * pad);
        var (isEligible, failedConditions) = _tweak.GetFateConditionDetails(fate);
        var automated = isEligible && !isBlacklisted;

        var details = new List<(string Label, string Value)>();
        void Add(string label, Func<string> value)
        {
            try
            {
                details.Add((label, value()));
            }
            catch
            {
                // a property that cannot be read right now is left out of the tooltip
            }
        }

        foreach (var prop in TooltipProperties)
        {
            var name = prop.Name;
            Add(FateChrome.Humanize(name), () =>
            {
                var raw = prop.GetValue(fate);
                return raw switch
                {
                    null => "?",
                    float f when name == nameof(PublicEvent.TimeRemaining) => FormatTimeRemaining(f),
                    Vector3 v when name == nameof(PublicEvent.Position) => v.ToString(),
                    bool b => b ? "True" : "False",
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    _ => raw.ToString() ?? "?",
                };
            });
        }
        Add("Blacklist", () => isBlacklisted ? "Yes" : "No");

        var line = FateChrome.LineHeight();
        var small = FateChrome.LineHeight(Theme.SecondaryScale);
        var nameHeight = FateChrome.WrappedHeight(displayName, inner);
        var reasonHeights = failedConditions.Select(reason => FateChrome.WrappedHeight(reason, inner - 14, Theme.SecondaryScale)).ToList();
        var total = nameHeight + 8 + line
            + (reasonHeights.Count > 0 ? 4 + reasonHeights.Sum() + (4 * (reasonHeights.Count - 1)) : 0)
            + 12 + 1 + 12 + (details.Count * small) + (4 * (details.Count - 1));

        var mouse = ImGui.GetMousePos();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var pos = mouse + new Vector2(18, 18);
        var viewportRight = ImGui.GetMainViewport().Pos.X + ImGui.GetMainViewport().Size.X;
        if (pos.X + tipWidth + 8 > viewportRight)
            pos.X = mouse.X - tipWidth - 12;
        var bottomLimit = windowPos.Y + windowSize.Y - 8;
        if (pos.Y + total + (2 * pad) > bottomLimit)
            pos.Y = MathF.Max(windowPos.Y + 8, bottomLimit - total - (2 * pad));

        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.TextSecondary with { W = 0.4f });
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(pad, pad));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, FateChrome.Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.BeginTooltip();
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var y = origin.Y;
        FateChrome.Wrapped(draw, new Vector2(origin.X, y), displayName, inner, Theme.TextPrimary);
        y += nameHeight + 8;
        var mark = automated ? Theme.StateGreen : Theme.StateAmber;
        draw.AddCircleFilled(new Vector2(origin.X + Theme.DotRadius, y + (line / 2f)), Theme.DotRadius, Theme.Pack(mark));
        FateChrome.Text(draw, new Vector2(origin.X + 14, y), automated ? "Will be automated" : "Blocked", mark);
        y += line;
        if (reasonHeights.Count > 0)
        {
            y += 4;
            for (var i = 0; i < failedConditions.Count; i++)
            {
                FateChrome.Wrapped(draw, new Vector2(origin.X + 14, y), failedConditions[i], inner - 14, Theme.TextPrimary, Theme.SecondaryScale);
                y += reasonHeights[i] + 4;
            }
            y -= 4;
        }
        y += 12;
        draw.AddLine(new Vector2(origin.X, y), new Vector2(origin.X + inner, y), Theme.Pack(Theme.SurfaceBorder));
        y += 13;
        foreach (var (label, value) in details)
        {
            FateChrome.Text(draw, new Vector2(origin.X, y), label, Theme.TextSecondary, Theme.SecondaryScale);
            FateChrome.TextRight(draw, origin.X + inner, y, FateChrome.Fit(value, inner * 0.6f, Theme.SecondaryScale), Theme.TextPrimary, Theme.SecondaryScale);
            y += small + 4;
        }
        ImGui.Dummy(new Vector2(inner, total));
        ImGui.EndTooltip();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    // ---- settings pages -------------------------------------------------------------------------------------------

    private void DrawFateSelectionPage()
    {
        FateChrome.Columns("selection",
            () => FateChrome.Card("allowed_fate_types", () =>
            {
                FateChrome.Section("Allowed FATE types", $"{FateRuleLabels.Count(row => !_tweak.Config.ExcludedFateRules.Contains(row.Rule))}/{FateRuleLabels.Length} enabled");
                FateChrome.Paragraph("Disabled FATE types are never started. Boss and trash-kill FATEs share the game's kill & boss rule; Bozja, Occult Crescent skirmishes, and cosmic exploration events use it too.");
                FateChrome.Space(12);
                var excluded = _tweak.Config.ExcludedFateRules;
                for (var i = 0; i < FateRuleLabels.Length; i++)
                {
                    var (rule, label) = FateRuleLabels[i];
                    var allowed = !excluded.Contains(rule);
                    if (!FateChrome.Toggle($"rule_{rule}", label, ref allowed, separator: i > 0))
                        continue;
                    if (allowed)
                        excluded.Remove(rule);
                    else
                        excluded.Add(rule);
                    _tweak.Config.Save();
                }
            }, fill: true),
            () =>
            {
                FateChrome.Card("target_priority", () =>
                {
                    FateChrome.Section("Target priority");
                    Setting("prioritize_forlorn", "Prioritize Forlorn Maidens",
                        () => _tweak.Config.PrioritizeForlornMaidens,
                        value => _tweak.Config.PrioritizeForlornMaidens = value,
                        "When a Forlorn Maiden or the Forlorn appears mid-FATE, combat targets it instead of the FATE's normal enemies until it is defeated or disappears.");
                });
                FateChrome.Card("selection_behavior", () =>
                {
                    FateChrome.Section("Selection behavior");
                    FateChrome.Paragraph("Eligibility still uses duration, progress, time remaining, blacklist, pending state, and player level. This page changes only which eligible types may run.");
                });
            });
    }

    private void ComboField<T>(string id, string preview, IEnumerable<T> options, Func<T, string> label, T current, Action<T> select, float height = FateChrome.Control)
    {
        using var style = FateChrome.FieldStyle(height);
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = FateChrome.Avail();
        ImGui.SetNextItemWidth(width);
        using var combo = ImRaii.Combo(id, FateChrome.Fit(preview, width - 32 - 24), ImGuiComboFlags.NoArrowButton);
        FateChrome.Chevron(draw, pos + new Vector2(width - 20, height / 2f));
        if (!combo)
            return;
        var index = 0;
        foreach (var option in options)
        {
            if (FateChrome.PopupRow($"{id}_{index++}", label(option), EqualityComparer<T>.Default.Equals(option, current)))
            {
                select(option);
                ImGui.CloseCurrentPopup();
            }
        }
    }

    private void DrawCurrencyFocusPage()
    {
        FateChrome.Columns("currency",
            () => FateChrome.Card("currency_settings", () =>
            {
                FateChrome.Section("Reward currency");
                FateChrome.Paragraph("Zone swaps prefer zones whose FATEs reward the focused currency. A grind mode with its own zone list still wins.");
                FateChrome.Space(16);
                FateChrome.Line("Focus currency");
                FateChrome.Space(8);
                var focus = _tweak.Config.CurrencyFocus;
                ComboField("###CurrencyFocus", FateToolKit.CurrencyName(focus), Enum.GetValues<FateCurrency>(), FateToolKit.CurrencyName, focus, currency =>
                {
                    if (focus == currency)
                        return;
                    _tweak.Config.CurrencyFocus = currency;
                    _tweak.Config.Save();
                });
                FateChrome.Space(8);
                FateChrome.Paragraph("Company Seals: A Realm Reborn, Heavensward, and Stormblood zones. Bicolor Gemstones: Shadowbringers and later zones. Yo-kai Medals: event zones with the watch equipped and matching minion out.");

                focus = _tweak.Config.CurrencyFocus;
                if (focus != FateCurrency.None)
                {
                    FateChrome.Divider();
                    FateChrome.Line("When focused zones have nothing to run");
                    FateChrome.Space(8);
                    var fallback = _tweak.Config.CurrencyFocusFallback;
                    ComboField("###CurrencyFocusFallback", CurrencyFallbackLabel(fallback), Enum.GetValues<CurrencyFocusFallback>(), CurrencyFallbackLabel, fallback, option =>
                    {
                        if (fallback == option)
                            return;
                        _tweak.Config.CurrencyFocusFallback = option;
                        _tweak.Config.Save();
                    });
                    FateChrome.Space(8);
                    FateChrome.Paragraph("Zone swaps first pass through every focused zone. Continue normally returns to the usual selection after a full pass; Idle in zone waits for focused-currency FATEs.");
                }
            }, fill: true),
            () => FateChrome.Card("currency_state", () =>
            {
                FateChrome.Section("Current rotation");
                FateChrome.Line(FateToolKit.CurrencyName(_tweak.Config.CurrencyFocus));
                FateChrome.Space(4);
                FateChrome.Paragraph(_tweak.Config.CurrencyFocus == FateCurrency.None
                    ? "Normal zone selection remains active."
                    : $"{CurrencyFallbackLabel(_tweak.Config.CurrencyFocusFallback)} after the focused pool is exhausted.");
                FateChrome.Divider();
                FateChrome.Line("Zone source", Theme.TextSecondary, Theme.SecondaryScale);
                FateChrome.Space(4);
                FateChrome.Paragraph(ZoneSourceLabel(), Theme.TextPrimary, 1f);
            }, fill: true));
    }

    private static string CurrencyFallbackLabel(CurrencyFocusFallback fallback) => fallback switch
    {
        CurrencyFocusFallback.Idle => "Idle in zone",
        _ => "Continue normally",
    };

    private void DrawZonesPage()
    {
        FateChrome.Card("zone_controls", () =>
        {
            FateChrome.Section("Zone pool", ZoneSourceLabel());
            FateChrome.Paragraph(ZoneHelpText());
            FateChrome.Space(12);
            using (FateChrome.FieldStyle(FateChrome.Control))
            {
                ImGui.SetNextItemWidth(FateChrome.Avail());
                ImGui.InputTextWithHint("###ZoneFilter", "Filter zones...", ref _zoneFilter, 64);
            }
            FateChrome.Space(12);

            var rawPool = _tweak.GetRawSwapZones();
            var buttons = new List<ButtonAction>();
            if (rawPool is { Count: > 0 } || _tweak.HasSelectedSwapZones)
            {
                buttons.Add(new ButtonAction("zones_select_all", "Select All", () =>
                {
                    _tweak.Config.ExcludedSwapZones.Clear();
                    if (rawPool == null && _tweak.HasSelectedSwapZones)
                        foreach (var zone in FateZones.All.Where(z => z.ZoneClass == FateZoneLogic.ZoneClass.FateZone))
                            _tweak.Config.SelectedSwapZones.Add(zone.TerritoryId);
                    _tweak.Config.Save();
                }));
                buttons.Add(new ButtonAction("zones_clear_all", "Clear All", () =>
                {
                    if (rawPool != null)
                        foreach (var zoneId in rawPool)
                            _tweak.Config.ExcludedSwapZones.Add(zoneId);
                    else
                        foreach (var zone in FateZones.All.Where(z => z.ZoneClass == FateZoneLogic.ZoneClass.FateZone))
                        {
                            _tweak.Config.SelectedSwapZones.Add(zone.TerritoryId);
                            _tweak.Config.ExcludedSwapZones.Add(zone.TerritoryId);
                        }
                    _tweak.Config.Save();
                }));
                if (!_tweak.ManualZonesOverridden)
                    buttons.Add(new ButtonAction("zones_edit", "Edit Selected Zones...", _tweak.OpenZoneSelector));
            }
            else if (!_tweak.ManualZonesOverridden)
            {
                buttons.Add(new ButtonAction("zones_choose", "Select Allowed Swap Zones...", _tweak.OpenZoneSelector));
            }
            if (buttons.Count > 0)
                DrawButtonRow(buttons.ToArray());
        });

        var anyGroup = false;
        foreach (var group in FateZones.ByExpansion)
        {
            var zones = group.Where(zone => ZoneNameMatches(zone.Name)).ToList();
            if (zones.Count == 0)
                continue;
            anyGroup = true;
            var allowedCount = zones.Count(zone => IsZoneChecked(zone.TerritoryId));
            FateChrome.Card($"expansion_{group.Key.Id}", () =>
            {
                FateChrome.Section(group.Key.Name, $"{allowedCount}/{zones.Count} allowed");
                DrawButtonRow(
                    new ButtonAction($"exp_all_{group.Key.Id}", "All", () => SetZonesChecked(zones, true)),
                    new ButtonAction($"exp_none_{group.Key.Id}", "None", () => SetZonesChecked(zones, false)));
                var noTeleport = zones.Count(zone => zone.ZoneClass != FateZoneLogic.ZoneClass.FateZone);
                if (noTeleport > 0)
                {
                    FateChrome.Space(8);
                    FateChrome.Paragraph($"{noTeleport} without a teleport aetheryte");
                }
                FateChrome.Space(8);
                DrawZoneToggles(zones);
            });
        }
        if (!anyGroup)
            FateChrome.Notice("No matching zones", $"Nothing matches \"{_zoneFilter}\". Clear the filter to restore the full zone list.", UiState.Empty);
    }

    private void DrawZoneToggles(List<FateZone> zones)
    {
        const float gap = 16;
        var width = FateChrome.Avail();
        var columns = Math.Max(1, (int)((width + gap) / (240 + gap)));
        var cell = (width - ((columns - 1) * gap)) / columns;
        var origin = ImGui.GetCursorScreenPos();
        float y = 0;
        for (var start = 0; start < zones.Count; start += columns)
        {
            var count = Math.Min(columns, zones.Count - start);
            var rowHeight = 0f;
            for (var c = 0; c < count; c++)
                rowHeight = MathF.Max(rowHeight, FateChrome.ToggleHeight(zones[start + c].Name, null, cell, compact: true));
            for (var c = 0; c < count; c++)
            {
                var zone = zones[start + c];
                var left = origin.X + (c * (cell + gap));
                ImGui.SetCursorScreenPos(new Vector2(left, origin.Y + y));
                FateChrome.PushRight(left + cell);
                var isChecked = IsZoneChecked(zone.TerritoryId);
                var tooltip = zone.ZoneClass == FateZoneLogic.ZoneClass.NoTeleportFateZone
                    ? "No aetheryte to teleport into this zone; it is never picked by automatic zone swaps."
                    : null;
                if (FateChrome.Toggle($"zone_{zone.TerritoryId}", zone.Name, ref isChecked, tooltip: tooltip, compact: true))
                    SetZonesChecked([zone], isChecked);
                FateChrome.PopRight();
            }
            y += rowHeight;
        }
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, y));
    }

    private string ZoneSourceLabel()
    {
        if (_tweak.ModeSuppliesSwapZones)
            return $"Mode: {_tweak.GetCurrentMode().DisplayName}";
        if (_tweak.Config.CurrencyFocus != FateCurrency.None && FateZones.CurrencyZones(_tweak.Config.CurrencyFocus).Count > 0)
            return $"Currency: {FateToolKit.CurrencyName(_tweak.Config.CurrencyFocus)}";
        if (_tweak.HasSelectedSwapZones)
            return $"Manual: {_tweak.SelectedSwapZones.Count} selected";
        return "Default achievement-guided rotation";
    }

    private string ZoneHelpText()
    {
        if (_tweak.ModeSuppliesSwapZones)
            return $"Grind mode '{_tweak.GetCurrentMode().DisplayName}' defines the zone list. Disable zones to exclude them from rotation.";
        if (_tweak.Config.CurrencyFocus != FateCurrency.None)
            return $"Currency focus '{FateToolKit.CurrencyName(_tweak.Config.CurrencyFocus)}' defines the zone list. Disable zones to exclude them from rotation.";
        if (_tweak.HasSelectedSwapZones)
            return $"Manual selection: {_tweak.SelectedSwapZones.Count} zones. Disable a zone to remove it from rotation.";
        return "Default rotation is achievement-guided. Enable zones to build a manual list, or leave the default pool intact.";
    }

    private (int Allowed, int Total) ZonePoolCounts()
    {
        var pool = _tweak.GetRawSwapZones() ?? FateZones.All
            .Where(zone => zone.ZoneClass == FateZoneLogic.ZoneClass.FateZone)
            .Select(zone => zone.TerritoryId)
            .ToHashSet();
        return (pool.Count(id => !_tweak.Config.ExcludedSwapZones.Contains(id)), pool.Count);
    }

    private bool ZoneNameMatches(string name)
        => string.IsNullOrEmpty(_zoneFilter) || name.Contains(_zoneFilter, StringComparison.OrdinalIgnoreCase);

    private bool IsZoneChecked(uint zoneId)
    {
        var rawPool = _tweak.GetRawSwapZones();
        if (rawPool != null)
            return !_tweak.Config.ExcludedSwapZones.Contains(zoneId);
        if (_tweak.Config.SelectedSwapZones.Count > 0)
            return _tweak.Config.SelectedSwapZones.Contains(zoneId) && !_tweak.Config.ExcludedSwapZones.Contains(zoneId);
        return !_tweak.Config.ExcludedSwapZones.Contains(zoneId);
    }

    private void SetZonesChecked(IEnumerable<FateZone> zones, bool value)
    {
        var selected = _tweak.Config.SelectedSwapZones;
        var excluded = _tweak.Config.ExcludedSwapZones;
        var rawPool = _tweak.GetRawSwapZones();
        var ids = zones.Where(zone => zone.ZoneClass == FateZoneLogic.ZoneClass.FateZone).Select(zone => zone.TerritoryId).ToList();

        if (rawPool != null)
        {
            foreach (var id in ids)
            {
                if (value)
                    excluded.Remove(id);
                else
                    excluded.Add(id);
            }
        }
        else if (selected.Count > 0)
        {
            foreach (var id in ids)
            {
                if (value)
                {
                    selected.Add(id);
                    excluded.Remove(id);
                }
                else
                {
                    selected.Remove(id);
                }
            }
        }
        else if (value)
        {
            foreach (var id in ids)
                excluded.Remove(id);
        }
        else
        {
            var removed = ids.ToHashSet();
            selected.Clear();
            foreach (var zone in FateZones.All)
                if (zone.ZoneClass == FateZoneLogic.ZoneClass.FateZone && !removed.Contains(zone.TerritoryId) && !excluded.Contains(zone.TerritoryId))
                    selected.Add(zone.TerritoryId);
        }
        _tweak.Config.Save();
    }

    private void DrawDisplayPage()
    {
        FateChrome.Columns("display",
            () =>
            {
                FateChrome.Section("Priority order", $"{_tweak.Config.SortOrder.Count} rules");
                FateChrome.Paragraph("Available FATEs use these rules from top to bottom. Drag a row to reorder it, change its direction, or remove it.");
                FateChrome.Space(12);
                var sortOrder = _tweak.Config.SortOrder.ToList();
                for (var i = 0; i < sortOrder.Count; i++)
                    if (DrawSortRow(sortOrder, i))
                        i--;
                DrawButtonRow(
                    new ButtonAction("sort_add", "Add Sort Criteria", () => ImGui.OpenPopup("###AddSortCriteria")),
                    new ButtonAction("sort_reset", "Reset to Default", () =>
                    {
                        _tweak.Config.SortOrder = Configuration.DefaultSortOrder();
                        _tweak.Config.Save();
                    }));
                FateChrome.Space(16);
                DrawAddSortPopup(sortOrder);
            },
            () => FateChrome.Card("display_settings", () =>
            {
                FateChrome.Section("Display");
                Setting("show_server_bar", "Show server info bar entry",
                    () => _tweak.Config.ShowServerBarEntry,
                    value => _tweak.Config.ShowServerBarEntry = value,
                    "The FATE bot's On/Off switch in the game's server info bar. Starting from it requires a confirming second click; the game and Umbra can also hide the entry independently.");
                FateChrome.Divider();
                FateChrome.Line("Display name format");
                FateChrome.Space(8);
                using (FateChrome.FieldStyle(FateChrome.Control))
                {
                    ImGui.SetNextItemWidth(FateChrome.Avail());
                    if (ImGui.InputText("###DisplayNameFormat", ref _tweak.Config.DisplayNameFormat, 256))
                        _tweak.Config.Save();
                }
                FateChrome.Space(8);
                FateChrome.Paragraph("Available tokens: {Level}, {Name}, {Id}, {Progress}, {TimeRemaining}, {Distance}, {State}. Changes save immediately and apply to every FATE row.");
            }, fill: true));
    }

    private bool DrawSortRow(List<FateSortOrder> sortOrder, int index)
    {
        var removed = false;
        FateChrome.Card($"sort_row_{index}", () =>
        {
            using var id = ImRaii.PushId($"sort_{index}");
            var item = sortOrder[index];
            var criteria = item.Criteria;
            var width = FateChrome.Avail();
            var draw = ImGui.GetWindowDrawList();
            var wide = width >= 560;
            var origin = ImGui.GetCursorScreenPos();
            const float gap = 8;
            const float control = 32;
            var dragSize = new Vector2(FateChrome.ActionWidth(58, "Drag"), control);
            var directionSize = new Vector2(FateChrome.ActionWidth(88, "High first", "Low first"), control);
            var removeSize = new Vector2(FateChrome.ActionWidth(72, "Remove"), control);

            FateChrome.OutlineButton(draw, $"sort_drag_{index}", "Drag", origin, dragSize);
            ImGui.DragDropSource(index, "FATE_SORT_ITEM"u8, FateChrome.Humanize(criteria.ToString()));
            ImGui.DragDropTarget(index, "FATE_SORT_ITEM"u8, sortOrder.Count, (sourceIndex, insertIndex) =>
            {
                var dragged = sortOrder[sourceIndex];
                sortOrder.RemoveAt(sourceIndex);
                if (sourceIndex < insertIndex)
                    insertIndex--;
                sortOrder.Insert(insertIndex, dragged);
                _tweak.Config.SortOrder = sortOrder;
                _tweak.Config.Save();
            });
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Drag to change priority order");

            var comboPos = origin + new Vector2(dragSize.X + gap, 0);
            var comboWidth = wide
                ? MathF.Max(120, width - dragSize.X - directionSize.X - removeSize.X - (3 * gap))
                : width - dragSize.X - gap;
            ImGui.SetCursorScreenPos(comboPos);
            FateChrome.PushRight(comboPos.X + comboWidth);
            ComboField($"###Criteria{index}", FateChrome.Humanize(criteria.ToString()), Enum.GetValues<FateSortCriteria>(),
                criterion => FateChrome.Humanize(criterion.ToString()), criteria, criterion =>
                {
                    if (criterion == criteria)
                        return;
                    item.Criteria = criterion;
                    _tweak.Config.SortOrder[index] = item;
                    _tweak.Config.Save();
                }, control);
            FateChrome.PopRight();

            var actionY = wide ? 0 : control + gap;
            var directionPos = wide
                ? origin + new Vector2(width - directionSize.X - removeSize.X - gap, 0)
                : origin + new Vector2(0, actionY);
            if (FateChrome.OutlineButton(draw, $"sort_direction_{index}", item.Descending ? "High first" : "Low first", directionPos, directionSize))
            {
                item.Descending = !item.Descending;
                _tweak.Config.SortOrder[index] = item;
                _tweak.Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(item.Descending ? "Descending (highest first)" : "Ascending (lowest first)");

            var removePos = wide
                ? origin + new Vector2(width - removeSize.X, 0)
                : origin + new Vector2(directionSize.X + gap, actionY);
            if (FateChrome.OutlineButton(draw, $"sort_remove_{index}", "Remove", removePos, removeSize))
            {
                sortOrder.RemoveAt(index);
                _tweak.Config.SortOrder = sortOrder;
                _tweak.Config.Save();
                removed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove this sort criterion");

            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new Vector2(width, wide ? control : (2 * control) + gap));
        }, row: true, gap: 8);
        return removed;
    }

    private void DrawAddSortPopup(List<FateSortOrder> sortOrder)
    {
        if (_buttonRects.TryGetValue("sort_add", out var anchor))
        {
            ImGui.SetNextWindowPos(new Vector2(anchor.Pos.X, anchor.Pos.Y + anchor.Size.Y + 4), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(FateChrome.PopupWidth(Enum.GetValues<FateSortCriteria>().Select(c => FateChrome.Humanize(c.ToString())), 240), 0));
            FateChrome.LimitPopupHeight(anchor.Pos.Y + anchor.Size.Y + 4);
        }
        using var style = FateChrome.PopupStyle();
        using var popup = ImRaii.Popup("###AddSortCriteria");
        if (!popup)
            return;
        FateChrome.PopupTitle("Add sort criterion");
        foreach (var criterion in Enum.GetValues<FateSortCriteria>())
        {
            if (!FateChrome.PopupRow("add_" + criterion, FateChrome.Humanize(criterion.ToString()), false))
                continue;
            sortOrder.Add(new FateSortOrder { Criteria = criterion, Descending = true });
            _tweak.Config.SortOrder = sortOrder;
            _tweak.Config.Save();
            ImGui.CloseCurrentPopup();
        }
    }

    private void Setting(string id, string label, Func<bool> get, Action<bool> set, string? help = null)
    {
        var value = get();
        if (!FateChrome.Toggle(id, label, ref value, help))
            return;
        set(value);
        _tweak.Config.Save();
    }

    private readonly record struct ButtonAction(string Id, string Label, Action Click);

    private void DrawButtonRow(params ButtonAction[] buttons)
    {
        if (buttons.Length == 0)
            return;
        var origin = ImGui.GetCursorScreenPos();
        var width = FateChrome.Avail();
        var draw = ImGui.GetWindowDrawList();
        const float gap = 8;
        const float height = 32;
        float x = 0;
        float y = 0;
        foreach (var button in buttons)
        {
            var size = new Vector2(MathF.Min(width, FateChrome.ActionWidth(72, button.Label)), height);
            if (x > 0 && x + size.X > width)
            {
                x = 0;
                y += height + gap;
            }
            var pos = origin + new Vector2(x, y);
            _buttonRects[button.Id] = (pos, size);
            if (FateChrome.OutlineButton(draw, button.Id, button.Label, pos, size))
                button.Click();
            x += size.X + gap;
        }
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, y + height));
    }

    private static string FormatTimeRemaining(float seconds)
        => seconds >= 0 ? TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss") : "∞";

    public string FormatDisplayName(PublicEvent fate) => _tweak.Config.DisplayNameFormat
        .Replace("{Level}", fate.Level.ToString())
        .Replace("{Name}", fate.Name)
        .Replace("{Id}", fate.Id.ToString())
        .Replace("{Progress}", fate.Progress.ToString())
        .Replace("{TimeRemaining}", FormatTimeRemaining(fate.TimeRemaining))
        .Replace("{Distance}", Player.Available ? Player.DistanceTo(fate.Position).ToString("F1") : "?")
        .Replace("{State}", fate.State.ToString());
}
