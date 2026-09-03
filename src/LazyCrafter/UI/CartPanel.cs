using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using LazyCrafter.Catalog;
using LazyCrafter.Core;

namespace LazyCrafter.UI;

/// <summary>
/// Bottom panel (Plan §Phase 4 task 4): the cart - recipe x quantity lines with cash cost, the aggregated missing
/// list across the whole cart (one shared inventory ledger, see <see cref="Tiering.AssessCart"/>), a disabled
/// <b>Dispatch</b> button that Phase 5 turns on, and <b>Export to TeamCraft</b>.
/// </summary>
public sealed class CartPanel
{
    private readonly Plugin _plugin;
    private bool _collapsed;

    public CartPanel(Plugin plugin) => _plugin = plugin;

    public float DesiredHeight(CatalogSnapshot snap)
    {
        var line = ImGui.GetTextLineHeightWithSpacing();
        if (_collapsed || snap.Cart.Count == 0) return line * 2.2f;
        var rows = Math.Min(6, Math.Max(snap.Cart.Count, snap.CartTotals.Missing.Count()));
        return line * (rows + 4.5f);
    }

    public void Draw(CatalogSnapshot snap)
    {
        var cart = snap.Cart;
        var totals = snap.CartTotals;
        var svc = _plugin.Catalog;

        if (!ImGui.BeginChild("##lcraft-cart", new Vector2(0, DesiredHeight(snap)), true)) { ImGui.EndChild(); return; }

        var header = cart.Count == 0 ? "Cart (empty)" : $"Cart - {cart.Count} recipe{(cart.Count == 1 ? "" : "s")}, {totals.Missing.Count()} materials missing, worst tier {Fmt.TierName(totals.Tier)}";
        if (ImGui.SmallButton(_collapsed ? ">" : "v")) _collapsed = !_collapsed;
        ImGui.SameLine();
        ImGui.TextColored(ImGuiColors.ParsedGold, header);
        if (cart.Count > 0)
        {
            var cost = cart.Sum(l => l.Estimate?.CashCost ?? 0);
            var complete = cart.All(l => l.Estimate is null || l.Estimate.CostComplete);
            ImGui.SameLine();
            ImGui.TextDisabled($"cash cost {(complete ? "" : ">")}{Fmt.Gil(cost)}");
            ImGui.SameLine();
            ImGui.BeginDisabled();
            ImGui.Button("Dispatch");
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Phase 5: sends ventures to ARC, gathering to GBR, then crafts with Artisan.");
            ImGui.SameLine();
            if (ImGui.Button("Export to TeamCraft"))
            {
                var link = TeamcraftExport.Link(cart.Where(l => l.Row is not null).Select(l => new TeamcraftExport.Line(l.Row!.ResultItemId, l.RecipeId, l.Crafts * l.Row.ResultAmount)));
                if (link is not null) { ImGui.SetClipboardText(link); Plugin.ChatGui.Print("[LazyCrafter] TeamCraft import link copied to clipboard."); }
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Copies a https://ffxivteamcraft.com/import/ link for the final items in the cart.");
            ImGui.SameLine();
            if (ImGui.Button("Clear")) svc.ClearCart();
        }
        if (_collapsed || cart.Count == 0) { ImGui.EndChild(); return; }

        var avail = ImGui.GetContentRegionAvail();
        var half = (avail.X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;

        if (ImGui.BeginChild("##cart-lines", new Vector2(half, 0), false))
        {
            if (ImGui.BeginTable("##cartlines", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("Recipe", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Crafts", ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableSetupColumn("Tier", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("Cost", ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableSetupColumn("##rm", ImGuiTableColumnFlags.WidthFixed, 22);
                ImGui.TableHeadersRow();
                foreach (var line in cart)
                {
                    ImGui.TableNextRow();
                    ImGui.PushID((int)line.RecipeId);
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(line.Row?.Name ?? $"recipe {line.RecipeId}");
                    ImGui.TableNextColumn();
                    var q = line.Crafts;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputInt("##q", ref q, 1, 10) && q != line.Crafts) svc.SetCartQuantity(line.RecipeId, q);
                    ImGui.TableNextColumn(); ImGui.TextColored(Fmt.TierColor(line.Assessment.Tier), Fmt.TierName(line.Assessment.Tier));
                    ImGui.TableNextColumn();
                    if (line.Estimate is { } e) ImGui.TextUnformatted((e.CostComplete ? "" : ">") + Fmt.Gil(e.CashCost)); else ImGui.TextDisabled("-");
                    ImGui.TableNextColumn();
                    if (ImGui.SmallButton("x")) svc.RemoveFromCart(line.RecipeId);
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##cart-missing", new Vector2(half, 0), false))
        {
            if (ImGui.BeginTable("##cartmissing", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("Missing material", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, 110);
                ImGui.TableSetupColumn("Est.", ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableHeadersRow();
                foreach (var leaf in totals.Missing)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextColored(Fmt.TierColor(leaf.Tier), _plugin.GameData?.ItemName(leaf.ItemId) ?? $"#{leaf.ItemId}");
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(leaf.Missing.ToString());
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(string.Join(", ", leaf.Sources.Where(s => s != Core.Model.SourceKind.OnHand).Select(Fmt.SourceName)));
                    ImGui.TableNextColumn();
                    if (svc.UnitCost(leaf.ItemId) is { } u) ImGui.TextUnformatted(Fmt.Gil(u * leaf.Missing)); else ImGui.TextDisabled("-");
                }
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
        ImGui.EndChild();
    }
}
