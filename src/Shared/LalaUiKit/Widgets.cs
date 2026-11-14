// LalaUiKit/Widgets.cs — pills, status circles, cards, info boxes, progress bars,
// banners and icon tiles (Brief "Kit shape", R4, R5, R6, R8).

namespace LalaUiKit;

using System;
using System.Collections.Generic;
using System.Linq;

using System.Numerics;
using Dalamud.Bindings.ImGui;

public static class Widgets
{
    // ---- text helpers (R7: the only way converted windows set a size) ----

    /// <summary>Title text: 1.15× base font. Nothing else may draw a bigger line.</summary>
    public static void Title(string text)
    {
        ImGui.SetWindowFontScale(Theme.TitleScale);
        ImGui.TextUnformatted(text);
        ImGui.SetWindowFontScale(1f);
    }

    public static void Body(string text) => ImGui.TextUnformatted(text);

    public static void Secondary(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    public static void Caption(string text)
    {
        ImGui.SetWindowFontScale(Theme.CaptionScale);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        ImGui.SetWindowFontScale(1f);
    }

    public static void SecondaryWrapped(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    // ---- status marks ----

    /// <summary>Small filled state circle (nav dots, inline marks). Red appears only on Failed (R4/R5).</summary>
    public static void StatusDot(UiState state, float radius = Theme.DotRadius)
    {
        var p = ImGui.GetCursorScreenPos();
        var lineH = ImGui.GetTextLineHeight();
        var center = new Vector2(p.X + radius, p.Y + (lineH / 2f));
        ImGui.GetWindowDrawList().AddCircleFilled(center, radius, Theme.Pack(Copy.MarkColor(state)));
        ImGui.Dummy(new Vector2(radius * 2f, lineH));
    }

    /// <summary>
    ///     Live-state pill for the Shell top bar (R3 place 1): dot + state word + optional
    ///     detail (feature name, timer). Draws at the cursor; alignRight pins it to the
    ///     window's right edge.
    /// </summary>
    public static void StatusPill(UiState state, string feature, string? detail = null, bool alignRight = false)
    {
        var word = Copy.Word(state);
        var text = string.IsNullOrEmpty(detail) ? $"{feature} · {word}" : $"{feature} · {word} · {detail}";
        var fs = ImGui.GetFontSize();
        var textW = ImGui.CalcTextSize(text).X;
        var padX = 2 * Theme.Unit;
        var w = padX + (Theme.DotRadius * 2) + Theme.Unit + textW + padX;
        var h = Theme.PillHeight;
        var p = ImGui.GetCursorScreenPos();
        if (alignRight)
            p = new Vector2(p.X + ImGui.GetWindowWidth() - w, p.Y);
        ImGui.GetWindowDrawList().AddRectFilled(p, p + new Vector2(w, h), Theme.Pack(new Vector4(1f, 1f, 1f, 0.08f)), h / 2f);
        ImGui.GetWindowDrawList().AddRect(p, p + new Vector2(w, h), Theme.Pack(Theme.SurfaceBorder), h / 2f);
        var dotC = new Vector2(p.X + padX + Theme.DotRadius, p.Y + (h / 2f));
        ImGui.GetWindowDrawList().AddCircleFilled(dotC, Theme.DotRadius, Theme.Pack(Copy.MarkColor(state)));
        ImGui.GetWindowDrawList().AddText(
            new Vector2(p.X + padX + (Theme.DotRadius * 2) + Theme.Unit, p.Y + ((h - fs) / 2f)),
            Theme.Pack(Theme.TextPrimary), text);
        ImGui.Dummy(new Vector2(w, h));
    }

    // ---- number card (R6) ----

    /// <summary>
    ///     Stat card (R6): label, current value, cap if one exists, thin progress bar, one
    ///     delta line that answers "what changed and why". Fixed width + shared minimum
    ///     height so card grids never show dead space under a short card (flaw → R6+R9).
    /// </summary>
    public static void StatCard(string label, string value, string? cap, float? frac, string? delta, float width)
    {
        var p = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, Theme.CardMinHeight);
        ImGui.GetWindowDrawList().AddRectFilled(p, p + size, Theme.Pack(Theme.Surface), 4f);
        ImGui.GetWindowDrawList().AddRect(p, p + size, Theme.Pack(Theme.SurfaceBorder), 4f);

        ImGui.BeginGroup();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(Theme.Unit, Theme.Unit));
        Caption(label);
        Title(cap is null ? value : $"{value} / {cap}");
        if (frac is { } f)
            ProgressBar(f);
        else
            ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, Theme.BarHeight));
        if (!string.IsNullOrEmpty(delta))
            Caption(delta);
        ImGui.PopStyleVar();
        ImGui.EndGroup();
        ImGui.Dummy(new Vector2(width, 0)); // hold the reserved card width
        ImGui.SameLine();
    }

    // ---- info box / banner ----

    /// <summary>Framed box with a state-coloured left edge and secondary body text.</summary>
    public static void InfoBox(string text, UiState tone = UiState.Idle)
    {
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;
        var lineH = ImGui.GetTextLineHeight();
        var pad = 2 * Theme.Unit;
        var textH = ImGui.CalcTextSize(text, false, w - (pad * 2) - Theme.Unit).Y;
        var h = (pad * 2) + MathF.Max(textH, lineH);
        ImGui.GetWindowDrawList().AddRectFilled(p, p + new Vector2(w, h), Theme.Pack(Theme.Surface), 4f);
        ImGui.GetWindowDrawList().AddRectFilled(p, p + new Vector2(2f, h), Theme.Pack(Copy.MarkColor(tone)));
        ImGui.GetWindowDrawList().AddRect(p, p + new Vector2(w, h), Theme.Pack(Theme.SurfaceBorder), 4f);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(pad, pad));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + w - (pad * 2));
        SecondaryWrapped(text);
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(w, h - (pad * 2) - textH + Theme.Unit));
    }

    /// <summary>
    ///     One-line announcement (R3): finished, failed and stopped are announced once,
    ///     here, with a one-line reason — never silently.
    /// </summary>
    public static void Banner(UiState state, string headline, string? detail = null)
    {
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;
        var h = Theme.PillHeight;
        ImGui.GetWindowDrawList().AddRectFilled(p, p + new Vector2(w, h), Theme.Pack(Copy.MarkColor(state) with { W = 0.14f }), 4f);
        ImGui.GetWindowDrawList().AddRect(p, p + new Vector2(w, h), Theme.Pack(Theme.SurfaceBorder), 4f);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(2 * Theme.Unit, (h - ImGui.GetTextLineHeight()) / 2f));
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
        ImGui.TextUnformatted(string.IsNullOrEmpty(detail) ? headline : $"{headline} — {detail}");
        ImGui.PopStyleColor();
        ImGui.Dummy(new Vector2(w, h));
    }

    // ---- progress bar ----

    /// <summary>
    ///     Thin bar (4 px). Data visualisation uses accent + state colours only (R4): pass
    ///     a state colour for state-bound progress, the default accent is active progress.
    /// </summary>
    public static void ProgressBar(float frac, Vector4? color = null)
    {
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;
        frac = Math.Clamp(frac, 0f, 1f);
        ImGui.GetWindowDrawList().AddRectFilled(p, p + new Vector2(w, Theme.BarHeight), Theme.Pack(new Vector4(1f, 1f, 1f, 0.10f)));
        if (frac > 0f)
            ImGui.GetWindowDrawList().AddRectFilled(p, p + new Vector2(w * frac, Theme.BarHeight), Theme.Pack(color ?? Theme.Accent));
        ImGui.Dummy(new Vector2(w, Theme.BarHeight));
    }

    // ---- icon tile (R4: the accent outline marks exactly the selected item) ----

    /// <summary>
    ///     Square tile holding an icon (or a glyph fallback). The accent outline is drawn
    ///     only when selected — one outline, one meaning: this item is the selected one.
    /// </summary>
    public static void IconTile(ImTextureID? icon, string? glyph, Vector2 size, bool selected)
    {
        var p = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(p, p + size, Theme.Pack(Theme.Surface), 4f);
        if (icon is { } tex)
        {
            ImGui.Image(tex, size);
        }
        else if (!string.IsNullOrEmpty(glyph))
        {
            var gs = ImGui.CalcTextSize(glyph);
            ImGui.GetWindowDrawList().AddText(
                p + ((size - gs) / 2f),
                Theme.Pack(selected ? Theme.Accent : Theme.TextSecondary), glyph);
        }

        if (selected)
            ImGui.GetWindowDrawList().AddRect(p, p + size, Theme.Pack(Theme.Accent), 4f, 0, 2f);
        ImGui.Dummy(size);
        ImGui.SameLine();
    }

    // ---- interactive controls (R4: accent marks the "on" state) ----

    /// <summary>Checkbox whose check mark is the accent — the "on" colour (R4).</summary>
    public static bool Checkbox(string label, ref bool value)
    {
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Theme.Accent);
        var hit = ImGui.Checkbox(label, ref value);
        ImGui.PopStyleColor();
        return hit;
    }

    /// <summary>Percent slider with accent grab handle (R4).</summary>
    public static bool SliderPercent(string label, ref float value, float min = 1f, float max = 99f)
    {
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Theme.AccentActive);
        var hit = ImGui.SliderFloat(label, ref value, min, max, "%.0f%%");
        ImGui.PopStyleColor(2);
        return hit;
    }

    /// <summary>Stop button (R3 footer): red is reserved for exactly this (R4).</summary>
    public static bool StopButton(string label = "Stop")
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.StateRed);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.StateRed with { W = 0.85f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.StateRed with { W = 0.70f });
        var hit = ImGui.Button(label);
        ImGui.PopStyleColor(3);
        return hit;
    }

    /// <summary>
    ///     Drag ghost: the small translucent icon that follows the cursor while an
    ///     action or preset icon is dragged onto a hotbar. Begin/End stay inside the
    ///     kit; when icon is null it draws the frame only (caller resolved no texture).
    /// </summary>
    public static void DragGhost(ImTextureID? icon, Vector2 screenPos, float size)
    {
        ImGui.SetNextWindowPos(screenPos);
        ImGui.SetNextWindowBgAlpha(0.5f);
        ImGui.Begin("###LalaUiKitDragGhost", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize);
        if (icon is { } tex)
            ImGui.Image(tex, new Vector2(size));
        ImGui.End();
    }
}
