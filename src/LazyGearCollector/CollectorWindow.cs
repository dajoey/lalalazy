using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace LazyGearCollector;

public sealed class CollectorWindow : Window
{
    private static readonly Vector4 Gold = new(0.95f, 0.80f, 0.30f, 1f);
    private static readonly Vector4 Green = new(0.35f, 0.80f, 0.40f, 1f);
    private static readonly Vector4 Dim = new(0.60f, 0.60f, 0.60f, 1f);
    private static readonly Vector4 Cyan = new(0.30f, 0.80f, 0.95f, 1f);
    private static readonly Vector4 BarDone = new(0.20f, 0.60f, 0.30f, 1f);
    private static readonly Vector4 BarPart = new(0.85f, 0.60f, 0.10f, 1f);

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

    private static readonly string[] TargetTierLabels = { "base", "+1", "+2", "+3" };

    // Planning walks the shop graph and resolves item names, so it is too expensive to redo for
    // every section of every frame: plan the whole collection once and let the bar, role table
    // and detail view filter the one result set. The cadence matches the ownership scanner's
    // live-count cache; changing the goal or the cached-containers option replans at once.
    private static readonly TimeSpan PlanCacheDuration = TimeSpan.FromSeconds(2);

    private readonly Plugin _plugin;
    private string? _selectedRole;
    private GearCollection? _planCollection;
    private int _planTarget = -1;
    private bool _planIncludeCached;
    private DateTime _planExpiry = DateTime.MinValue;
    private List<PiecePlan> _plans = [];
    private Dictionary<uint, long> _planTotal = new();
    private int _planDone;

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
        var collection = Current;
        if (collection == null)
        {
            ImGui.TextColored(Dim, "No collections could be built from the game data.");
            ImGui.TextWrapped("This usually means the game is on a patch that does not contain the tracked sets yet.");
            return;
        }

        DrawCollectionPicker(collection);
        ImGui.Separator();
        DrawWallet(collection);
        ImGui.Separator();

        var target = _plugin.Config.TargetTier;
        var (allPlans, grandTotal, donePieces) = CurrentPlans(collection, target);

        DrawOverall(collection, donePieces, allPlans.Count, grandTotal);
        ImGui.Separator();

        DrawRoleTable(collection, allPlans);

        if (_selectedRole != null)
        {
            ImGui.Separator();
            DrawRoleDetail(_selectedRole, allPlans);
        }
    }

    private (List<PiecePlan> Plans, Dictionary<uint, long> Total, int Done) CurrentPlans(
        GearCollection collection, int target)
    {
        var includeCached = _plugin.Config.IncludeCachedContainers;
        if (!ReferenceEquals(_planCollection, collection)
            || _planTarget != target
            || _planIncludeCached != includeCached
            || DateTime.UtcNow >= _planExpiry)
        {
            (_plans, _planTotal, _planDone) = _plugin.Planner.PlanMany(collection.Pieces, target);
            _planCollection = collection;
            _planTarget = target;
            _planIncludeCached = includeCached;
            _planExpiry = DateTime.UtcNow.Add(PlanCacheDuration);
        }
        return (_plans, _planTotal, _planDone);
    }

    private void DrawCollectionPicker(GearCollection collection)
    {
        ImGui.TextColored(Gold, collection.DisplayName);
        ImGui.SameLine();

        var target = _plugin.Config.TargetTier;
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 240f);
        ImGui.TextUnformatted("Goal:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        if (ImGui.BeginCombo("##target", TargetTierLabels[Math.Clamp(target, 0, 3)]))
        {
            for (var i = 0; i < TargetTierLabels.Length; i++)
            {
                if (ImGui.Selectable(TargetTierLabels[i], i == target))
                {
                    _plugin.Config.TargetTier = i;
                    _plugin.Config.Save();
                }
            }
            ImGui.EndCombo();
        }

        // Own line: the self-explanatory label is far wider than the old one and would not fit
        // next to the right-aligned goal combo.
        var cached = _plugin.Config.IncludeCachedContainers;
        if (ImGui.Checkbox("Count saddlebag + retainers (cached)##inc", ref cached))
        {
            _plugin.Config.IncludeCachedContainers = cached;
            _plugin.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Count pieces remembered from your saddlebag and retainers,\n" +
                             "in addition to what is live in your bags, armoury and equipped.");

        ImGui.TextColored(Dim, collection.SourceNote);
    }

    private void DrawWallet(GearCollection collection)
    {
        ImGui.TextUnformatted("Wallet:");
        foreach (var currencyId in collection.Currencies)
        {
            ImGui.SameLine();
            var have = _plugin.Ownership.TotalCount(currencyId);
            ImGui.TextColored(have > 0 ? Cyan : Dim,
                $"{_plugin.Shops.ItemName(currencyId)} {have:N0}");
        }
        if (collection.Currencies.Count == 0)
            ImGui.TextColored(Dim, " (no currency costs found)");
    }

    private void DrawOverall(GearCollection collection, int done, int total, Dictionary<uint, long> remaining)
    {
        var frac = total == 0 ? 0f : (float)done / total;
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, done == total ? BarDone : BarPart);
        ImGui.ProgressBar(frac, new Vector2(-1f, 22f), $"{done} / {total} pieces at goal");
        ImGui.PopStyleColor();

        if (remaining.Count == 0)
        {
            ImGui.TextColored(Green, "Collection complete. Nothing left to buy.");
            return;
        }

        ImGui.TextUnformatted("Still needed overall:");
        foreach (var kv in remaining.OrderByDescending(k => k.Value))
        {
            var have = _plugin.Ownership.TotalCount(kv.Key);
            var short_ = Math.Max(0, kv.Value - have);
            ImGui.SameLine();
            ImGui.TextColored(short_ == 0 ? Green : Gold,
                $"{kv.Value:N0} {_plugin.Shops.ItemName(kv.Key)}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(short_ == 0
                    ? "You already have enough of this."
                    : $"You hold {have:N0}. Short by {short_:N0}.");
        }
    }

    private void DrawRoleTable(GearCollection collection, List<PiecePlan> allPlans)
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
            var plans = allPlans.Where(p => p.Piece.Role == role).ToList();
            var (total, done) = Planner.Summarize(plans);

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var isSelected = _selectedRole == role;
            if (ImGui.Selectable($"{role}##role", isSelected, ImGuiSelectableFlags.SpanAllColumns))
                _selectedRole = isSelected ? null : role;

            ImGui.TableNextColumn();
            ImGui.TextColored(Dim, RoleJobs.TryGetValue(role, out var jobs) ? jobs : "");

            ImGui.TableNextColumn();
            var frac = plans.Count == 0 ? 0f : (float)done / plans.Count;
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, done == plans.Count ? BarDone : BarPart);
            ImGui.ProgressBar(frac, new Vector2(-1f, 18f), $"{done}/{plans.Count}");
            ImGui.PopStyleColor();

            ImGui.TableNextColumn();
            if (total.Count == 0)
            {
                ImGui.TextColored(Green, "done");
            }
            else
            {
                var parts = total.OrderByDescending(k => k.Value)
                    .Select(kv => $"{kv.Value:N0} {Abbreviate(_plugin.Shops.ItemName(kv.Key))}");
                ImGui.TextUnformatted(string.Join(", ", parts));
            }

            if (plans.Any(p => p.HasShortcut))
            {
                ImGui.SameLine();
                ImGui.TextColored(Cyan, "*");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("A free or cheaper trade-up is available for this role. Open it for detail.");
            }
        }

        ImGui.EndTable();
    }

    private void DrawRoleDetail(string role, List<PiecePlan> allPlans)
    {
        ImGui.TextColored(Gold, $"{role} - piece by piece");

        if (!ImGui.BeginTable("detail", 4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 70f);
        ImGui.TableSetupColumn("Piece", ImGuiTableColumnFlags.WidthFixed, 130f);
        ImGui.TableSetupColumn("Have", ImGuiTableColumnFlags.WidthFixed, 90f);
        ImGui.TableSetupColumn("What it needs", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var plan in allPlans.Where(p => p.Piece.Role == role))
        {
            var chain = plan.Piece;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(chain.SlotName);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(chain.PieceName);

            ImGui.TableNextColumn();
            if (plan.OwnedTier < 0)
                ImGui.TextColored(Dim, "none");
            else
                ImGui.TextColored(plan.Complete ? Green : Gold,
                    plan.OwnedTier == 0 ? "base" : $"+{plan.OwnedTier}");

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
                ImGui.TextColored(Green, "complete");
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

            foreach (var note in plan.Notes)
            {
                ImGui.TextColored(Cyan, "  -> " + note);
            }
        }

        ImGui.EndTable();

        ImGui.TextColored(Dim,
            "Live: bags, armoury chest and equipped gear. Saddlebag and retainers are remembered from the last time\n" +
            "the game had them open. The glamour dresser cannot be read by plugins on this API, so pieces stored\n" +
            "there will show as missing.");
    }

    /// <summary>Shorten long currency names so the summary columns stay readable.</summary>
    private static string Abbreviate(string name) => name
        .Replace("Enlightenment ", "")
        .Replace("Final Final ", "")
        .Replace(" Obol", " obol");
}
