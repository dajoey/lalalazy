// Shared source (NOT a shared DLL). Core/ is Dalamud-free.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Lalalazy.Telemetry;

/// <summary>
/// A 12-hex fingerprint that stays the same for "the same failure" across occurrences, builds and
/// machines: <c>sha1(plugin|area|typeChain|top frames)</c>. A frame is <c>Namespace.Type.Method</c> read
/// from the captured stack WITHOUT file info (no PDB lookup, no line numbers, no paths), with every digit
/// folded to <c>#</c>, so an unrelated edit that shifts line numbers, a compiler-renumbered lambda
/// (<c>b__12_0</c>) or a different build host does not split a group - and computing it stays cheap
/// enough to run on every occurrence, including the suppressed ones. The message is deliberately NOT
/// hashed (it carries ids, names, counts) - except for an exception that was never thrown and so has no
/// stack, where a digit/quote-normalised message is the only discriminator left.
/// </summary>
public static class ExceptionFingerprint
{
    /// <summary>How many leading stack frames identify a failure.</summary>
    public const int FrameCount = 5;

    private static readonly Regex InFileSuffix = new(@"\s+in\s+.+:line\s+\d+\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Quoted = new("(\"[^\"]*\"|'[^']*')", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Compute(string plugin, string area, Exception ex)
    {
        var sb = new StringBuilder(256);
        sb.Append(plugin).Append('|').Append(area).Append('|').Append(TypeChain(ex)).Append('|');

        // The outermost exception in the chain that has frames (a task's AggregateException has none).
        var frames = new List<string>(0);
        var cur = ex;
        for (var depth = 0; cur is not null && frames.Count == 0 && depth < 6; depth++)
        {
            frames = MethodFrames(cur, FrameCount);
            cur = cur is AggregateException { InnerExceptions.Count: > 0 } agg ? agg.InnerExceptions[0] : cur.InnerException;
        }

        if (frames.Count > 0)
            sb.Append(string.Join(";", frames));
        else
            sb.Append(NormalizeMessage(ex.Message));

        return Hash(sb.ToString());
    }

    /// <summary>Up to <paramref name="max"/> frames as <c>Type.Method</c> (digits folded), from the captured stack without file info.</summary>
    public static List<string> MethodFrames(Exception ex, int max)
    {
        var result = new List<string>(max);
        try
        {
            foreach (var frame in new StackTrace(ex, false).GetFrames())
            {
                var m = frame.GetMethod();
                if (m is null)
                    continue;
                result.Add(Digits.Replace((m.DeclaringType?.FullName ?? "?") + "." + m.Name, "#"));
                if (result.Count >= max)
                    break;
            }
        }
        catch
        {
            // Unresolvable frames: fall back to whatever was collected (possibly nothing -> the message).
        }
        return result;
    }

    /// <summary>Outer-to-inner exception type names joined by <c>&gt;</c> (first inner only for an AggregateException).</summary>
    public static string TypeChain(Exception ex)
    {
        var sb = new StringBuilder(96);
        var cur = ex;
        for (var depth = 0; cur is not null && depth < 6; depth++)
        {
            if (depth > 0)
                sb.Append('>');
            sb.Append(cur.GetType().FullName ?? cur.GetType().Name);
            cur = cur.InnerException;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Up to <paramref name="max"/> "at ..." frames of a stack TEXT, file/line stripped, digits folded to <c>#</c>.
    /// Not used for <see cref="Compute"/> (which reads frames without file info); kept for pipeline-side
    /// grouping of stack text and for the harness.
    /// </summary>
    public static List<string> NormalizedFrames(string? stackTrace, int max)
    {
        var result = new List<string>(max);
        if (string.IsNullOrEmpty(stackTrace))
            return result;

        foreach (var raw in stackTrace.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("at ", StringComparison.Ordinal))
                continue;
            result.Add(NormalizeFrame(line));
            if (result.Count >= max)
                break;
        }
        return result;
    }

    public static string NormalizeFrame(string frame)
    {
        var f = frame.Trim();
        if (f.StartsWith("at ", StringComparison.Ordinal))
            f = f[3..];
        f = InFileSuffix.Replace(f, string.Empty);
        return Digits.Replace(f, "#");
    }

    public static string NormalizeMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;
        return Digits.Replace(Quoted.Replace(message, "'_'"), "#");
    }

    /// <summary>First 12 lower-case hex digits of SHA-1 over the UTF-8 text.</summary>
    public static string Hash(string text)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }
}
