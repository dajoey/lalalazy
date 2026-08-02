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
///     "/gluttony buff" — applies the four long Knowledge Crystal party buffs (Enduring
///     Fortitude, Fleetfooted, Romeo's Ballad, Quicker Step), then restores the original job.
///     <para/>
///     Inquiring Mind fast path (v1.0.4.101): Phantom Freelancer's Inquiring Mind
///     (Action 46606, phantom slot 3 = GeneralAction 33, unlocks at Phantom Freelancer 15)
///     grants every one of those buffs in a single cast. Its own tooltip states the coverage
///     is gated per buff on the level of the *granting* job, not on Freelancer:
///     <list type="bullet">
///         <item>Enduring Fortitude — Phantom Knight 2+</item>
///         <item>Fleetfooted — Phantom Monk 3+</item>
///         <item>Romeo's Ballad — Phantom Bard 2+</item>
///         <item>Quicker Step — Phantom Dancer 2+</item>
///     </list>
///     So the sequence leads with Inquiring Mind whenever Freelancer is 15+ and at least one
///     buff qualifies, which collapses four job changes into one. The per-job steps are NOT
///     removed when that happens: they stay queued and skip themselves via the existing
///     <see cref="FreshSkipSeconds"/> check once the buff is actually on the player. That makes
///     them a verified fallback rather than dead weight — an under-levelled job, or an
///     Inquiring Mind that silently did not land, still gets its buff the old way.
///     <para/>
///     Cast strategy (v1.0.4.98): two independent, community-verified paths per step —
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
///     Success is verified by the buff status actually appearing/refreshing.
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

    /// <summary>One crystal buff and the Phantom Job that casts it directly.</summary>
    /// <param name="JobId">Support job to switch to (<see cref="OccultCrescent.JobIDs"/>).</param>
    /// <param name="GeneralActionId">Phantom Action hotbar slot (GeneralAction rows 31-35). Primary cast path.</param>
    /// <param name="ActionId">Real Action-sheet id. Fallback cast path.</param>
    /// <param name="BuffStatusId">Long party buff used to verify the cast landed.</param>
    /// <param name="PhantomJobStatusId">"Phantom Job" status the server applies once the job change lands.</param>
    /// <param name="InquiringMindJobLevel">Level this job must be for Inquiring Mind to grant this buff.</param>
    private sealed record CrystalBuff(
        int JobId,
        uint GeneralActionId,
        uint ActionId,
        uint BuffStatusId,
        uint PhantomJobStatusId,
        int InquiringMindJobLevel,
        string Label);

    /// <summary>
    ///     One executable step of the sequence: switch to <paramref name="JobId"/>, cast one
    ///     action, verify every status in <paramref name="BuffStatusIds"/>. The per-job steps
    ///     carry exactly one status; the Inquiring Mind step carries every buff it qualifies for.
    /// </summary>
    private sealed record BuffStep(
        int JobId,
        uint GeneralActionId,
        uint ActionId,
        uint[] BuffStatusIds,
        uint PhantomJobStatusId,
        string Label);

    private static readonly List<CrystalBuff> CrystalBuffMap =
    [
        new((int)OccultCrescent.JobIDs.Knight, 32, OccultCrescent.Pray, OccultCrescent.Buffs.EnduringFortitude, 4358, 2, "Pray (Enduring Fortitude)"),
        new((int)OccultCrescent.JobIDs.Monk, 33, OccultCrescent.Counterstance, OccultCrescent.Buffs.Fleetfooted, 4360, 3, "Counterstance (Fleetfooted)"),
        new((int)OccultCrescent.JobIDs.Bard, 32, OccultCrescent.RomeosBallad, OccultCrescent.Buffs.RomeosBallad, 4363, 2, "Romeo's Ballad"),
        new((int)OccultCrescent.JobIDs.Dancer, 32, OccultCrescent.Quickstep, 4799 /* Quicker Step */, 4805, 2, "Quickstep (Quicker Step)"),
    ];

    // Inquiring Mind: Freelancer's third phantom slot. Datamined from MKDSupportJob row 0,
    // whose Action array is slot-ordered — Occult Resuscitation (41650, unlock 5),
    // Occult Treasuresight (41651, unlock 10), Inquiring Mind (46606, unlock 15),
    // Wisdom on the Winds (49102, unlock 20).
    private const uint InquiringMindGeneralActionId = 33;
    private const int InquiringMindUnlockLevel = 15;
    private const uint FreelancerPhantomJobStatusId = 4242;

    // Skip a step entirely if its buffs already have this much time left (buffs run 1800s).
    private const float FreshSkipSeconds = 1500f;

    // A cast counts as landed when the buff's remaining time exceeds the pre-cast snapshot by this much.
    private const float CastSuccessMinGainSeconds = 60f;

    private const int JobConfirmTimeoutMs = 6000;   // max wait for the phantom-job status after ChangeSupportJob
    private const int ByteConfirmFloorMs = 1500;    // state-byte-only confirm must hold at least this long
    private const int PostConfirmSettleMs = 600;    // settle after confirm so the phantom slots initialize
    private const int AttemptIntervalMs = 800;      // spacing between cast attempts
    private const int GeneralActionAttempts = 3;    // attempts 1-3: GeneralAction slot path
    private const int MaxCastAttempts = 6;          // attempts 4-6: Action-sheet id fallback path
    private const int CastTimeoutMs = 10000;        // give up on a step's buff after this long in Cast
    private const int SettleMs = 1000;              // pause between steps (animation lock etc.)
    private const int SequenceTimeoutMs = 120000;   // whole-cycle safety net

    private static bool isRunning = false;
    private static int initialJob = -1;
    private static int currentStepIndex = 0;
    private static int successCount = 0;
    private static int castAttempts = 0;
    private static float?[] preCastRemaining = [];
    private static List<BuffStep> sequence = [];
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

    /// <summary>Current Phantom Job level, by support job id. 0 when the instance state is unavailable.</summary>
    private static unsafe int GetSupportJobLevel(int jobId)
    {
        if (jobId < 0) return 0;

        var inst = PublicContentOccultCrescent.GetInstance();
        if (inst == null) return 0;

        return inst->State.SupportJobLevels[(byte)jobId];
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
        // Inquiring Mind needs one too: its own tooltip gates the whole effect on "when
        // executed near a knowledge crystal", so the fast path has no extra location rule.
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

        var perJobBuffs = CrystalBuffMap
            .Where(b => Enum.IsDefined(typeof(OccultCrescent.JobIDs), b.JobId) &&
                        ((OccultCrescent.JobIDs)b.JobId).IsActive())
            .ToList();

        if (perJobBuffs.Count == 0)
        {
            DuoLog.Error("No active buffing Phantom Jobs found to cycle.");
            return;
        }

        sequence.Clear();

        // Lead with Inquiring Mind when it is unlocked, and let it carry every buff whose
        // granting job clears the level gate in its tooltip. Anything it cannot cover falls
        // through to that job's own step below, which is queued either way.
        var freelancerLevel = GetSupportJobLevel((int)OccultCrescent.JobIDs.Freelancer);
        if (freelancerLevel >= InquiringMindUnlockLevel)
        {
            var covered = perJobBuffs
                .Where(b => GetSupportJobLevel(b.JobId) >= b.InquiringMindJobLevel)
                .ToList();

            if (covered.Count > 0)
            {
                sequence.Add(new BuffStep(
                    (int)OccultCrescent.JobIDs.Freelancer,
                    InquiringMindGeneralActionId,
                    OccultCrescent.InquiringMind,
                    covered.Select(b => b.BuffStatusId).ToArray(),
                    FreelancerPhantomJobStatusId,
                    $"Inquiring Mind ({covered.Count}/{perJobBuffs.Count} buffs in one cast)"));

                var uncovered = perJobBuffs.Except(covered).ToList();
                if (uncovered.Count > 0)
                {
                    DuoLog.Information(
                        $"Inquiring Mind covers {covered.Count}/{perJobBuffs.Count} buffs — under-levelled: " +
                        string.Join(", ", uncovered.Select(b =>
                            $"{(OccultCrescent.JobIDs)b.JobId} {GetSupportJobLevel(b.JobId)}/{b.InquiringMindJobLevel}")) +
                        ". Those will be cast the long way.");
                }
            }
            else
            {
                DuoLog.Information("Inquiring Mind is unlocked but no buffing Phantom Job is levelled enough for it to grant anything — cycling jobs individually.");
            }
        }
        else
        {
            DuoLog.Information($"Inquiring Mind unavailable (Phantom Freelancer {freelancerLevel}/{InquiringMindUnlockLevel}) — cycling jobs individually.");
        }

        foreach (var buff in perJobBuffs)
        {
            sequence.Add(new BuffStep(
                buff.JobId,
                buff.GeneralActionId,
                buff.ActionId,
                [buff.BuffStatusId],
                buff.PhantomJobStatusId,
                buff.Label));
        }

        isRunning = true;
        currentStepIndex = 0;
        successCount = 0;
        subState = Step.SwitchJob;
        sequenceStartTime = DateTime.Now;
        stepStartTime = DateTime.Now;

        DuoLog.Information($"Starting Phantom Job crystal buff cycle across {sequence.Count} steps...");

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

    private static void NextStep()
    {
        currentStepIndex++;
        subState = Step.SwitchJob;
        stepStartTime = DateTime.Now;
    }

    private static void BeginCast(BuffStep step)
    {
        subState = Step.Cast;
        stepStartTime = DateTime.Now;
        lastAttemptTime = DateTime.MinValue;
        castAttempts = 0;
        preCastRemaining = step.BuffStatusIds.Select(GetStatusRemaining).ToArray();
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

        if (currentStepIndex >= sequence.Count)
        {
            if (initialJob >= 0 && inst->State.CurrentSupportJob != (byte)initialJob)
            {
                PublicContentOccultCrescent.ChangeSupportJob((byte)initialJob);
            }

            StopSequence("");
            if (successCount == sequence.Count)
                DuoLog.Information($"Phantom Job crystal buff cycle complete: {successCount}/{sequence.Count} steps applied. Restored original Phantom Job.");
            else
                DuoLog.Warning($"Phantom Job crystal buff cycle finished with {successCount}/{sequence.Count} steps applied.");
            return;
        }

        var step = sequence[currentStepIndex];

        switch (subState)
        {
            case Step.SwitchJob:
            {
                // Already fresh? Don't even swap. This is also what turns the per-job steps
                // into a fallback rather than a duplicate pass: once Inquiring Mind has put
                // a buff back to ~1800s, that job's own step skips without a job change.
                var existing = step.BuffStatusIds.Select(GetStatusRemaining).ToList();
                if (existing.All(r => r.HasValue && r.Value >= FreshSkipSeconds))
                {
                    successCount++;
                    DuoLog.Information($"{step.Label} already active ({existing.Min(r => r!.Value):F0}s) — skipping.");
                    NextStep();
                    return;
                }

                if (inst->State.CurrentSupportJob != (byte)step.JobId)
                {
                    PublicContentOccultCrescent.ChangeSupportJob((byte)step.JobId);
                    DuoLog.Information($"Switching to Phantom {(OccultCrescent.JobIDs)step.JobId} for {step.Label}...");
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
                bool statusConfirmed = HasStatus(step.PhantomJobStatusId);
                bool byteConfirmed = inst->State.CurrentSupportJob == (byte)step.JobId &&
                                     (DateTime.Now - stepStartTime).TotalMilliseconds >= ByteConfirmFloorMs;

                if (statusConfirmed || byteConfirmed)
                {
                    if (confirmedAtTime == DateTime.MinValue)
                        confirmedAtTime = DateTime.Now;

                    if ((DateTime.Now - confirmedAtTime).TotalMilliseconds >= PostConfirmSettleMs)
                        BeginCast(step);

                    return;
                }

                if ((DateTime.Now - stepStartTime).TotalMilliseconds > JobConfirmTimeoutMs)
                {
                    DuoLog.Warning($"Job change to Phantom {(OccultCrescent.JobIDs)step.JobId} did not confirm within {JobConfirmTimeoutMs / 1000}s. Skipping {step.Label}.");
                    NextStep();
                }
                return;
            }

            case Step.Cast:
            {
                // Success = every buff this step is responsible for is present and meaningfully
                // fresher than before the cast. For Inquiring Mind that is all the buffs it
                // qualified to grant, so a partial application is correctly NOT counted - the
                // per-job steps behind it will pick up whatever is still stale.
                bool applied = true;
                float lowest = float.MaxValue;
                for (int i = 0; i < step.BuffStatusIds.Length; i++)
                {
                    var remaining = GetStatusRemaining(step.BuffStatusIds[i]);
                    var before = i < preCastRemaining.Length ? preCastRemaining[i] : null;

                    if (!remaining.HasValue ||
                        (before.HasValue && remaining.Value <= before.Value + CastSuccessMinGainSeconds))
                    {
                        applied = false;
                        break;
                    }

                    lowest = Math.Min(lowest, remaining.Value);
                }

                if (applied)
                {
                    successCount++;
                    DuoLog.Information($"{step.Label} applied ({lowest:F0}s remaining).");
                    subState = Step.Settle;
                    stepStartTime = DateTime.Now;
                    return;
                }

                if ((DateTime.Now - stepStartTime).TotalMilliseconds >= CastTimeoutMs)
                {
                    DuoLog.Warning($"{step.Label} did not apply within {CastTimeoutMs / 1000}s. Moving on.");
                    subState = Step.Settle;
                    stepStartTime = DateTime.Now;
                    return;
                }

                if (castAttempts >= MaxCastAttempts)
                    return; // out of attempts — wait out the timeout window for a late server ack

                if ((DateTime.Now - lastAttemptTime).TotalMilliseconds < AttemptIntervalMs)
                    return;

                castAttempts++;
                lastAttemptTime = DateTime.Now;

                if (castAttempts <= GeneralActionAttempts)
                {
                    // Path 1: phantom hotbar slot (GeneralAction 31-35), like pressing the button.
                    ActionWatching.UseActionRaw(ActionType.GeneralAction, step.GeneralActionId);
                }
                else
                {
                    // Path 2: real Action-sheet id, self-targeted (RSR/Wrath-AutoRotation style).
                    ActionWatching.UseActionRaw(ActionType.Action, step.ActionId, Player.Object.GameObjectId);
                }
                return;
            }

            case Step.Settle:
            {
                if ((DateTime.Now - stepStartTime).TotalMilliseconds >= SettleMs)
                    NextStep();
                return;
            }
        }
    }
}
