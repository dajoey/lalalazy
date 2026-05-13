using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using LuminaCabinet = Lumina.Excel.Sheets.Cabinet;

namespace ArmoireAutoFill.Logic;

// Observes the in-game armoire (cabinet) and caches which item IDs the player
// currently has stored there. The game only exposes cabinet contents to plugins
// while the armoire UI is loaded (typically at an inn), so we snapshot on every
// refresh and persist the result so the plugin can answer "is this in your
// armoire?" between sessions.
//
// Hooks two addons:
//   "Cabinet"             — the armoire UI itself
//   "MiragePrismPrismBox" — the glamour dresser UI, used as a secondary trigger
//                           since the armoire is usually co-located at inns
public sealed class CabinetObserver : IDisposable
{
    private const string CabinetAddonName = "Cabinet";
    private const string DresserAddonName = "MiragePrismPrismBox";

    private HashSet<uint> _cachedArmoireItemIds;

    public CabinetObserver()
    {
        _cachedArmoireItemIds = [.. Plugin.Configuration.ArmoireItemIds];

        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, CabinetAddonName, OnAddonRefresh);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, DresserAddonName, OnAddonRefresh);

        // If the armoire is already open when the plugin loads, grab a snapshot.
        TrySnapshot();
    }

    public void Dispose()
    {
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostRefresh, CabinetAddonName, OnAddonRefresh);
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostRefresh, DresserAddonName, OnAddonRefresh);
    }

    public bool IsInArmoire(uint itemId) => _cachedArmoireItemIds.Contains(itemId);

    public IReadOnlyCollection<uint> CachedArmoireItemIds => _cachedArmoireItemIds;

    public DateTime LastSnapshot { get; private set; } = DateTime.MinValue;

    private void OnAddonRefresh(AddonEvent type, AddonArgs args) => TrySnapshot();

    private unsafe void TrySnapshot()
    {
        var uiState = UIState.Instance();
        if (uiState == null)
            return;

        if (!uiState->Cabinet.IsCabinetLoaded())
            return;

        var newIds = new HashSet<uint>();
        var cabinetSheet = Svc.Data.GetExcelSheet<LuminaCabinet>();
        if (cabinetSheet == null)
            return;

        foreach (var cabinetRow in cabinetSheet)
        {
            if (!uiState->Cabinet.IsItemInCabinet(cabinetRow.RowId))
                continue;

            var itemId = cabinetRow.Item.RowId;
            if (itemId != 0)
                newIds.Add(itemId);
        }

        if (newIds.SetEquals(_cachedArmoireItemIds))
            return;

        _cachedArmoireItemIds = newIds;
        Plugin.Configuration.ArmoireItemIds = [.. newIds];
        Plugin.Configuration.Save();
        LastSnapshot = DateTime.UtcNow;
        Svc.Log.Information($"[ArmoireAutoFill] cabinet snapshot updated: {newIds.Count} items stored");
    }
}
