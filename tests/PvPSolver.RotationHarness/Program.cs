using PvPSolver.RotationHarness.Cases;

namespace PvPSolver.RotationHarness;

/// <summary>
///     Offline proofs for the PvP Solver rotation-review fixes. Every change carries exactly one proof tier
///     (T1 pure core + canary, T2 source shape); see the case files. No game and no Dalamud are needed for
///     T2 cases, they read the real source files.
/// </summary>
internal static class Program
{
    /// <summary>The src/PvPSolver folder under test.</summary>
    public static string SrcRoot { get; private set; } = "";

    private static int Main()
    {
        AddDalamudResolver();
        SrcRoot = FindSrcRoot();
        Console.WriteLine("-- PvPSolver rotation harness --");
        Console.WriteLine("source root: " + SrcRoot);

        Scaffold.Run();
        EmergencyGcd.Run();
        SchChain.Run();
        VprCoil.Run();
        GnbBlast.Run();
        GnbHeart.Run();
        PldThreshold.Run();
        SmiteGuard.Run();
        UtilityAllowlist.Run();
        DefenseStall.Run();
        MeleeRange.Run();
        GuardianRange.Run();
        BrdPaean.Run();
        WhmAquaveil.Run();

        return Harness.Finish();
    }

    // The plugin's keep-lists leave Dalamud's own assemblies out of the output folder; resolve them from the
    // Dalamud dev folder the way the plugin build does so the real PvPSolver.Basic types load without a game.
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

    private static string FindSrcRoot()
    {
        var env = Environment.GetEnvironmentVariable("PVPROT_SRC");
        if (!string.IsNullOrWhiteSpace(env))
        {
            if (!File.Exists(Path.Combine(env, "PvPSolver.sln")))
                throw new DirectoryNotFoundException("PVPROT_SRC does not hold PvPSolver.sln: " + env);
            return Path.GetFullPath(env);
        }

        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "src", "PvPSolver");
            if (File.Exists(Path.Combine(c, "PvPSolver.sln"))) return c;
        }

        throw new DirectoryNotFoundException("src/PvPSolver not found above " + AppContext.BaseDirectory);
    }
}
