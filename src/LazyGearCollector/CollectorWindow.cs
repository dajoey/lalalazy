using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace LazyGearCollector;

public sealed class CollectorWindow : Window
{
    private static readonly Dictionary<string, string> RoleJobs = new()
    {
        ["Fending"] = "PLD  WAR  DRK  GNB",
        ["Maiming"] = "DRG  RPR",
        ["Striking"] = "MNK  SAM  VPR",
        ["Scouting"] = "NIN",
        ["Aiming"] = "BRD  MCH  DNC",
        ["Healing"] = "WHM  SCH  AST  SGE",
        ["Casting"] = "BLM  SMN  RDM  PCT",
        ["Slaying"] = "Melee and physical ranged",
    };

    private readonly Plugin _plugin;
    private string? _selectedRole;

    public CollectorWindow(Plugin plugin) : base("Lazy Gear Collector##lazygear")
    {
        _plugin = plugin;
        Size = new Vector2(760, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    private GearCollection? Current =>
        _plugin.Collections.FirstOrDefault(c => c.Id == _plugin.Config.LastCollectionId)
        ?? _plugin.Collections.FirstOrDefault();

    public override void Draw()
    {
        Theme.PushFrame();
        try
        {
            DrawFrame();
        }
        finally
        {
            Theme.PopFrame();
        }
    }

    private void DrawFrame()
    {
        var collection = Current;
        var state = collection == null ? UiState.Empty : UiState.Ready;

        // Converted to LalaUiKit per Projects/UI Refresh 2026-11/UI Brief (R1-R13): the suite
        // frame comes from Theme.PushFrame/PopFrame, every colour from the kit (one accent,
        // reserved state colours) and spacing from the kit 4 px grid. The top bar is the
        // same primitives Layout.Shell draws for self-beginning windows.
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetWindowWidth() - (2 * Theme.Unit);
        var barH = Theme.PillHeight;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, p + new Vector2(w, barH), Theme.Pack(Theme.ShellBar), 4f);
        dl.AddRect(p, p + new Vector2(w, barH), Theme.Pack(Theme.SurfaceBorder), 4f);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(2 * Theme.Unit, (barH - ImGui.GetTextLineHeight()) / 2f));
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
        ImGui.TextUnformatted("Lazy Gear Collector");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPosX(p.X + w - (2 * Theme.Unit));
        Widgets.StatusPill(state, "Gear Collector", alignRight: true);
        ImGui.SetCursorPosY(p.Y + barH + Theme.ItemSpacing.Y);

        if (collection == null)
        {
            Widgets.Secondary("No collections could be built from the game data.");
            Widgets.SecondaryWrapped("This usually means the game is on a patch that does not contain the tracked sets yet.");
            return;
        }

        var target = _plugin.Config.TargetTier;
        var (allPlans, grandTotal, donePieces) = _plugin.Planner.PlanMany(collection.Pieces, target);

        DrawCollectionHeader(collection, donePieces, allPlans.Count);
        ImGui.Separator();
        DrawSettingsRow();
        DrawWallet(collection);
        ImGui.Separator();

        DrawOverall(collection, donePieces, allPlans.Count, grandTotal);
        ImGui.Separator();

        DrawRoleTable(collection, target);

        if (_selectedRole != null)
        {
            ImGui.Separator();
            DrawRoleDetail(collection, _selectedRole, target);
        }

        DrawActivityFooter();
    }

    // R2: page header with icon, title, one-line description and a live count that
    // answers a question ("X / Y pieces at goal").
    private void DrawCollectionHeader(GearCollection collection, int done, int total)
    {
        Layout.PageHeader(null, "G", collection.DisplayName, collection.SourceNote,
            $"{done} / {total} pieces at goal");
    }

    private void DrawSettingsRow()
    {
        var target = _plugin.Config.TargetTier;
        ImGui.TextUnformatted("Goal:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        var labels = new[] { "base", "+1", "+2", "+3" };
        if (ImGui.BeginCombo("##target", labels[Math.Clamp(target, 0, 3)]))
        {
            for (var i = 0; i < labels.Length; i++)
            {
                if (ImGui.Selectable(labels[i], i == target))
                {
                    _plugin.Config.TargetTier = i;
                    _plugin.Config.Save();
                }
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        var cached = _plugin.Config.IncludeCachedContainers;
        if (Widgets.Checkbox("Cached##inc", ref cached))
        {
            _plugin.Config.IncludeCachedContainers = cached;
            _plugin.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Count pieces remembered from your saddlebag and retainers,\n" +
                             "in addition to what is live in your bags, armoury and equipped.");
    }

    // R4/R8: wallet numbers are data, not state — emphasis by contrast (primary vs
    // secondary), never a decorative hue.
    private void DrawWallet(GearCollection collection)
    {
        ImGui.TextUnformatted("Wallet:");
        foreach (var currencyId in collection.Currencies)
        {
            ImGui.SameLine();
            var have = _plugin.Ownership.TotalCount(currencyId);
            var label = $"{_plugin.Shops.ItemName(currencyId)} {have:N0}";
            if (have > 0)
                Widgets.Body(label);
            else
                Widgets.Secondary(label);
        }
        if (collection.Currencies.Count == 0)
            Widgets.Secondary(" (no currency costs found)");
    }

    private void DrawOverall(GearCollection collection, int done, int total, Dictionary<uint, long> remaining)
    {
        var frac = total == 0 ? 0f : (float)done / total;
        Widgets.ProgressBar(frac, done == total ? Theme.StateGreen : Theme.StateAmber);

        if (remaining.Count == 0)
        {
            Widgets.Banner(UiState.Ready, "Collection complete. Nothing left to buy.");
            return;
        }

        Widgets.Body("Still needed overall:");
        foreach (var kv in remaining.OrderByDescending(k => k.Value))
        {
            var have = _plugin.Ownership.TotalCount(kv.Key);
            var short_ = Math.Max(0, kv.Value - have);
            ImGui.SameLine();
            Widgets.StatusDot(short_ == 0 ? UiState.Ready : UiState.Running);
            ImGui.SameLine();
            Widgets.Body($"{kv.Value:N0} {_plugin.Shops.ItemName(kv.Key)}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(short_ == 0
                    ? "You already have enough of this."
                    : $"You hold {have:N0}. Short by {short_:N0}.");
        }
    }

    private void DrawRoleTable(GearCollection collection, int target)
    {
        if (!ImGui.BeginTable("roles", 4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Role", ImGuiTableColumnFlags.WidthFixed, 110f);
        ImGui.TableSetupColumn("Jobs", ImGuiTableColumnFlags.WidthFixed, 165f);
        ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Remaining", ImGuiTableColumnFlags.WidthFixed, 210f);
        ImGui.TableHeadersRow();

        foreach (var role in collection.Roles)
        {
            var chains = collection.ForRole(role).ToList();
            var (plans, total, done) = _plugin.Planner.PlanMany(chains, target);

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var isSelected = _selectedRole == role;
            if (ImGui.Selectable($"{role}##role", isSelected, ImGuiSelectableFlags.SpanAllColumns))
                _selectedRole = isSelected ? null : role;

            ImGui.TableNextColumn();
            Widgets.Secondary(RoleJobs.TryGetValue(role, out var jobs) ? jobs : "");

            ImGui.TableNextColumn();
            var frac = chains.Count == 0 ? 0f : (float)done / chains.Count;
            Widgets.ProgressBar(frac, done == chains.Count ? Theme.StateGreen : Theme.StateAmber);

            ImGui.TableNextColumn();
            if (total.Count == 0)
            {
                Widgets.StatusDot(UiState.Ready);
                ImGui.SameLine();
                Widgets.Body("done");
            }
            else
            {
                var parts = total.OrderByDescending(k => k.Value)
                    .Select(kv => $"{kv.Value:N0} {Abbreviate(_plugin.Shops.ItemName(kv.Key))}");
                ImGui.TextUnformatted(string.Join(", ", parts));
            }

            // Accent (R4): marks exactly this actionable hint — the row's trade-up shortcut.
            if (plans.Any(p => p.HasShortcut))
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent);
                ImGui.TextUnformatted("*");
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("A free or cheaper trade-up is available for this role. Open it for detail.");
            }
        }

        ImGui.EndTable();
    }

    private void DrawRoleDetail(GearCollection collection, string role, int target)
    {
        Widgets.Title($"{role} - piece by piece");

        if (!ImGui.BeginTable("detail", 4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 70f);
        ImGui.TableSetupColumn("Piece", ImGuiTableColumnFlags.WidthFixed, 130f);
        ImGui.TableSetupColumn("Have", ImGuiTableColumnFlags.WidthFixed, 90f);
        ImGui.TableSetupColumn("What it needs", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var chain in collection.ForRole(role))
        {
            var plan = _plugin.Planner.Plan(chain, target);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(chain.SlotName);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(chain.PieceName);

            ImGui.TableNextColumn();
            if (plan.OwnedTier < 0)
            {
                Widgets.Secondary("none");
            }
            else
            {
                Widgets.StatusDot(plan.Complete ? UiState.Ready : UiState.Running);
                ImGui.SameLine();
                Widgets.Body(plan.OwnedTier == 0 ? "base" : $"+{plan.OwnedTier}");
            }

            if (plan.OwnedTier >= 0 && ImGui.IsItemHovered())
            {
                var node = chain.Tier(plan.OwnedTier);
                if (node != null)
                {
                    var live = _plugin.Ownership.LiveCount(node.ItemId);
                    var sources = _plugin.Ownership.CachedSources(node.ItemId).ToList();
                    var text = live > 0 ? "In your bags, armoury or equipped." : "";
                    foreach (var (label, count, seen) in sources)
                        text += $"\n{label}: {count} (seen {seen.ToLocalTime():yyyy-MM-dd HH:mm})";
                    ImGui.SetTooltip(string.IsNullOrWhiteSpace(text) ? node.Name : $"{node.Name}\n{text}");
                }
            }

            ImGui.TableNextColumn();
            if (plan.Complete)
            {
                Widgets.StatusDot(UiState.Ready);
                ImGui.SameLine();
                Widgets.Body("complete");
            }
            else
            {
                var parts = plan.Remaining.OrderByDescending(k => k.Value)
                    .Select(kv =>
                    {
                        var have = _plugin.Ownership.TotalCount(kv.Key);
                        var shortBy = Math.Max(0, kv.Value - have);
                        return shortBy == 0
                            ? $"{kv.Value:N0} {Abbreviate(_plugin.Shops.ItemName(kv.Key))} (covered)"
                            : $"{kv.Value:N0} {Abbreviate(_plugin.Shops.ItemName(kv.Key))} (short {shortBy:N0})";
                    });
                ImGui.TextUnformatted(string.Join(", ", parts));
            }

            // Accent (R4): marks exactly this actionable hint — the trade-up note.
            foreach (var note in plan.Notes)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent);
                ImGui.TextUnformatted("  -> " + note);
                ImGui.PopStyleColor();
            }
        }

        ImGui.EndTable();

        Widgets.Secondary(
            "Live: bags, armoury chest and equipped gear. Saddlebag and retainers are remembered from the last time\n" +
            "the game had them open. The glamour dresser cannot be read by plugins on this API, so pieces stored\n" +
            "there will show as missing.");
    }

    // R3 place 3: this plugin refreshes ownership snapshots every 5 s while logged in
    // (Plugin.OnFrameworkUpdate). The sweep has no stop control — adding one would change
    // plugin behaviour, which this refresh must not — so the footer carries the live
    // action without a Stop button.
    private void DrawActivityFooter()
    {
        ImGui.Dummy(new Vector2(0, Theme.Unit));
        ImGui.Separator();
        Widgets.StatusDot(UiState.Ready);
        ImGui.SameLine();
        Widgets.Secondary("Ownership snapshots refresh every 5 seconds while you are logged in.");
    }

    /// <summary>Shorten long currency names so the summary columns stay readable.</summary>
    private static string Abbreviate(string name) => name
        .Replace("Enlightenment ", "")
        .Replace("Final Final ", "")
        .Replace(" Obol", " obol");
}
