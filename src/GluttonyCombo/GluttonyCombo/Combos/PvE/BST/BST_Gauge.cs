using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Runtime.InteropServices;
namespace GluttonyCombo.Combos.PvE;

// Beastmaster gauge, VENDORED from FFXIVClientStructs PR #1947 ("Add BeastmasterGauge",
// aers/FFXIVClientStructs), which was NOT merged as of 2026-09-09 - the vendoring date.
// Dalamud 15.0.3.4 ships no BSTGauge type and Svc.Gauges cannot return one, so the struct is
// overlaid on the raw gauge pointer instead. Field offsets, sizes and the KinshipState nibble
// split are copied verbatim from that PR (which documents them as runtime-verified on 7.56).
//
// RE-PIN ON MERGE: when PR #1947 lands and Dalamud picks it up, delete this overlay and read
// FFXIVClientStructs.FFXIV.Client.Game.Gauge.BeastmasterGauge (or Svc.Gauges) instead.
//
// Layout note: ClientStructs gauge structs model the gauge OBJECT including its 8-byte vtable
// header, which is why every field offset starts at 0x08 (cf. ViperGauge, ScholarGauge in
// Gauge/JobGauges.cs). JobGaugeManager.CurrentGauge points at that object, so casting it to
// this overlay lines the fields up exactly.
//
// BeastmasterAffinity / BeastmasterKinType (t_02fe2681: split into BST_Enums.cs so the
// rotation logic can compile Dalamud-free into tests/GluttonyCombo.BSTRotationHarness).

/// <summary> Vendored copy of ClientStructs PR #1947's <c>BeastmasterGauge</c>. </summary>
[StructLayout(LayoutKind.Explicit, Size = 0x11)]
public struct BeastmasterGaugeOverlay
{
    /// <summary> Beastmaster's own TP, 0-250. No passive regen; granted by the weaponskill combo. </summary>
    [FieldOffset(0x08)] public byte TPGauge;

    /// <summary> Familiar TP, 0-250. One pool shared across Battlehorn slots; +12 per familiar auto-attack. </summary>
    [FieldOffset(0x09)] public byte FamiliarTPGauge;

    /// <summary> Familiar TP immediately before the most recent familiar action (that action spends the whole pool). </summary>
    [FieldOffset(0x0A)] public byte FamiliarTPAtLastUse;

    /// <summary> Which Battlehorn SLOT is summoned (1-3), 0 when none. The creature identity is NOT in the gauge. </summary>
    [FieldOffset(0x0B)] public byte ActiveBattlehorn;

    /// <summary> Active affinity, or 7 while Wavering Heart (status 4643) locks out combo advancement. </summary>
    [FieldOffset(0x0C)] public byte InstinctualComboState;

    /// <summary> Affinity of the most recent instinctual skill; cleared ~7s after the last skill. </summary>
    [FieldOffset(0x0D)] public BeastmasterAffinity CurrentAffinity;

    /// <summary> Instinctual skills chained so far (shown under the Inner Compass). </summary>
    [FieldOffset(0x0E)] public byte ChainCount;

    /// <summary> High nibble = kin type, low nibble = the Battlehorn slot latched when Borrow was used. </summary>
    [FieldOffset(0x0F)] public byte KinshipState;

    /// <summary>
    ///     Byte 0x10: low 2 bits = Natural Instinct stacks (familiar side, spent by Rallying
    ///     Cheer), bits 2-3 = Mastered Instinct stacks (player side, spent by Rally).
    ///     Mapping taken from WrathCombo's WIP Beastmaster work (remote branch
    ///     mrbeastmaster, TmpBSTGauge in Data/JobGaugeDebugging.cs, read 2026-09-11) -
    ///     an INDEPENDENT implementation of the same gauge, which also re-derives
    ///     0x08-0x0F exactly as this overlay does. That branch is unmerged WIP, so treat
    ///     the two nibbles as provisionally mapped until a live BT| sample confirms
    ///     stack counts moving with completed instinctual combos (grade against real
    ///     play before hard-coding behaviour on it).
    /// </summary>
    [FieldOffset(0x10)] public byte InstinctStacks;

    public BeastmasterKinType KinshipKinType => (BeastmasterKinType)(KinshipState >> 4);

    public byte KinshipBattlehorn => (byte)(KinshipState & 0x0F);

    /// <summary> Mastered Instinct stacks (player side, 0-3) - spent by Rally (44905). </summary>
    public int MasterInstinct => (InstinctStacks & 0xC) >> 2;

    /// <summary> Natural Instinct stacks (familiar side, 0-3) - spent by Rallying Cheer (44904). </summary>
    public int PetInstinct => InstinctStacks & 0x3;
}

internal partial class BST
{
    /// <summary>
    ///     A safe snapshot of the Beastmaster gauge: all zeros when the local player is not on
    ///     BST, when the gauge manager is unavailable, or when the gauge pointer is null.
    /// </summary>
    /// <remarks>
    ///     Reads <see cref="BeastmasterGaugeOverlay"/> off
    ///     <c>JobGaugeManager.Instance()-&gt;CurrentGauge</c> because the struct is not part of
    ///     Dalamud's <c>Svc.Gauges</c> wrapper yet. Guarded on BOTH the ECommons job and the
    ///     manager's own <c>ClassJobId</c>, so a stale gauge for the previous job can never be
    ///     read as Beastmaster state.
    /// </remarks>
    internal static unsafe BeastmasterGaugeOverlay Gauge
    {
        get
        {
            if (Player.Job is not Job.BST)
                return default;

            var manager = JobGaugeManager.Instance();
            if (manager is null || manager->ClassJobId != (byte)Job.BST)
                return default;

            var current = manager->CurrentGauge;
            return current is null ? default : *(BeastmasterGaugeOverlay*)current;
        }
    }
}
