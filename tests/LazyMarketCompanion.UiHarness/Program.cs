using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using ECommons;
using Lala.UiRender;
using Lalalazy.Telemetry;
using LazyMarketCompanion;
using LazyMarketCompanion.Inventory;
using LazyMarketCompanion.Windows;

namespace LazyMarketCompanion.UiHarness;

internal static class Program
{
  private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
  private static string OutputDirectory = Path.Combine(ProjectRoot, "output");
  private static RenderOptions Options = RenderOptions.BuiltIn;
  private static bool _lookReported;
  private static bool _missingGlyphs;
  private static bool _strictGlyphs;

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

      // Draw methods swallow exceptions behind telemetry guards; route those lines here so a swallowed failure fails the run.
      LalaTelemetry.Hub = new TelemetryHub(
        new TelemetryIdentity { Plugin = "LazyMarketCompanion", DisplayName = "Lazy Market Companion", Version = "harness" },
        HarnessSink);

      OverlayMirror.VerifyAgainstSource(SourceRoot());
      Console.WriteLine("Overlay mirror: matches the statements it copies from MarketAutomation.cs.");

      var scenarios = Scenarios.Build(Options.IsGame);
      var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
      foreach (var scenario in scenarios)
      {
        var result = Render(scenario);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Path))).ToLowerInvariant();
        if (!hashes.TryAdd(hash, scenario.Name))
        {
          if (!Options.IsGame)
            throw new InvalidOperationException($"{scenario.Name} is byte-identical to {hashes[hash]}.");
          Console.WriteLine($"WARNING: {scenario.Name} is byte-identical to {hashes[hash]}; the state it sets up is not visible at this size.");
        }
        Console.WriteLine($"{scenario.Name}: {Path.GetRelativePath(ProjectRoot, result.Path).Replace('\\', '/')} | {scenario.Width}x{scenario.Height} | {result.Triangles} triangles | sha256 {hash[..16]}");
      }
      Console.WriteLine($"Rendered {scenarios.Count} real-source UI states ({hashes.Count} distinct). No window or game process was created.");

      if (_missingGlyphs && _strictGlyphs)
        throw new InvalidOperationException("--strict-glyphs: text the windows draw contains characters the game font lacks.");
      if (HarnessSink.Errors.Count > 0)
        throw new InvalidOperationException($"{HarnessSink.Errors.Count} draw exception(s) were swallowed by telemetry guards:\n" + string.Join("\n", HarnessSink.Errors));
      VerifyToggleInteraction();
      Console.WriteLine("Interactive drawn toggle: press and release changed the setting through MarketChrome.Toggle.");
      VerifyCloseButton();
      Console.WriteLine("Custom close control: a real press and release on the shell bar's close button closed the config window.");
      VerifyOverlayClearsCloseButton();
      Console.WriteLine("Retainer overlay (issue #12): on every frame of running/idle switches the overlay stayed left of the window's close button and never took the hover over it.");
      return 0;
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"Harness failed: {ex}");
      return 1;
    }
    finally
    {
      HarnessAddons.Clear();
    }
  }

  private static readonly CollectingSink HarnessSink = new();

  private sealed class CollectingSink : ITelemetrySink
  {
    public List<string> Errors { get; } = [];
    public void Write(TelemetryLevel level, string text) { if (level >= TelemetryLevel.Warning) Errors.Add(text); }
    public void Notice(string text) { }
  }

  private static string SourceRoot()
  {
    var root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "LalaSourceRoot")?.Value;
    return root is { Length: > 0 } ? Path.GetFullPath(root) : Path.GetFullPath(Path.Combine(ProjectRoot, "..", ".."));
  }

  // =================================================================================================
  // one scenario -> one PNG
  // =================================================================================================

  private static RenderResult Render(Scenario scenario)
  {
    HarnessAddons.Clear();
    var state = scenario.Prepare();
    var options = scenario.Scale is { } scale ? Options with { Scale = scale } : Options;
    var size = new Vector2(scenario.Width, scenario.Height);
    var context = ImGui.CreateContext();
    try
    {
      var io = ImGui.GetIO();
      io.DisplaySize = size;
      io.DisplayFramebufferScale = Vector2.One;
      io.DeltaTime = 1f / 60f;
      io.MousePos = scenario.MousePos ?? new Vector2(-10000, -10000);
      PublishMainViewport();
      var look = UiLook.Apply(options);
      if (!_lookReported)
      {
        _lookReported = true;
        Console.WriteLine($"Look: {look.Description}");
        if (Options.IsGame)
          GlyphAudit.Run(look, SourceRoot(), ref _missingGlyphs);
      }

      var frame = new byte[scenario.Width * scenario.Height * 4];
      for (var frameIndex = 0; frameIndex < scenario.Frames; frameIndex++)
      {
        ImGui.NewFrame();
        state.DrawFrame(frameIndex, size);
        ImGui.Render();
      }

      Fill(frame, scenario.Background);
      var drawData = ImGui.GetDrawData();
      if (!drawData.Valid || drawData.TotalVtxCount == 0)
        throw new InvalidOperationException($"{scenario.Name}: the real Draw method produced no draw data.");
      var stats = CpuRasterizer.Render(drawData, frame, scenario.Width, scenario.Height, look.Textures);
      var path = Path.Combine(OutputDirectory, scenario.Name + ".png");
      PngWriter.Write(path, frame, scenario.Width, scenario.Height);
      return new RenderResult(path, stats.Triangles);
    }
    finally
    {
      ImGui.DestroyContext(context);
      HarnessAddons.Clear();
    }
  }

  /// <summary>Dalamud publishes the main viewport to ImGuiHelpers at startup; do the same for this context.</summary>
  private static void PublishMainViewport()
  {
    var property = typeof(Dalamud.Interface.Utility.ImGuiHelpers).GetProperty("MainViewport", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
      ?? throw new InvalidOperationException("ImGuiHelpers.MainViewport is missing; update the harness.");
    property.SetValue(null, ImGui.GetMainViewport());
  }

  private static void Fill(byte[] target, (int R, int G, int B) colour)
  {
    for (var i = 0; i < target.Length; i += 4)
    {
      target[i] = (byte)colour.R;
      target[i + 1] = (byte)colour.G;
      target[i + 2] = (byte)colour.B;
      target[i + 3] = 255;
    }
  }

  // =================================================================================================
  // interaction checks on the real chrome (a press and a release must change what the control writes)
  // =================================================================================================

  private static void VerifyToggleInteraction()
  {
    var context = ImGui.CreateContext();
    try
    {
      var io = ImGui.GetIO();
      io.DisplaySize = new Vector2(480, 120);
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
        if (step == 0)
          clickPos = ImGui.GetCursorScreenPos() + new Vector2(ImGui.GetContentRegionAvail().X - 22, 12);
        MarketChrome.Toggle("Interaction setting", ref value);
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

  private static void VerifyCloseButton()
  {
    // The shell bar's close button sits at the right edge of the bar: press and release over it.
    var context = ImGui.CreateContext();
    try
    {
      Plugin.Configuration = Fixtures.Typical();
      var window = new ConfigWindow { IsOpen = true };
      var io = ImGui.GetIO();
      var size = new Vector2(1100, 760);
      io.DisplaySize = size;
      io.DisplayFramebufferScale = Vector2.One;
      io.DeltaTime = 1f / 60f;
      _ = UiLook.Apply(Options);
      for (var step = 0; step < 4; step++)
      {
        // The close button is 36 wide, 16 down from the bar's top and its right edge is one gutter (24 px at this width) from the window edge.
        io.MousePos = step == 0 ? new Vector2(-10000, -10000) : new Vector2(size.X - 24 - 18, 16 + 18);
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
        throw new InvalidOperationException("The config window's custom close button did not close the window.");
    }
    finally
    {
      ImGui.DestroyContext(context);
    }
  }

  /// <summary>
  /// Issue #12: the retainer overlay must never cover the game window's close button. Lays the sale list out at the
  /// game's geometry (ui/uld/RetainerSellList.uld: 598-wide window, anchor node 2 at (408, 6) 158 x 36, WindowA close
  /// button 33 px in from the right edge, 6 down, 28 square), places the overlay through the real OverlayPlacement,
  /// rests the mouse on the close button and switches Running/idle across frames (the auto-resize lags a frame). On
  /// every frame the overlay window must end left of the close button and must not be the hovered window.
  /// </summary>
  private static void VerifyOverlayClearsCloseButton()
  {
    var context = ImGui.CreateContext();
    try
    {
      Plugin.Configuration = Fixtures.Typical();
      Plugin.Configuration.AutoMarketEnabled = true;
      var io = ImGui.GetIO();
      io.DisplaySize = new Vector2(1100, 400);
      io.DisplayFramebufferScale = Vector2.One;
      io.DeltaTime = 1f / 60f;
      _ = UiLook.Apply(Options);
      foreach (var scale in new[] { 1f, 1.5f })
      {
        var window = new Vector2(120, 60);
        var anchor = window + new Vector2(408, 6) * scale;
        var anchorSize = new Vector2(158, 36) * scale;
        var close = new LazyMarketCompanion.AutoMarket.OverlayPlacement.Box(window + new Vector2(598 - 33, 6) * scale, new Vector2(28, 28) * scale);
        var right = LazyMarketCompanion.AutoMarket.OverlayPlacement.RightEdge(anchor, anchorSize, [close], scale);
        var closeCenter = close.Pos + (close.Size / 2f);
        (string Label, string Tooltip)[] idle = [("Auto Pinch", "t"), ("Auto Market", "t"), ("Sweep to Bags", "t")];

        // Control: an overlay that does reach over the close button IS seen taking the hover, so the check below can fail.
        var controlCaptured = false;
        for (var frame = 0; frame < 3; frame++)
        {
          io.MousePos = closeCenter;
          ImGui.NewFrame();
          controlCaptured |= frame > 0 && io.WantCaptureMouse;
          OverlayMirror.Draw($"###LMCOverlayControl{scale}", new Vector2(close.Right, anchor.Y), [], busy: true, arSession: false, anchorSize);
          ImGui.Render();
        }
        if (!controlCaptured)
          throw new InvalidOperationException($"Overlay x{scale} control: an overlay over the close button was not seen taking the hover; the check cannot fail.");
        bool[] busy = [true, true, false, false, true, false, true, true, false, true, false, false];
        for (var frame = 0; frame < busy.Length; frame++)
        {
          io.MousePos = closeCenter;
          ImGui.NewFrame();
          // NewFrame picks the hovered window from last frame's boxes: from frame 1 on, the close button must not be under the overlay.
          if (frame > 0 && io.WantCaptureMouse)
            throw new InvalidOperationException($"Overlay x{scale} frame {frame}: the mouse on the close button is captured by an ImGui window.");
          var box = OverlayMirror.Draw($"###LMCOverlayPlacement{scale}", new Vector2(right, anchor.Y), busy[frame] ? [] : idle, busy[frame], arSession: frame % 3 == 0, anchorSize);
          ImGui.Render();
          var boxRight = box.Pos.X + box.Size.X;
          if (boxRight > close.Left)
            throw new InvalidOperationException($"Overlay x{scale} frame {frame} ({(busy[frame] ? "running" : "idle")}): its right edge {boxRight:0.0} reaches the close button at {close.Left:0.0}.");
          if (box.Size.X > 0 && boxRight > right + 0.5f)
            throw new InvalidOperationException($"Overlay x{scale} frame {frame}: its right edge {boxRight:0.0} is past the placed edge {right:0.0}.");
        }
      }
    }
    finally
    {
      ImGui.DestroyContext(context);
    }
  }

  private sealed record RenderResult(string Path, int Triangles);
}

/// <summary>Reports every non-ASCII character the windows can draw that the game font has no glyph for.</summary>
internal static class GlyphAudit
{
  public static void Run(RenderLook look, string root, ref bool missing)
  {
    var found = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
    void Scan(string source, string text)
    {
      foreach (var entry in look.MissingGlyphs(text))
      {
        if (!found.TryGetValue(entry, out var sources))
          found[entry] = sources = [];
        sources.Add(source);
      }
    }

    var literal = new Regex("@?\"(?:[^\"\\\\\r\n]|\\\\.)*\"");
    var files = new List<(string Relative, string? Start, string? End)>();
    var src = Path.Combine("src", "LazyMarketCompanion");
    foreach (var file in new[]
             {
               Path.Combine(src, "Windows", "MarketChrome.cs"), Path.Combine(src, "Windows", "ConfigWindow.cs"),
               Path.Combine(src, "Windows", "AutoMarketPanelWindow.cs"), Path.Combine(src, "Inventory", "InventoryTab.cs"),
               Path.Combine(src, "Inventory", "OwnerTooltip.cs"),
             })
      files.Add((file, null, null));
    foreach (var core in Directory.EnumerateFiles(Path.Combine(root, src, "Inventory", "Core"), "*.cs"))
      files.Add((Path.GetRelativePath(root, core), null, null));
    files.Add((Path.Combine(src, "AutoMarket", "AutoMarketPanelModel.cs"), null, null));
    // The retainer overlay's drawing code lives in the middle of the automation file: scan only that region.
    files.Add((Path.Combine(src, "MarketAutomation.cs"), "// Draw: overlay buttons + watchdogs", "public void CancelEverything"));
    foreach (var kit in Directory.EnumerateFiles(Path.Combine(root, "src", "Shared", "LalaUiKit"), "*.cs"))
      files.Add((Path.GetRelativePath(root, kit), null, null));
    files.Add((Path.Combine("src", "Shared", "LalaTelemetry", "ReportPopup.cs"), null, null));

    foreach (var (relative, start, end) in files)
    {
      var path = Path.Combine(root, relative);
      if (!File.Exists(path))
        continue;
      var all = File.ReadAllText(path);
      if (start is not null)
      {
        var from = all.IndexOf(start, StringComparison.Ordinal);
        var to = all.IndexOf(end!, from < 0 ? 0 : from, StringComparison.Ordinal);
        if (from < 0 || to < 0)
          throw new InvalidOperationException($"Glyph audit: region markers not found in {relative}.");
        all = all[from..to];
      }
      var text = new StringBuilder();
      foreach (var line in all.Split('\n'))
      {
        if (line.TrimStart().StartsWith("//", StringComparison.Ordinal) || line.TrimStart().StartsWith("///", StringComparison.Ordinal))
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

    // Names the scenarios print (the stand-in catalogue and retainer names).
    Scan("harness fixtures", string.Join('\n', Fixtures.Items.Select(i => i.Name).Concat(Fixtures.Categories.Values).Concat(Fixtures.RetainerNames)));

    missing = found.Count > 0;
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
