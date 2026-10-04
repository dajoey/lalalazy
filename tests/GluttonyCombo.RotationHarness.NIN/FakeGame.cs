// FakeGame: the mutable stand-in for the game + Dalamud state that the real Ninja decision code
// reads through CustomComboFunctions. Derived from the VPR spike's FakeGame.cs. Every knob here is
// set by a harness case; defaults describe a level-100 Ninja in combat on a living hostile target
// with everything learned, no buffs on the player, and no cooldowns set.
// NOTE: this file is harness-owned (tests/ only). It must never be linked into the plugin.

using Dalamud.Game.ClientState.JobGauge.Types;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GluttonyCombo.RotationHarness.NIN;

internal static class FakeGame
{
    // ---- player / combat ----
    public static int Level = 100;
    public static bool AllTraitsKnown = true;
    public static bool InCombat = true;
    public static bool CanWeave = false;
    public static bool CanDelayedWeave = false;          // Mug's delayed-weave gate (real: Action.cs:353)
    public static bool InMeleeRange = true;
    public static bool HasBattleTarget = true;
    public static bool TargetIsBoss = false;
    public static bool InBossEncounter = false;
    public static bool GroupDamageIncoming = false;
    public static float TargetHPPercent = 100f;
    public static float HealthPercent = 100f;            // PlayerHealthPercentageHp()
    public static float TargetDistance = 3f;             // GetTargetDistance()
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
    public static uint LastAction = 0;                   // ActionWatching.LastAction / WasLastAction
    public static TimeSpan TimeStoodStill = TimeSpan.Zero;   // real: Movement.cs:51
    public static TimeSpan CombatEngageDuration = TimeSpan.FromSeconds(30); // real: Timer.cs:53
    public static Dictionary<uint, (float UsedAgo, float Window)> JustUsedActions = [];
    public static Dictionary<uint, GluttonyCombo.Data.CooldownData> Cooldowns = [];
    public static HashSet<uint> NotLearned = [];         // empty => everything learned
    public static Dictionary<uint, uint> HookOverrides = []; // OriginalHook map; identity by default

    // ---- enemies in range (NumberOfEnemiesInRange, real Target.cs:422) ----
    public static Dictionary<uint, int> EnemyCountsByAction = [];
    public static int DefaultEnemyCount = 1;             // the current target

    public static int NumberOfEnemiesInRange(uint aoeSpell) =>
        EnemyCountsByAction.GetValueOrDefault(aoeSpell, DefaultEnemyCount);

    // ---- presets / role actions ----
    public static HashSet<GluttonyCombo.Combos.Preset> EnabledPresets = [];
    public static bool OneButtonRotation = true;         // CustomActionHelper.OneButtonRotationChecker
    public static bool RoleActionReady = true;           // Feint/SecondWind/Bloodbath/TrueNorth
    public static bool LegSweepReady = true;

    // ---- statuses (the fake player's status list) ----
    public static List<FakeStatus> Statuses = [];

    // ---- the fake current target (separate status list: Kunai's Bane rides the target, not the player) ----
    public static FakeTarget CurrentTarget { get; } = new();

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

    /// <summary>Put an action on cooldown with the given remaining seconds.</summary>
    public static void PutOnCooldown(uint actionID, float remaining)
    {
        var cd = Cooldown(actionID);
        cd.IsCooldown = true;
        cd.CooldownRemaining = remaining;
        cd.CooldownElapsed = 0f;
    }

    /// <summary>Reset to the default state (used between cases).</summary>
    public static void Reset()
    {
        Level = 100;
        AllTraitsKnown = true;
        InCombat = true;
        CanWeave = false;
        CanDelayedWeave = false;
        InMeleeRange = true;
        HasBattleTarget = true;
        TargetIsBoss = false;
        InBossEncounter = false;
        GroupDamageIncoming = false;
        TargetHPPercent = 100f;
        HealthPercent = 100f;
        TargetDistance = 3f;
        TargetNeedsPositionals = true;
        OnTargetsFlank = false;
        OnTargetsRear = false;
        InActionRange = true;
        OutOfRangeActions = [];
        ComboTimer = 0f;
        ComboActionId = 0;
        CountdownActive = false;
        CountdownRemaining = 0f;
        LastAction = 0;
        TimeStoodStill = TimeSpan.Zero;
        CombatEngageDuration = TimeSpan.FromSeconds(30);
        JustUsedActions = [];
        Cooldowns = [];
        NotLearned = [];
        HookOverrides = [];
        EnemyCountsByAction = [];
        DefaultEnemyCount = 1;
        EnabledPresets = [];
        OneButtonRotation = true;
        RoleActionReady = true;
        LegSweepReady = true;
        Statuses = [];
        CurrentTarget.Statuses = [];
        BoolValues = [];
        IntValues = [];
        FloatValues = [];
        UiMouseOver = null;
        HardTarget = null;
        FakeGauges.ZeroAll();
    }
}

/// <summary>One status effect on a fake player or target.</summary>
internal sealed record FakeStatus(uint Id, float RemainingTime, ushort Param = 0, bool Own = true)
{
    public float RemainingTimeOrZero(bool checkAnimationLock = true) => RemainingTime;
}

/// <summary>The fake LocalPlayer: the only surface the real NIN code uses is HasStatus/Status.</summary>
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
///     The fake current target: the surfaces the real NIN code reaches are HasStatus (Trick/Kunai's
///     Bane/Mug debuff checks) and CanApplyStatus (debuff-applicability gating).
/// </summary>
internal sealed class FakeTarget
{
    public List<FakeStatus> Statuses = [];

    public bool HasStatus(uint id, bool anyOwner = false)
    {
        foreach (var s in Statuses)
        {
            if (s.Id != id) continue;
            if (!anyOwner && !s.Own) continue;
            return true;
        }

        return false;
    }

    public bool CanApplyStatus(uint[] statusIds) => true;
}

/// <summary>
///     Real Dalamud job-gauge objects over harness-owned unmanaged memory (identical machinery to the
///     VPR spike). NINGauge has a public NINGauge(IntPtr) ctor; each property (Ninki, Kazematoi) reads
///     bytes of the pointed-to memory. The harness derives the property-to-offset map by probing at
///     startup (write one byte at a time, see which property moves), so a Dalamud layout change
///     cannot silently mis-fake a gauge.
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
