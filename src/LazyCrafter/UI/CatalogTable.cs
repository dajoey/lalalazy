using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using LazyCrafter.Catalog;
using LazyCrafter.Core.Model;

namespace LazyCrafter.UI;

/// <summary>
/// The sortable catalog (Plan §Phase 4 task 2). Columns: item · job · lvl · craftable · margin (cash) · margin
/// (market) · /day · velocity · saturation · scrip · desynth · tier · missing summary. Sorting is delegated to
/// the worker (<see cref="ViewRequest.Sort"/>): the header click only changes the request, the rows arrive
/// pre-sorted. Rows are drawn through a list clipper so a 6,000-row bucket costs one screen of widgets.
/// </summary>
public sealed class CatalogTable
{
    private readonly Plugin _plugin;

    private static readonly (string Header, SortKey Key, ImGuiTableColumnFlags Flags, float Width)[] Columns =
    [
        ("Item", SortKey.Name, ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide, 0),
        ("Job", SortKey.Job, ImGuiTableColumnFlags.WidthFixed, 44),
        ("Lvl", SortKey.Level, ImGuiTableColumnFlags.WidthFixed, 36),
        ("Can", SortKey.Craftable, ImGuiTableColumnFlags.WidthFixed, 40),
        ("Margin$", SortKey.MarginCash, ImGuiTableColumnFlags.WidthFixed, 74),
        ("Margin@mkt", SortKey.MarginMarket, ImGuiTableColumnFlags.WidthFixed, 82),
        ("/day", SortKey.PerDay, ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.PreferSortDescending, 74),
        ("Vel", SortKey.Velocity, ImGuiTableColumnFlags.WidthFixed, 46),
        ("Sat", SortKey.Saturation, ImGuiTableColumnFlags.WidthFixed, 46),
        ("Scrip", SortKey.Scrip, ImGuiTableColumnFlags.WidthFixed, 48),
        ("Desynth", SortKey.Desynth, ImGuiTableColumnFlags.WidthFixed, 62),
        ("Tier", SortKey.Tier, ImGuiTableColumnFlags.WidthFixed, 56),
        ("Missing", SortKey.Missing, ImGuiTableColumnFlags.WidthStretch, 0),
    ];

    public CatalogTable(Plugin plugin) => _plugin = plugin;

    /// <summary>Draws the table; returns the (possibly changed) sort. <paramref name="selected"/> is updated on click.</summary>
    public (SortKey Sort, bool Descending) Draw(CatalogView view, CatalogSnapshot snap, CatalogTab tab, bool hq, ref uint selected, SortKey sort, bool descending)
    {
        var rows = view.Rows;
        var extra = tab switch { CatalogTab.Leveling => "EXP", CatalogTab.LogCompletion => "Cost", CatalogTab.Undersupplied => "Listings", _ => null };

        if (view.Generation != snap.Generation && rows.Count == 0)
            ImGui.TextDisabled(snap.Generation == 0 ? "Computing the catalog..." : "Filtering...");
        else
            ImGui.TextDisabled($"{rows.Count} recipes");

        var flags = ImGuiTableFlags.Sortable | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY
                  | ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable | ImGuiTableFlags.SizingStretchProp;
        var colCount = Columns.Length + (extra is null ? 0 : 1);
        // One table id per tab: the column set differs per tab and each tab keeps its own sort/width state.
        if (!ImGui.BeginTable($"##catalog-{tab}", colCount, flags)) return (sort, descending);

        ImGui.TableSetupScrollFreeze(0, 1);
        for (var i = 0; i < Columns.Length; i++)
        {
            var (header, key, cflags, width) = Columns[i];
            var f = cflags;
            if (key == sort) f |= descending ? ImGuiTableColumnFlags.PreferSortDescending : ImGuiTableColumnFlags.PreferSortAscending;
            if (key == sort) f |= ImGuiTableColumnFlags.DefaultSort;
            ImGui.TableSetupColumn(header, f, width, (uint)key + 1);
        }
        if (extra is not null)
        {
            var key = tab switch { CatalogTab.Leveling => SortKey.Exp, CatalogTab.LogCompletion => SortKey.CashCost, _ => SortKey.Listings };
            ImGui.TableSetupColumn(extra, ImGuiTableColumnFlags.WidthFixed | (key == sort ? ImGuiTableColumnFlags.DefaultSort : 0), 64, (uint)key + 1);
        }
        ImGui.TableHeadersRow();

        // Sort: ImGui tells us which column the user clicked; we translate to a SortKey and let the worker sort.
        var specs = ImGui.TableGetSortSpecs();
        if (!specs.IsNull && specs.SpecsDirty && specs.SpecsCount > 0)
        {
            var spec = specs.Specs;
            var key = (SortKey)(spec.ColumnUserID - 1);
            sort = key;
            descending = spec.SortDirection == ImGuiSortDirection.Descending;
            specs.SpecsDirty = false;
        }

        var clipper = ImGui.ImGuiListClipper();
        clipper.Begin(rows.Count, ImGui.GetTextLineHeightWithSpacing());
        while (clipper.Step())
        {
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                var row = rows[i];
                ImGui.TableNextRow();
                ImGui.PushID((int)row.RecipeId);
                DrawRow(row, view, hq, tab, ref selected);
                ImGui.PopID();
            }
        }
        clipper.End();
        clipper.Destroy();
        ImGui.EndTable();
        return (sort, descending);
    }

    private void DrawRow(CatalogRow row, CatalogView view, bool hq, CatalogTab tab, ref uint selected)
    {
        var est = row.Est(hq);
        var isSel = selected == row.RecipeId;

        ImGui.TableNextColumn();
        if (row.AboveLevel) ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
        if (ImGui.Selectable(row.Name + "##sel", isSel, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap))
            selected = row.RecipeId;
        if (row.AboveLevel) ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            var tip = $"{row.Name} (item {row.ResultItemId}, recipe {row.RecipeId})";
            if (!row.Marketable) tip += "\nUntradeable";
            if (row.AboveLevel) tip += $"\nNeeds {row.Job} {row.Level} (you: {row.JobLevel})";
            if (est is { CostComplete: false }) tip += "\nSome materials are unpriced - costs are a lower bound";
            if (row.MissingSummary.Length > 0) tip += "\nMissing: " + row.MissingSummary;
            ImGui.SetTooltip(tip);
        }
        if (ImGui.BeginPopupContextItem("##ctx"))
        {
            if (ImGui.MenuItem("Add 1 to cart")) _plugin.Catalog.AddToCart(row.RecipeId, 1);
            if (row.HowMany > 0 && ImGui.MenuItem($"Add all craftable ({row.HowMany}) to cart")) _plugin.Catalog.AddToCart(row.RecipeId, Math.Max(1, row.HowMany / row.ResultAmount));
            if (ImGui.MenuItem("Copy name")) ImGui.SetClipboardText(row.Name);
            ImGui.EndPopup();
        }

        ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Job);
        ImGui.TableNextColumn(); RightText(row.Level.ToString());
        ImGui.TableNextColumn();
        if (row.HowMany > 0) RightText(row.HowMany.ToString(), ImGuiColors.HealerGreen); else RightText("-", ImGuiColors.DalamudGrey3);
        ImGui.TableNextColumn(); Gil(est?.MarginCash, est);
        ImGui.TableNextColumn(); Gil(est?.MarginMarket, est);
        ImGui.TableNextColumn();
        if (est is { RevenueKnown: true }) RightText(Fmt.Gil((long)Math.Round(est.PerDay)), est.PerDay > 0 ? ImGuiColors.ParsedGold : ImGuiColors.DalamudGrey);
        else RightText("-", ImGuiColors.DalamudGrey3);
        ImGui.TableNextColumn();
        var vel = tab == CatalogTab.Undersupplied && view.Undersupplied is { } u && u.TryGetValue(row.ResultItemId, out var ui) ? ui.Velocity : est?.Velocity;
        if (vel is { } v && est is not null) RightText(v.ToString("0.#")); else RightText("-", ImGuiColors.DalamudGrey3);
        ImGui.TableNextColumn();
        if (est is { Velocity: > 0 }) RightText(double.IsInfinity(est.SaturationDays) ? "inf" : est.SaturationDays.ToString("0.#"), est.SaturationDays > 7 ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudWhite);
        else RightText("-", ImGuiColors.DalamudGrey3);
        ImGui.TableNextColumn();
        if (row.Scrip > 0) RightText(row.Scrip.ToString(), ImGuiColors.ParsedPurple); else RightText("-", ImGuiColors.DalamudGrey3);
        ImGui.TableNextColumn();
        if (row.Desynth is { } d && d > 0) { RightText("~" + Fmt.Gil((long)d)); if (ImGui.IsItemHovered()) ImGui.SetTooltip("Estimated desynth value (sum of drop chance x market price). Estimate."); }
        else RightText("-", ImGuiColors.DalamudGrey3);
        ImGui.TableNextColumn(); ImGui.TextColored(Fmt.TierColor(row.Tier), Fmt.TierName(row.Tier));
        ImGui.TableNextColumn(); ImGui.TextUnformatted(row.MissingSummary);

        switch (tab)
        {
            case CatalogTab.Leveling:
                ImGui.TableNextColumn(); RightText(row.ExpPerCraft.ToString("N0"));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Synthesis EXP per craft at {row.Job} {row.JobLevel} for a level {row.Level} recipe (community formula; first-craft bonus not included).");
                break;
            case CatalogTab.LogCompletion:
                ImGui.TableNextColumn();
                if (est is { CostComplete: true }) RightText(Fmt.Gil(est.CashCost));
                else if (est is not null) { RightText(">" + Fmt.Gil(est.CashCost), ImGuiColors.DalamudGrey); if (ImGui.IsItemHovered()) ImGui.SetTooltip("Some materials are unpriced; lower bound."); }
                else RightText("-", ImGuiColors.DalamudGrey3);
                break;
            case CatalogTab.Undersupplied:
                ImGui.TableNextColumn();
                if (view.Undersupplied is { } uu && uu.TryGetValue(row.ResultItemId, out var item)) RightText(item.Listings.ToString(), ImGuiColors.DalamudOrange);
                else RightText("-", ImGuiColors.DalamudGrey3);
                break;
        }
    }

    private static void Gil(long? value, ProfitEstimate? est)
    {
        if (value is null) { RightText("-", ImGuiColors.DalamudGrey3); return; }
        var color = value < 0 ? ImGuiColors.DalamudRed : ImGuiColors.DalamudWhite;
        RightText((est is { CostComplete: false } ? "<" : "") + Fmt.Gil(value.Value), color);
    }

    private static void RightText(string text) => RightText(text, ImGuiColors.DalamudWhite);

    private static void RightText(string text, Vector4 color)
    {
        var w = ImGui.CalcTextSize(text).X;
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail > w) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - w);
        ImGui.TextColored(color, text);
    }
}

/// <summary>Shared formatting for the UI files.</summary>
public static class Fmt
{
    public static string Gil(long v) => v.ToString("N0");

    public static string TierName(EffortTier t) => t switch
    {
        EffortTier.Now => "Now",
        EffortTier.Easy => "Easy",
        EffortTier.SomeEffort => "Some",
        EffortTier.RealEffort => "Real",
        _ => "Blocked",
    };

    public static Vector4 TierColor(EffortTier t) => t switch
    {
        EffortTier.Now => ImGuiColors.HealerGreen,
        EffortTier.Easy => ImGuiColors.ParsedGreen,
        EffortTier.SomeEffort => ImGuiColors.DalamudYellow,
        EffortTier.RealEffort => ImGuiColors.DalamudOrange,
        _ => ImGuiColors.DalamudRed,
    };

    public static string SourceName(SourceKind k) => k switch
    {
        SourceKind.OnHand => "on hand",
        SourceKind.SubCraft => "craft",
        SourceKind.GilVendor => "vendor",
        SourceKind.SpecialShop => "special shop",
        SourceKind.RegularNode => "gather",
        SourceKind.TimedNode => "timed node",
        SourceKind.Fish => "fish",
        SourceKind.Venture => "venture",
        SourceKind.Market => "market",
        SourceKind.Drop => "drop",
        _ => "unknown",
    };
}
