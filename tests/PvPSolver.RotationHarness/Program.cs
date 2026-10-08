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
        SrcRoot = FindSrcRoot();
        Console.WriteLine("-- PvPSolver rotation harness --");
        Console.WriteLine("source root: " + SrcRoot);

        Scaffold.Run();
        EmergencyGcd.Run();
        SchChain.Run();

        return Harness.Finish();
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
