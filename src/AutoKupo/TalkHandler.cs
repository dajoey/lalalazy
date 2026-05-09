using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;

namespace AutoKupo;

internal sealed class TalkHandler : IDisposable
{
    private readonly StateMachine stateMachine;
    private DateTime lastClick = DateTime.MinValue;

    public TalkHandler(StateMachine stateMachine)
    {
        this.stateMachine = stateMachine;
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "Talk", OnPostUpdate);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreClose, "Talk", OnTalkClose);
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "Talk", OnPostUpdate);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreClose, "Talk", OnTalkClose);
    }

    private unsafe void OnPostUpdate(AddonEvent type, AddonArgs args)
    {
        if (stateMachine.CurrentState != KupoState.InDialog)
            return;

        var now = DateTime.UtcNow;
        if ((now - lastClick).TotalMilliseconds < stateMachine.Configuration.TalkClickDelayMs)
            return;

        try
        {
            var atk = (AtkUnitBase*)args.Addon.Address;
            lastClick = now;
            AtkUnitBase.MemberFunctionPointers.FireCallback(atk, 0, null, false);
            Plugin.Log.Information($"Talk: auto-advancing dialog (state={stateMachine.CurrentState})");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Talk PostUpdate error: {ex}");
        }
    }

    private void OnTalkClose(AddonEvent type, AddonArgs args)
    {
        if (stateMachine.CurrentState == KupoState.InDialog)
        {
            Plugin.Log.Information("Talk closed — signaling state machine.");
            stateMachine.OnTalkClosed();
        }
    }
}
