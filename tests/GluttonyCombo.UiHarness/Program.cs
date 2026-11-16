using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using Dalamud.Bindings.ImGui;
using GluttonyCombo.Window;
using LalaUiKit;
using Lala.UiRender;

namespace GluttonyCombo.UiHarness;

// Renders the real shell drawing (GluttonyShellView + GluttonyChrome) for every window and top-level page. The plugin's own
// preset, settings and rotation renderers depend on game state and are not linked; their pages show sample bodies built
// from the same drawn controls (Card, Toggle, JobButton) so the shell around them is the real code.
internal static unsafe class Program
{
    private static int _checks;
    private static string _outputDirectory = "";
    private static RenderOptions _options = RenderOptions.BuiltIn;
    private static readonly SortedSet<string> Missing = [];
    private static int _nonAscii;
    private static readonly List<string> Drawn = [];

    private const int WideW = 1040;
    private const int WideH = 720;
    private const int NarrowW = 700;
    private const int NarrowH = 640;

    private enum Kind { Shell, Major, Strip }

    private sealed class Scenario
    {
        public required string Name;
        public Kind Kind;
        public int Width = WideW;
        public int Height = WideH;
        public required Func<ShellModel> Model;
        public Action Body = () => { };
        public string Search = "";
        public bool Descriptions = true;
        public bool HoverClose;
    }

    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            _options = RenderOptions.Resolve(args);
            _outputDirectory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..")), "output");
            if (_options.OutputSubdirectory.Length > 0)
                _outputDirectory = Path.Combine(_outputDirectory, _options.OutputSubdirectory);
            Directory.CreateDirectory(_outputDirectory);

            VerifyNavigation();
            var hashes = new Dictionary<string, string>();
            foreach (var scenario in Scenarios(_options.IsGame))
            {
                var path = Render(scenario);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                if (!hashes.TryAdd(hash, scenario.Name))
                    throw new InvalidOperationException($"{scenario.Name} is byte-identical to {hashes[hash]}.");
            }
            VerifyToggleInteraction();
            VerifyCloseInteraction();
            CheckGlyphs();
            Console.WriteLine($"PASS: {_checks} checks; {hashes.Count} images in {_outputDirectory}.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
    }

    // ---- scenarios ----

    private static readonly string[] Groups = GluttonyShellNavigation.Groups;

    private static ShellModel Model(string group, UiState state, string title, string description, string meta,
        IReadOnlyList<ShellFeature> features, bool search = false, bool descriptions = false, string? stop = null,
        string? footer = null, string hint = "Search presets...", bool collapsed = false)
    {
        var enabled = stop is not null;
        return new ShellModel
        {
            Groups = Groups,
            CurrentGroup = group,
            State = state,
            ShowSearch = search,
            ShowDescriptions = descriptions,
            SearchHint = hint,
            Features = features,
            FeaturesCollapsed = collapsed,
            Title = title,
            Description = description,
            Meta = meta.Length == 0 ? [] : [meta],
            ReportButton = () => ImGui.Button("Report a problem##r"),
            Footer = new ShellFooter
            {
                State = state,
                Primary = footer ?? (enabled ? $"{Copy.Word(state)} · AutoRotation" : "Ready · AutoRotation is off"),
                Secondary = !enabled ? "Turn it on from the Automation page; combos still work from their own buttons."
                    : state == UiState.Blocked ? "Automatic actions are paused" : "Automatic action replacement is enabled",
                StopLabel = stop,
            },
        };
    }

    private static IReadOnlyList<ShellFeature> Features(string group, int selected, params string[] extra)
    {
        var labels = group switch
        {
            "Combos" => new[] { "PvE jobs", "PvP jobs" },
            "Automation" => ["AutoRotation", "Custom Actions"],
            _ => ["Settings", "About"],
        };
        return labels.Select((label, i) => new ShellFeature(group + i, label, i == selected)).ToList();
    }

    private static IReadOnlyList<ShellFeature> JobFeatures(params string[] contents)
    {
        var list = new List<ShellFeature> { new("jobs", "Jobs", false) };
        for (var i = 0; i < contents.Length; i++)
            list.Add(new ShellFeature("c" + i, contents[i], i == 0));
        list.Add(new ShellFeature("pvp", "PvP jobs", false));
        return list;
    }

    private static Scenario[] Scenarios(bool game)
    {
        var pve = JobFeatures("Normal");
        var adv = JobFeatures("Job Roles", "Variant Dungeons", "Bozja", "Occult Crescent", "Deep Dungeon");
        Scenario Pve(string name, int w, int h, Action body, string search = "") => new()
        {
            Name = name, Width = w, Height = h, Search = search, Body = body,
            Model = () => Model("Combos", UiState.Ready, "Paladin", "Job combos, options, and content presets.",
                "12 preset groups", pve, search: true, descriptions: true),
        };

        if (!game)
        {
            return
            [
                new Scenario { Name = "shell-pve-jobs", Width = 960, Height = 700, Body = JobGrid(false), Model = () => Model("Combos", UiState.Ready, "PvE Features", "Select a job to see its combos.", "24 jobs", Features("Combos", 0)) },
                Pve("shell-pve-presets", 960, 700, PresetList),
                new Scenario { Name = "shell-autorotation-running", Width = 960, Height = 700, Body = AutoRotationPage, Model = () => Model("Automation", UiState.Running, "AutoRotation", "Choose how the rotation picks targets.", "4 auto modes", Features("Automation", 0), stop: "Stop") },
                new Scenario { Name = "major-changes", Kind = Kind.Major, Width = 780, Height = 620, Model = () => new ShellModel(), Body = MajorBody },
            ];
        }

        return
        [
            // First run: nothing selected, AutoRotation off.
            new() { Name = "main-first-run-pve-jobs-1040x720", Body = JobGrid(false), Model = () => Model("Combos", UiState.Ready, "PvE Features", "Select a job to see its combos.", "24 jobs", Features("Combos", 0)) },
            new() { Name = "main-first-run-pve-jobs-700x640", Width = NarrowW, Height = NarrowH, Body = JobGrid(false), Model = () => Model("Combos", UiState.Ready, "PvE Features", "Select a job to see its combos.", "24 jobs", Features("Combos", 0)) },
            // Typical use: a job is open with its preset list and search.
            Pve("main-pve-job-presets-1040x720", WideW, WideH, PresetList),
            Pve("main-pve-job-presets-700x640", NarrowW, NarrowH, PresetList),
            Pve("main-pve-search-results-1040x720", WideW, WideH, SearchResults, search: "mitigation"),
            new() { Name = "main-pve-adventurer-content-1040x720", Body = PresetList, Search = "",
                Model = () => Model("Combos", UiState.Ready, "Adventurer", "Job combos, options, and content presets.", "6 preset groups", adv, search: true, descriptions: true) },
            new() { Name = "main-pve-adventurer-content-700x640", Width = NarrowW, Height = NarrowH, Body = PresetList,
                Model = () => Model("Combos", UiState.Ready, "Adventurer", "Job combos, options, and content presets.", "6 preset groups", adv, search: true, descriptions: true) },
            new() { Name = "main-pve-destinations-collapsed-1040x720", Body = PresetList,
                Model = () => Model("Combos", UiState.Ready, "Paladin", "Job combos, options, and content presets.", "12 preset groups", pve, search: true, descriptions: true, collapsed: true) },
            // PvP.
            new() { Name = "main-pvp-jobs-1040x720", Body = () => { Banner(); JobGrid(true)(); },
                Model = () => Model("Combos", UiState.Ready, "PvP Features", "Presets for the Crystalline Conflict and Frontline.", "14 jobs", Features("Combos", 1)) },
            new() { Name = "main-pvp-job-presets-1040x720", Body = PresetList,
                Model = () => Model("Combos", UiState.Ready, "Paladin", "Presets for the Crystalline Conflict and Frontline.", "3 preset groups",
                    [new("pve", "PvE jobs", false), new("pvp", "PvP jobs", true)], search: true, descriptions: true) },
            // Automation.
            new() { Name = "main-autorotation-running-1040x720", Body = AutoRotationPage,
                Model = () => Model("Automation", UiState.Running, "AutoRotation", "Choose how the rotation picks targets, heals, and uses actions.", "4 auto modes", Features("Automation", 0), stop: "Stop") },
            new() { Name = "main-autorotation-blocked-700x640", Width = NarrowW, Height = NarrowH, Body = AutoRotationPage,
                Model = () => Model("Automation", UiState.Blocked, "AutoRotation", "Choose how the rotation picks targets, heals, and uses actions.", "4 auto modes", Features("Automation", 0), stop: "Stop") },
            new() { Name = "main-custom-actions-1040x720", Body = CustomActionsPage,
                Model = () => Model("Automation", UiState.Ready, "Custom Actions", "Drag bespoke rotation and utility actions to the hotbar.", "8 rotation · 5 utility", Features("Automation", 1)) },
            // System.
            new() { Name = "main-settings-1040x720", Body = SettingsPage,
                Model = () => Model("System", UiState.Ready, "Settings", "Everything that changes how the combos behave.", "62 settings", Features("System", 0), search: true, hint: "Search settings...") },
            new() { Name = "main-settings-700x640", Width = NarrowW, Height = NarrowH, Body = SettingsPage,
                Model = () => Model("System", UiState.Ready, "Settings", "Everything that changes how the combos behave.", "62 settings", Features("System", 0), search: true, hint: "Search settings...") },
            new() { Name = "main-about-1040x720", Body = AboutPage,
                Model = () => Model("System", UiState.Ready, "GluttonyCombo", "A lalalazy fork of Wrath Combo. All upstream credit to Team Wrath / PunishXIV.", "640 job presets", Features("System", 1)) },
            new() { Name = "main-close-tooltip-1040x720", HoverClose = true, Body = JobGrid(false),
                Model = () => Model("Combos", UiState.Ready, "PvE Features", "Select a job to see its combos.", "24 jobs", Features("Combos", 0)) },
            // Other windows.
            new() { Name = "major-changes-780x620", Kind = Kind.Major, Width = 780, Height = 620, Model = () => new ShellModel(), Body = MajorBody },
            new() { Name = "major-changes-560x500", Kind = Kind.Major, Width = 560, Height = 500, Model = () => new ShellModel(), Body = MajorBody },
            new() { Name = "overlay-fight-brain-strip-260x170", Kind = Kind.Strip, Width = 260, Height = 170, Model = () => new ShellModel(), Body = StripBody },
        ];
    }

    // ---- sample bodies (drawn controls only; the plugin's own renderers are not linked) ----

    private static Action JobGrid(bool pvp) => () =>
    {
        string[] regular = ["Paladin - PLD", "Warrior - WAR", "Dark Knight - DRK", "Gunbreaker - GNB", "White Mage - WHM", "Scholar - SCH",
            "Astrologian - AST", "Sage - SGE", "Monk - MNK", "Dragoon - DRG", "Ninja - NIN", "Samurai - SAM", "Reaper - RPR", "Viper - VPR",
            "Bard - BRD", "Machinist - MCH", "Dancer - DNC", "Black Mage - BLM", "Summoner - SMN", "Red Mage - RDM", "Pictomancer - PCT"];
        string[] special = ["Adventurer - ADV", "Blue Mage - BLU", "Miner - MIN", "Botanist - BTN", "Fisher - FSH"];
        void Table(string id, string[] jobs)
        {
            var columns = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / 220f));
            if (!ImGui.BeginTable(id, columns))
                return;
            foreach (var job in jobs)
            {
                ImGui.TableNextColumn();
                GluttonyChrome.JobButton(ImGui.GetWindowDrawList(), id + job, job, null, ImGui.GetCursorScreenPos(),
                    new Vector2(ImGui.GetContentRegionAvail().X, 52), job.StartsWith("Dark"), job.StartsWith("Blue"),
                    "Disabled until the next update.");
                ImGui.Dummy(new Vector2(0, Theme.Unit));
                Drawn.Add(job);
            }
            ImGui.EndTable();
        }
        if (!pvp)
        {
            Widgets.Title("Regular Jobs");
            Table("pve_regular", regular);
            ImGui.Dummy(new Vector2(0, 2 * Theme.Unit));
            Widgets.Title("Limited Jobs and Misc");
            Table("pve_special", special);
            return;
        }
        Table("pvp_jobs", regular.Take(14).ToArray());
    };

    private static void Banner()
    {
        Widgets.Banner(UiState.Blocked, "Action replacing is off: PvP combos need it.");
    }

    private static bool _a = true, _b, _c = true, _d, _e = true;

    private static void Preset(string title, string text, ref bool on, params (string, bool)[] children)
    {
        var enabled = on;
        GluttonyChrome.Card("preset_" + title, () =>
        {
            GluttonyChrome.Toggle(title + "##" + title, ref enabled);
            Widgets.SecondaryWrapped(text);
            ImGui.Indent();
            foreach (var (name, value) in children)
            {
                var v = value;
                GluttonyChrome.Toggle(name + "##" + title + name, ref v);
            }
            ImGui.Unindent();
            Drawn.Add(title + text);
        });
        on = enabled;
    }

    private static void PresetList()
    {
        Preset("Advanced Mode - Single Target", "Replaces Fast Blade with a full single target rotation, tuned for the current job gauge.", ref _a,
            ("Include Mitigations", true), ("Use Interrupt", false), ("Burst only on bosses", true));
        Preset("Advanced Mode - AoE", "Replaces Total Eclipse with the area rotation.", ref _b, ("Include Mitigations", false));
        Preset("Simple Mode - Single Target", "Recommended values, no options to tune.", ref _c);
        Preset("Mitigation - Tankbusters", "Uses defensive actions when a tankbuster is detected.", ref _d, ("Warn in chat", false));
        Preset("Balance Opener", "Opens the fight with the standard burst sequence.", ref _e);
    }

    private static void SearchResults()
    {
        Widgets.Caption("3 results");
        Preset("Mitigation - Tankbusters", "Uses defensive actions when a tankbuster is detected.", ref _d, ("Warn in chat", false));
        Preset("Advanced Mode - Single Target", "Includes mitigation options at set health thresholds.", ref _a, ("Include Mitigations", true));
        Preset("Advanced Mode - AoE", "Includes mitigation options when several enemies attack.", ref _b);
    }

    private static void AutoRotationPage()
    {
        bool on = true, o = false, t = true;
        GluttonyChrome.Toggle("Enable AutoRotation", ref on);
        Widgets.Title("Combat settings");
        GluttonyChrome.Toggle("Attack out of combat while another plugin controls Gluttony", ref o);
        GluttonyChrome.Toggle("Bypass self-use buffs", ref t);
        Widgets.Title("Automatic activation");
        GluttonyChrome.Toggle("Enable AutoRotation when entering an instance", ref t);
        GluttonyChrome.Toggle("Disable AutoRotation when leaving an instance", ref o);
        GluttonyChrome.Card("autorot_dps", () =>
        {
            Widgets.Title("Damage settings");
            Widgets.SecondaryWrapped("Targeting mode, range fallback and quest priority.");
            GluttonyChrome.Toggle("Attack an in-range enemy when the chosen target is out of range", ref t);
            GluttonyChrome.Toggle("Prefer non-combat targets", ref o);
        });
    }

    private static void CustomActionsPage()
    {
        bool a = true, b = false;
        Widgets.Title("Rotation Buttons");
        Widgets.SecondaryWrapped("These buttons replace the need to replace actions for all features marked as either Simple or Advanced. Drag the icon to a hotbar.");
        GluttonyChrome.Card("ca_buttons", () =>
        {
            GluttonyChrome.Toggle("Single Target DPS##ca1", ref a);
            GluttonyChrome.Toggle("AoE DPS##ca2", ref b);
            GluttonyChrome.Toggle("Single Target Heals##ca3", ref a);
            GluttonyChrome.Toggle("AoE Heals##ca4", ref b);
        });
        GluttonyChrome.Toggle("Do not override icons with job actions##ca5", ref b);
    }

    private static void SettingsPage()
    {
        bool a = true, b = false;
        Widgets.Title("Core");
        GluttonyChrome.Toggle("Action replacing##s1", ref a);
        GluttonyChrome.Toggle("Show the search bar##s2", ref a);
        GluttonyChrome.Toggle("Hide conflicting-plugin warnings##s3", ref b);
        Widgets.Title("Targeting");
        GluttonyChrome.Toggle("Show the target highlight on the party list##s4", ref a);
        GluttonyChrome.Toggle("Treat training dummies as enemies##s5", ref b);
    }

    private static void AboutPage()
    {
        GluttonyChrome.Card("about_links", () =>
        {
            Widgets.Title("Source and credits");
            Widgets.SecondaryWrapped("GluttonyCombo preserves the upstream combo suite and its fork integrations.");
            ImGui.Button("GitHub: dajoey/lalalazy");
            ImGui.Button("Additional custom action icons by alexisoffline");
        });
        GluttonyChrome.Card("about_support", () =>
        {
            Widgets.Title("Support");
            Widgets.SecondaryWrapped("Conflict warnings stay visible above every page. Reports use the existing telemetry surface.");
            ImGui.Button("Report a problem");
        });
    }

    private static void MajorBody()
    {
        Widgets.Title("Tank In-Combo Mitigation Has Been Overhauled and Moved");
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.StateAmber);
        ImGui.TextUnformatted("You had one of these tanks configured! Please Read!");
        ImGui.PopStyleColor();
        ImGui.TextUnformatted("Tanks have had their In-Combo Mitigation completely overhauled.\nMitigation is now in its own Section, under the Advanced Combos.\n\nPvE Features > [Tank of choice] > Advanced Mitigation Options");
        ImGui.NewLine();
        ImGui.Button("> Open Dark Knight's Config##m2");
        ImGui.Button("> Open Paladin's Config##m3");
        ImGui.Separator();
        Widgets.Title("Raidwide-Detection Improved, Tankbuster Detection Added");
        ImGui.TextUnformatted("Raidwide detection has been improved to now also check for nearby\nstack marker visual effects.\n\n(Can also search for 'Tankbuster' in your tank's configuration)");
        ImGui.Separator();
        ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - 200) / 2f);
        ImGui.Button("Close and Do Not Show again");
    }

    private static void StripBody()
    {
        using var card = GluttonyChrome.BeginCard(0, GluttonyChrome.OverlayCardWidth);
        Widgets.Title("Fight Brain");
        Widgets.Caption("3 code forecasts");
        ImGui.TextUnformatted("  4.2s  raidwide");
        ImGui.TextUnformatted(" 11.8s  tankbuster");
        ImGui.TextUnformatted(" 19.5s  knockback");
    }

    // ---- rendering ----

    private static string Render(Scenario s)
    {
        var size = new Vector2(s.Width, s.Height);
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.DisplaySize = size;
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            io.MousePos = new Vector2(-10000, -10000);
            var look = UiLook.Apply(_options);
            var search = s.Search;
            var descriptions = s.Descriptions;
            var frame = new byte[s.Width * s.Height * 4];
            for (var i = 0; i < 4; i++)
            {
                if (s.HoverClose && i >= 2)
                {
                    var g = GluttonyChrome.Gutter(s.Width);
                    io.MousePos = new Vector2(s.Width - g - 18, 34);
                }
                ImGui.NewFrame();
                DrawWindow(s, ref search, ref descriptions);
                ImGui.Render();
            }
            if (_options.IsGame)
            {
                var text = string.Concat(Drawn) + "·…";
                _nonAscii += text.Count(c => c > 127);
                foreach (var entry in look.MissingGlyphs(text))
                    Missing.Add(entry);
            }
            Drawn.Clear();
            Fill(frame, 20, 20, 24);
            var data = ImGui.GetDrawData();
            if (!data.Valid || data.TotalVtxCount == 0)
                throw new InvalidOperationException($"{s.Name}: no draw data.");
            CpuRasterizer.Render(data, frame, s.Width, s.Height, look.Textures);
            var path = Path.Combine(_outputDirectory, s.Name + ".png");
            PngWriter.Write(path, frame, s.Width, s.Height);
            if (s.Kind == Kind.Shell)
                VerifyGeometry(s, frame);
            return path;
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    private static void DrawWindow(Scenario s, ref string search, ref bool descriptions)
    {
        var model = s.Model();
        Drawn.Add(model.Title + model.Description + string.Concat(model.Meta) + model.Footer.Primary + model.Footer.Secondary +
                  string.Concat(model.Features.Select(f => f.Label)) + string.Concat(model.Groups));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        switch (s.Kind)
        {
            case Kind.Shell:
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(new Vector2(s.Width, s.Height));
                ImGui.Begin("GluttonyCombo", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
                    ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize);
                GluttonyShellView.Draw(model, ref search, ref descriptions, null, s.Body);
                ImGui.End();
                break;
            case Kind.Major:
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(new Vector2(s.Width, s.Height));
                ImGui.Begin("Gluttony Combo | New Changes", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize);
                Theme.PushFrame();
                GluttonyChrome.PushControlColors();
                var gutter = GluttonyChrome.Gutter(s.Width);
                GluttonyChrome.DrawTitleBand(ImGui.GetWindowDrawList(), ImGui.GetWindowPos(), s.Width, gutter, "New changes");
                ImGui.SetCursorPos(new Vector2(0, GluttonyChrome.TitleBandHeight));
                GluttonyChrome.BeginBody(new Vector2(s.Width, s.Height - GluttonyChrome.TitleBandHeight), gutter, "majorChangesBody");
                s.Body();
                GluttonyChrome.EndBody();
                GluttonyChrome.PopControlColors();
                Theme.PopFrame();
                ImGui.End();
                break;
            case Kind.Strip:
                ImGui.SetNextWindowPos(new Vector2(20, 20));
                ImGui.Begin("strip", ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize |
                    ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoInputs);
                Theme.PushFrame();
                s.Body();
                Theme.PopFrame();
                ImGui.End();
                break;
        }
        ImGui.PopStyleVar();
    }

    // ---- checks ----

    private static (byte R, byte G, byte B) Px(byte[] f, int w, int x, int y) => (f[((y * w) + x) * 4], f[((y * w) + x) * 4 + 1], f[((y * w) + x) * 4 + 2]);

    private static bool Near((byte R, byte G, byte B) c, Vector4 t, int tol = 6)
        => Math.Abs(c.R - (int)MathF.Round(t.X * 255)) <= tol && Math.Abs(c.G - (int)MathF.Round(t.Y * 255)) <= tol &&
           Math.Abs(c.B - (int)MathF.Round(t.Z * 255)) <= tol;

    /// <summary>Measured on the pixels: header band, rule, close box, footer band.</summary>
    private static void VerifyGeometry(Scenario s, byte[] f)
    {
        var w = s.Width;
        var g = (int)GluttonyChrome.Gutter(w);
        // Collect rule rows: full-width rows whose far-left pixel is the border colour.
        var rules = Enumerable.Range(0, s.Height).Where(y => Near(Px(f, w, 3, y), Theme.SurfaceBorder, 10) && Near(Px(f, w, w - 3, y), Theme.SurfaceBorder, 10)).ToList();
        var narrowHeader = !_options.IsGame || (s.Width < 760 && s.Name.Contains("700"));
        if (!narrowHeader)
        {
            Check(rules.Contains(68), $"{s.Name}: header band rule at y=68 (found {string.Join(",", rules.Take(4))}).");
            Check(Near(Px(f, w, 3, 40), Theme.ShellBar, 3), $"{s.Name}: header band filled with the shell bar colour.");
        }
        // Close box: first border pixel on row 34 left of the right gutter.
        var right = w - g;
        var left = -1;
        for (var x = right - 41; x < right; x++)
            if (Near(Px(f, w, x, 34), Theme.SurfaceBorder, 8) || Near(Px(f, w, x, 34), Theme.Accent, 12))
            {
                left = x;
                break;
            }
        Check(left >= right - 37 && left <= right - 35, $"{s.Name}: close box starts at right gutter - 36 (found {right - left} px).");
        Check(rules.Any(y => y > s.Height - 80 && y < s.Height - 55), $"{s.Name}: footer top rule 60 px above the window bottom (rules {string.Join(",", rules.TakeLast(3))}).");
        _ = g;
    }

    private static void VerifyNavigation()
    {
        Check(GluttonyShellNavigation.Groups.SequenceEqual(["Combos", "Automation", "System"]), "Top-level groups preserve the two-level shell.");
        Check(GluttonyShellNavigation.FirstPage("Combos", false) == GluttonyPage.PvE &&
              GluttonyShellNavigation.FirstPage("Automation", false) == GluttonyPage.AutoRotation &&
              GluttonyShellNavigation.FirstPage("System", false) == GluttonyPage.Settings, "Group destinations are stable.");
        var root = GluttonyShellNavigation.Features(GluttonyPage.PvE, false, null, false);
        Check(root.Count == 2 && root[0].Page == GluttonyPage.PvE && root[1].Page == GluttonyPage.PvP, "Combo root exposes PvE and PvP.");
        IReadOnlyList<GluttonyContent> contents = [new("normal", "Normal"), new("variant", "Variant"), new("bozja", "Bozja"), new("occult", "Occult Crescent"), new("deep-dungeon", "Deep Dungeon")];
        var open = GluttonyShellNavigation.Features(GluttonyPage.PvE, true, contents, false);
        Check(open.Count == 7 && open[0].ContentId == "root" && open[^1].Page == GluttonyPage.PvP, "An open job keeps Jobs and PvP reachable beside every content destination.");
        Check(open.Select(x => x.Id).Distinct().Count() == open.Count, "Feature IDs stay unique.");
        var pvp = GluttonyShellNavigation.Features(GluttonyPage.PvP, true, null, false);
        Check(pvp.Count == 2 && pvp[1].Page == GluttonyPage.PvP && pvp[1].ContentId == "root", "An open PvP job returns to the PvP job list from its own entry.");
        Check(GluttonyShellNavigation.Features(GluttonyPage.Settings, false, null, true).Any(x => x.Page == GluttonyPage.Debug) &&
              !GluttonyShellNavigation.Features(GluttonyPage.Settings, false, null, false).Any(x => x.Page == GluttonyPage.Debug), "Debug navigation stays build-configuration scoped.");
        Check(GluttonyShellNavigation.ShowSearch(GluttonyPage.Settings, false, true) && !GluttonyShellNavigation.ShowSearch(GluttonyPage.Settings, false, false), "Settings search honors the saved visibility setting.");
        Check(!GluttonyShellNavigation.ShowSearch(GluttonyPage.PvE, false, true) && GluttonyShellNavigation.ShowSearch(GluttonyPage.PvP, true, true) &&
              !GluttonyShellNavigation.ShowSearch(GluttonyPage.AutoRotation, true, true), "Search appears only on settings and open preset surfaces.");
        Check(GluttonyShellNavigation.Activity(false, false) == GluttonyActivity.Ready && GluttonyShellNavigation.Activity(true, false) == GluttonyActivity.Running &&
              GluttonyShellNavigation.Activity(true, true) == GluttonyActivity.Blocked, "AutoRotation state vocabulary maps to Ready, Running and Blocked.");
        Check(GluttonyChrome.Gutter(1040) == 24 && GluttonyChrome.Gutter(700) == 16 && GluttonyChrome.Gutter(500) == 12, "Gutter steps match the suite function.");
    }

    private static void VerifyToggleInteraction()
    {
        var v = false;
        var center = Vector2.Zero;
        ClickLoop(step =>
        {
            ImGui.SetCursorPos(new Vector2(20, 20));
            if (step == 0)
                center = ImGui.GetCursorScreenPos() + new Vector2(ImGui.GetContentRegionAvail().X - 22, 12);
            GluttonyChrome.Toggle("Interaction setting", ref v);
        }, () => center);
        Check(v, "A real press and release changes the drawn toggle value.");
    }

    private static void VerifyCloseInteraction()
    {
        var closed = false;
        var center = new Vector2(100, 40);
        ClickLoop(step =>
        {
            if (GluttonyChrome.CloseButton(ImGui.GetWindowDrawList(), new Vector2(82, 22), new Vector2(36)))
                closed = true;
        }, () => center);
        Check(closed, "A real press and release on the drawn close box reports a close.");
    }

    private static void ClickLoop(Action<int> draw, Func<Vector2> target)
    {
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.DisplaySize = new Vector2(480, 120);
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            _ = UiLook.Apply(RenderOptions.BuiltIn);
            for (var step = 0; step < 4; step++)
            {
                if (step == 0)
                    io.AddMousePosEvent(-10000, -10000);
                else
                    io.AddMousePosEvent(target().X, target().Y);
                io.AddMouseButtonEvent(0, step == 2);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(io.DisplaySize);
                ImGui.Begin("interaction", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings);
                draw(step);
                ImGui.End();
                ImGui.Render();
            }
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    private static void CheckGlyphs()
    {
        if (!_options.IsGame)
            return;
        Console.WriteLine($"Glyph check: {_nonAscii} non-ASCII characters drawn, {Missing.Count} missing.");
        Check(Missing.Count == 0, "Every non-ASCII character drawn has a glyph in the game font: " + string.Join(", ", Missing));
    }

    private static void Fill(byte[] frame, byte r, byte g, byte b)
    {
        for (var i = 0; i < frame.Length; i += 4)
        {
            frame[i] = r;
            frame[i + 1] = g;
            frame[i + 2] = b;
            frame[i + 3] = 255;
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
        _checks++;
    }
}
