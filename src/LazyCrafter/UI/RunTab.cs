using Dalamud.Bindings.ImGui;
using LalaUiKit;
using LazyCrafter.Adapters;
using LazyCrafter.Core;

namespace LazyCrafter.UI;

/// <summary>Snapshot-only run view. Buttons call the existing dispatch, shopping and report paths.</summary>
public sealed class RunTab
{
    private readonly Plugin _plugin;
    private readonly MainWindow _main;

    private RunSnapshot? _groupedFor;
    private List<(string Where, List<BlockedItem> Items)> _vendorGroups = new();
    private List<BlockedItem> _marketItems = new();
    private DateTime _copiedAt = DateTime.MinValue;

    public RunTab(Plugin plugin, MainWindow main)
    {
        _plugin = plugin;
        _main = main;
    }

    public static string TabLabel(RunSnapshot snapshot) => snapshot.State == RunState.Idle ? "Run###tab-run" : $"Run - {snapshot.PhaseLabel}###tab-run";

    /// <summary>Elapsed is live while running and frozen at EndedAt afterwards.</summary>
    public static TimeSpan ElapsedFor(RunSnapshot snapshot)
    {
        if (snapshot.StartedAt == DateTime.MinValue)
            return snapshot.Elapsed;
        if (snapshot.State == RunState.Running)
            return DateTime.UtcNow - snapshot.StartedAt;
        if (snapshot.EndedAt is { } end)
            return end - snapshot.StartedAt;
        return snapshot.Elapsed;
    }

    private string Name(uint itemId) => _plugin.GameData?.ItemName(itemId) ?? $"#{itemId}";

    public void Draw()
    {
        var dispatch = _plugin.Dispatch;
        var snapshot = dispatch.Snapshot;
        var elapsed = ElapsedFor(snapshot);
        var done = snapshot.Steps.Count(step => step.State == StepState.Done);

        CrafterChrome.DrawStatCards(
            new CrafterChrome.Stat("Phase", snapshot.State == RunState.Idle ? "Idle" : snapshot.PhaseLabel, null,
                snapshot.State == RunState.Idle ? "No dispatch has started" : snapshot.Pass > 0 ? $"Pass {snapshot.Pass}" : "First pass", null),
            new CrafterChrome.Stat("Elapsed", snapshot.State == RunState.Idle ? "—" : RunReport.Elapsed(elapsed), null,
                snapshot.StartedAt == DateTime.MinValue ? "No start time" : $"Started {snapshot.StartedAt.ToLocalTime():HH:mm:ss}", null),
            new CrafterChrome.Stat("Steps", done.ToString(), snapshot.Steps.Count.ToString(),
                snapshot.Steps.Count == 0 ? "No recorded steps" : $"{snapshot.Steps.Count - done} not done",
                snapshot.Steps.Count == 0 ? null : done / (float)snapshot.Steps.Count));

        if (snapshot.State == RunState.Idle)
        {
            CrafterChrome.Card("run_empty", () =>
            {
                CrafterChrome.Section("No active run");
                CrafterChrome.EmptyState("Nothing is running", Capitalize(RunReport.Headline(snapshot)) + " Build or reopen the saved cart to dispatch a plan.");
            }, minHeight: 240);
            return;
        }

        DrawSummary(snapshot);
        DrawToolbar(dispatch, snapshot, elapsed);
        CrafterChrome.Columns("run", () => DrawSteps(snapshot),
            () =>
            {
                if (snapshot.State == RunState.Blocked && snapshot.Blocked.Count > 0)
                {
                    DrawBlocked(snapshot);
                    DrawRunDetails(snapshot);
                }
                else
                {
                    DrawRunDetails(snapshot);
                    DrawNextAction(snapshot);
                }
            }, railFraction: 0.68f);
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private void DrawSummary(RunSnapshot snapshot)
    {
        var what = snapshot.CartNames.Count > 0 ? string.Join(", ", snapshot.CartNames) : snapshot.What;
        CrafterChrome.Card("run_summary", () =>
        {
            if (!string.IsNullOrEmpty(what))
            {
                CrafterChrome.Section("Current cart", what);
            }
            if (!string.IsNullOrEmpty(snapshot.Status))
                Banner(snapshot.Status, snapshot.State == RunState.Failed ? UiState.Failed : RunUiState(snapshot.State));
            if (!string.IsNullOrEmpty(snapshot.StoppedReason) && !string.Equals(snapshot.StoppedReason, snapshot.Status, StringComparison.Ordinal))
                Banner(snapshot.StoppedReason, snapshot.State == RunState.Blocked ? UiState.Blocked : UiState.Failed);
        });
    }

    private static void Banner(string text, UiState state)
    {
        var split = text.IndexOf(':');
        if (split > 0 && split < 40)
            CrafterChrome.Notice(text[..split], text[(split + 1)..].Trim(), state);
        else
            CrafterChrome.Notice("", text, state);
    }

    private void DrawToolbar(DispatchService dispatch, RunSnapshot snapshot, TimeSpan elapsed)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = CrafterChrome.Avail();
        var running = snapshot.State == RunState.Running;
        if (CrafterChrome.Button("stop", "Stop", disabled: !running))
            dispatch.Stop();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && running)
            ImGui.SetTooltip("Abort the run while keeping its plan for Resume.");

        CrafterChrome.WrapNext(CrafterChrome.ButtonWidth("Resume"));
        if (CrafterChrome.Button("resume", "Resume", snapshot.CanResume ? CrafterChrome.ButtonKind.Primary : CrafterChrome.ButtonKind.Normal, !snapshot.CanResume))
            dispatch.Resume();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (snapshot.CanResume)
                ImGui.SetTooltip("Re-plan from current bags and continue the same cart.");
            else
                ImGui.SetTooltip(snapshot.State == RunState.Running ? "Resume is for a stopped run." : "Nothing to resume. Dispatch the cart again.");
        }

        CrafterChrome.WrapNext(CrafterChrome.ButtonWidth("Copy report"));
        if (CrafterChrome.Button("copy", "Copy report"))
        {
            ImGui.SetClipboardText(RunReport.Render(snapshot, elapsed));
            _copiedAt = DateTime.UtcNow;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copy every step, reason and blocker as plain text.");
        if ((DateTime.UtcNow - _copiedAt).TotalSeconds < 3)
        {
            ImGui.SameLine();
            var pos = ImGui.GetCursorScreenPos();
            var draw = ImGui.GetWindowDrawList();
            CrafterChrome.DrawText(draw, pos + new System.Numerics.Vector2(Theme.Unit, (CrafterChrome.ControlHeight - CrafterChrome.LineHeight()) / 2f), "Copied", Theme.StateGreen);
            ImGui.Dummy(new System.Numerics.Vector2(CrafterChrome.TextWidth("Copied") + Theme.Unit, CrafterChrome.ControlHeight));
        }
        CrafterChrome.Advance(origin, width, ImGui.GetItemRectMax().Y - origin.Y, CrafterChrome.Gap);
    }

    private void DrawRunDetails(RunSnapshot snapshot)
    {
        CrafterChrome.Card("run_details", () =>
        {
            CrafterChrome.Section("Run details");
            var state = RunUiState(snapshot.State);
            CrafterChrome.KeyValue("State", Copy.Word(state), false, Copy.MarkColor(state));
            if (snapshot.StartedAt != DateTime.MinValue)
                CrafterChrome.KeyValue("Started", snapshot.StartedAt.ToLocalTime().ToString("HH:mm:ss"));
            if (snapshot.EndedAt is { } end)
                CrafterChrome.KeyValue("Ended", end.ToLocalTime().ToString("HH:mm:ss"));
            if (snapshot.Pass > 1)
                CrafterChrome.KeyValue("Pass", snapshot.Pass.ToString());
            CrafterChrome.KeyValue("Cart items", snapshot.CartNames.Count.ToString());
            CrafterChrome.KeyValue("Blocked items", snapshot.Blocked.Count.ToString());
        });
    }

    private void DrawNextAction(RunSnapshot snapshot)
    {
        CrafterChrome.Card("run_next_action", () =>
        {
            CrafterChrome.Section("Next action");
            switch (snapshot.State)
            {
                case RunState.Done:
                    CrafterChrome.Notice("Run finished", "The report remains available until the next dispatch.", UiState.Ready);
                    break;
                case RunState.Failed:
                    CrafterChrome.Notice("Run failed", "Read the failed step and reason, then copy the report or resume when the cause is fixed.", UiState.Failed);
                    break;
                case RunState.Running:
                    CrafterChrome.Notice("Dispatcher running", "It is following the current plan. Stop remains available in the footer.", UiState.Running);
                    break;
                default:
                    CrafterChrome.Notice("Run blocked", "Resolve the named step, then use Resume when the plan can continue.", UiState.Blocked);
                    break;
            }
        });
    }

    // ---- the "Required before Resume" ledger: one nested card per stop, action on the right of its heading ----

    private bool GroupHeader(string id, string title, string? sub, string? action, string? tooltip)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = CrafterChrome.Avail();
        var buttonWidth = action is null ? 0 : CrafterChrome.SmallButtonWidth(action);
        var textWidth = width - (buttonWidth > 0 ? buttonWidth + CrafterChrome.Gap : 0);
        CrafterChrome.DrawFitted(draw, pos, title, textWidth, Theme.TextPrimary);
        var height = CrafterChrome.LineHeight();
        if (!string.IsNullOrEmpty(sub))
        {
            height += Theme.Unit;
            height += CrafterChrome.DrawWrapped(draw, pos + new System.Numerics.Vector2(0, height), sub, textWidth, Theme.TextSecondary, Theme.SecondaryScale);
        }
        var clicked = false;
        if (action is not null)
        {
            ImGui.SetCursorScreenPos(pos + new System.Numerics.Vector2(width - buttonWidth, 0));
            clicked = CrafterChrome.SmallButton(id, action);
            if (tooltip is not null && ImGui.IsItemHovered())
                ImGui.SetTooltip(tooltip);
        }
        CrafterChrome.Advance(pos, width, height, 2 * Theme.Unit);
        return clicked;
    }

    private static void ItemRow(string name, string? right)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = CrafterChrome.Avail();
        var rightWidth = string.IsNullOrEmpty(right) ? 0 : MathF.Min(width * 0.5f, CrafterChrome.TextWidth(right, Theme.SecondaryScale));
        if (rightWidth > 0)
            CrafterChrome.DrawFittedRight(draw, pos.X + width, pos.Y + ((CrafterChrome.LineHeight() - CrafterChrome.LineHeight(Theme.SecondaryScale)) / 2f), right!, rightWidth + 1, Theme.TextSecondary, Theme.SecondaryScale);
        var used = CrafterChrome.WrappedHeight(name, width - rightWidth - CrafterChrome.Gap);
        CrafterChrome.DrawWrapped(draw, pos, name, width - rightWidth - CrafterChrome.Gap, Theme.TextPrimary);
        CrafterChrome.Advance(pos, width, used, Theme.Unit);
    }

    private void DrawBlocked(RunSnapshot snapshot)
    {
        if (!ReferenceEquals(_groupedFor, snapshot))
        {
            _groupedFor = snapshot;
            _marketItems = snapshot.Blocked.Where(item => item.Kind == StepKind.Market).ToList();
            _vendorGroups = snapshot.Blocked.Where(item => item.Kind == StepKind.Vendor)
                .GroupBy(item => item.Where ?? "")
                .Select(group => (group.Key, group.ToList()))
                .ToList();
        }

        CrafterChrome.Card("run_blocked", () =>
        {
            CrafterChrome.Section("Required before Resume", "Complete the named shopping or retrieval steps, then resume the same cart.");

            if (_marketItems.Count > 0)
            {
                long total = 0;
                var complete = true;
                foreach (var item in _marketItems)
                {
                    if (item.EstimatedGil is { } gil)
                        total += gil;
                    else
                        complete = false;
                }
                CrafterChrome.Card("blocked_market", () =>
                {
                    if (GroupHeader("market", "Market board", $"Estimated {(complete ? "" : ">")}{total:N0} gil", "Open market board",
                            "Print the list and use the existing Lifestream market-board route."))
                        _plugin.Dispatch.Lifestream.GoToMarket(_marketItems.Select(item => (item.ItemId, item.Quantity)).ToList(), Name, _plugin.Catalog.UnitCost);
                    foreach (var item in _marketItems)
                        ItemRow($"{item.Name} x{item.Quantity}", item.EstimatedGil is { } estimate ? $"~{estimate:N0} gil" : null);
                }, CrafterChrome.CardTone.Row);
            }

            var vendorIndex = 0;
            foreach (var (where, items) in _vendorGroups)
            {
                var index = vendorIndex++;
                CrafterChrome.Card("blocked_vendor_" + index, () =>
                {
                    var placed = !string.IsNullOrEmpty(where);
                    if (GroupHeader("vendor" + index, "Gil vendor", placed ? where : "No placed vendor found", placed ? "Flag on map" : null,
                            "Set the map flag and print the shopping list. No teleport."))
                        FlagVendor(items);
                    foreach (var item in items)
                        ItemRow($"{item.Name} x{item.Quantity}", null);
                }, CrafterChrome.CardTone.Row);
            }

            var currencyIndex = 0;
            foreach (var item in snapshot.Blocked.Where(item => item.Kind == StepKind.CurrencyShop))
            {
                var index = currencyIndex++;
                var captured = item;
                CrafterChrome.Card("blocked_currency_" + index, () =>
                {
                    if (GroupHeader("currency" + index, "Currency shop", $"Trade for {captured.Name} x{captured.Quantity} at {captured.Where}", "Flag on map",
                            "Set the map flag and print the price. No teleport or automatic trade."))
                        FlagCurrencyShop(captured);
                }, CrafterChrome.CardTone.Row);
            }

            var others = snapshot.Blocked.Where(item => item.Kind is not (StepKind.Market or StepKind.Vendor or StepKind.CurrencyShop)).ToList();
            foreach (var group in others.GroupBy(item => item.Kind))
            {
                var kind = group.Key;
                CrafterChrome.Card("blocked_other_" + kind, () =>
                {
                    GroupHeader("other" + kind, Capitalize(RunReport.KindName(kind)), null, null, null);
                    foreach (var item in group)
                        ItemRow($"{item.Name} x{item.Quantity}", string.IsNullOrEmpty(item.Where) ? null : item.Where);
                }, CrafterChrome.CardTone.Row);
            }

            if (snapshot.CanResume)
                CrafterChrome.Paragraph("Then press Resume or use /lcraft resume.", Theme.SecondaryScale);
        });
    }

    private void FlagVendor(List<BlockedItem> items)
    {
        var stops = _plugin.Dispatch.Vendors.Plan(
            items.Select(item => (item.ItemId, item.Quantity)).ToList(), out var unlocated, _plugin.Dispatch.Here());
        if (stops.Count == 0)
        {
            Plugin.ChatGui.PrintError("[LazyCrafter] no placed gil vendor found for " + string.Join(", ", items.Select(item => item.Name)) + ".");
            return;
        }
        foreach (var (where, group) in stops)
            _plugin.Dispatch.Lifestream.GoToVendor(where, group, Name, teleport: false);
        if (unlocated.Count > 0)
            Plugin.ChatGui.PrintError("[LazyCrafter] no placed gil vendor found for " + string.Join(", ", unlocated.Select(item => Name(item.ItemId))) + ".");
    }

    private void FlagCurrencyShop(BlockedItem item)
    {
        var gameData = _plugin.GameData;
        var offers = gameData is null ? Array.Empty<SpecialShopOffer>() : gameData.SpecialShopOffers(item.ItemId);
        var resolved = offers.Count == 0
            ? Array.Empty<SpecialShopCandidate>()
            : _plugin.Dispatch.Vendors.SpecialShopCandidates(item.ItemId, offers);
        var best = SpecialShopChoice.Best(resolved, item.Quantity, _plugin.Inventory.CurrencyBalance, _plugin.Dispatch.Here())
                   ?? (resolved.Count > 0 ? SpecialShopChoice.Named(resolved, item.Quantity, 1).FirstOrDefault() : null);
        if (best is null)
        {
            Plugin.ChatGui.PrintError($"[LazyCrafter] no placed currency vendor found for {item.Name} any more.");
            return;
        }
        _plugin.Dispatch.Lifestream.GoToCurrencyShop(best.Where.TerritoryId, best.Where.MapId, best.Where.MapX, best.Where.MapY,
            $"{best.NpcName} ({best.TerritoryName} {best.Where.MapX:0.0}, {best.Where.MapY:0.0}): {item.Name} x{item.Quantity} for {best.PriceFor(item.Quantity)}");
    }

    private static void DrawSteps(RunSnapshot snapshot)
    {
        CrafterChrome.Card("run_steps", () =>
        {
            CrafterChrome.Section("Steps", "Every dispatch step, its current state and the latest reason.", $"{snapshot.Steps.Count} steps");
            if (snapshot.Steps.Count == 0)
            {
                CrafterChrome.Paragraph("No steps have been recorded for this run.");
                return;
            }
            var flags = ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
            var width = CrafterChrome.Avail();
            if (!ImGui.BeginTable("##crafter_run_steps", 5, flags, new System.Numerics.Vector2(width, 0)))
                return;
            ImGui.PushTextWrapPos(0f);
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new System.Numerics.Vector2(Theme.Unit * 2, Theme.Unit * 2));
            var kindWidth = snapshot.Steps.Max(step => CrafterChrome.TextWidth(Capitalize(RunReport.KindName(step.Kind)))) + (3 * Theme.Unit);
            ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthFixed, MathF.Max(kindWidth, CrafterChrome.TextWidth("Step") + 16));
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 1.5f);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, CrafterChrome.TextWidth("888") + (4 * Theme.Unit));
            ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, CrafterChrome.TextWidth("Pending") + (4 * Theme.Unit));
            ImGui.TableSetupColumn("Reason / status", ImGuiTableColumnFlags.WidthStretch, 3f);
            CrafterChrome.HeadersRow(["Step", "Item", "Qty", "State", "Reason / status"], 2);
            var index = 0;
            foreach (var step in snapshot.Steps)
            {
                ImGui.TableNextRow();
                if (step.State == StepState.Running)
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(Theme.StateAmber with { W = 0.14f }));
                ImGui.PushID(index++);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(Capitalize(RunReport.KindName(step.Kind)));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(step.Name);
                ImGui.TableNextColumn();
                CrafterChrome.CellRight(step.Quantity.ToString(), Theme.TextPrimary);
                ImGui.TableNextColumn();
                ImGui.TextColored(StateColor(step.State), Capitalize(RunReport.StateName(step.State)));
                ImGui.TableNextColumn();
                var reason = step.Reason ?? "";
                var external = step.ExternalStatus ?? "";
                if (reason.Length > 0)
                    ImGui.TextColored(step.State == StepState.Failed ? Copy.MarkColor(UiState.Failed) : step.State == StepState.Blocked ? Copy.MarkColor(UiState.Blocked) : Theme.TextSecondary, reason);
                if (external.Length > 0)
                    ImGui.TextColored(Copy.MarkColor(UiState.Blocked), external);
                if (reason.Length == 0 && external.Length == 0)
                    ImGui.TextDisabled("-");
                ImGui.PopID();
            }
            ImGui.PopStyleVar();
            ImGui.PopTextWrapPos();
            ImGui.EndTable();
        }, fill: true);
    }

    private static UiState RunUiState(RunState state) => state switch
    {
        RunState.Running => UiState.Running,
        RunState.Blocked => UiState.Blocked,
        RunState.Done => UiState.Ready,
        RunState.Failed => UiState.Failed,
        _ => UiState.Idle,
    };

    private static System.Numerics.Vector4 StateColor(StepState state) => state switch
    {
        StepState.Running => Copy.MarkColor(UiState.Running),
        StepState.Done => Copy.MarkColor(UiState.Ready),
        StepState.Failed => Copy.MarkColor(UiState.Failed),
        StepState.Blocked => Copy.MarkColor(UiState.Blocked),
        _ => Theme.StateNeutral,
    };
}
