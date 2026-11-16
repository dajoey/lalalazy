using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using Dalamud.Bindings.ImGui;
using GluttonyCombo.Window;
using LalaUiKit;
using Lala.UiRender;
using Widgets = GluttonyCombo.Window.Widgets;

namespace GluttonyCombo.UiHarness;

// Renders the real shell drawing (GluttonyShellView + GluttonyChrome), the real preset card box (InfoBox), the real
// text and notice widgets and the real "New changes" window drawing and wording. The plugin's own preset list, settings
// and rotation renderers depend on live game state and are not linked: their pages show sample bodies that make the same
// calls (InfoBox around toggle rows, loose toggles under headings, collapsing sections) with a small set of sample data.
internal static unsafe class Program
{
    private static int _checks;
    private static string _outputDirectory = "";
    private static RenderOptions _options = RenderOptions.BuiltIn;
    private static readonly SortedSet<string> Missing = [];
    private static int _nonAscii;
    private static readonly List<string> Drawn = [];
    private static readonly Dictionary<string, List<int>> RulesByScenario = [];
    private static readonly Dictionary<string, int> RightEdgeByScenario = [];
    private static readonly Dictionary<string, float> SearchWidthByScenario = [];
    private static int _reportDraws;

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
        public bool BodyCard;
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
            if (_options.IsGame)
                VerifyConsistency();
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
            BrandHelp = "GluttonyCombo v1.2.3",
            Title = title,
            Description = description,
            Meta = meta.Length == 0 ? [] : [meta],
            ReportButton = () => { _reportDraws++; ImGui.Button("Report a problem##r"); },
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
                search.Length > 0 ? "3 results" : "12 preset groups", pve, search: true, descriptions: true),
        };

        if (!game)
        {
            return
            [
                new Scenario { Name = "shell-pve-jobs", Width = 960, Height = 700, Body = JobGrid(false), Model = () => Model("Combos", UiState.Ready, "PvE Features", "Select a job to see its combos.", "24 jobs", Features("Combos", 0)) },
                Pve("shell-pve-presets", 960, 700, PresetList),
                new Scenario { Name = "shell-autorotation-running", BodyCard = true, Width = 960, Height = 700, Body = AutoRotationPage, Model = () => Model("Automation", UiState.Running, "AutoRotation", "Choose how the rotation picks targets.", "4 auto modes", Features("Automation", 0), stop: "Stop") },
                new Scenario { Name = "major-changes", Kind = Kind.Major, Width = 780, Height = 620, Model = () => new ShellModel() },
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
            Pve("main-pve-nested-options-1040x720", WideW, WideH, NestedList),
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
            new() { Name = "main-autorotation-running-1040x720", BodyCard = true, Body = AutoRotationPage,
                Model = () => Model("Automation", UiState.Running, "AutoRotation", "Choose how the rotation picks targets, heals, and uses actions.", "4 auto modes", Features("Automation", 0), stop: "Stop") },
            new() { Name = "main-autorotation-blocked-700x640", BodyCard = true, Width = NarrowW, Height = NarrowH, Body = AutoRotationPage,
                Model = () => Model("Automation", UiState.Blocked, "AutoRotation", "Choose how the rotation picks targets, heals, and uses actions.", "4 auto modes", Features("Automation", 0), stop: "Stop") },
            new() { Name = "main-custom-actions-1040x720", BodyCard = true, Body = CustomActionsPage,
                Model = () => Model("Automation", UiState.Ready, "Custom Actions", "Drag bespoke rotation and utility actions to the hotbar.", "8 rotation · 5 utility", Features("Automation", 1)) },
            // System.
            new() { Name = "main-settings-1040x720", BodyCard = true, Body = SettingsPage,
                Model = () => Model("System", UiState.Ready, "Settings", "Everything that changes how the combos behave.", "62 settings", Features("System", 0), search: true, hint: "Search settings...") },
            new() { Name = "main-settings-700x640", BodyCard = true, Width = NarrowW, Height = NarrowH, Body = SettingsPage,
                Model = () => Model("System", UiState.Ready, "Settings", "Everything that changes how the combos behave.", "62 settings", Features("System", 0), search: true, hint: "Search settings...") },
            new() { Name = "main-about-1040x720", Body = AboutPage,
                Model = () => Model("System", UiState.Ready, "GluttonyCombo", "A lalalazy fork of Wrath Combo. All upstream credit to Team Wrath / PunishXIV.", "v1.2.3 · 640 job presets", Features("System", 1)) },
            new() { Name = "main-close-tooltip-1040x720", HoverClose = true, Body = JobGrid(false),
                Model = () => Model("Combos", UiState.Ready, "PvE Features", "Select a job to see its combos.", "24 jobs", Features("Combos", 0)) },
            // Other windows.
            new() { Name = "major-changes-780x620", Kind = Kind.Major, Width = 780, Height = 620, Model = () => new ShellModel() },
            new() { Name = "major-changes-560x500", Kind = Kind.Major, Width = 560, Height = 500, Model = () => new ShellModel() },
            new() { Name = "overlay-fight-brain-strip-260x200", Kind = Kind.Strip, Width = 260, Height = 200, Model = () => new ShellModel(), Body = StripBody },
        ];
    }

    // ---- sample bodies ----
    // The preset cards are the plugin's own InfoBox drawn the way the real renderers call it; the rows inside follow
    // Presets.DrawPreset (toggle, indented description, children). Job tiles, notices, headings and toggles are the
    // plugin's shared drawing code. The preset data itself is a small sample set.

    private static Action JobGrid(bool pvp) => () =>
    {
        string[] regular = ["Paladin - PLD", "Warrior - WAR", "Dark Knight - DRK", "Gunbreaker - GNB", "White Mage - WHM", "Scholar - SCH",
            "Astrologian - AST", "Sage - SGE", "Monk - MNK", "Dragoon - DRG", "Ninja - NIN", "Samurai - SAM", "Reaper - RPR", "Viper - VPR",
            "Bard - BRD", "Machinist - MCH", "Dancer - DNC", "Black Mage - BLM", "Summoner - SMN", "Red Mage - RDM", "Pictomancer - PCT"];
        string[] special = ["Adventurer - ADV", "Blue Mage - BLU", "Miner - MIN", "Botanist - BTN", "Fisher - FSH"];
        void Table(string id, string[] jobs)
        {
            if (!GluttonyChrome.BeginTileTable(id, ImGui.GetContentRegionAvail().X, out _))
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
            GluttonyChrome.EndTileTable();
        }
        if (!pvp)
        {
            Widgets.Title("Regular Jobs");
            Table("pve_regular", regular);
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

    private static bool _a = true, _b = true, _c = true, _d, _e = true;

    private sealed record Row(string Name, string Description, bool On, Row[]? Children = null);

    // Mirrors Presets.DrawPreset: spacing, toggle, an indented secondary description, then the children one level in.
    private static void DrawPresetRow(Row row, Func<bool> state, Action<bool> set)
    {
        var enabled = state();
        ImGui.Spacing();
        if (GluttonyChrome.Toggle($"{row.Name}###{row.Name}", ref enabled))
            set(enabled);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.Text(string.Empty);
        ImGui.SameLine();
        GluttonyChrome.WrappedText(row.Description);
        ImGui.PopStyleColor();
        ImGui.Spacing();
        Drawn.Add(row.Name + row.Description);
        if (row.Children is null || !enabled)
            return;
        ImGui.Indent();
        foreach (var child in row.Children)
        {
            var on = child.On;
            if (child.Children is { Length: > 0 })
            {
                var box = new InfoBox { HasMaxWidth = true, CurveRadius = 4f, ContentsAction = () => DrawPresetRow(child, () => on, v => on = v) };
                box.Draw();
                ImGui.Spacing();
            }
            else
                DrawPresetRow(child, () => on, v => on = v);
        }
        ImGui.Unindent();
    }

    // Mirrors PvEFeatures.DrawHeadingContents: one InfoBox per top-level preset, a spacing after each.
    private static void PresetBox(Row row, ref bool on)
    {
        var enabled = on;
        var box = new InfoBox { ContentsOffset = 5f, ContentsAction = () => DrawPresetRow(row, () => enabled, v => enabled = v) };
        box.Draw();
        ImGui.Spacing();
        on = enabled;
    }

    private static void PresetList()
    {
        PresetBox(new("Advanced Mode - Single Target", "Replaces Fast Blade with a full single target rotation, tuned for the current job gauge.", true,
        [
            new("Include Mitigations", "Uses defensive actions at set health thresholds.", true),
            new("Use Interrupt", "Interrupts enemy casts when able.", false),
            new("Burst only on bosses", "Keeps burst actions for boss targets.", true),
        ]), ref _a);
        PresetBox(new("Advanced Mode - AoE", "Replaces Total Eclipse with the area rotation.", true,
        [
            new("Include Mitigations", "Uses defensive actions when several enemies attack.", true,
            [
                new("Reprisal", "Uses Reprisal when the enemy count is reached.", true),
            ]),
        ]), ref _b);
        PresetBox(new("Simple Mode - Single Target", "Recommended values, no options to tune.", true), ref _c);
        PresetBox(new("Mitigation - Tankbusters", "Uses defensive actions when a tankbuster is detected.", true,
            [new("Warn in chat", "Prints a chat line before a tankbuster.", false)]), ref _d);
        PresetBox(new("Balance Opener", "Opens the fight with the standard burst sequence.", true), ref _e);
    }

    // A preset whose option has options of its own: the inner level is drawn in a nested box.
    private static void NestedList()
    {
        PresetBox(new("Advanced Mode - AoE", "Replaces Total Eclipse with the area rotation.", true,
        [
            new("Include Mitigations", "Uses defensive actions when several enemies attack.", true,
            [
                new("Reprisal", "Uses Reprisal when the enemy count is reached.", true),
                new("Arm's Length", "Uses Arm's Length when the enemy count is reached.", false),
            ]),
            new("Interrupt", "Interrupts enemy casts when able.", false),
        ]), ref _b);
        PresetBox(new("Simple Mode - AoE", "Recommended values, no options to tune.", true), ref _c);
    }

    private static void SearchResults()
    {
        PresetBox(new("Mitigation - Tankbusters", "Uses defensive actions when a tankbuster is detected.", true,
            [new("Warn in chat", "Prints a chat line before a tankbuster.", false)]), ref _d);
        PresetBox(new("Advanced Mode - Single Target", "Includes mitigation options at set health thresholds.", true,
            [new("Include Mitigations", "Uses defensive actions at set health thresholds.", true)]), ref _a);
        PresetBox(new("Advanced Mode - AoE", "Includes mitigation options when several enemies attack.", true), ref _b);
    }

    // Mirrors AutoRotationTab.Draw: loose toggles under headings, then collapsing sections.
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
        ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        if (ImGui.CollapsingHeader("Damage settings"))
        {
            GluttonyChrome.Toggle("Attack an in-range enemy when the chosen target is out of range", ref t);
            GluttonyChrome.Toggle("Prefer non-combat targets", ref o);
        }
        if (ImGui.CollapsingHeader("Healing settings"))
            GluttonyChrome.Toggle("Manage Kardia", ref t);
        Drawn.Add("Damage settings Healing settings");
    }

    // Mirrors CustomActions.Draw: a heading, wrapped explanation, rows of toggles and a closing toggle.
    private static void CustomActionsPage()
    {
        bool a = true, b = false;
        Widgets.Title("Rotation Buttons");
        Widgets.SecondaryWrapped("These buttons replace the need to replace actions for all features marked as either Simple or Advanced. Enable the type of combo you're wanting to use custom actions for, and drag the icon to your hotbar.");
        GluttonyChrome.Toggle("Single Target DPS##ca1", ref a);
        GluttonyChrome.Toggle("AoE DPS##ca2", ref b);
        GluttonyChrome.Toggle("Single Target Heals##ca3", ref a);
        GluttonyChrome.Toggle("AoE Heals##ca4", ref b);
        GluttonyChrome.Toggle("Don't Override Icons with Job Actions (drag action to hotbar again to take effect)##ca5", ref b);
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

    // Mirrors GluttonyShell.DrawAbout.
    private static void AboutPage()
    {
        GluttonyChrome.Card("about_links", () =>
        {
            Widgets.Title("Source and credits");
            Widgets.SecondaryWrapped("GluttonyCombo preserves the upstream combo suite and its fork integrations.");
            GluttonyChrome.StyledButton("GitHub: dajoey/lalalazy");
            GluttonyChrome.StyledButton("Additional custom action icons by alexisoffline");
        });
        GluttonyChrome.Card("about_support", () =>
        {
            Widgets.Title("Support");
            Widgets.SecondaryWrapped("Conflict warnings stay visible above every page. Reports use the existing telemetry surface.");
            GluttonyChrome.StyledButtonCallback(ImGui.GetCursorScreenPos(), () => ImGui.Button("Report a problem##about"));
        });
    }

    // Mirrors FightBrainStrip.Draw.
    private static void StripBody()
    {
        using var card = GluttonyChrome.BeginCard(0, GluttonyChrome.OverlayCardWidth);
        GluttonyChrome.ForecastHeader("Fight Brain", "3 code forecasts");
        (double, string)[] rows = [(4.2, "raidwide"), (11.8, "tankbuster"), (19.5, "knockback")];
        foreach (var (seconds, label) in rows)
            GluttonyChrome.ForecastRow(seconds, label, seconds <= rows[0].Item1);
        ImGui.Dummy(new Vector2(1, 1));
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
                _reportDraws = 0;
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
            {
                VerifyGeometry(s, frame);
                VerifyLayout(s, frame);
            }
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
                GluttonyShellView.Draw(model, ref search, ref descriptions, null, s.Body, s.BodyCard);
                ImGui.End();
                break;
            case Kind.Major:
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(new Vector2(s.Width, s.Height));
                ImGui.Begin("Gluttony Combo | New Changes", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize);
                Theme.PushFrame();
                GluttonyChrome.PushControlColors();
                GluttonyMajorChanges.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize(), GluttonyMajorChanges.Sections(true), "majorChangesBody");
                Drawn.Add(string.Concat(GluttonyMajorChanges.Sections(true).SelectMany(x => x.Paragraphs.Append(x.Title).Append(x.Alert ?? ""))) +
                          GluttonyMajorChanges.DismissLabel);
                GluttonyChrome.PopControlColors();
                Theme.PopFrame();
                ImGui.End();
                break;
            case Kind.Strip:
                ImGui.SetNextWindowPos(new Vector2(20, 20));
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
                ImGui.Begin("strip", ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize |
                    ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoInputs);
                Theme.PushFrame();
                s.Body();
                Theme.PopFrame();
                ImGui.End();
                ImGui.PopStyleVar();
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

    /// <summary>Per-image measurements on the pixels; the cross-image comparisons run in <see cref="VerifyConsistency"/>.</summary>
    private static void VerifyLayout(Scenario s, byte[] f)
    {
        var w = s.Width;
        var g = (int)GluttonyChrome.Gutter(w);
        Check(_reportDraws == 1, $"{s.Name}: the problem-report button is drawn exactly once, so it is reachable in this layout (drew {_reportDraws}).");
        RulesByScenario[s.Name] = Enumerable.Range(0, s.Height).Where(y => Near(Px(f, w, 3, y), Theme.SurfaceBorder, 10) && Near(Px(f, w, w - 3, y), Theme.SurfaceBorder, 10)).ToList();
        // Right edge of body content: the rightmost non-background pixel between the bands, left of the scroll bar zone.
        var bg = Px(f, w, 3, 300);
        var right = 0;
        for (var y = 200; y < s.Height - 120; y++)
            for (var x = w - 14; x > w / 2; x--)
            {
                var c = Px(f, w, x, y);
                if (Math.Abs(c.R - bg.R) > 8 || Math.Abs(c.G - bg.G) > 8 || Math.Abs(c.B - bg.B) > 8)
                {
                    right = Math.Max(right, x);
                    break;
                }
            }
        RightEdgeByScenario[s.Name] = right;
        Check(right >= w - g - 2 && right <= w - g, $"{s.Name}: body content ends at the gutter (right edge {right}, expected {w - g - 1}..{w - g}).");
        SearchWidthByScenario[s.Name] = GluttonyShellView.LastSearchWidth;
        var rules = RulesByScenario[s.Name];
        if (s.BodyCard)
        {
            var card = Px(f, w, g + 6, 300);
            Check(Near(card, Theme.Surface, 6) && Math.Abs(Px(f, w, 4, 300).R - bg.R) <= 6, $"{s.Name}: the loose page sits on one shared card ({card.R},{card.G},{card.B}).");
        }
        if (s.Name.StartsWith("main-first-run-pve-jobs") || s.Name.StartsWith("main-pve-job-presets"))
        {
            // Content that runs on below the footer fades into the window background instead of being sliced.
            var footerTop = rules[^1];
            var worst = 0;
            for (var x = g + 8; x < w - g - 14; x++)
            {
                var c = Px(f, w, x, footerTop - 3);
                worst = Math.Max(worst, Math.Max(Math.Abs(c.R - bg.R), Math.Max(Math.Abs(c.G - bg.G), Math.Abs(c.B - bg.B))));
            }
            Check(worst <= 45, $"{s.Name}: the last rows above the footer fade out (largest difference from the window background {worst}).");
        }
    }

    private static void VerifyConsistency()
    {
        string W(string n) => n;
        var normal = RulesByScenario["main-pve-job-presets-1040x720"];
        var collapsed = RulesByScenario["main-pve-destinations-collapsed-1040x720"];
        Check(normal.Take(2).SequenceEqual(collapsed.Take(2)), "The sub-navigation band has the same height collapsed and expanded (rules " +
              $"{string.Join(",", normal.Take(2))} vs {string.Join(",", collapsed.Take(2))}).");
        var searchPages = new[] { "main-pve-job-presets-1040x720", "main-settings-1040x720", "main-pve-search-results-1040x720", "main-pvp-job-presets-1040x720" };
        Check(searchPages.Select(n => SearchWidthByScenario[W(n)]).Distinct().Count() == 1, "The search field has one width on every page of the wide window.");
        var narrowPages = new[] { "main-pve-job-presets-700x640", "main-settings-700x640", "main-pve-adventurer-content-700x640" };
        Check(narrowPages.Select(n => SearchWidthByScenario[W(n)]).Distinct().Count() == 1, "The search field has one width on every page of the narrow window.");
        var wideEdges = RightEdgeByScenario.Where(kv => kv.Key.EndsWith("1040x720")).Select(kv => kv.Value).Distinct().ToList();
        Check(wideEdges.Count <= 2 && wideEdges.Max() - wideEdges.Min() <= 1, $"Scrolling and non-scrolling pages share one right edge ({string.Join(",", wideEdges)}).");
        var narrowEdges = RightEdgeByScenario.Where(kv => kv.Key.EndsWith("700x640")).Select(kv => kv.Value).Distinct().ToList();
        Check(narrowEdges.Max() - narrowEdges.Min() <= 1, $"The narrow window shares one right edge too ({string.Join(",", narrowEdges)}).");
        Check(GluttonyChrome.StateColor(UiState.Running) != GluttonyChrome.StateColor(UiState.Blocked), "Running and Blocked read differently.");
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

        // A page that throws inside a half-drawn preset card leaves the frame usable once the body is closed.
        var frames = 0;
        ClickLoop(step =>
        {
            try
            {
                GluttonyChrome.BeginBody(new Vector2(400, 100), 16, "##throwing");
                try
                {
                    GluttonyChrome.InfoBoxBegin(false);
                    throw new InvalidOperationException("sample page failure");
                }
                finally
                {
                    GluttonyChrome.EndBody();
                }
            }
            catch (InvalidOperationException)
            {
                frames++;
            }
        }, () => Vector2.Zero);
        Check(frames == 4, "Four frames in a row survive a page that throws inside an open preset card.");

        // The label toggles too, and the row's last item is the label, so a marker that follows sits beside the text.
        var w = false;
        var labelPoint = Vector2.Zero;
        float lastItemWidth = 0, markerX = 0, labelLeft = 0;
        ClickLoop(step =>
        {
            ImGui.SetCursorPos(new Vector2(20, 20));
            if (step == 0)
                labelPoint = ImGui.GetCursorScreenPos() + new Vector2(10, 12);
            labelLeft = ImGui.GetCursorScreenPos().X;
            GluttonyChrome.Toggle("Interaction setting", ref w);
            lastItemWidth = ImGui.GetItemRectMax().X - ImGui.GetItemRectMin().X;
            ImGui.SameLine();
            markerX = ImGui.GetCursorScreenPos().X;
            ImGui.TextDisabled("(?)");
        }, () => labelPoint);
        Check(w, "A press on the label text also changes the toggle value.");
        Check(lastItemWidth < 200 && markerX - labelLeft < 220, $"A marker drawn after a toggle sits beside the label, not at the row's far edge (marker at +{markerX - labelLeft:F0} px).");
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
