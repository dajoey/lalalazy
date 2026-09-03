using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using LazyCrafter.Core;
using LazyCrafter.Core.Model;
using Lumina.Excel.Sheets;

namespace LazyCrafter.Adapters;

/// <summary>
/// What the game knows about the logged-in character (Plan §Phase 3 task 4): crafter/gatherer job
/// levels, crafting-log completion (<see cref="ICraftingLog"/>), home world / data centre, and the
/// retainer stats the venture resolver needs.
/// <para>
/// Retainer stats come from ARControl's config file (<c>pluginConfigs/ARControl.json</c>), read-only,
/// because the game only exposes retainer stats while the summoning bell is open. Without that file
/// <see cref="Retainers"/> is empty, <see cref="SourceKind.Venture"/> never appears, and
/// <see cref="RetainerHint"/> says why.
/// </para>
/// Job levels / world / content id come from Dalamud's <c>IPlayerState</c> (API 15).
/// </summary>
public sealed class PlayerState : ICraftingLog
{
    /// <summary>ClassJob row ids of the eight crafters (CRP..CUL) and three gatherers (MIN, BTN, FSH).</summary>
    public static readonly uint[] CrafterJobs = [8, 9, 10, 11, 12, 13, 14, 15];
    public static readonly uint[] GathererJobs = [16, 17, 18];

    private readonly IClientState _clientState;
    private readonly IPlayerState _player;
    private readonly IDataManager _data;
    private readonly IPluginLog _log;
    private readonly string _arcConfigPath;

    private IReadOnlyList<RetainerStats> _retainers = [];
    private IReadOnlySet<uint>? _gatheredItems;
    private DateTime _arcReadAt = DateTime.MinValue;
    private DateTime _arcMtime = DateTime.MinValue;
    private ulong _arcReadFor;

    public PlayerState(IDalamudPluginInterface pi, IClientState clientState, IPlayerState player, IDataManager data, IPluginLog log)
    {
        _clientState = clientState;
        _player = player;
        _data = data;
        _log = log;
        _arcConfigPath = Path.Combine(pi.ConfigDirectory.Parent?.FullName ?? pi.ConfigDirectory.FullName, "ARControl.json");
    }

    public bool IsLoggedIn => _clientState.IsLoggedIn && _player.IsLoaded;
    public ulong ContentId => _player.IsLoaded ? _player.ContentId : 0;

    // ---------------------------------------------------------------- jobs

    /// <summary>Level of a job for the local player; 0 when not unlocked or not logged in.</summary>
    public int JobLevel(uint classJobId)
    {
        if (!IsLoggedIn) return 0;
        return _data.GetExcelSheet<ClassJob>().TryGetRow(classJobId, out var job) ? _player.GetClassJobLevel(job) : 0;
    }

    /// <summary>Every crafter/gatherer job with level > 0.</summary>
    public IReadOnlyDictionary<uint, int> UnlockedJobs()
    {
        var d = new Dictionary<uint, int>();
        foreach (var j in CrafterJobs.Concat(GathererJobs))
        {
            var lvl = JobLevel(j);
            if (lvl > 0) d[j] = lvl;
        }
        return d;
    }

    // ---------------------------------------------------------------- crafting log

    public bool IsRecipeComplete(uint recipeId) => IsLoggedIn && QuestManager.IsRecipeComplete(recipeId);

    // ---------------------------------------------------------------- world

    public uint HomeWorldId => _player.IsLoaded ? _player.HomeWorld.RowId : 0;

    public string HomeWorldName => _player.IsLoaded ? _player.HomeWorld.ValueNullable?.Name.ExtractText() ?? "" : "";

    /// <summary>Data-centre name as Universalis spells it (e.g. "Aether"); empty when not logged in.</summary>
    public string DataCenterName => _player.IsLoaded
        ? _player.HomeWorld.ValueNullable?.DataCenter.ValueNullable?.Name.ExtractText() ?? ""
        : "";

    // ---------------------------------------------------------------- retainers (ARControl.json, read-only)

    /// <summary>Managed retainers of the current character per ARControl; empty when unknown.</summary>
    public IReadOnlyList<RetainerStats> Retainers
    {
        get { RefreshRetainers(); return _retainers; }
    }

    /// <summary>Items the current character has gathered at least once (ARControl's copy of the gathering log), or <c>null</c> when unknown.</summary>
    public IReadOnlySet<uint>? GatheredItems
    {
        get { RefreshRetainers(); return _gatheredItems; }
    }

    public bool ArcConfigPresent => File.Exists(_arcConfigPath);

    /// <summary>Why there are no retainers, for the settings tab / debug line. <c>null</c> when everything is fine.</summary>
    public string? RetainerHint
    {
        get
        {
            if (!IsLoggedIn) return "not logged in";
            if (!ArcConfigPresent) return "ARControl.json not found - install/configure ARC (AutoRetainer Control) so LazyCrafter can see retainer stats; ventures are disabled until then";
            if (Retainers.Count == 0) return "ARControl.json has no managed retainers for this character; ventures are disabled";
            return null;
        }
    }

    /// <summary>Re-read ARControl.json when it changed on disk or the character changed (checked at most every 30 s).</summary>
    private void RefreshRetainers()
    {
        var cid = ContentId;
        if (cid == _arcReadFor && DateTime.UtcNow - _arcReadAt < TimeSpan.FromSeconds(30)) return;
        _arcReadAt = DateTime.UtcNow;
        try
        {
            if (cid == 0 || !File.Exists(_arcConfigPath)) { _retainers = []; _gatheredItems = null; _arcReadFor = cid; return; }
            var mtime = File.GetLastWriteTimeUtc(_arcConfigPath);
            if (cid == _arcReadFor && mtime == _arcMtime) return;
            _arcMtime = mtime;
            _arcReadFor = cid;

            using var doc = JsonDocument.Parse(File.ReadAllText(_arcConfigPath));
            var list = new List<RetainerStats>();
            HashSet<uint>? gathered = null;
            if (doc.RootElement.TryGetProperty("Characters", out var chars) && chars.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in chars.EnumerateArray())
                {
                    if (!c.TryGetProperty("LocalContentId", out var idEl) || !idEl.TryGetUInt64(out var id) || id != cid) continue;
                    if (c.TryGetProperty("Retainers", out var rets) && rets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var r in rets.EnumerateArray())
                        {
                            if (r.TryGetProperty("Managed", out var m) && m.ValueKind == JsonValueKind.False) continue;
                            list.Add(new RetainerStats(
                                Name: r.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
                                Level: Int(r, "Level"),
                                JobId: (uint)Int(r, "Job"),
                                ItemLevel: Int(r, "ItemLevel"),
                                Gathering: Int(r, "Gathering"),
                                Perception: Int(r, "Perception")));
                        }
                    }
                    if (c.TryGetProperty("GatheredItems", out var g) && g.ValueKind == JsonValueKind.Array)
                    {
                        gathered = new HashSet<uint>();
                        foreach (var e in g.EnumerateArray()) if (e.TryGetUInt32(out var item)) gathered.Add(item);
                    }
                    break;
                }
            }
            _retainers = list;
            _gatheredItems = gathered;
            _log.Debug("ARControl.json: {Count} managed retainers, {Gathered} gathered items for {Cid:X}", list.Count, gathered?.Count ?? -1, cid);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "ARControl.json unreadable; retainers unknown");
            _retainers = [];
            _gatheredItems = null;
        }

        static int Int(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;
    }
}
