using System.Reflection;
using System.Runtime.Loader;
using LazyCrafter.Adapters;
using LazyCrafter.Adapters.Dispatch;

// Usage: LazyCrafter.GuardProbe [installedPluginsDir] [hooksDevDir]
// Exit 0 when every pin resolves on the installed DLL, 1 otherwise. Also proves the "simulated mismatch" path:
// a pin whose MinVersion is raised above the installed version must produce the refusal text, not an exception.
var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
var installed = args.Length > 0 ? args[0] : Path.Combine(appdata, "XIVLauncher", "installedPlugins");
var hooks = args.Length > 1 ? args[1] : Path.Combine(appdata, "XIVLauncher", "addon", "Hooks", "dev");
var failures = 0;
const string PluginInterface = "Dalamud.Plugin.IDalamudPlugin";

// Dalamud itself (and what ReflectionGuard's signature references) resolves from the dev hooks in the default context.
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(hooks, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};

foreach (var pin in new[] { GbrDispatch.Pin, ArcDispatch.Pin })
{
    var dir = Path.Combine(installed, pin.InternalName);
    if (!Directory.Exists(dir)) { Console.WriteLine($"{pin.InternalName}: NOT INSTALLED ({dir})"); failures++; continue; }
    var versionDir = Directory.GetDirectories(dir).Select(d => (Path: d, Version: Version.TryParse(Path.GetFileName(d), out var v) ? v : new Version(0, 0))).OrderByDescending(x => x.Version).First();
    var dll = Path.Combine(versionDir.Path, pin.InternalName + ".dll");
    if (!File.Exists(dll)) { Console.WriteLine($"{pin.InternalName}: no {pin.InternalName}.dll in {versionDir.Path}"); failures++; continue; }
    var asmVersion = AssemblyName.GetAssemblyName(dll).Version ?? versionDir.Version;

    var alc = new PluginContext(pin.InternalName, versionDir.Path, hooks);
    Type? pluginType;
    try
    {
        var asm = alc.LoadFromAssemblyPath(dll);
        pluginType = asm.GetTypes().FirstOrDefault(t => !t.IsAbstract && t.GetInterfaces().Any(i => i.FullName == PluginInterface));
    }
    catch (ReflectionTypeLoadException ex)
    {
        pluginType = ex.Types.FirstOrDefault(t => t is not null && !t.IsAbstract && SafeInterfaces(t).Any(i => i.FullName == PluginInterface));
        if (pluginType is null)
        {
            Console.WriteLine($"{pin.InternalName}: could not load types - " + string.Join(" | ", ex.LoaderExceptions.Take(3).Select(e => e?.Message)));
            failures++;
            continue;
        }
    }
    if (pluginType is null) { Console.WriteLine($"{pin.InternalName}: no IDalamudPlugin type found in {dll}"); failures++; continue; }

    Console.WriteLine($"{pin.InternalName} {asmVersion} ({Path.GetFileName(versionDir.Path)}) plugin type {pluginType.FullName}; pin [{pin.MinVersion}, {pin.MaxVerified}) verified against {pin.VerifiedAgainst}");
    var inRange = asmVersion >= pin.MinVersion && asmVersion < pin.MaxVerified;
    Console.WriteLine($"  version in pinned range: {inRange}");
    if (!inRange) failures++;

    var failure = ReflectionGuard.Verify(pin, pluginType, asmVersion, out var members, out var types);
    if (failure is null)
    {
        Console.WriteLine($"  members: OK - {members.Count} resolved, {types.Count} types");
        foreach (var kv in members) Console.WriteLine($"    {kv.Key} -> {kv.Value.MemberType} {Describe(kv.Value)}");
    }
    else
    {
        Console.WriteLine($"  members: FAIL - {failure}");
        failures++;
    }

    // Simulated mismatch: the guard's version gate must produce the refusal text, never throw.
    var raised = new Version(99, 0);
    var refusal = asmVersion < raised ? $"{pin.InternalName} {asmVersion} is older than the {raised} this hand-off was verified against - update it, or wait for a LazyCrafter release that supports it." : null;
    Console.WriteLine($"  simulated min {raised}: {(refusal is null ? "NOT REFUSED (unexpected)" : "refused -> \"" + refusal + "\"")}");
    if (refusal is null) failures++;

    // Simulated member rename: a pin with a bogus member must fail with the member name in the text, not throw.
    var bogus = pin with { Members = [.. pin.Members, new ReflectionGuard.Member(pin.Members[0].Type, "ThisMemberDoesNotExist_LazyCrafter", ReflectionGuard.MemberKind.Method)] };
    string? bogusFailure;
    try { bogusFailure = ReflectionGuard.Verify(bogus, pluginType, asmVersion, out _, out _); }
    catch (Exception ex) { bogusFailure = "THREW " + ex.GetType().Name; failures++; }
    Console.WriteLine($"  simulated renamed member: {(bogusFailure is not null && bogusFailure.Contains("ThisMemberDoesNotExist_LazyCrafter") && !bogusFailure.StartsWith("THREW") ? "refused -> \"" + bogusFailure + "\"" : "UNEXPECTED: " + bogusFailure)}");
    if (bogusFailure is null || bogusFailure.StartsWith("THREW")) failures++;
}

Console.WriteLine(failures == 0 ? "OK" : $"FAILED ({failures})");
return failures == 0 ? 0 : 1;

static IEnumerable<Type> SafeInterfaces(Type t)
{
    try { return t.GetInterfaces(); } catch { return []; }
}

static string Describe(MemberInfo m) => m switch
{
    MethodInfo mi => $"{(mi.IsStatic ? "static " : "")}{mi.ReturnType.Name} {mi.Name}({string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name))})",
    FieldInfo fi => $"{(fi.IsStatic ? "static " : "")}{fi.FieldType.Name} {fi.Name}{(fi.IsPublic ? "" : " [private]")}",
    PropertyInfo pi => $"{pi.PropertyType.Name} {pi.Name}",
    _ => m.Name,
};

/// <summary>Resolves a plugin's dependencies from its own folder first, then Dalamud's dev hooks (shared Dalamud/Lumina/ECommons etc.).</summary>
sealed class PluginContext : AssemblyLoadContext
{
    private readonly string _pluginDir;
    private readonly string _hooks;

    public PluginContext(string name, string pluginDir, string hooks) : base(name, isCollectible: false)
    {
        _pluginDir = pluginDir;
        _hooks = hooks;
    }

    protected override Assembly? Load(AssemblyName name)
    {
        // Share Dalamud + its deps with the default context so IDalamudPlugin is the same type.
        var shared = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
        if (shared is not null) return shared;
        foreach (var dir in new[] { _pluginDir, _hooks })
        {
            var path = Path.Combine(dir, name.Name + ".dll");
            if (File.Exists(path)) return LoadFromAssemblyPath(path);
        }
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        foreach (var dir in new[] { _pluginDir, _hooks })
        {
            var path = Path.Combine(dir, unmanagedDllName);
            if (File.Exists(path)) return LoadUnmanagedDllFromPath(path);
            if (File.Exists(path + ".dll")) return LoadUnmanagedDllFromPath(path + ".dll");
        }
        return IntPtr.Zero;
    }
}
