using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using LalaUiKit;

namespace WondrousTailsSolver;

/// <summary>Read-only, responsive view of the solver's live Wondrous Tails probabilities.</summary>
internal sealed unsafe class LazyWTMathWindow : Window
{
    private const string Description = "Live line and shuffle chances from the current sticker board.";
    private const float CardPad = 16f;
    private const float SectionGap = 16f;

    private static readonly UiState[] AllStates = Enum.GetValues<UiState>();

    private readonly Action _showChangelog;

    /// <summary>One vertical slice of a column: measured at a width, then drawn at an origin with the height it was given.</summary>
    private readonly record struct Block(Func<float, float> Height, Action<Vector2, float, float> Draw);

    public LazyWTMathWindow(Action showChangelog) : base("Lazy WT Math###LazyWTMathMainWindow")
    {
        _showChangelog = showChangelog;
        Size = new Vector2(920, 700);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 600),
            MaximumSize = new Vector2(1400, 1000),
        };
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
    }

    // The shell draws full-bleed bands from the window edge and keeps its own gutter, so the window pads nothing.
    public override void PreDraw() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    public override void PostDraw() => ImGui.PopStyleVar();

    public override void Draw()
    {
        var snapshot = Capture();
        var state = State(snapshot);
        Theme.PushFrame();
        try
        {
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            var g = LazyWTMathChrome.Gutter(winSize.X);
            var draw = ImGui.GetWindowDrawList();

            var bandHeight = DrawTopBar(draw, winPos, winSize.X, g, state);
            var footerHeight = FooterHeight();

            ImGui.SetCursorPos(new Vector2(0, bandHeight));
            DrawBodyChild(snapshot, state, new Vector2(winSize.X, winSize.Y - bandHeight - footerHeight), g);

            DrawFooter(draw, winPos, winSize, g, footerHeight, snapshot, state);
        }
        finally
        {
            Theme.PopFrame();
        }
    }

    private static LazyWTMathSnapshot Capture()
    {
        var playerState = PlayerState.Instance();
        if (playerState is null)
            return LazyWTMathSnapshot.Unavailable();

        var cells = new bool[16];
        foreach (var index in Enumerable.Range(0, 16))
        {
            cells[index] = playerState->IsWeeklyBingoStickerPlaced(index);
            System.PerfectTails.GameState[index] = cells[index];
        }

        var stickersPlaced = playerState->WeeklyBingoNumPlacedStickers;
        var solverText = System.PerfectTails.SolveAndGetProbabilitySeString().TextValue;
        return LazyWTMathPresentation.Parse(stickersPlaced, cells, solverText);
    }

    private static UiState State(LazyWTMathSnapshot snapshot)
    {
        if (!snapshot.PlayerAvailable)
            return UiState.Idle;
        if (!snapshot.HasValidProbabilities)
            return UiState.Failed;
        return snapshot.StickersPlaced == 0 ? UiState.Empty : UiState.Ready;
    }

    // ------------------------------------------------------------------ top bar

    private float DrawTopBar(ImDrawListPtr draw, Vector2 winPos, float width, float g, UiState state)
    {
        const float padY = 14f;
        const float gap = 8f;
        var controlHeight = MathF.Max(36, LazyWTMathChrome.LineH() + 16);
        var brandHeight = MathF.Max(40, LazyWTMathChrome.LineH(Theme.TitleScale) + 16);
        var rowHeight = MathF.Max(brandHeight, controlHeight);
        const string brand = "Lazy WT Math";
        const string whatsNew = "What's new";
        var brandWidth = 12 + 24 + 12 + LazyWTMathChrome.TextWidth(brand, Theme.TitleScale) + 16;
        var whatsNewWidth = LazyWTMathChrome.ActionWidth(96, whatsNew);
        var widestState = 0f;
        foreach (var s in AllStates)
            widestState = MathF.Max(widestState, LazyWTMathChrome.TextWidth(Copy.Word(s)));
        var statusWidth = MathF.Max(104, 16 + (2 * Theme.DotRadius) + 8 + widestState + 16);
        var closeSize = controlHeight;
        var required = (2 * g) + brandWidth + gap + statusWidth + gap + whatsNewWidth + gap + closeSize;
        var singleRow = width >= required;
        var ruleY = (2 * padY) + rowHeight + (singleRow ? 0 : gap + controlHeight);
        var bandHeight = ruleY + 1;

        draw.AddRectFilled(winPos, winPos + new Vector2(width, ruleY), Theme.Pack(Theme.ShellBar));
        LazyWTMathChrome.Hairline(draw, winPos.X, winPos.X + width, winPos.Y + ruleY + 0.5f);

        var brandPos = winPos + new Vector2(g, padY + ((rowHeight - brandHeight) / 2f));
        LazyWTMathChrome.Panel(draw, brandPos, new Vector2(brandWidth, brandHeight), Theme.Surface, Theme.SurfaceBorder);
        LazyWTMathChrome.Diamond(draw, brandPos + new Vector2(24, brandHeight / 2f), 12, Theme.Pack(Theme.Accent));
        LazyWTMathChrome.Text(draw, brandPos + new Vector2(48, (brandHeight - LazyWTMathChrome.LineH(Theme.TitleScale)) / 2f),
            brand, Theme.TextPrimary, Theme.TitleScale);

        var controlY = padY + ((rowHeight - controlHeight) / 2f);
        var closePos = winPos + new Vector2(width - g - closeSize, controlY);
        var statusSize = new Vector2(statusWidth, controlHeight);
        var statusPos = closePos - new Vector2(gap + statusWidth, 0);
        var whatsNewPos = singleRow
            ? statusPos - new Vector2(gap + whatsNewWidth, 0)
            : winPos + new Vector2(g, padY + rowHeight + gap);
        var whatsNewSize = new Vector2(singleRow ? whatsNewWidth : width - (2 * g), controlHeight);

        LazyWTMathChrome.StatusPill(draw, statusPos, statusSize, state);
        if (LazyWTMathChrome.ActionButton(draw, "whats_new", whatsNew, whatsNewPos, whatsNewSize))
            _showChangelog();
        if (LazyWTMathChrome.CloseButton(draw, "close", closePos, closeSize))
            IsOpen = false;
        if (ImGui.IsItemHovered())
        {
            // Directly under the button, inside the bar: it covers neither the top-bar controls nor the page header.
            LazyWTMathChrome.TooltipBelow("Close", new Vector2(closePos.X + closeSize, closePos.Y + closeSize + 4));
        }

        return bandHeight;
    }

    // ------------------------------------------------------------------ footer

    private static string FooterLead(LazyWTMathSnapshot snapshot, UiState state) => state switch
    {
        UiState.Idle => "Waiting for a character",
        UiState.Failed => "Probabilities unavailable",
        UiState.Empty => "No stickers placed",
        _ => $"{snapshot.StickersPlaced} of 9 stickers placed",
    };

    private const string FooterDetail = "Read-only view; this window never changes the journal.";

    private static float FooterHeight()
        => 12 + MathF.Max(TextBlock(), 36) + 12;

    private static float TextBlock()
        => LazyWTMathChrome.LineH() + 4 + LazyWTMathChrome.LineH(Theme.SecondaryScale);

    private static void DrawFooter(ImDrawListPtr draw, Vector2 winPos, Vector2 winSize, float g, float height,
        LazyWTMathSnapshot snapshot, UiState state)
    {
        var top = winPos.Y + winSize.Y - height;
        draw.PushClipRect(new Vector2(winPos.X, top), winPos + winSize, false);
        draw.AddRectFilled(new Vector2(winPos.X, top), winPos + winSize, Theme.Pack(Theme.ShellBar));
        LazyWTMathChrome.Hairline(draw, winPos.X, winPos.X + winSize.X, top + 0.5f);

        var lineH = LazyWTMathChrome.LineH();
        var textTop = top + ((height - TextBlock()) / 2f);
        var textX = winPos.X + g + 16;
        var textWidth = winSize.X - (2 * g) - 16;
        draw.AddCircleFilled(new Vector2(winPos.X + g + 4, textTop + (lineH / 2f)), 4f, Theme.Pack(Copy.MarkColor(state)));
        LazyWTMathChrome.Text(draw, new Vector2(textX, textTop), LazyWTMathChrome.Fit(FooterLead(snapshot, state), textWidth), Theme.TextPrimary);
        LazyWTMathChrome.Text(draw, new Vector2(textX, textTop + lineH + 4),
            LazyWTMathChrome.Fit(FooterDetail, textWidth, Theme.SecondaryScale), Theme.TextSecondary, Theme.SecondaryScale);
        draw.PopClipRect();
    }

    // ------------------------------------------------------------------ body

    private void DrawBodyChild(LazyWTMathSnapshot snapshot, UiState state, Vector2 size, float g)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(g, 16));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8f);
        ImGui.PushStyleColor(ImGuiCol.Border, 0u);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Theme.TextSecondary with { W = 0.35f });
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Theme.TextSecondary with { W = 0.5f });
        try
        {
            ImGui.BeginChild("##wtmBody", size, true);
            try
            {
                DrawBody(snapshot, state);
                DrawScrollFades();
            }
            finally
            {
                ImGui.EndChild();
            }
        }
        finally
        {
            ImGui.PopStyleColor(6);
            ImGui.PopStyleVar(5);
        }
    }

    /// <summary>Soft edges where content is scrolled out of view, so a partial line never ends in a hard cut.</summary>
    private static void DrawScrollFades()
    {
        var max = ImGui.GetScrollMaxY();
        if (max <= 0)
            return;
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var opaque = ImGui.GetColorU32(ImGuiCol.WindowBg) | 0xFF000000u;
        var clear = opaque & 0x00FFFFFFu;
        var scroll = ImGui.GetScrollY();
        var right = pos.X + size.X - 12;
        if (scroll < max - 1)
            draw.AddRectFilledMultiColor(new Vector2(pos.X, pos.Y + size.Y - 28), new Vector2(right, pos.Y + size.Y), clear, clear, opaque, opaque);
        if (scroll > 1)
            draw.AddRectFilledMultiColor(new Vector2(pos.X, pos.Y), new Vector2(right, pos.Y + 20), opaque, opaque, clear, clear);
    }

    private static void DrawBody(LazyWTMathSnapshot snapshot, UiState state)
    {
        var origin = ImGui.GetCursorScreenPos();
        var availableHeight = ImGui.GetContentRegionAvail().Y;
        var width = ImGui.GetContentRegionAvail().X;
        var draw = ImGui.GetWindowDrawList();

        var y = DrawPageHeader(draw, origin, width, snapshot);

        if (Notice(snapshot) is { } notice)
        {
            var height = LazyWTMathChrome.NoticeHeight(notice.Lead, notice.Body, width);
            LazyWTMathChrome.Notice(draw, origin + new Vector2(0, y), width, notice.Lead, notice.Body, state);
            y += height + SectionGap;
        }

        var main = BuildMain(snapshot);
        var rail = BuildRail(snapshot);
        if (width >= 760)
        {
            var railWidth = Math.Clamp(0.26f * width, 300, 360);
            var mainWidth = width - SectionGap - railWidth;
            // Cards keep their content height. When a notice pushes the sticker map under the fold, the map's
            // cells give up a little height first so the default window still shows the whole board.
            _cellShrink = 0;
            var room = availableHeight - y - 1;
            var natural = MathF.Max(ColumnHeight(main, mainWidth), ColumnHeight(rail, railWidth));
            if (natural > room && snapshot.PlayerAvailable)
                _cellShrink = Math.Clamp((natural - room) / 4f, 0, CellHeight(mainWidth - (2 * CardPad)) - MinCell);
            var height = MathF.Max(ColumnHeight(main, mainWidth), ColumnHeight(rail, railWidth));
            DrawColumn(draw, main, origin + new Vector2(0, y), mainWidth);
            DrawColumn(draw, rail, origin + new Vector2(mainWidth + SectionGap, y), railWidth);
            y += height;
        }
        else
        {
            var stacked = main.Concat(rail).ToList();
            _cellShrink = 0;
            DrawColumn(draw, stacked, origin + new Vector2(0, y), width);
            y += ColumnHeight(stacked, width);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, y));
    }

    private static float ColumnHeight(List<Block> blocks, float width)
        => blocks.Sum(b => b.Height(width)) + (SectionGap * (blocks.Count - 1));

    /// <summary>Stacks the blocks, each at its own measured height.</summary>
    private static void DrawColumn(ImDrawListPtr draw, List<Block> blocks, Vector2 origin, float width)
    {
        var y = 0f;
        foreach (var block in blocks)
        {
            var height = block.Height(width);
            block.Draw(origin + new Vector2(0, y), width, height);
            y += height + SectionGap;
        }
    }

    private static (string Lead, string Body)? Notice(LazyWTMathSnapshot snapshot)
    {
        if (!snapshot.PlayerAvailable)
            return ("Idle", "Player state is unavailable. Log in to read the current Wondrous Tails board.");
        if (!snapshot.HasValidProbabilities)
            return ("Failed", "Probabilities are unavailable for the current sticker state. Check the Wondrous Tails journal data.");
        if (snapshot.StickersPlaced == 0)
            return ("Empty", "No stickers are placed. Place a sticker in the journal to update the board and reveal Shuffle Average.");
        return null;
    }

    private static float DrawPageHeader(ImDrawListPtr draw, Vector2 origin, float width, LazyWTMathSnapshot snapshot)
    {
        const float tile = 44f;
        const float titleScale = 1.35f;
        const string title = "Wondrous Tails probabilities";
        var meta = snapshot.PlayerAvailable ? $"{snapshot.StickersPlaced} / 9 stickers" : "Player state unavailable";
        var wide = ImGui.GetWindowWidth() >= 760;
        var textX = tile + 16;
        var metaWidth = wide ? LazyWTMathChrome.TextWidth(meta, Theme.SecondaryScale) : 0;
        var textWidth = width - textX - (wide ? metaWidth + 16 : 0);

        var titleLines = LazyWTMathChrome.ClampLines(title, textWidth, titleScale, wide ? 1 : 2, out var titleCut);
        var descLines = LazyWTMathChrome.ClampLines(Description, textWidth, Theme.SecondaryScale, wide ? 1 : 3, out var descCut);
        var titleLine = LazyWTMathChrome.LineH(titleScale);
        var descLine = LazyWTMathChrome.LineH(Theme.SecondaryScale);
        var titleHeight = titleLines.Count * titleLine;
        var descHeight = descLines.Count * descLine;
        var block = titleHeight + 4 + descHeight + (wide ? 0 : 4 + descLine);
        var height = MathF.Max(tile, block);
        origin += new Vector2(0, 4);

        var tilePos = origin;
        draw.AddRectFilled(tilePos, tilePos + new Vector2(tile), Theme.Pack(Theme.Accent with { W = 0.14f }), LazyWTMathChrome.Radius);
        LazyWTMathChrome.Diamond(draw, tilePos + new Vector2(tile / 2f), 10, Theme.Pack(Theme.Accent));

        var textPos = origin + new Vector2(textX, 0);
        LazyWTMathChrome.Lines(draw, textPos, titleLines, Theme.TextPrimary, titleScale);
        LazyWTMathChrome.Lines(draw, textPos + new Vector2(0, titleHeight + 4), descLines, Theme.TextSecondary, Theme.SecondaryScale);
        if (wide)
        {
            var metaY = textPos.Y + ((titleLine - LazyWTMathChrome.LineH(Theme.SecondaryScale)) / 2f);
            LazyWTMathChrome.TextRight(draw, origin.X + width, metaY, meta, Theme.TextSecondary, Theme.SecondaryScale);
        }
        else
            LazyWTMathChrome.Text(draw, textPos + new Vector2(0, titleHeight + 4 + descHeight + 4), meta, Theme.TextSecondary, Theme.SecondaryScale);

        if ((titleCut || descCut) && ImGui.IsMouseHoveringRect(textPos, textPos + new Vector2(textWidth, block)))
            ImGui.SetTooltip(title + "\n" + Description);

        return 4 + height + 8 + SectionGap;
    }

    // ------------------------------------------------------------------ left column

    private static List<Block> BuildMain(LazyWTMathSnapshot snapshot)
    {
        const string lineDescription = "Probability of completing one, two, or three lines from the current stickers.";
        var blocks = new List<Block>
        {
            new(
                w => LazyWTMathChrome.LineH() + 4 + LazyWTMathChrome.ParagraphHeight(lineDescription, w, Theme.SecondaryScale),
                (o, w, _) => DrawHeading(ImGui.GetWindowDrawList(), o, w, "Line chances", lineDescription)),
            new(w => StatRowHeight(w), (o, w, _) => DrawStatRow(ImGui.GetWindowDrawList(), o, w, snapshot)),
        };
        if (snapshot.PlayerAvailable)
            blocks.Add(MapCard(snapshot));
        return blocks;
    }

    private static float DrawHeading(ImDrawListPtr draw, Vector2 pos, float width, string heading, string description)
    {
        LazyWTMathChrome.Text(draw, pos, heading, Theme.TextPrimary);
        var y = LazyWTMathChrome.LineH() + 4;
        return y + LazyWTMathChrome.Paragraph(draw, pos + new Vector2(0, y), description, width, Theme.TextSecondary, Theme.SecondaryScale);
    }

    // Cards sit three across at every allowed width; a narrow row uses a smaller value size and tighter padding.
    private static bool Mini(float width) => width < 512;

    private static float StatCardHeight(bool mini)
    {
        var small = LazyWTMathChrome.LineH(Theme.SecondaryScale);
        var value = LazyWTMathChrome.LineH(mini ? 1.4f : 1.7f);
        var pad = mini ? 12f : CardPad;
        var step = mini ? 6f : 8f;
        return pad + small + step + value + step + Theme.BarHeight + step + small + pad;
    }

    private static float StatRowHeight(float width) => StatCardHeight(Mini(width));

    private static void DrawStatRow(ImDrawListPtr draw, Vector2 origin, float width, LazyWTMathSnapshot snapshot)
    {
        var mini = Mini(width);
        var height = StatCardHeight(mini);
        var gap = mini ? 8f : 16f;
        var cardWidth = (width - (2 * gap)) / 3f;
        for (var i = 0; i < 3; i++)
            DrawStatCard(draw, origin + new Vector2(i * (cardWidth + gap), 0), new Vector2(cardWidth, height), i, snapshot, mini);
    }

    private static void DrawStatCard(ImDrawListPtr draw, Vector2 pos, Vector2 size, int index, LazyWTMathSnapshot snapshot, bool mini)
    {
        var label = index == 0 ? "1 line" : $"{index + 1} lines";
        var valid = snapshot.HasValidProbabilities && snapshot.LineChances.Length == 3;
        var value = valid ? snapshot.LineChances[index] : "—";
        var fraction = 0f;
        var hasFraction = valid && LazyWTMathPresentation.TryPercent(value, out fraction);
        var complete = hasFraction && fraction >= 1f;
        var note = !snapshot.PlayerAvailable ? "No character" : !valid ? "Solver unavailable" : complete ? "Guaranteed" : "Current board";

        var pad = mini ? 12f : CardPad;
        var step = mini ? 6f : 8f;
        var valueScale = mini ? 1.4f : 1.7f;
        var small = LazyWTMathChrome.LineH(Theme.SecondaryScale);
        var valueHeight = LazyWTMathChrome.LineH(valueScale);
        var inner = size.X - (2 * pad);
        LazyWTMathChrome.Panel(draw, pos, size, Theme.Surface, Theme.SurfaceBorder);

        var y = pos.Y + pad;
        LazyWTMathChrome.Diamond(draw, new Vector2(pos.X + pad + 5, y + (small / 2f)), 5, Theme.Pack(Theme.Accent));
        LazyWTMathChrome.Text(draw, new Vector2(pos.X + pad + 16, y), label, Theme.TextSecondary, Theme.SecondaryScale);
        var noteColor = complete ? Theme.StateGreen : Theme.TextSecondary;
        y += small + step;

        LazyWTMathChrome.Text(draw, new Vector2(pos.X + pad, y), value, valid ? Theme.TextPrimary : Theme.TextSecondary, valueScale);
        y += valueHeight + step;

        var barMin = new Vector2(pos.X + pad, y);
        var barMax = new Vector2(pos.X + size.X - pad, y + Theme.BarHeight);
        if (hasFraction)
        {
            draw.AddRectFilled(barMin, barMax, Theme.Pack(Theme.SurfaceBorder), 2f);
            if (fraction > 0)
                draw.AddRectFilled(barMin, new Vector2(barMin.X + ((barMax.X - barMin.X) * Math.Clamp(fraction, 0, 1)), barMax.Y), Theme.Pack(Theme.StateGreen), 2f);
        }
        LazyWTMathChrome.Text(draw, new Vector2(pos.X + pad, y + Theme.BarHeight + step), LazyWTMathChrome.Fit(note, inner, Theme.SecondaryScale), noteColor, Theme.SecondaryScale);
    }

    private const string MapDescriptionFormat = "{0} / 9 stickers placed. Pink diamonds mark the occupied journal cells.";

    private static Block MapCard(LazyWTMathSnapshot snapshot)
    {
        var description = string.Format(MapDescriptionFormat, snapshot.StickersPlaced);
        return Card(
            inner => LazyWTMathChrome.LineH() + 4 + LazyWTMathChrome.ParagraphHeight(description, inner, Theme.SecondaryScale)
                     + 12 + MapHeight(inner),
            (o, inner, _) =>
            {
                var draw = ImGui.GetWindowDrawList();
                var y = DrawHeading(draw, o, inner, "Sticker map", description) + 12;
                DrawBoard(draw, o + new Vector2(0, y), inner, snapshot);
            });
    }

    private const float MinCell = 30f;

    /// <summary>How much each map cell gives up this frame so the board fits the visible page (0 when it already fits).</summary>
    private static float _cellShrink;

    private static float CellHeight(float width) => Math.Clamp(((width - 24) / 4f) * 0.32f, 40, 56);

    private static float MapHeight(float width) => (4 * (CellHeight(width) - _cellShrink)) + 24;

    private static void DrawBoard(ImDrawListPtr draw, Vector2 origin, float width, LazyWTMathSnapshot snapshot)
    {
        const float gap = 8f;
        var cellWidth = (width - (3 * gap)) / 4f;
        var cellHeight = CellHeight(width) - _cellShrink;
        for (var index = 0; index < 16; index++)
        {
            var pos = origin + new Vector2((index % 4) * (cellWidth + gap), (index / 4) * (cellHeight + gap));
            var selected = snapshot.Cells[index];
            LazyWTMathChrome.Panel(draw, pos, new Vector2(cellWidth, cellHeight),
                selected ? Theme.Accent with { W = 0.14f } : Theme.ShellBar,
                selected ? Theme.Accent : Theme.SurfaceBorder);
            var label = $"{index + 1:00}";
            var center = pos.Y + (cellHeight / 2f);
            LazyWTMathChrome.Text(draw, new Vector2(pos.X + 12, center - (LazyWTMathChrome.LineH(Theme.CaptionScale) / 2f)), label,
                selected ? Theme.Accent : Theme.TextSecondary, Theme.CaptionScale);
            var marker = new Vector2(pos.X + cellWidth - 12 - 10, center);
            if (selected)
                LazyWTMathChrome.Diamond(draw, marker, 10, Theme.Pack(Theme.Accent));
            else
                draw.AddCircle(marker, 7, Theme.Pack(Theme.SurfaceBorder), 16, 1);
        }
    }

    // ------------------------------------------------------------------ right rail

    private const string ShuffleDescription = "The existing sampled averages for the current sticker count.";
    private const string AboutDescription = "Line Chances and Shuffle Average come from the existing solver and current sticker state. This read-only window does not change the journal or make game decisions.";
    private const string OverlayDescription = "The same probabilities remain beside the journal's duty details with refreshed high-contrast text.";

    private static string ShuffleMessage(LazyWTMathSnapshot snapshot)
    {
        if (!snapshot.PlayerAvailable)
            return "Waiting for a character. Shuffle Average appears once the board can be read.";
        if (!snapshot.HasValidProbabilities)
            return "Shuffle Average is unavailable while the probabilities are.";
        return snapshot.StickersPlaced == 0
            ? "Shuffle Average appears once 1–7 stickers are placed."
            : "Shuffle Average is only available with 1–7 stickers placed.";
    }

    private static List<Block> BuildRail(LazyWTMathSnapshot snapshot)
    {
        var values = snapshot.HasValidProbabilities ? snapshot.ShuffleAverage : null;
        var rowHeight = LazyWTMathChrome.LineH(Theme.TitleScale) + 16;
        return
        [
            Card(
                inner => LazyWTMathChrome.LineH() + 4 + LazyWTMathChrome.ParagraphHeight(ShuffleDescription, inner, Theme.SecondaryScale) + 12
                         + (values is null
                             ? LazyWTMathChrome.ParagraphHeight(ShuffleMessage(snapshot), inner, Theme.SecondaryScale)
                             : values.Length * rowHeight),
                (o, inner, _) =>
                {
                    var draw = ImGui.GetWindowDrawList();
                    var y = DrawHeading(draw, o, inner, "Shuffle average", ShuffleDescription) + 12;
                    if (values is null)
                        LazyWTMathChrome.Paragraph(draw, o + new Vector2(0, y), ShuffleMessage(snapshot), inner, Theme.TextSecondary, Theme.SecondaryScale);
                    else
                        DrawShuffleRows(draw, o + new Vector2(0, y), inner, values, rowHeight);
                }),
            TextCard("About these values", AboutDescription),
            TextCard("Journal overlay", OverlayDescription),
        ];
    }

    private static Block TextCard(string heading, string text) => Card(
        inner => LazyWTMathChrome.LineH() + 4 + LazyWTMathChrome.ParagraphHeight(text, inner, Theme.SecondaryScale),
        (o, inner, _) => DrawHeading(ImGui.GetWindowDrawList(), o, inner, heading, text));

    private static void DrawShuffleRows(ImDrawListPtr draw, Vector2 origin, float width, string[] values, float rowHeight)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var top = origin.Y + (i * rowHeight);
            LazyWTMathChrome.Hairline(draw, origin.X, origin.X + width, top + 0.5f);
            var center = top + (rowHeight / 2f);
            var label = i == 0 ? "1 line" : $"{i + 1} lines";
            LazyWTMathChrome.Diamond(draw, new Vector2(origin.X + 5, center), 5, Theme.Pack(Theme.Accent));
            LazyWTMathChrome.Text(draw, new Vector2(origin.X + 20, center - (LazyWTMathChrome.LineH() / 2f)), label, Theme.TextSecondary);
            LazyWTMathChrome.TextRight(draw, origin.X + width, center - (LazyWTMathChrome.LineH(Theme.TitleScale) / 2f), values[i], Theme.TextPrimary, Theme.TitleScale);
        }
    }

    // ------------------------------------------------------------------ card container

    /// <summary>Bordered card with 16 px padding that takes its content height; <paramref name="content"/> gets the inner origin and width.</summary>
    private static Block Card(Func<float, float> contentHeight, Action<Vector2, float, float> content) => new(
        w => contentHeight(w - (2 * CardPad)) + (2 * CardPad),
        (o, w, h) =>
        {
            LazyWTMathChrome.Panel(ImGui.GetWindowDrawList(), o, new Vector2(w, h), Theme.Surface, Theme.SurfaceBorder);
            content(o + new Vector2(CardPad), w - (2 * CardPad), 0);
        });
}
