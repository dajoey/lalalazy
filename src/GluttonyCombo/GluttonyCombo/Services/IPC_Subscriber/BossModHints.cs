#region Dependencies

using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;
using GluttonyCombo.Services.IPC;
using Lalalazy.Telemetry;
using System;

#endregion

namespace GluttonyCombo.Services.IPC_Subscriber;

/// <summary>
///     Stock BossMod Reborn's incoming-hit clock, read through its public IPC only (no reflection, no fork endpoint):
///     <c>BossMod.Hints.NextDamageIn</c> (seconds to the earliest predicted hit, float.MaxValue none),
///     <c>BossMod.Hints.NextDamageType</c> (0 none, 1 tankbuster, 2 raidwide, 3 shared: <c>AIHints.PredictedDamageType</c>),
///     <c>BossMod.Hints.PredictedDamagePlayers</c> (the party bit mask of that same earliest prediction: bit 0 the character,
///     and since 7.5.6.16 the BST familiar at the first free party slot from 1), <c>BossMod.Hints.MaxCastTime</c> and
///     <c>BossMod.ActiveModuleName</c> (BossmodReborn origin/main <c>Framework/IPCProvider.cs</c> 19-20, 166-192;
///     <c>BossModule/AIHints.cs</c> 65-71, 364-371; <c>Framework/WorldStateGameSync.cs</c> 892-899).
///     <para>Absent or failing BMR reads neutral (no prediction), never a per-tick exception: a missing endpoint is seen through
///     <c>HasFunction</c> (no throw), and an endpoint that throws is left alone for <see cref="RetryMs"/>. Each change between
///     answering and not answering writes one <c>BH|</c> line; so does each change of the active module's name.</para>
/// </summary>
internal static class BossModHints
{
    /// <summary> One read of the hint endpoints; <see cref="Neutral"/> when BMR is absent, old or failing. </summary>
    internal readonly record struct Reading(bool Seen, float HitIn, int HitType, ulong Players, float MaxCast);

    internal static readonly Reading Neutral = new(false, float.MaxValue, 0, 0ul, float.MaxValue);

    /// <summary> An endpoint that threw is not asked again for this long (the BMR-absent cost: one caught exception per window). </summary>
    private const long RetryMs = 5000;

    /// <summary> The active module's name is asked at most this often (it allocates a string). </summary>
    private const long ModuleNamePollMs = 1000;

    private static ICallGateSubscriber<float>? _nextDamageIn;
    private static ICallGateSubscriber<int>? _nextDamageType;
    private static ICallGateSubscriber<ulong>? _predictedPlayers;
    private static ICallGateSubscriber<float>? _maxCastTime;
    private static ICallGateSubscriber<string?>? _activeModuleName;

    private static long _retryAtMs;
    private static bool? _answering;
    private static long _moduleNextPollMs;
    private static string? _moduleName;

    /// <summary> The active module's name as last read (null when none or BMR is absent). </summary>
    internal static string? ModuleName => _moduleName;

    /// <summary> Reads the hint endpoints once (call once per rotation tick, on a Crucible board only). </summary>
    internal static Reading Read(long nowMs)
    {
        if (nowMs < _retryAtMs)
            return Neutral;

        try
        {
            var pi = Svc.PluginInterface;
            _nextDamageIn ??= pi.GetIpcSubscriber<float>("BossMod.Hints.NextDamageIn");
            _nextDamageType ??= pi.GetIpcSubscriber<int>("BossMod.Hints.NextDamageType");
            _predictedPlayers ??= pi.GetIpcSubscriber<ulong>("BossMod.Hints.PredictedDamagePlayers");
            _maxCastTime ??= pi.GetIpcSubscriber<float>("BossMod.Hints.MaxCastTime");
            _activeModuleName ??= pi.GetIpcSubscriber<string?>("BossMod.ActiveModuleName");

            if (!_nextDamageIn.HasFunction || !_nextDamageType.HasFunction || !_predictedPlayers.HasFunction)
            {
                NoteAnswering(false, "missing");
                NoteModule(null);
                return Neutral;
            }

            var hitIn = IpcStallTrace.Time("Hints.NextDamageIn", _nextDamageIn.InvokeFunc);
            var type = IpcStallTrace.Time("Hints.NextDamageType", _nextDamageType.InvokeFunc);
            var players = IpcStallTrace.Time("Hints.PredictedDamagePlayers", _predictedPlayers.InvokeFunc);
            var maxCast = _maxCastTime.HasFunction ? IpcStallTrace.Time("Hints.MaxCastTime", _maxCastTime.InvokeFunc) : float.MaxValue;

            if (nowMs >= _moduleNextPollMs)
            {
                _moduleNextPollMs = nowMs + ModuleNamePollMs;
                NoteModule(_activeModuleName.HasFunction ? IpcStallTrace.Time("ActiveModuleName", _activeModuleName.InvokeFunc) : null);
            }

            NoteAnswering(true, "ok");
            // No prediction: BMR reports float.MaxValue with type 0 and an empty mask; keep that shape whatever it sends.
            if (float.IsNaN(hitIn) || hitIn >= float.MaxValue || players == 0ul)
                return Neutral with { Seen = true, MaxCast = maxCast };
            return new Reading(true, hitIn, type, players, maxCast);
        }
        catch (Exception e)
        {
            _retryAtMs = nowMs + RetryMs;
            NoteAnswering(false, e.GetType().Name);
            NoteModule(null);
            return Neutral;
        }
    }

    private static void NoteAnswering(bool answering, string why)
    {
        if (_answering == answering)
            return;
        _answering = answering;
        Log($"state={(answering ? "ok" : "absent")}|why={why}");
    }

    private static void NoteModule(string? name)
    {
        if (name == _moduleName)
            return;
        _moduleName = name;
        Log($"module={(name ?? "").Replace('|', '/')}");
    }

    private static void Log(string detail)
    {
        var line = $"BH|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|{detail}";
        Svc.Log.Information(line);
        LalaTelemetry.Record(line);
    }
}
