// The retainer-window overlay buttons are drawn from MarketAutomation (the automation state machine: task manager,
// AutoRetainer session, addon lifecycle), which cannot be compiled into a render harness. This mirror reproduces
// ONLY its two drawing methods (ImGuiSetup and DrawButtons) with the same kit calls, in the same order, at scale 1.
// VerifyAgainstSource fails the harness when the statements it copies change in MarketAutomation.cs, so the mirror
// cannot drift silently: update this file when that check fails.
using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;
using LazyMarketCompanion.Windows;

namespace LazyMarketCompanion.UiHarness;

internal static class OverlayMirror
{
  /// <summary>
  /// Draws one overlay window laid out from its top-right corner (issue #12: the source places it by its right
  /// edge so wider content grows left, never over the game window's close button): the buttons, or the Running
  /// pill and Stop while busy. <paramref name="minSize"/> stands in for the anchor node's size. Returns the
  /// overlay window's screen box this frame.
  /// </summary>
  public static (Vector2 Pos, Vector2 Size) Draw(string id, Vector2 topRight, (string Label, string Tooltip)[] buttons, bool busy, bool arSession, Vector2? minSize = null)
  {
    // ---- ImGuiSetup ----
    ImGui.SetNextWindowPos(topRight, ImGuiCond.None, new Vector2(1f, 0f));
    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3f, 3f));
    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0f, 0f));
    ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
    ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, minSize ?? new Vector2(300, 40));
    (Vector2 Pos, Vector2 Size) box;
    using (var overlay = Layout.Overlay.Begin(id, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoNavFocus
             | ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings))
    {
      box = (ImGui.GetWindowPos(), ImGui.GetWindowSize());
      // ---- DrawButtons ----
      DrawButtons(buttons, busy, arSession);
    }
    // ---- ImGuiPostSetup ----
    ImGui.PopStyleVar(5);
    return box;
  }

  private static void DrawButtons((string Label, string Tooltip)[] buttons, bool busy, bool arSession)
  {
    MarketChrome.PushFrame(compact: true);
    try
    {
      if (busy)
      {
        var pos = ImGui.GetCursorScreenPos();
        var label = "Running";
        var size = new Vector2(ImGui.CalcTextSize(label).X + 43, ImGui.GetFrameHeight());
        MarketChrome.StatusPill(ImGui.GetWindowDrawList(), pos, size, label, UiState.Running);
        ImGui.Dummy(size);
        ImGui.SameLine();
        var stopLabel = arSession ? "Stop (AutoRetainer)" : "Stop";
        if (MarketChrome.StopButton(stopLabel))
        {
        }
        return;
      }

      for (var i = 0; i < buttons.Length; i++)
      {
        var (label, _) = buttons[i];
        if (i > 0) ImGui.SameLine();
        var disabled = label == "Auto Market" && !Plugin.Configuration.AutoMarketEnabled;
        if (disabled) ImGui.BeginDisabled();
        if (MarketChrome.Button(label))
        {
        }
        if (disabled) ImGui.EndDisabled();
      }
    }
    finally
    {
      MarketChrome.PopFrame();
    }
  }

  /// <summary>The statements copied above, as they must still read in MarketAutomation.cs (whitespace-insensitive).</summary>
  private static readonly string[] Copied =
  [
    "ImGuiHelpers.SetNextWindowPosRelativeMainViewport(new System.Numerics.Vector2(right, position.Y), ImGuiCond.None, new System.Numerics.Vector2(1f, 0f));",
    "ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f.Scale());",
    "ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3f.Scale(), 3f.Scale()));",
    "ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0f.Scale(), 0f.Scale()));",
    "ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f.Scale());",
    "ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoNavFocus",
    "| ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);",
    "ImGui.PopStyleVar(5);",
    "MarketChrome.PushFrame(compact: true);",
    "var size = new Vector2(ImGui.CalcTextSize(label).X + 43, ImGui.GetFrameHeight());",
    "MarketChrome.StatusPill(ImGui.GetWindowDrawList(), pos, size, label, UiState.Running);",
    "ImGui.Dummy(size);",
    "var stopLabel = _arRetainer != null ? \"Stop (AutoRetainer)\" : \"Stop\";",
    "if (MarketChrome.StopButton(stopLabel))",
    "var disabled = label == \"Auto Market\" && !Plugin.Configuration.AutoMarketEnabled;",
    "if (disabled) ImGui.BeginDisabled();",
    "if (MarketChrome.Button(label))",
    "if (disabled) ImGui.EndDisabled();",
    "buttons.Add((\"Auto Pinch\",",
    "buttons.Add((\"Auto Market\",",
    "buttons.Add((\"Sweep to Bags\",",
    "MarketChrome.PopFrame();",
  ];

  public static void VerifyAgainstSource(string root)
  {
    var path = Path.Combine(root, "src", "LazyMarketCompanion", "MarketAutomation.cs");
    var text = new string(File.ReadAllText(path).Where(c => !char.IsWhiteSpace(c)).ToArray());
    var missing = Copied.Where(statement => !text.Contains(new string(statement.Where(c => !char.IsWhiteSpace(c)).ToArray()), StringComparison.Ordinal)).ToList();
    if (missing.Count > 0)
      throw new InvalidOperationException("OverlayMirror is out of sync with MarketAutomation.cs; these statements no longer match:\n  " + string.Join("\n  ", missing));
  }
}
