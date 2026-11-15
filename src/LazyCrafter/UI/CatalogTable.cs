using Dalamud.Bindings.ImGui;
using LalaUiKit;
using LazyCrafter.Catalog;
using LazyCrafter.Core.Model;

namespace LazyCrafter.UI;

/// <summary>Virtualized, worker-sorted catalog table with recipe selection and the existing context actions.</summary>
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

    /// <summary>Draws visible rows and returns any sort change requested through the column headers.</summary>
    public (SortKey Sort, bool Descending) Draw(CatalogView view, CatalogSnapshot snap, CatalogTab tab, bool hq, ref uint selected, SortKey sort, bool descending)
    {
        var rows = view.Rows;
        var selectedRecipe = selected;
        var extra = tab switch { CatalogTab.Leveling => "EXP", CatalogTab.LogCompletion => "Cost", CatalogTab.Undersupplied => "Listings", _ => null };
        CrafterChrome.Card("catalog_table", () =>
        {
            if (view.Generation != snap.Generation && rows.Count == 0)
            {
                Widgets.InfoBox(snap.Generation == 0
                    ? "Running — the first catalog computation has not published rows yet."
                    : "Running — applying the new filters to the current catalog.", UiState.Running);
                return;
            }
            if (rows.Count == 0)
            {
                Widgets.InfoBox("Empty — no recipes match this effort bucket and filter set. Clear a filter, choose another bucket, or enable Above level.", UiState.Empty);
                return;
            }

            Widgets.Caption(view.Generation != snap.Generation
                ? $"{Copy.Word(UiState.Running)} — showing {rows.Count:N0} rows while the new view is computed"
                : $"{rows.Count:N0} recipes · select for ingredients · right-click for cart actions");

            var flags = ImGuiTableFlags.Sortable | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY
                      | ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable | ImGuiTableFlags.SizingStretchProp;
            var columnCount = Columns.Length + (extra is null ? 0 : 1);
            var height = MathF.Max(280, ImGui.GetContentRegionAvail().Y);
            if (!ImGui.BeginTable($"##crafter_catalog_{tab}", columnCount, flags, new System.Numerics.Vector2(0, height)))
                return;

            ImGui.TableSetupScrollFreeze(1, 1);
            for (var i = 0; i < Columns.Length; i++)
            {
                var (header, key, columnFlags, width) = Columns[i];
                var effectiveFlags = columnFlags;
                if (key == sort)
                    effectiveFlags |= descending ? ImGuiTableColumnFlags.PreferSortDescending : ImGuiTableColumnFlags.PreferSortAscending;
                if (key == sort)
                    effectiveFlags |= ImGuiTableColumnFlags.DefaultSort;
                ImGui.TableSetupColumn(header, effectiveFlags, width, (uint)key + 1);
            }
            if (extra is not null)
            {
                var key = tab switch { CatalogTab.Leveling => SortKey.Exp, CatalogTab.LogCompletion => SortKey.CashCost, _ => SortKey.Listings };
                ImGui.TableSetupColumn(extra, ImGuiTableColumnFlags.WidthFixed | (key == sort ? ImGuiTableColumnFlags.DefaultSort : 0), 64, (uint)key + 1);
            }
            ImGui.TableHeadersRow();

            var specs = ImGui.TableGetSortSpecs();
            if (!specs.IsNull && specs.SpecsDirty && specs.SpecsCount > 0)
            {
                var spec = specs.Specs;
                sort = (SortKey)(spec.ColumnUserID - 1);
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
                    DrawRow(row, view, hq, tab, ref selectedRecipe);
                    ImGui.PopID();
                }
            }
            clipper.End();
            clipper.Destroy();
            ImGui.EndTable();
        });
        selected = selectedRecipe;
        return (sort, descending);
    }

    private void DrawRow(CatalogRow row, CatalogView view, bool hq, CatalogTab tab, ref uint selected)
    {
        var estimate = row.Est(hq);
        var isSelected = selected == row.RecipeId;

        ImGui.TableNextColumn();
        if (row.AboveLevel)
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        if (ImGui.Selectable(row.Name + "##select", isSelected, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap))
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

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(row.Job);
        ImGui.TableNextColumn();
        RightText(row.Level.ToString());
        ImGui.TableNextColumn();
        if (row.HowMany > 0)
            RightText(row.HowMany.ToString(), Copy.MarkColor(UiState.Ready));
        else
            RightText("-", Theme.TextSecondary);
        ImGui.TableNextColumn();
        Gil(estimate?.MarginCash, estimate);
        ImGui.TableNextColumn();
        Gil(estimate?.MarginMarket, estimate);
        ImGui.TableNextColumn();
        if (estimate is { RevenueKnown: true })
            RightText(Fmt.Gil((long)Math.Round(estimate.PerDay)), estimate.PerDay > 0 ? Theme.TextPrimary : Theme.TextSecondary);
        else
            RightText("-", Theme.TextSecondary);
        ImGui.TableNextColumn();
        var velocity = tab == CatalogTab.Undersupplied && view.Undersupplied is { } undersupplied && undersupplied.TryGetValue(row.ResultItemId, out var under)
            ? under.Velocity
            : estimate?.Velocity;
        if (velocity is { } value && estimate is not null)
            RightText(value.ToString("0.#"));
        else
            RightText("-", Theme.TextSecondary);
        ImGui.TableNextColumn();
        if (estimate is { Velocity: > 0 })
            RightText(double.IsInfinity(estimate.SaturationDays) ? "inf" : estimate.SaturationDays.ToString("0.#"), estimate.SaturationDays > 7 ? Theme.StateAmber : Theme.TextPrimary);
        else
            RightText("-", Theme.TextSecondary);
        ImGui.TableNextColumn();
        if (row.Scrip > 0)
            RightText(row.Scrip.ToString(), Theme.TextSecondary);
        else
            RightText("-", Theme.TextSecondary);
        ImGui.TableNextColumn();
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
        ImGui.TableNextColumn();
        ImGui.TextColored(Fmt.TierColor(row.Tier), Fmt.TierName(row.Tier));
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(row.MissingSummary);

        switch (tab)
        {
            case CatalogTab.Leveling:
                ImGui.TableNextColumn();
                RightText(row.ExpPerCraft.ToString("N0"));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Synthesis EXP per craft at {row.Job} {row.JobLevel} for a level {row.Level} recipe; first-craft bonus not included.");
                break;
            case CatalogTab.LogCompletion:
                ImGui.TableNextColumn();
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
            case CatalogTab.Undersupplied:
                ImGui.TableNextColumn();
                if (view.Undersupplied is { } items && items.TryGetValue(row.ResultItemId, out var item))
                    RightText(item.Listings.ToString(), Theme.StateAmber);
                else
                    RightText("-", Theme.TextSecondary);
                break;
        }
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

    private static void RightText(string text, System.Numerics.Vector4 color)
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
