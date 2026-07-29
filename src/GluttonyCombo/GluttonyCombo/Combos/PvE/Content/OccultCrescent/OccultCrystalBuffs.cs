using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using GluttonyCombo.CustomComboNS.Functions;

namespace GluttonyCombo.Combos.PvE;

internal static class OccultCrystalBuffs
{
    private static bool isRunning = false;
    private static int initialJob = -1;
    private static int currentJobIndex = 0;
    private static List<int> activeJobs = [];
    private static IGameObject? targetCrystal = null;
    private static DateTime stepStartTime = DateTime.MinValue;
    private static DateTime sequenceStartTime = DateTime.MinValue;

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
            Vector3.Distance(playerPos, o.Position) <= 8.0f &&
            (o.Name.TextValue.Contains("Crystal", StringComparison.OrdinalIgnoreCase) ||
             o.Name.TextValue.Contains("Node", StringComparison.OrdinalIgnoreCase) ||
             o.Name.TextValue.Contains("Knowledge", StringComparison.OrdinalIgnoreCase) ||
             o.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj));

        if (targetCrystal == null)
        {
            DuoLog.Error("No Knowledge Crystal or Aetherial Node found within 8 meters.");
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

        // Build list of active job IDs
        activeJobs.Clear();
        foreach (OccultCrescent.JobIDs job in Enum.GetValues(typeof(OccultCrescent.JobIDs)))
        {
            if (job.IsActive() && (int)job >= 0)
            {
                activeJobs.Add((int)job);
            }
        }

        if (activeJobs.Count == 0)
        {
            DuoLog.Error("No active Phantom Jobs found to cycle.");
            return;
        }

        isRunning = true;
        currentJobIndex = 0;
        sequenceStartTime = DateTime.Now;
        stepStartTime = DateTime.Now;

        // Set target to crystal
        Svc.Targets.Target = targetCrystal;

        DuoLog.Information($"Starting Phantom Job crystal buff cycle across {activeJobs.Count} jobs...");

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

    private static void OnFrameworkUpdate(object framework)
    {
        if (!isRunning) return;

        // Timeout guard: 30 seconds max
        if ((DateTime.Now - sequenceStartTime).TotalSeconds > 30)
        {
            StopSequence("Timeout exceeded (30s).");
            return;
        }

        if (Player.Object == null || CustomComboFunctions.InCombat() || Player.Object.IsDead)
        {
            StopSequence("Entered combat, player died, or player state lost.");
            return;
        }

        // Distance guard
        if (targetCrystal != null && Vector3.Distance(Player.Object.Position, targetCrystal.Position) > 10.0f)
        {
            StopSequence("Moved too far away from the crystal.");
            return;
        }

        // Step delay: 600ms per step
        if ((DateTime.Now - stepStartTime).TotalMilliseconds < 600)
        {
            return;
        }

        stepStartTime = DateTime.Now;

        unsafe
        {
            var inst = PublicContentOccultCrescent.GetInstance();
            if (inst == null)
            {
                StopSequence("Occult Crescent instance unavailable.");
                return;
            }

            // Interact with crystal to refresh/swap if target lost
            if (Svc.Targets.Target == null && targetCrystal != null)
            {
                Svc.Targets.Target = targetCrystal;
            }

            if (targetCrystal != null && TargetSystem.Instance() != null)
            {
                TargetSystem.Instance()->InteractWithObject((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)targetCrystal.Address, false);
            }

            if (currentJobIndex < activeJobs.Count)
            {
                int nextJobId = activeJobs[currentJobIndex];
                inst->State.CurrentSupportJob = (byte)nextJobId;
                currentJobIndex++;
            }
            else
            {
                // Sequence finished: restore initial job
                if (initialJob >= 0)
                {
                    inst->State.CurrentSupportJob = (byte)initialJob;
                }

                StopSequence("");
                DuoLog.Information("Phantom Job crystal buff cycle complete! Restored original Phantom Job.");
            }
        }
    }
}
