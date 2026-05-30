using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.ImGuiMethods;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LazyFateAutomation.Helpers.IPC;
using LazyFateAutomation.Helpers.Services;
using LazyFateAutomation.Helpers.Utils;
using LazyFateAutomation.Helpers.Extensions;

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

        using (var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 6f))
        using (var runButtonColor = ImRaii.PushColor(ImGuiCol.Button, _tweak.Running ? (uint)Colors.Negative : (uint)Colors.Positive)
            .Push(ImGuiCol.ButtonHovered, _tweak.Running ? (uint)Colors.NegativeHover : (uint)Colors.PositiveHover)
            .Push(ImGuiCol.ButtonActive, _tweak.Running ? (uint)Colors.NegativeActive : (uint)Colors.PositiveActive)) {

            if (ImGui.Button(_tweak.Running ? (_tweak.PendingStopWhenSafe ? "Stopping" : "Stop") : "Start")) {
                if (_tweak.Running) {
                    if (ImGui.GetIO().KeyCtrl) {
                        _tweak.PendingStopWhenSafe = true;
                    }
                    else {
                        _tweak.ToggleRunning();
                        Service.Navmesh.Stop();
                    }
                }
                else {
                    _tweak.ToggleRunning();
                }
            }
            TooltipOnHover(_tweak.Running, $"Stop. Ctrl+{SeIconChar.MouseLeftClick.ToIconString()} soft stop");

            ImGui.SameLine();
            DrawHeaderChip(
                $"Automation: {(_tweak.Running ? Service.Automation.Status : "Stopped")}",
                _tweak.Running ? Colors.ChipGold : Colors.ChipMuted,
                Colors.Grey2
            );

            ImGui.SameLine();
            DrawHeaderChip(
                $"State: {_tweak.CurrentState}",
                _tweak.Running && !_tweak.CurrentState.Equals("Idle", StringComparison.OrdinalIgnoreCase) ? Colors.ChipGold : Colors.ChipMuted,
                Colors.Grey2
            );

            ImGui.SameLine();
            DrawHeaderChip($"Completed: {_tweak.CompletedCount}", Colors.ChipInfo, Colors.Grey2);

            if (_tweak.RemainingUntilCompleted is { } remaining && remaining > 0) {
                ImGui.SameLine();
                DrawHeaderChip($"Remaining: {remaining}", Colors.ChipInfo, Colors.Grey2);
            }

            var modeRemaining = _tweak.GetCurrentMode().GetRemainingDisplay(_tweak);
            if (!string.IsNullOrEmpty(modeRemaining)) {
                ImGui.SameLine();
                var (bg, fg) = modeRemaining.Equals("Done", StringComparison.OrdinalIgnoreCase) ? (Colors.ChipMuted, Colors.Grey2) : (Colors.ChipInfo, Colors.Grey2);
                DrawHeaderChip(modeRemaining, bg, fg);
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
            using (var zoneButtonColor = ImRaii.PushColor(ImGuiCol.Text, _tweak.HasSelectedSwapZones ? (uint)Colors.Gold : ImGui.GetColorU32(ImGuiCol.Text))) {
                if (ImGuiComponents.IconButton("###ZoneSelector", FontAwesomeIcon.Globe))
                    _tweak.OpenZoneSelector();
            }
            if (_tweak.ModeSuppliesSwapZones)
                TooltipOnHover(ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled), "Zone list is defined by the current grind mode. Switch to None to select zones manually.");
            else if (_tweak.HasSelectedSwapZones)
                TooltipOnHover($"Swap Zones: {_tweak.SelectedSwapZones.Count}");
            else
                TooltipOnHover("Swap Zones (uses default swap behaviour if none selected)");

            if (minimised) {
                MinimisedContentWidth = Math.Max(400, leftContentRight - ImGui.GetWindowPos().X + leftRightGap + rightButtonWidth + style.WindowPadding.X * 2);
            }
        }

        if (_showSettings)
            DrawSettings();

        if (minimised)
            return;

        SpacedSeparator();

        if (_tweak.GetOrderedFates().ToList() is not { Count: > 0 } fates) {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No fates match the current filters.");
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
                    ImGui.Image(Svc.Texture.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(65001)).GetWrapOrEmpty().Handle, new Vector2(IconUnitHeight()));
                    ImGui.SameLine(0f, 0f);
                }

                using (var nameCol = ImRaii.PushColor(ImGuiCol.Text, isAvailable && !isBlacklisted ? (uint)EzColor.White : Colors.Grey3)) {
                    if (ImGui.Button(displayName, new Vector2(
                        fate.HasBonus
                            ? Math.Max(1f, nameWidth - IconUnitWidth())
                            : nameWidth,
                        0
                    ))) {
                        if (Service.Navmesh.IsRunning())
                            Service.Navmesh.Stop();
                        else
                            Service.Navmesh.PathfindAndMoveTo(fate.Position.RandomPoint(fate.Radius * 0.5f).OnMesh(), Svc.Condition[ConditionFlag.InFlight]);
                    }
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    _tweak.ToggleBlacklist(fate);

                TooltipOnHover(BuildFateTooltip(fate, displayName, isBlacklisted));
            }

            ImGui.SameLine();

            var percentage = fate.Progress / 100f;
            var progressLabel = $"{fate.Progress}%";

            var cursorPos = ImGui.GetCursorPos();
            var labelSize = ImGui.CalcTextSize(progressLabel);
            var textX = Math.Max(0f, progressWidth - labelSize.X - 4f);

            using (var color = ImRaii.PushColor(ImGuiCol.PlotHistogram, Config.BarColour))
                ImGui.ProgressBar(percentage, new Vector2(progressWidth, ImGui.GetFrameHeight()), "");

            ImGui.SetCursorPos(new Vector2(cursorPos.X + textX, cursorPos.Y + (ImGui.GetFrameHeight() - labelSize.Y) * 0.5f));
            ImGui.TextColored(
                GetProgressBarTextColor(Config.BarColour, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg], percentage, textX, labelSize.X, progressWidth),
                progressLabel
            );

            SpacedSeparator();
        }
    }

    private void DrawModeButton() {
        if (ImGuiComponents.IconButton("###GrindMode", FontAwesomeIcon.List))
            ImGui.OpenPopup("###GrindModePopup");
        TooltipOnHover($"Grind mode: {_tweak.GetCurrentMode().DisplayName}\nEXPERIMENTAL (didn't get to test non-gemstones)");

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

    private static void DrawHeaderChip(string text, EzColor background, EzColor textColor) {
        using var chipColor = ImRaii.PushColor(ImGuiCol.Button, (uint)background)
            .Push(ImGuiCol.ButtonHovered, (uint)background)
            .Push(ImGuiCol.ButtonActive, (uint)background)
            .Push(ImGuiCol.Text, (uint)textColor);
        ImGui.Button(text);
    }

    private void DrawSettings() {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1f), "Priority Order Configuration");
        ImGui.Spacing();
        ImGui.TextWrapped("Configure the order in which fates are prioritized. The order shown here is the order used by AvailableFates when selecting which fate to complete next.");
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1f), "Display Name");
        ImGui.Spacing();
        TextV("Format:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(400f);
        var fmt = Config.DisplayNameFormat;
        if (ImGui.InputText("###DisplayNameFormat", ref fmt, 256)) {
            Config.DisplayNameFormat = fmt;
            Config.Save();
        }
        ImGuiComponents.HelpMarker("Available tokens: {Level}, {Name}, {Id}, {Progress}, {TimeRemaining}, {Distance}, {State}");
        ImGui.Spacing();

        var sortOrder = Config.SortOrder.ToList();
        for (var i = 0; i < sortOrder.Count; i++) {
            using var id = ImRaii.PushId($"sort_{i}");
            var item = sortOrder[i];
            var criteria = item.Criteria;

            var handleSize = new Vector2(ImGui.GetFrameHeight());
            ImGui.Button($"##Drag{i}", handleSize);

            DragDropSource(i, "FATE_SORT_ITEM"u8, criteria.ToString().Replace("_", " "));
            DragDropTarget(i, "FATE_SORT_ITEM"u8, sortOrder.Count, (sourceIndex, insertIndex) => {
                var dragged = sortOrder[sourceIndex];
                sortOrder.RemoveAt(sourceIndex);
                if (sourceIndex < insertIndex)
                    insertIndex--;
                sortOrder.Insert(insertIndex, dragged);
                Config.SortOrder = sortOrder;
                Config.Save();
            });

            TooltipOnHover("Drag to change priority order");

            ImGui.SameLine();

            ImGui.SetNextItemWidth(200);
            using (var critCombo = ImRaii.Combo($"###Criteria{i}", criteria.ToString().Replace("_", " "))) {
                if (critCombo) {
                    foreach (var crit in Enum.GetValues<FateSortCriteria>()) {
                        if (ImGui.Selectable(crit.ToString().Replace("_", " "), crit == criteria)) {
                            item.Criteria = crit;
                            Config.SortOrder[i] = item;
                            Config.Save();
                        }
                    }
                }
            }

            ImGui.SameLine();
            var arrowIcon = item.Descending ? FontAwesomeIcon.ArrowDown : FontAwesomeIcon.ArrowUp;
            if (ImGuiComponents.IconButton($"###Dir{i}", arrowIcon)) {
                item.Descending = !item.Descending;
                Config.SortOrder[i] = item;
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(item.Descending ? "Descending (highest first)" : "Ascending (lowest first)");

            ImGui.SameLine();
            if (ImGuiComponents.IconButton($"###Remove{i}", FontAwesomeIcon.Trash)) {
                sortOrder.RemoveAt(i);
                Config.SortOrder = sortOrder;
                Config.Save();
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
                        Config.SortOrder = sortOrder;
                        Config.Save();
                    }
                }
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset to Default")) {
            Config.SortOrder =
            [
                new() { Criteria = FateSortCriteria.HasBonusWithTwist, Descending = true },
                new() { Criteria = FateSortCriteria.Progress, Descending = true },
                new() { Criteria = FateSortCriteria.HasBonus, Descending = true },
                new() { Criteria = FateSortCriteria.TimeRemainingUrgent, Descending = false },
                new() { Criteria = FateSortCriteria.Distance, Descending = false },
            ];
            Config.Save();
        }

        SpacedSeparator();
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
                    f >= 0 ? TimeSpan.FromSeconds(f).ToString(@"mm\:ss") : "∞",
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

    public string FormatDisplayName(PublicEvent fate) => Config.DisplayNameFormat
        .Replace("{Level}", fate.Level.ToString())
        .Replace("{Name}", fate.Name)
        .Replace("{Id}", fate.Id.ToString())
        .Replace("{Progress}", fate.Progress.ToString())
        .Replace("{TimeRemaining}", fate.TimeRemaining >= 0 ? TimeSpan.FromSeconds(fate.TimeRemaining).ToString(@"mm\:ss") : "∞")
        .Replace("{Distance}", Player.Available ? Player.DistanceTo(fate.Position).ToString("F1") : "?")
        .Replace("{State}", fate.State.ToString());
}
