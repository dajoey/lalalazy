// FakeGame: the mutable stand-in for the game + Dalamud state that the real Astrologian decision code
// reads through CustomComboFunctions. Every knob here is set by a harness case; defaults describe a
// level-100 Astrologian in combat, standing still (casting), on a living target, everything off
// cooldown except where noted, no buffs, no cards drawn.
// NOTE: this file is harness-owned (tests/ only). It must never be linked into the plugin.

using Dalamud.Game.ClientState.JobGauge.Enums;
using Dalamud.Game.ClientState.JobGauge.Types;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GluttonyCombo.RotationHarness.AST2;

internal static class FakeGame
{
    // ---- player / combat ----
    public static int Level = 100;
    public static bool AllTraitsKnown = true;
    public static bool InCombat = true;
    public static bool CanWeave = true;                  // AST oGCD block requires a weave window
    public static bool InMeleeRange = true;
    public static bool HasBattleTarget = true;
    public static bool TargetIsBoss = false;
    public static bool InBossEncounter = false;
    public static bool GroupDamageIncoming = false;
    public static float TargetHPPercent = 100f;
    public static bool TargetCanApplyStatus = false;     // NeedsDoT / DottableEnemy paths stay closed
    public static bool InActionRange = true;             // every action in range unless overridden
    public static HashSet<uint> OutOfRangeActions = [];

    // ---- movement / timing (AST reads these directly) ----
    public static bool IsMovingFlag = false;
    public static float TimeStoodStillSeconds = 5f;      // StandStill => >= 3s
    public static int NumberOfGcdsUsed = 0;              // ActionWatching.NumberOfGcdsUsed fake
    public static bool PartyInCombatFlag = true;
    public static bool IsInPartyFlag = false;
    public static bool PartyIsBurstingFlag = false;    // Bursting.PartyIsBursting fake

    // ---- role actions / raidwides ----
    public static bool CanLucidDream = false;            // Role.CanLucidDream fake
    public static bool OccultInstantCast = false;
    public static bool OccultDualcast = false;
    public static float PartyAvgHP = 100f;

    // ---- combo / cooldowns / weave bookkeeping ----
    public static float ComboTimer = 0f;
    public static uint ComboActionId = 0;
    public static bool CountdownActive = false;
    public static float CountdownRemaining = 0f;
    public static Dictionary<uint, (float UsedAgo, float Window)> JustUsedActions = [];
    public static Dictionary<uint, GluttonyCombo.Data.CooldownData> Cooldowns = [];
    public static HashSet<uint> NotLearned = [];         // empty => everything learned
    public static Dictionary<uint, uint> HookOverrides = []; // OriginalHook map; identity by default

    // ---- presets / one-button rotation ----
    public static HashSet<GluttonyCombo.Combos.Preset> EnabledPresets = [];
    public static bool OneButtonRotation = true;         // CustomActionHelper.OneButtonRotationChecker
    public static bool CustomActionEnabled = false;      // CustomActionHelper.CustomActionEnabled

    // ---- statuses (the fake player's status list) ----
    public static List<FakeStatus> Statuses = [];

    // ---- target-side statuses (DoT debuffs on the current target; keyed by status id) ----
    public static Dictionary<ushort, FakeTargetStatus> TargetStatuses = [];

    // ---- config values set by cases (name => value; fall back to the mirrored default) ----
    public static Dictionary<string, bool> BoolValues = [];
    public static Dictionary<string, int> IntValues = [];
    public static Dictionary<string, float> FloatValues = [];
    public static Dictionary<string, int[]> IntArrayValues = [];
    public static Dictionary<string, bool[]> BoolArrayValues = [];

    // ---- targeting ----
    public static Dalamud.Game.ClientState.Objects.Types.IGameObject? UiMouseOver;
    public static Dalamud.Game.ClientState.Objects.Types.IGameObject? HardTargetObject;

    public static bool GetBool(string name, bool def) => BoolValues.GetValueOrDefault(name, def);

    public static int GetInt(string name, int def) => IntValues.GetValueOrDefault(name, def);

    public static float GetFloat(string name, float def) => FloatValues.GetValueOrDefault(name, def);

    public static int[] GetIntArray(string name, int[] def)
    {
        if (!IntArrayValues.TryGetValue(name, out var v))
        {
            v = (int[])def.Clone();
            IntArrayValues[name] = v;
        }

        return v;
    }

    public static bool[] GetBoolArray(string name, bool[] def)
    {
        if (!BoolArrayValues.TryGetValue(name, out var v))
        {
            v = (bool[])def.Clone();
            BoolArrayValues[name] = v;
        }

        return v;
    }

    /// <summary>The cooldown model for an action, created ready-on-demand.</summary>
    public static GluttonyCombo.Data.CooldownData Cooldown(uint actionID)
    {
        if (!Cooldowns.TryGetValue(actionID, out var cd))
        {
            cd = new GluttonyCombo.Data.CooldownData { ActionID = actionID };
            Cooldowns[actionID] = cd;
        }

        return cd;
    }

    /// <summary>Reset to the default state (used between cases).</summary>
    public static void Reset()
    {
        Level = 100;
        AllTraitsKnown = true;
        InCombat = true;
        CanWeave = true;
        InMeleeRange = true;
        HasBattleTarget = true;
        TargetIsBoss = false;
        InBossEncounter = false;
        GroupDamageIncoming = false;
        TargetHPPercent = 100f;
        TargetCanApplyStatus = false;
        InActionRange = true;
        OutOfRangeActions = [];
        IsMovingFlag = false;
        TimeStoodStillSeconds = 5f;
        NumberOfGcdsUsed = 0;
        PartyInCombatFlag = true;
        IsInPartyFlag = false;
        PartyIsBurstingFlag = false;
        CanLucidDream = false;
        OccultInstantCast = false;
        OccultDualcast = false;
        PartyAvgHP = 100f;
        ComboTimer = 0f;
        ComboActionId = 0;
        CountdownActive = false;
        CountdownRemaining = 0f;
        JustUsedActions = [];
        Cooldowns = [];
        NotLearned = [];
        HookOverrides = [];
        EnabledPresets = [];
        OneButtonRotation = true;
        CustomActionEnabled = false;
        Statuses = [];
        TargetStatuses = [];
        BoolValues = [];
        IntValues = [];
        FloatValues = [];
        IntArrayValues = [];
        BoolArrayValues = [];
        UiMouseOver = null;
        HardTargetObject = null;
        GluttonyCombo.AutoRotation.AutoRotationController.RaidwideMitOnCooldown = true;
        FakeGauges.ZeroAll();
    }
}

/// <summary>One status effect on the fake player.</summary>
internal sealed record FakeStatus(uint Id, float RemainingTime, ushort Param = 0, bool Own = true)
{
    public float RemainingTimeOrZero(bool checkAnimationLock = true) => RemainingTime;
}

/// <summary>
///     A target-side debuff (e.g. the Combust DoT) as an <c>IStatus</c>, so the real
///     <c>CurrentTarget.Status(id).RemainingTimeOrZero()</c> path reads a case-set remaining time.
/// </summary>
internal sealed record FakeTargetStatus(uint StatusId, float RemainingTime)
    : Dalamud.Game.ClientState.Statuses.IStatus
{
    public uint SourceId => 0;
    public byte StackCount => 0;
    public ushort Param => 0;
    public bool IsSourcePlayer => true;
    public Dalamud.Game.ClientState.Objects.Types.IGameObject? SourceObject => null;
    public nint Address => 0;
    public Lumina.Excel.RowRef<Lumina.Excel.Sheets.Status> GameData => default;

    public bool Equals(Dalamud.Game.ClientState.Statuses.IStatus? other) =>
        other is FakeTargetStatus f && f == this;
}

/// <summary>The fake LocalPlayer: the only surface the real AST code uses is HasStatus/Status.</summary>
internal sealed class FakePlayer
{
    public bool HasStatus(uint id, bool anyOwner = false) => Status(id, anyOwner) is not null;

    public FakeStatus? Status(uint id, bool anyOwner = false)
    {
        foreach (var s in FakeGame.Statuses)
        {
            if (s.Id != id) continue;
            if (!anyOwner && !s.Own) continue;
            return s;
        }

        return null;
    }
}

/// <summary>
///     Real Dalamud job-gauge objects over harness-owned unmanaged memory (same mechanism as the VPR
///     spike). ASTGauge's scalar properties (e.g. DrawnCrownCard) are probed by <see cref="FakeGauges"/>;
///     the <c>DrawnCards</c> span cannot be probed that way (spans do not box), so
///     <see cref="FakeAstCards"/> discovers each card slot's byte offset by writing one byte at a time
///     and watching <c>DrawnCards[i]</c> move. A Dalamud layout change can only make the probes find new
///     offsets (printed as evidence each run), not silently fake wrong values.
/// </summary>
internal static class FakeGauges
{
    private static readonly nint Mem = Marshal.AllocHGlobal(64);
    private static readonly Dictionary<Type, object> Cache = [];
    private static readonly Dictionary<(Type Type, string Prop), int> OffsetMap = [];

    public static T Get<T>() where T : JobGaugeBase
    {
        if (!Cache.TryGetValue(typeof(T), out var g))
        {
            var ctor = typeof(T).GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, binder: null, [typeof(IntPtr)], modifiers: null)
                       ?? throw new MissingMethodException($"{typeof(T).Name}(IntPtr)");
            ZeroAll();
            g = ctor.Invoke([Mem])!;
            Cache[typeof(T)] = g;
            Probe<T>(g);
        }

        return (T)g;
    }

    public static unsafe void ZeroAll()
    {
        for (var i = 0; i < 64; i++)
            *(byte*)(Mem + i) = 0;
    }

    public static unsafe void WriteByte(int offset, byte value)
    {
        *(byte*)(Mem + offset) = value;
    }

    public static unsafe byte ReadByte(int offset) => *(byte*)(Mem + offset);

    public static unsafe void SetByte<T>(string prop, byte value) where T : JobGaugeBase
    {
        _ = Get<T>(); // ensure constructed + probed

        if (!OffsetMap.TryGetValue((typeof(T), prop), out var off))
            throw new KeyNotFoundException($"no gauge byte found for {typeof(T).Name}.{prop}");

        *(byte*)(Mem + off) = value;
    }

    public static IReadOnlyDictionary<(Type Type, string Prop), int> Offsets => OffsetMap;

    private static unsafe void Probe<T>(object gauge)
    {
        // Only scalar value props (enums / numerics): arrays and pointers are always "non-zero" and
        // would poison the map (ASTGauge.DrawnCards is a CardType[], JobGaugeBase.Address an IntPtr).
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => (p.PropertyType.IsEnum || p.PropertyType.IsPrimitive) && p.PropertyType != typeof(IntPtr))
            .ToList();
        for (var off = 0; off < 32; off++)
        {
            *(byte*)(Mem + off) = 1;
            foreach (var p in props)
            {
                object v;
                try
                {
                    v = p.GetValue(gauge)!;
                }
                catch
                {
                    continue;
                }

                if (!IsZero(v))
                    OffsetMap[(typeof(T), p.Name)] = off;
            }

            *(byte*)(Mem + off) = 0;
        }
    }

    private static bool IsZero(object v) => v switch
    {
        byte b => b == 0,
        sbyte b => b == 0,
        short s => s == 0,
        int i => i == 0,
        long l => l == 0,
        Enum e => Convert.ToInt64(e) == 0,
        _ => false,
    };
}

/// <summary>
///     The AST card/crown byte map, discovered at startup (never pinned). The 7.3 ASTGauge packs each
///     CardType into a NIBBLE (observed live: slot0 = byte8 low, slot1 = byte8 high, slot2 = byte9 low,
///     crown = byte9 high, ActiveDraw = byte10), so the probe writes each nibble pattern per byte and
///     watches which slot moves. A Dalamud layout change can only make the probes find new offsets,
///     which the evidence line prints every run.
/// </summary>
internal static class FakeAstCards
{
    private static Dictionary<int, (int Offset, int Shift)>? _slotMap;
    private static (int Offset, int Shift)? _crown;

    public static IReadOnlyDictionary<int, (int Offset, int Shift)> SlotMap
    {
        get
        {
            EnsureProbed();
            return _slotMap!;
        }
    }

    public static (int Offset, int Shift) Crown
    {
        get
        {
            EnsureProbed();
            if (_crown is null)
                throw new KeyNotFoundException("no gauge nibble found for ASTGauge.DrawnCrownCard");
            return _crown.Value;
        }
    }

    public static void SetCard(int slot, CardType type)
    {
        EnsureProbed();
        if (!_slotMap!.TryGetValue(slot, out var nib))
            throw new KeyNotFoundException($"no gauge nibble found for ASTGauge.DrawnCards[{slot}]");
        WriteNibble(nib.Offset, nib.Shift, (byte)type);
    }

    public static void SetCrown(CardType type) => WriteNibble(Crown.Offset, Crown.Shift, (byte)type);

    private static unsafe void WriteNibble(int offset, int shift, byte value)
    {
        var b = FakeGauges.ReadByte(offset);
        var mask = (byte)(0x0F << shift);
        FakeGauges.WriteByte(offset, (byte)((b & ~mask) | ((value & 0x0F) << shift)));
    }

    private static void EnsureProbed()
    {
        if (_slotMap != null)
            return;

        _slotMap = new Dictionary<int, (int, int)>();
        var gauge = FakeGauges.Get<ASTGauge>();
        FakeGauges.ZeroAll();

        for (var off = 0; off < 32; off++)
        {
            foreach (var shift in new[] { 0, 4 })
            {
                var pattern = (byte)(0x0F << shift); // a card value in exactly one nibble
                FakeGauges.WriteByte(off, pattern);

                for (var slot = 0; slot < 3; slot++)
                {
                    CardType v;
                    try
                    {
                        v = gauge.DrawnCards[slot];
                    }
                    catch
                    {
                        continue; // array shorter than this slot index
                    }

                    if (v != CardType.None && !_slotMap.ContainsKey(slot))
                        _slotMap[slot] = (off, shift);
                }

                if (gauge.DrawnCrownCard != CardType.None && _crown is null)
                    _crown = (off, shift);

                FakeGauges.WriteByte(off, 0);
            }
        }
    }
}
