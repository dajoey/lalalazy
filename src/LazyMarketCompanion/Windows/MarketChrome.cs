using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyMarketCompanion.Windows;

/// <summary>Drawn controls and content-measured surfaces for LazyMarketCompanion's windows.</summary>
internal static class MarketChrome
{
  private const float ControlHeight = 34f;
  private const float CardPadding = 12f;
  private const float CardGap = 12f;
  private static readonly Vector2 ToggleSize = new(44, 24);
  private static readonly Vector2 CompactToggleSize = new(30, 18);
  private static int _cardDepth;

  internal static void PushFrame()
  {
    Theme.PushFrame();
    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
    ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 8f);
    ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
    ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 12f);
    ImGui.PushStyleColor(ImGuiCol.Button, Theme.Surface);
    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Accent with { W = 0.14f });
    ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Accent with { W = 0.30f });
    ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Surface);
    ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.ShellBar);
    ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.SurfaceBorder);
    ImGui.PushStyleColor(ImGuiCol.Header, Theme.Surface);
    ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.14f });
    ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Accent with { W = 0.30f });
    ImGui.PushStyleColor(ImGuiCol.SliderGrab, Theme.Accent);
    ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Theme.AccentActive);
    ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
  }

  internal static void PopFrame()
  {
    ImGui.PopStyleColor(12);
    ImGui.PopStyleVar(4);
    Theme.PopFrame();
  }

  internal static void Diamond(ImDrawListPtr draw, Vector2 center, float radius, uint color)
  {
    var points = new[]
    {
      center + new Vector2(0, -radius),
      center + new Vector2(radius, 0),
      center + new Vector2(0, radius),
      center + new Vector2(-radius, 0),
    };
    draw.AddConvexPolyFilled(ref points[0], points.Length, color);
    draw.AddCircleFilled(center, radius * 0.30f, Theme.Pack(Theme.Surface));
  }

  internal static bool BrandButton(ImDrawListPtr draw, Vector2 pos, Vector2 size, string label = "Lazy Market Companion")
  {
    ImGui.SetCursorScreenPos(pos);
    var clicked = ImGui.InvisibleButton("##lmc_brand" + label, size);
    var hovered = ImGui.IsItemHovered();
    draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 8f);
    draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), 8f);
    Diamond(draw, pos + new Vector2(23, size.Y / 2f), 14, Theme.Pack(Theme.Accent));
    var textSize = ImGui.CalcTextSize(label) * Theme.TitleScale;
    DrawText(draw, pos + new Vector2(50, ((size.Y - textSize.Y) / 2f) - 1), label, Theme.TextPrimary, Theme.TitleScale);
    return clicked;
  }

  internal static bool OutlineButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected = false)
  {
    ImGui.SetCursorScreenPos(pos);
    var clicked = ImGui.InvisibleButton("##lmc_" + id, size);
    var hovered = ImGui.IsItemHovered();
    var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
    draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 8f);
    draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder), 8f);
    var textSize = ImGui.CalcTextSize(label);
    draw.AddText(pos + ((size - textSize) / 2f) - new Vector2(0, 1),
      Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), label);
    return clicked;
  }

  internal static bool FeatureButton(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
  {
    ImGui.SetCursorScreenPos(pos);
    var clicked = ImGui.InvisibleButton("##lmc_feature_" + id, size);
    var hovered = ImGui.IsItemHovered();
    var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
    draw.AddRectFilled(pos, pos + size, Theme.Pack(fill), 8f);
    if (selected || hovered)
      draw.AddRect(pos, pos + size, Theme.Pack(Theme.Accent), 8f);
    var marker = pos + new Vector2(16, size.Y / 2f);
    draw.AddCircle(marker, 6, Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), 12, 1);
    if (selected)
      draw.AddCircleFilled(marker, 3, Theme.Pack(Theme.Accent));
    var textSize = ImGui.CalcTextSize(label);
    draw.AddText(pos + new Vector2(30, ((size.Y - textSize.Y) / 2f) - 1),
      Theme.Pack(selected ? Theme.Accent : Theme.TextPrimary), label);
    return clicked;
  }

  internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, string label, UiState state)
  {
    var color = Copy.MarkColor(state);
    draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), size.Y / 2f);
    draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), size.Y / 2f);
    draw.AddCircleFilled(pos + new Vector2(15, size.Y / 2f), Theme.DotRadius, Theme.Pack(color));
    var textSize = ImGui.CalcTextSize(label);
    draw.AddText(pos + new Vector2(27, (size.Y - textSize.Y) / 2f), Theme.Pack(color), label);
  }

  internal static bool Button(string label)
  {
    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
    var clicked = ImGui.Button(label);
    ImGui.PopStyleVar();
    return clicked;
  }

  /// <summary>A pink track toggle. Labels wrap; compact table toggles keep their existing row identity.</summary>
  internal static bool Toggle(string literalLabel, ref bool value)
  {
    var separator = literalLabel.IndexOf("##", StringComparison.Ordinal);
    var label = separator < 0 ? literalLabel : literalLabel[..separator];
    if (label.Length == 0)
      return CompactToggle(literalLabel, ref value);

    var pos = ImGui.GetCursorScreenPos();
    var width = ImGui.GetContentRegionAvail().X;
    var labelWidth = MathF.Max(20, width - ToggleSize.X - (3 * Theme.Unit));
    var labelSize = ImGui.CalcTextSize(label, false, labelWidth);
    var rowHeight = MathF.Max(ToggleSize.Y, labelSize.Y);
    var togglePos = pos + new Vector2(width - ToggleSize.X, 0);
    var clicked = DrawTrack(literalLabel, togglePos, ToggleSize, ref value);

    var labelY = labelSize.Y <= ImGui.GetTextLineHeight() + 1 ? (ToggleSize.Y - labelSize.Y) / 2f : 0;
    ImGui.SetCursorScreenPos(pos + new Vector2(0, labelY));
    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + labelWidth);
    ImGui.TextUnformatted(label);
    ImGui.PopTextWrapPos();
    ImGui.SetCursorScreenPos(pos + new Vector2(0, rowHeight + Theme.ItemSpacing.Y));
    return clicked;
  }

  private static bool CompactToggle(string id, ref bool value)
  {
    var pos = ImGui.GetCursorScreenPos();
    return DrawTrack(id, pos, CompactToggleSize, ref value);
  }

  private static bool DrawTrack(string id, Vector2 pos, Vector2 size, ref bool value)
  {
    ImGui.SetCursorScreenPos(pos);
    var clicked = ImGui.InvisibleButton(id, size);
    if (clicked)
      value = !value;
    var hovered = ImGui.IsItemHovered();
    var draw = ImGui.GetWindowDrawList();
    draw.AddRectFilled(pos, pos + size, Theme.Pack(value ? Theme.Accent : Theme.SurfaceBorder), size.Y / 2f);
    if (hovered)
      draw.AddRect(pos, pos + size, Theme.Pack(Theme.Accent), size.Y / 2f);
    var radius = size.Y / 2f - 4f;
    var centerX = value ? pos.X + size.X - (size.Y / 2f) : pos.X + (size.Y / 2f);
    draw.AddCircleFilled(new Vector2(centerX, pos.Y + (size.Y / 2f)), radius,
      Theme.Pack(value ? Theme.Surface : Theme.TextSecondary));
    return clicked;
  }

  internal static void PageHeader(string title, string description, string count, string glyph = "◆")
  {
    var width = ImGui.GetContentRegionAvail().X;
    var origin = ImGui.GetCursorScreenPos();
    var draw = ImGui.GetWindowDrawList();
    var tileSize = new Vector2(42);
    draw.AddRectFilled(origin, origin + tileSize, Theme.Pack(Theme.Accent with { W = 0.14f }), 8f);
    if (glyph == "◆")
      Diamond(draw, origin + (tileSize / 2f), 10, Theme.Pack(Theme.Accent));
    else
    {
      var glyphSize = ImGui.CalcTextSize(glyph);
      draw.AddText(origin + ((tileSize - glyphSize) / 2f), Theme.Pack(Theme.Accent), glyph);
    }

    var textX = origin.X + tileSize.X + 12;
    if (width >= 700)
    {
      var countWidth = ImGui.CalcTextSize(count).X;
      DrawText(draw, new Vector2(textX, origin.Y), FitText(title, width - tileSize.X - 12 - countWidth - 16, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
      DrawText(draw, new Vector2(textX, origin.Y + 29), FitText(description, width - tileSize.X - 12, Theme.SecondaryScale), Theme.TextSecondary);
      DrawTextRight(draw, origin.X + width, origin.Y + 5, count, Theme.TextSecondary);
      ImGui.SetCursorScreenPos(origin + new Vector2(0, 56));
    }
    else
    {
      DrawText(draw, new Vector2(textX, origin.Y + 4), FitText(title, width - tileSize.X - 12, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
      ImGui.SetCursorScreenPos(origin + new Vector2(0, tileSize.Y + 6));
      Widgets.SecondaryWrapped(description);
      Widgets.Caption(count);
    }
    ImGui.Separator();
  }

  internal static void Section(string title, string? description = null, string? count = null)
  {
    Widgets.Title(count is null ? title : $"{title} · {count}");
    if (!string.IsNullOrEmpty(description))
      Widgets.SecondaryWrapped(description);
  }

  internal static void Card(string id, Action content, float minHeight = 0, float width = 0)
  {
    _ = id;
    var draw = ImGui.GetWindowDrawList();
    var origin = ImGui.GetCursorScreenPos();
    var cardWidth = width <= 0 ? ImGui.GetContentRegionAvail().X : MathF.Min(width, ImGui.GetContentRegionAvail().X);
    var topLevel = _cardDepth == 0;
    if (topLevel)
    {
      _cardDepth++;
      draw.ChannelsSplit(3);
    }
    try
    {
      draw.ChannelsSetCurrent(2);
      ImGui.SetCursorScreenPos(origin + new Vector2(CardPadding, CardPadding));
      ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * CardPadding)));
      ImGui.BeginGroup();
      content();
      ImGui.PopTextWrapPos();
      ImGui.EndGroup();
      var contentBottom = ImGui.GetCursorScreenPos().Y;
      var height = MathF.Max(minHeight, contentBottom - origin.Y + CardPadding);

      draw.ChannelsSetCurrent(topLevel ? 0 : 1);
      draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.Surface), 8f);
      draw.AddRect(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.SurfaceBorder), 8f);
      if (topLevel)
        draw.ChannelsMerge();
      else
        draw.ChannelsSetCurrent(2);
      ImGui.SetCursorScreenPos(origin + new Vector2(0, height + CardGap));
    }
    finally
    {
      if (topLevel)
        _cardDepth--;
    }
  }

  internal static void TwoColumns(Action work, Action rail, float leftFraction = 0.68f)
  {
    if (ImGui.GetContentRegionAvail().X >= 760)
    {
      var available = ImGui.GetContentRegionAvail().X;
      var gap = 3 * Theme.Unit;
      var leftWidth = (available - gap) * leftFraction;
      ImGui.BeginChild("##lmcWork", new Vector2(leftWidth, 0), false);
      work();
      ImGui.EndChild();
      ImGui.SameLine(0, gap);
      ImGui.BeginChild("##lmcRail", new Vector2(0, 0), false);
      rail();
      ImGui.EndChild();
    }
    else
    {
      work();
      ImGui.Separator();
      rail();
    }
  }

  internal readonly record struct Stat(string Label, string Value, string Note);

  internal static void StatCards(params Stat[] stats)
  {
    if (stats.Length == 0)
      return;
    var available = ImGui.GetContentRegionAvail().X;
    var stacked = available < 620;
    var gap = 3 * Theme.Unit;
    var width = stacked ? available : (available - ((stats.Length - 1) * gap)) / stats.Length;
    const float height = 86;
    var origin = ImGui.GetCursorScreenPos();
    var draw = ImGui.GetWindowDrawList();
    for (var i = 0; i < stats.Length; i++)
    {
      var stat = stats[i];
      var pos = origin + (stacked ? new Vector2(0, i * (height + gap)) : new Vector2(i * (width + gap), 0));
      draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), 8f);
      draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 8f);
      Diamond(draw, pos + new Vector2(18, 18), 5, Theme.Pack(Theme.Accent));
      DrawText(draw, pos + new Vector2(32, 10), FitText(stat.Label, width - 44), Theme.TextSecondary);
      DrawText(draw, pos + new Vector2(12, 31), FitText(stat.Value, width - 24, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
      DrawText(draw, pos + new Vector2(12, 62), FitText(stat.Note, width - 24), Theme.TextSecondary);
    }
    ImGui.SetCursorScreenPos(origin + new Vector2(0, stacked ? stats.Length * (height + gap) : height + gap));
  }

  internal static float FooterHeight(float width, bool hasStop) => hasStop && width < 620 ? 100 : 62;

  internal static void Footer(UiState state, string action, string detail, string? stopLabel, Action? stop)
  {
    var origin = ImGui.GetCursorScreenPos();
    var width = ImGui.GetContentRegionAvail().X;
    var hasStop = !string.IsNullOrEmpty(stopLabel) && stop is not null;
    var height = FooterHeight(width, hasStop);
    var draw = ImGui.GetWindowDrawList();
    draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar));
    draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
    var color = Copy.MarkColor(state);
    var narrow = hasStop && width < 620;
    var stopWidth = hasStop ? MathF.Max(92, ImGui.CalcTextSize(stopLabel!).X + 32) : 0;
    var stopPos = origin + new Vector2(width - stopWidth, narrow ? 54 : 13);
    var textRight = narrow || !hasStop ? origin.X + width : stopPos.X - 16;
    var textPos = origin + new Vector2(24, 8);
    draw.AddCircleFilled(origin + new Vector2(10, 18), Theme.DotRadius + 1, Theme.Pack(color));
    DrawText(draw, textPos, FitText($"{Copy.Word(state)} · {action}", textRight - textPos.X), Theme.TextPrimary);
    DrawText(draw, textPos + new Vector2(0, 25), FitText(detail, textRight - textPos.X), Theme.TextSecondary);
    if (hasStop)
    {
      ImGui.SetCursorScreenPos(stopPos);
      if (Widgets.StopButton(stopLabel!))
        stop!();
    }
    ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
  }

  internal static void DrawText(ImDrawListPtr draw, Vector2 pos, string text, Vector4 color, float scale = 1)
  {
    if (text.Length == 0)
      return;
    if (scale == 1)
      draw.AddText(pos, Theme.Pack(color), text);
    else
      draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Theme.Pack(color), text);
  }

  private static void DrawTextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color)
  {
    if (text.Length == 0)
      return;
    draw.AddText(new Vector2(right - ImGui.CalcTextSize(text).X, y), Theme.Pack(color), text);
  }

  internal static string FitText(string text, float width, float scale = 1)
  {
    if (width <= 0)
      return "";
    if (ImGui.CalcTextSize(text).X * scale <= width)
      return text;
    const string ellipsis = "…";
    var length = text.Length;
    while (length > 0 && ImGui.CalcTextSize(text[..length] + ellipsis).X * scale > width)
      length--;
    return length == 0 ? "" : text[..length].TrimEnd() + ellipsis;
  }
}
