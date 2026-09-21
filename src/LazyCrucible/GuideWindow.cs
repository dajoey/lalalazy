using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Lalalazy.Crucible;

namespace LazyCrucible;

/// <summary>
///     /lazycrucible guide — the fight repository. Follows the upcoming fight (the one the board layout or the
///     Battlehorn screen is focused on, the same identification the horn picks use) or browses any board. For each
///     fight: where it is, the horn picks with their reasons (the live ranking, so the guide and the picks agree), what
///     the enemy panel calls for and whether the picks cover it, the dangerous hits, the mechanics (tell → what to do),
///     kill order, what to bring (the guide's advice plus the items and feed whose tooltips resist what the fight
///     inflicts), and what no source covers. Source tags sit at the end of each line.
/// </summary>
internal sealed class GuideWindow : Window
{
    private static readonly Vector4 Danger = new(1f, 0.45f, 0.35f, 1f);
    private static readonly Vector4 Good = new(0.55f, 0.9f, 0.55f, 1f);
    private static readonly Vector4 Warn = new(1f, 0.8f, 0.35f, 1f);

    private int _board = 1;
    private int _battle = 1;
    private bool _follow = true;
    private (int, int) _lastFocus = (0, -1);

    public GuideWindow() : base("Crucible fight guide###LazyCrucibleGuide")
    {
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(380, 260), MaximumSize = new Vector2(1400, 1600) };
        Size = new Vector2(520, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary> Called every frame: open on a newly focused fight when the setting says so. </summary>
    public void FollowFocus()
    {
        var focus = RunTracker.Focus;
        if (focus.Battle < 0 || focus == _lastFocus)
            return;
        _lastFocus = focus;
        if (_follow)
        {
            _board = focus.Board;
            _battle = focus.Battle;
        }
        if (Plugin.Config.GuideAutoOpen)
            IsOpen = true;
    }

    public override void Draw()
    {
        ImGui.Checkbox("Follow the next fight", ref _follow);
        ImGui.SameLine();
        ImGui.TextDisabled(RunTracker.Focus.Battle >= 0
            ? $"(next: {BST_CrucibleData.BattleLabel(RunTracker.Focus.Board, RunTracker.Focus.Battle)})"
            : "(no fight in focus)");

        ImGui.TextUnformatted("Board");
        for (var b = 1; b <= 5; b++)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{b}##gb", _board == b))
            {
                _board = b;
                _battle = 1;
                _follow = false;
            }
        }
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##gbattle", Label(_board, _battle)))
        {
            foreach (var bt in BST_CrucibleData.Battles.Where(x => x.Board == _board).OrderBy(x => x.Battle == 0).ThenBy(x => x.Battle))
                if (ImGui.Selectable(Label(_board, bt.Battle), bt.Battle == _battle))
                {
                    _battle = bt.Battle;
                    _follow = false;
                }
            ImGui.EndCombo();
        }
        ImGui.Separator();

        var guide = Plugin.Guide;
        var fight = guide.Fight(_board, _battle);
        if (fight is null)
        {
            ImGui.TextWrapped("No guide entry for this fight.");
            return;
        }

        ImGui.BeginChild("##guidebody", new Vector2(0, 0), false);
        DrawFight(fight);
        DrawRecent();
        ImGui.EndChild();
    }

    private static string Label(int board, int battle)
    {
        var info = BST_CrucibleData.Battles.FirstOrDefault(x => x.Board == board && x.Battle == battle);
        var role = info.Role switch { CrucibleRole.Boss => "boss", CrucibleRole.EliteEnemy => "elite", _ => "enemy" };
        var title = Plugin.Guide.Fight(board, battle)?.Title ?? BST_CrucibleData.BattleLabel(board, battle);
        return $"{title} ({role}{(info.RandomOnly ? ", random" : "")})";
    }

    private static void Src(List<string> src)
    {
        if (src.Count == 0)
            return;
        ImGui.SameLine();
        ImGui.TextDisabled($"[{string.Join(" ", src)}]");
    }

    private static void Heading(string text)
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.7f, 0.8f, 1f, 1f), text);
    }

    private void DrawFight(GuideFight f)
    {
        ImGui.TextUnformatted(f.Title);
        ImGui.SameLine();
        ImGui.TextDisabled(CrucibleBoards.Where(f.Board, f.Battle));
        if (f.Summary.Length > 0)
            ImGui.TextWrapped(f.Summary);

        var weak = BST_CrucibleData.Enemies.Where(e => e.Board == f.Board && e.Battle == f.Battle)
            .Select(e => $"{e.Name}: {(e.Weakness == CrucibleWeakness.None ? "no weakness" : e.Weakness.ToString().ToLowerInvariant())}");
        ImGui.TextDisabled("Weak to  " + string.Join("; ", weak));

        // Horn picks: the live ranking (run roster and HP on this board, else every captured familiar).
        Heading("Horn picks");
        List<CrucibleBeastPick> picks;
        if (RunTracker.Board == f.Board && RunTracker.Roster.Count > 0)
            picks = BST_CrucibleAdvisor.PickSlots(f.Board, f.Battle, RunTracker.Roster, RunTracker.RosterHp, 3);
        else
            picks = BST_CrucibleAdvisor.Pick(f.Board, f.Battle, CrucibleGame.BeastCaptured);
        foreach (var p in picks)
        {
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextUnformatted(FamiliarState.BeastName(p.Row));
            ImGui.SameLine();
            ImGui.TextDisabled(p.Why);
        }

        if (f.Counters.Count > 0)
        {
            Heading("Calls for");
            var covered = picks.Aggregate(CrucibleNeeds.None, (acc, p) => acc | BST_CrucibleAdvisor.Answers(p.Row));
            foreach (var c in f.Counters)
            {
                var tool = c.AsNeed switch
                {
                    CrucibleNeeds.Interrupt => "interrupt (Soul Crush)",
                    CrucibleNeeds.Dispel => "dispel (Quelling Wave)",
                    CrucibleNeeds.Cleanse => "cleanse (Scouring Ash)",
                    _ => c.Need,
                };
                var ok = (covered & c.AsNeed) != 0;
                ImGui.TextColored(ok ? Good : Warn, ok ? "✓" : "!");
                ImGui.SameLine();
                ImGui.TextWrapped($"{tool}: {c.What}{(ok ? "" : " — no horn pick brings it")}");
                Src(c.Src);
            }
        }

        if (f.Hits.Count > 0)
        {
            Heading("Dangerous hits");
            foreach (var h in f.Hits)
                Mechanic(h, Danger);
        }

        if (f.Mechanics.Count > 0)
        {
            Heading("Mechanics");
            foreach (var m in f.Mechanics)
                Mechanic(m, null);
        }

        if (f.KillOrder is { } k)
        {
            Heading("Kill order");
            ImGui.TextWrapped(k.Text);
            Src(k.Src);
        }

        Heading("Bring");
        foreach (var b in f.Bring)
        {
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextWrapped(b.Text);
            Src(b.Src);
        }
        var resists = ResistItems(f.ThreatFlags);
        if (resists.Length > 0)
        {
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextWrapped(resists);
            Src(["sheet"]);
        }
        if (f.Threats.Count > 0)
            ImGui.TextDisabled("Threats  " + string.Join(", ", f.Threats.Select(CrucibleGuide.ThreatLabel)));

        if (f.Unknown.Count > 0)
        {
            Heading("Not covered by any source");
            foreach (var u in f.Unknown)
                ImGui.TextDisabled("· " + u);
        }
    }

    private static void Mechanic(GuideMechanic m, Vector4? colour)
    {
        var name = m.Cast is { } c ? $"{m.Name} ({c:0.#} s)" : m.Name;
        if (colour is { } col)
            ImGui.TextColored(col, name);
        else
            ImGui.TextUnformatted(name);
        Src(m.Src);
        ImGui.Indent();
        if (m.Tell.Length > 0)
            ImGui.TextWrapped("Tell: " + m.Tell);
        if (m.Do.Length > 0)
            ImGui.TextWrapped("Do: " + m.Do);
        ImGui.Unindent();
    }

    /// <summary> "Poison: Angel Robe (gear), Porcini Simular (feed), G2 Antipoison Soul Serum, Crucible Antidote" per status the fight inflicts. </summary>
    private static string ResistItems(CrucibleThreat threats)
    {
        var parts = new List<string>();
        foreach (var s in RunContext.Each((CrucibleStatus)0x3F))
        {
            if ((threats & RunContext.ThreatOf(s)) == 0)
                continue;
            var names = CrucibleItems.All
                .Where(i => i.IsDefined && i.Role != ItemRole.Feral && ((i.Resists & s) != 0 || (i.Cures & s) != 0))
                .Select(i => i.Type switch
                {
                    CrucibleItemType.Gear => $"{i.Name} (gear)",
                    CrucibleItemType.Feed => $"{i.Name} (feed)",
                    _ => i.Name,
                })
                .ToList();
            if (names.Count > 0)
                parts.Add($"{RunContext.StatusName(s)}: {string.Join(", ", names)}");
        }
        return parts.Count == 0 ? "" : "Resist or cure — " + string.Join("; ", parts);
    }

    private static void DrawRecent()
    {
        if (SelectionScreens.Recent.Count == 0)
            return;
        ImGui.Spacing();
        if (!ImGui.CollapsingHeader("What LazyCrucible chose this session"))
            return;
        foreach (var (at, screen, text) in SelectionScreens.Recent.AsEnumerable().Reverse().Take(30))
        {
            ImGui.TextDisabled($"{at:HH:mm:ss} {screen}");
            ImGui.SameLine();
            ImGui.TextWrapped(text);
        }
    }
}
