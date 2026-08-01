using System;
using System.Collections.Generic;
using System.Linq;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Modules.Buff.Chains;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using Ocelot.Chain;

namespace LazyOccultCrescent.Modules.Buff;

public class BuffManager
{
    private bool applyBuffsOnNextTick = false;

    // The job the player was on when a buff run started. Buffing has to hop
    // through several phantom jobs to cast each buff, and the restore at the end
    // of AllBuffsChain is just another chain link - so anything that aborts the
    // sequence skips it and strands the player on a job they did not pick. This
    // is the independent safety net for that.
    private Job? restoreTo;

    private int restoreAttempts;

    private const int MaxRestoreAttempts = 5;

    public void QueueBuffs()
    {
        applyBuffsOnNextTick = true;
    }

    public bool IsQueued()
    {
        return applyBuffsOnNextTick;
    }

    public Job? PendingRestore
    {
        get => restoreTo;
    }

    private int lowestTimer = int.MaxValue;

    public void Update(BuffModule module)
    {
        if (applyBuffsOnNextTick)
        {
            applyBuffsOnNextTick = false;
            ApplyBuffs(module);
        }

        RestoreJobIfStranded();

        if (EzThrottler.Throttle("BuffManager.Tick.GetLowestBuffTimer", 1000))
        {
            lowestTimer = GetLowestBuffTimer(module);
        }
    }

    public void ApplyBuffs(BuffModule module)
    {
        var manager = ChainManager.Get("LOC##BuffManager");
        if (manager.IsRunning)
        {
            return;
        }

        // Captured here rather than inside the chain: if a previous run was
        // stranded, Job.Current is already the wrong answer and capturing it
        // again would make the wrong job "correct".
        restoreTo ??= Job.Current;
        restoreAttempts = 0;

        manager.Submit(CreateSequence(module));
    }

    // ReturnChain builds a buff sequence directly rather than going through
    // ApplyBuffs(), which meant it ran with no restore tracking at all. Both
    // paths now come through here so the watchdog always knows what job to put
    // the player back on.
    public AllBuffsChain CreateSequence(BuffModule module)
    {
        restoreTo ??= Job.Current;
        restoreAttempts = 0;

        return new AllBuffsChain(module, restoreTo);
    }

    private void RestoreJobIfStranded()
    {
        if (restoreTo == null)
        {
            return;
        }

        var manager = ChainManager.Get("LOC##BuffManager");
        if (manager.IsRunning)
        {
            return;
        }

        if (Job.Current.id == restoreTo.id)
        {
            restoreTo = null;
            restoreAttempts = 0;
            return;
        }

        // A job change is rejected in combat; retrying there just burns attempts.
        if (Svc.Condition[ConditionFlag.InCombat] || Svc.Objects.LocalPlayer == null)
        {
            return;
        }

        if (restoreAttempts >= MaxRestoreAttempts)
        {
            // Give up rather than fight indefinitely: past this point the likeliest
            // explanation is that the player changed job deliberately.
            Svc.Log.Warning($"[Buff] could not restore {restoreTo.id} after {restoreAttempts} attempts - leaving job as-is");
            restoreTo = null;
            restoreAttempts = 0;
            return;
        }

        if (!EzThrottler.Throttle("BuffManager.RestoreJob", 3000))
        {
            return;
        }

        restoreAttempts++;
        Svc.Log.Information($"[Buff] buff sequence left job as {Job.Current.id}, restoring to {restoreTo.id} (attempt {restoreAttempts})");
        manager.Submit(restoreTo.ChangeToChain);
    }

    // The player changing job by hand is authoritative: stop trying to undo it.
    public void ForgetRestore()
    {
        restoreTo = null;
        restoreAttempts = 0;
    }

    private int GetLowestBuffTimer(BuffModule module)
    {
        List<uint> buffs = [];

        if (module.Config.ApplyEnduringFortitude)
        {
            buffs.Add((uint)PlayerStatus.EnduringFortitude);
        }

        if (module.Config.ApplyFleetfooted)
        {
            buffs.Add((uint)PlayerStatus.Fleetfooted);
        }

        if (module.Config.ApplyRomeosBallad)
        {
            buffs.Add((uint)PlayerStatus.RomeosBallad);
        }

        if (module.Config.ApplyQuickerStep)
        {
            buffs.Add((uint)PlayerStatus.QuickerStep);
        }
        if (module.Config.UseInquiringMind && !module.Config.ApplyQuickerStep)
        {
            buffs.Add((uint)PlayerStatus.QuickerStep);
        }

        var statuses = Player.Status.Where(s => buffs.Contains(s.StatusId)).ToList();
        return statuses.Count == 0 ? 0 : statuses.Select(status => (int)status.RemainingTime).Min();
    }

    public bool ShouldRefresh(BuffModule module)
    {
        if (!module.IsEnabled)
        {
            return false;
        }

        return lowestTimer <= module.Config.ReapplyThreshold * 60;
    }
}
