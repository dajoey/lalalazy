#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Lalalazy.Hub;

public sealed class ParsedControl
{
    public string Id = "";
    public string Label = "";
    public ControlKind Kind;
    public string Group = "";
    public string Tip = "";
    public bool Master;
    public double Min;
    public double Max;
    public double Step = 1;
    public string Unit = "";
    public int Decimals;
    public List<string> Choices = new List<string>();
    public string Confirm = "";
}

public sealed class ParsedDescriptor
{
    public string Plugin = "";
    public string Version = "";
    public List<ParsedControl> Controls = new List<ParsedControl>();
}

public sealed class ParsedState
{
    /// <summary>bool for a toggle, double for a stepper or choice index, null when the control has no value.</summary>
    public object? Value;
    public bool Enabled = true;
    public bool Locked;
    public string Why = "";

    public double Number => Value is double d ? d : 0;
    public bool Bool => Value is bool b && b;
}

/// <summary>
/// The hub side of the protocol. It reads what an adapter sent and never throws: an old, broken or
/// hostile adapter must not be able to crash the hub or the game, so anything unreadable becomes null.
/// </summary>
public static class DescriptorParser
{
    /// <summary>Longest payload read at all. The biggest legal descriptor is a few tens of KB.</summary>
    private const int MaxPayloadChars = 128 * 1024;

    public static ParsedDescriptor? ParseDescriptor(string? json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxPayloadChars) return null;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("p", out var p) || p.ValueKind != JsonValueKind.Number || !p.TryGetInt32(out var proto) || proto != HubProtocol.Version) return null;
            if (root.TryGetProperty("error", out _)) return null;

            var result = new ParsedDescriptor
            {
                Plugin = Clip(Str(root, "plugin"), HubProtocol.MaxPluginLength),
                Version = Clip(Str(root, "version"), HubProtocol.MaxVersionLength),
            };

            if (!root.TryGetProperty("controls", out var controls) || controls.ValueKind != JsonValueKind.Array) return result;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in controls.EnumerateArray())
            {
                if (result.Controls.Count >= HubProtocol.MaxControls) break;
                if (c.ValueKind != JsonValueKind.Object) continue;

                var id = Str(c, "id");
                if (!HubValidation.IsValidId(id)) continue;
                if (!seen.Add(id)) continue;   // a repeated id keeps the first, so one id never means two controls
                if (!TryKind(Str(c, "kind"), out var kind)) continue;

                var pc = new ParsedControl
                {
                    Id = id,
                    Label = Clip(Str(c, "label"), HubProtocol.MaxLabelLength),
                    Kind = kind,
                    Group = Clip(Str(c, "group"), HubProtocol.MaxLabelLength),
                    Tip = Clip(Str(c, "tip"), HubProtocol.MaxTipLength),
                    Master = kind == ControlKind.Toggle && Bool(c, "master"),   // only a toggle can drive the Quick tab's on/off
                    Confirm = Clip(Str(c, "confirm"), HubProtocol.MaxConfirmLength),
                    Unit = Clip(Str(c, "unit"), 8),
                };
                if (pc.Label.Length == 0) pc.Label = id;

                if (kind == ControlKind.Stepper)
                {
                    pc.Min = Num(c, "min", 0);
                    pc.Max = Num(c, "max", 1);
                    pc.Step = Num(c, "step", 1);
                    pc.Decimals = (int)Math.Max(0, Math.Min(6, Num(c, "decimals", 0)));
                    if (!(pc.Min < pc.Max) || !(pc.Step > 0)) continue;
                }
                else if (kind == ControlKind.Choice)
                {
                    var intact = true;
                    if (c.TryGetProperty("choices", out var ch) && ch.ValueKind == JsonValueKind.Array)
                        foreach (var item in ch.EnumerateArray())
                        {
                            if (pc.Choices.Count >= HubProtocol.MaxChoices) break;
                            // A non-text option would shift every later index, so the hub would write the wrong choice: drop the control.
                            if (item.ValueKind != JsonValueKind.String) { intact = false; break; }
                            pc.Choices.Add(Clip(item.GetString() ?? "", HubProtocol.MaxChoiceLength));
                        }
                    if (!intact || pc.Choices.Count == 0) continue;
                }

                result.Controls.Add(pc);
            }

            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static Dictionary<string, ParsedState>? ParseState(string? json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxPayloadChars) return null;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("p", out var p) || !p.TryGetInt32(out var proto) || proto != HubProtocol.Version) return null;
            if (!root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Object) return null;

            var map = new Dictionary<string, ParsedState>(StringComparer.Ordinal);
            foreach (var prop in v.EnumerateObject())
            {
                if (map.Count >= HubProtocol.MaxControls) break;
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;

                var st = new ParsedState();
                if (prop.Value.TryGetProperty("v", out var val))
                {
                    if (val.ValueKind == JsonValueKind.True) st.Value = true;
                    else if (val.ValueKind == JsonValueKind.False) st.Value = false;
                    else if (val.ValueKind == JsonValueKind.Number && val.TryGetDouble(out var d) && !double.IsNaN(d) && !double.IsInfinity(d)) st.Value = d;
                }
                if (prop.Value.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False) st.Enabled = false;
                st.Locked = Bool(prop.Value, "locked");
                st.Why = Clip(Str(prop.Value, "why"), HubProtocol.MaxTipLength);
                map[prop.Name] = st;
            }
            return map;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static SetOutcome ParseResult(string? json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxPayloadChars) return SetOutcome.Refuse("no answer from the plugin");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ok", out var ok)) return SetOutcome.Refuse("unreadable answer from the plugin");
            if (ok.ValueKind == JsonValueKind.True) return SetOutcome.Success;
            return SetOutcome.Refuse(Clip(Str(root, "why"), HubProtocol.MaxTipLength) is var w && w.Length > 0 ? w : "refused", Bool(root, "locked"));
        }
        catch (Exception)
        {
            return SetOutcome.Refuse("unreadable answer from the plugin");
        }
    }

    /// <summary>The JSON text the hub sends to <c>Set</c> for a value.</summary>
    public static string ValueToJson(object value)
    {
        switch (value)
        {
            case bool b: return b ? "true" : "false";
            case int i: return i.ToString(CultureInfo.InvariantCulture);
            case long l: return l.ToString(CultureInfo.InvariantCulture);
            case double d: return d.ToString("R", CultureInfo.InvariantCulture);
            default: return "null";
        }
    }

    private static bool TryKind(string name, out ControlKind kind)
    {
        switch (name)
        {
            case "toggle": kind = ControlKind.Toggle; return true;
            case "stepper": kind = ControlKind.Stepper; return true;
            case "choice": kind = ControlKind.Choice; return true;
            case "button": kind = ControlKind.Button; return true;
            default: kind = ControlKind.Button; return false;
        }
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? (p.GetString() ?? "") : "";

    private static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static double Num(JsonElement e, string name, double fallback)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d) && !double.IsNaN(d) && !double.IsInfinity(d) ? d : fallback;

    /// <summary>
    /// Strips control characters, then clips. Strings go straight into native text nodes, which read byte 0x02 as the
    /// start of a game text macro, so nothing an adapter sends may carry one.
    /// </summary>
    private static string Clip(string s, int max)
    {
        var clean = false;
        foreach (var ch in s) if (char.IsControl(ch)) { clean = true; break; }
        if (clean)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var ch in s) if (!char.IsControl(ch)) sb.Append(ch);
            s = sb.ToString();
        }
        return s.Length <= max ? s : s.Substring(0, max);
    }
}
