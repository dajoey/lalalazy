using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using LazyFashionReport.Core;

namespace LazyFashionReport;

/// <summary>
/// The main assistant window: week header, per-slot hint/dye/equipped/score rows, a live
/// total, and the owned-filtered candidate list under each hinted slot.
/// </summary>
internal class ReportWindow : Window
{
    private readonly Plugin _plugin;

    public ReportWindow(Plugin plugin) : base("LazyFashionReport##lfr")
    {
        _plugin = plugin;
        Size = new Vector2(560, 640);
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

        // Header: theme, week, base, data freshness.
        ImGui.TextUnformatted($"Week {week.Week} - {week.Theme}");
        ImGui.SameLine();
        ImGui.TextDisabled($"(base {week.BaseScore})");
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

        // Per-slot table.
        if (ImGui.BeginTable("slots", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY))
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
        DrawCandidates(outfit);
    }

    private void DrawCandidates(OutfitReport outfit)
    {
        var max = _plugin.Config.MaxCandidatesPerSlot;
        ImGui.TextUnformatted(_plugin.Config.FilterOwned ? "Candidates you own" : "Top candidates");
        ImGui.Separator();
        foreach (var s in outfit.Slots)
        {
            if (s.Hint is null) continue;
            if (!ImGui.CollapsingHeader($"{s.Slot.DisplayName()} - {s.Hint}"))
                continue;
            ImGui.Indent(16);
            if (s.Candidates.Count == 0)
            {
                ImGui.TextDisabled(FilterOwnedNote());
            }
            else
            {
                foreach (var c in s.Candidates.Take(max))
                {
                    ImGui.Bullet();
                    ImGui.TextUnformatted($"{c.Name}  ({c.Votes})");
                }
                if (s.Candidates.Count > max)
                    ImGui.TextDisabled($"... {s.Candidates.Count - max} more");
            }
            ImGui.Unindent(16);
        }

        DrawMissingPieces();
    }

    /// <summary>Auto-dress v1 step 4 UI: the missing-pieces plan (visible only when the config
    /// toggle is on and Artisan is installed). One "Craft via Artisan" button per piece - the
    /// button acts, nothing auto-crafts.</summary>
    private void DrawMissingPieces()
    {
        var svc = _plugin.Service;
        if (!_plugin.Config.FetchMissingCraft) return;

        ImGui.Separator();
        ImGui.TextUnformatted("Missing pieces (craftable via Artisan)");
        ImGui.SameLine();
        ImGui.TextDisabled(_plugin.Service.ArtisanInstalled ? "(Artisan detected)" : "(Artisan not installed)");
        if (!_plugin.Service.ArtisanInstalled) return;

        var pieces = svc.MissingPieces;
        if (pieces.Count == 0)
        {
            ImGui.TextDisabled("Nothing missing (or the plan is still building).");
            return;
        }
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

        foreach (var p in pieces)
        {
            if (p.Recipe is null) continue;   // not craftable: no button, no noise
            ImGui.Bullet();
            ImGui.TextUnformatted($"{p.Item.Name} for {p.Slot.DisplayName()} ({p.Hint}, {p.Item.Votes} votes)");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Craft via Artisan##lfr-craft-{p.Item.ItemId}"))
            {
                // The click IS the consent: one recipe, one run. CraftItem must run on the
                // framework thread (it opens the crafting log), so hop through the service.
                var err = Plugin.Framework.RunOnFrameworkThread(() => svc.CraftViaArtisan(p.Recipe.RecipeId)).Result;
                if (err is not null)
                {
                    _craftError = $"Craft failed: {err}";
                    Plugin.Log.Warning($"[LFR] craft request for recipe {p.Recipe.RecipeId} failed: {err}");
                }
                else
                {
                    _craftError = null;
                    Plugin.Log.Information($"[LFR] craft request handed to Artisan: recipe {p.Recipe.RecipeId} for {p.Item.Name}");
                }
            }
        }
    }

    private string? _craftError;

    private string FilterOwnedNote() => _plugin.Config.FilterOwned
        ? "No owned candidates yet (open the Fashion Report or press Refresh)."
        : "No crowd data for this hint yet.";
}
