using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using GluttonyCombo.CustomComboNS.Functions;

namespace GluttonyCombo.Combos.PvE;

internal static class OccultCrystalBuffs
{
    private enum SequenceSubState
    {
        InteractCrystal,
        SelectMenuOption,
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
    private static SequenceSubState subState = SequenceSubState.InteractCrystal;

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
            // READ ONLY: Save initial job
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
        subState = SequenceSubState.InteractCrystal;
        sequenceStartTime = DateTime.Now;
        stepStartTime = DateTime.Now;

        // Target crystal naturally
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

    private static unsafe void OnFrameworkUpdate(object framework)
    {
        if (!isRunning) return;

        // Timeout guard: 60 seconds max
        if ((DateTime.Now - sequenceStartTime).TotalSeconds > 60)
        {
            StopSequence("Timeout exceeded (60s).");
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

        if (currentJobIndex >= activeJobs.Count)
        {
            StopSequence("");
            DuoLog.Information("Phantom Job crystal buff cycle complete!");
            return;
        }

        int jobId = activeJobs[currentJobIndex];

        switch (subState)
        {
            case SequenceSubState.InteractCrystal:
                // Target and interact with crystal natively
                if (Svc.Targets.Target == null && targetCrystal != null)
                {
                    Svc.Targets.Target = targetCrystal;
                }

                unsafe
                {
                    if (targetCrystal != null && TargetSystem.Instance() != null)
                    {
                        TargetSystem.Instance()->InteractWithObject((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)targetCrystal.Address, false);
                    }
                }

                subState = SequenceSubState.SelectMenuOption;
                stepStartTime = DateTime.Now;
                break;

            case SequenceSubState.SelectMenuOption:
                // Handle AddonSelectString or AddonSelectIconString if menu opens
                if (GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>("SelectString", out var selectString) && GenericHelpers.IsAddonReady(selectString))
                {
                    var master = new AddonMaster.SelectString(selectString);
                    if (master.Entries.Length > currentJobIndex)
                    {
                        master.Entries[currentJobIndex].Select();
                    }
                    subState = SequenceSubState.CastBuff;
                    stepStartTime = DateTime.Now;
                    return;
                }
                else if (GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>("SelectIconString", out var selectIconString) && GenericHelpers.IsAddonReady(selectIconString))
                {
                    var master = new AddonMaster.SelectIconString(selectIconString);
                    if (master.Entries.Length > currentJobIndex)
                    {
                        master.Entries[currentJobIndex].Select();
                    }
                    subState = SequenceSubState.CastBuff;
                    stepStartTime = DateTime.Now;
                    return;
                }

                // If no menu opens after 600ms, proceed to cast buff for current active job
                if ((DateTime.Now - stepStartTime).TotalMilliseconds > 600)
                {
                    subState = SequenceSubState.CastBuff;
                    stepStartTime = DateTime.Now;
                }
                break;

            case SequenceSubState.CastBuff:
                // Resolve buff action ID for active job
                uint actionId = 0;
                if (JobBuffActions.TryGetValue(jobId, out uint defaultActionId))
                {
                    actionId = defaultActionId;
                }

                if (actionId == 0)
                {
                    OccultCrescent.TryGetPhantomAction(ref actionId);
                }

                unsafe
                {
                    if (actionId > 0 && ActionManager.Instance() != null)
                    {
                        ActionManager.Instance()->UseAction(ActionType.Action, actionId);
                    }
                }

                subState = SequenceSubState.WaitDelay;
                stepStartTime = DateTime.Now;
                break;

            case SequenceSubState.WaitDelay:
                // Wait 1200ms for action animation lock / buff application
                if ((DateTime.Now - stepStartTime).TotalMilliseconds < 1200)
                    return;

                // Move to next job
                currentJobIndex++;
                subState = SequenceSubState.InteractCrystal;
                stepStartTime = DateTime.Now;
                break;
        }
    }
}
