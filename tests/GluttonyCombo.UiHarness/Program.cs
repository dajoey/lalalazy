using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using GluttonyCombo.Window;
using LalaUiKit;
using RenderProbe;

namespace GluttonyCombo.UiHarness;

internal static class Program
{
    private static readonly IntPtr FontTextureId = new(1);
    private static int _checks;

    public static int Main()
    {
        try
        {
            VerifyNavigation();
            var image = RenderControls();
            VerifyToggleInteraction();
            Console.WriteLine($"PASS: {_checks} checks; real controls rendered to {image}.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness failed: {ex}");
            return 1;
        }
    }

    private static void VerifyNavigation()
    {
        Check(GluttonyShellNavigation.Groups.SequenceEqual(["Combos", "Automation", "System"]),
            "Top-level groups preserve the two-level shell.");
        Check(GluttonyShellNavigation.FirstPage("Combos", false) == GluttonyPage.PvE &&
              GluttonyShellNavigation.FirstPage("Automation", false) == GluttonyPage.AutoRotation &&
              GluttonyShellNavigation.FirstPage("System", false) == GluttonyPage.Settings,
            "Group destinations are stable.");

        var root = GluttonyShellNavigation.Features(GluttonyPage.PvE, false, null, false);
        Check(root.Count == 2 && root[0].Page == GluttonyPage.PvE && root[1].Page == GluttonyPage.PvP,
            "Combo root exposes PvE and PvP without a third navigation level.");

        IReadOnlyList<GluttonyContent> contents =
        [
            new("normal", "Normal"),
            new("variant", "Variant"),
            new("bozja", "Bozja"),
            new("occult", "Occult Crescent"),
            new("deep-dungeon", "Deep Dungeon"),
        ];
        var open = GluttonyShellNavigation.Features(GluttonyPage.PvE, true, contents, false);
        Check(open.Count == 7 && open[0].ContentId == "root" && open[^1].Page == GluttonyPage.PvP,
            "An open job keeps Jobs and PvP reachable beside every content destination.");
        Check(open.Skip(1).Take(5).Select(feature => feature.ContentId).SequenceEqual(contents.Select(content => content.Id)),
            "Existing content order is preserved.");
        Check(open.Select(feature => feature.Id).Distinct().Count() == open.Count,
            "Feature IDs remain unique when content tabs move into the shell.");
        Check(GluttonyShellNavigation.Features(GluttonyPage.Settings, false, null, true).Any(feature => feature.Page == GluttonyPage.About) &&
              !GluttonyShellNavigation.Features(GluttonyPage.Settings, false, null, false).Any(feature => feature.Page == GluttonyPage.Debug),
            "Debug navigation remains build-configuration scoped.");

        Check(GluttonyShellNavigation.ShowSearch(GluttonyPage.Settings, false, true) &&
              !GluttonyShellNavigation.ShowSearch(GluttonyPage.Settings, false, false),
            "Settings search honors the saved visibility setting.");
        Check(!GluttonyShellNavigation.ShowSearch(GluttonyPage.PvE, false, true) &&
              GluttonyShellNavigation.ShowSearch(GluttonyPage.PvE, true, true) &&
              GluttonyShellNavigation.ShowSearch(GluttonyPage.PvP, true, true) &&
              !GluttonyShellNavigation.ShowSearch(GluttonyPage.AutoRotation, true, true),
            "Search appears only on settings and open preset surfaces.");
        Check(GluttonyShellNavigation.Activity(false, false) == GluttonyActivity.Ready &&
              GluttonyShellNavigation.Activity(true, false) == GluttonyActivity.Running &&
              GluttonyShellNavigation.Activity(true, true) == GluttonyActivity.Blocked,
            "AutoRotation state vocabulary maps exactly to Ready, Running, and Blocked.");
    }

    private static unsafe string RenderControls()
    {
        const int width = 960;
        const int height = 640;
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.DisplaySize = new Vector2(width, height);
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            io.MousePos = new Vector2(-10000, -10000);
            ImGui.StyleColorsDark();
            var atlas = BuildFontAtlas(io.Fonts, out var atlasWidth, out var atlasHeight);
            var frame = new byte[width * height * 4];
            var enabled = true;
            var descriptions = false;

            for (var frameIndex = 0; frameIndex < 2; frameIndex++)
            {
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(io.DisplaySize);
                var open = true;
                ImGui.Begin("GluttonyCombo controls", ref open,
                    ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings);
                Theme.PushFrame();
                GluttonyChrome.PushControlColors();
                var origin = ImGui.GetCursorScreenPos();
                var draw = ImGui.GetWindowDrawList();
                GluttonyChrome.BrandButton(draw, origin + new Vector2(16, 16), new Vector2(218, 42));
                var x = 250f;
                foreach (var group in GluttonyShellNavigation.Groups)
                {
                    GluttonyChrome.OutlineButton(draw, "render_" + group, group, origin + new Vector2(x, 20),
                        new Vector2(group == "Automation" ? 112 : 92, 34), group == "Combos");
                    x += group == "Automation" ? 120 : 100;
                }
                GluttonyChrome.StatusPill(draw, origin + new Vector2(width - 118, 20), new Vector2(102, 34), "Running", UiState.Running);

                IReadOnlyList<GluttonyContent> contents = [new("normal", "Normal"), new("variant", "Variant")];
                var features = GluttonyShellNavigation.Features(GluttonyPage.PvE, true, contents, false);
                x = 16;
                foreach (var feature in features)
                {
                    var featureWidth = MathF.Max(116, ImGui.CalcTextSize(feature.Label).X + 52);
                    GluttonyChrome.FeatureButton(draw, feature.Id, feature.Label, origin + new Vector2(x, 82),
                        new Vector2(featureWidth, 34), feature.ContentId == "normal");
                    x += featureWidth + 12;
                }

                ImGui.SetCursorScreenPos(origin + new Vector2(16, 142));
                GluttonyChrome.Card("render_settings", () =>
                {
                    Widgets.Title("AutoRotation");
                    Widgets.SecondaryWrapped("Existing settings keep their values while the shell supplies drawn controls.");
                    GluttonyChrome.Toggle("Enable AutoRotation", ref enabled);
                    GluttonyChrome.Toggle("Search descriptions", ref descriptions);
                    Widgets.Caption("2 settings");
                });
                ImGui.SetCursorScreenPos(origin + new Vector2(16, 344));
                GluttonyChrome.Card("render_preset", () =>
                {
                    Widgets.Title("Advanced Combo");
                    Widgets.SecondaryWrapped("Preset content remains inside the existing renderer.");
                    GluttonyChrome.Toggle("Enable this combo", ref enabled);
                    Widgets.Caption("1 combo · 2 options");
                });
                GluttonyChrome.PopControlColors();
                Theme.PopFrame();
                ImGui.End();
                ImGui.Render();
            }

            Fill(frame, 15, 17, 20);
            var drawData = ImGui.GetDrawData();
            Check(drawData.Valid && drawData.TotalVtxCount > 0, "Real drawn controls produced render geometry.");
            var stats = CpuRasterizer.Render(drawData, frame, width, height, atlas, atlasWidth, atlasHeight, FontTextureId);
            var outputDirectory = Path.Combine(Path.GetTempPath(), "GluttonyCombo.UiHarness");
            Directory.CreateDirectory(outputDirectory);
            var path = Path.Combine(outputDirectory, "controls.png");
            PngWriter.Write(path, frame, width, height);
            Check(stats.Triangles > 100 && new FileInfo(path).Length > 5000,
                "Rendered control image is non-empty and contains substantial geometry.");
            Console.WriteLine($"Rendered controls: {stats.Triangles} triangles, {new FileInfo(path).Length:N0} bytes.");
            return path;
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    private static unsafe void VerifyToggleInteraction()
    {
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.DisplaySize = new Vector2(480, 120);
            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = 1f / 60f;
            _ = BuildFontAtlas(io.Fonts, out _, out _);
            var value = false;
            var toggleCenter = Vector2.Zero;
            for (var step = 0; step < 3; step++)
            {
                if (step == 0)
                    io.AddMousePosEvent(-10000, -10000);
                else
                    io.AddMousePosEvent(toggleCenter.X, toggleCenter.Y);
                io.AddMouseButtonEvent(0, step == 1);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(io.DisplaySize);
                var open = true;
                ImGui.Begin("Drawn toggle interaction", ref open,
                    ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings);
                ImGui.SetCursorPos(new Vector2(20, 20));
                if (step == 0)
                {
                    var origin = ImGui.GetCursorScreenPos();
                    toggleCenter = origin + new Vector2(ImGui.GetContentRegionAvail().X - 22, 12);
                }
                GluttonyChrome.Toggle("Interaction setting", ref value);
                ImGui.End();
                ImGui.Render();
            }
            Check(value, "A real press and release changes the drawn toggle value.");
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    private static unsafe byte[] BuildFontAtlas(ImFontAtlasPtr fonts, out int width, out int height)
    {
        fonts.AddFontDefault();
        byte* pixels = null;
        var atlasWidth = 0;
        var atlasHeight = 0;
        var bytesPerPixel = 0;
        fonts.GetTexDataAsRGBA32(0, &pixels, &atlasWidth, &atlasHeight, &bytesPerPixel);
        width = atlasWidth;
        height = atlasHeight;
        if (bytesPerPixel != 4 || width <= 0 || height <= 0)
            throw new InvalidOperationException("Invalid ImGui RGBA font atlas.");
        fonts.SetTexID(0, new ImTextureID(FontTextureId));
        var copy = new byte[width * height * 4];
        Marshal.Copy((IntPtr)pixels, copy, 0, copy.Length);
        return copy;
    }

    private static void Fill(byte[] frame, byte red, byte green, byte blue)
    {
        for (var i = 0; i < frame.Length; i += 4)
        {
            frame[i] = red;
            frame[i + 1] = green;
            frame[i + 2] = blue;
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
