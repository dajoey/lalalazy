using System;
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
    private readonly Action _showChangelog;

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
        Flags = ImGuiWindowFlags.NoCollapse;
    }

    public override void Draw()
    {
        var snapshot = Capture();
        Theme.PushFrame();
        try
        {
            DrawTopBar(snapshot);
            DrawPageHeader(snapshot);

            ImGui.BeginChild("##wtmBody", Vector2.Zero, false);
            if (ImGui.GetContentRegionAvail().X >= 760)
                DrawWideBody(snapshot);
            else
            {
                DrawWork(snapshot);
                ImGui.Separator();
                ImGui.Dummy(new Vector2(0, Theme.Unit));
                DrawRail(snapshot);
            }
            ImGui.EndChild();
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

    private void DrawTopBar(LazyWTMathSnapshot snapshot)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var draw = ImGui.GetWindowDrawList();
        var gap = 3 * Theme.Unit;
        var brandSize = new Vector2(178, 42);
        var actionSize = new Vector2(104, 34);
        var state = State(snapshot);
        var stateLabel = Copy.Word(state);
        var statusSize = new Vector2(MathF.Max(82, ImGui.CalcTextSize(stateLabel).X + 43), 34);
        var requiredWidth = 24 + brandSize.X + gap + actionSize.X + gap + statusSize.X;
        var singleRow = width >= requiredWidth;
        var barHeight = singleRow ? 62f : 104f;

        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar), 2 * Theme.Unit);
        draw.AddRect(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);

        var brandPos = origin + new Vector2(12, singleRow ? 10 : 8);
        draw.AddRectFilled(brandPos, brandPos + brandSize, Theme.Pack(Theme.Surface), 2 * Theme.Unit);
        draw.AddRect(brandPos, brandPos + brandSize, Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
        LazyWTMathChrome.Diamond(draw, brandPos + new Vector2(24, 21), 12, Theme.Pack(Theme.Accent));
        LazyWTMathChrome.Text(draw, brandPos + new Vector2(48, 11), "Lazy WT Math", Theme.TextPrimary, Theme.TitleScale);

        var actionPos = origin + (singleRow
            ? new Vector2(width - 12 - actionSize.X, 14)
            : new Vector2(width - 12 - statusSize.X - gap - actionSize.X, 58));
        var statusPos = origin + (singleRow
            ? new Vector2(actionPos.X - origin.X - gap - statusSize.X, 14)
            : new Vector2(width - 12 - statusSize.X, 58));

        if (LazyWTMathChrome.OutlineButton(draw, "whats_new", "What's new", actionPos, actionSize))
            _showChangelog();
        LazyWTMathChrome.StatusPill(draw, statusPos, statusSize, state);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private static void DrawPageHeader(LazyWTMathSnapshot snapshot)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var draw = ImGui.GetWindowDrawList();
        var tileSize = new Vector2(42);
        var count = snapshot.PlayerAvailable ? $"{snapshot.StickersPlaced} / 9 stickers" : "player state unavailable";
        var wide = width >= 640;

        draw.AddRectFilled(origin, origin + tileSize, Theme.Pack(Theme.Accent with { W = 0.14f }), 2 * Theme.Unit);
        LazyWTMathChrome.Diamond(draw, origin + (tileSize / 2f), 10, Theme.Pack(Theme.Accent));

        if (wide)
        {
            var textX = origin.X + tileSize.X + 12;
            var countWidth = ImGui.CalcTextSize(count).X;
            LazyWTMathChrome.Text(draw, new Vector2(textX, origin.Y),
                LazyWTMathChrome.FitText("Wondrous Tails probabilities", width - tileSize.X - 12 - countWidth - 16, Theme.TitleScale),
                Theme.TextPrimary, Theme.TitleScale);
            LazyWTMathChrome.Text(draw, new Vector2(textX, origin.Y + 29),
                LazyWTMathChrome.FitText(Description, width - tileSize.X - 12), Theme.TextSecondary);
            LazyWTMathChrome.TextRight(draw, origin.X + width, origin.Y + 5, count, Theme.TextSecondary);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, 58));
        }
        else
        {
            LazyWTMathChrome.Text(draw, origin + new Vector2(tileSize.X + 12, 4),
                LazyWTMathChrome.FitText("Wondrous Tails probabilities", width - tileSize.X - 12, Theme.TitleScale),
                Theme.TextPrimary, Theme.TitleScale);
            LazyWTMathChrome.Text(draw, origin + new Vector2(0, 54),
                LazyWTMathChrome.FitText(Description, width), Theme.TextSecondary);
            LazyWTMathChrome.Text(draw, origin + new Vector2(0, 76), count, Theme.TextSecondary, Theme.CaptionScale);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, 100));
        }

        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, Theme.Unit));
    }

    private static void DrawWideBody(LazyWTMathSnapshot snapshot)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var gap = 2 * Theme.Unit;
        var leftWidth = (available - gap) * 0.64f;

        ImGui.BeginChild("##wtmWork", new Vector2(leftWidth, 0), false);
        DrawWork(snapshot);
        ImGui.EndChild();

        ImGui.SameLine(0, gap);
        ImGui.BeginChild("##wtmRail", Vector2.Zero, false);
        DrawRail(snapshot);
        ImGui.EndChild();
    }

    private static void DrawWork(LazyWTMathSnapshot snapshot)
    {
        Widgets.Title("Line chances");
        Widgets.SecondaryWrapped("Probability of completing one, two, or three lines from the current stickers.");
        ImGui.Dummy(new Vector2(0, Theme.Unit));
        DrawProbabilityCards(snapshot);

        if (!snapshot.PlayerAvailable)
            Widgets.InfoBox("Idle — player state is unavailable. Log in to read the current Wondrous Tails board.", UiState.Idle);
        else if (!snapshot.HasValidProbabilities)
            Widgets.InfoBox("Failed — probabilities are unavailable for the current sticker state. Check the Wondrous Tails journal data.", UiState.Failed);
        else if (snapshot.StickersPlaced == 0)
            Widgets.InfoBox("Empty — no stickers are placed. Place a sticker in the journal to update the board and reveal Shuffle Average.", UiState.Empty);

        if (snapshot.PlayerAvailable)
        {
            ImGui.Dummy(new Vector2(0, Theme.Unit));
            Widgets.Title("Sticker map");
            Widgets.SecondaryWrapped($"{snapshot.StickersPlaced} / 9 stickers placed. Pink diamonds mark the occupied journal cells.");
            ImGui.Dummy(new Vector2(0, Theme.Unit));
            DrawBoard(snapshot);
        }
    }

    private static void DrawProbabilityCards(LazyWTMathSnapshot snapshot)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var stacked = available < 560;
        var gap = 3 * Theme.Unit;
        var width = stacked ? available : (available - (2 * gap)) / 3;
        const float height = 108;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();

        for (var i = 0; i < 3; i++)
        {
            var pos = origin + (stacked ? new Vector2(0, i * (height + gap)) : new Vector2(i * (width + gap), 0));
            var label = i == 0 ? "1 line" : $"{i + 1} lines";
            var valid = snapshot.HasValidProbabilities && snapshot.LineChances.Length == 3;
            var value = valid ? snapshot.LineChances[i] : "—";
            var fraction = 0f;
            var hasFraction = valid && LazyWTMathPresentation.TryPercent(value, out fraction);

            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), 2 * Theme.Unit);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
            LazyWTMathChrome.Diamond(draw, pos + new Vector2(20, 20), 5, Theme.Pack(Theme.Accent));
            LazyWTMathChrome.Text(draw, pos + new Vector2(36, 12), label, Theme.TextSecondary, Theme.CaptionScale);
            LazyWTMathChrome.Text(draw, pos + new Vector2(12, 34), value, Theme.TextPrimary, Theme.TitleScale);

            var barMin = pos + new Vector2(12, 68);
            var barMax = pos + new Vector2(width - 12, 72);
            draw.AddRectFilled(barMin, barMax, Theme.Pack(Theme.SurfaceBorder), 2f);
            if (hasFraction && fraction > 0)
                draw.AddRectFilled(barMin, barMin + new Vector2((barMax.X - barMin.X) * Math.Clamp(fraction, 0, 1), 4), Theme.Pack(Theme.Accent), 2f);

            LazyWTMathChrome.Text(draw, pos + new Vector2(12, 84), valid ? "Current board" : "Unavailable", Theme.TextSecondary, Theme.CaptionScale);
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(0, stacked ? (3 * height) + (2 * gap) + gap : height + gap));
    }

    private static void DrawBoard(LazyWTMathSnapshot snapshot)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var gap = 2 * Theme.Unit;
        var cellWidth = MathF.Min(78, (available - (3 * gap)) / 4);
        const float cellHeight = 64;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();

        for (var index = 0; index < 16; index++)
        {
            var row = index / 4;
            var column = index % 4;
            var pos = origin + new Vector2(column * (cellWidth + gap), row * (cellHeight + gap));
            var selected = snapshot.Cells[index];
            var fill = selected ? Theme.Accent with { W = 0.14f } : Theme.Surface;
            draw.AddRectFilled(pos, pos + new Vector2(cellWidth, cellHeight), Theme.Pack(fill), 2 * Theme.Unit);
            draw.AddRect(pos, pos + new Vector2(cellWidth, cellHeight), Theme.Pack(selected ? Theme.Accent : Theme.SurfaceBorder), 2 * Theme.Unit);
            LazyWTMathChrome.Text(draw, pos + new Vector2(8, 6), $"{index + 1:00}", selected ? Theme.Accent : Theme.TextSecondary, Theme.CaptionScale);

            var marker = pos + new Vector2(cellWidth / 2f, 39);
            if (selected)
                LazyWTMathChrome.Diamond(draw, marker, 11, Theme.Pack(Theme.Accent));
            else
                draw.AddCircle(marker, 7, Theme.Pack(Theme.SurfaceBorder), 16, 1);
        }

        var height = (4 * cellHeight) + (3 * gap);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height + gap));
    }

    private static void DrawRail(LazyWTMathSnapshot snapshot)
    {
        Widgets.Title("Shuffle average");
        Widgets.SecondaryWrapped("The existing sampled averages for the current sticker count.");
        ImGui.Dummy(new Vector2(0, Theme.Unit));

        if (snapshot.HasValidProbabilities && snapshot.ShuffleAverage is { } shuffle)
            DrawShuffleCards(shuffle);
        else if (snapshot.HasValidProbabilities)
            Widgets.InfoBox("Shuffle Average is available with 1–7 stickers placed.", UiState.Idle);

        ImGui.Dummy(new Vector2(0, Theme.Unit));
        Widgets.Title("About these values");
        Widgets.SecondaryWrapped("Line Chances and Shuffle Average come from the existing solver and current sticker state. This read-only window does not change the journal or make game decisions.");
        ImGui.Dummy(new Vector2(0, Theme.Unit));
        Widgets.Title("Journal overlay");
        Widgets.SecondaryWrapped("The same probabilities remain beside the journal's duty details with refreshed high-contrast text.");
    }

    private static void DrawShuffleCards(string[] values)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        const float height = 58;
        var gap = 2 * Theme.Unit;

        for (var i = 0; i < values.Length; i++)
        {
            var pos = origin + new Vector2(0, i * (height + gap));
            var label = i == 0 ? "1 line" : $"{i + 1} lines";
            draw.AddRectFilled(pos, pos + new Vector2(width, height), Theme.Pack(Theme.Surface), 2 * Theme.Unit);
            draw.AddRect(pos, pos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
            LazyWTMathChrome.Diamond(draw, pos + new Vector2(20, height / 2f), 5, Theme.Pack(Theme.Accent));
            LazyWTMathChrome.Text(draw, pos + new Vector2(36, 20), label, Theme.TextPrimary);
            LazyWTMathChrome.TextRight(draw, pos.X + width - 12, pos.Y + 19, values[i], Theme.TextPrimary, Theme.TitleScale);
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(0, (values.Length * height) + ((values.Length - 1) * gap) + gap));
    }
}
