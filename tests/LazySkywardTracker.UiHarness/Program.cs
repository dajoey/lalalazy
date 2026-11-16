using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Lala.UiRender;

namespace LazySkywardTracker.UiHarness;

/// <summary>
/// Renders the real TrackerWindow.Draw to PNG on the CPU. Without arguments: offline checks plus a hermetic built-in
/// render (output/). With <c>--game</c> (or LALA_UI_FIDELITY=game): the game-look render (output/game/) and the glyph
/// audit; <c>--strict-glyphs</c> turns a missing glyph into a failure.
/// </summary>
internal static class Program
{
    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));

    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            var options = RenderOptions.Resolve(args);
            var strictGlyphs = args.Contains("--strict-glyphs");
            var ok = Checks.RunOffline();

            var outputDirectory = Path.Combine(ProjectRoot, "output");
            if (options.OutputSubdirectory.Length > 0)
                outputDirectory = Path.Combine(outputDirectory, options.OutputSubdirectory);
            Directory.CreateDirectory(outputDirectory);

            var dataStrings = Scenarios.DataStrings().ToList(); // before any scenario: it sets up shared fake state
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            var glyphsOk = true;
            var first = true;
            foreach (var scenario in Scenarios.All)
            {
                if (!options.IsGame && scenario.Scale is not null)
                    continue; // the scale evidence image only exists in game mode
                var scenarioOptions = scenario.Scale is { } scale ? options with { Scale = scale } : options;
                var audit = first && options.IsGame;
                first = false;
                var result = RenderScenario(scenario, scenarioOptions, outputDirectory, audit ? look => glyphsOk = Checks.GlyphAudit(look, dataStrings) : null);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Path))).ToLowerInvariant();
                if (!hashes.TryAdd(hash, scenario.Name))
                    Console.WriteLine($"WARNING: {scenario.Name} is byte-identical to {hashes[hash]}; the state it sets up is not visible.");
                Console.WriteLine($"{scenario.Name}: {Path.GetRelativePath(ProjectRoot, result.Path).Replace('\\', '/')} | {result.Triangles} triangles | sha256 {hash[..16]}");
            }

            Console.WriteLine($"Rendered {hashes.Count} distinct real-source UI states to {Path.GetRelativePath(ProjectRoot, outputDirectory).Replace('\\', '/')}. No window or game process was created.");
            if (!glyphsOk && strictGlyphs)
                throw new InvalidOperationException("--strict-glyphs: text the window draws contains characters the game font lacks.");
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
    }

    private sealed record RenderResult(string Path, int Triangles);

    private static RenderResult RenderScenario(Scenario scenario, RenderOptions options, string outputDirectory, Action<RenderLook>? onFirstLook)
    {
        scenario.Setup();
        var plugin = new Plugin();
        var window = new TrackerWindow(plugin);
        if (scenario.Width < MinW(window) || scenario.Height < MinH(window) ||
            scenario.Width > window.SizeConstraints!.Value.MaximumSize.X || scenario.Height > window.SizeConstraints.Value.MaximumSize.Y)
            throw new InvalidOperationException($"{scenario.Name}: {scenario.Width}x{scenario.Height} is outside the window's allowed size.");

        var size = new Vector2(scenario.Width, scenario.Height);
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = size;
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            io.MousePos = Off;
            var look = UiLook.Apply(options);
            UiBuilder.IconFont = look.IconFont.IsNull ? look.DefaultFont : look.IconFont;
            if (onFirstLook is not null)
            {
                Console.WriteLine($"Look: {look.Description}");
                onFirstLook(look); // the font pointers are only valid while this context lives
            }

            var frames = 0;
            void Frame(Vector2 mouse, float wheel = 0)
            {
                io.MousePos = mouse;
                io.MouseWheel = wheel;
                ImGui.NewFrame();
                DrawWindow(window, size);
                ImGui.Render();
                frames++;
            }

            Frame(Off);
            Frame(Off);
            if (scenario.Wheel > 0)
            {
                // Real wheel input over the jobs list; the list is the only scrollable child.
                var over = new Vector2(size.X / 2f, size.Y * 0.6f);
                for (var i = 0; i < scenario.Wheel; i++)
                    Frame(over, -1f);
                Frame(Off);
                Frame(Off);
            }

            var mouse = Off;
            if (scenario.TooltipRun is { } run)
            {
                mouse = FindTooltipMouse(scenario, size, io, Frame, run);
                for (var i = 0; i < 4; i++)
                    Frame(mouse);
            }
            else
            {
                Frame(Off);
            }

            var frame = new byte[scenario.Width * scenario.Height * 4];
            for (var i = 0; i < frame.Length; i += 4)
            {
                frame[i] = 20; frame[i + 1] = 20; frame[i + 2] = 24; frame[i + 3] = 255;
            }

            var drawData = ImGui.GetDrawData();
            if (!drawData.Valid || drawData.TotalVtxCount == 0)
                throw new InvalidOperationException($"{scenario.Name}: the real Draw method produced no draw data.");
            var stats = CpuRasterizer.Render(drawData, frame, scenario.Width, scenario.Height, look.Textures);
            var path = Path.Combine(outputDirectory, scenario.Name + ".png");
            PngWriter.Write(path, frame, scenario.Width, scenario.Height);
            return new RenderResult(path, stats.Triangles);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    private static readonly Vector2 Off = new(-10000, -10000);

    private static float MinW(TrackerWindow window) => window.SizeConstraints!.Value.MinimumSize.X;

    private static float MinH(TrackerWindow window) => window.SizeConstraints!.Value.MinimumSize.Y;

    /// <summary>
    /// Sweeps the mouse down the progress column and returns the middle of the Nth run of positions where the real
    /// window opens its projection tooltip (it adds one render window). Deterministic: only the window's own hover logic decides.
    /// </summary>
    private static Vector2 FindTooltipMouse(Scenario scenario, Vector2 size, ImGuiIOPtr io, Action<Vector2, float> frame, int runIndex)
    {
        frame(Off, 0);
        frame(Off, 0);
        var baseline = io.MetricsRenderWindows;
        var x = size.X * (size.X >= 760 ? 0.55f : 0.5f);
        var runs = new List<(float Start, float End)>();
        float? start = null;
        float last = 0;
        for (var y = 4f; y < size.Y - 4; y += 3)
        {
            var mouse = new Vector2(x, y);
            frame(mouse, 0);
            frame(mouse, 0);
            var hit = io.MetricsRenderWindows > baseline;
            if (hit && start is null)
                start = y;
            if (!hit && start is not null)
            {
                runs.Add((start.Value, last));
                start = null;
            }
            last = y;
        }
        if (start is not null)
            runs.Add((start.Value, last));

        Console.WriteLine($"  {scenario.Name}: tooltip rows at y = {string.Join(", ", runs.Select(r => $"{r.Start:0}-{r.End:0}"))}");
        if (runIndex >= runs.Count)
            throw new InvalidOperationException($"{scenario.Name}: wanted tooltip row {runIndex} but only {runs.Count} row(s) open a tooltip.");
        var run = runs[runIndex];
        return new Vector2(x, (run.Start + run.End) / 2f);
    }

    private static void DrawWindow(TrackerWindow window, Vector2 size)
    {
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(size);
        var open = true;
        ImGui.Begin(window.WindowName, ref open, window.Flags |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
        window.Draw();
        ImGui.End();
    }
}
