// Shared source (NOT a shared DLL). Core/ is Dalamud-free.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Lalalazy.Telemetry;

/// <summary>Does an exception involve a given assembly's code? (TaskScheduler events are process-wide.)</summary>
public static class ExceptionOrigin
{
    public static bool Involves(Exception? ex, Assembly assembly) => Involves(ex, assembly, 0);

    private static bool Involves(Exception? ex, Assembly assembly, int depth)
    {
        if (ex is null || depth > 8)
            return false;

        try
        {
            if (ex.TargetSite?.DeclaringType?.Assembly == assembly)
                return true;

            var frames = new StackTrace(ex, false).GetFrames();
            foreach (var frame in frames)
            {
                if (frame.GetMethod()?.DeclaringType?.Assembly == assembly)
                    return true;
            }
        }
        catch
        {
            // A frame whose method cannot be resolved just does not count as ours.
        }

        if (ex is AggregateException agg)
        {
            foreach (var inner in agg.InnerExceptions)
                if (Involves(inner, assembly, depth + 1))
                    return true;
            return false;
        }

        return Involves(ex.InnerException, assembly, depth + 1);
    }
}

/// <summary>
/// Records <see cref="TaskScheduler.UnobservedTaskException"/> - but only exceptions whose stack involves
/// <c>assembly</c>, because the event is process-wide and would otherwise capture every other plugin's
/// faulted tasks. Does not call SetObserved (the process-wide policy is not this plugin's to change).
/// </summary>
public sealed class UnobservedTaskWatcher : IDisposable
{
    private readonly Assembly _assembly;
    private readonly Action<Exception> _record;
    private bool _disposed;

    public UnobservedTaskWatcher(Assembly assembly, Action<Exception> record)
    {
        _assembly = assembly;
        _record = record;
        TaskScheduler.UnobservedTaskException += OnUnobserved;
    }

    /// <summary>Unobserved exceptions seen that were NOT ours (for the harness; never logged).</summary>
    public int ForeignSeen { get; private set; }

    public int OwnSeen { get; private set; }

    private void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (_disposed)
            return;
        try
        {
            if (!ExceptionOrigin.Involves(e.Exception, _assembly))
            {
                ForeignSeen++;
                return;
            }
            OwnSeen++;
            _record(e.Exception);
        }
        catch
        {
            // Runs on the finalizer thread: throwing here would take the process down.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        TaskScheduler.UnobservedTaskException -= OnUnobserved;
    }
}

/// <summary>
/// Compact one-line summary of a configuration object for a report: scalars (bool/number/enum) by name,
/// collections as <c>name#=count</c>, nested objects one level deep as <c>parent.child=</c>. Strings are
/// left out on purpose (names, paths and free text do not belong in a report), and so is anything whose
/// getter throws.
/// </summary>
public static class ConfigSummary
{
    public static string Describe(object? config, int maxDepth = 2, int maxChars = 4000)
    {
        if (config is null)
            return string.Empty;
        var sb = new StringBuilder(512);
        Walk(sb, config, string.Empty, 0, Math.Max(1, maxDepth), maxChars);
        return sb.ToString();
    }

    private static void Walk(StringBuilder sb, object obj, string prefix, int depth, int maxDepth, int maxChars)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;
        var type = obj.GetType();

        var members = new List<(string Name, Func<object?> Get)>();
        foreach (var p in type.GetProperties(Flags))
        {
            if (!p.CanRead || p.GetIndexParameters().Length > 0)
                continue;
            var prop = p;
            members.Add((prop.Name, () => prop.GetValue(obj)));
        }
        foreach (var f in type.GetFields(Flags))
        {
            var field = f;
            members.Add((field.Name, () => field.GetValue(obj)));
        }

        foreach (var (name, get) in members)
        {
            if (sb.Length >= maxChars)
                return;

            object? value;
            try
            {
                value = get();
            }
            catch
            {
                continue;
            }

            var key = prefix + name;
            switch (value)
            {
                case null:
                    continue;
                case string:
                    continue;
                case bool b:
                    Append(sb, key, b ? "1" : "0");
                    break;
                case Enum e:
                    Append(sb, key, e.ToString());
                    break;
                case float or double or decimal:
                    Append(sb, key, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
                    break;
                case sbyte or byte or short or ushort or int or uint or long or ulong:
                    Append(sb, key, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
                    break;
                case IEnumerable when TryCount(value, out var count):
                    Append(sb, key + "#", count.ToString(CultureInfo.InvariantCulture));
                    break;
                default:
                    if (depth + 1 < maxDepth && !value.GetType().IsValueType)
                        Walk(sb, value, key + ".", depth + 1, maxDepth, maxChars);
                    break;
            }
        }
    }

    /// <summary>Count of a collection: ICollection, else a public int Count property (HashSet&lt;T&gt;), else false.</summary>
    private static bool TryCount(object value, out int count)
    {
        if (value is ICollection c)
        {
            count = c.Count;
            return true;
        }
        if (value.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance) is { PropertyType: var t } p && t == typeof(int))
        {
            try
            {
                count = (int)p.GetValue(value)!;
                return true;
            }
            catch
            {
                // Fall through: an uncountable collection is left out like any other throwing getter.
            }
        }
        count = 0;
        return false;
    }

    private static void Append(StringBuilder sb, string key, string value)
    {
        if (sb.Length > 0)
            sb.Append(';');
        sb.Append(key).Append('=').Append(value);
    }
}
