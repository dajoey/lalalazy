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
            if (atk->IsVisible)
            {
                lastClick = now;
                AtkUnitBase.MemberFunctionPointers.FireCallback(atk, 0, null, false);
                Plugin.Log.Debug("Talk: auto-advancing dialog");
            }
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
            Plugin.Log.Debug("Talk closed — signaling state machine.");
            stateMachine.OnTalkClosed();
        }
    }
}
