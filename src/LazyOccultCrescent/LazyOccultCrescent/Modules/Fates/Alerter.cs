using System;
using System.Collections.Generic;
using LazyOccultCrescent.Enums;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace LazyOccultCrescent.Modules.Fates;

public class Alerter : IDisposable
{
    private readonly FatesModule module;

    private Dictionary<Demiatma, Func<bool>> DemiatmaAlerts
    {
        get => new()
        {
            [Demiatma.Azurite] = () => module.Config.AlertAzurite,
            [Demiatma.Verdigris] = () => module.Config.AlertVerdigris,
            [Demiatma.Malachite] = () => module.Config.AlertMalachite,
            [Demiatma.Realgar] = () => module.Config.AlertRealgar,
            [Demiatma.CaputMortuum] = () => module.Config.AlertCaputMortuum,
            [Demiatma.Orpiment] = () => module.Config.AlertOrpiment,
        };
    }

    public Alerter(FatesModule module)
    {
        this.module = module;

        this.module.tracker.OnFateSpawned += OnFateSpawned;
        this.module.tracker.OnFateDespawned += OnFateDespawned;
    }

    private void OnFateSpawned(Fate fate)
    {
        if (module.Config.LogSpawn)
        {
            Svc.Chat.Print($"{fate.Name} has Spawned");
        }

        if (!ShouldAlertForFate(fate))
        {
            return;
        }

        unsafe { UIGlobals.PlaySoundEffect(66); }
    }

    private void OnFateDespawned(Fate fate)
    {
        if (module.Config.LogSpawn)
        {
            Svc.Chat.Print($"{fate.Name} has Despawned");
        }

        if (!ShouldAlertForFate(fate))
        {
            return;
        }

        unsafe { UIGlobals.PlaySoundEffect(68); }
    }

    private bool ShouldAlertForFate(Fate fate)
    {
        if (module.Config.AlertAll)
        {
            return true;
        }

        if (fate.Data.Demiatma != null)
        {
            var demiatma = (Demiatma)fate.Data.Demiatma;
            if (DemiatmaAlerts.TryGetValue(demiatma, out var getter))
            {
                return getter();
            }
        }

        return false;
    }

    public void Dispose()
    {
        module.tracker.OnFateSpawned -= OnFateSpawned;
        module.tracker.OnFateDespawned -= OnFateDespawned;
    }
}
