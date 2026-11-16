using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace LazyFishSitter;

internal class ConfigWindow : Window
{
    private const float ControlHeight = 36f;
    private const float DefaultHeight = 780f;
    private const float MinimumHeight = 480f;
    private readonly Plugin _plugin;

    private readonly record struct LiveStatus(UiState State, string Action, string? Reason);

    public ConfigWindow(Plugin plugin) : base("Lazy Fish Sitter##cfg")
    {
        _plugin = plugin;
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        Size = new Vector2(900, DefaultHeight);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, MinimumHeight),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void PreDraw()
    {
        // First-use height follows the screen so the footer stays on it at 1280x720; a saved size always wins.
        var screenHeight = ImGui.GetMainViewport().Size.Y;
        if (screenHeight > 0)
            Size = new Vector2(900, Math.Clamp(screenHeight - 80, MinimumHeight, DefaultHeight));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    }

    public override void PostDraw() => ImGui.PopStyleVar();

    private static float Gutter(float width) => width >= 760 ? 24 : width >= 560 ? 16 : 12;

    public override void Draw()
    {
        var config = _plugin.Config;
        var service = _plugin._service;
        var changed = false;
        var status = CurrentStatus(config, service);
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var gutter = Gutter(winSize.X);
        var compact = winSize.Y < 720;

        Theme.PushFrame();
        try
        {
            var y = DrawTopBar(winPos, winSize.X, gutter, compact, status.State);
            var footer = MeasureFooter(config, service, status, winSize.X, gutter);
            if (!compact)
            {
                y += DrawPageHeader(winPos + new Vector2(0, y), winSize.X, gutter, service);
            }

            ImGui.SetCursorScreenPos(winPos + new Vector2(0, y));
            var bodySize = new Vector2(winSize.X, MathF.Max(80, winSize.Y - footer.Height - y));
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(gutter, 16f));
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 10f);
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8f);
            ImGui.PushStyleColor(ImGuiCol.Border, 0u);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, 0u);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Theme.SurfaceBorder);
            ImGui.BeginChild("##fishSitterBody", bodySize, true);
            try
            {
                if (compact)
                {
                    var origin = ImGui.GetCursorScreenPos();
                    var width = FishSitterChrome.Avail();
                    var headerHeight = DrawHeaderBlock(origin, width, winSize.X, service);
                    ImGui.SetCursorScreenPos(origin);
                    ImGui.Dummy(new Vector2(width, headerHeight + 16 - Theme.ItemSpacing.Y));
                }
                changed |= DrawBody(config, service, status);
                DrawScrollFade();
            }
            finally
            {
                ImGui.EndChild();
                ImGui.PopStyleColor(4);
                ImGui.PopStyleVar(5);
            }

            DrawFooter(winPos + new Vector2(0, winSize.Y - footer.Height), winSize.X, gutter, config, footer, ref changed);
        }
        finally
        {
            Theme.PopFrame();
            if (changed)
                _plugin.SaveConfig();
        }
    }

    /// <summary>A short fade at the bottom of the body while more content is scrolled out of view.</summary>
    private static void DrawScrollFade()
    {
        if (ImGui.GetScrollMaxY() <= 0 || ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 1)
            return;
        var draw = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var bottom = ImGui.GetColorU32(ImGuiCol.WindowBg) | 0xFF000000u;
        var top = bottom & 0x00FFFFFFu;
        var left = min + new Vector2(0, size.Y - 28);
        draw.AddRectFilledMultiColor(left, min + size - new Vector2(10, 0), top, top, bottom, bottom);
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

    private float DrawTopBar(Vector2 winPos, float width, float gutter, bool compact, UiState state)
    {
        var draw = ImGui.GetWindowDrawList();
        var pad = compact ? 8f : 14f;
        var brandHeight = compact ? 36f : 40f;
        var brandSize = new Vector2(FishSitterChrome.BrandWidth(), brandHeight);
        var pillSize = new Vector2(FishSitterChrome.StatusPillWidth(), ControlHeight);
        var closeSize = ControlHeight;
        var inner = width - (2 * gutter);
        var stacked = brandSize.X + pillSize.X + closeSize + 16 > inner;
        var height = stacked ? pad + brandHeight + 8 + ControlHeight + pad : pad + brandHeight + pad;

        draw.AddRectFilled(winPos, winPos + new Vector2(width, height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(winPos + new Vector2(0, height), winPos + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder));
        var rowCenter = pad + (brandHeight / 2f);
        FishSitterChrome.Brand(draw, winPos + new Vector2(gutter, pad), brandSize);
        var closePos = winPos + new Vector2(width - gutter - closeSize, rowCenter - (closeSize / 2f));
        var pillPos = stacked
            ? winPos + new Vector2(gutter, pad + brandHeight + 8)
            : winPos + new Vector2(closePos.X - 8 - pillSize.X - winPos.X, rowCenter - (ControlHeight / 2f));
        FishSitterChrome.StatusPill(draw, pillPos, pillSize, state);
        if (FishSitterChrome.CloseButton(draw, closePos, closeSize))
            IsOpen = false;
        return height + 1;
    }

    /// <summary>The fixed page header: 20 px above and 8 px below, with no rule.</summary>
    private static float DrawPageHeader(Vector2 bandOrigin, float width, float gutter, FishSitService service)
    {
        var contentHeight = DrawHeaderBlock(bandOrigin + new Vector2(gutter, 20), width - (2 * gutter), width, service);
        var height = 20 + contentHeight + 8;
        return height;
    }

    /// <summary>Tile, title, description and the trip count. Returns the block height.</summary>
    private static float DrawHeaderBlock(Vector2 origin, float width, float windowWidth, FishSitService service)
    {
        var draw = ImGui.GetWindowDrawList();
        const float tile = 44;
        var wide = windowWidth >= 760;
        var count = $"{service.SendsThisTrip} sit {(service.SendsThisTrip == 1 ? "send" : "sends")} this trip";
        const string title = "Fish sitting";
        const string description = "Sits at a safe fishing beat, then stays out of the way until the character stands again.";
        const float scale = 1.35f;

        draw.AddRectFilled(origin, origin + new Vector2(tile), Theme.Pack(Theme.Accent with { W = 0.14f }), 2 * Theme.Unit);
        FishSitterChrome.Diamond(draw, origin + new Vector2(tile / 2f), 10, Theme.Pack(Theme.Accent));

        var textX = origin.X + 60;
        var countWidth = wide ? MathF.Min(FishSitterChrome.TextWidth(count, Theme.SecondaryScale), 0.4f * windowWidth) : 0;
        var textWidth = MathF.Max(40, width - 60 - (wide ? countWidth + 16 : 0));
        var shownTitle = FishSitterChrome.ClampLines(title, textWidth, scale, wide ? 1 : 2);
        var titleBlock = FishSitterChrome.WrappedHeight(shownTitle, textWidth, scale);
        var shownDescription = FishSitterChrome.ClampLines(description, textWidth, Theme.SecondaryScale, wide ? 1 : 3);
        var descriptionHeight = FishSitterChrome.WrappedHeight(shownDescription, textWidth, Theme.SecondaryScale);

        var y = origin.Y;
        FishSitterChrome.DrawWrapped(draw, new Vector2(textX, y), shownTitle, textWidth, Theme.TextPrimary, scale);
        y += titleBlock + 4;
        FishSitterChrome.DrawWrapped(draw, new Vector2(textX, y), shownDescription, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
        y += descriptionHeight;

        if (wide)
        {
            var shownCount = FishSitterChrome.FitText(count, countWidth, Theme.SecondaryScale);
            FishSitterChrome.DrawTextRight(draw, origin.X + width,
                origin.Y + ((FishSitterChrome.LineHeight(scale) - FishSitterChrome.LineHeight(Theme.SecondaryScale)) / 2f), shownCount, Theme.TextSecondary, Theme.SecondaryScale);
        }
        else
        {
            y += 4;
            FishSitterChrome.DrawText(draw, new Vector2(textX, y), FishSitterChrome.FitText(count, textWidth, Theme.SecondaryScale),
                Theme.TextSecondary, Theme.SecondaryScale);
            y += FishSitterChrome.LineHeight(Theme.SecondaryScale);
        }
        return MathF.Max(tile, y - origin.Y);
    }

    private static bool DrawBody(Configuration config, FishSitService service, LiveStatus status)
    {
        var changed = false;
        var lastSit = service.LastSitSentUtc == DateTime.MinValue
            ? "Never"
            : service.LastSitSentUtc.ToLocalTime().ToString("HH:mm:ss");
        var sends = service.SendsThisTrip;
        var capReached = status.State == UiState.Blocked && service.LastSkipReason.StartsWith("cap reached", StringComparison.OrdinalIgnoreCase);
        FishSitterChrome.StatCards(
            new FishSitterStat("Sit sends", sends.ToString(), null, null,
                capReached ? "Retry limit reached" : "Total this hole visit", capReached),
            new FishSitterStat("Fishing hole", service.TripActive ? "Active" : "None", null, null,
                service.TripActive ? "Trip counters are live" : "Start fishing to begin a trip"),
            new FishSitterStat("Posture", service.SitBelieved ? "Seated" : "Not seated", null, null,
                service.SitBelieved ? "No sit command needed" : service.SeatedReadWorksWithRodOut
                    ? "Rod-out read proven" : "Rod-out read not seen yet"));

        FishSitterChrome.Columns("body",
            () => FishSitterChrome.Card(() => DrawTripCard(service, lastSit, status), fill: true),
            () => FishSitterChrome.Card(() => DrawSettingsCard(config, ref changed), fill: true),
            640);
        return changed;
    }

    private static void DrawTripCard(FishSitService service, string lastSit, LiveStatus status)
    {
        var positive = Theme.StateGreen;
        FishSitterChrome.SectionTitle("This fishing trip");
        if (!service.TripActive)
        {
            FishSitterChrome.Para("No active fishing hole. Start fishing to populate the live trip details below.", Theme.SecondaryScale, Theme.TextSecondary);
            FishSitterChrome.Gap(8);
        }
        FishSitterChrome.KeyValue("Fishing hole", service.TripActive ? "Active" : "Not active", service.TripActive ? positive : null);
        FishSitterChrome.KeyValue("Seated", service.SitBelieved ? "Yes" : "No", service.SitBelieved ? positive : null);
        FishSitterChrome.KeyValue("Rod-out seated read", service.SeatedReadWorksWithRodOut ? "Proven" : "Not seen yet",
            service.SeatedReadWorksWithRodOut ? positive : null);
        FishSitterChrome.KeyValue("Last sit command", lastSit);
        FishSitterChrome.Gap(4);
        FishSitterChrome.Hairline();
        FishSitterChrome.Gap(12);
        // While setup is incomplete the policy's own last read is not what holds the command, so show the setup reason.
        var (summary, detail) = status.State == UiState.NeedsSetup && status.Reason is not null
            ? (status.Reason, null)
            : Describe(service.LastSkipReason);
        FishSitterChrome.Para("Last check", Theme.SecondaryScale, Theme.TextSecondary);
        FishSitterChrome.Gap(4);
        FishSitterChrome.Para(summary, 1f, Theme.TextPrimary);
        if (detail is not null)
        {
            FishSitterChrome.Gap(4);
            FishSitterChrome.Para(detail, Theme.SecondaryScale, Theme.TextSecondary);
        }
    }

    private static void DrawSettingsCard(Configuration config, ref bool changed)
    {
        FishSitterChrome.SectionTitle("Settings");
        var enabled = config.Enabled;
        if (FishSitterChrome.Toggle("Sit while fishing", ref enabled,
            "Uses the standby beat or a settled line, then leaves the character seated. Standing again re-arms the existing policy."))
        {
            config.Enabled = enabled;
            changed = true;
        }

        FishSitterChrome.Hairline();
        FishSitterChrome.Gap(12);
        ImGui.TextUnformatted("Sit command");
        FishSitterChrome.Gap(8);
        var command = config.SitCommand;
        var invalid = !string.IsNullOrWhiteSpace(command) && !command.StartsWith('/');
        var lineHeight = FishSitterChrome.LineHeight();
        ImGui.SetNextItemWidth(FishSitterChrome.Avail());
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(16, (ControlHeight - lineHeight) / 2f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2 * Theme.Unit);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.Border, invalid ? Theme.StateAmber : Theme.SurfaceBorder);
        if (ImGui.InputText("##fish_sitter_command", ref command, 64))
        {
            config.SitCommand = command.Trim();
            changed = true;
        }
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(3);
        FishSitterChrome.Gap(8);
        FishSitterChrome.Para("Default /sit. The command must start with a slash.", Theme.SecondaryScale,
            invalid ? Theme.StateAmber : Theme.TextSecondary);

        FishSitterChrome.Gap(12);
        FishSitterChrome.Hairline();
        FishSitterChrome.Gap(12);
        FishSitterChrome.Para($"Runs every frame. Sends stay at least 10 seconds apart and stop after {FishSitService.MaxSends} consecutive unaccepted sends in a fishing trip.",
            Theme.SecondaryScale, Theme.TextSecondary);
    }

    /// <summary>
    /// Display wording for the policy's last-check text: a capitalised summary, with any bracketed detail
    /// (game-read key=value pairs, counts) split off for a smaller muted line. The raw text is unchanged.
    /// </summary>
    internal static (string Summary, string? Detail) Describe(string raw)
    {
        var text = raw.Trim();
        string? detail = null;
        if (text.StartsWith('(') && text.EndsWith(')'))
            text = text[1..^1];
        else
        {
            var open = text.IndexOf(" (", StringComparison.Ordinal);
            if (open > 0 && text.EndsWith(')'))
            {
                detail = text[(open + 2)..^1];
                text = text[..open];
            }
        }

        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?<=\p{L})\(\d+\)", "").Replace(" - ", " — ");
        if (detail is not null)
        {
            var parts = detail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && parts.All(p => p.Contains('=')))
                detail = string.Join(" · ", parts);
        }
        return (Capitalise(text), detail);
    }

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private readonly record struct FooterLayout(
        float Height, bool Narrow, string Primary, string Secondary, string FullSecondary, string ActionLabel,
        float ActionWidth, float TextWidth, float PrimaryHeight, float SecondaryHeight, UiState State);

    private static FooterLayout MeasureFooter(Configuration config, FishSitService service, LiveStatus status, float width, float gutter)
    {
        var narrow = width < 620;
        var actionLabel = config.Enabled ? "Stop" : "Enable auto-sit";
        var actionWidth = narrow ? width - (2 * gutter) : FishSitterChrome.ActionWidth(96, "Stop", "Enable auto-sit");
        var textRight = narrow ? width - gutter : width - gutter - actionWidth - 16;
        var textWidth = MathF.Max(40, textRight - (gutter + 16));
        var primary = $"{Copy.Word(status.State)} · {status.Action}";
        var full = status.Reason is { } reason ? Capitalise(reason) : "Last check: " + Describe(service.LastSkipReason).Summary;
        var shown = FishSitterChrome.ClampLines(full, textWidth, Theme.SecondaryScale, narrow ? 2 : 1);
        var primaryHeight = FishSitterChrome.LineHeight();
        var secondaryHeight = FishSitterChrome.WrappedHeight(shown, textWidth, Theme.SecondaryScale);
        var block = primaryHeight + 4 + secondaryHeight;
        var height = narrow ? 12 + block + 12 + ControlHeight + 12 : 12 + MathF.Max(block, ControlHeight) + 12;
        return new FooterLayout(height, narrow, FishSitterChrome.FitText(primary, textWidth), shown, status.Reason is null ? service.LastSkipReason : full,
            actionLabel, actionWidth, textWidth, primaryHeight, secondaryHeight, status.State);
    }

    private static void DrawFooter(Vector2 origin, float width, float gutter, Configuration config, FooterLayout layout, ref bool changed)
    {
        var draw = ImGui.GetWindowDrawList();
        var block = layout.PrimaryHeight + 4 + layout.SecondaryHeight;
        var bandHeight = layout.Narrow ? layout.Height - 12 - ControlHeight - 12 : layout.Height;
        var textTop = origin.Y + (layout.Narrow ? 12 : (bandHeight - block) / 2f);
        var left = origin.X + gutter;
        // Full-bleed background and top hairline, clipped to the window rect so nothing leaks past the frame.
        draw.PushClipRect(origin, origin + new Vector2(width, layout.Height), false);
        draw.AddRectFilled(origin, origin + new Vector2(width, layout.Height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, 0.5f), origin + new Vector2(width, 0.5f), Theme.Pack(Theme.SurfaceBorder));
        draw.PopClipRect();

        var dotColor = Theme.Pack(FishSitterChrome.StateColor(layout.State));
        draw.AddCircleFilled(new Vector2(left + 4, textTop + (layout.PrimaryHeight / 2f)), Theme.DotRadius + 1, dotColor);
        FishSitterChrome.DrawText(draw, new Vector2(left + 16, textTop), layout.Primary, Theme.TextPrimary);
        FishSitterChrome.DrawWrapped(draw, new Vector2(left + 16, textTop + layout.PrimaryHeight + 4), layout.Secondary,
            layout.TextWidth, Theme.TextSecondary, Theme.SecondaryScale);

        var textMin = new Vector2(left + 16, textTop);
        var textMax = textMin + new Vector2(layout.TextWidth, block);
        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(textMin, textMax) && layout.FullSecondary.Length > 0)
            ImGui.SetTooltip(layout.FullSecondary);

        var actionPos = layout.Narrow
            ? new Vector2(left, origin.Y + layout.Height - 12 - ControlHeight)
            : new Vector2(origin.X + width - gutter - layout.ActionWidth, origin.Y + ((layout.Height - ControlHeight) / 2f));
        var enabled = config.Enabled;
        var actionSize = new Vector2(layout.ActionWidth, ControlHeight);
        var pressed = enabled
            ? FishSitterChrome.StopButton(draw, "footer_action", layout.ActionLabel, actionPos, actionSize)
            : FishSitterChrome.ActionButton(draw, "footer_action", layout.ActionLabel, actionPos, actionSize);
        if (pressed)
        {
            config.Enabled = !config.Enabled;
            changed = true;
        }
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, layout.Height));
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
