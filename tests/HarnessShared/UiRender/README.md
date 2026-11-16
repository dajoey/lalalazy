# Shared UI render core

Plain C# source files that render real ImGui window code to PNG on the CPU, with an optional "game look" that makes
the render match what a stock Dalamud install draws. Each harness project links these files; nothing here is a
library or a package.

| File | What it is |
| --- | --- |
| `Rasterizer.cs` | `CpuRasterizer` (ImGui draw data to an RGBA frame), `AtlasTexture`, `PngWriter` |
| `RenderOptions.cs` | mode selection: `--game` / `LALA_UI_FIDELITY=game`, `LALA_UI_ASSET_DIR`, `LALA_UI_SCALE` |
| `UiLook.cs` | `UiLook.Apply(options)` sets fonts and style for a context; `RenderLook` (fonts, atlas textures, `MissingGlyphs`) |
| `GameLook.cs` | the Dalamud default font, icon font, global scale and style |

## Adding a harness for another plugin

1. Create `tests/<Plugin>.UiHarness/` with a console project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <LalaSourceRoot Condition="'$(LalaSourceRoot)' == ''">$(MSBuildThisFileDirectory)..\..</LalaSourceRoot>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Dalamud" HintPath="$(DalamudLibPath)\Dalamud.dll" />
    <Reference Include="Dalamud.Bindings.ImGui" HintPath="$(DalamudLibPath)\Dalamud.Bindings.ImGui.dll" />
    <None Include="$(DalamudLibPath)\cimgui.dll" Link="cimgui.dll" CopyToOutputDirectory="PreserveNewest" />
    <Compile Include="..\HarnessShared\UiRender\*.cs" Link="UiRender\%(Filename)%(Extension)" />
    <!-- the real window sources under test, linked from $(LalaSourceRoot)\src\... -->
  </ItemGroup>
</Project>
```

   Compile the real window and kit sources, and stub only the game and runtime boundaries (services, game state,
   unmanaged addons) in a `Stubs.cs`. Keep the stubs next to the harness; they are plugin specific.

2. Program skeleton (one context per image):

```csharp
var options = RenderOptions.Resolve(args);                 // built-in unless --game / LALA_UI_FIDELITY=game
var outputDirectory = Path.Combine(harnessRoot, "output", options.OutputSubdirectory);
Directory.CreateDirectory(outputDirectory);

var context = ImGui.CreateContext();
try
{
    var io = ImGui.GetIO();
    io.DisplaySize = new Vector2(width, height);
    io.DisplayFramebufferScale = Vector2.One;
    io.DeltaTime = 1f / 60f;
    io.MousePos = new Vector2(-10000, -10000);
    var look = UiLook.Apply(options);                      // style + fonts; before the first NewFrame
    for (var frame = 0; frame < 2; frame++)                // two frames settle window storage
    {
        ImGui.NewFrame();
        // ImGui.SetNextWindowPos/Size, ImGui.Begin(window flags), window.Draw(), ImGui.End()
        ImGui.Render();
    }
    var pixels = new byte[width * height * 4];             // fill a background colour first
    CpuRasterizer.Render(ImGui.GetDrawData(), pixels, width, height, look.Textures);
    PngWriter.Write(Path.Combine(outputDirectory, name + ".png"), pixels, width, height);
}
finally { ImGui.DestroyContext(context); }
```

   `look.IconFont` is the FontAwesome font Dalamud exposes as `UiBuilder.IconFont`; a plugin stub for icon pushes
   should `ImGui.PushFont(look.IconFont)`. `look.MissingGlyphs(text)` lists the characters of a string that have no
   glyph in the active default font; run it over every string the window can draw and fail on a non-empty result.

3. Run it through any wrapper that sets the three environment variables, on a machine that has the native
   `cimgui.dll` (the harness cannot run without it):

```
DalamudLibPath      folder holding Dalamud.dll, Dalamud.Bindings.ImGui.dll and cimgui.dll
LALA_UI_ASSET_DIR   Dalamud's UIRes asset folder (NotoSansCJK-Medium.ttc, FontAwesome710FreeSolid.otf, ...)
LALA_UI_SCALE       global UI scale, for example 1.02 (default 1.0)
```

   `dotnet run --project <harness> -c Release -- --game` renders game mode; without `--game` the harness stays
   hermetic and byte-stable. Write game images to `output/game/` (`options.OutputSubdirectory`) so both modes can
   coexist. Game mode fails with a clear message when the asset folder or a required font file is missing.

## What game mode reproduces from Dalamud

Source paths are relative to the Dalamud repository (`Dalamud/...`), version 15.0.3.2.

| Aspect | Value and origin |
| --- | --- |
| Default font file | `DefaultFontSpec` of a stock config is `SingleFontSpec { FontId = DalamudAssetFontAndFamilyId { Asset = 2000 }, SizePx = 17, LineHeight = 1 }`. `DalamudAsset.NotoSansCjkMedium = 2000` (`DalamudAsset.cs`) is `UIRes/NotoSansCJK-Medium.ttc`, font number 0 (`SingleFontSpec.FontNo`) |
| Glyph ranges | none in the spec, so `[1, 0xFFFE]` (`FontAtlasFactory.BuildToolkit.AddFontFromImGuiHeapAllocatedMemory`): the whole BMP the font covers |
| Font size and scale | atlas built at `SizePx * GlobalUiScale` (`BuildToolkit.PreBuild` also scales glyph min/max advance and offset); after `ImFontAtlas.Build` the glyph metrics are unscaled by `1 / scale` (`BuildToolkit.PostBuild`, `ImGuiHelpers.AdjustGlyphMetrics`, called directly); `io.FontGlobalScale = GlobalUiScale` (`InterfaceManager`, `ImGuiHelpers.GlobalScale` reads it back, so no reflection is needed) |
| Spec post-build | ascent and size snapped to whole physical pixels, descent recomputed (`SingleFontSpec.AddToBuildToolkit` post-build action) |
| Rasterizer settings | oversample 1/1, pixel snap on, gamma 1.7, multiply 1 (`SafeFontConfig` constructor, used directly) |
| Kerning | GPOS/kern pairs registered per font (`AddFontFromImGuiHeapAllocatedMemory`); the helper is internal, so it is reached by reflection and skipped if absent |
| Fallback and ellipsis | first present of `U+3013 U+FFFD ? -` and `U+2026 U+0085` (`FontAtlasFactory.FallbackCodepoints`, `EllipsisCodepoints`); lookup table rebuilt with the custom-ImGui hot-data fix (`BuildToolkit.BuildLookupTable`) |
| Icon font | `FontAwesome710FreeSolid.otf` (asset 2003) at the default size, glyph range min..max of `FontAwesomeIcon`, min and max advance pinned to the default size (`InterfaceManager.SetupHooks`, `IconFontHandle`) |
| Style | `StyleModelV1.DalamudStandard.Apply()` from the supplied `Dalamud.dll`: every metric and colour `Apply` sets (`Interface/Style/StyleModelV1.cs`) |
| Style metrics vs scale | Dalamud does not scale style metrics by the global scale; only font size follows it. The harness does the same |
| Config flags | docking and viewports off |

Not reproduced: the game-symbol glyphs Dalamud merges into the default font (private use area; the harness has no
game font files, so `MissingGlyphs` reports them), the monospace and fixed-width icon fonts (unused by the harness),
a player's custom font or style, the Dalamud window chrome (title bar buttons, background blur, `Window` class
behaviour), the GPU renderer (blending is the CPU approximation in `Rasterizer.cs`), and any difference between the
supplied runtime build and the one the player runs.
