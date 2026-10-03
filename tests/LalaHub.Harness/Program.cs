using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Lalalazy.Hub;

// LalaHub.Harness: the hub protocol's pure logic with no Dalamud and no game.
// Covers the adapter side (HubEndpoint: describe, get, set, invoke, validation, coercion) and the
// hub side (DescriptorParser: it must survive garbage from an old or broken adapter).
// Exit code 0 only when every case passes.

int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) { pass++; Console.WriteLine("PASS  " + name); }
    else { fail++; Console.WriteLine("FAIL  " + name + (detail.Length > 0 ? "  -> " + detail : "")); }
}

JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
string Prop(string json, params string[] path)
{
    var e = J(json);
    foreach (var p in path) e = e.GetProperty(p);
    return e.ToString();
}

// ===== a representative adapter ===================================================================
bool master = true, only = false;
double hp = 60;
int mode = 0;
int fired = 0;
bool lockedNow = false;

HubEndpoint Make()
{
    var ep = new HubEndpoint("AutoPotion", "0.2.5.0");
    ep.Toggle("master_enable", "Auto-use potions", () => master, v => { master = v; },
        group: "General", tip: "Master switch.", master: true);
    ep.Toggle("only_in_combat", "Only in combat", () => only, v => { only = v; }, group: "General",
        state: () => new ControlState(Enabled: true, Locked: lockedNow, Why: lockedNow ? "controlled by another plugin" : ""));
    ep.Stepper("hp_threshold", "HP threshold", min: 1, max: 99, step: 5, get: () => hp, set: v => { hp = v; }, unit: "%");
    ep.Choice("mode", "Mode", new[] { "Auto", "Manual", "Off" }, () => mode, i => { mode = i; });
    ep.Button("fire", "Fire once", () => { fired++; });
    return ep;
}

var ep0 = Make();

// ===== validation =================================================================================
Check("a valid adapter has no validation errors", ep0.Validate().Count == 0, string.Join("; ", ep0.Validate()));

{
    var bad = new HubEndpoint("X", "1.0");
    bad.Toggle("a", "A", () => true, v => { });
    bad.Toggle("a", "A again", () => true, v => { });
    Check("duplicate control ids are rejected", bad.Validate().Any(e => e.Contains("duplicate")), string.Join("; ", bad.Validate()));
}
{
    var bad = new HubEndpoint("X", "1.0");
    bad.Stepper("s", "S", min: 5, max: 5, step: 1, get: () => 5, set: v => { });
    Check("a stepper with min >= max is rejected", bad.Validate().Any(e => e.Contains("min")), string.Join("; ", bad.Validate()));
}
{
    var bad = new HubEndpoint("X", "1.0");
    bad.Stepper("s", "S", min: 0, max: 10, step: 0, get: () => 5, set: v => { });
    Check("a stepper with step <= 0 is rejected", bad.Validate().Any(e => e.Contains("step")), string.Join("; ", bad.Validate()));
}
{
    var bad = new HubEndpoint("X", "1.0");
    bad.Choice("c", "C", new string[0], () => 0, i => { });
    Check("a choice with no options is rejected", bad.Validate().Any(e => e.Contains("choice")), string.Join("; ", bad.Validate()));
}
{
    var bad = new HubEndpoint("X", "1.0");
    bad.Toggle("Bad Id!", "B", () => true, v => { });
    Check("an id with spaces or capitals is rejected", bad.Validate().Any(e => e.Contains("id")), string.Join("; ", bad.Validate()));
}
{
    var bad = new HubEndpoint("X", "1.0");
    bad.Toggle("m1", "M1", () => true, v => { }, master: true);
    bad.Toggle("m2", "M2", () => true, v => { }, master: true);
    Check("two master controls are rejected", bad.Validate().Any(e => e.Contains("master")), string.Join("; ", bad.Validate()));
}
{
    var bad = new HubEndpoint("X", "1.0");
    for (int i = 0; i < 40; i++) bad.Toggle("c" + i, "C" + i, () => true, v => { });
    Check("more than 32 controls are rejected", bad.Validate().Any(e => e.Contains("32")), string.Join("; ", bad.Validate()));
}
{
    var bad = new HubEndpoint("X", "1.0");
    bad.Toggle("long", new string('x', 200), () => true, v => { });
    Check("an over-long label is rejected", bad.Validate().Any(e => e.Contains("label")), string.Join("; ", bad.Validate()));
}
{
    var bad = new HubEndpoint("", "1.0");
    bad.Toggle("a", "A", () => true, v => { });
    Check("an empty plugin name is rejected", bad.Validate().Any(e => e.Contains("plugin")), string.Join("; ", bad.Validate()));
}

// ===== describe ====================================================================================
var desc = ep0.Describe();
Check("describe is valid JSON carrying protocol 1, plugin and version",
    Prop(desc, "p") == "1" && Prop(desc, "plugin") == "AutoPotion" && Prop(desc, "version") == "0.2.5.0", desc);
Check("describe lists every control in declaration order",
    string.Join(",", J(desc).GetProperty("controls").EnumerateArray().Select(c => c.GetProperty("id").GetString()))
        == "master_enable,only_in_combat,hp_threshold,mode,fire");
Check("describe marks only the master toggle", J(desc).GetProperty("controls").EnumerateArray()
        .Count(c => c.TryGetProperty("master", out var m) && m.GetBoolean()) == 1);
Check("describe carries stepper range, step and unit",
    Prop(desc, "controls") != "" && J(desc).GetProperty("controls").EnumerateArray().First(c => c.GetProperty("id").GetString() == "hp_threshold") is var hpC
    && hpC.GetProperty("min").GetDouble() == 1 && hpC.GetProperty("max").GetDouble() == 99 && hpC.GetProperty("step").GetDouble() == 5 && hpC.GetProperty("unit").GetString() == "%");
Check("describe carries choice labels",
    J(desc).GetProperty("controls").EnumerateArray().First(c => c.GetProperty("id").GetString() == "mode").GetProperty("choices").GetArrayLength() == 3);
Check("describe is deterministic", ep0.Describe() == desc);
Check("an invalid adapter describes as an error object instead of throwing", new HubEndpoint("", "1").Describe().Contains("\"error\""));

// ===== get all =====================================================================================
var all = ep0.GetAll();
Check("getAll reports current values by id",
    J(all).GetProperty("v").GetProperty("master_enable").GetProperty("v").GetBoolean() == true &&
    J(all).GetProperty("v").GetProperty("hp_threshold").GetProperty("v").GetDouble() == 60 &&
    J(all).GetProperty("v").GetProperty("mode").GetProperty("v").GetInt32() == 0, all);
{
    var ep = new HubEndpoint("Y", "1");
    ep.Toggle("ok_one", "Fine", () => true, v => { });
    ep.Toggle("bad_one", "Throws", () => throw new InvalidOperationException("boom"), v => { });
    var s = ep.GetAll();
    Check("a getter that throws marks only that control unavailable",
        J(s).GetProperty("v").GetProperty("ok_one").GetProperty("v").GetBoolean() &&
        J(s).GetProperty("v").GetProperty("bad_one").GetProperty("enabled").GetBoolean() == false, s);
}
lockedNow = true;
Check("a locked control reports locked and its reason",
    J(ep0.GetAll()).GetProperty("v").GetProperty("only_in_combat") is var lk && lk.GetProperty("locked").GetBoolean()
    && lk.GetProperty("why").GetString() == "controlled by another plugin");
lockedNow = false;

// ===== set =========================================================================================
Check("set toggle changes the value and reports ok",
    Prop(ep0.Set("only_in_combat", "true"), "ok") == "True" && only == true);
Check("set toggle back", Prop(ep0.Set("only_in_combat", "false"), "ok") == "True" && only == false);
Check("set stepper stores the value", Prop(ep0.Set("hp_threshold", "70"), "ok") == "True" && hp == 70);
Check("set stepper clamps above max", Prop(ep0.Set("hp_threshold", "500"), "ok") == "True" && hp == 99);
Check("set stepper clamps below min", Prop(ep0.Set("hp_threshold", "-10"), "ok") == "True" && hp == 1);
Check("set stepper snaps to the nearest multiple of the step", Prop(ep0.Set("hp_threshold", "33"), "ok") == "True" && hp == 35, "hp=" + hp);
Check("set stepper keeps a value that is already on a multiple", Prop(ep0.Set("hp_threshold", "60"), "ok") == "True" && hp == 60, "hp=" + hp);
Check("set stepper reaches the top of a range that is not a multiple (99 with step 5)", Prop(ep0.Set("hp_threshold", "99"), "ok") == "True" && hp == 99, "hp=" + hp);
Check("set choice stores the index", Prop(ep0.Set("mode", "2"), "ok") == "True" && mode == 2);
Check("set choice rejects an out-of-range index and keeps the old value",
    Prop(ep0.Set("mode", "9"), "ok") == "False" && mode == 2);
Check("set choice rejects a fractional index", Prop(ep0.Set("mode", "1.5"), "ok") == "False" && mode == 2);
Check("set toggle rejects a number", Prop(ep0.Set("only_in_combat", "1"), "ok") == "False" && only == false);
Check("set stepper rejects a string", Prop(ep0.Set("hp_threshold", "\"70\""), "ok") == "False");
Check("set stepper rejects garbage JSON without throwing", Prop(ep0.Set("hp_threshold", "{{{"), "ok") == "False");
Check("set on an unknown id fails with a reason", Prop(ep0.Set("nope", "true"), "ok") == "False" && Prop(ep0.Set("nope", "true"), "why").Length > 0);
Check("set on a button is refused (use invoke)", Prop(ep0.Set("fire", "true"), "ok") == "False" && fired == 0);
{
    lockedNow = true; only = false;
    var r = ep0.Set("only_in_combat", "true");
    Check("set on a locked control is refused, flagged locked, and the setter does not run",
        Prop(r, "ok") == "False" && J(r).GetProperty("locked").GetBoolean() && only == false, r);
    lockedNow = false;
}
{
    var ep = new HubEndpoint("Z", "1");
    int ran = 0;
    ep.Toggle("t", "T", () => true, v => { ran++; }, state: () => new ControlState(Enabled: false, Why: "not available now"));
    var r = ep.Set("t", "false");
    Check("set on a disabled control is refused and the setter does not run", Prop(r, "ok") == "False" && ran == 0, r);
}
{
    var ep = new HubEndpoint("Z", "1");
    ep.Toggle("t", "T", () => true, new Action<bool>(v => { throw new InvalidOperationException("setter exploded"); }));
    string r = null; Exception thrown = null;
    try { r = ep.Set("t", "false"); } catch (Exception e) { thrown = e; }
    Check("a setter that throws never escapes: ok=false with a reason", thrown == null && Prop(r, "ok") == "False" && Prop(r, "why").Length > 0, r ?? thrown?.Message);
}
{
    var ep = new HubEndpoint("Z", "1");
    ep.Toggle("t", "T", () => false, v => SetOutcome.Refuse("run in progress"));
    var r = ep.Set("t", "true");
    Check("a setter may refuse with its own reason", Prop(r, "ok") == "False" && Prop(r, "why") == "run in progress", r);
}

// ===== invoke ======================================================================================
Check("invoke runs a button once", Prop(ep0.Invoke("fire"), "ok") == "True" && fired == 1);
Check("invoke on a toggle is refused", Prop(ep0.Invoke("master_enable"), "ok") == "False" && fired == 1);
Check("invoke on an unknown id is refused", Prop(ep0.Invoke("nope"), "ok") == "False");
{
    var ep = new HubEndpoint("Z", "1");
    ep.Button("b", "B", new Action(() => { throw new InvalidOperationException("button exploded"); }));
    string r = null; Exception thrown = null;
    try { r = ep.Invoke("b"); } catch (Exception e) { thrown = e; }
    Check("a button that throws never escapes", thrown == null && Prop(r, "ok") == "False", r ?? thrown?.Message);
}

// ===== the hub's parser: it must never throw on what an adapter sends ============================
{
    var d = DescriptorParser.ParseDescriptor(desc);
    Check("parser reads a real descriptor", d != null && d.Plugin == "AutoPotion" && d.Version == "0.2.5.0" && d.Controls.Count == 5);
    Check("parser keeps kinds, master and ranges",
        d != null && d.Controls[0].Kind == ControlKind.Toggle && d.Controls[0].Master
        && d.Controls[2].Kind == ControlKind.Stepper && d.Controls[2].Min == 1 && d.Controls[2].Max == 99 && d.Controls[2].Step == 5
        && d.Controls[3].Kind == ControlKind.Choice && d.Controls[3].Choices.Count == 3
        && d.Controls[4].Kind == ControlKind.Button);
}
Check("parser rejects an unknown protocol", DescriptorParser.ParseDescriptor("{\"p\":99,\"plugin\":\"X\",\"version\":\"1\",\"controls\":[]}") == null);
Check("parser returns null for garbage", DescriptorParser.ParseDescriptor("not json") == null && DescriptorParser.ParseDescriptor("") == null && DescriptorParser.ParseDescriptor(null) == null);
Check("parser returns null for truncated JSON", DescriptorParser.ParseDescriptor(desc.Substring(0, desc.Length / 2)) == null);
Check("parser skips a control of an unknown kind and keeps the rest",
    DescriptorParser.ParseDescriptor("{\"p\":1,\"plugin\":\"X\",\"version\":\"1\",\"controls\":[{\"id\":\"a\",\"label\":\"A\",\"kind\":\"hologram\"},{\"id\":\"b\",\"label\":\"B\",\"kind\":\"toggle\"}]}") is var pd
    && pd != null && pd.Controls.Count == 1 && pd.Controls[0].Id == "b");
Check("parser drops a control with no id", DescriptorParser.ParseDescriptor("{\"p\":1,\"plugin\":\"X\",\"version\":\"1\",\"controls\":[{\"label\":\"A\",\"kind\":\"toggle\"}]}") is var pe && pe != null && pe.Controls.Count == 0);
Check("parser caps an absurd control count", DescriptorParser.ParseDescriptor("{\"p\":1,\"plugin\":\"X\",\"version\":\"1\",\"controls\":[" +
    string.Join(",", Enumerable.Range(0, 500).Select(i => "{\"id\":\"c" + i + "\",\"label\":\"C\",\"kind\":\"toggle\"}")) + "]}") is var pf && pf != null && pf.Controls.Count <= 32);
{
    var s = DescriptorParser.ParseState(all);
    Check("parser reads state values", s != null && (bool)s["master_enable"].Value == true && s["hp_threshold"].Number == 60 && s["mode"].Number == 0, all);
    Check("parser reads lock and enabled flags", DescriptorParser.ParseState("{\"p\":1,\"v\":{\"a\":{\"v\":true,\"locked\":true,\"why\":\"x\",\"enabled\":false}}}") is var ps
        && ps != null && ps["a"].Locked && !ps["a"].Enabled && ps["a"].Why == "x");
    Check("state parser returns null for garbage", DescriptorParser.ParseState("???") == null && DescriptorParser.ParseState(null) == null);
}
{
    var okR = DescriptorParser.ParseResult(ep0.Set("mode", "1"));
    var noR = DescriptorParser.ParseResult(ep0.Set("mode", "99"));
    Check("result parser reads ok and refusal", okR.Ok && !noR.Ok && noR.Why.Length > 0);
    Check("result parser treats garbage as a refusal", !DescriptorParser.ParseResult("???").Ok && !DescriptorParser.ParseResult(null).Ok);
}
Check("value to JSON round-trips each kind",
    DescriptorParser.ValueToJson(true) == "true" && DescriptorParser.ValueToJson(false) == "false"
    && DescriptorParser.ValueToJson(3) == "3" && DescriptorParser.ValueToJson(2.5) == "2.5");

Console.WriteLine("LalaHub.Harness: " + pass + " pass, " + fail + " fail");
return fail == 0 ? 0 : 1;
