using System.Globalization;
using System.Reflection;
using System.Numerics;
using System.Security.Cryptography;
using ECommons.DalamudServices;
using Dalamud.Bindings.ImGui;
using LazyCrucible;
using LazyCrucible.Policy;
using Dalamud.Interface.Windowing;
using Lala.UiRender;

namespace LazyCrucibleHarness;

internal static class Program
{
    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    private static bool _lookReported;
    private static bool _missingGlyphs;
    private static bool _strictGlyphs;
    private static string OutputDirectory = Path.Combine(ProjectRoot, "output");
    private static RenderOptions Options = RenderOptions.BuiltIn;

    private enum ScenarioKind
    {
        Main,
        Guide,
        Overlay,
    }

    /// <summary>
    /// One rendered image. <see cref="State"/> sets the faked game/runtime state (see Stubs.cs), <see cref="Shell"/> sets
    /// the shell's own navigation fields, <see cref="Expand"/> lists advisor battles to open (tree nodes the real page
    /// only opens on a click).
    /// </summary>
    private sealed record Scenario(string Name, ScenarioKind Kind, int Width, int Height)
    {
        public string? Page { get; init; }
        public Action? State { get; init; }
        public Action<CrucibleShell>? Shell { get; init; }
        public int[] Expand { get; init; } = [];
        public float? Scale { get; init; }
    }

    private static Scenario MainWin(string name, int width, int height, string? page = null, Action? state = null,
        Action<CrucibleShell>? shell = null, int[]? expand = null, float? scale = null)
        => new(name, ScenarioKind.Main, width, height)
        { Page = page, State = state, Shell = shell, Expand = expand ?? [], Scale = scale };

    private static Scenario GuideWin(string name, int width, int height, string? page = null, Action? state = null,
        Action<CrucibleShell>? shell = null)
        => new(name, ScenarioKind.Guide, width, height) { Page = page, State = state, Shell = shell };

    // Window sizes read from the sources: both windows open at 1280x840 (SizeCondition FirstUseEver). The narrowest the
    // constraints allow is 420 (main) and 380 (guide) wide; the height stays 840 so wrapping below the header is visible
    // (at the 300 / 260 minimum height only the header block fits).
    private const int FirstUseWidth = 1280;
    private const int FirstUseHeight = 840;

    private static Scenario[] BuildScenarios(RenderOptions options)
    {
        const string Roster = CrucibleShell.RosterHornsPage;
        if (!options.IsGame)
        {
            // Hermetic regression set (13 px built-in font): the original eight states, now reaching their real pages.
            return
            [
                MainWin("main-empty-ready", 1100, 1600),
                MainWin("main-autoduty-standby", 1100, 1600, state: () => PetSelect.YieldReason = "autoduty_running"),
                MainWin("main-gluttony-conflict", 1100, 1600, state: () => PetSelect.YieldReason = "conflict_gluttony"),
                MainWin("main-advisor-expanded", 1100, 1600, CrucibleShell.AdvisorPage, state: Populated, expand: [1, 3]),
                GuideWin("guide-strix-piece", 900, 1300, state: () => FocusOnly(4, 1)),
                GuideWin("guide-incoming-warning", 900, 1300, state: () => { FocusOnly(5, 6); IncomingAtomicRay(); }),
                GuideWin("guide-missing-entry", 900, 700, state: () => FocusOnly(4, 99)),
                new("suggestion-overlay", ScenarioKind.Overlay, 800, 600),
            ];
        }

        return
        [
            // First run: nothing loaded, never selected, Bestiary missing.
            MainWin("main-first-run-1280x840", FirstUseWidth, FirstUseHeight),
            // Typical use, the approved mock-up page: focused fight, live horn plan, filled stat cards.
            MainWin("main-roster-horns-1280x840", FirstUseWidth, FirstUseHeight, Roster, Populated),
            // Smaller sizes of the same state: a realistic narrow resize, then the narrowest width the window allows.
            MainWin("main-roster-horns-920x640", 920, 640, Roster, Populated),
            MainWin("main-roster-horns-420x840", 420, 840, Roster, Populated),
            MainWin("scale150-main-roster-horns-1280x840", FirstUseWidth, FirstUseHeight, Roster, Populated, scale: 1.5f),
            // Home: live overview, and the failed state (older plugin writing the same screens) with nothing recorded yet.
            MainWin("main-home-1280x840", FirstUseWidth, FirstUseHeight, CrucibleShell.OverviewPage, Populated),
            MainWin("main-home-conflict-1280x840", FirstUseWidth, FirstUseHeight, CrucibleShell.OverviewPage,
                () => PetSelect.YieldReason = "conflict_gluttony", shell: s => s.SearchText = "roster"),
            // Run pages. AutoDuty standby is the blocked/busy state, shown on the board overview.
            MainWin("main-board-overview-standby-1280x840", FirstUseWidth, FirstUseHeight, "run.board_overview",
                () => { Populated(); PetSelect.YieldReason = "autoduty_running"; }),
            MainWin("main-feed-shop-1280x840", FirstUseWidth, FirstUseHeight, "run.feed_shop", Populated),
            MainWin("main-campsite-1280x840", FirstUseWidth, FirstUseHeight, "run.campsite", Populated),
            MainWin("main-display-logging-1280x840", FirstUseWidth, FirstUseHeight, "run.display_logging", Populated),
            // Rewards pages.
            MainWin("main-spoils-1280x840", FirstUseWidth, FirstUseHeight, "rewards.spoils", Populated),
            MainWin("main-treasure-1280x840", FirstUseWidth, FirstUseHeight, "rewards.treasure", Populated),
            // Advisor with two battles expanded, and the same page with the Bestiary missing at the narrow size.
            MainWin("main-advisor-expanded-1280x840", FirstUseWidth, FirstUseHeight, CrucibleShell.AdvisorPage, Populated, expand: [1, 3]),
            MainWin("main-advisor-missing-bestiary-920x640", 920, 640, CrucibleShell.AdvisorPage,
                () => { Populated(); CrucibleGame.RosterLoaded = false; }, expand: [1]),
            // Guide window: the focused fight with an incoming cast and the low-HP entry advisory, then its narrowest width
            // with the longest real fight title.
            GuideWin("guide-upcoming-incoming-1280x840", FirstUseWidth, FirstUseHeight, state: () =>
            {
                Populated();
                RunTracker.Board = 5;
                RunTracker.Focus = (5, 6);
                RunTracker.PlayerHpPercent = 23;
                IncomingAtomicRay();
            }),
            GuideWin("guide-upcoming-380x840", 380, FirstUseHeight, state: () => { Populated(); FocusOn(5, 11); }),
            GuideWin("guide-idle-1280x840", FirstUseWidth, FirstUseHeight, state: () => FocusOnly(0, -1)),
            GuideWin("guide-missing-entry-920x640", 920, 640, state: () => FocusOnly(4, 99)),
            GuideWin("guide-browse-1280x840", FirstUseWidth, FirstUseHeight, "guide.browse", Populated, s => { s.GuideFollow = false; s.GuideBoard = 4; s.GuideBattle = 8; }),
            GuideWin("guide-recent-1280x840", FirstUseWidth, FirstUseHeight, "guide.recent", Populated),
            new("suggestion-overlay", ScenarioKind.Overlay, 800, 600),
        ];
    }

    // ---- faked game state ---------------------------------------------------------------------------------------

    private const uint FirstMastersTerritory = 1342;
    private const uint SecondMastersTerritory = 1343;

    // Real bestiary rows: diremite, sabotender, vulture, megalocrab, flying trap, spriggan, rotting goobbue, ice golem,
    // Karlabos, lamb, crab, adamantoise.
    private static readonly int[] RunRoster = [8, 22, 11, 9, 20, 29, 45, 47, 48, 3, 15, 25];
    private static readonly HashSet<int> NotCaptured = [10, 38, 46, 49, 50];

    private static void FocusOnly(int board, int battle)
    {
        RunTracker.Board = board;
        RunTracker.Focus = (board, battle);
    }

    private static void FocusOn(int board, int battle)
    {
        Svc.ClientState.TerritoryType = board == 5 ? SecondMastersTerritory : FirstMastersTerritory;
        RunTracker.Board = board;
        RunTracker.Focus = (board, battle);
        CrucibleGame.CurrentBattleValue = battle;
    }

    private static void IncomingAtomicRay()
        => CrucibleGame.WarnedCastValue = FightWarnings.ForCast(49272, 6.4f)
            ?? throw new InvalidOperationException("The real Atomic Ray warning data is missing.");

    private static int Item(string name)
    {
        var row = CrucibleItems.All.Select((item, index) => (item, index)).FirstOrDefault(x => x.item.Name == name).index;
        return row > 0 ? row : throw new InvalidOperationException($"The real item '{name}' is not in the item data.");
    }

    /// <summary>Typical mid-run state on the First Master's Board: Strix Piece in focus, a full run roster, partial captures.</summary>
    private static void Populated()
    {
        FocusOn(4, 1);
        RunTracker.Roster = [.. RunRoster];
        RunTracker.RosterHp = RunRoster.Select((row, i) => (row, hp: i switch { 4 => 62, 7 => 35, 9 => 88, _ => 100 })).ToDictionary(x => x.row, x => x.hp);
        RunTracker.PlayerHpPercent = 100;
        CrucibleGame.RosterLoaded = true;
        CrucibleGame.CapturePredicate = row => !NotCaptured.Contains(row);

        var picks = CruciblePages.Selection(4, 1).Picks;
        PetSelect.LastSummary = $"Battlehorns for {BST_CrucibleData.BattleLabel(4, 1)}: "
            + string.Join(", ", picks.Select(p => $"{CruciblePages.BeastName(p.Row)} ({p.Why})"));
        PetSelect.LastSummaryAt = new DateTime(2026, 11, 15, 12, 30, 0);
        SelectionScreens.Status = $"Feed: {CrucibleItems.NameOf(Item("Rolanberry Cheese Simular"))} \u2192 Rotting goobbue (keeps feed through a campsite rest; a horn pick for 2 fights ahead)";

        var day = new DateTime(2026, 11, 15, 12, 0, 0);
        SelectionScreens.Recent.Clear();
        SelectionScreens.Recent.AddRange(
        [
            (day.AddMinutes(8).AddSeconds(14), "Shop", $"Buying {CrucibleItems.NameOf(Item("G2 Antipetrification Soul Serum"))}: Petrification resist for a fight ahead (320 of 1,480 tokens)"),
            (day.AddMinutes(9).AddSeconds(2), "Shop", "Done: 2 bought, 840 tokens left."),
            (day.AddMinutes(11).AddSeconds(40), "Feed", $"{CrucibleItems.NameOf(Item("Potion of Tempered Constitution"))} \u2192 Rotting goobbue (max HP +10%; a horn pick for 3 fights ahead)"),
            (day.AddMinutes(14).AddSeconds(5), "Campsite", "Rest Rotting goobbue 35%, Ice golem 62%: +40% each and you (you 100%); its feed is lost"),
            (day.AddMinutes(17).AddSeconds(51), "Treasure", $"Suggest: {CrucibleItems.NameOf(Item("G2 Antiparalysis Soul Serum"))}: Paralysis resist for a fight ahead"),
            (day.AddMinutes(19).AddSeconds(33), "Spoils", $"Take all: {CrucibleItems.NameOf(Item("Potion of Tempered Intensity"))}, {CrucibleItems.NameOf(Item("Potion of Breathtaking Swiftness"))}"),
            (day.AddMinutes(22).AddSeconds(9), "Spoils", "Not taking all (gear slots full). Best first: " + CrucibleItems.NameOf(Item("Potion of Feral Constitution")) + ", " + CrucibleItems.NameOf(Item("Potion of Feral Intensity"))),
            (day.AddMinutes(25).AddSeconds(27), "Feed", "Nothing worth feeding (every familiar is full)."),
            (day.AddMinutes(28).AddSeconds(46), "Treasure", "Nothing here helps the fights ahead (or your slots are full); leaving the choice to you."),
            (day.AddMinutes(30).AddSeconds(0), "Shop", $"Buying {CrucibleItems.NameOf(Item("Rolanberry Cheese Simular"))} \u2192 Spriggan (keeps feed through a campsite rest; a horn pick for 2 fights ahead) (180 of 840 tokens)"),
        ]);
    }

    /// <summary>Tree-node id of an advisor battle header, built the way the page builds it.</summary>
    private static string AdvisorHeader(int board, int battle)
    {
        var info = BST_CrucibleData.Battles.First(x => x.Board == board && x.Battle == battle);
        var here = BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType);
        var current = here == board ? CrucibleGame.CurrentBattle() : -1;
        var tag = info.Role switch { CrucibleRole.Boss => "boss", CrucibleRole.EliteEnemy => "elite", _ => "enemy" };
        return $"{(battle == current ? "> " : "")}{BST_CrucibleData.BattleLabel(board, battle)} ({tag}{(info.RandomOnly ? ", random space" : "")})##lc{board}_{battle}";
    }

    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            Options = RenderOptions.Resolve(args);
            _strictGlyphs = args.Contains("--strict-glyphs");
            if (Options.OutputSubdirectory.Length > 0)
                OutputDirectory = Path.Combine(OutputDirectory, Options.OutputSubdirectory);
            Directory.CreateDirectory(OutputDirectory);
            var scenarios = BuildScenarios(Options);
            using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("CrucibleGuide.json")
                               ?? throw new InvalidOperationException("The real CrucibleGuide.json was not embedded.");
            using var reader = new StreamReader(stream);
            var guide = CrucibleGuide.Parse(reader.ReadToEnd());
            guide.InstallNeedModel();
            if (guide.Fight(4, 1)?.Title != "Strix Piece")
                throw new InvalidOperationException("The real Strix Piece guide entry did not load.");
            var validationProblems = guide.Validate();
            Console.WriteLine($"Real guide: {guide.File.Fights.Count} fights; validation problems: {validationProblems.Count}.");
            if (validationProblems.Count != 0)
                throw new InvalidOperationException($"The real guide has {validationProblems.Count} validation problems.");

            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var scenario in scenarios)
            {
                var result = RenderScenario(guide, scenario);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Path))).ToLowerInvariant();
                // Every image must show something the others do not; a duplicate means a scenario never reached its state.
                if (!hashes.TryAdd(hash, scenario.Name))
                    throw new InvalidOperationException($"{scenario.Name} is byte-identical to {hashes[hash]}.");
                Console.WriteLine($"{scenario.Name}: {Path.GetRelativePath(ProjectRoot, result.Path).Replace('\\', '/')} | {result.Triangles} triangles | {result.Bytes:N0} bytes | sha256 {hash[..16]}…");
            }
            Console.WriteLine($"Rendered {scenarios.Length} real-source UI states ({hashes.Count} distinct). No window or game process was created.");
            if (_missingGlyphs && _strictGlyphs)
                throw new InvalidOperationException("--strict-glyphs: text the windows draw contains characters the game font lacks.");
            VerifyToggleInteraction();
            Console.WriteLine("Interactive drawn toggle: press and release changed the setting through CrucibleShell.");
            VerifyCloseCallbacks();
            Console.WriteLine("Custom close controls: both window callbacks close the correct window.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
        finally
        {
            ScreenReader.Reset();
        }
    }

    private static RenderResult RenderScenario(CrucibleGuide guide, Scenario scenario)
    {
        ResetState();
        Plugin.Config = new Configuration();
        Plugin.Guide = guide;
        ExternalDrivers.DrivenSurfaces = CrucibleSurface.All & ~CrucibleSurface.Horns;
        scenario.State?.Invoke();

        var plugin = new Plugin();
        var mainShell = new CrucibleShell();
        var guideShell = new CrucibleShell { CurrentPage = CrucibleShell.UpcomingPage };
        var main = new MainWindow(plugin, mainShell);
        var guideWindow = new GuideWindow(plugin, guideShell);
        var shell = scenario.Kind == ScenarioKind.Guide ? guideShell : mainShell;
        if (scenario.Kind == ScenarioKind.Guide)
            guideWindow.FollowFocus();
        if (scenario.Page is not null)
            shell.CurrentPage = scenario.Page;
        scenario.Shell?.Invoke(shell);

        if (scenario.Kind == ScenarioKind.Overlay)
        {
            if (Options.IsGame)
            {
                // The treasure suggestion as the plugin words it ("Take <item>: <why>"), over a narrow game window.
                SelectionScreens.Suggestions.Add(("XBMContentsTreasure",
                    $"Take {CrucibleItems.NameOf(Item("G2 Antipetrification Soul Serum"))}: Petrification resist for a fight ahead; no other choice here helps the fights ahead."));
                ScreenReader.SetAddon("XBMContentsTreasure", 200, 400, 300, 170);
            }
            else
            {
                SelectionScreens.Suggestions.Add(("XBMPetParty", "Pick megalocrab: Quelling Wave answers Ultimate Focus."));
                ScreenReader.SetAddon("XBMPetParty", 170, 410, 430, 150);
            }
        }

        var options = scenario.Scale is { } scale ? Options with { Scale = scale } : Options;
        var size = new Vector2(scenario.Width, scenario.Height);
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = size;
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            io.MousePos = new Vector2(-10000, -10000);
            var look = UiLook.Apply(options);
            if (!_lookReported)
            {
                _lookReported = true;
                Console.WriteLine($"Look: {look.Description}");
                if (Options.IsGame)
                    CheckGlyphs(look);
            }
            var frame = new byte[scenario.Width * scenario.Height * 4];

            // Frame 0 only opens advisor tree nodes (their open state lives in a child window's storage, which the real
            // page fills on a click); it draws no content. Two frames then settle window storage and layout.
            var window = scenario.Kind == ScenarioKind.Guide ? (Window)guideWindow : main;
            var frames = scenario.Expand.Length > 0 ? 3 : 2;
            for (var frameIndex = 0; frameIndex < frames; frameIndex++)
            {
                ImGui.NewFrame();
                if (scenario.Expand.Length > 0 && frameIndex == 0)
                {
                    OpenAdvisorNodes(window, size, scenario.Expand, shell);
                    ImGui.Render();
                    continue;
                }
                switch (scenario.Kind)
                {
                    case ScenarioKind.Main:
                        DrawMainWindow(main, size);
                        break;
                    case ScenarioKind.Guide:
                        DrawGuideWindow(guideWindow, size);
                        break;
                    case ScenarioKind.Overlay:
                        SuggestionOverlay.Draw();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                ImGui.Render();
            }
            if (scenario.Kind != ScenarioKind.Overlay && scenario.Page is not null && shell.CurrentPage != scenario.Page)
                throw new InvalidOperationException($"{scenario.Name}: expected page {scenario.Page} but the shell shows {shell.CurrentPage}.");

            Fill(frame, 20, 20, 24);
            var drawData = ImGui.GetDrawData();
            if (!drawData.Valid || drawData.TotalVtxCount == 0)
                throw new InvalidOperationException($"{scenario.Name}: the real Draw method produced no draw data.");
            var stats = CpuRasterizer.Render(drawData, frame, scenario.Width, scenario.Height, look.Textures);
            var path = Path.Combine(OutputDirectory, scenario.Name + ".png");
            PngWriter.Write(path, frame, scenario.Width, scenario.Height);
            return new RenderResult(path, new FileInfo(path).Length, stats.Triangles);
        }
        finally
        {
            ImGui.DestroyContext(context);
            ScreenReader.Reset();
        }
    }

    /// <summary>
    /// Opens the given advisor battles. The page keeps tree-node state in the storage of the body child window; this pass
    /// begins the same child (same name, same id stack) and writes the same node ids a click would, one frame before the real draw.
    /// </summary>
    private static void OpenAdvisorNodes(Window window, Vector2 size, int[] battles, CrucibleShell shell)
    {
        BeginRealWindow(window.WindowName, size, window.Flags);
        var board = shell.AdvisorBoard is >= 1 and <= 5 ? shell.AdvisorBoard : CruciblePages.ActiveBoard() is var active && active != 0 ? active : 1;
        void Open()
        {
            foreach (var battle in battles)
                ImGui.GetStateStorage().SetInt(ImGui.GetID(AdvisorHeader(board, battle)), 1);
        }
        ImGui.BeginChild("##crucibleBody", new Vector2(0, 0), true);
        Open();
        ImGui.EndChild();
        ImGui.End();
    }

    /// <summary>
    /// Heuristic glyph audit: every non-ASCII character in the guide data and in the string literals of the compiled
    /// window sources must exist in the active font, otherwise ImGui draws the fallback glyph. Printed as a report;
    /// <c>--strict-glyphs</c> turns a miss into a failure.
    /// </summary>
    private static void CheckGlyphs(RenderLook look)
    {
        var found = new SortedDictionary<string, SortedSet<string>>();
        void Scan(string source, string text)
        {
            foreach (var entry in look.MissingGlyphs(text))
            {
                if (!found.TryGetValue(entry, out var sources))
                    found[entry] = sources = [];
                sources.Add(source);
            }
        }

        using (var stream = typeof(Plugin).Assembly.GetManifestResourceStream("CrucibleGuide.json"))
        {
            if (stream is not null)
            {
                using var document = System.Text.Json.JsonDocument.Parse(stream);
                var strings = new List<string>();
                CollectStrings(document.RootElement, strings);
                Scan("CrucibleGuide.json", string.Concat(strings.Select(x => x + "\n")));
            }
        }

        // Names the windows draw from the compiled data tables (familiars, items, boards, battles, enemies).
        Scan("data: familiars", string.Join("\n", Enumerable.Range(1, BST_Beasts.Count).Select(row => CruciblePages.BeastName(row))));
        Scan("data: items", string.Join("\n", CrucibleItems.All.Select(item => item.Name ?? "")));
        Scan("data: boards", string.Join("\n", BST_CrucibleData.Boards.Select(board => board.Name)));
        Scan("data: battles", string.Join("\n", BST_CrucibleData.Battles.Select(battle => BST_CrucibleData.BattleLabel(battle.Board, battle.Battle))));
        Scan("data: enemies", string.Join("\n", BST_CrucibleData.Enemies.Select(enemy => enemy.Name)));

        var root = typeof(Plugin).Assembly.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "LalaSourceRoot")?.Value;
        var literal = new System.Text.RegularExpressions.Regex("@?\"(?:[^\"\\\r\n]|\\.)*\"");
        foreach (var folder in new[] { Path.Combine("src", "LazyCrucible"), Path.Combine("src", "Shared", "LalaUiKit"), Path.Combine("src", "Shared", "LalaCrucible") })
        {
            var directory = root is null ? null : Path.Combine(root, folder);
            if (directory is null || !Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;
                var text = new System.Text.StringBuilder();
                foreach (var line in File.ReadLines(file))
                {
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                        continue;
                    foreach (System.Text.RegularExpressions.Match match in literal.Matches(line))
                    {
                        var value = match.Value;
                        try { value = System.Text.RegularExpressions.Regex.Unescape(value); } catch (ArgumentException) { }
                        text.Append(value).Append('\n');
                    }
                }
                Scan(Path.GetRelativePath(root!, file).Replace('\\', '/'), text.ToString());
            }
        }

        // Self-test: a private-use game symbol is absent from the font, so the audit must be able to report it.
        if (look.MissingGlyphs("\uE0BB").Count == 0)
            throw new InvalidOperationException("The glyph audit cannot detect a missing glyph; its result would mean nothing.");

        _missingGlyphs = found.Count > 0;
        if (found.Count == 0)
        {
            Console.WriteLine("Glyph check: every drawn non-ASCII character exists in the game font.");
            return;
        }
        Console.WriteLine($"Glyph check: {found.Count} character(s) in drawn text have no glyph in the game font:");
        foreach (var (entry, sources) in found)
            Console.WriteLine($"  {entry} in {string.Join(", ", sources)}");
    }

    private static void CollectStrings(System.Text.Json.JsonElement element, List<string> into)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.String:
                into.Add(element.GetString() ?? "");
                break;
            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectStrings(item, into);
                break;
            case System.Text.Json.JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    CollectStrings(property.Value, into);
                break;
        }
    }

    private static void VerifyCloseCallbacks()
    {
        var plugin = new Plugin();
        var mainShell = new CrucibleShell();
        var main = new MainWindow(plugin, mainShell) { IsOpen = true };
        mainShell.CloseRequested?.Invoke();
        if (main.IsOpen)
            throw new InvalidOperationException("The main-window custom close control did not close its window.");
        var guideShell = new CrucibleShell();
        var guide = new GuideWindow(plugin, guideShell) { IsOpen = true };
        guideShell.CloseRequested?.Invoke();
        if (guide.IsOpen)
            throw new InvalidOperationException("The guide-window custom close control did not close its window.");
    }

    private static void VerifyToggleInteraction()
    {
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(480, 120);
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            _ = UiLook.Apply(Options);
            var value = false;
            var clickPos = Vector2.Zero;
            var shell = new CrucibleShell();
            for (var step = 0; step < 3; step++)
            {
                io.MousePos = step == 0 ? new Vector2(-10000, -10000) : clickPos;
                io.MouseDown[0] = step == 1;
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(io.DisplaySize);
                var open = true;
                ImGui.Begin("Drawn toggle interaction", ref open, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings);
                ImGui.SetCursorPos(new Vector2(20, 20));
                if (step == 0)
                {
                    var origin = ImGui.GetCursorScreenPos();
                    clickPos = origin + new Vector2(ImGui.GetContentRegionAvail().X - 22, 24);
                }
                shell.Setting("Interaction setting", () => value, newValue => value = newValue);
                ImGui.End();
                ImGui.Render();
            }
            if (!value)
                throw new InvalidOperationException("The drawn toggle did not change the setting after a real press and release.");
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    private static void DrawMainWindow(MainWindow window, Vector2 size)
    {
        window.PreDraw();
        BeginRealWindow(window.WindowName, size, window.Flags);
        window.Draw();
        ImGui.End();
        window.PostDraw();
    }

    private static void DrawGuideWindow(GuideWindow window, Vector2 size)
    {
        window.PreDraw();
        BeginRealWindow(window.WindowName, size, window.Flags);
        window.Draw();
        ImGui.End();
        window.PostDraw();
    }

    private static void BeginRealWindow(string name, Vector2 size, ImGuiWindowFlags windowFlags)
    {
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(size);
        var open = true;
        ImGui.Begin(name, ref open, windowFlags |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
    }

    private static void ResetState()
    {
        PetSelect.YieldReason = null;
        PetSelect.LastSummary = "";
        PetSelect.LastSummaryAt = default;
        SelectionScreens.Status = "";
        SelectionScreens.Recent.Clear();
        SelectionScreens.Suggestions.Clear();
        ExternalDrivers.StallReason = null;
        ExternalDrivers.DrivenSurfaces = CrucibleSurface.All;
        Svc.ClientState.TerritoryType = 0;
        RunTracker.Board = 0;
        RunTracker.Focus = (0, -1);
        RunTracker.Roster = [];
        RunTracker.RosterHp = [];
        RunTracker.PlayerHpPercent = 100;
        CrucibleGame.RosterLoaded = false;
        CrucibleGame.CurrentBattleValue = -1;
        CrucibleGame.WarnedCastValue = null;
        CrucibleGame.CapturePredicate = null;
    }

    private static void Fill(byte[] target, byte r, byte g, byte b)
    {
        for (var i = 0; i < target.Length; i += 4)
        {
            target[i] = r;
            target[i + 1] = g;
            target[i + 2] = b;
            target[i + 3] = 255;
        }
    }

    private sealed record RenderResult(string Path, long Bytes, int Triangles);
}
