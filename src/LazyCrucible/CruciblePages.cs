using System.Numerics;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using Lalalazy.Crucible;
using LalaUiKit;

namespace LazyCrucible;

internal static class CruciblePages
{
    private const float RunRosterSlots = 12;

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

    /// <summary>Card heading: title, optional trailing count, optional dim caption.</summary>
    public static void Section(string title, string? description = null, string? count = null)
        => CrucibleChrome.CardHeader(title, description, count);

    /// <summary>A wrapped paragraph in the secondary colour, measured from the live font.</summary>
    public static void Paragraph(string text, float scale = Theme.SecondaryScale, Vector4? color = null, float gap = 2 * Theme.Unit)
    {
        if (text.Length == 0)
            return;
        var pos = ImGui.GetCursorScreenPos();
        var width = CrucibleChrome.Avail();
        var height = CrucibleChrome.DrawWrapped(ImGui.GetWindowDrawList(), pos, text, width, color ?? Theme.TextSecondary, scale);
        CrucibleChrome.Advance(pos, width, height, gap);
    }

    /// <summary>
    ///     Two columns (main and rail) whose last cards share one bottom edge; below the breakpoint they stack.
    ///     Heights come from the previous frame, so the first frame after a layout change settles in one more frame.
    /// </summary>
    public static void Columns(string id, Action main, Action rail, float breakpoint = 820, float? railFraction = null, bool fillPage = false)
    {
        var width = CrucibleChrome.Avail();
        var saved = CrucibleCard.FillBottom;
        if (width < breakpoint)
        {
            CrucibleCard.FillBottom = float.NaN;
            main();
            rail();
            CrucibleCard.FillBottom = saved;
            return;
        }
        var gap = CrucibleChrome.Gap;
        var origin = ImGui.GetCursorScreenPos();
        var railWidth = railFraction is { } fraction ? (width - gap) * fraction : Math.Clamp(width * 0.26f, 300, 360);
        var mainWidth = width - gap - railWidth;
        ColumnHeights.TryGetValue(id, out var previous);
        var target = MathF.Max(previous.Main, previous.Rail);
        if (fillPage)
            target = MathF.Max(target, ImGui.GetWindowHeight() - CrucibleShell.PageBottomReserve - ImGui.GetCursorPos().Y);

        // Each column is a group so that, after every item, the cursor returns to the column's own left edge.
        CrucibleChrome.PushRight(origin.X + mainWidth);
        CrucibleCard.BeginColumn(origin.Y + target);
        ImGui.SetCursorScreenPos(origin);
        ImGui.BeginGroup();
        main();
        ImGui.EndGroup();
        var mainBottom = ImGui.GetItemRectMax().Y + Theme.ItemSpacing.Y;
        var mainNatural = mainBottom - CrucibleChrome.Gap - origin.Y - CrucibleCard.LastStretch;
        CrucibleChrome.PopRight();

        CrucibleChrome.PushRight(origin.X + width);
        CrucibleCard.BeginColumn(origin.Y + target);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + mainWidth + gap, origin.Y));
        ImGui.BeginGroup();
        rail();
        ImGui.EndGroup();
        var railBottom = ImGui.GetItemRectMax().Y + Theme.ItemSpacing.Y;
        var railNatural = railBottom - CrucibleChrome.Gap - origin.Y - CrucibleCard.LastStretch;
        CrucibleChrome.PopRight();

        ColumnHeights[id] = (mainNatural, railNatural);
        CrucibleCard.FillBottom = saved;
        ImGui.SetCursorScreenPos(new Vector2(origin.X, MathF.Max(mainBottom, railBottom) - Theme.ItemSpacing.Y));
        ImGui.Dummy(new Vector2(width, 0));
    }

    private static readonly Dictionary<string, (float Main, float Rail)> ColumnHeights = new();

    /// <summary>Centered empty state for a card with free space: icon, title, short explanation.</summary>
    public static void EmptyState(string title, string body)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = CrucibleChrome.Avail();
        var textWidth = MathF.Min(width, MathF.Min(440, CrucibleChrome.TextWidth(body, Theme.SecondaryScale) + 2));
        var titleHeight = CrucibleChrome.LineHeight(Theme.TitleScale);
        var bodyHeight = CrucibleChrome.WrappedHeight(body, textWidth, Theme.SecondaryScale);
        const float icon = 28;
        var block = icon + CrucibleChrome.Gap + titleHeight + Theme.Unit + bodyHeight;
        var top = MathF.Max(Theme.Unit * 2, (CrucibleCard.RemainingFill() - block) / 2f);
        var centerX = pos.X + (width / 2f);
        CrucibleChrome.Diamond(draw, new Vector2(centerX, pos.Y + top + (icon / 2f)), icon / 2f, Theme.Pack(Theme.Accent with { W = 0.55f }));
        var y = pos.Y + top + icon + CrucibleChrome.Gap;
        CrucibleChrome.DrawText(draw, new Vector2(centerX - (CrucibleChrome.TextWidth(title, Theme.TitleScale) / 2f), y), title, Theme.TextPrimary, Theme.TitleScale);
        CrucibleChrome.DrawWrapped(draw, new Vector2(centerX - (textWidth / 2f), y + titleHeight + Theme.Unit), body, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
        CrucibleChrome.Advance(pos, width, top + block, 0);
    }

    private static void DrawOverview(CrucibleShell shell)
    {
        var focus = FocusedFight();
        var roster = RunTracker.Roster.Count;
        DrawStatCards(
            new Stat("Run roster", roster > 0 ? roster.ToString() : "—", roster > 0 ? RunRosterSlots.ToString("0") : null,
                roster > 0 ? "Live run roster" : CrucibleGame.RosterLoaded ? "No run roster captured" : Copy.Missing("Bestiary"),
                roster > 0 ? Math.Min(1f, roster / RunRosterSlots) : null),
            new Stat("Ranked horn plan", focus is null ? "—" : Selection(focus.Board, focus.Battle).Picks.Count.ToString(), focus is null ? null : "3",
                focus is null ? "No fight in focus" : "Ranked from the live fight", focus is null ? null : Selection(focus.Board, focus.Battle).Picks.Count / 3f),
            new Stat("Guide coverage", Plugin.Guide.File.Fights.Count.ToString(), "fights", "Guide entries loaded", null));

        Columns("home",
            () =>
            {
                CrucibleCard.Draw("session_activity", 0, () =>
                {
                    Section("Session activity", null, SelectionScreens.Recent.Count + " recorded choices");
                    Paragraph(PetSelect.LastSummary.Length == 0 ? "No familiar selection yet this session." : PetSelect.LastSummary, 1f, Theme.TextPrimary);
                    Paragraph(SelectionScreens.Status.Length == 0
                        ? "Open a Crucible selection screen to apply prepared picks."
                        : SelectionScreens.Status);
                });
                CrucibleCard.Draw("home_focus", 0, () =>
                {
                    Section("Focused fight");
                    if (focus is null)
                        CrucibleChrome.Notice("Idle — no fight is in focus. Board and guide data remain available without inventing a horn plan.", UiState.Idle);
                    else
                    {
                        Paragraph(focus.Title, 1f, Theme.TextPrimary, Theme.Unit);
                        Paragraph(focus.Summary);
                    }
                });
                CrucibleCard.Draw("home_manual", 0, () =>
                {
                    Section("What stays manual");
                    Paragraph("Manual edits still win on the current selection screen. LazyCrucible never walks, starts a battle, confirms Rest, sells, or leaves.");
                }, fill: true);
            },
            () =>
            {
                CrucibleCard.Draw("quick_destinations", 0, () =>
                {
                    Section("Quick destinations");
                    var width = CrucibleChrome.Avail();
                    Destination(shell, CrucibleShell.RosterHornsPage, width);
                    Destination(shell, "run.feed_shop", width);
                    Destination(shell, CrucibleShell.UpcomingPage, width);
                    Destination(shell, CrucibleShell.AdvisorPage, width);
                    Destination(shell, "rewards.spoils", width);
                });
                CrucibleCard.Draw("live_focus", 0, () =>
                {
                    Section("Live focus");
                    Paragraph(focus is null ? "No fight in focus" : focus.Title + " · " + focus.Summary);
                }, fill: true);
            }, railFraction: 0.34f);
    }

    private static void Destination(CrucibleShell shell, string pageId, float width)
    {
        var page = CrucibleShell.Pages.First(p => p.Id == pageId);
        if (CrucibleChrome.NavRow("destination_" + pageId, $"{page.Group} · {page.Title}", width))
            shell.CurrentPage = pageId;
    }

    private static void DrawBoardOverview(CrucibleShell shell)
    {
        var active = ActiveBoard();
        DrawStatCards(
            new Stat("Boards", BST_CrucibleData.Boards.Length.ToString(), "total", "Board data loaded", null),
            new Stat("Active board", active == 0 ? "—" : active.ToString(), "5", active == 0 ? "No active board" : "Read from live state", active == 0 ? null : active / 5f),
            new Stat("Guide entries", Plugin.Guide.File.Fights.Count.ToString(), "fights", "Guide coverage", null));
        Columns("boards",
            () =>
            {
                CrucibleCard.Draw("board_progression", 0, () =>
                {
                    Section("Board progression");
                    foreach (var board in BST_CrucibleData.Boards)
                    {
                        if (CrucibleChrome.ListRow("board_" + board.Board, $"{board.Board} · {board.Name} · L{board.Level} · rank {board.BeastRank}",
                                $"{Plugin.Guide.File.Fights.Count(f => f.Board == board.Board)} guide fights", null, active == board.Board))
                        {
                            shell.GuideBoard = board.Board;
                            shell.GuideBattle = 1;
                            shell.GuideFollow = false;
                            shell.CurrentPage = "guide.browse";
                        }
                    }
                }, fill: true);
            },
            () =>
            {
                CrucibleCard.Draw("board_focus", 0, () =>
                {
                    Section("Focused fight");
                    if (FocusedFight() is not { } fight)
                        CrucibleChrome.Notice("No active board or focused fight. Choose a board to browse its real guide entries.", UiState.Idle);
                    else
                    {
                        Paragraph(fight.Title, 1f, Theme.TextPrimary, Theme.Unit);
                        Paragraph(fight.Summary);
                        Paragraph(CrucibleBoards.Where(fight.Board, fight.Battle));
                        if (CrucibleChrome.ActionButton("open_upcoming", "Open upcoming fight"))
                            shell.CurrentPage = CrucibleShell.UpcomingPage;
                    }
                }, fill: true);
            }, railFraction: 0.36f);
    }

    private static void DrawRosterHorns(CrucibleShell shell)
    {
        var cfg = Plugin.Config;
        var focus = FocusedFight();
        var selection = focus is null ? null : Selection(focus.Board, focus.Battle);
        var needs = selection is null ? [] : GuideNeeds.Rows(selection);
        var required = selection is null ? (Covered: 0, Total: 0) : UiPresentationLogic.RequiredCallStats(needs);
        var rosterCount = RunTracker.Roster.Count;
        Columns("roster",
            () =>
            {
                DrawStatCards(
                    new Stat("Run roster", rosterCount > 0 ? rosterCount.ToString() : "—", rosterCount > 0 ? RunRosterSlots.ToString("0") : null,
                        rosterCount > 0 ? "Filled on the next entry menu" : CrucibleGame.RosterLoaded ? "No run roster captured" : Copy.Missing("Bestiary"),
                        rosterCount > 0 ? Math.Min(1f, rosterCount / RunRosterSlots) : null),
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
                        EmptyState("No fight in focus", "The roster and yield settings stay available, but no horn loadout is invented until a fight is in focus.");
                        return;
                    }
                    var rows = new List<PlanEntry>();
                    foreach (var counter in focus.Counters)
                    {
                        var match = needs.FirstOrDefault(row => row.What == counter.What);
                        var covered = match.What is not null && match.Covered;
                        var response = match.What is null ? counter.What : covered
                            ? $"{match.Tool} — {BeastName(match.CoveredBy)}"
                            : match.WhyNot ?? "No horn pick answers this call";
                        rows.Add(new PlanEntry(counter.What, response, covered ? UiState.Ready : UiState.Blocked, covered ? "Ready" : "Needed"));
                    }
                    var guideCall = focus.Mechanics
                        .OrderByDescending(mechanic => mechanic.Tell.Contains("never interruptible", StringComparison.OrdinalIgnoreCase))
                        .ThenByDescending(mechanic => mechanic.Cast ?? 0)
                        .Take(1);
                    foreach (var mechanic in guideCall)
                        rows.Add(new PlanEntry(mechanic.Name, mechanic.Do, UiState.Blocked, "Guide call"));
                    PlanTable(rows);
                    CrucibleCard.Draw("recommended_horns", 0, () =>
                    {
                        Paragraph("Recommended horns", 1f, Theme.TextPrimary, Theme.Unit);
                        HornLines(selection.Picks.Select(pick => (BeastName(pick.Row), pick.Why)), Theme.StateGreen);
                        Paragraph("Manual changes win; knocked-out familiars are never picked; LazyCrucible never starts the fight.");
                    }, CrucibleCardTone.Recommended, padY: 3 * Theme.Unit);
                }, fill: true);
            },
            () =>
            {
                CrucibleCard.Draw("requirements", 0, () =>
                {
                    Section("Requirements");
                    StatusRow("Beastmaster", "Required", UiState.Ready, true);
                    StatusRow("Master's Bestiary", CrucibleGame.RosterLoaded ? "Loaded" : Copy.Missing("Bestiary"),
                        CrucibleGame.RosterLoaded ? UiState.Ready : UiState.Missing, CrucibleGame.RosterLoaded);
                    StatusRow("Active board", ActiveBoard() != 0 ? "Ready" : "Optional", ActiveBoard() != 0 ? UiState.Ready : UiState.Idle, ActiveBoard() != 0);
                    StatusRow("AutoDuty", PetSelect.YieldReason == "autoduty_running" ? "Standby" : "Optional",
                        PetSelect.YieldReason == "autoduty_running" ? UiState.Blocked : UiState.Idle, PetSelect.YieldReason != "autoduty_running");
                });
                CrucibleCard.Draw("roster_settings", 0, () =>
                {
                    Section("Settings");
                    shell.Setting("Fill the run roster at the Bentbranch Meadows entry menu", () => cfg.AutoRoster, value => cfg.AutoRoster = value);
                    shell.Setting("Score: three familiars", () => cfg.ScoreThreeFamiliars, value => cfg.ScoreThreeFamiliars = value, separator: true);
                    shell.Setting("Fill the Battlehorn slots before each fight", () => cfg.AutoHorns, value => cfg.AutoHorns = value, separator: true);
                    shell.Setting("Stand down while AutoDuty is running", () => cfg.YieldToAutoDuty, value => cfg.YieldToAutoDuty = value, separator: true);
                }, fill: true);
            }, breakpoint: 980, fillPage: true);
    }

    private readonly record struct PlanEntry(string Call, string Response, UiState State, string Label);

    /// <summary>Header and rows share one set of measured columns; every cell is centred in its row.</summary>
    private static void PlanTable(List<PlanEntry> rows)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = CrucibleChrome.Avail();
        var wide = width >= 560;
        const float padX = CrucibleChrome.Gap;
        const float padY = 3 * Theme.Unit;
        var inner = width - (2 * padX);
        var gap = CrucibleChrome.Gap;
        var stateWidth = rows.Count == 0 ? 0 : rows.Max(r => (Theme.DotRadius * 2) + (2 * Theme.Unit) + CrucibleChrome.TextWidth(r.Label, Theme.SecondaryScale));
        var callWidth = Math.Clamp(inner * 0.30f, 160, 280);
        var responseWidth = MathF.Max(60, inner - callWidth - stateWidth - (2 * gap));
        var y = 0f;
        if (wide)
        {
            var captionY = origin.Y + ((CrucibleChrome.LineHeight() - CrucibleChrome.LineHeight(Theme.CaptionScale)) / 2f);
            CrucibleChrome.DrawText(draw, new Vector2(origin.X + padX, captionY), "CALL", Theme.TextSecondary, Theme.CaptionScale);
            CrucibleChrome.DrawText(draw, new Vector2(origin.X + padX + callWidth + gap, captionY), "RESPONSE", Theme.TextSecondary, Theme.CaptionScale);
            CrucibleChrome.DrawTextRight(draw, origin.X + width - padX, captionY, "STATE", Theme.TextSecondary, Theme.CaptionScale);
            y = CrucibleChrome.LineHeight() + (2 * Theme.Unit);
        }
        foreach (var row in rows)
        {
            var color = Copy.MarkColor(row.State);
            float height;
            if (wide)
            {
                var callHeight = CrucibleChrome.WrappedHeight(row.Call, callWidth);
                var responseHeight = CrucibleChrome.WrappedHeight(row.Response, responseWidth, Theme.SecondaryScale);
                var cell = MathF.Max(callHeight, MathF.Max(responseHeight, CrucibleChrome.LineHeight()));
                height = cell + (2 * padY);
                var top = origin + new Vector2(0, y);
                draw.AddRectFilled(top, top + new Vector2(width, height), Theme.Pack(Theme.ShellBar), CrucibleChrome.Radius);
                CrucibleChrome.DrawWrapped(draw, top + new Vector2(padX, (height - callHeight) / 2f), row.Call, callWidth, Theme.TextPrimary);
                CrucibleChrome.DrawWrapped(draw, top + new Vector2(padX + callWidth + gap, (height - responseHeight) / 2f), row.Response, responseWidth, Theme.TextSecondary, Theme.SecondaryScale);
                var stateX = top.X + width - padX - stateWidth;
                draw.AddCircleFilled(new Vector2(stateX + Theme.DotRadius, top.Y + (height / 2f)), Theme.DotRadius, Theme.Pack(color));
                CrucibleChrome.DrawText(draw, new Vector2(stateX + (Theme.DotRadius * 2) + (2 * Theme.Unit), top.Y + ((height - CrucibleChrome.LineHeight(Theme.SecondaryScale)) / 2f)), row.Label, color, Theme.SecondaryScale);
            }
            else
            {
                var callHeight = CrucibleChrome.WrappedHeight(row.Call, inner);
                var responseHeight = CrucibleChrome.WrappedHeight(row.Response, inner, Theme.SecondaryScale);
                height = (2 * padY) + callHeight + Theme.Unit + responseHeight + Theme.Unit + CrucibleChrome.LineHeight(Theme.SecondaryScale);
                var top = origin + new Vector2(0, y);
                draw.AddRectFilled(top, top + new Vector2(width, height), Theme.Pack(Theme.ShellBar), CrucibleChrome.Radius);
                var cursorY = top.Y + padY;
                cursorY += CrucibleChrome.DrawWrapped(draw, new Vector2(top.X + padX, cursorY), row.Call, inner, Theme.TextPrimary) + Theme.Unit;
                cursorY += CrucibleChrome.DrawWrapped(draw, new Vector2(top.X + padX, cursorY), row.Response, inner, Theme.TextSecondary, Theme.SecondaryScale) + Theme.Unit;
                CrucibleChrome.DrawText(draw, new Vector2(top.X + padX, cursorY), row.Label, color, Theme.SecondaryScale);
            }
            y += height + (2 * Theme.Unit);
        }
        CrucibleChrome.Advance(origin, width, y, 2 * Theme.Unit);
    }

    /// <summary>Picks with their traits in two aligned columns (one shared name width).</summary>
    internal static void HornLines(IEnumerable<(string Name, string Why)> picks, Vector4 dot)
    {
        var list = picks.ToList();
        if (list.Count == 0)
            return;
        var nameWidth = list.Max(p => CrucibleChrome.TextWidth(p.Name)) + CrucibleChrome.Gap;
        foreach (var (name, why) in list)
            HornLine(name, why, dot, nameWidth);
    }

    private static void HornLine(string name, string why, Vector4? dot, float sharedNameWidth)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = CrucibleChrome.Avail();
        draw.AddCircleFilled(pos + new Vector2(Theme.Unit, CrucibleChrome.LineHeight() / 2f), Theme.DotRadius, Theme.Pack(dot ?? Theme.StateGreen));
        var nameWidth = MathF.Min(width * 0.4f, sharedNameWidth);
        var left = 4 * Theme.Unit;
        CrucibleChrome.DrawFitted(draw, pos + new Vector2(left, 0), name, nameWidth, Theme.TextPrimary);
        var whyWidth = width - left - nameWidth - CrucibleChrome.Gap;
        float height;
        if (whyWidth >= 140)
        {
            var whyHeight = CrucibleChrome.DrawWrapped(draw, pos + new Vector2(left + nameWidth + CrucibleChrome.Gap, (CrucibleChrome.LineHeight() - CrucibleChrome.LineHeight(Theme.SecondaryScale)) / 2f), why, whyWidth, Theme.TextSecondary, Theme.SecondaryScale);
            height = MathF.Max(CrucibleChrome.LineHeight(), whyHeight);
        }
        else
        {
            var whyHeight = CrucibleChrome.DrawWrapped(draw, pos + new Vector2(left, CrucibleChrome.LineHeight()), why, width - left, Theme.TextSecondary, Theme.SecondaryScale);
            height = CrucibleChrome.LineHeight() + whyHeight;
        }
        CrucibleChrome.Advance(pos, width, height, Theme.Unit * 2);
    }

    /// <summary>Requirement or key/value row: fixed glyph slot, dim label, small coloured value at the inner right edge.</summary>
    private static void StatusRow(string label, string value, UiState state, bool ok, bool glyph = true)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = CrucibleChrome.Avail();
        var rowHeight = CrucibleChrome.LineHeight() + (3 * Theme.Unit);
        var textY = pos.Y + ((rowHeight - CrucibleChrome.LineHeight()) / 2f);
        const float slot = 5 * Theme.Unit;
        var labelX = glyph ? slot + Theme.Unit : 0;
        if (glyph)
        {
            var mark = ok ? "✓" : "—";
            var markColor = ok ? Theme.StateGreen : Theme.TextSecondary;
            CrucibleChrome.DrawText(draw, new Vector2(pos.X + ((slot - CrucibleChrome.TextWidth(mark)) / 2f), textY), mark, markColor);
        }
        var valueColor = state switch
        {
            UiState.Ready => Theme.StateGreen,
            UiState.Missing or UiState.Blocked => Theme.StateAmber,
            _ => Theme.TextSecondary,
        };
        var valueWidth = CrucibleChrome.TextWidth(value, Theme.SecondaryScale);
        CrucibleChrome.DrawFitted(draw, pos + new Vector2(labelX, textY - pos.Y), label, width - labelX - valueWidth - CrucibleChrome.Gap, Theme.TextSecondary);
        CrucibleChrome.DrawTextRight(draw, pos.X + width, pos.Y + ((rowHeight - CrucibleChrome.LineHeight(Theme.SecondaryScale)) / 2f), value, valueColor, Theme.SecondaryScale);
        CrucibleChrome.Advance(pos, width, rowHeight, 0);
    }

    private static readonly string[] ScoreGoalItems =
    [
        "Survival first (default)",
        "Starve the Fever (never feed)",
        "Feed the Bold (feed anything harmless)",
    ];

    private static void DrawFeedShop(CrucibleShell shell)
    {
        var cfg = Plugin.Config;
        Columns("feed",
            () =>
            {
                CrucibleCard.Draw("beast_feed", 0, () =>
                {
                    Section("Beast Feed", "Choose who eats and confirm only the matching prompt");
                    shell.Setting("Pick who eats the feed and confirm", () => cfg.AutoFeed, value => cfg.AutoFeed = value,
                        "When a feed is bought, feeds it to the familiar it helps most in the fights ahead. Never a familiar whose kin cannot eat it, one that is full or knocked out, or one that already ate it. A feed that would hurt every familiar is left to you.");
                    var pos = ImGui.GetCursorScreenPos();
                    var width = CrucibleChrome.Avail();
                    var draw = ImGui.GetWindowDrawList();
                    draw.AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
                    CrucibleChrome.DrawText(draw, pos + new Vector2(0, 3 * Theme.Unit), "Score goal for feeding", Theme.TextPrimary);
                    CrucibleChrome.Advance(pos, width, (3 * Theme.Unit) + CrucibleChrome.LineHeight(), 2 * Theme.Unit);
                    var goal = Math.Clamp((int)cfg.ScoreGoal, 0, ScoreGoalItems.Length - 1);
                    var open = CrucibleChrome.BeginCombo("##scoreGoal", ScoreGoalItems[goal], width);
                    if (open)
                    {
                        for (var i = 0; i < ScoreGoalItems.Length; i++)
                            if (ImGui.Selectable(ScoreGoalItems[i], i == goal))
                            {
                                cfg.ScoreGoal = (ScoreGoal)i;
                                shell.ScoreGoalChanged();
                            }
                    }
                    CrucibleChrome.EndCombo(open);
                    Paragraph("All three existing values remain available. Manual purchases and feeding edits hand the rest of the visit to the player. No selling or leaving is added.");
                }, fill: true);
            },
            () =>
            {
                CrucibleCard.Draw("shop", 0, () =>
                {
                    Section("Shop", "Buy what the fights ahead call for; never sell or leave");
                    shell.Setting("Buy what the fights ahead call for", () => cfg.AutoShop, value => cfg.AutoShop = value,
                        "Keeps enough heals for the fights ahead, then buys what keeps you and your familiars alive: resistance, max HP and damage-taken gear, revives, and feed for the horn picks. Each purchase is confirmed only when the prompt names that item.");
                }, fill: true);
            }, railFraction: 0.5f);
    }

    private static void DrawCampsite(CrucibleShell shell)
    {
        var cfg = Plugin.Config;
        Columns("camp",
            () =>
            {
                CrucibleCard.Draw("campsite_selection", 0, () =>
                {
                    Section("Campsite selection", "Select who rests; Rest remains the player's button");
                    shell.Setting("Select who rests (you press Rest)", () => cfg.AutoCamp, value => cfg.AutoCamp = value,
                        "Weighs your heal against the familiars' (resting familiars lose their feed unless they ate Lily Simular) and selects the familiars worth resting. Your self heal wins when you are low. Rest is always your button.");
                }, fill: true);
            },
            () =>
            {
                CrucibleCard.Draw("campsite_state", 0, () =>
                {
                    Section("State");
                    CrucibleChrome.Notice("Selection only. Rest confirmation is never automated.", UiState.Ready);
                }, fill: true);
            }, railFraction: 0.5f);
        CrucibleCard.Draw("campsite_tradeoffs", 0, () =>
        {
            Section("Resting trade-offs");
            Paragraph("Knocked-out familiars are never picked. Feed effects, current HP and upcoming horn demand continue to use the real policy inputs.", gap: 0);
        });
    }

    private static void DrawDisplayLogging(CrucibleShell shell)
    {
        var cfg = Plugin.Config;
        Columns("display",
            () =>
            {
                CrucibleCard.Draw("guide_announcements", 0, () =>
                {
                    Section("Guide & announcements");
                    shell.Setting("Open the fight guide on the upcoming fight", () => cfg.GuideAutoOpen, value => cfg.GuideAutoOpen = value,
                        "When the board layout or Battlehorn screen points at a fight, the guide opens on it; warned casts can open it too.");
                    shell.Setting("Announce picks in chat", () => cfg.AnnouncePicks, value => cfg.AnnouncePicks = value,
                        "One chat line per fight naming each familiar and why it was picked.", separator: true);
                    shell.Setting("Record Crucible screens to the log", () => cfg.RecordScreens, value => cfg.RecordScreens = value,
                        "Read-only. Writes Crucible screens and their buttons to the Dalamud log. Never presses anything.", separator: true);
                    var pos = ImGui.GetCursorScreenPos();
                    var width = CrucibleChrome.Avail();
                    ImGui.GetWindowDrawList().AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
                    ImGui.SetCursorScreenPos(pos + new Vector2(0, 3 * Theme.Unit));
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(CrucibleChrome.Gap, (CrucibleChrome.ControlHeight - ImGui.GetTextLineHeight()) / 2f));
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, CrucibleChrome.Radius);
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
                    ImGui.PushStyleColor(ImGuiCol.Button, Theme.Surface);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Accent with { W = 0.14f });
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Accent with { W = 0.28f });
                    ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
                    Plugin.Telemetry?.DrawReportButton();
                    ImGui.PopStyleColor(4);
                    ImGui.PopStyleVar(3);
                }, fill: true);
            },
            () =>
            {
                CrucibleCard.Draw("current_behavior", 0, () =>
                {
                    Section("Current behavior");
                    StatusRow("Guide auto-open", cfg.GuideAutoOpen ? "On" : "Off", cfg.GuideAutoOpen ? UiState.Ready : UiState.Idle, cfg.GuideAutoOpen, glyph: false);
                    StatusRow("Chat announcements", cfg.AnnouncePicks ? "On" : "Off", cfg.AnnouncePicks ? UiState.Ready : UiState.Idle, cfg.AnnouncePicks, glyph: false);
                    StatusRow("Screen recording", cfg.RecordScreens ? "On" : "Off", cfg.RecordScreens ? UiState.Ready : UiState.Idle, cfg.RecordScreens, glyph: false);
                });
                CrucibleCard.Draw("unchanged", 0, () =>
                {
                    Section("Unchanged");
                    Paragraph("Slash commands and Lazy Hub quick controls work as before.", gap: 0);
                }, fill: true);
            }, railFraction: 0.36f);
    }

    private static void DrawRewards(CrucibleShell shell, bool treasure)
    {
        var cfg = Plugin.Config;
        CrucibleCard.Draw(treasure ? "treasure" : "spoils", 0, () =>
        {
            if (treasure)
            {
                Section("Treasure suggestion");
                shell.Setting("Suggest the best choice", () => cfg.AutoTreasure, value => cfg.AutoTreasure = value,
                    "Marks the choice that helps most and says why. It only suggests; no coffer choice is clicked.");
                CrucibleChrome.Notice("Suggestion only — no coffer choice is clicked.", UiState.Idle);
            }
            else
            {
                Section("Spoils handling");
                shell.Setting("Take all when everything fits", () => cfg.AutoSpoils, value => cfg.AutoSpoils = value,
                    "If the loot would not fit (or repeats gear you own), shows the best items first and leaves the choice to you.");
                CrucibleChrome.Notice("Overflow or duplicate gear remains the player's decision. No selling is added.", UiState.Ready);
            }
        }, width: 720);
    }

    private readonly record struct Stat(string Label, string Value, string? Cap, string Note, float? Fraction);

    private static void DrawStatCards(params Stat[] stats)
    {
        var draw = ImGui.GetWindowDrawList();
        var available = CrucibleChrome.Avail();
        var gap = CrucibleChrome.Gap;
        var stacked = available < (3 * 220) + (2 * gap);
        var width = stacked ? available : (available - (2 * gap)) / 3;
        const float pad = CrucibleChrome.Gap;
        var labelHeight = CrucibleChrome.LineHeight(Theme.SecondaryScale);
        var valueHeight = CrucibleChrome.LineHeight(CrucibleChrome.StatValueScale);
        var noteHeight = CrucibleChrome.LineHeight(Theme.SecondaryScale);
        var height = pad + labelHeight + (2 * Theme.Unit) + valueHeight + (2 * Theme.Unit) + Theme.BarHeight + (2 * Theme.Unit) + noteHeight + pad;
        var origin = ImGui.GetCursorScreenPos();
        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (stacked ? new Vector2(0, i * (height + gap)) : new Vector2(i * (width + gap), 0));
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), CrucibleChrome.Radius);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), CrucibleChrome.Radius);

            var y = pos.Y + pad;
            CrucibleChrome.Diamond(draw, new Vector2(pos.X + pad + 5, y + (labelHeight / 2f)), 5, Theme.Pack(Theme.Accent));
            CrucibleChrome.DrawFitted(draw, new Vector2(pos.X + pad + 16, y), stat.Label, width - (2 * pad) - 16, Theme.TextSecondary, Theme.SecondaryScale);
            y += labelHeight + (2 * Theme.Unit);

            var empty = stat.Value == "—";
            CrucibleChrome.DrawText(draw, new Vector2(pos.X + pad, y), stat.Value, empty ? Theme.TextSecondary : Theme.TextPrimary, CrucibleChrome.StatValueScale);
            if (stat.Cap is { } cap)
            {
                var trimmed = cap.TrimStart('/', ' ');
                var capText = int.TryParse(trimmed, out _) ? "/ " + trimmed : trimmed;
                var capHeight = CrucibleChrome.LineHeight(Theme.SecondaryScale);
                var capX = pos.X + pad + CrucibleChrome.TextWidth(stat.Value, CrucibleChrome.StatValueScale) + (2 * Theme.Unit);
                CrucibleChrome.DrawText(draw, new Vector2(capX, y + valueHeight - capHeight - (valueHeight * 0.08f)), capText, Theme.TextSecondary, Theme.SecondaryScale);
            }
            y += valueHeight + (2 * Theme.Unit);

            var min = new Vector2(pos.X + pad, y);
            var max = new Vector2(pos.X + width - pad, y + Theme.BarHeight);
            if (stat.Fraction is { } fraction)
            {
                draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), Theme.BarHeight / 2f);
                if (fraction > 0)
                    draw.AddRectFilled(min, new Vector2(min.X + ((max.X - min.X) * Math.Clamp(fraction, 0, 1)), max.Y), Theme.Pack(Theme.StateGreen), Theme.BarHeight / 2f);
            }
            y += Theme.BarHeight + (2 * Theme.Unit);

            var noteColor = stat.Fraction is >= 1f ? Theme.StateGreen : Theme.TextSecondary;
            CrucibleChrome.DrawFitted(draw, new Vector2(pos.X + pad, y), stat.Note, width - (2 * pad), noteColor, Theme.SecondaryScale);
        }
        var total = stacked ? (stats.Length * height) + ((stats.Length - 1) * gap) : height;
        CrucibleChrome.Advance(origin, available, total);
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
    public const float Padding = 4 * Theme.Unit;
    private static int _depth;

    /// <summary>Screen Y that cards marked <c>fill</c> stretch down to, or NaN when columns are stacked.</summary>
    public static float FillBottom { get; set; } = float.NaN;

    /// <summary>The stretch given to the most recent filling card, so column heights can be stored without it.</summary>
    public static float LastStretch { get; private set; }

    public static float BeginColumn(float bottom)
    {
        FillBottom = bottom;
        LastStretch = 0;
        return bottom;
    }

    /// <summary>Free height below the cursor inside a card that is being stretched (0 when not stretching).</summary>
    public static float RemainingFill()
        => float.IsNaN(FillBottom) ? 0 : MathF.Max(0, FillBottom - Padding - ImGui.GetCursorScreenPos().Y);

    public static void Draw(string id, float minHeight, Action content, CrucibleCardTone tone = CrucibleCardTone.Surface,
        float width = 0, bool fill = false, float padY = Padding)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var available = CrucibleChrome.Avail();
        var cardWidth = width <= 0 ? available : MathF.Min(width, available);
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
            ImGui.SetCursorScreenPos(origin + new Vector2(Padding, padY));
            CrucibleChrome.PushRight(origin.X + cardWidth - Padding);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * Padding)));
            ImGui.BeginGroup();
            content();
            ImGui.EndGroup();
            ImGui.PopTextWrapPos();
            CrucibleChrome.PopRight();
            var contentBottom = ImGui.GetItemRectMax().Y;
            var natural = MathF.Max(minHeight, contentBottom - origin.Y + padY);
            var height = natural;
            if (fill && !float.IsNaN(FillBottom) && topLevel)
            {
                height = MathF.Max(natural, FillBottom - origin.Y);
                LastStretch = height - natural;
            }

            draw.ChannelsSetCurrent(topLevel ? 0 : 1);
            var (fillColor, border) = Colors(tone);
            draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), fillColor, CrucibleChrome.Radius);
            draw.AddRect(origin, origin + new Vector2(cardWidth, height), border, CrucibleChrome.Radius);
            if (topLevel)
                draw.ChannelsMerge();
            else
                draw.ChannelsSetCurrent(2);
            CrucibleChrome.Advance(origin + new Vector2(0, height), cardWidth, 0, topLevel ? CrucibleChrome.Gap : 2 * Theme.Unit);
        }
        finally
        {
            if (topLevel)
                _depth--;
        }
    }

    private static (uint Fill, uint Border) Colors(CrucibleCardTone tone) => tone switch
    {
        CrucibleCardTone.Row => (Theme.Pack(Theme.ShellBar), Theme.Pack(Theme.ShellBar)),
        CrucibleCardTone.Recommended => (Theme.Pack(Theme.StateGreen with { W = 0.10f }), Theme.Pack(Theme.StateGreen with { W = 0.35f })),
        CrucibleCardTone.Attention => (Theme.Pack(Theme.StateAmber with { W = 0.10f }), Theme.Pack(Theme.StateAmber with { W = 0.35f })),
        _ => (Theme.Pack(Theme.Surface), Theme.Pack(Theme.SurfaceBorder)),
    };
}
