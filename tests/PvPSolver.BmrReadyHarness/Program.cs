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

        // Nothing in the IPC helpers may still use the typed-cast lookup: Version(name) went through it too.
        var ipcText = File.ReadAllText(src);
        Check("IPCSubscriber.cs has no TryGetDalamudPlugin call left (Version included)", !ipcText.Contains("TryGetDalamudPlugin("));

        // Once-per-change logging: a per-frame presence check logs the first answer and each flip, never per frame.
        Console.WriteLine("-- BMR presence logged once per change --");
        var latchType = Type.GetType("RotationSolver.IPC.PresenceLatch, PvPSolver");
        Check("PresenceLatch exists", latchType != null);
        if (latchType != null)
        {
            var latch = Activator.CreateInstance(latchType, nonPublic: true)!;
            var changed = latchType.GetMethod("Changed", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
                          ?? throw new MissingMemberException("PresenceLatch.Changed not found");
            int Frames(bool present, int frames)
            {
                var logs = 0;
                for (var i = 0; i < frames; i++)
                    if ((bool)changed.Invoke(latch, new object[] { present })!) logs++;
                return logs;
            }
            Check("BMR missing for 10,000 frames -> logged once", Frames(false, 10_000) == 1);
            Check("BMR loads -> logged once more, then quiet for 10,000 frames", Frames(true, 10_001) == 1);
            Check("BMR unloads -> logged once", Frames(false, 10_000) == 1);
            var fresh = Activator.CreateInstance(latchType, nonPublic: true)!;
            Check("a present-at-start BMR is logged once too", (bool)changed.Invoke(fresh, new object[] { true })! && !(bool)changed.Invoke(fresh, new object[] { true })!);
        }

        var timelineSrc = File.ReadAllText(FindSource("BMRTimeline_IPCSubscriber.cs"));
        var isEnabledBody = Between(timelineSrc, "internal static bool IsEnabled", "[EzIPC(");
        Check("BMRTimeline IsEnabled logs through a PresenceLatch",
            System.Text.RegularExpressions.Regex.IsMatch(timelineSrc, @"PresenceLatch\s+_presence\s*=") && (isEnabledBody.Contains("_presence.Changed(") || isEnabledBody.Contains("_presence.Observe(")));

        // Unload drops the timings read from BMR: the latch's change to "not present" runs the reset exactly once.
        Console.WriteLine("-- BMR unload resets the timeline data --");
        var observe = latchType?.GetMethod("Observe", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        Check("PresenceLatch.Observe exists", observe != null);
        if (latchType != null && observe != null)
        {
            var latch = Activator.CreateInstance(latchType, nonPublic: true)!;
            var logs = 0;
            var resets = 0;
            var nextRaidwideIn = float.MaxValue; // stands in for DataCenter.BMRNextRaidwideIn
            Action<bool> log = _ => logs++;
            Action gone = () => { resets++; nextRaidwideIn = float.MaxValue; };
            void Frames(bool present, int frames)
            {
                for (var i = 0; i < frames; i++)
                {
                    observe.Invoke(latch, new object[] { present, log, gone });
                    if (present) nextRaidwideIn = 3.5f; // BossModUpdater polling a live timeline
                }
            }
            Frames(true, 1000);
            Check("BMR present at start: logged once, no reset", logs == 1 && resets == 0, $"logs={logs} resets={resets}");
            Check("while present the timing is live", nextRaidwideIn == 3.5f);
            Frames(false, 1000);
            Check("BMR unloads: timings reset once (no stale 3.5 s raidwide left)", resets == 1 && nextRaidwideIn == float.MaxValue, $"resets={resets} value={nextRaidwideIn}");
            Check("BMR unloads: logged once more, not per frame", logs == 2, logs.ToString());
            Frames(true, 1000);
            Frames(false, 1000);
            Check("load then unload again: one more reset each unload", resets == 2 && logs == 4, $"resets={resets} logs={logs}");
        }

        var majorSrc = File.ReadAllText(FindSource(Path.Combine("..", "Updaters", "MajorUpdater.cs")));
        Check("IsEnabled resets DataCenter BMR data through the latch",
            isEnabledBody.Contains("_presence.Observe(") && isEnabledBody.Contains("DataCenter.ResetBmrData"));
        Check("MajorUpdater gates BossModUpdater on BMRTimeline IsEnabled (the latch sees every unload while active)",
            System.Text.RegularExpressions.Regex.IsMatch(majorSrc, @"if \(BMRTimeline_IPCSubscriber\.IsEnabled\)\s*\{\s*BossModUpdater\.Update\(\);"));

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

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { _pass++; Console.WriteLine("PASS " + name); }
        else { _fail++; Console.WriteLine("FAIL " + name + (detail.Length > 0 ? $"  [{detail}]" : "")); }
    }
}
