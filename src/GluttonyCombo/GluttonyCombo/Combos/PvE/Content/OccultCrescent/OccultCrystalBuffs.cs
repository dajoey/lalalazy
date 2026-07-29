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
        CastBuff,
        WaitDelay
    }

    private static bool isRunning = false;
    private static int initialJob = -1;
    private static int currentJobIndex = 0;
    private static List<int> activeJobs = [];
    private static IGameObject? targetCrystal = null;
    private static DateTime stepStartTime = DateTime.MinValue;
    private static DateTime sequenceStartTime = DateTime.MinValue;
    private static SequenceSubState subState = SequenceSubState.SwitchJob;

    // Map each Phantom Job ID to its primary buff/utility action ID
    private static readonly Dictionary<int, uint> JobBuffActions = new()
    {
        { (int)OccultCrescent.JobIDs.Freelancer, OccultCrescent.OccultTreasuresight },
        { (int)OccultCrescent.JobIDs.Knight, OccultCrescent.PhantomGuard },
        { (int)OccultCrescent.JobIDs.Berserker, OccultCrescent.Rage },
        { (int)OccultCrescent.JobIDs.Monk, OccultCrescent.Counterstance },
        { (int)OccultCrescent.JobIDs.Ranger, OccultCrescent.PhantomAim },
        { (int)OccultCrescent.JobIDs.Samurai, OccultCrescent.Shirahadori },
        { (int)OccultCrescent.JobIDs.Bard, OccultCrescent.HerosRime },
        { (int)OccultCrescent.JobIDs.Geomancer, OccultCrescent.BattleBell },
        { (int)OccultCrescent.JobIDs.TimeMage, OccultCrescent.OccultQuick },
        { (int)OccultCrescent.JobIDs.Cannoneer, OccultCrescent.PhantomFire },
        { (int)OccultCrescent.JobIDs.Chemist, OccultCrescent.OccultPotion },
        { (int)OccultCrescent.JobIDs.Oracle, OccultCrescent.Predict },
        { (int)OccultCrescent.JobIDs.Thief, OccultCrescent.Vigilance },
        { (int)OccultCrescent.JobIDs.MysticKnight, OccultCrescent.MagicShell },
        { (int)OccultCrescent.JobIDs.Gladiator, OccultCrescent.Defend },
        { (int)OccultCrescent.JobIDs.Dancer, OccultCrescent.Quickstep },
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
        subState = SequenceSubState.SwitchJob;
        sequenceStartTime = DateTime.Now;
        stepStartTime = DateTime.Now;

        // Target crystal
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

        unsafe
        {
            var inst = PublicContentOccultCrescent.GetInstance();
            if (inst == null)
            {
                StopSequence("Occult Crescent instance unavailable.");
                return;
            }

            if (currentJobIndex >= activeJobs.Count)
            {
                // Sequence complete: restore initial job
                if (initialJob >= 0)
                {
                    inst->State.CurrentSupportJob = (byte)initialJob;
                }

                StopSequence("");
                DuoLog.Information("Phantom Job crystal buff cycle complete! Restored original Phantom Job.");
                return;
            }

            int jobId = activeJobs[currentJobIndex];

            switch (subState)
            {
                case SequenceSubState.SwitchJob:
                    // Set support job at crystal
                    inst->State.CurrentSupportJob = (byte)jobId;

                    // Re-interact with crystal if target lost
                    if (Svc.Targets.Target == null && targetCrystal != null)
                    {
                        Svc.Targets.Target = targetCrystal;
                    }

                    if (targetCrystal != null && TargetSystem.Instance() != null)
                    {
                        TargetSystem.Instance()->InteractWithObject((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)targetCrystal.Address, false);
                    }

                    subState = SequenceSubState.CastBuff;
                    stepStartTime = DateTime.Now;
                    break;

                case SequenceSubState.CastBuff:
                    // Wait 400ms after job switch before casting
                    if ((DateTime.Now - stepStartTime).TotalMilliseconds < 400)
                        return;

                    // Resolve buff action ID
                    uint actionId = 0;
                    if (JobBuffActions.TryGetValue(jobId, out uint defaultActionId))
                    {
                        actionId = defaultActionId;
                    }

                    // Fallback to TryGetPhantomAction logic if default action unavailable
                    if (actionId == 0 || ActionManager.Instance() == null || ActionManager.Instance()->GetActionStatus(ActionType.Action, actionId) != 0)
                    {
                        OccultCrescent.TryGetPhantomAction(ref actionId);
                    }

                    if (actionId > 0 && ActionManager.Instance() != null)
                    {
                        ActionManager.Instance()->UseAction(ActionType.Action, actionId);
                    }

                    subState = SequenceSubState.WaitDelay;
                    stepStartTime = DateTime.Now;
                    break;

                case SequenceSubState.WaitDelay:
                    // Wait 1000ms for action animation lock / buff application
                    if ((DateTime.Now - stepStartTime).TotalMilliseconds < 1000)
                        return;

                    // Move to next job
                    currentJobIndex++;
                    subState = SequenceSubState.SwitchJob;
                    stepStartTime = DateTime.Now;
                    break;
            }
        }
    }
}
