using System.Globalization;
using System.Text;
using Dalamud.Hooking;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LazyCrucible.Policy;

namespace LazyCrucible;

/// <summary>
///     Read-only ReceiveEvent probe on every Crucible (XBM) agent. Each distinct vtable address is hooked once
///     (an agent that does not override ReceiveEvent shares the base implementation with unrelated agents, so
///     events are attributed by agent pointer and everything else passes straight through). Detours log, then
///     call the original with the arguments untouched and return its result.
///     <list type="bullet">
///         <item><c>PSP|ms|ag=pp|nb|via=re|rewr|kind=K|n=N|0:i1|1:i8</c> — pet-party / notebook, always on while
///             Beastmaster (the familiar-selection grammar graders already query).</item>
///         <item><c>XE|ms|ag=AgentName|via=..|kind=..|n=..|values</c> — every other XBM agent, when screen
///             recording is on.</item>
///     </list>
///     Identical consecutive events from an agent other than the pet party within one second are collapsed
///     (notebook hovers wrote 18,000 lines in one session); the next distinct line carries <c>rep=</c>.
/// </summary>
internal static unsafe class AgentProbe
{
    internal delegate AtkValue* ReceiveEventDelegate(
        AgentInterface* agent, AtkValue* returnValues, AtkValue* values, uint valueCount, ulong eventKind);

    /// <summary> Crucible agents in the linked ClientStructs (AgentId 497-506) plus their log tags. </summary>
    private static readonly (AgentId Id, string Tag)[] Watched =
    [
        (AgentId.XBMPetParty, "pp"),
        (AgentId.XBMMonsterNotebook, "nb"),
        (AgentId.XBMContentsMainHUD, "XBMContentsMainHUD"),
        (AgentId.XBMItemDetail, "XBMItemDetail"),
        (AgentId.XBMBattleMonsterDetail, "XBMBattleMonsterDetail"),
        (AgentId.XBMStageDetailList, "XBMStageDetailList"),
        (AgentId.XBMStageList, "XBMStageList"),
        (AgentId.XBMStageMap, "XBMStageMap"),
        (AgentId.XBMResult, "XBMResult"),
        (AgentId.XBMRanking, "XBMRanking"),
    ];

    private sealed class ProbeHook
    {
        public required string Via { get; init; }
        public Hook<ReceiveEventDelegate>? Hook { get; set; }
        public ReceiveEventDelegate? Detour { get; set; }
    }

    // Detours running right now; Teardown() waits for them before it disposes the hooks (hot-reload crash 2026-10-03).
    private static readonly Lalalazy.HookGuard.HookInFlight InFlight = new();
    private static readonly Dictionary<nint, ProbeHook> Hooks = [];
    private static readonly Dictionary<nint, string> TagByAgent = [];
    private static bool _active;

    /// <summary>
    ///     The board's degree as the layout last set it (<see cref="DegreeLatch"/>), published to GluttonyCombo over IPC by <c>Plugin</c>. Fed
    ///     from the same latch point as the familiar selection, never behind the log breaker. Forgotten when the hooks come down for a job change (nothing
    ///     is watching the layout then, so a later change would go unseen), kept across a loading screen (<see cref="DegreeLatch.HooksDown"/>).
    /// </summary>
    internal static readonly DegreeLatch Degree = new();

    private static string _lastKey = "";
    private static long _lastKeyMs;
    private static int _repeats;

    /// <summary> Resolve the watched agents and hook their event entries (idempotent; call every tick while wanted). </summary>
    public static void Ensure()
    {
        _active = true;
        try
        {
            var module = AgentModule.Instance();
            if (module is null)
                return;
            foreach (var (id, tag) in Watched)
            {
                var agent = (nint)module->GetAgentByInternalId(id);
                if (agent == 0)
                    continue;
                TagByAgent[agent] = tag;
                var vt = ((AgentInterface*)agent)->VirtualTable;
                if (vt is null)
                    continue;
                HookOnce((nint)vt->ReceiveEvent, "re");
                HookOnce((nint)vt->ReceiveEventWithResult, "rewr");
            }
        }
        catch (Exception ex)
        {
            CrucibleLog.Error(ex, "agent probe hook");
        }
    }

    private static void HookOnce(nint address, string via)
    {
        if (address == 0 || Hooks.ContainsKey(address))
            return;
        var probe = new ProbeHook { Via = via };
        probe.Detour = (agent, ret, values, count, kind) => ReceiveEventDetour(probe, agent, ret, values, count, kind);
        probe.Hook = Svc.Hook.HookFromAddress(address, probe.Detour);
        probe.Hook.Enable();
        Hooks[address] = probe;
    }

    private static AtkValue* ReceiveEventDetour(ProbeHook probe, AgentInterface* agent, AtkValue* returnValues, AtkValue* values, uint valueCount, ulong eventKind)
    {
        using var inFlight = InFlight.Enter();
        var hook = probe.Hook;
        if (hook is null || hook.IsDisposed)
            return returnValues;

        Observe(agent, probe.Via, values, valueCount, eventKind);
        return hook.Original(agent, returnValues, values, valueCount, eventKind);
    }

    /// <summary> Unhook everything and forget the degree (unload). </summary>
    public static void Teardown() => Unhook(playerAvailable: true);

    /// <summary> Unhook everything because the character is not a Beastmaster (job change) or is gone for a loading screen: see <see cref="DegreeLatch.HooksDown"/>. </summary>
    public static void TeardownNotBst(bool playerAvailable) => Unhook(playerAvailable);

    private static void Unhook(bool playerAvailable)
    {
        _active = false;
        Degree.HooksDown(playerAvailable);
        // Stop new calls, let the ones already inside a detour finish, only then dispose and null.
        foreach (var probe in Hooks.Values)
        {
            try
            {
                probe.Hook?.Disable();
            }
            catch
            {
                // ignored: unload path
            }
        }
        InFlight.Drain("AgentProbe", CrucibleLog.Warning);
        foreach (var probe in Hooks.Values)
        {
            try
            {
                probe.Hook?.Dispose();
            }
            catch
            {
                // ignored: unload path
            }
            probe.Hook = null;
        }
        Hooks.Clear();
        TagByAgent.Clear();
    }

    private static void Observe(AgentInterface* agent, string via, AtkValue* values, uint valueCount, ulong eventKind)
    {
        // The edit latches first (familiar selection, then the selection screens' Beast Feed picker / campsite),
        // never behind a breaker: skipping them could let a pass overwrite a manual edit.
        string? tag;
        try
        {
            if (!_active || !TagByAgent.TryGetValue((nint)agent, out tag))
                return;

            int? firstInt = values is not null && valueCount > 0 && values[0].Type == AtkValueType.Int ? values[0].Int : null;
            PetSelect.OnAgentEvent(tag, eventKind, valueCount, firstInt);
            SelectionScreens.OnAgentEvent(tag, eventKind, valueCount, firstInt);
            if (tag == "XBMStageDetailList" && valueCount == 2 && values is not null && Degree.Note(
                    eventKind, valueCount, firstInt, values[1].Type == AtkValueType.Int ? values[1].Int : null))
                CrucibleLog.Line($"PS|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|note=degree|dg={Degree.Value}|via={via}|calls=0");
        }
        catch (Exception ex)
        {
            CrucibleLog.Error(ex, "agent probe latch");
            return;
        }

        // The PSP| / XE| event log, under its breaker (src/Shared/LalaTelemetry).
        var guard = CrucibleLog.ProbeHook;
        if (!guard.TryEnter())
            return;
        try
        {
            var familiar = tag is "pp" or "nb";
            if (!familiar && !Plugin.Config.RecordScreens)
                return;

            var payload = DescribeValues(values, valueCount, familiar ? 8u : 16u, stringsAsType: familiar);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (tag != "pp")
            {
                var key = $"{tag}|{via}|{eventKind}|{valueCount}|{payload}";
                if (key == _lastKey && now - _lastKeyMs < 1000)
                {
                    _repeats++;
                    _lastKeyMs = now;
                    return;
                }
                _lastKey = key;
                _lastKeyMs = now;
            }

            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(160);
            sb.Append(familiar ? "PSP|" : "XE|").Append(now.ToString(inv))
              .Append("|ag=").Append(tag)
              .Append("|via=").Append(via)
              .Append("|kind=").Append(eventKind.ToString(inv))
              .Append("|n=").Append(valueCount.ToString(inv))
              .Append(payload);
            if (_repeats > 0)
            {
                sb.Append("|rep=").Append(_repeats.ToString(inv));
                _repeats = 0;
            }
            CrucibleLog.Line(sb.ToString());
        }
        catch (Exception ex)
        {
            guard.Failed(ex);
        }
    }

    /// <summary>
    ///     <c>|i:tV</c> per value: i Int, u UInt, b Bool, s String (40 chars), t other type number. With
    ///     <paramref name="stringsAsType"/> strings print as their type number, as the original PSP| grammar did.
    /// </summary>
    internal static string DescribeValues(AtkValue* values, uint valueCount, uint max, bool stringsAsType = false)
    {
        if (values is null || valueCount == 0)
            return "";
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(64);
        var n = Math.Min(valueCount, max);
        for (var i = 0u; i < n; i++)
        {
            var v = values[i];
            sb.Append('|').Append(i.ToString(inv)).Append(':');
            switch (v.Type)
            {
                case AtkValueType.Int:
                    sb.Append('i').Append(v.Int.ToString(inv));
                    break;
                case AtkValueType.UInt:
                    sb.Append('u').Append(v.UInt.ToString(inv));
                    break;
                case AtkValueType.Bool:
                    sb.Append('b').Append(v.Byte != 0 ? '1' : '0');
                    break;
                case AtkValueType.String when !stringsAsType:
                case AtkValueType.ConstString when !stringsAsType:
                case AtkValueType.ManagedString when !stringsAsType:
                    sb.Append('s').Append(ScreenRecorder.CleanString(v, 40));
                    break;
                default:
                    sb.Append('t').Append(((int)v.Type).ToString(inv));
                    break;
            }
        }
        return sb.ToString();
    }
}
