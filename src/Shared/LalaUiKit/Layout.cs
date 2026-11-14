// LalaUiKit/Layout.cs ??? Shell top bar, page header, two-column grid and footer
// (Brief "Kit shape", R1, R2, R3, R9, R10).

namespace LalaUiKit;

using System;
using System.Collections.Generic;
using System.Linq;

using System.Numerics;
using Dalamud.Bindings.ImGui;

public static class Layout
{
    // ---- Shell: the plugin's main window (R1: two navigation layers maximum) ----

    /// <summary>
    ///     A Shell window: applies the suite frame (R9), draws the top bar with the plugin
    ///     title on the left and the live-state pill (R3 place 1) on the right. Dispose to
    ///     end it. Group tabs live between Shell and NavSection (R1) ??? AutoPotion has none
    ///     (one functional area).
    /// </summary>
    public sealed class Shell : IDisposable
    {
        private readonly bool _opened;

        private Shell(bool opened) => _opened = opened;

        public static Shell Begin(string name, string title, ref bool open, UiState state, string feature, string? pillDetail = null, ImGuiWindowFlags extraFlags = ImGuiWindowFlags.None)
        {
            var flags = extraFlags | ImGuiWindowFlags.NoCollapse;
            var opened = ImGui.Begin(name, ref open, flags);
            var shell = new Shell(opened);
            if (!opened)
                return shell; // Begin failed: Dispose ends the window, nothing else drawn

            Theme.PushFrame();

            // Top bar: full-width strip, title left, pill right (R3 place 1).
            var p = ImGui.GetCursorScreenPos();
            var w = ImGui.GetWindowWidth() - (2 * Theme.Unit);
            var barH = Theme.PillHeight;
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(p, p + new Vector2(w, barH), Theme.Pack(Theme.ShellBar), 4f);
            dl.AddRect(p, p + new Vector2(w, barH), Theme.Pack(Theme.SurfaceBorder), 4f);
            ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(2 * Theme.Unit, (barH - ImGui.GetTextLineHeight()) / 2f));
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
            ImGui.TextUnformatted(title);
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.SetCursorPosX(p.X + w - (2 * Theme.Unit));
            Widgets.StatusPill(state, feature, pillDetail, alignRight: true);
            ImGui.SetCursorPosY(p.Y + barH + Theme.ItemSpacing.Y);
            return shell;
        }

        public bool Opened => _opened;

        public void Dispose()
        {
            if (_opened)
                Theme.PopFrame();
            ImGui.End();
        }
    }

    // ---- section/feature row (R1 layer 2: icon + live dot; the dot is R3 place 2) ----

    /// <summary>
    ///     One entry in the section row: icon tile, label, live dot for its state. The
    ///     accent outline marks exactly the selected entry and nothing else (R4). Returns
    ///     true when clicked (callers switch pages).
    /// </summary>
    public static bool NavSection(string id, string label, ImTextureID? icon, string? glyph, UiState dotState, bool selected)
    {
        var p = ImGui.GetCursorScreenPos();
        var size = new Vector2(Theme.NavTileSize, Theme.NavTileSize);
        Widgets.IconTile(icon, glyph, size, selected);

        var lineH = ImGui.GetTextLineHeight();
        var cursor = ImGui.GetCursorPos();
        ImGui.SetCursorPos(cursor + new Vector2(Theme.Unit, (size.Y - lineH) / 2f));
        ImGui.PushStyleColor(ImGuiCol.Text, selected ? Theme.TextPrimary : Theme.TextSecondary);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPos(cursor + new Vector2(Theme.Unit + ImGui.CalcTextSize(label).X + Theme.ItemInnerSpacing.X, (size.Y - lineH) / 2f));
        Widgets.StatusDot(dotState);

        var rowH = MathF.Max(size.Y, lineH) + Theme.Unit;
        ImGui.SetCursorScreenPos(p + new Vector2(0, rowH));
        ImGui.InvisibleButton(id, new Vector2(ImGui.GetContentRegionAvail().X, rowH));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(label);
        return ImGui.IsItemClicked();
    }

    // ---- page header (R2) ----

    /// <summary>
    ///     Every feature page header: icon, title, one-line description of what the page
    ///     does, and a live count that answers a question. No headerless content pages.
    /// </summary>
    public static void PageHeader(ImTextureID? icon, string? glyph, string title, string description, string liveCount)
    {
        Widgets.IconTile(icon, glyph, new Vector2(Theme.NavTileSize, Theme.NavTileSize), false);
        ImGui.SameLine();
        var w = ImGui.GetContentRegionAvail().X;
        var countX = w - ImGui.CalcTextSize(liveCount).X;

        ImGui.BeginGroup();
        Widgets.Title(title);
        ImGui.SameLine();
        ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX() + Theme.Unit, countX + Theme.Unit));
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
        ImGui.TextUnformatted(liveCount);
        ImGui.PopStyleColor();
        Widgets.Secondary(description);
        ImGui.EndGroup();
        ImGui.Dummy(new Vector2(0, Theme.Unit));
    }

    // ---- two-column grid (R10) ----

    /// <summary>
    ///     Work on the left; rail with Requirements and grouped Settings on the right.
    ///     Usage: Begin() ??? left content ??? Split() ??? rail content ??? Dispose(). Pages
    ///     without settings or requirements use a single column (don't call this).
    /// </summary>
    public sealed class TwoColumns : IDisposable
    {
        private readonly float _leftWidth;
        private bool _split;

        private TwoColumns(float leftWidth) => _leftWidth = leftWidth;

        public static TwoColumns Begin(float leftFraction = 0.62f)
        {
            var avail = ImGui.GetContentRegionAvail().X;
            var tc = new TwoColumns(avail * leftFraction);
            ImGui.BeginChild("kit_left", new Vector2(tc._leftWidth, 0), false);
            return tc;
        }

        /// <summary>Call once the left (work) column is drawn; then draw the right rail.</summary>
        public void Split()
        {
            ImGui.EndChild();
            ImGui.SameLine();
            ImGui.BeginChild("kit_rail", new Vector2(0, 0), false);
            _split = true;
        }

        public void Dispose()
        {
            if (!_split)
            {
                ImGui.EndChild();
                ImGui.SameLine();
                ImGui.BeginChild("kit_rail", new Vector2(0, 0), false);
                _split = true;
            }

            ImGui.EndChild();
        }
    }

    // ---- footer (R3 place 3) ----

    /// <summary>
    ///     Footer line: live dot, the state word, the current action (with the one-line
    ///     reason when one exists), and the Stop button pinned to the right edge.
    /// </summary>
    public static void Footer(UiState state, string action, string? reason, string stopLabel, out bool stopClicked)
    {
        stopClicked = false;
        ImGui.Dummy(new Vector2(0, Theme.Unit));
        ImGui.Separator();
        Widgets.StatusDot(state);
        ImGui.SameLine();
        var line = string.IsNullOrEmpty(reason)
            ? $"{Copy.Word(state)} ??? {action}"
            : $"{Copy.Word(state)} ??? {action} ??? {reason}";
        Widgets.Secondary(line);
        ImGui.SameLine();

        var stopW = ImGui.CalcTextSize(stopLabel).X + (Theme.FramePadding.X * 2);
        ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - stopW - Theme.WindowPadding.X - Theme.Unit));
        stopClicked = Widgets.StopButton(stopLabel);
    }

    /// <summary>Announce an outcome once (R3): the banner the footer's state points at.</summary>
    public static void Announce(UiState state, string headline, string? detail = null) =>
        Widgets.Banner(state, headline, detail);

    // ---- overlay (raw-window replacement) ----

    /// <summary>
    ///     A frameless overlay window for game-anchored content (retainer buttons, tooltips,
    ///     diagnostic dots): the Begin/End pair those draws need, with the caller keeping full
    ///     control of position, size and content. Transparent background by default; pass
    ///     transparentBackground: false to keep the window's own surface (boxed tooltips).
    ///     Raw-window conversion tasks route every ImGui.Begin through here so plugin code
    ///     carries none.
    /// </summary>
    public sealed class Overlay : IDisposable
    {
        private readonly bool _transparent;

        private Overlay(bool transparent) => _transparent = transparent;

        public static Overlay Begin(string name, ImGuiWindowFlags flags, bool transparentBackground = true)
        {
            if (transparentBackground)
                ImGui.PushStyleColor(ImGuiCol.WindowBg, 0u);
            ImGui.Begin(name, flags);
            return new Overlay(transparentBackground);
        }

        public void Dispose()
        {
            ImGui.End();
            if (_transparent)
                ImGui.PopStyleColor();
        }
    }


    /// <summary>
    ///     The suite's tooltip window: the Begin/End pair for hover tooltips, themed by the
    ///     kit (solid background, SurfaceBorder border shadow). Sizes and position arrive
    ///     pre-scaled from the caller so the kit keeps its ImGui-and-Numerics-only deps.
    ///     Raw-tooltip conversion tasks route their tooltip windows through here so plugin
    ///     code carries no raw ImGui.Begin.
    /// </summary>
    public sealed class TooltipScope : IDisposable
    {
        private readonly bool _opened;

        private TooltipScope(bool opened) => _opened = opened;

        public static TooltipScope Begin(string name, ImGuiWindowFlags flags, Vector2 minSize, Vector2 maxSize, Vector2 position)
        {
            ImGui.SetNextWindowBgAlpha(1f);
            ImGui.PushStyleColor(ImGuiCol.BorderShadow, Theme.SurfaceBorder);
            ImGui.SetNextWindowSizeConstraints(minSize, maxSize);
            ImGui.SetWindowPos(name, position);
            var opened = ImGui.Begin(name, flags);
            return new TooltipScope(opened);
        }

        public bool Opened => _opened;

        public void Dispose()
        {
            if (_opened)
                ImGui.End();
            ImGui.PopStyleColor();
        }
    }
}
