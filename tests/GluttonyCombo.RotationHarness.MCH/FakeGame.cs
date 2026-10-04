// FakeGame: the mutable stand-in for the game + Dalamud state that the real Machinist decision code
// reads through CustomComboFunctions. Every knob here is set by a harness case; defaults describe a
// level-100 Machinist in combat on a living target with everything off cooldown and no buffs.
// NOTE: this file is harness-owned (tests/ only). It must never be linked into the plugin.

using Dalamud.Game.ClientState.JobGauge.Types;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GluttonyCombo.RotationHarness.MCH;

internal static class FakeGame
{
    // ---- player / combat ----
    public static int Level = 100;
    public static bool AllTraitsKnown = true;
    public static bool InCombat = true;
    public static bool CanWeave = false;                 // false => skip the oGCD block entirely
    public static bool InMeleeRange = true;
    public static bool HasBattleTarget = true;
    public static bool TargetIsBoss = false;
    public static bool InBossEncounter = false;
    public static bool GroupDamageIncoming = false;
    public static float TargetHPPercent = 100f;
    public static bool TargetNeedsPositionals = true;
    public static bool OnTargetsFlank = false;
    public static bool OnTargetsRear = false;
    public static bool InActionRange = true;             // every action in range unless overridden
    public static HashSet<uint> OutOfRangeActions = [];

    // ---- enemy / ally geometry (MCH's AoE decisions read these) ----
    public static int EnemiesInRange = 1;
    public static int AlliesInRange = 0;
    public static int PartySize = 4;

    // ---- movement ----
    public static bool IsMoving = false;
    public static TimeSpan TimeStoodStill = TimeSpan.FromSeconds(10);

    // ---- combo / cooldowns / weave bookkeeping ----
    public static float ComboTimer = 0f;
    public static uint ComboActionId = 0;
    public static bool CountdownActive = false;
    public static float CountdownRemaining = 0f;
    public static Dictionary<uint, (float UsedAgo, float Window)> JustUsedActions = [];
    public static Dictionary<uint, GluttonyCombo.Data.CooldownData> Cooldowns = [];
    public static HashSet<uint> NotLearned = [];         // empty => everything learned
    public static Dictionary<uint, uint> HookOverrides = []; // OriginalHook map; identity by default
    public static int HasWeavedCount = 0;

    // ---- presets / role actions ----
    public static HashSet<GluttonyCombo.Combos.Preset> EnabledPresets = [];
    public static bool OneButtonRotation = true;         // CustomActionHelper.OneButtonRotationChecker
    public static bool RoleActionReady = true;           // SecondWind/HeadGraze role actions

    // ---- statuses (the fake player's and the fake target's status lists) ----
    public static List<FakeStatus> Statuses = [];
    public static List<FakeStatus> TargetStatuses = [];
    public static bool TargetCanApplyStatus = true;

    // ---- config values set by cases (name => value; fall back to the mirrored default) ----
    public static Dictionary<string, bool> BoolValues = [];
    public static Dictionary<string, int> IntValues = [];
    public static Dictionary<string, float> FloatValues = [];

    public static bool GetBool(string name, bool def) => BoolValues.GetValueOrDefault(name, def);

    public static int GetInt(string name, int def) => IntValues.GetValueOrDefault(name, def);

    public static float GetFloat(string name, float def) => FloatValues.GetValueOrDefault(name, def);

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
        CanWeave = false;
        InMeleeRange = true;
        HasBattleTarget = true;
        TargetIsBoss = false;
        InBossEncounter = false;
        GroupDamageIncoming = false;
        TargetHPPercent = 100f;
        TargetNeedsPositionals = true;
        OnTargetsFlank = false;
        OnTargetsRear = false;
        InActionRange = true;
        OutOfRangeActions = [];
        EnemiesInRange = 1;
        AlliesInRange = 0;
        PartySize = 4;
        IsMoving = false;
        TimeStoodStill = TimeSpan.FromSeconds(10);
        ComboTimer = 0f;
        ComboActionId = 0;
        CountdownActive = false;
        CountdownRemaining = 0f;
        JustUsedActions = [];
        Cooldowns = [];
        NotLearned = [];
        HookOverrides = [];
        HasWeavedCount = 0;
        EnabledPresets = [];
        OneButtonRotation = true;
        RoleActionReady = true;
        Statuses = [];
        TargetStatuses = [];
        TargetCanApplyStatus = true;
        BoolValues = [];
        IntValues = [];
        FloatValues = [];
        FakeGauges.ZeroAll();
    }
}

/// <summary>One status effect on the fake player or the fake current target.</summary>
internal sealed record FakeStatus(uint Id, float RemainingTime, ushort Param = 0, bool Own = true)
{
    public float RemainingTimeOrZero(bool checkAnimationLock = true) => RemainingTime;
}

/// <summary>The fake LocalPlayer: the surface the real MCH code uses is HasStatus/Status/HasStatusEffects.</summary>
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

    public bool HasStatusEffects(ushort[] effectIds, bool anyOwner = false) =>
        effectIds.Any(id => HasStatus(id, anyOwner));
}

/// <summary>The fake current target: the surface the real MCH code uses is HasStatus/Status/CanApplyStatus.</summary>
internal sealed class FakeTarget
{
    public bool HasStatus(uint id, bool anyOwner = false) => Status(id, anyOwner) is not null;

    public FakeStatus? Status(uint id, bool anyOwner = false)
    {
        foreach (var s in FakeGame.TargetStatuses)
        {
            if (s.Id != id) continue;
            if (!anyOwner && !s.Own) continue;
            return s;
        }

        return null;
    }

    public bool CanApplyStatus(uint statusId) => FakeGame.TargetCanApplyStatus;
}

/// <summary>
///     Real Dalamud job-gauge objects over harness-owned unmanaged memory (the VPR spike pattern).
///     MCHGauge has a public MCHGauge(IntPtr) ctor; its properties (Heat, Battery, IsOverheated,
///     IsRobotActive, timers) read bytes of the pointed-to memory. The harness derives the
///     property-to-offset map by probing at startup (write one byte at a time, see which property
///     moves), so a Dalamud layout change cannot silently mis-fake a gauge. Unlike the VPR spike,
///     MCHGauge's decision-critical members include BOOLS (IsOverheated, IsRobotActive): the probe
///     treats a bool as its backing byte, and the byte range is probed to 31 because the MCH gauge
///     fields sit behind the JobGaugeBase header.
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
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
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
        bool b => !b,
        byte b => b == 0,
        sbyte b => b == 0,
        short s => s == 0,
        int i => i == 0,
        long l => l == 0,
        Enum e => Convert.ToInt64(e) == 0,
        _ => false,
    };
}
