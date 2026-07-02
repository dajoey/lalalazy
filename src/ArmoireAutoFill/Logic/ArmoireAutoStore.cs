using System.Collections.Generic;
using System.Linq;
using ArmoireAutoFill.Data;
using ArmoireAutoFill.Models;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using LuminaCabinet = Lumina.Excel.Sheets.Cabinet;

namespace ArmoireAutoFill.Logic;

/// <summary>
/// Stores eligible items from the player's inventory into the armoire (cabinet).
/// Uses the native Cabinet.StoreCabinetItem API, which is the same code path the game
/// uses when you manually right-click → "Store in Armoire" inside the Cabinet UI.
/// </summary>
public sealed class ArmoireAutoStore : IDisposable
{
    private const string CabinetAddonName = "Cabinet";

    /// <summary>Armed when the Cabinet addon opens; the actual store is deferred until
    /// the cabinet data has finished loading (polled on Framework.Update).</summary>
    private bool _pendingAutoStore;
    private DateTime _pendingDeadline;
    private static readonly TimeSpan AutoStoreTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Item ID → Cabinet RowId lookup, built once from the Lumina Cabinet sheet.</summary>
    private readonly Dictionary<uint, uint> _itemToCabinetId = [];

    /// <summary>True while a store operation is in progress (prevents re-entry).</summary>
    private bool _isStoring;

    public bool IsStoring => _isStoring;
    public int LastStoredCount { get; private set; }
    public string LastResultMessage { get; private set; } = string.Empty;

    public event Action? OnStoreComplete;

    public ArmoireAutoStore()
    {
        BuildItemLookup();

        // Auto-store when the armoire UI opens, if configured.
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, CabinetAddonName, OnCabinetOpened);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, CabinetAddonName, OnCabinetClosed);
        Svc.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnFrameworkUpdate;
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, CabinetAddonName, OnCabinetOpened);
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, CabinetAddonName, OnCabinetClosed);
    }

    private void BuildItemLookup()
    {
        var sheet = Svc.Data.GetExcelSheet<LuminaCabinet>();
        if (sheet == null)
        {
            Svc.Log.Error("[ArmoireAutoFill] Cabinet sheet unavailable; auto-store disabled.");
            return;
        }

        foreach (var row in sheet)
        {
            var itemId = row.Item.RowId;
            if (itemId != 0)
                _itemToCabinetId[itemId] = row.RowId;
        }

        Svc.Log.Information($"[ArmoireAutoFill] auto-store lookup built: {_itemToCabinetId.Count} cabinet entries.");
    }

    private void OnCabinetOpened(AddonEvent type, AddonArgs args)
    {
        if (!Plugin.Configuration.AutoStoreOnOpen)
            return;

        // Cabinet contents load from the server asynchronously AFTER the addon opens,
        // so IsCabinetLoaded() is typically still false here. Arm the store and let
        // OnFrameworkUpdate fire it once the data is actually available.
        _pendingAutoStore = true;
        _pendingDeadline = DateTime.UtcNow + AutoStoreTimeout;
        Svc.Log.Debug("[ArmoireAutoFill] Cabinet opened; auto-store armed, waiting for cabinet data.");
    }

    private void OnCabinetClosed(AddonEvent type, AddonArgs args)
    {
        _pendingAutoStore = false;
    }

    private unsafe void OnFrameworkUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        if (!_pendingAutoStore)
            return;

        if (DateTime.UtcNow > _pendingDeadline)
        {
            _pendingAutoStore = false;
            Svc.Log.Warning("[ArmoireAutoFill] auto-store timed out waiting for cabinet data to load.");
            return;
        }

        var uiState = UIState.Instance();
        if (uiState == null || !uiState->Cabinet.IsCabinetLoaded())
            return;

        _pendingAutoStore = false;
        StoreAll();
    }

    /// <summary>
    /// Builds the set of base item IDs that belong to any saved gearset, so they can be
    /// excluded from auto-store. Uses RaptureGearsetModule (same source as the in-game UI).
    /// </summary>
    private static unsafe HashSet<uint> BuildGearsetItemSet()
    {
        var set = new HashSet<uint>();
        var gm = RaptureGearsetModule.Instance();
        if (gm == null)
            return set;

        for (byte i = 0; i < 100; ++i)
        {
            if (!gm->IsValidGearset(i))
                continue;
            var gs = gm->GetGearset(i);
            if (gs == null || !gs->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
                continue;
            foreach (var it in gs->Items.ToArray())
            {
                var id = it.ItemId % 1000000u; // strip the HQ flag
                if (id != 0)
                    set.Add(id);
            }
        }
        return set;
    }

    /// <summary>
    /// Scans the player's inventory for items eligible for the armoire and stores them.
    /// Requires the Cabinet UI to be open (Cabinet.State == Loaded).
    /// </summary>
    public unsafe void StoreAll()
    {
        if (_isStoring)
        {
            Svc.Log.Warning("[ArmoireAutoFill] StoreAll already in progress.");
            return;
        }

        var uiState = UIState.Instance();
        if (uiState == null || !uiState->Cabinet.IsCabinetLoaded())
        {
            LastResultMessage = "Armoire UI must be open to store items.";
            Svc.Log.Warning($"[ArmoireAutoFill] {LastResultMessage}");
            OnStoreComplete?.Invoke();
            return;
        }

        _isStoring = true;
        var stored = 0;
        var skipped = 0;

        // Scan the same containers as InventoryScanner.
        var inventoryManager = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
        if (inventoryManager == null)
        {
            LastResultMessage = "InventoryManager unavailable.";
            _isStoring = false;
            OnStoreComplete?.Invoke();
            return;
        }

        // Deduplicate: an item may appear in multiple inventory slots but we only need
        // to store once per unique item ID. The armoire stores by item type, not stack.
        var candidates = new HashSet<uint>();

        // Regular inventory (bags) is always scanned; the armoury chest is opt-in.
        var containerList = new List<FFXIVClientStructs.FFXIV.Client.Game.InventoryType>
        {
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory1,
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory2,
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory3,
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory4,
        };

        if (Plugin.Configuration.AutoStoreIncludeArmory)
        {
            containerList.AddRange(
            [
                FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryMainHand,
                FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryOffHand,
                FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryHead,
                FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryBody,
                FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryHands,
                FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryLegs,
                FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryFeets,
                FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryEar,
                FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryNeck,
                FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryWrist,
                FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryRings,
                FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmorySoulCrystal,
            ]);
        }

        var containers = containerList;

        foreach (var containerType in containers)
        {
            var container = inventoryManager->GetInventoryContainer(containerType);
            if (container == null) continue;

            for (int i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null) continue;
                var itemId = slot->ItemId;
                if (itemId > 0)
                    candidates.Add(itemId);
            }
        }

        var gearsetItems = Plugin.Configuration.SkipGearsetItems ? BuildGearsetItemSet() : new HashSet<uint>();
        var gearsetSkipped = 0;

        foreach (var itemId in candidates)
        {
            // Skip gear that belongs to a saved gearset (opt-in via config, on by default).
            if (gearsetItems.Contains(itemId))
            {
                gearsetSkipped++;
                skipped++;
                continue;
            }

            // Not armoire-eligible at all.
            if (!_itemToCabinetId.TryGetValue(itemId, out var cabinetId))
            {
                skipped++;
                continue;
            }

            // Skip items already in the armoire.
            if (uiState->Cabinet.IsItemInCabinet(cabinetId))
            {
                skipped++;
                continue;
            }

            var success = uiState->Cabinet.StoreCabinetItem(cabinetId);
            if (success)
            {
                stored++;
                Svc.Log.Information($"[ArmoireAutoFill] stored item {itemId} (cabinet row {cabinetId})");
            }
            else
            {
                skipped++;
                Svc.Log.Warning($"[ArmoireAutoFill] failed to store item {itemId} (cabinet row {cabinetId})");
            }
        }

        LastStoredCount = stored;
        var gsNote = gearsetSkipped > 0 ? $" {gearsetSkipped} kept (in a gearset)." : "";
        LastResultMessage = stored > 0
            ? $"Stored {stored} item{(stored != 1 ? "s" : "")} to armoire ({skipped} skipped).{gsNote}"
            : $"Nothing new to store ({skipped} items already stored or ineligible).{gsNote}";

        Svc.Log.Information($"[ArmoireAutoFill] auto-store complete: {LastResultMessage}");

        _isStoring = false;
        OnStoreComplete?.Invoke();
    }
}
