using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using LazyCrafter.Catalog;
using LazyCrafter.Core.Model;

namespace LazyCrafter.UI;

/// <summary>
/// Right-hand panel (Plan §Phase 4 task 3): the selected recipe's ingredient tree - per leaf have/need, source
/// kinds, tier, unit price - plus a fulfil button per available channel. In Phase 4 the buttons are placeholders
/// that say what Phase 5 will do (Artisan / GBR / ARC / Lifestream) and are disabled; only "Add to cart" acts.
/// </summary>
public sealed class IngredientTree
{
    private readonly Plugin _plugin;
    private int _qty = 1;
    private uint _qtyFor;

    public IngredientTree(Plugin plugin) => _plugin = plugin;

    public void Draw(CatalogRow row, CatalogSnapshot snap, CatalogView view, bool hq)
    {
        if (_qtyFor != row.RecipeId) { _qtyFor = row.RecipeId; _qty = 1; }
        var est = row.Est(hq);

        ImGui.TextColored(ImGuiColors.ParsedGold, row.Name);
        ImGui.SameLine();
        ImGui.TextDisabled($"{row.Job} {row.Level}  x{row.ResultAmount}/craft");
        ImGui.TextColored(Fmt.TierColor(row.Tier), Fmt.TierName(row.Tier));
        ImGui.SameLine();
        ImGui.TextUnformatted(row.HowMany > 0 ? $"can craft {row.HowMany}" : "cannot craft yet");
        if (row.LogComplete) { ImGui.SameLine(); ImGui.TextDisabled("(in log)"); }
        if (est is not null)
        {
            ImGui.TextUnformatted($"Revenue {(est.RevenueKnown ? Fmt.Gil((hq ? est.RevenueHq : est.RevenueNq)!.Value) : "?")}  tax {Fmt.Gil(est.Tax)}  cash cost {(est.CostComplete ? "" : ">")}{Fmt.Gil(est.CashCost)}  at market {Fmt.Gil(est.MarketCost)}");
            ImGui.TextUnformatted($"Margin cash {(est.MarginCash is { } mc ? Fmt.Gil(mc) : "?")}  market {(est.MarginMarket is { } mm ? Fmt.Gil(mm) : "?")}  /day {Fmt.Gil((long)Math.Round(est.PerDay))}  vel {est.Velocity:0.#}  listings {est.Listings}");
            if (!est.CostComplete)
                ImGui.TextColored(ImGuiColors.DalamudGrey, "unpriced: " + string.Join(", ", est.UnpricedItems.Take(4).Select(Name)) + (est.UnpricedItems.Count > 4 ? $" +{est.UnpricedItems.Count - 4}" : ""));
        }
        else if (!row.Marketable) ImGui.TextDisabled("Untradeable - no market value.");
        if (row.Scrip > 0) ImGui.TextColored(ImGuiColors.ParsedPurple, $"{row.Scrip} scrip per turn-in at max collectability");

        ImGui.SetNextItemWidth(70f);
        ImGui.InputInt("##qty", ref _qty);
        if (_qty < 1) _qty = 1;
        ImGui.SameLine();
        if (ImGui.Button("Add to cart")) _plugin.Catalog.AddToCart(row.RecipeId, _qty);
        ImGui.SameLine();
        if (ImGui.Button("Copy TeamCraft link"))
        {
            var link = Core.TeamcraftExport.Link([new Core.TeamcraftExport.Line(row.ResultItemId, row.RecipeId, _qty * row.ResultAmount)]);
            if (link is not null) { ImGui.SetClipboardText(link); Plugin.ChatGui.Print("[LazyCrafter] TeamCraft link copied."); }
        }
        ImGui.Separator();

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("##tree", 5, flags)) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Ingredient", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Have/Need", ImGuiTableColumnFlags.WidthFixed, 78);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableSetupColumn("Unit", ImGuiTableColumnFlags.WidthFixed, 62);
        ImGui.TableSetupColumn("Fulfil", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableHeadersRow();

        var roots = Core.IngredientTree.Build(row.Leaves);
        foreach (var node in roots) DrawNode(node, 0);
        ImGui.EndTable();
    }

    private void DrawNode(Core.IngredientTree.Node node, int depth)
    {
        var leaf = node.Leaf;
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.PushID((int)leaf.ItemId + depth * 1_000_000);
        var flags = ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.DefaultOpen;
        if (node.Children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.Bullet;
        var color = leaf.Missing == 0 ? ImGuiColors.HealerGreen : Fmt.TierColor(leaf.Tier);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        var open = ImGui.TreeNodeEx(Name(leaf.ItemId), flags);
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"item {leaf.ItemId}\n{string.Join(", ", leaf.Sources.Select(Fmt.SourceName))}");

        ImGui.TableNextColumn();
        ImGui.TextColored(leaf.Missing == 0 ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, $"{leaf.Have}/{leaf.Need}");
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(leaf.Missing == 0 ? "on hand" : string.Join(", ", leaf.Sources.Where(s => s != SourceKind.OnHand).Select(Fmt.SourceName)));
        ImGui.TableNextColumn();
        var unit = _plugin.Catalog.UnitCost(leaf.ItemId);
        if (unit is { } u) ImGui.TextUnformatted(Fmt.Gil(u)); else ImGui.TextDisabled("-");
        ImGui.TableNextColumn();
        DrawFulfil(leaf);

        if (open && node.Children.Count > 0)
        {
            foreach (var c in node.Children) DrawNode(c, depth + 1);
            ImGui.TreePop();
        }
        ImGui.PopID();
    }

    /// <summary>One small button per channel that could fill this leaf. Phase 5 wires them; here they are disabled hints.</summary>
    private static void DrawFulfil(IngredientLeaf leaf)
    {
        if (leaf.Missing == 0) return;
        var first = true;
        foreach (var s in leaf.Sources)
        {
            var (label, tip) = s switch
            {
                SourceKind.SubCraft => ("Craft", "Artisan.CraftItem - Phase 5"),
                SourceKind.RegularNode or SourceKind.TimedNode or SourceKind.Fish => ("Gather", "GatherBuddyReborn gather list - Phase 5"),
                SourceKind.Venture => ("Venture", "ARC venture list - Phase 5"),
                SourceKind.GilVendor or SourceKind.SpecialShop => ("Vendor", "Lifestream teleport + map flag - Phase 5"),
                SourceKind.Market => ("Buy", "Lifestream /li mb + shopping list - Phase 5"),
                _ => ("", ""),
            };
            if (label.Length == 0) continue;
            if (!first) ImGui.SameLine();
            first = false;
            ImGui.BeginDisabled();
            ImGui.SmallButton(label);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tip);
        }
    }

    private string Name(uint itemId) => _plugin.GameData?.ItemName(itemId) ?? $"#{itemId}";
}
