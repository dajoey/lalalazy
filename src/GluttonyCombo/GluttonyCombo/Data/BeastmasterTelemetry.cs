using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using GluttonyCombo.Combos.PvE;
using GluttonyCombo.Core;
using System;
using System.Collections.Generic;

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

    /// <summary> Reusable status buffer; the collector runs on the framework thread only. </summary>
    private static readonly List<ushort> StatusBuffer = [];

    /// <summary> Forgets the remembered snapshot, so the current state re-emits immediately. </summary>
    public static void Reset() => _gate.Reset();

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

            if (!BeastmasterTelemetryFormat.ShouldEmit(ref _gate, now, snapshot))
                return;

            Svc.Log.Information(BeastmasterTelemetryFormat.BuildLine(now, snapshot));
        }
        catch (Exception ex)
        {
            // A collector must never be able to break the framework tick.
            Svc.Log.Debug(ex, "[BeastmasterTelemetry] failed to emit a collector line");
        }
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
            petDataId = petObject.DataId;
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
            gauge.InstinctStacks);
    }
}
