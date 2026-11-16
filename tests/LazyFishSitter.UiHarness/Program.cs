using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Lala.UiRender;
using LalaUiKit;
using LazyFishSitter;
using LazyFishSitter.Core;

namespace LazyFishSitterHarness;

/// <summary>
/// Renders the real LazyFishSitter ConfigWindow (the only window the plugin draws) to PNG. The live status texts come
/// from the real SitPolicy replaying a scripted fishing session; see Stubs.cs for what is faked.
/// To add a scenario: append one line to <see cref="Scenarios"/> and describe the session in a <c>Rig</c> script.
/// </summary>
internal static class Program
{
    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    private static string OutputDirectory = Path.Combine(ProjectRoot, "output");
    private static RenderOptions Options = RenderOptions.BuiltIn;
    private static bool _lookReported;
    private static bool _missingGlyphs;

    private enum SizeKind { FirstUse, Narrowest, Custom }

    private sealed record Scenario(string Name, SizeKind Kind, Action<Rig> Script)
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public float? Scale { get; init; }
        public string Shows { get; init; } = "";
    }

    /// <summary>A scripted fishing session: sets game state, then holds it for a number of seconds of fake clock.</summary>
    private sealed class Rig
    {
        private static readonly DateTime Epoch = new(2026, 11, 15, 20, 14, 7, DateTimeKind.Utc);
        private const double FrameSeconds = 0.25;
        private DateTime _clock = Epoch;

        public Plugin Plugin { get; } = new();
        public bool Fishing, CanFish, Changing, Seated;
        public FishState State = FishState.None;
        public string Mode = "Normal";
        public byte ModeValue = 1;

        public Rig Hold(double seconds)
        {
            var frames = Math.Max(1, (int)Math.Round(seconds / FrameSeconds));
            for (var i = 0; i < frames; i++)
            {
                var snapshot = new FishingSnapshot(
                    Fishing, true, State, Changing, CanFish, Mode, ModeValue, 0,
                    (ushort)(Seated ? 50 : 0), Seated ? "SittingOnGround" : "Normal", Seated, Seated);
                Plugin._service.Feed(snapshot, _clock);
                _clock = _clock.AddSeconds(FrameSeconds);
            }
            return this;
        }

        /// <summary>Walk up to a hole, take the rod out and wait on the standby beat; the policy sends one /sit.</summary>
        public Rig StandbyWithOneSend()
        {
            CanFish = true;
            Hold(2);
            Fishing = true;
            State = FishState.PoleReady;
            Mode = "Gathering";
            ModeValue = 6;
            return Hold(5);
        }

        /// <summary>The game takes the /sit: position change, then a seated read with the line out.</summary>
        public Rig GameSeatsCharacter()
        {
            Changing = true;
            Hold(0.5);
            Changing = false;
            Seated = true;
            State = FishState.LineInWater;
            return Hold(3);
        }
    }

    private static readonly Scenario[] Scenarios =
    [
        new("idle-first-run", SizeKind.FirstUse, r => r.Hold(1))
        {
            Shows = "first run, not at a fishing hole: Idle, auto-sit on, default /sit",
        },
        new("idle-first-run", SizeKind.Narrowest, r => r.Hold(1))
        {
            Shows = "first run at the narrowest allowed size: stacked top bar, stacked cards, tall footer",
        },
        new("armed-sent", SizeKind.FirstUse, r => r.StandbyWithOneSend())
        {
            Shows = "typical: at a hole, standing on the standby beat, one /sit sent (Ready, Armed)",
        },
        new("armed-hooking", SizeKind.Narrowest, r =>
        {
            r.StandbyWithOneSend();
            r.State = FishState.Hooking;
            r.Hold(1);
        })
        {
            Shows = "narrowest, Armed with the longest policy text in the last-check line (hooking state)",
        },
        new("armed-sent", SizeKind.Custom, r => r.StandbyWithOneSend())
        {
            Width = 700, Height = 560,
            Shows = "mid width between the 620/720 breakpoints: wide header with stacked cards",
        },
        new("seated", SizeKind.FirstUse, r => r.StandbyWithOneSend().GameSeatsCharacter())
        {
            Shows = "running well: the game took the /sit, seated read proven (Ready, Seated), long posture line",
        },
        new("seated", SizeKind.Narrowest, r => r.StandbyWithOneSend().GameSeatsCharacter())
        {
            Shows = "seated at the narrowest size: the long posture line wrapping in a narrow card",
        },
        new("seated-tall", SizeKind.Custom, r => r.StandbyWithOneSend().GameSeatsCharacter())
        {
            Width = 420, Height = 1300,
            Shows = "narrowest width, tall enough to show everything the scrolling body hides at 420x480",
        },
        new("blocked-combat", SizeKind.FirstUse, r =>
        {
            r.StandbyWithOneSend();
            Plugin.Condition.Set(ConditionFlag.InCombat, true);
            r.Hold(1);
        })
        {
            Shows = "blocked: a game condition holds the sit command (Blocked, in combat)",
        },
        new("blocked-cutscene", SizeKind.Narrowest, r =>
        {
            r.StandbyWithOneSend();
            Plugin.Condition.Set(ConditionFlag.OccupiedInCutSceneEvent, true);
            r.Hold(1);
        })
        {
            Shows = "blocked with the longest hold reason, at the narrowest size",
        },
        new("blocked-cap-reached", SizeKind.FirstUse, r =>
        {
            r.CanFish = true;
            r.Hold(2);
            r.Fishing = true;
            r.State = FishState.PoleReady;
            r.Mode = "Gathering";
            r.ModeValue = 6;
            r.Hold(36);
        })
        {
            Shows = "failed path: three unaccepted sends, retry limit reached (Blocked, 3 sends)",
        },
        new("blocked-cap-reached", SizeKind.Narrowest, r =>
        {
            r.CanFish = true;
            r.Hold(2);
            r.Fishing = true;
            r.State = FishState.PoleReady;
            r.Mode = "Gathering";
            r.ModeValue = 6;
            r.Hold(36);
        })
        {
            Shows = "retry limit reached at the narrowest size",
        },
        new("off-disabled", SizeKind.FirstUse, r =>
        {
            r.StandbyWithOneSend();
            r.Plugin.Config.Enabled = false;
            r.Hold(1);
        })
        {
            Shows = "auto-sit switched off: Needs setup, footer action reads Enable auto-sit, toggle off",
        },
        new("bad-command", SizeKind.FirstUse, r =>
        {
            r.Plugin.Config.SitCommand = "groundsit";
            r.StandbyWithOneSend();
        })
        {
            Shows = "needs setup: the sit command lacks its leading slash",
        },
        new("scale150-seated", SizeKind.FirstUse, r => r.StandbyWithOneSend().GameSeatsCharacter())
        {
            Scale = 1.5f,
            Shows = "evidence only: the seated state at global scale 1.5",
        },
    ];

    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            Options = RenderOptions.Resolve(args);
            var strictGlyphs = args.Contains("--strict-glyphs");
            if (Options.OutputSubdirectory.Length > 0)
                OutputDirectory = Path.Combine(OutputDirectory, Options.OutputSubdirectory);
            Directory.CreateDirectory(OutputDirectory);

            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            var count = 0;
            foreach (var scenario in Scenarios)
            {
                if (!Options.IsGame && scenario.Scale is not null)
                    continue; // scale only exists in game mode
                var result = RenderScenario(scenario);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Path))).ToLowerInvariant();
                if (!hashes.TryAdd(hash, result.Name))
                {
                    if (!Options.IsGame)
                        throw new InvalidOperationException($"{result.Name} is byte-identical to {hashes[hash]}.");
                    Console.WriteLine($"WARNING: {result.Name} is byte-identical to {hashes[hash]}.");
                }
                count++;
                Console.WriteLine($"{result.Name}: {result.Width}x{result.Height} | {result.Triangles} triangles | sha256 {hash[..16]} | {scenario.Shows}");
            }
            Console.WriteLine($"Rendered {count} real-source UI states ({hashes.Count} distinct). No window or game process was created.");
            if (_missingGlyphs && strictGlyphs)
                throw new InvalidOperationException("--strict-glyphs: text the window draws contains characters the game font lacks.");
            VerifyToggleInteraction();
            Console.WriteLine("Interactive drawn toggle: press and release changed the setting through FishSitterChrome.Toggle.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
    }

    private sealed record RenderResult(string Name, string Path, int Width, int Height, int Triangles);

    private static RenderResult RenderScenario(Scenario scenario)
    {
        Plugin.Condition.Clear();
        var rig = new Rig();
        scenario.Script(rig);
        var window = new ConfigWindow(rig.Plugin);

        // Sizes come from the real window: first-use Size and the SizeConstraints minimum.
        var first = window.Size ?? throw new InvalidOperationException("ConfigWindow no longer declares a Size.");
        var minimum = window.SizeConstraints?.MinimumSize ?? first * 0.6f;
        var size = scenario.Kind switch
        {
            SizeKind.FirstUse => first,
            SizeKind.Narrowest => minimum,
            _ => new Vector2(scenario.Width, scenario.Height),
        };
        var width = (int)size.X;
        var height = (int)size.Y;
        var name = $"{scenario.Name}-{width}x{height}";
        if (scenario.Kind == SizeKind.Narrowest && !scenario.Name.StartsWith("scale", StringComparison.Ordinal))
            name = $"{scenario.Name}-min-{width}x{height}";

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

            for (var frame = 0; frame < 2; frame++)
            {
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

            var pixels = new byte[width * height * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 20; pixels[i + 1] = 20; pixels[i + 2] = 24; pixels[i + 3] = 255;
            }
            var drawData = ImGui.GetDrawData();
            if (!drawData.Valid || drawData.TotalVtxCount == 0)
                throw new InvalidOperationException($"{name}: the real Draw method produced no draw data.");
            var stats = CpuRasterizer.Render(drawData, pixels, width, height, look.Textures);
            var path = Path.Combine(OutputDirectory, name + ".png");
            PngWriter.Write(path, pixels, width, height);
            return new RenderResult(name, path, width, height, stats.Triangles);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    /// <summary>
    /// Heuristic glyph audit: every non-ASCII character in the string literals of the window, chrome, policy text and
    /// shared kit sources must exist in the active font, otherwise ImGui draws the fallback glyph. <c>--strict-glyphs</c>
    /// turns a miss into a failure.
    /// </summary>
    private static void CheckGlyphs(RenderLook look)
    {
        var found = new SortedDictionary<string, SortedSet<string>>();
        var nonAscii = new SortedSet<string>();
        var root = typeof(Plugin).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "LalaSourceRoot")?.Value;
        var literal = new System.Text.RegularExpressions.Regex("@?\"(?:[^\"\\\\\r\n]|\\\\.)*\"");
        var files = new List<string>();
        if (root is not null)
        {
            foreach (var folder in new[] { Path.Combine("src", "LazyFishSitter"), Path.Combine("src", "Shared", "LalaUiKit") })
            {
                var directory = Path.Combine(root, folder);
                if (!Directory.Exists(directory))
                    continue;
                files.AddRange(Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")));
            }
        }

        foreach (var file in files)
        {
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
            var relative = Path.GetRelativePath(root!, file).Replace('\\', '/');
            foreach (var rune in text.ToString().EnumerateRunes().Where(r => r.Value > 127))
                nonAscii.Add($"U+{rune.Value:X4} {rune}");
            foreach (var entry in look.MissingGlyphs(text.ToString()))
            {
                if (!found.TryGetValue(entry, out var sources))
                    found[entry] = sources = [];
                sources.Add(relative);
            }
        }

        // Text the plugin builds at run time from non-literal parts is ASCII (enum names, numbers, HH:mm:ss).
        Console.WriteLine($"Glyph check: scanned {files.Count} source file(s); non-ASCII characters in drawn literals: {(nonAscii.Count == 0 ? "none" : string.Join(", ", nonAscii))}.");
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
                const float width = 300;
                if (step == 0)
                    clickPos = ImGui.GetCursorScreenPos() + new Vector2(width - 20, 12);
                FishSitterChrome.Toggle("Sit while fishing", ref value, null, width);
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
}
