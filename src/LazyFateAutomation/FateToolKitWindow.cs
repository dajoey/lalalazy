using LazyFateAutomation.Helpers.ImGuiHelpers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using LalaUiKit;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LazyFateAutomation;

public class FateToolKitWindow : MinimisableWindow {
    private readonly FateToolKit _tweak;
    private bool _showSettings;

    private static readonly PropertyInfo[] _tooltipProperties;

    static FateToolKitWindow() {
        _tooltipProperties = [.. typeof(PublicEvent)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(p => p.Name)];
    }

    public FateToolKitWindow(FateToolKit tweak) : base($"Fate Tracker##{nameof(FateToolKitWindow)}") {
        _tweak = tweak;
        TitleBarButtons.Add(new TitleBarButton {
            Icon = FontAwesomeIcon.Cog,
            Click = _ => _showSettings = !_showSettings,
        });
    }

    protected override Vector2 MinimisedSize => new(700, 90);

    public override bool DrawConditions() => Player.Available;

    protected override void DrawContent(bool minimised) {
        _tweak.SyncRunningState();
        // Converted to LalaUiKit per Projects/UI Refresh 2026-11/UI Brief (R1-R13): the suite
        // frame comes from Theme.PushFrame/PopFrame, every colour from the kit (one accent,
        // reserved state colours) and spacing from the kit's 4 px grid. Live state (R3) is
        // derived from the tweak's own facts in OverallState().
        Theme.PushFrame();
        try {
            var (state, action) = OverallState();

            // Top bar (R3 place 1): title left, live-state pill right -- the same primitives
            // Layout.Shell draws for self-beginning windows. Skipped when minimised: the
            // Dalamud title bar already names the window and MinimisedSize(700, 90) has no
            // room for a second bar.
            if (!minimised)
                DrawTopBar(state);
            if (_tweak.Running) {
                if (Widgets.StopButton(_tweak.PendingStopWhenSafe ? "Stopping" : "Stop")) {
                    if (ImGui.GetIO().KeyCtrl) {
                        _tweak.PendingStopWhenSafe = true;
                    }
                    else {
                        _tweak.ToggleRunning();
                        Service.Navmesh.Stop();
                    }
                }
            }
            else {
                // Start: green = Ready to run (R4). The kit's StopButton covers the red side.
                using (ImRaii.PushColor(ImGuiCol.Button, Theme.Pack(Theme.StateGreen))
                    .Push(ImGuiCol.ButtonHovered, Theme.Pack(Theme.StateGreen with { W = 0.85f }))
                    .Push(ImGuiCol.ButtonActive, Theme.Pack(Theme.StateGreen with { W = 0.70f }))) {
                    if (ImGui.Button("Start")) {
                        _tweak.ToggleRunning();
                    }
                }
            }
            ImGui.SameLine();
            DrawHeaderChip(
                $"Automation: {(_tweak.Running ? Service.Automation.Status : "Stopped")}",
                state
            );

            ImGui.SameLine();
            DrawHeaderChip(
                $"State: {_tweak.CurrentState}",
                _tweak.Running && !_tweak.CurrentState.Equals("Idle", StringComparison.OrdinalIgnoreCase) ? UiState.Running : UiState.Idle
            );

            ImGui.SameLine();
            DrawHeaderChip($"Completed: {_tweak.CompletedCount}");

            if (_tweak.RemainingUntilCompleted is { } remaining && remaining > 0) {
                ImGui.SameLine();
                DrawHeaderChip($"Remaining: {remaining}");
            }

            var modeRemaining = _tweak.GetCurrentMode().GetRemainingDisplay(_tweak);
            if (!string.IsNullOrEmpty(modeRemaining)) {
                ImGui.SameLine();
                DrawHeaderChip(modeRemaining, modeRemaining.Equals("Done", StringComparison.OrdinalIgnoreCase) ? UiState.Ready : UiState.Running);
            }

            ImGui.SameLine();
            var style = ImGui.GetStyle();
            var rightButtonWidth = (ImGui.GetFrameHeight() + style.FramePadding.X * 2f) * 2f + style.ItemSpacing.X;
            var leftRightGap = style.ItemSpacing.X;
            var leftContentRight = ImGui.GetItemRectMax().X;
            if (Math.Max(0f, ImGui.GetContentRegionAvail().X - rightButtonWidth) is > 0 and var spacer) {
                ImGui.Dummy(new Vector2(spacer, 0f));
                ImGui.SameLine();
            }
            DrawModeButton();
            ImGui.SameLine();
            using (var _ = ImRaii.Disabled(_tweak.ModeSuppliesSwapZones))
            using (var zoneButtonColor = ImRaii.PushColor(ImGuiCol.Text, _tweak.HasSelectedSwapZones ? Theme.Pack(Theme.Accent) : ImGui.GetColorU32(ImGuiCol.Text))) {
                if (ImGuiComponents.IconButton("###ZoneSelector", FontAwesomeIcon.Globe))
                    _tweak.OpenZoneSelector();
            }
            if (_tweak.ModeSuppliesSwapZones)
                ImGui.TooltipOnHover(ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled), "Zone list is defined by the current grind mode. Switch to None to select zones manually.");
            else if (_tweak.HasSelectedSwapZones)
                ImGui.TooltipOnHover($"Swap Zones: {_tweak.SelectedSwapZones.Count}");
            else
                ImGui.TooltipOnHover("Swap Zones (uses default swap behaviour if none selected)");

            if (minimised) {
                MinimisedContentWidth = Math.Max(400, leftContentRight - ImGui.GetWindowPos().X + leftRightGap + rightButtonWidth + style.WindowPadding.X * 2);
            }

            if (_showSettings)
                DrawSettings();

            if (minimised)
                return;

            ImGui.SpacedSeparator();

            if (_tweak.GetOrderedFates().ToList() is not { Count: > 0 } fates) {
                Widgets.Secondary("No fates match the current filters.");
                return;
            }

            foreach (var (fate, isAvailable) in fates) {
                using var id = ImRaii.PushId($"fate_{fate.Id}");

                var availableWidth = ImGui.GetContentRegionAvail().X;
                var displayName = FormatDisplayName(fate);
                var nameWidth = Math.Min(200f.Scale(), availableWidth * 0.4f);
                var progressWidth = Math.Max(1f, availableWidth - nameWidth - ImGui.GetStyle().ItemSpacing.X);

                using (var buttonStyle = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f)))
                using (var color = ImRaii.PushColor(ImGuiCol.Button, 0).Push(ImGuiCol.ButtonHovered, ImGui.GetColorU32(ImGuiCol.ButtonHovered)).Push(ImGuiCol.ButtonActive, ImGui.GetColorU32(ImGuiCol.ButtonActive))) {
                    var isBlacklisted = _tweak.IsBlacklisted(fate);

                    if (fate.HasBonus) {
                        ImGui.Image(Svc.Texture.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(65001)).GetWrapOrEmpty().Handle, new Vector2(ImGui.IconUnitHeight()));
                        ImGui.SameLine(0f, 0f);
                    }

                    using (var nameCol = ImRaii.PushColor(ImGuiCol.Text, isAvailable && !isBlacklisted ? Theme.Pack(Theme.TextPrimary) : Theme.Pack(Theme.TextSecondary))) {
                        if (ImGui.Button(displayName, new Vector2(
                            fate.HasBonus
                                ? Math.Max(1f, nameWidth - ImGui.IconUnitWidth())
                                : nameWidth,
                            0
                        ))) {
                            if (Svc.Navmesh.IsRunning())
                                Svc.Navmesh.Stop();
                            else
                                Svc.Navmesh.PathfindAndMoveTo(fate.Position.RandomPoint(fate.Radius * 0.5f).OnMesh(), Svc.Condition[ConditionFlag.InFlight]);
                        }
                    }

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        _tweak.ToggleBlacklist(fate);

                    ImGui.TooltipOnHover(BuildFateTooltip(fate, displayName, isBlacklisted));
                }

                ImGui.SameLine();

                var percentage = fate.Progress / 100f;
                var progressLabel = $"{fate.Progress}%";

                var cursorPos = ImGui.GetCursorPos();
                var labelSize = ImGui.CalcTextSize(progressLabel);
                var textX = Math.Max(0f, progressWidth - labelSize.X - 4f);

                using (var color = ImRaii.PushColor(ImGuiCol.PlotHistogram, _tweak.Config.BarColour))
                    ImGui.ProgressBar(percentage, new Vector2(progressWidth, ImGui.GetFrameHeight()), "");

                ImGui.SetCursorPos(new Vector2(cursorPos.X + textX, cursorPos.Y + (ImGui.GetFrameHeight() - labelSize.Y) * 0.5f));
                ImGui.TextColored(
                    ImGui.GetProgressBarTextColor(_tweak.Config.BarColour, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg], percentage, textX, labelSize.X, progressWidth),
                    progressLabel
                );

                ImGui.SpacedSeparator();
            }

            // Footer (R3 place 3): the automation runs in the background, so the footer carries
            // the live state, the current action and the Stop control pinned right. Hard stop:
            // the same path the header button takes without Ctrl.
            Layout.Footer(state, action, null, _tweak.PendingStopWhenSafe ? "Stopping" : "Stop", out var stop);
            if (stop && _tweak.Running) {
                _tweak.ToggleRunning();
                Service.Navmesh.Stop();
            }
        }
        finally {
            Theme.PopFrame();
        }
    }

    private (UiState State, string Action) OverallState() {
        if (!_tweak.Running)
            return (UiState.Idle, "press Start to grind fates");
        if (_tweak.PendingStopWhenSafe)
            return (UiState.Running, "stopping when the current fate ends safely");
        return (UiState.Running, "grinding fates");
    }

    private void DrawTopBar(UiState state) {
        // Top bar (R3 place 1): title left, live-state pill right -- the same primitives
        // Layout.Shell draws for self-beginning windows.
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetWindowWidth() - (2 * Theme.Unit);
        var barH = Theme.PillHeight;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, p + new Vector2(w, barH), Theme.Pack(Theme.ShellBar), 4f);
        dl.AddRect(p, p + new Vector2(w, barH), Theme.Pack(Theme.SurfaceBorder), 4f);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(2 * Theme.Unit, (barH - ImGui.GetTextLineHeight()) / 2f));
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
        ImGui.TextUnformatted("Fate Tracker");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPosX(p.X + w - (2 * Theme.Unit));
        Widgets.StatusPill(state, "Fate Tracker", alignRight: true);
        ImGui.SetCursorPosY(p.Y + barH + Theme.ItemSpacing.Y);
    }

    private static void DrawHeaderChip(string text, UiState? mark = null) {
        // Header chip in the kit's pill shape: Surface fill, border, kit text; the optional
        // dot carries the live state (R3 place 2) in the kit's mark colour.
        var p = ImGui.GetCursorScreenPos();
        var pad = 2 * Theme.Unit;
        var textW = ImGui.CalcTextSize(text).X;
        var dotW = mark is null ? 0f : (Theme.DotRadius * 2) + Theme.Unit;
        var w = pad + dotW + textW + pad;
        var h = Theme.PillHeight;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, p + new Vector2(w, h), Theme.Pack(Theme.Surface), h / 2f);
        dl.AddRect(p, p + new Vector2(w, h), Theme.Pack(Theme.SurfaceBorder), h / 2f);
        var textX = p.X + pad + dotW;
        if (mark is { } m) {
            var dotC = new Vector2(p.X + pad + Theme.DotRadius, p.Y + (h / 2f));
            dl.AddCircleFilled(dotC, Theme.DotRadius, Theme.Pack(Copy.MarkColor(m)));
        }
        dl.AddText(new Vector2(textX, p.Y + ((h - ImGui.GetTextLineHeight()) / 2f)), Theme.Pack(Theme.TextPrimary), text);
        ImGui.Dummy(new Vector2(w, h));
    }

    private void DrawModeButton() {
        if (ImGuiComponents.IconButton("###GrindMode", FontAwesomeIcon.List))
            ImGui.OpenPopup("###GrindModePopup");
        ImGui.TooltipOnHover($"Grind mode: {_tweak.GetCurrentMode().DisplayName}\nEXPERIMENTAL (didn't get to test non-gemstones)");

        using var popup = ImRaii.Popup("###GrindModePopup");
        if (popup) {
            foreach (var mode in FateGrindModes.All) {
                if (ImGui.Selectable(mode.DisplayName, mode.DisplayName == _tweak.SelectedModeId)) {
                    _tweak.SelectedModeId = mode.DisplayName;
                    ImGui.CloseCurrentPopup();
                }
            }
        }
    }


    private static readonly (FateRule Rule, string Label)[] FateRuleLabels = [
        (FateRule.Normal, "Kill & boss FATEs"),
        (FateRule.Collect, "Item collection (turn-in)"),
        (FateRule.Escort, "Escort"),
        (FateRule.Defend, "Defend"),
        (FateRule.EventFate, "Seasonal event"),
        (FateRule.Chase, "Chase"),
        (FateRule.ConcertedWorks, "Firmament: Concerted Works"),
        (FateRule.Fete, "Firmament: Fete"),
    ];

    private string _zoneFilter = "";

    private void DrawSettings() {
        DrawFateSelectionSection();
        DrawCurrencyFocusSection();
        DrawZonesSection();
        DrawSortingSection();
    }

    private void DrawFateSelectionSection() {
        if (!ImGui.CollapsingHeader("FATE Selection", ImGuiTreeNodeFlags.DefaultOpen)) return;

        var prioritizeForlorn = _tweak.Config.PrioritizeForlornMaidens;
        if (Widgets.Checkbox("Prioritize Forlorn Maidens", ref prioritizeForlorn)) {
            _tweak.Config.PrioritizeForlornMaidens = prioritizeForlorn;
            _tweak.Config.Save();
        }
        ImGuiComponents.HelpMarker("When a Forlorn Maiden or the Forlorn appears mid-FATE, combat targets it instead of the FATE's normal enemies until it's defeated or disappears.");

        ImGui.Spacing();
        Widgets.SecondaryWrapped("Unchecked FATE types are never started. Boss FATEs and trash-kill FATEs cannot be separated (both are 'kill & boss' in the game data). Bozja / Occult Crescent skirmishes and cosmic exploration events count as kill & boss.");
        ImGui.Spacing();

        var excluded = _tweak.Config.ExcludedFateRules;
        for (var i = 0; i < FateRuleLabels.Length; i++) {
            var (rule, label) = FateRuleLabels[i];
            using var id = ImRaii.PushId($"rule_{rule}");
            var allowed = !excluded.Contains(rule);
            if (Widgets.Checkbox(label, ref allowed)) {
                if (allowed)
                    excluded.Remove(rule);
                else
                    excluded.Add(rule);
                _tweak.Config.Save();
            }
            if (i % 2 == 0 && i < FateRuleLabels.Length - 1)
                ImGui.SameLine();
        }
        ImGui.Spacing();
    }

    private void DrawCurrencyFocusSection() {
        if (!ImGui.CollapsingHeader("Currency Focus", ImGuiTreeNodeFlags.DefaultOpen)) return;

        Widgets.SecondaryWrapped("Focus the automation on a FATE reward currency: zone swaps prefer zones whose FATEs reward it.");
        ImGui.Spacing();

        var focus = _tweak.Config.CurrencyFocus;
        ImGuiEx.TextV("Focus currency:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(260f);
        using (var combo = ImRaii.Combo("###CurrencyFocus", FateToolKit.CurrencyName(focus))) {
            if (combo) {
                foreach (var currency in Enum.GetValues<FateCurrency>()) {
                    if (ImGui.Selectable(FateToolKit.CurrencyName(currency), currency == focus) && focus != currency) {
                        _tweak.Config.CurrencyFocus = currency;
                        _tweak.Config.Save();
                    }
                }
            }
        }
        ImGuiComponents.HelpMarker("Company Seals: A Realm Reborn, Heavensward and Stormblood zones.\nBicolor Gemstones: Shadowbringers and later zones.\nYo-kai Medals: the Yo-kai Watch event zones (watch equipped and the matching minion out).\nA grind mode with its own zone list still wins over the currency focus.");

        if (focus != FateCurrency.None) {
            var fallback = _tweak.Config.CurrencyFocusFallback;
            ImGuiEx.TextV("When the focused zones have nothing to run:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(260f);
            using (var combo = ImRaii.Combo("###CurrencyFocusFallback", CurrencyFallbackLabel(fallback))) {
                if (combo) {
                    foreach (var option in Enum.GetValues<CurrencyFocusFallback>()) {
                        if (ImGui.Selectable(CurrencyFallbackLabel(option), option == fallback) && fallback != option) {
                            _tweak.Config.CurrencyFocusFallback = option;
                            _tweak.Config.Save();
                        }
                    }
                }
            }
            ImGuiComponents.HelpMarker("Zone swaps first pass through every focused zone. If a full pass finds no FATE to run:\nContinue normally: swap like usual (achievement-guided / same expansion) and grind whatever FATEs are up.\nIdle in zone: stay put and wait for focused-currency FATEs instead of grinding other FATEs.");
        }
        ImGui.Spacing();
    }

    private static string CurrencyFallbackLabel(CurrencyFocusFallback fallback) => fallback switch {
        CurrencyFocusFallback.Idle => "Idle in zone",
        _ => "Continue normally",
    };

    private void DrawZonesSection() {
        if (!ImGui.CollapsingHeader("Swap Zones", ImGuiTreeNodeFlags.DefaultOpen)) return;

        if (_tweak.ModeSuppliesSwapZones)
            Widgets.SecondaryWrapped($"Grind mode '{_tweak.GetCurrentMode().DisplayName}' defines the zone list. Uncheck zones to exclude them from rotation.");
        else if (_tweak.Config.CurrencyFocus != FateCurrency.None)
            Widgets.SecondaryWrapped($"Currency focus '{FateToolKit.CurrencyName(_tweak.Config.CurrencyFocus)}' defines the zone list. Uncheck zones to exclude them from rotation.");
        else if (_tweak.HasSelectedSwapZones)
            Widgets.SecondaryWrapped($"Manual selection: {_tweak.SelectedSwapZones.Count} zones. Uncheck a zone to remove it from rotation.");
        else
            Widgets.SecondaryWrapped("Default rotation (achievement-guided). Check zones to build a manual list, or leave everything unchecked to keep the default.");

        ImGui.Spacing();
        ImGui.SetNextItemWidth(260f);
        ImGui.InputTextWithHint("###ZoneFilter", "Filter zones...", ref _zoneFilter, 64);

        var rawPool = _tweak.GetRawSwapZones();
        if (rawPool is { Count: > 0 } || _tweak.HasSelectedSwapZones) {
            ImGui.SameLine();
            if (ImGui.Button("Select All")) {
                _tweak.Config.ExcludedSwapZones.Clear();
                if (rawPool == null && _tweak.HasSelectedSwapZones)
                    foreach (var zone in FateZones.All.Where(z => z.ZoneClass == FateZoneLogic.ZoneClass.FateZone))
                        _tweak.Config.SelectedSwapZones.Add(zone.TerritoryId);
                _tweak.Config.Save();
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear All")) {
                if (rawPool != null)
                    foreach (var z in rawPool)
                        _tweak.Config.ExcludedSwapZones.Add(z);
                else
                    foreach (var zone in FateZones.All.Where(z => z.ZoneClass == FateZoneLogic.ZoneClass.FateZone)) {
                        _tweak.Config.SelectedSwapZones.Add(zone.TerritoryId);
                        _tweak.Config.ExcludedSwapZones.Add(zone.TerritoryId);
                    }
                _tweak.Config.Save();
            }
            if (!_tweak.ManualZonesOverridden) {
                ImGui.SameLine();
                if (ImGui.Button("Edit Selected Zones..."))
                    _tweak.OpenZoneSelector();
            }
        }
        else if (!_tweak.ManualZonesOverridden) {
            ImGui.SameLine();
            if (ImGui.Button("Select Allowed Swap Zones..."))
                _tweak.OpenZoneSelector();
        }
        ImGui.Spacing();

        foreach (var group in FateZones.ByExpansion) {
            var zones = group.Where(z => ZoneNameMatches(z.Name)).ToList();
            if (zones.Count == 0) continue;
            var allowedCount = zones.Count(z => IsZoneChecked(z.TerritoryId));

            if (string.IsNullOrEmpty(_zoneFilter))
                ImGui.SetNextItemOpen(true, ImGuiCond.FirstUseEver);
            else
                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            if (!ImGui.TreeNode($"{group.Key.Name} ({allowedCount}/{zones.Count})###expansion_{group.Key.Id}"))
                continue;

            if (ImGui.SmallButton($"All##exp_all_{group.Key.Id}"))
                SetZonesChecked(zones, true);
            ImGui.SameLine();
            if (ImGui.SmallButton($"None##exp_none_{group.Key.Id}"))
                SetZonesChecked(zones, false);
            ImGui.SameLine();
            ImGui.TextDisabled($"({zones.Count(zone => zone.ZoneClass != FateZoneLogic.ZoneClass.FateZone)} no teleport)");

            for (var i = 0; i < zones.Count; i++) {
                var zone = zones[i];
                using var id = ImRaii.PushId($"zone_{group.Key.Id}_{zone.TerritoryId}");
                var isChecked = IsZoneChecked(zone.TerritoryId);
                if (Widgets.Checkbox(zone.Name, ref isChecked))
                    SetZonesChecked([zone], isChecked);
                if (zone.ZoneClass == FateZoneLogic.ZoneClass.NoTeleportFateZone && ImGui.IsItemHovered())
                    ImGui.SetTooltip("No aetheryte to teleport into this zone; it is never picked by automatic zone swaps.");
                if (i % 2 == 0 && i < zones.Count - 1)
                    ImGui.SameLine();
            }
            ImGui.TreePop();
        }

        ImGui.Spacing();
    }

    private bool ZoneNameMatches(string name)
        => string.IsNullOrEmpty(_zoneFilter) || name.Contains(_zoneFilter, StringComparison.OrdinalIgnoreCase);

    private bool IsZoneChecked(uint zoneId) {
        var rawPool = _tweak.GetRawSwapZones();
        if (rawPool != null)
            return !_tweak.Config.ExcludedSwapZones.Contains(zoneId);
        if (_tweak.Config.SelectedSwapZones.Count > 0)
            return _tweak.Config.SelectedSwapZones.Contains(zoneId) && !_tweak.Config.ExcludedSwapZones.Contains(zoneId);
        // default rotation: exclusions render as unchecked and are materialized on the first toggle
        return !_tweak.Config.ExcludedSwapZones.Contains(zoneId);
    }

    private void SetZonesChecked(IEnumerable<FateZone> zones, bool value) {
        var selected = _tweak.Config.SelectedSwapZones;
        var excluded = _tweak.Config.ExcludedSwapZones;
        var rawPool = _tweak.GetRawSwapZones();
        var ids = zones.Where(z => z.ZoneClass == FateZoneLogic.ZoneClass.FateZone).Select(z => z.TerritoryId).ToList();

        if (rawPool != null) {
            // pool comes from the grind mode or currency focus: checkboxes edit the exclusions
            foreach (var id in ids) {
                if (value) excluded.Remove(id);
                else excluded.Add(id);
            }
        }
        else if (selected.Count > 0) {
            // manual selection: checkboxes edit the selection
            foreach (var id in ids) {
                if (value) {
                    selected.Add(id);
                    excluded.Remove(id);
                }
                else {
                    selected.Remove(id);
                }
            }
        }
        else if (value) {
            // default rotation, allowing a zone: just clear any stale exclusion
            foreach (var id in ids)
                excluded.Remove(id);
        }
        else {
            // default rotation, disallowing a zone: materialize the manual selection
            // (all swappable zones except the disallowed ones, honoring existing exclusions)
            var removed = ids.ToHashSet();
            selected.Clear();
            foreach (var zone in FateZones.All)
                if (zone.ZoneClass == FateZoneLogic.ZoneClass.FateZone && !removed.Contains(zone.TerritoryId) && !excluded.Contains(zone.TerritoryId))
                    selected.Add(zone.TerritoryId);
        }
        _tweak.Config.Save();
    }

    private void DrawSortingSection() {
        if (!ImGui.CollapsingHeader("Sorting & Display")) return;

        var showDtr = _tweak.Config.ShowServerBarEntry;
        if (Widgets.Checkbox("Show server info bar entry", ref showDtr)) {
            _tweak.Config.ShowServerBarEntry = showDtr;
            _tweak.Config.Save();
        }
        ImGuiComponents.HelpMarker("The FATE bot's On/Off switch in the game's server info bar; Umbra renders it as a button (\"Lazy Fate Automation\"). Starting from it needs a confirming second click. It can also be hidden per-plugin in Umbra or under /xlsettings -> Server Info Bar.");
        ImGui.Spacing();

        Widgets.Title("Priority Order Configuration");
        ImGui.Spacing();
        Widgets.SecondaryWrapped("Configure the order in which fates are prioritized. The order shown here is the order used by AvailableFates when selecting which fate to complete next.");
        ImGui.Spacing();

        Widgets.Caption("Display Name");
        ImGui.Spacing();
        ImGuiEx.TextV("Format:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(400f);
        if (ImGui.InputText("###DisplayNameFormat", ref _tweak.Config.DisplayNameFormat, 256))
            _tweak.Config.Save();
        ImGuiComponents.HelpMarker("Available tokens: {Level}, {Name}, {Id}, {Progress}, {TimeRemaining}, {Distance}, {State}");
        ImGui.Spacing();

        var sortOrder = _tweak.Config.SortOrder.ToList();
        for (var i = 0; i < sortOrder.Count; i++) {
            using var id = ImRaii.PushId($"sort_{i}");
            var item = sortOrder[i];
            var criteria = item.Criteria;

            var handleSize = new Vector2(ImGui.GetFrameHeight());
            ImGui.Button($"##Drag{i}", handleSize);

            ImGui.DragDropSource(i, "FATE_SORT_ITEM"u8, criteria.ToString().Replace("_", " "));
            ImGui.DragDropTarget(i, "FATE_SORT_ITEM"u8, sortOrder.Count, (sourceIndex, insertIndex) => {
                var dragged = sortOrder[sourceIndex];
                sortOrder.RemoveAt(sourceIndex);
                if (sourceIndex < insertIndex)
                    insertIndex--;
                sortOrder.Insert(insertIndex, dragged);
                _tweak.Config.SortOrder = sortOrder;
                _tweak.Config.Save();
            });

            ImGui.TooltipOnHover("Drag to change priority order");

            ImGui.SameLine();

            ImGui.SetNextItemWidth(200);
            using (var critCombo = ImRaii.Combo($"###Criteria{i}", criteria.ToString().Replace("_", " "))) {
                if (critCombo) {
                    foreach (var crit in Enum.GetValues<FateSortCriteria>()) {
                        if (ImGui.Selectable(crit.ToString().Replace("_", " "), crit == criteria)) {
                            item.Criteria = crit;
                            _tweak.Config.SortOrder[i] = item;
                            _tweak.Config.Save();
                        }
                    }
                }
            }

            ImGui.SameLine();
            var arrowIcon = item.Descending ? FontAwesomeIcon.ArrowDown : FontAwesomeIcon.ArrowUp;
            if (ImGuiComponents.IconButton($"###Dir{i}", arrowIcon)) {
                item.Descending = !item.Descending;
                _tweak.Config.SortOrder[i] = item;
                _tweak.Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(item.Descending ? "Descending (highest first)" : "Ascending (lowest first)");

            ImGui.SameLine();
            if (ImGuiComponents.IconButton($"###Remove{i}", FontAwesomeIcon.Trash)) {
                sortOrder.RemoveAt(i);
                _tweak.Config.SortOrder = sortOrder;
                _tweak.Config.Save();
                i--;
                continue;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove this sort criteria");
        }

        ImGui.Spacing();
        if (ImGui.Button("Add Sort Criteria"))
            ImGui.OpenPopup("###AddSortCriteria");

        using (var popup = ImRaii.Popup("###AddSortCriteria")) {
            if (popup) {
                foreach (var crit in Enum.GetValues<FateSortCriteria>()) {
                    if (ImGui.Selectable(crit.ToString().Replace("_", " "))) {
                        sortOrder.Add(new FateSortOrder { Criteria = crit, Descending = true });
                        _tweak.Config.SortOrder = sortOrder;
                        _tweak.Config.Save();
                    }
                }
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset to Default")) {
            _tweak.Config.SortOrder = Configuration.DefaultSortOrder();
            _tweak.Config.Save();
        }

        ImGui.Spacing();
    }

    private string BuildFateTooltip(PublicEvent fate, string displayName, bool isBlacklisted) {
        var sb = new StringBuilder();

        sb.AppendLine($"Display: {displayName}");

        foreach (var prop in _tooltipProperties) {
            object? raw;
            try {
                raw = prop.GetValue(fate);
            }
            catch {
                continue;
            }

            var value = raw switch {
                null => "?",
                float f when prop.Name == nameof(PublicEvent.TimeRemaining) =>
                    f >= 0 ? TimeSpan.FromSeconds(f).ToString(@"mm\:ss") : "âˆž",
                Vector3 v when prop.Name == nameof(PublicEvent.Position) =>
                    v.ToString(),
                bool b => b ? "True" : "False",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => raw.ToString() ?? "?"
            };

            sb.AppendLine($"{prop.Name}: {value}");
        }

        sb.AppendLine($"Blacklist: {(isBlacklisted ? "Yes" : "No")}");

        var (isEligible, failedConditions) = _tweak.GetFateConditionDetails(fate);
        sb.AppendLine($"Will be automated? {isEligible}");
        if (failedConditions.Count > 0) {
            sb.AppendLine("Blocked by:");
            foreach (var reason in failedConditions)
                sb.AppendLine($" - {reason}");
        }

        return sb.ToString().TrimEnd();
    }

    public string FormatDisplayName(PublicEvent fate) => _tweak.Config.DisplayNameFormat
        .Replace("{Level}", fate.Level.ToString())
        .Replace("{Name}", fate.Name)
        .Replace("{Id}", fate.Id.ToString())
        .Replace("{Progress}", fate.Progress.ToString())
        .Replace("{TimeRemaining}", fate.TimeRemaining >= 0 ? TimeSpan.FromSeconds(fate.TimeRemaining).ToString(@"mm\:ss") : "âˆž")
        .Replace("{Distance}", Player.Available ? Player.DistanceTo(fate.Position).ToString("F1") : "?")
        .Replace("{State}", fate.State.ToString());
}
