using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using Dalamud.Bindings.ImGui;
using Lala.UiRender;
using LazyGearCollector;

namespace LazyGearCollector.UiHarness;

internal static class Program
{
    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    private static string OutputDirectory = Path.Combine(ProjectRoot, "output");
    private static RenderOptions Options = RenderOptions.BuiltIn;
    private static bool _strictGlyphs;
    private static bool _missingGlyphs;
    private static bool _lookReported;

    // The window opens at 1100x720 on first use (SizeCondition FirstUseEver) and its minimum size is 560x480.
    private const int DefaultWidth = 1100, DefaultHeight = 720, MinWidth = 560, MinHeight = 480;

    private sealed record Scenario(string Name, int Width, int Height, Action<Ctx> Setup)
    {
        public Vector2? Mouse { get; init; }
        public float? Scale { get; init; }
        public bool GameOnly { get; init; }
        /// <summary>Mouse wheel notches applied on the second frame (negative scrolls down); needs Mouse over the scrolling body.</summary>
        public float Wheel { get; init; }
    }

    /// <summary>What a scenario can set up: plugin state (config, ownership, collection) and window page state.</summary>
    private sealed class Ctx(Plugin plugin, CollectorWindow window)
    {
        public Plugin Plugin { get; } = plugin;
        public CollectorWindow Window { get; } = window;

        public void Page(string group, string overviewPage = "Summary", string? role = null, string search = "")
        {
            SetField("_group", Enum.Parse(Field("_group").FieldType, group));
            SetField("_overviewPage", Enum.Parse(Field("_overviewPage").FieldType, overviewPage));
            SetField("_selectedRole", role);
            SetField("_searchText", search);
        }

        private FieldInfo Field(string name) =>
            typeof(CollectorWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"CollectorWindow has no field {name}; update the harness.");

        private void SetField(string name, object? value) => Field(name).SetValue(Window, value);

        /// <summary>Own one tier of a piece (-1 = nothing of that chain).</summary>
        public void Own(string role, string slot, int tier)
        {
            if (tier < 0)
                return;
            Plugin.Ownership.Live[Data.Item(Data.IndexOf(role, slot), tier)] = 1;
        }

        public void OwnArcanaut(string role, string slot) =>
            Plugin.Ownership.Live[Data.TradeIds(Data.IndexOf(role, slot)).ArcanautId] = 1;

        public void OwnLunar(string role, string slot) =>
            Plugin.Ownership.Live[Data.TradeIds(Data.IndexOf(role, slot)).LunarId] = 1;

        public void Wallet(uint currency, int count) => Plugin.Ownership.Live[currency] = count;

        public void OwnAll(int tier)
        {
            foreach (var (role, slot, _) in Data.AllPieces())
                Own(role, slot, tier);
        }

        /// <summary>A mid-progress account: every role in a different state, two trade-up routes.</summary>
        public void Typical()
        {
            var plan = new (string Role, int[] Tiers)[]
            {
                ("Fending", [3, 3, 2, 3, 1]),
                ("Maiming", [3, 2, 3, 2, 2]),
                ("Striking", [1, 1, 0, -1, 1]),
                ("Scouting", [3, 3, 3, 3, 3]),
                ("Aiming", [2, 2, 1, 2, 0]),
                ("Healing", [-1, 0, 1, -1, 0]),
                ("Casting", [0, 3, 2, 1, -1]),
                ("Slaying", [1, 0, -1, 2]),
            };
            var slots = new Dictionary<string, string[]>
            {
                ["Slaying"] = ["Ears", "Neck", "Wrists", "Ring"],
            };
            foreach (var (role, tiers) in plan)
            {
                var names = slots.TryGetValue(role, out var s) ? s : new[] { "Head", "Body", "Hands", "Legs", "Feet" };
                for (var i = 0; i < tiers.Length; i++)
                    Own(role, names[i], tiers[i]);
            }
            // A free trade-up (holding the older family's piece) and a two-step route.
            OwnArcanaut("Healing", "Head");
            OwnLunar("Casting", "Feet");
            OwnArcanaut("Casting", "Head");
            Wallet(Data.SilverObol, 1840);
            Wallet(Data.GoldObol, 212);
            Wallet(Data.Fixative, 38);
            Wallet(Data.LunarResin, 11);
        }

        public void Containers()
        {
            Plugin.Config.Snapshots["saddlebag"] = new ContainerSnapshot
            {
                Label = "Saddlebag",
                Counts = new() { [Data.GoldObol] = 140, [Data.Fixative] = 12 },
                SeenUtc = new DateTime(2026, 11, 12, 21, 40, 0, DateTimeKind.Utc),
            };
            Plugin.Config.Snapshots["saddlebag-premium"] = new ContainerSnapshot
            {
                Label = "Premium saddlebag",
                Counts = new() { [Data.Item(Data.IndexOf("Maiming", "Hands"), 2)] = 1, [Data.SilverObol] = 620 },
                SeenUtc = new DateTime(2026, 11, 12, 21, 41, 0, DateTimeKind.Utc),
            };
            Plugin.Config.Snapshots["retainer:1:1"] = new ContainerSnapshot
            {
                Label = "Hollyberry",
                Counts = new() { [Data.LunarResin] = 4 },
                SeenUtc = new DateTime(2026, 11, 14, 8, 5, 0, DateTimeKind.Utc),
            };
            Plugin.Config.Snapshots["retainer:2:1"] = new ContainerSnapshot
            {
                Label = "Wanderingstar Tidewalker",
                Counts = new() { [Data.Item(Data.IndexOf("Casting", "Head"), 0)] = 1, [Data.Aethersand] = 2 },
                SeenUtc = new DateTime(2026, 11, 9, 18, 22, 0, DateTimeKind.Utc),
            };
        }
    }

    private static Scenario[] BuildScenarios()
    {
        var s = new List<Scenario>
        {
            // First run: the game data held no tracked set, so there is no collection at all.
            new("empty-no-collection", DefaultWidth, DefaultHeight, c =>
            {
                c.Plugin.Collections.Clear();
            }) { },
            new("empty-no-collection-narrow", MinWidth, MinHeight, c => c.Plugin.Collections.Clear()),

            // Typical use.
            new("summary-typical", DefaultWidth, DefaultHeight, c => { c.Typical(); c.Containers(); }),
            new("summary-typical-narrow", MinWidth, MinHeight, c => { c.Typical(); c.Containers(); }),
            new("summary-typical-narrow-scrolled", MinWidth, MinHeight, c => { c.Typical(); c.Containers(); }) { Mouse = new Vector2(280, 300), Wheel = -90 },
            new("summary-complete", DefaultWidth, DefaultHeight, c => c.OwnAll(3)),
            new("summary-loggedout-idle", DefaultWidth, DefaultHeight, c =>
            {
                Plugin.ClientState.IsLoggedIn = false;
                c.Typical();
                c.Containers();
            }),
            new("materials-typical", DefaultWidth, DefaultHeight, c =>
            {
                c.Typical();
                c.Containers();
                c.Page("Overview", "Materials");
            }),
            new("role-casting-typical", DefaultWidth, DefaultHeight, c =>
            {
                c.Typical();
                c.Containers();
                c.Page("Roles", role: "Casting");
            }),
            new("role-casting-typical-narrow", MinWidth, MinHeight, c =>
            {
                c.Typical();
                c.Containers();
                c.Page("Roles", role: "Casting");
            }),
            new("role-casting-scrolled", DefaultWidth, DefaultHeight, c =>
            {
                c.Typical();
                c.Containers();
                c.Page("Roles", role: "Casting");
            }) { Mouse = new Vector2(500, 560), Wheel = -90 },
            new("role-fending-nothing-owned", DefaultWidth, DefaultHeight, c => c.Page("Roles", role: "Fending")),
            new("role-casting-ownership-tooltip", DefaultWidth, DefaultHeight, c =>
            {
                c.Typical();
                c.Containers();
                c.Page("Roles", role: "Casting");
            }) { Mouse = new Vector2(80, 404) },
            new("settings-containers", DefaultWidth, DefaultHeight, c =>
            {
                c.Typical();
                c.Containers();
                c.Plugin.Config.TargetTier = 2;
                c.Page("Settings");
            }),
            new("settings-empty-narrow", MinWidth, MinHeight, c =>
            {
                c.Typical();
                c.Plugin.Config.IncludeCachedContainers = false;
                c.Page("Settings");
            }),
            new("search-results", DefaultWidth, DefaultHeight, c =>
            {
                c.Typical();
                c.Page("Overview", search: "heal");
            }),
            new("search-no-match-narrow", MinWidth, MinHeight, c =>
            {
                c.Typical();
                c.Page("Overview", search: "warrior");
            }),
            new("scale150-summary-typical", DefaultWidth, DefaultHeight, c => { c.Typical(); c.Containers(); }) { Scale = 1.5f, GameOnly = true },
        };
        return [.. s.Where(x => Options.IsGame || !x.GameOnly)];
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

            var scenarios = BuildScenarios();
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var scenario in scenarios)
            {
                var result = Render(scenario);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Path))).ToLowerInvariant();
                if (!hashes.TryAdd(hash, scenario.Name))
                {
                    // Hover and scroll scenarios aim at game-mode positions; in built-in mode the metrics differ, so report only.
                    Console.WriteLine($"WARNING: {scenario.Name} is byte-identical to {hashes[hash]}.");
                }
                Console.WriteLine($"{scenario.Name}: {Path.GetRelativePath(ProjectRoot, result.Path).Replace('\\', '/')} | {scenario.Width}x{scenario.Height} | {result.Triangles} triangles | sha256 {hash[..16]}");
            }
            Console.WriteLine($"Rendered {scenarios.Length} real-source UI states ({hashes.Count} distinct).");
            if (_missingGlyphs && _strictGlyphs)
                throw new InvalidOperationException("--strict-glyphs: text the window draws contains characters the game font lacks.");
            VerifySettingsInteraction();
            Console.WriteLine("Interactive drawn controls: target tier button and cached-container toggle changed the real configuration.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
    }

    private static (string Path, int Triangles) Render(Scenario scenario)
    {
        Plugin.ClientState.IsLoggedIn = true;
        var plugin = new Plugin();
        plugin.Collections.Add(Data.Build(plugin.Shops));
        var window = new CollectorWindow(plugin);
        var ctx = new Ctx(plugin, window);
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
            io.MousePos = scenario.Mouse ?? new Vector2(-10000, -10000);
            var look = UiLook.Apply(options);
            if (!_lookReported)
            {
                _lookReported = true;
                Console.WriteLine($"Look: {look.Description}");
                if (Options.IsGame)
                    CheckGlyphs(look, plugin);
            }

            for (var frame = 0; frame < 6; frame++)
            {
                if (scenario.Mouse is { } mouse)
                    io.AddMousePosEvent(mouse.X, mouse.Y);
                if (frame is >= 1 and <= 3 && scenario.Wheel != 0)
                    io.AddMouseWheelEvent(0, scenario.Wheel / 3f);
                ImGui.NewFrame();
                Begin(window.WindowName, size, window.Flags);
                window.Draw();
                ImGui.End();
                ImGui.Render();
            }

            var pixels = new byte[scenario.Width * scenario.Height * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 20; pixels[i + 1] = 20; pixels[i + 2] = 24; pixels[i + 3] = 255;
            }
            var drawData = ImGui.GetDrawData();
            if (!drawData.Valid || drawData.TotalVtxCount == 0)
                throw new InvalidOperationException($"{scenario.Name}: the real Draw method produced no draw data.");
            var stats = CpuRasterizer.Render(drawData, pixels, scenario.Width, scenario.Height, look.Textures);
            var path = Path.Combine(OutputDirectory, scenario.Name + ".png");
            PngWriter.Write(path, pixels, scenario.Width, scenario.Height);
            return (path, stats.Triangles);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    private static void Begin(string name, Vector2 size, ImGuiWindowFlags flags)
    {
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(size);
        var open = true;
        ImGui.Begin(name, ref open, flags | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove |
                                    ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
    }

    /// <summary>
    /// Every non-ASCII character the plugin's drawn strings can contain must exist in the game font, otherwise ImGui
    /// draws the fallback mark. Scans string literals of the window, chrome, planner and shared kit sources plus the
    /// sample data names. <c>--strict-glyphs</c> turns a miss into a failure.
    /// </summary>
    private static void CheckGlyphs(RenderLook look, Plugin plugin)
    {
        var found = new SortedDictionary<string, SortedSet<string>>();
        var scanned = 0;
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
        var literal = new System.Text.RegularExpressions.Regex("@?\"(?:[^\"\\\r\n]|\\.)*\"");
        var files = new List<string>();
        if (root is not null)
        {
            foreach (var name in new[] { "CollectorWindow.cs", "CollectorChrome.cs", "Planner.cs", "CollectionModels.cs" })
                files.Add(Path.Combine(root, "src", "LazyGearCollector", name));
            var kit = Path.Combine(root, "src", "Shared", "LalaUiKit");
            if (Directory.Exists(kit))
                files.AddRange(Directory.EnumerateFiles(kit, "*.cs", SearchOption.AllDirectories));
        }
        foreach (var file in files.Where(File.Exists))
        {
            scanned++;
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
        Scan("sample data names", string.Concat(plugin.Shops.Names.Values.Select(n => n + "\n")));

        _missingGlyphs = found.Count > 0;
        if (found.Count == 0)
        {
            Console.WriteLine($"Glyph check: every drawn non-ASCII character exists in the game font ({scanned} source files scanned).");
            return;
        }
        Console.WriteLine($"Glyph check: {found.Count} character(s) in drawn text have no glyph in the game font:");
        foreach (var (entry, sources) in found)
            Console.WriteLine($"  {entry} in {string.Join(", ", sources)}");
    }

    /// <summary>Drives the real drawn controls with a pointer press and release, so a polish pass cannot break them silently.</summary>
    private static void VerifySettingsInteraction()
    {
        var plugin = new Plugin();
        plugin.Collections.Add(Data.Build(plugin.Shops));
        plugin.Config.TargetTier = 3;
        plugin.Config.IncludeCachedContainers = true;
        var window = new CollectorWindow(plugin);
        new Ctx(plugin, window).Page("Settings");
        var size = new Vector2(DefaultWidth, DefaultHeight);
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = size;
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            _ = UiLook.Apply(Options);
            // Settings card positions are found by sweeping candidate points: press and release at each, stop when the
            // target tier changed, then do the same for the toggle. Deterministic and independent of the exact layout.
            var tierChanged = false;
            var toggled = false;
            for (var y = 200; y < 700 && !(tierChanged && toggled); y += 8)
            {
                for (var x = 40; x < 1000 && !(tierChanged && toggled); x += 16)
                {
                    foreach (var down in new[] { false, true, false })
                    {
                        io.MousePos = new Vector2(x, y);
                        io.MouseDown[0] = down;
                        ImGui.NewFrame();
                        Begin(window.WindowName, size, window.Flags);
                        window.Draw();
                        ImGui.End();
                        ImGui.Render();
                    }
                    tierChanged |= plugin.Config.TargetTier != 3;
                    toggled |= !plugin.Config.IncludeCachedContainers;
                }
            }
            if (!tierChanged)
                throw new InvalidOperationException("No press changed the target tier through the real drawn buttons.");
            if (!toggled)
                throw new InvalidOperationException("No press changed the cached-container toggle through the real drawn switch.");
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }
}
