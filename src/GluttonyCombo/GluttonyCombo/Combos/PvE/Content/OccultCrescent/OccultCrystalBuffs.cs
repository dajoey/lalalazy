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
using GluttonyCombo.Data;

namespace GluttonyCombo.Combos.PvE;

/// <summary>
///     "/gluttony buff" — cycles Phantom Jobs at a Knowledge Crystal, applies each job's long
///     party buff (Pray, Counterstance, Romeo's Ballad, Quickstep), then restores the original job.
///     <para/>
///     Cast strategy (v1.0.4.98): two independent, community-verified paths per buff —
///     <list type="number">
///         <item>ActionType.GeneralAction phantom slot (rows 31-35 = "Phantom Action I-V" —
///         verified unchanged on the live 7.5x GeneralAction sheet). Identical to pressing the
///         phantom hotbar button; this is what BOCCHI's Buff module does.</item>
///         <item>ActionType.Action with the real Action-sheet id (Pray 41589 etc.), explicit
///         self-target — the way RotationSolverReborn and Wrath's own AutoRotation cast
///         phantom actions.</item>
///     </list>
///     Both paths go through <see cref="ActionWatching.UseActionRaw"/>, which calls the game's
///     UseAction directly and bypasses GluttonyCombo's own UseAction detour (penalty gate,
///     retargeting, queue handling) so plugin combat logic can never silently eat the cast —
///     the leading candidate cause of the v1.0.4.86-96 "cycles jobs but never casts" failures.
///     Success is verified by the buff status actually appearing/refreshing, and every attempt
///     logs GetActionStatus + the UseAction return value to the Dalamud log (/xllog) under
///     [CrystalBuffs], so any remaining failure is diagnosable from a single run.
/// </summary>
internal static class OccultCrystalBuffs
{
    private enum Step
    {
        SwitchJob,
        ConfirmJob,
        Cast,
        Settle,
    }

    /// <param name="JobId">Support job to switch to (<see cref="OccultCrescent.JobIDs"/>).</param>
    /// <param name="GeneralActionId">Phantom Action hotbar slot (GeneralAction rows 31-35). Primary cast path.</param>
    /// <param name="ActionId">Real Action-sheet id. Fallback cast path.</param>
    /// <param name="BuffStatusId">Long party buff used to verify the cast landed.</param>
    /// <param name="PhantomJobStatusId">"Phantom &lt;Job&gt;" status the server applies once the job change lands.</param>
    private sealed record BuffJob(int JobId, uint GeneralActionId, uint ActionId, uint BuffStatusId, uint PhantomJobStatusId, string Label);

    private static readonly List<BuffJob> JobBuffMap =
    [
        new((int)OccultCrescent.JobIDs.Knight, 32, OccultCrescent.Pray, OccultCrescent.Buffs.EnduringFortitude, 4358, "Pray (Enduring Fortitude)"),
        new((int)OccultCrescent.JobIDs.Monk, 33, OccultCrescent.Counterstance, OccultCrescent.Buffs.Fleetfooted, 4360, "Counterstance (Fleetfooted)"),
        new((int)OccultCrescent.JobIDs.Bard, 32, OccultCrescent.RomeosBallad, OccultCrescent.Buffs.RomeosBallad, 4363, "Romeo's Ballad"),
        new((int)OccultCrescent.JobIDs.Dancer, 32, OccultCrescent.Quickstep, 4799 /* Quicker Step */, 4805, "Quickstep (Quicker Step)"),
    ];

    // Skip a job entirely if its buff already has this much time left (buffs run 1800s).
    private const float FreshSkipSeconds = 1500f;

    // A cast counts as landed when the buff's remaining time exceeds the pre-cast snapshot by this much.
    private const float CastSuccessMinGainSeconds = 60f;

    private const int JobConfirmTimeoutMs = 6000;   // max wait for the phantom-job status after ChangeSupportJob
    private const int ByteConfirmFloorMs = 1500;    // state-byte-only confirm must hold at least this long
    private const int PostConfirmSettleMs = 600;    // settle after confirm so the phantom slots initialize
    private const int AttemptIntervalMs = 800;      // spacing between cast attempts
    private const int GeneralActionAttempts = 3;    // attempts 1-3: GeneralAction slot path
    private const int MaxCastAttempts = 6;          // attempts 4-6: Action-sheet id fallback path
    private const int CastTimeoutMs = 10000;        // give up on a job's buff after this long in Cast
    private const int SettleMs = 1000;              // pause between jobs (animation lock etc.)
    private const int SequenceTimeoutMs = 120000;   // whole-cycle safety net

    private static bool isRunning = false;
    private static int initialJob = -1;
    private static int currentJobIndex = 0;
    private static int successCount = 0;
    private static int castAttempts = 0;
    private static float? preCastRemaining = null;
    private static List<BuffJob> buffingJobs = [];
    private static IGameObject? targetCrystal = null;
    private static DateTime stepStartTime = DateTime.MinValue;
    private static DateTime sequenceStartTime = DateTime.MinValue;
    private static DateTime lastAttemptTime = DateTime.MinValue;
    private static DateTime confirmedAtTime = DateTime.MinValue;
    private static Step subState = Step.SwitchJob;

    private static bool HasStatus(uint statusId)
    {
        return Player.Object != null && Player.Object.StatusList.Any(s => s.StatusId == statusId);
    }

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

        // Job changes only work at a Knowledge Crystal / Aetherial Node — require one nearby.
        // NOTE: deliberately no hard-targeting of the crystal (BOCCHI parity); the buffs are
        // self/party casts and an EventObj hard target is at best useless to them.
        if (Svc.Targets.Target != null && Vector3.Distance(playerPos, Svc.Targets.Target.Position) <= 10.0f)
        {
            targetCrystal = Svc.Targets.Target;
        }
        else
        {
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
        successCount = 0;
        subState = Step.SwitchJob;
        sequenceStartTime = DateTime.Now;
        stepStartTime = DateTime.Now;

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

    private static void NextJob()
    {
        currentJobIndex++;
        subState = Step.SwitchJob;
        stepStartTime = DateTime.Now;
    }

    private static void BeginCast(BuffJob job)
    {
        subState = Step.Cast;
        stepStartTime = DateTime.Now;
        lastAttemptTime = DateTime.MinValue;
        castAttempts = 0;
        preCastRemaining = GetStatusRemaining(job.BuffStatusId);
        Svc.Log.Information($"[CrystalBuffs] {job.Label}: job confirmed, starting casts (pre-cast remaining: {(preCastRemaining.HasValue ? $"{preCastRemaining.Value:F0}s" : "none")}).");
    }

    private static unsafe void OnFrameworkUpdate(object framework)
    {
        if (!isRunning) return;

        if ((DateTime.Now - sequenceStartTime).TotalMilliseconds > SequenceTimeoutMs)
        {
            StopSequence("Timeout exceeded (120s).");
            return;
        }

        if (Player.Object == null || CustomComboFunctions.InCombat() || Player.Object.IsDead)
        {
            StopSequence("Entered combat, player died, or player state lost.");
            return;
        }

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
            if (initialJob >= 0 && inst->State.CurrentSupportJob != (byte)initialJob)
            {
                PublicContentOccultCrescent.ChangeSupportJob((byte)initialJob);
            }

            StopSequence("");
            if (successCount == buffingJobs.Count)
                DuoLog.Information($"Phantom Job crystal buff cycle complete: {successCount}/{buffingJobs.Count} buffs applied. Restored original Phantom Job.");
            else
                DuoLog.Warning($"Phantom Job crystal buff cycle finished with {successCount}/{buffingJobs.Count} buffs applied — check /xllog ([CrystalBuffs]) for the per-attempt action status codes.");
            return;
        }

        var job = buffingJobs[currentJobIndex];

        switch (subState)
        {
            case Step.SwitchJob:
            {
                // Already fresh? Don't even swap.
                var existing = GetStatusRemaining(job.BuffStatusId);
                if (existing.HasValue && existing.Value >= FreshSkipSeconds)
                {
                    successCount++;
                    DuoLog.Information($"{job.Label} already active ({existing.Value:F0}s) — skipping.");
                    NextJob();
                    return;
                }

                if (inst->State.CurrentSupportJob != (byte)job.JobId)
                {
                    PublicContentOccultCrescent.ChangeSupportJob((byte)job.JobId);
                    DuoLog.Information($"Switching to Phantom {(OccultCrescent.JobIDs)job.JobId} for {job.Label}...");
                }

                subState = Step.ConfirmJob;
                stepStartTime = DateTime.Now;
                confirmedAtTime = DateTime.MinValue;
                return;
            }

            case Step.ConfirmJob:
            {
                // Primary confirm: the "Phantom <Job>" status — applied by the server once the
                // support-job change fully lands (BOCCHI parity). Fallback: the state byte,
                // but only after it has held for a floor period, since it can lead the server.
                bool statusConfirmed = HasStatus(job.PhantomJobStatusId);
                bool byteConfirmed = inst->State.CurrentSupportJob == (byte)job.JobId &&
                                     (DateTime.Now - stepStartTime).TotalMilliseconds >= ByteConfirmFloorMs;

                if (statusConfirmed || byteConfirmed)
                {
                    if (confirmedAtTime == DateTime.MinValue)
                        confirmedAtTime = DateTime.Now;

                    if ((DateTime.Now - confirmedAtTime).TotalMilliseconds >= PostConfirmSettleMs)
                        BeginCast(job);

                    return;
                }

                if ((DateTime.Now - stepStartTime).TotalMilliseconds > JobConfirmTimeoutMs)
                {
                    DuoLog.Warning($"Job change to Phantom {(OccultCrescent.JobIDs)job.JobId} did not confirm within {JobConfirmTimeoutMs / 1000}s. Skipping {job.Label}.");
                    NextJob();
                }
                return;
            }

            case Step.Cast:
            {
                // Success = the buff is present and meaningfully fresher than before we started.
                var remaining = GetStatusRemaining(job.BuffStatusId);
                bool applied = remaining.HasValue &&
                               (!preCastRemaining.HasValue || remaining.Value > preCastRemaining.Value + CastSuccessMinGainSeconds);
                if (applied)
                {
                    successCount++;
                    DuoLog.Information($"{job.Label} applied ({remaining!.Value:F0}s remaining).");
                    subState = Step.Settle;
                    stepStartTime = DateTime.Now;
                    return;
                }

                if ((DateTime.Now - stepStartTime).TotalMilliseconds >= CastTimeoutMs)
                {
                    DuoLog.Warning($"{job.Label} did not apply within {CastTimeoutMs / 1000}s ({castAttempts} attempts across both cast paths) — see /xllog ([CrystalBuffs]) for the action status codes.");
                    subState = Step.Settle;
                    stepStartTime = DateTime.Now;
                    return;
                }

                if (castAttempts >= MaxCastAttempts)
                    return; // out of attempts — wait out the timeout window for a late server ack

                if ((DateTime.Now - lastAttemptTime).TotalMilliseconds < AttemptIntervalMs)
                    return;

                var am = ActionManager.Instance();
                if (am == null)
                    return;

                castAttempts++;
                lastAttemptTime = DateTime.Now;

                if (castAttempts <= GeneralActionAttempts)
                {
                    // Path 1: phantom hotbar slot (GeneralAction 31-35), like pressing the button.
                    uint status = am->GetActionStatus(ActionType.GeneralAction, job.GeneralActionId);
                    bool used = ActionWatching.UseActionRaw(ActionType.GeneralAction, job.GeneralActionId);
                    Svc.Log.Information($"[CrystalBuffs] {job.Label}: attempt {castAttempts}/{MaxCastAttempts} via GeneralAction slot {job.GeneralActionId} -> UseAction={used}, GetActionStatus={status}");
                }
                else
                {
                    // Path 2: real Action-sheet id, self-targeted (RSR/Wrath-AutoRotation style).
                    uint status = am->GetActionStatus(ActionType.Action, job.ActionId);
                    bool used = ActionWatching.UseActionRaw(ActionType.Action, job.ActionId, Player.Object.GameObjectId);
                    Svc.Log.Information($"[CrystalBuffs] {job.Label}: attempt {castAttempts}/{MaxCastAttempts} via Action {job.ActionId} -> UseAction={used}, GetActionStatus={status}");
                }
                return;
            }

            case Step.Settle:
            {
                if ((DateTime.Now - stepStartTime).TotalMilliseconds >= SettleMs)
                    NextJob();
                return;
            }
        }
    }
}
