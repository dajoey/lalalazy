// Shared source (NOT a shared DLL) - compiled into every lalalazy plugin that opts in, and into
// tests/LalaTelemetry.Harness. Core/ must never reference a Dalamud type: the harness build is the lint.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lalalazy.Telemetry;

/// <summary>
/// The one escaping rule every <c>ER|</c> / <c>RP|</c> value goes through (docs/telemetry-lines.md).
/// After escaping, a value never contains a raw <c>|</c>, CR, LF or other control character, so a
/// line splits on the literal <c>|</c> with no lookbehind and stays on one physical log line.
/// </summary>
public static class TelemetryEscape
{
    /// <summary>Field separator. Never appears inside an escaped value.</summary>
    public const char Separator = '|';

    /// <summary>Escapes a value: <c>\</c>-&gt;<c>\\</c>, <c>|</c>-&gt;<c>\p</c>, LF/CR/TAB-&gt;<c>\n \r \t</c>, other C0 + DEL -&gt;<c>\u00XX</c>.</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var sb = new StringBuilder(value.Length + 8);
        AppendEscaped(sb, value, int.MaxValue);
        return sb.ToString();
    }

    /// <summary>
    /// Appends at most <paramref name="maxChars"/> source characters of <paramref name="value"/>, escaped.
    /// Returns true when the value was cut short. Never splits a surrogate pair.
    /// </summary>
    public static bool AppendEscaped(StringBuilder sb, string? value, int maxChars)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        var n = value.Length;
        var truncated = false;
        if (n > maxChars)
        {
            n = Math.Max(0, maxChars);
            if (n > 0 && char.IsHighSurrogate(value[n - 1]))
                n--;
            truncated = true;
        }

        for (var i = 0; i < n; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '|': sb.Append("\\p"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20 || c == 0x7F)
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }

        return truncated;
    }

    /// <summary>Reverses <see cref="Escape"/>. An unknown or dangling escape is kept literally (never throws).</summary>
    public static string Unescape(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0)
            return value ?? string.Empty;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c != '\\' || i + 1 >= value.Length)
            {
                sb.Append(c);
                continue;
            }

            var e = value[i + 1];
            switch (e)
            {
                case '\\': sb.Append('\\'); i++; break;
                case 'p': sb.Append('|'); i++; break;
                case 'n': sb.Append('\n'); i++; break;
                case 'r': sb.Append('\r'); i++; break;
                case 't': sb.Append('\t'); i++; break;
                case 'u' when i + 5 < value.Length
                              && int.TryParse(value.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code):
                    sb.Append((char)code);
                    i += 5;
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// Builds one <c>XX|unixms|kind|key=value|...</c> line. Keys are fixed lower-case ASCII; every value is
/// escaped and length-capped. Keys whose value was cut short are listed in a trailing <c>tr=</c> field,
/// so truncation is explicit instead of guessed from a marker character.
/// </summary>
public sealed class TelemetryLineBuilder
{
    private readonly StringBuilder _sb;
    private List<string>? _truncated;

    public TelemetryLineBuilder(string prefix, long unixMs, string kind, int capacity = 256)
    {
        _sb = new StringBuilder(capacity);
        _sb.Append(prefix).Append(unixMs.ToString(CultureInfo.InvariantCulture)).Append(TelemetryEscape.Separator);
        TelemetryEscape.AppendEscaped(_sb, kind, 32);
    }

    /// <summary>Appends <c>|key=value</c> (value escaped, at most <paramref name="maxChars"/> source characters).</summary>
    public TelemetryLineBuilder Field(string key, string? value, int maxChars = 512)
    {
        _sb.Append(TelemetryEscape.Separator).Append(key).Append('=');
        if (TelemetryEscape.AppendEscaped(_sb, value, maxChars))
            (_truncated ??= new List<string>(2)).Add(key);
        return this;
    }

    public TelemetryLineBuilder Field(string key, long value)
    {
        _sb.Append(TelemetryEscape.Separator).Append(key).Append('=').Append(value.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    public TelemetryLineBuilder Field(string key, bool value)
    {
        _sb.Append(TelemetryEscape.Separator).Append(key).Append('=').Append(value ? '1' : '0');
        return this;
    }

    public override string ToString()
    {
        if (_truncated is null)
            return _sb.ToString();
        return new StringBuilder(_sb.Length + 16).Append(_sb).Append("|tr=").Append(string.Join(",", _truncated)).ToString();
    }
}

/// <summary>A parsed <c>XX|unixms|kind|k=v|...</c> line (harness + reference parser for docs/telemetry-lines.md).</summary>
public sealed class ParsedTelemetryLine
{
    public string Prefix { get; private init; } = string.Empty;
    public long UnixMs { get; private init; }
    public string Kind { get; private init; } = string.Empty;
    public IReadOnlyList<KeyValuePair<string, string>> Fields { get; private init; } = Array.Empty<KeyValuePair<string, string>>();

    /// <summary>First value for <paramref name="key"/>, unescaped; null when absent.</summary>
    public string? Get(string key)
    {
        foreach (var kv in Fields)
            if (kv.Key == key)
                return kv.Value;
        return null;
    }

    /// <summary>
    /// Splits on the literal <c>|</c> (safe: escaped values never contain one), then each field on its
    /// FIRST <c>=</c>. Returns false for anything that is not <c>AA|digits|kind...</c>.
    /// </summary>
    public static bool TryParse(string? line, out ParsedTelemetryLine parsed)
    {
        parsed = new ParsedTelemetryLine();
        if (string.IsNullOrEmpty(line))
            return false;

        var parts = line.Split(TelemetryEscape.Separator);
        if (parts.Length < 3 || parts[0].Length == 0)
            return false;
        if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ms))
            return false;

        var fields = new List<KeyValuePair<string, string>>(parts.Length - 3);
        for (var i = 3; i < parts.Length; i++)
        {
            var p = parts[i];
            var eq = p.IndexOf('=');
            if (eq <= 0)
                return false;
            fields.Add(new KeyValuePair<string, string>(p[..eq], TelemetryEscape.Unescape(p[(eq + 1)..])));
        }

        parsed = new ParsedTelemetryLine
        {
            Prefix = parts[0] + "|",
            UnixMs = ms,
            Kind = TelemetryEscape.Unescape(parts[2]),
            Fields = fields,
        };
        return true;
    }
}
