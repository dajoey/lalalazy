using System.Reflection;
using System.Runtime.Loader;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     The real PvPSolver.Basic assembly for reflection proofs. Normally the one built from this tree (project
///     reference). With env PVPROT_BASIC_DLL set it is that file, loaded into its own load context, so a proof can be
///     replayed against a build of the unmodified origin/main.
/// </summary>
internal static class BasicAssembly
{
    private static Assembly? _asm;

    public static Assembly Get()
    {
        if (_asm != null) return _asm;
        var path = Environment.GetEnvironmentVariable("PVPROT_BASIC_DLL");
        if (string.IsNullOrWhiteSpace(path))
            return _asm = typeof(RotationSolver.Basic.Data.ActionID).Assembly;

        var ctx = new AssemblyLoadContext("pvprot-basic-replay", isCollectible: false);
        return _asm = ctx.LoadFromAssemblyPath(Path.GetFullPath(path));
    }

    private static Assembly? _old;

    /// <summary>
    ///     A build of the previous release (unmodified origin/main) for cross-version checks: the file named by env
    ///     PVPROT_OLD_BASIC_DLL, loaded into its own load context, or null when the variable is not set. Used to prove a
    ///     file written by this build loads in the previous one and that an untouched file is identical in both.
    /// </summary>
    public static Assembly? Old()
    {
        if (_old != null)
        {
            return _old;
        }

        var path = Environment.GetEnvironmentVariable("PVPROT_OLD_BASIC_DLL");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var ctx = new AssemblyLoadContext("pvprot-old-basic", isCollectible: false);
        return _old = ctx.LoadFromAssemblyPath(Path.GetFullPath(path));
    }

    public static Type Type(string fullName) =>
        Get().GetType(fullName) ?? throw new TypeLoadException(fullName + " not found in " + Get().Location);
}
