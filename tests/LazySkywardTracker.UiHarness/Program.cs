using System.Reflection;

namespace LazySkywardTracker.UiHarness;

/// <summary>Offline checks for the tracker window's live-state decisions and row layout math.</summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;
    private static MethodInfo _derive = null!;
    private static PropertyInfo _state = null!;
    private static PropertyInfo _reason = null!;

    private static void Main()
    {
        AddDalamudResolver();
        var window = Type.GetType("LazySkywardTracker.TrackerWindow, LazySkywardTracker")
                     ?? throw new MissingMemberException("TrackerWindow not found");
        _derive = window.GetMethod("DeriveLive", BindingFlags.NonPublic | BindingFlags.Static)
                  ?? throw new MissingMemberException("DeriveLive not found");
        var live = _derive.ReturnType;
        _state = live.GetProperty("State") ?? throw new MissingMemberException("Live.State not found");
        _reason = live.GetProperty("Reason") ?? throw new MissingMemberException("Live.Reason not found");

        State("logged out remains Blocked even with cached progress", "Blocked", Derive(false, 11, 4, 2000000, 800));
        State("logged in without a progress report remains Empty", "Empty", Derive(true, 0, 0, 0, 0));
        State("all jobs complete remains Ready", "Ready", Derive(true, 11, 11, 5500000, 0));
        State("partial progress remains Running", "Running", Derive(true, 11, 4, 2000000, 0));

        var projected = Derive(true, 11, 4, 2000000, 1200);
        Check("inventory points keep their live footer reason", _reason.GetValue(projected) is string reason && reason.Contains("points waiting", StringComparison.Ordinal));
        Check("no inventory points keep an empty footer reason", _reason.GetValue(Derive(true, 11, 4, 2000000, 0)) is null);

        var columns = window.GetMethod("ColumnsFor", BindingFlags.NonPublic | BindingFlags.Static)
                      ?? throw new MissingMemberException("ColumnsFor not found");
        foreach (var width in new[] { 760f, 900f, 1180f })
        {
            var value = columns.Invoke(null, [width]) ?? throw new InvalidOperationException("ColumnsFor returned null");
            var type = value.GetType();
            var progressX = (float)(type.GetProperty("ProgressX")?.GetValue(value) ?? 0f);
            var progressWidth = (float)(type.GetProperty("ProgressWidth")?.GetValue(value) ?? 0f);
            Check($"wide row columns fit at {width:0}px", progressWidth >= 80 && progressX + progressWidth + 206 <= width + 0.01f);
        }

        Console.WriteLine(_fail == 0 ? $"OK ({_pass} checks)" : $"FAILED ({_fail} of {_pass + _fail})");
        Environment.ExitCode = _fail == 0 ? 0 : 1;
    }

    private static object Derive(bool loggedIn, int reports, int completed, uint current, uint inventory)
        => _derive.Invoke(null, [loggedIn, reports, completed, current, inventory])
           ?? throw new InvalidOperationException("DeriveLive returned null");

    private static void State(string name, string expected, object live)
        => Check(name, Equals(_state.GetValue(live)?.ToString(), expected));

    private static void Check(string name, bool passed)
    {
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
        if (passed) _pass++; else _fail++;
    }

    private static void AddDalamudResolver()
    {
        var dir = Environment.GetEnvironmentVariable("DALAMUD_HOME")
                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                      "XIVLauncher", "addon", "Hooks", "dev");
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var file = Path.Combine(dir, name.Name + ".dll");
            return File.Exists(file) ? context.LoadFromAssemblyPath(file) : null;
        };
    }
}
