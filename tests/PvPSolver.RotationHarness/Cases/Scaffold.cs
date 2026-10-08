using System.Text.RegularExpressions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>Checks of the harness machinery itself (the scanner finds what it must, and can fail).</summary>
internal static class Scaffold
{
    public static void Run()
    {
        Console.WriteLine("-- scaffold --");

        const string sample = @"
class A {
    // protected override bool Fake(out int x) { base.Other(out x); }
    protected override bool Foo(out int x)
    {
        var s = ""} base.Bar( {"";
        /* base.Baz( */
        if (true) { x = 1; return base.Foo(out x); }
        return base.Qux(out x);
    }
}";
        var head = new Regex(@"\boverride\s+bool\s+(?<name>\w+)\s*\(");
        var ms = CsSource.Methods(CsSource.Sanitize(sample), "sample.cs", head);
        Harness.Case("scanner: finds exactly the real override, not the commented one", ms.Count == 1 && ms[0].Name == "Foo",
            string.Join(",", ms.Select(m => m.Name)));
        var calls = ms.Count == 1
            ? Regex.Matches(ms[0].Body, @"\bbase\s*\.\s*(\w+)\s*\(").Select(m => m.Groups[1].Value).ToArray()
            : [];
        Harness.Case("scanner: sees base calls in code, ignores strings and comments", calls.SequenceEqual(["Foo", "Qux"]),
            string.Join(",", calls));

        // Canary: false by construction; proves a failing assertion is reported as a failure.
        Harness.Canary("scanner: a wrong base call must be reported as clean (it is not)", calls.Length == 0);
    }
}
