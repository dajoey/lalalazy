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
        {
            Plugin.Log.Information($"SelectString opened but state is {stateMachine.CurrentState}, ignoring");
            return;
        }

        try
        {
            var atk = (AtkUnitBase*)args.Addon.Address;
            Plugin.Log.Information($"SelectString opened — selecting first entry");

            var v = stackalloc AtkValue[1];
            v[0].Type = AtkValueType.Int;
            v[0].Int = 0;
            AtkUnitBase.MemberFunctionPointers.FireCallback(atk, 0, v, false);

            stateMachine.OnMenuSelected();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"SelectString error: {ex}");
        }
    }
}
