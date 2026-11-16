// Shared source (NOT a shared DLL) - compiled into every lalalazy plugin.
using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using LalaUiKit;

namespace Lalalazy.Changelog;

/// <summary>
/// The "What's new" popup in the suite window shell: header band with the plugin's brand chip and a drawn close box,
/// a page header with the version range, one collapsible card per version (newest expanded) with coloured section
/// headings and word-wrapped bullets in a single scrolling body, and a footer with "Open changelog on GitHub" + "Got it".
/// </summary>
public sealed class ChangelogWindow : Window
{
    private readonly string _displayName;
    private readonly string _githubUrl;
    private readonly Action _onDismiss;

    private IReadOnlyList<ChangelogEntry> _entries = Array.Empty<ChangelogEntry>();
    private Version _current = new(0, 0, 0, 0);
    private Version? _previous;
    private bool _firstOpen = true;

    // Index of every version card that is expanded (the newest starts open, as before).
    private readonly HashSet<int> _expanded = new();

    public ChangelogWindow(string displayName, string githubUrl, Action onDismiss)
        : base($"{displayName} \u2014 What's new##lala-changelog",
               ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _displayName = displayName;
        _githubUrl = githubUrl;
        _onDismiss = onDismiss;

        Size = new Vector2(640, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 420),
            MaximumSize = new Vector2(1100, 900),
        };
        RespectCloseHotkey = true;
    }

    public void Show(IReadOnlyList<ChangelogEntry> entries, Version current, Version? previous)
    {
        _entries = entries;
        _current = current;
        _previous = previous;
        _expanded.Clear();
        if (entries.Count > 0) _expanded.Add(0);
        WindowName = $"{_displayName} \u2014 What's new in v{current}##lala-changelog";
        IsOpen = true;
        BringToFront();
    }

    public override void OnOpen()
    {
        if (_firstOpen)
        {
            // Centre on the main viewport the first time the window ever appears.
            var vp = ImGui.GetMainViewport();
            var size = Size ?? new Vector2(640, 560);
            Position = vp.Pos + (vp.Size - size) * 0.5f;
            PositionCondition = ImGuiCond.FirstUseEver;
            _firstOpen = false;
        }
    }

    public override void OnClose()
    {
        // Closing with the X or Escape counts as "seen" too, otherwise it re-pops every login.
        _onDismiss();
    }

    public override void PreDraw() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    public override void PostDraw() => ImGui.PopStyleVar();

    public override void Draw()
    {
        Theme.PushFrame();
        try
        {
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            var gutter = PopupChrome.Gutter(winSize.X);
            var draw = ImGui.GetWindowDrawList();

            var y = PopupChrome.HeaderBand(draw, winPos, winSize.X, gutter, _displayName, "##lala-cl-close", out var closeClicked);
            if (closeClicked)
                IsOpen = false; // OnClose -> _onDismiss persists LastSeen

            var meta = _previous is not null && _previous > new Version(0, 0, 0, 0)
                ? $"v{_previous} to v{_current}"
                : $"v{_current}";
            y += PopupChrome.PageHeader(draw, winPos, winSize.X, gutter, y, "What's new", $"{_displayName} release notes", meta);

            var bodyHeight = MathF.Max(80, winSize.Y - PopupChrome.FooterHeight - y);
            DrawBody(winSize.X, gutter, y, bodyHeight);
            DrawFooter(draw, winPos, winSize, gutter);
        }
        finally
        {
            Theme.PopFrame();
        }
    }

    // ---- body -----------------------------------------------------------------------------------------------

    private void DrawBody(float width, float gutter, float top, float height)
    {
        ImGui.SetCursorPos(new Vector2(0, top));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(gutter, 16));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8f);
        ImGui.PushStyleColor(ImGuiCol.Border, 0u);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Theme.SurfaceBorder);
        ImGui.BeginChild("##lala-changelog-body", new Vector2(width, height), true);

        var draw = ImGui.GetWindowDrawList();
        var cardWidth = ImGui.GetContentRegionAvail().X;
        if (_entries.Count == 0)
        {
            DrawNotice(draw, cardWidth, "No release notes were embedded in this build.");
        }
        else
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                if (i > 0)
                    ImGui.Dummy(new Vector2(cardWidth, 16 - Theme.ItemSpacing.Y));
                DrawEntry(draw, i, _entries[i], cardWidth);
            }
        }

        // Fade where more content scrolls in below, so nothing looks cut by the footer.
        if (ImGui.GetScrollMaxY() > 0 && ImGui.GetScrollY() < ImGui.GetScrollMaxY() - 1)
        {
            var min = ImGui.GetWindowPos();
            var bottom = min.Y + height - 1;
            var right = min.X + width - 11;
            var window = ImGui.GetColorU32(ImGuiCol.WindowBg);
            draw.AddRectFilledMultiColor(
                new Vector2(min.X, bottom - 28),
                new Vector2(right, bottom),
                window & 0x00FFFFFFu,
                window & 0x00FFFFFFu,
                window | 0xFF000000u,
                window | 0xFF000000u);
        }

        ImGui.EndChild();
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(5);
    }

    private static void DrawNotice(ImDrawListPtr draw, float width, string text)
    {
        var origin = ImGui.GetCursorScreenPos();
        var textW = MathF.Max(1, width - 32);
        var height = 16 + PopupChrome.WrappedHeight(text, textW) + 16;
        PopupChrome.CardBackground(draw, origin, new Vector2(width, height));
        PopupChrome.Text(draw, origin + new Vector2(16, 16), text, Theme.TextSecondary, 1f, textW);
        ImGui.Dummy(new Vector2(width, height));
    }

    // One version card: title row (version, date, chevron) and, when expanded, its sections.
    private void DrawEntry(ImDrawListPtr draw, int index, ChangelogEntry e, float width)
    {
        var expanded = _expanded.Contains(index);
        var origin = ImGui.GetCursorScreenPos();
        var height = LayoutEntry(draw, origin, width, e, expanded, paint: false, hovered: false);

        var rowH = TitleRowHeight();
        ImGui.SetCursorScreenPos(origin);
        var clicked = ImGui.InvisibleButton($"##lala-cl-toggle-{index}", new Vector2(width, Pad + rowH + 8));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (clicked && !_expanded.Remove(index))
            _expanded.Add(index);

        PopupChrome.CardBackground(draw, origin, new Vector2(width, height), hovered);
        LayoutEntry(draw, origin, width, e, expanded, paint: true, hovered: hovered);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    private const float Pad = 16f;

    private static float TitleRowHeight() => MathF.Max(PopupChrome.LineHeight(Theme.TitleScale), 24f);

    /// <summary>Measures (and, with <paramref name="paint"/>, draws) one card's content and returns the card height.</summary>
    private static float LayoutEntry(ImDrawListPtr draw, Vector2 origin, float width, ChangelogEntry e, bool expanded, bool paint, bool hovered)
    {
        var sm = Theme.SecondaryScale;
        var lh = PopupChrome.LineHeight();
        var rowH = TitleRowHeight();
        var y = Pad;

        if (paint)
        {
            // Chevron at the right edge, date to its left, version at the left (elided when the row is short).
            var chevron = new Vector2(origin.X + width - Pad - 6, origin.Y + y + (rowH / 2f));
            var col = Theme.Pack(hovered ? Theme.Accent : Theme.TextSecondary);
            if (expanded)
            {
                draw.AddLine(chevron + new Vector2(-5, -2.5f), chevron + new Vector2(0, 2.5f), col, 1.5f);
                draw.AddLine(chevron + new Vector2(0, 2.5f), chevron + new Vector2(5, -2.5f), col, 1.5f);
            }
            else
            {
                draw.AddLine(chevron + new Vector2(-2.5f, -5), chevron + new Vector2(2.5f, 0), col, 1.5f);
                draw.AddLine(chevron + new Vector2(2.5f, 0), chevron + new Vector2(-2.5f, 5), col, 1.5f);
            }

            var rightEdge = chevron.X - 12;
            var date = e.Date ?? string.Empty;
            var dateW = date.Length == 0 ? 0 : PopupChrome.TextWidth(date, sm);
            if (dateW > 0)
                PopupChrome.TextRight(draw, rightEdge, origin.Y + y + ((rowH - PopupChrome.LineHeight(sm)) / 2f), date, Theme.TextSecondary, sm);
            var versionRoom = rightEdge - (dateW > 0 ? dateW + 16 : 0) - (origin.X + Pad);
            PopupChrome.Text(
                draw,
                new Vector2(origin.X + Pad, origin.Y + y + ((rowH - PopupChrome.LineHeight(Theme.TitleScale)) / 2f)),
                PopupChrome.FitText($"v{e.VersionText}", versionRoom, Theme.TitleScale),
                Theme.TextPrimary,
                Theme.TitleScale);
        }

        y += rowH;
        if (expanded && e.Sections.Count > 0)
        {
            y += 12;
            var textW = MathF.Max(1, width - (2 * Pad) - 16);
            for (var s = 0; s < e.Sections.Count; s++)
            {
                var section = e.Sections[s];
                if (paint)
                {
                    var color = ColorFor(section.Kind);
                    draw.AddCircleFilled(new Vector2(origin.X + Pad + 3, origin.Y + y + (lh / 2f)), Theme.DotRadius, Theme.Pack(color));
                    PopupChrome.Text(draw, new Vector2(origin.X + Pad + 14, origin.Y + y), section.Name, color);
                }

                y += lh + 6;
                foreach (var b in section.Bullets)
                {
                    var h = PopupChrome.WrappedHeight(b, textW);
                    if (paint)
                    {
                        draw.AddCircleFilled(new Vector2(origin.X + Pad + 3, origin.Y + y + (lh / 2f)), 2f, Theme.Pack(Theme.TextSecondary));
                        PopupChrome.Text(draw, new Vector2(origin.X + Pad + 16, origin.Y + y), b, Theme.TextPrimary, 1f, textW);
                    }

                    y += h + 6;
                }

                y -= 6;
                if (s < e.Sections.Count - 1)
                    y += 14;
            }
        }

        return y + Pad;
    }

    // ---- footer ---------------------------------------------------------------------------------------------

    private void DrawFooter(ImDrawListPtr draw, Vector2 winPos, Vector2 winSize, float gutter)
    {
        var width = winSize.X;
        var top = winSize.Y - PopupChrome.FooterHeight;
        PopupChrome.FooterBand(draw, winPos, winSize, top);

        const string gotIt = "Got it";
        const string github = "Open changelog on GitHub";
        var inner = width - (2 * gutter);
        var gotSize = new Vector2(PopupChrome.ActionWidth(96, gotIt), PopupChrome.Control);
        var githubSize = new Vector2(PopupChrome.ActionWidth(96, github), PopupChrome.Control);
        // At a large UI scale the pair can exceed the window: the link button gives up width first.
        githubSize.X = MathF.Min(githubSize.X, MathF.Max(PopupChrome.Control, inner - PopupChrome.Gap - gotSize.X));
        var buttonY = top + ((PopupChrome.FooterHeight - PopupChrome.Control) / 2f);
        var gotPos = new Vector2(width - gutter - gotSize.X, buttonY);
        var githubPos = new Vector2(gotPos.X - PopupChrome.Gap - githubSize.X, buttonY);

        // Left block: brand word and the number of version cards, dropped when the buttons leave no room.
        var lh = PopupChrome.LineHeight();
        var smH = PopupChrome.LineHeight(Theme.SecondaryScale);
        var block = lh + 4 + smH;
        var textX = gutter + 16;
        var textRoom = githubPos.X - 16 - textX;
        var textTop = top + ((PopupChrome.FooterHeight - block) / 2f);
        var line1 = PopupChrome.FitText("lalalazy", textRoom);
        if (line1.Length > 0)
        {
            var count = _entries.Count;
            var detail = count == 0 ? "No release notes" : count == 1 ? "1 version" : $"{count} versions";
            draw.AddCircleFilled(winPos + new Vector2(gutter + 4, textTop + (lh / 2f)), 4, Theme.Pack(Theme.StateNeutral));
            PopupChrome.Text(draw, winPos + new Vector2(textX, textTop), line1, Theme.TextPrimary);
            PopupChrome.Text(draw, winPos + new Vector2(textX, textTop + lh + 4), PopupChrome.FitText(detail, textRoom, Theme.SecondaryScale), Theme.TextSecondary, Theme.SecondaryScale);
        }

        if (PopupChrome.ActionButton(draw, "##lala-cl-github", github, winPos + githubPos, githubSize))
            Util.OpenLink(_githubUrl);
        if (PopupChrome.ActionButton(draw, "##lala-cl-gotit", gotIt, winPos + gotPos, gotSize))
        {
            IsOpen = false; // OnClose -> _onDismiss persists LastSeen
        }
    }

    private static Vector4 ColorFor(ChangelogSectionKind kind) => kind switch
    {
        ChangelogSectionKind.Added => ImGuiColors.HealerGreen,
        ChangelogSectionKind.Changed => ImGuiColors.DalamudOrange,
        ChangelogSectionKind.Fixed => ImGuiColors.TankBlue,
        ChangelogSectionKind.Removed => ImGuiColors.DalamudRed,
        ChangelogSectionKind.Notes => ImGuiColors.DalamudGrey,
        _ => ImGuiColors.DalamudWhite,
    };
}
