using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace LazyRetainerLive;

// The Dalamud Window base owns Begin/End. This class draws the complete content shell,
// including its integrated status bar, responsive body and reserved footer.
internal class ConfigWindow : Window
{
    private const float ControlHeight = 34f;
    private const float WideFooterHeight = 72f;
    private const float NarrowFooterHeight = 112f;

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
            DrawTopBar(state);
            DrawPageHeader(config, snapshot);

            var width = ImGui.GetContentRegionAvail().X;
            var footerHeight = FooterHeight(width);
            var bodyHeight = MathF.Max(80, ImGui.GetContentRegionAvail().Y - footerHeight);
            ImGui.BeginChild("##retainerLiveBody", new Vector2(0, bodyHeight), false);
            DrawBody(config, snapshot, fresh, httpError, status, () => changed = true);
            ImGui.EndChild();

            DrawFooter(config, status, state, () => changed = true);
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

    private void DrawTopBar(UiState state)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var gap = 2 * Theme.Unit;
        var brandSize = new Vector2(232, 42);
        var closeSize = new Vector2(ControlHeight);
        var closePos = new Vector2(width - closeSize.X, (66 - ControlHeight) / 2f);
        var stateLabel = Copy.Word(state);
        var statusSize = new Vector2(MathF.Max(92, ImGui.CalcTextSize(stateLabel).X + 43), ControlHeight);
        var singleRow = width >= brandSize.X + statusSize.X + closeSize.X + (2 * gap);
        var statusPos = singleRow
            ? new Vector2(closePos.X - gap - statusSize.X, closePos.Y)
            : new Vector2(width - statusSize.X, 62);
        var barHeight = singleRow ? 66f : 108f;

        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        RetainerLiveChrome.Brand(draw, origin + new Vector2(0, 12), brandSize);
        RetainerLiveChrome.StatusPill(draw, origin + statusPos, statusSize, stateLabel, state);
        if (RetainerLiveChrome.CloseButton(draw, origin + closePos, closeSize))
            IsOpen = false;
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private static void DrawPageHeader(Configuration config, CharInfo? snapshot)
    {
        var title = "Retainer live state";
        var description = "Publishes the current retainer table as read-only JSON on loopback.";
        var count = RetainerLivePresentation.LiveCount(snapshot);
        var subtext = RetainerLivePresentation.LiveSubtext(config.Enabled, config.Port);
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var tileSize = new Vector2(42);

        draw.AddRectFilled(origin, origin + tileSize, Theme.Pack(Theme.Accent with { W = 0.14f }), 2 * Theme.Unit);
        RetainerLiveChrome.Diamond(draw, origin + (tileSize / 2f), 10, Theme.Pack(Theme.Accent));

        if (width >= 720)
        {
            var textX = origin.X + tileSize.X + 12;
            var countWidth = ImGui.CalcTextSize(count).X;
            var subtextWidth = ImGui.CalcTextSize(subtext).X;
            RetainerLiveChrome.Text(
                draw,
                new Vector2(textX, origin.Y),
                RetainerLiveChrome.FitText(title, width - tileSize.X - 12 - countWidth - 16, Theme.TitleScale),
                Theme.TextPrimary,
                Theme.TitleScale);
            RetainerLiveChrome.Text(
                draw,
                new Vector2(textX, origin.Y + 29),
                RetainerLiveChrome.FitText(description, width - tileSize.X - 12 - subtextWidth - 16),
                Theme.TextSecondary);
            RetainerLiveChrome.TextRight(draw, origin.X + width, origin.Y + 4, count, Theme.TextSecondary);
            RetainerLiveChrome.TextRight(draw, origin.X + width, origin.Y + 31, subtext, Theme.TextSecondary);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, 58));
        }
        else
        {
            RetainerLiveChrome.Text(
                draw,
                origin + new Vector2(tileSize.X + 12, 4),
                RetainerLiveChrome.FitText(title, width - tileSize.X - 12, Theme.TitleScale),
                Theme.TextPrimary,
                Theme.TitleScale);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, tileSize.Y + 8));
            Widgets.SecondaryWrapped(description);
            Widgets.Caption(count);
            Widgets.Caption(subtext);
        }

        ImGui.Separator();
    }

    private static void DrawBody(
        Configuration config,
        CharInfo? snapshot,
        bool fresh,
        string? httpError,
        RetainerLiveStatus status,
        Action markChanged)
    {
        DrawStatCards(snapshot, fresh);
        TwoColumns(
            () =>
            {
                RetainerLiveChrome.Card(() => DrawEndpoint(config, snapshot, fresh, httpError, status));
                RetainerLiveChrome.Card(() => DrawSnapshotContinuity(snapshot, fresh));
            },
            () =>
            {
                RetainerLiveChrome.Card(() => DrawRequirements(config, snapshot, fresh, httpError));
                RetainerLiveChrome.Card(() => DrawSettings(config, markChanged));
            });
    }

    private static void TwoColumns(Action work, Action rail)
    {
        var available = ImGui.GetContentRegionAvail().X;
        if (available < 760)
        {
            work();
            rail();
            return;
        }

        var gap = 3 * Theme.Unit;
        var leftWidth = (available - gap) * 0.68f;
        ImGui.BeginChild("##retainerLiveWork", new Vector2(leftWidth, 0), false);
        work();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("##retainerLiveRail", new Vector2(0, 0), false);
        rail();
        ImGui.EndChild();
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

        var available = ImGui.GetContentRegionAvail().X;
        var stacked = available < 680;
        var gap = 3 * Theme.Unit;
        var width = stacked ? available : (available - (2 * gap)) / 3;
        const float height = 104;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();

        for (var i = 0; i < stats.Length; i++)
        {
            var stat = stats[i];
            var pos = origin + (stacked
                ? new Vector2(0, i * (height + gap))
                : new Vector2(i * (width + gap), 0));
            var size = new Vector2(width, height);
            draw.AddRectFilled(pos, pos + size, Theme.Pack(Theme.Surface), 8f);
            draw.AddRect(pos, pos + size, Theme.Pack(Theme.SurfaceBorder), 8f);
            RetainerLiveChrome.Diamond(draw, pos + new Vector2(20, 20), 5, Theme.Pack(Theme.Accent));

            ImGui.SetCursorScreenPos(pos + new Vector2(34, 11));
            Widgets.Caption(stat.Label);
            ImGui.SetCursorScreenPos(pos + new Vector2(12, 33));
            Widgets.Title(stat.Value);
            if (stat.Fraction is { } fraction)
            {
                var min = pos + new Vector2(12, 65);
                var max = pos + new Vector2(width - 12, 69);
                draw.AddRectFilled(min, max, Theme.Pack(Theme.SurfaceBorder), 2f);
                if (fraction > 0)
                {
                    draw.AddRectFilled(
                        min,
                        min + new Vector2((max.X - min.X) * Math.Clamp(fraction, 0, 1), 0),
                        Theme.Pack(Theme.Accent),
                        2f);
                }
            }

            ImGui.SetCursorScreenPos(pos + new Vector2(12, 82));
            Widgets.Caption(RetainerLiveChrome.FitText(stat.Note, width - 24, Theme.CaptionScale));
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(0, stacked ? stats.Length * (height + gap) : height + gap));
    }

    private static void DrawEndpoint(
        Configuration config,
        CharInfo? snapshot,
        bool fresh,
        string? httpError,
        RetainerLiveStatus status)
    {
        Section("Loopback endpoint", "The exact read-only surface consumed by the dashboard relay.");
        KeyValue("Method", "GET");
        KeyValue("Address", config.Enabled ? $"http://127.0.0.1:{config.Port}/retainers" : "Listener off");
        KeyValue("Payload", snapshot is null ? "503 until the first snapshot" : fresh ? "200 · current live snapshot" : "200 · last-known snapshot");
        if (snapshot is not null)
        {
            KeyValue("Character", snapshot.Char);
            KeyValue("World", snapshot.World);
        }

        ImGui.Separator();
        if (!string.IsNullOrEmpty(httpError) && config.Enabled)
        {
            Widgets.StateText("Failed", UiState.Failed);
            Widgets.SecondaryWrapped(httpError);
        }
        else
        {
            Widgets.StateText(Copy.Word(ToUiState(status.State)), ToUiState(status.State));
            Widgets.SecondaryWrapped(status.Detail);
        }
    }

    private static void DrawSnapshotContinuity(CharInfo? snapshot, bool fresh)
    {
        Section("Snapshot continuity", "The HTTP thread only reads the latest completed immutable payload.");
        if (snapshot is null)
        {
            Widgets.SecondaryWrapped("Needs setup — no retainer table has been published since plugin load. Log in; the endpoint answers 503 rather than inventing an empty character.");
            return;
        }

        Widgets.SecondaryWrapped(fresh
            ? "Ready — the relay receives the latest completed live retainer table, venture state, wallet and bag counts."
            : "Blocked — the current game table is unavailable, so the last-known payload stays served through logout and zone transitions without flapping to an empty response.");
        ImGui.Separator();
        KeyValue("Gil", snapshot.Gil.ToString("N0"));
        KeyValue("Venture coins", snapshot.Ventures.ToString("N0"));
        KeyValue("Company seals", snapshot.Seals.ToString("N0"));
    }

    private static void DrawRequirements(Configuration config, CharInfo? snapshot, bool fresh, string? httpError)
    {
        Section("Requirements", "What the current serving path needs.");
        Requirement(
            "Loopback listener",
            !config.Enabled ? "Idle" : !string.IsNullOrEmpty(httpError) ? "Failed" : "Ready",
            !config.Enabled ? UiState.Idle : !string.IsNullOrEmpty(httpError) ? UiState.Failed : UiState.Ready);
        Requirement(
            "Retainer snapshot",
            snapshot is null ? Copy.Missing("snapshot") : fresh ? "Ready" : "Last-known",
            snapshot is null ? UiState.Missing : fresh ? UiState.Ready : UiState.Blocked);
        Requirement("Network scope", "127.0.0.1 only", UiState.Ready);
        Requirement("Port rebind", config.Enabled ? "Next framework tick" : "On enable", UiState.Idle);
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

        ImGui.Separator();
        Widgets.Body("Loopback port");
        var port = config.Port;
        ImGui.SetNextItemWidth(MathF.Min(180, ImGui.GetContentRegionAvail().X));
        if (ImGui.InputInt("##retainerLivePort", ref port))
        {
            config.Port = Math.Clamp(port, 1024, 65535);
            markChanged();
        }
        Widgets.SecondaryWrapped("Saved port edits rebind automatically on the next framework tick. Valid ports: 1024–65535; no plugin reload is needed.");
    }

    private static void Section(string title, string? description = null)
    {
        Widgets.Title(title);
        if (!string.IsNullOrEmpty(description))
            Widgets.SecondaryWrapped(description);
    }

    private static void KeyValue(string label, string value)
    {
        var start = ImGui.GetCursorPos();
        var width = ImGui.GetContentRegionAvail().X;
        Widgets.Secondary(label);
        var valueWidth = ImGui.CalcTextSize(value).X;
        var labelWidth = ImGui.CalcTextSize(label).X;
        if (labelWidth + valueWidth + 32 <= width)
        {
            ImGui.SetCursorPos(new Vector2(start.X + width - valueWidth, start.Y));
            Widgets.Body(value);
        }
        else
        {
            Widgets.Body(value);
        }
    }

    private static void Requirement(string label, string value, UiState state)
    {
        var start = ImGui.GetCursorPos();
        var width = ImGui.GetContentRegionAvail().X;
        Widgets.StateText(state == UiState.Ready ? "✓" : "•", state);
        ImGui.SameLine();
        Widgets.Secondary(label);
        var valueWidth = ImGui.CalcTextSize(value).X;
        var labelWidth = ImGui.CalcTextSize(label).X;
        if (labelWidth + valueWidth + 40 <= width)
        {
            ImGui.SetCursorPos(new Vector2(start.X + width - valueWidth, start.Y));
            Widgets.Caption(value);
        }
        else
        {
            ImGui.SetCursorPosX(start.X + 24);
            Widgets.Caption(value);
        }
    }

    private static float FooterHeight(float width) => width < 620 ? NarrowFooterHeight : WideFooterHeight;

    private static void DrawFooter(Configuration config, RetainerLiveStatus status, UiState state, Action markChanged)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = FooterHeight(width);
        var narrow = width < 620;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));

        var actionSize = new Vector2(MathF.Min(132, width), 36);
        var actionPos = origin + new Vector2(width - actionSize.X, narrow ? 64 : 18);
        var textRight = narrow ? origin.X + width : actionPos.X - 16;
        var textPos = origin + new Vector2(24, 9);
        draw.AddCircleFilled(origin + new Vector2(10, 19), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(state)));
        RetainerLiveChrome.Text(
            draw,
            textPos,
            RetainerLiveChrome.FitText($"{Copy.Word(state)} · {status.Action}", textRight - textPos.X),
            Theme.TextPrimary);
        RetainerLiveChrome.Text(
            draw,
            textPos + new Vector2(0, 29),
            RetainerLiveChrome.FitText(status.Detail, textRight - textPos.X),
            Theme.TextSecondary);

        if (RetainerLiveChrome.StopButton(draw, actionPos, actionSize, config.Enabled))
        {
            config.Enabled = false;
            markChanged();
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
    }
}
