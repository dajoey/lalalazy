using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GluttonyCombo.Data;

/// <summary>
///     The pure half of the lease/connector collector (<c>LS|</c>): what another plugin (AutoDuty, through the WrathCombo gates the
///     omasky bridge forwards, or any other leasing plugin) did to Gluttony's settings and when. A lease overrides a stored option
///     while it lives and is never written to the player's saved config, so without these lines nothing recorded which settings a run
///     really ran with (the IPC channel logs at Debug only). Fixed greppable prefix: <c>message LIKE 'LS|%'</c> in ffxivdb.
///     No Dalamud/game types; asserted by <c>tests/GluttonyCombo.TelemetryHarness</c>.
/// </summary>
internal static class LeaseTelemetryFormat
{
    /// <summary> Fixed, greppable line prefix. </summary>
    public const string Prefix = "LS|";

    /// <summary> Hard budget for one emitted line (names are cut to fit). </summary>
    public const int MaxLineLength = 200;

    private const int MaxName = 40;

    /// <summary> <c>LS|ms|ev=register|plugin=..|lease=..</c>: a plugin took a lease (the first 8 characters of its id). </summary>
    public static string Register(long unixMs, string plugin, string leaseId) =>
        Line(unixMs, "register", $"plugin={Clean(plugin)}", $"lease={Clean(leaseId, 8)}");

    /// <summary> <c>ev=state</c>: the lease turns Auto-Rotation on or off; <c>stored</c> is what the player's own switch says. </summary>
    public static string State(long unixMs, string plugin, bool on, bool stored) =>
        Line(unixMs, "state", $"plugin={Clean(plugin)}", $"autorot={(on ? "on" : "off")}", $"stored={(stored ? "on" : "off")}");

    /// <summary>
    ///     <c>ev=config</c>: the lease overrides one Auto-Rotation option. <c>val</c> is the leased value, <c>stored</c> the player's saved
    ///     one (empty when unknown), <c>diff=1</c> when they differ, <c>0</c> when equal, <c>?</c> when the stored one is unknown.
    ///     Booleans are 0/1, enums their number.
    /// </summary>
    public static string Config(long unixMs, string plugin, string option, int value, int? stored) =>
        Line(unixMs, "config", $"plugin={Clean(plugin)}", $"opt={Clean(option)}", $"val={value.ToString(CultureInfo.InvariantCulture)}",
            $"stored={(stored is { } s ? s.ToString(CultureInfo.InvariantCulture) : string.Empty)}",
            $"diff={(stored is { } t ? (t != value ? "1" : "0") : "?")}");

    /// <summary> <c>ev=release</c>: the lease ended (released, cancelled by the player, plugin unloaded). </summary>
    public static string Release(long unixMs, string plugin, string reason) =>
        Line(unixMs, "release", $"plugin={Clean(plugin)}", $"why={Clean(reason)}");

    private static string Line(long unixMs, string ev, params string[] fields)
    {
        var sb = new StringBuilder(Prefix).Append(unixMs.ToString(CultureInfo.InvariantCulture)).Append("|ev=").Append(ev);
        foreach (var f in fields)
            sb.Append('|').Append(f);
        return sb.Length <= MaxLineLength ? sb.ToString() : sb.ToString(0, MaxLineLength);
    }

    /// <summary> A name that cannot add a field or a line: separators and control characters become underscores, length is capped. </summary>
    private static string Clean(string? text, int max = MaxName)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var take = text.Length < max ? text.Length : max;
        var sb = new StringBuilder(take);
        for (var i = 0; i < take; i++)
            sb.Append(text[i] is '|' or '\r' or '\n' || char.IsControl(text[i]) ? '_' : text[i]);
        return sb.ToString();
    }

    /// <summary>
    ///     "Only a first or a changed value is a new fact": AutoDuty re-sends its twelve option values every 5 s while it runs, so the
    ///     plugin writes a line when (plugin, key) is new or its value changed. <see cref="Forget"/> on release makes the next lease start
    ///     fresh.
    /// </summary>
    internal sealed class ChangeGate
    {
        private readonly Dictionary<(string Plugin, string Key), string> _last = new();

        public bool ShouldEmit(string plugin, string key, string value)
        {
            if (_last.TryGetValue((plugin, key), out var seen) && seen == value)
                return false;
            _last[(plugin, key)] = value;
            return true;
        }

        public void Forget(string plugin)
        {
            foreach (var k in new List<(string Plugin, string Key)>(_last.Keys))
                if (k.Plugin == plugin)
                    _last.Remove(k);
        }
    }
}
