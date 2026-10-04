using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lalalazy.HookGuard;

namespace KamiToolKit.Classes.Controllers;

/// <summary>
/// Only one or the other field will be valid, be sure to check for null.
/// </summary>
public unsafe class ListItemData {
    public AtkComponentListItemPopulator.ListItemInfo* ItemInfo { get; set; }
    public AtkComponentListItemRenderer* ItemRenderer { get; set; }
}

public unsafe class NativeListController(string addonName) : IDisposable {

    public required ShouldModifyElementHandler ShouldModifyElement { get; init; }
    public required UpdateElementHandler UpdateElement { get; init; }
    public required ResetElementHandler ResetElement { get; init; }
    public required GetPopulatorNodeHandler GetPopulatorNode { get; init; }

    private Hook<AtkComponentListItemPopulator.PopulateDelegate>? onListPopulate;
    private Hook<AtkComponentListItemPopulator.PopulateWithRendererDelegate>? onRendererPopulate;

    // Populate detours of this controller running right now; teardown drains this before pulling the
    // hooks (a detour that outlives its hook is the 2026-08-31 / 2026-10-03 game crash family).
    private readonly HookInFlight InFlight = new();

    public readonly List<uint> ModifiedIndexes = [];
    
    public event Action? OnClose {
        add => OnInnerClose += value;
        remove => throw new Exception("Do not remove events, on dispose addon state will be managed properly.");
    }
    
    public event Action? OnOpen {
        add => OnInnerOpen += value;
        remove => throw new Exception("Do not remove events, on dispose addon state will be managed properly.");
    }

    public void Enable() {
        DalamudInterface.Instance.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, addonName, OnAddonSetup);
        DalamudInterface.Instance.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, addonName, OnAddonFinalize);

        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName(addonName);
        if (addon is not null) {
            Log.Warning("Caution: ListController was loaded after list was initialized, data may be stale.");
            LoadPopulators(addon);
        }
    }

    public void Disable() => Dispose();

    public void Dispose() {
        DalamudInterface.Instance.AddonLifecycle.UnregisterListener(OnAddonSetup, OnAddonFinalize);
        DisposeHooks();
    }

    /// <summary>
    /// Disables both populate hooks, waits for the detours still inside them, then disposes and drops
    /// them. Free of Dalamud services on purpose: it is exactly the draining teardown of the
    /// 2026-10-03 hot-reload crash family, callable when the addon finalizes or the plugin unloads.
    /// </summary>
    private void DisposeHooks() {
        onListPopulate?.Disable();
        onRendererPopulate?.Disable();
        InFlight.Drain("KamiToolKit.NativeListController", static m => Log.Warning(m));
        onListPopulate?.Dispose();
        onListPopulate = null;
        onRendererPopulate?.Dispose();
        onRendererPopulate = null;
    }

    private void OnAddonSetup(AddonEvent type, AddonArgs args)
        => LoadPopulators((AtkUnitBase*)args.Addon.Address);

    private void OnAddonFinalize(AddonEvent type, AddonArgs args) {
        DisposeHooks();
        
        ModifiedIndexes.Clear();
        
        OnInnerClose?.Invoke();
    }
    
    private void LoadPopulators(AtkUnitBase* addon) {
        var populateMethod = GetPopulatorNode(addon)->Populator;

        if (populateMethod.Populate is not null) {
            onListPopulate = DalamudInterface.Instance.GameInteropProvider.HookFromAddress<AtkComponentListItemPopulator.PopulateDelegate>((nint)populateMethod.Populate, OnPopulateDetour);
            onListPopulate?.Enable();
        }

        if (populateMethod.PopulateWithRenderer is not null) {
            onRendererPopulate = DalamudInterface.Instance.GameInteropProvider.HookFromAddress<AtkComponentListItemPopulator.PopulateWithRendererDelegate>((nint)populateMethod.PopulateWithRenderer, OnRendererPopulateDetour);
            onRendererPopulate?.Enable();
        }

        OnInnerOpen?.Invoke();
    }

    private void OnPopulateDetour(AtkEventListener* eventListener, AtkComponentListItemPopulator.ListItemInfo* itemInfo, AtkResNode** nodeList) {
        // Count this detour so teardown can wait for it, and never run the payload against a
        // torn-down hook: an exception escaping here takes the game down (2026-10-03 crash family).
        using var inFlight = InFlight.Enter();
        var hook = onListPopulate;
        if (hook == null || hook.IsDisposed) {
            return;
        }

        var unitBase = (AtkUnitBase*)eventListener;
        try {
            var listItemData = new ListItemData {
                ItemInfo = itemInfo,
            };
            
            var shouldModifyElement = ShouldModifyElement(unitBase, listItemData, nodeList);

            if (!shouldModifyElement) {
                if (ModifiedIndexes.Contains(itemInfo->ListItem->Renderer->OwnerNode->NodeId)) {
                    ResetElement.Invoke(unitBase, listItemData, nodeList);
                    ModifiedIndexes.Remove(itemInfo->ListItem->Renderer->OwnerNode->NodeId);
                }
            }
            
            hook.Original(eventListener, itemInfo, nodeList);

            if (shouldModifyElement) {
                UpdateElement.Invoke(unitBase, listItemData, nodeList);
                ModifiedIndexes.Add(itemInfo->ListItem->Renderer->OwnerNode->NodeId);
            }
        }
        catch (Exception e) {
            Log.Exception(e);
        }
    }
    
    private void OnRendererPopulateDetour(AtkEventListener* eventListener, int listItemIndex, AtkResNode** nodeList, AtkComponentListItemRenderer* listItemRenderer) {
        // Count this detour so teardown can wait for it, and never run the payload against a
        // torn-down hook: an exception escaping here takes the game down (2026-10-03 crash family).
        using var inFlight = InFlight.Enter();
        var hook = onRendererPopulate;
        if (hook == null || hook.IsDisposed) {
            return;
        }

        var unitBase = (AtkUnitBase*)eventListener;
        try {
            var listItemData = new ListItemData {
                ItemRenderer = listItemRenderer,
            };
            
            var shouldModifyElement = ShouldModifyElement(unitBase, listItemData, nodeList);

            if (!shouldModifyElement) {
                if (ModifiedIndexes.Contains(listItemRenderer->OwnerNode->NodeId)) {
                    ResetElement.Invoke(unitBase, listItemData, nodeList);
                    ModifiedIndexes.Remove(listItemRenderer->OwnerNode->NodeId);
                }
            }
            
            hook.Original(eventListener, listItemIndex, nodeList, listItemRenderer);

            if (shouldModifyElement) {
                UpdateElement.Invoke(unitBase, listItemData, nodeList);
                ModifiedIndexes.Add(listItemRenderer->OwnerNode->NodeId);
            }
        }
        catch (Exception e) {
            Log.Exception(e);
        }
    }

    public delegate bool ShouldModifyElementHandler(AtkUnitBase* unitBase, ListItemData listItemInfo, AtkResNode** nodeList);
    public delegate AtkComponentListItemRenderer* GetPopulatorNodeHandler(AtkUnitBase* addon);
    public delegate void UpdateElementHandler(AtkUnitBase* unitBase, ListItemData listItemInfo, AtkResNode** nodeList);
    public delegate void ResetElementHandler(AtkUnitBase* unitBase, ListItemData listItemInfo, AtkResNode** nodeList);

    private Action? OnInnerClose { get; set; }
    private Action? OnInnerOpen { get; set; }
}
