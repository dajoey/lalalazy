using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using Lala.UiRender;
using RotationSolver;
using RotationSolver.Basic;
using RotationSolver.Basic.Configuration;
using RotationSolver.Basic.Configuration.RotationConfig;
using RotationSolver.Basic.Data;
using RotationSolver.Decisions;
using RotationSolver.Helpers;
using RotationSolver.UI.Pvp;
using RotationSolver.Updaters;

namespace PvPSolver.UiHarness;

internal static class Program
{
    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    private static string OutputDirectory = Path.Combine(ProjectRoot, "output");
    private static RenderOptions Options = RenderOptions.BuiltIn;
    private static bool _lookReported;
    private static bool _missingGlyphs;
    private static bool _strictGlyphs;
    private static string SourceRoot = string.Empty;

    /// <summary>Which character/zone situation the on/off strip is in.</summary>
    private enum Situation
    {
        /// <summary>No character in the world: the switch is unavailable.</summary>
        NoCharacter,

        /// <summary>A character outside a PvP zone: the switch is unavailable.</summary>
        OutsidePvp,

        /// <summary>A character in a PvP zone: the switch can be turned on.</summary>
        Ready,

        /// <summary>PvP Solver is on.</summary>
        Running,
    }

    private enum Hover
    {
        None,
        FooterButton,
    }

    private enum SizeKind
    {
        /// <summary>The window's own first-use size (Size, FirstUseEver).</summary>
        FirstUse,

        /// <summary>The window's smallest allowed size (SizeConstraints.MinimumSize).</summary>
        Narrow,

        /// <summary>The smallest allowed width at the first-use height: shows wrapping without the page body being squeezed out.</summary>
        NarrowWidth,
    }

    private sealed record Scenario(string Name, string Page, string Shows)
    {
        public SizeKind Size { get; init; } = SizeKind.FirstUse;
        public Situation Situation { get; init; } = Situation.Ready;
        public Job Playing { get; init; } = Job.PLD;
        public string? PickedJob { get; init; }
        public Action<Configs>? Configure { get; init; }
        public string? SwitchMessage { get; init; }
        public string? Popup { get; init; }
        public Hover Hover { get; init; }
        public float? ScrollY { get; init; } // mouse wheel clicks over the page body; one click is about 86 px at scale 1.02
        public float? Scale { get; init; }
        public string? OptionsFail { get; init; }
        public bool OptionsLoading { get; init; }
        public bool RetryEveryFrame { get; init; }
        public bool BreakDisplayColour { get; init; }
        public bool ExpectError { get; init; }

        /// <summary>Whether built-in (hermetic) mode renders it too; game mode renders every scenario.</summary>
        public bool InBuiltIn { get; init; }
    }

    private const float ToBottom = 100000f;
    private const float PastPicker = 4f;

    private static void Typical(Configs c)
    {
        c.AutoOffAfterCombatTime = 45;
        c.HealthForGuard = 0.35f;
        c.PvpPurifyHeavy.Value = true;
        c.StickyTargetMaxRemaining = 12;
        c.TargetingTypes =
        [
            TargetingType.PvPHighestPressure, TargetingType.PvPHealers, TargetingType.LowHPPercent,
            TargetingType.HighMaxHP, TargetingType.Nearest, TargetingType.Big,
        ];
        c.TargetingIndex = 1;
        c.ShowControlWindow.Value = true;
        c.ShowCooldownWindow.Value = true;
        c.TeachingMode.Value = true;
        c.TeachingModeAutoTarget.Value = true;
        c.TeachingModeColor = new Vector4(1f, 0.45f, 0.75f, 1f);
        c.DTRType = DTRType.DTRAllAuto;
        _ = c.DisabledJobs.Add(Job.DRK);
        foreach (DefensiveRow row in DefensiveTable.ForJob("PLD"))
        {
            if (row.ActionName is "HolySheltronPvP" or "RampartPvP")
            {
                c.SetDefensiveEnabled(row, true);
                c.SetDefensivePercent(row, 0.55f);
            }
        }

        foreach (DefensiveRow row in DefensiveTable.ForJob("MNK"))
        {
            if (row.ActionName == "RiddleOfEarthPvP")
            {
                c.SetDefensivePercent(row, 0.65f);
            }
        }

        HarnessOptionStore.Values[(Job.PLD, "GuardianFree")] = "True";
        HarnessOptionStore.Values[(Job.PLD, "GuardianThreshold")] = "0.55";
        HarnessOptionStore.Values[(Job.MNK, "SmitePvPPercent")] = "0.4";
    }

    private static readonly Scenario[] AllScenarios =
    [
        // ---- Match page: every state of the on/off strip ----
        new("match-first-run", "Match", "First run: default settings, no character in the world, the switch unavailable (Idle).")
        { Situation = Situation.NoCharacter, InBuiltIn = true },
        new("match-ready", "Match", "Typical use in a PvP zone: Ready, changed values with Reset buttons.")
        { Configure = Typical, InBuiltIn = true },
        new("match-ready-bottom", "Match", "Match scrolled to the end: the commands card with its Copy buttons.")
        { Configure = Typical, ScrollY = ToBottom },
        new("match-running", "Match", "PvP Solver on: Running pill, red Stop button.")
        { Situation = Situation.Running, Configure = Typical },
        new("match-switch-failed", "Match", "A turn-on that did not happen: Failed pill, error box and footer message.")
        { Configure = Typical, SwitchMessage = "Could not change PvP Solver right now." },
        new("match-footer-tooltip", "Match", "Hovering the unavailable turn-on button: the tooltip that says why.")
        { Situation = Situation.NoCharacter, Hover = Hover.FooterButton },

        // ---- Survival ----
        new("survival-ready", "Survival", "Guard slider with a changed value, Purify toggles.")
        { Configure = Typical, InBuiltIn = true },
        new("survival-ready-bottom", "Survival", "Survival scrolled to the end: Movement and the defensive master switch with its notes.")
        { Configure = Typical, ScrollY = ToBottom },

        // ---- Targeting ----
        new("targeting-first-run", "Targeting", "First run: the target list is empty so the default order is announced; attack options below.")
        { Situation = Situation.NoCharacter },
        new("targeting-ready", "Targeting", "A six entry priority list with the in-use marker and the disabled Down on the last row.")
        { Configure = Typical, InBuiltIn = true },
        new("targeting-ready-bottom", "Targeting", "Targeting scrolled to the end: staying on a target, invulnerable enemies and The Shatter.")
        { Configure = Typical, ScrollY = ToBottom },

        // ---- My Job ----
        new("job-pld-playing", "My Job", "My Job for the played Paladin, top of the page: the job picker and the enable switch.")
        { Configure = Typical, InBuiltIn = true },
        new("job-pld-options", "My Job", "The Paladin option card with a parent option and its indented child, changed values with Reset.")
        { Configure = Typical, ScrollY = PastPicker },
        new("job-pld-bottom", "My Job", "My Job scrolled to the end: the defensive rows with percentage sliders and their help text.")
        { Configure = Typical, ScrollY = ToBottom },
        new("job-mnk-options", "My Job", "A Monk edited while the Paladin is played: long option names, five options.")
        { Configure = Typical, PickedJob = "MNK", ScrollY = PastPicker },
        new("job-no-character", "My Job", "Not logged in: Idle strip and the options card asking for a character.")
        { Situation = Situation.NoCharacter, ScrollY = PastPicker },
        new("job-options-failed", "My Job", "The rotation options could not be loaded: the failure line (shown on the frame the load is retried).")
        { Configure = Typical, OptionsFail = "The Reborn rotation for this job did not finish loading.", RetryEveryFrame = true, ScrollY = PastPicker, ExpectError = true },

        // ---- Display ----
        new("display-ready", "Display", "Info bar combo, overlay windows, the nested Lock toggle.")
        { Configure = Typical, InBuiltIn = true },
        new("display-ready-bottom", "Display", "Display scrolled to the end: teaching colour swatch, hotbars and tooltips, what's new.")
        { Configure = Typical, ScrollY = ToBottom },
        new("display-tab-error", "Display", "A tab that throws while drawing: the window's own error line and the way to the full settings.")
        { Configure = Typical, BreakDisplayColour = true, ExpectError = true, ScrollY = ToBottom },

        // ---- Advanced ----
        new("advanced-ready", "Advanced", "The way to the full settings, tab link buttons, backup and tools.")
        { Configure = Typical, InBuiltIn = true },
        new("advanced-ready-bottom", "Advanced", "Advanced scrolled to the end: reset card and the debugging toggle.")
        { Configure = Typical, ScrollY = ToBottom },
        new("advanced-reset-confirm", "Advanced", "The confirmation popup before every setting is reset.")
        { Configure = Typical, Popup = "reset" },

        // ---- narrowest allowed size ----
        new("narrow-match-running", "Match", "Smallest allowed size (width and height): top bar, feature row and stacked footer leave little room for the page body.")
        { Size = SizeKind.Narrow, Situation = Situation.Running, Configure = Typical },
        new("narrow-width-targeting", "Targeting", "Smallest allowed width at the first-use height: the priority list rows and their small buttons.")
        { Size = SizeKind.NarrowWidth, Configure = Typical },
        new("narrow-width-job-mnk", "My Job", "Smallest allowed width: the picker wrap, the long Monk option names and the options card.")
        { Size = SizeKind.NarrowWidth, Configure = Typical, PickedJob = "MNK", ScrollY = PastPicker },
        new("narrow-width-advanced", "Advanced", "Smallest allowed width: tab link buttons wrapping and the tool buttons.")
        { Size = SizeKind.NarrowWidth, Configure = Typical },

        // ---- scale evidence ----
        new("scale150-job-mnk", "My Job", "Evidence only: the Monk options at global scale 1.5.")
        { Configure = Typical, PickedJob = "MNK", Scale = 1.5f, ScrollY = 3f },
    ];

    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            Options = RenderOptions.Resolve(args);
            _strictGlyphs = args.Contains("--strict-glyphs");
            SourceRoot = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "LalaSourceRoot")?.Value
                ?? throw new InvalidOperationException("LalaSourceRoot was not embedded.");
            RotationUpdater.SourceRoot = SourceRoot;
            if (Options.OutputSubdirectory.Length > 0)
                OutputDirectory = Path.Combine(OutputDirectory, Options.OutputSubdirectory);
            Directory.CreateDirectory(OutputDirectory);

            VerifyCatalogBindings();
            var scenarios = AllScenarios.Where(s => Options.IsGame || s.InBuiltIn).ToArray();
            var sizes = ReadWindowSizes();
            Console.WriteLine($"Window first-use size {sizes.FirstUse.X}x{sizes.FirstUse.Y}, smallest allowed {sizes.Narrow.X}x{sizes.Narrow.Y} (read from PvpSettingsWindow).");

            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var scenario in scenarios)
            {
                var size = scenario.Size switch
                {
                    SizeKind.Narrow => sizes.Narrow,
                    SizeKind.NarrowWidth => new Vector2(sizes.Narrow.X, sizes.FirstUse.Y),
                    _ => sizes.FirstUse,
                };
                var name = $"{scenario.Name}-{(int)size.X}x{(int)size.Y}";
                var result = RenderScenario(scenario, name, size);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Path))).ToLowerInvariant();
                if (!hashes.TryAdd(hash, name))
                {
                    if (!Options.IsGame)
                        throw new InvalidOperationException($"{name} is byte-identical to {hashes[hash]}.");
                    Console.WriteLine($"WARNING: {name} is byte-identical to {hashes[hash]}; the state it sets up is not visible.");
                }

                Console.WriteLine($"{name}: {Path.GetRelativePath(ProjectRoot, result.Path).Replace('\\', '/')} | {result.Triangles} triangles | sha256 {hash[..16]}");
            }

            Console.WriteLine($"Rendered {scenarios.Length} real-source UI states ({hashes.Count} distinct). No window or game process was created.");
            if (_missingGlyphs && _strictGlyphs)
                throw new InvalidOperationException("--strict-glyphs: text the window draws contains characters the game font lacks.");
            VerifyCloseButton();
            Console.WriteLine("Custom close control: a press and release on the drawn close button closes the window.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
    }

    // ---- stub fidelity -------------------------------------------------------------------------------------------

    /// <summary>
    /// The harness' Configs stand-in must answer every catalog property the way the real class does: same name, type
    /// and default. A drift would make the window draw a "not found" line or a wrong default, which is a harness fault.
    /// </summary>
    private static void VerifyCatalogBindings()
    {
        var config = new Configs();
        Service.Config = config;
        var checkedCount = 0;
        foreach (var spec in PvpSettingsCatalog.All)
        {
            if (!PvpBinding.Exists(spec.Property))
                throw new InvalidOperationException($"Harness Configs has no property {spec.Property}.");
            switch (spec.Control)
            {
                case SettingControl.Toggle:
                    if (!PvpBinding.TryGetBool(spec.Property, out var on) || on != spec.DefaultOn)
                        throw new InvalidOperationException($"{spec.Property}: harness default {on} differs from the catalog default {spec.DefaultOn}.");
                    break;
                case SettingControl.Percent:
                case SettingControl.Seconds:
                    if (!PvpBinding.TryGetFloat(spec.Property, out var number) || Math.Abs(number - spec.DefaultNumber) > 1e-6f)
                        throw new InvalidOperationException($"{spec.Property}: harness default {number} differs from the catalog default {spec.DefaultNumber}.");
                    break;
                case SettingControl.Choice:
                    if (!PvpBinding.TryGetChoice(spec.Property, out var member) || member != spec.DefaultChoice)
                        throw new InvalidOperationException($"{spec.Property}: harness default {member} differs from the catalog default {spec.DefaultChoice}.");
                    var enumType = typeof(Configs).GetProperty(spec.Property)!.PropertyType;
                    foreach (var choice in spec.Choices)
                        if (!Enum.TryParse(enumType, choice.Name, false, out _))
                            throw new InvalidOperationException($"{spec.Property}: {choice.Name} is not a member of {enumType.Name}.");
                    break;
            }

            checkedCount++;
        }

        Console.WriteLine($"Catalog bindings: {checkedCount} settings answer through the harness Configs with the catalog defaults.");
    }

    private static (Vector2 FirstUse, Vector2 Narrow) ReadWindowSizes()
    {
        var window = new PvpSettingsWindow();
        var first = window.Size ?? throw new InvalidOperationException("The settings window declares no Size.");
        var narrow = window.SizeConstraints?.MinimumSize ?? throw new InvalidOperationException("The settings window declares no minimum size.");
        return (first, narrow);
    }

    // ---- rendering -----------------------------------------------------------------------------------------------

    private static void ResetState()
    {
        Service.Config = new Configs();
        PvpSwitch.IsOn = false;
        PvpSwitch.InPvpZone = false;
        Player.Available = false;
        Player.Job = Job.ADV;
        HarnessOptionStore.Values.Clear();
        RotationUpdater.FailWith = null;
        RotationUpdater.NotLoadedYet = false;
        PluginLog.Errors.Clear();
        PvPSolverPlugin.Calls.Clear();
        SetStatic(typeof(PvpJobTab), "_picked", null);
        SetStatic(typeof(PvpAdvancedTab), "_openReset", false);
        SetStatic(typeof(PvpAdvancedTab), "_openRestore", false);
        SetStatic(typeof(PvpAdvancedTab), "_message", string.Empty);
        PvpApprovedChrome.BeginFrame();
        ((System.Collections.IDictionary)GetStatic(typeof(PvpJobOptions), "RetryAt")!).Clear();
    }

    private static object? GetStatic(Type type, string field) =>
        (type.GetField(field, BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingFieldException(type.Name, field)).GetValue(null);

    private static void SetStatic(Type type, string field, object? value) =>
        (type.GetField(field, BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingFieldException(type.Name, field)).SetValue(null, value);

    private static RenderResult RenderScenario(Scenario scenario, string name, Vector2 size)
    {
        ResetState();
        Player.Available = scenario.Situation != Situation.NoCharacter;
        if (Player.Available)
            Player.Job = scenario.Playing;
        PvpSwitch.InPvpZone = scenario.Situation is Situation.Ready or Situation.Running;
        PvpSwitch.IsOn = scenario.Situation == Situation.Running;
        scenario.Configure?.Invoke(Service.Config);
        RotationUpdater.FailWith = scenario.OptionsFail;
        RotationUpdater.NotLoadedYet = scenario.OptionsLoading;
        Service.Config.ThrowOnTeachingColorRead = scenario.BreakDisplayColour;

        var window = new PvpSettingsWindow();
        window.OnOpen();
        var shell = typeof(PvpSettingsWindow).GetField("_shell", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        shell.GetType().GetField("_currentPage", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(shell, scenario.Page);
        if (scenario.PickedJob is not null)
            SetStatic(typeof(PvpJobTab), "_picked", scenario.PickedJob);
        if (scenario.SwitchMessage is not null)
            typeof(PvpSettingsWindow).GetField("_switchMessage", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, scenario.SwitchMessage);
        if (scenario.Popup == "reset")
            SetStatic(typeof(PvpAdvancedTab), "_openReset", true);
        if (scenario.Popup == "restore")
            SetStatic(typeof(PvpAdvancedTab), "_openRestore", true);

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

            // The shell has no native window padding; scroll and hover positions are measured from the window edge.
            var padding = Vector2.Zero;
            if (scenario.Hover == Hover.FooterButton)
                io.MousePos = new Vector2(size.X - 24 - 48, size.Y - 30);

            // Frames settle window storage and (for the popups) the modal. A scrolled scenario sends one large mouse
            // wheel step over the page body (ImGui clamps it to the end of the content), then lets the layout settle.
            var frames = scenario.Popup is not null ? 8 : scenario.ScrollY is null ? 3 : 5;
            if (scenario.ScrollY is not null)
                io.MousePos = new Vector2(padding.X + 40, size.Y / 2f);
            for (var frame = 0; frame < frames; frame++)
            {
                if (scenario.RetryEveryFrame)
                    ((System.Collections.IDictionary)GetStatic(typeof(PvpJobOptions), "RetryAt")!).Clear();
                if (frame == 2 && scenario.ScrollY is not null)
                    io.AddMouseWheelEvent(0f, -scenario.ScrollY!.Value);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(size);
                var open = true;
                window.PreDraw();
                ImGui.Begin(window.WindowName, ref open, window.Flags |
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
                window.Draw();
                ImGui.End();
                window.PostDraw();
                ImGui.Render();
            }

            var width = (int)size.X;
            var height = (int)size.Y;
            var pixels = new byte[width * height * 4];
            Fill(pixels, 20, 20, 24);
            var drawData = ImGui.GetDrawData();
            if (!drawData.Valid || drawData.TotalVtxCount == 0)
                throw new InvalidOperationException($"{name}: the real Draw method produced no draw data.");
            var stats = CpuRasterizer.Render(drawData, pixels, width, height, look.Textures);
            var path = Path.Combine(OutputDirectory, name + ".png");
            PngWriter.Write(path, pixels, width, height);

            var errors = PluginLog.Errors.Count;
            if (scenario.ExpectError ? errors == 0 : errors != 0)
                throw new InvalidOperationException($"{name}: expected {(scenario.ExpectError ? "an" : "no")} error in the plugin log, found {errors}: {string.Join(" | ", PluginLog.Errors.Select(e => e.Split('\n')[0]))}");
            return new RenderResult(path, stats.Triangles);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
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

    // ---- interaction check ---------------------------------------------------------------------------------------

    /// <summary>A real press and release on the drawn close button must reach the window's close callback.</summary>
    private static void VerifyCloseButton()
    {
        ResetState();
        Player.Available = true;
        Player.Job = Job.PLD;
        var window = new PvpSettingsWindow { IsOpen = true };
        window.OnOpen();
        var size = new Vector2(1080, 760);
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = size;
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            _ = UiLook.Apply(Options);
            // The close button is the last control of the 68 px header band: 36 px square, one gutter (24 px) from the right edge.
            var click = new Vector2(size.X - 24 - 18, 34);
            for (var step = 0; step < 4; step++)
            {
                io.MousePos = step == 0 ? new Vector2(-10000, -10000) : click;
                io.MouseDown[0] = step == 2;
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(size);
                var open = true;
                window.PreDraw();
                ImGui.Begin(window.WindowName, ref open, window.Flags | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
                window.Draw();
                ImGui.End();
                window.PostDraw();
                ImGui.Render();
            }

            if (window.IsOpen)
                throw new InvalidOperationException("A press and release on the drawn close button did not close the settings window.");
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    // ---- glyph audit ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Every non-ASCII character in the string literals of the window sources, the shared kit, the catalog and
    /// defensive table, and every rotation option name must exist in the active font; a missing one draws the
    /// fallback mark in game. Printed as a report; <c>--strict-glyphs</c> turns a miss into a failure.
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

        var literal = new Regex("@?\"(?:[^\"\\\\\\r\\n]|\\\\.)*\"");
        var folders = new[]
        {
            Path.Combine("src", "PvPSolver", "PvPSolver", "UI", "Pvp"),
            Path.Combine("src", "PvPSolver", "PvPSolver", "Basic", "Decisions"),
            Path.Combine("src", "Shared", "LalaUiKit"),
        };
        var scanned = 0;
        foreach (var folder in folders)
        {
            var directory = Path.Combine(SourceRoot, folder);
            if (!Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
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

                Scan(Path.GetRelativePath(SourceRoot, file).Replace('\\', '/'), text.ToString());
                scanned++;
            }
        }

        var optionNames = RotationUpdater.AllOptionNames(SourceRoot).Distinct().ToArray();
        Scan("RebornRotations/PVPRotations option names", string.Concat(optionNames.Select(n => n + "\n")));

        _missingGlyphs = found.Count > 0;
        if (found.Count == 0)
        {
            Console.WriteLine($"Glyph check: {scanned} source files and {optionNames.Length} rotation option names scanned; every drawn non-ASCII character exists in the game font.");
            return;
        }

        Console.WriteLine($"Glyph check: {found.Count} character(s) in drawn text have no glyph in the game font:");
        foreach (var (entry, sources) in found)
            Console.WriteLine($"  {entry} in {string.Join(", ", sources)}");
    }

    private sealed record RenderResult(string Path, int Triangles);
}
