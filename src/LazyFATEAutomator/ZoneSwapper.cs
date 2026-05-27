using System;
using System.Collections.Generic;
using ECommons.Automation;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace LazyFATEAutomator;

/// <summary>
/// Picks a random overworld zone in the same expansion as the player's current zone and
/// returns its primary aetheryte's PlaceName for use with /tp (TeleporterPlugin).
///
/// Does NOT depend on Telepo.TeleportList — that list is only populated after the player
/// has opened the in-game teleport menu, and was the cause of the "no unlocked aetherytes"
/// lie in 0.0.2.5. Instead we enumerate the Aetheryte excel sheet directly and trust
/// /tp to fail gracefully if the player hasn't unlocked the target aetheryte yet
/// (TeleporterPlugin prints a chat warning and the state machine will retry next swap tick).
/// </summary>
public static class ZoneSwapper
{
    public static string? PickRandomSameExpacAetheryte()
    {
        try
        {
            var dm = Plugin.DataManager;
            var territoryTypeSheet = dm.GetExcelSheet<TerritoryType>();
            var aetheryteSheet     = dm.GetExcelSheet<Aetheryte>();
            if (territoryTypeSheet == null || aetheryteSheet == null)
            {
                Plugin.PluginLog.Warning("ZoneSwapper: Lumina sheets unavailable");
                return null;
            }

            var currentTerritoryId = Svc.ClientState.TerritoryType;
            if (!territoryTypeSheet.TryGetRow(currentTerritoryId, out var currentTerritory))
            {
                Plugin.PluginLog.Warning($"ZoneSwapper: TerritoryType row {currentTerritoryId} not found");
                return null;
            }
            var currentExpansion = currentTerritory.ExVersion.RowId;

            // First pass: build territoryId -> primary-aetheryte-name map by walking Aetheryte sheet
            // Diagnostic counters so we can see WHY a filter cuts results.
            int aeRows = 0, aeSkipNoTerritory = 0, aeSkipNotAetheryte = 0, aeSkipNoName = 0, aeAccepted = 0;
            var territoryToAetheryte = new Dictionary<uint, string>();

            foreach (var ae in aetheryteSheet)
            {
                aeRows++;
                if (ae.RowId == 0) continue;
                if (ae.Territory.RowId == 0) { aeSkipNoTerritory++; continue; }
                if (!ae.IsAetheryte) { aeSkipNotAetheryte++; continue; }
                var name = ae.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;
                if (string.IsNullOrEmpty(name)) { aeSkipNoName++; continue; }
                if (!territoryToAetheryte.ContainsKey(ae.Territory.RowId))
                    territoryToAetheryte[ae.Territory.RowId] = name;
                aeAccepted++;
            }

            // Second pass: filter TerritoryType for candidates
            int terrRows = 0, terrSameExpac = 0, terrWithAe = 0, terrFinal = 0;
            var candidates = new List<(uint terrId, string aeName)>();
            foreach (var t in territoryTypeSheet)
            {
                terrRows++;
                if (t.RowId == 0 || t.RowId == currentTerritoryId) continue;
                if (t.ExVersion.RowId != currentExpansion) continue;
                terrSameExpac++;
                if (t.IsPvpZone) continue;
                if (!territoryToAetheryte.TryGetValue(t.RowId, out var aeName)) continue;
                terrWithAe++;
                candidates.Add((t.RowId, aeName));
                terrFinal++;
            }

            Plugin.PluginLog.Information(
                $"ZoneSwapper diagnostic: currentTerritory={currentTerritoryId} exVer={currentExpansion} | " +
                $"Aetheryte sheet: {aeRows} rows, accepted={aeAccepted} (skipNoTerritory={aeSkipNoTerritory}, skipNotAetheryte={aeSkipNotAetheryte}, skipNoName={aeSkipNoName}) | " +
                $"TerritoryType: {terrRows} rows, sameExpac={terrSameExpac}, withAe={terrWithAe}, candidates={terrFinal}");

            if (candidates.Count == 0)
            {
                Plugin.PluginLog.Warning(
                    $"ZoneSwapper: no candidate aetherytes in expansion {currentExpansion}. " +
                    $"(territoryToAetheryte map has {territoryToAetheryte.Count} entries total — " +
                    "if that's also 0, the Aetheryte sheet enumeration is broken.)");
                return null;
            }

            var picked = candidates[Random.Shared.Next(candidates.Count)];
            Plugin.PluginLog.Information($"ZoneSwapper: picked '{picked.aeName}' (territory {picked.terrId}) from {candidates.Count} candidates in expansion {currentExpansion}.");
            return picked.aeName;
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Warning(ex, "ZoneSwapper: enumeration failed");
            return null;
        }
    }

    public static void TeleportTo(string aetheryteName)
    {
        if (string.IsNullOrEmpty(aetheryteName)) return;
        Chat.SendMessage($"/tp {aetheryteName}");
    }
}
