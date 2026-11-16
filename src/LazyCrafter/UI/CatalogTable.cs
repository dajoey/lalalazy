using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;
using LazyCrafter.Catalog;
using LazyCrafter.Core.Model;

namespace LazyCrafter.UI;

/// <summary>Virtualized, worker-sorted catalog table with recipe selection and the existing context actions.</summary>
public sealed class CatalogTable
{
    private readonly Plugin _plugin;

    // Header text, sort key, kind of column and the sample text that sizes it. Priority 0 is always shown; higher
    // numbers are dropped first when the card is too narrow, so no column is ever cut in half.
    private enum Col
    {
        Item,
        Job,
        Lvl,
        Can,
        MarginCash,
        MarginMarket,
        PerDay,
        Vel,
        Sat,
        Scrip,
        Desynth,
        Tier,
        Missing,
        Extra,
    }

    private sealed record ColumnSpec(Col Id, string Header, SortKey Key, string Sample, int Priority, bool Stretch = false, ImGuiTableColumnFlags Extra = 0)
    {
        /// <summary>Numeric columns draw right-aligned values, so their header is right-aligned too.</summary>
        public bool Right => Id is not (Col.Item or Col.Job or Col.Tier or Col.Missing);
    }

    private static readonly ColumnSpec[] Columns =
    [
        new(Col.Item, "Item", SortKey.Name, "", 0, true),
        new(Col.Job, "Job", SortKey.Job, "BSM", 1),
        new(Col.Lvl, "Lvl", SortKey.Level, "100", 2),
        new(Col.Can, "Can", SortKey.Craftable, "999", 3),
        new(Col.MarginCash, "Margin", SortKey.MarginCash, "<-88,888", 2),
        new(Col.MarginMarket, "Mkt margin", SortKey.MarginMarket, "<-88,888", 6),
        new(Col.PerDay, "/day", SortKey.PerDay, "888,888", 1, false, ImGuiTableColumnFlags.PreferSortDescending),
        new(Col.Vel, "Vel", SortKey.Velocity, "88.8", 7),
        new(Col.Sat, "Sat", SortKey.Saturation, "18.8", 8),
        new(Col.Scrip, "Scrip", SortKey.Scrip, "888", 10),
        new(Col.Desynth, "Desynth", SortKey.Desynth, "~88,888", 9),
        new(Col.Tier, "Tier", SortKey.Tier, "Blocked", 4),
        new(Col.Missing, "Missing", SortKey.Missing, "", 11, true),
    ];

    public CatalogTable(Plugin plugin) => _plugin = plugin;

    private static float RowHeight => ImGui.GetTextLineHeight() + (3 * Theme.Unit);

    /// <summary>
    ///     Draws visible rows and returns any sort change requested through the column headers. A cardHeight above 0 makes
    ///     the card exactly that tall (the table scrolls inside it); 0 sizes it to its rows, with a cap.
    /// </summary>
    public (SortKey Sort, bool Descending) Draw(CatalogView view, CatalogSnapshot snap, CatalogTab tab, bool hq, ref uint selected, SortKey sort, bool descending, float cardHeight)
    {
        var rows = view.Rows;
        var selectedRecipe = selected;
        var extra = tab switch { CatalogTab.Leveling => "EXP", CatalogTab.LogCompletion => "Cost", CatalogTab.Undersupplied => "Listings", _ => null };
        var minHeight = cardHeight > 0 ? cardHeight : rows.Count == 0 ? 240 : 0;
        CrafterChrome.Card("catalog_table", () =>
        {
            if (view.Generation != snap.Generation && rows.Count == 0)
            {
                if (snap.Generation == 0)
                    CrafterChrome.EmptyState("Computing the catalog", "The first catalog computation has not published rows yet.");
                else
                    CrafterChrome.EmptyState("Applying filters", "Applying the new filters to the current catalog.");
                return;
            }
            if (rows.Count == 0)
            {
                CrafterChrome.EmptyState("No recipes match", "No recipes match this effort bucket and filter set. Clear a filter, choose another bucket, or enable Above level.");
                return;
            }

            var cardTop = ImGui.GetCursorScreenPos().Y - CrafterChrome.CardPadding;
            var caption = view.Generation != snap.Generation
                ? $"Updating · showing {rows.Count:N0} rows while the new view is computed"
                : $"{rows.Count:N0} recipes · select for ingredients · right-click for cart actions";
            var captionPos = ImGui.GetCursorScreenPos();
            var captionWidth = CrafterChrome.Avail();
            CrafterChrome.DrawFitted(ImGui.GetWindowDrawList(), captionPos, caption, captionWidth, Theme.TextSecondary, Theme.SecondaryScale);
            CrafterChrome.Advance(captionPos, captionWidth, CrafterChrome.LineHeight(Theme.SecondaryScale), 2 * Theme.Unit);

            var tableWidth = CrafterChrome.Avail();
            var specs = BuildSpecs(tab, extra);
            var visible = VisibleColumns(rows, tab, extra, sort, tableWidth);

            var rowsHeight = (rows.Count * RowHeight) + RowHeight + (2 * Theme.Unit);
            float height;
            if (cardHeight > 0)
                height = MathF.Max(120, cardTop + cardHeight - CrafterChrome.CardPadding - ImGui.GetCursorScreenPos().Y);
            else
                height = MathF.Min(rowsHeight, 440);

            // The table keeps its id and its full column set (so saved column settings survive) and the built-in header
            // behaviour: sorting, resizing, reordering and the right-click menu that shows and hides columns.
            var flags = ImGuiTableFlags.Sortable | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY
                      | ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable
                      | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
            // Resizable columns draw a divider between every column; keep those quieter than the row rules.
            ImGui.PushStyleColor(ImGuiCol.TableBorderLight, Theme.SurfaceBorder with { W = 0.4f });
            if (!ImGui.BeginTable($"##crafter_catalog_{tab}", specs.Count, flags, new Vector2(tableWidth, height)))
            {
                ImGui.PopStyleColor();
                return;
            }

            ImGui.TableSetupScrollFreeze(1, 1);
            for (var i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                var effectiveFlags = spec.Extra;
                effectiveFlags |= spec.Stretch ? ImGuiTableColumnFlags.WidthStretch : ImGuiTableColumnFlags.WidthFixed;
                if (spec.Id == Col.Item)
                    effectiveFlags |= ImGuiTableColumnFlags.NoHide;
                if (spec.Key == sort)
                    effectiveFlags = (effectiveFlags & ~ImGuiTableColumnFlags.PreferSortDescending) | (descending ? ImGuiTableColumnFlags.PreferSortDescending : ImGuiTableColumnFlags.PreferSortAscending)
                                      | ImGuiTableColumnFlags.DefaultSort;
                // Columns that do not fit beside the item name start hidden; the column menu shows them again.
                if (!visible[i])
                    effectiveFlags |= ImGuiTableColumnFlags.DefaultHide;
                var width = spec.Stretch ? (spec.Id == Col.Item ? 1f : 1.2f) : ColumnWidth(spec);
                ImGui.TableSetupColumn(spec.Header, effectiveFlags, width, (uint)spec.Key + 1);
            }

            // Built-in header cells (sort clicks, drag to resize or reorder, right-click column menu); the label and the
            // sort arrow are drawn over them so numeric columns line up with their right-aligned values.
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            for (var i = 0; i < specs.Count; i++)
            {
                if (!ImGui.TableSetColumnIndex(i))
                    continue;
                var spec = specs[i];
                CrafterChrome.NativeHeaderCell(i, spec.Header, spec.Right, spec.Key == sort ? (descending ? -1 : 1) : 0);
            }

            var sortSpecs = ImGui.TableGetSortSpecs();
            if (!sortSpecs.IsNull && sortSpecs.SpecsDirty && sortSpecs.SpecsCount > 0)
            {
                var sortSpec = sortSpecs.Specs;
                sort = (SortKey)(sortSpec.ColumnUserID - 1);
                descending = sortSpec.SortDirection == ImGuiSortDirection.Descending;
                sortSpecs.SpecsDirty = false;
            }

            var clipper = ImGui.ImGuiListClipper();
            clipper.Begin(rows.Count, RowHeight);
            while (clipper.Step())
            {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    var row = rows[i];
                    ImGui.TableNextRow();
                    ImGui.PushID((int)row.RecipeId);
                    DrawRow(row, view, hq, tab, ref selectedRecipe);
                    ImGui.PopID();
                }
            }
            clipper.End();
            clipper.Destroy();
            var fade = ImGui.GetScrollMaxY() > 0 && ImGui.GetScrollY() < ImGui.GetScrollMaxY() - 1;
            var fadeMin = ImGui.GetWindowPos();
            var fadeMax = fadeMin + ImGui.GetWindowSize() - new Vector2(ImGui.GetScrollMaxY() > 0 ? 2 * Theme.Unit + 2 : 0, 0);
            ImGui.EndTable();
            ImGui.PopStyleColor();
            if (fade)
                CrafterChrome.FadeBottom(ImGui.GetForegroundDrawList(), fadeMin, fadeMax, Theme.Pack(Theme.Surface));
        }, minHeight: minHeight);
        selected = selectedRecipe;
        return (sort, descending);
    }

    private static List<ColumnSpec> BuildSpecs(CatalogTab tab, string? extra)
    {
        var list = new List<ColumnSpec>(Columns);
        if (extra is not null)
        {
            var key = tab switch { CatalogTab.Leveling => SortKey.Exp, CatalogTab.LogCompletion => SortKey.CashCost, _ => SortKey.Listings };
            // The bucket's own column sits next to the item and is the last one dropped.
            list.Insert(1, new ColumnSpec(Col.Extra, extra, key, extra == "Listings" ? "888" : "88,888", 1));
        }
        return list;
    }

    // ImGui adds the cell padding (2 x 8 px) to a fixed column's content width; ColumnWidth is the content only.
    private const float CellPad = 4 * Theme.Unit;

    private static float ColumnWidth(ColumnSpec spec)
        => MathF.Max(CrafterChrome.TextWidth(spec.Header) + CrafterChrome.SortArrowReserve, CrafterChrome.TextWidth(spec.Sample));

    /// <summary>Columns that fit beside a readable item name, by priority; the sorted column always stays.</summary>
    private static bool[] VisibleColumns(IReadOnlyList<CatalogRow> rows, CatalogTab tab, string? extra, SortKey sort, float tableWidth)
    {
        var specs = BuildSpecs(tab, extra);
        var visible = new bool[specs.Count];
        var longest = 0f;
        for (var i = 0; i < Math.Min(rows.Count, 150); i++)
            longest = MathF.Max(longest, CrafterChrome.TextWidth(rows[i].Name));
        var itemWidth = Math.Clamp(longest + (4 * Theme.Unit), 160, 260);
        var budget = tableWidth - (2 * Theme.Unit) - itemWidth;
        visible[0] = true;
        // Always-visible set: the item, the extra column of this bucket and the column the table is sorted by.
        for (var i = 1; i < specs.Count; i++)
            if (specs[i].Key == sort || specs[i].Id == Col.Extra)
            {
                visible[i] = true;
                budget -= ColumnWidth(specs[i]) + CellPad;
            }
        foreach (var index in Enumerable.Range(1, specs.Count - 1).Where(i => !visible[i]).OrderBy(i => specs[i].Priority))
        {
            var spec = specs[index];
            var cost = spec.Stretch ? 120f : ColumnWidth(spec) + CellPad;
            if (budget - cost < 0)
                continue;
            visible[index] = true;
            budget -= cost;
        }
        return visible;
    }

    private void DrawRow(CatalogRow row, CatalogView view, bool hq, CatalogTab tab, ref uint selected)
    {
        var estimate = row.Est(hq);
        var isSelected = selected == row.RecipeId;

        bool Next(Col col)
        {
            // The bucket's own column only exists in the buckets that have one.
            if (col == Col.Extra && tab is not (CatalogTab.Leveling or CatalogTab.LogCompletion or CatalogTab.Undersupplied))
                return false;
            return ImGui.TableNextColumn();
        }

        ImGui.TableNextColumn();
        if (row.AboveLevel)
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        var label = CrafterChrome.Fit(row.Name, ImGui.GetContentRegionAvail().X - 4);
        if (ImGui.Selectable(label + "##select", isSelected, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap))
            selected = row.RecipeId;
        if (row.AboveLevel)
            ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            var tooltip = $"{row.Name} (item {row.ResultItemId}, recipe {row.RecipeId})";
            if (!row.Marketable)
                tooltip += "\nUntradeable";
            if (row.AboveLevel)
                tooltip += $"\nNeeds {row.Job} {row.Level} (current: {row.JobLevel})";
            if (estimate is { CostComplete: false })
                tooltip += "\nSome materials are unpriced; costs are a lower bound";
            if (row.MissingSummary.Length > 0)
                tooltip += "\nMissing: " + row.MissingSummary;
            ImGui.SetTooltip(tooltip);
        }
        if (ImGui.BeginPopupContextItem("##recipe_context"))
        {
            if (ImGui.MenuItem("Add 1 to cart"))
                _plugin.Catalog.AddToCart(row.RecipeId, 1);
            if (row.HowMany > 0 && ImGui.MenuItem($"Add all craftable ({row.HowMany}) to cart"))
                _plugin.Catalog.AddToCart(row.RecipeId, Math.Max(1, row.HowMany / row.ResultAmount));
            if (ImGui.MenuItem("Copy name"))
                ImGui.SetClipboardText(row.Name);
            ImGui.EndPopup();
        }

        if (Next(Col.Extra))
        {
            switch (tab)
            {
                case CatalogTab.Leveling:
                    RightText(row.ExpPerCraft.ToString("N0"));
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"Synthesis EXP per craft at {row.Job} {row.JobLevel} for a level {row.Level} recipe; first-craft bonus not included.");
                    break;
                case CatalogTab.LogCompletion:
                    if (estimate is { CostComplete: true })
                        RightText(Fmt.Gil(estimate.CashCost));
                    else if (estimate is not null)
                    {
                        RightText(">" + Fmt.Gil(estimate.CashCost), Theme.TextSecondary);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Some materials are unpriced; lower bound.");
                    }
                    else
                    {
                        RightText("-", Theme.TextSecondary);
                    }
                    break;
                default:
                    if (view.Undersupplied is { } items && items.TryGetValue(row.ResultItemId, out var item))
                        RightText(item.Listings.ToString(), Theme.StateAmber);
                    else
                        RightText("-", Theme.TextSecondary);
                    break;
            }
        }
        if (Next(Col.Job))
            ImGui.TextUnformatted(row.Job);
        if (Next(Col.Lvl))
            RightText(row.Level.ToString());
        if (Next(Col.Can))
        {
            if (row.HowMany > 0)
                RightText(row.HowMany.ToString(), Copy.MarkColor(UiState.Ready));
            else
                RightText("-", Theme.TextSecondary);
        }
        if (Next(Col.MarginCash))
            Gil(estimate?.MarginCash, estimate);
        if (Next(Col.MarginMarket))
            Gil(estimate?.MarginMarket, estimate);
        if (Next(Col.PerDay))
        {
            if (estimate is { RevenueKnown: true })
                RightText(Fmt.Gil((long)Math.Round(estimate.PerDay)), estimate.PerDay > 0 ? Theme.TextPrimary : Theme.TextSecondary);
            else
                RightText("-", Theme.TextSecondary);
        }
        if (Next(Col.Vel))
        {
            var velocity = tab == CatalogTab.Undersupplied && view.Undersupplied is { } undersupplied && undersupplied.TryGetValue(row.ResultItemId, out var under)
                ? under.Velocity
                : estimate?.Velocity;
            if (velocity is { } value && estimate is not null)
                RightText(value.ToString("0.#"));
            else
                RightText("-", Theme.TextSecondary);
        }
        if (Next(Col.Sat))
        {
            if (estimate is { Velocity: > 0 })
                RightText(double.IsInfinity(estimate.SaturationDays) ? "inf" : estimate.SaturationDays.ToString("0.#"), estimate.SaturationDays > 7 ? Theme.StateAmber : Theme.TextPrimary);
            else
                RightText("-", Theme.TextSecondary);
        }
        if (Next(Col.Scrip))
            RightText(row.Scrip > 0 ? row.Scrip.ToString() : "-", Theme.TextSecondary);
        if (Next(Col.Desynth))
        {
            if (row.Desynth is { } desynth && desynth > 0)
            {
                RightText("~" + Fmt.Gil((long)desynth));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Estimated desynth value: sum of drop chance multiplied by market price.");
            }
            else
            {
                RightText("-", Theme.TextSecondary);
            }
        }
        if (Next(Col.Tier))
            ImGui.TextColored(Fmt.TierColor(row.Tier), Fmt.TierName(row.Tier));
        if (Next(Col.Missing))
            ImGui.TextUnformatted(CrafterChrome.Fit(row.MissingSummary, ImGui.GetContentRegionAvail().X));
    }

    private static void Gil(long? value, ProfitEstimate? estimate)
    {
        if (value is null)
        {
            RightText("-", Theme.TextSecondary);
            return;
        }
        var color = value < 0 ? Theme.StateRed : Theme.TextPrimary;
        RightText((estimate is { CostComplete: false } ? "<" : "") + Fmt.Gil(value.Value), color);
    }

    private static void RightText(string text) => RightText(text, Theme.TextPrimary);

    private static void RightText(string text, Vector4 color)
    {
        var width = ImGui.CalcTextSize(text).X;
        var available = ImGui.GetContentRegionAvail().X;
        if (available > width)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + available - width);
        ImGui.TextColored(color, text);
    }
}

/// <summary>Shared formatting for LazyCrafter UI files.</summary>
public static class Fmt
{
    public static string Gil(long value) => value.ToString("N0");

    public static string TierName(EffortTier tier) => tier switch
    {
        EffortTier.Now => "Now",
        EffortTier.Easy => "Easy",
        EffortTier.SomeEffort => "Some",
        EffortTier.RealEffort => "Real",
        _ => "Blocked",
    };

    public static System.Numerics.Vector4 TierColor(EffortTier tier) => tier switch
    {
        EffortTier.Now => Theme.StateGreen,
        EffortTier.Blocked => Theme.StateAmber,
        _ => Theme.TextPrimary,
    };

    public static string SourceName(SourceKind kind) => kind switch
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
