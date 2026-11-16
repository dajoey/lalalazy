using System.Numerics;
using LazyMarketCompanion.AutoMarket;

// Offline cases 181-182 (issue #12): the retainer-window overlay never covers the window's close
// button or the game's own content in its header slot (the retainer list's venture count).
//
// Issue #12 reported the overlay buttons covering the venture currency and the close button on the
// retainer windows. 0.2.8.17 only added switches to hide each button; the in-run control could not
// be hidden, and the 0.2.8.24 polish made it wider, so the close button was covered again. These
// cases pin the reported symptom itself, so any later layout change that brings it back fails here.
//
// Geometry comes from the game's own layout files (ui/uld/RetainerSellList.uld, RetainerList.uld):
// sale list window 598 wide, anchor node 2 at (408, 6) 158 x 36 holding a 120 x 24 text (node 3)
// and a 36 x 36 node at x 122 (node 4); retainer list window 930 wide, anchor node 3 at (730, 9)
// 120 x 24 (the venture count) beside the 36 x 36 venture icon (node 4) at (852, 5). The window
// component (WindowA, 144 wide) holds the close button, component node 7, at x 111, y 6, 28 x 28,
// anchored right: 33 px in from the window's right edge.
internal static class OverlayPlacementCases
{
  // Overlay widths drawn at the game font (NotoSans 17 px, 1.02 scale): the Running pill (text + 43)
  // + item spacing + the Stop button (at least 72), and the three idle buttons (text + 32 each).
  private const float RunningWidth = 183f;
  private const float IdleThreeButtonWidth = 362f;

  // From the ULD: WindowA component width 144, close button x 111, y 6, 28 square.
  private const float UldComponentWidth = 144f, UldCloseX = 111f, UldCloseY = 6f, UldCloseSize = 28f;

  private static OverlayPlacement.Box Close(Vector2 windowPos, float windowWidth, float scale)
    => new(windowPos + new Vector2((windowWidth - (UldComponentWidth - UldCloseX)) * scale, UldCloseY * scale), new Vector2(UldCloseSize * scale, UldCloseSize * scale));

  private static bool Covers(float overlayLeft, float overlayWidth, OverlayPlacement.Box b)
    => overlayLeft < b.Right && overlayLeft + overlayWidth > b.Left;

  private sealed record Window(string Name, float Width, Vector2 Anchor, Vector2 AnchorSize, (Vector2 Pos, Vector2 Size)[] Content);

  public static void Run(Action<string, bool, string> check)
  {
    var windows = new[]
    {
      // Sale list: the anchor's text and 36 px node hidden (nothing drawn in the slot).
      new Window("sale list, empty slot", 598f, new(408f, 6f), new(158f, 36f), []),
      // Sale list: the anchor's text and 36 px node drawn.
      new Window("sale list, slot drawn", 598f, new(408f, 6f), new(158f, 36f), [(new(408f, 10f), new(120f, 24f)), (new(530f, 6f), new(36f, 36f))]),
      // Retainer list: the venture count (the anchor itself) and the venture icon beside it.
      new Window("retainer list", 930f, new(730f, 9f), new(120f, 24f), [(new(730f, 9f), new(120f, 24f)), (new(852f, 5f), new(36f, 36f))]),
    };

    foreach (var w in windows)
    {
      foreach (var scale in new[] { 1f, 1.5f, 2f })
      {
        var windowPos = new Vector2(240f, 130f);
        var anchor = windowPos + (w.Anchor * scale);
        var anchorSize = w.AnchorSize * scale;
        var close = Close(windowPos, w.Width, scale);
        var content = w.Content.Select(c => new OverlayPlacement.Box(windowPos + (c.Pos * scale), c.Size * scale)).ToList();
        var keepClear = new List<OverlayPlacement.Box> { close };
        keepClear.AddRange(content);
        var right = OverlayPlacement.RightEdge(anchor, anchorSize, keepClear, scale);

        // 181a. Control: the old layout (left-aligned at the anchor) put the running overlay over the
        // close button on the sale list and over the venture count on the retainer list - the
        // reported defect, reproduced from the game's geometry.
        var oldCovered = Covers(anchor.X, RunningWidth * scale, close) || content.Any(c => Covers(anchor.X, RunningWidth * scale, c));
        check($"181 issue #12 {w.Name} x{scale}: control - the old left-aligned running overlay covered the close button or the game's content",
          oldCovered, $"oldRight={anchor.X + (RunningWidth * scale):0} closeLeft={close.Left:0}");

        // 181b. No overlay width the plugin can draw covers the close button: running, idle with all
        // three buttons, and every width up to the whole window.
        var widths = new List<float> { 0f, anchorSize.X, RunningWidth * scale, IdleThreeButtonWidth * scale };
        for (var width = 0f; width <= w.Width * scale; width += 7f) widths.Add(width);
        var coveringClose = widths.Where(width => Covers(right - width, width, close)).ToList();
        check($"181 issue #12 {w.Name} x{scale}: no overlay width covers the close button",
          coveringClose.Count == 0, coveringClose.Count == 0 ? "" : $"first covering width={coveringClose[0]:0}");

        // 181c. ...nor any of the game's content drawn in the anchor's slot (the venture count).
        var coveringContent = widths.Where(width => content.Any(c => Covers(right - width, width, c))).ToList();
        check($"181 issue #12 {w.Name} x{scale}: no overlay width covers the game's content in the header slot",
          coveringContent.Count == 0, coveringContent.Count == 0 ? "" : $"first covering width={coveringContent[0]:0}");

        // 181d. The overlay keeps the 4 px (scaled) gap to whatever it was pulled away from.
        var nearest = keepClear.Where(b => b.Right > anchor.X).Select(b => b.Left - right).DefaultIfEmpty(float.PositiveInfinity).Min();
        check($"181 issue #12 {w.Name} x{scale}: the nearest kept-clear box is at least the gap away",
          nearest >= (OverlayPlacement.Gap * scale) - 0.01f, $"nearest={nearest:0.00}");

        // 181e. The close button box rebuilt from the window's own box (used when the node cannot be
        // read) is the box the game's layout file gives.
        var fallback = OverlayPlacement.FallbackClose(new OverlayPlacement.Box(windowPos, new Vector2(w.Width * scale, 460f * scale)), scale);
        check($"181 issue #12 {w.Name} x{scale}: the fallback close box matches the game's WindowA close button",
          Vector2.Distance(fallback.Pos, close.Pos) < 0.01f && Vector2.Distance(fallback.Size, close.Size) < 0.01f,
          $"fallback={fallback.Pos}/{fallback.Size} close={close.Pos}/{close.Size}");
      }
    }

    // 182a. With nothing to keep clear the overlay keeps its anchor's right edge, so content no
    // wider than the anchor sits exactly where it always did.
    {
      var anchor = new Vector2(500f, 100f);
      var size = new Vector2(158f, 36f);
      var right = OverlayPlacement.RightEdge(anchor, size, new List<OverlayPlacement.Box>(), 1f);
      check("182 nothing to keep clear: the right edge is the anchor's right edge", right == anchor.X + size.X, $"right={right}");
    }

    // 182b. A box wholly left of the anchor does not pull the overlay.
    {
      var anchor = new Vector2(500f, 100f);
      var size = new Vector2(158f, 36f);
      var left = new OverlayPlacement.Box(new Vector2(400f, 100f), new Vector2(28f, 28f));
      var right = OverlayPlacement.RightEdge(anchor, size, new List<OverlayPlacement.Box> { left }, 1f);
      check("182 a box left of the anchor is ignored", right == anchor.X + size.X, $"right={right}");
    }

    // 182c. Help and config buttons, when the window shows them, are kept clear too.
    {
      var close = Close(Vector2.Zero, 598f, 1f);
      var config = new OverlayPlacement.Box(new Vector2(598f - 47f, 8f), new Vector2(16f, 16f));
      var help = new OverlayPlacement.Box(new Vector2(598f - 61f, 8f), new Vector2(16f, 16f));
      var buttons = new List<OverlayPlacement.Box> { close, config, help };
      var right = OverlayPlacement.RightEdge(new Vector2(408f, 6f), new Vector2(158f, 36f), buttons, 1f);
      var covered = buttons.Where(b => Covers(right - RunningWidth, RunningWidth, b)).ToList();
      check("182 help and config buttons are kept clear as well", covered.Count == 0, $"right={right} covered={covered.Count}");
    }

    // 182d. A header button read before the game laid it out (screen origin) is not trusted, so the
    // window-geometry fallback is used; a button inside its window is trusted.
    {
      var frame = new OverlayPlacement.Box(new Vector2(240f, 130f), new Vector2(598f, 460f));
      var unlaid = new OverlayPlacement.Box(Vector2.Zero, new Vector2(28f, 28f));
      var laid = Close(frame.Pos, 598f, 1f);
      check("182 a header button at the screen origin is not trusted; one inside the window is",
        !OverlayPlacement.Inside(unlaid, frame) && OverlayPlacement.Inside(laid, frame), "");
    }

    // 182e. Source-scan wiring pins (a missing source or marker FAILS): both retainer windows place
    // their overlay through the placement helper, laid out from the right edge.
    var roots = new[]
    {
      Path.Combine("..", "..", "..", "..", "..", "src", "LazyMarketCompanion"),
      Path.Combine("src", "LazyMarketCompanion"),
    };
    var root = roots.Where(Directory.Exists).FirstOrDefault() ?? "";
    var path = Path.Combine(root, "MarketAutomation.cs");
    var src = root.Length > 0 && File.Exists(path) ? File.ReadAllText(path) : "";
    check("182 scan: MarketAutomation.cs is found (control)", src.Length > 0, $"root={root}");
    var setupAt = src.IndexOf("private unsafe float ImGuiSetup(", StringComparison.Ordinal);
    var setupEnd = setupAt >= 0 ? src.IndexOf("_overlay = LalaUiKit.Layout.Overlay.Begin(", setupAt, StringComparison.Ordinal) : -1;
    var setup = setupAt >= 0 && setupEnd > setupAt ? src[setupAt..setupEnd] : "";
    check("182 scan: the overlay setup keeps clear of the header buttons and game content and places by the right edge",
      setup.Contains("KeepClear(addon, node,") && setup.Contains("OverlayPlacement.RightEdge(") && setup.Contains("keepClear.Content")
        && setup.Contains("new System.Numerics.Vector2(1f, 0f)"),
      setup.Length == 0 ? "ImGuiSetup not found" : "");
    check("182 scan: both retainer windows pass their addon to the overlay setup",
      src.Contains("ImGuiSetup(addon, \"RetainerList\", node)") && src.Contains("ImGuiSetup(addon, \"RetainerSellList\", node)"), "");
  }
}
