using System;
using System.Collections.Generic;
using System.Linq;
using ECommons.DalamudServices;
using Ocelot.Modules;

namespace LazyOccultCrescent.Modules.Fates;

public class FateTracker
{
    public readonly Dictionary<uint, Fate> Fates = [];

    public event Action<Fate>? OnFateSpawned;

    public event Action<Fate>? OnFateDespawned;


    public void Update(UpdateContext context)
    {
        var currentFates = Svc.Fates.ToDictionary(f => (uint)f.FateId, f => f);

        foreach (var (id, data) in currentFates)
        {
            if (Fates.TryGetValue(id, out _))
            {
                // Already tracked. Fate wraps IFate by reference so the underlying
                // data stays live; replacing the wrapper would throw away the
                // progress samples the ETA is computed from.
                continue;
            }

            var fate = new Fate(data);
            Fates[id] = fate;
            OnFateSpawned?.Invoke(fate);
        }

        var despawned = Fates.Keys.Except(currentFates.Keys).ToList();
        foreach (var id in despawned)
        {
            OnFateDespawned?.Invoke(Fates[id]);
            Fates.Remove(id);
        }

        foreach (var fate in Fates.Values)
        {
            fate.Update(context);
        }
    }
}
