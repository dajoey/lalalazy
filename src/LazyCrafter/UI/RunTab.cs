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
                snapshot.State == RunState.Idle ? "No dispatch has started" : Copy.Word(RunUiState(snapshot.State)), null),
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
                Widgets.InfoBox("Idle — " + RunReport.Headline(snapshot) + " Build or reopen the saved cart to dispatch a plan.", UiState.Idle);
                if (ImGui.Button("Open cart"))
                    _main.OpenCart();
            });
            return;
        }

        DrawSummary(snapshot);
        DrawToolbar(dispatch, snapshot, elapsed);
        CrafterChrome.TwoColumns("run", 0.70f,
            () => DrawSteps(snapshot),
            () =>
            {
                DrawRunDetails(snapshot);
                if (snapshot.State == RunState.Blocked && snapshot.Blocked.Count > 0)
                    DrawBlocked(snapshot);
                else
                    DrawNextAction(snapshot);
            });
    }

    private void DrawSummary(RunSnapshot snapshot)
    {
        var what = snapshot.CartNames.Count > 0 ? string.Join(", ", snapshot.CartNames) : snapshot.What;
        CrafterChrome.Card("run_summary", () =>
        {
            if (!string.IsNullOrEmpty(what))
            {
                CrafterChrome.Section("Current cart");
                Widgets.SecondaryWrapped(what);
            }
            if (!string.IsNullOrEmpty(snapshot.Status))
                Widgets.InfoBox(snapshot.Status, snapshot.State == RunState.Failed ? UiState.Failed : RunUiState(snapshot.State));
            if (!string.IsNullOrEmpty(snapshot.StoppedReason) && !string.Equals(snapshot.StoppedReason, snapshot.Status, StringComparison.Ordinal))
                Widgets.InfoBox(snapshot.StoppedReason, snapshot.State == RunState.Blocked ? UiState.Blocked : UiState.Failed);
        }, snapshot.State is RunState.Running or RunState.Blocked ? CrafterChrome.CardTone.Attention : CrafterChrome.CardTone.Surface);
    }

    private void DrawToolbar(DispatchService dispatch, RunSnapshot snapshot, TimeSpan elapsed)
    {
        if (snapshot.State == RunState.Running)
        {
            if (Widgets.StopButton("Stop"))
                dispatch.Stop();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Abort the run while keeping its plan for Resume.");
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("Stop");
            ImGui.EndDisabled();
        }

        CrafterChrome.SameLineWrapped(CrafterChrome.ButtonWidth("Resume"));
        if (snapshot.CanResume)
        {
            if (ImGui.Button("Resume"))
                dispatch.Resume();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Re-plan from current bags and continue the same cart.");
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("Resume");
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(snapshot.State == RunState.Running ? "Resume is for a stopped run." : "Nothing to resume. Dispatch the cart again.");
        }

        CrafterChrome.SameLineWrapped(CrafterChrome.ButtonWidth("Copy report"));
        if (ImGui.Button("Copy report"))
        {
            ImGui.SetClipboardText(RunReport.Render(snapshot, elapsed));
            _copiedAt = DateTime.UtcNow;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copy every step, reason and blocker as plain text.");
        if ((DateTime.UtcNow - _copiedAt).TotalSeconds < 3)
        {
            ImGui.SameLine();
            Widgets.StateText("Copied", UiState.Ready);
        }
        ImGui.Dummy(new System.Numerics.Vector2(0, Theme.Unit));
    }

    private void DrawRunDetails(RunSnapshot snapshot)
    {
        CrafterChrome.Card("run_details", () =>
        {
            CrafterChrome.Section("Run details");
            CrafterChrome.Requirement("State", Copy.Word(RunUiState(snapshot.State)), RunUiState(snapshot.State));
            if (snapshot.StartedAt != DateTime.MinValue)
                Widgets.Caption($"Started {snapshot.StartedAt.ToLocalTime():HH:mm:ss}");
            if (snapshot.EndedAt is { } end)
                Widgets.Caption($"Ended {end.ToLocalTime():HH:mm:ss}");
            if (snapshot.Pass > 1)
                Widgets.Caption($"Pass {snapshot.Pass}");
            Widgets.Caption($"{snapshot.CartNames.Count} cart item(s) · {snapshot.Blocked.Count} blocked item(s)");
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
                    Widgets.InfoBox("Ready — the run finished. The report remains available until the next dispatch.", UiState.Ready);
                    break;
                case RunState.Failed:
                    Widgets.InfoBox("Failed — read the failed step and reason, then copy the report or resume when the cause is fixed.", UiState.Failed);
                    break;
                case RunState.Running:
                    Widgets.InfoBox("Running — the dispatcher is following the current plan. Stop remains available in the footer.", UiState.Running);
                    break;
                default:
                    Widgets.InfoBox("Blocked — resolve the named step, then use Resume when the plan can continue.", UiState.Blocked);
                    break;
            }
        });
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
            CrafterChrome.Section("Required before Resume");
            Widgets.InfoBox("Blocked — complete the named shopping or retrieval steps, then resume the same cart.", UiState.Blocked);

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
                Widgets.Body($"Market board · est. {(complete ? "" : ">")}{total:N0} gil");
                if (ImGui.SmallButton("Open market board##market"))
                    _plugin.Dispatch.Lifestream.GoToMarket(_marketItems.Select(item => (item.ItemId, item.Quantity)).ToList(), Name, _plugin.Catalog.UnitCost);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Print the list and use the existing Lifestream market-board route.");
                foreach (var item in _marketItems)
                    Widgets.SecondaryWrapped($"{item.Name} x{item.Quantity}{(item.EstimatedGil is { } estimate ? $" · ~{estimate:N0} gil" : "")}");
            }

            var vendorIndex = 0;
            foreach (var (where, items) in _vendorGroups)
            {
                Widgets.Body(string.IsNullOrEmpty(where) ? "Gil vendor · no placed vendor found" : "Gil vendor · " + where);
                if (!string.IsNullOrEmpty(where))
                {
                    if (ImGui.SmallButton($"Flag on map##vendor{vendorIndex}"))
                        FlagVendor(items);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Set the map flag and print the shopping list. No teleport.");
                }
                foreach (var item in items)
                    Widgets.SecondaryWrapped($"{item.Name} x{item.Quantity}");
                vendorIndex++;
            }

            var currencyIndex = 0;
            foreach (var item in snapshot.Blocked.Where(item => item.Kind == StepKind.CurrencyShop))
            {
                Widgets.SecondaryWrapped($"Trade for {item.Name} x{item.Quantity} at {item.Where}");
                if (ImGui.SmallButton($"Flag on map##currency{currencyIndex}"))
                    FlagCurrencyShop(item);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Set the map flag and print the price. No teleport or automatic trade.");
                currencyIndex++;
            }

            foreach (var item in snapshot.Blocked.Where(item => item.Kind is not (StepKind.Market or StepKind.Vendor or StepKind.CurrencyShop)))
                Widgets.SecondaryWrapped($"{RunReport.KindName(item.Kind)}: {item.Name} x{item.Quantity}{(string.IsNullOrEmpty(item.Where) ? "" : " — " + item.Where)}");

            if (snapshot.CanResume)
                Widgets.Caption("Then press Resume or use /lcraft resume.");
        }, CrafterChrome.CardTone.Attention);
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
            CrafterChrome.Section("Steps", "Every dispatch step, its current state and the latest reason.");
            if (snapshot.Steps.Count == 0)
            {
                Widgets.InfoBox("Empty — no steps have been recorded for this run.", UiState.Empty);
                return;
            }
            var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;
            var height = MathF.Max(220, ImGui.GetContentRegionAvail().Y);
            if (!ImGui.BeginTable("##crafter_run_steps", 5, flags, new System.Numerics.Vector2(0, height)))
                return;
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthFixed, 76);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 48);
            ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 76);
            ImGui.TableSetupColumn("Reason / status", ImGuiTableColumnFlags.WidthStretch, 4f);
            ImGui.TableHeadersRow();
            var index = 0;
            foreach (var step in snapshot.Steps)
            {
                ImGui.TableNextRow();
                if (step.State == StepState.Running)
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(Theme.StateAmber with { W = 0.25f }));
                ImGui.PushID(index++);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(RunReport.KindName(step.Kind));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(step.Name);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(step.Quantity.ToString());
                ImGui.TableNextColumn();
                ImGui.TextColored(StateColor(step.State), RunReport.StateName(step.State));
                ImGui.TableNextColumn();
                var reason = step.Reason ?? "";
                var external = step.ExternalStatus ?? "";
                if (reason.Length > 0)
                {
                    ImGui.TextColored(step.State == StepState.Failed ? Copy.MarkColor(UiState.Failed) : step.State == StepState.Blocked ? Copy.MarkColor(UiState.Blocked) : Theme.TextSecondary, reason);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(reason);
                }
                if (external.Length > 0)
                {
                    if (reason.Length > 0)
                        ImGui.SameLine();
                    ImGui.TextColored(Copy.MarkColor(UiState.Blocked), external);
                }
                if (reason.Length == 0 && external.Length == 0)
                    ImGui.TextDisabled("-");
                ImGui.PopID();
            }
            ImGui.EndTable();
        });
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
