#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Lalalazy.Hub;

/// <summary>
/// The adapter side of the hub protocol. A plugin declares its controls here; the four string methods are
/// what the Dalamud IPC provider exposes as <c>Lala.&lt;InternalName&gt;.Describe / GetAll / Set / Invoke</c>.
///
/// Rules this class enforces so an adapter cannot hurt its plugin or the hub:
/// - nothing here ever throws to the caller; every failure becomes <c>{"ok":false,"why":...}</c>;
/// - a control that is locked or disabled refuses writes and its setter does not run;
/// - values are validated and clamped before a setter sees them;
/// - a declaration that fails <see cref="Validate"/> describes as an error object and is never registered.
/// </summary>
public sealed class HubEndpoint
{
    private readonly List<ControlDef> _defs = new List<ControlDef>();
    private readonly Dictionary<string, ControlDef> _byId = new Dictionary<string, ControlDef>(StringComparer.Ordinal);

    // Failures already logged, so a control that stays broken is reported once instead of on every poll.
    private readonly HashSet<string> _logged = new HashSet<string>(StringComparer.Ordinal);

    public string Plugin { get; }
    public string Version { get; }

    /// <summary>Optional sink for short diagnostics, for example the plugin log.</summary>
    public Action<string>? Log { get; set; }

    public HubEndpoint(string plugin, string version)
    {
        Plugin = plugin ?? "";
        Version = version ?? "";
    }

    // ----- declaration ------------------------------------------------------------------------------

    public HubEndpoint Toggle(string id, string label, Func<bool> get, Action<bool> set,
        string group = "", string tip = "", bool master = false, string confirm = "", Func<ControlState>? state = null)
        => Toggle(id, label, get, v => { set(v); return SetOutcome.Success; }, group, tip, master, confirm, state);

    public HubEndpoint Toggle(string id, string label, Func<bool> get, Func<bool, SetOutcome> set,
        string group = "", string tip = "", bool master = false, string confirm = "", Func<ControlState>? state = null)
        => Add(new ControlDef
        {
            Id = id, Label = label, Kind = ControlKind.Toggle, Group = group, Tip = tip, Master = master, Confirm = confirm,
            Get = () => get(),
            Set = v => set((bool)v),
            State = state,
        });

    public HubEndpoint Stepper(string id, string label, double min, double max, double step,
        Func<double> get, Action<double> set, string unit = "", int decimals = 0,
        string group = "", string tip = "", Func<ControlState>? state = null)
        => Stepper(id, label, min, max, step, get, v => { set(v); return SetOutcome.Success; }, unit, decimals, group, tip, state);

    public HubEndpoint Stepper(string id, string label, double min, double max, double step,
        Func<double> get, Func<double, SetOutcome> set, string unit = "", int decimals = 0,
        string group = "", string tip = "", Func<ControlState>? state = null)
        => Add(new ControlDef
        {
            Id = id, Label = label, Kind = ControlKind.Stepper, Group = group, Tip = tip,
            Min = min, Max = max, Step = step, Unit = unit, Decimals = decimals,
            Get = () => get(),
            Set = v => set((double)v),
            State = state,
        });

    public HubEndpoint Choice(string id, string label, IReadOnlyList<string> choices,
        Func<int> get, Action<int> set, string group = "", string tip = "", Func<ControlState>? state = null)
        => Choice(id, label, choices, get, v => { set(v); return SetOutcome.Success; }, group, tip, state);

    public HubEndpoint Choice(string id, string label, IReadOnlyList<string> choices,
        Func<int> get, Func<int, SetOutcome> set, string group = "", string tip = "", Func<ControlState>? state = null)
        => Add(new ControlDef
        {
            Id = id, Label = label, Kind = ControlKind.Choice, Group = group, Tip = tip, Choices = choices,
            Get = () => get(),
            Set = v => set((int)v),
            State = state,
        });

    public HubEndpoint Button(string id, string label, Action invoke,
        string group = "", string tip = "", string confirm = "", Func<ControlState>? state = null)
        => Button(id, label, () => { invoke(); return SetOutcome.Success; }, group, tip, confirm, state);

    public HubEndpoint Button(string id, string label, Func<SetOutcome> invoke,
        string group = "", string tip = "", string confirm = "", Func<ControlState>? state = null)
        => Add(new ControlDef
        {
            Id = id, Label = label, Kind = ControlKind.Button, Group = group, Tip = tip, Confirm = confirm,
            Run = invoke,
            State = state,
        });

    private HubEndpoint Add(ControlDef def)
    {
        _defs.Add(def);
        if (!string.IsNullOrEmpty(def.Id) && !_byId.ContainsKey(def.Id)) _byId[def.Id] = def;
        return this;
    }

    /// <summary>Problems with the declaration; empty when it is safe to register.</summary>
    public IReadOnlyList<string> Validate() => HubValidation.Validate(Plugin, Version, _defs);

    // ----- the four endpoints -----------------------------------------------------------------------

    public string Describe()
    {
        try
        {
            var errors = Validate();
            if (errors.Count > 0)
                return Json(w =>
                {
                    w.WriteNumber("p", HubProtocol.Version);
                    w.WriteString("error", string.Join("; ", errors));
                });

            return Json(w =>
            {
                w.WriteNumber("p", HubProtocol.Version);
                w.WriteString("plugin", Plugin);
                w.WriteString("version", Version);
                w.WriteStartArray("controls");
                foreach (var d in _defs) WriteDescriptor(w, d);
                w.WriteEndArray();
            });
        }
        catch (Exception ex)
        {
            return ErrorJson("describe failed: " + ex.GetType().Name);
        }
    }

    public string GetAll()
    {
        try
        {
            return Json(w =>
            {
                w.WriteNumber("p", HubProtocol.Version);
                w.WriteStartObject("v");
                foreach (var d in _defs)
                {
                    if (!HubValidation.IsValidId(d.Id)) continue;
                    w.WriteStartObject(d.Id);
                    WriteState(w, d);
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            });
        }
        catch (Exception ex)
        {
            return ErrorJson("getAll failed: " + ex.GetType().Name);
        }
    }

    public string Set(string id, string valueJson)
    {
        try
        {
            if (id == null || !_byId.TryGetValue(id, out var def)) return Result(SetOutcome.Refuse("unknown control"));
            if (def.Kind == ControlKind.Button) return Result(SetOutcome.Refuse("use invoke for a button"));

            var blocked = CheckUsable(def);
            if (blocked.HasValue) return Result(blocked.Value);

            JsonElement json;
            try
            {
                using var doc = JsonDocument.Parse(valueJson ?? "");
                json = doc.RootElement.Clone();
            }
            catch (Exception)
            {
                return Result(SetOutcome.Refuse("the value is not valid JSON"));
            }

            if (!ValueCoercion.TryCoerce(def, json, out var value, out var why))
                return Result(SetOutcome.Refuse(why));

            return Result(Run(def, () => def.Set!(value!)));
        }
        catch (Exception ex)
        {
            return Result(SetOutcome.Refuse("set failed: " + ex.GetType().Name));
        }
    }

    public string Invoke(string id)
    {
        try
        {
            if (id == null || !_byId.TryGetValue(id, out var def)) return Result(SetOutcome.Refuse("unknown control"));
            if (def.Kind != ControlKind.Button) return Result(SetOutcome.Refuse("only a button can be invoked"));

            var blocked = CheckUsable(def);
            if (blocked.HasValue) return Result(blocked.Value);

            return Result(Run(def, () => def.Run!()));
        }
        catch (Exception ex)
        {
            return Result(SetOutcome.Refuse("invoke failed: " + ex.GetType().Name));
        }
    }

    // ----- helpers ----------------------------------------------------------------------------------

    private SetOutcome Run(ControlDef def, Func<SetOutcome> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            Log?.Invoke("hub control '" + def.Id + "' failed: " + ex.GetType().Name + ": " + ex.Message);
            return SetOutcome.Refuse("the plugin could not apply that");
        }
    }

    /// <summary>Null when the control may be written now; otherwise the refusal.</summary>
    private SetOutcome? CheckUsable(ControlDef def)
    {
        ControlState? state = null;
        try { state = def.State?.Invoke(); }
        catch (Exception ex) { Log?.Invoke("hub state of '" + def.Id + "' failed: " + ex.Message); return SetOutcome.Refuse("unavailable right now"); }

        if (state == null) return null;
        if (state.Locked) return SetOutcome.Refuse(string.IsNullOrEmpty(state.Why) ? "controlled by something else" : state.Why, locked: true);
        if (!state.Enabled) return SetOutcome.Refuse(string.IsNullOrEmpty(state.Why) ? "not available right now" : state.Why);
        return null;
    }

    private void WriteDescriptor(Utf8JsonWriter w, ControlDef d)
    {
        w.WriteStartObject();
        w.WriteString("id", d.Id);
        w.WriteString("label", d.Label);
        w.WriteString("kind", KindName(d.Kind));
        if (!string.IsNullOrEmpty(d.Group)) w.WriteString("group", d.Group);
        if (!string.IsNullOrEmpty(d.Tip)) w.WriteString("tip", d.Tip);
        if (d.Master) w.WriteBoolean("master", true);
        if (!string.IsNullOrEmpty(d.Confirm)) w.WriteString("confirm", d.Confirm);

        if (d.Kind == ControlKind.Stepper)
        {
            w.WriteNumber("min", d.Min);
            w.WriteNumber("max", d.Max);
            w.WriteNumber("step", d.Step);
            if (!string.IsNullOrEmpty(d.Unit)) w.WriteString("unit", d.Unit);
            if (d.Decimals > 0) w.WriteNumber("decimals", d.Decimals);
        }
        else if (d.Kind == ControlKind.Choice)
        {
            w.WriteStartArray("choices");
            foreach (var c in d.Choices) w.WriteStringValue(c);
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    private void WriteState(Utf8JsonWriter w, ControlDef d)
    {
        var enabled = true;
        var locked = false;
        var why = "";

        try
        {
            var st = d.State?.Invoke();
            if (st != null) { enabled = st.Enabled; locked = st.Locked; why = st.Why ?? ""; }
            _logged.Remove("state:" + d.Id);
        }
        catch (Exception ex)
        {
            LogOnce("state:" + d.Id, "hub state of '" + d.Id + "' failed: " + ex.Message);
            enabled = false;
            why = "unavailable";
        }

        if (d.Kind != ControlKind.Button && d.Get != null)
        {
            try
            {
                var value = d.Get();
                switch (value)
                {
                    case bool b: w.WriteBoolean("v", b); break;
                    case int i: w.WriteNumber("v", i); break;
                    case double dbl: w.WriteNumber("v", dbl); break;
                    default: enabled = false; if (why.Length == 0) why = "unavailable"; break;
                }
                _logged.Remove("value:" + d.Id);
            }
            catch (Exception ex)
            {
                LogOnce("value:" + d.Id, "hub value of '" + d.Id + "' failed: " + ex.Message);
                enabled = false;
                if (why.Length == 0) why = "unavailable";
            }
        }

        if (!enabled) w.WriteBoolean("enabled", false);
        if (locked) w.WriteBoolean("locked", true);
        if (why.Length > 0) w.WriteString("why", why);
    }

    private void LogOnce(string key, string message)
    {
        if (_logged.Add(key)) Log?.Invoke(message);
    }

    private static string KindName(ControlKind k) => k switch
    {
        ControlKind.Toggle => "toggle",
        ControlKind.Stepper => "stepper",
        ControlKind.Choice => "choice",
        _ => "button",
    };

    private static string Result(SetOutcome o) => Json(w =>
    {
        w.WriteBoolean("ok", o.Ok);
        if (!o.Ok)
        {
            w.WriteString("why", string.IsNullOrEmpty(o.Why) ? "refused" : o.Why);
            if (o.Locked) w.WriteBoolean("locked", true);
        }
    });

    private static string ErrorJson(string message) => Json(w =>
    {
        w.WriteNumber("p", HubProtocol.Version);
        w.WriteString("error", message);
    });

    private static string Json(Action<Utf8JsonWriter> body)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
