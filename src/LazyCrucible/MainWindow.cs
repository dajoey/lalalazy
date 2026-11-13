using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using LalaUiKit;

namespace LazyCrucible;

// Converted to LalaUiKit per Projects/UI Refresh 2026-11/UI Brief (R1-R13).
// The Dalamud Window base auto-begins/ends this window (Draw() draws content only),
// so the top bar is drawn with the same kit primitives Layout.Shell uses, and the
// suite frame comes from Theme.PushFrame/PopFrame. Colours come only from the kit
// (one accent, reserved state colours). No background activity runs between screens,
// so the live state ships in the pill and the footer is omitted (Brief R3).
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

    /// <summary> Live state for the pill (R3 place 1): why writing is held, else ready. </summary>
    private static (UiState State, string? Detail) OverallState()
    {
        if (PetSelect.YieldReason == "conflict_gluttony")
            return (UiState.Blocked, "held — GluttonyCombo also fills familiars");
        if (PetSelect.YieldReason == "autoduty_running")
            return (UiState.Idle, "standing by for AutoDuty");
        return (UiState.Ready, null);
    }

    public override void Draw()
    {
        var cfg = Plugin.Config;

        Theme.PushFrame();
        try
        {
            DrawTopBar();
            Layout.PageHeader(null, FontAwesomeIcon.Paw.ToIconString(), "LazyCrucible",
                "Fills the run roster and the Battlehorn slots for each fight's mechanics, with a beast-pick advisor and a read-only screen recorder.",
                RunTracker.Focus.Battle >= 0
                    ? $"next: {BST_CrucibleData.BattleLabel(RunTracker.Focus.Board, RunTracker.Focus.Battle)}"
                    : "no fight in focus");

            if (PetSelect.YieldReason == "conflict_gluttony")
                Widgets.Banner(UiState.Blocked, "LazyCrucible is not writing",
                    "an older GluttonyCombo that also fills familiars is loaded — update it first");
            else if (PetSelect.YieldReason == "autoduty_running")
                Widgets.Banner(UiState.Idle, "Standing by", Policy.AutoDutyCrucible.StandingBy(ExternalDrivers.DrivenSurfaces));
            else if (Plugin.Config.YieldToAutoDuty && ExternalDrivers.StallReason is { } stall)
                Widgets.Banner(UiState.Blocked, "AutoDuty reports running but has stopped stepping",
                    $"{StallText(stall)} — LazyCrucible is not standing down for it");

            ImGui.TextWrapped(PetSelect.LastSummary.Length == 0
                ? "No familiar selection yet this session."
                : $"{PetSelect.LastSummaryAt:HH:mm:ss}  {PetSelect.LastSummary}");
            if (SelectionScreens.Status.Length > 0)
                ImGui.TextWrapped(SelectionScreens.Status);
            if (ImGui.Button("Open the fight guide"))
                _plugin.ToggleGuide();
            ImGui.SameLine();
            ImGui.TextDisabled("/lazycrucible guide");
            ImGui.Separator();

            var changed = false;
            var roster = cfg.AutoRoster;
            if (Widgets.Checkbox("Fill the run roster at the Bentbranch Meadows entry menu", ref roster))
            {
                cfg.AutoRoster = roster;
                changed = true;
            }
            Help("Once per visit to the entry menu, sets the ten familiars that cover the board's battles best. A roster changed by hand is left alone until the next run.");

            var score = cfg.ScoreThreeFamiliars;
            if (Widgets.Checkbox("Score: three familiars", ref score))
            {
                cfg.ScoreThreeFamiliars = score;
                changed = true;
            }
            Help("For score runs: the run roster is the best three familiars for the whole board instead of the full roster. Three familiars on the roster score the Beast Squad III bonus on every board, and three of one kin can add the Gambit bonus. The three chosen cover as many of the board's interrupt, dispel and cleanse needs as any three can; what they cannot answer is named on the summary line. Off by default; with it off nothing changes. Does nothing on a screen AutoDuty is driving.");

            var horns = cfg.AutoHorns;
            if (Widgets.Checkbox("Fill the Battlehorn slots before each fight", ref horns))
            {
                cfg.AutoHorns = horns;
                changed = true;
            }
            Help("On the formation screen before every fight, puts the three familiars that answer that fight's mechanics on the horns: elemental weakness, interrupts (Soul Crush), dispels (Quelling Wave), cleanses and crowd control the enemies are vulnerable to. Knocked-out familiars are never picked; badly hurt ones lose to a healthy one that fits nearly as well. Never starts the fight, never touches the shop's feeding screen, and stops for that screen as soon as a slot is changed by hand.");

            ImGui.Spacing();
            Widgets.Title("Selection screens");
            Widgets.Secondary("Only when you open them; never walks, starts a battle, rests, sells or leaves.");

            var feed = cfg.AutoFeed;
            if (Widgets.Checkbox("Beast Feed: pick who eats the feed and confirm", ref feed))
            {
                cfg.AutoFeed = feed;
                changed = true;
            }
            Help("When a feed is bought, feeds it to the familiar it helps most in the fights ahead. Never a familiar whose kin cannot eat it, one that is full or knocked out, or one that already ate it. A feed that would hurt every familiar (for example max HP -80%) is left to you.");

            var shop = cfg.AutoShop;
            if (Widgets.Checkbox("Shop: buy what the fights ahead call for", ref shop))
            {
                cfg.AutoShop = shop;
                changed = true;
            }
            Help("Keeps enough heals for the fights ahead, then buys what keeps you and your familiars alive: resistance to what those fights inflict, max HP and damage-taken gear, revives, and feed for the familiars the horns will use. Each purchase is confirmed only when the prompt names that item. Never sells and never leaves the shop; buying anything yourself hands the rest of the visit to you.");

            var spoils = cfg.AutoSpoils;
            if (Widgets.Checkbox("Spoils: Take all when everything fits", ref spoils))
            {
                cfg.AutoSpoils = spoils;
                changed = true;
            }
            Help("If the loot would not fit (or repeats gear you own), shows the best items first and leaves the choice to you.");

            var treasure = cfg.AutoTreasure;
            if (Widgets.Checkbox("Treasure: suggest the best choice", ref treasure))
            {
                cfg.AutoTreasure = treasure;
                changed = true;
            }
            Help("Marks the choice that helps most and says why. It does not click yet: which button belongs to which choice has not been recorded.");

            var camp = cfg.AutoCamp;
            if (Widgets.Checkbox("Campsite: select who rests (you press Rest)", ref camp))
            {
                cfg.AutoCamp = camp;
                changed = true;
            }
            Help("Weighs your heal against the familiars' (resting familiars lose their feed unless they ate Lily Simular) and selects the familiars worth resting. Your self heal wins when you are low. Rest is always your button.");

            var goal = (int)cfg.ScoreGoal;
            ImGui.SetNextItemWidth(260);
            if (ImGui.Combo("Score goal for feeding", ref goal, "Survival first (default)\0Starve the Fever (never feed)\0Feed the Bold (feed anything harmless)\0"))
            {
                cfg.ScoreGoal = (ScoreGoal)goal;
                changed = true;
            }

            var guideOpen = cfg.GuideAutoOpen;
            if (Widgets.Checkbox("Open the fight guide on the upcoming fight", ref guideOpen))
            {
                cfg.GuideAutoOpen = guideOpen;
                changed = true;
            }
            Help("When the board layout or the Battlehorn screen points at a fight, the guide opens on it: horn picks and why, what the fight calls for, dangerous hits, mechanics and what to bring.");
            ImGui.Spacing();

            var yieldAd = cfg.YieldToAutoDuty;
            if (Widgets.Checkbox("Stand down while AutoDuty is running", ref yieldAd))
            {
                cfg.YieldToAutoDuty = yieldAd;
                changed = true;
            }
            Help("AutoDuty can run whole Crucible boards with its own team (for example a leveling team). While it is running, LazyCrucible stands by on every screen AutoDuty works: the run roster, the shop, treasure, spoils and the campsite always (AutoDuty closes the ones its toggles turn off), and the battlehorns while AutoDuty's Fight picks is on. With Fight picks off, LazyCrucible picks the battlehorns. The two never overwrite each other. AutoDuty's settings are only read, never changed.");

            var announce = cfg.AnnouncePicks;
            if (Widgets.Checkbox("Announce picks in chat", ref announce))
            {
                cfg.AnnouncePicks = announce;
                changed = true;
            }
            Help("One chat line per fight naming each familiar and why it was picked.");

            var record = cfg.RecordScreens;
            if (Widgets.Checkbox("Record Crucible screens to the log", ref record))
            {
                cfg.RecordScreens = record;
                changed = true;
            }
            Help("Read-only. Writes every Crucible screen (path choice, spoils, treasure, shop, feeding, campsite, beast gear) and the buttons pressed on it to the Dalamud log, so those screens can be automated later. Never presses anything.");

            if (changed)
                cfg.Save();

            // Shared "Report a problem" (src/Shared/LalaTelemetry): writes an RP| report (game state, recent
            // selection and screen lines, the full values of the open Crucible screens) to the plugin log.
            ImGui.Spacing();
            Plugin.Telemetry?.DrawReportButton();

            ImGui.Spacing();
            DrawAdvisor();
        }
        finally
        {
            Theme.PopFrame();
        }
    }

    private void DrawTopBar()
    {
        // Top bar: title left, live-state pill right (R3 place 1) — the same primitives
        // Layout.Shell draws for self-beginning windows.
        var (state, detail) = OverallState();
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetWindowWidth() - (2 * Theme.Unit);
        var barH = Theme.PillHeight;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, p + new Vector2(w, barH), Theme.Pack(Theme.ShellBar), 4f);
        dl.AddRect(p, p + new Vector2(w, barH), Theme.Pack(Theme.SurfaceBorder), 4f);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(2 * Theme.Unit, (barH - ImGui.GetTextLineHeight()) / 2f));
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
        ImGui.TextUnformatted("LazyCrucible");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPosX(p.X + w - (2 * Theme.Unit));
        Widgets.StatusPill(state, "LazyCrucible", detail, alignRight: true);
        ImGui.SetCursorPosY(p.Y + barH + Theme.ItemSpacing.Y);
    }

    /// <summary> The watch's reason code in words, for the window. </summary>
    private static string StallText(string reason) => reason switch
    {
        "not_running" => "neither looping nor navigating",
        "idle_in_board" => "not navigating this board",
        "navigating_in_lobby" => "still navigating after leaving its run",
        "other_board" => "driving a different board",
        "no_progress_in_lobby" => "no progress in the lobby",
        _ => reason,
    };

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

        var roster = BST_CrucibleNeedFirst.BoardRoster(board.Board, CrucibleGame.BeastCaptured);
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

            var selection = BST_CrucibleNeedFirst.SelectCaptured(battle.Board, battle.Battle, CrucibleGame.BeastCaptured);
            var picks = selection.Picks;
            foreach (var pick in picks)
                ImGui.BulletText($"{BeastName(pick.Row)}: {pick.Why}");

            var capture = BST_CrucibleNeedFirst.WorthCapturing(battle.Board, battle.Battle, CrucibleGame.BeastCaptured, selection);
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
