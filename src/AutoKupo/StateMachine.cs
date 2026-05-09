using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;
using System.Numerics;

namespace AutoKupo;

internal sealed class StateMachine : IDisposable
{
    public Configuration Configuration { get; }
    public KupoState CurrentState { get; private set; } = KupoState.Idle;
    public int CardsProcessed { get; set; }

    private readonly LizbethDetector detector = new();
    private readonly CardHandler cardHandler;
    private readonly TalkHandler talkHandler;
    private readonly SelectStringHandler selectStringHandler;

    private DateTime lastTargetAttempt = DateTime.MinValue;
    private DateTime stateEnteredAt = DateTime.UtcNow;
    private ulong targetObjectId;
    private bool started;
    private bool dialogActive;

    public StateMachine(Configuration config)
    {
        Configuration = config;
        cardHandler = new CardHandler(this);
        talkHandler = new TalkHandler(this);
        selectStringHandler = new SelectStringHandler(this);

        Plugin.Framework.Update += OnFrameworkUpdate;
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "HWDLottery", OnCardOpened);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "HWDLottery", OnCardClosed);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectString", OnSelectStringOpened);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "Talk", OnTalkOpened);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreClose, "Talk", OnTalkClosed);
    }

    public void Start()
    {
        started = true;
        CardsProcessed = 0;
        dialogActive = false;
        TransitionTo(KupoState.ScanningForLizbeth);
    }

    public void Stop()
    {
        started = false;
        dialogActive = false;
        TransitionTo(KupoState.Idle);
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "HWDLottery", OnCardOpened);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "HWDLottery", OnCardClosed);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectString", OnSelectStringOpened);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "Talk", OnTalkOpened);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreClose, "Talk", OnTalkClosed);

        cardHandler.Dispose();
        talkHandler.Dispose();
        selectStringHandler.Dispose();
    }

    private void TransitionTo(KupoState newState)
    {
        var old = CurrentState;
        CurrentState = newState;
        stateEnteredAt = DateTime.UtcNow;
        Plugin.Log.Debug($"State: {old} -> {newState}");
    }

    public void OnTalkOpened(AddonEvent type, AddonArgs args)
    {
        if (!started) return;
        dialogActive = true;

        if (CurrentState != KupoState.ProcessingCard && CurrentState != KupoState.SelectingMenu)
        {
            TransitionTo(KupoState.InDialog);
        }
    }

    public void OnTalkClosed(AddonEvent type, AddonArgs args)
    {
        if (!started) return;
        Plugin.Log.Debug("Talk addon closed by game.");
    }

    public void OnSelectStringOpened(AddonEvent type, AddonArgs args)
    {
        if (!started) return;
        dialogActive = true;

        if (CurrentState != KupoState.ProcessingCard)
        {
            TransitionTo(KupoState.SelectingMenu);
        }
    }

    private void OnCardOpened(AddonEvent type, AddonArgs args)
    {
        if (!started) return;
        TransitionTo(KupoState.ProcessingCard);
    }

    private void OnCardClosed(AddonEvent type, AddonArgs args)
    {
        if (!started) return;
        Plugin.Log.Debug("HWDLottery closed.");
    }

    public void OnMenuSelected()
    {
        Plugin.Log.Debug("Menu entry selected, waiting for card.");
    }

    public void OnTalkClosed()
    {
        dialogActive = false;
    }

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        if (!started || !Configuration.Enabled)
            return;

        if (CardsProcessed >= Configuration.MaxIterations)
        {
            Plugin.ChatGui.Print($"[AutoKupo] Max iterations ({Configuration.MaxIterations}) reached. Stopped.");
            Stop();
            Configuration.Enabled = false;
            Configuration.Save();
            return;
        }

        if (Plugin.Condition[ConditionFlag.InCombat] || Plugin.Condition[ConditionFlag.Mounted])
            return;

        var timeInState = (DateTime.UtcNow - stateEnteredAt).TotalMilliseconds;

        switch (CurrentState)
        {
            case KupoState.ScanningForLizbeth:
                HandleScanning();
                break;
            case KupoState.TargetingLizbeth:
                HandleTargeting();
                break;
            case KupoState.TryingToInteract:
                HandleInteract();
                break;
            case KupoState.InDialog:
                HandleInDialog(timeInState);
                break;
            case KupoState.SelectingMenu:
                HandleInMenu(timeInState);
                break;
            case KupoState.ProcessingCard:
                HandleCardActive(timeInState);
                break;
            case KupoState.Done:
                HandleDone();
                break;
            case KupoState.Error:
                HandleError(timeInState);
                break;
        }
    }

    private void HandleScanning()
    {
        if (detector.TryFindLizbeth(out var distance, out var objId))
        {
            if (distance <= Configuration.MaxDistance)
            {
                targetObjectId = objId;
                Plugin.ChatGui.Print($"[AutoKupo] Lizbeth at {distance:F1} yalms. Starting...");
                TransitionTo(KupoState.TargetingLizbeth);
            }
        }
    }

    private void HandleTargeting()
    {
        if (!Configuration.AutoInteract)
        {
            Plugin.ChatGui.Print("[AutoKupo] Auto-interact disabled. Target Lizbeth manually.");
            TransitionTo(KupoState.Done);
            return;
        }

        var targetObj = FindObjectById(targetObjectId);
        if (targetObj == null)
        {
            Plugin.Log.Debug("Target invalid, re-scanning.");
            TransitionTo(KupoState.ScanningForLizbeth);
            return;
        }

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null) return;

        var dist = Vector3.Distance(player.Position, targetObj.Position);
        if (dist > Configuration.MaxDistance)
        {
            TransitionTo(KupoState.ScanningForLizbeth);
            return;
        }

        if ((DateTime.UtcNow - lastTargetAttempt).TotalSeconds < 1.5)
            return;

        lastTargetAttempt = DateTime.UtcNow;

        Plugin.TargetManager.Target = targetObj;
        Plugin.Log.Debug($"Target set to Lizbeth (ID: {targetObjectId})");
        TransitionTo(KupoState.TryingToInteract);
    }

    private unsafe void HandleInteract()
    {
        if ((DateTime.UtcNow - stateEnteredAt).TotalMilliseconds < 500)
            return;

        try
        {
            var obj = FindObjectById(targetObjectId);
            if (obj != null)
            {
                var ts = TargetSystem.Instance();
                var ptr = (GameObject*)obj.Address;
                ts->OpenObjectInteraction(ptr);
                Plugin.Log.Debug($"Interacted with Lizbeth via OpenObjectInteraction");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Interact failed: {ex}");
            Plugin.ChatGui.PrintError("[AutoKupo] Auto-interact failed. Try manually.");
            TransitionTo(KupoState.Done);
            return;
        }

        TransitionTo(KupoState.ScanningForLizbeth);
    }

    private void HandleInDialog(double timeInState)
    {
        if (CardsProcessed > 0 && !dialogActive && timeInState > 4_000)
        {
            TransitionTo(KupoState.Done);
            return;
        }

        if (timeInState > 60_000)
        {
            Plugin.Log.Warning("Dialog timeout. Stopping.");
            TransitionTo(KupoState.Error);
        }
    }

    private void HandleInMenu(double timeInState)
    {
        if (timeInState > 15_000)
        {
            Plugin.Log.Warning("Menu timeout. Stopping.");
            TransitionTo(KupoState.Error);
        }
    }

    private void HandleCardActive(double timeInState)
    {
        if (timeInState > 30_000)
        {
            Plugin.Log.Warning("Card timeout. Stopping.");
            TransitionTo(KupoState.Error);
        }
    }

    private void HandleDone()
    {
        Plugin.ChatGui.Print($"[AutoKupo] All done! Processed {CardsProcessed} cards.");
        Stop();
        Configuration.Enabled = false;
        Configuration.Save();
    }

    private void HandleError(double timeInState)
    {
        if (timeInState > 2_000)
        {
            Plugin.ChatGui.PrintError($"[AutoKupo] Error state. Stopped after {CardsProcessed} cards.");
            Stop();
            Configuration.Enabled = false;
            Configuration.Save();
        }
    }

    private static Dalamud.Game.ClientState.Objects.Types.IGameObject? FindObjectById(ulong id)
    {
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj != null && obj.IsValid() && obj.GameObjectId == id)
                return obj;
        }
        return null;
    }
}
