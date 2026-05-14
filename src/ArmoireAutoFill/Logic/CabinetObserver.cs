using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using LuminaCabinet = Lumina.Excel.Sheets.Cabinet;
using CabinetState = FFXIVClientStructs.FFXIV.Client.Game.UI.Cabinet.CabinetState;

namespace ArmoireAutoFill.Logic;

// Observes the in-game armoire (cabinet) and caches which item IDs the player
// has stored there.
//
// The game keeps a bitmap of cabinet contents in ItemFinderModule that is
// populated automatically on login (the CabinetState byte goes from Invalid
// to Loaded once the server delivers the data). We poll that bitmap on
// Framework.Update — no need for the player to open the armoire UI.
//
// When the bitmap changes (user stored / withdrew an item, or it just
// became available after login), we fire OnSnapshotChanged so the rest of
// the plugin can re-scan.
public sealed class CabinetObserver : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private HashSet<uint> _cachedArmoireItemIds;
    private DateTime _nextPoll = DateTime.MinValue;

    public event Action? OnSnapshotChanged;

    public CabinetObserver()
    {
        _cachedArmoireItemIds = [.. Plugin.Configuration.ArmoireItemIds];
        Svc.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnFrameworkUpdate;
    }

    public bool IsInArmoire(uint itemId) => _cachedArmoireItemIds.Contains(itemId);

    public IReadOnlyCollection<uint> CachedArmoireItemIds => _cachedArmoireItemIds;

    public DateTime LastSnapshot { get; private set; } = DateTime.MinValue;

    public bool CabinetDataAvailable { get; private set; }

    public void ForceSnapshot() => TrySnapshot(force: true);

    private void OnFrameworkUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        if (DateTime.UtcNow < _nextPoll)
            return;
        _nextPoll = DateTime.UtcNow + PollInterval;
        TrySnapshot(force: false);
    }

    private unsafe void TrySnapshot(bool force)
    {
        var ifm = ItemFinderModule.Instance();
        if (ifm == null)
        {
            CabinetDataAvailable = false;
            return;
        }

        // ItemFinderModule->CabinetState is a byte mirror of Cabinet.CabinetState.
        // Until it reaches Loaded the bitmap is meaningless.
        if ((CabinetState)ifm->CabinetState != CabinetState.Loaded)
        {
            CabinetDataAvailable = false;
            if (!force)
                return;
        }
        else
        {
            CabinetDataAvailable = true;
        }

        var cabinetSheet = Svc.Data.GetExcelSheet<LuminaCabinet>();
        if (cabinetSheet == null)
            return;

        var newIds = new HashSet<uint>();
        foreach (var cabinetRow in cabinetSheet)
        {
            var rowId = cabinetRow.RowId;
            var wordIdx = rowId / 32;
            var bitIdx = (int)(rowId % 32);
            if (wordIdx >= 125)
                continue;
            if ((ifm->CabinetItemUnlockBits[(int)wordIdx] & (1u << bitIdx)) == 0)
                continue;

            var itemId = cabinetRow.Item.RowId;
            if (itemId != 0)
                newIds.Add(itemId);
        }

        if (!force && newIds.SetEquals(_cachedArmoireItemIds))
            return;

        _cachedArmoireItemIds = newIds;
        Plugin.Configuration.ArmoireItemIds = [.. newIds];
        Plugin.Configuration.Save();
        LastSnapshot = DateTime.UtcNow;
        Svc.Log.Information($"[ArmoireAutoFill] cabinet snapshot updated: {newIds.Count} items stored");
        OnSnapshotChanged?.Invoke();
    }
}
