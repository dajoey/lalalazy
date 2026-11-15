using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ECommons.DalamudServices;
using ImGuiNET;
using LazyCrucible;
using LazyCrucible.Policy;
using RenderProbe;

namespace LazyCrucibleHarness;

internal static class Program
{
    private static readonly IntPtr FontTextureId = new(1);
    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    private static readonly string OutputDirectory = Path.Combine(ProjectRoot, "output");

    private enum ScenarioKind
    {
        Main,
        Guide,
        Overlay,
    }

    private sealed record Scenario(string Name, ScenarioKind Kind, int Width, int Height)
    {
        public string? YieldReason { get; init; }
        public bool Advisor { get; init; }
        public (int Board, int Battle) Focus { get; init; } = (0, -1);
        public bool Incoming { get; init; }
    }

    private static readonly Scenario[] Scenarios =
    [
        new("main-empty-ready", ScenarioKind.Main, 1100, 1600),
        new("main-autoduty-standby", ScenarioKind.Main, 1100, 1600)
        {
            YieldReason = "autoduty_running",
        },
        new("main-gluttony-conflict", ScenarioKind.Main, 1100, 1600)
        {
            YieldReason = "conflict_gluttony",
        },
        new("main-advisor-expanded", ScenarioKind.Main, 1100, 1600)
        {
            Advisor = true,
        },
        new("guide-strix-piece", ScenarioKind.Guide, 900, 1300)
        {
            Focus = (4, 1),
        },
        new("guide-incoming-warning", ScenarioKind.Guide, 900, 1300)
        {
            Focus = (5, 6),
            Incoming = true,
        },
        new("guide-missing-entry", ScenarioKind.Guide, 900, 700)
        {
            Focus = (4, 99),
        },
        new("suggestion-overlay", ScenarioKind.Overlay, 800, 600),
    ];

    public static int Main()
    {
        try
        {
            Directory.CreateDirectory(OutputDirectory);
            using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("CrucibleGuide.json")
                               ?? throw new InvalidOperationException("The real CrucibleGuide.json was not embedded.");
            using var reader = new StreamReader(stream);
            var guide = CrucibleGuide.Parse(reader.ReadToEnd());
            guide.InstallNeedModel();
            if (guide.Fight(4, 1)?.Title != "Strix Piece")
                throw new InvalidOperationException("The real Strix Piece guide entry did not load.");
            var validationProblems = guide.Validate();
            Console.WriteLine($"Real guide: {guide.File.Fights.Count} fights; validation problems: {validationProblems.Count}.");
            if (validationProblems.Count != 0)
                throw new InvalidOperationException($"The real guide has {validationProblems.Count} validation problems.");

            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var scenario in Scenarios)
            {
                var result = RenderScenario(guide, scenario);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Path))).ToLowerInvariant();
                if (!hashes.TryAdd(hash, scenario.Name))
                    throw new InvalidOperationException($"{scenario.Name} is byte-identical to {hashes[hash]}.");
                Console.WriteLine($"{scenario.Name}: {result.Path} | {result.Triangles} triangles | {result.Bytes:N0} bytes | sha256 {hash[..16]}…");
            }
            Console.WriteLine($"Rendered {Scenarios.Length} distinct real-source UI states. No window or game process was created.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
        finally
        {
            ScreenReader.Reset();
        }
    }

    private static RenderResult RenderScenario(CrucibleGuide guide, Scenario scenario)
    {
        ResetState();
        Plugin.Config = new Configuration();
        Plugin.Guide = guide;
        PetSelect.YieldReason = scenario.YieldReason;
        ExternalDrivers.DrivenSurfaces = CrucibleSurface.All & ~CrucibleSurface.Horns;
        RunTracker.Focus = scenario.Focus;
        RunTracker.Board = scenario.Focus.Board;

        if (scenario.Advisor)
        {
            Svc.ClientState.TerritoryType = 1342; // Real First Master's Board territory -> board 4.
            CrucibleGame.CurrentBattleValue = 1;
        }

        var plugin = new Plugin();
        var main = new MainWindow(plugin);
        var guideWindow = new GuideWindow();
        if (scenario.Incoming)
            CrucibleGame.WarnedCastValue = FightWarnings.ForCast(49272, 6.4f)
                ?? throw new InvalidOperationException("The real Atomic Ray warning data is missing.");
        if (scenario.Kind == ScenarioKind.Guide)
            guideWindow.FollowFocus();

        if (scenario.Kind == ScenarioKind.Overlay)
        {
            SelectionScreens.Suggestions.Add(("XBMPetParty", "Pick megalocrab: Quelling Wave answers Ultimate Focus."));
            ScreenReader.SetAddon("XBMPetParty", 170, 410, 430, 150);
        }

        var size = new Vector2(scenario.Width, scenario.Height);
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = size;
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            io.MousePos = new Vector2(-10000, -10000);
            ImGui.StyleColorsDark();

            var atlas = BuildFontAtlas(io.Fonts, out var atlasWidth, out var atlasHeight);
            var frame = new byte[scenario.Width * scenario.Height * 4];

            // Two frames stabilize window storage and, for the advisor, allow a deterministic scroll to the real section.
            for (var frameIndex = 0; frameIndex < 2; frameIndex++)
            {
                ImGui.NewFrame();
                switch (scenario.Kind)
                {
                    case ScenarioKind.Main:
                        DrawMainWindow(main, scenario.Advisor && frameIndex == 0, scenario.Advisor, size);
                        break;
                    case ScenarioKind.Guide:
                        DrawGuideWindow(guideWindow, size);
                        break;
                    case ScenarioKind.Overlay:
                        SuggestionOverlay.Draw();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                ImGui.Render();
            }

            Fill(frame, 20, 20, 24);
            var drawData = ImGui.GetDrawData();
            if (!drawData.Valid || drawData.TotalVtxCount == 0)
                throw new InvalidOperationException($"{scenario.Name}: the real Draw method produced no draw data.");
            var stats = CpuRasterizer.Render(drawData, frame, scenario.Width, scenario.Height,
                atlas, atlasWidth, atlasHeight, FontTextureId);
            var path = Path.Combine(OutputDirectory, scenario.Name + ".png");
            PngWriter.Write(path, frame, scenario.Width, scenario.Height);
            return new RenderResult(path, new FileInfo(path).Length, stats.Triangles);
        }
        finally
        {
            ImGui.DestroyContext(context);
            ScreenReader.Reset();
        }
    }

    private static void DrawMainWindow(MainWindow window, bool scrollToAdvisor, bool advisor, Vector2 size)
    {
        BeginRealWindow(window.Name, size);
        if (advisor)
        {
            // Set the same window storage CollapsingHeader/TreeNode use; no replacement controls are drawn.
            ImGui.GetStateStorage().SetInt(ImGui.GetID("Beast picks by battle"), 1);
            ImGui.GetStateStorage().SetInt(ImGui.GetID("> Strix piece (enemy)##lc4_1"), 1);
        }
        window.Draw();
        if (scrollToAdvisor)
            ImGui.SetScrollY(ImGui.GetScrollMaxY());
        ImGui.End();
    }

    private static void DrawGuideWindow(GuideWindow window, Vector2 size)
    {
        BeginRealWindow(window.Name, size);
        window.Draw();
        ImGui.End();
    }

    private static void BeginRealWindow(string name, Vector2 size)
    {
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(size);
        var open = true;
        ImGui.Begin(name, ref open,
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
    }

    private static byte[] BuildFontAtlas(ImFontAtlasPtr fonts, out int width, out int height)
    {
        // Dalamud supplies these glyphs in its font atlas. Include the real UI's em dash, arrows and check marks.
        var ranges = new ushort[]
        {
            0x0020, 0x00ff,
            0x2000, 0x206f,
            0x2190, 0x21ff,
            0x2500, 0x25ff,
            0x2700, 0x27bf,
            0,
        };
        var pinned = GCHandle.Alloc(ranges, GCHandleType.Pinned);
        try
        {
            var fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            var fontPath = Path.Combine(fontsDirectory, "seguisym.ttf");
            if (!File.Exists(fontPath))
                fontPath = Path.Combine(fontsDirectory, "segoeui.ttf");
            if (File.Exists(fontPath))
                fonts.AddFontFromFileTTF(fontPath, 18, default, pinned.AddrOfPinnedObject());
            else
                fonts.AddFontDefault();

            fonts.GetTexDataAsRGBA32(out IntPtr pixels, out width, out height, out int bytesPerPixel);
            if (bytesPerPixel != 4 || width <= 0 || height <= 0)
                throw new InvalidOperationException("Invalid ImGui RGBA font atlas.");
            fonts.SetTexID(FontTextureId);
            var copy = new byte[width * height * 4];
            Marshal.Copy(pixels, copy, 0, copy.Length);
            return copy;
        }
        finally
        {
            pinned.Free();
        }
    }

    private static void ResetState()
    {
        PetSelect.YieldReason = null;
        PetSelect.LastSummary = "";
        PetSelect.LastSummaryAt = default;
        SelectionScreens.Status = "";
        SelectionScreens.Recent.Clear();
        SelectionScreens.Suggestions.Clear();
        ExternalDrivers.StallReason = null;
        ExternalDrivers.DrivenSurfaces = CrucibleSurface.All;
        Svc.ClientState.TerritoryType = 0;
        RunTracker.Board = 0;
        RunTracker.Focus = (0, -1);
        RunTracker.Roster = [];
        RunTracker.RosterHp = [];
        RunTracker.PlayerHpPercent = 100;
        CrucibleGame.RosterLoaded = false;
        CrucibleGame.CurrentBattleValue = -1;
        CrucibleGame.WarnedCastValue = null;
        CrucibleGame.CapturePredicate = null;
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

    private sealed record RenderResult(string Path, long Bytes, int Triangles);
}
