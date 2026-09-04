using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons.Reflection;

namespace LazyCrafter.Adapters;

/// <summary>
/// Version pin + loud failure for every reflection hand-off (Plan §Phase 5 layout: <c>ReflectionGuard.cs</c>; Scope §0
/// "version-gate + loud failure"). Reflection into another plugin's private members breaks silently when that plugin
/// refactors; this class turns that into one clear chat line and a <c>false</c>, never an exception.
/// <para>
/// A <see cref="Pin"/> names the plugin (Dalamud <c>InternalName</c>), the version range the member names were verified
/// against, and the members themselves. <see cref="Require"/> checks: plugin installed and loaded → version within
/// [<see cref="Pin.MinVersion"/>, <see cref="Pin.MaxVerified"/>) or above with a warning → every pinned member resolves on
/// the live types. The first failure is reported through <see cref="Fail"/> (chat + log) and the call returns <c>false</c>.
/// </para>
/// <para>
/// Simulating a mismatch for the acceptance test: <c>/lcraft guard &lt;plugin&gt; &lt;minVersion&gt;</c> raises the minimum
/// at runtime (session only) so the next dispatch refuses with the version line; <c>/lcraft guard reset</c> undoes it.
/// </para>
/// </summary>
public sealed class ReflectionGuard
{
    public const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>One reflected member: the type it lives on (<c>null</c> = the plugin instance's type), its name, and what it must be.</summary>
    public sealed record Member(string Type, string Name, MemberKind Kind, Type[]? Parameters = null);
    public enum MemberKind { Field, Property, Method, StaticProperty, StaticMethod, TypeOnly }

    /// <summary>What we verified, and against which upstream version.</summary>
    public sealed record Pin(string InternalName, Version MinVersion, Version MaxVerified, string VerifiedAgainst, IReadOnlyList<Member> Members);

    public sealed class Resolved
    {
        public required IDalamudPlugin Plugin { get; init; }
        public required Assembly Assembly { get; init; }
        public required Version Version { get; init; }
        public required IReadOnlyDictionary<string, MemberInfo> Members { get; init; }
        public required IReadOnlyDictionary<string, Type> Types { get; init; }

        public FieldInfo Field(string key) => (FieldInfo)Members[key];
        public PropertyInfo Property(string key) => (PropertyInfo)Members[key];
        public MethodInfo Method(string key) => (MethodInfo)Members[key];
        public Type Type(string key) => Types[key];
    }

    private readonly IDalamudPluginInterface _pi;
    private readonly IChatGui _chat;
    private readonly IPluginLog _log;
    private readonly Dictionary<string, Version> _overrides = new(StringComparer.OrdinalIgnoreCase);

    public ReflectionGuard(IDalamudPluginInterface pi, IChatGui chat, IPluginLog log)
    {
        _pi = pi;
        _chat = chat;
        _log = log;
    }

    /// <summary>Session-only override of a pin's minimum version (the acceptance test's "simulated mismatch").</summary>
    public void OverrideMinVersion(string internalName, Version? min)
    {
        if (min is null) _overrides.Remove(internalName);
        else _overrides[internalName] = min;
    }

    public IReadOnlyDictionary<string, Version> Overrides => _overrides;

    /// <summary>The installed version of a plugin, or <c>null</c> when it is not installed / not loaded.</summary>
    public Version? InstalledVersion(string internalName, out bool loaded)
    {
        loaded = false;
        foreach (var p in _pi.InstalledPlugins)
        {
            if (!string.Equals(p.InternalName, internalName, StringComparison.Ordinal)) continue;
            loaded = p.IsLoaded;
            return p.Version;
        }
        return null;
    }

    /// <summary>
    /// Verify the pin against the live plugin. Returns the resolved members on success; on any failure prints one
    /// <c>[LazyCrafter]</c> error line to chat, logs the detail, and returns <c>null</c>. Never throws.
    /// </summary>
    public Resolved? Require(Pin pin, string purpose)
    {
        try
        {
            var version = InstalledVersion(pin.InternalName, out var loaded);
            if (version is null) return Fail(pin, purpose, $"{pin.InternalName} is not installed.");
            if (!loaded) return Fail(pin, purpose, $"{pin.InternalName} is installed but not loaded (enable it in the plugin installer).");

            var min = _overrides.TryGetValue(pin.InternalName, out var o) ? o : pin.MinVersion;
            if (version < min)
                return Fail(pin, purpose, $"{pin.InternalName} {version} is older than the {min} this hand-off was verified against - update it, or wait for a LazyCrafter release that supports it.");
            if (version >= pin.MaxVerified)
                _log.Warning("{Plugin} {Version} is newer than the {Max} LazyCrafter's {Purpose} hand-off was verified against ({Against}); trying anyway and checking every member",
                    pin.InternalName, version, pin.MaxVerified, purpose, pin.VerifiedAgainst);

            if (!DalamudReflector.TryGetDalamudPlugin(pin.InternalName, out var plugin, false, true) || plugin is null)
                return Fail(pin, purpose, $"could not reach the {pin.InternalName} plugin instance through Dalamud's plugin manager.");

            var failure = Verify(pin, plugin.GetType(), version, out var members, out var types);
            if (failure is not null) return Fail(pin, purpose, failure);
            return new Resolved { Plugin = plugin, Assembly = plugin.GetType().Assembly, Version = version, Members = members, Types = types };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "ReflectionGuard.Require({Plugin}) threw", pin.InternalName);
            return Fail(pin, purpose, $"unexpected error while inspecting {pin.InternalName}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolve every pinned member on the live types. Static and Dalamud-free so <c>tests/LazyCrafter.GuardProbe</c> can run
    /// it against the installed plugin DLLs without the client. Returns the failure text, or <c>null</c> when all resolved.
    /// </summary>
    public static string? Verify(Pin pin, Type pluginType, Version version, out Dictionary<string, MemberInfo> members, out Dictionary<string, Type> types)
    {
        var assembly = pluginType.Assembly;
        types = new Dictionary<string, Type>(StringComparer.Ordinal);
        members = new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
        foreach (var m in pin.Members)
        {
            var type = m.Type.Length == 0 ? pluginType : ResolveType(assembly, m.Type);
            if (type is null)
                return $"type '{m.Type}' no longer exists in {pin.InternalName} {version} (verified against {pin.VerifiedAgainst}).";
            types[m.Type.Length == 0 ? "<plugin>" : m.Type] = type;
            if (m.Kind == MemberKind.TypeOnly) continue;

            MemberInfo? found = m.Kind switch
            {
                MemberKind.Field => type.GetField(m.Name, Any),
                MemberKind.Property or MemberKind.StaticProperty => type.GetProperty(m.Name, Any),
                MemberKind.Method or MemberKind.StaticMethod => FindMethod(type, m),
                _ => null,
            };
            if (found is null)
                return $"{m.Kind.ToString().ToLowerInvariant()} '{m.Name}' is missing from {type.FullName} in {pin.InternalName} {version} (verified against {pin.VerifiedAgainst}).";
            members[Key(m)] = found;
        }
        return null;
    }

    /// <summary>Lookup key for a pinned member: <c>Type.Name</c> (or <c>Name</c> on the plugin type).</summary>
    public static string Key(Member m) => m.Type.Length == 0 ? m.Name : m.Type + "." + m.Name;
    public static string Key(string type, string name) => type.Length == 0 ? name : type + "." + name;

    /// <summary>Method by name, then by parameter types when given; parameter types are matched by full name so pins can name types from the target assembly.</summary>
    private static MethodInfo? FindMethod(Type type, Member m)
    {
        var candidates = type.GetMethods(Any).Where(x => x.Name == m.Name).ToArray();
        if (candidates.Length == 0) return null;
        if (m.Parameters is null) return candidates[0];
        foreach (var c in candidates)
        {
            var ps = c.GetParameters();
            if (ps.Length != m.Parameters.Length) continue;
            var ok = true;
            for (var i = 0; i < ps.Length && ok; i++)
                ok = ps[i].ParameterType == m.Parameters[i] || ps[i].ParameterType.FullName == m.Parameters[i].FullName;
            if (ok) return c;
        }
        return null;
    }

    private static Type? ResolveType(Assembly assembly, string fullName)
    {
        var t = assembly.GetType(fullName, false);
        if (t is not null) return t;
        // Nested types are addressed with '+' by the runtime; accept '.' in the pin for readability.
        var plus = fullName.LastIndexOf('.');
        while (plus > 0)
        {
            var candidate = fullName[..plus] + "+" + fullName[(plus + 1)..];
            t = assembly.GetType(candidate, false);
            if (t is not null) return t;
            plus = fullName.LastIndexOf('.', plus - 1);
        }
        // Referenced assemblies of the plugin (GBR keeps GameData in its own DLL).
        foreach (var refName in assembly.GetReferencedAssemblies())
        {
            try
            {
                var refAsm = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(assembly)?.LoadFromAssemblyName(refName);
                t = refAsm?.GetType(fullName, false);
                if (t is not null) return t;
            }
            catch { /* not ours to load */ }
        }
        return null;
    }

    private Resolved? Fail(Pin pin, string purpose, string why)
    {
        var line = $"[LazyCrafter] {purpose} hand-off refused: {why}";
        _log.Error("{Line}", line);
        try { _chat.PrintError(line); } catch { /* chat may not be ready at load */ }
        return null;
    }
}
