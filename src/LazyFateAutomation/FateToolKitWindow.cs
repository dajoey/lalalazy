using LazyFateAutomation.Helpers.ImGuiHelpers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Utility.Raii;
using LalaUiKit;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace LazyFateAutomation;

public class FateToolKitWindow : MinimisableWindow
{
    private const string LivePage = "fates.live";
    private const string SelectionPage = "settings.selection";
    private const string CurrencyPage = "settings.currency";
    private const string ZonesPage = "settings.zones";
    private const string DisplayPage = "settings.display";

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

    private static readonly PropertyInfo[] TooltipProperties;

    static FateToolKitWindow()
    {
        TooltipProperties = [.. typeof(PublicEvent)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(p => p.Name)];
    }

    public FateToolKitWindow(FateToolKit tweak) : base($"Fate Tracker##{nameof(FateToolKitWindow)}")
    {
        _tweak = tweak;
        Size = new Vector2(1120, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            Click = _ => ToggleSettings(),
        });
    }

    protected override Vector2 MinimisedSize => new(700, 90);

    public override bool DrawConditions() => Player.Available;

    private FatePage Current => Pages.First(page => page.Id == _currentPage);

    protected override void DrawContent(bool minimised)
    {
        _tweak.SyncRunningState();
        Theme.PushFrame();
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 8f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent with { W = 0.14f });
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.24f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Accent with { W = 0.30f });
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.ShellBar);
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

            DrawTopBar(state);
            if (Current.Group == "Settings")
                DrawFeatureRow();
            DrawPageHeader(fates);

            var footerHeight = FooterHeight(ImGui.GetContentRegionAvail().X);
            ImGui.BeginChild("##fate_body", new Vector2(0, MathF.Max(60, ImGui.GetContentRegionAvail().Y - footerHeight)), false);
            try
            {
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
            }
            finally
            {
                ImGui.EndChild();
            }
            DrawFooter(state, action);
        }
        finally
        {
            ImGui.PopStyleColor(8);
            ImGui.PopStyleVar(3);
            Theme.PopFrame();
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
            : _tweak.CurrentState);
    }

    private void DrawTopBar(UiState state)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var gap = 2 * Theme.Unit;
        var groups = Pages.Select(page => page.Group).Distinct().ToArray();
        var brandSize = new Vector2(MathF.Max(214, (ImGui.CalcTextSize("LazyFateAutomation").X * Theme.TitleScale) + 68), 42);
        var groupSizes = groups.Select(group => new Vector2(MathF.Max(82, ImGui.CalcTextSize(group).X + 32), FateChrome.ControlHeight)).ToArray();
        var statusSize = new Vector2(MathF.Max(82, ImGui.CalcTextSize(Copy.Word(state)).X + 43), FateChrome.ControlHeight);
        var totalWidth = brandSize.X + statusSize.X + groupSizes.Sum(size => size.X) + ((groups.Length + 1) * gap);
        var singleRow = width >= totalWidth;
        var brandWithStatus = singleRow || width >= brandSize.X + statusSize.X + gap;
        var brandPos = new Vector2(0, 12);
        var statusPos = singleRow
            ? new Vector2(width - statusSize.X, (66 - FateChrome.ControlHeight) / 2f)
            : new Vector2(width - statusSize.X, brandWithStatus ? 16 : 62);
        var groupY = singleRow ? (66 - FateChrome.ControlHeight) / 2f : brandWithStatus ? 62 : 104;
        var groupPositions = new Vector2[groups.Length];
        var x = singleRow ? brandSize.X + gap : 0;
        var y = groupY;
        for (var i = 0; i < groups.Length; i++)
        {
            var buttonWidth = MathF.Min(groupSizes[i].X, width);
            if (x > 0 && x + buttonWidth > width)
            {
                x = 0;
                y += FateChrome.ControlHeight + gap;
            }
            groupPositions[i] = new Vector2(x, y);
            x += buttonWidth + gap;
        }
        var barHeight = singleRow ? 66 : y + FateChrome.ControlHeight + 12;

        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        if (FateChrome.BrandButton(draw, origin + brandPos, brandSize))
            _currentPage = LivePage;
        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            var destination = group == "FATEs" ? LivePage : _lastSettingsPage;
            if (FateChrome.OutlineButton(draw, "group_" + group, group, origin + groupPositions[i], groupSizes[i], Current.Group == group))
                _currentPage = destination;
        }
        FateChrome.StatusPill(draw, origin + statusPos, statusSize, state);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private void DrawFeatureRow()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var gap = 3 * Theme.Unit;
        var features = Pages.Where(page => page.Group == "Settings").ToArray();
        var sizes = features.Select(feature => new Vector2(
            MathF.Min(width, MathF.Max(132, ImGui.CalcTextSize(feature.Title).X + 52)), FateChrome.ControlHeight)).ToArray();
        var positions = new Vector2[features.Length];
        float x = 0;
        float y = 8;
        for (var i = 0; i < features.Length; i++)
        {
            if (x > 0 && x + sizes[i].X > width)
            {
                x = 0;
                y += FateChrome.ControlHeight + (2 * Theme.Unit);
            }
            positions[i] = new Vector2(x, y);
            x += sizes[i].X + gap;
        }
        var barHeight = y + FateChrome.ControlHeight + 8;
        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        for (var i = 0; i < features.Length; i++)
        {
            var feature = features[i];
            if (FateChrome.FeatureButton(draw, feature.Id, feature.Title, origin + positions[i], sizes[i], _currentPage == feature.Id))
            {
                _currentPage = feature.Id;
                _lastSettingsPage = feature.Id;
            }
        }
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private void DrawPageHeader(List<(PublicEvent Fate, bool IsAvailable)>? fates)
    {
        var page = Current;
        var count = LiveCount(fates);
        var subtext = LiveSubtext();
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var tileSize = new Vector2(42);
        draw.AddRectFilled(origin, origin + tileSize, Theme.Pack(Theme.Accent with { W = 0.14f }), 8f);
        FateChrome.Diamond(draw, origin + (tileSize / 2f), 10, Theme.Pack(Theme.Accent));

        if (width >= 700)
        {
            var textX = origin.X + tileSize.X + 12;
            var countWidth = ImGui.CalcTextSize(count).X;
            var subtextWidth = ImGui.CalcTextSize(subtext).X;
            FateChrome.DrawText(draw, new Vector2(textX, origin.Y), FateChrome.FitText(page.Title, width - tileSize.X - 12 - countWidth - 16, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            FateChrome.DrawText(draw, new Vector2(textX, origin.Y + 29), FateChrome.FitText(page.Purpose, width - tileSize.X - 12 - subtextWidth - 16), Theme.TextSecondary);
            FateChrome.DrawTextRight(draw, origin.X + width, origin.Y + 4, count, Theme.TextSecondary);
            FateChrome.DrawTextRight(draw, origin.X + width, origin.Y + 31, subtext, Theme.TextSecondary);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, 58));
            ImGui.Separator();
            return;
        }

        FateChrome.DrawText(draw, origin + new Vector2(tileSize.X + 12, 4), FateChrome.FitText(page.Title, width - tileSize.X - 12, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, tileSize.Y + 8));
        Widgets.SecondaryWrapped(page.Purpose);
        Widgets.Caption(count);
        Widgets.Caption(subtext);
        ImGui.Separator();
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
            return $"{_tweak.GetCurrentMode().DisplayName} mode · {_tweak.CurrentState}";
        if (_currentPage == SelectionPage)
            return $"Forlorn priority {(_tweak.Config.PrioritizeForlornMaidens ? "on" : "off")}";
        if (_currentPage == CurrencyPage)
            return _tweak.Config.CurrencyFocus == FateCurrency.None ? "Normal zone selection" : CurrencyFallbackLabel(_tweak.Config.CurrencyFocusFallback);
        if (_currentPage == ZonesPage)
            return ZoneSourceLabel();
        return $"Server bar {(_tweak.Config.ShowServerBarEntry ? "on" : "off")}";
    }

    private void DrawLivePage(List<(PublicEvent Fate, bool IsAvailable)> fates)
    {
        var eligible = fates.Count(row => row.IsAvailable);
        FateChrome.StatCards(
            new FateChrome.Stat("Tracked FATEs", fates.Count.ToString(), fates.Count == 0 ? "No FATEs in this zone" : "All visible FATEs"),
            new FateChrome.Stat("Eligible now", eligible.ToString(), eligible == 0 ? "None currently eligible" : "Pass every automation rule", fates.Count > 0 ? eligible / (float)fates.Count : null),
            new FateChrome.Stat("Completed this run", _tweak.CompletedCount.ToString(),
                _tweak.RemainingUntilCompleted is { } remaining ? $"{remaining} remaining" : "No completion target",
                _tweak.RunUntilCompleted is { } target && target > 0 ? _tweak.CompletedCount / (float)target : null));

        DrawRunSetup();

        if (fates.Count == 0)
        {
            Widgets.InfoBox("Empty — no FATEs are visible in this zone. Move to a zone with active FATEs or wait for one to spawn.", UiState.Empty);
            return;
        }

        Section("FATEs in this zone", $"{eligible} eligible of {fates.Count}");
        Widgets.SecondaryWrapped("Click a FATE to pathfind there, or click again while navigation is active to stop. Right-click toggles its blacklist entry; hover for eligibility details.");
        foreach (var (fate, isAvailable) in fates)
            DrawFateCard(fate, isAvailable);
    }

    private void DrawRunSetup()
    {
        FateChrome.Card("run_setup", () =>
        {
            Section("Run setup", _tweak.Running ? Service.Automation.Status : "Automation stopped");
            Widgets.SecondaryWrapped($"Grind mode: {_tweak.GetCurrentMode().DisplayName}. Completed FATEs: {_tweak.CompletedCount}. {ModeProgressText()}");
            DrawRunControls("run", false);
        });
    }

    private string ModeProgressText()
    {
        var modeRemaining = _tweak.GetCurrentMode().GetRemainingDisplay(_tweak);
        if (!string.IsNullOrEmpty(modeRemaining))
            return $"Mode progress: {modeRemaining}.";
        return _tweak.RemainingUntilCompleted is { } remaining
            ? $"Completion target remaining: {remaining}."
            : "No completion target is set.";
    }

    private void DrawRunControls(string id, bool compact)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var gap = 2 * Theme.Unit;
        var runSize = new Vector2(compact ? 82 : 92, FateChrome.ControlHeight);
        var modeLabel = compact ? _tweak.GetCurrentMode().DisplayName : $"Mode: {_tweak.GetCurrentMode().DisplayName}";
        var modeSize = new Vector2(MathF.Min(compact ? 150 : 220, MathF.Max(compact ? 92 : 130, ImGui.CalcTextSize(modeLabel).X + 32)), FateChrome.ControlHeight);
        var zoneSize = new Vector2(compact ? 100 : 124, FateChrome.ControlHeight);
        var total = runSize.X + modeSize.X + zoneSize.X + (2 * gap);
        var runPos = origin;
        Vector2 modePos;
        Vector2 zonePos;
        float height;
        if (total <= width)
        {
            modePos = origin + new Vector2(runSize.X + gap, 0);
            zonePos = origin + new Vector2(runSize.X + modeSize.X + (2 * gap), 0);
            height = FateChrome.ControlHeight;
        }
        else
        {
            modePos = origin + new Vector2(0, FateChrome.ControlHeight + gap);
            zonePos = modePos + new Vector2(modeSize.X + gap, 0);
            height = (2 * FateChrome.ControlHeight) + gap;
            if (zonePos.X + zoneSize.X > origin.X + width)
            {
                zonePos = origin + new Vector2(0, 2 * (FateChrome.ControlHeight + gap));
                height = (3 * FateChrome.ControlHeight) + (2 * gap);
            }
        }

        DrawRunButton(id + "_primary", runPos, runSize);
        DrawModeButton(id + "_mode", modePos, modeSize, modeLabel);
        DrawZoneButton(id + "_zones", zonePos, zoneSize);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    private void DrawRunButton(string id, Vector2 pos, Vector2 size)
    {
        var draw = ImGui.GetWindowDrawList();
        var clicked = _tweak.Running
            ? FateChrome.DangerButton(draw, id, _tweak.PendingStopWhenSafe ? "Stopping" : "Stop", pos, size)
            : FateChrome.OutlineButton(draw, id, "Start", pos, size, true);
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

        using var popup = ImRaii.Popup("###GrindModePopup");
        if (!popup)
            return;
        Widgets.Title("Grind mode");
        ImGui.Separator();
        foreach (var mode in FateGrindModes.All)
        {
            if (ImGui.Selectable(mode.DisplayName, mode.DisplayName == _tweak.SelectedModeId))
            {
                _tweak.SelectedModeId = mode.DisplayName;
                ImGui.CloseCurrentPopup();
            }
        }
    }

    private void DrawZoneButton(string id, Vector2 pos, Vector2 size)
    {
        using var disabled = ImRaii.Disabled(_tweak.ModeSuppliesSwapZones);
        if (FateChrome.OutlineButton(ImGui.GetWindowDrawList(), id, "Swap zones", pos, size, _tweak.HasSelectedSwapZones))
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
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var gap = 2 * Theme.Unit;
        var runSize = new Vector2(82, FateChrome.ControlHeight);
        var statusSize = new Vector2(82, FateChrome.ControlHeight);
        var modeSize = new Vector2(132, FateChrome.ControlHeight);
        var zoneSize = new Vector2(100, FateChrome.ControlHeight);
        var x = 0f;
        DrawRunButton("compact_primary", origin, runSize);
        x += runSize.X + gap;
        FateChrome.StatusPill(draw, origin + new Vector2(x, 0), statusSize, state);
        x += statusSize.X + gap;
        var completed = $"{_tweak.CompletedCount} completed";
        FateChrome.DrawText(draw, origin + new Vector2(x, 9), FateChrome.FitText(completed, 94), Theme.TextSecondary);
        x += 102;
        DrawModeButton("compact_mode", origin + new Vector2(x, 0), modeSize, _tweak.GetCurrentMode().DisplayName);
        x += modeSize.X + gap;
        DrawZoneButton("compact_zones", origin + new Vector2(x, 0), zoneSize);
        FateChrome.DrawText(draw, origin + new Vector2(0, 44), FateChrome.FitText($"{Copy.Word(state)} · {action}", width), Theme.TextSecondary);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, 62));
    }

    private void DrawFateCard(PublicEvent fate, bool isAvailable)
    {
        FateChrome.Card($"fate_{fate.Id}", () =>
        {
            var origin = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var draw = ImGui.GetWindowDrawList();
            var displayName = FormatDisplayName(fate);
            var isBlacklisted = _tweak.IsBlacklisted(fate);
            var rowState = isAvailable && !isBlacklisted ? UiState.Ready : UiState.Blocked;
            var stateLabel = Copy.Word(rowState);
            const float rowHeight = 64;

            ImGui.SetCursorScreenPos(origin);
            ImGui.InvisibleButton("##fate_open", new Vector2(width, rowHeight));
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
                ImGui.SetTooltip(BuildFateTooltip(fate, displayName, isBlacklisted));

            FateChrome.Diamond(draw, origin + new Vector2(11, 14), 6, Theme.Pack(rowState == UiState.Ready ? Theme.Accent : Theme.TextSecondary));
            var stateWidth = ImGui.CalcTextSize(stateLabel).X;
            FateChrome.DrawText(draw, origin + new Vector2(26, 3), FateChrome.FitText(displayName, width - 26 - stateWidth - 18), isAvailable && !isBlacklisted ? Theme.TextPrimary : Theme.TextSecondary);
            FateChrome.DrawTextRight(draw, origin.X + width, origin.Y + 3, stateLabel, Copy.MarkColor(rowState));

            var time = FormatTimeRemaining(fate.TimeRemaining);
            var distance = Player.Available ? Player.DistanceTo(fate.Position).ToString("F1") + " y" : "?";
            var bonus = fate.HasBonus ? " · Bonus" : "";
            var metadata = $"{time} · {distance} · {fate.State}{bonus}";
            var progressLabel = $"{fate.Progress}%";
            var progressLabelWidth = ImGui.CalcTextSize(progressLabel).X;
            FateChrome.DrawText(draw, origin + new Vector2(26, 29), FateChrome.FitText(metadata, width - 26 - progressLabelWidth - 18), Theme.TextSecondary);
            FateChrome.DrawTextRight(draw, origin.X + width, origin.Y + 29, progressLabel, Theme.TextSecondary);

            var fraction = Math.Clamp(fate.Progress / 100f, 0f, 1f);
            var barMin = origin + new Vector2(26, 53);
            var barMax = origin + new Vector2(width, 57);
            draw.AddRectFilled(barMin, barMax, Theme.Pack(Theme.SurfaceBorder), 2f);
            if (fraction > 0)
                draw.AddRectFilled(barMin, barMin + new Vector2((barMax.X - barMin.X) * fraction, 0), Theme.Pack(_tweak.Config.BarColour), 2f);
        }, row: true);
    }

    private void DrawFateSelectionPage()
    {
        FateChrome.TwoColumns(
            () => FateChrome.Card("allowed_fate_types", () =>
            {
                Section("Allowed FATE types", $"{FateRuleLabels.Count(row => !_tweak.Config.ExcludedFateRules.Contains(row.Rule))}/{FateRuleLabels.Length} enabled");
                Widgets.SecondaryWrapped("Disabled FATE types are never started. Boss and trash-kill FATEs share the game's kill & boss rule; Bozja, Occult Crescent skirmishes, and cosmic exploration events use it too.");
                var excluded = _tweak.Config.ExcludedFateRules;
                foreach (var (rule, label) in FateRuleLabels)
                {
                    var allowed = !excluded.Contains(rule);
                    if (!FateChrome.Toggle($"rule_{rule}", label, ref allowed))
                        continue;
                    if (allowed)
                        excluded.Remove(rule);
                    else
                        excluded.Add(rule);
                    _tweak.Config.Save();
                }
            }),
            () =>
            {
                FateChrome.Card("target_priority", () =>
                {
                    Section("Target priority");
                    Setting("prioritize_forlorn", "Prioritize Forlorn Maidens",
                        () => _tweak.Config.PrioritizeForlornMaidens,
                        value => _tweak.Config.PrioritizeForlornMaidens = value,
                        "When a Forlorn Maiden or the Forlorn appears mid-FATE, combat targets it instead of the FATE's normal enemies until it is defeated or disappears.");
                });
                FateChrome.Card("selection_behavior", () =>
                {
                    Section("Selection behavior");
                    Widgets.SecondaryWrapped("Eligibility still uses duration, progress, time remaining, blacklist, pending state, and player level. This page changes only which eligible types may run.");
                });
            });
    }

    private void DrawCurrencyFocusPage()
    {
        FateChrome.TwoColumns(
            () => FateChrome.Card("currency_settings", () =>
            {
                Section("Reward currency");
                Widgets.SecondaryWrapped("Zone swaps prefer zones whose FATEs reward the focused currency. A grind mode with its own zone list still wins.");
                Widgets.Body("Focus currency");
                ImGui.SetNextItemWidth(MathF.Min(340, ImGui.GetContentRegionAvail().X));
                var focus = _tweak.Config.CurrencyFocus;
                using (var combo = ImRaii.Combo("###CurrencyFocus", FateToolKit.CurrencyName(focus)))
                {
                    if (combo)
                    {
                        foreach (var currency in Enum.GetValues<FateCurrency>())
                        {
                            if (ImGui.Selectable(FateToolKit.CurrencyName(currency), currency == focus) && focus != currency)
                            {
                                _tweak.Config.CurrencyFocus = currency;
                                _tweak.Config.Save();
                            }
                        }
                    }
                }
                Widgets.SecondaryWrapped("Company Seals: A Realm Reborn, Heavensward, and Stormblood zones. Bicolor Gemstones: Shadowbringers and later zones. Yo-kai Medals: event zones with the watch equipped and matching minion out.");

                focus = _tweak.Config.CurrencyFocus;
                if (focus != FateCurrency.None)
                {
                    ImGui.Separator();
                    Widgets.Body("When focused zones have nothing to run");
                    ImGui.SetNextItemWidth(MathF.Min(340, ImGui.GetContentRegionAvail().X));
                    var fallback = _tweak.Config.CurrencyFocusFallback;
                    using (var combo = ImRaii.Combo("###CurrencyFocusFallback", CurrencyFallbackLabel(fallback)))
                    {
                        if (combo)
                        {
                            foreach (var option in Enum.GetValues<CurrencyFocusFallback>())
                            {
                                if (ImGui.Selectable(CurrencyFallbackLabel(option), option == fallback) && fallback != option)
                                {
                                    _tweak.Config.CurrencyFocusFallback = option;
                                    _tweak.Config.Save();
                                }
                            }
                        }
                    }
                    Widgets.SecondaryWrapped("Zone swaps first pass through every focused zone. Continue normally returns to the usual selection after a full pass; Idle in zone waits for focused-currency FATEs.");
                }
            }),
            () => FateChrome.Card("currency_state", () =>
            {
                Section("Current rotation");
                Widgets.Body(FateToolKit.CurrencyName(_tweak.Config.CurrencyFocus));
                Widgets.SecondaryWrapped(_tweak.Config.CurrencyFocus == FateCurrency.None
                    ? "Normal zone selection remains active."
                    : $"{CurrencyFallbackLabel(_tweak.Config.CurrencyFocusFallback)} after the focused pool is exhausted.");
                ImGui.Separator();
                Section("Zone source");
                Widgets.SecondaryWrapped(ZoneSourceLabel());
            }));
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
            Section("Zone pool", ZoneSourceLabel());
            Widgets.SecondaryWrapped(ZoneHelpText());
            ImGui.SetNextItemWidth(MathF.Min(340, ImGui.GetContentRegionAvail().X));
            ImGui.InputTextWithHint("###ZoneFilter", "Filter zones...", ref _zoneFilter, 64);

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
                Section(group.Key.Name, $"{allowedCount}/{zones.Count} allowed");
                DrawButtonRow(
                    new ButtonAction($"exp_all_{group.Key.Id}", "All", () => SetZonesChecked(zones, true)),
                    new ButtonAction($"exp_none_{group.Key.Id}", "None", () => SetZonesChecked(zones, false)));
                var noTeleport = zones.Count(zone => zone.ZoneClass != FateZoneLogic.ZoneClass.FateZone);
                if (noTeleport > 0)
                    Widgets.Caption($"{noTeleport} without a teleport aetheryte");
                DrawZoneToggles(zones);
            });
        }
        if (!anyGroup)
            Widgets.InfoBox($"Empty — no zones match ‘{_zoneFilter}’. Clear the filter to restore the full zone list.", UiState.Empty);
    }

    private void DrawZoneToggles(List<FateZone> zones)
    {
        void DrawRange(IEnumerable<FateZone> source)
        {
            foreach (var zone in source)
            {
                var isChecked = IsZoneChecked(zone.TerritoryId);
                var tooltip = zone.ZoneClass == FateZoneLogic.ZoneClass.NoTeleportFateZone
                    ? "No aetheryte to teleport into this zone; it is never picked by automatic zone swaps."
                    : null;
                if (FateChrome.Toggle($"zone_{zone.TerritoryId}", zone.Name, ref isChecked, tooltip: tooltip))
                    SetZonesChecked([zone], isChecked);
            }
        }

        if (ImGui.GetContentRegionAvail().X >= 720)
        {
            FateChrome.TwoColumns(
                () => DrawRange(zones.Where((_, index) => index % 2 == 0)),
                () => DrawRange(zones.Where((_, index) => index % 2 == 1)),
                0.5f);
        }
        else
        {
            DrawRange(zones);
        }
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
        FateChrome.TwoColumns(
            () =>
            {
                Section("Priority order", $"{_tweak.Config.SortOrder.Count} rules");
                Widgets.SecondaryWrapped("Available FATEs use these rules from top to bottom. Drag a row to reorder it, change its direction, or remove it.");
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
                DrawAddSortPopup(sortOrder);
            },
            () => FateChrome.Card("display_settings", () =>
            {
                Section("Display");
                Setting("show_server_bar", "Show server info bar entry",
                    () => _tweak.Config.ShowServerBarEntry,
                    value => _tweak.Config.ShowServerBarEntry = value,
                    "The FATE bot's On/Off switch in the game's server info bar. Starting from it requires a confirming second click; the game and Umbra can also hide the entry independently.");
                ImGui.Separator();
                Section("Display name format");
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputText("###DisplayNameFormat", ref _tweak.Config.DisplayNameFormat, 256))
                    _tweak.Config.Save();
                Widgets.SecondaryWrapped("Available tokens: {Level}, {Name}, {Id}, {Progress}, {TimeRemaining}, {Distance}, {State}. Changes save immediately and apply to every FATE row.");
            }),
            0.68f);
    }

    private bool DrawSortRow(List<FateSortOrder> sortOrder, int index)
    {
        var removed = false;
        FateChrome.Card($"sort_row_{index}", () =>
        {
            using var id = ImRaii.PushId($"sort_{index}");
            var item = sortOrder[index];
            var criteria = item.Criteria;
            var width = ImGui.GetContentRegionAvail().X;
            var draw = ImGui.GetWindowDrawList();
            var wide = width >= 560;
            var origin = ImGui.GetCursorScreenPos();
            var gap = 2 * Theme.Unit;
            var dragSize = new Vector2(58, 32);
            var directionSize = new Vector2(94, 32);
            var removeSize = new Vector2(80, 32);

            FateChrome.OutlineButton(draw, $"sort_drag_{index}", "Drag", origin, dragSize);
            ImGui.DragDropSource(index, "FATE_SORT_ITEM"u8, criteria.ToString().Replace("_", " "));
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

            var comboPos = wide ? origin + new Vector2(dragSize.X + gap, 0) : origin + new Vector2(0, 40);
            var comboWidth = wide
                ? MathF.Max(120, width - dragSize.X - directionSize.X - removeSize.X - (4 * gap))
                : width;
            ImGui.SetCursorScreenPos(comboPos);
            ImGui.SetNextItemWidth(comboWidth);
            using (var combo = ImRaii.Combo($"###Criteria{index}", criteria.ToString().Replace("_", " ")))
            {
                if (combo)
                {
                    foreach (var criterion in Enum.GetValues<FateSortCriteria>())
                    {
                        if (ImGui.Selectable(criterion.ToString().Replace("_", " "), criterion == criteria))
                        {
                            item.Criteria = criterion;
                            _tweak.Config.SortOrder[index] = item;
                            _tweak.Config.Save();
                        }
                    }
                }
            }

            var actionY = wide ? 0 : 80;
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
            ImGui.Dummy(new Vector2(width, wide ? 32 : 112));
        }, row: true);
        return removed;
    }

    private void DrawAddSortPopup(List<FateSortOrder> sortOrder)
    {
        using var popup = ImRaii.Popup("###AddSortCriteria");
        if (!popup)
            return;
        Widgets.Title("Add sort criterion");
        ImGui.Separator();
        foreach (var criterion in Enum.GetValues<FateSortCriteria>())
        {
            if (!ImGui.Selectable(criterion.ToString().Replace("_", " ")))
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

    private static void Section(string title, string? detail = null)
    {
        Widgets.Title(detail is null ? title : $"{title} · {detail}");
    }

    private readonly record struct ButtonAction(string Id, string Label, Action Click);

    private static void DrawButtonRow(params ButtonAction[] buttons)
    {
        if (buttons.Length == 0)
            return;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var draw = ImGui.GetWindowDrawList();
        var gap = 2 * Theme.Unit;
        float x = 0;
        float y = 0;
        foreach (var button in buttons)
        {
            var size = new Vector2(MathF.Min(width, MathF.Max(72, ImGui.CalcTextSize(button.Label).X + 32)), 32);
            if (x > 0 && x + size.X > width)
            {
                x = 0;
                y += 32 + gap;
            }
            if (FateChrome.OutlineButton(draw, button.Id, button.Label, origin + new Vector2(x, y), size))
                button.Click();
            x += size.X + gap;
        }
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, y + 32 + gap));
    }

    private static float FooterHeight(float width) => width < 620 ? 100f : 68f;

    private void DrawFooter(UiState state, string action)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = FooterHeight(width);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        var narrow = width < 620;
        var actionSize = new Vector2(narrow ? MathF.Min(160, width) : 100, 36);
        var actionPos = origin + new Vector2(width - actionSize.X, narrow ? 54 : 16);
        var textRight = narrow ? origin.X + width : actionPos.X - 16;
        var textPos = origin + new Vector2(24, 9);
        var primary = $"{Copy.Word(state)} · {action}";
        var secondary = _tweak.Running
            ? $"Mode: {_tweak.GetCurrentMode().DisplayName} · {_tweak.CompletedCount} completed"
            : $"Mode: {_tweak.GetCurrentMode().DisplayName} · {_tweak.Config.SortOrder.Count} sort rules";
        draw.AddCircleFilled(origin + new Vector2(10, 19), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(state)));
        FateChrome.DrawText(draw, textPos, FateChrome.FitText(primary, textRight - textPos.X), Theme.TextPrimary);
        FateChrome.DrawText(draw, textPos + new Vector2(0, 27), FateChrome.FitText(secondary, textRight - textPos.X), Theme.TextSecondary);
        DrawRunButton("footer_primary", actionPos, actionSize);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
    }

    private string BuildFateTooltip(PublicEvent fate, string displayName, bool isBlacklisted)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Display: {displayName}");

        foreach (var prop in TooltipProperties)
        {
            object? raw;
            try
            {
                raw = prop.GetValue(fate);
            }
            catch
            {
                continue;
            }

            var value = raw switch
            {
                null => "?",
                float f when prop.Name == nameof(PublicEvent.TimeRemaining) => FormatTimeRemaining(f),
                Vector3 v when prop.Name == nameof(PublicEvent.Position) => v.ToString(),
                bool b => b ? "True" : "False",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => raw.ToString() ?? "?",
            };
            sb.AppendLine($"{prop.Name}: {value}");
        }

        sb.AppendLine($"Blacklist: {(isBlacklisted ? "Yes" : "No")}");
        var (isEligible, failedConditions) = _tweak.GetFateConditionDetails(fate);
        sb.AppendLine($"Will be automated? {isEligible}");
        if (failedConditions.Count > 0)
        {
            sb.AppendLine("Blocked by:");
            foreach (var reason in failedConditions)
                sb.AppendLine($" - {reason}");
        }
        return sb.ToString().TrimEnd();
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
