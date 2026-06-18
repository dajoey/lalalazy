using System.Collections.Generic;
using System.Linq;
using ArmoireAutoFill.Data;
using ArmoireAutoFill.Models;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
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
    }

    public void Dispose()
    {
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, CabinetAddonName, OnCabinetOpened);
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
        if (Plugin.Configuration.AutoStoreOnOpen)
            StoreAll();
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

        var containers = new[]
        {
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory1,
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory2,
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory3,
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory4,
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
        };

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

        foreach (var itemId in candidates)
        {
            // Skip items already in the armoire.
            if (uiState->Cabinet.IsItemInCabinet(_itemToCabinetId.GetValueOrDefault(itemId, 0u)))
            {
                skipped++;
                continue;
            }

            if (!_itemToCabinetId.TryGetValue(itemId, out var cabinetId))
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
        LastResultMessage = stored > 0
            ? $"Stored {stored} item{(stored != 1 ? "s" : "")} to armoire ({skipped} skipped)."
            : $"Nothing new to store ({skipped} items already stored or ineligible).";

        Svc.Log.Information($"[ArmoireAutoFill] auto-store complete: {LastResultMessage}");

        _isStoring = false;
        OnStoreComplete?.Invoke();
    }
}
