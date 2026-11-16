using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ArmoireAutoFill.Windows;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Lala.UiRender;

namespace ArmoireAutoFill.UiHarness;

internal static class Program
{
    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    private static string OutputDirectory = Path.Combine(ProjectRoot, "output");
    private static RenderOptions Options = RenderOptions.BuiltIn;
    private static bool _lookReported;
    private static bool _missingGlyphs;
    private static bool _strictGlyphs;

    private enum Surface { Collection, Shopping, Store, Settings }

    /// <summary>How big the window is drawn: the size a first-time user gets, the smallest the window allows, or a fixed size.</summary>
    private enum SizeKind { FirstUse, Narrowest, Fixed }

    private sealed record Scenario(string Name, Surface Surface, SizeKind Size, Action Setup)
    {
        public int Width { get; init; }
        public int Height { get; init; }

        /// <summary>
        /// Ordinals (top to bottom) of the interactive items in the left column to press, e.g. the dungeon rows or the
        /// "Pieces" tree nodes; the harness finds them by sweeping the mouse, so a layout change moves them with it.
        /// The run prints the list it found. On the Collection page #0 is the table header and the dungeon rows follow (#1 is the first dungeon).
        /// </summary>
        public int[] Click { get; init; } = [];

        /// <summary>Hover the first piece line under the first row (draws the item tooltip).</summary>
        public bool HoverFirstPiece { get; init; }

        /// <summary>Mouse-wheel notches (positive scrolls the body down) applied over the body after the clicks, to show a scrolled page.</summary>
        public int Scroll { get; init; }

        /// <summary>Notches applied before the clicks, so rows below the fold can be reached.</summary>
        public int ScrollBefore { get; init; }

        /// <summary>Text set under the shopping list, as pressing "Copy import code" does.</summary>
        public bool ImportStatus { get; init; }

        public float Scale { get; init; }
        public bool GameOnly { get; init; }

        /// <summary>Simple state variants kept out of the default set; rendered with the <c>--extra</c> switch.</summary>
        public bool Extra { get; init; }
    }

    // The Collection and Auto-store windows have no Size of their own, so a first-time player gets the size ImGui
    // auto-fits inside the size constraints (measured, see ResolveSize); that equals the narrowest allowed size. Every
    // main surface is therefore rendered at that size AND at the narrowest allowed width with enough height to see the
    // compact layout, and at resized widths that switch the wide layouts on (about 720, 780 and 1000 px of content).
    private static readonly Scenario[] Scenarios =
    [
        // --- Collection ---
        new("collection-first-use", Surface.Collection, SizeKind.FirstUse, Sim.Typical),
        new("collection-narrowest", Surface.Collection, SizeKind.Narrowest, Sim.Typical),
        new("collection-narrow-waiting-for-cabinet", Surface.Collection, SizeKind.Fixed, Sim.WaitingForCabinet) { Width = 520, Height = 1500 },
        new("collection-first-run-empty", Surface.Collection, SizeKind.Fixed, Sim.FirstRun) { Width = 1000, Height = 1150 },
        new("collection-wide-expanded-tooltip", Surface.Collection, SizeKind.Fixed, () =>
            {
                Sim.Typical();
                Plugin.Configuration.ShowOwnedItems = true;
            })
            { Width = 1360, Height = 1150, Click = [1], HoverFirstPiece = true },
        new("collection-gear-database-missing", Surface.Collection, SizeKind.Fixed, Sim.GearDatabaseMissing) { Width = 760, Height = 900 },
        new("collection-all-complete-auto-store-off", Surface.Collection, SizeKind.Fixed, () =>
            {
                Sim.AllComplete();
                Plugin.Configuration.AutoStoreOnOpen = false;
            })
            { Width = 1000, Height = 1000, Extra = true },

        // --- Shopping ---
        new("shopping-first-use", Surface.Shopping, SizeKind.FirstUse, () => { Sim.Typical(); Sim.CatalogBuilt(true); }),
        new("shopping-narrowest", Surface.Shopping, SizeKind.Narrowest, () => { Sim.Typical(); Sim.CatalogBuilt(true); }),
        new("shopping-narrow-typical", Surface.Shopping, SizeKind.Fixed, () => { Sim.Typical(); Sim.CatalogBuilt(true); }) { Width = 520, Height = 1300 },
        new("shopping-full-expanded-knightshopper-missing", Surface.Shopping, SizeKind.Fixed, () => { Sim.Typical(); Sim.CatalogBuilt(false); })
            { Width = 1280, Height = 3600, Click = [2, 4, 6], ImportStatus = true },
        new("shopping-catalog-building", Surface.Shopping, SizeKind.Fixed, () => { Sim.Typical(); Sim.CatalogBuilding(); }) { Width = 1000, Height = 560 },
        new("shopping-game-data-unavailable-narrow", Surface.Shopping, SizeKind.Fixed, () => { Sim.Typical(); Sim.CatalogSheetsUnavailable(); }) { Width = 520, Height = 640 },
        new("shopping-catalog-failed", Surface.Shopping, SizeKind.Fixed, () => { Sim.Typical(); Sim.CatalogBuildFailed(); }) { Width = 1000, Height = 560 },
        new("shopping-scan-needed", Surface.Shopping, SizeKind.Fixed, () => { Sim.FirstRun(); Sim.CatalogBuilt(true); }) { Width = 1000, Height = 560, Extra = true },
        new("shopping-nothing-missing", Surface.Shopping, SizeKind.Fixed, Sim.NothingMissing) { Width = 1000, Height = 640, Extra = true },

        // --- Auto-store ---
        new("store-first-use", Surface.Store, SizeKind.FirstUse, Sim.Typical),
        new("store-narrowest", Surface.Store, SizeKind.Narrowest, Sim.Typical),
        new("store-narrow-finished-armoire-closed", Surface.Store, SizeKind.Fixed, Sim.StoreFinished) { Width = 520, Height = 900 },
        new("store-wide-running-scan-cooldown", Surface.Store, SizeKind.Fixed, Sim.StoreRunning) { Width = 1000, Height = 640 },

        // --- Settings window (opens at 600x360, narrowest 400x300) ---
        new("settings-first-use", Surface.Settings, SizeKind.FirstUse, Sim.Typical),
        new("settings-narrowest", Surface.Settings, SizeKind.Narrowest, Sim.Typical),

        // Evidence only: an open dungeon scrolled up under the header band and down under the footer, at the first-use size.
        new("collection-scrolled-open-dungeon", Surface.Collection, SizeKind.Fixed, () =>
            {
                Sim.Typical();
                Plugin.Configuration.ShowOwnedItems = true;
            })
            { Width = 820, Height = 600, Click = [1], ScrollBefore = 4, Scroll = 6, GameOnly = true },

        // Evidence only: the same typical page at 150 percent global scale.
        new("scale150-collection-typical", Surface.Collection, SizeKind.Fixed, Sim.Typical)
            { Width = 1000, Height = 1400, Click = [1], Scale = 1.5f, GameOnly = true },

        // Evidence only: the narrowest width at 150 percent scale, where the five-column table gives way to stacked rows.
        new("scale150-collection-narrow", Surface.Collection, SizeKind.Fixed, Sim.Typical)
            { Width = 520, Height = 1500, Click = [1], Scale = 1.5f, GameOnly = true },
    ];

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

            var extra = args.Contains("--extra");
            var scenarios = Scenarios.Where(s => (Options.IsGame || !s.GameOnly) && (extra || !s.Extra)).ToArray();
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var scenario in scenarios)
            {
                var result = RenderScenario(scenario);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Path))).ToLowerInvariant();
                if (!hashes.TryAdd(hash, scenario.Name))
                {
                    if (!Options.IsGame)
                        throw new InvalidOperationException($"{scenario.Name} is byte-identical to {hashes[hash]}.");
                    Console.WriteLine($"WARNING: {scenario.Name} is byte-identical to {hashes[hash]}; the state it sets up is not visible.");
                }
                Console.WriteLine($"{scenario.Name}: {Path.GetRelativePath(ProjectRoot, result.Path).Replace('\\', '/')} | {result.Size.X:0}x{result.Size.Y:0} | {result.Triangles} triangles | sha256 {hash[..16]}…");
            }
            Console.WriteLine($"Rendered {scenarios.Length} real-source UI states ({hashes.Count} distinct). No window or game process was created.");
            if (_missingGlyphs && _strictGlyphs)
                throw new InvalidOperationException("--strict-glyphs: text the windows draw contains characters the game font lacks.");

            VerifyToggleInteraction();
            Console.WriteLine("Interactive drawn toggle: press and release changed the setting through the real Settings window.");
            VerifyCloseButtons();
            Console.WriteLine("Custom close controls: a real click on each window's close button closes that window.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
    }

    // ---- windows ---------------------------------------------------------------------------------------------------

    private sealed class Target
    {
        public required Window Window { get; init; }
        public required Action Draw { get; init; }
    }

    private static readonly FieldInfo SectionField =
        typeof(MainWindow).GetField("_section", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("MainWindow._section was renamed; update the harness page selector.");

    private static readonly FieldInfo KsStatusField =
        typeof(MainWindow).GetField("_ksStatus", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("MainWindow._ksStatus was renamed; update the harness import-status setter.");

    private static Target CreateTarget(Scenario scenario)
    {
        if (scenario.Surface == Surface.Settings)
        {
            var config = new ConfigWindow();
            return new Target { Window = config, Draw = config.Draw };
        }

        var main = new MainWindow(Sim.Scanner, Sim.Cabinet, Sim.AutoStore);
        var section = scenario.Surface switch
        {
            Surface.Collection => 0,
            Surface.Shopping => 1,
            _ => 2,
        };
        // The nav enum is private; the pages are selected by value exactly like clicking the nav pill would.
        SectionField.SetValue(main, Enum.ToObject(SectionField.FieldType, section));
        if (scenario.ImportStatus)
            KsStatusField.SetValue(main, Sim.CopiedMessage());
        return new Target { Window = main, Draw = main.Draw };
    }

    private static Vector2 ResolveSize(Scenario scenario, Target target)
    {
        var min = target.Window.SizeConstraints?.MinimumSize ?? new Vector2(320, 240);
        switch (scenario.Size)
        {
            case SizeKind.Narrowest:
                return min;
            case SizeKind.Fixed:
                return new Vector2(scenario.Width, scenario.Height);
        }

        // First use: the window's own Size when it sets one (FirstUseEver), otherwise the size ImGui auto-fits to
        // inside the size constraints, which is what a player gets before ever resizing the window.
        if (target.Window.Size is { } explicitSize)
            return explicitSize;
        return MeasureAutoFit(scenario);
    }

    private static Vector2 MeasureAutoFit(Scenario scenario)
    {
        var target = CreateTarget(scenario);
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(2400, 1600);
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            io.MousePos = new Vector2(-10000, -10000);
            _ = UiLook.Apply(ScenarioOptions(scenario));
            var size = Vector2.Zero;
            for (var frame = 0; frame < 6; frame++)
            {
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(Vector2.Zero);
                ApplyConstraints(target.Window);
                var open = true;
                target.Window.PreDraw();
                ImGui.Begin(target.Window.WindowName, ref open, target.Window.Flags | ImGuiWindowFlags.NoSavedSettings);
                target.Draw();
                size = ImGui.GetWindowSize();
                ImGui.End();
                target.Window.PostDraw();
                ImGui.Render();
            }
            return new Vector2(MathF.Round(size.X), MathF.Round(size.Y));
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    private static void ApplyConstraints(Window window)
    {
        if (window.SizeConstraints is { } c)
            ImGui.SetNextWindowSizeConstraints(c.MinimumSize, c.MaximumSize);
    }

    private static RenderOptions ScenarioOptions(Scenario scenario)
        => scenario.Scale > 0 ? Options with { Scale = scenario.Scale } : Options;

    // ---- rendering -------------------------------------------------------------------------------------------------

    private static readonly Vector2 Away = new(-10000, -10000);

    private static ulong DrawFrame(Target target, Vector2 size, Vector2 mouse, bool down, bool render)
    {
        var io = ImGui.GetIO();
        io.MousePos = mouse;
        io.MouseDown[0] = down;
        ImGui.NewFrame();
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(size);
        ApplyConstraints(target.Window);
        var open = true;
        // Dalamud's window system pushes the window's own style (zero padding) around Begin and End.
        target.Window.PreDraw();
        ImGui.Begin(target.Window.WindowName, ref open, target.Window.Flags |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
        target.Draw();
        ImGui.End();
        target.Window.PostDraw();
        ulong hovered = ImGui.GetCurrentContext().HoveredId;
        if (render)
            ImGui.Render();
        else
            ImGui.EndFrame();
        return hovered;
    }

    /// <summary>Segments of the left column (top to bottom) where the mouse hovers one interactive item.</summary>
    private static List<(ulong Id, float Y0, float Y1)> Sweep(Target target, Vector2 size, float x)
    {
        var found = new List<(ulong, float, float)>();
        ulong last = 0;
        for (var y = 64f; y < size.Y - 56; y += 2)
        {
            var id = DrawFrame(target, size, new Vector2(x, y), false, false);
            if (id != 0 && id == last)
                found[^1] = (id, found[^1].Item2, y);
            else if (id != 0)
                found.Add((id, y, y));
            last = id;
        }
        return found;
    }

    private static void ClickAt(Target target, Vector2 size, Vector2 at)
    {
        DrawFrame(target, size, at, false, false);
        DrawFrame(target, size, at, true, false);
        DrawFrame(target, size, at, false, false);
        DrawFrame(target, size, Away, false, false);
    }

    private static void ScrollBody(Target target, Vector2 size, int notches)
    {
        var at = new Vector2(size.X / 2f, size.Y / 2f);
        for (var notch = 0; notch < notches; notch++)
        {
            ImGui.GetIO().MouseWheel = -1f;
            DrawFrame(target, size, at, false, false);
            ImGui.GetIO().MouseWheel = 0f;
            DrawFrame(target, size, at, false, false);
        }
    }

    private static RenderResult RenderScenario(Scenario scenario)
    {
        Sim.Reset();
        scenario.Setup();
        var target = CreateTarget(scenario);
        var size = ResolveSize(scenario, target);
        var width = (int)size.X;
        var height = (int)size.Y;

        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = size;
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            io.MousePos = Away;
            var look = UiLook.Apply(ScenarioOptions(scenario));
            if (!_lookReported)
            {
                _lookReported = true;
                Console.WriteLine($"Look: {look.Description}");
                if (Options.IsGame)
                    CheckGlyphs(look);
            }

            for (var warm = 0; warm < 3; warm++)
                DrawFrame(target, size, Away, false, false);

            var probeX = 44f;
            ScrollBody(target, size, scenario.ScrollBefore);
            var hoverTarget = Vector2.Zero;
            foreach (var ordinal in scenario.Click)
            {
                var segments = Sweep(target, size, probeX);
                if (ordinal >= segments.Count)
                    throw new InvalidOperationException($"{scenario.Name}: wanted clickable #{ordinal} in the left column but found {segments.Count} ({string.Join(", ", segments.Select(s => $"{s.Y0:0}-{s.Y1:0}"))}).");
                var (_, y0, y1) = segments[ordinal];
                var at = new Vector2(probeX, (y0 + y1) / 2f);
                Console.WriteLine($"  {scenario.Name}: pressing clickable #{ordinal} at y {y0:0}-{y1:0} (of {segments.Count}: {string.Join(" ", segments.Select(g => $"{g.Y0:0}"))})");
                ClickAt(target, size, at);
            }

            ScrollBody(target, size, scenario.Scroll);

            if (scenario.HoverFirstPiece)
            {
                // The first piece line under the first expanded row. Its column depends on the layout, so look for
                // the spot where the item tooltip window appears (the draw data gains a window).
                var row = Sweep(target, size, probeX)[scenario.Click[0]];
                DrawFrame(target, size, Away, false, true);
                var baseline = ImGui.GetDrawData().CmdListsCount;
                var found = false;
                var maxLists = baseline;
                Console.WriteLine($"  {scenario.Name}: first expanded row at y {row.Y0:0}-{row.Y1:0}; baseline draw lists {baseline}");
                for (var y = row.Y1 + 4; y < row.Y1 + 64 && !found; y += 6)
                    for (var x = 60f; x < size.X - 12 && !found; x += 24)
                    {
                        DrawFrame(target, size, new Vector2(x, y), false, false);
                        DrawFrame(target, size, new Vector2(x, y), false, true); // a new tooltip window is hidden for its first frame
                        maxLists = Math.Max(maxLists, ImGui.GetDrawData().CmdListsCount);
                        if (ImGui.GetDrawData().CmdListsCount > baseline)
                        {
                            hoverTarget = new Vector2(x, y);
                            found = true;
                        }
                    }
                if (!found)
                {
                    Console.WriteLine($"WARNING: {scenario.Name}: no piece line showed a tooltip (draw lists peaked at {maxLists}, baseline {baseline}).");
                    hoverTarget = new Vector2(1290, row.Y1 + 22);
                }
            }

            var settleMouse = scenario.HoverFirstPiece ? hoverTarget : Away;
            DrawFrame(target, size, settleMouse, false, false);
            DrawFrame(target, size, settleMouse, false, true);

            var frame = new byte[width * height * 4];
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
            var stats = CpuRasterizer.Render(drawData, frame, width, height, look.Textures);
            var path = Path.Combine(OutputDirectory, scenario.Name + ".png");
            PngWriter.Write(path, frame, width, height);
            return new RenderResult(path, size, stats.Triangles);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    // ---- behaviour checks ------------------------------------------------------------------------------------------

    /// <summary>A real press and release on the drawn toggle in the Settings window flips the configuration value.</summary>
    private static void VerifyToggleInteraction()
    {
        Sim.Reset();
        Sim.Typical();
        var scenario = new Scenario("toggle-probe", Surface.Settings, SizeKind.FirstUse, Sim.Typical);
        var target = CreateTarget(scenario);
        var size = new Vector2(600, 460);
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = size;
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            _ = UiLook.Apply(Options);
            for (var warm = 0; warm < 3; warm++)
                DrawFrame(target, size, Away, false, false);

            var before = Plugin.Configuration.ScanOnLoad;
            for (var y = 100f; y < 300f && Plugin.Configuration.ScanOnLoad == before; y += 6)
                for (var x = size.X - 80; x < size.X - 8 && Plugin.Configuration.ScanOnLoad == before; x += 6)
                    ClickAt(target, size, new Vector2(x, y));
            if (Plugin.Configuration.ScanOnLoad == before)
                throw new InvalidOperationException("The drawn toggle did not change the setting after a real press and release.");
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    /// <summary>A real click on each window's drawn close button must close that window.</summary>
    private static void VerifyCloseButtons()
    {
        Sim.Reset();
        Sim.Typical();
        foreach (var (name, surface) in new[] { ("Collection window", Surface.Collection), ("Settings window", Surface.Settings) })
        {
            var scenario = new Scenario("close-probe", surface, SizeKind.FirstUse, Sim.Typical);
            var target = CreateTarget(scenario);
            target.Window.IsOpen = true;
            var size = new Vector2(900, 600);
            var context = ImGui.CreateContext();
            try
            {
                var io = ImGui.GetIO();
                io.DisplaySize = size;
                io.DisplayFramebufferScale = Vector2.One;
                io.DeltaTime = 1f / 60f;
                _ = UiLook.Apply(Options);
                for (var warm = 0; warm < 3; warm++)
                    DrawFrame(target, size, Away, false, false);
                for (var x = size.X - 14; x > size.X - 70 && target.Window.IsOpen; x -= 4)
                    for (var y = 12f; y < 70 && target.Window.IsOpen; y += 6)
                        ClickAt(target, size, new Vector2(x, y));
                if (target.Window.IsOpen)
                    throw new InvalidOperationException($"The {name} close button did not close its window.");
            }
            finally
            {
                ImGui.DestroyContext(context);
            }
        }
    }

    // ---- glyph audit -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Heuristic glyph audit: every non-ASCII character in the string literals of the compiled UI sources, the shared
    /// kit and the fixture names must exist in the game font, otherwise ImGui draws the fallback mark. Printed as a
    /// report; <c>--strict-glyphs</c> turns a miss into a failure.
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

        Scan("Fixtures/armoire-real-data.json", string.Concat(Sim.FixtureStrings().Select(x => x + "\n")));

        var root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "LalaSourceRoot")?.Value;
        var literal = new Regex("@?\"(?:[^\"\\\\\r\n]|\\\\.)*\"");
        var files = new List<(string Dir, string Pattern)>
        {
            (Path.Combine("src", "ArmoireAutoFill", "Windows"), "*.cs"),
            (Path.Combine("src", "ArmoireAutoFill", "Data", "Shopping"), "KnightshopperInstructions.cs"),
            (Path.Combine("src", "Shared", "LalaUiKit"), "*.cs"),
        };
        foreach (var (folder, pattern) in files)
        {
            var directory = root is null ? null : Path.Combine(root, folder);
            if (directory is null || !Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;
                var text = new StringBuilder();
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
                Scan(Path.GetRelativePath(root!, file).Replace('\\', '/'), text.ToString());
            }
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

    private sealed record RenderResult(string Path, Vector2 Size, int Triangles);
}
