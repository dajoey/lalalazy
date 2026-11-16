using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Lala.UiRender;

namespace LazyFateAutomation.UiHarness;

internal static class Program
{
    private const string LivePage = "fates.live";
    private const string SelectionPage = "settings.selection";
    private const string CurrencyPage = "settings.currency";
    private const string ZonesPage = "settings.zones";
    private const string DisplayPage = "settings.display";

    // The window opens at 1120x760 on first use (Size/SizeCondition.FirstUseEver in FateToolKitWindow). It sets no size
    // constraints, so "narrowest" is 60 percent of that width. The minimised form sets its own size (MinimisedSize).
    private const int FirstUseWidth = 1120, FirstUseHeight = 760, NarrowWidth = 672, MinimisedWidth = 700, MinimisedHeight = 140;

    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    private static string OutputDirectory = Path.Combine(ProjectRoot, "output");
    private static RenderOptions Options = RenderOptions.BuiltIn;
    private static bool _lookReported;
    private static bool _missingGlyphs;

    /// <summary>Everything a scenario setup can reach: the kit (fake state), the real window, the real configuration.</summary>
    private sealed class Ctx(FateToolKit tweak, FateToolKitWindow window)
    {
        public FateToolKit Tweak { get; } = tweak;
        public FateToolKitWindow Window { get; } = window;
        public Configuration Config => Plugin.Config;

        public void Page(string page)
        {
            Field("_currentPage", page);
            if (page != LivePage)
                Field("_lastSettingsPage", page);
        }

        public void ZoneFilter(string text) => Field("_zoneFilter", text);
        public void Minimise() => typeof(FateToolKitWindow).BaseType!.GetMethod("ToggleMinimised", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window, null);

        public void Run(string state, string status, int completed = 0, int? target = null)
        {
            Tweak.ToggleRunning();
            Tweak.CompletedCount = completed;
            Tweak.RunUntilCompleted = target;
            Tweak.CurrentState = state;
            Service.Automation.Status = status;
        }

        private void Field(string name, object value)
            => typeof(FateToolKitWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Window, value);
    }

    private enum StepKind { Move, Click, Scroll }

    /// <summary>One scripted mouse action in window pixels (the window sits at 0,0): move the pointer, or press and release.</summary>
    private sealed record Step(StepKind Kind, float X, float Y, float Wheel = 0);

    private static Step Move(float x, float y) => new(StepKind.Move, x, y);
    private static Step Click(float x, float y) => new(StepKind.Click, x, y);

    /// <summary>Mouse wheel over a point; negative ticks scroll the content down.</summary>
    private static Step Scroll(float x, float y, float ticks) => new(StepKind.Scroll, x, y, ticks);

    private sealed record Scenario(string Name, string Shows, int Width, int Height, Action<Ctx> Setup)
    {
        public Step[] Steps { get; init; } = [];
        public float? Scale { get; init; }
        public bool GameOnly { get; init; }
    }

    private static void Populate(Ctx c)
    {
        PublicEvent.Fates = Fixtures.KozamaukaFates();
        c.Config.Blacklist[FateType.Normal] = [Fixtures.BlacklistedFate];
    }

    private static readonly Scenario[] Scenarios =
    [
        // ---- live page --------------------------------------------------------------------------------------
        new("live-first-run-empty", "Live page, first run: defaults, automation stopped, a zone with no FATEs (empty info box, zero stats).",
            FirstUseWidth, FirstUseHeight, c => c.Page(LivePage)),
        new("live-populated-stopped", "Live page, stopped, nine real Kozama'uka FATEs: eligible, over progress, short time, over-long and blacklisted, pending collect FATE, long names, bonus FATE.",
            FirstUseWidth, FirstUseHeight, c => { Populate(c); c.Page(LivePage); }),
        new("live-populated-stopped-narrow", "Same populated live page at the narrowest width: stat cards stacked, run controls wrapped, rows clipped.",
            NarrowWidth, FirstUseHeight, c => { Populate(c); c.Page(LivePage); }),
        new("live-running", "Live page while running: Engaging, run-until target 20 with 7 completed (progress on the stat card), Stop button, running status pill and footer.",
            FirstUseWidth, FirstUseHeight, c =>
            {
                Populate(c);
                c.Page(LivePage);
                c.Run("Engaging", "Engaging", completed: 7, target: 20);
            }),
        new("live-stopping-narrow", "Running with Stop-when-safe pending ('Stopping'), Gemstones mode with its remaining count, at the narrowest width.",
            NarrowWidth, FirstUseHeight, c =>
            {
                Populate(c);
                c.Page(LivePage);
                c.Config.SelectedModeId = "Gemstones";
                c.Run("BetweenFates", "Teleporting to fate", completed: 3);
                c.Tweak.PendingStopWhenSafe = true;
            }),
        new("live-fate-tooltip", "Live page scrolled to the blocked FATEs, hovering the blacklisted over-long one: the tooltip lists every FATE property plus the 'Blocked by' reasons.",
            FirstUseWidth, FirstUseHeight, c => { Populate(c); c.Page(LivePage); }) { Steps = [Scroll(500, 500, -30), Move(420, 512)] },
        new("live-mode-popup", "Grind-mode popup open over the live page: every mode, the current one selected.",
            FirstUseWidth, FirstUseHeight, c => { Populate(c); c.Page(LivePage); }) { Steps = [Click(185, 337), Move(900, 620)] },

        // ---- settings pages ---------------------------------------------------------------------------------
        new("settings-selection", "FATE selection page: allowed FATE types with three types switched off, target priority and behaviour cards in the right rail.",
            FirstUseWidth, FirstUseHeight, c =>
            {
                c.Config.ExcludedFateRules = [FateRule.Escort, FateRule.Defend, FateRule.EventFate];
                c.Page(SelectionPage);
            }),
        new("settings-selection-narrow", "FATE selection page at the narrowest width: one column, rail below, Forlorn priority off.",
            NarrowWidth, FirstUseHeight, c =>
            {
                c.Config.PrioritizeForlornMaidens = false;
                c.Page(SelectionPage);
            }),
        new("settings-currency", "Currency focus page with Bicolor Gemstones focused: both combos, fallback 'Idle in zone', rotation card in the rail.",
            FirstUseWidth, FirstUseHeight, c =>
            {
                c.Config.CurrencyFocus = FateCurrency.BicolorGemstones;
                c.Config.CurrencyFocusFallback = CurrencyFocusFallback.Idle;
                c.Page(CurrencyPage);
            }),
        new("settings-currency-narrow", "Currency focus page at the narrowest width with no focus set (single combo, 'Normal zone selection').",
            NarrowWidth, FirstUseHeight, c => c.Page(CurrencyPage)),
        new("settings-zones", "Swap zones page with a manual selection of seven zones (counts, All/None per expansion, Edit Selected Zones button, no-teleport caption).",
            FirstUseWidth, FirstUseHeight, c =>
            {
                c.Config.SelectedSwapZones = [1187, 1188, 1189, 1190, 1191, 1192, 961];
                c.Page(ZonesPage);
            }),
        new("settings-zones-tall", "Swap zones page, default pool, in a tall window (evidence): at this width each expansion card is built from two full-height columns, so the first card takes all the room and the other five expansions sit below it.",
            FirstUseWidth, 1400, c => c.Page(ZonesPage)),
        new("settings-zones-narrow-mode", "Swap zones page at the narrowest width with a grind mode that supplies the zone list (Law's Order) and two zones excluded.",
            NarrowWidth, FirstUseHeight, c =>
            {
                c.Config.SelectedModeId = "Law's Order (Resistance)";
                c.Config.ExcludedSwapZones = [620, 622];
                c.Page(ZonesPage);
            }),
        new("settings-zones-filter-empty", "Swap zones page with a filter that matches nothing: the empty-state info box.",
            FirstUseWidth, FirstUseHeight, c =>
            {
                c.Page(ZonesPage);
                c.ZoneFilter("Eureka");
            }),
        new("settings-display", "Sorting and display page: six default sort rows (drag, criteria combo, direction, remove), Add/Reset buttons, display settings in the rail.",
            FirstUseWidth, FirstUseHeight, c => c.Page(DisplayPage)),
        new("settings-display-narrow-add-popup", "Display page at the narrowest width, scrolled to the end (Add/Reset buttons, display card with a long name format), with the Add sort criterion popup open.",
            NarrowWidth, FirstUseHeight, c =>
            {
                c.Config.DisplayNameFormat = "[{Level}] {Name} ({Progress}%) - {TimeRemaining} - {Distance}y";
                c.Page(DisplayPage);
            }) { Steps = [Scroll(300, 500, -30), Click(71, 414), Move(560, 330)] },

        // ---- minimised form ---------------------------------------------------------------------------------
        new("minimised-stopped", "Minimised form, stopped: brand, status pill, Settings and Restore buttons, run controls, status line.",
            MinimisedWidth, MinimisedHeight, c =>
            {
                c.Page(LivePage);
                c.Minimise();
            }),
        new("minimised-running", "Minimised form while running in Yo-kai Watch mode with seven FATEs completed.",
            MinimisedWidth, MinimisedHeight, c =>
            {
                Populate(c);
                c.Config.SelectedModeId = "Yo-kai Watch (Medals)";
                c.Page(LivePage);
                c.Run("WaitingForFates", "Waiting for fates to spawn", completed: 7);
                c.Minimise();
            }),

        // ---- evidence ---------------------------------------------------------------------------------------
        new("scale150-live-populated", "Evidence only: the populated live page at global UI scale 1.5.",
            FirstUseWidth, FirstUseHeight, c => { Populate(c); c.Page(LivePage); }) { Scale = 1.5f, GameOnly = true },
    ];

    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            Options = RenderOptions.Resolve(args);
            var strictGlyphs = args.Contains("--strict-glyphs");
            var only = args.SkipWhile(a => a != "--only").Skip(1).FirstOrDefault()?.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (Options.OutputSubdirectory.Length > 0)
                OutputDirectory = Path.Combine(OutputDirectory, Options.OutputSubdirectory);
            Directory.CreateDirectory(OutputDirectory);

            var selected = Scenarios
                .Where(s => Options.IsGame || !s.GameOnly)
                .Where(s => only is null || only.Contains(s.Name))
                .ToArray();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var scenario in Scenarios)
                if (!names.Add(scenario.Name))
                    throw new InvalidOperationException($"Duplicate scenario name {scenario.Name}.");

            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var scenario in selected)
            {
                var result = RenderScenario(scenario);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Path))).ToLowerInvariant();
                if (!hashes.TryAdd(hash, scenario.Name))
                {
                    if (!Options.IsGame)
                        throw new InvalidOperationException($"{scenario.Name} is byte-identical to {hashes[hash]}.");
                    Console.WriteLine($"WARNING: {scenario.Name} is byte-identical to {hashes[hash]}; the state it sets up is not visible.");
                }
                Console.WriteLine($"{scenario.Name}: {Path.GetRelativePath(ProjectRoot, result.Path).Replace('\\', '/')} | {scenario.Width}x{scenario.Height} | {result.Triangles} triangles | sha256 {hash[..16]}");
            }
            Console.WriteLine($"Rendered {selected.Length} real-source UI states ({hashes.Count} distinct). No window or game process was created.");
            if (_missingGlyphs && strictGlyphs)
                throw new InvalidOperationException("--strict-glyphs: text the window draws contains characters the game font lacks.");
            VerifyCloseControl();
            Console.WriteLine("Custom close control: the drawn close button closes the window.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
    }

    private static void ResetState()
    {
        Plugin.Config = new Configuration();
        Service.Automation.Running = false;
        Service.Automation.Status = "Idle";
        Svc.Navmesh.Running = false;
        Player.Available = true;
        Player.Position = Vector3.Zero;
        PublicEvent.Fates = [];
    }

    private sealed record RenderResult(string Path, int Triangles);

    private static RenderResult RenderScenario(Scenario scenario)
    {
        ResetState();
        var tweak = new FateToolKit();
        var window = new FateToolKitWindow(tweak);
        var ctx = new Ctx(tweak, window);
        scenario.Setup(ctx);

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

            void Frame(Vector2 mouse, bool down, float wheel = 0)
            {
                io.MousePos = mouse;
                io.MouseDown[0] = down;
                if (wheel != 0)
                    io.AddMouseWheelEvent(0f, wheel);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(size);
                var open = true;
                ImGui.Begin(window.WindowName, ref open, window.Flags |
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
                window.Draw();
                ImGui.End();
                ImGui.Render();
            }

            var away = new Vector2(-10000, -10000);
            for (var i = 0; i < 3; i++)
                Frame(away, false);
            foreach (var step in scenario.Steps)
            {
                var point = new Vector2(step.X, step.Y);
                switch (step.Kind)
                {
                    case StepKind.Move:
                        Frame(point, false);
                        Frame(point, false);
                        break;
                    case StepKind.Scroll:
                        Frame(point, false);
                        for (var i = 0; i < 6; i++)
                            Frame(point, false, step.Wheel / 6f);
                        Frame(point, false);
                        break;
                    case StepKind.Click:
                        Frame(point, false);
                        Frame(point, true);
                        Frame(point, false);
                        Frame(point, false);
                        break;
                }
            }
            if (scenario.Steps.Length == 0)
                Frame(away, false);

            var frame = new byte[scenario.Width * scenario.Height * 4];
            for (var i = 0; i < frame.Length; i += 4)
            {
                frame[i] = 20;
                frame[i + 1] = 20;
                frame[i + 2] = 24;
                frame[i + 3] = 255;
            }
            var drawData = ImGui.GetDrawData();
            if (!drawData.Valid || drawData.TotalVtxCount == 0)
                throw new InvalidOperationException($"{scenario.Name}: the real Draw method produced no draw data.");
            var stats = CpuRasterizer.Render(drawData, frame, scenario.Width, scenario.Height, look.Textures);
            var path = Path.Combine(OutputDirectory, scenario.Name + ".png");
            PngWriter.Write(path, frame, scenario.Width, scenario.Height);
            return new RenderResult(path, stats.Triangles);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    /// <summary>The drawn close button must close the window it belongs to (the real window sets IsOpen = false).</summary>
    private static void VerifyCloseControl()
    {
        ResetState();
        var window = new FateToolKitWindow(new FateToolKit()) { IsOpen = true };
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            var size = new Vector2(FirstUseWidth, FirstUseHeight);
            io.DisplaySize = size;
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            _ = UiLook.Apply(Options);
            // Close button: the right-most 34 px square of the top bar, inside the window padding.
            var click = new Vector2(FirstUseWidth - 12 - 17, 12 + 16 + 17);
            for (var step = 0; step < 6; step++)
            {
                io.MousePos = step < 3 ? new Vector2(-10000, -10000) : click;
                io.MouseDown[0] = step == 4;
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(size);
                var open = true;
                ImGui.Begin(window.WindowName, ref open, window.Flags | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
                window.Draw();
                ImGui.End();
                ImGui.Render();
            }
            if (window.IsOpen)
                throw new InvalidOperationException("The drawn close button did not close the window after a press and release.");
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    /// <summary>
    /// Glyph audit: every non-ASCII character in the string literals of the plugin's UI sources, the shared kit and the
    /// fixture data must exist in the game font, otherwise ImGui draws the fallback mark. <c>--strict-glyphs</c> fails on a miss.
    /// </summary>
    private static void CheckGlyphs(RenderLook look)
    {
        var found = new SortedDictionary<string, SortedSet<string>>();
        var root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "LalaSourceRoot")?.Value;
        var literal = new Regex("@?\"(?:[^\"\\\r\n]|\\.)*\"");

        void ScanFile(string file, string display)
        {
            var text = new System.Text.StringBuilder();
            foreach (var line in File.ReadLines(file))
            {
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;
                foreach (Match match in literal.Matches(line))
                {
                    var value = match.Value;
                    try { value = Regex.Unescape(value); } catch (ArgumentException) { }
                    text.Append(value).Append('\n');
                }
            }
            foreach (var entry in look.MissingGlyphs(text.ToString()))
            {
                if (!found.TryGetValue(entry, out var sources))
                    found[entry] = sources = [];
                sources.Add(display);
            }
        }

        if (root is not null)
        {
            var plugin = Path.Combine(root, "src", "LazyFateAutomation");
            foreach (var name in new[]
                     {
                         "FateToolKitWindow.cs", "FateChrome.cs", "FateToolKit.cs", "FateGrindMode.cs", "FateGrind.cs",
                         "FateZoneLogic.cs", "Configuration.cs", Path.Combine("Helpers", "ImGuiHelpers", "MinimisableWindow.cs"),
                     })
            {
                var file = Path.Combine(plugin, name);
                if (File.Exists(file))
                    ScanFile(file, "src/LazyFateAutomation/" + name.Replace('\\', '/'));
            }
            var kit = Path.Combine(root, "src", "Shared", "LalaUiKit");
            if (Directory.Exists(kit))
                foreach (var file in Directory.EnumerateFiles(kit, "*.cs", SearchOption.AllDirectories))
                    ScanFile(file, "src/Shared/LalaUiKit/" + Path.GetFileName(file));
        }
        var fixtures = Path.Combine(ProjectRoot, "Fixtures.cs");
        if (File.Exists(fixtures))
            ScanFile(fixtures, "tests/LazyFateAutomation.UiHarness/Fixtures.cs");
        // Real FATE and zone names the fixtures draw, by value (also covers anything built at run time).
        var drawn = string.Join('\n', Fixtures.KozamaukaFates().Select(f => f.Name).Concat(Fixtures.Zones.Select(z => z.Name + " " + z.ExVersionName)));
        foreach (var entry in look.MissingGlyphs(drawn))
        {
            if (!found.TryGetValue(entry, out var sources))
                found[entry] = sources = [];
            sources.Add("fixture names");
        }

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
}
