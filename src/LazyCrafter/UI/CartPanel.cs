using Dalamud.Bindings.ImGui;
using LalaUiKit;
using LazyCrafter.Catalog;
using LazyCrafter.Core;

namespace LazyCrafter.UI;

/// <summary>Cart quantities, the shared missing-material ledger, and the existing dispatch/export actions.</summary>
public sealed class CartPanel
{
    private readonly Plugin _plugin;
    private readonly MainWindow _main;
    private DispatchPlan.Plan? _preview;
    private int _previewFor = -1;

    public CartPanel(Plugin plugin, MainWindow main)
    {
        _plugin = plugin;
        _main = main;
    }

    private string PreviewText(DispatchPlan.Plan plan)
    {
        string Name(uint id) => _plugin.GameData?.ItemName(id) ?? $"#{id}";
        var text = new System.Text.StringBuilder("Dispatch order: ARC -> GBR -> Artisan\n");
        if (plan.Retrievals.Count > 0)
            text.Append("RETRIEVE FIRST (not in bags): ").AppendJoin(", ", plan.Retrievals.Select(item => $"{Name(item.ItemId)} x{item.Quantity} from {item.Places}")).Append('\n');
        if (plan.Ventures.Count > 0)
            text.Append("ARC ventures: ").AppendJoin(", ", plan.Ventures.Select(item => $"{Name(item.ItemId)} x{item.Quantity} ({item.Match.Retainer.Name})")).Append('\n');
        if (plan.Gathers.Count > 0)
            text.Append("GBR gather: ").AppendJoin(", ", plan.Gathers.Select(item => $"{Name(item.ItemId)} x{item.Quantity}")).Append('\n');
        if (plan.Crafts.Count > 0)
            text.Append("Artisan crafts: ").AppendJoin(", ", plan.Crafts.Select(item => $"{Name(item.ResultItemId)} x{item.Crafts}{(item.AfterGather ? "*" : "")}")).Append('\n');
        if (plan.Vendor.Count > 0)
            text.Append("Gil vendor (list + map flag): ").AppendJoin(", ", plan.Vendor.Select(item => $"{Name(item.ItemId)} x{item.Quantity}")).Append('\n');
        if (plan.Market.Count > 0)
            text.Append("Market board (list): ").AppendJoin(", ", plan.Market.Select(item => $"{Name(item.ItemId)} x{item.Quantity}")).Append('\n');
        if (plan.Manual.Count > 0)
            text.Append("Manual: ").AppendJoin(", ", plan.Manual.Select(item => $"{Name(item.ItemId)} x{item.Quantity}")).Append('\n');
        if (plan.Deferred.Count > 0)
            text.Append("Not crafted yet (blocked): ").AppendJoin(", ", plan.Deferred.Select(item => Name(item.ResultItemId))).Append('\n');
        if (plan.Crafts.Any(item => item.AfterGather))
            text.Append("* waits for GBR to finish");
        return text.ToString().TrimEnd();
    }

    public void Draw(CatalogSnapshot snap)
    {
        var cart = snap.Cart;
        var totals = snap.CartTotals;
        var missingCount = totals.Missing.Count();
        var cashCost = cart.Sum(line => line.Estimate?.CashCost ?? 0);
        var hasEstimate = cart.Any(line => line.Estimate is not null);
        var complete = cart.All(line => line.Estimate is null || line.Estimate.CostComplete);

        CrafterChrome.DrawStatCards(
            new CrafterChrome.Stat("Cart lines", cart.Count.ToString(), null,
                cart.Count == 0 ? "No recipes selected" : $"{cart.Sum(line => line.Crafts):N0} total crafts", null),
            new CrafterChrome.Stat("Missing materials", missingCount.ToString(), null,
                missingCount == 0 ? "Every material is accounted for" : $"Worst tier {Fmt.TierName(totals.Tier)}", null),
            new CrafterChrome.Stat("Estimated cash cost", hasEstimate ? (complete ? "" : ">") + Fmt.Gil(cashCost) : "—", null,
                !hasEstimate ? "No material prices are available yet" : complete ? "Material estimate is complete" : "Lower bound; some materials are unpriced", null));

        if (cart.Count == 0)
        {
            CrafterChrome.Card("cart_empty", () =>
            {
                CrafterChrome.Section("Build a cart from the Catalog");
                Widgets.InfoBox("Empty — select a recipe, choose a quantity, then use Add to cart. The saved cart and all dispatch actions remain here.", UiState.Empty);
                if (ImGui.Button("Browse catalog"))
                    _main.OpenCatalog();
            });
            return;
        }

        DrawToolbar(snap);
        CrafterChrome.TwoColumns("cart", 0.52f,
            () => CrafterChrome.Card("cart_lines", () => DrawCartLines(snap)),
            () => CrafterChrome.Card("cart_missing", () => DrawMissing(snap)));
    }

    private void DrawToolbar(CatalogSnapshot snap)
    {
        var dispatch = _plugin.Dispatch;
        if (dispatch.Running)
        {
            if (Widgets.StopButton("Stop"))
                dispatch.Stop();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Abort the run: retainer queue, GBR and Artisan receive the existing stop requests.");
            CrafterChrome.SameLineWrapped(CrafterChrome.ButtonWidth("Open run"));
            if (ImGui.Button("Open run"))
                _main.OpenRunTab();
            CrafterChrome.SameLineWrapped(220);
            Widgets.StateText(Copy.Word(UiState.Running), UiState.Running);
            ImGui.SameLine();
            Widgets.SecondaryWrapped(dispatch.Status);
        }
        else
        {
            if (ImGui.Button("Dispatch"))
                dispatch.DispatchCart();
            if (ImGui.IsItemHovered())
            {
                if (_previewFor != snap.Generation)
                {
                    _preview = dispatch.PlanFor();
                    _previewFor = snap.Generation;
                }
                ImGui.SetTooltip(_preview is null
                    ? "Sends ventures to ARC, gathering to GBR, then crafts with Artisan in that order."
                    : PreviewText(_preview));
            }
            if (dispatch.Current is Adapters.DispatchService.Phase.Failed)
            {
                ImGui.SameLine();
                Widgets.StateText(dispatch.Status, UiState.Failed);
            }
        }

        CrafterChrome.SameLineWrapped(CrafterChrome.ButtonWidth("Export to TeamCraft"));
        if (ImGui.Button("Export to TeamCraft"))
        {
            var link = TeamcraftExport.Link(snap.Cart.Where(line => line.Row is not null)
                .Select(line => new TeamcraftExport.Line(line.Row!.ResultItemId, line.RecipeId, line.Crafts * line.Row.ResultAmount)));
            if (link is not null)
            {
                ImGui.SetClipboardText(link);
                Plugin.ChatGui.Print("[LazyCrafter] TeamCraft import link copied to clipboard.");
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copy an import link for the final items in the cart.");
        CrafterChrome.SameLineWrapped(CrafterChrome.ButtonWidth("Clear"));
        if (ImGui.Button("Clear"))
            _plugin.Catalog.ClearCart();
        ImGui.Dummy(new System.Numerics.Vector2(0, Theme.Unit));
    }

    private void DrawCartLines(CatalogSnapshot snap)
    {
        CrafterChrome.Section("Recipes", "Quantities update the saved cart immediately.");
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("##crafter_cart_lines", 5, flags, new System.Numerics.Vector2(0, TableHeight(snap.Cart.Count))))
            return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Recipe", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Crafts", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Tier", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Cost", ImGuiTableColumnFlags.WidthFixed, 82);
        ImGui.TableSetupColumn("##remove", ImGuiTableColumnFlags.WidthFixed, 34);
        ImGui.TableHeadersRow();
        foreach (var line in snap.Cart)
        {
            ImGui.TableNextRow();
            ImGui.PushID((int)line.RecipeId);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(line.Row?.Name ?? $"recipe {line.RecipeId}");
            ImGui.TableNextColumn();
            var quantity = line.Crafts;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputInt("##quantity", ref quantity, 1, 10) && quantity != line.Crafts)
                _plugin.Catalog.SetCartQuantity(line.RecipeId, quantity);
            ImGui.TableNextColumn();
            ImGui.TextColored(Fmt.TierColor(line.Assessment.Tier), Fmt.TierName(line.Assessment.Tier));
            ImGui.TableNextColumn();
            if (line.Estimate is { } estimate)
                ImGui.TextUnformatted((estimate.CostComplete ? "" : ">") + Fmt.Gil(estimate.CashCost));
            else
                ImGui.TextDisabled("-");
            ImGui.TableNextColumn();
            if (ImGui.SmallButton("×"))
                _plugin.Catalog.RemoveFromCart(line.RecipeId);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove this recipe from the cart.");
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private void DrawMissing(CatalogSnapshot snap)
    {
        var missing = snap.CartTotals.Missing.ToArray();
        CrafterChrome.Section("Missing materials", "One shared inventory ledger across every cart line.");
        if (missing.Length == 0)
        {
            Widgets.InfoBox("Ready — no materials are missing from the current cart.", UiState.Ready);
            return;
        }
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("##crafter_cart_missing", 4, flags, new System.Numerics.Vector2(0, TableHeight(missing.Length))))
            return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("Est.", ImGuiTableColumnFlags.WidthFixed, 82);
        ImGui.TableHeadersRow();
        foreach (var leaf in missing)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(Fmt.TierColor(leaf.Tier), _plugin.GameData?.ItemName(leaf.ItemId) ?? $"#{leaf.ItemId}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(leaf.Missing.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(string.Join(", ", leaf.Sources.Where(source => source != Core.Model.SourceKind.OnHand).Select(Fmt.SourceName)));
            ImGui.TableNextColumn();
            if (_plugin.Catalog.UnitCost(leaf.ItemId) is { } unit)
                ImGui.TextUnformatted(Fmt.Gil(unit * leaf.Missing));
            else
                ImGui.TextDisabled("-");
        }
        ImGui.EndTable();
    }

    private static float TableHeight(int rows) =>
        Math.Clamp((rows + 1.5f) * ImGui.GetTextLineHeightWithSpacing(), 120, 340);
}
