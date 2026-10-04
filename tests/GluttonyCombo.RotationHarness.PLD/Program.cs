// Program — the offline harness for the Paladin decisions. Cases drive PLD_ST_SimpleMode.Invoke with
// a weave-blocked fake state (mits/oGCD blocks bail at their first weave check) so the GCD decision
// chain is what gets exercised.
//
//   dotnet build tests\GluttonyCombo.RotationHarness.PLD -c Release
//   dotnet tests\GluttonyCombo.RotationHarness.PLD\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.PLD.dll
//
// A clearly-marked CANARY case is expected to fail; the run prints OK and exits 0 only when every real
// case passes AND the canary fails. The job source under src/ is NEVER touched.

using System.Reflection;

namespace GluttonyCombo.RotationHarnessPLD;

internal static class Program
{
    private static int Main()
    {
        var pass = 0;
        var fail = 0;
        var canary = 0;
        try
        {
            AddResolver();
            Run(ref pass, ref fail, ref canary);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HARNESS CRASH: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 2;
        }

        var ok = fail == 0 && canary == 1;
        var verdict = ok ? $"OK ({pass} checks, canary failed as expected)" : "FAILED";
        Console.WriteLine($"--- {verdict}");
        return ok ? 0 : 1;
    }

    private static void Run(ref int pass, ref int fail, ref int canary)
    {
        // ---- gauge probe (evidence that a REAL Dalamud PLDGauge sits over harness-owned memory) ----
        var gauge = FakeGauges.Get<Dalamud.Game.ClientState.JobGauge.Types.PLDGauge>();
        var offsets = FakeGauges.Offsets
            .Where(kv => kv.Key.Type == typeof(Dalamud.Game.ClientState.JobGauge.Types.PLDGauge))
            .Select(kv => $"{kv.Key.Prop}={kv.Value}")
            .Order()
            .ToList();
        Console.WriteLine($"gauge probe PLDGauge: {string.Join(", ", offsets)} (oath={gauge.OathGauge})");
        FakeGauges.SetByte<Dalamud.Game.ClientState.JobGauge.Types.PLDGauge>(nameof(Dalamud.Game.ClientState.JobGauge.Types.PLDGauge.OathGauge), 100);
        Console.WriteLine($"gauge probe PLDGauge: OathGauge={gauge.OathGauge} after write (expect 100)");
        FakeGauges.ZeroAll();

        // ---- the case state: FoF burn window with Divine Might, no Atonement procs ----
        SetFoFBurnState(withSepulchre: false);
        var burn = new Combos.PvE.PLD.PLD_ST_SimpleMode().RunInvoke(Combos.PvE.PLD.FastBlade);

        // PLD-1 (pair, unchanged behaviour): with FoF + Divine Might and no proc to spend, the
        // one-button spends Divine Might on Holy Spirit inside the window. Must hold before AND
        // after the PLD-1 change.
        Report(
            "PLD-1 (pair): Fight or Flight + Divine Might, no Atonement procs -> Invoke(FastBlade) returns Holy Spirit",
            burn == Combos.PvE.PLD.HolySpirit,
            $"got {ActionName(burn)}",
            ref pass, ref fail);

        // CANARY — asserts the opposite of the pair case and must therefore FAIL. It stays
        // with the harness forever: if it ever passes, the harness no longer sees the real code.
        Canary(
            "CANARY (expected to fail): same state but expecting NOT Holy Spirit",
            burn != Combos.PvE.PLD.HolySpirit,
            $"got {ActionName(burn)}",
            ref canary, ref fail);

        // ---- PLD-1: the Sepulchre proc must outrank Holy Spirit inside Fight or Flight ----
        SetFoFBurnState(withSepulchre: true);
        var proc = new Combos.PvE.PLD.PLD_ST_SimpleMode().RunInvoke(Combos.PvE.PLD.FastBlade);
        Report(
            "PLD-1: Fight or Flight + Divine Might + SepulchreReady (Atonement hooked to Sepulchre) -> Invoke(FastBlade) returns Sepulchre, not Holy Spirit",
            proc == Combos.PvE.PLD.Sepulchre,
            $"got {ActionName(proc)}",
            ref pass, ref fail);
    }

    /// <summary>The FoF burn-window states for the Sepulchre decision (PLD_Helper.cs:700-744).</summary>
    private static void SetFoFBurnState(bool withSepulchre)
    {
        FakeGame.Reset(); // level-100 PLD, in combat, weave-blocked, in melee on a living target, full MP

        FakeGame.Statuses.Add(new FakeStatus(Combos.PvE.PLD.Buffs.FightOrFlight, 15f));  // FoF window live
        FakeGame.Statuses.Add(new FakeStatus(Combos.PvE.PLD.Buffs.DivineMight, 30f));    // well above the 6s expiring clause

        if (!withSepulchre) return;

        FakeGame.Statuses.Add(new FakeStatus(Combos.PvE.PLD.Buffs.SepulchreReady, 20f)); // the proc to spend
        FakeGame.HookOverrides[Combos.PvE.PLD.Atonement] = Combos.PvE.PLD.Sepulchre;     // the game's proc upgrade
    }

    private static void Report(string name, bool condition, string detail, ref int pass, ref int fail)
    {
        Console.WriteLine($"[{(condition ? "PASS" : "FAIL")}] {name} — {detail}");
        if (condition) pass++;
        else fail++;
    }

    private static void Canary(string name, bool condition, string detail, ref int canaryCount, ref int fail)
    {
        Console.WriteLine($"[{(condition ? "PASS" : "FAIL")}] {name} — {detail}");
        if (condition) fail++;        // the canary PASSED: the harness no longer sees the real code
        else canaryCount++;           // failed, exactly as expected
    }

    private static readonly (uint Id, string Name)[] KnownActions =
    [
        (Combos.PvE.PLD.FastBlade, nameof(Combos.PvE.PLD.FastBlade)),
        (Combos.PvE.PLD.HolySpirit, nameof(Combos.PvE.PLD.HolySpirit)),
        (Combos.PvE.PLD.Atonement, nameof(Combos.PvE.PLD.Atonement)),
        (Combos.PvE.PLD.Sepulchre, nameof(Combos.PvE.PLD.Sepulchre)),
    ];

    private static string ActionName(uint id) =>
        KnownActions.FirstOrDefault(k => k.Id == id) is { } k ? k.Name : id.ToString();

    // .NET resolves Dalamud.dll (dotnet-reference style: Private=False, not copied) at load time.
    private static void AddResolver()
    {
        var dev = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncher", "addon", "Hooks", "dev");
        if (!Directory.Exists(dev)) return;

        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var name = new AssemblyName(args.Name).Name + ".dll";
            var path = Path.Combine(dev, name);
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
    }
}
