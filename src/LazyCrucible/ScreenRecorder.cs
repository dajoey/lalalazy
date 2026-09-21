using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Hooking;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LazyCrucible;

/// <summary>
///     Read-only recorder of every Crucible screen, so the remaining decision screens (path, spoils, treasure,
///     shop and feeding, campsite, beast gear, board items, suspend) can be automated from one ordinary play
///     session instead of probe rounds. It never presses, fires or writes anything. Lines:
///     <list type="bullet">
///         <item><c>XV|ms|open=Name</c> / <c>XV|ms|close=Name</c> — addon visibility transitions (the run's state
///             machine).</item>
///         <item><c>XB|ms|Name|n=count|idx:type=value;...</c> (+ <c>XB+|ms|Name|part|...</c>) — the addon's full
///             AtkValues when it opens and whenever they change (at most once a second per addon). Same grammar as
///             GluttonyCombo's collector, with a larger cap: 2000 values, 60-character strings, 40 chunks.</item>
///         <item><c>XC|ms|Name|upd=0|1|n=count|0:i..|1:u..</c> — every callback the addon fires
///             (AtkUnitBase.FireCallback): the exact values a click sends, replayable later with
///             ECommons Callback.Fire.</item>
///         <item><c>XR|ms|Name|type=T|param=P|node=N|d=hex</c> — button / list clicks the addon receives
///             (Dalamud addon lifecycle PreReceiveEvent). Plain buttons such as "Take all" or the start-battle
///             button never go through FireCallback, so this is how their node and param are learned.</item>
///         <item><c>XA|ms|pp|mode=..|sub=..|selIdx=..|u138=..|u13c=..|src=..|party=..|sel=..</c> and
///             <c>XA|ms|stage|content=..|mode=..</c> — AgentXBMPetParty / AgentXBMStageDetailList fields at the
///             offsets ClientStructs PR #1952 documents, on change.</item>
///         <item><c>XO|ms|pos=x,y,z|obj=kind:DataId@x,y,z;...</c> — on a board, the character's position and the
///             event / treasure objects within 40 yalms, on change (at most once a second): how stepping onto a
///             space commits the path choice.</item>
///         <item><c>XK|ms|+Flag</c> / <c>XK|ms|-Flag</c> — condition changes in the Crucible area.</item>
///     </list>
///     Scope: addons named XBM*, plus SelectString / SelectIconString / SelectYesno / ContentsFinderConfirm /
///     Talk while on Beastmaster in Central Shroud (Bentbranch Meadows) or on a Crucible board — the entry
///     NPC, duty confirm and forfeit prompts. Agent events are recorded by <see cref="AgentProbe"/>.
/// </summary>
internal static unsafe class ScreenRecorder
{
    private const int MaxValues = 2000;
    private const int MaxString = 60;
    private const int Chunk = 900;
    private const int MaxChunks = 40;
    private const ushort CentralShroud = 148;

    private static readonly string[] GenericAddons = ["SelectString", "SelectIconString", "SelectYesno", "ContentsFinderConfirm", "Talk"];

    private static readonly Dictionary<string, (int Hash, long Ms)> Seen = [];
    private static readonly HashSet<string> Visible = [];
    private static readonly HashSet<string> NowVisible = [];
    private static Hook<AtkUnitBase.Delegates.FireCallback>? _fireCallbackHook;
    private static long _nextScanMs;
    private static bool _listenersOn;
    private static string _lastPp = "";
    private static string _lastStage = "";
    private static string _lastObjects = "";
    private static long _nextObjectsMs;

    /// <summary> Addon event types worth recording (clicks and selections; no hover, move, focus or timers). </summary>
    private static readonly HashSet<int> ClickEventTypes = [9, 10, 23, 25, 27, 31, 35, 36, 38, 58, 61, 63];

    /// <summary> Framework tick while Beastmaster: visibility, snapshots, callback hook lifecycle. </summary>
    public static void Tick(bool wanted)
    {
        if (!wanted)
        {
            Stop();
            return;
        }

        EnsureCallbackHook();
        EnsureListeners();

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now < _nextScanMs)
            return;
        _nextScanMs = now + 100; // 10 scans a second is enough to catch every screen

        try
        {
            Scan(now);
            AgentState(now);
            Objects(now);
        }
        catch (Exception ex)
        {
            CrucibleLog.Error(ex, "screen recorder scan");
        }
    }

    public static void Stop()
    {
        if (_fireCallbackHook is not null)
        {
            try
            {
                _fireCallbackHook.Disable();
                _fireCallbackHook.Dispose();
            }
            catch
            {
                // ignored: unload path
            }
            _fireCallbackHook = null;
        }
        if (_listenersOn)
        {
            try
            {
                Svc.AddonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, OnReceiveEvent);
                Svc.Condition.ConditionChange -= OnConditionChange;
            }
            catch
            {
                // ignored: unload path
            }
            _listenersOn = false;
        }
        Seen.Clear();
        Visible.Clear();
        _lastPp = _lastStage = _lastObjects = "";
    }

    private static void EnsureListeners()
    {
        if (_listenersOn)
            return;
        try
        {
            Svc.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, OnReceiveEvent);
            Svc.Condition.ConditionChange += OnConditionChange;
            _listenersOn = true;
        }
        catch (Exception ex)
        {
            CrucibleLog.Error(ex, "recorder listeners");
        }
    }

    private static void OnReceiveEvent(AddonEvent type, AddonArgs args)
    {
        try
        {
            if (args is not AddonReceiveEventArgs e)
                return;
            var eventType = (int)e.AtkEventType;
            if (!ClickEventTypes.Contains(eventType) || !Wanted(e.AddonName, InCrucibleArea()))
                return;

            var node = 0u;
            if (e.AtkEvent != 0)
            {
                var atkEvent = (AtkEvent*)e.AtkEvent;
                if (atkEvent->Node is not null)
                    node = atkEvent->Node->NodeId;
            }
            var data = "";
            if (e.AtkEventData != 0)
            {
                var bytes = new byte[16];
                Marshal.Copy(e.AtkEventData, bytes, 0, bytes.Length);
                data = Convert.ToHexString(bytes);
            }
            CrucibleLog.Line($"XR|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|{e.AddonName}|type={eventType}|param={e.EventParam}|node={node}|d={data}");
        }
        catch (Exception ex)
        {
            CrucibleLog.Error(ex, "addon event log");
        }
    }

    private static void OnConditionChange(ConditionFlag flag, bool value)
    {
        try
        {
            if (InCrucibleArea())
                CrucibleLog.Line($"XK|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|{(value ? '+' : '-')}{flag}");
        }
        catch (Exception ex)
        {
            CrucibleLog.Error(ex, "condition log");
        }
    }

    /// <summary> AgentXBMPetParty / AgentXBMStageDetailList fields at PR #1952 offsets (inside the documented struct sizes). </summary>
    private static void AgentState(long now)
    {
        var module = AgentModule.Instance();
        if (module is null)
            return;

        var pp = (nint)module->GetAgentByInternalId(AgentId.XBMPetParty);
        if (pp != 0)
        {
            var line = $"pp|mode={*(uint*)(pp + 0x64)}|sub={*(uint*)(pp + 0x68)}|selIdx={*(uint*)(pp + 0x140)}"
                       + $"|u138={*(uint*)(pp + 0x138)}|u13c={*(uint*)(pp + 0x13C)}|src={*(uint*)(pp + 0x154)}"
                       + $"|content={*(uint*)(pp + 0x70)}|party={Vector(pp, 0x78)}|sel={Vector(pp, 0x90)}";
            if (line != _lastPp)
            {
                _lastPp = line;
                CrucibleLog.Line($"XA|{now}|{line}");
            }
        }

        var stage = (nint)module->GetAgentByInternalId(AgentId.XBMStageDetailList);
        if (stage != 0)
        {
            var line = $"stage|content={*(uint*)(stage + 0x38)}|mode={*(uint*)(stage + 0x3C)}";
            if (line != _lastStage)
            {
                _lastStage = line;
                CrucibleLog.Line($"XA|{now}|{line}");
            }
        }
    }

    /// <summary> Pet ids of a StdVector&lt;{uint PetId, uint SortKey}&gt; (at most 15), dot-joined. </summary>
    private static string Vector(nint agent, int offset)
    {
        var first = *(nint*)(agent + offset);
        var last = *(nint*)(agent + offset + 8);
        if (first == 0 || last < first)
            return "";
        var count = (int)((last - first) / 8);
        if (count is <= 0 or > 15)
            return count == 0 ? "" : $"n{count}";
        var ids = new string[count];
        for (var i = 0; i < count; i++)
            ids[i] = (*(uint*)(first + i * 8)).ToString(CultureInfo.InvariantCulture);
        return string.Join(".", ids);
    }

    /// <summary> On a board: position and nearby event / treasure objects, on change, at most once a second. </summary>
    private static void Objects(long now)
    {
        if (now < _nextObjectsMs || BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType) == 0)
            return;
        _nextObjectsMs = now + 1000;
        var me = Svc.Objects.LocalPlayer;
        if (me is null)
            return;

        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(256);
        sb.Append("pos=").Append(me.Position.X.ToString("0", inv)).Append(',').Append(me.Position.Y.ToString("0", inv))
          .Append(',').Append(me.Position.Z.ToString("0", inv)).Append("|obj=");
        foreach (var obj in Svc.Objects)
        {
            if (obj.ObjectKind is not (ObjectKind.EventObj or ObjectKind.Treasure or ObjectKind.EventNpc))
                continue;
            if (System.Numerics.Vector3.Distance(obj.Position, me.Position) > 40f)
                continue;
            sb.Append((int)obj.ObjectKind).Append(':').Append(obj.BaseId.ToString(inv)).Append('@')
              .Append(obj.Position.X.ToString("0", inv)).Append(',').Append(obj.Position.Z.ToString("0", inv))
              .Append(obj.IsTargetable ? "" : "/nt").Append(';');
        }
        var line = sb.ToString();
        if (line == _lastObjects)
            return;
        _lastObjects = line;
        CrucibleLog.Line($"XO|{now}|{line}");
    }

    private static bool InCrucibleArea()
    {
        var territory = Svc.ClientState.TerritoryType;
        return territory == CentralShroud || BST_CrucibleData.BoardOfTerritory(territory) != 0;
    }

    private static bool Wanted(string name, bool inArea) =>
        name.StartsWith("XBM", StringComparison.Ordinal) || (inArea && Array.IndexOf(GenericAddons, name) >= 0);

    private static void Scan(long now)
    {
        var manager = RaptureAtkUnitManager.Instance();
        if (manager is null)
            return;

        var inArea = InCrucibleArea();
        NowVisible.Clear();
        var list = manager->AtkUnitManager.AllLoadedUnitsList;
        var count = Math.Min((int)list.Count, list.Entries.Length);
        for (var i = 0; i < count; i++)
        {
            var unit = list.Entries[i].Value;
            if (unit is null || !unit->IsVisible)
                continue;
            var name = unit->NameString;
            if (string.IsNullOrEmpty(name) || !Wanted(name, inArea))
                continue;
            NowVisible.Add(name);

            if (!Visible.Contains(name))
                CrucibleLog.Line($"XV|{now}|open={name}");
            Snapshot(unit, name, now);
        }

        foreach (var name in Visible)
        {
            if (!NowVisible.Contains(name))
            {
                CrucibleLog.Line($"XV|{now}|close={name}");
                Seen.Remove(name); // a reopen is always recorded
            }
        }
        Visible.Clear();
        Visible.UnionWith(NowVisible);
    }

    private static void Snapshot(AtkUnitBase* unit, string name, long now)
    {
        var text = DescribeAddon(unit);
        var hash = text.GetHashCode();
        if (Seen.TryGetValue(name, out var seen) && (seen.Hash == hash || now - seen.Ms < 1000))
            return;
        Seen[name] = (hash, now);

        for (int offset = 0, part = 0; offset < text.Length && part < MaxChunks; offset += Chunk, part++)
        {
            var prefix = part == 0 ? $"XB|{now}|{name}|n={unit->AtkValuesCount}|" : $"XB+|{now}|{name}|{part}|";
            CrucibleLog.Line(prefix + text.Substring(offset, Math.Min(Chunk, text.Length - offset)));
        }
    }

    /// <summary> <c>idx:type=value;</c> for every defined AtkValue (Undefined skipped), same grammar as the XB| collector. </summary>
    private static string DescribeAddon(AtkUnitBase* unit)
    {
        var values = unit->AtkValues;
        if (values is null)
            return "";
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(512);
        var n = Math.Min((int)unit->AtkValuesCount, MaxValues);
        for (var i = 0; i < n; i++)
        {
            var v = values[i];
            switch (v.Type)
            {
                case AtkValueType.Undefined:
                    continue;
                case AtkValueType.Bool:
                    sb.Append(i).Append(":b=").Append(v.Byte != 0 ? '1' : '0');
                    break;
                case AtkValueType.Int:
                    sb.Append(i).Append(":i=").Append(v.Int.ToString(inv));
                    break;
                case AtkValueType.UInt:
                    sb.Append(i).Append(":u=").Append(v.UInt.ToString(inv));
                    break;
                case AtkValueType.Float:
                    sb.Append(i).Append(":f=").Append(v.Float.ToString("0.###", inv));
                    break;
                case AtkValueType.String:
                case AtkValueType.ConstString:
                case AtkValueType.ManagedString:
                    sb.Append(i).Append(":s=").Append(CleanString(v, MaxString));
                    break;
                default:
                    sb.Append(i).Append(":t").Append(((int)v.Type).ToString(inv));
                    break;
            }
            sb.Append(';');
        }
        return sb.ToString();
    }

    /// <summary> A string AtkValue as log-safe text (| ; and newlines replaced), cut to <paramref name="max"/> characters. </summary>
    internal static string CleanString(AtkValue v, int max)
    {
        if (v.Pointer is null)
            return "";
        var s = Marshal.PtrToStringUTF8((nint)v.Pointer) ?? "";
        if (s.Length > max)
            s = s[..max];
        return s.Replace('|', '/').Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ');
    }

    // ------------------------------------------------------------------ callbacks (read-only hook)

    private static void EnsureCallbackHook()
    {
        if (_fireCallbackHook is not null)
            return;
        try
        {
            _fireCallbackHook = Svc.Hook.HookFromAddress<AtkUnitBase.Delegates.FireCallback>(
                AtkUnitBase.MemberFunctionPointers.FireCallback, FireCallbackDetour);
            _fireCallbackHook.Enable();
        }
        catch (Exception ex)
        {
            CrucibleLog.Error(ex, "callback hook");
            _fireCallbackHook = null;
        }
    }

    private static bool FireCallbackDetour(AtkUnitBase* unit, uint valueCount, AtkValue* values, bool updateState)
    {
        try
        {
            if (unit is not null && Plugin.Config.RecordScreens)
            {
                var name = unit->NameString;
                if (!string.IsNullOrEmpty(name) && Wanted(name, InCrucibleArea()))
                {
                    var inv = CultureInfo.InvariantCulture;
                    CrucibleLog.Line($"XC|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(inv)}|{name}|upd={(updateState ? 1 : 0)}|n={valueCount.ToString(inv)}"
                                     + AgentProbe.DescribeValues(values, valueCount, 16));
                }
            }
        }
        catch (Exception ex)
        {
            CrucibleLog.Error(ex, "callback log");
        }
        return _fireCallbackHook!.Original(unit, valueCount, values, updateState);
    }
}
