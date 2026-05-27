using System;
using System.Collections.Generic;
using ECommons.Automation;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using Telepo = FFXIVClientStructs.FFXIV.Client.Game.UI.Telepo;

namespace LazyFATEAutomator;

/// <summary>
/// Picks a random overworld aetheryte in the same expansion as the player's current zone
/// (that the player has unlocked, per Telepo's teleport list) and returns its display name
/// for use with /tp (TeleporterPlugin) or /li (Lifestream).
/// </summary>
public static class ZoneSwapper
{
    public static unsafe string? PickRandomSameExpacAetheryte()
    {
        try
        {
            var dm = Plugin.DataManager;
            var territoryTypeSheet = dm.GetExcelSheet<TerritoryType>();
            var aetheryteSheet     = dm.GetExcelSheet<Aetheryte>();
            if (territoryTypeSheet == null || aetheryteSheet == null) return null;

            var currentTerritoryId = Svc.ClientState.TerritoryType;
            if (!territoryTypeSheet.TryGetRow(currentTerritoryId, out var currentTerritory)) return null;
            var currentExpansion = currentTerritory.ExVersion.RowId;

            // Telepo.TeleportList is the player's authoritative unlocked-aetheryte list.
            var telepo = Telepo.Instance();
            if (telepo == null) return null;
            telepo->UpdateAetheryteList();
            if (telepo->TeleportList.Count == 0) return null;

            // Build set of unlocked aetheryte IDs (skip housing entries via EstateType == 0)
            var unlockedAetheryteIds = new HashSet<uint>();
            foreach (var t in telepo->TeleportList)
            {
                if (t.EstateType != 0) continue;     // housing/apartment/private chambers — skip
                if (t.AetheryteId == 0) continue;
                if (t.TerritoryId == currentTerritoryId) continue;
                unlockedAetheryteIds.Add(t.AetheryteId);
            }
            if (unlockedAetheryteIds.Count == 0)
            {
                Plugin.PluginLog.Warning("ZoneSwapper: TeleportList is empty after filters (no other aetherytes unlocked?)");
                return null;
            }

            var candidates = new List<(uint id, string name, uint territoryId)>();
            foreach (var ae in aetheryteSheet)
            {
                if (!unlockedAetheryteIds.Contains(ae.RowId)) continue;
                if (!ae.IsAetheryte) continue; // aethernet shards are AetheryteId children too — skip
                if (ae.Territory.RowId == 0) continue;
                if (ae.Territory.ValueNullable is not { } terr) continue;
                if (terr.ExVersion.RowId != currentExpansion) continue;
                if (terr.IsPvpZone) continue;

                var name = ae.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;
                if (string.IsNullOrEmpty(name)) continue;
                candidates.Add((ae.RowId, name, ae.Territory.RowId));
            }

            if (candidates.Count == 0)
            {
                Plugin.PluginLog.Warning($"ZoneSwapper: no eligible aetherytes in expansion {currentExpansion} (had {unlockedAetheryteIds.Count} unlocked total).");
                return null;
            }

            var picked = candidates[Random.Shared.Next(candidates.Count)];
            Plugin.PluginLog.Information($"ZoneSwapper: picked {picked.name} (aetheryte {picked.id}, territory {picked.territoryId}) from {candidates.Count} candidates in expansion {currentExpansion}.");
            return picked.name;
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Warning(ex, "ZoneSwapper: candidate enumeration failed");
            return null;
        }
    }

    /// <summary>Issues a teleport via TeleporterPlugin (/tp). User has TeleporterPlugin installed.</summary>
    public static void TeleportTo(string aetheryteName)
    {
        if (string.IsNullOrEmpty(aetheryteName)) return;
        Chat.SendMessage($"/tp {aetheryteName}");
    }
}
