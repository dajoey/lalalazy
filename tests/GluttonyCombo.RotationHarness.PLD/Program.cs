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

        // ---- PLD-2: outside Fight or Flight the chain is held, not spent ----
        // Held mid-chain (ComboAction RiotBlade), an unexpiring Atonement proc, no FoF: the one-button
        // must continue the combo (Royal Authority), not spend the chain.
        SetHoldState(Combos.PvE.PLD.Buffs.AtonementReady);
        var hold = new Combos.PvE.PLD.PLD_ST_SimpleMode().RunInvoke(Combos.PvE.PLD.FastBlade);
        Report(
            "PLD-2: no FoF, mid-chain (RiotBlade), AtonementReady held (not expiring) -> Invoke(FastBlade) returns the combo continuation (Royal Authority), not the chain",
            hold == Combos.PvE.PLD.RoyalAuthority,
            $"got {ActionName(hold)}",
            ref pass, ref fail);

        // Same hold for the middle link: a held Supplication proc must not be spent mid-chain either.
        SetHoldState(Combos.PvE.PLD.Buffs.SupplicationReady);
        var holdSupp = new Combos.PvE.PLD.PLD_ST_SimpleMode().RunInvoke(Combos.PvE.PLD.FastBlade);
        Report(
            "PLD-2: no FoF, mid-chain (RiotBlade), SupplicationReady held (not expiring) -> Invoke(FastBlade) returns the combo continuation (Royal Authority), not the chain",
            holdSupp == Combos.PvE.PLD.RoyalAuthority,
            $"got {ActionName(holdSupp)}",
            ref pass, ref fail);

        // Negative (a): inside FoF the chain still burns at once (the kept FoF clause).
        SetHoldState(Combos.PvE.PLD.Buffs.AtonementReady, inFoF: true);
        var burnInFof = new Combos.PvE.PLD.PLD_ST_SimpleMode().RunInvoke(Combos.PvE.PLD.FastBlade);
        Report(
            "PLD-2 negative: FoF + mid-chain (RiotBlade) + AtonementReady held -> Invoke(FastBlade) still burns the chain (Atonement) at once",
            burnInFof == Combos.PvE.PLD.Atonement,
            $"got {ActionName(burnInFof)}",
            ref pass, ref fail);

        // Negative (b): a stack about to expire (< 6 s) is still burned outside FoF (the kept expiring guard).
        SetHoldState(Combos.PvE.PLD.Buffs.AtonementReady, expiring: true, midChain: false);
        var burnExpiring = new Combos.PvE.PLD.PLD_ST_SimpleMode().RunInvoke(Combos.PvE.PLD.FastBlade);
        Report(
            "PLD-2 negative: no FoF, not mid-chain, AtonementReady expiring (5 s) -> Invoke(FastBlade) still burns it (Atonement)",
            burnExpiring == Combos.PvE.PLD.Atonement,
            $"got {ActionName(burnExpiring)}",
            ref pass, ref fail);

        // Negative (c): the Divine Might mid-combo clause is untouched and keeps its place above the hold.
        SetHoldState(Combos.PvE.PLD.Buffs.AtonementReady, withDivineMight: true);
        var dmMidCombo = new Combos.PvE.PLD.PLD_ST_SimpleMode().RunInvoke(Combos.PvE.PLD.FastBlade);
        Report(
            "PLD-2 negative: no FoF, Divine Might + mid-chain (RiotBlade) + AtonementReady held -> Invoke(FastBlade) still returns Holy Spirit (clause order unchanged)",
            dmMidCombo == Combos.PvE.PLD.HolySpirit,
            $"got {ActionName(dmMidCombo)}",
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

    /// <summary>The PLD-2 hold states: outside FoF by default, one Atonement-family proc held
    /// (fresh 20 s, or 5 s = inside the 6 s expiring guard), optionally inside FoF and/or with
    /// Divine Might, mid-chain (ComboAction RiotBlade) or not. The game's level-100 action upgrade
    /// (Rage of Halone -> Royal Authority) and the Supplication proc upgrade are mirrored the same
    /// way SetFoFBurnState mirrors Atonement -> Sepulchre.</summary>
    private static void SetHoldState(uint procId, bool inFoF = false, bool expiring = false, bool midChain = true, bool withDivineMight = false)
    {
        FakeGame.Reset(); // level-100 PLD, in combat, weave-blocked, in melee on a living target, full MP

        FakeGame.ComboTimer = 30f;                                          // a live ST combo
        FakeGame.ComboActionId = midChain ? Combos.PvE.PLD.RiotBlade : Combos.PvE.PLD.FastBlade;
        FakeGame.HookOverrides[Combos.PvE.PLD.RageOfHalone] = Combos.PvE.PLD.RoyalAuthority;

        FakeGame.Statuses.Add(new FakeStatus(procId, expiring ? 5f : 20f)); // the held proc

        if (withDivineMight)
            FakeGame.Statuses.Add(new FakeStatus(Combos.PvE.PLD.Buffs.DivineMight, 30f)); // above the 6 s expiring clause

        if (inFoF)
            FakeGame.Statuses.Add(new FakeStatus(Combos.PvE.PLD.Buffs.FightOrFlight, 15f)); // FoF window live

        if (procId == Combos.PvE.PLD.Buffs.SupplicationReady)
            FakeGame.HookOverrides[Combos.PvE.PLD.Atonement] = Combos.PvE.PLD.Supplication; // the game's proc upgrade
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
        (Combos.PvE.PLD.RiotBlade, nameof(Combos.PvE.PLD.RiotBlade)),
        (Combos.PvE.PLD.RoyalAuthority, nameof(Combos.PvE.PLD.RoyalAuthority)),
        (Combos.PvE.PLD.Supplication, nameof(Combos.PvE.PLD.Supplication)),
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
