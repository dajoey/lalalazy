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
        DrawIngredients(row);
        DrawEconomics(row, hq);
    }

    private void DrawIngredients(CatalogRow row)
    {
        CrafterChrome.Card("recipe_ingredients", () =>
        {
            CrafterChrome.Section("Ingredients", null, row.Leaves.Count == 0 ? null : $"{row.Leaves.Count} items");
            if (row.Leaves.Count == 0)
            {
                CrafterChrome.Paragraph("This recipe has no ingredient leaves to display.");
                return;
            }

            var flags = ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
            var width = CrafterChrome.Avail();
            if (!ImGui.BeginTable("##crafter_ingredients", 2, flags, new System.Numerics.Vector2(width, 0)))
                return;
            ImGui.PushTextWrapPos(0f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(Theme.Unit, Theme.Unit / 2f));
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new System.Numerics.Vector2(0, Theme.Unit * 1.5f));
            ImGui.TableSetupColumn("Ingredient", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Have/Need", ImGuiTableColumnFlags.WidthFixed, MathF.Max(CrafterChrome.TextWidth("888/888"), CrafterChrome.TextWidth("Have/Need", Theme.CaptionScale)) + (2 * Theme.Unit));
            DrawHeader();
            foreach (var node in Core.IngredientTree.Build(row.Leaves))
                DrawNode(node, 0, row.JobId);
            ImGui.PopStyleVar(2);
            ImGui.PopTextWrapPos();
            ImGui.EndTable();
        });
    }

    private static void DrawHeader()
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        // The longest wording that fits whole; a narrow rail or a large UI scale falls back to shorter ones.
        var room = ImGui.GetContentRegionAvail().X - 2;
        var heading = new[] { "Ingredient · source · unit price", "Ingredient · source · price", "Ingredient · source", "Ingredient" }
            .FirstOrDefault(text => CrafterChrome.TextWidth(text, Theme.CaptionScale) <= room)
            ?? CrafterChrome.Fit("Ingredient", room, Theme.CaptionScale);
        ImGui.SetWindowFontScale(Theme.CaptionScale);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.PushTextWrapPos(-1f);
        ImGui.TextUnformatted(heading);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
        ImGui.SetWindowFontScale(1f);
        ImGui.TableNextColumn();
        var text = "Have/Need";
        var avail = ImGui.GetContentRegionAvail().X;
        var width = CrafterChrome.TextWidth(text, Theme.CaptionScale);
        if (avail > width)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - width - 1);
        ImGui.PushTextWrapPos(-1f);
        Widgets.Caption(text);
        ImGui.PopTextWrapPos();
    }

    private void DrawSummary(CatalogRow row, bool hq)
    {
        CrafterChrome.Card("recipe_summary", () =>
        {
            var draw = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var width = CrafterChrome.Avail();
            var title = CrafterChrome.ClampLines(row.Name, width, Theme.TitleScale, 2);
            var used = CrafterChrome.DrawWrapped(draw, pos, title, width, Theme.TextPrimary, Theme.TitleScale);
            if (title != row.Name && ImGui.IsMouseHoveringRect(pos, pos + new System.Numerics.Vector2(width, used)))
                ImGui.SetTooltip(row.Name);
            var y = used + Theme.Unit;
            CrafterChrome.DrawFitted(draw, pos + new System.Numerics.Vector2(0, y),
                $"{row.Job} {row.Level} · x{row.ResultAmount}/craft · {(row.LogComplete ? "in crafting log" : "not yet crafted")}", width, Theme.TextSecondary, Theme.SecondaryScale);
            y += CrafterChrome.LineHeight(Theme.SecondaryScale) + (2 * Theme.Unit);
            var tierState = row.Tier == EffortTier.Now ? UiState.Ready : row.Tier == EffortTier.Blocked ? UiState.Blocked : UiState.Idle;
            var tierName = Fmt.TierName(row.Tier);
            var tierColor = tierState == UiState.Idle ? Theme.TextPrimary : Copy.MarkColor(tierState);
            CrafterChrome.DrawText(draw, pos + new System.Numerics.Vector2(0, y), tierName, tierColor);
            CrafterChrome.DrawFitted(draw, pos + new System.Numerics.Vector2(CrafterChrome.TextWidth(tierName) + (2 * Theme.Unit), y),
                row.HowMany > 0 ? $"Can craft {row.HowMany}" : "Cannot craft yet", width - CrafterChrome.TextWidth(tierName) - (2 * Theme.Unit), Theme.TextSecondary);
            y += CrafterChrome.LineHeight() + (3 * Theme.Unit);
            CrafterChrome.Advance(pos, width, y, 0);

            if (row.Scrip > 0)
                CrafterChrome.Paragraph($"{row.Scrip} scrip per turn-in at maximum collectability", Theme.SecondaryScale);

            ImGui.Dummy(new System.Numerics.Vector2(0, Theme.Unit));
            ImGui.SetNextItemWidth(88);
            ImGui.InputInt("##crafter_recipe_quantity", ref _qty);
            if (_qty < 1)
                _qty = 1;
            CrafterChrome.WrapNext(CrafterChrome.ButtonWidth("Add to cart"));
            if (CrafterChrome.Button("add", "Add to cart", CrafterChrome.ButtonKind.Primary))
                _plugin.Catalog.AddToCart(row.RecipeId, _qty);
            CrafterChrome.WrapNext(CrafterChrome.ButtonWidth("Copy TeamCraft link"));
            if (CrafterChrome.Button("link", "Copy TeamCraft link"))
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

    private void DrawEconomics(CatalogRow row, bool hq)
    {
        var estimate = row.Est(hq);
        if (estimate is null && row.Marketable)
            return;
        CrafterChrome.Card("recipe_economics", () =>
        {
            CrafterChrome.Section("Market");
            if (estimate is null)
            {
                CrafterChrome.Notice("Untradeable", "No market value is estimated for this result.", UiState.Idle);
                return;
            }
            string Gil(long? value) => value is { } v ? Fmt.Gil(v) : "?";
            var revenue = estimate.RevenueKnown ? Fmt.Gil((hq ? estimate.RevenueHq : estimate.RevenueNq)!.Value) : "?";
            CrafterChrome.KeyValue("Revenue", $"{revenue} (tax {Fmt.Gil(estimate.Tax)})", false);
            CrafterChrome.KeyValue("Cost", $"{(estimate.CostComplete ? "" : ">")}{Fmt.Gil(estimate.CashCost)} cash · {Fmt.Gil(estimate.MarketCost)} market");
            CrafterChrome.KeyValue("Margin", $"{Gil(estimate.MarginCash)} cash · {Gil(estimate.MarginMarket)} market",
                true, estimate.MarginCash < 0 ? Theme.StateRed : null);
            CrafterChrome.KeyValue("Per day", Fmt.Gil((long)Math.Round(estimate.PerDay)));
            CrafterChrome.KeyValue("Demand", $"{estimate.Velocity:0.#}/day · {estimate.Listings} listings");
            if (!estimate.CostComplete)
                CrafterChrome.Paragraph("Unpriced: " + string.Join(", ", estimate.UnpricedItems.Take(4).Select(Name)) +
                    (estimate.UnpricedItems.Count > 4 ? $" +{estimate.UnpricedItems.Count - 4}" : ""), Theme.CaptionScale, gap: 0);
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
        var labelOffset = ImGui.GetFontSize() + (2 * Theme.Unit);
        var name = Name(leaf.ItemId);
        var nodeWidth = ImGui.GetContentRegionAvail().X - labelOffset;
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        var open = ImGui.TreeNodeEx(CrafterChrome.Fit(name, nodeWidth) + "###node", flags);
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"{name}\nitem {leaf.ItemId}\n{string.Join(", ", leaf.Sources.Select(Fmt.SourceName))}");

        // Source and unit price sit under the name, aligned with the label text.
        var left = ImGui.GetCursorPosX() + labelOffset;
        var source = leaf.Missing == 0 ? "on hand" : string.Join(", ", leaf.Sources.Where(s => s != SourceKind.OnHand).Select(Fmt.SourceName));
        var unit = _plugin.Catalog.UnitCost(leaf.ItemId) is { } cost ? Fmt.Gil(cost) + " gil each" : "no price";
        ImGui.SetCursorPosX(left);
        ImGui.SetWindowFontScale(Theme.SecondaryScale);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(source.Length == 0 ? unit : $"{source} · {unit}");
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
        ImGui.SetWindowFontScale(1f);
        ImGui.SetCursorPosX(left);
        DrawFulfil(leaf, parentJob);

        ImGui.TableNextColumn();
        var haveNeed = $"{leaf.Have}/{leaf.Need}";
        var avail = ImGui.GetContentRegionAvail().X;
        var needWidth = ImGui.CalcTextSize(haveNeed).X;
        if (avail > needWidth)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - needWidth - 1);
        ImGui.PushTextWrapPos(-1f);
        ImGui.TextColored(leaf.Missing == 0 ? Copy.MarkColor(UiState.Ready) : Theme.TextPrimary, haveNeed);
        ImGui.PopTextWrapPos();

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
        var startX = ImGui.GetCursorPosX();
        var placed = false;
        if (fetchable > 0)
        {
            if (CrafterChrome.SmallButton("retrieve", "Retrieve", running))
            {
                var itemId = leaf.ItemId;
                var quantity = fetchable;
                Plugin.Framework.RunOnFrameworkThread(() => dispatch.RetrieveOne(itemId, quantity));
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(running
                    ? $"Dispatch running: {dispatch.Status}"
                    : $"Fetch {fetchable} from {Places(DispatchPlan.PlacesFor(reachable, fetchable))} into bags at a summoning bell.");
            placed = true;
        }

        if (leaf.Missing == 0)
            return;
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
            if (placed)
            {
                if (ImGui.GetItemRectMax().X + Theme.ItemSpacing.X + CrafterChrome.SmallButtonWidth(label) > ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X)
                    ImGui.SetCursorPosX(startX);
                else
                    ImGui.SameLine();
            }
            placed = true;
            var disabled = running && label != "Vendor" && label != "Buy";
            if (CrafterChrome.SmallButton("fulfil_" + label, label, disabled))
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
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(disabled ? $"Dispatch running: {dispatch.Status}" : tooltip);
        }
    }

    private static string Places(IReadOnlyList<StoredElsewhere> where) => string.Join(", ", where.Select(place => place.Where));

    private string Name(uint itemId) => _plugin.GameData?.ItemName(itemId) ?? $"#{itemId}";
}
