using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace LazySkywardTracker;

// Converted to LalaUiKit per Projects/UI Refresh 2026-11/UI Brief (R1-R13).
// The Dalamud Window base auto-begins/ends this window (Draw() draws content only),
// so the top bar is drawn with the same kit primitives Layout.Shell uses, and the
// suite frame comes from Theme.PushFrame/PopFrame (LazyFoodBuff pattern). The plugin
// is a pure tracker: nothing runs in the background to stop, so the footer carries
// the live state without a Stop button (red stays reserved for Stop/Failed, R4),
// and R12 guardrails do not apply — the only action is Refresh, which makes the
// same read-only progress/inventory requests the old button made.

public sealed class TrackerWindow : Window
{
    private readonly Plugin _plugin;

    public TrackerWindow(Plugin plugin) : base("Lazy Skyward Tracker##lazysky")
    {
        _plugin = plugin;
        Size = new Vector2(550, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    // Everything the pill, cards and footer need, derived once per frame from the same
    // data the old draw read (achievement completions, progress cache, inventory
    // projections) — read-only, no behaviour change.
    private sealed record Live(
        UiState State,
        string Action,
        string? Reason,
        string? PillDetail,
        int Completed,
        uint TotalCurrent,
        uint TotalInventoryPoints);

    public override void Draw()
    {
        Theme.PushFrame();
        try
        {
            var projections = _plugin.Scanner.ScanInventory();

            // ── Overall progress calculations (unchanged behaviour) ─────────────
            uint totalCurrent = 0;
            uint totalMax = 5500000;
            uint totalInventoryPts = 0;
            int completedCount = 0;
            var turnInItems = 0;

            foreach (var id in Plugin.AchievementIds)
            {
                if (Plugin.IsAchievementCompleted(id))
                {
                    totalCurrent += 500000;
                    completedCount++;
                }
                else if (Plugin.ProgressCache.TryGetValue(id, out var progress))
                {
                    totalCurrent += Math.Min(progress.Current, 500000);
                    if (progress.Current >= 500000)
                        completedCount++;
                }

                if (projections.TryGetValue(id, out var proj))
                {
                    totalInventoryPts += proj.TotalPoints;
                    turnInItems += proj.Items.Count;
                }
            }

            var live = DeriveLive(completedCount, totalCurrent, totalInventoryPts);

            DrawTopBar(live);
            Layout.NavSection("lst_nav_tracker", "Tracker", null, FontAwesomeIcon.Mountain.ToIconString(), live.State, true);
            ImGui.Dummy(new Vector2(0, Theme.Unit));

            Layout.PageHeader(null, FontAwesomeIcon.Mountain.ToIconString(), "Skyward Tracker",
                "Tracks Skyward and Skybuilders' points for all jobs toward the Pteranodon mount.",
                $"{completedCount} of 11 jobs complete");

            DrawStatCards(live, totalMax, turnInItems);

            ImGui.Spacing();

            // Section header with the refresh control on the right (the old window kept
            // its Refresh button on this line too).
            Widgets.Title("Job progress");
            ImGui.SameLine();
            var refreshW = ImGui.CalcTextSize("Refresh progress").X + (Theme.FramePadding.X * 2);
            ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - refreshW - Theme.WindowPadding.X));
            if (ImGui.Button("Refresh progress"))
            {
                _plugin.RequestAllProgress();
                _plugin.Scanner.InvalidateCache();
            }

            if (!Plugin.ClientState.IsLoggedIn)
                Widgets.InfoBox("Log in to load Skyward achievement progress — the table fills in right after character select.", UiState.Blocked);
            else if (Plugin.ProgressCache.Count == 0)
                Widgets.InfoBox("No progress data yet — the game reports it shortly after login. Press Refresh progress to request it now.", UiState.Empty);

            DrawJobsTable(projections);

            DrawFooter(live);
        }
        finally
        {
            Theme.PopFrame();
        }
    }

    private Live DeriveLive(int completedCount, uint totalCurrent, uint totalInventoryPoints)
    {
        var pill = $"{completedCount} of 11";

        if (!Plugin.ClientState.IsLoggedIn)
            return new Live(UiState.Blocked, "waiting for you to log in", "no character", pill, completedCount, totalCurrent, totalInventoryPoints);

        if (Plugin.ProgressCache.Count == 0)
            return new Live(UiState.Empty, "awaiting first progress report", "press Refresh progress to request it now", pill, completedCount, totalCurrent, totalInventoryPoints);

        if (completedCount >= Plugin.AchievementIds.Length)
            return new Live(UiState.Ready, "all 11 jobs complete", "Pteranodon point goal met", pill, completedCount, totalCurrent, totalInventoryPoints);

        var reason = totalInventoryPoints > 0 ? $"+{totalInventoryPoints:N0} points waiting in your bags" : null;
        return new Live(UiState.Running, "tracking Skyward points", reason, pill, completedCount, totalCurrent, totalInventoryPoints);
    }

    private void DrawTopBar(Live live)
    {
        // Top bar: title left, live-state pill right (R3 place 1) — the same primitives
        // Layout.Shell draws for self-beginning windows.
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetWindowWidth() - (2 * Theme.Unit);
        var barH = Theme.PillHeight;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, p + new Vector2(w, barH), Theme.Pack(Theme.ShellBar), 4f);
        dl.AddRect(p, p + new Vector2(w, barH), Theme.Pack(Theme.SurfaceBorder), 4f);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(2 * Theme.Unit, (barH - ImGui.GetTextLineHeight()) / 2f));
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
        ImGui.TextUnformatted("Lazy Skyward Tracker");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPosX(p.X + w - (2 * Theme.Unit));
        Widgets.StatusPill(live.State, "Skyward", live.PillDetail, alignRight: true);
        ImGui.SetCursorPosY(p.Y + barH + Theme.ItemSpacing.Y);
    }

    private void DrawStatCards(Live live, uint totalMax, int turnInItems)
    {
        var width = (ImGui.GetContentRegionAvail().X - (2 * Theme.ItemSpacing.X)) / 3f;

        Widgets.StatCard("Overall progress", $"{live.TotalCurrent:N0}", $"{totalMax:N0}",
            (float)live.TotalCurrent / totalMax,
            live.TotalInventoryPoints > 0 ? $"+{live.TotalInventoryPoints:N0} from inventory turn-ins" : "no turn-in points in bags",
            width);

        Widgets.StatCard("Jobs complete", $"{live.Completed}", "11",
            (float)live.Completed / Plugin.AchievementIds.Length,
            live.Completed >= Plugin.AchievementIds.Length ? "Pteranodon point goal met" : $"{Plugin.AchievementIds.Length - live.Completed} jobs to go",
            width);

        Widgets.StatCard("Inventory turn-ins", live.TotalInventoryPoints > 0 ? $"+{live.TotalInventoryPoints:N0}" : "None", null, null,
            turnInItems > 0 ? $"{turnInItems} turn-in items in your bags" : "no turn-in items in bags",
            width);
    }

    private void DrawJobsTable(Dictionary<uint, InventoryScanner.InventoryProjection> projections)
    {
        if (!ImGui.BeginTable("skyward_jobs_table", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, -1)))
            return;

        ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableSetupColumn("Achievement", ImGuiTableColumnFlags.WidthFixed, 140);
        ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Points", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableHeadersRow();

        foreach (var id in Plugin.AchievementIds)
        {
            var info = Plugin.SkywardAchievements[id];
            bool isCompleted = Plugin.IsAchievementCompleted(id);
            uint current = 0;
            uint max = 500000;

            if (isCompleted)
            {
                current = 500000;
            }
            else if (Plugin.ProgressCache.TryGetValue(id, out var progress))
            {
                current = progress.Current;
                max = progress.Max > 0 ? progress.Max : 500000;
            }

            projections.TryGetValue(id, out var projection);
            uint invPts = projection?.TotalPoints ?? 0;
            var done = isCompleted || current >= max;

            ImGui.TableNextRow();

            // Column 1: Job with its live-state dot (R3/R4: state visible on every row).
            var rowState = done ? UiState.Ready
                : Plugin.ProgressCache.ContainsKey(id) ? (current > 0 ? UiState.Running : UiState.Idle)
                : UiState.Missing;
            ImGui.TableNextColumn();
            Widgets.StatusDot(rowState);
            ImGui.SameLine();
            ImGui.TextUnformatted(info.Job);

            // Column 2: Achievement name.
            ImGui.TableNextColumn();
            Widgets.Secondary(info.AchName);

            // Column 3: progress bar (accent while active, green only when done) with the
            // projected-inventory overlay (amber = adds points, green = would complete).
            ImGui.TableNextColumn();
            float fraction = (float)current / max;

            if (done)
            {
                Widgets.ProgressBar(1f, Theme.StateGreen);
            }
            else
            {
                Widgets.ProgressBar(fraction);

                if (invPts > 0)
                {
                    float projFrac = MathF.Min((current + invPts) / (float)max, 1f);
                    var overlayTint = current + invPts >= max
                        ? Theme.StateGreen with { W = 0.35f }
                        : Theme.StateAmber with { W = 0.35f };
                    DrawBarOverlay(fraction, projFrac, overlayTint);
                }
            }

            // Tooltip on the bar (kept 1:1 from the old window, kit colours).
            if (ImGui.IsItemHovered() && projection is { Items.Count: > 0 })
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted($"Inventory turn-ins (+{invPts:N0} points):");
                ImGui.Separator();
                foreach (var item in projection.Items)
                {
                    ImGui.TextUnformatted($"  {item.Quantity}x {item.ItemName}");
                    ImGui.SameLine();
                    Widgets.Secondary($"= {item.TotalPoints:N0} pts");
                }
                var projected = current + invPts;
                ImGui.Spacing();
                if (projected >= max)
                {
                    Widgets.StatusDot(UiState.Ready);
                    ImGui.SameLine();
                    ImGui.TextUnformatted($"Would complete this job ({projected:N0} / {max:N0})");
                }
                else
                {
                    ImGui.TextUnformatted($"Projected: {projected:N0} / {max:N0}");
                }
                ImGui.EndTooltip();
            }

            // Column 4: Points.
            ImGui.TableNextColumn();
            if (invPts > 0 && !done)
            {
                ImGui.TextUnformatted($"{current:N0}");
                ImGui.SameLine(0, 0);
                Widgets.Secondary($"+{invPts:N0}");
                ImGui.SameLine(0, 2);
                ImGui.TextUnformatted($"/ {max:N0}");
            }
            else
            {
                ImGui.TextUnformatted($"{current:N0} / {max:N0}");
            }
        }
        ImGui.EndTable();
    }

    private static void DrawFooter(Live live)
    {
        // Footer (R3 place 3): live dot, state word, current action and the one-line
        // reason. No Stop button — a tracker has nothing running to stop.
        ImGui.Dummy(new Vector2(0, Theme.Unit));
        ImGui.Separator();
        Widgets.StatusDot(live.State);
        ImGui.SameLine();
        var line = string.IsNullOrEmpty(live.Reason)
            ? $"{Copy.Word(live.State)} — {live.Action}"
            : $"{Copy.Word(live.State)} — {live.Action} — {live.Reason}";
        Widgets.Secondary(line);
    }

    /// <summary>
    /// Draws a semi-transparent state-coloured rectangle over the last ImGui item (a
    /// kit progress bar) from <paramref name="fromFrac"/> to <paramref name="toFrac"/>
    /// to visualize projected progress. Colours come from the kit's reserved state set.
    /// </summary>
    private static void DrawBarOverlay(float fromFrac, float toFrac, Vector4 tint)
    {
        toFrac = MathF.Min(toFrac, 1.0f);
        if (toFrac <= fromFrac) return;

        var barMin = ImGui.GetItemRectMin();
        var barMax = ImGui.GetItemRectMax();
        float barWidth = barMax.X - barMin.X;

        var drawList = ImGui.GetWindowDrawList();
        var color = ImGui.GetColorU32(tint);

        drawList.AddRectFilled(
            new Vector2(barMin.X + barWidth * fromFrac, barMin.Y),
            new Vector2(barMin.X + barWidth * toFrac, barMax.Y),
            color);
    }
}
