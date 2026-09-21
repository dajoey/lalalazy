using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;

namespace LazyCrucible;

/// <summary> Settings, what the familiar selection last did, and the beast-pick advisor for every board. </summary>
internal sealed class MainWindow : Window
{
    private readonly Plugin _plugin;
    private int _advisorBoard;

    public MainWindow(Plugin plugin) : base("LazyCrucible###LazyCrucibleMain")
    {
        _plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 300), MaximumSize = new Vector2(1400, 1400) };
    }

    public override void Draw()
    {
        var cfg = Plugin.Config;

        if (PetSelect.YieldReason == "conflict_gluttony")
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.3f, 1f),
                "An older GluttonyCombo that also fills familiars is loaded. LazyCrucible is not writing until it is updated.");
        else if (PetSelect.YieldReason == "autoduty_running")
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.4f, 1f),
                "AutoDuty is running this board and picks its own familiars; LazyCrucible is leaving the familiar screens alone.");

        ImGui.TextWrapped(PetSelect.LastSummary.Length == 0
            ? "No familiar selection yet this session."
            : $"{PetSelect.LastSummaryAt:HH:mm:ss}  {PetSelect.LastSummary}");
        ImGui.Separator();

        var changed = false;
        var roster = cfg.AutoRoster;
        if (ImGui.Checkbox("Fill the run roster at the Bentbranch Meadows entry menu", ref roster))
        {
            cfg.AutoRoster = roster;
            changed = true;
        }
        Help("Once per visit to the entry menu, sets the ten familiars that cover the board's battles best. A roster changed by hand is left alone until the next run.");

        var horns = cfg.AutoHorns;
        if (ImGui.Checkbox("Fill the Battlehorn slots before each fight", ref horns))
        {
            cfg.AutoHorns = horns;
            changed = true;
        }
        Help("On the formation screen before every fight, puts the three familiars that answer that fight's mechanics on the horns: elemental weakness, interrupts (Soul Crush), dispels (Quelling Wave), cleanses and crowd control the enemies are vulnerable to. Knocked-out familiars are never picked; badly hurt ones lose to a healthy one that fits nearly as well. Never starts the fight, never touches the shop's feeding screen, and stops for that screen as soon as a slot is changed by hand.");

        var yieldAd = cfg.YieldToAutoDuty;
        if (ImGui.Checkbox("Stand down while AutoDuty is running", ref yieldAd))
        {
            cfg.YieldToAutoDuty = yieldAd;
            changed = true;
        }
        Help("AutoDuty can run whole Crucible boards and fills the roster and horns with its own team (for example a leveling team). While it is running, LazyCrucible does not touch the familiar screens, so the two never overwrite each other.");

        var announce = cfg.AnnouncePicks;
        if (ImGui.Checkbox("Announce picks in chat", ref announce))
        {
            cfg.AnnouncePicks = announce;
            changed = true;
        }
        Help("One chat line per fight naming each familiar and why it was picked.");

        var record = cfg.RecordScreens;
        if (ImGui.Checkbox("Record Crucible screens to the log", ref record))
        {
            cfg.RecordScreens = record;
            changed = true;
        }
        Help("Read-only. Writes every Crucible screen (path choice, spoils, treasure, shop, feeding, campsite, beast gear) and the buttons pressed on it to the Dalamud log, so those screens can be automated later. Never presses anything.");

        if (changed)
            cfg.Save();

        ImGui.Spacing();
        DrawAdvisor();
    }

    private static void Help(string text)
    {
        ImGui.Indent();
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
        ImGui.Unindent();
    }

    /// <summary> Beast picks per battle for a chosen board, from the captured roster (usable before reaching the board). </summary>
    private void DrawAdvisor()
    {
        if (!ImGui.CollapsingHeader("Beast picks by battle"))
            return;

        var here = BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType);
        if (_advisorBoard == 0)
            _advisorBoard = here != 0 ? here : 1;

        ImGui.TextUnformatted("Board");
        for (var b = 1; b <= 5; b++)
        {
            ImGui.SameLine();
            ImGui.RadioButton($"{b}##lcBoard", ref _advisorBoard, b);
        }

        var board = BST_CrucibleData.Boards[_advisorBoard - 1];
        ImGui.TextWrapped($"{board.Name}: L{board.Level}{(board.ItemLevel > 0 ? $" / iL{board.ItemLevel}" : "")}, beast rank {board.BeastRank}");
        if (!CrucibleGame.RosterLoaded)
            ImGui.TextWrapped("The captured familiar list has not arrived yet (open the Master's Bestiary once); picks assume every familiar is captured.");

        var roster = BST_CrucibleAdvisor.BoardRoster(board.Board, CrucibleGame.BeastCaptured);
        ImGui.TextWrapped("Roster (familiars in the most battles' picks, number of battles): "
                          + string.Join(", ", roster.ConvertAll(r => $"{BeastName(r.Row)} ({r.Battles})")));

        var current = here == board.Board ? CrucibleGame.CurrentBattle() : -1;
        foreach (var battle in BST_CrucibleData.Battles)
        {
            if (battle.Board != board.Board)
                continue;

            var label = BST_CrucibleData.BattleLabel(battle.Board, battle.Battle);
            var tag = battle.Role switch
            {
                CrucibleRole.Boss => "boss",
                CrucibleRole.EliteEnemy => "elite",
                _ => "enemy",
            };
            var header = $"{(battle.Battle == current ? "> " : "")}{label} ({tag}{(battle.RandomOnly ? ", random space" : "")})##lc{battle.Board}_{battle.Battle}";
            if (!ImGui.TreeNode(header))
                continue;

            var weaknesses = new List<string>();
            foreach (var e in BST_CrucibleData.Enemies)
                if (e.Board == battle.Board && e.Battle == battle.Battle)
                    weaknesses.Add($"{e.Name}: {e.Weakness}");
            ImGui.TextWrapped(string.Join("; ", weaknesses));
            ImGui.TextWrapped($"Enemy panel calls for: {BST_CrucibleData.BattleNeeds(battle.Board, battle.Battle)}");

            var picks = BST_CrucibleAdvisor.Pick(battle.Board, battle.Battle, CrucibleGame.BeastCaptured);
            foreach (var pick in picks)
                ImGui.BulletText($"{BeastName(pick.Row)}: {pick.Why}");

            var capture = BST_CrucibleAdvisor.WorthCapturing(battle.Board, battle.Battle, CrucibleGame.BeastCaptured, picks);
            if (CrucibleGame.RosterLoaded && capture.Count > 0)
                ImGui.TextWrapped("Worth capturing: " + string.Join(", ",
                    capture.ConvertAll(c => $"{BeastName(c.Row)} (L{BST_Beasts.All[c.Row].CaptureLevel}: {c.Why})")));

            ImGui.TreePop();
        }
    }

    private static string BeastName(int row)
    {
        var name = BST_Beasts.All[row].Name;
        return name.Length == 0 ? "?" : char.ToUpperInvariant(name[0]) + name[1..];
    }
}
