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

    private static string Label(int board, int battle)
    {
        var info = BST_CrucibleData.Battles.FirstOrDefault(x => x.Board == board && x.Battle == battle);
        var role = info.Role switch { CrucibleRole.Boss => "boss", CrucibleRole.EliteEnemy => "elite", _ => "enemy" };
        var title = Plugin.Guide.Fight(board, battle)?.Title ?? BST_CrucibleData.BattleLabel(board, battle);
        return $"{title} ({role}{(info.RandomOnly ? ", random" : "")})";
    }

    private static void DrawUpcoming(CrucibleShell shell)
    {
        if (shell.GuideFollow && RunTracker.Focus.Battle < 0)
        {
            CruciblePages.TwoColumns(
                () => Widgets.InfoBox("Idle — no fight is in focus. Follow the next fight or browse a real guide entry.", UiState.Idle),
                () => DrawFightNavigation(shell));
            return;
        }
        var fight = Plugin.Guide.Fight(shell.GuideBoard, shell.GuideBattle);
        if (fight is null)
        {
            Widgets.InfoBox("No guide entry for this fight. Browse the real board entries or choose another focused fight; no substitute content is invented.", UiState.Empty);
            if (ImGui.Button("Browse boards"))
                shell.CurrentPage = "guide.browse";
            return;
        }
        CruciblePages.TwoColumns(
            () => DrawFight(fight),
            () =>
            {
                DrawFightNavigation(shell);
                DrawBringAndFacts(fight);
            });
    }

    private static void DrawFightNavigation(CrucibleShell shell)
    {
        CrucibleCard.Draw("fight_navigation", 0, () =>
        {
            CruciblePages.Section("Fight navigation");
            CrucibleChrome.Toggle("Follow the next fight", ref shell.GuideFollow, null);
            Widgets.Caption(RunTracker.Focus.Battle >= 0
                ? $"next: {BST_CrucibleData.BattleLabel(RunTracker.Focus.Board, RunTracker.Focus.Battle)}"
                : "no fight in focus");
            Widgets.Secondary("Board");
            var boardButtonWidth = MathF.Min(46, (ImGui.GetContentRegionAvail().X - (4 * 2 * Theme.Unit)) / 5);
            for (var b = 1; b <= 5; b++)
            {
                if (b > 1)
                    ImGui.SameLine();
                if (CrucibleChrome.OutlineButton(ImGui.GetWindowDrawList(), $"guide_board_{b}", b.ToString(),
                        ImGui.GetCursorScreenPos(), new Vector2(boardButtonWidth, 28), shell.GuideBoard == b))
                {
                    shell.GuideBoard = b;
                    shell.GuideBattle = 1;
                    shell.GuideFollow = false;
                }
            }
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##guideBattle", Label(shell.GuideBoard, shell.GuideBattle)))
            {
                foreach (var battle in BST_CrucibleData.Battles.Where(x => x.Board == shell.GuideBoard).OrderBy(x => x.Battle == 0).ThenBy(x => x.Battle))
                    if (ImGui.Selectable(Label(shell.GuideBoard, battle.Battle), battle.Battle == shell.GuideBattle))
                    {
                        shell.GuideBattle = battle.Battle;
                        shell.GuideFollow = false;
                    }
                ImGui.EndCombo();
            }
        });
    }

    private static void DrawFight(GuideFight fight)
    {
        var selection = CruciblePages.Selection(fight.Board, fight.Battle);
        CrucibleCard.TwoCards(0.60f,
            () =>
            {
                CruciblePages.Section("Horn picks", selection.Picks.Count + " ranked picks");
                if (!CrucibleGame.RosterLoaded)
                    Widgets.SecondaryWrapped("Captured list unavailable; ranking assumes every familiar is captured.");
                foreach (var pick in selection.Picks)
                {
                    var start = ImGui.GetCursorPos();
                    var available = ImGui.GetContentRegionAvail().X;
                    var point = ImGui.GetCursorScreenPos();
                    ImGui.GetWindowDrawList().AddCircleFilled(point + new Vector2(4, 8), 4, Theme.Pack(Theme.Accent));
                    var labelX = start.X + 16;
                    ImGui.SetCursorPosX(labelX);
                    Widgets.Body(CruciblePages.BeastName(pick.Row));
                    var nameBottom = ImGui.GetCursorPosY();
                    if (available >= 420)
                    {
                        ImGui.SetCursorPos(new Vector2(start.X + 180, start.Y));
                        ImGui.PushTextWrapPos(start.X + available);
                        Widgets.SecondaryWrapped(pick.Why);
                        ImGui.PopTextWrapPos();
                        ImGui.SetCursorPos(new Vector2(start.X, MathF.Max(nameBottom, ImGui.GetCursorPosY())));
                    }
                    else
                    {
                        ImGui.SetCursorPosX(labelX);
                        Widgets.SecondaryWrapped(pick.Why);
                        ImGui.SetCursorPosX(start.X);
                    }
                }
            },
            () => DrawCalls(fight, selection));

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
                MechanicCard(mechanic, false);
            foreach (var hit in fight.Hits)
                MechanicCard(hit, true);
            if (fight.KillOrder is { } kill)
                CrucibleCard.Row("kill_order", () =>
                {
                    Widgets.Caption("Kill order");
                    ImGui.TextWrapped(kill.Text);
                    Src(kill.Src);
                });
        });
    }

    private static void DrawCalls(GuideFight fight, CrucibleSelection selection)
    {
        var rows = GuideNeeds.Rows(selection);
        var mitigation = GuideNeeds.MitigationRows(selection);
        var required = UiPresentationLogic.RequiredCallStats(rows);
        CruciblePages.Section("Calls for", required.Total + " required");
        foreach (var row in rows)
        {
            var state = row.Covered ? UiState.Ready : row.Warn ? UiState.Blocked : UiState.Idle;
            var detail = row.Covered
                ? $"{row.Tool} — {CruciblePages.BeastName(row.CoveredBy)}"
                : row.WhyNot ?? "No horn pick answers this call";
            CallLine(row.Covered ? "✓" : row.Warn ? "!" : "·", row.What + UiPresentationLogic.TierSuffix(row.Tier), detail,
                row.Covered ? "Ready" : row.Warn ? "Needed" : "Useful", state);
            Src(row.Src.ToList());
        }
        foreach (var row in mitigation)
        {
            Widgets.StateText(row.CoveredBy != 0 ? "✓" : "·", row.CoveredBy != 0 ? UiState.Ready : UiState.Idle);
            ImGui.SameLine();
            var by = row.CoveredBy != 0 ? $" — {CruciblePages.BeastName(row.CoveredBy)} ({row.Skill})" : " — no pick answers it";
            ImGui.TextWrapped($"{row.Tool}: {row.What}{by}");
            Src(row.Src.ToList());
        }
        foreach (var mechanic in fight.Mechanics.Where(m => m.Tell.Contains("never interruptible", StringComparison.OrdinalIgnoreCase)))
            CallLine("•", mechanic.Name, mechanic.Do, "Guide call", UiState.Blocked);
        if (rows.Count == 0 && mitigation.Count == 0)
            Widgets.Secondary("No required horn calls for this fight.");
    }

    private static void CallLine(string mark, string main, string detail, string stateLabel, UiState state)
    {
        var start = ImGui.GetCursorPos();
        var width = ImGui.GetContentRegionAvail().X;
        var stateWidth = ImGui.CalcTextSize(stateLabel).X;
        Widgets.StateText(mark, state);
        ImGui.SameLine();
        if (width >= 300)
        {
            ImGui.PushTextWrapPos(start.X + width - stateWidth - 12);
            ImGui.TextWrapped(main);
            ImGui.PopTextWrapPos();
            var mainBottom = ImGui.GetCursorPosY();
            ImGui.SetCursorPos(new Vector2(start.X + width - stateWidth, start.Y));
            ImGui.GetWindowDrawList().AddText(ImGui.GetCursorScreenPos(), Theme.Pack(Copy.MarkColor(state)), stateLabel);
            ImGui.SetCursorPos(new Vector2(start.X + 20, MathF.Max(mainBottom, start.Y + ImGui.GetTextLineHeight() + Theme.ItemSpacing.Y)));
        }
        else
        {
            ImGui.TextWrapped(main);
            Widgets.StateText(stateLabel, state);
            ImGui.SetCursorPosX(start.X + 20);
        }
        if (detail.Length > 0)
            Widgets.SecondaryWrapped(detail);
    }

    private static void MechanicCard(GuideMechanic mechanic, bool danger)
    {
        CrucibleCard.Row("mechanic_" + mechanic.Name, () =>
        {
            var start = ImGui.GetCursorPos();
            var width = ImGui.GetContentRegionAvail().X;
            var wide = width >= 560 && ImGui.CalcTextSize(mechanic.Name).X < width - 260;
            var showBadge = wide && mechanic.Cast.HasValue && ImGui.CalcTextSize(mechanic.Name).X <= 120;
            var name = !showBadge && mechanic.Cast is { } cast ? $"{mechanic.Name} ({cast:0.#} s)" : mechanic.Name;
            if (danger)
                Widgets.StateText(name, UiState.Blocked);
            else
                Widgets.Body(name);
            var nameBottom = ImGui.GetCursorPosY();
            if (showBadge && mechanic.Cast is { } seconds)
            {
                var draw = ImGui.GetWindowDrawList();
                var text = $"{seconds:0.#} s";
                var textSize = ImGui.CalcTextSize(text);
                var badgePos = ImGui.GetCursorScreenPos() - new Vector2(0, ImGui.GetTextLineHeightWithSpacing());
                badgePos += new Vector2(146, -2);
                draw.AddRectFilled(badgePos, badgePos + textSize + new Vector2(16, 8), Theme.Pack(Theme.StateAmber with { W = 0.16f }), 8);
                draw.AddText(badgePos + new Vector2(8, 4), Theme.Pack(Theme.StateAmber), text);
            }
            var instructionX = wide ? start.X + MathF.Max(230, ImGui.CalcTextSize(name).X + 24) : start.X;
            if (wide)
                ImGui.SetCursorPos(new Vector2(instructionX, start.Y));
            if (mechanic.Tell.Length > 0)
            {
                Widgets.Caption("Tell");
                ImGui.SameLine();
                ImGui.TextWrapped(mechanic.Tell);
            }
            if (mechanic.Do.Length > 0)
            {
                ImGui.SetCursorPosX(instructionX);
                Widgets.Caption("Do");
                ImGui.SameLine();
                ImGui.TextWrapped(mechanic.Do);
            }
            ImGui.SetCursorPos(new Vector2(start.X, MathF.Max(nameBottom, ImGui.GetCursorPosY())));
            Src(mechanic.Src);
        }, 64);
    }

    private static void Src(List<string> src)
    {
        if (src.Count > 0)
            Widgets.SecondaryWrapped($"[{string.Join(" ", src)}]");
    }

    private static void DrawBringAndFacts(GuideFight fight)
    {
        CrucibleCard.Draw("bring", 0, () =>
        {
            CruciblePages.Section("Bring");
            foreach (var item in fight.Bring)
            {
                ImGui.Bullet();
                ImGui.SameLine();
                ImGui.TextWrapped(item.Text);
                if (item.Src.Count > 0)
                    Widgets.Caption($"[{string.Join(" ", item.Src)}]");
            }
            var resists = ResistItems(fight.ThreatFlags);
            if (resists.Length > 0)
            {
                ImGui.Bullet();
                ImGui.SameLine();
                ImGui.TextWrapped(resists);
                Widgets.Caption("[sheet]");
            }
        });
        CrucibleCard.Draw("fight_facts", 0, () =>
        {
            CruciblePages.Section("Fight facts");
            var weak = BST_CrucibleData.Enemies.Where(e => e.Board == fight.Board && e.Battle == fight.Battle)
                .Select(e => $"{e.Name}: {(e.Weakness == CrucibleWeakness.None ? "no weakness" : e.Weakness.ToString().ToLowerInvariant())}");
            Widgets.SecondaryWrapped("Weak to  " + string.Join("; ", weak));
            if (fight.Threats.Count > 0)
                Widgets.SecondaryWrapped("Threats  " + string.Join(", ", fight.Threats.Select(CrucibleGuide.ThreatLabel)));
            var sources = fight.Hits.Concat(fight.Mechanics).SelectMany(m => m.Src).Concat(fight.Bring.SelectMany(b => b.Src)).ToList();
            if (fight.KillOrder is { } kill)
                sources.AddRange(kill.Src);
            if (sources.Count > 0)
                Widgets.SecondaryWrapped("Sources  " + string.Join(" · ", sources.Distinct().OrderBy(s => s)));
            if (fight.Unknown.Count > 0)
            {
                CruciblePages.Section("Not covered by any source");
                foreach (var unknown in fight.Unknown)
                    Widgets.Caption("· " + unknown);
            }
        });
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
        CruciblePages.TwoColumns(
            () =>
            {
                CruciblePages.Section("Boards");
                for (var b = 1; b <= 5; b++)
                {
                    var board = BST_CrucibleData.Boards[b - 1];
                    if (ImGui.Selectable($"{b} · {board.Name}##browse_board_{b}", shell.GuideBoard == b))
                    {
                        shell.GuideBoard = b;
                        shell.GuideBattle = 1;
                        shell.GuideFollow = false;
                    }
                    Widgets.Caption($"L{board.Level} · beast rank {board.BeastRank}");
                }
                Widgets.SecondaryWrapped("Choose a fight to open its horn picks, calls, mechanics and sources.");
            },
            () =>
            {
                CruciblePages.Section(BST_CrucibleData.Boards[shell.GuideBoard - 1].Name, "Real guide entries");
                var fights = Plugin.Guide.File.Fights.Where(f => f.Board == shell.GuideBoard).OrderBy(f => f.Battle == 0).ThenBy(f => f.Battle).ToList();
                ImGui.BeginChild("##browseFightScroll", new Vector2(0, 320), true);
                foreach (var fight in fights)
                {
                    var battleLabel = fight.Battle == 0 ? "Boss" : $"Battle {fight.Battle}";
                    if (ImGui.Selectable($"{fight.Title} · {battleLabel}##browse_fight_{fight.Board}_{fight.Battle}", shell.GuideBattle == fight.Battle))
                    {
                        shell.GuideBattle = fight.Battle;
                        shell.GuideFollow = false;
                        shell.CurrentPage = CrucibleShell.UpcomingPage;
                    }
                }
                ImGui.EndChild();
            });
    }

    private static void DrawAdvisor(CrucibleShell shell)
    {
        if (shell.AdvisorBoard == 0)
            shell.AdvisorBoard = CruciblePages.ActiveBoard() is var active && active != 0 ? active : 1;
        var board = BST_CrucibleData.Boards[shell.AdvisorBoard - 1];
        CruciblePages.Section("Beast picks by battle", $"{board.Name}: L{board.Level} · beast rank {board.BeastRank}");
        for (var b = 1; b <= 5; b++)
        {
            if (b > 1)
                ImGui.SameLine();
            ImGui.RadioButton($"Board {b}##advisorBoard", ref shell.AdvisorBoard, b);
        }
        var roster = BST_CrucibleNeedFirst.BoardRoster(shell.AdvisorBoard, CrucibleGame.BeastCaptured);
        if (!CrucibleGame.RosterLoaded)
            Widgets.InfoBox("Needs setup — Missing Bestiary. The captured familiar list has not arrived yet (open the Master's Bestiary once); picks assume every familiar is captured.", UiState.Missing);
        CruciblePages.Section("Run roster", roster.Count + " advisor candidates");
        Widgets.SecondaryWrapped("Roster (familiars in the most battles' picks, number of battles): " + string.Join(", ", roster.Select(r => $"{CruciblePages.BeastName(r.Row)} ({r.Battles})")));

        var battles = BST_CrucibleData.Battles.Where(b => b.Board == shell.AdvisorBoard).ToList();
        if (!battles.Any(b => b.Battle == shell.AdvisorBattle))
            shell.AdvisorBattle = battles.First().Battle;
        CruciblePages.TwoColumns(
            () =>
            {
                CruciblePages.Section("Battles", "Expand a battle for its ranked picks and capture advice");
                var here = BST_CrucibleData.BoardOfTerritory(ECommons.DalamudServices.Svc.ClientState.TerritoryType);
                var current = here == shell.AdvisorBoard ? CrucibleGame.CurrentBattle() : -1;
                foreach (var battle in battles)
                {
                    var label = BST_CrucibleData.BattleLabel(battle.Board, battle.Battle);
                    var tag = battle.Role switch { CrucibleRole.Boss => "boss", CrucibleRole.EliteEnemy => "elite", _ => "enemy" };
                    var header = $"{(battle.Battle == current ? "> " : "")}{label} ({tag}{(battle.RandomOnly ? ", random space" : "")})##lc{battle.Board}_{battle.Battle}";
                    if (!ImGui.TreeNode(header))
                        continue;
                    if (ImGui.Selectable("Show in the loadout rail##advisor_select_" + header, shell.AdvisorBattle == battle.Battle))
                        shell.AdvisorBattle = battle.Battle;
                    var weaknesses = BST_CrucibleData.Enemies.Where(e => e.Board == battle.Board && e.Battle == battle.Battle)
                        .Select(e => $"{e.Name}: {e.Weakness}");
                    ImGui.TextWrapped(string.Join("; ", weaknesses));
                    ImGui.TextWrapped($"Enemy panel calls for: {BST_CrucibleData.BattleNeeds(battle.Board, battle.Battle)}");
                    var selection = BST_CrucibleNeedFirst.SelectCaptured(battle.Board, battle.Battle, CrucibleGame.BeastCaptured);
                    foreach (var pick in selection.Picks)
                        ImGui.BulletText($"{CruciblePages.BeastName(pick.Row)}: {pick.Why}");
                    var capture = BST_CrucibleNeedFirst.WorthCapturing(battle.Board, battle.Battle, CrucibleGame.BeastCaptured, selection);
                    if (CrucibleGame.RosterLoaded && capture.Count > 0)
                        ImGui.TextWrapped("Worth capturing: " + string.Join(", ", capture.Select(c => $"{CruciblePages.BeastName(c.Row)} (L{BST_Beasts.All[c.Row].CaptureLevel}: {c.Why})")));
                    ImGui.TreePop();
                }
            },
            () =>
            {
                var selection = BST_CrucibleNeedFirst.SelectCaptured(shell.AdvisorBoard, shell.AdvisorBattle, CrucibleGame.BeastCaptured);
                CruciblePages.Section("Selected battlehorn loadout", BST_CrucibleData.BattleLabel(shell.AdvisorBoard, shell.AdvisorBattle));
                Widgets.SecondaryWrapped("Enemy panel calls for: " + BST_CrucibleData.BattleNeeds(shell.AdvisorBoard, shell.AdvisorBattle));
                foreach (var pick in selection.Picks)
                {
                    ImGui.Bullet();
                    ImGui.SameLine();
                    Widgets.Body(CruciblePages.BeastName(pick.Row));
                    Widgets.SecondaryWrapped(pick.Why);
                }
                ImGui.Separator();
                CruciblePages.Section("Capture advice");
                var capture = BST_CrucibleNeedFirst.WorthCapturing(shell.AdvisorBoard, shell.AdvisorBattle, CrucibleGame.BeastCaptured, selection);
                var text = CrucibleGame.RosterLoaded && capture.Count > 0
                    ? "Worth capturing: " + string.Join(", ", capture.Select(c => $"{CruciblePages.BeastName(c.Row)} (L{BST_Beasts.All[c.Row].CaptureLevel}: {c.Why})"))
                    : CrucibleGame.RosterLoaded ? "No additional familiar is needed for this battle." : "Open the Master's Bestiary once for personalized capture advice.";
                Widgets.SecondaryWrapped(text);
            });
    }

    private static void DrawRecent()
    {
        CruciblePages.Section("What LazyCrucible chose this session", "Newest 30");
        if (SelectionScreens.Recent.Count == 0)
        {
            Widgets.InfoBox("No selection choices recorded this session.", UiState.Empty);
            return;
        }
        foreach (var (at, screen, text) in SelectionScreens.Recent.AsEnumerable().Reverse().Take(30))
        {
            Widgets.Caption($"{at:HH:mm:ss} {screen}");
            ImGui.SameLine();
            ImGui.TextWrapped(text);
        }
    }
}
