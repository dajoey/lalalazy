using System;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace LazyFATEAutomator;

public class FATEAutomatorWindow : Window, IDisposable
{
    private readonly Plugin _plugin;

    public FATEAutomatorWindow(Plugin plugin)
        : base("Lazy FATE Automator###LazyFATEAutomatorMain")
    {
        _plugin = plugin;
        Size = new Vector2(520, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("LazyFateAutomatorTabs"))
        {
            if (ImGui.BeginTabItem("Dashboard")) { DrawDashboard();    ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Active FATEs")) { DrawActiveFates(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Sort Order")) { DrawSortOrder();   ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Settings"))    { DrawSettings();    ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    // ----------- Dashboard -----------

    private void DrawDashboard()
    {
        ImGui.Spacing();
        bool enabled = _plugin.StateController.IsEnabled;
        if (ImGui.Checkbox("Enable Auto-FATE Farming", ref enabled))
        {
            if (enabled) _plugin.StateController.Start();
            else _plugin.StateController.Stop();
        }
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("State: "); ImGui.SameLine();
        var state = _plugin.StateController.State;
        var color = state switch
        {
            GrindState.Idle             => ImGuiColors.DalamudGrey,
            GrindState.WaitingForFates  => ImGuiColors.DalamudOrange,
            GrindState.BetweenFates     => ImGuiColors.DalamudYellow,
            GrindState.Mounting         => ImGuiColors.ParsedBlue,
            GrindState.SwapZones        => ImGuiColors.ParsedPink,
            GrindState.Engaging         => ImGuiColors.HealerGreen,
            GrindState.Unconscious      => ImGuiColors.DalamudRed,
            _                           => ImGuiColors.DalamudWhite
        };
        ImGui.TextColored(color, state.ToString());

        ImGui.Text("Status: "); ImGui.SameLine();
        ImGui.TextUnformatted(_plugin.StateController.Status);

        ImGui.Text("Gluttony lease: "); ImGui.SameLine();
        ImGui.TextColored(_plugin.StateController.GluttonyLeaseHeld ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey,
            _plugin.StateController.GluttonyLeaseHeld ? "held" : "not held (manual combat)");

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

        ImGui.TextColored(ImGuiColors.DalamudWhite, "Active target:");
        var active = _plugin.FatesSolver.ActiveTarget;
        ImGui.Indent();
        if (active != null)
        {
            ImGui.Text($"Name: {active.Name}");
            ImGui.Text($"ID: {active.FateId}  Level: {active.Level}");
            ImGui.TextUnformatted($"Progress: {active.Progress}%");
            var t = (int)Math.Max(0, active.TimeRemaining);
            ImGui.Text($"Time remaining: {t / 60}m {t % 60}s");
        }
        else
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "(none)");
        }
        ImGui.Unindent();

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

        ImGui.TextColored(ImGuiColors.DalamudWhite, "Session:");
        ImGui.Indent();
        ImGui.Text($"Completed FATEs: {_plugin.StateController.CompletedFatesCount}");
        var dur = _plugin.StateController.IsEnabled
            ? DateTime.UtcNow - _plugin.StateController.SessionStartTime
            : TimeSpan.Zero;
        ImGui.Text($"Duration: {(int)dur.TotalHours}h {dur.Minutes}m {dur.Seconds}s");
        ImGui.Unindent();
    }

    // ----------- Active FATEs -----------

    private void DrawActiveFates()
    {
        ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudWhite, "FATEs in this zone:");
        ImGui.SameLine();
        ImGui.TextColored(ImGuiColors.DalamudGrey, "(sorted by configured priority; eligible first)");
        ImGui.Spacing();

        var rows = _plugin.FatesSolver.GetAllForDisplay().ToList();
        if (rows.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "No FATEs in zone.");
            return;
        }

        if (ImGui.BeginTable("ActiveFatesTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("Name",     ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Lv",       ImGuiTableColumnFlags.WidthFixed, 30);
            ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Bonus",    ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Distance", ImGuiTableColumnFlags.WidthFixed, 65);
            ImGui.TableSetupColumn("Action",   ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableHeadersRow();

            foreach (var (fate, eligible) in rows)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (!eligible) ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
                ImGui.TextUnformatted(fate.Name.ToString());
                if (!eligible) ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"FATE {fate.FateId}\nLevel {fate.Level}\n{fate.TimeRemaining:F0}s remaining");

                ImGui.TableNextColumn(); ImGui.Text(fate.Level.ToString());
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{fate.Progress}%");
                ImGui.TableNextColumn();
                if (fate.HasBonus) ImGui.TextColored(ImGuiColors.HealerGreen, "YES");
                else               ImGui.TextColored(ImGuiColors.DalamudGrey, "No");
                ImGui.TableNextColumn(); ImGui.Text($"{_plugin.Navigation.GetDistanceTo(fate.Position):F0}y");

                ImGui.TableNextColumn();
                bool isBlack = _plugin.Config.BlacklistedFateIds.Contains(fate.FateId);
                if (ImGui.SmallButton((isBlack ? "Unban##" : "Ban##") + fate.FateId))
                {
                    if (isBlack) _plugin.Config.BlacklistedFateIds.Remove(fate.FateId);
                    else         _plugin.Config.BlacklistedFateIds.Add(fate.FateId);
                    _plugin.SaveConfig();
                }
            }
            ImGui.EndTable();
        }
    }

    // ----------- Sort order -----------

    private void DrawSortOrder()
    {
        ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudWhite, "Priority order (first row = primary sort key):");
        ImGui.Spacing();

        var rules = _plugin.Config.SortRules;
        int? swapA = null, swapB = null;
        int? removeAt = null;
        bool changed = false;

        for (int i = 0; i < rules.Count; i++)
        {
            ImGui.PushID(i);
            var rule = rules[i];

            if (ImGui.ArrowButton("up", ImGuiDir.Up) && i > 0)   { swapA = i; swapB = i - 1; }
            ImGui.SameLine();
            if (ImGui.ArrowButton("dn", ImGuiDir.Down) && i < rules.Count - 1) { swapA = i; swapB = i + 1; }
            ImGui.SameLine();

            // Criterion combo
            int idx = (int)rule.Criteria;
            var names = Enum.GetNames(typeof(FateSortCriteria));
            ImGui.SetNextItemWidth(180);
            if (ImGui.Combo("##crit", ref idx, names, names.Length))
            {
                rule.Criteria = (FateSortCriteria)idx;
                changed = true;
            }
            ImGui.SameLine();

            bool desc = rule.Descending;
            if (ImGui.Checkbox("Descending", ref desc)) { rule.Descending = desc; changed = true; }
            ImGui.SameLine();
            if (ImGui.SmallButton("Remove")) removeAt = i;
            ImGui.PopID();
        }

        if (swapA.HasValue) { (rules[swapA.Value], rules[swapB!.Value]) = (rules[swapB.Value], rules[swapA.Value]); changed = true; }
        if (removeAt.HasValue) { rules.RemoveAt(removeAt.Value); changed = true; }

        ImGui.Spacing();
        if (ImGui.Button("Add criterion"))
        {
            rules.Add(new FateSortRule { Criteria = FateSortCriteria.Distance, Descending = false });
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset to defaults"))
        {
            rules.Clear();
            rules.Add(new FateSortRule { Criteria = FateSortCriteria.HasBonusWithTwist,   Descending = true });
            rules.Add(new FateSortRule { Criteria = FateSortCriteria.Progress,            Descending = true });
            rules.Add(new FateSortRule { Criteria = FateSortCriteria.HasBonus,            Descending = true });
            rules.Add(new FateSortRule { Criteria = FateSortCriteria.TimeRemainingUrgent, Descending = true });
            rules.Add(new FateSortRule { Criteria = FateSortCriteria.Distance,            Descending = false });
            changed = true;
        }

        if (changed) _plugin.SaveConfig();
    }

    // ----------- Settings -----------

    private void DrawSettings()
    {
        ImGui.Spacing();
        bool dirty = false;

        ImGui.TextColored(ImGuiColors.DalamudWhite, "FATE filters:");

        int v = _plugin.Config.MinTimeRemaining;
        ImGui.SetNextItemWidth(120);
        if (ImGui.DragInt("Min time remaining (s)", ref v, 5, 30, 600)) { _plugin.Config.MinTimeRemaining = v; dirty = true; }

        v = _plugin.Config.MaxProgress;
        ImGui.SetNextItemWidth(120);
        if (ImGui.SliderInt("Max progress (%)", ref v, 10, 100)) { _plugin.Config.MaxProgress = v; dirty = true; }

        v = _plugin.Config.MaxDuration;
        ImGui.SetNextItemWidth(120);
        if (ImGui.DragInt("Max duration (s)", ref v, 10, 300, 1800)) { _plugin.Config.MaxDuration = v; dirty = true; }

        v = _plugin.Config.MaxLevelDelta;
        ImGui.SetNextItemWidth(120);
        if (ImGui.SliderInt("Max FATE-level above me", ref v, 0, 5))  { _plugin.Config.MaxLevelDelta = v; dirty = true; }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("FATEs above your current level cannot be synced UP. Default 0.");

        v = _plugin.Config.MinTimeToPrioritise;
        ImGui.SetNextItemWidth(120);
        if (ImGui.DragInt("Urgent-when-under (s)", ref v, 10, 30, 600)) { _plugin.Config.MinTimeToPrioritise = v; dirty = true; }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("FATEs with less than this remaining are flagged 'urgent' for the TimeRemainingUrgent sort key.");

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudWhite, "Automation:");

        bool b = _plugin.Config.SwapZones;
        if (ImGui.Checkbox("Swap zones when dry", ref b)) { _plugin.Config.SwapZones = b; dirty = true; }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Lifestream-teleport to a random zone when no FATEs are active. Suppressed while you have Twist of Fate.");

        b = _plugin.Config.AutoSyncLevel;
        if (ImGui.Checkbox("Auto level-sync inside FATEs", ref b)) { _plugin.Config.AutoSyncLevel = b; dirty = true; }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudWhite, $"Blacklist ({_plugin.Config.BlacklistedFateIds.Count} items):");
        if (_plugin.Config.BlacklistedFateIds.Count > 0)
        {
            ImGui.Spacing();
            if (ImGui.Button("Clear all")) { _plugin.Config.BlacklistedFateIds.Clear(); _plugin.SaveConfig(); }
            ImGui.Spacing();
            if (ImGui.BeginChild("BL", new Vector2(0, 140), true))
            {
                foreach (var bid in _plugin.Config.BlacklistedFateIds.ToArray())
                {
                    if (ImGui.SmallButton($"Remove##{bid}")) { _plugin.Config.BlacklistedFateIds.Remove(bid); _plugin.SaveConfig(); }
                    ImGui.SameLine(); ImGui.Text($"FATE {bid}");
                }
                ImGui.EndChild();
            }
        }
        else
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "(empty)");
        }

        if (dirty) _plugin.SaveConfig();
    }

    public void Dispose() { }
}
