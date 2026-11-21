using System.Reflection;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons.Reflection;

namespace LazyCrafter.Adapters.Dispatch;

/// <summary>
/// Monster spawn levels embedded in the installed WahTools (0.1.7.15, WahTools finding 4): the manifest resource
/// <c>WahTools.Data.Hunter.spawns.json</c>, shaped <c>{"monsters": {"&lt;BNpcNameId&gt;": {"level": [low, high]}}}</c>,
/// read out of the loaded plugin's own assembly the way <see cref="LazyCrafter.Adapters.ReflectionGuard"/> reads a
/// pinned plugin. LazyCrafter has no monster-level data of its own; these feed
/// <see cref="Core.FarmContext.OrderMobs"/> so Hunter is never sent at a monster far above the character.
/// <para>Failures are soft: an unreadable resource yields an empty map - no reordering - and one warning per session.</para>
/// </summary>
public sealed class WahToolsSpawnLevels
{
    public const string ResourceName = "WahTools.Data.Hunter.spawns.json";

    private readonly IDalamudPluginInterface _pi;
    private readonly IPluginLog _log;
    private readonly Dictionary<Version, Dictionary<uint, (int Low, int High)>> _cache = new();
    private bool _warned;

    public WahToolsSpawnLevels(IDalamudPluginInterface pi, IPluginLog log)
    {
        _pi = pi;
        _log = log;
    }

    /// <summary>The (Low, High) levels for a BNpcName id, or null when WahTools does not know the monster.</summary>
    public (int Low, int High)? Get(uint bnpcNameId) =>
        Levels().TryGetValue(bnpcNameId, out var lv) ? lv : null;

    private IReadOnlyDictionary<uint, (int Low, int High)> Levels()
    {
        if (!DalamudReflector.TryGetDalamudPlugin("WahTools", out var plugin, false, true) || plugin is null)
            return Warn("WahTools is not loaded");
        var asm = plugin.GetType().Assembly;
        var ver = asm.GetName().Version ?? new Version(0, 0);
        if (_cache.TryGetValue(ver, out var cached)) return cached;
        var parsed = Parse(asm, ver);
        _cache[ver] = parsed;
        return parsed;
    }

    private Dictionary<uint, (int Low, int High)> Parse(Assembly asm, Version ver)
    {
        try
        {
            using var stream = asm.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"manifest resource {ResourceName} not found");
            using var doc = JsonDocument.Parse(stream);
            var map = new Dictionary<uint, (int Low, int High)>();
            foreach (var entry in doc.RootElement.GetProperty("monsters").EnumerateObject())
            {
                if (!uint.TryParse(entry.Name, out var id) || !entry.Value.TryGetProperty("level", out var lv)) continue;
                int low = int.MaxValue, high = int.MinValue;
                if (!CollectLevels(lv, ref low, ref high)) continue;
                map[id] = (low, high);
            }
            return map;
        }
        catch (Exception ex)
        {
            return Warn($"cannot read {ResourceName} from WahTools {ver}: {ex.Message}");
        }
    }

    /// <summary>Widen [low, high] over every number in the level array; a range may itself hold sub-ranges.</summary>
    private static bool CollectLevels(JsonElement node, ref int low, ref int high)
    {
        var any = false;
        if (node.ValueKind != JsonValueKind.Array) return any;
        foreach (var e in node.EnumerateArray())
        {
            if (e.ValueKind == JsonValueKind.Number)
            {
                var v = e.GetInt32();
                low = Math.Min(low, v);
                high = Math.Max(high, v);
                any = true;
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                any |= CollectLevels(e, ref low, ref high);
            }
        }
        return any && low <= high;
    }

    private Dictionary<uint, (int Low, int High)> Warn(string why)
    {
        if (!_warned)
        {
            _warned = true;
            _log.Warning("WahTools spawn levels unavailable: {Why}", why);
        }
        return new Dictionary<uint, (int Low, int High)>();
    }
}
