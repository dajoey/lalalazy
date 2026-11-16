using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyCrucible;

internal sealed record CruciblePage(string Id, string Group, string Title, string Description, string SearchTerms);

/// <summary>Per-plugin navigation, focus, search, status and save-on-change state for the structural UI.</summary>
internal sealed class CrucibleShell
{
    public const string OverviewPage = "shell.overview";
    public const string RosterHornsPage = "run.roster_horns";
    public const string UpcomingPage = "guide.upcoming";
    public const string AdvisorPage = "guide.beast_picks";

    public static readonly CruciblePage[] Pages =
    [
        new(OverviewPage, "Home", "Crucible overview", "Live readiness, recent activity, and the next useful destination.", "home overview readiness activity"),
        new("run.board_overview", "Run", "Board overview", "Board progression and the fight currently in focus.", "run board progression"),
        new(RosterHornsPage, "Run", "Roster & horns", "Prepares the familiar roster and Battlehorn picks for the active board.", "run roster horns score auto duty"),
        new("run.feed_shop", "Run", "Feed & shop", "Keeps feeding and purchases aligned with the fights ahead.", "run feed shop score goal"),
        new("run.campsite", "Run", "Campsite", "Selects familiars worth resting while leaving Rest to the player.", "run campsite rest"),
        new("run.display_logging", "Run", "Display & logging", "Controls guide opening, announcements, and read-only screen recording.", "display logging announce record report"),
        new("rewards.spoils", "Rewards", "Spoils", "Take all only when the rewards fit; otherwise keep the choice visible.", "rewards spoils loot"),
        new("rewards.treasure", "Rewards", "Treasure", "Suggests the best coffer without pressing a choice.", "rewards treasure coffer"),
        new(UpcomingPage, "Guide", "Upcoming fight", "Real horn picks, calls, mechanics, and advice for the focused fight.", "guide upcoming follow fight"),
        new("guide.browse", "Guide", "Browse boards", "Browse every real guide entry by board and battle.", "guide browse boards battles"),
        new(AdvisorPage, "Guide", "Beast picks by battle", "The existing advisor: roster, battle picks, and capture advice.", "guide beast picks advisor bestiary capture"),
        new("guide.recent", "Guide", "What LazyCrucible chose this session", "The newest 30 real selection decisions, with their reasons.", "guide recent choices session"),
    ];

    private Plugin? _plugin;
    private bool _changed;
    private long _nextIncomingScan;

    public string CurrentPage { get; set; } = RosterHornsPage;
    public string SearchText = "";
    public int AdvisorBoard;
    public int AdvisorBattle = 1;
    public int GuideBoard = 1;
    public int GuideBattle = 1;
    public bool GuideFollow = true;
    public IncomingCast? Incoming { get; private set; }
    public CruciblePage Current => Pages.First(p => p.Id == CurrentPage);
    public Action? CloseRequested { get; set; }

    private (int Board, int Battle) _lastFocus = (0, -1);

    public void FollowFocus(GuideWindow window)
    {
        WatchIncoming(window);
        var focus = RunTracker.Focus;
        if (focus.Battle < 0 || focus == _lastFocus)
            return;
        _lastFocus = focus;
        if (GuideFollow)
        {
            GuideBoard = focus.Board;
            GuideBattle = focus.Battle;
        }
        if (Plugin.Config.GuideAutoOpen)
        {
            CurrentPage = UpcomingPage;
            window.IsOpen = true;
        }
    }

    public void SyncLiveFocus()
    {
        if (GuideFollow && RunTracker.Focus.Battle >= 0)
        {
            GuideBoard = RunTracker.Focus.Board;
            GuideBattle = RunTracker.Focus.Battle;
        }
        var now = Environment.TickCount64;
        if (now < _nextIncomingScan)
            return;
        _nextIncomingScan = now + 250;
        Incoming = CrucibleGame.WarnedCast();
    }

    private void WatchIncoming(GuideWindow window)
    {
        var now = Environment.TickCount64;
        if (now < _nextIncomingScan)
            return;
        _nextIncomingScan = now + 250;
        var found = CrucibleGame.WarnedCast();
        if (found is not null && Incoming is null && Plugin.Config.GuideAutoOpen)
        {
            CurrentPage = UpcomingPage;
            window.IsOpen = true;
        }
        Incoming = found;
    }

    /// <summary>Bottom space kept under the last card: the body padding plus the gap every card leaves.</summary>
    public const float PageBottomReserve = 2 * CrucibleChrome.Gap;

    private const float BodyPadY = CrucibleChrome.Gap;

    public void Draw(Plugin? plugin)
    {
        _plugin = plugin ?? _plugin;
        _changed = false;
        CrucibleChrome.ResetLayout();
        CrucibleCard.FillBottom = float.NaN;
        Theme.PushFrame();
        try
        {
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            var gutter = CrucibleChrome.Gutter(winSize.X);
            ImGui.SetCursorPos(Vector2.Zero);
            var y = DrawTopBar(winPos, winSize.X, gutter);
            y = DrawFeatureBar(winPos, winSize.X, gutter, y);
            y = DrawPageHeader(winPos, winSize.X, gutter, y);

            var footerHeight = FooterHeight(winSize.X);
            ImGui.SetCursorPos(new Vector2(0, y));
            DrawBody(new Vector2(winSize.X, MathF.Max(40, winSize.Y - footerHeight - y)), gutter);
            DrawFooter(winPos, winSize, gutter, footerHeight);
        }
        finally
        {
            Theme.PopFrame();
            if (_changed)
                Plugin.Config.Save();
        }
    }

    private void DrawBody(Vector2 size, float gutter)
    {
        // A one pixel transparent border makes the child honour its window padding, which gives every page the same
        // left and right gutter and a bottom margin; the scrollbar then sits on the window edge.
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(gutter, BodyPadY));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 2 * Theme.Unit + 2);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 2 * Theme.Unit);
        ImGui.PushStyleColor(ImGuiCol.Border, 0u);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Theme.TextSecondary with { W = 0.45f });
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Theme.Accent with { W = 0.6f });
        ImGui.BeginChild("##crucibleBody", size, true);
        // The child has applied its own padding; popups and tooltips opened from page content keep the theme padding.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.WindowPadding);
        DrawStatusBanner();
        CruciblePages.Render(this, CurrentPage);
        ImGui.PopStyleVar();
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

    public void Setting(string literalLabel, Func<bool> get, Action<bool> set, string? help = null, bool separator = false)
    {
        var value = get();
        if (!CrucibleChrome.Toggle(literalLabel, ref value, help, separator))
            return;
        set(value);
        _changed = true;
    }

    public void ScoreGoalChanged()
    {
        _changed = true;
    }

    public (UiState State, string Detail) OverallState()
    {
        if (PetSelect.YieldReason == "conflict_gluttony")
            return (UiState.Failed, "selection writes held");
        if (PetSelect.YieldReason == "autoduty_running" || (Plugin.Config.YieldToAutoDuty && ExternalDrivers.StallReason is not null))
            return (UiState.Blocked, "AutoDuty attention");
        if (Incoming is not null)
            return (UiState.Running, "incoming cast");
        if (CurrentPage == AdvisorPage && !CrucibleGame.RosterLoaded)
            return (UiState.Missing, Copy.Missing("Bestiary"));
        if (CurrentPage == UpcomingPage)
        {
            if (GuideFollow && RunTracker.Focus.Battle < 0)
                return (UiState.Idle, "no fight in focus");
            if (Plugin.Guide.Fight(GuideBoard, GuideBattle) is not { } fight)
                return (UiState.Empty, "no guide entry");
            if (RunTracker.Focus == (fight.Board, fight.Battle)
                && FightWarnings.EntryAdvice(fight.Board, fight.Battle, RunTracker.PlayerHpPercent) is not null)
                return (UiState.Blocked, "Low HP");
        }
        if (CurrentPage == OverviewPage && PetSelect.LastSummary.Length == 0)
            return (UiState.Empty, "no selection yet");
        return (UiState.Ready, RunTracker.Focus.Battle >= 0
            ? $"next: {BST_CrucibleData.BattleLabel(RunTracker.Focus.Board, RunTracker.Focus.Battle)}"
            : "settings and guide ready");
    }

    private float DrawTopBar(Vector2 winPos, float winWidth, float gutter)
    {
        var draw = ImGui.GetWindowDrawList();
        var gap = 2 * Theme.Unit;
        var wideGap = 4 * Theme.Unit;
        var inner = winWidth - (2 * gutter);
        var groups = Pages.Select(p => p.Group).Distinct().ToArray();
        var brandSize = new Vector2(CrucibleChrome.BrandWidth(), 10 * Theme.Unit);
        var groupSizes = groups.Select(group => new Vector2(MathF.Max(18 * Theme.Unit, CrucibleChrome.TextWidth(group) + (8 * Theme.Unit)), CrucibleChrome.ControlHeight)).ToArray();
        var (state, _) = OverallState();
        var stateLabel = Copy.Word(state);
        var pillSize = new Vector2(CrucibleChrome.PillWidth(), CrucibleChrome.ControlHeight);
        var closeSize = new Vector2(CrucibleChrome.ControlHeight);
        var groupsWidth = groupSizes.Sum(size => size.X) + ((groups.Length - 1) * gap);
        var rightWidth = pillSize.X + gap + closeSize.X;
        const float minSearch = 180;
        const float maxSearch = 320;
        var navWidth = brandSize.X + wideGap + groupsWidth;
        var padY = 3.5f * Theme.Unit;
        var rowHeight = brandSize.Y;

        var oneRow = inner >= navWidth + wideGap + minSearch + wideGap + rightWidth;
        var twoRows = !oneRow && inner >= navWidth + wideGap + rightWidth;
        var groupPositions = new Vector2[groups.Length];
        var groupSizesUsed = (Vector2[])groupSizes.Clone();
        Vector2 brandPos, pillPos, closePos, searchPos;
        float searchWidth;
        float barHeight;
        var rowTop = padY;
        var controlY = rowTop + ((rowHeight - CrucibleChrome.ControlHeight) / 2f);
        brandPos = new Vector2(gutter, rowTop);
        closePos = new Vector2(winWidth - gutter - closeSize.X, controlY);
        pillPos = new Vector2(closePos.X - gap - pillSize.X, controlY);
        if (oneRow || twoRows)
        {
            var x = gutter + brandSize.X + wideGap;
            for (var i = 0; i < groups.Length; i++)
            {
                groupPositions[i] = new Vector2(x, controlY);
                x += groupSizes[i].X + gap;
            }
            if (oneRow)
            {
                var room = pillPos.X - wideGap - (gutter + navWidth + wideGap);
                searchWidth = MathF.Min(maxSearch, room);
                searchPos = new Vector2(pillPos.X - wideGap - searchWidth, controlY);
                barHeight = rowTop + rowHeight + padY;
            }
            else
            {
                searchPos = new Vector2(gutter, rowTop + rowHeight + gap);
                searchWidth = inner;
                barHeight = searchPos.Y + CrucibleChrome.ControlHeight + padY;
            }
        }
        else
        {
            // Compact: brand with status on the first row, groups sharing the second, search on its own row.
            var groupRowY = rowTop + rowHeight + gap;
            var each = (inner - ((groups.Length - 1) * gap)) / groups.Length;
            for (var i = 0; i < groups.Length; i++)
            {
                groupSizesUsed[i] = new Vector2(each, CrucibleChrome.ControlHeight);
                groupPositions[i] = new Vector2(gutter + (i * (each + gap)), groupRowY);
            }
            searchPos = new Vector2(gutter, groupRowY + CrucibleChrome.ControlHeight + gap);
            searchWidth = inner;
            barHeight = searchPos.Y + CrucibleChrome.ControlHeight + padY;
        }

        draw.PushClipRect(winPos, winPos + new Vector2(winWidth, barHeight + 1), false);
        draw.AddRectFilled(winPos, winPos + new Vector2(winWidth, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, barHeight), winPos + new Vector2(winWidth, barHeight), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();
        if (CrucibleChrome.BrandButton(draw, winPos + brandPos, brandSize))
            CurrentPage = OverviewPage;
        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            var first = Pages.First(p => p.Group == group);
            if (CrucibleChrome.OutlineButton(draw, "group_" + group, group, winPos + groupPositions[i], groupSizesUsed[i], Current.Group == group))
                CurrentPage = first.Id;
        }
        DrawSearchInput(winPos + searchPos, searchWidth);
        CrucibleChrome.StatusPill(draw, winPos + pillPos, pillSize, stateLabel, state);
        if (CrucibleChrome.CloseButton(draw, winPos + closePos, closeSize))
            CloseRequested?.Invoke();
        DrawSearchResults(winPos + searchPos, searchWidth);
        return barHeight + 1;
    }

    private float DrawFeatureBar(Vector2 winPos, float winWidth, float gutter, float top)
    {
        var draw = ImGui.GetWindowDrawList();
        var inner = winWidth - (2 * gutter);
        var gap = 3 * Theme.Unit;
        var features = Pages.Where(p => p.Group == Current.Group).ToArray();
        if (features.Length < 2)
            return top;
        var titles = features.Select(FeatureTitle).ToArray();
        var heightEach = 8 * Theme.Unit;
        var padY = 3 * Theme.Unit;
        var sizes = titles.Select(title => new Vector2(CrucibleChrome.FeatureWidth(title), heightEach)).ToArray();
        var total = sizes.Sum(size => size.X) + ((features.Length - 1) * gap);
        var collapsed = total > inner;
        float barHeight;
        if (collapsed)
        {
            barHeight = padY + CrucibleChrome.ControlHeight + padY;
            DrawBar(draw, winPos, winWidth, top, barHeight);
            var fieldPos = winPos + new Vector2(gutter, top + padY);
            var fieldSize = new Vector2(inner, CrucibleChrome.ControlHeight);
            ImGui.SetCursorScreenPos(fieldPos);
            var open = ImGui.InvisibleButton("##crucible_feature_menu", fieldSize);
            var hovered = ImGui.IsItemHovered();
            draw.AddRectFilled(fieldPos, fieldPos + fieldSize, Theme.Pack(Theme.Accent with { W = 0.14f }), CrucibleChrome.Radius);
            draw.AddRect(fieldPos, fieldPos + fieldSize, Theme.Pack(hovered ? Theme.Accent : Theme.Accent with { W = 0.6f }), CrucibleChrome.Radius);
            var currentTitle = FeatureTitle(Current);
            CrucibleChrome.DrawFitted(draw, fieldPos + new Vector2(CrucibleChrome.Gap, (fieldSize.Y - CrucibleChrome.LineHeight()) / 2f), currentTitle,
                inner - (4 * CrucibleChrome.Gap), Theme.Accent);
            CrucibleChrome.Chevron(draw, fieldPos + new Vector2(inner - CrucibleChrome.Gap - 4, fieldSize.Y / 2f), Theme.Accent);
            if (open)
                ImGui.OpenPopup("##crucibleFeatureMenu");
            ImGui.SetNextWindowPos(fieldPos + new Vector2(0, fieldSize.Y + Theme.Unit));
            ImGui.SetNextWindowSize(new Vector2(inner, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, CrucibleChrome.Radius);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2 * Theme.Unit, 2 * Theme.Unit));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2 * Theme.Unit, Theme.Unit));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
            ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
            ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent with { W = 0.20f });
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.14f });
            if (ImGui.BeginPopup("##crucibleFeatureMenu"))
            {
                for (var i = 0; i < features.Length; i++)
                    if (ImGui.Selectable(titles[i] + "##feature_" + features[i].Id, CurrentPage == features[i].Id, ImGuiSelectableFlags.None, new Vector2(0, CrucibleChrome.ControlHeight - 8)))
                        CurrentPage = features[i].Id;
                ImGui.EndPopup();
            }
            ImGui.PopStyleColor(4);
            ImGui.PopStyleVar(4);
        }
        else
        {
            barHeight = padY + heightEach + padY;
            DrawBar(draw, winPos, winWidth, top, barHeight);
            var x = gutter;
            for (var i = 0; i < features.Length; i++)
            {
                if (CrucibleChrome.FeatureButton(draw, features[i].Id, titles[i], winPos + new Vector2(x, top + padY), sizes[i], CurrentPage == features[i].Id))
                    CurrentPage = features[i].Id;
                x += sizes[i].X + gap;
            }
        }
        return top + barHeight + 1;
    }

    private static void DrawBar(ImDrawListPtr draw, Vector2 winPos, float winWidth, float top, float height)
    {
        draw.PushClipRect(winPos + new Vector2(0, top), winPos + new Vector2(winWidth, top + height + 1), false);
        draw.AddRectFilled(winPos + new Vector2(0, top), winPos + new Vector2(winWidth, top + height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, top + height), winPos + new Vector2(winWidth, top + height), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();
    }

    private static string FeatureTitle(CruciblePage feature) => feature.Id == AdvisorPage ? "Beast picks by battle" :
        feature.Id == "guide.recent" ? "Recent choices" : feature.Title;

    private void DrawSearchInput(Vector2 pos, float width)
    {
        ImGui.SetCursorScreenPos(pos);
        ImGui.SetNextItemWidth(width);
        var framePadding = new Vector2(CrucibleChrome.Gap, MathF.Max(Theme.Unit, (CrucibleChrome.ControlHeight - ImGui.GetTextLineHeight()) / 2f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, framePadding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, CrucibleChrome.Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.InputTextWithHint("##crucibleSearch", "Search settings and fights...", ref SearchText, 256);
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(3);
    }

    /// <summary>Results float under the search box as a popup; opening it never moves the page.</summary>
    private void DrawSearchResults(Vector2 searchPos, float searchWidth)
    {
        const string popupId = "##crucibleSearchResults";
        var query = SearchText.Trim();
        if (query.Length == 0)
            return;
        var matches = Pages.Where(p => (p.Title + " " + p.SearchTerms).Contains(query, StringComparison.OrdinalIgnoreCase)).Take(6).ToList();
        var width = MathF.Max(searchWidth, 300);
        ImGui.SetNextWindowPos(searchPos + new Vector2(0, CrucibleChrome.ControlHeight + Theme.Unit));
        ImGui.SetNextWindowSize(new Vector2(width, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, CrucibleChrome.Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(3 * Theme.Unit, 3 * Theme.Unit));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2 * Theme.Unit, 2 * Theme.Unit));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent with { W = 0.20f });
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.14f });
        if (!ImGui.IsPopupOpen(popupId))
            ImGui.OpenPopup(popupId);
        if (ImGui.BeginPopup(popupId, ImGuiWindowFlags.NoFocusOnAppearing))
        {
            Widgets.Caption(matches.Count == 1 ? $"1 destination for ‘{SearchText}’" : $"{matches.Count} destinations for ‘{SearchText}’");
            foreach (var match in matches)
                if (ImGui.Selectable($"{match.Group} · {match.Title}##search_{match.Id}"))
                {
                    CurrentPage = match.Id;
                    SearchText = "";
                }
            ImGui.EndPopup();
        }
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(4);
    }

    private float DrawPageHeader(Vector2 winPos, float winWidth, float gutter, float top)
    {
        var page = Current;
        var title = page.Title;
        var description = page.Description;
        var count = CruciblePages.LiveCount(this, CurrentPage);
        var subtext = CruciblePages.LiveSubtext(this, CurrentPage);
        if (CurrentPage == UpcomingPage)
        {
            if (GuideFollow && RunTracker.Focus.Battle < 0)
                subtext = "";
            else if (Plugin.Guide.Fight(GuideBoard, GuideBattle) is { } fight)
            {
                title = fight.Title;
                description = fight.Summary;
            }
        }
        var draw = ImGui.GetWindowDrawList();
        var tile = 11 * Theme.Unit;
        var padTop = 5 * Theme.Unit;
        var wide = winWidth >= 760;
        var textX = gutter + tile + (4 * Theme.Unit);
        var metaWidth = wide ? MathF.Min(winWidth * 0.40f, MathF.Max(CrucibleChrome.TextWidth(count, Theme.SecondaryScale), CrucibleChrome.TextWidth(subtext, Theme.SecondaryScale))) : 0;
        var textWidth = winWidth - textX - gutter - (wide ? metaWidth + (6 * Theme.Unit) : 0);
        var origin = winPos + new Vector2(gutter, top + padTop);
        draw.AddRectFilled(origin, origin + new Vector2(tile), Theme.Pack(Theme.Accent with { W = 0.14f }), CrucibleChrome.Radius);
        CrucibleChrome.Diamond(draw, origin + new Vector2(tile / 2f), 10, Theme.Pack(Theme.Accent));

        var shownTitle = CrucibleChrome.ClampLines(title, textWidth, CrucibleChrome.PageTitleScale, wide ? 1 : 2);
        var titleHeight = CrucibleChrome.WrappedHeight(shownTitle, textWidth, CrucibleChrome.PageTitleScale);
        var textOrigin = new Vector2(winPos.X + textX, origin.Y);
        CrucibleChrome.DrawWrapped(draw, textOrigin, shownTitle, textWidth, Theme.TextPrimary, CrucibleChrome.PageTitleScale);
        var shownDescription = CrucibleChrome.ClampLines(description, textWidth, Theme.SecondaryScale, wide ? 1 : 3);
        var descriptionTop = textOrigin.Y + titleHeight + Theme.Unit;
        var descriptionHeight = CrucibleChrome.DrawWrapped(draw, new Vector2(textOrigin.X, descriptionTop), shownDescription, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
        var blockBottom = descriptionTop + descriptionHeight;
        if ((shownTitle != title || shownDescription != description)
            && ImGui.IsMouseHoveringRect(textOrigin, new Vector2(textOrigin.X + textWidth, blockBottom)))
            ImGui.SetTooltip(shownTitle != title ? title : description);

        if (wide)
        {
            var right = winPos.X + winWidth - gutter;
            var metaScale = Theme.SecondaryScale;
            var line1 = textOrigin.Y + ((titleHeight - CrucibleChrome.LineHeight(metaScale)) / 2f);
            var line2 = descriptionTop;
            CrucibleChrome.DrawFittedRight(draw, right, line1, count, metaWidth, Theme.TextSecondary, metaScale);
            CrucibleChrome.DrawFittedRight(draw, right, line2, subtext, metaWidth, Theme.TextSecondary, metaScale);
        }
        else
        {
            var y = blockBottom + Theme.Unit;
            foreach (var meta in new[] { count, subtext })
            {
                if (meta.Length == 0)
                    continue;
                CrucibleChrome.DrawFitted(draw, new Vector2(textOrigin.X, y), meta, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
                y += CrucibleChrome.LineHeight(Theme.SecondaryScale) + (Theme.Unit / 2f);
            }
            blockBottom = y;
        }
        var height = MathF.Max(tile, blockBottom - origin.Y);
        return top + padTop + height + (2 * Theme.Unit);
    }

    private void DrawStatusBanner()
    {
        if (PetSelect.YieldReason == "conflict_gluttony")
            CrucibleChrome.Notice("LazyCrucible is not writing — an older GluttonyCombo that also fills familiars is loaded and must be updated.", UiState.Failed);
        else if (PetSelect.YieldReason == "autoduty_running")
            CrucibleChrome.Notice("AutoDuty standby", Policy.AutoDutyCrucible.StandingBy(ExternalDrivers.DrivenSurfaces), UiState.Blocked);
        else if (Plugin.Config.YieldToAutoDuty && ExternalDrivers.StallReason is { } stall)
            CrucibleChrome.Notice("AutoDuty attention", $"AutoDuty reports running but has stopped stepping ({StallText(stall)}); LazyCrucible is not standing down for it.", UiState.Blocked);

        // Incoming guidance is independent of the main AutoDuty/conflict banner and must never be suppressed.
        if (Incoming is { } warn)
        {
            var guideDo = Plugin.Guide.File.Fights.SelectMany(f => f.Hits.Concat(f.Mechanics))
                .Where(m => m.Ids.Contains(warn.CastId) && m.Do.Length > 0).Select(m => m.Do).FirstOrDefault();
            CrucibleChrome.Notice("Incoming", warn.Text + (guideDo is null ? "" : " Guide: " + guideDo), UiState.Running, strong: true);
        }
        if (CurrentPage == UpcomingPage && Plugin.Guide.Fight(GuideBoard, GuideBattle) is { } fight
            && RunTracker.Focus == (fight.Board, fight.Battle)
            && FightWarnings.EntryAdvice(fight.Board, fight.Battle, RunTracker.PlayerHpPercent) is { } advice)
            CrucibleChrome.Notice("Before this fight", advice, UiState.Blocked);
    }

    private static string StallText(string reason) => reason switch
    {
        "not_running" => "neither looping nor navigating",
        "idle_in_board" => "not navigating this board",
        "navigating_in_lobby" => "still navigating after leaving its run",
        "other_board" => "driving a different board",
        "no_progress_in_lobby" => "no progress in the lobby",
        _ => reason,
    };

    private static float FooterHeight(float width)
    {
        var textBlock = CrucibleChrome.LineHeight() + Theme.Unit + CrucibleChrome.LineHeight(Theme.SecondaryScale);
        const float pad = 3 * Theme.Unit;
        return width < 620
            ? pad + textBlock + pad + CrucibleChrome.ControlHeight + pad
            : pad + MathF.Max(textBlock, CrucibleChrome.ControlHeight) + pad;
    }

    private static readonly System.Text.RegularExpressions.Regex Parenthetical = new(@"\s*\([^)]*\)", System.Text.RegularExpressions.RegexOptions.Compiled);

    private void DrawFooter(Vector2 winPos, Vector2 winSize, float gutter, float height)
    {
        var top = winPos.Y + winSize.Y - height;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(new Vector2(winPos.X, top), winPos + winSize, false);
        draw.AddRectFilled(new Vector2(winPos.X, top), winPos + winSize, Theme.Pack(Theme.ShellBar));
        draw.AddLine(new Vector2(winPos.X, top), new Vector2(winPos.X + winSize.X, top), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();
        var (state, detail) = OverallState();
        string primary;
        string fullPrimary;
        string secondary;
        if (Current.Group == "Guide")
        {
            primary = state switch
            {
                UiState.Running => "Incoming cast · guidance only",
                UiState.Blocked when detail == "Low HP" => "Low HP · review the entry warning",
                UiState.Blocked => "Blocked · selection needs attention",
                UiState.Empty => "Empty · no guide entry for the selected fight",
                UiState.Idle => "Idle · no fight in focus",
                UiState.Missing => "Needs setup · Missing Bestiary",
                _ => "Ready · guide and horn picks use the same ranking",
            };
            fullPrimary = primary;
            secondary = state == UiState.Missing
                ? "Open the Master's Bestiary once; picks assume every familiar is captured until then."
                : "Follow the next fight uses the existing focus; no action is added.";
        }
        else
        {
            fullPrimary = PetSelect.LastSummary.Length == 0 ? "No familiar selection yet this session." : $"{PetSelect.LastSummaryAt:HH:mm:ss}  {PetSelect.LastSummary}";
            // The summary lists each horn with its traits; the footer keeps the names and the full line is the tooltip.
            primary = PetSelect.LastSummary.Length == 0 ? fullPrimary
                : $"{PetSelect.LastSummaryAt:HH:mm:ss}  {Parenthetical.Replace(PetSelect.LastSummary, "")}";
            secondary = SelectionScreens.Status.Length > 0 ? SelectionScreens.Status : detail;
        }

        var actionLabel = Current.Group == "Guide" ? "Return to run setup" : "Open the fight guide";
        var narrow = winSize.X < 620;
        var inner = winSize.X - (2 * gutter);
        var actionSize = new Vector2(narrow ? inner : CrucibleChrome.ActionWidth(96, actionLabel), CrucibleChrome.ControlHeight);
        var textBlock = CrucibleChrome.LineHeight() + Theme.Unit + CrucibleChrome.LineHeight(Theme.SecondaryScale);
        var pad = 3 * Theme.Unit;
        var textLeft = winPos.X + gutter + (4 * Theme.Unit);
        Vector2 actionPos;
        float textTop;
        float textRight;
        if (narrow)
        {
            textTop = top + pad;
            actionPos = new Vector2(winPos.X + gutter, textTop + textBlock + pad);
            textRight = winPos.X + winSize.X - gutter;
        }
        else
        {
            textTop = top + ((height - textBlock) / 2f);
            actionPos = new Vector2(winPos.X + winSize.X - gutter - actionSize.X, top + ((height - actionSize.Y) / 2f));
            textRight = actionPos.X - (4 * Theme.Unit);
        }
        draw.AddCircleFilled(new Vector2(winPos.X + gutter + Theme.Unit, textTop + (CrucibleChrome.LineHeight() / 2f)), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(state)));
        var textWidth = textRight - textLeft;
        var shown = CrucibleChrome.Fit(primary, textWidth);
        CrucibleChrome.DrawText(draw, new Vector2(textLeft, textTop), shown, Theme.TextPrimary);
        var shownSecondary = CrucibleChrome.Fit(secondary, textWidth, Theme.SecondaryScale);
        CrucibleChrome.DrawText(draw, new Vector2(textLeft, textTop + CrucibleChrome.LineHeight() + Theme.Unit), shownSecondary, Theme.TextSecondary, Theme.SecondaryScale);
        if ((shown != fullPrimary || shownSecondary != secondary)
            && ImGui.IsMouseHoveringRect(new Vector2(textLeft, textTop), new Vector2(textRight, textTop + textBlock)))
            ImGui.SetTooltip(fullPrimary + "\n" + secondary);
        if (CrucibleChrome.OutlineButton(draw, "footer_action", actionLabel, actionPos, actionSize))
        {
            if (Current.Group == "Guide")
                CurrentPage = RosterHornsPage;
            else
                _plugin?.ToggleGuide();
        }
    }
}
