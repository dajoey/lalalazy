using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Linq;

namespace AutoKupo;

internal sealed class CardHandler : IDisposable
{
    private readonly StateMachine stateMachine;
    private readonly Random rng = new();

    public CardHandler(StateMachine stateMachine)
    {
        this.stateMachine = stateMachine;
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "HWDLottery", OnPostSetup);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "HWDLottery", OnPostUpdate);
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "HWDLottery", OnPostSetup);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "HWDLottery", OnPostUpdate);
    }

    private unsafe void OnPostSetup(AddonEvent type, AddonArgs args)
    {
        if (stateMachine.CurrentState != KupoState.ProcessingCard)
            return;

        try
        {
            var atk = (AtkUnitBase*)args.Addon.Address;

            Plugin.Log.Debug("HWDLottery PostSetup — dumping AtkValues:");
            for (var i = 0; i < 40; i++)
                Plugin.Log.Debug($"  AtkValue[{i}] = UInt:{atk->AtkValues[i].UInt}, Int:{atk->AtkValues[i].Int}");

            var rightIndex = rng.Next(0, 3);
            Plugin.Log.Debug($"Selecting random right chest: index {rightIndex}");

            FireKupoCallback(atk, isLeft: true, index: 0);
            FireKupoCallback(atk, isLeft: false, index: rightIndex);

            stateMachine.CardsProcessed++;
            Plugin.ChatGui.Print($"[AutoKupo] Processing card #{stateMachine.CardsProcessed} (right pick: {rightIndex})");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"HWDLottery PostSetup error: {ex}");
        }
    }

    private static unsafe void FireKupoCallback(AtkUnitBase* atk, bool isLeft, int index)
    {
        var v = stackalloc AtkValue[3];
        v[0].Type = AtkValueType.Bool;
        v[0].Byte = (byte)(isLeft ? 1 : 0);
        v[1].Type = AtkValueType.Int;
        v[1].Int = index;
        v[2].Type = AtkValueType.Int;
        v[2].Int = 1;

        AtkUnitBase.MemberFunctionPointers.FireCallback(atk, 3, v, false);
    }

    private unsafe void OnPostUpdate(AddonEvent type, AddonArgs args)
    {
        if (stateMachine.CurrentState != KupoState.ProcessingCard)
            return;

        try
        {
            var atk = (AtkUnitBase*)args.Addon.Address;

            var allRevealed = Enumerable.Range(32, 5).All(i => atk->AtkValues[i].UInt != 0);

            if (allRevealed)
            {
                var closeButton = atk->UldManager.NodeList[7]->GetAsAtkComponentButton();
                if (closeButton != null && closeButton->IsEnabled)
                {
                    Plugin.Log.Debug("All slots revealed, clicking close button.");
                    var atkEvent = new AtkEvent();
                    atk->ReceiveEvent(AtkEventType.ButtonClick, 0, &atkEvent, null);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"HWDLottery PostUpdate error: {ex}");
        }
    }
}
