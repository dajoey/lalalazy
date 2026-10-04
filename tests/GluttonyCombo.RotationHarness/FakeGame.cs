// FakeGame: the mutable stand-in for the game + Dalamud state that the real Viper decision code reads
// through CustomComboFunctions. Every knob here is set by a harness case; defaults describe a level-100
// Viper in combat on a positional-needing target with everything off cooldown and no buffs.
// NOTE: this file is harness-owned (tests/ only). It must never be linked into the plugin.

using Dalamud.Game.ClientState.JobGauge.Types;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GluttonyCombo.RotationHarness;

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

    // ---- combo / cooldowns / weave bookkeeping ----
    public static float ComboTimer = 0f;
    public static uint ComboActionId = 0;
    public static bool CountdownActive = false;
    public static float CountdownRemaining = 0f;
    public static Dictionary<uint, (float UsedAgo, float Window)> JustUsedActions = [];
    public static Dictionary<uint, GluttonyCombo.Data.CooldownData> Cooldowns = [];
    public static HashSet<uint> NotLearned = [];         // empty => everything learned
    public static Dictionary<uint, uint> HookOverrides = []; // OriginalHook map; identity by default

    // ---- presets / role actions ----
    public static HashSet<GluttonyCombo.Combos.Preset> EnabledPresets = [];
    public static bool OneButtonRotation = true;         // CustomActionHelper.OneButtonRotationChecker
    public static bool RoleActionReady = true;           // Feint/SecondWind/Bloodbath/TrueNorth
    public static bool LegSweepReady = true;

    // ---- statuses (the fake player's status list) ----
    public static List<FakeStatus> Statuses = [];

    // ---- config values set by cases (name => value; fall back to the mirrored default) ----
    public static Dictionary<string, bool> BoolValues = [];
    public static Dictionary<string, int> IntValues = [];
    public static Dictionary<string, float> FloatValues = [];

    // ---- targeting ----
    public static object? UiMouseOver = null;
    public static object? HardTarget = null;

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
        RoleActionReady = true;
        LegSweepReady = true;
        Statuses = [];
        BoolValues = [];
        IntValues = [];
        FloatValues = [];
        UiMouseOver = null;
        HardTarget = null;
        FakeGauges.ZeroAll();
    }
}

/// <summary>One status effect on the fake player.</summary>
internal sealed record FakeStatus(uint Id, float RemainingTime, ushort Param = 0, bool Own = true)
{
    public float RemainingTimeOrZero(bool checkAnimationLock = true) => RemainingTime;
}

/// <summary>The fake LocalPlayer: the only surface the real Viper code uses is HasStatus/Status.</summary>
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
///     Real Dalamud job-gauge objects over harness-owned unmanaged memory. VPRGauge has a public
///     VPRGauge(IntPtr) ctor; each property reads one byte of the pointed-to memory. The harness derives
///     the property-to-offset map by probing at startup (write one byte at a time, see which property
///     moves), so a Dalamud layout change cannot silently mis-fake a gauge.
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
            var ctor = typeof(T).GetConstructor([typeof(IntPtr)])
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
        for (var off = 0; off < 16; off++)
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
