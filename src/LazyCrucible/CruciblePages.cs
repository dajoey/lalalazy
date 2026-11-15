using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using ECommons.DalamudServices;
using Lalalazy.Crucible;
using LalaUiKit;

namespace LazyCrucible;

internal static class CruciblePages
{
    public static string LiveCount(CrucibleShell shell, string page)
    {
        if (page.StartsWith("guide."))
            return CrucibleGuidePages.LiveCount(shell, page);
        if (page is CrucibleShell.RosterHornsPage or CrucibleShell.OverviewPage)
        {
            var focus = FocusedFight();
            var hornText = focus is null ? "no ranked horn plan" : $"{Selection(focus.Board, focus.Battle).Picks.Count}/3 horns";
            return $"{hornText} · {Plugin.Guide.File.Fights.Count} guide entries";
        }
        if (page == "run.board_overview")
            return ActiveBoard() is var board && board != 0 ? $"board {board} · {Plugin.Guide.File.Fights.Count} guide fights" : "no active board";
        return $"{CrucibleShell.Pages.Count(p => p.Group == shell.Current.Group)} destinations";
    }

    public static string LiveSubtext(CrucibleShell shell, string page)
    {
        if (page.StartsWith("guide."))
            return CrucibleGuidePages.LiveSubtext(shell, page);
        return FocusedFight() is { } fight ? $"{fight.Title} · {CrucibleBoards.Where(fight.Board, fight.Battle)}" : "no fight in focus";
    }

    public static void Render(CrucibleShell shell, string page)
    {
        if (page.StartsWith("guide."))
        {
            CrucibleGuidePages.Render(shell, page);
            return;
        }
        switch (page)
        {
            case CrucibleShell.OverviewPage: DrawOverview(shell); break;
            case "run.board_overview": DrawBoardOverview(shell); break;
            case CrucibleShell.RosterHornsPage: DrawRosterHorns(shell); break;
            case "run.feed_shop": DrawFeedShop(shell); break;
            case "run.campsite": DrawCampsite(shell); break;
            case "run.display_logging": DrawDisplayLogging(shell); break;
            case "rewards.spoils": DrawRewards(shell, false); break;
            case "rewards.treasure": DrawRewards(shell, true); break;
            default: throw new InvalidOperationException("Unknown LazyCrucible page " + page);
        }
    }

    public static GuideFight? FocusedFight()
        => RunTracker.Focus.Battle >= 0 ? Plugin.Guide.Fight(RunTracker.Focus.Board, RunTracker.Focus.Battle) : null;

    public static int ActiveBoard()
    {
        var territory = BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType);
        return territory != 0 ? territory : RunTracker.Board is >= 1 and <= 5 ? RunTracker.Board : 0;
    }

    public static CrucibleSelection Selection(int board, int battle)
        => GuideNeeds.SelectionFor(board, battle, RunTracker.Board, RunTracker.Roster, RunTracker.RosterHp, CrucibleGame.BeastCaptured);

    public static string BeastName(int row) => FamiliarState.BeastName(row);

    public static void Section(string title, string? description = null, string? count = null)
    {
        Widgets.Title(count is null ? title : $"{title} · {count}");
        if (!string.IsNullOrEmpty(description))
            Widgets.Secondary(description);
    }

    public static void TwoColumns(Action work, Action rail)
    {
        if (ImGui.GetContentRegionAvail().X >= 760)
        {
            using var columns = Layout.TwoColumns.Begin(0.72f);
            work();
            columns.Split();
            rail();
        }
        else
        {
            work();
            ImGui.Separator();
            rail();
        }
    }

    private static void DrawOverview(CrucibleShell shell)
    {
        var focus = FocusedFight();
        DrawStatCards(
            new Stat("Run roster", RunTracker.Roster.Count > 0 ? RunTracker.Roster.Count.ToString() : "—", null,
                RunTracker.Roster.Count > 0 ? "Live run roster" : CrucibleGame.RosterLoaded ? "No run roster captured" : Copy.Missing("Bestiary"),
                RunTracker.Roster.Count > 0 ? 1f : null),
            new Stat("Ranked horn plan", focus is null ? "—" : Selection(focus.Board, focus.Battle).Picks.Count.ToString(), focus is null ? null : "3",
                focus is null ? "No fight in focus" : "Ranked from the live fight", focus is null ? null : Selection(focus.Board, focus.Battle).Picks.Count / 3f),
            new Stat("Guide coverage", Plugin.Guide.File.Fights.Count.ToString(), "fights", "Real guide entries loaded", null));

        TwoColumns(
            () =>
            {
                Section("Session activity", SelectionScreens.Recent.Count + " recorded choices");
                Widgets.Body(PetSelect.LastSummary.Length == 0 ? "No familiar selection yet this session." : PetSelect.LastSummary);
                Widgets.SecondaryWrapped(SelectionScreens.Status.Length == 0
                    ? "Open a Crucible selection screen to apply prepared picks."
                    : SelectionScreens.Status);
                ImGui.Separator();
                Section("Focused fight");
                if (focus is null)
                    Widgets.InfoBox("Idle — no fight is in focus. Board and guide data remain available without inventing a horn plan.", UiState.Idle);
                else
                {
                    Widgets.Body(focus.Title);
                    Widgets.SecondaryWrapped(focus.Summary);
                }
                ImGui.Separator();
                Section("What stays manual");
                Widgets.SecondaryWrapped("Manual edits still win on the current selection screen. LazyCrucible never walks, starts a battle, confirms Rest, sells, or leaves.");
            },
            () =>
            {
                Section("Quick destinations");
                Destination(shell, CrucibleShell.RosterHornsPage);
                Destination(shell, "run.feed_shop");
                Destination(shell, CrucibleShell.UpcomingPage);
                Destination(shell, CrucibleShell.AdvisorPage);
                Destination(shell, "rewards.spoils");
                ImGui.Separator();
                Section("Live focus");
                Widgets.SecondaryWrapped(focus is null ? "No fight in focus" : focus.Title + " · " + focus.Summary);
            });
    }

    private static void Destination(CrucibleShell shell, string pageId)
    {
        var page = CrucibleShell.Pages.First(p => p.Id == pageId);
        if (ImGui.Button($"{page.Group} · {page.Title}##destination_{pageId}"))
            shell.CurrentPage = pageId;
    }

    private static void DrawBoardOverview(CrucibleShell shell)
    {
        var active = ActiveBoard();
        DrawStatCards(
            new Stat("Boards", BST_CrucibleData.Boards.Length.ToString(), "total", "Real board data", null),
            new Stat("Active board", active == 0 ? "—" : active.ToString(), "/ 5", active == 0 ? "No active board" : "Read from live state", null),
            new Stat("Guide entries", Plugin.Guide.File.Fights.Count.ToString(), "fights", "Real guide coverage", null));
        TwoColumns(
            () =>
            {
                Section("Board progression");
                foreach (var board in BST_CrucibleData.Boards)
                {
                    if (ImGui.Selectable($"{board.Board} · {board.Name} · L{board.Level} · rank {board.BeastRank}##board_{board.Board}", active == board.Board))
                    {
                        shell.GuideBoard = board.Board;
                        shell.GuideBattle = 1;
                        shell.GuideFollow = false;
                        shell.CurrentPage = "guide.browse";
                    }
                    Widgets.Caption($"{Plugin.Guide.File.Fights.Count(f => f.Board == board.Board)} guide fights");
                }
            },
            () =>
            {
                Section("Focused fight");
                if (FocusedFight() is not { } fight)
                    Widgets.InfoBox("No active board or focused fight. Choose a board to browse its real guide entries.", UiState.Idle);
                else
                {
                    Widgets.Body(fight.Title);
                    Widgets.SecondaryWrapped(fight.Summary);
                    Widgets.Caption(CrucibleBoards.Where(fight.Board, fight.Battle));
                    if (ImGui.Button("Open upcoming fight"))
                        shell.CurrentPage = CrucibleShell.UpcomingPage;
                }
            });
    }

    private static void DrawRosterHorns(CrucibleShell shell)
    {
        var cfg = Plugin.Config;
        var focus = FocusedFight();
        var selection = focus is null ? null : Selection(focus.Board, focus.Battle);
        var needs = selection is null ? [] : GuideNeeds.Rows(selection);
        var required = selection is null ? (Covered: 0, Total: 0) : UiPresentationLogic.RequiredCallStats(needs);
        var rosterCount = RunTracker.Roster.Count;
        TwoColumns(
            () =>
            {
                DrawStatCards(
                    new Stat("Run roster", rosterCount > 0 ? rosterCount.ToString() : "—", null,
                        rosterCount > 0 ? "Filled on the next entry menu" : CrucibleGame.RosterLoaded ? "No run roster captured" : Copy.Missing("Bestiary"), rosterCount > 0 ? 1f : null),
                    new Stat("Battlehorns", selection is null ? "—" : selection.Picks.Count.ToString(), selection is null ? null : "3",
                        selection is null ? "No fight in focus" : "Ready for the next formation", selection is null ? null : selection.Picks.Count / 3f),
                    new Stat("Required calls", selection is null ? "—" : required.Covered.ToString(), selection is null ? null : required.Total.ToString(),
                        selection is null ? "No fight in focus" : required.Total == 0 ? "No required calls" : "Covered by the live ranking", selection is null || required.Total == 0 ? null : required.Covered / (float)required.Total));

                CrucibleCard.Draw("battlehorn_plan", 0, () =>
                {
                    Section("Battlehorn plan", focus is null ? "No live horn plan" : $"{focus.Title} · {BST_CrucibleData.Boards[focus.Board - 1].Name}",
                        focus is null ? null : focus.Counters.Count + focus.Mechanics.Count + " guide calls");
                    if (focus is null || selection is null)
                    {
                        Widgets.InfoBox("Idle — no fight is in focus. The four roster and yield settings remain available, but no horn loadout is invented.", UiState.Idle);
                        return;
                    }
                    PlanHeader();
                    foreach (var counter in focus.Counters)
                    {
                        var match = needs.FirstOrDefault(row => row.What == counter.What);
                        var covered = match.What is not null && match.Covered;
                        var response = match.What is null ? counter.What : covered
                            ? $"{match.Tool} — {BeastName(match.CoveredBy)}"
                            : match.WhyNot ?? "No horn pick answers this call";
                        PlanRow(counter.What, response, covered ? UiState.Ready : UiState.Blocked, covered ? "Ready" : "Needed");
                    }
                    foreach (var mechanic in focus.Mechanics.Take(2))
                        PlanRow(mechanic.Name, mechanic.Do, UiState.Blocked, "Guide call");
                    ImGui.Dummy(new Vector2(0, Theme.Unit));
                    CrucibleCard.Draw("recommended_horns", 0, () =>
                    {
                        Widgets.StateText("●", UiState.Ready);
                        ImGui.SameLine();
                        Widgets.Body("Recommended horns: " + string.Join(" · ", selection.Picks.Select(p => BeastName(p.Row))));
                        Widgets.SecondaryWrapped(string.Join(" · ", selection.Picks.Select(p => p.Why))
                            + ". Manual changes win; knocked-out familiars are never picked; LazyCrucible never starts the fight.");
                    }, CrucibleCardTone.Recommended);
                });
            },
            () =>
            {
                CrucibleCard.Draw("requirements", 0, () =>
                {
                    Section("Requirements");
                    Requirement("Beastmaster", "Required", true);
                    Requirement("Master's Bestiary", CrucibleGame.RosterLoaded ? "Loaded" : Copy.Missing("Bestiary"), CrucibleGame.RosterLoaded);
                    Requirement("Active board", ActiveBoard() != 0 ? "Ready" : "Optional", ActiveBoard() != 0);
                    Requirement("AutoDuty", PetSelect.YieldReason == "autoduty_running" ? "Standby" : "Optional", PetSelect.YieldReason != "autoduty_running");
                });
                CrucibleCard.Draw("roster_settings", 0, () =>
                {
                    Section("Settings");
                    shell.Setting("Fill the run roster at the Bentbranch Meadows entry menu", () => cfg.AutoRoster, value => cfg.AutoRoster = value);
                    Help("Once per entry-menu visit; a roster changed by hand is left alone until the next run.");
                    shell.Setting("Score: three familiars", () => cfg.ScoreThreeFamiliars, value => cfg.ScoreThreeFamiliars = value);
                    Help("Opt in to a three-familiar score roster; never used on an AutoDuty-driven screen.");
                    shell.Setting("Fill the Battlehorn slots before each fight", () => cfg.AutoHorns, value => cfg.AutoHorns = value);
                    Help("Ranks three answers to the fight's mechanics; never starts the fight.");
                    shell.Setting("Stand down while AutoDuty is running", () => cfg.YieldToAutoDuty, value => cfg.YieldToAutoDuty = value);
                    Help("Reads AutoDuty settings only; the two drivers never overwrite each other.");
                });
            });
    }

    private static void PlanHeader()
    {
        var start = ImGui.GetCursorPos();
        var width = ImGui.GetContentRegionAvail().X;
        Widgets.Caption("CALL");
        if (width >= 560)
        {
            ImGui.SetCursorPos(new Vector2(start.X + 260, start.Y));
            Widgets.Caption("RESPONSE");
            ImGui.SetCursorPos(new Vector2(start.X + width - 104, start.Y));
            Widgets.Caption("STATE");
        }
        else
        {
            Widgets.Caption("RESPONSE · STATE");
        }
    }

    private static void PlanRow(string call, string response, UiState state, string stateLabel)
    {
        CrucibleCard.Row("plan_" + call, () =>
        {
            var start = ImGui.GetCursorPos();
            var width = ImGui.GetContentRegionAvail().X;
            Widgets.Body(call);
            if (width >= 560)
            {
                ImGui.SetCursorPos(new Vector2(start.X + 260, start.Y));
                ImGui.PushTextWrapPos(start.X + width - 116);
                Widgets.SecondaryWrapped(response);
                ImGui.PopTextWrapPos();
                var responseBottom = ImGui.GetCursorPosY();
                ImGui.SetCursorPos(new Vector2(start.X + width - 104, start.Y));
                Widgets.StateText("● " + stateLabel, state);
                ImGui.SetCursorPosY(MathF.Max(responseBottom, ImGui.GetCursorPosY()));
            }
            else
            {
                Widgets.SecondaryWrapped(response);
                Widgets.StateText(stateLabel, state);
            }
        });
    }

    private static void Requirement(string label, string value, bool ready)
    {
        var start = ImGui.GetCursorPos();
        var width = ImGui.GetContentRegionAvail().X;
        Widgets.StateText(ready ? "✓" : "—", ready ? UiState.Ready : UiState.Idle);
        ImGui.SameLine();
        Widgets.Secondary(label);
        var valueWidth = ImGui.CalcTextSize(value).X;
        var labelWidth = ImGui.CalcTextSize(label).X;
        if (labelWidth + valueWidth + 36 <= width)
        {
            ImGui.SetCursorPos(new Vector2(start.X + width - valueWidth, start.Y));
            Widgets.Caption(value);
        }
        else
        {
            ImGui.SetCursorPosX(start.X + 24);
            Widgets.Caption(value);
        }
    }

    private static void DrawFeedShop(CrucibleShell shell)
    {
        var cfg = Plugin.Config;
        TwoColumns(
            () =>
            {
                Section("Beast Feed", "Choose who eats and confirm only the matching prompt");
                shell.Setting("Beast Feed: pick who eats the feed and confirm", () => cfg.AutoFeed, value => cfg.AutoFeed = value);
                Help("When a feed is bought, feeds it to the familiar it helps most in the fights ahead. Never a familiar whose kin cannot eat it, one that is full or knocked out, or one that already ate it. A feed that would hurt every familiar is left to you.");
                ImGui.Separator();
                Section("Score goal for feeding");
                var goal = (int)cfg.ScoreGoal;
                ImGui.SetNextItemWidth(MathF.Min(360, ImGui.GetContentRegionAvail().X));
                if (ImGui.Combo("Score goal for feeding", ref goal, "Survival first (default)\0Starve the Fever (never feed)\0Feed the Bold (feed anything harmless)\0"))
                {
                    cfg.ScoreGoal = (ScoreGoal)goal;
                    shell.ScoreGoalChanged();
                }
                Widgets.SecondaryWrapped("All three existing values remain available. Manual purchases and feeding edits hand the rest of the visit to the player. No selling or leaving is added.");
            },
            () =>
            {
                Section("Shop", "Buy what the fights ahead call for; never sell or leave");
                shell.Setting("Shop: buy what the fights ahead call for", () => cfg.AutoShop, value => cfg.AutoShop = value);
                Help("Keeps enough heals for the fights ahead, then buys what keeps you and your familiars alive: resistance, max HP and damage-taken gear, revives, and feed for the horn picks. Each purchase is confirmed only when the prompt names that item.");
            });
    }

    private static void DrawCampsite(CrucibleShell shell)
    {
        var cfg = Plugin.Config;
        TwoColumns(
            () =>
            {
                Section("Campsite selection", "Select who rests; Rest remains the player's button");
                shell.Setting("Campsite: select who rests (you press Rest)", () => cfg.AutoCamp, value => cfg.AutoCamp = value);
                Help("Weighs your heal against the familiars' (resting familiars lose their feed unless they ate Lily Simular) and selects the familiars worth resting. Your self heal wins when you are low. Rest is always your button.");
            },
            () =>
            {
                Section("State");
                Widgets.InfoBox("Selection only. Rest confirmation is never automated.", UiState.Ready);
            });
        ImGui.Separator();
        Section("Resting trade-offs");
        Widgets.SecondaryWrapped("Knocked-out familiars are never picked. Feed effects, current HP and upcoming horn demand continue to use the real policy inputs.");
    }

    private static void DrawDisplayLogging(CrucibleShell shell)
    {
        var cfg = Plugin.Config;
        TwoColumns(
            () =>
            {
                Section("Guide & announcements");
                shell.Setting("Open the fight guide on the upcoming fight", () => cfg.GuideAutoOpen, value => cfg.GuideAutoOpen = value);
                Help("When the board layout or Battlehorn screen points at a fight, the guide opens on it; warned casts can open it too.");
                shell.Setting("Announce picks in chat", () => cfg.AnnouncePicks, value => cfg.AnnouncePicks = value);
                Help("One chat line per fight naming each familiar and why it was picked.");
                shell.Setting("Record Crucible screens to the log", () => cfg.RecordScreens, value => cfg.RecordScreens = value);
                Help("Read-only. Writes Crucible screens and their buttons to the Dalamud log. Never presses anything.");
                ImGui.Separator();
                Plugin.Telemetry?.DrawReportButton();
                Widgets.Caption("Shared report and changelog surfaces keep their current kit; structural consistency is a shared-kit follow-up.");
            },
            () =>
            {
                Section("Current behavior");
                Widgets.SecondaryWrapped($"Guide auto-open: {(cfg.GuideAutoOpen ? "on" : "off")}\nChat announcements: {(cfg.AnnouncePicks ? "on" : "off")}\nScreen recording: {(cfg.RecordScreens ? "on" : "off")}");
                ImGui.Separator();
                Section("Invariants");
                Widgets.SecondaryWrapped("Slash commands, IPC and Lazy Hub quick controls are unchanged.");
            });
    }

    private static void DrawRewards(CrucibleShell shell, bool treasure)
    {
        var cfg = Plugin.Config;
        if (treasure)
        {
            Section("Treasure suggestion");
            shell.Setting("Treasure: suggest the best choice", () => cfg.AutoTreasure, value => cfg.AutoTreasure = value);
            Help("Marks the choice that helps most and says why. It does not click yet: which button belongs to which choice has not been recorded.");
            Widgets.InfoBox("Suggestion only — no coffer choice is clicked.", UiState.Idle);
        }
        else
        {
            Section("Spoils handling");
            shell.Setting("Spoils: Take all when everything fits", () => cfg.AutoSpoils, value => cfg.AutoSpoils = value);
            Help("If the loot would not fit (or repeats gear you own), shows the best items first and leaves the choice to you.");
            Widgets.InfoBox("Overflow or duplicate gear remains the player's decision. No selling is added.", UiState.Ready);
        }
    }

    private static void Help(string text) => Widgets.SecondaryWrapped(text);

    private readonly record struct Stat(string Label, string Value, string? Cap, string Note, float? Fraction);

    private static void DrawStatCards(params Stat[] stats)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var stacked = available < 650;
        var gap = 3 * Theme.Unit;
        var width = stacked ? available : (available - (2 * gap)) / 3;
        const float height = 112;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (stacked ? new Vector2(0, i * (height + gap)) : new Vector2(i * (width + gap), 0));
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), 8f);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 8f);

            ImGui.SetCursorScreenPos(pos + new Vector2(12, 10));
            Widgets.IconTile(null, FontAwesomeIcon.Paw.ToIconString(), new Vector2(18), false);
            ImGui.SetCursorScreenPos(pos + new Vector2(38, 11));
            Widgets.Caption(stat.Label);
            ImGui.SetCursorScreenPos(pos + new Vector2(12, 34));
            Widgets.Title(stat.Cap is null ? stat.Value : $"{stat.Value} / {stat.Cap.TrimStart('/')}");
            if (stat.Fraction is { } fraction)
            {
                var min = pos + new Vector2(12, 70);
                var max = pos + new Vector2(width - 12, 74);
                draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), 2f);
                if (fraction > 0)
                    draw.AddRectFilled(min, min + new Vector2((max.X - min.X) * Math.Clamp(fraction, 0, 1), 0), Theme.Pack(Theme.StateGreen), 2f);
            }
            ImGui.SetCursorScreenPos(pos + new Vector2(12, 84));
            Widgets.Caption(stat.Note);
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(0, stacked ? stats.Length * (height + gap) : height + gap));
    }
}

internal enum CrucibleCardTone
{
    Surface,
    Row,
    Recommended,
    Attention,
}

/// <summary>Content-measured rounded cards. Backgrounds never cover text and heights follow the live layout.</summary>
internal static class CrucibleCard
{
    private const float Padding = 12;
    private const float Gap = 12;
    private static int _depth;

    public static void Draw(string id, float minHeight, Action content, CrucibleCardTone tone = CrucibleCardTone.Surface, float width = 0)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var cardWidth = width <= 0 ? ImGui.GetContentRegionAvail().X : MathF.Min(width, ImGui.GetContentRegionAvail().X);
        var topLevel = _depth == 0;
        if (topLevel)
        {
            _depth++;
            // Outer surface, nested row surfaces, then glyphs. Never split channels recursively.
            draw.ChannelsSplit(3);
        }
        try
        {
            draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(Padding, Padding));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * Padding)));
            ImGui.BeginGroup();
            content();
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();
            var contentBottom = ImGui.GetCursorScreenPos().Y;
            var height = MathF.Max(minHeight, contentBottom - origin.Y + Padding);

            draw.ChannelsSetCurrent(topLevel ? 0 : 1);
            var (fill, border) = Colors(tone);
            draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), fill, 8f);
            draw.AddRect(origin, origin + new Vector2(cardWidth, height), border, 8f);
            if (tone is CrucibleCardTone.Recommended or CrucibleCardTone.Attention)
                draw.AddRectFilled(origin + new Vector2(0, 8), origin + new Vector2(3, height - 8), Theme.Pack(tone == CrucibleCardTone.Recommended ? Theme.StateGreen : Theme.StateAmber), 2f);
            if (topLevel)
                draw.ChannelsMerge();
            else
                draw.ChannelsSetCurrent(2);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, height + Gap));
        }
        finally
        {
            if (topLevel)
                _depth--;
        }
    }

    public static void Row(string id, Action content, float minHeight = 48)
        => Draw(id, minHeight, content, CrucibleCardTone.Row);

    public static void TwoCards(float leftFraction, Action left, Action right)
    {
        if (ImGui.GetContentRegionAvail().X < 520)
        {
            left();
            right();
            return;
        }
        var startX = ImGui.GetCursorPosX();
        var startY = ImGui.GetCursorScreenPos().Y;
        var available = ImGui.GetContentRegionAvail().X;
        var gap = 2 * Theme.Unit;
        var leftWidth = (available - gap) * leftFraction;
        CrucibleCard.Draw("two_left", 0, left, CrucibleCardTone.Surface, leftWidth);
        var leftBottom = ImGui.GetCursorScreenPos().Y;
        ImGui.SetCursorScreenPos(new Vector2(startX + leftWidth + gap, startY));
        CrucibleCard.Draw("two_right", 0, right, CrucibleCardTone.Surface, available - leftWidth - gap);
        var rightBottom = ImGui.GetCursorScreenPos().Y;
        ImGui.SetCursorScreenPos(new Vector2(startX, MathF.Max(leftBottom, rightBottom)));
    }

    private static (uint Fill, uint Border) Colors(CrucibleCardTone tone) => tone switch
    {
        CrucibleCardTone.Row => (Theme.Pack(Theme.ShellBar), Theme.Pack(Theme.SurfaceBorder)),
        CrucibleCardTone.Recommended => (Theme.Pack(Theme.StateGreen with { W = 0.13f }), Theme.Pack(Theme.SurfaceBorder)),
        CrucibleCardTone.Attention => (Theme.Pack(Theme.StateAmber with { W = 0.13f }), Theme.Pack(Theme.SurfaceBorder)),
        _ => (Theme.Pack(Theme.Surface), Theme.Pack(Theme.SurfaceBorder)),
    };
}
