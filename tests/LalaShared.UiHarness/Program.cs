using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Lala.UiRender;
using Lalalazy.Changelog;
using Lalalazy.Telemetry;

namespace LalaShared.UiHarness;

// Renders the real shared popups to PNG: the "What's new" changelog window (ChangelogWindow, fed with real CHANGELOG.md
// files from this repository) and the "Report a problem" popup (ReportPopup). Built-in mode (default) is hermetic;
// "--game" renders the game look into output/game/. Only DalamudTelemetry is stubbed (Stubs.cs).
internal static class Program
{
    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    private static string OutputDirectory = Path.Combine(ProjectRoot, "output");
    private static RenderOptions Options = RenderOptions.BuiltIn;
    private static bool _lookReported;
    private static bool _missingGlyphs;
    private static bool _strictGlyphs;

    // First-use size of the changelog window and the smallest size it allows (ChangelogWindow: Size / SizeConstraints).
    private const int ChangelogW = 640, ChangelogH = 560;
    private const int MinW = 420, MinH = 420;
    private const float PopupWidth = 520f;

    private static string SourceRoot => typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "LalaSourceRoot")?.Value
        ?? throw new InvalidOperationException("LalaSourceRoot is not set.");

    private static List<ChangelogEntry> Changelog(string plugin)
        => ChangelogParser.Parse(File.ReadAllText(Path.Combine(SourceRoot, "src", plugin, "CHANGELOG.md")));

    // ---- scenes ---------------------------------------------------------------------------------------------

    private abstract class Scene
    {
        public abstract void Draw(Vector2 size);

        /// <summary>Screen position the pointer should rest on in this frame (null = away from everything).</summary>
        public virtual Vector2? Pointer(Vector2 size, int frame) => null;
    }

    private sealed class ChangelogScene : Scene
    {
        public ChangelogWindow Window { get; }

        public bool HoverClose { get; init; }

        public ChangelogScene(string name, IReadOnlyList<ChangelogEntry> entries, Version current, Version? previous, bool expandAll)
        {
            Window = new ChangelogWindow(name, "https://example.invalid/changelog", () => { });
            Window.Show(entries, current, previous);
            if (expandAll)
            {
                // The version cards are collapsible; a press on a header expands one, so this does the same for all.
                var field = typeof(ChangelogWindow).GetField("_expanded", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.GetValue(Window) is HashSet<int> set)
                    for (var i = 0; i < entries.Count; i++)
                        set.Add(i);
            }
        }

        public override void Draw(Vector2 size)
        {
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(size);
            var open = true;
            Window.PreDraw();
            ImGui.Begin(Window.WindowName, ref open, Window.Flags |
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
            Window.Draw();
            ImGui.End();
            Window.PostDraw();
        }

        public override Vector2? Pointer(Vector2 size, int frame)
            => HoverClose ? new Vector2(size.X - Gutter(size.X) - 18, 34) : null;
    }

    private sealed class ReportScene : Scene
    {
        private static readonly FieldInfo TextField = typeof(ReportPopup).GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo IdField = typeof(ReportPopup).GetField("_lastId", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo OpenField = typeof(ReportPopup).GetField("_openRequested", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private bool _opened;

        public DalamudTelemetry Telemetry { get; }

        public ReportPopup Popup { get; }

        public bool FromTab { get; init; }

        public bool HoverClose { get; init; }

        public ReportScene(string displayName, string text = "", string? lastId = null)
        {
            Telemetry = new DalamudTelemetry(displayName);
            Popup = new ReportPopup(Telemetry);
            TextField.SetValue(Popup, text);
            // The popup clears the id when it opens; a state that shows it keeps it set after the first draw.
            LastId = lastId;
        }

        private string? LastId { get; }

        public override void Draw(Vector2 size)
        {
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(size);
            ImGui.Begin("Host window", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
            ImGui.TextUnformatted("Plugin window content");
            ImGui.TextDisabled("The host window the popup opens from.");
            ImGui.Spacing();
            if (!_opened)
            {
                _opened = true;
                OpenField.SetValue(Popup, true);
            }

            if (FromTab)
            {
                if (ImGui.BeginTabBar("##host-tabs"))
                {
                    if (ImGui.BeginTabItem("Settings"))
                    {
                        ImGui.TextUnformatted("Settings tab");
                        ImGui.EndTabItem();
                    }

                    Popup.DrawTabButton("Report a problem");
                    ImGui.EndTabBar();
                }
            }
            else
            {
                Popup.DrawButton("Report a problem");
            }

            ImGui.End();
            if (LastId is not null && IdField.GetValue(Popup) is null && _opened)
                IdField.SetValue(Popup, LastId);
        }

        public override Vector2? Pointer(Vector2 size, int frame)
        {
            if (!HoverClose || frame < 3)
                return null;
            var (pos, popup) = PopupRect();
            return new Vector2(pos.X + popup.X - PopupGutter - 18, pos.Y + 34);
        }
    }

    private const float PopupGutter = 16f;

    /// <summary>Position and size of the open modal popup, read from the ImGui context.</summary>
    private static (Vector2 Pos, Vector2 Size) PopupRect()
    {
        var window = ImGuiP.GetTopMostPopupModal();
        return (window.Pos, window.Size);
    }

    private static float Gutter(float width) => width >= 760 ? 24 : width >= 560 ? 16 : 12;

    private sealed record Scenario(string Name, int Width, int Height, Func<Scene> Make)
    {
        /// <summary>Wheel-scrolls the changelog body to the bottom (real wheel input over the body).</summary>
        public bool ScrollToBottom { get; init; }

        /// <summary>Overrides the render scale (evidence only).</summary>
        public float? Scale { get; init; }

        /// <summary>Render only when the game look is active.</summary>
        public bool GameOnly { get; init; }
    }

    private static readonly Version Current = new(0, 5, 5, 0);

    private static Scenario[] BuildScenarios()
    {
        // Real changelog content: a short single entry, and several long blocks with "### Changed" sub-headings.
        List<ChangelogEntry> OneShort() => ChangelogParser.Between(Changelog("LazyHub"), new Version(0, 1, 1, 5), new Version(0, 1, 1, 6));
        List<ChangelogEntry> Several() => ChangelogParser.Between(Changelog("ArmoireAutoFill"), new Version(0, 5, 0, 0), Current);
        List<ChangelogEntry> LongBullets() => ChangelogParser.Between(Changelog("LazyMarketCompanion"), null, new Version(int.MaxValue, 0, 0, 0)).Take(8).ToList();
        List<ChangelogEntry> Everything() => ChangelogParser.Between(Changelog("ArmoireAutoFill"), null, new Version(int.MaxValue, 0, 0, 0));

        Scene Short() => new ChangelogScene("LazyHub", OneShort(), new Version(0, 1, 1, 6), new Version(0, 1, 1, 5), false);
        Scene Multi(bool all, bool hover = false) => new ChangelogScene("ArmoireAutoFill", Several(), Current, new Version(0, 5, 0, 0), all) { HoverClose = hover };

        const string typed = "The Armoire window shows 12 items as missing even though I own them. I expected the count to match the cabinet.\n\nIt happens after a zone change and goes away when I reopen the window.";

        return
        [
            new("changelog-short-640x560", ChangelogW, ChangelogH, () => Short()),
            new("changelog-short-minimum-size", MinW, MinH, () => Short()),
            new("changelog-several-640x560", ChangelogW, ChangelogH, () => Multi(false)),
            new("changelog-several-expanded-640x560", ChangelogW, ChangelogH, () => Multi(true)),
            new("changelog-several-expanded-scrolled-640x560", ChangelogW, ChangelogH, () => Multi(true)) { ScrollToBottom = true },
            new("changelog-several-expanded-420x640", MinW, 640, () => Multi(true)),
            new("changelog-several-expanded-420x640-scrolled", MinW, 640, () => Multi(true)) { ScrollToBottom = true },
            new("changelog-several-expanded-minimum-size", MinW, MinH, () => Multi(true)),
            new("changelog-several-expanded-900x700", 900, 700, () => Multi(true)),
            new("changelog-long-bullets-640x560", ChangelogW, ChangelogH,
                () => new ChangelogScene("LazyMarketCompanion", LongBullets(), new Version(99, 0, 0, 0), null, true)),
            new("changelog-all-entries-640x560", ChangelogW, ChangelogH,
                () => new ChangelogScene("ArmoireAutoFill", Everything(), Current, null, false)),
            new("changelog-collapsed-cards-640x560", ChangelogW, ChangelogH,
                () => new ChangelogScene("LazyHub", ChangelogParser.Between(Changelog("LazyHub"), null, new Version(int.MaxValue, 0, 0, 0)), new Version(0, 1, 1, 6), null, false)),
            new("changelog-long-name-420x560", MinW, 560,
                () => new ChangelogScene("LazyCurrencySpender", OneShort(), new Version(0, 1, 1, 6), new Version(0, 1, 1, 5), true)),
            new("changelog-empty-640x560", ChangelogW, ChangelogH,
                () => new ChangelogScene("LazyHub", [], new Version(0, 1, 1, 6), null, false)),
            new("changelog-hover-close-640x560", ChangelogW, ChangelogH, () => Multi(false, hover: true)),
            new("changelog-scale150-640x560", ChangelogW, ChangelogH, () => Multi(false)) { Scale = 1.5f, GameOnly = true },

            new("report-empty-800x600", 800, 600, () => new ReportScene("ArmoireAutoFill")),
            new("report-text-800x600", 800, 600, () => new ReportScene("ArmoireAutoFill", typed)),
            new("report-written-800x600", 800, 600, () => new ReportScene("ArmoireAutoFill", lastId: "7F3A9C21")),
            new("report-from-tab-800x600", 800, 600, () => new ReportScene("ArmoireAutoFill", typed) { FromTab = true }),
            new("report-hover-close-800x600", 800, 600, () => new ReportScene("ArmoireAutoFill") { HoverClose = true }),
            new("report-long-name-560x500", 560, 500, () => new ReportScene("LazyCurrencySpender", typed)),
            new("report-scale150-800x600", 800, 600, () => new ReportScene("ArmoireAutoFill", typed)) { Scale = 1.5f, GameOnly = true },
        ];
    }

    // The scenarios that render in built-in mode (the hermetic, byte-stable subset).
    private static readonly string[] BuiltInNames = ["changelog-several-640x560", "report-empty-800x600"];

    // ---- main -----------------------------------------------------------------------------------------------

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

            var all = BuildScenarios();
            var scenarios = Options.IsGame ? all : [.. all.Where(s => BuiltInNames.Contains(s.Name))];
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
                throw new InvalidOperationException("--strict-glyphs: text the popups draw contains characters the game font lacks.");

            if (args.Contains("--no-checks"))
                return 0;
            VerifyChangelogClose();
            Console.WriteLine("Changelog: the drawn close box and the Got it button close the window.");
            VerifyChangelogToggle();
            Console.WriteLine("Changelog: a press on the newest version card header collapses it.");
            VerifyReportButtons();
            Console.WriteLine("Report popup: Cancel and the close box close it; Write report files the typed text, clears it and closes; a refused write keeps it open.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
    }

    // ---- rendering ------------------------------------------------------------------------------------------

    private static void WithContext(Vector2 size, RenderOptions options, Action<ImGuiIOPtr, RenderLook> body)
    {
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = size;
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            io.MousePos = new Vector2(-10000, -10000);
            var look = UiLook.Apply(options);
            body(io, look);
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    private static RenderResult RenderScenario(Scenario scenario)
    {
        var scene = scenario.Make();
        var size = new Vector2(scenario.Width, scenario.Height);
        var options = scenario.Scale is { } scale && Options.IsGame ? Options with { Scale = scale } : Options;
        var path = Path.Combine(OutputDirectory, scenario.Name + ".png");
        var triangles = 0;
        WithContext(size, options, (io, look) =>
        {
            if (!_lookReported)
            {
                _lookReported = true;
                Console.WriteLine($"Look: {look.Description}");
                if (Options.IsGame)
                    CheckGlyphs(look);
            }

            // Several frames settle window storage, auto-sized popups and tooltips; scrolling needs extra frames of wheel input.
            var frames = scenario.ScrollToBottom ? 16 : 8;
            for (var frame = 0; frame < frames; frame++)
            {
                io.MousePos = scene.Pointer(size, frame) ?? new Vector2(-10000, -10000);
                if (scenario.ScrollToBottom)
                {
                    io.MousePos = new Vector2(size.X * 0.4f, size.Y * 0.5f);
                    io.MouseWheel = frame >= 2 && frame < frames - 2 ? -20f : 0f;
                }

                ImGui.NewFrame();
                scene.Draw(size);
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
            triangles = CpuRasterizer.Render(drawData, pixels, scenario.Width, scenario.Height, look.Textures).Triangles;
            PngWriter.Write(path, pixels, scenario.Width, scenario.Height);
        });
        return new RenderResult(path, triangles);
    }

    // ---- behaviour checks -----------------------------------------------------------------------------------

    private static void Press(Vector2 size, Func<Scene> make, Func<Scene, Vector2> target, Action<Scene> check, int settleFrames = 6)
    {
        var scene = make();
        WithContext(size, Options, (io, _) =>
        {
            for (var step = 0; step < settleFrames; step++)
            {
                io.MousePos = new Vector2(-10000, -10000);
                ImGui.NewFrame();
                scene.Draw(size);
                ImGui.Render();
            }

            var position = target(scene);
            // Move onto the control, press, release: a click needs the press and the release on the same frame boundary pair.
            for (var step = 0; step < 4; step++)
            {
                io.MousePos = position;
                io.MouseDown[0] = step == 2;
                ImGui.NewFrame();
                scene.Draw(size);
                ImGui.Render();
            }

            io.MouseDown[0] = false;
            check(scene);
        });
    }

    private static void VerifyChangelogClose()
    {
        var size = new Vector2(ChangelogW, ChangelogH);
        var g = Gutter(size.X);
        Scene Make() => new ChangelogScene("ArmoireAutoFill", ChangelogParser.Between(Changelog("ArmoireAutoFill"), new Version(0, 5, 0, 0), Current), Current, new Version(0, 5, 0, 0), false);

        foreach (var (what, point) in new[]
        {
            ("close box", new Vector2(size.X - g - 18, 34)),
            ("Got it", new Vector2(size.X - g - 48, size.Y - 30)),
        })
        {
            Press(size, Make, _ => point, scene =>
            {
                if (((ChangelogScene)scene).Window.IsOpen)
                    throw new InvalidOperationException($"The changelog {what} did not close the window after a real press and release.");
            });
        }

        // A press elsewhere (the page header) must leave it open.
        Press(size, Make, _ => new Vector2(size.X / 2, 100), scene =>
        {
            if (!((ChangelogScene)scene).Window.IsOpen)
                throw new InvalidOperationException("A press on the page header closed the changelog window.");
        });
    }

    private static void VerifyChangelogToggle()
    {
        var size = new Vector2(ChangelogW, ChangelogH);
        var field = typeof(ChangelogWindow).GetField("_expanded", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Scene Make() => new ChangelogScene("ArmoireAutoFill", ChangelogParser.Between(Changelog("ArmoireAutoFill"), new Version(0, 5, 0, 0), Current), Current, new Version(0, 5, 0, 0), false);
        HashSet<int> Expanded(Scene s) => (HashSet<int>)field.GetValue(((ChangelogScene)s).Window)!;

        // The newest card starts expanded; its header is the first thing in the body, below the page header.
        var header = new Vector2(size.X / 2, 200);
        Press(size, Make, _ => header, scene =>
        {
            if (Expanded(scene).Contains(0))
                throw new InvalidOperationException("A press on the newest card header did not collapse it.");
        });
    }

    private static void VerifyReportButtons()
    {
        var size = new Vector2(800, 600);

        bool PopupOpen() => !ImGuiP.GetTopMostPopupModal().IsNull;

        // Cancel: right edge 16 + 96 + 8 + (Write report width) from the popup's right edge; the footer is the last 60 px.
        Press(size, () => new ReportScene("ArmoireAutoFill", "text"), _ =>
        {
            var (pos, popup) = PopupRect();
            return pos + new Vector2(popup.X - PopupGutter - 8 - 96 - 48, popup.Y - 30);
        }, scene =>
        {
            var report = (ReportScene)scene;
            if (PopupOpen())
                throw new InvalidOperationException("Cancel did not close the report popup.");
            if (report.Telemetry.FileCount != 0)
                throw new InvalidOperationException("Cancel filed a report.");
        }, settleFrames: 6);

        Press(size, () => new ReportScene("ArmoireAutoFill", "text"), _ =>
        {
            var (pos, popup) = PopupRect();
            return pos + new Vector2(popup.X - PopupGutter - 18, 34);
        }, scene =>
        {
            if (PopupOpen())
                throw new InvalidOperationException("The report popup close box did not close it.");
        });

        Press(size, () => new ReportScene("ArmoireAutoFill", "typed words"), _ =>
        {
            var (pos, popup) = PopupRect();
            return pos + new Vector2(popup.X - PopupGutter - 48, popup.Y - 30);
        }, scene =>
        {
            var report = (ReportScene)scene;
            if (report.Telemetry.FileCount != 1 || report.Telemetry.LastText != "typed words")
                throw new InvalidOperationException($"Write report did not file the typed text (filed {report.Telemetry.FileCount}x, '{report.Telemetry.LastText}').");
            if (PopupOpen())
                throw new InvalidOperationException("A successful Write report did not close the popup.");
            var text = (string)typeof(ReportPopup).GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(report.Popup)!;
            var id = (string?)typeof(ReportPopup).GetField("_lastId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(report.Popup);
            if (text.Length != 0 || id != "7F3A9C21")
                throw new InvalidOperationException($"After a write the text must clear and the id be kept (text '{text}', id '{id}').");
        });

        var refused = () => new ReportScene("ArmoireAutoFill", "typed words");
        Press(size, () =>
        {
            var scene = (ReportScene)refused();
            scene.Telemetry.NextReportId = null;
            return scene;
        }, _ =>
        {
            var (pos, popup) = PopupRect();
            return pos + new Vector2(popup.X - PopupGutter - 48, popup.Y - 30);
        }, scene =>
        {
            var report = (ReportScene)scene;
            if (report.Telemetry.FileCount != 1)
                throw new InvalidOperationException("Write report did not try to file the report.");
            if (!PopupOpen())
                throw new InvalidOperationException("A refused write closed the popup; it must stay open.");
        });
    }

    // ---- glyph audit ----------------------------------------------------------------------------------------

    /// <summary>
    /// Every non-ASCII character the popups can draw must exist in the active font, otherwise ImGui draws the fallback
    /// glyph. Scans the string literals of the compiled popup and kit sources, and the parsed text of every plugin's real
    /// CHANGELOG.md (the changelog window draws that text). Printed as a report; --strict-glyphs turns a miss into a failure.
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

        var root = SourceRoot;
        var literal = new Regex("@?\"(?:[^\"\\\\\r\n]|\\\\.)*\"");
        var files = new[]
        {
            Path.Combine("src", "Shared", "LalaChangelog", "ChangelogWindow.cs"),
            Path.Combine("src", "Shared", "LalaTelemetry", "ReportPopup.cs"),
            Path.Combine("src", "Shared", "LalaUiKit", "PopupChrome.cs"),
        };
        foreach (var relative in files)
        {
            var text = new StringBuilder();
            foreach (var line in File.ReadLines(Path.Combine(root, relative)))
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

            Scan(relative.Replace('\\', '/'), text.ToString());
        }

        var changelogs = 0;
        var characters = 0;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "CHANGELOG.md", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var plugin = Path.GetFileName(Path.GetDirectoryName(file))!;
            var drawn = new StringBuilder();
            foreach (var entry in ChangelogParser.Parse(File.ReadAllText(file)))
            {
                drawn.Append("v").Append(entry.VersionText).Append(' ').Append(entry.Date).Append('\n');
                foreach (var section in entry.Sections)
                {
                    drawn.Append(section.Name).Append('\n');
                    foreach (var bullet in section.Bullets)
                        drawn.Append(bullet).Append('\n');
                }
            }

            changelogs++;
            characters += drawn.Length;
            Scan($"src/{plugin}/CHANGELOG.md", drawn.ToString());
        }

        // Self-test: a private-use game symbol is absent from the font, so the audit must be able to report it.
        if (look.MissingGlyphs("").Count == 0)
            throw new InvalidOperationException("The glyph audit cannot detect a missing glyph; its result would mean nothing.");

        Console.WriteLine($"Glyph check scanned {files.Length} source file(s) and {changelogs} real changelog(s) ({characters:N0} drawn characters).");
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
