using System;
using System.Collections.Generic;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Enums;
using ECommons.DalamudServices;
using Ocelot.IPC;
using Ocelot.Modules;
using GluttonyIPC = LazyOccultCrescent.IPC.GluttonyCombo;

namespace LazyOccultCrescent.Modules.MobFarmer;

// Rotation provider backed by GluttonyCombo rather than upstream Wrath Combo.
//
// Why this exists: as of 2026-08-01 upstream Wrath had shipped no support for the
// eight phantom jobs added in 7.55, so a Wrath-driven farm loop is dead weight in
// North Horn. GluttonyCombo implements all 24 phantom jobs as Phantom_<Job> combo
// options, so this provider maps the whole roster instead of the single Cannoneer
// entry upstream BOCCHI shipped.
public class Gluttony : IRotationPlugin
{
    private readonly GluttonyIPC gluttony;

    private readonly Guid lease;

    // Preset names are the CustomComboPreset identifiers in GluttonyCombo. All 24
    // exist; the 7.55 eight live in the reserved 110090-110136 range.
    private readonly static Dictionary<JobId, string> Options = new()
    {
        { JobId.Freelancer, "Phantom_Freelancer" },
        { JobId.Knight, "Phantom_Knight" },
        { JobId.Berserker, "Phantom_Berserker" },
        { JobId.Monk, "Phantom_Monk" },
        { JobId.Ranger, "Phantom_Ranger" },
        { JobId.Samurai, "Phantom_Samurai" },
        { JobId.Bard, "Phantom_Bard" },
        { JobId.Geomancer, "Phantom_Geomancer" },
        { JobId.TimeMage, "Phantom_TimeMage" },
        { JobId.Cannoneer, "Phantom_Cannoneer" },
        { JobId.Chemist, "Phantom_Chemist" },
        { JobId.Oracle, "Phantom_Oracle" },
        { JobId.Thief, "Phantom_Thief" },
        { JobId.MysticKnight, "Phantom_MysticKnight" },
        { JobId.Gladiator, "Phantom_Gladiator" },
        { JobId.Dancer, "Phantom_Dancer" },

        // 7.55
        { JobId.Ninja, "Phantom_Ninja" },
        { JobId.WhiteMage, "Phantom_WhiteMage" },
        { JobId.BlackMage, "Phantom_BlackMage" },
        { JobId.Dragoon, "Phantom_Dragoon" },
        { JobId.Summoner, "Phantom_Summoner" },
        { JobId.BlueMage, "Phantom_BlueMage" },
        { JobId.RedMage, "Phantom_RedMage" },
        { JobId.Necromancer, "Phantom_Necromancer" },
    };

    public Gluttony(IModule module)
    {
        gluttony = module.GetIPCSubscriber<GluttonyIPC>();

        var acquired = gluttony.RegisterForLease(Svc.PluginInterface.InternalName, module.GetType().FullName!);
        if (acquired == null)
        {
            throw new Exception("Unable to acquire a GluttonyCombo lease");
        }

        lease = (Guid)acquired;
    }

    public void PhantomJobOn(Job? job = null)
    {
        SetState(job, true);
    }

    public void PhantomJobOff(Job? job = null)
    {
        SetState(job, false);
    }

    private void SetState(Job? job, bool enabled)
    {
        job ??= Job.Current;

        if (!Options.TryGetValue(job.id, out var option))
        {
            return;
        }

        var result = gluttony.SetComboOptionState(lease, option, enabled);
        if (result != WrathCombo.SetResult.Okay && result != WrathCombo.SetResult.OkayWorking)
        {
            // Worth surfacing: a revoked or blacklisted lease means the farm loop
            // is silently running with no rotation behind it.
            Svc.Log.Warning($"[Gluttony] {option} -> {enabled} returned {result}");
        }
    }

    void IDisposable.Dispose()
    {
        gluttony.ReleaseControl(lease);
    }
}
