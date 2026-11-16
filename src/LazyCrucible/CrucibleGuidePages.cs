using System.Numerics;
using Dalamud.Bindings.ImGui;
using Lalalazy.Crucible;
using LalaUiKit;

namespace LazyCrucible;

internal static class CrucibleGuidePages
{
    public static string LiveCount(CrucibleShell shell, string page)
    {
        if (page == CrucibleShell.UpcomingPage)
        {
            if (shell.GuideFollow && RunTracker.Focus.Battle < 0)
                return "No fight in focus";
            var fight = Plugin.Guide.Fight(shell.GuideBoard, shell.GuideBattle);
            if (fight is null)
                return "No guide entry";
            return $"{CruciblePages.Selection(fight.Board, fight.Battle).Picks.Count} horn picks · {fight.Mechanics.Count} mechanics";
        }
        if (page == CrucibleShell.AdvisorPage)
            return $"{BST_CrucibleNeedFirst.BoardRoster(EffectiveAdvisorBoard(shell), CrucibleGame.BeastCaptured).Count} advisor candidates";
        if (page == "guide.recent")
            return $"{SelectionScreens.Recent.Count} recorded choices";
        if (page == "guide.browse")
            return $"{Plugin.Guide.File.Fights.Count(f => f.Board == shell.GuideBoard)} fights on board {shell.GuideBoard}";
        return "Guide";
    }

    public static string LiveSubtext(CrucibleShell shell, string page)
    {
        var board = page == CrucibleShell.AdvisorPage
            ? EffectiveAdvisorBoard(shell)
            : Math.Clamp(shell.GuideBoard, 1, 5);
        var battle = page == CrucibleShell.AdvisorPage ? shell.AdvisorBattle : shell.GuideBattle;
        return $"{BST_CrucibleData.Boards[board - 1].Name} · {Label(board, battle)}{UiPresentationLogic.LocationSuffix(board, battle)}";
    }

    private static int EffectiveAdvisorBoard(CrucibleShell shell)
        => shell.AdvisorBoard is >= 1 and <= 5
            ? shell.AdvisorBoard
            : CruciblePages.ActiveBoard() is var active && active != 0 ? active : 1;

    public static void Render(CrucibleShell shell, string page)
    {
        switch (page)
        {
            case CrucibleShell.UpcomingPage: DrawUpcoming(shell); break;
            case "guide.browse": DrawBrowse(shell); break;
            case CrucibleShell.AdvisorPage: DrawAdvisor(shell); break;
            case "guide.recent": DrawRecent(); break;
            default: throw new InvalidOperationException("Unknown guide page " + page);
        }
    }

    /// <summary>The fight's guide title, or its battle label; "?" from the data table reads as an unknown fight.</summary>
    internal static string FightTitle(int board, int battle)
    {
        var title = Plugin.Guide.Fight(board, battle)?.Title ?? BST_CrucibleData.BattleLabel(board, battle);
        return string.IsNullOrWhiteSpace(title) || title == "?" ? $"Unknown fight {battle}" : title;
    }

    private static string Label(int board, int battle)
    {
        var info = BST_CrucibleData.Battles.FirstOrDefault(x => x.Board == board && x.Battle == battle);
        var role = info.Role switch { CrucibleRole.Boss => "boss", CrucibleRole.EliteEnemy => "elite", _ => "enemy" };
        var title = FightTitle(board, battle);
        return $"{title} ({role}{(info.RandomOnly ? ", random" : "")})";
    }

    private static readonly Dictionary<string, string> SourceNames = new()
    {
        ["panel"] = "Game data",
        ["sheet"] = "Game sheets",
        ["IV"] = "Icy Veins",
        ["NT"] = "nettoge",
        ["AS"] = "asellog",
        ["G8"] = "Game8",
        ["LSB"] = "Lodestone blog",
        ["CGW"] = "Console Games Wiki",
        ["MRB"] = "MagitekRoutine data",
        ["CHR"] = "CherryXIV",
        ["BMR"] = "BossMod Reborn",
        ["R"] = "Reddit",
        ["YT"] = "Video",
        ["LOG"] = "Recorded runs",
        ["LSN"] = "Lodestone news",
    };

    /// <summary>Readable source names for the guide's short source codes.</summary>
    internal static string SourceText(IEnumerable<string> sources)
        => string.Join(", ", sources.Select(code =>
        {
            var key = code.Split(':')[0];
            return SourceNames.TryGetValue(key, out var name) ? name : key;
        }).Distinct());

    private static void SourceLine(IEnumerable<string> sources, float indent = 0)
    {
        var text = SourceText(sources);
        if (text.Length == 0)
            return;
        var pos = ImGui.GetCursorScreenPos() + new Vector2(indent, 0);
        var width = CrucibleChrome.Avail() - indent;
        var height = CrucibleChrome.DrawWrapped(ImGui.GetWindowDrawList(), pos, "Source: " + text, width, Theme.TextSecondary, Theme.SecondaryScale);
        CrucibleChrome.Advance(ImGui.GetCursorScreenPos(), width, height, Theme.Unit);
    }

    private static void DrawUpcoming(CrucibleShell shell)
    {
        if (shell.GuideFollow && RunTracker.Focus.Battle < 0)
        {
            CruciblePages.Columns("guide_idle",
                () => CrucibleCard.Draw("guide_idle", 0, () =>
                    CruciblePages.EmptyState("No fight in focus", "Follow the next fight or browse a real guide entry."), fill: true),
                () => DrawFightNavigation(shell, fill: true), fillPage: true);
            return;
        }
        var fight = Plugin.Guide.Fight(shell.GuideBoard, shell.GuideBattle);
        if (fight is null)
        {
            CrucibleChrome.Notice("No guide entry for this fight. Browse the real board entries or choose another focused fight; no substitute content is invented.", UiState.Empty);
            if (CrucibleChrome.ActionButton("browse_boards", "Browse boards"))
                shell.CurrentPage = "guide.browse";
            return;
        }
        CruciblePages.Columns("guide",
            () => DrawFight(fight),
            () =>
            {
                DrawFightNavigation(shell, fill: false);
                DrawBringAndFacts(fight);
            });
    }

    private static void DrawFightNavigation(CrucibleShell shell, bool fill)
    {
        CrucibleCard.Draw("fight_navigation", 0, () =>
        {
            CruciblePages.Section("Fight navigation");
            CrucibleChrome.Toggle("Follow the next fight", ref shell.GuideFollow, RunTracker.Focus.Battle >= 0
                ? $"Next: {FightTitle(RunTracker.Focus.Board, RunTracker.Focus.Battle)}"
                : "No fight in focus");
            var pos = ImGui.GetCursorScreenPos();
            var width = CrucibleChrome.Avail();
            var draw = ImGui.GetWindowDrawList();
            draw.AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
            CrucibleChrome.DrawText(draw, pos + new Vector2(0, 3 * Theme.Unit), "Board", Theme.TextPrimary);
            var rowTop = pos + new Vector2(0, (3 * Theme.Unit) + CrucibleChrome.LineHeight() + (2 * Theme.Unit));
            var gap = 2 * Theme.Unit;
            var buttonWidth = (width - (4 * gap)) / 5;
            for (var b = 1; b <= 5; b++)
            {
                if (CrucibleChrome.OutlineButton(draw, $"guide_board_{b}", b.ToString(),
                        rowTop + new Vector2((b - 1) * (buttonWidth + gap), 0), new Vector2(buttonWidth, CrucibleChrome.ControlHeight), shell.GuideBoard == b))
                {
                    shell.GuideBoard = b;
                    shell.GuideBattle = 1;
                    shell.GuideFollow = false;
                }
            }
            CrucibleChrome.Advance(rowTop, width, CrucibleChrome.ControlHeight, 2 * Theme.Unit);
            var open = CrucibleChrome.BeginCombo("##guideBattle", Label(shell.GuideBoard, shell.GuideBattle), width);
            if (open)
            {
                foreach (var battle in BST_CrucibleData.Battles.Where(x => x.Board == shell.GuideBoard).OrderBy(x => x.Battle == 0).ThenBy(x => x.Battle))
                    if (ImGui.Selectable(Label(shell.GuideBoard, battle.Battle), battle.Battle == shell.GuideBattle))
                    {
                        shell.GuideBattle = battle.Battle;
                        shell.GuideFollow = false;
                    }
            }
            CrucibleChrome.EndCombo(open);
        }, fill: fill);
    }

    private static void DrawFight(GuideFight fight)
    {
        var selection = CruciblePages.Selection(fight.Board, fight.Battle);
        CruciblePages.Columns("guide_picks",
            () => CrucibleCard.Draw("horn_picks", 0, () =>
            {
                CruciblePages.Section("Horn picks", null, selection.Picks.Count + " ranked picks");
                if (!CrucibleGame.RosterLoaded)
                    Widgets.SecondaryWrapped("Captured list unavailable; ranking assumes every familiar is captured.");
                CruciblePages.HornLines(selection.Picks.Select(pick => (CruciblePages.BeastName(pick.Row), pick.Why)), Theme.Accent);
            }, fill: true),
            () => CrucibleCard.Draw("calls", 0, () => DrawCalls(fight, selection), fill: true),
            breakpoint: 640, railFraction: 0.42f);

        CrucibleCard.Draw("mechanics", 0, () =>
        {
            CruciblePages.Section("Dangerous hits & mechanics", "Tell → what to do");
            var rows = GuideNeeds.Rows(selection);
            bool CoveredByCall(GuideMechanic mechanic) => rows.Any(row =>
                mechanic.Name.Contains(row.What, StringComparison.OrdinalIgnoreCase)
                || row.What.Contains(mechanic.Name, StringComparison.OrdinalIgnoreCase));
            var ordered = fight.Mechanics.OrderByDescending(CoveredByCall)
                .ThenByDescending(m => m.Tell.Contains("never interruptible", StringComparison.OrdinalIgnoreCase));
            foreach (var mechanic in ordered)
                MechanicRow(mechanic, false);
            foreach (var hit in fight.Hits)
                MechanicRow(hit, true);
            if (fight.KillOrder is { } kill)
                KillOrderRow(kill);
        }, fill: true);
    }

    private static void DrawCalls(GuideFight fight, CrucibleSelection selection)
    {
        var rows = GuideNeeds.Rows(selection);
        var mitigation = GuideNeeds.MitigationRows(selection);
        var required = UiPresentationLogic.RequiredCallStats(rows);
        CruciblePages.Section("Calls for", null, required.Total + " required");
        foreach (var row in rows)
        {
            var state = row.Covered ? UiState.Ready : row.Warn ? UiState.Blocked : UiState.Idle;
            var detail = row.Covered
                ? $"{row.Tool} — {CruciblePages.BeastName(row.CoveredBy)}"
                : row.WhyNot ?? "No horn pick answers this call";
            CallLine(row.Covered ? "✓" : row.Warn ? "!" : "·", row.What + UiPresentationLogic.TierSuffix(row.Tier), detail,
                row.Covered ? "Ready" : row.Warn ? "Needed" : "Useful", state, row.Src);
        }
        foreach (var row in mitigation)
        {
            var by = row.CoveredBy != 0 ? $" — {CruciblePages.BeastName(row.CoveredBy)} ({row.Skill})" : " — no pick answers it";
            CallLine(row.CoveredBy != 0 ? "✓" : "·", $"{row.Tool}: {row.What}{by}", "", "", row.CoveredBy != 0 ? UiState.Ready : UiState.Idle, row.Src);
        }
        foreach (var mechanic in fight.Mechanics.Where(m => m.Tell.Contains("never interruptible", StringComparison.OrdinalIgnoreCase)))
            CallLine("•", mechanic.Name, mechanic.Do, "Guide call", UiState.Blocked, []);
        if (rows.Count == 0 && mitigation.Count == 0)
            CruciblePages.Paragraph("No required horn calls for this fight.");
    }

    private static void CallLine(string mark, string main, string detail, string stateLabel, UiState state, IEnumerable<string> src)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = CrucibleChrome.Avail();
        const float slot = 5 * Theme.Unit;
        var color = Copy.MarkColor(state);
        var stateWidth = stateLabel.Length == 0 ? 0 : CrucibleChrome.TextWidth(stateLabel, Theme.SecondaryScale);
        var mainWidth = width - slot - (stateWidth > 0 ? stateWidth + CrucibleChrome.Gap : 0);
        CrucibleChrome.DrawText(draw, pos + new Vector2((slot - CrucibleChrome.TextWidth(mark)) / 2f, 0), mark, color);
        var y = CrucibleChrome.DrawWrapped(draw, pos + new Vector2(slot, 0), main, mainWidth, Theme.TextPrimary);
        if (stateWidth > 0)
            CrucibleChrome.DrawTextRight(draw, pos.X + width, pos.Y + ((CrucibleChrome.LineHeight() - CrucibleChrome.LineHeight(Theme.SecondaryScale)) / 2f), stateLabel, color, Theme.SecondaryScale);
        if (detail.Length > 0)
            y += Theme.Unit + CrucibleChrome.DrawWrapped(draw, pos + new Vector2(slot, y + Theme.Unit), detail, width - slot, Theme.TextSecondary, Theme.SecondaryScale);
        CrucibleChrome.Advance(pos, width, y, Theme.Unit);
        SourceLine(src, slot);
        ImGui.Dummy(new Vector2(0, Theme.Unit));
    }

    private static void MechanicRow(GuideMechanic mechanic, bool danger)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = CrucibleChrome.Avail();
        const float padX = CrucibleChrome.Gap;
        const float padY = 3 * Theme.Unit;
        var inner = width - (2 * padX);
        var wide = inner >= 520;
        var gap = CrucibleChrome.Gap;
        var nameWidth = wide ? Math.Clamp(inner * 0.28f, 150, 240) : inner;
        var rightWidth = wide ? inner - nameWidth - gap : inner;
        var nameHeight = CrucibleChrome.WrappedHeight(mechanic.Name, nameWidth);
        var chipText = mechanic.Cast is { } cast ? $"{cast:0.#} s" : "";
        var chipHeight = CrucibleChrome.LineHeight(Theme.SecondaryScale) + (2 * Theme.Unit);
        var chipWidth = chipText.Length == 0 ? 0 : CrucibleChrome.TextWidth(chipText, Theme.SecondaryScale) + (4 * Theme.Unit);
        var leftHeight = nameHeight + (chipText.Length == 0 ? 0 : Theme.Unit + chipHeight);

        var labelWidth = MathF.Max(CrucibleChrome.TextWidth("Tell", Theme.SecondaryScale), CrucibleChrome.TextWidth("Do", Theme.SecondaryScale)) + (2 * Theme.Unit);
        var textWidth = MathF.Max(60, rightWidth - labelWidth);
        var tellHeight = mechanic.Tell.Length == 0 ? 0 : CrucibleChrome.WrappedHeight(mechanic.Tell, textWidth);
        var doHeight = mechanic.Do.Length == 0 ? 0 : CrucibleChrome.WrappedHeight(mechanic.Do, textWidth);
        var source = SourceText(mechanic.Src);
        var sourceHeight = source.Length == 0 ? 0 : CrucibleChrome.WrappedHeight("Source: " + source, rightWidth, Theme.SecondaryScale);
        var rightHeight = tellHeight + doHeight + sourceHeight
            + (tellHeight > 0 && doHeight > 0 ? Theme.Unit : 0)
            + (sourceHeight > 0 && (tellHeight + doHeight) > 0 ? Theme.Unit : 0);

        var contentHeight = wide ? MathF.Max(leftHeight, rightHeight) : leftHeight + (rightHeight > 0 ? 2 * Theme.Unit + rightHeight : 0);
        var height = contentHeight + (2 * padY);
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar), CrucibleChrome.Radius);

        var top = origin + new Vector2(padX, padY);
        CrucibleChrome.DrawWrapped(draw, top, mechanic.Name, nameWidth, danger ? Theme.StateAmber : Theme.TextPrimary);
        if (chipText.Length > 0)
        {
            var chipMin = top + new Vector2(0, nameHeight + Theme.Unit);
            draw.AddRectFilled(chipMin, chipMin + new Vector2(chipWidth, chipHeight), Theme.Pack(Theme.StateAmber with { W = 0.16f }), chipHeight / 2f);
            CrucibleChrome.DrawText(draw, chipMin + new Vector2(2 * Theme.Unit, Theme.Unit), chipText, Theme.StateAmber, Theme.SecondaryScale);
        }
        var right = wide ? top + new Vector2(nameWidth + gap, 0) : top + new Vector2(0, leftHeight + (2 * Theme.Unit));
        var cursor = 0f;
        void Line(string label, string text, float h)
        {
            if (h <= 0)
                return;
            CrucibleChrome.DrawText(draw, right + new Vector2(0, cursor + ((CrucibleChrome.LineHeight() - CrucibleChrome.LineHeight(Theme.SecondaryScale)) / 2f)), label, Theme.TextSecondary, Theme.SecondaryScale);
            CrucibleChrome.DrawWrapped(draw, right + new Vector2(labelWidth, cursor), text, textWidth, Theme.TextPrimary);
            cursor += h + Theme.Unit;
        }
        Line("Tell", mechanic.Tell, tellHeight);
        Line("Do", mechanic.Do, doHeight);
        if (sourceHeight > 0)
            CrucibleChrome.DrawWrapped(draw, right + new Vector2(0, cursor), "Source: " + source, rightWidth, Theme.TextSecondary, Theme.SecondaryScale);
        CrucibleChrome.Advance(origin, width, height, 2 * Theme.Unit);
    }

    private static void KillOrderRow(GuideNote kill)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = CrucibleChrome.Avail();
        const float padX = CrucibleChrome.Gap;
        const float padY = 3 * Theme.Unit;
        var inner = width - (2 * padX);
        var textHeight = CrucibleChrome.WrappedHeight(kill.Text, inner);
        var source = SourceText(kill.Src);
        var sourceHeight = source.Length == 0 ? 0 : Theme.Unit + CrucibleChrome.WrappedHeight("Source: " + source, inner, Theme.SecondaryScale);
        var labelHeight = CrucibleChrome.LineHeight(Theme.SecondaryScale);
        var height = (2 * padY) + labelHeight + Theme.Unit + textHeight + sourceHeight;
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar), CrucibleChrome.Radius);
        var y = origin.Y + padY;
        CrucibleChrome.DrawText(draw, new Vector2(origin.X + padX, y), "Kill order", Theme.TextSecondary, Theme.SecondaryScale);
        y += labelHeight + Theme.Unit;
        CrucibleChrome.DrawWrapped(draw, new Vector2(origin.X + padX, y), kill.Text, inner, Theme.TextPrimary);
        if (sourceHeight > 0)
            CrucibleChrome.DrawWrapped(draw, new Vector2(origin.X + padX, y + textHeight + Theme.Unit), "Source: " + source, inner, Theme.TextSecondary, Theme.SecondaryScale);
        CrucibleChrome.Advance(origin, width, height, 2 * Theme.Unit);
    }

    private static void DrawBringAndFacts(GuideFight fight)
    {
        CrucibleCard.Draw("bring", 0, () =>
        {
            CruciblePages.Section("Bring");
            foreach (var item in fight.Bring)
                BulletLine(item.Text, item.Src);
            var resists = ResistItems(fight.ThreatFlags);
            if (resists.Length > 0)
                BulletLine(resists, ["sheet"]);
        });
        CrucibleCard.Draw("fight_facts", 0, () =>
        {
            CruciblePages.Section("Fight facts");
            var weak = BST_CrucibleData.Enemies.Where(e => e.Board == fight.Board && e.Battle == fight.Battle)
                .Select(e => $"{e.Name}: {(e.Weakness == CrucibleWeakness.None ? "no weakness" : e.Weakness.ToString().ToLowerInvariant())}");
            CruciblePages.Paragraph("Weak to  " + string.Join("; ", weak));
            if (fight.Threats.Count > 0)
                Widgets.SecondaryWrapped("Threats  " + string.Join(", ", fight.Threats.Select(CrucibleGuide.ThreatLabel)));
            var sources = fight.Hits.Concat(fight.Mechanics).SelectMany(m => m.Src).Concat(fight.Bring.SelectMany(b => b.Src)).ToList();
            if (fight.KillOrder is { } kill)
                sources.AddRange(kill.Src);
            if (sources.Count > 0)
                Widgets.SecondaryWrapped("Sources  " + SourceText(sources.Distinct().OrderBy(s => s)));
            if (fight.Unknown.Count > 0)
            {
                CruciblePages.Paragraph("Not covered by any source", 1f, Theme.TextPrimary, Theme.Unit);
                foreach (var unknown in fight.Unknown)
                    CruciblePages.Paragraph("· " + unknown, gap: Theme.Unit);
            }
        }, fill: true);
    }

    private static void BulletLine(string text, IEnumerable<string> src)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = CrucibleChrome.Avail();
        const float slot = 4 * Theme.Unit;
        draw.AddCircleFilled(pos + new Vector2(Theme.Unit, CrucibleChrome.LineHeight() / 2f), Theme.DotRadius - 1, Theme.Pack(Theme.Accent));
        var height = CrucibleChrome.DrawWrapped(draw, pos + new Vector2(slot, 0), text, width - slot, Theme.TextPrimary);
        CrucibleChrome.Advance(pos, width, height, Theme.Unit);
        SourceLine(src, slot);
        ImGui.Dummy(new Vector2(0, Theme.Unit));
    }

    private static string ResistItems(CrucibleThreat threats)
    {
        var parts = new List<string>();
        foreach (var status in RunContext.Each((CrucibleStatus)0x3F))
        {
            if ((threats & RunContext.ThreatOf(status)) == 0)
                continue;
            var names = CrucibleItems.All
                .Where(i => i.IsDefined && i.Role != ItemRole.Feral && ((i.Resists & status) != 0 || (i.Cures & status) != 0))
                .Select(i => i.Type switch
                {
                    CrucibleItemType.Gear => $"{i.Name} (gear)",
                    CrucibleItemType.Feed => $"{i.Name} (feed)",
                    _ => i.Name,
                })
                .ToList();
            if (names.Count > 0)
                parts.Add($"{RunContext.StatusName(status)}: {string.Join(", ", names)}");
        }
        return parts.Count == 0 ? "" : "Resist or cure — " + string.Join("; ", parts);
    }

    private static void DrawBrowse(CrucibleShell shell)
    {
        CruciblePages.Columns("browse",
            () => CrucibleCard.Draw("browse_boards", 0, () =>
            {
                CruciblePages.Section("Boards");
                for (var b = 1; b <= 5; b++)
                {
                    var board = BST_CrucibleData.Boards[b - 1];
                    if (CrucibleChrome.ListRow("browse_board_" + b, $"{b} · {board.Name}", $"L{board.Level} · beast rank {board.BeastRank}", null, shell.GuideBoard == b))
                    {
                        shell.GuideBoard = b;
                        shell.GuideBattle = 1;
                        shell.GuideFollow = false;
                    }
                }
                CruciblePages.Paragraph("Choose a fight to open its horn picks, calls, mechanics and sources.");
            }, fill: true),
            () => CrucibleCard.Draw("browse_fights", 0, () =>
            {
                CruciblePages.Section(BST_CrucibleData.Boards[shell.GuideBoard - 1].Name, "Real guide entries");
                var fights = Plugin.Guide.File.Fights.Where(f => f.Board == shell.GuideBoard).OrderBy(f => f.Battle == 0).ThenBy(f => f.Battle).ToList();
                foreach (var fight in fights)
                {
                    var battleLabel = fight.Battle == 0 ? "Boss" : $"Battle {fight.Battle}";
                    if (CrucibleChrome.ListRow($"browse_fight_{fight.Board}_{fight.Battle}", fight.Title, null, battleLabel, shell.GuideBattle == fight.Battle))
                    {
                        shell.GuideBattle = fight.Battle;
                        shell.GuideFollow = false;
                        shell.CurrentPage = CrucibleShell.UpcomingPage;
                    }
                }
            }, fill: true), breakpoint: 760, railFraction: 0.62f);
    }

    private static void DrawAdvisor(CrucibleShell shell)
    {
        if (shell.AdvisorBoard == 0)
            shell.AdvisorBoard = CruciblePages.ActiveBoard() is var active && active != 0 ? active : 1;
        var board = BST_CrucibleData.Boards[shell.AdvisorBoard - 1];
        var roster = BST_CrucibleNeedFirst.BoardRoster(shell.AdvisorBoard, CrucibleGame.BeastCaptured);
        if (!CrucibleGame.RosterLoaded)
            CrucibleChrome.Notice("Needs setup — Missing Bestiary. The captured familiar list has not arrived yet (open the Master's Bestiary once); picks assume every familiar is captured.", UiState.Missing);

        CrucibleCard.Draw("advisor_board", 0, () =>
        {
            CruciblePages.Section(board.Name, $"L{board.Level} · beast rank {board.BeastRank}");
            var draw = ImGui.GetWindowDrawList();
            var available = CrucibleChrome.Avail();
            var used = 0f;
            for (var b = 1; b <= 5; b++)
            {
                var label = $"Board {b}";
                var pillWidth = CrucibleChrome.TextWidth(label) + (4 * Theme.Unit * 2);
                if (b > 1)
                {
                    if (used + (2 * Theme.Unit) + pillWidth <= available)
                    {
                        ImGui.SameLine(0, 2 * Theme.Unit);
                        used += 2 * Theme.Unit;
                    }
                    else
                        used = 0;
                }
                var pos = ImGui.GetCursorScreenPos();
                var size = new Vector2(pillWidth, 32);
                if (CrucibleChrome.OutlineButton(draw, $"advisor_board_{b}", label, pos, size, shell.AdvisorBoard == b))
                    shell.AdvisorBoard = b;
                ImGui.SetCursorScreenPos(pos);
                ImGui.Dummy(size);
                used += pillWidth;
            }
            ImGui.Dummy(new Vector2(0, Theme.Unit));
            CruciblePages.Paragraph("Run roster: familiars in the most battles' picks, with the number of battles", gap: Theme.Unit * 2);
            CrucibleChrome.Chips(roster.Select(r => $"{CruciblePages.BeastName(r.Row)} ({r.Battles})"));
        });

        var battles = BST_CrucibleData.Battles.Where(b => b.Board == shell.AdvisorBoard).ToList();
        if (!battles.Any(b => b.Battle == shell.AdvisorBattle))
            shell.AdvisorBattle = battles.First().Battle;
        CruciblePages.Columns("advisor",
            () => CrucibleCard.Draw("advisor_battles", 0, () =>
            {
                CruciblePages.Section("Battles", "Expand a battle for its ranked picks and capture advice");
                var here = BST_CrucibleData.BoardOfTerritory(ECommons.DalamudServices.Svc.ClientState.TerritoryType);
                var current = here == shell.AdvisorBoard ? CrucibleGame.CurrentBattle() : -1;
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Unit, 2 * Theme.Unit));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, CrucibleChrome.Radius);
                ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent with { W = 0.20f });
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.14f });
                ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Accent with { W = 0.28f });
                foreach (var battle in battles)
                {
                    var label = BST_CrucibleData.BattleLabel(battle.Board, battle.Battle);
                    var tag = battle.Role switch { CrucibleRole.Boss => "boss", CrucibleRole.EliteEnemy => "elite", _ => "enemy" };
                    var isCurrent = battle.Battle == current;
                    var header = $"{label} ({tag}{(battle.RandomOnly ? ", random space" : "")}){(isCurrent ? " · current" : "")}##lc{battle.Board}_{battle.Battle}";
                    if (isCurrent)
                        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent);
                    var open = ImGui.TreeNode(header);
                    if (isCurrent)
                        ImGui.PopStyleColor();
                    if (!open)
                        continue;
                    if (CrucibleChrome.ListRow($"advisor_select_{battle.Board}_{battle.Battle}", "Show in the loadout rail", null, null, shell.AdvisorBattle == battle.Battle))
                        shell.AdvisorBattle = battle.Battle;
                    ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
                    var weaknesses = BST_CrucibleData.Enemies.Where(e => e.Board == battle.Board && e.Battle == battle.Battle)
                        .Select(e => $"{e.Name}: {e.Weakness}");
                    Widgets.SecondaryWrapped(string.Join("; ", weaknesses));
                    Widgets.SecondaryWrapped($"Enemy panel calls for: {BST_CrucibleData.BattleNeeds(battle.Board, battle.Battle)}");
                    var selection = BST_CrucibleNeedFirst.SelectCaptured(battle.Board, battle.Battle, CrucibleGame.BeastCaptured);
                    foreach (var pick in selection.Picks)
                    {
                        ImGui.Bullet();
                        ImGui.SameLine();
                        ImGui.TextWrapped($"{CruciblePages.BeastName(pick.Row)}: {pick.Why}");
                    }
                    var capture = BST_CrucibleNeedFirst.WorthCapturing(battle.Board, battle.Battle, CrucibleGame.BeastCaptured, selection);
                    if (CrucibleGame.RosterLoaded && capture.Count > 0)
                        ImGui.TextWrapped("Worth capturing: " + string.Join(", ", capture.Select(c => $"{CruciblePages.BeastName(c.Row)} (L{BST_Beasts.All[c.Row].CaptureLevel}: {c.Why})")));
                    ImGui.PopStyleColor();
                    ImGui.TreePop();
                }
                ImGui.PopStyleColor(3);
                ImGui.PopStyleVar(2);
            }, fill: true),
            () =>
            {
                var selection = BST_CrucibleNeedFirst.SelectCaptured(shell.AdvisorBoard, shell.AdvisorBattle, CrucibleGame.BeastCaptured);
                CrucibleCard.Draw("advisor_loadout", 0, () =>
                {
                    CruciblePages.Section("Selected battlehorn loadout", BST_CrucibleData.BattleLabel(shell.AdvisorBoard, shell.AdvisorBattle));
                    CruciblePages.Paragraph("Enemy panel calls for: " + BST_CrucibleData.BattleNeeds(shell.AdvisorBoard, shell.AdvisorBattle));
                    CruciblePages.HornLines(selection.Picks.Select(pick => (CruciblePages.BeastName(pick.Row), pick.Why)), Theme.Accent);
                });
                CrucibleCard.Draw("advisor_capture", 0, () =>
                {
                    CruciblePages.Section("Capture advice");
                    var capture = BST_CrucibleNeedFirst.WorthCapturing(shell.AdvisorBoard, shell.AdvisorBattle, CrucibleGame.BeastCaptured, selection);
                    var text = CrucibleGame.RosterLoaded && capture.Count > 0
                        ? "Worth capturing: " + string.Join(", ", capture.Select(c => $"{CruciblePages.BeastName(c.Row)} (L{BST_Beasts.All[c.Row].CaptureLevel}: {c.Why})"))
                        : CrucibleGame.RosterLoaded ? "No additional familiar is needed for this battle." : "Open the Master's Bestiary once for personalized capture advice.";
                    CruciblePages.Paragraph(text, gap: 0);
                }, fill: true);
            }, railFraction: 0.36f);
    }

    private static void DrawRecent()
    {
        if (SelectionScreens.Recent.Count == 0)
        {
            CrucibleChrome.Notice("No selection choices recorded this session.", UiState.Empty);
            return;
        }
        var entries = SelectionScreens.Recent.AsEnumerable().Reverse().Take(30).ToList();
        CrucibleCard.Draw("recent_choices", 0, () =>
        {
            CruciblePages.Section("Decisions", null, "Newest 30");
            var draw = ImGui.GetWindowDrawList();
            var width = CrucibleChrome.Avail();
            var timeWidth = CrucibleChrome.TextWidth("00:00:00", Theme.SecondaryScale);
            var screenWidth = entries.Max(e => CrucibleChrome.TextWidth(e.Screen, Theme.SecondaryScale));
            var gap = CrucibleChrome.Gap;
            var textX = timeWidth + gap + screenWidth + gap;
            var wide = width - textX >= 200;
            var first = true;
            foreach (var (at, screen, text) in entries)
            {
                var pos = ImGui.GetCursorScreenPos();
                const float padY = 2 * Theme.Unit;
                if (!first)
                    draw.AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder with { W = 0.6f }));
                first = false;
                var meta = CrucibleChrome.LineHeight(Theme.SecondaryScale);
                float height;
                if (wide)
                {
                    var textHeight = CrucibleChrome.WrappedHeight(text, width - textX);
                    var row = MathF.Max(textHeight, CrucibleChrome.LineHeight());
                    CrucibleChrome.DrawText(draw, pos + new Vector2(0, padY + ((CrucibleChrome.LineHeight() - meta) / 2f)), $"{at:HH:mm:ss}", Theme.TextSecondary, Theme.SecondaryScale);
                    CrucibleChrome.DrawText(draw, pos + new Vector2(timeWidth + gap, padY + ((CrucibleChrome.LineHeight() - meta) / 2f)), screen, Theme.TextSecondary, Theme.SecondaryScale);
                    CrucibleChrome.DrawWrapped(draw, pos + new Vector2(textX, padY), text, width - textX, Theme.TextPrimary);
                    height = row + (2 * padY);
                }
                else
                {
                    CrucibleChrome.DrawText(draw, pos + new Vector2(0, padY), $"{at:HH:mm:ss}  {screen}", Theme.TextSecondary, Theme.SecondaryScale);
                    var textHeight = CrucibleChrome.DrawWrapped(draw, pos + new Vector2(0, padY + meta + Theme.Unit), text, width, Theme.TextPrimary);
                    height = padY + meta + Theme.Unit + textHeight + padY;
                }
                CrucibleChrome.Advance(pos, width, height, 0);
            }
        }, fill: true);
    }
}
