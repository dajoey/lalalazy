namespace PvPSolver.RotationHarness;

/// <summary>Case registry: PASS/FAIL per case, one canary that must fail, exit code from the totals.</summary>
internal static class Harness
{
    public static int Pass { get; private set; }
    public static int Fail { get; private set; }
    public static int CanaryFailed { get; private set; }
    public static int CanaryPassedWrongly { get; private set; }

    /// <summary>One check. <paramref name="detail"/> is printed on FAIL (and on PASS when it is non-empty).</summary>
    public static void Case(string name, bool ok, string detail = "")
    {
        if (ok)
        {
            Pass++;
            Console.WriteLine("PASS " + name + (detail.Length > 0 ? " [" + detail + "]" : ""));
        }
        else
        {
            Fail++;
            Console.WriteLine("FAIL " + name + (detail.Length > 0 ? ": " + detail : ""));
        }
    }

    /// <summary>
    ///     A canary is an assertion that is false by construction. It proves the check machinery can fail:
    ///     it must evaluate false, otherwise the harness itself is broken and the run fails.
    /// </summary>
    public static void Canary(string name, bool assertion)
    {
        if (!assertion)
        {
            CanaryFailed++;
            Console.WriteLine("CANARY-FAILED-AS-EXPECTED " + name);
        }
        else
        {
            CanaryPassedWrongly++;
            Console.WriteLine("FAIL canary did not fail (harness cannot fail): " + name);
        }
    }

    public static int Finish()
    {
        var checks = Pass + Fail;
        var canaryOk = CanaryFailed >= 1 && CanaryPassedWrongly == 0;
        if (Fail == 0 && canaryOk)
        {
            Console.WriteLine($"OK ({checks} checks, canary failed as expected)");
            return 0;
        }

        Console.WriteLine(canaryOk
            ? $"FAILED ({Fail} of {checks} checks failed)"
            : $"FAILED (canary did not fail as expected; {Fail} of {checks} checks failed)");
        return 1;
    }
}
