using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using GluttonyCombo.CustomComboNS.Functions;

namespace GluttonyCombo.Combos.PvE;

internal static class OccultCrystalBuffs
{
    private enum SequenceSubState
    {
        SwitchJob,
        WaitForJobChange,
        CastBuff,
        WaitDelay
    }

    // Phantom job abilities are NOT castable via ActionType.Action + the 41xxx Action-sheet IDs —
    // the client silently rejects those casts (this was the v1.0.4.86-95 "waits but never casts"
    // bug). The phantom hotbar casts them via ActionType.GeneralAction with per-slot GeneralAction
    // row IDs (31-34), exactly like pressing the buttons manually.
    // Reference: BOCCHI Buff module (github.com/OhKannaDuh/BOCCHI), verified working in-game.
    private sealed record BuffJob(int JobId, uint GeneralActionId, uint BuffStatusId, uint PhantomJobStatusId, string Label);

    private static readonly List<BuffJob> JobBuffMap =
    [
        new((int)OccultCrescent.JobIDs.Knight, 32, 4233, 4358, "Pray (Enduring Fortitude)"),   // Knight slot 2
        new((int)OccultCrescent.JobIDs.Monk,   33, 4239, 4360, "Counterstance (Fleetfooted)"), // Monk slot 3
        new((int)OccultCrescent.JobIDs.Bard,   32, 4244, 4363, "Romeo's Ballad"),              // Bard slot 2
        new((int)OccultCrescent.JobIDs.Dancer, 32, 4799, 4805, "Quickstep (Quicker Step)"),    // Dancer slot 2
    ];

    // Buffs last 30 minutes (1800s); a freshly applied one shows >= ~1780s remaining.
    private const float FreshBuffThresholdSeconds = 1780f;

    private static bool isRunning = false;
    private static int initialJob = -1;
    private static int currentJobIndex = 0;
    private static List<BuffJob> buffingJobs = [];
    private static IGameObject? targetCrystal = null;
    private static DateTime stepStartTime = DateTime.MinValue;
    private static DateTime sequenceStartTime = DateTime.MinValue;
    private static DateTime lastCastTime = DateTime.MinValue;
    private static SequenceSubState subState = SequenceSubState.SwitchJob;

    // Helper to check if local player has a given status
    private static bool HasStatus(uint statusId)
    {
        return Player.Object != null && Player.Object.StatusList.Any(s => s.StatusId == statusId);
    }

    // Remaining seconds on a status, or null if not present
    private static float? GetStatusRemaining(uint statusId)
    {
        if (Player.Object == null) return null;
        var status = Player.Object.StatusList.FirstOrDefault(s => s.StatusId == statusId);
        return status?.RemainingTime;
    }

    public static void StartSequence()
    {
        if (isRunning)
        {
            DuoLog.Warning("Crystal buff sequence is already running.");
            return;
        }

        if (!OccultCrescent.IsInOccult)
        {
            DuoLog.Error("You must be inside Occult Crescent (North or South Horn) to use this command.");
            return;
        }

        if (CustomComboFunctions.InCombat())
        {
            DuoLog.Error("Cannot start crystal buff sequence while in combat.");
            return;
        }

        if (Player.Object == null)
        {
            DuoLog.Error("Player state unavailable.");
            return;
        }

        Vector3 playerPos = Player.Object.Position;

        // If player already targets a crystal/node object nearby, use it directly
        if (Svc.Targets.Target != null && Vector3.Distance(playerPos, Svc.Targets.Target.Position) <= 10.0f)
        {
            targetCrystal = Svc.Targets.Target;
        }
        else
        {
            // Search for nearby crystal / node
            targetCrystal = Svc.Objects.FirstOrDefault(o =>
                o != null &&
                Vector3.Distance(playerPos, o.Position) <= 10.0f &&
                (o.Name.TextValue.Contains("Crystal", StringComparison.OrdinalIgnoreCase) ||
                 o.Name.TextValue.Contains("Node", StringComparison.OrdinalIgnoreCase) ||
                 o.Name.TextValue.Contains("Knowledge", StringComparison.OrdinalIgnoreCase) ||
                 o.Name.TextValue.Contains("Aetherial", StringComparison.OrdinalIgnoreCase) ||
                 o.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj));
        }

        if (targetCrystal == null)
        {
            DuoLog.Error("No Knowledge Crystal or Aetherial Node found within 10 meters.");
            return;
        }

        unsafe
        {
            var inst = PublicContentOccultCrescent.GetInstance();
            if (inst == null)
            {
                DuoLog.Error("Unable to access Occult Crescent state.");
                return;
            }
            initialJob = inst->State.CurrentSupportJob;
        }

        // Build ordered list of active buffing jobs
        buffingJobs.Clear();
        foreach (var buffJob in JobBuffMap)
        {
            if (Enum.IsDefined(typeof(OccultCrescent.JobIDs), buffJob.JobId) &&
                ((OccultCrescent.JobIDs)buffJob.JobId).IsActive())
            {
                buffingJobs.Add(buffJob);
            }
        }

        if (buffingJobs.Count == 0)
        {
            DuoLog.Error("No active buffing Phantom Jobs found to cycle.");
            return;
        }

        isRunning = true;
        currentJobIndex = 0;
        subState = SequenceSubState.SwitchJob;
        sequenceStartTime = DateTime.Now;
        stepStartTime = DateTime.Now;
        lastCastTime = DateTime.MinValue;

        // Target crystal
        Svc.Targets.Target = targetCrystal;

        DuoLog.Information($"Starting Phantom Job crystal buff cycle across {buffingJobs.Count} jobs...");

        Svc.Framework.Update += OnFrameworkUpdate;
    }

    public static void StopSequence(string reason = "")
    {
        if (!isRunning) return;

        Svc.Framework.Update -= OnFrameworkUpdate;
        isRunning = false;

        if (!string.IsNullOrEmpty(reason))
        {
            DuoLog.Warning($"Crystal buff sequence stopped: {reason}");
        }
    }

    private static unsafe void OnFrameworkUpdate(object framework)
    {
        if (!isRunning) return;

        // Timeout guard: 120 seconds max (worst case ~17s per job)
        if ((DateTime.Now - sequenceStartTime).TotalSeconds > 120)
        {
            StopSequence("Timeout exceeded (120s).");
            return;
        }

        if (Player.Object == null || CustomComboFunctions.InCombat() || Player.Object.IsDead)
        {
            StopSequence("Entered combat, player died, or player state lost.");
            return;
        }

        // Distance guard
        if (targetCrystal != null && Vector3.Distance(Player.Object.Position, targetCrystal.Position) > 12.0f)
        {
            StopSequence("Moved too far away from the crystal.");
            return;
        }

        var inst = PublicContentOccultCrescent.GetInstance();
        if (inst == null)
        {
            StopSequence("Occult Crescent instance state lost.");
            return;
        }

        if (currentJobIndex >= buffingJobs.Count)
        {
            // Sequence complete: restore initial job natively
            if (initialJob >= 0 && inst->State.CurrentSupportJob != (byte)initialJob)
            {
                PublicContentOccultCrescent.ChangeSupportJob((byte)initialJob);
            }

            StopSequence("");
            DuoLog.Information("Phantom Job crystal buff cycle complete! Restored original Phantom Job.");
            return;
        }

        var buffJob = buffingJobs[currentJobIndex];
        int jobId = buffJob.JobId;

        switch (subState)
        {
            case SequenceSubState.SwitchJob:
                // Native C++ function call to change support job at crystal
                if (inst->State.CurrentSupportJob != (byte)jobId)
                {
                    PublicContentOccultCrescent.ChangeSupportJob((byte)jobId);
                    DuoLog.Information($"Switching to Phantom {(OccultCrescent.JobIDs)jobId} for {buffJob.Label}...");
                }

                subState = SequenceSubState.WaitForJobChange;
                stepStartTime = DateTime.Now;
                break;

            case SequenceSubState.WaitForJobChange:
                // Confirm via the Phantom Job status (e.g. Phantom Monk) — this is what the
                // server applies once the change fully lands; more reliable than the state byte.
                if (HasStatus(buffJob.PhantomJobStatusId) || inst->State.CurrentSupportJob == (byte)jobId)
                {
                    // 400ms post-change settle so the phantom hotbar/equipped actions initialize
                    if ((DateTime.Now - stepStartTime).TotalMilliseconds >= 400)
                    {
                        subState = SequenceSubState.CastBuff;
                        stepStartTime = DateTime.Now;
                        lastCastTime = DateTime.MinValue;
                    }
                    return;
                }

                // Wait up to 5.0s for server job change confirmation
                if ((DateTime.Now - stepStartTime).TotalMilliseconds > 5000)
                {
                    DuoLog.Warning($"Job change to Phantom {(OccultCrescent.JobIDs)jobId} did not confirm in time. Skipping to next job.");
                    currentJobIndex++;
                    subState = SequenceSubState.SwitchJob;
                    stepStartTime = DateTime.Now;
                }
                break;

            case SequenceSubState.CastBuff:
                {
                    // Done when the buff is present AND freshly applied (>= ~1780s of 1800s left)
                    var remaining = GetStatusRemaining(buffJob.BuffStatusId);
                    if (remaining.HasValue && remaining.Value >= FreshBuffThresholdSeconds)
                    {
                        DuoLog.Information($"{buffJob.Label} applied ({remaining.Value:F0}s).");
                        subState = SequenceSubState.WaitDelay;
                        stepStartTime = DateTime.Now;
                        return;
                    }

                    // Cast via GeneralAction slot when off recast, retrying every 500ms
                    var am = ActionManager.Instance();
                    if (am != null && (DateTime.Now - lastCastTime).TotalMilliseconds >= 500)
                    {
                        float recast = am->GetRecastTime(ActionType.GeneralAction, buffJob.GeneralActionId);
                        float elapsed = am->GetRecastTimeElapsed(ActionType.GeneralAction, buffJob.GeneralActionId);
                        if (recast - elapsed <= 0f)
                        {
                            am->UseAction(ActionType.GeneralAction, buffJob.GeneralActionId);
                            lastCastTime = DateTime.Now;
                        }
                    }

                    // Max 10s in CastBuff per job, then move on with a warning
                    if ((DateTime.Now - stepStartTime).TotalMilliseconds >= 10000)
                    {
                        DuoLog.Warning($"{buffJob.Label} did not confirm within 10s. Moving to next job.");
                        subState = SequenceSubState.WaitDelay;
                        stepStartTime = DateTime.Now;
                    }
                    break;
                }

            case SequenceSubState.WaitDelay:
                // 800ms settle so animation lock finishes before the next job swap
                if ((DateTime.Now - stepStartTime).TotalMilliseconds >= 800)
                {
                    currentJobIndex++;
                    subState = SequenceSubState.SwitchJob;
                    stepStartTime = DateTime.Now;
                }
                break;
        }
    }
}
