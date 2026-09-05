using System;

namespace AutoPotion;

/// <summary>
///     Optional, off-by-default potion-decision tap (v0.2.4.0).<br />
///     When <see cref="Configuration.DecisionTelemetry"/> is on, one structured
///     line is written at Information level through the normal plugin logger for
///     every potion AutoPotion fires, plus a gated line for the near-misses:
///     <c>PT|unixms|job|ev|itemId|hpPct|hpThr|mpPct|mpThr|inCombat|inDuty|deepDungeon|reason|item</c>.<br />
///     It rides the existing dalamud.log → ffxivdb <c>plugin_log_lines</c> harvest
///     (no transport of its own) and is joined to <c>player_samples</c> and
///     <c>deaths</c> by timestamp, which is what turns "is HpPotionThreshold=60 too
///     late?" from a guess into a query.
/// </summary>
/// <remarks>
///     <b>Cost when off:</b> a single bool read in <see cref="PotionService.Tick"/>;
///     nothing else runs — not even the near-miss reason classification.<br />
///     <b>Volume:</b> fires are always emitted (they are rare and each one matters);
///     near-misses go through <see cref="PotionTelemetryFormat.ShouldEmitNearMiss"/>,
///     which emits only on a (job, reason) CHANGE and never faster than
///     <see cref="PotionTelemetryFormat.NearMissMinIntervalMs"/>. The plugin-off
///     gates (MasterEnable, no LocalPlayer, dead, out of combat, out of duty) never
///     emit anything at all — they are not decisions.<br />
///     The line format and the gate live in <see cref="PotionTelemetryFormat"/> so
///     they can be asserted offline by <c>tests/AutoPotion.TelemetryHarness</c>.
/// </remarks>
internal static class PotionTelemetry
{
    /// <inheritdoc cref="PotionTelemetryFormat.Prefix"/>
    public const string Prefix = PotionTelemetryFormat.Prefix;

    private static readonly PotionTelemetryFormat.NearMissGate Gate = new();

    /// <summary> Forgets the last near-miss (toggle-on, so the first state re-emits). </summary>
    public static void Reset() => Gate.Reset();

    /// <summary>
    ///     Nothing was wrong this tick — no threshold crossed. Clears the remembered
    ///     near-miss reason (so a later dip back into it is reported again) without
    ///     writing anything. This is the ordinary healthy tick and must stay silent.
    /// </summary>
    public static void NoteResolved() => Gate.NoteResolved();

    /// <summary>
    ///     Emits a potion-fired line. Never gated: a potion use is rare and every one
    ///     is a graded event.
    /// </summary>
    public static void RecordFire(char ev, uint itemId, string itemName, in PotionSnapshot snap)
    {
        Gate.NoteResolved();
        Emit(ev, itemId, itemName, PotionTelemetryFormat.ReasonOk, snap, gated: false);
    }

    /// <summary>
    ///     Offers a near-miss for emission. Drops it unless the (job, reason) pair
    ///     changed and the rate-limit window has passed.
    /// </summary>
    public static void RecordNearMiss(string reason, in PotionSnapshot snap)
    {
        Emit(PotionTelemetryFormat.EvNearMiss, 0, null, reason, snap, gated: true);
    }

    private static void Emit(char ev, uint itemId, string? itemName, string reason, in PotionSnapshot snap, bool gated)
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (gated && !PotionTelemetryFormat.ShouldEmitNearMiss(Gate, snap.Job, reason, now))
                return;

            Plugin.Log.Information(PotionTelemetryFormat.BuildLine(
                now, snap.Job, ev, itemId,
                snap.HpPct, snap.HpThreshold, snap.MpPct, snap.MpThreshold,
                snap.InCombat, snap.InDuty, snap.DeepDungeon,
                reason, itemName));
        }
        catch (Exception ex)
        {
            // A telemetry tap must never be able to break the plugin.
            Plugin.Log.Debug(ex, "[PotionTelemetry] failed to emit a decision line");
        }
    }
}

/// <summary>
///     The game state one Tick() evaluated, gathered once and shared by the fire and
///     near-miss paths so a line can never disagree with the decision that produced it.
///     Built only when <see cref="Configuration.DecisionTelemetry"/> is on.
/// </summary>
internal readonly struct PotionSnapshot
{
    public readonly uint Job;
    public readonly float HpPct;
    public readonly float HpThreshold;
    public readonly float MpPct;
    public readonly float MpThreshold;
    public readonly bool InCombat;
    public readonly bool InDuty;
    public readonly bool DeepDungeon;

    public PotionSnapshot(
        uint job, float hpPct, float hpThreshold, float mpPct, float mpThreshold,
        bool inCombat, bool inDuty, bool deepDungeon)
    {
        Job = job;
        HpPct = hpPct;
        HpThreshold = hpThreshold;
        MpPct = mpPct;
        MpThreshold = mpThreshold;
        InCombat = inCombat;
        InDuty = inDuty;
        DeepDungeon = deepDungeon;
    }
}
