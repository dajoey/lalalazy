using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace LazyFishSitter;

internal class ConfigWindow : Window
{
    private const float ControlHeight = 34f;
    private const float WideFooterHeight = 72f;
    private const float NarrowFooterHeight = 108f;
    private readonly Plugin _plugin;

    private readonly record struct LiveStatus(UiState State, string Action, string? Reason);

    public ConfigWindow(Plugin plugin) : base("Lazy Fish Sitter##cfg")
    {
        _plugin = plugin;
        Flags = ImGuiWindowFlags.NoTitleBar;
        Size = new Vector2(900, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 480),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        var config = _plugin.Config;
        var service = _plugin._service;
        var changed = false;
        var status = CurrentStatus(config, service);

        Theme.PushFrame();
        try
        {
            DrawTopBar(status.State);
            DrawFeatureRow();
            DrawPageHeader(service);

            var width = ImGui.GetContentRegionAvail().X;
            var footerHeight = FooterHeight(width);
            ImGui.BeginChild("##fishSitterBody", new Vector2(0, MathF.Max(80, ImGui.GetContentRegionAvail().Y - footerHeight)), false);
            changed |= DrawBody(config, service);
            ImGui.EndChild();

            DrawFooter(config, service, status, ref changed);
        }
        finally
        {
            Theme.PopFrame();
            if (changed)
                _plugin.SaveConfig();
        }
    }

    private static LiveStatus CurrentStatus(Configuration config, FishSitService service)
    {
        if (!config.Enabled)
            return new LiveStatus(UiState.NeedsSetup, "Auto-sit is off", "Enable auto-sit to resume the same saved command.");
        if (!string.IsNullOrWhiteSpace(config.SitCommand) && !config.SitCommand.StartsWith('/'))
            return new LiveStatus(UiState.NeedsSetup, "Correct the sit command", "The command must start with a slash.");
        if (!service.TripActive)
            return new LiveStatus(UiState.Idle, "Waiting for a fishing hole", null);
        if (service.SitBelieved)
            return new LiveStatus(UiState.Ready, "Seated for this fishing trip", null);

        var block = SitBlockReason();
        if (block is not null)
            return new LiveStatus(UiState.Blocked, "Sit command held", block);
        if (service.LastSkipReason.StartsWith("cap reached", StringComparison.OrdinalIgnoreCase))
            return new LiveStatus(UiState.Blocked, "Sit retry limit reached", "Leave the fishing hole to reset the retry limit.");
        return new LiveStatus(UiState.Ready, "Armed for a safe sit window", null);
    }

    private static void DrawTopBar(UiState state)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var gap = 2 * Theme.Unit;
        var brandSize = new Vector2(MathF.Max(216, (ImGui.CalcTextSize("LazyFishSitter").X * Theme.TitleScale) + 64), 40);
        var stateLabel = Copy.Word(state);
        var statusSize = new Vector2(MathF.Max(82, ImGui.CalcTextSize(stateLabel).X + 43), ControlHeight);
        var stacked = width < brandSize.X + statusSize.X + (3 * gap);
        var barHeight = stacked ? 104f : 58f;
        var brandPos = origin + new Vector2(0, 9);
        var statusPos = stacked
            ? origin + new Vector2(0, 59)
            : origin + new Vector2(width - statusSize.X, (barHeight - ControlHeight) / 2f);

        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        FishSitterChrome.Brand(draw, brandPos, brandSize);
        FishSitterChrome.StatusPill(draw, statusPos, statusSize, state);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private static void DrawFeatureRow()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        const float barHeight = 52;
        var tabWidth = MathF.Min(width, MathF.Max(132, ImGui.CalcTextSize("Fish sitting").X + 52));
        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        FishSitterChrome.FeatureTab(draw, "Fish sitting", origin + new Vector2(0, 9), new Vector2(tabWidth, ControlHeight));
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private static void DrawPageHeader(FishSitService service)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var tileSize = new Vector2(42);
        var count = $"{service.SendsThisTrip} sit {(service.SendsThisTrip == 1 ? "send" : "sends")} this trip";
        const string title = "Fish sitting";
        const string description = "Sits at a safe fishing beat, then stays out of the way until the character stands again.";

        draw.AddRectFilled(origin, origin + tileSize, Theme.Pack(Theme.Accent with { W = 0.14f }), 2 * Theme.Unit);
        FishSitterChrome.Diamond(draw, origin + (tileSize / 2f), 10, Theme.Pack(Theme.Accent));
        if (width >= 620)
        {
            var textX = origin.X + tileSize.X + 12;
            var countWidth = ImGui.CalcTextSize(count).X * Theme.CaptionScale;
            FishSitterChrome.DrawText(draw, new Vector2(textX, origin.Y),
                FishSitterChrome.FitText(title, width - tileSize.X - 12 - countWidth - 16, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            FishSitterChrome.DrawText(draw, new Vector2(textX, origin.Y + 28),
                FishSitterChrome.FitText(description, width - tileSize.X - 12), Theme.TextSecondary);
            FishSitterChrome.DrawTextRight(draw, origin.X + width, origin.Y + 5, count, Theme.TextSecondary, Theme.CaptionScale);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, 56));
        }
        else
        {
            FishSitterChrome.DrawText(draw, origin + new Vector2(tileSize.X + 12, 4),
                FishSitterChrome.FitText(title, width - tileSize.X - 12, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, tileSize.Y + 6));
            Widgets.SecondaryWrapped(description);
            Widgets.Caption(count);
        }
        ImGui.Separator();
    }

    private static bool DrawBody(Configuration config, FishSitService service)
    {
        var changed = false;
        var lastSit = service.LastSitSentUtc == DateTime.MinValue
            ? "Never"
            : service.LastSitSentUtc.ToLocalTime().ToString("HH:mm:ss");
        FishSitterChrome.StatCards(
            new FishSitterStat("Sit sends", service.SendsThisTrip.ToString(), "Total this hole visit"),
            new FishSitterStat("Fishing hole", service.TripActive ? "Active" : "None",
                service.TripActive ? "Trip counters are live" : "Start fishing to begin a trip"),
            new FishSitterStat("Posture", service.SitBelieved ? "Seated" : "Not seated",
                service.SitBelieved ? "No sit command needed" : service.SeatedReadWorksWithRodOut
                    ? "Rod-out seated read proven" : "Rod-out seated read not seen yet"));

        if (ImGui.GetContentRegionAvail().X >= 720)
        {
            var origin = ImGui.GetCursorScreenPos();
            var available = ImGui.GetContentRegionAvail().X;
            var gap = 3 * Theme.Unit;
            var leftWidth = (available - gap) * 0.58f;
            FishSitterChrome.Card(width => DrawTripCard(service, lastSit, width), leftWidth);
            var leftBottom = ImGui.GetCursorScreenPos().Y;
            ImGui.SetCursorScreenPos(origin + new Vector2(leftWidth + gap, 0));
            FishSitterChrome.Card(width => DrawSettingsCard(config, width, ref changed), available - leftWidth - gap);
            var rightBottom = ImGui.GetCursorScreenPos().Y;
            ImGui.SetCursorScreenPos(new Vector2(origin.X, MathF.Max(leftBottom, rightBottom)));
        }
        else
        {
            FishSitterChrome.Card(width => DrawTripCard(service, lastSit, width));
            FishSitterChrome.Card(width => DrawSettingsCard(config, width, ref changed));
        }
        return changed;
    }

    private static void DrawTripCard(FishSitService service, string lastSit, float width)
    {
        FishSitterChrome.SectionTitle("This fishing trip");
        if (!service.TripActive)
            Widgets.SecondaryWrapped("No active fishing hole. Start fishing to populate the live trip details below.");
        FishSitterChrome.KeyValue("Fishing hole", service.TripActive ? "Active" : "Not active", width);
        FishSitterChrome.KeyValue("Seated", service.SitBelieved ? "Yes" : "No", width);
        FishSitterChrome.KeyValue("Rod-out seated read", service.SeatedReadWorksWithRodOut ? "Proven" : "Not seen yet", width);
        FishSitterChrome.KeyValue("Last sit command", lastSit, width);
        ImGui.Separator();
        Widgets.Caption("LAST CHECK");
        Widgets.SecondaryWrapped(service.LastSkipReason);
    }

    private static void DrawSettingsCard(Configuration config, float width, ref bool changed)
    {
        FishSitterChrome.SectionTitle("Settings");
        var enabled = config.Enabled;
        if (FishSitterChrome.Toggle("Sit while fishing", ref enabled,
            "Uses the standby beat or a settled line, then leaves the character seated. Standing again re-arms the existing policy.", width))
        {
            config.Enabled = enabled;
            changed = true;
        }

        ImGui.Separator();
        Widgets.Body("Sit command");
        var command = config.SitCommand;
        ImGui.SetNextItemWidth(width);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2 * Theme.Unit);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.SurfaceBorder);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.SurfaceBorder);
        if (ImGui.InputText("##fish_sitter_command", ref command, 64))
        {
            config.SitCommand = command.Trim();
            changed = true;
        }
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
        Widgets.SecondaryWrapped("Default /sit. The command must start with a slash.");

        ImGui.Separator();
        Widgets.SecondaryWrapped($"Runs every frame. Sends stay at least 10 seconds apart and stop after {FishSitService.MaxSends} consecutive unaccepted sends in a fishing trip.");
    }

    private static float FooterHeight(float width) => width < 560 ? NarrowFooterHeight : WideFooterHeight;

    private static void DrawFooter(Configuration config, FishSitService service, LiveStatus status, ref bool changed)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = FooterHeight(width);
        var draw = ImGui.GetWindowDrawList();
        var narrow = width < 560;
        var actionLabel = config.Enabled ? "Stop" : "Enable auto-sit";
        var actionWidth = MathF.Min(width, MathF.Max(104, ImGui.CalcTextSize(actionLabel).X + 44));
        var actionPos = origin + new Vector2(width - actionWidth, narrow ? 62 : 18);
        var textRight = narrow ? origin.X + width : actionPos.X - 16;
        var textPos = origin + new Vector2(24, 10);
        var primary = $"{Copy.Word(status.State)} · {status.Action}";
        var secondary = status.Reason ?? "Last check: " + service.LastSkipReason;

        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        draw.AddCircleFilled(origin + new Vector2(10, 20), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(status.State)));
        FishSitterChrome.DrawText(draw, textPos, FishSitterChrome.FitText(primary, textRight - textPos.X), Theme.TextPrimary);
        FishSitterChrome.DrawText(draw, textPos + new Vector2(0, 27), FishSitterChrome.FitText(secondary, textRight - textPos.X), Theme.TextSecondary);
        if (FishSitterChrome.ActionButton(draw, "footer_action", actionLabel, actionPos, new Vector2(actionWidth, 34), config.Enabled))
        {
            config.Enabled = !config.Enabled;
            changed = true;
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
    }

    /// <summary>Display-side read of the conditions that make injecting a command unsafe.</summary>
    private static string? SitBlockReason()
    {
        if (Plugin.Condition[ConditionFlag.BetweenAreas]) return "between areas";
        if (Plugin.Condition[ConditionFlag.OccupiedInEvent]) return "occupied in event";
        if (Plugin.Condition[ConditionFlag.OccupiedInQuestEvent]) return "occupied in quest event";
        if (Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]) return "occupied in cutscene event";
        if (Plugin.Condition[ConditionFlag.WatchingCutscene]) return "watching cutscene";
        if (Plugin.Condition[ConditionFlag.InCombat]) return "in combat";
        if (Plugin.Condition[ConditionFlag.Emoting]) return "mid-emote";
        if (Plugin.Condition[ConditionFlag.Jumping]) return "jumping";
        return null;
    }
}
