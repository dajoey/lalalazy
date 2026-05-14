using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using LuminaCabinet = Lumina.Excel.Sheets.Cabinet;
using CabinetState = FFXIVClientStructs.FFXIV.Client.Game.UI.Cabinet.CabinetState;

namespace ArmoireAutoFill.Logic;

// Observes the in-game armoire (cabinet) and caches which item IDs the player
// has stored there.
//
// TWO data sources, in order of preference:
//   1. UIState.Cabinet.IsItemInCabinet — the live API. Covers ALL cabinet rows
//      (Dawntrail items included). Only usable while Cabinet.State == Loaded,
//      which becomes true when the player opens the armoire UI. We hook the
//      Cabinet and MiragePrismPrismBox addons to catch that moment.
//   2. ItemFinderModule.CabinetItemUnlockBits — the per-session cached bitmap.
//      Loaded automatically on login. Limited to FixedSizeArray125<uint> =
//      4000 bits, so cabinet rows past 4000 (current = DT items) are invisible
//      here. Used only as a cold-start fallback before the user has opened
//      the armoire UI.
//
// When the snapshot changes we fire OnSnapshotChanged so the rest of the
// plugin can re-scan and the UI updates without user action.
public sealed class CabinetObserver : IDisposable
{
    private const string CabinetAddonName = "Cabinet";
    private const string DresserAddonName = "MiragePrismPrismBox";
    private const int BitmapWords = 125;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private HashSet<uint> _cachedArmoireItemIds;
    private DateTime _nextPoll = DateTime.MinValue;
    private bool _liveCaptureDone;

    public event Action? OnSnapshotChanged;

    public CabinetObserver()
    {
        _cachedArmoireItemIds = [.. Plugin.Configuration.ArmoireItemIds];
        Svc.Framework.Update += OnFrameworkUpdate;
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, CabinetAddonName, OnAddonRefresh);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, DresserAddonName, OnAddonRefresh);
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnFrameworkUpdate;
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostRefresh, CabinetAddonName, OnAddonRefresh);
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostRefresh, DresserAddonName, OnAddonRefresh);
    }

    public bool IsInArmoire(uint itemId) => _cachedArmoireItemIds.Contains(itemId);

    public IReadOnlyCollection<uint> CachedArmoireItemIds => _cachedArmoireItemIds;

    public DateTime LastSnapshot { get; private set; } = DateTime.MinValue;

    public bool CabinetDataAvailable { get; private set; }

    public bool LiveCaptureDone => _liveCaptureDone;

    public void ForceSnapshot() => TrySnapshot(force: true);

    private void OnAddonRefresh(AddonEvent type, AddonArgs args) => TrySnapshot(force: true);

    private void OnFrameworkUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        if (DateTime.UtcNow < _nextPoll)
            return;
        _nextPoll = DateTime.UtcNow + PollInterval;
        TrySnapshot(force: false);
    }

    private unsafe void TrySnapshot(bool force)
    {
        var cabinetSheet = Svc.Data.GetExcelSheet<LuminaCabinet>();
        if (cabinetSheet == null)
            return;

        // Prefer the live API (full row coverage) when the armoire UI has been opened
        // at least once this session. Fall back to the ItemFinderModule bitmap for
        // cold-start coverage (limited to first 4000 cabinet rows).
        var uiState = UIState.Instance();
        var useLiveApi = uiState != null && uiState->Cabinet.IsCabinetLoaded();
        CabinetDataAvailable = useLiveApi;

        HashSet<uint> newIds;
        if (useLiveApi)
        {
            newIds = SnapshotFromLiveApi(cabinetSheet);
            _liveCaptureDone = true;
        }
        else
        {
            if (_liveCaptureDone && !force)
            {
                // Once we've captured the live API at least once, don't downgrade
                // to the bitmap — that would erase DT items every time the armoire
                // UI is closed.
                return;
            }
            var ifm = ItemFinderModule.Instance();
            if (ifm == null)
                return;
            if ((CabinetState)ifm->CabinetState != CabinetState.Loaded)
                return;
            newIds = SnapshotFromBitmap(cabinetSheet, ifm);
        }

        if (!force && newIds.SetEquals(_cachedArmoireItemIds))
            return;

        _cachedArmoireItemIds = newIds;
        Plugin.Configuration.ArmoireItemIds = [.. newIds];
        Plugin.Configuration.Save();
        LastSnapshot = DateTime.UtcNow;
        Svc.Log.Information(
            $"[ArmoireAutoFill] cabinet snapshot updated: {newIds.Count} items stored "
            + $"(via {(useLiveApi ? "live API" : "bitmap fallback")})");
        OnSnapshotChanged?.Invoke();
    }

    private static unsafe HashSet<uint> SnapshotFromLiveApi(Lumina.Excel.ExcelSheet<LuminaCabinet> cabinetSheet)
    {
        var uiState = UIState.Instance();
        var newIds = new HashSet<uint>();
        foreach (var cabinetRow in cabinetSheet)
        {
            if (!uiState->Cabinet.IsItemInCabinet(cabinetRow.RowId))
                continue;
            var itemId = cabinetRow.Item.RowId;
            if (itemId != 0)
                newIds.Add(itemId);
        }
        return newIds;
    }

    private static unsafe HashSet<uint> SnapshotFromBitmap(Lumina.Excel.ExcelSheet<LuminaCabinet> cabinetSheet, ItemFinderModule* ifm)
    {
        var newIds = new HashSet<uint>();
        foreach (var cabinetRow in cabinetSheet)
        {
            var rowId = cabinetRow.RowId;
            var wordIdx = rowId / 32;
            var bitIdx = (int)(rowId % 32);
            if (wordIdx >= BitmapWords)
                continue;
            if ((ifm->CabinetItemUnlockBits[(int)wordIdx] & (1u << bitIdx)) == 0)
                continue;
            var itemId = cabinetRow.Item.RowId;
            if (itemId != 0)
                newIds.Add(itemId);
        }
        return newIds;
    }
}
