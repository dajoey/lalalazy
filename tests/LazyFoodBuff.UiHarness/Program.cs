using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Lala.UiRender;
using Lumina.Excel.Sheets;

namespace LazyFoodBuff.UiHarness;

/// <summary>
/// Renders the real LazyFoodBuff ConfigWindow (its only window) to PNG through the shared render core. Built-in mode
/// (default) is hermetic; <c>--game</c> reproduces the in-game font, scale and style and adds the size and scale variants.
/// </summary>
internal static class Program
{
    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    private static string OutputDirectory = Path.Combine(ProjectRoot, "output");
    private static RenderOptions Options = RenderOptions.BuiltIn;
    private static bool _lookReported;
    private static bool _missingGlyphs;
    private static bool _strictGlyphs;

    private enum Popup
    {
        None,

        /// <summary>The job profile combo (first combo of the Food selection card).</summary>
        JobSelector,

        /// <summary>The manual food combo (second combo; needs Manual mode).</summary>
        FoodPicker,
    }

    private sealed record Scenario(string Name, int Width, int Height, Action<Configuration> Setup)
    {
        /// <summary>The job profile the window has selected (0 = default profile).</summary>
        public uint SelectedJob { get; init; }
        public Popup Popup { get; init; }

        /// <summary>Scrolls the body to its end (content below the fold).</summary>
        public bool ScrollToEnd { get; init; }

        /// <summary>Scrolls the (stacked) body down by this many mouse-wheel notches (about 85 px each) from the top.</summary>
        public float ScrollNotches { get; init; }

        /// <summary>Display height when taller than the window: popups open in the free space the game screen has around a window.</summary>
        public int? CanvasHeight { get; init; }

        public int ImageHeight => CanvasHeight ?? Height;

        /// <summary>Overrides the global UI scale (evidence renders only).</summary>
        public float? Scale { get; init; }
    }

    // The window opens at 1080x760 on first use; its minimum is 420x480 (read from ConfigWindow's constructor).
    private const int DefaultWidth = 1080, DefaultHeight = 760;
    private const int MinWidth = 420, MinHeight = 480;

    // Job ids used below (ClassJob rows): 21 warrior, 24 white mage, 32 dark knight, 37 gunbreaker.
    private static IEnumerable<Scenario> BuildScenarios()
    {
        const int W = DefaultWidth, H = DefaultHeight;
        string N(string name, int w, int h) => $"{name}-{w}x{h}";

        // Default size, top of the window.
        yield return new(N("first-run-empty", W, H), W, H, FirstRun);
        yield return new(N("ready-fed", W, H), W, H, ReadyFed);
        yield return new(N("blocked-logged-out", W, H), W, H, LoggedOut);
        yield return new(N("first-run-empty-scrolled", W, H), W, H, FirstRun) { ScrollToEnd = true };
        yield return new(N("idle-off", W, H), W, H, IdleOff);
        yield return new(N("job-selector-open", W, H), W, H, ReadyFed) { Popup = Popup.JobSelector, CanvasHeight = H + 300 };

        // Default size, both columns scrolled to their end (the content that sits below the fold at first use).
        yield return new(N("ready-fed-scrolled", W, H), W, H, ReadyFed) { ScrollToEnd = true };
        yield return new(N("refresh-due-override-scrolled", W, H), W, H, RefreshDueOverride) { SelectedJob = 32, ScrollToEnd = true };
        yield return new(N("manual-needs-setup-scrolled", W, H), W, H, ManualNeedsSetup) { SelectedJob = 21, ScrollToEnd = true };
        yield return new(N("manual-missing-stock-scrolled", W, H), W, H, ManualMissingStock) { SelectedJob = 37, ScrollToEnd = true };
        yield return new(N("manual-in-stock-scrolled", W, H), W, H, ManualInStock) { SelectedJob = 24, ScrollToEnd = true };
        yield return new(N("food-picker-open-scrolled", W, H), W, H, ManualPickerOpen) { SelectedJob = 21, ScrollNotches = 3, Popup = Popup.FoodPicker, CanvasHeight = H + 300 };

        // Game mode only: the stacked layouts and the evidence-only scale render.
        yield return new(N("mid-ready-fed", 760, 760), 760, 760, ReadyFed);
        yield return new(N("narrow-ready-fed", MinWidth, 760), MinWidth, 760, ReadyFed);
        yield return new(N("narrow-ready-fed-scrolled", MinWidth, 760), MinWidth, 760, ReadyFed) { ScrollToEnd = true };
        yield return new(N("narrow-first-run-empty", MinWidth, MinHeight), MinWidth, MinHeight, FirstRun);
        yield return new(N("narrow-manual-missing-stock-scrolled", MinWidth, 760), MinWidth, 760, ManualMissingStock) { SelectedJob = 37, ScrollNotches = 8 };
        yield return new(N("scale150-ready-fed", W, H), W, H, ReadyFed) { Scale = 1.5f };
    }

    private static bool IsGameOnly(Scenario s) => s.Width != DefaultWidth || s.Scale is not null;

    // ---------------------------------------------------------------------------------------------------------------
    // Scenario states. Each one sets the fake game world (World) and the saved configuration.
    // ---------------------------------------------------------------------------------------------------------------

    // Real food names. Stat bonuses are approximate and only drive which food the real ranking recommends.
    private static List<Food> MakeFoods() =>
    [
        new(44001, "Archon Burger", 4601, hq: 24, nq: 0, (27, 10, 98, 4, 14), (44, 6, 61, 3, 9), (3, 5, 120, 2, 18)),
        new(44002, "Baked Eggplant", 4602, hq: 6, nq: 14, (44, 9, 90, 3, 12), (22, 7, 80, 3, 10)),
        new(44003, "Sarmale", 4603, hq: 0, nq: 2, (27, 8, 84, 4, 13), (46, 8, 74, 3, 11)),
        new(44004, "Rroneek Steak", 4604, hq: 3, nq: 0, (19, 9, 88, 4, 14), (27, 6, 62, 3, 9)),
        new(44005, "Peperoncino", 4605, hq: 0, nq: 0, (27, 8, 80, 3, 11), (44, 7, 70, 3, 10)),
        new(44006, "Dhalmel Steak", 4606, hq: 0, nq: 0, (22, 9, 85, 4, 12), (44, 5, 54, 2, 8)),
        new(44007, "Stuffed Highland Cabbage", 4607, hq: 12, nq: 20, (27, 9, 92, 4, 14), (45, 7, 72, 3, 10)),
        new(44008, "Pumpkin Ratatouille", 4608, hq: 0, nq: 0, (44, 8, 78, 3, 11), (6, 8, 76, 3, 11)),
        new(44009, "Smoked Chicken", 4609, hq: 0, nq: 0, (27, 7, 66, 3, 9), (22, 6, 60, 3, 8)),
        new(44010, "Orange Juice", 4610, hq: 0, nq: 0, (3, 4, 50, 2, 6)),
    ];

    private static Food FoodNamed(string name) => World.Foods.First(f => f.Name == name);

    private static void Common(bool inDuty)
    {
        World.Reset();
        World.Foods.AddRange(MakeFoods());
        World.InCombatDuty = inDuty;
    }

    private static void FirstRun(Configuration c)
    {
        Common(inDuty: false);
        foreach (var food in World.Foods)
        {
            food.Hq = 0;
            food.Nq = 0;
        }
    }

    private static void ReadyFed(Configuration c)
    {
        Common(inDuty: true);
        World.WellFed = true;
        World.WellFedFoodRow = FoodNamed("Archon Burger").ItemFoodRowId;
        World.WellFedLeft = TimeSpan.FromMinutes(23.4);
    }

    private static void RefreshDueOverride(Configuration c)
    {
        Common(inDuty: true);
        World.WellFed = true;
        World.WellFedFoodRow = FoodNamed("Stuffed Highland Cabbage").ItemFoodRowId;
        World.WellFedLeft = TimeSpan.FromMinutes(4.2);
        c.Jobs[32] = new JobFoodSettings();
        c.Jobs[21] = new JobFoodSettings { Mode = FoodSelectionMode.Manual, ManualFoodItemId = FoodNamed("Rroneek Steak").Id };
        c.WarningSoundId = 87;
    }

    private static void LoggedOut(Configuration c)
    {
        Common(inDuty: false);
        World.LoggedIn = false;
    }

    private static void IdleOff(Configuration c)
    {
        Common(inDuty: true);
        World.WellFed = true;
        World.WellFedFoodRow = FoodNamed("Baked Eggplant").ItemFoodRowId;
        World.WellFedLeft = TimeSpan.FromMinutes(12);
        c.MasterEnable = false;
        c.WarningEnabled = false;
        c.OnlyInCombatDuty = false;
    }

    private static void ManualNeedsSetup(Configuration c)
    {
        Common(inDuty: true);
        c.Jobs[21] = new JobFoodSettings { Mode = FoodSelectionMode.Manual, ManualFoodItemId = 0 };
    }

    private static void ManualMissingStock(Configuration c)
    {
        Common(inDuty: true);
        var cabbage = FoodNamed("Stuffed Highland Cabbage");
        cabbage.Hq = 0;
        cabbage.Nq = 0;
        c.Jobs[37] = new JobFoodSettings
        {
            Mode = FoodSelectionMode.Manual,
            ManualFoodItemId = cabbage.Id,
            FallbackToAutoSelect = false,
        };
        c.WarningThresholdCount = 12;
    }

    private static void ManualInStock(Configuration c)
    {
        Common(inDuty: true);
        World.WellFed = true;
        World.WellFedFoodRow = FoodNamed("Sarmale").ItemFoodRowId;
        World.WellFedLeft = TimeSpan.FromMinutes(27.6);
        c.Jobs[24] = new JobFoodSettings { Mode = FoodSelectionMode.Manual, ManualFoodItemId = FoodNamed("Sarmale").Id };
    }

    private static void ManualPickerOpen(Configuration c)
    {
        Common(inDuty: true);
        c.Jobs[21] = new JobFoodSettings { Mode = FoodSelectionMode.Manual, ManualFoodItemId = FoodNamed("Rroneek Steak").Id };
    }

    // ---------------------------------------------------------------------------------------------------------------

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

            var scenarios = BuildScenarios().Where(s => Options.IsGame || !IsGameOnly(s)).ToList();
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var scenario in scenarios)
            {
                var result = RenderScenario(scenario);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Path))).ToLowerInvariant();
                if (!hashes.TryAdd(hash, scenario.Name))
                {
                    if (!Options.IsGame)
                        throw new InvalidOperationException($"{scenario.Name} is byte-identical to {hashes[hash]}.");
                    Console.WriteLine($"WARNING: {scenario.Name} is byte-identical to {hashes[hash]}; the state it sets up is not visible at this size.");
                }
                Console.WriteLine($"{scenario.Name}: {Path.GetRelativePath(ProjectRoot, result.Path).Replace('\\', '/')} | {result.Triangles} triangles | {result.Bytes:N0} bytes | sha256 {hash[..16]}…");
            }
            Console.WriteLine($"Rendered {scenarios.Count} real-source UI states ({hashes.Count} distinct). No window or game process was created.");
            if (_missingGlyphs && _strictGlyphs)
                throw new InvalidOperationException("--strict-glyphs: text the window draws contains characters the game font lacks.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // One ImGui context + the real window.
    // ---------------------------------------------------------------------------------------------------------------

    private sealed class Session : IDisposable
    {
        private static readonly FieldInfo SelectedJobField =
            typeof(ConfigWindow).GetField("_selectedJobId", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ConfigWindow._selectedJobId was not found; update the harness.");

        private readonly Scenario _scenario;
        private readonly Plugin _plugin = new();
        private readonly ConfigWindow _window;
        private readonly ImGuiContextPtr _context;

        public Session(Scenario scenario)
        {
            _scenario = scenario;
            _window = new ConfigWindow(_plugin);
            _context = ImGui.CreateContext();
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(scenario.Width, scenario.ImageHeight);
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            io.MousePos = new Vector2(-10000, -10000);
            var options = scenario.Scale is { } scale ? Options with { Scale = scale } : Options;
            Look = UiLook.Apply(options);
            Textures = [.. Look.Textures, MakeJobIcon()];
            ResetState();
        }

        public RenderLook Look { get; }
        public IReadOnlyList<AtlasTexture> Textures { get; }

        /// <summary>Rebuilds the world and the configuration exactly as the scenario declares them.</summary>
        public void ResetState()
        {
            _plugin.Config = new Configuration();
            _scenario.Setup(_plugin.Config);
            SelectedJobField.SetValue(_window, _scenario.SelectedJob);
        }

        public void Frame(Vector2 mouse, bool down)
        {
            var io = ImGui.GetIO();
            io.MousePos = mouse;
            io.MouseDown[0] = down;
            ImGui.NewFrame();
            _window.PreDraw();
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(new Vector2(_scenario.Width, _scenario.Height));
            var open = true;
            ImGui.Begin(_window.WindowName, ref open, _window.Flags |
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
            _window.Draw();
            ImGui.End();
            _window.PostDraw();
            ImGui.Render();
        }

        public void Idle(int frames)
        {
            for (var i = 0; i < frames; i++)
                Frame(new Vector2(-10000, -10000), false);
        }

        /// <summary>A real click: hover, press, release, then the pointer leaves (a combo popup stays open).</summary>
        public void Click(Vector2 at)
        {
            Frame(at, false);
            Frame(at, true);
            Frame(at, false);
            Frame(new Vector2(-10000, -10000), false);
        }

        /// <summary>Scrolls the regions that scroll (the two columns of the wide layout, or the whole body when stacked) to their end.</summary>
        public void ScrollAllToEnd()
        {
            var io = ImGui.GetIO();
            foreach (var at in new[]
                     {
                         new Vector2(_scenario.Width * 0.3f, _scenario.Height * 0.6f),
                         new Vector2(_scenario.Width * 0.85f, _scenario.Height * 0.6f),
                     })
            {
                Frame(at, false);
                io.AddMouseWheelEvent(0, -80f);
                Frame(at, false);
                Idle(2);
            }
        }

        /// <summary>Settles layout and applies the scenario's scroll position.</summary>
        public void Prepare()
        {
            Idle(3);
            if (_scenario.ScrollToEnd)
                ScrollAllToEnd();
            if (_scenario.ScrollNotches > 0)
            {
                var at = new Vector2(_scenario.Width * 0.3f, _scenario.Height * 0.6f);
                // Start from the top: a probe trial leaves the body scrolled, and notches add up.
                Frame(at, false);
                ImGui.GetIO().AddMouseWheelEvent(0, 100f);
                Frame(at, false);
                Idle(2);
                Frame(at, false);
                ImGui.GetIO().AddMouseWheelEvent(0, -_scenario.ScrollNotches);
                Frame(at, false);
                Idle(2);
            }
            Idle(2);
        }

        public static bool AnyPopupOpen() => ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel);

        public void DismissPopups()
        {
            Click(new Vector2(1, 1));
            Idle(1);
            if (AnyPopupOpen())
                throw new InvalidOperationException("A popup stayed open after a click outside it.");
        }

        public void Dispose() => ImGui.DestroyContext(_context);
    }

    /// <summary>Stand-in for a game job icon: a flat slate tile with a light border.</summary>
    private static AtlasTexture MakeJobIcon()
    {
        const int size = 48;
        var rgba = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var edge = x < 2 || y < 2 || x >= size - 2 || y >= size - 2;
                var i = ((y * size) + x) * 4;
                rgba[i] = (byte)(edge ? 170 : 74);
                rgba[i + 1] = (byte)(edge ? 180 : 88);
                rgba[i + 2] = (byte)(edge ? 200 : 118);
                rgba[i + 3] = 255;
            }
        }
        return new AtlasTexture(new IntPtr(World.JobIconTextureId), rgba, size, size);
    }

    /// <summary>
    /// Finds where a combo sits by pressing and releasing down a column inside the left card: the first rows that open
    /// a popup, grouped, are the combos in draw order (job selector, then the manual food picker).
    /// </summary>
    private static Vector2 FindCombo(Scenario scenario, int index)
    {
        using var probe = new Session(scenario);
        probe.Prepare();
        var hits = new List<float>();
        const float x = 64;
        for (var y = 40f; y < scenario.Height - 40; y += 4)
        {
            // A trial can click a mode button, which changes the content height and clamps the scroll: restore both each time.
            probe.ResetState();
            probe.Prepare();
            probe.Click(new Vector2(x, y));
            probe.Idle(1);
            if (Session.AnyPopupOpen())
            {
                hits.Add(y);
                probe.DismissPopups();
            }
        }
        var clusters = new List<List<float>>();
        foreach (var y in hits)
        {
            if (clusters.Count > 0 && y - clusters[^1][^1] <= 8)
                clusters[^1].Add(y);
            else
                clusters.Add([y]);
        }
        if (index >= clusters.Count)
            throw new InvalidOperationException($"{scenario.Name}: combo #{index} was not found ({clusters.Count} combo(s) respond at x={x}).");
        var cluster = clusters[index];
        return new Vector2(x, cluster[cluster.Count / 2]);
    }

    private static RenderResult RenderScenario(Scenario scenario)
    {
        var comboAt = scenario.Popup switch
        {
            Popup.JobSelector => (Vector2?)FindCombo(scenario, 0),
            Popup.FoodPicker => FindCombo(scenario, 1),
            _ => null,
        };

        using var session = new Session(scenario);
        if (!_lookReported)
        {
            _lookReported = true;
            Console.WriteLine($"Look: {session.Look.Description}");
            if (Options.IsGame)
                CheckGlyphs(session.Look);
        }

        session.Prepare();
        if (comboAt is { } at)
        {
            session.Click(at);
            session.Idle(3);
            if (!Session.AnyPopupOpen())
                throw new InvalidOperationException($"{scenario.Name}: the combo popup did not open.");
        }

        var frame = new byte[scenario.Width * scenario.ImageHeight * 4];
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
        var stats = CpuRasterizer.Render(drawData, frame, scenario.Width, scenario.ImageHeight, session.Textures);
        var path = Path.Combine(OutputDirectory, scenario.Name + ".png");
        PngWriter.Write(path, frame, scenario.Width, scenario.ImageHeight);
        return new RenderResult(path, new FileInfo(path).Length, stats.Triangles);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Glyph audit.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Every non-ASCII character the window can draw must exist in the game font, otherwise ImGui draws the fallback mark.
    /// Scans the string literals of the compiled UI sources and the fake data the harness feeds them; the icon glyph the
    /// page header draws is checked separately. <c>--strict-glyphs</c> turns a miss into a failure.
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

        var root = typeof(Plugin).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "LalaSourceRoot")?.Value;
        var literal = new Regex("@?\"(?:[^\"\\\r\n]|\\.)*\"");
        var files = new List<string>();
        if (root is not null)
        {
            var plugin = Path.Combine(root, "src", "LazyFoodBuff", "LazyFoodBuff");
            files.Add(Path.Combine(plugin, "ConfigWindow.cs"));
            files.Add(Path.Combine(plugin, "FoodUiChrome.cs"));
            var kit = Path.Combine(root, "src", "Shared", "LalaUiKit");
            if (Directory.Exists(kit))
                files.AddRange(Directory.EnumerateFiles(kit, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")));
        }
        foreach (var file in files.Where(File.Exists))
        {
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

        // The fake data the window is fed (real item and job names).
        Scan("harness food names", string.Concat(MakeFoods().Select(f => f.Name + "\n")));
        var jobs = new HarnessData().GetExcelSheet<ClassJob>();
        if (jobs is not null)
            Scan("harness job names", string.Concat(jobs.Select(j => j.Name.ExtractText() + "\n")));

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

    private sealed record RenderResult(string Path, long Bytes, int Triangles);
}
