using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace LazySkywardTracker;

public sealed class TrackerWindow : Window
{
    private const uint PointsPerJob = 500000;
    private const uint TotalPointGoal = 5500000;
    private const float WideFooterHeight = 64;
    private const float NarrowFooterHeight = 84;

    private readonly Plugin _plugin;

    public TrackerWindow(Plugin plugin) : base("Lazy Skyward Tracker##lazysky")
    {
        _plugin = plugin;
        Flags = ImGuiWindowFlags.NoTitleBar;
        Size = new Vector2(1180, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 360),
            MaximumSize = new Vector2(1600, 1400),
        };
    }

    private sealed record Live(
        UiState State,
        string Action,
        string? Reason,
        int Completed,
        uint TotalCurrent,
        uint TotalInventoryPoints);

    private readonly record struct Stat(string Label, string Value, string? Cap, float? Fraction, string Note);

    private readonly record struct JobColumns(float AchievementX, float ProgressX, float ProgressWidth);

    public override void Draw()
    {
        Theme.PushFrame();
        try
        {
            var projections = _plugin.Scanner.ScanInventory();
            var loggedIn = Plugin.ClientState.IsLoggedIn;

            uint totalCurrent = 0;
            uint totalInventoryPts = 0;
            var completedCount = 0;
            var turnInItems = 0;

            foreach (var id in Plugin.AchievementIds)
            {
                if (Plugin.IsAchievementCompleted(id))
                {
                    totalCurrent += PointsPerJob;
                    completedCount++;
                }
                else if (Plugin.ProgressCache.TryGetValue(id, out var progress))
                {
                    totalCurrent += Math.Min(progress.Current, PointsPerJob);
                    if (progress.Current >= PointsPerJob)
                        completedCount++;
                }

                if (projections.TryGetValue(id, out var projection))
                {
                    totalInventoryPts += projection.TotalPoints;
                    turnInItems += projection.Items.Count;
                }
            }

            var live = DeriveLive(loggedIn, Plugin.ProgressCache.Count, completedCount, totalCurrent, totalInventoryPts);
            DrawTopBar(live);
            DrawPageHeader(live);

            var footerHeight = FooterHeight(ImGui.GetContentRegionAvail().X);
            var bodyHeight = MathF.Max(80, ImGui.GetContentRegionAvail().Y - footerHeight);
            if (ImGui.BeginChild("##skyward_body", new Vector2(0, bodyHeight), false))
            {
                DrawStatCards(live, turnInItems);
                DrawJobsCard(projections, loggedIn);
            }
            ImGui.EndChild();

            DrawFooter(live);
        }
        finally
        {
            Theme.PopFrame();
        }
    }

    private static Live DeriveLive(bool loggedIn, int progressReports, int completedCount, uint totalCurrent, uint totalInventoryPoints)
    {
        if (!loggedIn)
            return new Live(UiState.Blocked, "waiting for you to log in", "no character", completedCount, totalCurrent, totalInventoryPoints);

        if (progressReports == 0)
            return new Live(UiState.Empty, "awaiting first progress report", "press Refresh progress to request it now", completedCount, totalCurrent, totalInventoryPoints);

        if (completedCount >= Plugin.AchievementIds.Length)
            return new Live(UiState.Ready, "all 11 jobs complete", "Pteranodon point goal met", completedCount, totalCurrent, totalInventoryPoints);

        var reason = totalInventoryPoints > 0 ? $"+{totalInventoryPoints:N0} points waiting in your bags" : null;
        return new Live(UiState.Running, "tracking Skyward points", reason, completedCount, totalCurrent, totalInventoryPoints);
    }

    private static void DrawTopBar(Live live)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        const string title = "Lazy Skyward Tracker";
        var brandWidth = MathF.Max(230, SkywardChrome.TextWidth(title, Theme.TitleScale) + 68);
        var statusText = $"{Copy.Word(live.State)} · {live.Completed} of {Plugin.AchievementIds.Length}";
        var statusWidth = MathF.Max(132, SkywardChrome.TextWidth(statusText) + 48);
        var gap = 3 * Theme.Unit;
        var singleRow = width >= brandWidth + statusWidth + gap;
        brandWidth = MathF.Min(brandWidth, width);
        statusWidth = MathF.Min(statusWidth, width);
        var barHeight = singleRow ? 66f : 108f;
        var brandPos = origin + new Vector2(0, 12);
        var statusPos = singleRow
            ? origin + new Vector2(width - statusWidth, 16)
            : origin + new Vector2(width - statusWidth, 62);

        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        draw.AddRectFilled(brandPos, brandPos + new Vector2(brandWidth, 42), Theme.Pack(Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(brandPos, brandPos + new Vector2(brandWidth, 42), Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
        SkywardChrome.Diamond(draw, brandPos + new Vector2(23, 21), 14, Theme.Pack(Theme.Accent));
        SkywardChrome.DrawText(draw, brandPos + new Vector2(49, 10),
            SkywardChrome.FitText(title, brandWidth - 59, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
        SkywardChrome.StatusPill(draw, statusPos, new Vector2(statusWidth, 34), live.State, $"{live.Completed} of {Plugin.AchievementIds.Length}");

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private static void DrawPageHeader(Live live)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var tileSize = new Vector2(42);
        const string title = "Skyward Tracker";
        const string description = "Tracks Skyward and Skybuilders' points for all jobs toward the Pteranodon mount.";
        var count = $"{live.Completed} of {Plugin.AchievementIds.Length} jobs complete";
        var subtext = $"{live.TotalCurrent:N0} / {TotalPointGoal:N0} total points";
        if (live.TotalInventoryPoints > 0)
            subtext += $" · +{live.TotalInventoryPoints:N0} in bags";

        draw.AddRectFilled(origin, origin + tileSize, Theme.Pack(Theme.Accent with { W = 0.14f }), 2 * Theme.Unit);
        var glyph = FontAwesomeIcon.Mountain.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        var glyphSize = ImGui.CalcTextSize(glyph);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), origin + ((tileSize - glyphSize) / 2f), Theme.Pack(Theme.Accent), glyph);
        ImGui.PopFont();

        if (width >= 700)
        {
            var rightWidth = MathF.Min(width * 0.42f, MathF.Max(SkywardChrome.TextWidth(count), SkywardChrome.TextWidth(subtext)));
            var textX = origin.X + tileSize.X + 12;
            var textWidth = width - tileSize.X - 12 - rightWidth - 16;
            SkywardChrome.DrawText(draw, new Vector2(textX, origin.Y),
                SkywardChrome.FitText(title, textWidth, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            SkywardChrome.DrawText(draw, new Vector2(textX, origin.Y + 29),
                SkywardChrome.FitText(description, textWidth, Theme.SecondaryScale), Theme.TextSecondary, Theme.SecondaryScale);
            SkywardChrome.DrawTextRight(draw, origin.X + width, origin.Y + 4,
                SkywardChrome.FitText(count, rightWidth), Theme.TextSecondary);
            SkywardChrome.DrawTextRight(draw, origin.X + width, origin.Y + 31,
                SkywardChrome.FitText(subtext, rightWidth, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, 58));
        }
        else
        {
            SkywardChrome.DrawText(draw, origin + new Vector2(tileSize.X + 12, 10),
                SkywardChrome.FitText(title, width - tileSize.X - 12, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, 52));
            Widgets.SecondaryWrapped(description);
            Widgets.Caption(count);
            Widgets.Caption(subtext);
        }
        ImGui.Separator();
    }

    private static void DrawStatCards(Live live, int turnInItems)
    {
        var stats = new[]
        {
            new Stat("Overall progress", $"{live.TotalCurrent:N0}", $"{TotalPointGoal:N0}",
                (float)live.TotalCurrent / TotalPointGoal,
                live.TotalInventoryPoints > 0 ? $"+{live.TotalInventoryPoints:N0} inventory points" : "No turn-in points in bags"),
            new Stat("Jobs complete", live.Completed.ToString(), Plugin.AchievementIds.Length.ToString(),
                (float)live.Completed / Plugin.AchievementIds.Length,
                live.Completed >= Plugin.AchievementIds.Length ? "Pteranodon point goal met" : $"{Plugin.AchievementIds.Length - live.Completed} jobs remaining"),
            new Stat("Inventory turn-ins", live.TotalInventoryPoints > 0 ? $"+{live.TotalInventoryPoints:N0}" : "None", null, null,
                turnInItems > 0 ? $"{turnInItems} turn-in items in bags" : "No turn-in items in bags"),
        };

        var available = ImGui.GetContentRegionAvail().X;
        var stacked = available < 700;
        var gap = 3 * Theme.Unit;
        var width = stacked ? available : (available - (2 * gap)) / 3f;
        const float height = 112;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();

        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (stacked ? new Vector2(0, i * (height + gap)) : new Vector2(i * (width + gap), 0));
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), 8f);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 8f);
            SkywardChrome.Diamond(draw, pos + new Vector2(20, 20), 5, Theme.Pack(Theme.Accent));
            SkywardChrome.DrawText(draw, pos + new Vector2(36, 11),
                SkywardChrome.FitText(stat.Label, width - 48, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);
            var value = stat.Cap is null ? stat.Value : $"{stat.Value} / {stat.Cap}";
            SkywardChrome.DrawText(draw, pos + new Vector2(12, 35),
                SkywardChrome.FitText(value, width - 24, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            if (stat.Fraction is { } fraction)
                SkywardChrome.Progress(draw, pos + new Vector2(12, 72), width - 24, fraction,
                    fraction >= 1f ? Theme.StateGreen : Theme.Accent);
            SkywardChrome.DrawText(draw, pos + new Vector2(12, 86),
                SkywardChrome.FitText(stat.Note, width - 24, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(0, stacked ? stats.Length * (height + gap) : height + gap));
    }

    private void DrawJobsCard(Dictionary<uint, InventoryScanner.InventoryProjection> projections, bool loggedIn)
    {
        SkywardCard.Draw(0, () =>
        {
            DrawJobsHeader();
            if (!loggedIn)
                Widgets.InfoBox("Log in to load Skyward achievement progress — the table fills in right after character select.", UiState.Blocked);
            else if (Plugin.ProgressCache.Count == 0)
                Widgets.InfoBox("No progress data yet — the game reports it shortly after login. Press Refresh progress to request it now.", UiState.Empty);

            var width = ImGui.GetContentRegionAvail().X;
            var wide = width >= 760;
            if (wide)
                DrawJobColumnHeader(width);

            foreach (var id in Plugin.AchievementIds)
            {
                var info = Plugin.SkywardAchievements[id];
                var isCompleted = Plugin.IsAchievementCompleted(id);
                uint current = 0;
                uint max = PointsPerJob;
                if (isCompleted)
                {
                    current = PointsPerJob;
                }
                else if (Plugin.ProgressCache.TryGetValue(id, out var progress))
                {
                    current = progress.Current;
                    max = progress.Max > 0 ? progress.Max : PointsPerJob;
                }

                projections.TryGetValue(id, out var projection);
                var inventoryPoints = projection?.TotalPoints ?? 0;
                var done = isCompleted || current >= max;
                var rowState = done ? UiState.Ready
                    : Plugin.ProgressCache.ContainsKey(id) ? (current > 0 ? UiState.Running : UiState.Idle)
                    : UiState.Missing;

                SkywardCard.Row(() =>
                {
                    if (wide)
                        DrawWideJobRow(id, info.Job, info.AchName, current, max, inventoryPoints, done, rowState, projection);
                    else
                        DrawNarrowJobRow(id, info.Job, info.AchName, current, max, inventoryPoints, done, rowState, projection);
                });
            }
        });
    }

    private void DrawJobsHeader()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var buttonSize = new Vector2(MathF.Min(154, width), 34);
        var stacked = width < 480;
        var textWidth = stacked ? width : width - buttonSize.X - 16;
        SkywardChrome.DrawText(draw, origin, SkywardChrome.FitText("Job progress", textWidth, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
        SkywardChrome.DrawText(draw, origin + new Vector2(0, 29),
            SkywardChrome.FitText("Achievement points and real inventory turn-in projections.", textWidth, Theme.SecondaryScale),
            Theme.TextSecondary, Theme.SecondaryScale);
        var buttonPos = origin + new Vector2(width - buttonSize.X, stacked ? 56 : 0);
        if (SkywardChrome.OutlineButton(draw, "refresh", "Refresh progress", buttonPos, buttonSize))
        {
            _plugin.RequestAllProgress();
            _plugin.Scanner.InvalidateCache();
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, stacked ? 100 : 54));
    }

    private static JobColumns ColumnsFor(float width)
    {
        const float jobWidth = 145;
        const float achievementWidth = 185;
        const float pointsWidth = 190;
        const float gap = 16;
        var achievementX = jobWidth + gap;
        var progressX = achievementX + achievementWidth + gap;
        var progressWidth = MathF.Max(80, width - progressX - pointsWidth - gap);
        return new JobColumns(achievementX, progressX, progressWidth);
    }

    private static void DrawJobColumnHeader(float width)
    {
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var columns = ColumnsFor(width);
        SkywardChrome.DrawText(draw, origin, "JOB", Theme.TextSecondary, Theme.CaptionScale);
        SkywardChrome.DrawText(draw, origin + new Vector2(columns.AchievementX, 0), "ACHIEVEMENT", Theme.TextSecondary, Theme.CaptionScale);
        SkywardChrome.DrawText(draw, origin + new Vector2(columns.ProgressX, 0), "PROGRESS", Theme.TextSecondary, Theme.CaptionScale);
        SkywardChrome.DrawTextRight(draw, origin.X + width, origin.Y, "POINTS", Theme.TextSecondary, Theme.CaptionScale);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, 22));
    }

    private static void DrawWideJobRow(
        uint id,
        string job,
        string achievement,
        uint current,
        uint max,
        uint inventoryPoints,
        bool done,
        UiState rowState,
        InventoryScanner.InventoryProjection? projection)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var columns = ColumnsFor(width);
        var stateLabel = rowState == UiState.Missing ? Copy.Missing("progress") : Copy.Word(rowState);
        var fraction = current / (float)max;
        var projectedFraction = MathF.Min((current + inventoryPoints) / (float)max, 1f);

        draw.AddCircleFilled(origin + new Vector2(4, 9), Theme.DotRadius, Theme.Pack(Copy.MarkColor(rowState)));
        SkywardChrome.DrawText(draw, origin + new Vector2(13, 0), SkywardChrome.FitText(job, 128), Theme.TextPrimary);
        SkywardChrome.DrawText(draw, origin + new Vector2(13, 27), SkywardChrome.FitText(stateLabel, 128, Theme.CaptionScale),
            Copy.MarkColor(rowState), Theme.CaptionScale);
        SkywardChrome.DrawText(draw, origin + new Vector2(columns.AchievementX, 15),
            SkywardChrome.FitText(achievement, 185, Theme.SecondaryScale), Theme.TextSecondary, Theme.SecondaryScale);
        DrawJobProgress(draw, origin + new Vector2(columns.ProgressX, 15), columns.ProgressWidth, fraction, projectedFraction, inventoryPoints, done);
        var progressNote = inventoryPoints > 0 && !done
            ? $"{Math.Clamp(fraction, 0f, 1f) * 100:0.0}% · +{inventoryPoints:N0} projected"
            : $"{Math.Clamp(fraction, 0f, 1f) * 100:0.0}%";
        SkywardChrome.DrawText(draw, origin + new Vector2(columns.ProgressX, 30),
            SkywardChrome.FitText(progressNote, columns.ProgressWidth, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);
        SkywardChrome.DrawTextRight(draw, origin.X + width, origin.Y + 2,
            SkywardChrome.FitText($"{current:N0} / {max:N0}", 190), Theme.TextPrimary);
        var pointsNote = done ? "Goal met" : $"{max - Math.Min(current, max):N0} remaining";
        SkywardChrome.DrawTextRight(draw, origin.X + width, origin.Y + 29,
            SkywardChrome.FitText(pointsNote, 190, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);

        ProjectionHitArea(id, origin + new Vector2(columns.ProgressX, 6), new Vector2(columns.ProgressWidth, 42),
            current, max, inventoryPoints, projection);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, 58));
    }

    private static void DrawNarrowJobRow(
        uint id,
        string job,
        string achievement,
        uint current,
        uint max,
        uint inventoryPoints,
        bool done,
        UiState rowState,
        InventoryScanner.InventoryProjection? projection)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var stateLabel = rowState == UiState.Missing ? Copy.Missing("progress") : Copy.Word(rowState);
        var stateWidth = MathF.Min(110, SkywardChrome.TextWidth(stateLabel, Theme.CaptionScale));
        var fraction = current / (float)max;
        var projectedFraction = MathF.Min((current + inventoryPoints) / (float)max, 1f);

        draw.AddCircleFilled(origin + new Vector2(4, 9), Theme.DotRadius, Theme.Pack(Copy.MarkColor(rowState)));
        SkywardChrome.DrawText(draw, origin + new Vector2(13, 0),
            SkywardChrome.FitText(job, width - stateWidth - 24), Theme.TextPrimary);
        SkywardChrome.DrawTextRight(draw, origin.X + width, origin.Y + 2,
            SkywardChrome.FitText(stateLabel, stateWidth, Theme.CaptionScale), Copy.MarkColor(rowState), Theme.CaptionScale);
        SkywardChrome.DrawText(draw, origin + new Vector2(0, 26),
            SkywardChrome.FitText(achievement, width, Theme.SecondaryScale), Theme.TextSecondary, Theme.SecondaryScale);
        DrawJobProgress(draw, origin + new Vector2(0, 52), width, fraction, projectedFraction, inventoryPoints, done);
        var progressNote = inventoryPoints > 0 && !done
            ? $"{Math.Clamp(fraction, 0f, 1f) * 100:0.0}% · +{inventoryPoints:N0} projected"
            : $"{Math.Clamp(fraction, 0f, 1f) * 100:0.0}%";
        SkywardChrome.DrawText(draw, origin + new Vector2(0, 63),
            SkywardChrome.FitText(progressNote, width, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);
        var pointsNote = done ? "Goal met" : $"{max - Math.Min(current, max):N0} remaining";
        SkywardChrome.DrawText(draw, origin + new Vector2(0, 87),
            SkywardChrome.FitText($"{current:N0} / {max:N0}", width * 0.60f), Theme.TextPrimary);
        SkywardChrome.DrawTextRight(draw, origin.X + width, origin.Y + 89,
            SkywardChrome.FitText(pointsNote, width * 0.38f, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);

        ProjectionHitArea(id, origin + new Vector2(0, 43), new Vector2(width, 32), current, max, inventoryPoints, projection);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, 110));
    }

    private static void DrawJobProgress(
        ImDrawListPtr draw,
        Vector2 pos,
        float width,
        float fraction,
        float projectedFraction,
        uint inventoryPoints,
        bool done)
    {
        SkywardChrome.Progress(draw, pos, width, fraction, done ? Theme.StateGreen : Theme.Accent);
        if (done || inventoryPoints == 0 || projectedFraction <= fraction)
            return;
        var from = Math.Clamp(fraction, 0f, 1f);
        var to = Math.Clamp(projectedFraction, from, 1f);
        var tint = projectedFraction >= 1f ? Theme.StateGreen with { W = 0.40f } : Theme.StateAmber with { W = 0.40f };
        draw.AddRectFilled(pos + new Vector2(width * from, 0), pos + new Vector2(width * to, Theme.BarHeight), Theme.Pack(tint), 2f);
    }

    private static void ProjectionHitArea(
        uint id,
        Vector2 pos,
        Vector2 size,
        uint current,
        uint max,
        uint inventoryPoints,
        InventoryScanner.InventoryProjection? projection)
    {
        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton($"##skyward_projection_{id}", size);
        if (!ImGui.IsItemHovered() || projection is not { Items.Count: > 0 })
            return;

        ImGui.BeginTooltip();
        ImGui.TextUnformatted($"Inventory turn-ins (+{inventoryPoints:N0} points):");
        ImGui.Separator();
        foreach (var item in projection.Items)
        {
            ImGui.TextUnformatted($"  {item.Quantity}x {item.ItemName}");
            ImGui.SameLine();
            Widgets.Secondary($"= {item.TotalPoints:N0} pts");
        }

        var projected = current + inventoryPoints;
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

    private static float FooterHeight(float width) => width < 620 ? NarrowFooterHeight : WideFooterHeight;

    private static void DrawFooter(Live live)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = FooterHeight(width);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        draw.AddCircleFilled(origin + new Vector2(10, 17), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(live.State)));

        var primary = $"{Copy.Word(live.State)} · {live.Action}";
        var secondary = live.Reason ?? $"{live.TotalCurrent:N0} total Skyward points tracked";
        SkywardChrome.DrawText(draw, origin + new Vector2(24, 8),
            SkywardChrome.FitText(primary, width - 36), Theme.TextPrimary);
        SkywardChrome.DrawText(draw, origin + new Vector2(24, 35),
            SkywardChrome.FitText(secondary, width - 36, Theme.CaptionScale), Theme.TextSecondary, Theme.CaptionScale);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
    }
}
