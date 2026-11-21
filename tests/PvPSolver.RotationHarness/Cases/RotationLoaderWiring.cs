using System.Text.RegularExpressions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Regression gate for v0.1.2.26 - v0.1.2.31: the duty-rotation removal deleted the only call
///     that fills CustomRotations, so the plugin loaded, logged "triggering rotation load..." and
///     then silently did nothing (the scan never ran, no error line, no rotation in any PvP match).
///     T2 source shape: LoadBuiltInRotations must assign CustomRotations from LoadCustomRotationGroup
///     and log the found-count line; MajorUpdater must still call LoadBuiltInRotations.
///     Metadata: the real plugin assembly must contain at least one non-abstract ICustomRotation type
///     with a parameterless constructor and a JobsAttribute - the exact filter LoadCustomRotationGroup
///     applies - so a scrub that empties the rotation set fails here too.
/// </summary>
internal static class RotationLoaderWiring
{
    private static readonly Regex PublicStaticVoid = new(@"\bpublic\s+static\s+void\s+(?<name>\w+)\s*\(", RegexOptions.Compiled);

    private const string WiringCall = "CustomRotations = LoadCustomRotationGroup(assemblies)";

    public static void Run()
    {
        // T2: the scan call inside LoadBuiltInRotations (sanitized, so a commented-out call cannot pass).
        string updaterPath = Path.Combine(Program.SrcRoot, "PvPSolver", "Updaters", "RotationUpdater.cs");
        string raw = File.ReadAllText(updaterPath);
        string san = CsSource.Sanitize(raw);
        CsSource.Method? load = CsSource.Methods(san, updaterPath, PublicStaticVoid).FirstOrDefault(m => m.Name == "LoadBuiltInRotations");
        Harness.Case("loader wiring: LoadBuiltInRotations exists", load != null, updaterPath);
        if (load != null)
        {
            Harness.Case("loader wiring: scan call present in LoadBuiltInRotations", load.Body.Contains(WiringCall),
                "without this assignment CustomRotations stays empty and every rotation consumer silently no-ops (shipped v0.1.2.26 - v0.1.2.31)");
            Harness.Case("loader wiring: found-count log line present",
                raw.Contains("LoadBuiltInRotations: found {CustomRotations.Length} rotation groups"),
                "the live-path evidence line the log shows after 'triggering rotation load...'");
            // Canary: the same detector on text with the call removed must NOT find it.
            Harness.Canary("loader wiring: detector fails when the call is removed",
                load.Body.Replace(WiringCall, "").Contains(WiringCall));
        }

        // T2: the trigger side - MajorUpdater's first-valid-cycle path must call the loader.
        string majorPath = Path.Combine(Program.SrcRoot, "PvPSolver", "Updaters", "MajorUpdater.cs");
        string majorSan = CsSource.Sanitize(File.ReadAllText(majorPath));
        Harness.Case("loader wiring: MajorUpdater calls the loader",
            majorSan.Contains("RotationUpdater.LoadBuiltInRotations()"),
            "the trigger logs 'triggering rotation load...' and must then call RotationUpdater.LoadBuiltInRotations()");

        // Metadata: the scan filter over the real plugin assembly must find at least one rotation.
        (int Total, int Icr, int Loadable, string? Error) scan = Scan(typeof(RotationSolver.PvPSolverPlugin).Assembly);
        Harness.Case("loader scan: plugin assembly has loadable rotations", scan.Error == null && scan.Loadable > 0,
            scan.Error ?? $"scanned {scan.Total} types, {scan.Icr} implement ICustomRotation, {scan.Loadable} pass the scan filter (need > 0)");
        // Canary: the same filter over an assembly that contains no rotations must count 0.
        (int Total, int Icr, int Loadable, string? Error) self = Scan(typeof(Program).Assembly);
        Harness.Canary("loader scan: filter counts 0 on an assembly without rotations", self.Error == null && self.Loadable > 0);
    }

    private static (int Total, int Icr, int Loadable, string? Error) Scan(System.Reflection.Assembly assembly)
    {
        try
        {
            Type[] types = assembly.GetTypes();
            int icr = 0, loadable = 0;
            foreach (Type t in types)
            {
                if (t.IsAbstract || t.IsInterface) continue;
                if (!typeof(RotationSolver.Basic.Rotations.ICustomRotation).IsAssignableFrom(t)) continue;
                icr++;
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                // inherit: true mirrors runtime GetCustomAttribute<JobsAttribute>(); the PvP rotations carry
                // [Jobs] on their GENERATED per-job base classes, not on themselves (verified: zero direct
                // [Jobs( hits under RebornRotations/PVPRotations; the attribute is in Resources.resx).
                if (!t.GetCustomAttributes(inherit: true).Any(a => a.GetType().Name == "JobsAttribute")) continue;
                loadable++;
            }

            return (types.Length, icr, loadable, null);
        }
        catch (Exception e)
        {
            return (0, 0, 0, e.GetType().Name + ": " + e.Message);
        }
    }
}
