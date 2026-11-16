using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace LazyRetainerLive;

// The Dalamud Window base owns Begin/End. This class draws the complete content shell,
// including its integrated status bar, responsive body and reserved footer.
internal class ConfigWindow : Window
{
    private const float ControlHeight = 36f;

    private readonly Plugin _plugin;

    public ConfigWindow(Plugin plugin) : base("LazyRetainerLive##cfg")
    {
        _plugin = plugin;
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        Size = new Vector2(980, 680);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 480),
            MaximumSize = new Vector2(1600, 1100),
        };
    }

    public override void PreDraw() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    public override void PostDraw() => ImGui.PopStyleVar();

    private static float Gutter(float width) => width >= 900 ? 24 : width >= 560 ? 16 : 12;

    public override void Draw()
    {
        var config = _plugin.Config;
        var snapshot = _plugin.State?.Current;
        var fresh = _plugin.State?.LastTickOk == true;
        var httpError = _plugin.State != null ? _plugin.HttpError : null;
        var status = RetainerLivePresentation.Status(config.Enabled, httpError, snapshot, fresh);
        var state = ToUiState(status.State);
        var changed = false;

        Theme.PushFrame();
        try
        {
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            var gutter = Gutter(winSize.X);
            var draw = ImGui.GetWindowDrawList();

            var y = DrawTopBar(draw, winPos, winSize.X, gutter, state);
            y += DrawPageHeader(draw, winPos, winSize.X, gutter, y, config, snapshot);

            var footerHeight = FooterHeight(winSize.X);
            var bodyHeight = MathF.Max(80, winSize.Y - footerHeight - y);
            DrawBodyChild(winSize.X, gutter, y, bodyHeight, config, snapshot, fresh, httpError, status, () => changed = true);

            DrawFooter(draw, winPos, winSize, gutter, config, status, state, () => changed = true);
        }
        finally
        {
            Theme.PopFrame();
            if (changed)
                _plugin.SaveConfig();
        }
    }

    private static UiState ToUiState(RetainerLiveState state) => state switch
    {
        RetainerLiveState.Ready => UiState.Ready,
        RetainerLiveState.Idle => UiState.Idle,
        RetainerLiveState.Blocked => UiState.Blocked,
        RetainerLiveState.Failed => UiState.Failed,
        RetainerLiveState.NeedsSetup => UiState.NeedsSetup,
        _ => UiState.Idle,
    };

    // Band: 14 px top and bottom, 40 px brand, 36 px controls centred on it. Returns the band height.
    private float DrawTopBar(ImDrawListPtr draw, Vector2 winPos, float width, float gutter, UiState state)
    {
        const float pad = 14;
        const float gap = 8;
        const float brandHeight = 40;
        var closeSize = new Vector2(ControlHeight);
        var pillSize = new Vector2(RetainerLiveChrome.StatusPillWidth(), ControlHeight);
        var inner = width - (2 * gutter);

        // Brand at the title size when the row has room, one step smaller otherwise, else the pill drops to row 2.
        var brandScale = Theme.TitleScale;
        var rest = pillSize.X + closeSize.X + (2 * gap);
        if (RetainerLiveChrome.BrandWidth(brandScale) + rest > inner)
            brandScale = 1f;
        var brandWidth = RetainerLiveChrome.BrandWidth(brandScale);
        var singleRow = brandWidth + rest <= inner;
        var barHeight = singleRow ? pad + brandHeight + pad : pad + brandHeight + gap + ControlHeight + pad;

        draw.PushClipRect(winPos, winPos + new Vector2(width, barHeight + 1), false);
        draw.AddRectFilled(winPos, winPos + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(
            winPos + new Vector2(0, barHeight),
            winPos + new Vector2(width, barHeight),
            Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        var rowOneCentre = pad + (brandHeight / 2f);
        var closePos = new Vector2(width - gutter - closeSize.X, rowOneCentre - (closeSize.Y / 2f));
        var pillPos = singleRow
            ? new Vector2(closePos.X - gap - pillSize.X, closePos.Y)
            : new Vector2(width - gutter - pillSize.X, pad + brandHeight + gap);

        RetainerLiveChrome.Brand(draw, winPos + new Vector2(gutter, pad), new Vector2(brandWidth, brandHeight), brandScale);
        RetainerLiveChrome.StatusPill(draw, winPos + pillPos, pillSize, Copy.Word(state), state);
        if (RetainerLiveChrome.CloseButton(draw, winPos + closePos, closeSize))
            IsOpen = false;
        return barHeight;
    }

    // Tile 44, text column at gutter + 60. Returns the header height; the divider sits on its bottom edge.
    private static float DrawPageHeader(
        ImDrawListPtr draw,
        Vector2 winPos,
        float width,
        float gutter,
        float top,
        Configuration config,
        CharInfo? snapshot)
    {
        const string title = "Retainer live state";
        const string description = "Publishes the current retainer table as read-only JSON on loopback.";
        const float titleScale = 1.35f;
        const float tile = 44;
        var sm = Theme.SecondaryScale;
        var count = RetainerLivePresentation.LiveCount(snapshot);
        var subtext = RetainerLivePresentation.LiveSubtext(config.Enabled, config.Port);
        var wide = width >= 760;
        var textX = gutter + 60;
        var left = winPos + new Vector2(textX, top + 20);
        var titleH = RetainerLiveChrome.LineHeight(titleScale);
        var smH = RetainerLiveChrome.LineHeight(sm);
        float block;

        draw.AddRectFilled(winPos + new Vector2(gutter, top + 20), winPos + new Vector2(gutter + tile, top + 20 + tile), Theme.Pack(Theme.Accent with { W = 0.14f }), 8f);
        RetainerLiveChrome.Diamond(draw, winPos + new Vector2(gutter + (tile / 2f), top + 20 + (tile / 2f)), 10, Theme.Pack(Theme.Accent));

        if (wide)
        {
            var metaWidth = MathF.Min(
                MathF.Max(RetainerLiveChrome.TextWidth(count, sm), RetainerLiveChrome.TextWidth(subtext, sm)),
                width * 0.4f);
            var textWidth = width - gutter - textX - metaWidth - 16;
            var shownTitle = RetainerLiveChrome.FitText(title, textWidth, titleScale);
            var shownDescription = RetainerLiveChrome.FitText(description, textWidth, sm);
            RetainerLiveChrome.Text(draw, left, shownTitle, Theme.TextPrimary, titleScale);
            RetainerLiveChrome.Text(draw, left + new Vector2(0, titleH + 4), shownDescription, Theme.TextSecondary, sm);
            block = titleH + 4 + smH;

            // Meta: first line centred on the title line, second on the description line.
            var metaRight = winPos.X + width - gutter;
            var shownCount = RetainerLiveChrome.FitText(count, metaWidth, sm);
            var shownSubtext = RetainerLiveChrome.FitText(subtext, metaWidth, sm);
            RetainerLiveChrome.TextRight(draw, metaRight, left.Y + ((titleH - smH) / 2f), shownCount, Theme.TextSecondary, sm);
            RetainerLiveChrome.TextRight(draw, metaRight, left.Y + titleH + 4, shownSubtext, Theme.TextSecondary, sm);
            TipIfElided(draw, shownTitle != title || shownDescription != description || shownCount != count || shownSubtext != subtext, winPos, top, width, block, $"{title}\n{description}\n{count}\n{subtext}");
        }
        else
        {
            var textWidth = width - gutter - textX;
            var shownTitle = RetainerLiveChrome.FitText(title, textWidth, titleScale);
            RetainerLiveChrome.Text(draw, left, shownTitle, Theme.TextPrimary, titleScale);
            var y = left.Y + titleH + 4;
            var descriptionH = RetainerLiveChrome.WrappedHeight(description, textWidth, sm);
            RetainerLiveChrome.Text(draw, new Vector2(left.X, y), description, Theme.TextSecondary, sm, textWidth);
            y += descriptionH + 4;
            RetainerLiveChrome.Text(draw, new Vector2(left.X, y), RetainerLiveChrome.FitText(count, textWidth, sm), Theme.TextSecondary, sm);
            y += smH + 2;
            RetainerLiveChrome.Text(draw, new Vector2(left.X, y), RetainerLiveChrome.FitText(subtext, textWidth, sm), Theme.TextSecondary, sm);
            block = y + smH - left.Y;
        }

        var height = 20 + MathF.Max(tile, block) + 8;
        draw.AddLine(
            winPos + new Vector2(0, top + height),
            winPos + new Vector2(width, top + height),
            Theme.Pack(Theme.SurfaceBorder));
        return height + 1;
    }

    private static void TipIfElided(ImDrawListPtr draw, bool elided, Vector2 winPos, float top, float width, float block, string full)
    {
        if (!elided)
            return;
        var min = winPos + new Vector2(0, top + 20);
        var max = winPos + new Vector2(width, top + 20 + block);
        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max))
            RetainerLiveChrome.Tip(full);
    }

    private static void DrawBodyChild(
        float width,
        float gutter,
        float top,
        float height,
        Configuration config,
        CharInfo? snapshot,
        bool fresh,
        string? httpError,
        RetainerLiveStatus status,
        Action markChanged)
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
        ImGui.BeginChild("##retainerLiveBody", new Vector2(width, height), true);
        DrawBody(height, config, snapshot, fresh, httpError, status, markChanged);
        // Fade where more content scrolls in below, so nothing looks cut by the footer.
        if (ImGui.GetScrollMaxY() > 0 && ImGui.GetScrollY() < ImGui.GetScrollMaxY() - 1)
        {
            var bodyDraw = ImGui.GetWindowDrawList();
            var min = ImGui.GetWindowPos();
            var bottom = min.Y + height - 1;
            var right = min.X + width - 11;
            var window = ImGui.GetColorU32(ImGuiCol.WindowBg);
            bodyDraw.AddRectFilledMultiColor(
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

    private static void DrawBody(
        float bodyHeight,
        Configuration config,
        CharInfo? snapshot,
        bool fresh,
        string? httpError,
        RetainerLiveStatus status,
        Action markChanged)
    {
        DrawStatCards(snapshot, fresh);
        RetainerLiveChrome.Columns(
            "retainerLiveBody",
            bodyHeight,
            () =>
            {
                RetainerLiveChrome.Card(() => DrawEndpoint(config, snapshot, fresh, httpError, status));
                RetainerLiveChrome.Card(() => DrawSnapshotContinuity(snapshot, fresh), fill: true, gapAfter: false);
            },
            () =>
            {
                RetainerLiveChrome.Card(() => DrawRequirements(config, snapshot, fresh, httpError));
                RetainerLiveChrome.Card(() => DrawSettings(config, markChanged), fill: true, gapAfter: false);
            });
    }

    private readonly record struct Stat(string Label, string Value, string Note, float? Fraction);

    private static void DrawStatCards(CharInfo? snapshot, bool fresh)
    {
        var active = snapshot is null ? 0 : RetainerLivePresentation.ActiveVentures(snapshot);
        var stats = new[]
        {
            new Stat(
                "Retainers",
                snapshot?.Retainers.Count.ToString() ?? "—",
                snapshot is null ? "No published table" : fresh ? "Current live table" : "Last-known table",
                null),
            new Stat(
                "Active ventures",
                snapshot is null ? "—" : active.ToString(),
                snapshot is null ? "No venture data" : $"{active} of {snapshot.Retainers.Count} retainers",
                snapshot is null || snapshot.Retainers.Count == 0 ? null : active / (float)snapshot.Retainers.Count),
            new Stat(
                "Free inventory slots",
                snapshot?.Inventory.ToString() ?? "—",
                snapshot is null ? "No wallet or bag data" : "Four main inventory bags",
                null),
        };

        const float gap = 16;
        var available = RetainerLiveChrome.Avail();
        var wide = available >= (3 * 220) + (2 * gap);
        var width = wide ? (available - (2 * gap)) / 3 : available;
        var height = RetainerLiveChrome.StatCardHeight(!wide);
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();

        for (var i = 0; i < stats.Length; i++)
        {
            var pos = origin + (wide
                ? new Vector2(i * (width + gap), 0)
                : new Vector2(0, i * (height + gap)));
            var stat = stats[i];
            RetainerLiveChrome.StatCard(draw, pos, width, !wide, stat.Label, stat.Value, stat.Note, stat.Fraction);
        }

        var total = wide ? height : (stats.Length * height) + ((stats.Length - 1) * gap);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(available, total));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, total));
        ImGui.Dummy(new Vector2(available, gap - Theme.ItemSpacing.Y));
    }

    private static void Section(string title, string description)
    {
        RetainerLiveChrome.Para(title, Theme.TitleScale, Theme.TextPrimary, 4);
        RetainerLiveChrome.Para(description, Theme.SecondaryScale, Theme.TextSecondary, 8);
    }

    // One value-column edge for every key/value row in the main column.
    private static float LabelColumn() => new[] { "Method", "Address", "Payload", "Character", "World", "Gil", "Venture coins", "Company seals" }
        .Max(label => RetainerLiveChrome.TextWidth(label)) + 20;

    private static void DrawEndpoint(
        Configuration config,
        CharInfo? snapshot,
        bool fresh,
        string? httpError,
        RetainerLiveStatus status)
    {
        Section("Loopback endpoint", "The exact read-only surface consumed by the dashboard relay.");
        var labels = LabelColumn();
        RetainerLiveChrome.KeyValue("Method", "GET", labels);
        RetainerLiveChrome.KeyValue("Address", config.Enabled ? $"http://127.0.0.1:{config.Port}/retainers" : "Listener off", labels, elide: true);
        RetainerLiveChrome.KeyValue("Payload", snapshot is null ? "503 until the first snapshot" : fresh ? "200 · current live snapshot" : "200 · last-known snapshot", labels);
        if (snapshot is not null)
        {
            RetainerLiveChrome.KeyValue("Character", snapshot.Char, labels);
            RetainerLiveChrome.KeyValue("World", snapshot.World, labels);
        }

        ImGui.SetCursorScreenPos(ImGui.GetCursorScreenPos() + new Vector2(0, 4));
        if (!string.IsNullOrEmpty(httpError) && config.Enabled)
        {
            RetainerLiveChrome.Notice("Failed", httpError, UiState.Failed);
        }
        else
        {
            var state = ToUiState(status.State);
            RetainerLiveChrome.Notice(Copy.Word(state), status.Detail, state);
        }
    }

    private static void DrawSnapshotContinuity(CharInfo? snapshot, bool fresh)
    {
        Section("Snapshot continuity", "The HTTP thread only reads the latest completed immutable payload.");
        if (snapshot is null)
        {
            RetainerLiveChrome.Para(
                "Needs setup — no retainer table has been published since plugin load. Log in; the endpoint answers 503 rather than inventing an empty character.",
                Theme.SecondaryScale,
                Theme.TextSecondary,
                0);
            return;
        }

        RetainerLiveChrome.Para(
            fresh
                ? "Ready — the relay receives the latest completed live retainer table, venture state, wallet and bag counts."
                : "Blocked — the current game table is unavailable, so the last-known payload stays served through logout and zone transitions without flapping to an empty response.",
            Theme.SecondaryScale,
            Theme.TextSecondary,
            8);
        var labels = LabelColumn();
        RetainerLiveChrome.KeyValue("Gil", snapshot.Gil.ToString("N0"), labels, numeric: true);
        RetainerLiveChrome.KeyValue("Venture coins", snapshot.Ventures.ToString("N0"), labels, numeric: true);
        RetainerLiveChrome.KeyValue("Company seals", snapshot.Seals.ToString("N0"), labels, numeric: true);
    }

    private static void DrawRequirements(Configuration config, CharInfo? snapshot, bool fresh, string? httpError)
    {
        Section("Requirements", "What the current serving path needs.");
        RetainerLiveChrome.StatusRow(
            "Loopback listener",
            !config.Enabled ? "Idle" : !string.IsNullOrEmpty(httpError) ? "Failed" : "Ready",
            !config.Enabled ? UiState.Idle : !string.IsNullOrEmpty(httpError) ? UiState.Failed : UiState.Ready);
        RetainerLiveChrome.StatusRow(
            "Retainer snapshot",
            snapshot is null ? Copy.Missing("snapshot") : fresh ? "Ready" : "Last-known",
            snapshot is null ? UiState.Missing : fresh ? UiState.Ready : UiState.Blocked);
        RetainerLiveChrome.StatusRow("Network scope", "127.0.0.1 only", UiState.Ready);
        RetainerLiveChrome.StatusRow("Port rebind", config.Enabled ? "Next framework tick" : "On enable", UiState.Idle);
    }

    private static void DrawSettings(Configuration config, Action markChanged)
    {
        Section("Settings", "Changes save immediately; config keys are unchanged.");
        var enabled = config.Enabled;
        if (RetainerLiveChrome.Toggle(
                "Serve retainer data",
                ref enabled,
                "Bind the loopback listener and serve the latest completed snapshot. Snapshot collection continues while off."))
        {
            config.Enabled = enabled;
            markChanged();
        }

        RetainerLiveChrome.Hairline();
        ImGui.SetCursorScreenPos(ImGui.GetCursorScreenPos() + new Vector2(0, 12));
        RetainerLiveChrome.Para("Loopback port", 1f, Theme.TextPrimary, 8);

        var port = config.Port;
        var lineHeight = ImGui.GetTextLineHeight();
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(16, (ControlHeight - lineHeight) / 2f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.SurfaceBorder);
        ImGui.SetNextItemWidth(MathF.Min(220, RetainerLiveChrome.Avail()));
        var edited = ImGui.InputInt("##retainerLivePort", ref port);
        ImGui.PopStyleColor(7);
        ImGui.PopStyleVar(3);
        if (edited)
        {
            config.Port = Math.Clamp(port, 1024, 65535);
            markChanged();
        }

        ImGui.SetCursorScreenPos(ImGui.GetCursorScreenPos() + new Vector2(0, 8 - Theme.ItemSpacing.Y));
        RetainerLiveChrome.Para("Saved port edits rebind automatically on the next framework tick. Valid ports: 1024–65535; no plugin reload is needed.", Theme.SecondaryScale, Theme.TextSecondary, 0);
    }

    private static float FooterHeight(float width)
    {
        var textBlock = RetainerLiveChrome.LineHeight() + 4 + RetainerLiveChrome.LineHeight(Theme.SecondaryScale);
        return width < 620
            ? 12 + textBlock + 12 + ControlHeight + 12
            : 12 + MathF.Max(textBlock, ControlHeight) + 12;
    }

    private static void DrawFooter(
        ImDrawListPtr draw,
        Vector2 winPos,
        Vector2 winSize,
        float gutter,
        Configuration config,
        RetainerLiveStatus status,
        UiState state,
        Action markChanged)
    {
        var width = winSize.X;
        var narrow = width < 620;
        var height = FooterHeight(width);
        var top = winSize.Y - height;
        var lh = RetainerLiveChrome.LineHeight();
        var smH = RetainerLiveChrome.LineHeight(Theme.SecondaryScale);
        var textBlock = lh + 4 + smH;

        draw.PushClipRect(winPos + new Vector2(0, top), winPos + winSize, false);
        draw.AddRectFilled(winPos + new Vector2(0, top), winPos + winSize, Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, top), winPos + new Vector2(width, top), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        var actionSize = new Vector2(MathF.Min(132, width - (2 * gutter)), ControlHeight);
        var actionPos = narrow
            ? new Vector2(gutter, top + height - 12 - ControlHeight)
            : new Vector2(width - gutter - actionSize.X, top + ((height - ControlHeight) / 2f));
        if (narrow)
            actionSize.X = width - (2 * gutter);
        var textRight = narrow ? width - gutter : actionPos.X - 16;
        var textX = gutter + 16;
        var textTop = narrow ? top + 12 : top + ((height - textBlock) / 2f);
        var textWidth = textRight - textX;

        // Concise first line (parentheticals stripped); the tooltip carries the full text when anything is elided.
        var headline = System.Text.RegularExpressions.Regex.Replace($"{Copy.Word(state)} · {status.Action}", @"\s*\([^)]*\)", "");
        var shownHeadline = RetainerLiveChrome.FitText(headline, textWidth);
        var shownDetail = RetainerLiveChrome.FitText(status.Detail, textWidth, Theme.SecondaryScale);
        draw.AddCircleFilled(
            winPos + new Vector2(gutter + 4, textTop + (lh / 2f)),
            4,
            Theme.Pack(Copy.MarkColor(state)));
        RetainerLiveChrome.Text(draw, winPos + new Vector2(textX, textTop), shownHeadline, Theme.TextPrimary);
        RetainerLiveChrome.Text(draw, winPos + new Vector2(textX, textTop + lh + 4), shownDetail, Theme.TextSecondary, Theme.SecondaryScale);
        if (shownHeadline != headline || shownDetail != status.Detail)
        {
            var min = winPos + new Vector2(textX, textTop);
            var max = winPos + new Vector2(textRight, textTop + textBlock);
            if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max))
                RetainerLiveChrome.Tip($"{headline}\n{status.Detail}");
        }

        if (RetainerLiveChrome.StopButton(draw, winPos + actionPos, actionSize, config.Enabled))
        {
            config.Enabled = false;
            markChanged();
        }
    }
}
