using System.Text;
using System.Text.RegularExpressions;

namespace PvPSolver.RotationHarness;

/// <summary>
///     Just enough C# lexing for source-shape tests: comments become spaces and string/char literal
///     contents become spaces (quotes kept), so a regex or brace match never trips on text that is not code.
///     Offsets and line numbers are preserved.
/// </summary>
internal static class CsSource
{
    public sealed record Method(string Name, string Body, int Line, string File);

    public static string Sanitize(string s)
    {
        var o = new StringBuilder(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            var n = i + 1 < s.Length ? s[i + 1] : '\0';
            if (c == '/' && n == '/')
            {
                while (i < s.Length && s[i] != '\n') { o.Append(s[i] == '\r' ? '\r' : ' '); i++; }
            }
            else if (c == '/' && n == '*')
            {
                o.Append("  "); i += 2;
                while (i < s.Length && !(s[i] == '*' && i + 1 < s.Length && s[i + 1] == '/'))
                { o.Append(s[i] is '\n' or '\r' ? s[i] : ' '); i++; }
                if (i < s.Length) { o.Append("  "); i += 2; }
            }
            else if (c is '"' or '@' or '$' && IsStringStart(s, i))
            {
                // optional @ and $ prefixes
                var verbatim = false;
                while (s[i] is '@' or '$') { if (s[i] == '@') verbatim = true; o.Append(s[i]); i++; }
                o.Append('"'); i++;
                while (i < s.Length)
                {
                    if (verbatim)
                    {
                        if (s[i] == '"' && i + 1 < s.Length && s[i + 1] == '"') { o.Append("  "); i += 2; continue; }
                        if (s[i] == '"') break;
                    }
                    else
                    {
                        if (s[i] == '\\' && i + 1 < s.Length) { o.Append("  "); i += 2; continue; }
                        if (s[i] == '"' || s[i] == '\n') break;
                    }
                    o.Append(s[i] is '\n' or '\r' ? s[i] : ' '); i++;
                }
                if (i < s.Length && s[i] == '"') { o.Append('"'); i++; }
            }
            else if (c == '\'' && IsCharLiteral(s, i))
            {
                o.Append('\''); i++;
                while (i < s.Length && s[i] != '\'')
                {
                    if (s[i] == '\\' && i + 1 < s.Length) { o.Append("  "); i += 2; continue; }
                    o.Append(' '); i++;
                }
                if (i < s.Length) { o.Append('\''); i++; }
            }
            else { o.Append(c); i++; }
        }

        return o.ToString();
    }

    private static bool IsStringStart(string s, int i)
    {
        var j = i;
        while (j < s.Length && s[j] is '@' or '$') j++;
        return j < s.Length && s[j] == '"';
    }

    private static bool IsCharLiteral(string s, int i)
    {
        // 'x' or '\x' ; anything else (e.g. a tick in a type parameter) is not a char literal
        if (i + 2 < s.Length && s[i + 1] != '\\' && s[i + 2] == '\'') return true;
        return i + 3 < s.Length && s[i + 1] == '\\' && s.IndexOf('\'', i + 3) is >= 0 and var k && k - i <= 12;
    }

    /// <summary>Index of the bracket that closes the one opened at <paramref name="open"/> (sanitized text), or -1.</summary>
    public static int Match(string s, int open)
    {
        char o = s[open], c = o switch { '{' => '}', '(' => ')', '[' => ']', _ => throw new ArgumentException("not a bracket") };
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == o) depth++;
            else if (s[i] == c && --depth == 0) return i;
        }

        return -1;
    }

    public static int LineOf(string s, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < s.Length; i++) if (s[i] == '\n') line++;
        return line;
    }

    /// <summary>All methods whose signature matches <paramref name="head"/> (must capture group "name" and end at the "(").</summary>
    public static List<Method> Methods(string sanitized, string file, Regex head)
    {
        var list = new List<Method>();
        foreach (Match m in head.Matches(sanitized))
        {
            var openParen = m.Index + m.Length - 1;
            var closeParen = Match(sanitized, openParen);
            if (closeParen < 0) continue;
            var j = closeParen + 1;
            while (j < sanitized.Length && char.IsWhiteSpace(sanitized[j])) j++;
            if (j >= sanitized.Length || sanitized[j] != '{') continue; // expression-bodied or abstract: no block body
            var end = Match(sanitized, j);
            if (end < 0) continue;
            list.Add(new Method(m.Groups["name"].Value, sanitized.Substring(j + 1, end - j - 1), LineOf(sanitized, m.Index), file));
        }

        return list;
    }

    /// <summary>Conditions of every <c>if (...)</c> in <paramref name="body"/> (whitespace collapsed).</summary>
    public static List<string> IfConditions(string body)
    {
        var list = new List<string>();
        foreach (Match m in Regex.Matches(body, @"\bif\s*\("))
        {
            var open = m.Index + m.Length - 1;
            var close = Match(body, open);
            if (close > open) list.Add(Squash(body.Substring(open + 1, close - open - 1)));
        }

        return list;
    }

    public static string Squash(string s) => Regex.Replace(s, @"\s+", " ").Trim();
}
