// FakeGame.cs — BRD edition of the mutable per-case state the harness presents to the real
// rotation code (pattern from the VPR spike, rot/harness-spike). Program.cs sets these
// before each Invoke; the fakes in Fakes.cs read them.
using GluttonyCombo.Combos;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GluttonyCombo.RotationHarness;

/// <summary>Environment + game state for one harness case.</summary>
internal static class FakeGame
{
    // enabled presets/combos (Joey-like AdvMode setup is built per case by Program.cs)
    public static HashSet<Preset> EnabledPresets { get; private set; } = [];
    public static HashSet<CustomComboNS.CustomCombo> EnabledCombos { get; private set; } = [];

    // environment
    public static uint Level = 100;
    public static bool InCombat = true;
    public static bool InBossEncounter;
    public static bool HasBattleTarget = true;
    public static float TargetHPPercent = 100f;
    public static bool TargetIsBoss;
    public static bool TargetCanApplyStatus = true;
    public static bool CanWeave;
    public static bool GroupDamageIncoming;
    public static bool CountdownActive;
    public static float CountdownRemaining;
    public static bool RoleActionReady;

    // statuses: player side (FakePlayer.Status) and target side (StatusExtensions.Status)
    public static List<FakeStatus> Statuses { get; private set; } = [];
    public static List<FakeStatus> TargetStatuses { get; private set; } = [];

    // cooldowns, action hooks, action memory
    private static readonly Dictionary<uint, Data.CooldownData> Cooldowns = [];
    public static Dictionary<uint, uint> HookOverrides { get; private set; } = [];
    public static HashSet<uint> JustUsedActions { get; private set; } = [];

    // UserBoolArray backing store, keyed by config name ("BRD_Adv_DoT_Options" etc.)
    public static Dictionary<string, Dictionary<int, bool>> BoolArrayValues { get; private set; } = [];

    internal static Data.CooldownData Cooldown(uint actionId)
    {
        if (!Cooldowns.TryGetValue(actionId, out var cd))
        {
            cd = new Data.CooldownData
            {
                ActionID = actionId, IsCooldown = false, CooldownRemaining = 0f, CooldownElapsed = 0,
                CooldownTotal = 0, MaxCharges = 0, RemainingCharges = 0, ChargeDuration = 0,
            };
            Cooldowns[actionId] = cd;
        }

        return cd;
    }

    public static bool GetBoolArray(string name, int index) =>
        BoolArrayValues.TryGetValue(name, out var arr) && arr.TryGetValue(index, out var v) && v;

    public static void Reset()
    {
        EnabledPresets = [];
        EnabledCombos = [];
        Statuses = [];
        TargetStatuses = [];
        Cooldowns.Clear();
        HookOverrides = [];
        JustUsedActions = [];
        BoolArrayValues = [];

        Level = 100;
        InCombat = true;
        InBossEncounter = false;
        HasBattleTarget = true;
        TargetHPPercent = 100f;
        TargetIsBoss = false;
        TargetCanApplyStatus = true;
        CanWeave = false;
        GroupDamageIncoming = false;
        CountdownActive = false;
        CountdownRemaining = 0f;
        RoleActionReady = false;
        FakeGauges.ZeroAll();
    }
}

/// <summary>
///     Real Dalamud job-gauge objects over harness-owned unmanaged memory (Approach B "stub
///     layer"): the REAL gauge class reads its bytes from the pointer its base class was
///     constructed with, so a gauge constructed over AllocHGlobal memory is a real gauge
///     reading fake bytes. The property-to-offset map is derived by probing at startup —
///     one hot byte at a time, see which property moves — exactly like the VPR spike, so a
///     Dalamud layout change cannot silently mis-fake a gauge. Construction goes through
///     explicit GetConstructor(Public|NonPublic, IntPtr) + Invoke because the gauge ctor is
///     not public and Activator's nonPublic overload does not find it (learned from the
///     spike; re-confirmed here when BRDGauge hit MissingMethodException).
/// </summary>
internal static class FakeGauges
{
    private static readonly nint Mem = Marshal.AllocHGlobal(64);
    private static readonly Dictionary<Type, object> Cache = [];
    private static readonly Dictionary<(Type Type, string Prop), int> OffsetMap = [];

    public static IReadOnlyDictionary<(Type Type, string Prop), int> ProbedOffsets => OffsetMap;

    public static T Get<T>() where T : Dalamud.Game.ClientState.JobGauge.Types.JobGaugeBase
    {
        if (!Cache.TryGetValue(typeof(T), out var g))
        {
            var ctor = typeof(T).GetConstructor(
                          BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                          binder: null, [typeof(IntPtr)], modifiers: null)
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

    public static unsafe void SetByte<T>(string prop, byte value)
        where T : Dalamud.Game.ClientState.JobGauge.Types.JobGaugeBase
    {
        _ = Get<T>(); // ensure constructed + probed

        if (!OffsetMap.TryGetValue((typeof(T), prop), out var off))
            throw new KeyNotFoundException($"no gauge byte found for {typeof(T).Name}.{prop}");

        *(byte*)(Mem + off) = value;
    }

    private static unsafe void Probe<T>(object gauge)
        where T : Dalamud.Game.ClientState.JobGauge.Types.JobGaugeBase
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            // only byte/short/int/long/enum props are gauge fields; others (Address:nint,
            // Coda:array/struct) are never zero and would map spuriously to the last hot byte
            .Where(p => p.PropertyType is { IsEnum: true }
                        || p.PropertyType == typeof(byte) || p.PropertyType == typeof(sbyte)
                        || p.PropertyType == typeof(short) || p.PropertyType == typeof(int)
                        || p.PropertyType == typeof(long))
            .ToArray();
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
