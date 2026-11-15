using Dalamud.Bindings.ImGui;
using LalaUiKit;
using LazyCrafter.Catalog;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;

namespace LazyCrafter.UI;

/// <summary>Selected-recipe economics, ingredient hierarchy and the existing fulfil hand-offs.</summary>
public sealed class IngredientTree
{
    private readonly Plugin _plugin;
    private int _qty = 1;
    private uint _qtyFor;

    public IngredientTree(Plugin plugin) => _plugin = plugin;

    public void Draw(CatalogRow row, CatalogSnapshot snap, CatalogView view, bool hq)
    {
        if (_qtyFor != row.RecipeId)
        {
            _qtyFor = row.RecipeId;
            _qty = 1;
        }
        DrawSummary(row, hq);

        if (row.Leaves.Count == 0)
        {
            Widgets.InfoBox("Empty — this recipe has no ingredient leaves to display.", UiState.Empty);
            return;
        }

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;
        var height = MathF.Max(240, ImGui.GetContentRegionAvail().Y);
        if (!ImGui.BeginTable("##crafter_ingredients", 5, flags, new System.Numerics.Vector2(0, height)))
            return;
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableSetupColumn("Ingredient", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Have/Need", ImGuiTableColumnFlags.WidthFixed, 82);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, 116);
        ImGui.TableSetupColumn("Unit", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Fulfil", ImGuiTableColumnFlags.WidthFixed, 174);
        ImGui.TableHeadersRow();
        foreach (var node in Core.IngredientTree.Build(row.Leaves))
            DrawNode(node, 0, row.JobId);
        ImGui.EndTable();
    }

    private void DrawSummary(CatalogRow row, bool hq)
    {
        var estimate = row.Est(hq);
        CrafterChrome.Card("recipe_summary", () =>
        {
            Widgets.Title(row.Name);
            Widgets.Caption($"{row.Job} {row.Level} · x{row.ResultAmount}/craft · {(row.LogComplete ? "in crafting log" : "not yet crafted")}");
            Widgets.StateText(Fmt.TierName(row.Tier), row.Tier == EffortTier.Now ? UiState.Ready : row.Tier == EffortTier.Blocked ? UiState.Blocked : UiState.Idle);
            ImGui.SameLine();
            Widgets.Secondary(row.HowMany > 0 ? $"Can craft {row.HowMany}" : "Cannot craft yet");

            if (estimate is not null)
            {
                Widgets.SecondaryWrapped($"Revenue {(estimate.RevenueKnown ? Fmt.Gil((hq ? estimate.RevenueHq : estimate.RevenueNq)!.Value) : "?")} · tax {Fmt.Gil(estimate.Tax)} · cash cost {(estimate.CostComplete ? "" : ">")}{Fmt.Gil(estimate.CashCost)} · market {Fmt.Gil(estimate.MarketCost)}");
                Widgets.SecondaryWrapped($"Margin cash {(estimate.MarginCash is { } cash ? Fmt.Gil(cash) : "?")} · market {(estimate.MarginMarket is { } market ? Fmt.Gil(market) : "?")} · /day {Fmt.Gil((long)Math.Round(estimate.PerDay))} · velocity {estimate.Velocity:0.#} · listings {estimate.Listings}");
                if (!estimate.CostComplete)
                    Widgets.Caption("Unpriced: " + string.Join(", ", estimate.UnpricedItems.Take(4).Select(Name)) +
                        (estimate.UnpricedItems.Count > 4 ? $" +{estimate.UnpricedItems.Count - 4}" : ""));
            }
            else if (!row.Marketable)
            {
                Widgets.InfoBox("Untradeable — no market value is estimated for this result.", UiState.Idle);
            }
            if (row.Scrip > 0)
                Widgets.Caption($"{row.Scrip} scrip per turn-in at maximum collectability");

            ImGui.SetNextItemWidth(76);
            ImGui.InputInt("##crafter_recipe_quantity", ref _qty);
            if (_qty < 1)
                _qty = 1;
            CrafterChrome.SameLineWrapped(CrafterChrome.ButtonWidth("Add to cart"));
            if (ImGui.Button("Add to cart"))
                _plugin.Catalog.AddToCart(row.RecipeId, _qty);
            CrafterChrome.SameLineWrapped(CrafterChrome.ButtonWidth("Copy TeamCraft link"));
            if (ImGui.Button("Copy TeamCraft link"))
            {
                var link = TeamcraftExport.Link([new TeamcraftExport.Line(row.ResultItemId, row.RecipeId, _qty * row.ResultAmount)]);
                if (link is not null)
                {
                    ImGui.SetClipboardText(link);
                    Plugin.ChatGui.Print("[LazyCrafter] TeamCraft link copied.");
                }
            }
        });
    }

    private void DrawNode(Core.IngredientTree.Node node, int depth, uint parentJob)
    {
        var leaf = node.Leaf;
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.PushID((int)leaf.ItemId + (depth * 1_000_000));
        var flags = ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.DefaultOpen;
        if (node.Children.Count == 0)
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.Bullet;
        var color = leaf.Missing == 0 ? Copy.MarkColor(UiState.Ready) : Fmt.TierColor(leaf.Tier);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        var open = ImGui.TreeNodeEx(Name(leaf.ItemId), flags);
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"item {leaf.ItemId}\n{string.Join(", ", leaf.Sources.Select(Fmt.SourceName))}");

        ImGui.TableNextColumn();
        ImGui.TextColored(leaf.Missing == 0 ? Copy.MarkColor(UiState.Ready) : Theme.TextPrimary, $"{leaf.Have}/{leaf.Need}");
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(leaf.Missing == 0 ? "on hand" : string.Join(", ", leaf.Sources.Where(source => source != SourceKind.OnHand).Select(Fmt.SourceName)));
        ImGui.TableNextColumn();
        if (_plugin.Catalog.UnitCost(leaf.ItemId) is { } unit)
            ImGui.TextUnformatted(Fmt.Gil(unit));
        else
            ImGui.TextDisabled("-");
        ImGui.TableNextColumn();
        DrawFulfil(leaf, parentJob);

        if (open && node.Children.Count > 0)
        {
            var subJob = _plugin.Dispatch.RecipeFor(leaf.ItemId, parentJob)?.JobId ?? parentJob;
            foreach (var child in node.Children)
                DrawNode(child, depth + 1, subJob);
            ImGui.TreePop();
        }
        ImGui.PopID();
    }

    /// <summary>Each visible action calls the same hand-off and disabled logic as before the refresh.</summary>
    private void DrawFulfil(IngredientLeaf leaf, uint parentJob)
    {
        var dispatch = _plugin.Dispatch;
        var running = dispatch.Running;
        var outside = _plugin.Inventory.StoredWhere(leaf.ItemId);
        var reachable = outside.Where(place => place.Fetchable).ToList();
        var needed = Math.Max(0, leaf.Need - _plugin.Inventory.CountInBags(leaf.ItemId));
        var fetchable = Math.Min(needed, reachable.Sum(place => place.Quantity));
        if (fetchable > 0)
        {
            if (running)
                ImGui.BeginDisabled();
            if (ImGui.SmallButton("Retrieve"))
            {
                var itemId = leaf.ItemId;
                var quantity = fetchable;
                Plugin.Framework.RunOnFrameworkThread(() => dispatch.RetrieveOne(itemId, quantity));
            }
            if (running)
                ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(running
                    ? $"Dispatch running: {dispatch.Status}"
                    : $"Fetch {fetchable} from {Places(DispatchPlan.PlacesFor(reachable, fetchable))} into bags at a summoning bell.");
            if (leaf.Missing > 0)
                ImGui.SameLine();
        }

        if (leaf.Missing == 0)
            return;
        var first = true;
        var shown = new HashSet<string>();
        foreach (var source in leaf.Sources)
        {
            var (label, tooltip) = source switch
            {
                SourceKind.SubCraft => ("Craft", $"Artisan: craft {leaf.Missing} with the same-job recipe"),
                SourceKind.RegularNode or SourceKind.TimedNode or SourceKind.Fish => ("Gather", $"GatherBuddyReborn: add {leaf.Missing} to the LazyCrafter gather list and start auto-gather"),
                SourceKind.Venture => ("Venture", $"ARC: queue {leaf.Missing} on the LazyCrafter venture list"),
                SourceKind.GilVendor => ("Vendor", "Lifestream: travel to the nearest vendor and set a map flag"),
                SourceKind.Market => ("Buy", "Lifestream: open the market board route and print a shopping list"),
                _ => ("", ""),
            };
            if (label.Length == 0 || !shown.Add(label))
                continue;
            if (!first)
                ImGui.SameLine();
            first = false;
            var disabled = running && label != "Vendor" && label != "Buy";
            if (disabled)
                ImGui.BeginDisabled();
            if (ImGui.SmallButton(label))
            {
                var itemId = leaf.ItemId;
                var quantity = leaf.Missing;
                Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    switch (label)
                    {
                        case "Craft":
                        {
                            var subRecipe = dispatch.RecipeFor(itemId, parentJob);
                            if (subRecipe is null)
                            {
                                Plugin.ChatGui.PrintError($"[LazyCrafter] no recipe found for {Name(itemId)}.");
                                break;
                            }
                            dispatch.CraftOne(subRecipe.RecipeId, (quantity + Math.Max(1, subRecipe.ResultAmount) - 1) / Math.Max(1, subRecipe.ResultAmount));
                            break;
                        }
                        case "Gather": dispatch.GatherOne(itemId, quantity); break;
                        case "Venture": dispatch.VentureOne(itemId, quantity); break;
                        case "Vendor": dispatch.VendorOne(itemId, quantity); break;
                        case "Buy": dispatch.MarketOne(itemId, quantity); break;
                    }
                });
            }
            if (disabled)
                ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(disabled ? $"Dispatch running: {dispatch.Status}" : tooltip);
        }
    }

    private static string Places(IReadOnlyList<StoredElsewhere> where) => string.Join(", ", where.Select(place => place.Where));

    private string Name(uint itemId) => _plugin.GameData?.ItemName(itemId) ?? $"#{itemId}";
}
