using Dalamud.Game.ClientState.Objects.Types;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using GluttonyCombo.Combos.PvE;
using GluttonyCombo.Core;
using Lalalazy.Telemetry;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace GluttonyCombo.Data;

/// <summary>
///     Optional, off-by-default Beastmaster debug collector (fork, BST skeleton card).<br />
///     When <see cref="Configuration.ComboTelemetry"/> is on AND the player is on Beastmaster,
///     one structured line is written at Information level through the normal plugin logger
///     whenever the sampled state CHANGES:
///     <c>BT|unixms|gaugeHex|battlehorn|affinity|chain|kinship|pet|bm|av|statuses</c>.
/// </summary>
/// <remarks>
///     <b>Why it exists:</b> Beastmaster has no rotation logic in this build, so there is no
///     decision to tap. This samples the state a rotation WOULD need - the vendored gauge, the
///     summoned familiar's identity, and how the client is currently adjusting the two
///     replaceable actions - so the follow-up rotation cards can mine real play out of ffxivdb
///     <c>plugin_log_lines</c> instead of guessing.<br />
///     <b>Cost when off:</b> a single bool read in the framework tick; nothing else runs. Cost
///     when on but not on BST: one extra job comparison.<br />
///     <b>Rate:</b> gated on change plus a hard 4 lines/s floor
///     (<see cref="BeastmasterTelemetryFormat.MinIntervalMs"/>), asserted by replay in
///     <c>tests/GluttonyCombo.TelemetryHarness</c>.<br />
///     The line format and the emit gate live in <see cref="BeastmasterTelemetryFormat"/> so
///     they can be asserted offline.
/// </remarks>
internal static class BeastmasterTelemetry
{
    /// <inheritdoc cref="BeastmasterTelemetryFormat.Prefix"/>
    public const string Prefix = BeastmasterTelemetryFormat.Prefix;

    private static BeastmasterTelemetryFormat.GateState _gate;

    private static CrucibleTelemetryFormat.GateState _crucibleGate;

    /// <summary> Stalled-GCD gate and movement tracking for <c>SG|</c> / <c>CR|mv=</c>. </summary>
    private static CrucibleStallFormat.GateState _stallGate;
    private static long _lastPosTick;
    private static Vector3 _lastPos;
    private static float _moveSpeed;

    /// <summary> Reusable status buffer; the collector runs on the framework thread only. </summary>
    private static readonly List<ushort> StatusBuffer = [];

    /// <summary> Forgets the remembered snapshot, so the current state re-emits immediately. </summary>
    public static void Reset()
    {
        _gate.Reset();
        _crucibleGate.Reset();
        _stallGate.Reset();
        _lastPosTick = 0;
        _moveSpeed = 0f;
    }

    /// <summary>
    ///     Samples Beastmaster state once. Called from the framework tick; emits at most one
    ///     line, and only when the snapshot changed.
    /// </summary>
    public static void Tick()
    {
        if (Player.Job is not Job.BST)
            return;

        try
        {
            var snapshot = Sample();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (BeastmasterTelemetryFormat.ShouldEmit(ref _gate, now, snapshot))
            {
                var line = BeastmasterTelemetryFormat.BuildLine(now, snapshot);
                Svc.Log.Information(line);
                LalaTelemetry.Record(line);
            }

            // Crucible of the Unbroken: CR| stays board-only; XB| follows any visible XBM* addon (Bentbranch included).
            if (Player.Object is not null)
            {
                // Own movement speed (CR|mv=, SG|): position deltas between framework ticks, EMA-smoothed, yalms/s.
                var tick = Environment.TickCount64;
                if (_lastPosTick != 0 && tick > _lastPosTick)
                {
                    var dt = (tick - _lastPosTick) / 1000f;
                    if (dt is > 0.001f and < 1f)
                    {
                        var dx = Player.Object.Position.X - _lastPos.X;
                        var dz = Player.Object.Position.Z - _lastPos.Z;
                        var speed = MathF.Sqrt(dx * dx + dz * dz) / dt;
                        _moveSpeed = _moveSpeed <= 0f ? speed : 0.6f * _moveSpeed + 0.4f * speed;
                    }
                }
                _lastPos = Player.Object.Position;
                _lastPosTick = tick;

                if (BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType) != 0)
                {
                    var state = BST.ReadState();
                    var crucible = SampleCrucible(state);
                    if (CrucibleTelemetryFormat.ShouldEmit(ref _crucibleGate, now, crucible))
                    {
                        var line = CrucibleTelemetryFormat.BuildLine(now, crucible);
                        Svc.Log.Information(line);
                        LalaTelemetry.Record(line);
                    }

                    // Stalled GCD (SG|): the rotation chose an attack, the GCD sits ready, nothing is being sent.
                    SampleStall(state, now);
                }
                BST.CaptureCrucibleUi(now);
            }
        }
        catch (Exception ex)
        {
            // A collector must never be able to break the framework tick - nor fail silently every
            // frame (it used to log at Debug, i.e. invisibly). Rate-limited WRN via the error reporting.
            LalaTelemetry.Swallowed("telemetry.bt", ex);
        }
    }

    /// <summary> The hitbox-edge distance to a target (centers minus both hitboxes), or -1 without one. </summary>
    private static float EdgeDistanceTo(IGameObject target)
    {
        if (Player.Object is not { } player)
            return -1f;
        var dx = target.Position.X - player.Position.X;
        var dz = target.Position.Z - player.Position.Z;
        var center = MathF.Sqrt(dx * dx + dz * dz);
        return MathF.Max(0f, center - target.HitboxRadius - player.HitboxRadius);
    }

    /// <summary>
    ///     Stalled GCD (SG|, 1.0.4.266): while the rotation names an attack, the global cooldown is ready and
    ///     nothing has been sent for over a second, one line per stall says WHY - in order: no hostile target,
    ///     own cast bar, animation lock, a queued action, the chosen action out of range of the current target,
    ///     or none of those (unknown). Re-logs a continuing stall at most every 2 s with its length so far.
    /// </summary>
    private static unsafe void SampleStall(in BST_RotationLogic.BstState s, long unixMs)
    {
        var act = BST.LastDecisionActionId;
        var isAttack = act != 0 && ActionWatching.ActionSheet.TryGetValue(act, out var sheet)
            && sheet.CanTargetHostile && !sheet.CanTargetSelf;
        // ActionWatching keeps LOCAL time; subtracting UtcNow saturated every line at s=999 (2026-10-03).
        var sinceFire = CrucibleStallFormat.SinceFireSeconds(ActionWatching.TimeSinceLastAction);
        var holding = CrucibleStallFormat.IsStalled(s.InCombat, s.GcdReady, isAttack, sinceFire);
        if (!holding)
            return;

        var why = CrucibleStallFormat.Reason.Unknown;
        var edge = -1f;
        if (!s.HasHostileTarget)
            why = CrucibleStallFormat.Reason.NoTarget;
        else if (s.PlayerIsCasting)
            why = CrucibleStallFormat.Reason.Cast;
        else if (Player.AnimationLock > 0.05f)
            why = CrucibleStallFormat.Reason.Lock;
        else if (ActionManager.Instance()->QueuedActionId != 0)
            why = CrucibleStallFormat.Reason.Queue;
        else if (Svc.Targets.Target is { } target && Player.Object is { } player)
        {
            edge = EdgeDistanceTo(target);
            if (edge >= 0f && ActionManager.GetActionInRangeOrLoS(act, player.GameObject(), target.Struct()) is not (0 or 565))
                why = CrucibleStallFormat.Reason.Range;
        }

        var snap = new CrucibleStallFormat.Snapshot(act, why, edge, BST.LastCrucibleTargetNameId, sinceFire);
        if (CrucibleStallFormat.ShouldEmit(ref _stallGate, unixMs, snap))
        {
            var line = CrucibleStallFormat.BuildLine(unixMs, snap);
            Svc.Log.Information(line);
            LalaTelemetry.Record(line);
        }
    }

    /// <summary> The rotation's own view of the Crucible (BST.ReadState), sampled even when the rotation is idle. </summary>
    private static CrucibleTelemetryFormat.Snapshot SampleCrucible(in BST_RotationLogic.BstState s)
    {
        var flags = CrucibleTelemetryFormat.Flags.None;
        if (s.TargetHasDispellableBuff) flags |= CrucibleTelemetryFormat.Flags.TargetDispellable;
        if (s.TargetInStance) flags |= CrucibleTelemetryFormat.Flags.TargetStance;
        if (s.TargetDoNotAttack) flags |= CrucibleTelemetryFormat.Flags.TargetDoNotAttack;
        if (s.ProtectedNearTarget) flags |= CrucibleTelemetryFormat.Flags.ProtectedNear;
        if (s.TargetHasParry) flags |= CrucibleTelemetryFormat.Flags.TargetParry;
        if (s.ParryJustEnded) flags |= CrucibleTelemetryFormat.Flags.ParryEnded;
        if (s.PlayerHasCleansableDebuff) flags |= CrucibleTelemetryFormat.Flags.PlayerCleansable;
        if (s.EnemyTargetsPet) flags |= CrucibleTelemetryFormat.Flags.TargetOnPet;
        if (s.EnemyTargetsPlayer) flags |= CrucibleTelemetryFormat.Flags.TargetOnPlayer;
        if (s.ReadySnarl) flags |= CrucibleTelemetryFormat.Flags.SnarlReady;
        if (s.ReadyChallenge) flags |= CrucibleTelemetryFormat.Flags.ChallengeReady;
        if (s.TargetInterruptible) flags |= CrucibleTelemetryFormat.Flags.Interruptible;

        static byte Pct(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 100);

        return new CrucibleTelemetryFormat.Snapshot(
            (byte)s.CrucibleBoard,
            (sbyte)Math.Clamp(s.CrucibleBattle, -1, 100),
            (byte)s.CrucibleNeeds,
            (byte)Math.Clamp(s.EnemyCount, 0, 255),
            Pct(s.HighestEnemyHpPercent),
            s.HasHostileTarget ? BST.LastCrucibleTargetNameId : 0,
            s.HasHostileTarget ? Pct(s.TargetHpPercent) : (byte)0,
            s.TargetCastId,
            s.TargetCastRemaining,
            flags,
            Pct(s.PlayerHpPercent),
            s.ActiveSlot != 0 ? Pct(s.PetHpPercent) : (byte)0,
            $"{Pct(s.Slot1PetHp)}.{Pct(s.Slot2PetHp)}.{Pct(s.Slot3PetHp)}",
            BST.LastDecisionActionId,
            BST.LastDecisionReason,
            BST.LastShadow,
            s.TargetTimeToDeath,
            (int)s.PlayerIntakePerSecond,
            s.TargetVulnerabilityRemaining,
            BST.PartyHpVerified,
            Svc.Targets.Target is { } tgt ? EdgeDistanceTo(tgt) : -1f,
            _moveSpeed);
    }

    private static unsafe BeastmasterTelemetryFormat.Snapshot Sample()
    {
        var gauge = BST.Gauge;

        // The familiar IS a pet buddy, the same path SCH/SMN use via HasPetPresent().
        ulong petId = 0;
        string? petName = null;
        uint petDataId = 0;

        var pet = Svc.Buddies.PetBuddy;
        if (pet?.GameObject is { } petObject)
        {
            petId = petObject.GameObjectId;
            petName = petObject.Name.TextValue;
            petDataId = petObject.BaseId;
        }

        // Beast Mode (44886) resolves to the concrete 44896-44903 variant for the active
        // Kinship, and Avalanche Axe (44884) becomes Brutal Rage (44930) at 50. Both are the
        // cheapest reliable read of state the gauge does not spell out.
        uint adjustedBeastMode = 0;
        uint adjustedAvalanche = 0;

        var actionManager = ActionManager.Instance();
        if (actionManager is not null)
        {
            adjustedBeastMode = actionManager->GetAdjustedActionId(BST.BeastMode);
            adjustedAvalanche = actionManager->GetAdjustedActionId(BST.AvalancheAxe);
        }

        StatusBuffer.Clear();
        if (Player.Object is { } player)
        {
            foreach (var status in player.StatusList)
            {
                var id = status.StatusId;
                if (id is >= BST.StatusRangeStart and <= BST.StatusRangeEnd)
                    StatusBuffer.Add((ushort)id);
            }
        }

        return new BeastmasterTelemetryFormat.Snapshot(
            gauge.TPGauge,
            gauge.FamiliarTPGauge,
            gauge.FamiliarTPAtLastUse,
            gauge.ActiveBattlehorn,
            gauge.InstinctualComboState,
            (byte)gauge.CurrentAffinity,
            gauge.ChainCount,
            gauge.KinshipState,
            petId,
            petName,
            petDataId,
            adjustedBeastMode,
            adjustedAvalanche,
            StatusBuffer,
            BST.LastDecisionActionId,
            BST.LastDecisionReason,
            BST.FamiliarDeclineReason,
            gauge.InstinctStacks,
            BST.LastSlotBeasts,
            (byte)(Player.Object?.Level ?? 0));
    }
}
