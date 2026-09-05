using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using LazyCrafter.Adapters;
using LazyCrafter.Core;

namespace LazyCrafter.UI;

/// <summary>
/// The <b>Run</b> tab (card t_c360953f): where a dispatch can be watched, stopped, resumed and copied out.
/// <para>
/// Joey, 2026-09-05: "it would be nice if I had a place to view the status and could stop it if it was stuck... or
/// potentially even try to fix it if I can see what it's trying to do." Until now the only in-window signal during a
/// run was one orange status line at the bottom of the cart, and the "not crafting X yet - needs ..." reasons had
/// scrolled off chat within seconds of a 16-minute gather.
/// </para>
/// <para>
/// Draws <b>only</b> from <see cref="DispatchService.Snapshot"/> - the immutable <see cref="RunSnapshot"/> the
/// dispatcher republishes on every phase change / poll tick (same rule as <see cref="CatalogService"/>: nothing is
/// computed in Draw, no game state is touched). The only per-frame arithmetic is the elapsed clock. Buttons call the
/// same public hand-offs the cart panel and ingredient tree already use: <c>Stop()</c>, <c>Resume()</c>,
/// <c>Lifestream.GoToMarket</c> (<c>/li mb</c>), <c>Lifestream.GoToVendor(teleport:false)</c> (map flag + chat line).
/// The <b>Copy report</b> text is <see cref="RunReport.Render"/>, the same renderer <c>/lcraft status</c> prints and
/// <c>tests/LazyCrafter.Probe</c> checks offline.
/// </para>
/// </summary>
public sealed class RunTab
{
    private readonly Plugin _plugin;

    // Vendor groups are keyed by the BlockedItem.Where string; recomputed only when the snapshot instance changes.
    private RunSnapshot? _groupedFor;
    private List<(string Where, List<BlockedItem> Items)> _vendorGroups = new();
    private List<BlockedItem> _marketItems = new();
    private DateTime _copiedAt = DateTime.MinValue;

    public RunTab(Plugin plugin) => _plugin = plugin;

    /// <summary>"Run - Gathering###tab-run": the badge is the phase label; the ### keeps the tab id stable while the label changes.</summary>
    public static string TabLabel(RunSnapshot s) => s.State == RunState.Idle ? "Run###tab-run" : $"Run - {s.PhaseLabel}###tab-run";

    /// <summary>Elapsed for display: live while running, frozen at EndedAt afterwards, the snapshot's own value as the fallback.</summary>
    public static TimeSpan ElapsedFor(RunSnapshot s)
    {
        if (s.StartedAt == DateTime.MinValue) return s.Elapsed;
        if (s.State == RunState.Running) return DateTime.UtcNow - s.StartedAt;
        if (s.EndedAt is { } end) return end - s.StartedAt;
        return s.Elapsed;
    }

    private string Name(uint itemId) => _plugin.GameData?.ItemName(itemId) ?? $"#{itemId}";

    public void Draw()
    {
        var dispatch = _plugin.Dispatch;
        var s = dispatch.Snapshot;
        var elapsed = ElapsedFor(s);

        if (s.State == RunState.Idle)
        {
            ImGui.TextDisabled(RunReport.Headline(s));
            return;
        }

        // ---- header: phase badge, elapsed, started-at, cart names, pass
        ImGui.TextColored(PhaseColor(s.State), s.PhaseLabel);
        ImGui.SameLine();
        ImGui.TextUnformatted($"{RunReport.Elapsed(elapsed)} elapsed");
        if (s.StartedAt != DateTime.MinValue)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"started {s.StartedAt.ToLocalTime():HH:mm:ss}{(s.EndedAt is { } e ? $", ended {e.ToLocalTime():HH:mm:ss}" : "")}{(s.Pass > 1 ? $", pass {s.Pass}" : "")}");
        }
        var what = s.CartNames.Count > 0 ? string.Join(", ", s.CartNames) : s.What;
        if (!string.IsNullOrEmpty(what)) ImGui.TextUnformatted(what);
        if (!string.IsNullOrEmpty(s.Status)) ImGui.TextColored(s.State == RunState.Failed ? ImGuiColors.DalamudRed : ImGuiColors.DalamudOrange, s.Status);
        if (!string.IsNullOrEmpty(s.StoppedReason) && !string.Equals(s.StoppedReason, s.Status, StringComparison.Ordinal))
            ImGui.TextColored(s.State == RunState.Blocked ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudRed, s.StoppedReason);

        // ---- buttons: Stop / Resume / Copy report
        if (s.State == RunState.Running)
        {
            if (ImGui.Button("Stop")) dispatch.Stop();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Abort the run: retainer queue aborted, GBR off, Artisan stop request. The plan is kept so Resume can pick it up.");
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("Stop");
            ImGui.EndDisabled();
        }
        ImGui.SameLine();
        if (s.CanResume)
        {
            if (ImGui.Button("Resume")) dispatch.Resume();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Re-plan from what is in your bags now and continue the same cart.");
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("Resume");
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(s.State == RunState.Running ? "Resume is for a run that has stopped." : "Nothing to resume - the plan is gone. Press Dispatch on the cart again.");
        }
        ImGui.SameLine();
        if (ImGui.Button("Copy report"))
        {
            ImGui.SetClipboardText(RunReport.Render(s, elapsed));
            _copiedAt = DateTime.UtcNow;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Copy this whole run - every step, reason and blocker - as plain text (paste it into a Helm note).");
        if ((DateTime.UtcNow - _copiedAt).TotalSeconds < 3) { ImGui.SameLine(); ImGui.TextColored(ImGuiColors.HealerGreen, "copied"); }

        // ---- blocked section: only when Blocked; the same lists the chat block prints, with the two actions.
        if (s.State == RunState.Blocked && s.Blocked.Count > 0) DrawBlocked(s);

        // ---- the step list
        ImGui.Separator();
        DrawSteps(s);
    }

    private void DrawBlocked(RunSnapshot s)
    {
        if (!ReferenceEquals(_groupedFor, s))
        {
            _groupedFor = s;
            _marketItems = s.Blocked.Where(b => b.Kind == StepKind.Market).ToList();
            _vendorGroups = s.Blocked.Where(b => b.Kind == StepKind.Vendor)
                .GroupBy(b => b.Where ?? "")
                .Select(g => (g.Key, g.ToList()))
                .ToList();
        }

        ImGui.Separator();
        ImGui.TextColored(ImGuiColors.DalamudOrange, "Stopped - the run needs you before it can continue:");

        if (_marketItems.Count > 0)
        {
            long total = 0;
            var complete = true;
            foreach (var b in _marketItems) { if (b.EstimatedGil is { } g) total += g; else complete = false; }
            ImGui.Bullet();
            ImGui.TextUnformatted($"Buy on the market board (est. {(complete ? "" : ">")}{total:N0} gil):");
            ImGui.SameLine();
            if (ImGui.SmallButton("Open market board##mb"))
                _plugin.Dispatch.Lifestream.GoToMarket(_marketItems.Select(b => (b.ItemId, b.Quantity)).ToList(), Name, _plugin.Catalog.UnitCost);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Prints the list and sends you to the nearest market board via Lifestream (/li mb).");
            foreach (var b in _marketItems)
            {
                ImGui.Indent();
                ImGui.TextUnformatted($"{b.Name} x{b.Quantity}");
                if (b.EstimatedGil is { } g) { ImGui.SameLine(); ImGui.TextDisabled($"~{g:N0} gil"); }
                ImGui.Unindent();
            }
        }

        var vi = 0;
        foreach (var (where, items) in _vendorGroups)
        {
            ImGui.Bullet();
            ImGui.TextUnformatted(string.IsNullOrEmpty(where) ? "Buy from a gil vendor (no placed vendor found):" : $"Buy from {where}:");
            if (!string.IsNullOrEmpty(where))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"Flag on map##vendor{vi}")) FlagVendor(items);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Sets the map flag on the vendor and prints the shopping list with a map link. No teleport.");
            }
            foreach (var b in items)
            {
                ImGui.Indent();
                ImGui.TextUnformatted($"{b.Name} x{b.Quantity}");
                ImGui.Unindent();
            }
            vi++;
        }

        foreach (var b in s.Blocked.Where(b => b.Kind is not (StepKind.Market or StepKind.Vendor)))
        {
            ImGui.Bullet();
            ImGui.TextUnformatted($"{RunReport.KindName(b.Kind)}: {b.Name} x{b.Quantity}{(string.IsNullOrEmpty(b.Where) ? "" : $" - {b.Where}")}");
        }

        if (s.CanResume) ImGui.TextDisabled("Then press Resume (or /lcraft resume).");
    }

    /// <summary>Map flag + chat line for one vendor group, via the existing no-teleport path. Button handler (framework thread).</summary>
    private void FlagVendor(List<BlockedItem> items)
    {
        var vendors = _plugin.Dispatch.Vendors;
        VendorLocator.Location? where = null;
        foreach (var b in items)
        {
            where = vendors.Find(b.ItemId);
            if (where is not null) break;
        }
        if (where is null) { Plugin.ChatGui.PrintError("[LazyCrafter] no placed gil vendor found for " + string.Join(", ", items.Select(b => b.Name)) + "."); return; }
        _plugin.Dispatch.Lifestream.GoToVendor(where, items.Select(b => (b.ItemId, b.Quantity)).ToList(), Name, teleport: false);
    }

    private static void DrawSteps(RunSnapshot s)
    {
        if (s.Steps.Count == 0) { ImGui.TextDisabled("No steps recorded for this run."); return; }
        var done = s.Steps.Count(st => st.State == StepState.Done);
        ImGui.TextDisabled($"Steps - {done}/{s.Steps.Count} done");
        if (!ImGui.BeginTable("##run-steps", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp, new Vector2(0, 0))) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 45);
        ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Reason / status", ImGuiTableColumnFlags.WidthStretch, 4f);
        ImGui.TableHeadersRow();
        var i = 0;
        foreach (var st in s.Steps)
        {
            ImGui.TableNextRow();
            if (st.State == StepState.Running)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.65f, 0.15f, 0.25f)));
            ImGui.PushID(i++);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(RunReport.KindName(st.Kind));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(st.Name);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(st.Quantity.ToString());
            ImGui.TableNextColumn(); ImGui.TextColored(StateColor(st.State), RunReport.StateName(st.State));
            ImGui.TableNextColumn();
            var reason = st.Reason ?? "";
            var ext = st.ExternalStatus ?? "";
            if (reason.Length > 0)
            {
                ImGui.TextColored(st.State is StepState.Blocked or StepState.Failed ? ImGuiColors.DalamudRed : ImGuiColors.DalamudGrey, reason);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(reason);
            }
            if (ext.Length > 0)
            {
                if (reason.Length > 0) ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.DalamudOrange, ext);
            }
            if (reason.Length == 0 && ext.Length == 0) ImGui.TextDisabled("-");
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private static Vector4 PhaseColor(RunState s) => s switch
    {
        RunState.Running => ImGuiColors.DalamudOrange,
        RunState.Blocked => ImGuiColors.DalamudYellow,
        RunState.Done => ImGuiColors.HealerGreen,
        RunState.Failed => ImGuiColors.DalamudRed,
        _ => ImGuiColors.DalamudGrey,
    };

    private static Vector4 StateColor(StepState s) => s switch
    {
        StepState.Running => ImGuiColors.DalamudOrange,
        StepState.Done => ImGuiColors.HealerGreen,
        StepState.Failed => ImGuiColors.DalamudRed,
        StepState.Blocked => ImGuiColors.DalamudYellow,
        _ => ImGuiColors.DalamudGrey,
    };
}
