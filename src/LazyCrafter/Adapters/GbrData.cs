using System.Reflection;
using Dalamud.Plugin.Services;
using ECommons.Reflection;
using LazyCrafter.Core;

namespace LazyCrafter.Adapters;

/// <summary>
/// Reads node type / level / gathering job for gatherable items out of a loaded GatherBuddyReborn
/// (Plan §Phase 3 task 1: "read from <c>GatherBuddy.GameData</c> via reflection when GBR is loaded").
/// <para>
/// Reflected shape (verified against GBR source 2026-09-03, plugin InternalName <c>GatherBuddyReborn</c>):
/// <c>GatherBuddy.GatherBuddy.GameData</c> (public static) → <c>.Gatherables</c>
/// (<c>FrozenDictionary&lt;uint itemId, Gatherable&gt;</c>) → <c>Gatherable.NodeType</c> (byte enum:
/// Regular 0, Unspoiled 1, Ephemeral 2, Legendary 3, Clouded 4, Unknown 255), <c>.Level</c> (int),
/// <c>.GatheringType</c> (byte enum: Mining 0, Quarrying 1, Logging 2, Harvesting 3, Spearfishing 4,
/// Botanist 5, Miner 6, Fisher 7, Multiple 8). Any shape mismatch is logged once and the reader reports
/// <see cref="Available"/> = false so <see cref="LuminaGameData"/> falls back to the sheets.
/// </para>
/// </summary>
public sealed class GbrData
{
    private readonly IPluginLog _log;
    private Dictionary<uint, GatherInfo>? _snapshot;
    private bool _probed;

    public GbrData(IPluginLog log) => _log = log;

    public bool Available => _snapshot is { Count: > 0 };
    public int Count => _snapshot?.Count ?? 0;

    /// <summary>The GBR view of an item, or <c>null</c> when GBR is not loaded / does not know it.</summary>
    public GatherInfo? Get(uint itemId)
    {
        if (!_probed) Refresh();
        return _snapshot is not null && _snapshot.TryGetValue(itemId, out var g) ? g : null;
    }

    /// <summary>(Re)read the whole gatherable table from GBR. Cheap (a few thousand entries); call on demand.</summary>
    public void Refresh()
    {
        _probed = true;
        try
        {
            if (!DalamudReflector.TryGetDalamudPlugin("GatherBuddyReborn", out var plugin, false, true) || plugin is null)
            {
                _snapshot = null;
                return;
            }

            var pluginType = plugin.GetType();
            var gameDataProp = pluginType.GetProperty("GameData", BindingFlags.Public | BindingFlags.Static);
            var gameData = gameDataProp?.GetValue(null);
            if (gameData is null) { Fail("GatherBuddy.GameData property missing"); return; }

            var gatherablesProp = gameData.GetType().GetProperty("Gatherables", BindingFlags.Public | BindingFlags.Instance);
            if (gatherablesProp?.GetValue(gameData) is not System.Collections.IEnumerable dict) { Fail("GameData.Gatherables missing"); return; }

            var result = new Dictionary<uint, GatherInfo>();
            var skipped = 0;
            PropertyInfo? nodeTypeProp = null, levelProp = null, gatheringTypeProp = null, itemDataProp = null;
            PropertyInfo? isCollectableProp = null;
            foreach (var kv in dict)
            {
                var kvType = kv.GetType();
                var value = kvType.GetProperty("Value")?.GetValue(kv);
                if (value is null) continue;
                var key = kvType.GetProperty("Key")?.GetValue(kv);
                if (key is not uint itemId) continue;

                var t = value.GetType();
                nodeTypeProp ??= t.GetProperty("NodeType");
                levelProp ??= t.GetProperty("Level");
                gatheringTypeProp ??= t.GetProperty("GatheringType");
                itemDataProp ??= t.GetProperty("ItemData");
                if (nodeTypeProp is null || levelProp is null || gatheringTypeProp is null) { Fail("Gatherable shape changed"); return; }

                var nodeType = Convert.ToByte(nodeTypeProp.GetValue(value));
                var level = Convert.ToInt32(levelProp.GetValue(value));
                var gatheringType = Convert.ToByte(gatheringTypeProp.GetValue(value));

                // GBR leaves NodeType at Unknown (255) for a gatherable it has no reachable node for - its
                // AddNodeToItem only runs for nodes in a live territory. Those entries carry no usable node type,
                // and folding them to Regular would overwrite a correct sheet-derived timed node with a wrong
                // "regular" one. Skip them; the sheet pass already has the right answer.
                if (nodeType == 255) { skipped++; continue; }

                var collectable = false;
                if (itemDataProp?.GetValue(value) is { } itemRow)
                {
                    isCollectableProp ??= itemRow.GetType().GetProperty("IsCollectable");
                    if (isCollectableProp?.GetValue(itemRow) is bool c) collectable = c;
                }

                result[itemId] = new GatherInfo(
                    JobId: JobFor(gatheringType),
                    Level: level,
                    NodeType: nodeType switch
                    {
                        1 => NodeType.Unspoiled,
                        2 => NodeType.Ephemeral,
                        3 => NodeType.Legendary,
                        4 => NodeType.Clouded,
                        _ => NodeType.Regular,
                    },
                    Timed: nodeType is 1 or 2 or 3,
                    Collectable: collectable);
            }
            _snapshot = result;
            _log.Information("GatherBuddyReborn game data read via reflection: {Count} gatherables with a reachable node ({Skipped} listed but nodeless, ignored)", result.Count, skipped);
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    /// <summary>GBR GatheringType → ClassJob row id (MIN 16 / BTN 17 / FSH 18). Multiple/unknown → MIN (the level gate still applies).</summary>
    private static uint JobFor(byte gatheringType) => gatheringType switch
    {
        0 or 1 or 6 => 16,
        2 or 3 or 5 => 17,
        4 or 7 => 18,
        _ => 16,
    };

    private void Fail(string why)
    {
        _snapshot = null;
        _log.Warning("GatherBuddyReborn reflection unavailable ({Why}); node types fall back to GatheringPointTransient", why);
    }
}
