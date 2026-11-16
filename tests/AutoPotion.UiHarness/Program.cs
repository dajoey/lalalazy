using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AutoPotion;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Lala.UiRender;

namespace AutoPotion.UiHarness;

/// <summary>
/// Renders the real AutoPotion ConfigWindow.Draw to PNG without the game, Dalamud services or a GPU. Built-in mode is
/// hermetic; <c>--game</c> renders with the stock Dalamud font, scale and style into output/game/.
/// </summary>
internal static class Program
{
    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    private static string OutputDirectory = Path.Combine(ProjectRoot, "output");
    private static RenderOptions Options = RenderOptions.BuiltIn;
    private static bool _lookReported;
    private static bool _missingGlyphs;
    private static bool _strictGlyphs;

    // The window opens at 1080x780 on first use; its minimum size is 420x540 and the layout changes at 960 px of
    // content width, so 900x700 shows the stacked layout and the two sizes below show the wide and the narrow one.
    private const int WideW = 1080, WideH = 780;
    private const int MidW = 900, MidH = 700;
    private const int NarrowW = 420, NarrowH = 540;

    private const uint Paladin = 19, Warrior = 21, BlackMage = 25, DarkKnight = 32, Astrologian = 33, Gunbreaker = 37, Dancer = 38, Sage = 40;
    private const uint UnlistedJob = 99; // a job id the data sheet has no row for: the window falls back to "Job 99"

    /// <summary>One image: the fake game state and config the real window draws, and the frame it is drawn in.</summary>
    private sealed record Scenario(string Name, int Width, int Height)
    {
        public uint JobId { get; init; }                       // 0 = no character on a job (title screen)
        public bool HasPlayer { get; init; }
        public bool Dead { get; init; }
        public bool InCombat { get; init; }
        public bool InDuty { get; init; }
        public bool Hover { get; init; }                       // mouse over the close button (tooltip)
        public float RulesWheel { get; init; }                 // wheel notches (negative = down; about 86 px each) over the rules column, or the whole body when stacked; -200 = to its end
        public float RailWheel { get; init; }                  // the same for the right rail (wide layout only)
        private bool Scrolls => RulesWheel != 0f || RailWheel != 0f;
        public bool ScrollsAny => Scrolls;
        public float? Scale { get; init; }                     // overrides LALA_UI_SCALE (evidence only)
        public bool GameOnly { get; init; }
        public Action<Configuration, JobPotionSettings>? Configure { get; init; }
    }

    private static void AllRules(Configuration c, JobPotionSettings j)
    {
        c.DecisionTelemetry = true;
        c.OnlyInDuty = true;
        j.MpPotionEnable = true;
        j.SilenceEchoDropsEnable = true;
        j.HpPotionThreshold = 99f;
        j.MpPotionThreshold = 1f;
        j.RegenPotionThreshold = 5f;
    }

    private static readonly Scenario[] AllScenarios =
    [
        // First run: factory defaults, no character yet. HP and deep-dungeon regen are on, MP and Echo Drops are off.
        new("first-run-default", WideW, WideH),
        new("first-run-default", NarrowW, NarrowH),

        // Running: Paladin in combat inside a duty, the duty gate switched on so every requirement row is live.
        new("running-paladin", WideW, WideH)
        {
            HasPlayer = true, JobId = Paladin, InCombat = true, InDuty = true,
            Configure = (c, j) => { c.OnlyInDuty = true; j.HpPotionThreshold = 55f; },
        },
        new("running-paladin", NarrowW, NarrowH)
        {
            HasPlayer = true, JobId = Paladin, InCombat = true, InDuty = true,
            Configure = (c, j) => { c.OnlyInDuty = true; j.HpPotionThreshold = 55f; },
        },
        // Stacked layout (content narrower than 960 px) with three rules on.
        new("running-gunbreaker", MidW, MidH)
        {
            HasPlayer = true, JobId = Gunbreaker, InCombat = true,
            Configure = (c, j) => { j.HpPotionThreshold = 70f; j.MpPotionEnable = true; j.MpPotionThreshold = 40f; },
        },
        // Hover on the drawn close button shows its tooltip.
        new("hover-close", WideW, WideH)
        {
            HasPlayer = true, JobId = Paladin, InCombat = true, Hover = true,
        },

        // Stopped: master switch off, so the Stop button is gone and the footer explains that profiles are kept.
        new("stopped", WideW, WideH)
        {
            HasPlayer = true, JobId = Dancer, InCombat = true,
            Configure = (c, _) => c.MasterEnable = false,
        },

        // Needs setup: every rule of the active profile is off.
        new("needs-setup", WideW, WideH)
        {
            HasPlayer = true, JobId = Warrior, InCombat = true,
            Configure = (_, j) => { j.HpPotionEnable = false; j.RegenPotionEnable = false; },
        },
        new("needs-setup", NarrowW, NarrowH)
        {
            HasPlayer = true, JobId = Warrior, InCombat = true,
            Configure = (_, j) => { j.HpPotionEnable = false; j.RegenPotionEnable = false; },
        },

        // Blocked by the combat gate (default OnlyInCombat) with the longest two-word job name in the sheet subset.
        new("waiting-combat", WideW, WideH)
        {
            HasPlayer = true, JobId = DarkKnight,
            Configure = (_, j) => { j.MpPotionEnable = true; },
        },
        // Blocked by the duty gate at the narrowest size, scrolled to the rail (Requirements and Settings cards stacked below the rules).
        new("waiting-duty", NarrowW, NarrowH)
        {
            HasPlayer = true, JobId = BlackMage, InCombat = true, RulesWheel = -11f,
            Configure = (c, j) => { c.OnlyInDuty = true; j.MpPotionEnable = true; },
        },
        // Dead character.
        new("waiting-living-character", WideW, WideH)
        {
            HasPlayer = true, JobId = Sage, Dead = true, InCombat = true,
        },

        // All four rules, extreme thresholds, the longest single-word job name and the diagnostics toggle on.
        new("all-rules-telemetry", WideW, WideH)
        {
            HasPlayer = true, JobId = Astrologian, InCombat = true, InDuty = true,
            Configure = AllRules,
        },
        // The same state with both wide-layout columns scrolled to their end: regen and Echo Drops rules, diagnostics card.
        new("all-rules-telemetry-scrolled", WideW, WideH)
        {
            HasPlayer = true, JobId = Astrologian, InCombat = true, InDuty = true, RulesWheel = -200f, RailWheel = -200f,
            Configure = AllRules,
        },
        // Job with no sheet row ("Job 99"), several profiles saved, diagnostics on, at the narrowest size, scrolled to the
        // end of the single stacked column (diagnostics card, long help text).
        new("unlisted-job-telemetry-scrolled", NarrowW, NarrowH)
        {
            HasPlayer = true, JobId = UnlistedJob, InCombat = true, InDuty = true, RulesWheel = -200f,
            Configure = (c, j) =>
            {
                c.DecisionTelemetry = true;
                c.OnlyInDuty = true;
                j.SilenceEchoDropsEnable = true;
                j.MpPotionEnable = true;
                c.GetOrCreateJobSettings(Paladin);
                c.GetOrCreateJobSettings(Gunbreaker);
                c.GetOrCreateJobSettings(Astrologian);
            },
        },

        // Evidence only: the typical running state at UI scale 1.5.
        new("scale150-running-paladin", WideW, WideH)
        {
            HasPlayer = true, JobId = Paladin, InCombat = true, InDuty = true, Scale = 1.5f, GameOnly = true,
            Configure = (c, j) => { c.OnlyInDuty = true; j.HpPotionThreshold = 55f; },
        },
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

            var scenarios = AllScenarios.Where(s => Options.IsGame || !s.GameOnly).ToArray();
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var scenario in scenarios)
            {
                var name = $"{scenario.Name}-{scenario.Width}x{scenario.Height}";
                var result = RenderScenario(scenario, name);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Path))).ToLowerInvariant();
                if (!hashes.TryAdd(hash, name))
                {
                    if (!Options.IsGame)
                        throw new InvalidOperationException($"{name} is byte-identical to {hashes[hash]}.");
                    Console.WriteLine($"WARNING: {name} is byte-identical to {hashes[hash]}; the state it sets up is not visible.");
                }
                Console.WriteLine($"{name}: {Path.GetRelativePath(ProjectRoot, result.Path).Replace('\\', '/')} | {result.Triangles} triangles | {result.Bytes:N0} bytes | sha256 {hash[..16]}");
            }
            Console.WriteLine($"Rendered {scenarios.Length} real-source UI states ({hashes.Count} distinct). No window or game process was created.");

            if (_missingGlyphs && _strictGlyphs)
                throw new InvalidOperationException("--strict-glyphs: text the window draws contains characters the game font lacks.");
            VerifyCloseButton();
            Console.WriteLine("Custom close control: a press and release on the drawn button closes the window.");
            VerifyToggleInteraction();
            Console.WriteLine("Drawn toggle: a press and release changed the setting.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
    }

    private static RenderResult RenderScenario(Scenario scenario, string name)
    {
        Plugin.ResetState();
        var plugin = new Plugin { Config = new Configuration() };
        Plugin.Objects.LocalPlayer = scenario.HasPlayer
            ? new HarnessPlayer
            {
                ClassJob = new HarnessClassJob(scenario.JobId),
                IsDead = scenario.Dead,
                MaxHp = scenario.Dead ? 0u : 98_000u,
            }
            : null;
        if (scenario.InCombat)
            Plugin.Condition.Flags.Add(ConditionFlag.InCombat);
        if (scenario.InDuty)
            Plugin.Condition.Flags.Add(ConditionFlag.BoundByDuty);
        if (scenario.JobId == UnlistedJob)
            Plugin.Data.MissingJobs.Add(UnlistedJob);
        // The window edits the active job's profile, or the Default profile when no job is active.
        var job = scenario.HasPlayer ? plugin.Config.GetOrCreateJobSettings(scenario.JobId) : plugin.Config.DefaultJob;
        scenario.Configure?.Invoke(plugin.Config, job);

        var window = new ConfigWindow(plugin) { IsOpen = true };
        var size = new Vector2(scenario.Width, scenario.Height);
        var options = scenario.Scale is { } scale ? Options with { Scale = scale } : Options;
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
            // Frame plan: frame 0 measures; later frames settle window storage. The hover scenario holds the mouse over the
            // close button; scroll scenarios park the mouse over a column for a frame, send one large wheel event
            // that scrolls it to its end, then move the mouse away so no hover highlight is left in the image.
            var plan = new List<(Spot Mouse, float Wheel)> { (Spot.Away, 0f), (Spot.Away, 0f) };
            if (scenario.Hover)
                plan.Add((Spot.Close, 0f));
            if (scenario.ScrollsAny)
            {
                plan.Clear();
                plan.Add((Spot.Away, 0f));
                if (scenario.RulesWheel != 0f) { plan.Add((Spot.Rules, 0f)); plan.Add((Spot.Rules, scenario.RulesWheel)); plan.Add((Spot.Rules, 0f)); }
                if (scenario.RailWheel != 0f) { plan.Add((Spot.Rail, 0f)); plan.Add((Spot.Rail, scenario.RailWheel)); plan.Add((Spot.Rail, 0f)); }
                plan.Add((Spot.Away, 0f));
                plan.Add((Spot.Away, 0f));
            }
            var spots = new Dictionary<Spot, Vector2> { [Spot.Away] = new Vector2(-10000, -10000) };
            for (var index = 0; index < plan.Count; index++)
            {
                var (spot, wheel) = plan[index];
                io.MousePos = spots.TryGetValue(spot, out var position) ? position : spots[Spot.Away];
                if (wheel != 0f)
                    io.AddMouseWheelEvent(0f, wheel);
                ImGui.NewFrame();
                BeginRealWindow(window.WindowName, size, window.Flags);
                if (index == 0)
                {
                    var origin = ImGui.GetCursorScreenPos();
                    var avail = ImGui.GetContentRegionAvail().X;
                    spots[Spot.Close] = CloseButtonCenter();
                    // ConfigWindow.DrawWideBody: left column floor((avail - 12) * 0.68) wide, 12 px gap, rail takes the rest.
                    // The spots sit on a card edge, outside every control, so no hover highlight is drawn.
                    var y = scenario.Height * 0.55f;
                    var left = avail >= 960f ? MathF.Floor((avail - 12f) * 0.68f) : 0f;
                    spots[Spot.Rules] = new Vector2(origin.X + 3f, y);
                    spots[Spot.Rail] = left > 0f ? new Vector2(origin.X + left + 12f + 3f, y) : spots[Spot.Rules];
                }
                window.Draw();
                ImGui.End();
                ImGui.Render();
            }

            Fill(frame, 20, 20, 24);
            var drawData = ImGui.GetDrawData();
            if (!drawData.Valid || drawData.TotalVtxCount == 0)
                throw new InvalidOperationException($"{name}: the real Draw method produced no draw data.");
            var textures = new List<AtlasTexture>(look.Textures);
            if (scenario.HasPlayer && scenario.JobId != 0)
                textures.Add(IconTexture(scenario.JobId));
            var stats = CpuRasterizer.Render(drawData, frame, scenario.Width, scenario.Height, textures);
            var path = Path.Combine(OutputDirectory, name + ".png");
            PngWriter.Write(path, frame, scenario.Width, scenario.Height);
            return new RenderResult(path, new FileInfo(path).Length, stats.Triangles);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    /// <summary>Centre of the close button, derived from the window's content origin (top bar layout in ConfigWindow.DrawTopBar).</summary>
    private static Vector2 CloseButtonCenter()
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        const float barHeight = 66f, controlHeight = 34f;
        return origin + new Vector2(width - (controlHeight / 2f), ((barHeight - controlHeight) / 2f) + (controlHeight / 2f));
    }

    /// <summary>Placeholder art for a game job icon: a deterministic tinted tile, so the framed icon slot is filled like in game.</summary>
    private static AtlasTexture IconTexture(uint jobId)
    {
        const int Size = 40;
        var pixels = new byte[Size * Size * 4];
        var hue = (jobId * 47u) % 360u;
        var (r, g, b) = Hsv(hue, 0.55f, 0.85f);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var offset = ((y * Size) + x) * 4;
                var inner = Math.Abs(x - 19.5f) + Math.Abs(y - 19.5f) < 11f;
                var edge = x < 2 || y < 2 || x >= Size - 2 || y >= Size - 2;
                var shade = edge ? 0.45f : inner ? 1.15f : 0.8f;
                pixels[offset] = (byte)Math.Clamp(r * shade, 0, 255);
                pixels[offset + 1] = (byte)Math.Clamp(g * shade, 0, 255);
                pixels[offset + 2] = (byte)Math.Clamp(b * shade, 0, 255);
                pixels[offset + 3] = 255;
            }
        }
        return new AtlasTexture(new IntPtr(HarnessTextures.FirstIconTextureId + jobId), pixels, Size, Size);
    }

    private static (float R, float G, float B) Hsv(float hue, float saturation, float value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60f % 2) - 1));
        var m = value - c;
        var (r, g, b) = (int)(hue / 60f) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return ((r + m) * 255f, (g + m) * 255f, (b + m) * 255f);
    }

    /// <summary>
    /// Glyph audit: every non-ASCII character in the string literals of the drawn sources (the window, its chrome and
    /// the shared kit it draws with) must exist in the game font, otherwise ImGui draws the fallback glyph.
    /// <c>--strict-glyphs</c> turns a miss into a failure.
    /// </summary>
    private static void CheckGlyphs(RenderLook look)
    {
        var found = new SortedDictionary<string, SortedSet<string>>();
        var scanned = 0;
        var nonAscii = new SortedSet<int>();
        var root = typeof(Plugin).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "LalaSourceRoot")?.Value;
        var literal = new Regex("@?\"(?:[^\"\\\\\r\n]|\\\\.)*\"");
        if (root is not null)
        {
            var files = new List<string>
            {
                Path.Combine(root, "src", "AutoPotion", "AutoPotion", "ConfigWindow.cs"),
                Path.Combine(root, "src", "AutoPotion", "AutoPotion", "AutoPotionChrome.cs"),
            };
            var kit = Path.Combine(root, "src", "Shared", "LalaUiKit");
            if (Directory.Exists(kit))
                files.AddRange(Directory.EnumerateFiles(kit, "*.cs", SearchOption.AllDirectories));
            foreach (var file in files.Where(File.Exists))
            {
                scanned++;
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
                foreach (var rune in text.ToString().EnumerateRunes())
                    if (rune.Value > 0x7F)
                        nonAscii.Add(rune.Value);
                foreach (var entry in look.MissingGlyphs(text.ToString()))
                {
                    if (!found.TryGetValue(entry, out var sources))
                        found[entry] = sources = [];
                    sources.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
                }
            }
        }

        _missingGlyphs = found.Count > 0;
        Console.WriteLine($"Glyph check: scanned {scanned} source file(s); non-ASCII characters drawn: " +
                          string.Join(" ", nonAscii.Select(v => $"U+{v:X4}")));
        if (found.Count == 0)
        {
            Console.WriteLine("Glyph check: every drawn non-ASCII character exists in the game font.");
            return;
        }
        Console.WriteLine($"Glyph check: {found.Count} character(s) in drawn text have no glyph in the game font:");
        foreach (var (entry, sources) in found)
            Console.WriteLine($"  {entry} in {string.Join(", ", sources)}");
    }

    private static void VerifyCloseButton()
    {
        Plugin.ResetState();
        var plugin = new Plugin();
        var window = new ConfigWindow(plugin) { IsOpen = true };
        var size = new Vector2(WideW, WideH);
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = size;
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            _ = UiLook.Apply(Options);
            var target = Vector2.Zero;
            for (var step = 0; step < 4; step++)
            {
                io.MousePos = step == 0 ? new Vector2(-10000, -10000) : target;
                io.MouseDown[0] = step is 2;
                ImGui.NewFrame();
                BeginRealWindow(window.WindowName, size, window.Flags);
                if (step == 0)
                    target = CloseButtonCenter();
                window.Draw();
                ImGui.End();
                ImGui.Render();
            }
            if (window.IsOpen)
                throw new InvalidOperationException("The drawn close control did not close the window after a press and release.");
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    private static void VerifyToggleInteraction()
    {
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(480, 160);
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            _ = UiLook.Apply(Options);
            var value = false;
            var clickPos = Vector2.Zero;
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
                    clickPos = origin + new Vector2(ImGui.GetContentRegionAvail().X - 22, 12);
                }
                AutoPotionChrome.Toggle("probe", "Interaction setting", ref value);
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

    private static void BeginRealWindow(string name, Vector2 size, ImGuiWindowFlags windowFlags)
    {
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(size);
        var open = true;
        ImGui.Begin(name, ref open, windowFlags |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
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

    private enum Spot { Away, Close, Rules, Rail }

    private sealed record RenderResult(string Path, long Bytes, int Triangles);
}
