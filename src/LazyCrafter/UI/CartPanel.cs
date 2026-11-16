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
        var dataFailed = _plugin.GameData is null && _plugin.GameDataLoad.IsCompleted;

        CrafterChrome.DrawStatCards(
            new CrafterChrome.Stat("Cart lines", cart.Count.ToString(), null,
                cart.Count == 0 ? "No recipes selected" : $"{cart.Sum(line => line.Crafts):N0} total crafts", null),
            new CrafterChrome.Stat("Missing materials", dataFailed && cart.Count == 0 ? "—" : missingCount.ToString(), null,
                dataFailed && cart.Count == 0 ? "Unknown until game data loads" : missingCount == 0 ? "Every material is accounted for" : $"Worst tier {Fmt.TierName(totals.Tier)}", null),
            new CrafterChrome.Stat("Estimated cash cost", hasEstimate ? (complete ? "" : ">") + Fmt.Gil(cashCost) : "—", null,
                !hasEstimate ? "No material prices are available yet" : complete ? "Material estimate is complete" : "Lower bound; some materials are unpriced", null));

        if (cart.Count == 0)
        {
            CrafterChrome.Card("cart_empty", () =>
            {
                CrafterChrome.Section("Build a cart from the Catalog");
                CrafterChrome.EmptyState("Your cart is empty", "Select a recipe in the Catalog, choose a quantity, then use Add to cart. The saved cart and all dispatch actions remain here.");
            }, minHeight: 240);
            return;
        }

        DrawToolbar(snap);
        CrafterChrome.Columns("cart", () => CrafterChrome.Card("cart_lines", () => DrawCartLines(snap), fill: true),
            () => CrafterChrome.Card("cart_missing", () => DrawMissing(snap), fill: true), railFraction: 0.5f);
    }

    private void DrawToolbar(CatalogSnapshot snap)
    {
        var dispatch = _plugin.Dispatch;
        var origin = ImGui.GetCursorScreenPos();
        var width = CrafterChrome.Avail();
        ImGui.SetCursorScreenPos(origin);
        if (dispatch.Running)
        {
            if (CrafterChrome.Button("stop", "Stop"))
                dispatch.Stop();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Abort the run: retainer queue, GBR and Artisan receive the existing stop requests.");
            CrafterChrome.WrapNext(CrafterChrome.ButtonWidth("Open run"));
            if (CrafterChrome.Button("open_run", "Open run"))
                _main.OpenRunTab();
        }
        else
        {
            if (CrafterChrome.Button("dispatch", "Dispatch", CrafterChrome.ButtonKind.Primary))
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
        }

        CrafterChrome.WrapNext(CrafterChrome.ButtonWidth("Export to TeamCraft"));
        if (CrafterChrome.Button("export", "Export to TeamCraft"))
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
        CrafterChrome.WrapNext(CrafterChrome.ButtonWidth("Clear"));
        if (CrafterChrome.Button("clear", "Clear"))
            _plugin.Catalog.ClearCart();

        // Run status sits right of the buttons when it fits, never wrapping inside a button row.
        string? status = null;
        UiState statusState = UiState.Idle;
        if (dispatch.Running)
        {
            status = dispatch.Status;
            statusState = UiState.Running;
        }
        else if (dispatch.Current is Adapters.DispatchService.Phase.Failed)
        {
            status = dispatch.Status;
            statusState = UiState.Failed;
        }
        if (!string.IsNullOrEmpty(status))
        {
            var draw = ImGui.GetWindowDrawList();
            var lineEnd = ImGui.GetItemRectMax();
            var statusPos = new System.Numerics.Vector2(lineEnd.X, ImGui.GetItemRectMin().Y);
            var lineY = statusPos.Y;
            var room = (origin.X + width) - statusPos.X;
            if (room < 200)
            {
                ImGui.NewLine();
                statusPos = ImGui.GetCursorScreenPos();
                lineY = statusPos.Y;
                room = width;
            }
            else
                statusPos = new System.Numerics.Vector2(statusPos.X + Theme.ItemSpacing.X, lineY);
            var markColor = Copy.MarkColor(statusState);
            var h = CrafterChrome.ControlHeight;
            draw.AddCircleFilled(statusPos + new System.Numerics.Vector2(Theme.DotRadius, h / 2f), Theme.DotRadius, Theme.Pack(markColor));
            CrafterChrome.DrawFitted(draw, statusPos + new System.Numerics.Vector2(14, (h - CrafterChrome.LineHeight(Theme.SecondaryScale)) / 2f), status, room - 14, Theme.TextSecondary, Theme.SecondaryScale);
            ImGui.SetCursorScreenPos(new System.Numerics.Vector2(origin.X, lineY));
            ImGui.Dummy(new System.Numerics.Vector2(1, h));
        }
        CrafterChrome.Advance(origin, width, ImGui.GetItemRectMax().Y - origin.Y, CrafterChrome.Gap);
    }

    private void DrawCartLines(CatalogSnapshot snap)
    {
        CrafterChrome.Section("Recipes", "Quantities update the saved cart immediately.");
        var flags = ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
        var width = CrafterChrome.Avail();
        if (!ImGui.BeginTable("##crafter_cart_lines", 5, flags, new System.Numerics.Vector2(width, 0)))
            return;
        ImGui.PushTextWrapPos(0f);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new System.Numerics.Vector2(Theme.Unit, Theme.Unit));
        ImGui.TableSetupColumn("Recipe", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Crafts", ImGuiTableColumnFlags.WidthFixed, (3 * CrafterChrome.ControlHeight) + 8);
        ImGui.TableSetupColumn("Tier", ImGuiTableColumnFlags.WidthFixed, CrafterChrome.TextWidth("Blocked") + (3 * Theme.Unit));
        ImGui.TableSetupColumn("Cost", ImGuiTableColumnFlags.WidthFixed, CrafterChrome.TextWidth("<888,888") + (3 * Theme.Unit));
        ImGui.TableSetupColumn("##remove", ImGuiTableColumnFlags.WidthFixed, CrafterChrome.ControlHeight);
        ImGui.TableHeadersRow();
        foreach (var line in snap.Cart)
        {
            ImGui.TableNextRow();
            ImGui.PushID((int)line.RecipeId);
            ImGui.TableNextColumn();
            CellText(line.Row?.Name ?? $"recipe {line.RecipeId}", Theme.TextPrimary);
            ImGui.TableNextColumn();
            var quantity = line.Crafts;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputInt("##quantity", ref quantity, 1, 10) && quantity != line.Crafts)
                _plugin.Catalog.SetCartQuantity(line.RecipeId, quantity);
            ImGui.TableNextColumn();
            CellText(Fmt.TierName(line.Assessment.Tier), Fmt.TierColor(line.Assessment.Tier));
            ImGui.TableNextColumn();
            if (line.Estimate is { } estimate)
                CellText((estimate.CostComplete ? "" : ">") + Fmt.Gil(estimate.CashCost), Theme.TextPrimary);
            else
                CellText("-", Theme.TextSecondary);
            ImGui.TableNextColumn();
            var removeWidth = CrafterChrome.SmallButtonWidth("×");
            var removeHeight = CrafterChrome.LineHeight(Theme.SecondaryScale) + (3 * Theme.Unit);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, (ImGui.GetContentRegionAvail().X - removeWidth) / 2f));
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ((CrafterChrome.ControlHeight - removeHeight) / 2f));
            if (CrafterChrome.SmallButton("remove", "×"))
                _plugin.Catalog.RemoveFromCart(line.RecipeId);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove this recipe from the cart.");
            ImGui.PopID();
        }
        ImGui.PopStyleVar();
        ImGui.PopTextWrapPos();
        ImGui.EndTable();
    }

    /// <summary>One line of cell text centred in a control-height row (drawn directly, so a framed neighbour cannot shift it).</summary>
    private static void CellText(string text, System.Numerics.Vector4 color)
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        CrafterChrome.DrawFitted(ImGui.GetWindowDrawList(), pos + new System.Numerics.Vector2(0, (CrafterChrome.ControlHeight - CrafterChrome.LineHeight()) / 2f), text, width, color);
        ImGui.Dummy(new System.Numerics.Vector2(1, CrafterChrome.ControlHeight));
    }

    private void DrawMissing(CatalogSnapshot snap)
    {
        var missing = snap.CartTotals.Missing.ToArray();
        CrafterChrome.Section("Missing materials", "One shared inventory ledger across every cart line.");
        if (missing.Length == 0)
        {
            CrafterChrome.Notice("Nothing missing", "No materials are missing from the current cart.", UiState.Ready);
            return;
        }
        var flags = ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
        var width = CrafterChrome.Avail();
        if (!ImGui.BeginTable("##crafter_cart_missing", 4, flags, new System.Numerics.Vector2(width, 0)))
            return;
        ImGui.PushTextWrapPos(0f);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new System.Numerics.Vector2(Theme.Unit, 2 * Theme.Unit));
        ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, CrafterChrome.TextWidth("Need") + (4 * Theme.Unit));
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch, 1.3f);
        ImGui.TableSetupColumn("Est.", ImGuiTableColumnFlags.WidthFixed, CrafterChrome.TextWidth("888,888") + (3 * Theme.Unit));
        ImGui.TableHeadersRow();
        foreach (var leaf in missing)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(Fmt.TierColor(leaf.Tier), _plugin.GameData?.ItemName(leaf.ItemId) ?? $"#{leaf.ItemId}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(leaf.Missing.ToString());
            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.TextSecondary, string.Join(", ", leaf.Sources.Where(source => source != Core.Model.SourceKind.OnHand).Select(Fmt.SourceName)));
            ImGui.TableNextColumn();
            if (_plugin.Catalog.UnitCost(leaf.ItemId) is { } unit)
                ImGui.TextUnformatted(Fmt.Gil(unit * leaf.Missing));
            else
                ImGui.TextDisabled("-");
        }
        ImGui.PopStyleVar();
        ImGui.PopTextWrapPos();
        ImGui.EndTable();
    }
}
