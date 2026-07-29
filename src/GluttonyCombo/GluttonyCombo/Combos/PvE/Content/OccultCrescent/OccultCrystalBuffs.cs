using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
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

    private static bool isRunning = false;
    private static int initialJob = -1;
    private static int currentJobIndex = 0;
    private static List<int> buffingJobs = [];
    private static IGameObject? targetCrystal = null;
    private static DateTime stepStartTime = DateTime.MinValue;
    private static DateTime sequenceStartTime = DateTime.MinValue;
    private static SequenceSubState subState = SequenceSubState.SwitchJob;

    // Map each key buffing Phantom Job ID to its specific buff action ID and expected status ID
    private static readonly Dictionary<int, (uint ActionId, uint StatusId)> JobBuffMap = new()
    {
        { (int)OccultCrescent.JobIDs.Bard, (41609, 4244) },         // Romeo's Ballad
        { (int)OccultCrescent.JobIDs.Knight, (41589, 4233) },       // Pray / Enduring Fortitude
        { (int)OccultCrescent.JobIDs.Monk, (41597, 4239) },         // Counterstance / Fleetfooted
        { (int)OccultCrescent.JobIDs.Dancer, (46603, 4799) },       // Quickstep / Quicker Step
    };

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

        // Search for nearby crystal / node
        targetCrystal = Svc.Objects.FirstOrDefault(o =>
            o != null &&
            Vector3.Distance(playerPos, o.Position) <= 10.0f &&
            (o.Name.TextValue.Contains("Crystal", StringComparison.OrdinalIgnoreCase) ||
             o.Name.TextValue.Contains("Node", StringComparison.OrdinalIgnoreCase) ||
             o.Name.TextValue.Contains("Knowledge", StringComparison.OrdinalIgnoreCase) ||
             o.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj));

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

        // Build list of active buffing job IDs
        buffingJobs.Clear();
        foreach (var kvp in JobBuffMap)
        {
            int jobId = kvp.Key;
            if (Enum.IsDefined(typeof(OccultCrescent.JobIDs), jobId) && ((OccultCrescent.JobIDs)jobId).IsActive())
            {
                buffingJobs.Add(jobId);
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

        // Timeout guard: 45 seconds max
        if ((DateTime.Now - sequenceStartTime).TotalSeconds > 45)
        {
            StopSequence("Timeout exceeded (45s).");
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

        int jobId = buffingJobs[currentJobIndex];

        switch (subState)
        {
            case SequenceSubState.SwitchJob:
                // Native C++ function call to change support job at crystal
                if (inst->State.CurrentSupportJob != (byte)jobId)
                {
                    PublicContentOccultCrescent.ChangeSupportJob((byte)jobId);
                }

                subState = SequenceSubState.WaitForJobChange;
                stepStartTime = DateTime.Now;
                break;

            case SequenceSubState.WaitForJobChange:
                // Check if server confirmed job change
                if (inst->State.CurrentSupportJob == (byte)jobId)
                {
                    subState = SequenceSubState.CastBuff;
                    stepStartTime = DateTime.Now;
                    return;
                }

                // Wait up to 2.5s for server job change confirmation
                if ((DateTime.Now - stepStartTime).TotalMilliseconds > 2500)
                {
                    DuoLog.Warning($"Job change to Phantom Job ID {jobId} did not confirm in time. Skipping to next job.");
                    currentJobIndex++;
                    subState = SequenceSubState.SwitchJob;
                    stepStartTime = DateTime.Now;
                }
                break;

            case SequenceSubState.CastBuff:
                if (JobBuffMap.TryGetValue(jobId, out var buffData))
                {
                    uint actionId = buffData.ActionId;
                    if (actionId > 0 && ActionManager.Instance() != null)
                    {
                        if (ActionManager.Instance()->GetActionStatus(ActionType.Action, actionId) == 0)
                        {
                            ActionManager.Instance()->UseAction(ActionType.Action, actionId);
                        }
                    }
                }

                subState = SequenceSubState.WaitDelay;
                stepStartTime = DateTime.Now;
                break;

            case SequenceSubState.WaitDelay:
                // Wait 1200ms for action execution / animation lock
                if ((DateTime.Now - stepStartTime).TotalMilliseconds < 1200)
                    return;

                // Advance to next job
                currentJobIndex++;
                subState = SequenceSubState.SwitchJob;
                stepStartTime = DateTime.Now;
                break;
        }
    }
}
