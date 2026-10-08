using System.Reflection;

namespace PvPSolver.BmrReadyHarness;

/// <summary>
///     Offline proof for the BossModReborn presence check. The pure matcher
///     IPCSubscriber_Common.IsLoadedIn(entries, name) must say true exactly when an installed plugin
///     with that name or internal name is loaded, and the shipping IsReady must no longer go through
///     ECommons' TryGetDalamudPlugin (typed cast, ERR log per call).
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static void AddDalamudResolver()
    {
        var dir = Environment.GetEnvironmentVariable("DALAMUD_HOME")
                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                      "XIVLauncher", "addon", "Hooks", "dev");
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            var file = Path.Combine(dir, name.Name + ".dll");
            return File.Exists(file) ? ctx.LoadFromAssemblyPath(file) : null;
        };
    }

    private static int Main()
    {
        AddDalamudResolver();
        Console.WriteLine("-- PvPSolver BMR presence check --");

        var common = Type.GetType("RotationSolver.IPC.IPCSubscriber_Common, PvPSolver")
                     ?? throw new MissingMemberException("IPCSubscriber_Common not found");
        var matcher = common.GetMethod("IsLoadedIn", BindingFlags.NonPublic | BindingFlags.Static)
                      ?? throw new MissingMemberException("IPCSubscriber_Common.IsLoadedIn not found");

        bool Run(IEnumerable<(string Name, string InternalName, bool IsLoaded)> entries, string name)
            => (bool)matcher.Invoke(null, new object[] { entries, name })!;

        var bmrLoaded = new[] { ("Boss Mod Reborn", "BossModReborn", true), ("Other", "Other", true) };
        var bmrDisabled = new[] { ("Boss Mod Reborn", "BossModReborn", false) };
        var bossModOnly = new[] { ("Boss Mod", "BossMod", true) };
        var byDisplayName = new[] { ("BossModReborn", "SomethingElse", true) };

        Check("loaded plugin by internal name -> ready", Run(bmrLoaded, "BossModReborn"));
        Check("installed but not loaded -> not ready", !Run(bmrDisabled, "BossModReborn"));
        Check("not installed -> not ready", !Run(Array.Empty<(string, string, bool)>(), "BossModReborn"));
        Check("a different loaded plugin does not satisfy the name", !Run(bossModOnly, "BossModReborn"));
        Check("name match is case-insensitive", Run(bmrLoaded, "bossmodreborn"));
        Check("matches on display name too", Run(byDisplayName, "BossModReborn"));

        // The shipping IsReady must not use the typed-cast lookup that threw for BossMod.Plugin.
        var src = FindSource("IPCSubscriber.cs");
        var body = Between(File.ReadAllText(src), "internal static bool IsReady(", ";");
        Check("IsReady no longer calls TryGetDalamudPlugin", !body.Contains("TryGetDalamudPlugin"));

        Console.WriteLine(_fail == 0 ? $"OK ({_pass} checks)" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    private static string FindSource(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var p = Path.Combine(dir.FullName, "src", "PvPSolver", "PvPSolver", "IPC", file);
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(file);
    }

    private static string Between(string text, string start, string end)
    {
        var i = text.IndexOf(start, StringComparison.Ordinal);
        if (i < 0) return "";
        var j = text.IndexOf(end, i, StringComparison.Ordinal);
        return j < 0 ? text[i..] : text[i..j];
    }

    private static void Check(string name, bool ok)
    {
        if (ok) { _pass++; Console.WriteLine("PASS " + name); }
        else { _fail++; Console.WriteLine("FAIL " + name); }
    }
}
