using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lala.UiRender;
using WondrousTailsSolver;

namespace LazyWTMath.UiHarness;

internal static class Program
{
    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    private static int _pass;
    private static int _fail;
    private static RenderOptions Options = RenderOptions.BuiltIn;
    private static bool _lookReported;
    private static bool _missingGlyphs;
    private static bool _strictGlyphs;

    // The window's real first-use size (Size, SizeCondition FirstUseEver) and its size limits, read from LazyWTMathWindow.
    private static readonly Vector2 FirstUse = new(920, 700);
    private static readonly Vector2 Narrowest = new(480, 600);
    private static readonly Vector2 Largest = new(1400, 1000);

    // Journal states. Cell indexes are 0-based positions on the 4x4 sticker board.
    private static readonly int[] None = [];
    private static readonly int[] Four = [0, 5, 6, 15];
    private static readonly int[] Seven = [0, 1, 2, 3, 5, 10, 15];       // a full row and the main diagonal
    private static readonly int[] Eight = [0, 1, 2, 3, 5, 6, 10, 15];
    private static readonly int[] Nine = [0, 1, 2, 3, 5, 6, 9, 10, 15];
    private static readonly int[] Corrupt = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]; // more than the nine stickers a journal holds

    private enum Hover { None, Close }

    /// <param name="Cells">null = no character logged in.</param>
    private sealed record Scenario(string Name, Vector2 Size, int[]? Cells, int StickerCount = -1)
    {
        public Hover Hover { get; init; }
        public float Scale { get; init; }
        public bool GameOnly { get; init; }
        /// <summary>Wheel-scroll the scrollable column under the pointer to its end (the window scrolls its body, not itself).</summary>
        public bool ScrollEnd { get; init; }
        /// <summary>Wheel ticks for <see cref="ScrollEnd"/>; the default is far past the end of any column.</summary>
        public float Wheel { get; init; } = -200;
        public int Stickers => StickerCount >= 0 ? StickerCount : Cells?.Length ?? 0;
    }

    // Order is the reading order of the contact sheet: states at first-use size, then the same states narrow, then extremes.
    private static readonly Scenario[] Scenarios =
    [
        new("idle-no-player-920x700", FirstUse, null),
        new("empty-no-stickers-920x700", FirstUse, None),
        new("ready-4-stickers-920x700", FirstUse, Four),
        new("ready-7-stickers-920x700", FirstUse, Seven),
        new("ready-7-stickers-920x700-scrolled-end-map", FirstUse, Seven) { GameOnly = true, ScrollEnd = true },
        new("ready-8-stickers-no-shuffle-920x700", FirstUse, Eight),
        new("ready-9-stickers-complete-920x700", FirstUse, Nine),
        new("failed-bad-solver-state-920x700", FirstUse, Corrupt),
        new("idle-no-player-480x600", Narrowest, null) { GameOnly = true },
        new("empty-no-stickers-480x600", Narrowest, None) { GameOnly = true },
        new("ready-4-stickers-480x600", Narrowest, Four) { GameOnly = true },
        new("ready-4-stickers-480x600-scrolled-end-rail", Narrowest, Four) { GameOnly = true, ScrollEnd = true },
        new("failed-bad-solver-state-480x600-scrolled-to-message", Narrowest, Corrupt) { GameOnly = true, ScrollEnd = true, Wheel = -4 },
        new("ready-4-stickers-1400x1000-largest", Largest, Four) { GameOnly = true },
        new("hover-close-tooltip-920x700", FirstUse, Four) { GameOnly = true, Hover = Hover.Close },
        new("scale150-ready-4-stickers-920x700", FirstUse, Four) { GameOnly = true, Scale = 1.5f },
    ];

    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        RunPresentationChecks();
        if (_fail != 0)
        {
            Console.WriteLine($"FAILED ({_fail} of {_pass + _fail})");
            return 1;
        }

        try
        {
            Options = RenderOptions.Resolve(args);
            _strictGlyphs = args.Contains("--strict-glyphs");
            var outputDirectory = Path.Combine(ProjectRoot, "output");
            if (Options.OutputSubdirectory.Length > 0)
                outputDirectory = Path.Combine(outputDirectory, Options.OutputSubdirectory);
            Directory.CreateDirectory(outputDirectory);

            // The real solver builds its tables in the constructor. Its shuffle averages are random samples: seed them so
            // every render is reproducible (same code path, same table shape, fixed numbers).
            WondrousTailsSolver.System.PerfectTails = new PerfectTails();
            SeedShuffleSamples(WondrousTailsSolver.System.PerfectTails);

            var scenarios = Scenarios.Where(s => Options.IsGame || !s.GameOnly).ToArray();
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var scenario in scenarios)
            {
                var (path, triangles, bytes) = RenderScenario(scenario, outputDirectory);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
                if (!hashes.TryAdd(hash, scenario.Name))
                {
                    if (!Options.IsGame)
                        throw new InvalidOperationException($"{scenario.Name} is byte-identical to {hashes[hash]}.");
                    Console.WriteLine($"WARNING: {scenario.Name} is byte-identical to {hashes[hash]}; the state it sets up is not visible.");
                }
                Console.WriteLine($"{scenario.Name}: {Path.GetRelativePath(ProjectRoot, path).Replace('\\', '/')} | {triangles} triangles | {bytes:N0} bytes | sha256 {hash[..16]}");
            }
            Console.WriteLine($"Rendered {scenarios.Length} real-source UI states ({hashes.Count} distinct). No window or game process was created.");

            if (_missingGlyphs && _strictGlyphs)
                throw new InvalidOperationException("--strict-glyphs: text the window draws contains characters the game font lacks.");
            VerifyControls();
            Console.WriteLine("Drawn controls: What's new and the custom close control call their real callbacks.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
    }

    // ---------------------------------------------------------------- rendering

    private static (string Path, int Triangles, long Bytes) RenderScenario(Scenario scenario, string outputDirectory)
    {
        PlayerState.Set(scenario.Cells is not null, scenario.Stickers, scenario.Cells ?? []);
        var options = scenario.Scale > 0 ? Options with { Scale = scenario.Scale } : Options;
        var window = new LazyWTMathWindow(() => { }) { IsOpen = true };
        var width = (int)scenario.Size.X;
        var height = (int)scenario.Size.Y;

        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = scenario.Size;
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            io.MousePos = scenario.Hover == Hover.Close ? CloseCenter(width)
                : scenario.ScrollEnd ? new Vector2(width * 0.3f, height * 0.7f) : new Vector2(-10000, -10000);
            var look = UiLook.Apply(options);
            if (!_lookReported)
            {
                _lookReported = true;
                Console.WriteLine($"Look: {look.Description}");
                if (Options.IsGame)
                    CheckGlyphs(look);
            }

            // Window storage settles over two frames, hover state (tooltip) arrives one frame after the pointer; a wheel
            // event (far larger than the content) scrolls the column under the pointer to its end, then it settles.
            var frames = scenario.ScrollEnd ? 6 : 3;
            for (var frame = 0; frame < frames; frame++)
            {
                if (scenario.ScrollEnd && frame == 2)
                    io.AddMouseWheelEvent(0, scenario.Wheel);
                ImGui.NewFrame();
                BeginRealWindow(window, scenario.Size);
                window.Draw();
                ImGui.End();
                ImGui.Render();
            }

            var pixels = new byte[width * height * 4];
            Fill(pixels, 38, 42, 48);
            var drawData = ImGui.GetDrawData();
            if (!drawData.Valid || drawData.TotalVtxCount == 0)
                throw new InvalidOperationException($"{scenario.Name}: the real Draw method produced no draw data.");
            var stats = CpuRasterizer.Render(drawData, pixels, width, height, look.Textures);
            var path = Path.Combine(outputDirectory, scenario.Name + ".png");
            PngWriter.Write(path, pixels, width, height);
            return (path, stats.Triangles, new FileInfo(path).Length);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    private static void BeginRealWindow(LazyWTMathWindow window, Vector2 size)
    {
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(size);
        var open = true;
        ImGui.Begin(window.WindowName, ref open, window.Flags |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
    }

    // Window padding is 12 px (kit frame); the close control sits 12 px from the bar's right edge, 14 px down, 34 px square.
    private static Vector2 CloseCenter(int width) => new(width - 12 - 12 - 17, 12 + 14 + 17);

    // What's new sits one gap (12 px) and its own width (104 px) to the left of the close control.
    private static Vector2 WhatsNewCenter(int width) => CloseCenter(width) - new Vector2(17 + 12 + 52, 0);

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

    /// <summary>Replaces the solver's random shuffle-average tables with the same calculation over a fixed seed.</summary>
    private static void SeedShuffleSamples(PerfectTails solver)
    {
        const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var solve = typeof(PerfectTails).GetMethod("Solve", Flags)
            ?? throw new InvalidOperationException("PerfectTails.Solve is missing; update the harness seeding.");
        var table = (Dictionary<int, double[]>?)typeof(PerfectTails).GetField("sampleProbabilities", Flags)?.GetValue(solver)
            ?? throw new InvalidOperationException("PerfectTails.sampleProbabilities is missing; update the harness seeding.");
        var random = new Random(20261115);
        for (var placed = 1; placed <= 7; placed++)
        {
            var samples = new List<double[]>();
            for (var i = 0; i < 500; i++)
            {
                var state = new bool[16];
                foreach (var index in Enumerable.Range(0, 16).OrderBy(_ => random.Next()).Take(placed))
                    state[index] = true;
                samples.Add((double[])solve.Invoke(solver, [state])!);
            }
            table[placed] = [Math.Round(samples.Average(s => s[0]), 4), Math.Round(samples.Average(s => s[1]), 4), Math.Round(samples.Average(s => s[2]), 4)];
        }
    }

    // ---------------------------------------------------------------- glyph audit

    /// <summary>
    /// Every non-ASCII character in the string literals of the window, chrome and kit sources (the text the window can
    /// draw) must have a glyph in the game font; otherwise ImGui draws the fallback mark. The solver text is ASCII.
    /// <c>--strict-glyphs</c> turns a miss into a failure.
    /// </summary>
    private static void CheckGlyphs(RenderLook look)
    {
        var root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "LalaSourceRoot")?.Value;
        var found = new SortedDictionary<string, SortedSet<string>>();
        var scanned = 0;
        var nonAscii = new SortedSet<string>();
        var literal = new Regex("@?\"(?:[^\"\\\\\r\n]|\\\\.)*\"");
        var folders = new[]
        {
            Path.Combine("src", "LazyWTMath", "LazyWTMath"),
            Path.Combine("src", "Shared", "LalaUiKit"),
        };
        var files = new List<string>();
        foreach (var folder in folders)
        {
            var directory = root is null ? null : Path.Combine(root, folder);
            if (directory is null || !Directory.Exists(directory))
                continue;
            var option = folder.EndsWith("LalaUiKit", StringComparison.Ordinal) ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            files.AddRange(Directory.EnumerateFiles(directory, "*.cs", option).Where(f => Path.GetFileName(f).StartsWith("LazyWTMath", StringComparison.Ordinal)
                || f.Contains("LalaUiKit", StringComparison.Ordinal)));
        }
        foreach (var file in files)
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
            scanned++;
            var relative = Path.GetRelativePath(root!, file).Replace('\\', '/');
            foreach (var rune in text.ToString().EnumerateRunes().Where(r => r.Value > 0x7E))
                nonAscii.Add($"U+{rune.Value:X4} '{rune}'");
            foreach (var entry in look.MissingGlyphs(text.ToString()))
            {
                if (!found.TryGetValue(entry, out var sources))
                    found[entry] = sources = [];
                sources.Add(relative);
            }
        }

        _missingGlyphs = found.Count > 0;
        Console.WriteLine($"Glyph check: scanned {scanned} source files; non-ASCII characters in drawn text: {(nonAscii.Count == 0 ? "none" : string.Join(", ", nonAscii))}.");
        if (found.Count == 0)
        {
            Console.WriteLine("Glyph check: every drawn non-ASCII character exists in the game font.");
            return;
        }
        Console.WriteLine($"Glyph check: {found.Count} character(s) in drawn text have no glyph in the game font:");
        foreach (var (entry, sources) in found)
            Console.WriteLine($"  {entry} in {string.Join(", ", sources)}");
    }

    // ---------------------------------------------------------------- behaviour checks that need ImGui

    /// <summary>Press and release over the drawn What's new and close controls: each must fire its real callback.</summary>
    private static void VerifyControls()
    {
        PlayerState.Set(true, 4, Four);
        Console.WriteLine("-- LazyWTMath drawn controls --");
        var shown = 0;
        var window = new LazyWTMathWindow(() => shown++) { IsOpen = true };
        Click(window, (int)FirstUse.X, (int)FirstUse.Y, WhatsNewCenter((int)FirstUse.X));
        Check("What's new control calls the changelog callback once", shown == 1);
        Check("What's new control leaves the window open", window.IsOpen);
        Click(window, (int)FirstUse.X, (int)FirstUse.Y, CloseCenter((int)FirstUse.X));
        Check("custom close control closes the window", !window.IsOpen);
        Console.WriteLine(_fail == 0 ? $"OK ({_pass} checks)" : $"FAILED ({_fail} of {_pass + _fail})");
        if (_fail != 0)
            throw new InvalidOperationException("Drawn control checks failed.");
    }

    private static void Click(LazyWTMathWindow window, int width, int height, Vector2 at)
    {
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(width, height);
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            _ = UiLook.Apply(Options);
            // hover for two frames, press, release
            for (var step = 0; step < 4; step++)
            {
                io.MousePos = at;
                io.MouseDown[0] = step == 2;
                ImGui.NewFrame();
                BeginRealWindow(window, new Vector2(width, height));
                window.Draw();
                ImGui.End();
                ImGui.Render();
            }
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    // ---------------------------------------------------------------- original presentation checks (unchanged)

    private static void RunPresentationChecks()
    {
        Console.WriteLine("-- LazyWTMath UI presentation --");

        var cells = new bool[16];
        cells[0] = true;
        cells[5] = true;
        var withShuffle = LazyWTMathPresentation.Parse(7, cells,
            "Line Chances: 12.34%  56.78%  90.00%\rShuffle Average: 10.00% 20.00% 30.00%");
        Check("line and shuffle text is accepted", withShuffle.HasValidProbabilities);
        Check("line values remain exact", withShuffle.LineChances.SequenceEqual(["12.34%", "56.78%", "90.00%"]));
        Check("shuffle values remain exact", withShuffle.ShuffleAverage?.SequenceEqual(["10.00%", "20.00%", "30.00%"]) == true);
        Check("sticker count remains live", withShuffle.StickersPlaced == 7);
        Check("sticker map is copied", withShuffle.Cells[0] && withShuffle.Cells[5] && !withShuffle.Cells[1]);
        Check("percentage becomes a bounded fraction", LazyWTMathPresentation.TryPercent("56.78%", out var fraction)
            && Math.Abs(fraction - 0.5678f) < 0.0001f);

        cells[0] = false;
        Check("later game-state updates do not mutate the snapshot", withShuffle.Cells[0]);

        var withoutShuffle = LazyWTMathPresentation.Parse(9, new bool[16],
            "Line Chances: 100.00% 25.00% 0.00%");
        Check("line-only solver text is accepted", withoutShuffle.HasValidProbabilities);
        Check("missing shuffle sample stays empty", withoutShuffle.ShuffleAverage is null);

        var solverError = LazyWTMathPresentation.Parse(10, new bool[16], "Line Chances: error error error");
        Check("solver errors never become fake percentages", !solverError.HasValidProbabilities);

        var malformed = LazyWTMathPresentation.Parse(3, new bool[16], "Line Chances: 10.00% 20.00%");
        Check("incomplete values are rejected", !malformed.HasValidProbabilities);

        var malformedShuffle = LazyWTMathPresentation.Parse(3, new bool[16],
            "Line Chances: 10.00% 20.00% 30.00%\nShuffle Average: unavailable");
        Check("invalid shuffle values reject the snapshot", !malformedShuffle.HasValidProbabilities);

        var unavailable = LazyWTMathSnapshot.Unavailable();
        Check("unavailable player state is distinct from valid zeroes", !unavailable.PlayerAvailable && !unavailable.HasValidProbabilities);

        Console.WriteLine(_fail == 0 ? $"OK ({_pass} checks)" : $"FAILED ({_fail} of {_pass + _fail})");
    }

    private static void Check(string name, bool condition)
    {
        Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}");
        if (condition) _pass++; else _fail++;
    }
}
