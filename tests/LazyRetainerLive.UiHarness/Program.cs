using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Lala.UiRender;
using LazyRetainerLive;

namespace LazyRetainerLiveHarness;

// Renders the real ConfigWindow.Draw to PNG. Built-in mode (default) is hermetic; "--game" renders the game look
// into output/game/. Only the plugin/service boundary is stubbed (Stubs.cs). To add a scenario: add one entry to
// Scenarios below (name, size, a Plugin setup lambda) and re-render.
internal static class Program
{
    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    private static string OutputDirectory = Path.Combine(ProjectRoot, "output");
    private static RenderOptions Options = RenderOptions.BuiltIn;
    private static bool _lookReported;
    private static bool _missingGlyphs;
    private static bool _strictGlyphs;

    // First-use window size and the smallest size the window allows (ConfigWindow: Size / SizeConstraints).
    private const int DefaultW = 980, DefaultH = 680;
    private const int MinW = 420, MinH = 480;

    private sealed record Scenario(string Name, int Width, int Height, Action<Plugin> Setup)
    {
        /// <summary>Put the mouse over the custom close control so its tooltip draws.</summary>
        public bool HoverClose { get; init; }

        /// <summary>Overrides the render scale (evidence only).</summary>
        public float? Scale { get; init; }

        /// <summary>Wheel-scrolls the window body to the bottom (real wheel input over the body, so it stops at the true extent).</summary>
        public bool ScrollToBottom { get; init; }
    }

    // ---- stub state -------------------------------------------------------------------------------------------

    private static CharInfo Snapshot(string name, string world, long gil, long ventures, long seals, long inventory, params (string Name, int Job, int Level, bool Venture, long Mb)[] retainers)
    {
        var now = new DateTimeOffset(2026, 11, 15, 20, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var info = new CharInfo { Char = name, World = world, Gil = gil, Ventures = ventures, Seals = seals, Inventory = inventory };
        var index = 0;
        foreach (var r in retainers)
        {
            info.Retainers.Add(new RetainerInfo
            {
                Name = r.Name,
                Job = r.Job,
                Level = r.Level,
                HasVenture = r.Venture,
                EndsAt = r.Venture ? now + 1800 + (index * 900) : 0,
                Gil = 1_250_000 + (index * 310_000),
                VentureId = r.Venture ? 395 + index : 0,
                Mb = (int)r.Mb,
            });
            index++;
        }
        return info;
    }

    // Typical character: six retainers, four out on ventures.
    private static CharInfo Typical() => Snapshot("Test Adventurer", "Cactuar", 12_480_615, 1_284, 38_250, 87,
        ("Retainer A", 31, 100, true, 20), ("Retainer B", 33, 100, true, 20), ("Retainer C", 18, 98, true, 20),
        ("Retainer D", 16, 100, true, 20), ("Retainer E", 17, 91, false, 20), ("Retainer F", 29, 100, false, 20));

    // Longest names and largest values the fields can hold: 20-character name, ten retainers all out, capped gil.
    private static CharInfo Extreme() => Snapshot("Adventurer Placeholder", "Sargatanas", 999_999_999, 65_000, 90_000, 140,
        Enumerable.Range(0, 10).Select(i => ($"Retainer {(char)('A' + i)}", 31, 100, true, 20L)).ToArray());

    // Smallest character: one retainer, nothing out, almost no bag space.
    private static CharInfo Minimal() => Snapshot("Mog Moo", "Ragnarok", 0, 0, 0, 3,
        ("Retainer A", 31, 1, false, 20));

    private static void Live(Plugin p, CharInfo snapshot, bool fresh = true)
    {
        p.State!.Current = snapshot;
        p.State.LastTickOk = fresh;
    }

    // The message the loopback listener reports when another program already owns the port.
    private const string BindError = "Only one usage of each socket address (protocol/network address/port) is normally permitted.";

    private static readonly Scenario[] Scenarios =
    [
        new("01-first-run-needs-setup", DefaultW, DefaultH, p => { }),
        new("02-first-run-needs-setup-narrow", MinW, MinH, p => { }),
        new("03-ready-typical", DefaultW, DefaultH, p => Live(p, Typical())),
        new("04-ready-typical-narrow", MinW, MinH, p => Live(p, Typical())),
        new("05-ready-typical-narrow-tall", MinW, 1100, p => Live(p, Typical())),
        new("06-ready-typical-mid-width", 720, 600, p => Live(p, Typical())),
        new("07-ready-extreme-values", DefaultW, DefaultH, p =>
        {
            p.Config.Port = 65535;
            Live(p, Extreme());
        }),
        new("08-ready-minimal-character", DefaultW, DefaultH, p =>
        {
            p.Config.Port = 1024;
            Live(p, Minimal());
        }),
        new("09-blocked-last-known", DefaultW, DefaultH, p => Live(p, Typical(), fresh: false)),
        new("10-blocked-last-known-narrow", MinW, MinH, p => Live(p, Typical(), fresh: false)),
        new("11-failed-listener-bind", DefaultW, DefaultH, p =>
        {
            p.HttpError = BindError;
            Live(p, Typical());
        }),
        new("12-failed-listener-bind-narrow", MinW, MinH, p =>
        {
            p.HttpError = BindError + " (127.0.0.1:10504)";
        }),
        new("13-idle-listener-off", DefaultW, DefaultH, p =>
        {
            p.Config.Enabled = false;
            Live(p, Typical());
        }),
        new("14-hover-close-tooltip", DefaultW, DefaultH, p => Live(p, Typical())) { HoverClose = true },
        new("15-failed-listener-bind-tall", DefaultW, 1100, p =>
        {
            p.HttpError = BindError;
            Live(p, Typical());
        }),
        new("16-ready-typical-narrow-scrolled", MinW, MinH, p => Live(p, Typical())) { ScrollToBottom = true },
        new("scale150-ready-typical", DefaultW, DefaultH, p => Live(p, Typical())) { Scale = 1.5f },
    ];

    // The scenarios that render in built-in mode (the hermetic, byte-stable subset).
    private static readonly string[] BuiltInNames = ["01-first-run-needs-setup", "03-ready-typical", "11-failed-listener-bind"];

    // ---- main -------------------------------------------------------------------------------------------------

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

            var scenarios = Options.IsGame ? Scenarios : [.. Scenarios.Where(s => BuiltInNames.Contains(s.Name))];
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var scenario in scenarios)
            {
                var result = RenderScenario(scenario);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Path))).ToLowerInvariant();
                if (!hashes.TryAdd(hash, scenario.Name))
                {
                    if (!Options.IsGame)
                        throw new InvalidOperationException($"{scenario.Name} is byte-identical to {hashes[hash]}.");
                    Console.WriteLine($"WARNING: {scenario.Name} is byte-identical to {hashes[hash]}.");
                }
                Console.WriteLine($"{scenario.Name}: {Path.GetRelativePath(ProjectRoot, result.Path).Replace('\\', '/')} | {scenario.Width}x{scenario.Height} | {result.Triangles} triangles | sha256 {hash[..16]}");
            }
            Console.WriteLine($"Rendered {scenarios.Length} real-source UI states ({hashes.Count} distinct). No window or game process was created.");
            if (_missingGlyphs && _strictGlyphs)
                throw new InvalidOperationException("--strict-glyphs: text the window draws contains characters the game font lacks.");

            VerifyClose();
            Console.WriteLine("Custom close control: a real press and release closes the window.");
            VerifyStopButton();
            Console.WriteLine("Settings: the stop button disables the listener and requests a save.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
    }

    private static Plugin NewPlugin(Action<Plugin> setup)
    {
        var plugin = new Plugin();
        setup(plugin);
        return plugin;
    }

    private static RenderResult RenderScenario(Scenario scenario)
    {
        var plugin = NewPlugin(scenario.Setup);
        var window = new ConfigWindow(plugin);
        var size = new Vector2(scenario.Width, scenario.Height);
        var options = scenario.Scale is { } scale && Options.IsGame ? Options with { Scale = scale } : Options;
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = size;
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            io.MousePos = scenario.HoverClose ? ClosePosition(size, options) : new Vector2(-10000, -10000);
            var look = UiLook.Apply(options);
            if (!_lookReported)
            {
                _lookReported = true;
                Console.WriteLine($"Look: {look.Description}");
                if (Options.IsGame)
                    CheckGlyphs(look);
            }

            // Two frames settle window storage (child sizes, hover state); scrolling needs extra frames of wheel input.
            if (scenario.ScrollToBottom)
                io.MousePos = new Vector2(size.X * 0.2f, size.Y * 0.5f);
            var frames = scenario.ScrollToBottom ? 14 : scenario.HoverClose ? 5 : 2; // a tooltip needs a frame to size itself
            for (var frame = 0; frame < frames; frame++)
            {
                io.MouseWheel = scenario.ScrollToBottom && frame >= 2 && frame < frames - 2 ? -20f : 0f;
                ImGui.NewFrame();
                BeginRealWindow(window, size);
                window.Draw();
                ImGui.End();
                ImGui.Render();
            }

            var pixels = new byte[scenario.Width * scenario.Height * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 20;
                pixels[i + 1] = 20;
                pixels[i + 2] = 24;
                pixels[i + 3] = 255;
            }
            var drawData = ImGui.GetDrawData();
            if (!drawData.Valid || drawData.TotalVtxCount == 0)
                throw new InvalidOperationException($"{scenario.Name}: the real Draw method produced no draw data.");
            var stats = CpuRasterizer.Render(drawData, pixels, scenario.Width, scenario.Height, look.Textures);
            var path = Path.Combine(OutputDirectory, scenario.Name + ".png");
            PngWriter.Write(path, pixels, scenario.Width, scenario.Height);
            return new RenderResult(path, stats.Triangles);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    private static void BeginRealWindow(ConfigWindow window, Vector2 size)
    {
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(size);
        var open = true;
        ImGui.Begin(window.WindowName, ref open, window.Flags |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
    }

    /// <summary>Screen position of the middle of the close control for a window of this size (right edge, top bar).</summary>
    private static Vector2 ClosePosition(Vector2 size, RenderOptions options)
        // Window padding 12 (Theme.PushFrame), close control 34 square at the right of the 66 px top bar.
        => new(size.X - 12 - 17, 12 + 33);

    // ---- behaviour checks -------------------------------------------------------------------------------------

    /// <summary>Presses and releases the mouse over the close control and expects the window to close.</summary>
    private static void VerifyClose()
    {
        var plugin = NewPlugin(p => Live(p, Typical()));
        var window = new ConfigWindow(plugin) { IsOpen = true };
        var size = new Vector2(DefaultW, DefaultH);
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = size;
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            _ = UiLook.Apply(Options);
            for (var step = 0; step < 4; step++)
            {
                io.MousePos = step == 0 ? new Vector2(-10000, -10000) : ClosePosition(size, Options);
                io.MouseDown[0] = step == 2;
                ImGui.NewFrame();
                BeginRealWindow(window, size);
                window.Draw();
                ImGui.End();
                ImGui.Render();
            }
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
        if (window.IsOpen)
            throw new InvalidOperationException("The custom close control did not close the window after a real press and release.");
    }

    /// <summary>Clicks the stop button through the real window and checks config and save.</summary>
    private static void VerifyStopButton()
    {
        var size = new Vector2(DefaultW, DefaultH);
        // Stop button: bottom-right of the footer. Footer is 72 px high, the button 132x36 at (right-132, +18).
        var stop = NewPlugin(p => Live(p, Typical()));
        Click(stop, size, new Vector2(size.X - 12 - 66, size.Y - 12 - 72 + 18 + 18));
        if (stop.Config.Enabled || stop.SaveCount == 0)
            throw new InvalidOperationException("The stop button did not disable the listener and save.");
    }

    private static void Click(Plugin plugin, Vector2 size, Vector2 position)
    {
        var window = new ConfigWindow(plugin);
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = size;
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            _ = UiLook.Apply(Options);
            for (var step = 0; step < 4; step++)
            {
                io.MousePos = step == 0 ? new Vector2(-10000, -10000) : position;
                io.MouseDown[0] = step == 2;
                ImGui.NewFrame();
                BeginRealWindow(window, size);
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

    // ---- glyph audit ------------------------------------------------------------------------------------------

    /// <summary>
    /// Every non-ASCII character in the string literals of the compiled window, chrome, presentation and kit sources
    /// must exist in the active font, otherwise ImGui draws the fallback glyph. Printed as a report; --strict-glyphs
    /// turns a miss into a failure.
    /// </summary>
    private static void CheckGlyphs(RenderLook look)
    {
        var found = new SortedDictionary<string, SortedSet<string>>();
        var root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "LalaSourceRoot")?.Value;
        var literal = new Regex("@?\"(?:[^\"\\\\\r\n]|\\\\.)*\"");
        var scanned = new List<string>();
        var folders = new[]
        {
            Path.Combine("src", "LazyRetainerLive", "LazyRetainerLive"),
            Path.Combine("src", "Shared", "LalaUiKit"),
        };
        foreach (var folder in folders)
        {
            var directory = root is null ? null : Path.Combine(root, folder);
            if (directory is null || !Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var separator = Path.DirectorySeparatorChar;
                if (file.Contains($"{separator}obj{separator}") || file.Contains($"{separator}bin{separator}"))
                    continue;
                var relative = Path.GetRelativePath(root!, file).Replace('\\', '/');
                // Only files compiled into the harness draw anything; server, service and plugin entry text is not UI.
                var name = Path.GetFileName(file);
                if (folder.EndsWith("LazyRetainerLive", StringComparison.Ordinal) &&
                    name is not ("ConfigWindow.cs" or "RetainerLiveChrome.cs" or "RetainerLivePresentation.cs"))
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
                scanned.Add(relative);
                foreach (var entry in look.MissingGlyphs(text.ToString()))
                {
                    if (!found.TryGetValue(entry, out var sources))
                        found[entry] = sources = [];
                    sources.Add(relative);
                }
            }
        }

        Console.WriteLine($"Glyph check scanned {scanned.Count} source file(s): {string.Join(", ", scanned.Select(Path.GetFileName))}");
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

    private sealed record RenderResult(string Path, int Triangles);
}
