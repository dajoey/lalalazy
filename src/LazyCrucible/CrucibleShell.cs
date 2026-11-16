using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyCrucible;

internal sealed record CruciblePage(string Id, string Group, string Title, string Description, string SearchTerms);

/// <summary>Per-plugin navigation, focus, search, status and save-on-change state for the structural UI.</summary>
internal sealed class CrucibleShell
{
    private const float ControlHeight = 34f;
    private const float WideFooterHeight = 68f;
    private const float NarrowFooterHeight = 100f;

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

    public void Draw(Plugin? plugin)
    {
        _plugin = plugin ?? _plugin;
        _changed = false;
        Theme.PushFrame();
        try
        {
            DrawTopGroups();
            DrawSearch();
            DrawFeatureRow();
            DrawPageHeader();
            DrawStatusBanner();
            var footerHeight = FooterHeight(ImGui.GetContentRegionAvail().X);
            ImGui.BeginChild("##crucibleBody", new Vector2(0, MathF.Max(40, ImGui.GetContentRegionAvail().Y - footerHeight)), false);
            CruciblePages.Render(this, CurrentPage);
            ImGui.EndChild();
            DrawFooter();
        }
        finally
        {
            Theme.PopFrame();
            if (_changed)
                Plugin.Config.Save();
        }
    }

    public void Setting(string literalLabel, Func<bool> get, Action<bool> set, string? help = null)
    {
        var value = get();
        if (!CrucibleChrome.Toggle(literalLabel, ref value, help))
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

    private void DrawTopGroups()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var gap = 2 * Theme.Unit;
        var groups = Pages.Select(p => p.Group).Distinct().ToArray();
        var brandSize = new Vector2(MathF.Max(180, (ImGui.CalcTextSize("LazyCrucible").X * Theme.TitleScale) + 68), 42);
        var groupSizes = groups.Select(group => new Vector2(MathF.Max(72, ImGui.CalcTextSize(group).X + 32), ControlHeight)).ToArray();
        var (state, _) = OverallState();
        var stateLabel = Copy.Word(state);
        var statusSize = new Vector2(MathF.Max(76, ImGui.CalcTextSize(stateLabel).X + 43), ControlHeight);
        var closeSize = new Vector2(ControlHeight);
        var leftWidth = brandSize.X + gap + groupSizes.Sum(size => size.X) + ((groupSizes.Length - 1) * gap);
        const float minSearchWidth = 180;
        const float maxSearchWidth = 280;
        var singleRow = width >= leftWidth + minSearchWidth + statusSize.X + closeSize.X + (4 * gap);
        var groupPositions = new Vector2[groups.Length];
        var brandPos = new Vector2(0, 12);
        float searchWidth;
        Vector2 searchPos;
        Vector2 statusPos;
        Vector2 closePos;
        float barHeight;

        if (singleRow)
        {
            barHeight = 66;
            var x = brandSize.X + gap;
            var y = (barHeight - ControlHeight) / 2f;
            for (var i = 0; i < groups.Length; i++)
            {
                groupPositions[i] = new Vector2(x, y);
                x += groupSizes[i].X + gap;
            }
            searchWidth = MathF.Min(maxSearchWidth, width - leftWidth - statusSize.X - closeSize.X - (4 * gap));
            statusPos = new Vector2(width - statusSize.X - closeSize.X - gap, y);
            closePos = new Vector2(width - closeSize.X, y);
            searchPos = new Vector2(statusPos.X - gap - searchWidth, y);
        }
        else
        {
            var groupsBesideBrand = leftWidth <= width;
            var x = groupsBesideBrand ? brandSize.X + gap : 0;
            var y = groupsBesideBrand ? 16f : 62f;
            for (var i = 0; i < groups.Length; i++)
            {
                var buttonWidth = MathF.Min(groupSizes[i].X, width);
                if (x > 0 && x + buttonWidth > width)
                {
                    x = 0;
                    y += ControlHeight + gap;
                }
                groupPositions[i] = new Vector2(x, y);
                x += buttonWidth + gap;
            }
            var navigationBottom = groupsBesideBrand ? brandPos.Y + brandSize.Y : y + ControlHeight;
            var searchY = navigationBottom + gap;
            searchWidth = MathF.Min(maxSearchWidth, MathF.Max(120, width - statusSize.X - closeSize.X - (2 * gap)));
            statusPos = new Vector2(width - statusSize.X - closeSize.X - gap, searchY);
            closePos = new Vector2(width - closeSize.X, searchY);
            searchPos = new Vector2(statusPos.X - gap - searchWidth, searchY);
            barHeight = searchY + ControlHeight + 12;
        }

        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        if (CrucibleChrome.BrandButton(draw, origin + brandPos, brandSize))
            CurrentPage = OverviewPage;
        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            var first = Pages.First(p => p.Group == group);
            NavButton("group_" + group, group, origin + groupPositions[i], groupSizes[i], Current.Group == group, () => CurrentPage = first.Id);
        }
        DrawSearchInput(origin + searchPos, searchWidth);
        CrucibleChrome.StatusPill(draw, origin + statusPos, statusSize, stateLabel, state);
        if (CrucibleChrome.OutlineButton(draw, "close_window", "×", origin + closePos, closeSize))
            CloseRequested?.Invoke();
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private static void NavButton(string id, string label, Vector2 pos, Vector2 size, bool selected, Action navigate)
    {
        if (CrucibleChrome.OutlineButton(ImGui.GetWindowDrawList(), id, label, pos, size, selected))
            navigate();
    }

    private void DrawFeatureRow()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var gap = 3 * Theme.Unit;
        var features = Pages.Where(p => p.Group == Current.Group).ToArray();
        var titles = features.Select(FeatureTitle).ToArray();
        var sizes = titles.Select(title => new Vector2(
            MathF.Min(width, MathF.Max(110, ImGui.CalcTextSize(title).X + 52)), ControlHeight)).ToArray();
        var positions = new Vector2[features.Length];
        float x = 0;
        float y = 8;
        for (var i = 0; i < features.Length; i++)
        {
            if (x > 0 && x + sizes[i].X > width)
            {
                x = 0;
                y += ControlHeight + (2 * Theme.Unit);
            }
            positions[i] = new Vector2(x, y);
            x += sizes[i].X + gap;
        }
        var barHeight = y + ControlHeight + 8;
        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        for (var i = 0; i < features.Length; i++)
        {
            var feature = features[i];
            if (CrucibleChrome.FeatureButton(draw, feature.Id, titles[i], origin + positions[i], sizes[i], CurrentPage == feature.Id))
                CurrentPage = feature.Id;
        }
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private static string FeatureTitle(CruciblePage feature) => feature.Id == AdvisorPage ? "Beast picks by battle" :
        feature.Id == "guide.recent" ? "Recent choices" : feature.Title;

    private void DrawSearchInput(Vector2 pos, float width)
    {
        ImGui.SetCursorScreenPos(pos);
        ImGui.SetNextItemWidth(width);
        var framePadding = Theme.FramePadding;
        framePadding.Y = MathF.Max(framePadding.Y, (ControlHeight - ImGui.GetTextLineHeight()) / 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, framePadding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2 * Theme.Unit);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.SurfaceBorder);
        ImGui.InputTextWithHint("##crucibleSearch", "Search settings and fights...", ref SearchText, 256);
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
    }

    private void DrawSearch()
    {
        if (SearchText.Trim().Length == 0)
            return;
        var matches = Pages.Where(p => (p.Title + " " + p.SearchTerms).Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)).Take(6).ToList();
        Widgets.Caption($"{matches.Count} destinations for ‘{SearchText}’");
        if (matches.Count == 0)
            return;
        var height = MathF.Min(240, (matches.Count * ImGui.GetFrameHeightWithSpacing()) + 12);
        ImGui.BeginChild("##crucibleSearchResults", new Vector2(0, height), true);
        foreach (var match in matches)
            if (ImGui.Selectable($"{match.Group} · {match.Title}##search_{match.Id}"))
            {
                CurrentPage = match.Id;
                SearchText = "";
            }
        ImGui.EndChild();
    }

    private void DrawPageHeader()
    {
        var page = Current;
        var title = page.Title;
        var description = page.Description;
        if (CurrentPage == UpcomingPage && Plugin.Guide.Fight(GuideBoard, GuideBattle) is { } fight)
        {
            title = fight.Title;
            description = fight.Summary;
        }
        var count = CruciblePages.LiveCount(this, CurrentPage);
        var subtext = CruciblePages.LiveSubtext(this, CurrentPage);
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var tileSize = new Vector2(42);
        draw.AddRectFilled(origin, origin + tileSize, Theme.Pack(Theme.Accent with { W = 0.14f }), 2 * Theme.Unit);
        CrucibleChrome.Diamond(draw, origin + (tileSize / 2f), 10, Theme.Pack(Theme.Accent));

        if (width >= 1100)
        {
            var textX = origin.X + tileSize.X + 12;
            var countWidth = ImGui.CalcTextSize(count).X;
            var subtextWidth = ImGui.CalcTextSize(subtext).X;
            DrawText(draw, new Vector2(textX, origin.Y), FitText(title, width - tileSize.X - 12 - countWidth - 16, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            DrawText(draw, new Vector2(textX, origin.Y + 29), FitText(description, width - tileSize.X - 12 - subtextWidth - 16), Theme.TextSecondary);
            DrawTextRight(draw, origin.X + width, origin.Y + 4, count, Theme.TextSecondary);
            DrawTextRight(draw, origin.X + width, origin.Y + 31, subtext, Theme.TextSecondary);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, 58));
            ImGui.Separator();
            return;
        }

        DrawText(draw, origin + new Vector2(tileSize.X + 12, 4), FitText(title, width - tileSize.X - 12, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, tileSize.Y + 8));
        Widgets.SecondaryWrapped(description);
        Widgets.Caption(count);
        Widgets.Caption(subtext);
        ImGui.Separator();
    }

    private void DrawStatusBanner()
    {
        if (PetSelect.YieldReason == "conflict_gluttony")
            Widgets.InfoBox("LazyCrucible is not writing — an older GluttonyCombo that also fills familiars is loaded and must be updated.", UiState.Failed);
        else if (PetSelect.YieldReason == "autoduty_running")
            Widgets.InfoBox("AutoDuty standby — " + Policy.AutoDutyCrucible.StandingBy(ExternalDrivers.DrivenSurfaces), UiState.Blocked);
        else if (Plugin.Config.YieldToAutoDuty && ExternalDrivers.StallReason is { } stall)
            Widgets.InfoBox($"AutoDuty reports running but has stopped stepping ({StallText(stall)}); LazyCrucible is not standing down for it.", UiState.Blocked);

        // Incoming guidance is independent of the main AutoDuty/conflict banner and must never be suppressed.
        if (Incoming is { } warn)
        {
            var guideDo = Plugin.Guide.File.Fights.SelectMany(f => f.Hits.Concat(f.Mechanics))
                .Where(m => m.Ids.Contains(warn.CastId) && m.Do.Length > 0).Select(m => m.Do).FirstOrDefault();
            Widgets.InfoBox("INCOMING — " + warn.Text + (guideDo is null ? "" : " Guide: " + guideDo), UiState.Running);
        }
        if (CurrentPage == UpcomingPage && Plugin.Guide.Fight(GuideBoard, GuideBattle) is { } fight
            && RunTracker.Focus == (fight.Board, fight.Battle)
            && FightWarnings.EntryAdvice(fight.Board, fight.Battle, RunTracker.PlayerHpPercent) is { } advice)
            Widgets.InfoBox("Before this fight — " + advice, UiState.Blocked);
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

    private static float FooterHeight(float width) => width < 620 ? NarrowFooterHeight : WideFooterHeight;

    private void DrawFooter()
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = FooterHeight(width);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        var (state, detail) = OverallState();
        string primary;
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
            secondary = state == UiState.Missing
                ? "Open the Master's Bestiary once; picks assume every familiar is captured until then."
                : "Follow the next fight uses the existing focus; no action is added.";
        }
        else
        {
            primary = PetSelect.LastSummary.Length == 0 ? "No familiar selection yet this session." : $"{PetSelect.LastSummaryAt:HH:mm:ss}  {PetSelect.LastSummary}";
            secondary = SelectionScreens.Status.Length > 0 ? SelectionScreens.Status : detail;
        }

        var actionLabel = Current.Group == "Guide" ? "Return to run setup" : "Open the fight guide";
        var actionWidth = MathF.Min(width, MathF.Max(180, ImGui.CalcTextSize(actionLabel).X + 48));
        var narrow = width < 620;
        var actionPos = origin + new Vector2(width - actionWidth, narrow ? 56 : 16);
        var textRight = narrow ? origin.X + width : actionPos.X - 16;
        var textPos = origin + new Vector2(24, 9);
        draw.AddCircleFilled(origin + new Vector2(10, 19), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(state)));
        DrawText(draw, textPos, FitText(primary, textRight - textPos.X), Theme.TextPrimary);
        DrawText(draw, textPos + new Vector2(0, 27), FitText(secondary, textRight - textPos.X), Theme.TextSecondary);
        if (CrucibleChrome.OutlineButton(draw, "footer_action", actionLabel, actionPos, new Vector2(actionWidth, 36)))
        {
            if (Current.Group == "Guide")
                CurrentPage = RosterHornsPage;
            else
                _plugin?.ToggleGuide();
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
    }

    private static void DrawText(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1)
    {
        if (text.Length == 0)
            return;
        if (scale == 1)
            draw.AddText(pos, Theme.Pack(color), text);
        else
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
    }

    private static void DrawTextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color)
    {
        if (text.Length == 0)
            return;
        draw.AddText(new Vector2(right - ImGui.CalcTextSize(text).X, y), Theme.Pack(color), text);
    }

    private static string FitText(string text, float width, float scale = 1)
    {
        if (width <= 0)
            return "";
        if (ImGui.CalcTextSize(text).X * scale <= width)
            return text;
        const string ellipsis = "…";
        var length = text.Length;
        while (length > 0 && ImGui.CalcTextSize(text[..length] + ellipsis).X * scale > width)
            length--;
        return length == 0 ? "" : text[..length].TrimEnd() + ellipsis;
    }
}
