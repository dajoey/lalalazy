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
    private const float CompactHeight = 520;

    private readonly Plugin _plugin;

    public TrackerWindow(Plugin plugin) : base("Lazy Skyward Tracker##lazysky")
    {
        _plugin = plugin;
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
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

    // The shell lays out its own gutters from the window edge, so the window itself carries no padding.
    public override void PreDraw() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    public override void PostDraw() => ImGui.PopStyleVar();

    private Vector2 _winPos;
    private Vector2 _winSize;

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
            _winPos = ImGui.GetWindowPos();
            _winSize = ImGui.GetWindowSize();
            var gutter = SkywardChrome.Gutter(_winSize.X);
            var compact = _winSize.Y < CompactHeight;

            ImGui.SetCursorPos(Vector2.Zero);
            var y = DrawTopBar(live, gutter, compact);
            y = DrawPageHeader(live, gutter, y, compact);

            var footerHeight = FooterHeight();
            ImGui.SetCursorPos(new Vector2(0, y));
            DrawBody(new Vector2(_winSize.X, MathF.Max(40, _winSize.Y - footerHeight - y)), gutter, live, turnInItems, projections, loggedIn);
            DrawFooter(live, gutter, footerHeight);
        }
        finally
        {
            Theme.PopFrame();
        }
    }

    private void DrawBody(Vector2 size, float gutter, Live live, int turnInItems, Dictionary<uint, InventoryScanner.InventoryProjection> projections, bool loggedIn)
    {
        // A one pixel transparent border makes the child honour its window padding, which gives the page the same
        // left and right gutter and a bottom margin; the scrollbar then sits on the window edge.
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(gutter, SkywardChrome.Gap));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 2 * Theme.Unit + 2);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 2 * Theme.Unit);
        ImGui.PushStyleColor(ImGuiCol.Border, 0u);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Theme.TextSecondary with { W = 0.45f });
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Theme.Accent with { W = 0.6f });
        ImGui.BeginChild("##skyward_body", size, true);
        DrawStatCards(live, turnInItems);
        DrawJobsCard(projections, loggedIn);
        // Rows are never sliced by the footer without a hint: a soft fade marks content that continues below.
        if (ImGui.GetScrollMaxY() > 0 && ImGui.GetScrollY() < ImGui.GetScrollMaxY() - 1)
        {
            var draw = ImGui.GetWindowDrawList();
            var bg = ImGui.GetColorU32(ImGuiCol.WindowBg);
            var min = ImGui.GetWindowPos() + new Vector2(1, ImGui.GetWindowHeight() - (7 * Theme.Unit));
            draw.AddRectFilledMultiColor(min, ImGui.GetWindowPos() + new Vector2(ImGui.GetWindowWidth() - 1, ImGui.GetWindowHeight() - 1),
                bg & 0x00FFFFFFu, bg & 0x00FFFFFFu, bg, bg);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor(6);
        ImGui.PopStyleVar(5);
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

    private float DrawTopBar(Live live, float gutter, bool compact)
    {
        var draw = ImGui.GetWindowDrawList();
        var width = _winSize.X;
        var inner = width - (2 * gutter);
        const string title = "Lazy Skyward Tracker";
        const float padY = 3.5f * Theme.Unit;
        const float brandHeight = 10 * Theme.Unit;
        var gap = 2 * Theme.Unit;
        var brandNatural = 48 + SkywardChrome.TextWidth(title, Theme.TitleScale) + 16;
        var pillSize = new Vector2(SkywardChrome.PillWidth(), SkywardChrome.ControlHeight);
        var closeSize = new Vector2(SkywardChrome.ControlHeight);
        var oneRow = inner >= brandNatural + (4 * Theme.Unit) + pillSize.X + gap + closeSize.X;
        var twoRows = !oneRow && !compact;

        var controlY = padY + ((brandHeight - SkywardChrome.ControlHeight) / 2f);
        var closePos = new Vector2(width - gutter - closeSize.X, controlY);
        var pillPos = twoRows
            ? new Vector2(gutter, padY + brandHeight + gap)
            : new Vector2(closePos.X - gap - pillSize.X, controlY);
        var brandWidth = oneRow ? brandNatural
            : twoRows ? MathF.Min(brandNatural, inner - closeSize.X - gap)
            : MathF.Max(brandHeight, pillPos.X - (4 * Theme.Unit) - gutter);
        var barHeight = padY + brandHeight + padY + (twoRows ? gap + SkywardChrome.ControlHeight : 0);

        draw.PushClipRect(_winPos, _winPos + new Vector2(width, barHeight + 1), false);
        draw.AddRectFilled(_winPos, _winPos + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(_winPos + new Vector2(0, barHeight), _winPos + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        var brandPos = _winPos + new Vector2(gutter, padY);
        var textRoom = brandWidth - 48 - 16;
        var iconOnly = textRoom < SkywardChrome.TextWidth("Lazy", Theme.TitleScale) * 2;
        if (iconOnly)
            brandWidth = brandHeight;
        draw.AddRectFilled(brandPos, brandPos + new Vector2(brandWidth, brandHeight), Theme.Pack(Theme.Surface), SkywardChrome.Radius);
        draw.AddRect(brandPos, brandPos + new Vector2(brandWidth, brandHeight), Theme.Pack(Theme.SurfaceBorder), SkywardChrome.Radius);
        if (iconOnly)
        {
            SkywardChrome.Diamond(draw, brandPos + new Vector2(brandHeight / 2f), 12, Theme.Pack(Theme.Accent));
        }
        else
        {
            SkywardChrome.Diamond(draw, brandPos + new Vector2(24, brandHeight / 2f), 12, Theme.Pack(Theme.Accent));
            SkywardChrome.DrawFitted(draw,
                brandPos + new Vector2(48, (brandHeight - SkywardChrome.LineHeight(Theme.TitleScale)) / 2f),
                title, textRoom, Theme.TextPrimary, Theme.TitleScale);
        }

        SkywardChrome.StatusPill(draw, _winPos + pillPos, pillSize, live.State);
        if (SkywardChrome.CloseButton(draw, _winPos + closePos, closeSize))
            IsOpen = false;
        return barHeight + 1;
    }

    private float DrawPageHeader(Live live, float gutter, float top, bool compact)
    {
        var draw = ImGui.GetWindowDrawList();
        var width = _winSize.X;
        var tile = 11 * Theme.Unit;
        var padTop = compact ? 3 * Theme.Unit : 5 * Theme.Unit;
        var wide = width >= 760;
        var textX = gutter + tile + (4 * Theme.Unit);
        const string title = "Skyward Tracker";
        const string description = "Tracks Skyward and Skybuilders' points for all jobs toward the Pteranodon mount.";
        var count = $"{live.Completed} of {Plugin.AchievementIds.Length} jobs complete";
        var subtext = $"{live.TotalCurrent:N0} / {TotalPointGoal:N0} total points";
        if (live.TotalInventoryPoints > 0)
            subtext += $" · +{live.TotalInventoryPoints:N0} in bags";

        var origin = _winPos + new Vector2(gutter, top + padTop);
        draw.AddRectFilled(origin, origin + new Vector2(tile), Theme.Pack(Theme.Accent with { W = 0.14f }), SkywardChrome.Radius);
        SkywardChrome.Diamond(draw, origin + new Vector2(tile / 2f), 10, Theme.Pack(Theme.Accent));

        var metaWidth = wide && !compact
            ? MathF.Min(width * 0.40f, MathF.Max(SkywardChrome.TextWidth(count, Theme.SecondaryScale), SkywardChrome.TextWidth(subtext, Theme.SecondaryScale)))
            : 0;
        var textWidth = width - textX - gutter - (metaWidth > 0 ? metaWidth + (6 * Theme.Unit) : 0);
        var textOrigin = new Vector2(_winPos.X + textX, origin.Y);
        var shownTitle = SkywardChrome.ClampLines(title, textWidth, SkywardChrome.PageTitleScale, wide || compact ? 1 : 2);
        var titleHeight = SkywardChrome.WrappedHeight(shownTitle, textWidth, SkywardChrome.PageTitleScale);
        float blockBottom;
        if (compact)
        {
            // Short windows keep only the title so the body and the footer both stay visible.
            var titleTop = textOrigin.Y + ((tile - titleHeight) / 2f);
            SkywardChrome.DrawWrapped(draw, new Vector2(textOrigin.X, titleTop), shownTitle, textWidth, Theme.TextPrimary, SkywardChrome.PageTitleScale);
            blockBottom = origin.Y + tile;
        }
        else
        {
            SkywardChrome.DrawWrapped(draw, textOrigin, shownTitle, textWidth, Theme.TextPrimary, SkywardChrome.PageTitleScale);
            var shownDescription = SkywardChrome.ClampLines(description, textWidth, Theme.SecondaryScale, wide ? 1 : 3);
            var descriptionTop = textOrigin.Y + titleHeight + Theme.Unit;
            var descriptionHeight = SkywardChrome.DrawWrapped(draw, new Vector2(textOrigin.X, descriptionTop), shownDescription, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
            blockBottom = descriptionTop + descriptionHeight;
            if (shownDescription != description && ImGui.IsMouseHoveringRect(textOrigin, new Vector2(textOrigin.X + textWidth, blockBottom)))
                ImGui.SetTooltip(description);

            if (wide)
            {
                var right = _winPos.X + width - gutter;
                var line1 = textOrigin.Y + ((titleHeight - SkywardChrome.LineHeight(Theme.SecondaryScale)) / 2f);
                SkywardChrome.DrawFittedRight(draw, right, line1, count, metaWidth, Theme.TextSecondary, Theme.SecondaryScale);
                SkywardChrome.DrawFittedRight(draw, right, descriptionTop, subtext, metaWidth, Theme.TextSecondary, Theme.SecondaryScale);
            }
            else
            {
                var y = blockBottom + Theme.Unit;
                foreach (var meta in new[] { count, subtext })
                {
                    SkywardChrome.DrawFitted(draw, new Vector2(textOrigin.X, y), meta, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
                    y += SkywardChrome.LineHeight(Theme.SecondaryScale) + (Theme.Unit / 2f);
                }
                blockBottom = y - (Theme.Unit / 2f);
            }
        }

        var bottom = top + padTop + MathF.Max(tile, blockBottom - origin.Y) + (2 * Theme.Unit);
        return bottom;
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

        var draw = ImGui.GetWindowDrawList();
        var available = ImGui.GetContentRegionAvail().X;
        var gap = SkywardChrome.Gap;
        var stacked = available < (3 * 220) + (2 * gap);
        var width = stacked ? available : (available - (2 * gap)) / 3f;
        const float pad = SkywardChrome.Gap;
        var labelHeight = SkywardChrome.LineHeight(Theme.SecondaryScale);
        var valueHeight = SkywardChrome.LineHeight(SkywardChrome.StatValueScale);
        var noteHeight = SkywardChrome.LineHeight(Theme.SecondaryScale);
        // Stacked cards put the note on the label line so three full-width cards stay short.
        var height = stacked
            ? (3 * Theme.Unit) + labelHeight + Theme.Unit + valueHeight + (2 * Theme.Unit) + Theme.BarHeight + (3 * Theme.Unit)
            : pad + labelHeight + (2 * Theme.Unit) + valueHeight + (2 * Theme.Unit) + Theme.BarHeight + (2 * Theme.Unit) + noteHeight + pad;
        var origin = ImGui.GetCursorScreenPos();

        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (stacked ? new Vector2(0, i * (height + gap)) : new Vector2(i * (width + gap), 0));
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), SkywardChrome.Radius);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), SkywardChrome.Radius);

            var inner = width - (2 * pad);
            var noteColor = stat.Fraction is >= 1f ? Theme.StateGreen : Theme.TextSecondary;
            var y = pos.Y + (stacked ? 3 * Theme.Unit : pad);
            SkywardChrome.Diamond(draw, new Vector2(pos.X + pad + 5, y + (labelHeight / 2f)), 5, Theme.Pack(Theme.Accent));
            var labelWidth = SkywardChrome.TextWidth(stat.Label, Theme.SecondaryScale);
            SkywardChrome.DrawFitted(draw, new Vector2(pos.X + pad + 16, y), stat.Label, inner - 16, Theme.TextSecondary, Theme.SecondaryScale);
            if (stacked)
            {
                var noteRoom = inner - 16 - labelWidth - gap;
                if (noteRoom > 40)
                    SkywardChrome.DrawFittedRight(draw, pos.X + width - pad, y, stat.Note, noteRoom, noteColor, Theme.SecondaryScale);
            }
            y += labelHeight + (stacked ? Theme.Unit : 2 * Theme.Unit);

            var empty = stat.Value == "None";
            SkywardChrome.DrawText(draw, new Vector2(pos.X + pad, y), stat.Value, empty ? Theme.TextSecondary : Theme.TextPrimary, SkywardChrome.StatValueScale);
            if (stat.Cap is { } cap)
            {
                var capText = int.TryParse(cap.Replace(",", ""), out _) ? "/ " + cap : cap;
                var capHeight = SkywardChrome.LineHeight(Theme.SecondaryScale);
                var capX = pos.X + pad + SkywardChrome.TextWidth(stat.Value, SkywardChrome.StatValueScale) + (2 * Theme.Unit);
                SkywardChrome.DrawFitted(draw, new Vector2(capX, y + valueHeight - capHeight - (valueHeight * 0.08f)), capText,
                    pos.X + width - pad - capX, Theme.TextSecondary, Theme.SecondaryScale);
            }
            y += valueHeight + (2 * Theme.Unit);

            if (stat.Fraction is { } fraction)
                SkywardChrome.Progress(draw, new Vector2(pos.X + pad, y), inner, fraction, Theme.StateGreen);
            y += Theme.BarHeight + (2 * Theme.Unit);
            if (!stacked)
                SkywardChrome.DrawFitted(draw, new Vector2(pos.X + pad, y), stat.Note, inner, noteColor, Theme.SecondaryScale);
        }

        var total = stacked ? (stats.Length * height) + ((stats.Length - 1) * gap) : height;
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(MathF.Max(1, available), total + gap - Theme.ItemSpacing.Y));
    }

    private static string StateLabel(UiState rowState) => rowState == UiState.Missing ? Copy.Missing("progress") : Copy.Word(rowState);

    // Rows that have no progress report are not an alert: they read neutral, amber stays with the Blocked pill and notice.
    private static Vector4 RowColor(UiState rowState) => rowState == UiState.Missing ? Theme.StateNeutral : Copy.MarkColor(rowState);

    private void DrawJobsCard(Dictionary<uint, InventoryScanner.InventoryProjection> projections, bool loggedIn)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var cardWidth = ImGui.GetContentRegionAvail().X;
        const float pad = SkywardChrome.Gap;
        var inner = cardWidth - (2 * pad);
        var left = origin.X + pad;
        var right = origin.X + cardWidth - pad;
        const float rowGap = 2 * Theme.Unit;

        // ---- header ----
        const string subtitle = "Achievement points and real inventory turn-in projections.";
        var buttonSize = new Vector2(MathF.Min(inner, SkywardChrome.TextWidth("Refresh progress") + (2 * pad)), SkywardChrome.ControlHeight);
        var stackedHeader = cardWidth < 480;
        var textWidth = stackedHeader ? inner : inner - buttonSize.X - pad;
        var titleHeight = SkywardChrome.LineHeight(Theme.TitleScale);
        var shownSubtitle = SkywardChrome.ClampLines(subtitle, textWidth, Theme.SecondaryScale, stackedHeader ? 2 : 1);
        var subtitleHeight = SkywardChrome.WrappedHeight(shownSubtitle, textWidth, Theme.SecondaryScale);
        var blockHeight = titleHeight + Theme.Unit + subtitleHeight;
        var headerHeight = stackedHeader ? blockHeight + (3 * Theme.Unit) + buttonSize.Y : MathF.Max(blockHeight, buttonSize.Y);

        // ---- notice ----
        SkywardChrome.NoticeLayout? notice = null;
        var noticeState = UiState.Empty;
        if (!loggedIn)
        {
            noticeState = UiState.Blocked;
            notice = SkywardChrome.MeasureNotice(inner, "Log in to load Skyward achievement progress — the table fills in right after character select.");
        }
        else if (Plugin.ProgressCache.Count == 0)
        {
            notice = SkywardChrome.MeasureNotice(inner, "No progress data yet — the game reports it shortly after login. Press Refresh progress to request it now.");
        }

        // ---- columns ----
        var cellWidth = inner - (2 * pad);
        var jobWidth = 14f;
        var achievementWidth = 0f;
        var stateLabels = new[] { StateLabel(UiState.Missing), StateLabel(UiState.Idle), StateLabel(UiState.Running), StateLabel(UiState.Ready) };
        foreach (var id in Plugin.AchievementIds)
        {
            var info = Plugin.SkywardAchievements[id];
            jobWidth = MathF.Max(jobWidth, 14 + SkywardChrome.TextWidth(info.Job));
            achievementWidth = MathF.Max(achievementWidth, SkywardChrome.TextWidth(info.AchName, Theme.SecondaryScale));
        }
        foreach (var label in stateLabels)
            jobWidth = MathF.Max(jobWidth, 14 + SkywardChrome.TextWidth(label, Theme.CaptionScale));
        var pointsWidth = MathF.Max(SkywardChrome.TextWidth($"{PointsPerJob:N0} / {PointsPerJob:N0}"),
            SkywardChrome.TextWidth($"{PointsPerJob:N0} remaining", Theme.CaptionScale));
        var columns = ColumnsFor(cellWidth, jobWidth, achievementWidth, pointsWidth);
        var wide = columns.ProgressWidth >= 140;

        var lh = SkywardChrome.LineHeight();
        var secondaryHeight = SkywardChrome.LineHeight(Theme.SecondaryScale);
        var captionHeight = SkywardChrome.LineHeight(Theme.CaptionScale);
        var rowHeight = wide
            ? MathF.Max(lh + (Theme.Unit / 2f) + captionHeight, Theme.BarHeight + (3 * Theme.Unit / 2f) + captionHeight) + (6 * Theme.Unit)
            : (3 * Theme.Unit) + lh + Theme.Unit + secondaryHeight + (2 * Theme.Unit) + Theme.BarHeight + (3 * Theme.Unit / 2f) + captionHeight + (3 * Theme.Unit);

        var count = Plugin.AchievementIds.Length;
        var total = pad + headerHeight + pad
                    + (notice is { } n ? n.Height + (3 * Theme.Unit) : 0)
                    + (wide ? captionHeight + rowGap : 0)
                    + (count * rowHeight) + ((count - 1) * rowGap) + pad;

        draw.AddRectFilled(origin, origin + new Vector2(cardWidth, total), Theme.Pack(Theme.Surface), SkywardChrome.Radius);
        draw.AddRect(origin, origin + new Vector2(cardWidth, total), Theme.Pack(Theme.SurfaceBorder), SkywardChrome.Radius);

        // ---- draw header ----
        var y = origin.Y + pad;
        var textTop = stackedHeader ? y : y + ((headerHeight - blockHeight) / 2f);
        SkywardChrome.DrawText(draw, new Vector2(left, textTop), "Job progress", Theme.TextPrimary, Theme.TitleScale);
        SkywardChrome.DrawWrapped(draw, new Vector2(left, textTop + titleHeight + Theme.Unit), shownSubtitle, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
        var buttonPos = stackedHeader
            ? new Vector2(left, y + blockHeight + (3 * Theme.Unit))
            : new Vector2(right - buttonSize.X, y + ((headerHeight - buttonSize.Y) / 2f));
        if (SkywardChrome.OutlineButton(draw, "refresh", "Refresh progress", buttonPos, buttonSize))
        {
            _plugin.RequestAllProgress();
            _plugin.Scanner.InvalidateCache();
        }
        y += headerHeight + pad;

        if (notice is { } box)
        {
            SkywardChrome.DrawNotice(draw, new Vector2(left, y), inner, box, noticeState);
            y += box.Height + (3 * Theme.Unit);
        }

        if (wide)
        {
            var cx = left + pad;
            SkywardChrome.DrawText(draw, new Vector2(cx, y), "JOB", Theme.TextSecondary, Theme.CaptionScale);
            SkywardChrome.DrawText(draw, new Vector2(cx + columns.AchievementX, y), "ACHIEVEMENT", Theme.TextSecondary, Theme.CaptionScale);
            SkywardChrome.DrawText(draw, new Vector2(cx + columns.ProgressX, y), "PROGRESS", Theme.TextSecondary, Theme.CaptionScale);
            SkywardChrome.DrawTextRight(draw, cx + cellWidth, y, "POINTS", Theme.TextSecondary, Theme.CaptionScale);
            y += captionHeight + rowGap;
        }

        // ---- rows ----
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

            var rowMin = new Vector2(left, y);
            draw.AddRectFilled(rowMin, rowMin + new Vector2(inner, rowHeight), Theme.Pack(Theme.ShellBar), SkywardChrome.Radius);
            var row = new RowGeometry(new Vector2(left + pad, y), cellWidth, rowHeight);
            if (wide)
                DrawWideJobRow(draw, row, columns, id, info.Job, info.AchName, current, max, inventoryPoints, done, rowState, projection);
            else
                DrawNarrowJobRow(draw, row, id, info.Job, info.AchName, current, max, inventoryPoints, done, rowState, projection);
            y += rowHeight + rowGap;
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(MathF.Max(1, cardWidth), total));
    }

    /// <summary>The content box of one job row: its top left, the width inside the row padding and the row height.</summary>
    private readonly record struct RowGeometry(Vector2 Origin, float Width, float Height);

    /// <summary>Column layout of a wide row from the measured widest cell of each column; the progress column takes the rest.</summary>
    private static JobColumns ColumnsFor(float width, float jobWidth, float achievementWidth, float pointsWidth)
    {
        const float gap = 6 * Theme.Unit;
        var achievementX = jobWidth + gap;
        var progressX = achievementX + achievementWidth + gap;
        var progressWidth = MathF.Max(80, width - progressX - pointsWidth - gap);
        return new JobColumns(achievementX, progressX, progressWidth);
    }

    private static string ProgressNote(float fraction, uint inventoryPoints, bool done)
        => inventoryPoints > 0 && !done
            ? $"{Math.Clamp(fraction, 0f, 1f) * 100:0.0}% · +{inventoryPoints:N0} projected"
            : $"{Math.Clamp(fraction, 0f, 1f) * 100:0.0}%";

    private void DrawWideJobRow(
        ImDrawListPtr draw,
        RowGeometry row,
        JobColumns columns,
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
        var lh = SkywardChrome.LineHeight();
        var secondaryHeight = SkywardChrome.LineHeight(Theme.SecondaryScale);
        var captionHeight = SkywardChrome.LineHeight(Theme.CaptionScale);
        var fraction = current / (float)max;
        var projectedFraction = MathF.Min((current + inventoryPoints) / (float)max, 1f);
        var color = RowColor(rowState);
        var cx = row.Origin.X;
        var right = cx + row.Width;
        var nameWidth = columns.AchievementX - (6 * Theme.Unit) - 14;

        // Every cell is centred on the row's own centre line.
        var twoLine = lh + (Theme.Unit / 2f) + captionHeight;
        var top = row.Origin.Y + ((row.Height - twoLine) / 2f);
        draw.AddCircleFilled(new Vector2(cx + 3, top + (lh / 2f)), Theme.DotRadius, Theme.Pack(color));
        SkywardChrome.DrawFitted(draw, new Vector2(cx + 14, top), job, nameWidth, Theme.TextPrimary);
        SkywardChrome.DrawFitted(draw, new Vector2(cx + 14, top + lh + (Theme.Unit / 2f)), StateLabel(rowState), nameWidth, color, Theme.CaptionScale);

        var achievementWidth = columns.ProgressX - columns.AchievementX - (6 * Theme.Unit);
        SkywardChrome.DrawFitted(draw, new Vector2(cx + columns.AchievementX, row.Origin.Y + ((row.Height - secondaryHeight) / 2f)),
            achievement, achievementWidth, Theme.TextSecondary, Theme.SecondaryScale);

        var progressHeight = Theme.BarHeight + (3 * Theme.Unit / 2f) + captionHeight;
        var progressTop = new Vector2(cx + columns.ProgressX, row.Origin.Y + ((row.Height - progressHeight) / 2f));
        DrawJobProgress(draw, progressTop, columns.ProgressWidth, fraction, projectedFraction, inventoryPoints, done);
        SkywardChrome.DrawFitted(draw, progressTop + new Vector2(0, Theme.BarHeight + (3 * Theme.Unit / 2f)),
            ProgressNote(fraction, inventoryPoints, done), columns.ProgressWidth, Theme.TextSecondary, Theme.CaptionScale);

        var pointsWidth = right - (cx + columns.ProgressX + columns.ProgressWidth) - (6 * Theme.Unit);
        SkywardChrome.DrawFittedRight(draw, right, top, $"{current:N0} / {max:N0}", pointsWidth, Theme.TextPrimary);
        var pointsNote = done ? "Goal met" : $"{max - Math.Min(current, max):N0} remaining";
        SkywardChrome.DrawFittedRight(draw, right, top + lh + (Theme.Unit / 2f), pointsNote, pointsWidth,
            done ? Theme.StateGreen : Theme.TextSecondary, Theme.CaptionScale);

        ProjectionHitArea(id, progressTop - new Vector2(0, (row.Height - progressHeight) / 2f), new Vector2(columns.ProgressWidth, row.Height),
            current, max, inventoryPoints, projection);
    }

    private void DrawNarrowJobRow(
        ImDrawListPtr draw,
        RowGeometry row,
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
        var lh = SkywardChrome.LineHeight();
        var secondaryHeight = SkywardChrome.LineHeight(Theme.SecondaryScale);
        var captionHeight = SkywardChrome.LineHeight(Theme.CaptionScale);
        var fraction = current / (float)max;
        var projectedFraction = MathF.Min((current + inventoryPoints) / (float)max, 1f);
        var color = RowColor(rowState);
        var left = row.Origin.X;
        var right = left + row.Width;
        var stateLabel = StateLabel(rowState);
        var stateWidth = MathF.Min(row.Width * 0.45f, SkywardChrome.TextWidth(stateLabel, Theme.CaptionScale));

        var y = row.Origin.Y + (3 * Theme.Unit);
        draw.AddCircleFilled(new Vector2(left + 3, y + (lh / 2f)), Theme.DotRadius, Theme.Pack(color));
        SkywardChrome.DrawFitted(draw, new Vector2(left + 14, y), job, row.Width - 14 - stateWidth - Gap, Theme.TextPrimary);
        SkywardChrome.DrawFittedRight(draw, right, y + ((lh - captionHeight) / 2f), stateLabel, stateWidth, color, Theme.CaptionScale);
        y += lh + Theme.Unit;

        var points = $"{current:N0} / {max:N0}";
        var pointsWidth = SkywardChrome.TextWidth(points);
        SkywardChrome.DrawFitted(draw, new Vector2(left, y + ((lh - secondaryHeight) / 2f)), achievement,
            row.Width - pointsWidth - Gap, Theme.TextSecondary, Theme.SecondaryScale);
        SkywardChrome.DrawTextRight(draw, right, y, points, Theme.TextPrimary);
        y += lh + (2 * Theme.Unit);

        var barTop = y;
        DrawJobProgress(draw, new Vector2(left, barTop), row.Width, fraction, projectedFraction, inventoryPoints, done);
        y += Theme.BarHeight + (3 * Theme.Unit / 2f);
        var pointsNote = done ? "Goal met" : $"{max - Math.Min(current, max):N0} remaining";
        var noteWidth = SkywardChrome.TextWidth(pointsNote, Theme.CaptionScale);
        SkywardChrome.DrawFitted(draw, new Vector2(left, y), ProgressNote(fraction, inventoryPoints, done),
            row.Width - noteWidth - Gap, Theme.TextSecondary, Theme.CaptionScale);
        SkywardChrome.DrawTextRight(draw, right, y, pointsNote, done ? Theme.StateGreen : Theme.TextSecondary, Theme.CaptionScale);

        ProjectionHitArea(id, new Vector2(left, barTop - (2 * Theme.Unit)), new Vector2(row.Width, Theme.BarHeight + (4 * Theme.Unit)),
            current, max, inventoryPoints, projection);
    }

    private const float Gap = SkywardChrome.Gap;

    private static void DrawJobProgress(
        ImDrawListPtr draw,
        Vector2 pos,
        float width,
        float fraction,
        float projectedFraction,
        uint inventoryPoints,
        bool done)
    {
        SkywardChrome.Progress(draw, pos, width, fraction, Theme.StateGreen);
        if (done || inventoryPoints == 0 || projectedFraction <= fraction)
            return;
        var from = Math.Clamp(fraction, 0f, 1f);
        var to = Math.Clamp(projectedFraction, from, 1f);
        var tint = projectedFraction >= 1f ? Theme.StateGreen with { W = 0.40f } : Theme.StateAmber with { W = 0.40f };
        draw.AddRectFilled(pos + new Vector2(width * from, 0), pos + new Vector2(width * to, Theme.BarHeight), Theme.Pack(tint), Theme.BarHeight / 2f);
    }

    private void ProjectionHitArea(
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

        var rowMin = ImGui.GetItemRectMin();
        var rowMax = ImGui.GetItemRectMax();
        var header = $"Inventory turn-ins (+{inventoryPoints:N0} points):";
        var projected = current + inventoryPoints;
        var completes = projected >= max;
        var footer = completes ? $"Would complete this job ({projected:N0} / {max:N0})" : $"Projected: {projected:N0} / {max:N0}";
        var markWidth = completes ? (2 * Theme.DotRadius) + (2 * Theme.Unit) : 0;
        const float padding = 3 * Theme.Unit;
        var columnGap = 4 * Theme.Unit;
        var amountWidth = projection.Items.Max(item => ImGui.CalcTextSize($"= {item.TotalPoints:N0} pts").X);
        var itemWidth = projection.Items.Max(item => ImGui.CalcTextSize($"{item.Quantity}x {item.ItemName}").X);
        var contentWidth = MathF.Max(MathF.Max(ImGui.CalcTextSize(header).X, itemWidth + columnGap + amountWidth), markWidth + ImGui.CalcTextSize(footer).X);
        var lineHeight = ImGui.GetTextLineHeight();
        var lineGap = Theme.Unit;
        var lines = projection.Items.Count + 2;
        var tipSize = new Vector2(contentWidth + (2 * padding), (2 * padding) + (lines * lineHeight) + ((lines + 1) * lineGap) + Theme.Unit + 1);

        // Sit above the hovered row so its own numbers stay readable; fall back to below when there is no room above.
        var minX = _winPos.X + Theme.Unit;
        var maxX = _winPos.X + _winSize.X - Theme.Unit;
        var x = Math.Clamp(rowMin.X, minX, MathF.Max(minX, maxX - tipSize.X));
        var y = rowMin.Y - (2 * Theme.Unit) - tipSize.Y;
        if (y < _winPos.Y + Theme.Unit)
            y = rowMax.Y + (2 * Theme.Unit);
        y = Math.Clamp(y, _winPos.Y + Theme.Unit, MathF.Max(_winPos.Y + Theme.Unit, _winPos.Y + _winSize.Y - Theme.Unit - tipSize.Y));

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2 * Theme.Unit, lineGap));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, SkywardChrome.Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface with { X = 0.16f, Y = 0.17f, Z = 0.20f });
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.TextSecondary with { W = 0.45f });
        ImGui.SetNextWindowPos(new Vector2(x, y));
        ImGui.SetNextWindowSize(new Vector2(tipSize.X, 0));
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(header);
        ImGui.Separator();
        foreach (var item in projection.Items)
        {
            ImGui.TextUnformatted($"{item.Quantity}x {item.ItemName}");
            ImGui.SameLine(padding + contentWidth - ImGui.CalcTextSize($"= {item.TotalPoints:N0} pts").X);
            Widgets.Secondary($"= {item.TotalPoints:N0} pts");
        }

        ImGui.Dummy(new Vector2(1, Theme.Unit));
        if (completes)
        {
            var cursor = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddCircleFilled(cursor + new Vector2(Theme.DotRadius, lineHeight / 2f), Theme.DotRadius, Theme.Pack(Copy.MarkColor(UiState.Ready)));
            ImGui.SetCursorScreenPos(cursor + new Vector2(markWidth, 0));
        }
        ImGui.TextUnformatted(footer);
        ImGui.EndTooltip();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(4);
    }

    private static float FooterHeight()
    {
        var textBlock = SkywardChrome.LineHeight() + Theme.Unit + SkywardChrome.LineHeight(Theme.SecondaryScale);
        return 12 + MathF.Max(textBlock, SkywardChrome.ControlHeight) + 12;
    }

    private void DrawFooter(Live live, float gutter, float height)
    {
        var top = _winPos.Y + _winSize.Y - height;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(new Vector2(_winPos.X, top), _winPos + _winSize, false);
        draw.AddRectFilled(new Vector2(_winPos.X, top), _winPos + _winSize, Theme.Pack(Theme.ShellBar));
        draw.AddLine(new Vector2(_winPos.X, top), new Vector2(_winPos.X + _winSize.X, top), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        var primary = $"{Copy.Word(live.State)} · {live.Action}";
        var secondary = SkywardChrome.Sentence(live.Reason ?? $"{live.TotalCurrent:N0} total Skyward points tracked");
        var textLeft = _winPos.X + gutter + (4 * Theme.Unit);
        var textRight = _winPos.X + _winSize.X - gutter;
        var textTop = top + ((height - (SkywardChrome.LineHeight() + Theme.Unit + SkywardChrome.LineHeight(Theme.SecondaryScale))) / 2f);
        var lineHeight = SkywardChrome.LineHeight();
        draw.AddCircleFilled(new Vector2(_winPos.X + gutter + Theme.Unit, textTop + (lineHeight / 2f)), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(live.State)));
        SkywardChrome.DrawFitted(draw, new Vector2(textLeft, textTop), primary, textRight - textLeft, Theme.TextPrimary);
        SkywardChrome.DrawFitted(draw, new Vector2(textLeft, textTop + lineHeight + Theme.Unit), secondary, textRight - textLeft, Theme.TextSecondary, Theme.SecondaryScale);
    }
}
