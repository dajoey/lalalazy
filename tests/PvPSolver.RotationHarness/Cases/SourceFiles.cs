using System.Text.RegularExpressions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>Reading the real source files for the T2 (source shape) cases.</summary>
internal static class SourceFiles
{
    public static readonly Regex OverrideBool = new(@"\boverride\s+bool\s+(?<name>\w+)\s*\(", RegexOptions.Compiled);

    public static string PvpRotationsDir => Path.Combine(Program.SrcRoot, "PvPSolver", "RebornRotations", "PVPRotations");

    public static string[] AllPvpRotationFiles() =>
        Directory.GetFiles(PvpRotationsDir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToArray();

    /// <summary>Sanitized source of one per-job PvP rotation file, found by file name anywhere under PVPRotations.</summary>
    public static (string Path, string Sanitized) Rotation(string fileName)
    {
        var hits = AllPvpRotationFiles().Where(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (hits.Length != 1) throw new FileNotFoundException($"{fileName}: {hits.Length} matches under {PvpRotationsDir}");
        return (hits[0], CsSource.Sanitize(File.ReadAllText(hits[0])));
    }

    /// <summary>The single override method <paramref name="method"/> in a per-job rotation file.</summary>
    public static CsSource.Method OverrideMethod(string fileName, string method)
    {
        var (path, san) = Rotation(fileName);
        var ms = CsSource.Methods(san, fileName, OverrideBool).Where(m => m.Name == method).ToArray();
        if (ms.Length != 1) throw new InvalidOperationException($"{fileName}: expected one override bool {method}, found {ms.Length}");
        return ms[0];
    }
}
