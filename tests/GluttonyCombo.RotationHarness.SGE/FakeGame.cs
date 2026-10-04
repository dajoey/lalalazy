// FakeGame: the mutable stand-in for the game + Dalamud state that the real Sage decision code
// reads through CustomComboFunctions. Every knob here is set by a harness case; defaults describe
// a level-100 Sage in combat, standing still (casting), on a living target, everything off
// cooldown except where noted, no buffs, no addersgall.
// NOTE: this file is harness-owned (tests/ only). It must never be linked into the plugin.

using Dalamud.Game.ClientState.JobGauge.Types;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GluttonyCombo.RotationHarness.SGE;

internal static class FakeGame
{
    // ---- player / combat ----
    public static int Level = 100;
    public static bool AllTraitsKnown = true;
    public static bool InCombat = true;
    public static bool CanWeave = true;
    public static bool InMeleeRange = true;
    public static bool HasBattleTarget = true;
    public static bool TargetIsBoss = false;
    public static bool InBossEncounter = false;
    public static bool GroupDamageIncoming = false;
    public static bool DottableEnemyPresent = false;   // SimpleTarget.DottableEnemy fake: a dottable target exists
    public static int EnemiesInRangeCount = 0;         // EnemiesInRange fake: how many enemies are around
    public static float TargetHPPercent = 100f;
    public static bool TargetCanApplyStatus = false;     // ShouldRefreshEDosis's gate stays closed
    public static bool InActionRange = true;             // every action in range unless overridden
    public static HashSet<uint> OutOfRangeActions = [];

    // ---- movement / timing (SGE reads these directly) ----
    public static bool IsMovingFlag = false;
    public static float TimeStoodStillSeconds = 5f;
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

    // ---- target-side statuses (the Eukrasian Dosis debuff on the current target; keyed by status id) ----
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
        DottableEnemyPresent = false;
        EnemiesInRangeCount = 0;
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
        GluttonyCombo.AutoRotation.AutoRotationController.RaidwideShieldOnCooldown = true;
        FakeGauges.ZeroAll();
    }
}

/// <summary>One status effect on the fake player.</summary>
internal sealed record FakeStatus(uint Id, float RemainingTime, ushort Param = 0, bool Own = true)
{
    public float RemainingTimeOrZero(bool checkAnimationLock = true) => RemainingTime;
}

/// <summary>
///     A target-side debuff (the Eukrasian Dosis DoT) as an <c>IStatus</c>, so the real
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

/// <summary>The fake LocalPlayer: the only surface the real SGE code uses is HasStatus/Status.</summary>
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
///     spike and the round-6/7 harnesses). SGEGauge's scalar properties (Addersgall, Addersting) are
///     probed by writing one byte at a time and recording which property moves, so the
///     property-to-offset map is *discovered*, never pinned. A Dalamud gauge layout change can only
///     make the probe find new offsets (printed as evidence each run), not silently fake wrong values.
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
        // would poison the map (JobGaugeBase.Address is an IntPtr).
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
///     A fake enemy battle character: implements the REAL Dalamud IBattleChara surface so the
///     real SGE code can carry it as a target. The decision code only reaches it through the
///     GameObjectExtensions fakes (Status / CanApplyStatus / IsBoss), which read harness state
///     instead of object members - the object is the non-null carrier those checks gate on
///     (same trick as the WHM harness's FakeGameObject). Any un-faked member throws loudly.
/// </summary>
internal sealed class FakeBattleChara : Dalamud.Game.ClientState.Objects.Types.IBattleChara
{
    internal static FakeBattleChara Enemy { get; } = new();

    internal static IEnumerable<Dalamud.Game.ClientState.Objects.Types.IGameObject> Enemies(int count)
    {
        for (var i = 0; i < count; i++)
            yield return Enemy;
    }

    // IGameObject (signatures reflected from the dev Dalamud.dll; same as the WHM harness's
    // FakeGameObject, plus the ICharacter / IBattleChara members the SGE target type needs)
    public Dalamud.Game.Text.SeStringHandling.SeString Name => "FakeEnemy";
    public ulong GameObjectId => 2;
    public uint EntityId => 2;
    public uint DataId => 0x1001;
    public uint BaseId => 0x1001;
    public uint OwnerId => 0;
    public ushort ObjectIndex => 2;
    public Dalamud.Game.ClientState.Objects.Enums.ObjectKind ObjectKind =>
        Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc;
    public byte SubKind => 5; // 5 = enemy battle NPC
    public byte YalmDistanceX => 3;
    public byte YalmDistanceZ => 0;
    public byte CurrentDistance => 3;
    public byte NextDistance => 3;
    public bool IsDead => false;
    public bool IsTargetable => true;
    public System.Numerics.Vector3 Position => new(0f, 0f, -3f);
    public float Rotation => 0f;
    public float HitboxRadius => 2f;
    public ulong TargetObjectId => 0;
    public Dalamud.Game.ClientState.Objects.Types.IGameObject? TargetObject => null;
    public nint Address => (nint)0x2000_0000;
    public bool IsValid() => true;
    public bool Equals(Dalamud.Game.ClientState.Objects.Types.IGameObject? other) => other == this;

    // ICharacter: rows and stats an enemy target would carry; the decision code never reads
    // them through this object (HP/status read through the fakes), so defaults are fine.
    public uint CurrentHp => 100_000;
    public uint MaxHp => 100_000;
    public uint CurrentMp => 10_000;
    public uint MaxMp => 10_000;
    public uint CurrentGp => 0;
    public uint MaxGp => 0;
    public uint CurrentCp => 0;
    public uint MaxCp => 0;
    public byte ShieldPercentage => 0;
    public Lumina.Excel.RowRef<Lumina.Excel.Sheets.ClassJob> ClassJob => default;
    public byte Level => 90;
    public Span<byte> Customize => Span<byte>.Empty;
    public Dalamud.Game.ClientState.Customize.ICustomizeData? CustomizeData => null;
    public Dalamud.Game.Text.SeStringHandling.SeString CompanyTag => "";
    public uint NameId => 0;
    public Lumina.Excel.RowRef<Lumina.Excel.Sheets.OnlineStatus> OnlineStatus => default;
    public Dalamud.Game.ClientState.Objects.Enums.StatusFlags StatusFlags =>
        default;
    public Lumina.Excel.RowRef<Lumina.Excel.Sheets.Mount>? CurrentMount => null;
    public Lumina.Excel.RowRef<Lumina.Excel.Sheets.Companion>? CurrentMinion => null;

    // IBattleChara: a non-casting enemy. StatusList would be read only through the real
    // GameObjectExtensions paths, which the harness fakes - so reaching here is a boundary
    // violation and throws loudly.
    public Dalamud.Game.ClientState.Statuses.StatusList? StatusList =>
        throw new NotSupportedException("harness boundary: statuses go through the GameObjectExtensions fake");
    public bool IsCasting => false;
    public bool IsCastInterruptible => false;
    public byte CastActionType => 0;
    public uint CastActionId => 0;
    public ulong CastTargetObjectId => 0;
    public float CurrentCastTime => 0f;
    public float BaseCastTime => 0f;
    public float TotalCastTime => 0f;
}
