using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyMarketCompanion.Windows;

/// <summary>
/// Drawn controls and content-measured surfaces for LazyMarketCompanion's windows: the shell bands
/// (header, sub-navigation, page header, footer), cards, rows, stat cards and the styled controls.
/// Every size is measured from the live font; nothing is tuned to one font size.
/// </summary>
internal static class MarketChrome
{
  internal const float Control = 36f;
  internal const float Radius = 8f;
  private const float CardPad = 16f;
  private const float RowPad = 12f;
  private const float Gap = 8f;
  private static readonly Vector2 ToggleSize = new(44, 24);

  // ---- layout state (reset every frame by PushFrame) ----
  private static readonly Stack<float> Rights = new();
  private static int _cardDepth;
  private static int _rowsInCard;
  private static float _trailPad;
  private static float _fillExtra;
  private static float _fillStretch;
  private static bool _bodyScrollable;
  private static readonly Dictionary<string, (float Main, float Rail)> ColumnHeights = new();

  // =====================================================================================
  // measuring
  // =====================================================================================

  private static T Measure<T>(float scale, Func<T> measure)
  {
    if (scale == 1f)
      return measure();
    ImGui.SetWindowFontScale(scale);
    try { return measure(); }
    finally { ImGui.SetWindowFontScale(1f); }
  }

  /// <summary>Short windows (under 520 px tall) trade band padding for body room.</summary>
  internal static bool Short => ImGui.GetWindowHeight() < 520;

  internal static float TextWidth(string text, float scale = 1f) => Measure(scale, () => ImGui.CalcTextSize(text).X);

  internal static float LineHeight(float scale = 1f) => Measure(scale, () => ImGui.GetTextLineHeight());

  private static float WrappedHeight(string text, float width, float scale = 1f)
    => Measure(scale, () => ImGui.CalcTextSize(text, false, MathF.Max(1f, width)).Y);

  internal static float Gutter(float windowWidth) => windowWidth >= 760 ? 24 : windowWidth >= 560 ? 16 : 12;

  private static uint A(Vector4 color) => Theme.Pack(color with { W = color.W * ImGui.GetStyle().Alpha });

  /// <summary>Distance from the cursor to the inner right edge of whatever surface is being laid out.</summary>
  internal static float Avail()
  {
    var x = ImGui.GetCursorScreenPos().X;
    return (Rights.Count > 0 ? Rights.Peek() : x + ImGui.GetContentRegionAvail().X) - x;
  }

  internal static void PushRight(float screenX) => Rights.Push(screenX);

  internal static void PopRight()
  {
    if (Rights.Count > 0)
      Rights.Pop();
  }

  // =====================================================================================
  // frame
  // =====================================================================================

  internal static void PushFrame(bool compact = false)
  {
    Rights.Clear();
    _cardDepth = 0;
    _rowsInCard = 0;
    Theme.PushFrame();
    var pad = compact ? new Vector2(10, 5) : new Vector2(12, MathF.Max(4, MathF.Floor((Control - ImGui.GetTextLineHeight()) / 2f)));
    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Radius);
    ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, Radius);
    ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Radius);
    ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 11f);
    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, pad);
    ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
    ImGui.PushStyleVar(ImGuiStyleVar.GrabMinSize, 22f);
    ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(8, 6));
    ImGui.PushStyleVar(ImGuiStyleVar.DisabledAlpha, 0.72f);
    ImGui.PushStyleColor(ImGuiCol.Button, Theme.ShellBar);
    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Accent with { W = 0.16f });
    ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Accent with { W = 0.30f });
    ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.ShellBar);
    ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.ShellBar);
    ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.Surface);
    ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent with { W = 0.14f });
    ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent with { W = 0.20f });
    ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Accent with { W = 0.30f });
    ImGui.PushStyleColor(ImGuiCol.SliderGrab, Theme.Accent);
    ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Theme.AccentActive);
    ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
    ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
    ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, Theme.ShellBar);
    ImGui.PushStyleColor(ImGuiCol.TableRowBg, 0u);
    ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, Theme.ShellBar with { W = 0.55f });
    ImGui.PushStyleColor(ImGuiCol.TableBorderLight, Theme.SurfaceBorder with { W = 0.6f });
    ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, Theme.SurfaceBorder);
    ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, Theme.Accent with { W = 0.30f });
  }

  internal static void PopFrame()
  {
    ImGui.PopStyleColor(19);
    ImGui.PopStyleVar(9);
    Theme.PopFrame();
  }

  // =====================================================================================
  // small drawn parts
  // =====================================================================================

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
    draw.AddCircleFilled(center, radius * 0.32f, Theme.Pack(Theme.Surface));
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

  private static void DrawTextRight(ImDrawListPtr draw, float right, float y, string text, Vector4 color, float scale = 1)
  {
    if (text.Length == 0)
      return;
    DrawText(draw, new Vector2(right - TextWidth(text, scale), y), text, color, scale);
  }

  internal static string FitText(string text, float width, float scale = 1)
  {
    if (width <= 0)
      return "";
    if (TextWidth(text, scale) <= width)
      return text;
    const string ellipsis = "…";
    var length = text.Length;
    while (length > 0 && TextWidth(text[..length] + ellipsis, scale) > width)
      length--;
    return length == 0 ? "" : text[..length].TrimEnd() + ellipsis;
  }

  /// <summary>Quality marker that follows an item name; never elided.</summary>
  internal const string HqMark = " (HQ)";

  internal static string ItemLabel(string name, bool hq) => hq ? name + HqMark : name;

  /// <summary>FitText for text that ends in a marker which must stay whole: only the part before the suffix is elided.</summary>
  internal static string FitTextKeepSuffix(string text, string suffix, float width, float scale = 1)
  {
    if (suffix.Length == 0 || !text.EndsWith(suffix, StringComparison.Ordinal))
      return FitText(text, width, scale);
    if (TextWidth(text, scale) <= width)
      return text;
    var room = width - TextWidth(suffix, scale);
    var head = text[..^suffix.Length];
    if (room <= TextWidth("…", scale))
      return FitText(text, width, scale);
    return FitText(head, room, scale) + suffix;
  }

  /// <summary>Greedy word wrap into at most maxLines lines at the draw size; the last line is elided (keepSuffix, when the text ends in it, stays whole).</summary>
  internal static List<string> WrapText(string text, float width, float scale, int maxLines, string keepSuffix = "")
  {
    var lines = new List<string>();
    if (text.Length == 0 || width <= 0)
      return lines;
    var words = text.Split(' ');
    var line = string.Empty;
    for (var i = 0; i < words.Length; i++)
    {
      var candidate = line.Length == 0 ? words[i] : line + " " + words[i];
      if (line.Length > 0 && TextWidth(candidate, scale) > width)
      {
        lines.Add(line);
        line = words[i];
        if (lines.Count == maxLines - 1)
        {
          line = string.Join(' ', words.Skip(i));
          break;
        }
      }
      else
        line = candidate;
    }
    lines.Add(FitTextKeepSuffix(line, keepSuffix, width, scale));
    return lines;
  }

  private static void TipIfElided(string full, string shown, Vector2 min, Vector2 max)
  {
    if (full == shown || !ImGui.IsMouseHoveringRect(min, max))
      return;
    ImGui.SetTooltip(full);
  }

  // =====================================================================================
  // shell: header band
  // =====================================================================================

  internal readonly record struct NavTab(string Label, bool Selected);

  internal static float PillWidth()
    => MathF.Max(104, 16 + (2 * Theme.DotRadius) + 8 + Enum.GetValues<UiState>().Max(s => TextWidth(Copy.Word(s))) + 16);

  internal static void StatusPill(ImDrawListPtr draw, Vector2 pos, Vector2 size, string label, UiState state)
  {
    var mark = Copy.MarkColor(state);
    var neutral = state is UiState.Idle or UiState.NeedsSetup or UiState.Empty;
    draw.AddRectFilled(pos, pos + size, A(Theme.Surface), size.Y / 2f);
    draw.AddRect(pos, pos + size, A(Theme.SurfaceBorder), size.Y / 2f);
    var group = (2 * Theme.DotRadius) + 8 + TextWidth(label);
    var x = pos.X + ((size.X - group) / 2f);
    draw.AddCircleFilled(new Vector2(x + Theme.DotRadius, pos.Y + (size.Y / 2f)), Theme.DotRadius, A(mark));
    var text = new Vector2(x + (2 * Theme.DotRadius) + 8, pos.Y + ((size.Y - LineHeight()) / 2f));
    draw.AddText(text, A(neutral ? Theme.TextSecondary : mark), label);
  }

  internal static bool CloseButton(ImDrawListPtr draw, Vector2 pos, Vector2 size)
  {
    ImGui.SetCursorScreenPos(pos);
    var clicked = ImGui.InvisibleButton("##lmc_close", size);
    var hovered = ImGui.IsItemHovered();
    draw.AddRectFilled(pos, pos + size, Theme.Pack(hovered ? Theme.Accent with { W = 0.14f } : Theme.Surface), Radius);
    draw.AddRect(pos, pos + size, Theme.Pack(hovered ? Theme.Accent : Theme.SurfaceBorder), Radius);
    var c = pos + (size / 2f);
    var col = Theme.Pack(hovered ? Theme.Accent : Theme.TextSecondary);
    draw.AddLine(c + new Vector2(-6, -6), c + new Vector2(6, 6), col, 1.5f);
    draw.AddLine(c + new Vector2(-6, 6), c + new Vector2(6, -6), col, 1.5f);
    if (hovered)
    {
      ImGui.SetNextWindowPos(new Vector2(pos.X + size.X, pos.Y + size.Y + 4), ImGuiCond.Always, new Vector2(1f, 0f));
      ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
      ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
      ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 4));
      ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Radius);
      ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
      ImGui.BeginTooltip();
      ImGui.TextUnformatted("Close");
      ImGui.EndTooltip();
      ImGui.PopStyleVar(3);
      ImGui.PopStyleColor(2);
    }
    return clicked;
  }

  /// <summary>The 68 px header band: brand chip, optional navigation pills, status pill and close. Returns the y where the next band starts.</summary>
  internal static float HeaderBand(string brand, UiState state, NavTab[] nav, out int navClicked, out bool brandClicked, out bool closeClicked)
  {
    navClicked = -1;
    brandClicked = false;
    var win = ImGui.GetWindowPos();
    var width = ImGui.GetWindowWidth();
    var draw = ImGui.GetWindowDrawList();
    var g = Gutter(width);
    var pad = Short ? 8f : 14f;
    var brandH = Short ? 36f : 40f;
    var pillSize = new Vector2(PillWidth(), Control);
    var closeSize = new Vector2(Control);
    var rightWidth = pillSize.X + Gap + closeSize.X;
    var naturalBrand = 48 + TextWidth(brand, Theme.TitleScale) + 16;
    var navWidths = nav.Select(n => MathF.Max(72, TextWidth(n.Label) + 32)).ToArray();
    var navTotal = navWidths.Sum() + (Gap * MathF.Max(0, nav.Length - 1));
    var singleRow = nav.Length == 0 || g + naturalBrand + 16 + navTotal + 16 + rightWidth + g <= width;
    var bar = singleRow ? pad + brandH + pad : pad + brandH + 6 + Control + (Short ? 8 : 12);

    draw.PushClipRect(win, win + new Vector2(width, bar + 1), false);
    draw.AddRectFilled(win, win + new Vector2(width, bar), Theme.Pack(Theme.ShellBar));
    draw.AddLine(win + new Vector2(0, bar), win + new Vector2(width, bar), Theme.Pack(Theme.SurfaceBorder));
    draw.PopClipRect();

    var rowCentre = pad + (brandH / 2f);
    var closePos = new Vector2(width - g - Control, rowCentre - (Control / 2f));
    var pillPos = new Vector2(closePos.X - Gap - pillSize.X, closePos.Y);
    var brandRoom = singleRow && nav.Length > 0 ? naturalBrand : MathF.Min(naturalBrand, pillPos.X - 16 - g);
    brandRoom = MathF.Max(brandRoom, 48 + 16 + 24);
    var brandPos = win + new Vector2(g, pad);
    var brandText = FitText(brand, brandRoom - 48 - 16, Theme.TitleScale);
    ImGui.SetCursorScreenPos(brandPos);
    brandClicked = ImGui.InvisibleButton("##lmc_brand", new Vector2(brandRoom, brandH));
    var brandHover = ImGui.IsItemHovered();
    draw.AddRectFilled(brandPos, brandPos + new Vector2(brandRoom, brandH), Theme.Pack(Theme.Surface), Radius);
    draw.AddRect(brandPos, brandPos + new Vector2(brandRoom, brandH), Theme.Pack(brandHover ? Theme.Accent : Theme.SurfaceBorder), Radius);
    Diamond(draw, brandPos + new Vector2(24, brandH / 2f), 12, Theme.Pack(Theme.Accent));
    DrawText(draw, brandPos + new Vector2(48, (brandH - LineHeight(Theme.TitleScale)) / 2f), brandText, Theme.TextPrimary, Theme.TitleScale);

    if (nav.Length > 0)
    {
      float x;
      float y;
      float[] widths = navWidths;
      if (singleRow)
      {
        x = win.X + g + brandRoom + 16;
        y = win.Y + rowCentre - (Control / 2f);
      }
      else
      {
        var inner = width - (2 * g);
        var each = (inner - (Gap * (nav.Length - 1))) / nav.Length;
        widths = nav.Select(_ => each).ToArray();
        x = win.X + g;
        y = win.Y + pad + brandH + 6;
      }
      for (var i = 0; i < nav.Length; i++)
      {
        if (NavPill(draw, "nav_" + nav[i].Label, nav[i].Label, new Vector2(x, y), new Vector2(widths[i], Control), nav[i].Selected))
          navClicked = i;
        x += widths[i] + Gap;
      }
    }

    StatusPill(draw, win + pillPos, pillSize, Copy.Word(state), state);
    closeClicked = CloseButton(draw, win + closePos, closeSize);
    ImGui.SetCursorPos(new Vector2(0, bar + 1));
    return bar + 1;
  }

  private static bool NavPill(ImDrawListPtr draw, string id, string label, Vector2 pos, Vector2 size, bool selected)
  {
    ImGui.SetCursorScreenPos(pos);
    var clicked = ImGui.InvisibleButton("##lmc_" + id, size);
    var hovered = ImGui.IsItemHovered();
    draw.AddRectFilled(pos, pos + size, Theme.Pack(selected ? Theme.Accent with { W = 0.14f } : Theme.Surface), Radius);
    draw.AddRect(pos, pos + size, Theme.Pack(selected || hovered ? Theme.Accent : Theme.SurfaceBorder with { W = 0.6f }), Radius);
    var color = selected ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary;
    DrawText(draw, pos + new Vector2((size.X - TextWidth(label)) / 2f, (size.Y - LineHeight()) / 2f), label, color);
    return clicked;
  }

  /// <summary>The 56 px sub-navigation band. Only drawn by callers whose group has two or more pages.</summary>
  internal static float SubNavBand(string[] labels, int selected, out int clicked)
  {
    clicked = -1;
    var win = ImGui.GetWindowPos();
    var top = ImGui.GetCursorPosY();
    var width = ImGui.GetWindowWidth();
    var draw = ImGui.GetWindowDrawList();
    var g = Gutter(width);
    var band = Short ? 44f : 56f;
    var pillH = Short ? 28f : 32f;
    var origin = win + new Vector2(0, top);
    draw.PushClipRect(origin, origin + new Vector2(width, band + 1), false);
    draw.AddRectFilled(origin, origin + new Vector2(width, band), Theme.Pack(Theme.ShellBar));
    draw.AddLine(origin + new Vector2(0, band), origin + new Vector2(width, band), Theme.Pack(Theme.SurfaceBorder));
    draw.PopClipRect();
    var x = origin.X + g;
    for (var i = 0; i < labels.Length; i++)
    {
      var size = new Vector2(16 + 12 + 8 + TextWidth(labels[i], Theme.SecondaryScale) + 16 + 1, pillH);
      var pos = new Vector2(x, origin.Y + ((band - pillH) / 2f));
      ImGui.SetCursorScreenPos(pos);
      if (ImGui.InvisibleButton("##lmc_sub_" + i, size))
        clicked = i;
      var hovered = ImGui.IsItemHovered();
      var sel = i == selected;
      draw.AddRectFilled(pos, pos + size, Theme.Pack(sel ? Theme.Accent with { W = 0.14f } : Theme.Surface), Radius);
      draw.AddRect(pos, pos + size, Theme.Pack(sel || hovered ? Theme.Accent : Theme.SurfaceBorder with { W = 0.6f }), Radius);
      var marker = pos + new Vector2(16, size.Y / 2f);
      draw.AddCircle(marker, 6, Theme.Pack(sel ? Theme.Accent : Theme.TextSecondary), 12, 1);
      if (sel)
        draw.AddCircleFilled(marker, 3, Theme.Pack(Theme.Accent));
      DrawText(draw, pos + new Vector2(36, (size.Y - LineHeight(Theme.SecondaryScale)) / 2f), labels[i],
        sel ? Theme.Accent : hovered ? Theme.TextPrimary : Theme.TextSecondary, Theme.SecondaryScale);
      x += size.X + 12;
    }
    ImGui.SetCursorPos(new Vector2(0, top + band + 1));
    return band + 1;
  }

  /// <summary>Page header: accent tile, 1.35x title, description, right-aligned meta. Returns its height.</summary>
  internal static float PageHeader(string title, string description, string meta)
  {
    var win = ImGui.GetWindowPos();
    var top = ImGui.GetCursorPosY();
    var width = ImGui.GetWindowWidth();
    var g = Gutter(width);
    var draw = ImGui.GetWindowDrawList();
    var compact = ImGui.GetWindowHeight() < 520;
    var wide = width >= 760;
    var tile = compact ? 32f : 44f;
    const float titleScale = 1.35f;
    var padTop = compact ? 10f : 20f;
    var textX = g + (compact ? 46 : 60);
    var origin = win + new Vector2(g, top + padTop);
    draw.AddRectFilled(origin, origin + new Vector2(tile), Theme.Pack(Theme.Accent with { W = 0.14f }), Radius);
    Diamond(draw, origin + new Vector2(tile / 2f), compact ? 8 : 10, Theme.Pack(Theme.Accent));

    var titleH = LineHeight(titleScale);
    var descH = LineHeight(Theme.SecondaryScale);
    var metaW = wide && meta.Length > 0 ? MathF.Min(TextWidth(meta, Theme.SecondaryScale), width * 0.4f) : 0;
    var textW = wide ? width - g - textX - metaW - 16 : width - g - textX;
    var shownTitle = FitText(title, textW, titleScale);
    DrawText(draw, new Vector2(win.X + textX, origin.Y), shownTitle, Theme.TextPrimary, titleScale);
    var block = titleH;
    if (!compact)
    {
      var descLines = WrapText(description, textW, Theme.SecondaryScale, wide ? 1 : 3);
      var y = origin.Y + titleH + 4;
      foreach (var line in descLines)
      {
        DrawText(draw, new Vector2(win.X + textX, y), line, Theme.TextSecondary, Theme.SecondaryScale);
        y += descH;
      }
      block = titleH + 4 + (descLines.Count * descH);
      if (wide && meta.Length > 0)
      {
        var shownMeta = FitText(meta, metaW, Theme.SecondaryScale);
        DrawTextRight(draw, win.X + width - g, origin.Y + ((titleH - descH) / 2f), shownMeta, Theme.TextSecondary, Theme.SecondaryScale);
      }
      else if (meta.Length > 0)
      {
        DrawText(draw, new Vector2(win.X + textX, y + 4), FitText(meta, textW, Theme.SecondaryScale), Theme.TextSecondary, Theme.SecondaryScale);
        block += 4 + descH;
      }
    }
    var height = padTop + MathF.Max(tile, block) + (compact ? 6 : 8);
    ImGui.SetCursorPos(new Vector2(0, top + height));
    return height;
  }

  // =====================================================================================
  // shell: footer
  // =====================================================================================

  /// <summary>Short windows with nothing to stop show the status line only; a running action keeps its note.</summary>
  private static bool OneLineFooter(bool hasAction) => Short && !hasAction;

  internal static float FooterHeight(float width, bool hasAction)
  {
    var block = OneLineFooter(hasAction) ? LineHeight() : LineHeight() + 4 + LineHeight(Theme.SecondaryScale);
    var pad = Short ? 10f : 12f;
    return Stacked(width, hasAction) ? pad + block + pad + Control + pad : pad + MathF.Max(block, Control) + pad;
  }

  /// <summary>Narrow, tall-enough windows put the footer action on its own full-width row under the status text.</summary>
  private static bool Stacked(float width, bool hasAction) => width < 620 && hasAction && !Short;

  /// <summary>Footer band, drawn from the window's bottom edge. The action button sits on the status row when it fits.</summary>
  internal static void Footer(UiState state, string action, string detail, string? stopLabel, Action? stop)
  {
    var win = ImGui.GetWindowPos();
    var width = ImGui.GetWindowWidth();
    var g = Gutter(width);
    var hasStop = !string.IsNullOrEmpty(stopLabel) && stop is not null;
    var height = FooterHeight(width, hasStop);
    var top = ImGui.GetWindowHeight() - height;
    var origin = win + new Vector2(0, top);
    var draw = ImGui.GetWindowDrawList();
    draw.PushClipRect(origin, win + ImGui.GetWindowSize(), false);
    draw.AddRectFilled(origin, win + ImGui.GetWindowSize(), Theme.Pack(Theme.ShellBar));
    draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
    draw.PopClipRect();

    var stacked = Stacked(width, hasStop);
    var stopWidth = hasStop ? (stacked ? width - (2 * g) : MathF.Max(72, TextWidth(stopLabel!) + 32)) : 0;
    var buttonLeft = hasStop ? width - g - stopWidth : width - g;
    var textX = g + 16;
    var textRoom = (hasStop && !stacked ? buttonLeft - 16 : width - g) - textX;
    var oneLine = OneLineFooter(hasStop);
    var block = oneLine ? LineHeight() : LineHeight() + 4 + LineHeight(Theme.SecondaryScale);
    var textTop = stacked ? origin.Y + 12 : origin.Y + ((height - block) / 2f);
    var line1 = $"{Copy.Word(state)} · {action}";
    var shown1 = FitText(line1, textRoom);
    var shown2 = FitText(detail, textRoom, Theme.SecondaryScale);
    draw.AddCircleFilled(new Vector2(win.X + g + 4, textTop + (LineHeight() / 2f)), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(state)));
    DrawText(draw, new Vector2(win.X + textX, textTop), shown1, Theme.TextPrimary);
    if (!oneLine)
      DrawText(draw, new Vector2(win.X + textX, textTop + LineHeight() + 4), shown2, Theme.TextSecondary, Theme.SecondaryScale);
    var mouse = ImGui.GetMousePos();
    if ((shown1 != line1 || (!oneLine && shown2 != detail) || oneLine) && mouse.X >= win.X + textX && mouse.X <= win.X + textX + textRoom && mouse.Y >= textTop && mouse.Y <= textTop + block)
      ImGui.SetTooltip(line1 + "\n" + detail);

    if (hasStop)
    {
      ImGui.SetCursorScreenPos(origin + new Vector2(width - g - stopWidth, stacked ? height - 12 - Control : (height - Control) / 2f));
      if (StopButton(stopLabel!, new Vector2(stopWidth, Control)))
        stop!();
    }
  }

  // =====================================================================================
  // body
  // =====================================================================================

  /// <summary>The single scrolling body: gutter padding, 10 px scrollbar track that is always reserved so content never shifts.</summary>
  internal static bool BeginBody(float footerHeight)
  {
    var width = ImGui.GetWindowWidth();
    var g = Gutter(width);
    var top = ImGui.GetCursorPosY();
    var size = new Vector2(width, MathF.Max(80, ImGui.GetWindowHeight() - footerHeight - top));
    ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4f, 16f));
    ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 10f);
    ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8f);
    ImGui.PushStyleColor(ImGuiCol.Border, 0u);
    ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
    ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, 0u);
    var grab = _bodyScrollable ? Theme.SurfaceBorder : Theme.SurfaceBorder with { W = 0f };
    ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, grab);
    ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, _bodyScrollable ? Theme.TextSecondary with { W = 0.5f } : grab);
    ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, _bodyScrollable ? Theme.Accent : grab);
    var open = ImGui.BeginChild("##lmcBody", size, true, ImGuiWindowFlags.AlwaysVerticalScrollbar);
    ImGui.PopStyleColor(6);
    ImGui.PopStyleVar(4);
    _bodyOpen = open;
    if (open)
    {
      Rights.Clear();
      // The child keeps a small window padding (its clip rect shrinks by half of it); the gutter is an indent.
      ImGui.Indent(g - 4f);
      // Content right edge = window width - gutter: the scrollbar track (10) is part of the gutter.
      PushRight(ImGui.GetWindowPos().X + width - g);
    }
    return open;
  }

  private static bool _bodyOpen;

  internal static void EndBody()
  {
    if (!_bodyOpen)
    {
      ImGui.EndChild();
      return;
    }
    PopRight();
    ImGui.Unindent(Gutter(ImGui.GetWindowWidth()) - 4f);
    var scrollMax = ImGui.GetScrollMaxY();
    _bodyScrollable = scrollMax > 0;
    if (_bodyScrollable && ImGui.GetScrollY() < scrollMax - 1)
    {
      var draw = ImGui.GetWindowDrawList();
      var pos = ImGui.GetWindowPos();
      var size = ImGui.GetWindowSize();
      var bg = ImGui.GetColorU32(ImGuiCol.WindowBg);
      var clear = bg & 0x00FFFFFFu;
      draw.AddRectFilledMultiColor(pos + new Vector2(0, size.Y - 28), pos + size - new Vector2(12, 0), clear, clear, bg, bg);
    }
    ImGui.EndChild();
  }

  // =====================================================================================
  // cards
  // =====================================================================================

  internal static void Card(string id, Action content, float minHeight = 0, float width = 0, bool fill = false, float gap = 16f)
  {
    _ = id;
    var draw = ImGui.GetWindowDrawList();
    var origin = ImGui.GetCursorScreenPos();
    var avail = Avail();
    var cardWidth = width <= 0 ? avail : MathF.Min(width, avail);
    var topLevel = _cardDepth == 0;
    var savedRows = _rowsInCard;
    var savedTrail = _trailPad;
    if (topLevel)
    {
      draw.ChannelsSplit(3);
    }
    _cardDepth++;
    try
    {
      draw.ChannelsSetCurrent(2);
      ImGui.SetCursorScreenPos(origin + new Vector2(CardPad, CardPad));
      PushRight(origin.X + cardWidth - CardPad);
      ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, cardWidth - (2 * CardPad)));
      ImGui.BeginGroup();
      _rowsInCard = 0;
      _trailPad = 0;
      content();
      ImGui.EndGroup();
      ImGui.PopTextWrapPos();
      PopRight();
      var contentBottom = ImGui.GetItemRectMax().Y - _trailPad;
      var natural = MathF.Max(minHeight, contentBottom - origin.Y + CardPad);
      var height = natural;
      if (fill && _fillExtra > 0)
      {
        height += _fillExtra;
        _fillStretch += _fillExtra;
        _fillExtra = 0;
      }

      draw.ChannelsSetCurrent(topLevel ? 0 : 1);
      draw.AddRectFilled(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.Surface), Radius);
      draw.AddRect(origin, origin + new Vector2(cardWidth, height), Theme.Pack(Theme.SurfaceBorder), Radius);
      if (topLevel)
        draw.ChannelsMerge();
      else
        draw.ChannelsSetCurrent(2);
      ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
      ImGui.Dummy(new Vector2(cardWidth, MathF.Max(0, gap - ImGui.GetStyle().ItemSpacing.Y)));
    }
    finally
    {
      _cardDepth--;
      _rowsInCard = savedRows;
      _trailPad = savedTrail;
    }
  }

  /// <summary>Card title with an optional one-line description. Resets the row hairline state.</summary>
  internal static void CardHeader(string title, string? description = null, string? meta = null)
  {
    var pos = ImGui.GetCursorScreenPos();
    var width = Avail();
    var draw = ImGui.GetWindowDrawList();
    var titleH = LineHeight(Theme.TitleScale);
    DrawText(draw, pos, FitText(title, meta is null ? width : width - TextWidth(meta, Theme.SecondaryScale) - 16, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
    if (meta is not null)
      DrawTextRight(draw, pos.X + width, pos.Y + ((titleH - LineHeight(Theme.SecondaryScale)) / 2f), meta, Theme.TextSecondary, Theme.SecondaryScale);
    ImGui.SetCursorScreenPos(pos);
    ImGui.Dummy(new Vector2(width, titleH));
    if (!string.IsNullOrEmpty(description))
      Secondary(description);
    ImGui.Dummy(new Vector2(0, 8 - ImGui.GetStyle().ItemSpacing.Y + 4));
    _rowsInCard = 0;
    _trailPad = 0;
  }

  internal static void Secondary(string text)
  {
    ImGui.SetWindowFontScale(Theme.SecondaryScale);
    ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
    ImGui.TextWrapped(text);
    ImGui.PopStyleColor();
    ImGui.SetWindowFontScale(1f);
    _trailPad = 0;
  }

  /// <summary>One line of text in a state's mark colour.</summary>
  internal static void StateText(string text, UiState state)
  {
    ImGui.PushStyleColor(ImGuiCol.Text, Copy.MarkColor(state));
    ImGui.TextUnformatted(text);
    ImGui.PopStyleColor();
  }

  /// <summary>Page-level section heading outside a card.</summary>
  internal static void Section(string title, string? description = null, string? count = null)
  {
    CardHeader(title, description, count);
  }

  internal static void Columns(string id, Action main, Action rail, float breakpoint = 860)
  {
    var avail = Avail();
    if (avail < breakpoint)
    {
      main();
      rail();
      return;
    }
    var railWidth = Math.Clamp(avail * 0.30f, 300f, 360f);
    var mainWidth = avail - railWidth - 16;
    var origin = ImGui.GetCursorScreenPos();
    ColumnHeights.TryGetValue(id, out var prev);
    var target = MathF.Max(prev.Main, prev.Rail);

    _fillExtra = MathF.Max(0, target - prev.Main);
    _fillStretch = 0;
    PushRight(origin.X + mainWidth);
    ImGui.BeginGroup();
    main();
    ImGui.EndGroup();
    PopRight();
    var mainBottom = ImGui.GetItemRectMax().Y - origin.Y - _fillStretch;

    _fillExtra = MathF.Max(0, target - prev.Rail);
    _fillStretch = 0;
    ImGui.SetCursorScreenPos(new Vector2(origin.X + mainWidth + 16, origin.Y));
    PushRight(origin.X + avail);
    ImGui.BeginGroup();
    rail();
    ImGui.EndGroup();
    PopRight();
    var railBottom = ImGui.GetItemRectMax().Y - origin.Y - _fillStretch;
    _fillExtra = 0;
    ColumnHeights[id] = (mainBottom, railBottom);
    var stretchedBottom = origin.Y + MathF.Max(mainBottom, railBottom);
    ImGui.SetCursorScreenPos(new Vector2(origin.X, stretchedBottom));
    ImGui.Dummy(new Vector2(avail, 0));
  }

  // =====================================================================================
  // rows
  // =====================================================================================

  private static float BeginRow(Vector2 pos, float width, out float topPad)
  {
    var first = _rowsInCard == 0;
    topPad = first ? 0 : RowPad;
    if (!first)
      ImGui.GetWindowDrawList().AddLine(pos, pos + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder with { W = 0.7f }));
    _rowsInCard++;
    return topPad;
  }

  private static void EndRow(Vector2 pos, float width, float rowHeight)
  {
    ImGui.SetCursorScreenPos(pos);
    ImGui.Dummy(new Vector2(width, rowHeight - ImGui.GetStyle().ItemSpacing.Y));
    _trailPad = RowPad;
  }

  /// <summary>Ends a run of rows inside a card (use before free-form content such as a table).</summary>
  internal static void RowsDone()
  {
    _rowsInCard = 0;
    _trailPad = 0;
  }

  /// <summary>A pink track toggle with its label on the left, switch right edge on the inner edge of the surface. Compact variant for table cells when the label is empty.</summary>
  internal static bool Toggle(string literalLabel, ref bool value)
    => ToggleRow(literalLabel, ref value, null, 0);

  /// <summary>Toggle row with an extra control (for example a key picker) drawn to the left of the switch on the same row.</summary>
  internal static bool ToggleWith(string literalLabel, ref bool value, float extraWidth, Func<float, bool>? extra)
    => ToggleRow(literalLabel, ref value, extra, extraWidth);

  private static bool ToggleRow(string literalLabel, ref bool value, Func<float, bool>? extra, float extraWidth)
  {
    var separator = literalLabel.IndexOf("##", StringComparison.Ordinal);
    var label = separator < 0 ? literalLabel : literalLabel[..separator];
    if (label.Length == 0)
      return CellToggle(literalLabel, ref value);

    var pos = ImGui.GetCursorScreenPos();
    var width = Avail();
    var extraRoom = extra is null ? 0 : extraWidth + 16;
    var textW = MathF.Max(40, width - ToggleSize.X - 16 - extraRoom);
    var labelH = WrappedHeight(label, textW);
    var contentH = MathF.Max(extra is null ? ToggleSize.Y : Control, labelH);
    var first = _rowsInCard == 0;
    var top = BeginRow(pos, width, out _);
    var rowH = top + contentH + RowPad;
    var y = pos.Y + top;

    ImGui.SetCursorScreenPos(new Vector2(pos.X, y + ((contentH - labelH) / 2f)));
    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textW);
    ImGui.TextUnformatted(label);
    ImGui.PopTextWrapPos();

    var clicked = false;
    if (extra is not null)
    {
      ImGui.SetCursorScreenPos(new Vector2(pos.X + width - ToggleSize.X - 16 - extraWidth, y + ((contentH - Control) / 2f)));
      extra(extraWidth);
    }
    var switchPos = new Vector2(pos.X + width - ToggleSize.X, y + ((contentH - ToggleSize.Y) / 2f));
    ImGui.SetCursorScreenPos(switchPos);
    clicked = DrawTrack(literalLabel, switchPos, ToggleSize, ref value);
    _ = first;
    EndRow(pos, width, rowH);
    return clicked;
  }

  private static bool CellToggle(string id, ref bool value)
  {
    var pos = ImGui.GetCursorScreenPos() + new Vector2(0, MathF.Max(0, (Control - ToggleSize.Y) / 2f));
    var clicked = DrawTrack(id, pos, ToggleSize, ref value);
    ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y - MathF.Max(0, (Control - ToggleSize.Y) / 2f)));
    ImGui.Dummy(new Vector2(ToggleSize.X, Control));
    return clicked;
  }

  private static bool DrawTrack(string id, Vector2 pos, Vector2 size, ref bool value)
  {
    ImGui.SetCursorScreenPos(pos);
    var clicked = ImGui.InvisibleButton(id, size);
    if (clicked)
      value = !value;
    var hovered = ImGui.IsItemHovered();
    var draw = ImGui.GetWindowDrawList();
    draw.AddRectFilled(pos, pos + size, A(value ? Theme.Accent : Theme.SurfaceBorder), size.Y / 2f);
    if (hovered)
      draw.AddRect(pos, pos + size, A(Theme.Accent), size.Y / 2f);
    var radius = (size.Y / 2f) - 4f;
    var centerX = value ? pos.X + size.X - (size.Y / 2f) : pos.X + (size.Y / 2f);
    draw.AddCircleFilled(new Vector2(centerX, pos.Y + (size.Y / 2f)), radius, A(value ? Theme.Surface : Theme.TextSecondary));
    return clicked;
  }

  /// <summary>A row with a one or two line text block on the left and a button on the right. Returns true when the button was pressed.</summary>
  internal static bool ActionRow(string title, string? sub, string buttonLabel, string keepSuffix = "")
  {
    var pos = ImGui.GetCursorScreenPos();
    var width = Avail();
    var buttonW = ButtonWidth(buttonLabel.Contains("##", StringComparison.Ordinal) ? buttonLabel[..buttonLabel.IndexOf("##", StringComparison.Ordinal)] : buttonLabel);
    var textW = MathF.Max(40, width - buttonW - 16);
    var titleH = LineHeight();
    var subH = sub is null ? 0 : LineHeight(Theme.SecondaryScale);
    var block = titleH + (sub is null ? 0 : 2 + subH);
    var contentH = MathF.Max(Control, block);
    var top = BeginRow(pos, width, out _);
    var rowH = top + contentH + RowPad;
    var y = pos.Y + top + ((contentH - block) / 2f);
    var draw = ImGui.GetWindowDrawList();
    var shownTitle = FitTextKeepSuffix(title, keepSuffix, textW);
    DrawText(draw, new Vector2(pos.X, y), shownTitle, Theme.TextPrimary);
    var shownSub = sub is null ? string.Empty : FitText(sub, textW, Theme.SecondaryScale);
    if (sub is not null)
      DrawText(draw, new Vector2(pos.X, y + titleH + 2), shownSub, Theme.TextSecondary, Theme.SecondaryScale);
    ImGui.SetCursorScreenPos(new Vector2(pos.X + width - buttonW, pos.Y + top + ((contentH - Control) / 2f)));
    var clicked = ImGui.Button(buttonLabel, new Vector2(buttonW, Control));
    var textMin = new Vector2(pos.X, y);
    var textMax = new Vector2(pos.X + textW, y + block);
    if ((shownTitle != title || (sub is not null && shownSub != sub)) && ImGui.IsMouseHoveringRect(textMin, textMax))
      ImGui.SetTooltip(sub is null ? title : title + "\n" + sub);
    EndRow(pos, width, rowH);
    return clicked;
  }

  /// <summary>Lays out count cells in a grid of equal columns; each cell is drawn as a row of its own.</summary>
  internal static void Grid(int count, int columns, Action<int> cell)
  {
    var avail = Avail();
    var origin = ImGui.GetCursorScreenPos();
    const float gap = 24f;
    columns = Math.Max(1, columns);
    var cellWidth = (avail - (gap * (columns - 1))) / columns;
    var y = origin.Y;
    for (var start = 0; start < count; start += columns)
    {
      var bottom = y;
      for (var col = 0; col < columns && start + col < count; col++)
      {
        var x = origin.X + (col * (cellWidth + gap));
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        PushRight(x + cellWidth);
        _rowsInCard = start == 0 ? 0 : 1;
        cell(start + col);
        PopRight();
        bottom = MathF.Max(bottom, ImGui.GetCursorScreenPos().Y);
      }
      y = bottom;
    }
    ImGui.SetCursorScreenPos(new Vector2(origin.X, y));
  }

  /// <summary>Content set in from the left with a thin rule, for a sub-setting that belongs to the row above.</summary>
  internal static void Indented(Action content)
  {
    var pos = ImGui.GetCursorScreenPos();
    var avail = Avail();
    ImGui.SetCursorScreenPos(pos + new Vector2(16, 0));
    ImGui.BeginGroup();
    content();
    ImGui.EndGroup();
    var bottom = ImGui.GetItemRectMax().Y;
    ImGui.GetWindowDrawList().AddLine(new Vector2(pos.X + 4, pos.Y + 4), new Vector2(pos.X + 4, bottom - _trailPad), Theme.Pack(Theme.Accent with { W = 0.5f }), 2f);
    ImGui.SetCursorScreenPos(new Vector2(pos.X, bottom));
    ImGui.Dummy(new Vector2(avail, 0));
    _trailPad = RowPad;
  }

  internal static float ControlWidth(float avail) => Math.Clamp(avail * 0.45f, 200f, 320f);

  /// <summary>One row model for every labelled control: label left, control right at a shared width (stacked below the label when the row is too narrow).</summary>
  internal static bool Field(string label, Func<float, bool> control, string? unit = null, float? width = null)
  {
    var pos = ImGui.GetCursorScreenPos();
    var avail = Avail();
    var cw = Math.Min(width ?? ControlWidth(avail), avail);
    var unitW = unit is null ? 0 : TextWidth(unit) + 8;
    var labelW = TextWidth(label);
    var stacked = labelW + cw + 24 > avail;
    var top = BeginRow(pos, avail, out _);
    var y = pos.Y + top;
    var changed = false;
    float rowH;
    if (!stacked)
    {
      rowH = top + Control + RowPad;
      ImGui.SetCursorScreenPos(new Vector2(pos.X, y + ((Control - LineHeight()) / 2f)));
      ImGui.TextUnformatted(label);
      var controlX = pos.X + avail - cw;
      ImGui.SetCursorScreenPos(new Vector2(controlX, y));
      changed = control(MathF.Max(40f, cw - unitW));
      if (unit is not null)
        DrawText(ImGui.GetWindowDrawList(), new Vector2(controlX + cw - unitW + 8, y + ((Control - LineHeight()) / 2f)), unit, Theme.TextSecondary);
    }
    else
    {
      var labelH = WrappedHeight(label, avail);
      rowH = top + labelH + 6 + Control + RowPad;
      ImGui.SetCursorScreenPos(new Vector2(pos.X, y));
      ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + avail);
      ImGui.TextUnformatted(label);
      ImGui.PopTextWrapPos();
      var cy = y + labelH + 6;
      ImGui.SetCursorScreenPos(new Vector2(pos.X, cy));
      changed = control(MathF.Max(40f, avail - unitW));
      if (unit is not null)
        DrawText(ImGui.GetWindowDrawList(), new Vector2(pos.X + avail - unitW + 8, cy + ((Control - LineHeight()) / 2f)), unit, Theme.TextSecondary);
    }
    EndRow(pos, avail, rowH);
    return changed;
  }

  internal static bool Combo(string id, ref int index, string[] items, float width)
  {
    var draw = ImGui.GetWindowDrawList();
    ImGui.SetNextItemWidth(width);
    var shown = items.Length == 0 ? string.Empty : FitText(items[Math.Clamp(index, 0, items.Length - 1)], width - 24 - 20);
    var open = ImGui.BeginCombo(id, shown, ImGuiComboFlags.NoArrowButton);
    var min = ImGui.GetItemRectMin();
    var max = ImGui.GetItemRectMax();
    var cx = max.X - 20f;
    var cy = (min.Y + max.Y) / 2f;
    var col = Theme.Pack(Theme.TextSecondary);
    draw.AddLine(new Vector2(cx - 5, cy - 2), new Vector2(cx, cy + 3), col, 1.5f);
    draw.AddLine(new Vector2(cx, cy + 3), new Vector2(cx + 5, cy - 2), col, 1.5f);
    var changed = false;
    if (open)
    {
      for (var i = 0; i < items.Length; i++)
      {
        if (ImGui.Selectable(items[i] + "##opt" + i, i == index))
        {
          index = i;
          changed = true;
        }
      }
      ImGui.EndCombo();
    }
    return changed;
  }

  internal static bool IntInput(string id, ref int value, float width, int step = 1)
  {
    ImGui.SetNextItemWidth(width);
    return ImGui.InputInt(id, ref value, step, step);
  }

  /// <summary>Slider with its value drawn at the right end of the control column (after the track).</summary>
  internal static bool SliderInt(string id, ref int value, int min, int max, float width, string suffix = "")
  {
    var valueW = TextWidth(max.ToString() + suffix) + 12;
    ImGui.SetNextItemWidth(MathF.Max(40, width - valueW));
    var pos = ImGui.GetCursorScreenPos();
    var changed = ImGui.SliderInt(id, ref value, min, max, "");
    var text = value + suffix;
    DrawText(ImGui.GetWindowDrawList(), new Vector2(pos.X + width - TextWidth(text), pos.Y + ((Control - LineHeight()) / 2f)), text, Theme.TextPrimary);
    return changed;
  }

  internal static bool SliderFloat(string id, ref float value, float min, float max, float width, string format = "0.0", string suffix = "")
  {
    var valueW = TextWidth(max.ToString(format, System.Globalization.CultureInfo.InvariantCulture) + suffix) + 12;
    ImGui.SetNextItemWidth(MathF.Max(40, width - valueW));
    var pos = ImGui.GetCursorScreenPos();
    var changed = ImGui.SliderFloat(id, ref value, min, max, "");
    var text = value.ToString(format, System.Globalization.CultureInfo.InvariantCulture) + suffix;
    DrawText(ImGui.GetWindowDrawList(), new Vector2(pos.X + width - TextWidth(text), pos.Y + ((Control - LineHeight()) / 2f)), text, Theme.TextPrimary);
    return changed;
  }

  // =====================================================================================
  // buttons
  // =====================================================================================

  internal static bool Button(string label) => Button(label, 0f);

  internal static bool Button(string label, float width, float height = 0f)
  {
    var shown = label.Contains("##", StringComparison.Ordinal) ? label[..label.IndexOf("##", StringComparison.Ordinal)] : label;
    var w = width > 0 ? width : TextWidth(shown) + 32;
    return ImGui.Button(label, new Vector2(w, height > 0 ? height : 0f));
  }

  internal static float ButtonWidth(string label) => TextWidth(label) + 32;

  /// <summary>Red-tinted Stop button with white text.</summary>
  internal static bool StopButton(string label, Vector2? size = null)
  {
    ImGui.PushStyleColor(ImGuiCol.Button, Theme.StateRed with { W = 0.12f });
    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.StateRed with { W = 0.24f });
    ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.StateRed with { W = 0.34f });
    ImGui.PushStyleColor(ImGuiCol.Border, Theme.StateRed with { W = 0.55f });
    ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
    var clicked = ImGui.Button(label, size ?? new Vector2(MathF.Max(72, TextWidth(label) + 32), 0));
    ImGui.PopStyleColor(5);
    return clicked;
  }

  /// <summary>Accent-filled primary button for confirmations.</summary>
  internal static bool PrimaryButton(string label, Vector2 size)
  {
    ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentHover);
    ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentActive);
    ImGui.PushStyleColor(ImGuiCol.Border, Theme.Accent);
    ImGui.PushStyleColor(ImGuiCol.Text, Theme.ShellBar);
    var clicked = ImGui.Button(label, size);
    ImGui.PopStyleColor(5);
    return clicked;
  }

  /// <summary>A non-interactive outlined tag.</summary>
  internal static void Tag(string text)
  {
    var size = new Vector2(TextWidth(text) + 32, Control);
    var pos = ImGui.GetCursorScreenPos();
    var draw = ImGui.GetWindowDrawList();
    draw.AddRect(pos, pos + size, A(Theme.SurfaceBorder), Radius);
    DrawText(draw, pos + new Vector2(16, (Control - LineHeight()) / 2f), text, Theme.TextSecondary);
    ImGui.Dummy(size);
  }

  // =====================================================================================
  // notices
  // =====================================================================================

  /// <summary>Rounded notice. "Lead · body" renders the lead in primary text with the body beneath it.</summary>
  internal static void Notice(string text, UiState state = UiState.Idle)
  {
    var split = text.IndexOf(" · ", StringComparison.Ordinal);
    var lead = split > 0 ? text[..split] : text;
    var body = split > 0 ? text[(split + 3)..] : string.Empty;
    var pos = ImGui.GetCursorScreenPos();
    var width = Avail();
    var textW = width - 34 - 16;
    var leadH = WrappedHeight(lead, textW);
    var bodyH = body.Length > 0 ? WrappedHeight(body, textW, Theme.SecondaryScale) : 0;
    var height = 12 + leadH + (body.Length > 0 ? 4 + bodyH : 0) + 12;
    var alert = state is UiState.Failed or UiState.Blocked or UiState.Running or UiState.Missing;
    var mark = Copy.MarkColor(state);
    var draw = ImGui.GetWindowDrawList();
    draw.AddRectFilled(pos, pos + new Vector2(width, height), A(alert ? mark with { W = 0.12f } : Theme.ShellBar), Radius);
    draw.AddRect(pos, pos + new Vector2(width, height), A(alert ? mark with { W = 0.45f } : Theme.SurfaceBorder), Radius);
    draw.AddCircleFilled(new Vector2(pos.X + 16, pos.Y + 12 + (LineHeight() / 2f)), Theme.DotRadius, A(mark));
    ImGui.SetCursorScreenPos(new Vector2(pos.X + 34, pos.Y + 12));
    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textW);
    ImGui.TextUnformatted(lead);
    ImGui.PopTextWrapPos();
    if (body.Length > 0)
    {
      ImGui.SetCursorScreenPos(new Vector2(pos.X + 34, pos.Y + 12 + leadH + 4));
      ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textW);
      Secondary(body);
      ImGui.PopTextWrapPos();
    }
    ImGui.SetCursorScreenPos(pos);
    ImGui.Dummy(new Vector2(width, height - ImGui.GetStyle().ItemSpacing.Y + 8));
    _trailPad = 0;
  }

  // =====================================================================================
  // stat cards
  // =====================================================================================

  internal readonly record struct Stat(string Label, string Value, string Note, string? Cap = null, float? Fraction = null);

  internal static void StatCards(params Stat[] stats)
  {
    if (stats.Length == 0)
      return;
    var available = Avail();
    var tier = available >= 692 ? 0 : available >= 480 ? 1 : 2;
    var gap = tier == 0 ? 16f : 12f;
    var columns = stats.Length;
    var width = (available - ((columns - 1) * gap)) / columns;
    var pad = tier == 0 ? 16f : tier == 1 ? 12f : 10f;
    var valueScale = tier == 0 ? 1.7f : tier == 1 ? 1.4f : 1.25f;
    var showNote = tier < 2;
    var labelH = LineHeight(Theme.SecondaryScale);
    var valueH = LineHeight(valueScale);
    var noteMax = tier == 0 ? 1 : 2;
    var noteLines = showNote ? stats.Max(stat => WrapText(stat.Note, width - (2 * pad), Theme.SecondaryScale, noteMax).Count) : 0;
    var height = pad + labelH + (tier == 0 ? 8 : 4) + valueH + (tier == 0 ? 8 : 4) + Theme.BarHeight + (showNote ? (tier == 0 ? 8 : 4) + (noteLines * labelH) : 0) + pad;
    var origin = ImGui.GetCursorScreenPos();
    var draw = ImGui.GetWindowDrawList();
    for (var i = 0; i < stats.Length; i++)
    {
      var stat = stats[i];
      var pos = origin + new Vector2(i * (width + gap), 0);
      draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), Radius);
      draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), Radius);
      var y = pos.Y + pad;
      Diamond(draw, new Vector2(pos.X + pad + 5, y + (labelH / 2f)), 5, Theme.Pack(Theme.Accent));
      DrawText(draw, new Vector2(pos.X + pad + 16, y), FitText(stat.Label, width - (2 * pad) - 16, Theme.SecondaryScale), Theme.TextSecondary, Theme.SecondaryScale);
      y += labelH + (tier == 0 ? 8 : 4);
      var value = FitText(stat.Value, width - (2 * pad), valueScale);
      DrawText(draw, new Vector2(pos.X + pad, y), value, Theme.TextPrimary, valueScale);
      if (stat.Cap is { Length: > 0 } cap)
      {
        var capX = pos.X + pad + TextWidth(value, valueScale) + 8;
        var capShown = FitText(cap, pos.X + width - pad - capX, Theme.SecondaryScale);
        DrawText(draw, new Vector2(capX, y + valueH - labelH - (0.08f * valueH)), capShown, Theme.TextSecondary, Theme.SecondaryScale);
      }
      y += valueH + (tier == 0 ? 8 : 4);
      if (stat.Fraction is { } fraction)
      {
        var min = new Vector2(pos.X + pad, y);
        var max = new Vector2(pos.X + width - pad, y + Theme.BarHeight);
        draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), 2f);
        if (fraction > 0)
          draw.AddRectFilled(min, new Vector2(min.X + ((max.X - min.X) * Math.Clamp(fraction, 0, 1)), max.Y), Theme.Pack(Theme.StateGreen), 2f);
      }
      y += Theme.BarHeight;
      if (showNote)
      {
        y += tier == 0 ? 8 : 4;
        var done = stat.Fraction is >= 1f;
        foreach (var noteLine in WrapText(stat.Note, width - (2 * pad), Theme.SecondaryScale, noteMax))
        {
          DrawText(draw, new Vector2(pos.X + pad, y), noteLine, done ? Theme.StateGreen : Theme.TextSecondary, Theme.SecondaryScale);
          y += labelH;
        }
      }
    }
    ImGui.SetCursorScreenPos(origin);
    ImGui.Dummy(new Vector2(available, height - ImGui.GetStyle().ItemSpacing.Y + 16));
    _trailPad = 0;
  }

  // =====================================================================================
  // tables
  // =====================================================================================

  internal static bool BeginTable(string id, int columns, ImGuiTableFlags extra = ImGuiTableFlags.None, Vector2 outer = default)
  {
    var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchProp | extra;
    return ImGui.BeginTable(id, columns, flags, outer);
  }

  /// <summary>
  /// Height for a long table that keeps its own header row frozen while the rows scroll, sized to the visible body.
  /// Returns 0 when every row fits in that space, so short tables stay in the page flow with no inner scroll.
  /// </summary>
  internal static float FrozenTableHeight(int rows, float rowHeight = 0f)
  {
    if (rowHeight <= 0f)
      rowHeight = ImGui.GetFrameHeight() + (ImGui.GetStyle().CellPadding.Y * 2f) + 1f;
    var cap = Math.Clamp(ImGui.GetWindowHeight() - 140f, 260f, 520f);
    return (rows + 1) * rowHeight <= cap ? 0f : cap;
  }

  /// <summary>Header row on a subtle band; numeric columns right-aligned.</summary>
  internal static void TableHeader(params (string Label, bool Right)[] columns)
  {
    ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, Theme.Pack(Theme.ShellBar));
    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, Theme.Pack(Theme.ShellBar));
    for (var i = 0; i < columns.Length; i++)
    {
      ImGui.TableSetColumnIndex(i);
      var (label, right) = columns[i];
      if (right)
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, ImGui.GetContentRegionAvail().X - TextWidth(label, Theme.SecondaryScale)));
      ImGui.SetWindowFontScale(Theme.SecondaryScale);
      ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
      ImGui.TextUnformatted(label);
      ImGui.PopStyleColor();
      ImGui.SetWindowFontScale(1f);
    }
  }

  /// <summary>Right-aligned cell text.</summary>
  internal static void CellRight(string text, Vector4? color = null)
  {
    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, ImGui.GetContentRegionAvail().X - TextWidth(text)));
    if (color is { } c)
      ImGui.PushStyleColor(ImGuiCol.Text, c);
    ImGui.TextUnformatted(text);
    if (color is not null)
      ImGui.PopStyleColor();
  }

  /// <summary>Cell text that is elided to the column and shows the full text on hover.</summary>
  internal static void CellText(string text, Vector4? color = null, string keepSuffix = "")
  {
    var room = ImGui.GetContentRegionAvail().X;
    var shown = FitTextKeepSuffix(text, keepSuffix, room);
    if (color is { } c)
      ImGui.PushStyleColor(ImGuiCol.Text, c);
    ImGui.TextUnformatted(shown);
    if (color is not null)
      ImGui.PopStyleColor();
    if (shown != text && ImGui.IsItemHovered())
      ImGui.SetTooltip(text);
  }

  // =====================================================================================
  // modal
  // =====================================================================================

  internal const ImGuiWindowFlags ModalFlags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings;

  /// <summary>Call right before ImGui.BeginPopupModal: dims the host window while the modal is up, centres the modal in it and pushes its frame styles.</summary>
  internal static void PushModalStyle(string name)
  {
    var win = ImGui.GetWindowPos();
    var size = ImGui.GetWindowSize();
    if (ImGui.IsPopupOpen(name))
      ImGui.GetWindowDrawList().AddRectFilled(win, win + size, Theme.Pack(new Vector4(0f, 0f, 0f, 0.55f)));
    ImGui.SetNextWindowPos(win + (size / 2f), ImGuiCond.Always, new Vector2(0.5f));
    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 20));
    ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Radius);
    ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
    ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
    ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
    ImGui.PushStyleColor(ImGuiCol.WindowBg, Theme.Surface);
    ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
  }

  /// <summary>Call with the result of BeginPopupModal: pops the styles and, when the modal is open, sets up its content width and title. Pair an open result with EndModal.</summary>
  internal static bool ModalBegan(bool open, string title, float contentWidth)
  {
    ImGui.PopStyleColor(3);
    ImGui.PopStyleVar(4);
    if (!open)
      return false;
    var size = ImGui.GetMainViewport().Size;
    var w = MathF.Max(200, MathF.Min(contentWidth, size.X - 40));
    PushRight(ImGui.GetCursorScreenPos().X + w);
    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + w);
    ImGui.Dummy(new Vector2(w, 0));
    ImGui.SetWindowFontScale(Theme.TitleScale);
    ImGui.TextUnformatted(title);
    ImGui.SetWindowFontScale(1f);
    ImGui.Dummy(new Vector2(0, 4));
    return true;
  }

  internal static void EndModal()
  {
    ImGui.PopTextWrapPos();
    PopRight();
    ImGui.EndPopup();
  }

  /// <summary>Primary and cancel buttons side by side, each half the width. Returns 1 for primary, 2 for cancel, 0 for neither.</summary>
  internal static int ModalButtons(string primary, string cancel, bool destructive)
  {
    var w = Avail();
    var each = (w - Gap) / 2f;
    var result = 0;
    ImGui.Dummy(new Vector2(0, 4));
    var size = new Vector2(each, Control);
    var clicked = destructive ? StopButton(primary, size) : PrimaryButton(primary, size);
    if (clicked)
      result = 1;
    ImGui.SameLine(0, Gap);
    if (ImGui.Button(cancel, size))
      result = 2;
    return result;
  }
}
