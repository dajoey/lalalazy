using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CurrencySpender;
using CurrencySpender.Classes;
using CurrencySpender.Configuration;
using CurrencySpender.Data;
using CurrencySpender.Helpers;
using CurrencySpender.Windows;
using Dalamud.Bindings.ImGui;
using Lala.UiRender;
using static CurrencySpender.Plugin;

namespace LazyCurrencySpender.UiHarness;

internal static unsafe class Program
{
    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    private static string OutputDirectory = Path.Combine(ProjectRoot, "output");
    private static RenderOptions Options = RenderOptions.BuiltIn;
    private static bool _missingGlyphs;
    private static bool _strictGlyphs;
    private static bool _lookReported;

    private enum Kind { Main, Config, Spending, Wizard, Debug, Overlay }

    // Window minimum sizes come from each window's SizeConstraints in the real source; no window sets Size, so the
    // first-use size is the minimum. WIDE sizes stand for a window the player has resized.
    private sealed record Scenario(string Name, Kind Kind, int Width, int Height, Action Setup)
    {
        public float Scale { get; init; }
        public string FileName => $"{Name}-{Width}x{Height}";
    }

    private static readonly uint[] TypicalSelection =
        [20, 21, 22, 29, 28, 47, 25, 27, 10307, 26807, 33913, 41785, 37549, 45691, 21076, 22525];

    // ---- state helpers ----------------------------------------------------------------------------------------

    private static void Fresh()
    {
        FakeService.LoggedIn = true;
        P = new Plugin
        {
            config = new Config { Version = "1.3.1", Separator = 2 },
            Currencies = Fixtures.BuildCurrencies(),
            changelogPath = Path.Combine(SourceRoot, "src", "LazyCurrencySpender", "CHANGELOG.md"),
        };
        foreach (CollectableType type in Enum.GetValues(typeof(CollectableType)))
            if (type is not (CollectableType.None or CollectableType.Container))
                C.SelectedCollectableTypes.Add(type);
        C.SelectedCurrencies = [.. TypicalSelection];
        Fixtures.BuildItems();
        PlayerHelper.GCRanks = new() { { 1, 99 }, { 2, 99 }, { 3, 99 } };
        PlayerHelper.GCRanksCreated = true;
        PlayerHelper.SharedFateRanks = new();
        PlayerHelper.SharedFateRanksMax = true;
        ItemHelper.ContainerUnlocked = new() { { 10128, (4, 15) }, { 10129, (9, 14) } };
        MainTab.NotUpdated = true;
        MainTab.MissingCollectables.Clear();
        SpendingWindow.Currency = null;
        SetStatic(typeof(ItemsTab), "SearchQuery", "");
        SetStatic(typeof(ItemsTab), "FilteredItems", new List<ShopItem>());
        SetStatic(typeof(ConfigWizardWindow), "Step", 0);
        GenericHelpersAddon(false);

        P.spendingWindow = new SpendingWindow();
        P.debugTabWindow = new DebugTabWindow();
        P.mainTabWindow = new MainTabWindow();
        P.configTabWindow = new ConfigTabWindow();
        P.configWizard = new ConfigWizardWindow();
        P.currencyOverlay = new CurrencyOverlay();
        P.configWizard.SetVersion("1.0.0");
        C.ItemsOfInterest = [22009, 28007, 26002, 26007, 22010];
    }

    private static string SourceRoot => typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .First(a => a.Key == "LalaSourceRoot").Value!;

    private static void SetStatic(Type type, string field, object? value)
        => type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(null, value);

    private static void SetInstance(object target, string field, object? value)
        => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static void SetSection(object window, string name)
    {
        var nested = window.GetType().GetNestedType("Section", BindingFlags.NonPublic)!;
        SetInstance(window, "_section", Enum.Parse(nested, name));
    }

    private static void Minimize(object window)
    {
        var controls = window.GetType().GetField("_controls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        controls.GetType().GetMethod("ToggleMinimize", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controls, null);
    }

    private static TrackedCurrency Cur(uint id) => P.GetCurrency(id)!;

    private static void GenericHelpersAddon(bool present)
    {
        ECommons.GenericHelpers.CurrencyAddon = null;
        if (present)
        {
            var addon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)System.Runtime.InteropServices.NativeMemory.AllocZeroed(8);
            addon->X = 0; // the overlay only repositions itself when the addon is not at the origin
            addon->Y = 0;
            ECommons.GenericHelpers.CurrencyAddon = addon;
        }
    }

    private static void OpenSpending(uint currencyId) => P.spendingWindow.GetData(Cur(currencyId));

    // ---- scenarios ---------------------------------------------------------------------------------------------

    private static Scenario[] BuildScenarios()
    {
        var list = new List<Scenario>
        {
            // Main window (min 460x320).
            new("main-currencies-typical", Kind.Main, 1000, 1000, () => { }),
            new("main-currencies-min", Kind.Main, 460, 320, () => { }),
            new("main-first-run-empty", Kind.Main, 1000, 640, () => { C.SelectedCurrencies.Clear(); }),
            new("main-logged-out", Kind.Main, 1000, 560, () => FakeService.LoggedIn = false),
            new("main-shared-fate-problem", Kind.Main, 1000, 1100, () => { P.Problem = true; C.SelectedCurrencies.Add(26807); }),
            new("main-instructions", Kind.Main, 1000, 980, () => SetSection(P.mainTabWindow, "Instructions")),
            new("main-minimized", Kind.Main, 760, 69, () => Minimize(P.mainTabWindow)),
            new("scale150-main-currencies-typical", Kind.Main, 1000, 1100, () => { }) { Scale = 1.5f },

            // Settings window (min 520x360).
            new("config-settings", Kind.Config, 1100, 940, () => { }),
            new("config-settings-stacked", Kind.Config, 640, 1900, () => { }),
            new("config-settings-min", Kind.Config, 520, 360, () => { }),
            new("config-currencies", Kind.Config, 1100, 1180, () => SetSection(P.configTabWindow, "Currencies")),
            new("config-items-tracked", Kind.Config, 1100, 760, () =>
            {
                SetSection(P.configTabWindow, "Items");
                SetStatic(typeof(ItemsTab), "SearchQuery", "wind");
                SetStatic(typeof(ItemsTab), "FilteredItems",
                    Generator.items.Where(i => i.Name.Contains("wind", StringComparison.OrdinalIgnoreCase)).GroupBy(i => i.Name).Select(g => g.First()).ToList());
            }),
            new("config-items-first-run-empty", Kind.Config, 1100, 520, () => { SetSection(P.configTabWindow, "Items"); C.ItemsOfInterest.Clear(); }),
            new("config-changelog", Kind.Config, 1100, 900, () => SetSection(P.configTabWindow, "Changelog")),
            new("config-about", Kind.Config, 1100, 780, () => SetSection(P.configTabWindow, "About")),
            new("config-debug", Kind.Config, 1100, 820, () =>
            {
                C.Debug = true;
                PlayerHelper.GCRanks = new() { { 1, 10 }, { 2, 7 }, { 3, 9 } };
                PlayerHelper.SharedFateRanks = new() { { 1187, 4 }, { 1188, 5 } };
                SetSection(P.configTabWindow, "Debug");
            }),

            // Spending guide (min 720x360).
            new("spending-flame-seals", Kind.Spending, 1180, 1100, () =>
            {
                PlayerHelper.GCRanks = new() { { 1, 10 }, { 2, 10 }, { 3, 7 } };
                OpenSpending(22);
            }),
            new("spending-bicolor-gemstones", Kind.Spending, 1180, 1200, () =>
            {
                PlayerHelper.SharedFateRanksMax = false;
                C.ItemsOfInterest = [26002, 26007];
                OpenSpending(26807);
            }),
            new("spending-min-debug", Kind.Spending, 720, 360, () =>
            {
                C.Debug = true;
                PlayerHelper.GCRanks = new() { { 1, 10 }, { 2, 10 }, { 3, 7 } };
                OpenSpending(22);
            }),

            // Configuration wizard (min 520x420).
            new("wizard-welcome-min", Kind.Wizard, 520, 420, () => { }),
            new("wizard-step1-currencies", Kind.Wizard, 900, 860, () => SetStatic(typeof(ConfigWizardWindow), "Step", 1)),
            new("wizard-step2-collectables", Kind.Wizard, 900, 760, () => SetStatic(typeof(ConfigWizardWindow), "Step", 2)),
            new("wizard-last-step-min", Kind.Wizard, 520, 420, () => SetStatic(typeof(ConfigWizardWindow), "Step", 6)),

            // Debug data window (min 520x320).
            new("debug-window", Kind.Debug, 1000, 960, () => C.Debug = true),

            // Overlay next to the game's Currency window.
            new("overlay-open-button", Kind.Overlay, 340, 100, () => GenericHelpersAddon(true)),
        };
        return [.. list];
    }

    // ---- main ----------------------------------------------------------------------------------------------------

    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            Options = RenderOptions.Resolve(args);
            _strictGlyphs = args.Contains("--strict-glyphs");
            if (!Options.IsGame)
            {
                Console.WriteLine("Built-in mode: no scenarios are defined for this harness; run with --game for the render set.");
                return 0;
            }
            OutputDirectory = Path.Combine(OutputDirectory, Options.OutputSubdirectory);
            Directory.CreateDirectory(OutputDirectory);

            var scenarios = BuildScenarios();
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var scenario in scenarios)
            {
                var (path, triangles, bytes) = Render(scenario);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
                if (!hashes.TryAdd(hash, scenario.Name))
                    Console.WriteLine($"WARNING: {scenario.Name} is byte-identical to {hashes[hash]}; the state it sets up is not visible.");
                Console.WriteLine($"{scenario.FileName}: {Path.GetRelativePath(ProjectRoot, path).Replace('\\', '/')} | {triangles} triangles | {bytes:N0} bytes | sha256 {hash[..16]}");
            }
            Console.WriteLine($"Rendered {scenarios.Length} real-source UI states ({hashes.Count} distinct). No window or game process was created.");
            if (_missingGlyphs && _strictGlyphs)
                throw new InvalidOperationException("--strict-glyphs: text the windows draw contains characters the game font lacks.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
    }

    private static (string Path, int Triangles, long Bytes) Render(Scenario scenario)
    {
        Fresh();
        scenario.Setup();
        var options = scenario.Scale > 0 ? Options with { Scale = scenario.Scale } : Options;
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
                CheckGlyphs(look);
            }

            for (var frame = 0; frame < 2; frame++)
            {
                ImGui.NewFrame();
                switch (scenario.Kind)
                {
                    case Kind.Main: DrawWindow(P.mainTabWindow, size, () => P.mainTabWindow.Draw(), P.mainTabWindow.PreDraw); break;
                    case Kind.Config: DrawWindow(P.configTabWindow, size, () => P.configTabWindow.Draw(), P.configTabWindow.PreDraw); break;
                    case Kind.Spending: DrawWindow(P.spendingWindow, size, () => P.spendingWindow.Draw(), P.spendingWindow.PreDraw); break;
                    case Kind.Wizard: DrawWindow(P.configWizard, size, () => P.configWizard.Draw(), P.configWizard.PreDraw); break;
                    case Kind.Debug: DrawWindow(P.debugTabWindow, size, () => P.debugTabWindow.Draw(), P.debugTabWindow.PreDraw); break;
                    case Kind.Overlay: DrawOverlay(); break;
                }
                ImGui.Render();
            }

            var pixels = new byte[scenario.Width * scenario.Height * 4];
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 20; pixels[i + 1] = 20; pixels[i + 2] = 24; pixels[i + 3] = 255; }
            var drawData = ImGui.GetDrawData();
            if (!drawData.Valid || drawData.TotalVtxCount == 0)
                throw new InvalidOperationException($"{scenario.Name}: the real Draw method produced no draw data.");
            var textures = look.Textures.Concat(Fixtures.IconTextures).ToList();
            var stats = CpuRasterizer.Render(drawData, pixels, scenario.Width, scenario.Height, textures);
            var path = Path.Combine(OutputDirectory, scenario.FileName + ".png");
            PngWriter.Write(path, pixels, scenario.Width, scenario.Height);
            return (path, stats.Triangles, new FileInfo(path).Length);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    private static void DrawWindow(Window window, Vector2 size, Action draw, Action preDraw)
    {
        preDraw();
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(size);
        var open = true;
        ImGui.Begin(window.WindowName, ref open, window.Flags |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
        draw();
        ImGui.End();
    }

    private static void DrawOverlay()
    {
        var window = P.currencyOverlay;
        if (!window.DrawConditions())
            throw new InvalidOperationException("The overlay did not accept the stubbed Currency addon.");
        ImGui.SetNextWindowPos(new Vector2(24, 24));
        var open = true;
        window.PreDraw();
        ImGui.Begin("Currency overlay###harness", ref open, window.Flags | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
        window.Draw();
        ImGui.End();
        window.PostDraw();
    }

    // Every non-ASCII character in strings the windows, the kit, the changelog and the fixtures can draw must exist in
    // the game font; otherwise ImGui draws the fallback glyph. Printed as a report; --strict-glyphs fails on a miss.
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

        var root = SourceRoot;
        var literal = new Regex("@?\"(?:[^\"\\\\\r\n]|\\\\.)*\"");
        var scanned = new SortedDictionary<string, SortedSet<char>>();
        foreach (var folder in new[] { Path.Combine("src", "LazyCurrencySpender", "Windows"), Path.Combine("src", "LazyCurrencySpender", "Helpers"), Path.Combine("src", "Shared", "LalaUiKit") })
        {
            var directory = Path.Combine(root, folder);
            if (!Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
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
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                Scan(relative, text.ToString());
                foreach (var ch in text.ToString().Where(c => c > 127))
                {
                    if (!scanned.TryGetValue(ch.ToString(), out var files)) scanned[ch.ToString()] = files = [];
                    files.Add(Path.GetFileName(file)[0]);
                }
            }
        }
        var changelog = Path.Combine(root, "src", "LazyCurrencySpender", "CHANGELOG.md");
        if (File.Exists(changelog))
            Scan("CHANGELOG.md (Changelog tab)", File.ReadAllText(changelog));

        var characters = new SortedSet<string>(scanned.Keys);
        Console.WriteLine($"Glyph check: scanned UI string literals; non-ASCII characters found: {string.Join(" ", characters.Select(c => $"U+{(int)c[0]:X4}{c}"))}");
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
