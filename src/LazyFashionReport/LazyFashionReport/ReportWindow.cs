using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using LazyFashionReport.Core;

namespace LazyFashionReport;

/// <summary>
/// The main assistant window: week header, per-slot hint/dye/equipped/score table, a live
/// total, and below it THE WEEK'S PIECES — one flat block per hinted slot showing what to
/// wear (owned candidates) and what is missing (every not-owned candidate with its source,
/// never silently hidden for being uncraftable). No collapsing headers: everything visible
/// on open (UI unhide, v0.2.0.0).
/// </summary>
internal class ReportWindow : Window
{
    private readonly Plugin _plugin;

    public ReportWindow(Plugin plugin) : base("LazyFashionReport##lfr")
    {
        _plugin = plugin;
        Size = new Vector2(780, 700);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var svc = _plugin.Service;
        var outfit = svc.Outfit;
        var week = svc.Week;

        // Data-source status line: week 449's field report was "just a list of slots and no
        // hint" because the theme/hint source had silently failed to bind — the window never
        // said why. Always show which datasets actually loaded.
        ImGui.TextDisabled($"data: {svc.XivLoaded} | {svc.StateLoaded}");

        if (week is null)
        {
            ImGui.TextUnformatted("Loading week data... (if this never fills in, the status line above says why)");
            if (ImGui.Button("Retry now")) svc.RequestRefresh();
            return;
        }

        if (outfit is null)
        {
            ImGui.TextUnformatted("Building prediction...");
            return;
        }

        // v0.6.1.0: the "Apply outfit" button is pinned to the TOP of the window. Under
        // the old layout it rendered at the very bottom, below the slots table, pushed
        // past the window edge with no way to reach it - reported in game as "there's
        // literally no button on the interface".
        DrawApply();

        // Header: theme, week, base, data freshness.
        ImGui.TextUnformatted($"Week {week.Week} - {week.Theme}");
        ImGui.SameLine();
        ImGui.TextDisabled($"(base {week.BaseScore})");
        // P5: judged-week feedback under the header when a judged record exists.
        var judged = svc.JudgedSummary;
        if (judged.Length > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.HealerGreen);
            ImGui.TextUnformatted(judged);
            ImGui.PopStyleColor();
        }
        // Total readout.
        var total = outfit.Total;
        ImGui.PushFont(UiBuilder.MonoFont);
        ImGui.TextUnformatted(outfit.StatusLine);
        ImGui.PopFont();
        if (total < 80)
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
            ImGui.TextUnformatted($"  (fill empty slots: up to {outfit.AchievableIfFilled})");
            ImGui.PopStyleColor();
        }
        ImGui.Separator();

        // Per-slot scoring table. v0.6.1.0: no ScrollY here - a scrolling table grows
        // to fill the rest of the window, which pushed everything after it out of reach;
        // the window itself scrolls instead.
        if (ImGui.BeginTable("slots", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
        {
            ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Hint / dye", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn("Wearing", ImGuiTableColumnFlags.WidthStretch, 220);
            ImGui.TableSetupColumn("Pts", ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableHeadersRow();

            foreach (var s in outfit.Slots)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(s.Slot.DisplayName());

                ImGui.TableNextColumn();
                if (s.Hint is { } h)
                {
                    ImGui.TextUnformatted(h);
                    if (s.Slot.IsLeftSide() && s.PlusTwoDye is { } p2)
                    {
                        ImGui.Bullet();
                        ImGui.TextUnformatted($"dye: {p2} (+2) / {s.PlusOneShade ?? "?"} shade (+1)");
                    }
                }
                else
                {
                    ImGui.TextDisabled("no hint");
                }

                ImGui.TableNextColumn();
                if (s.Equipped is { } eq)
                {
                    var nm = _plugin.Service.Sheets.ItemName(eq.ItemId);
                    ImGui.TextUnformatted(nm);
                    if (s.ItemSatisfiesHint)
                    {
                        ImGui.SameLine();
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.HealerGreen);
                        ImGui.TextUnformatted("(gold)");
                        ImGui.PopStyleColor();
                    }
                    if (s.Dye is { SlotHasDye: true } d)
                    {
                        var dyeName = _plugin.Service.Sheets.DyeNameFor(eq.Stain0Id) ?? $"{eq.Stain0Id}";
                        var pts = d.Points;
                        ImGui.TextDisabled($"  dye {dyeName} +{pts}");
                    }
                }
                else
                {
                    ImGui.TextDisabled("(empty)");
                }

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(s.Score.ToString());
            }
            ImGui.EndTable();
        }

        ImGui.Separator();
        DrawAssembly();
        DrawWeekPieces();
    }

    /// <summary>P4 planner half: the best 80+ outfit the player can assemble right now from
    /// owned pieces and owned dyes, with the predicted total and what blocks 80 when it is
    /// not reachable. Read-only — the physical equip/dye application is a later step.</summary>
    private void DrawAssembly()
    {
        var asm = _plugin.Service.Assembly;
        if (asm is null) return;

        ImGui.TextUnformatted(asm.Reaches80 ? $"Assemble for 80+ (predicted {asm.Total})" : $"Best from owned pieces (predicted {asm.Total})");
        ImGui.SameLine();
        ImGui.TextDisabled(asm.Reaches80 ? "- everything below is owned or in bags" : "- see the gap notes");

        if (ImGui.BeginTable("assembly", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
        {
            ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Wear", ImGuiTableColumnFlags.WidthStretch, 240);
            ImGui.TableSetupColumn("Dye", ImGuiTableColumnFlags.WidthStretch, 200);
            ImGui.TableSetupColumn("Pts", ImGuiTableColumnFlags.WidthFixed, 36);
            ImGui.TableHeadersRow();
            foreach (var p in asm.Pieces)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(p.Slot.DisplayName());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(p.ItemName);
                if (p.SatisfiesHint)
                {
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.HealerGreen);
                    ImGui.TextUnformatted("(gold)");
                    ImGui.PopStyleColor();
                }
                if (p.LocationNote.Length > 0)
                {
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                    ImGui.TextUnformatted(p.LocationNote);
                    ImGui.PopStyleColor();
                }
                ImGui.TableNextColumn();
                if (p.DyeNote.Length > 0)
                {
                    if (p.DyeNote.StartsWith("apply ")) ImGui.TextUnformatted(p.DyeNote);
                    else ImGui.TextDisabled(p.DyeNote);
                }
                else ImGui.TextDisabled("-");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(p.Score.ToString());
            }
            ImGui.EndTable();
        }

        foreach (var gap in asm.Gaps)
        {
            ImGui.Bullet();
            ImGui.TextDisabled(gap);
        }
    }

    /// <summary>P4 executor live half (v0.6.0.0): the "Apply outfit" button and its result.
    /// v0.6.1.0 pins this block to the TOP of the window - under the old bottom placement
    /// it rendered below the slots table, past the window edge, unreachable in game
    /// ("there's literally no button on the interface"). One press equips the assembled
    /// outfit for real - pieces move from bags/armoury onto the character, stored pieces
    /// come out of the glamour dresser or armoire first. Dye is never auto-applied: planned
    /// dyes come back as manual reminders, and every move is logged line-by-line to the
    /// plugin log.</summary>
    private void DrawApply()
    {
        var svc = _plugin.Service;
        ImGui.Spacing();

        // Disabled with a reason line while the plan builds - ApplyOutfit is a no-op
        // without an assembly, and a silently dead button is indistinguishable from none.
        var asm = svc.Assembly;
        var busy = svc.ApplyBusy;
        if (busy || asm is null) ImGui.BeginDisabled();
        if (ImGui.Button("Apply outfit##lfr-apply"))
            svc.ApplyOutfit();
        if (busy || asm is null) ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled(asm is null
            ? "- building the outfit plan..."
            : busy
                ? "- finishing a withdraw, one moment..."
                : "- one press equips the assembled outfit; dye stays manual");

        var run = svc.LastApply;
        if (run is null) return;

        ImGui.PushFont(UiBuilder.MonoFont);
        ImGui.TextUnformatted(run.Reaches80
            ? $"after apply: {run.PredictedTotal} - full 50k MGP"
            : $"after apply: {run.PredictedTotal} - needs +{80 - run.PredictedTotal} for 80");
        ImGui.PopFont();

        if (ImGui.BeginTable("apply", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
        {
            ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthStretch, 420);
            ImGui.TableHeadersRow();
            foreach (var s in run.Steps)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(s.Step.Slot.DisplayName());
                ImGui.TableNextColumn();
                switch (s.Status)
                {
                    case ApplyStepStatus.Equipped:
                    case ApplyStepStatus.WithdrewThenEquipped:
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.HealerGreen);
                        ImGui.TextUnformatted(s.Line);
                        ImGui.PopStyleColor();
                        break;
                    case ApplyStepStatus.WithdrawPending:
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                        ImGui.TextUnformatted(s.Line);
                        ImGui.PopStyleColor();
                        break;
                    case ApplyStepStatus.Failed:
                    case ApplyStepStatus.SkippedChanged:
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                        ImGui.TextUnformatted(s.Line);
                        ImGui.PopStyleColor();
                        break;
                    default:
                        ImGui.TextDisabled(s.Line);
                        break;
                }
            }
            ImGui.EndTable();
        }

        foreach (var d in run.DyeReminders)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            ImGui.TextUnformatted(d);
            ImGui.PopStyleColor();
        }
    }

    /// <summary>The week's pieces: one flat block per hinted slot — wear list, missing list
    /// with per-piece source, craft buttons where a recipe exists. Every hinted slot renders
    /// even with empty lists (an empty list is information).</summary>
    private void DrawWeekPieces()
    {
        var svc = _plugin.Service;
        var plans = svc.SlotPlans;

        ImGui.TextUnformatted("The week's pieces");
        ImGui.SameLine();
        ImGui.TextDisabled(plans.Count > 0
            ? $"{plans.Count} hinted slot(s) - wear what is owned, fetch what is not"
            : "no hinted slots loaded yet");

        if (svc.ArtisanBusy == true)
            ImGui.TextColored(ImGuiColors.DalamudYellow, "Artisan is busy - wait for it to finish.");

        var error = _craftError;
        if (error is not null)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, error);
            ImGui.SameLine();
            if (ImGui.SmallButton("Dismiss##lfr-craft-err"))
                _craftError = null;
        }

        foreach (var plan in plans)
            DrawSlotPlan(plan);
    }

    private void DrawSlotPlan(SlotPlan plan)
    {
        var svc = _plugin.Service;
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
        ImGui.TextUnformatted($"{plan.Slot.DisplayName().ToUpperInvariant()} - \"{plan.Hint}\"");
        ImGui.PopStyleColor();

        // Wear list.
        ImGui.TextDisabled("Wear (owned):");
        ImGui.Indent(16);
        if (plan.Wear.Count == 0)
        {
            ImGui.TextDisabled(plan.OwnershipUnknown || !plan.WearIsOwnedFiltered
                ? "no crowd data for this hint yet"
                : "nothing owned fits this hint yet");
        }
        else
        {
            foreach (var c in plan.Wear)
            {
                ImGui.Bullet();
                ImGui.TextUnformatted($"{c.Name}  ({c.Votes})");
                // P3 get-to: say WHERE an owned-but-stored piece sits (bags need no note).
                var note = _plugin.Service.LocationNoteFor(c.ItemId);
                if (note.Length > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudYellow, note);
                }
            }
        }
        ImGui.Unindent(16);

        // Missing list: EVERY not-owned candidate with a source, never hidden.
        ImGui.TextDisabled("Missing (not owned):");
        ImGui.Indent(16);
        if (plan.OwnershipUnknown)
        {
            ImGui.TextDisabled("ownership not read yet - open the Fashion Report window or press Refresh");
        }
        else if (plan.Fetch.Count == 0)
        {
            ImGui.TextDisabled("nothing missing for this hint");
        }
        else
        {
            foreach (var piece in plan.Fetch)
            {
                ImGui.Bullet();
                ImGui.TextUnformatted($"{piece.Item.Name}  ({piece.Item.Votes})");
                ImGui.SameLine();
                DrawBuySource(piece);
            }
        }
        ImGui.Unindent(16);
    }

    /// <summary>Source label + action for one missing piece (buy leg, v0.3.0.0). Craft keeps
    /// its Artisan button; placed gil/currency vendors get a Shop button that flags the map;
    /// market shows the median when a fresh quote exists.</summary>
    private void DrawBuySource(FetchPiece piece)
    {
        var svc = _plugin.Service;
        var buy = piece.Buy;
        switch (buy?.Source)
        {
            case BuySource.Craft when buy.Recipe is { } r:
                ImGui.TextDisabled($"- craftable ({CraftName(r.CraftTypeId)} lv {r.Level})");
                if (_plugin.Config.FetchMissingCraft && svc.ArtisanInstalled)
                {
                    ImGui.SameLine();
                    DrawCraftButton(piece);
                }
                break;

            case BuySource.GilVendor:
                ImGui.TextDisabled($"- {buy.Label}");
                if (buy.HasMapFlag)
                {
                    ImGui.SameLine();
                    DrawShopButton(buy);
                }
                break;

            case BuySource.SpecialShop:
                ImGui.TextDisabled($"- {buy.Label}");
                if (buy.HasMapFlag)
                {
                    ImGui.SameLine();
                    DrawShopButton(buy);
                }
                break;

            case BuySource.Market:
                var median = _plugin.Service.MarketMedianFor(piece.Item.ItemId);
                ImGui.TextDisabled(median is { } m
                    ? $"- market board (median ~{m:N0} gil)"
                    : "- market board");
                break;

            default:
                // Not craftable and nothing resolved: the honest label, never silence.
                ImGui.TextDisabled("- not craftable (no vendor or market source found)");
                break;
        }
    }

    /// <summary>Flag the vendor on the map (framework-safe: OpenMapWithMapLink must run on
    /// the framework thread). The click is the consent; nothing is bought automatically.</summary>
    private void DrawShopButton(BuyOption buy)
    {
        if (ImGui.SmallButton($"Shop##lfr-shop-{buy.ShopId}-{buy.TerritoryId}-{buy.MapX:0.##}-{buy.MapY:0.##}"))
        {
            Plugin.Framework.RunOnFrameworkThread(() =>
            {
                try
                {
                    var payload = new Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload(
                        buy.TerritoryId, buy.MapId, buy.MapX, buy.MapY);
                    Plugin.GameGui.OpenMapWithMapLink(payload);
                    Plugin.Log.Information($"[LFR] map flag set for vendor: {buy.Label}");
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warning(ex, "[LFR] failed to open map link");
                }
            });
        }
    }

    private static string CraftName(int craftTypeId) => craftTypeId switch
    {
        0 => "Carpenter",
        1 => "Blacksmith",
        2 => "Armorer",
        3 => "Goldsmith",
        4 => "Leatherworker",
        5 => "Weaver",
        6 => "Alchemist",
        7 => "Culinarian",
        _ => "craft",
    };

    private void DrawCraftButton(FetchPiece piece)
    {
        if (ImGui.SmallButton($"Craft via Artisan##lfr-craft-{piece.Item.ItemId}"))
        {
            // The click IS the consent: one recipe, one run. CraftItem must run on the
            // framework thread (it opens the crafting log), so hop through the service.
            var err = Plugin.Framework.RunOnFrameworkThread(
                () => _plugin.Service.CraftViaArtisan(piece.Recipe!.RecipeId)).Result;
            if (err is not null)
            {
                _craftError = $"Craft failed: {err}";
                Plugin.Log.Warning($"[LFR] craft request for recipe {piece.Recipe!.RecipeId} failed: {err}");
            }
            else
            {
                _craftError = null;
                Plugin.Log.Information($"[LFR] craft request handed to Artisan: recipe {piece.Recipe!.RecipeId} for {piece.Item.Name}");
            }
        }
    }

    private string? _craftError;
}
