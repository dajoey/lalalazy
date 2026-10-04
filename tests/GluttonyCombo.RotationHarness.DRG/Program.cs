// RotationHarness DRG (2026-10-04, SC11 round 6): offline harness for real Dragoon Invoke decisions.
//
// WHAT IS REAL HERE: the Dragoon job source itself. DRG.cs and DRG_Helper.cs are compiled into this
// console app UNCHANGED (see the csproj Compile Include links): every Invoke branch, the Life Surge
// placement gate, the buff/dive holds, the gauge reads and the action/buff tables are the exact shipping
// code. Everything the decisions READ (cooldowns, statuses, gauge bytes, target, presets, settings) comes
// from the harness fakes instead of the game.
//
// THE CASE (characterization, current behaviour): in Life of the Dragon with Lance Charge up, Power Surge
// up, Life Surge ready and not yet active, right after Fang and Claw (whose next GCD is Drakesbane),
// DRG_ST_AdvancedMode.Invoke returns Life Surge — the buff lands on Drakesbane, matching The Balance's
// "use it on Heavens' Thrust or Drakesbane" rule (DRG_Helper.cs UseLifeSurge, the JustUsed(FangAndClaw)
// term).
//
// THE DRG-1 CASE: identical state except the just-used GCD is Heavens' Thrust: Life Surge must NOT be
// offered there (it would buff Fang and Claw / Wheeling Thrust, not Heavens' Thrust or Drakesbane).
//
// THE CANARY: the same state run through an assertion of the OPPOSITE behaviour. It is EXPECTED TO FAIL;
// the harness only exits 0 when the canary fails as expected (proof the test can actually fail).
//
//   dotnet build tests\GluttonyCombo.RotationHarness.DRG -c Release
//   dotnet tests\GluttonyCombo.RotationHarness.DRG\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.DRG.dll

using Dalamud.Game.ClientState.JobGauge.Types;
using GluttonyCombo.Combos;
using GluttonyCombo.Combos.PvE;

namespace GluttonyCombo.RotationHarness;

internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        AddDalamudResolver();
        return Run();
    }

    // Kept out of Main: Main is JIT-compiled before the resolver below can be registered, and its body
    // references Dalamud types (DRGGauge), so the load must happen only after the hook is in place.
    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo rotation harness: DRG_ST_AdvancedMode, Life Surge placement, offline --");

        // Evidence: the gauge fake is a REAL DRGGauge over harness memory; show the probed layout.
        var gauge = FakeGauges.Get<DRGGauge>();
        Console.WriteLine("gauge probe: DRGGauge over harness memory, byte offsets = " +
                          string.Join(", ", FakeGauges.Offsets.Where(kv => kv.Key.Type == typeof(DRGGauge))
                              .Select(kv => $"{kv.Key.Prop}=@{kv.Value}")));
        FakeGauges.SetByteExact<DRGGauge>("IsLOTDActive", 2); // IsLOTDActive == (LotdState == 2), per Dalamud.dll IL
        Console.WriteLine($"DRGGauge.IsLOTDActive reads {gauge.IsLOTDActive} after SetByteExact 2 (expected True)");
        FakeGauges.ZeroAll();

        // ---- the CHARACTERIZATION case: real behaviour on the UNCHANGED source ----
        SetLifeSurgeState(DRG.FangAndClaw);
        Console.WriteLine(CaseStateLine(DRG.FangAndClaw));
        uint got = new DRG.DRG_ST_AdvancedMode().RunInvoke(DRG.TrueThrust);
        Check($"DRG-1 (unchanged pair): LotD active + Lance Charge up + just used Fang and Claw (next GCD is " +
              $"Drakesbane): Invoke(TrueThrust) returns Life Surge ({DRG.LifeSurge}) onto Drakesbane",
            got == DRG.LifeSurge, $"returned {got}");

        // ---- the DRG-1 case: the behaviour the improvement row wants ----
        // The Balance D1: "we tend to use it on either Heavens' Thrust or Drakesbane". Right AFTER
        // Heavens' Thrust the buff would land on Fang and Claw / Wheeling Thrust instead, so Invoke
        // must NOT offer Life Surge in this state.
        SetLifeSurgeState(DRG.HeavensThrust);
        Console.WriteLine(CaseStateLine(DRG.HeavensThrust));
        uint gotDrg1 = new DRG.DRG_ST_AdvancedMode().RunInvoke(DRG.TrueThrust);
        Check($"DRG-1: LotD active + Lance Charge up + just used Heavens' Thrust (the buffed GCD already " +
              $"fired; the next combo GCD is not Drakesbane): Invoke(TrueThrust) must NOT return Life Surge " +
              $"({DRG.LifeSurge}); Life Surge is only for Heavens' Thrust or Drakesbane (The Balance, D1)",
            gotDrg1 != DRG.LifeSurge, $"returned {gotDrg1}");

        // ---- the CANARY: same state, opposite assertion; must FAIL ----
        SetLifeSurgeState(DRG.FangAndClaw);
        uint got2 = new DRG.DRG_ST_AdvancedMode().RunInvoke(DRG.TrueThrust);
        CheckCanary($"CANARY (expected to FAIL): identical state, asserting Invoke(TrueThrust) does NOT " +
                    $"return Life Surge", got2 != DRG.LifeSurge, $"returned {got2}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The exact Life Surge placement state the DRG-1 row specifies: real DRGGauge with Life of the
    ///     Dragon active, Lance Charge up, Power Surge up (the oGCD gate), Life Surge ready and not active,
    ///     weave window open with nothing weaved, and one just-used GCD set by the caller.
    /// </summary>
    private static void SetLifeSurgeState(uint justUsedAction)
    {
        FakeGame.Reset();

        // weave window open, nothing weaved yet; the oGCD gate is Power Surge (CanWeaveOgcds)
        FakeGame.CanWeave = true;
        FakeGame.Statuses.Add(new FakeStatus(DRG.Buffs.PowerSurge, 30f));

        // Life of the Dragon active on the REAL gauge (LotdState byte set to 2, the exact equality value
        // Dalamud's IL tests); Lance Charge status up; Full Thrust hooked to Heavens' Thrust the way the
        // game does inside LotD at 96+ (so OriginalHook(FullThrust) is 25771)
        FakeGauges.SetByteExact<DRGGauge>("IsLOTDActive", 2);
        FakeGame.Statuses.Add(new FakeStatus(DRG.Buffs.LanceCharge, 15f));
        FakeGame.HookOverrides[DRG.FullThrust] = DRG.HeavensThrust;

        // Life Surge ready (default cooldown state) and not already active (status 116 not present)

        // the GCD that fired immediately before this weave window
        FakeGame.JustUsedActions[justUsedAction] = (0.5f, 1.5f);

        // Advanced-Mode internal gates: the Buffs group with Life Surge enabled; every other sub-feature
        // (Litany/Lance Charge weaves, damage oGCDs, opener, heals, holds) stays off so the case isolates
        // the Life Surge decision
        FakeGame.EnabledPresets.Add(Preset.DRG_ST_AdvancedMode);
        FakeGame.EnabledPresets.Add(Preset.DRG_ST_Buffs);
        FakeGame.EnabledPresets.Add(Preset.DRG_ST_LifeSurge);
    }

    private static string CaseStateLine(uint justUsed) =>
        $"case state: justUsed={ActionName(justUsed)} ({justUsed}), IsLOTDActive=" +
        $"{FakeGauges.Get<DRGGauge>().IsLOTDActive}, CanWeave={FakeGame.CanWeave}, statuses=[" +
        $"{string.Join(",", FakeGame.Statuses.Select(s => s.Id))}], LifeSurge ready=" +
        $"{!FakeGame.Cooldown(DRG.LifeSurge).IsCooldown}, OriginalHook(FullThrust)=" +
        $"{FakeGame.HookOverrides.GetValueOrDefault(DRG.FullThrust, DRG.FullThrust)}";

    private static string ActionName(uint id) => id switch
    {
        DRG.FangAndClaw => "FangAndClaw",
        DRG.WheelingThrust => "WheelingThrust",
        DRG.HeavensThrust => "HeavensThrust",
        DRG.VorpalThrust => "VorpalThrust",
        DRG.LanceBarrage => "LanceBarrage",
        DRG.FullThrust => "FullThrust",
        DRG.TrueThrust => "TrueThrust",
        DRG.LifeSurge => "LifeSurge",
        _ => id.ToString(),
    };

    private static void Check(string desc, bool ok, string detail = "")
    {
        if (ok)
        {
            _pass++;
            Console.WriteLine($"PASS {desc}");
        }
        else
        {
            _fail++;
            Console.WriteLine($"FAIL {desc} [{detail}]");
        }
    }

    /// <summary>A canary asserts the OPPOSITE of the real behaviour and is required to fail.</summary>
    private static void CheckCanary(string desc, bool oppositeAssertion, string detail = "")
    {
        if (oppositeAssertion)
        {
            // the canary's wrong assertion unexpectedly held: the harness cannot detect regressions
            _fail++;
            Console.WriteLine($"FAIL {desc} — canary PASSED UNEXPECTEDLY, the harness is not actually " +
                              $"exercising this decision [{detail}]");
        }
        else
        {
            _pass++;
            Console.WriteLine($"FAIL {desc} — canary failed as expected [{detail}]");
        }
    }

    // The packaging keep-lists leave Dalamud's own assemblies out of the output folder; resolve them from
    // the Dalamud dev folder the way the plugin build does (same pattern as GluttonyCombo.HookTeardownHarness
    // and the rot/harness-spike VPR harness).
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
}
