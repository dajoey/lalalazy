using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;

namespace AutoKupo;

internal sealed class SelectStringHandler : IDisposable
{
    private readonly StateMachine stateMachine;

    public SelectStringHandler(StateMachine stateMachine)
    {
        this.stateMachine = stateMachine;
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectString", OnPostSetup);
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectString", OnPostSetup);
    }

    private unsafe void OnPostSetup(AddonEvent type, AddonArgs args)
    {
        if (stateMachine.CurrentState != KupoState.SelectingMenu)
            return;

        try
        {
            var atk = (AtkUnitBase*)args.Addon.Address;
            Plugin.Log.Debug($"SelectString opened (visible entries: {atk->UldManager.NodeListCount})");

            AtkUnitBase.MemberFunctionPointers.FireCallback(atk, 0, null, false);
            Plugin.Log.Debug("SelectString: fired callback to select first entry");

            stateMachine.OnMenuSelected();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"SelectString error: {ex}");
        }
    }
}
