using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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
        new(OverviewPage, "Home", "Crucible overview", "Live readiness, recent activity, and useful destinations.", "home overview readiness activity"),
        new("run.board_overview", "Run", "Board overview", "Board progression and the fight currently in focus.", "run board progression"),
        new(RosterHornsPage, "Run", "Roster & horns", "Familiar roster settings and the live Battlehorn plan.", "run roster horns score auto duty"),
        new("run.feed_shop", "Run", "Feed & shop", "Feeding, purchases and score-goal behavior.", "run feed shop score goal"),
        new("run.campsite", "Run", "Campsite", "Familiar rest selection with Rest left to the player.", "run campsite rest"),
        new("run.display_logging", "Run", "Display & logging", "Guide opening, announcements and read-only recording.", "display logging announce record report"),
        new("rewards.spoils", "Rewards", "Spoils", "Take all only when rewards fit; otherwise choose.", "rewards spoils loot"),
        new("rewards.treasure", "Rewards", "Treasure", "Suggestion-only coffer guidance.", "rewards treasure coffer"),
        new(UpcomingPage, "Guide", "Upcoming fight", "Horn picks, calls, mechanics and advice for one fight.", "guide upcoming follow fight"),
        new("guide.browse", "Guide", "Browse boards", "Browse every real guide entry by board and battle.", "guide browse boards battles"),
        new(AdvisorPage, "Guide", "Beast picks by battle", "Board advisor, ranked picks and capture advice.", "guide beast picks advisor bestiary capture"),
        new("guide.recent", "Guide", "What LazyCrucible chose this session", "The newest 30 selection decisions.", "guide recent choices session"),
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
            var footerHeight = (2 * ImGui.GetFrameHeightWithSpacing()) + (2 * Theme.Unit);
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

    public void Setting(string literalLabel, Func<bool> get, Action<bool> set)
    {
        var value = get();
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Theme.Accent);
        var changed = ImGui.Checkbox("##crucible_setting_" + literalLabel, ref value);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
        ImGui.TextWrapped(literalLabel);
        ImGui.PopTextWrapPos();
        if (!changed)
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
        var p = ImGui.GetCursorScreenPos();
        var height = Theme.PillHeight;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, p + new Vector2(width, height), Theme.Pack(Theme.ShellBar), 4f);
        dl.AddRect(p, p + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 4f);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(2 * Theme.Unit, (height - ImGui.GetTextLineHeight()) / 2f));
        Widgets.Title("LazyCrucible");
        ImGui.SameLine();
        foreach (var group in Pages.Select(p => p.Group).Distinct())
        {
            var first = Pages.First(p => p.Group == group);
            NavButton(group, Current.Group == group, () => CurrentPage = first.Id);
            ImGui.SameLine();
        }
        var (state, detail) = OverallState();
        var pillDetail = state switch
        {
            UiState.Missing => "Bestiary",
            UiState.Ready when RunTracker.Focus.Battle < 0 => null,
            _ => detail,
        };
        var word = Copy.Word(state);
        var label = string.IsNullOrEmpty(pillDetail) ? $"Crucible · {word}" : $"Crucible · {word} · {pillDetail}";
        var pillWidth = ImGui.CalcTextSize(label).X + 40;
        if (ImGui.GetContentRegionAvail().X < pillWidth + Theme.Unit)
            ImGui.NewLine();
        var pillPos = new Vector2(p.X + width - pillWidth, p.Y);
        dl.AddRectFilled(pillPos, pillPos + new Vector2(pillWidth, height), Theme.Pack(Theme.Surface), height / 2f);
        dl.AddRect(pillPos, pillPos + new Vector2(pillWidth, height), Theme.Pack(Theme.SurfaceBorder), height / 2f);
        dl.AddCircleFilled(pillPos + new Vector2(12, height / 2f), Theme.DotRadius, Theme.Pack(Copy.MarkColor(state)));
        dl.AddText(pillPos + new Vector2(24, (height - ImGui.GetTextLineHeight()) / 2f), Theme.Pack(Theme.TextPrimary), label);
        ImGui.SetCursorScreenPos(p + new Vector2(0, height + Theme.ItemSpacing.Y));
    }

    private static void NavButton(string label, bool selected, Action navigate)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? Theme.AccentActive : Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentActive);
        if (ImGui.Button(label + (selected ? " •" : "")))
            navigate();
        ImGui.PopStyleColor(3);
    }

    private void DrawFeatureRow()
    {
        foreach (var feature in Pages.Where(p => p.Group == Current.Group))
        {
            var title = feature.Id == AdvisorPage ? "Beast picks by battle" :
                feature.Id == "guide.recent" ? "Recent choices" : feature.Title;
            var width = ImGui.CalcTextSize(title).X + 32;
            if (ImGui.GetContentRegionAvail().X < width + Theme.Unit)
                ImGui.NewLine();
            NavButton(title, CurrentPage == feature.Id, () => CurrentPage = feature.Id);
            if (CurrentPage == feature.Id)
            {
                ImGui.SameLine();
                Widgets.StatusDot(UiState.Ready);
            }
            ImGui.SameLine();
        }
        ImGui.NewLine();
        ImGui.Separator();
    }

    private void DrawSearch()
    {
        var width = MathF.Min(360, MathF.Max(180, ImGui.GetContentRegionAvail().X));
        ImGui.SetNextItemWidth(width);
        ImGui.InputTextWithHint("##crucibleSearch", "Search settings and fights...", ref SearchText, 256);
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
        if (ImGui.GetContentRegionAvail().X < 620)
        {
            Widgets.IconTile(null, FontAwesomeIcon.Paw.ToIconString(), new Vector2(Theme.NavTileSize), false);
            Widgets.Title(title);
            Widgets.Secondary(description);
            Widgets.Caption(count);
        }
        else
        {
            Layout.PageHeader(null, FontAwesomeIcon.Paw.ToIconString(), title, description, count);
        }
        Widgets.Caption(CruciblePages.LiveSubtext(this, CurrentPage));
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

    private void DrawFooter()
    {
        ImGui.Dummy(new Vector2(0, Theme.Unit));
        ImGui.Separator();
        var (state, detail) = OverallState();
        Widgets.StatusDot(state);
        ImGui.SameLine();
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
        Widgets.Secondary(primary);
        if (ImGui.GetContentRegionAvail().X < 230)
            ImGui.NewLine();
        else
            ImGui.SameLine(ImGui.GetWindowWidth() - 220);
        if (Current.Group == "Guide")
        {
            if (ImGui.Button("Return to run setup"))
                CurrentPage = RosterHornsPage;
        }
        else if (ImGui.Button("Open the fight guide"))
            _plugin?.ToggleGuide();
        Widgets.Caption(secondary);
    }
}
