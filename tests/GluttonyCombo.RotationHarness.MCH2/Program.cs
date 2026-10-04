// GluttonyCombo.RotationHarness.MCH2 (2026-10-04, SC11 round 7): offline harness for the Machinist
// Queen decision (Improvements Ranked MCH-2), cloned from the round-6 MCH harness instance of the
// offline rotation-harness pattern (notebook "Offline Harness Approach", section 9). A separate
// project per job is deliberate; this MCH2 copy beside the round-6 MCH harness is deliberate too.
//
// WHAT IS REAL HERE: the Machinist job source itself. MCH.cs and MCH_Helper.cs are compiled into this
// console app UNCHANGED (see the csproj Compile Include links): every Invoke branch, the
// hypercharge/wildfire/queen/reassemble/gauss helpers, the gauge properties and the action/buff tables
// are the exact shipping code. Everything the decisions READ (cooldowns, statuses, gauge bytes, target,
// presets, settings, enemy counts) comes from the harness fakes instead of the game.
//
// THE DECISION (MCH-2, the behaviour the Improvements Ranked row wants - Icy Veins guide, patch 7.5:
// "the most effective practice is to summon queen between burst windows at 50 and 60 gauge while
// keeping a queen of 100 battery for each 2 minute burst window"): with the ST Advanced mode and its
// Turret/Queen child enabled (Joey's live preset set, opener off), weave open, boss target, no robot
// active, Wildfire 70 s from ready (between 2-minute bursts) and Battery 55 (inside the 50-60 band),
// MCH_ST_AdvancedMode.Invoke(SplitShot) must return the Automaton Queen.
//
// THIS COMMIT characterizes TODAY'S behaviour on the UNCHANGED source: the same state keeps returning
// the basic combo (SplitShot), not the Queen - there is no between-bursts rule in ShouldUseQueenST
// yet. The MCH-2 commit flips this case to the wanted behaviour (failing test first, then the
// ShouldUseQueenST change makes it pass).
//
// THE CANARY: the same state asserting the behaviour that is NOT current (Queen at Battery 55 between
// bursts). It is EXPECTED TO FAIL; the harness only exits 0 when the canary fails as expected (proof
// the case can actually fail).
//
//   dotnet build tests\GluttonyCombo.RotationHarness.MCH2 -c Release
//   dotnet tests\GluttonyCombo.RotationHarness.MCH2\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.MCH2.dll

using Dalamud.Game.ClientState.JobGauge.Types;
using GluttonyCombo.Combos;
using MchJob = GluttonyCombo.Combos.PvE.MCH;

namespace GluttonyCombo.RotationHarness.MCH2;

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
    // references Dalamud types (MCHGauge), so the load must happen only after the hook is in place.
    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo rotation harness MCH2: MCH_ST_AdvancedMode Queen decision, offline --");

        // Evidence: the gauge fake is a REAL MCHGauge over harness memory; show the probed layout.
        var gauge = FakeGauges.Get<MCHGauge>();
        Console.WriteLine("gauge probe: MCHGauge over harness memory, byte offsets = " +
                          string.Join(", ", FakeGauges.Offsets.Where(kv => kv.Key.Type == typeof(MCHGauge))
                              .Select(kv => $"{kv.Key.Prop}=@{kv.Value}")));
        Console.WriteLine($"MCHGauge.Battery reads {gauge.Battery} (fresh, expected 0)");

        // ---- characterization: today's behaviour on the UNCHANGED source ----
        SetQueenState(battery: 55, wildfireRemaining: 70f);
        Console.WriteLine($"case state: Battery={FakeGauges.Get<MCHGauge>().Battery}, " +
                          $"IsRobotActive={FakeGauges.Get<MCHGauge>().IsRobotActive}, CanWeave={FakeGame.CanWeave}, " +
                          $"TargetIsBoss={FakeGame.TargetIsBoss}, Wildfire remaining=" +
                          $"{FakeGame.Cooldown(MchJob.Wildfire).CooldownRemaining}s, Hypercharge remaining=" +
                          $"{FakeGame.Cooldown(MchJob.Hypercharge).CooldownRemaining}s, Queen preset=" +
                          $"{FakeGame.EnabledPresets.Contains(Preset.MCH_ST_Adv_TurretQueen)}");
        uint got = new MchJob.MCH_ST_AdvancedMode().RunInvoke(MchJob.SplitShot);
        Check("characterization: Battery 55 + Wildfire 70s away + boss target + weave open: " +
              $"Invoke(SplitShot) returns the basic combo ({MchJob.SplitShot}), NOT the Queen ({MchJob.AutomatonQueen})",
            got == MchJob.SplitShot, $"returned {got}");

        // ---- the CANARY: asserts the behaviour that is NOT current; must FAIL ----
        SetQueenState(battery: 55, wildfireRemaining: 70f);
        uint got2 = new MchJob.MCH_ST_AdvancedMode().RunInvoke(MchJob.SplitShot);
        CheckCanary("CANARY (expected to FAIL): identical state, asserting Invoke(SplitShot) already " +
                    $"returns the Queen between bursts at Battery 55",
            got2 == MchJob.AutomatonQueen, $"returned {got2}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The MCH-2 state: a level-100 Machinist mid-fight, between 2-minute burst windows (Wildfire
    ///     and Hypercharge spent at the last burst and still recharging), weave open, boss target, no
    ///     robot active, Joey's live ST Advanced preset set (round-7 verdict page, section (a):
    ///     children on, opener off). Only the two knobs the decision reads vary per case.
    /// </summary>
    private static void SetQueenState(byte battery, float wildfireRemaining)
    {
        FakeGame.Reset();

        // Joey's preset set for ST Advanced mode; opener 8101 off (config read, verdict section (a))
        FakeGame.EnabledPresets =
        [
            Preset.MCH_ST_AdvancedMode,
            Preset.MCH_ST_Adv_GaussRicochet,
            Preset.MCH_ST_Adv_WildFire,
            Preset.MCH_ST_Adv_Hypercharge,
            Preset.MCH_ST_Adv_Reassemble,
            Preset.MCH_ST_Adv_Stabilizer,
            Preset.MCH_ST_Adv_TurretQueen,
            Preset.MCH_ST_Adv_QueenInHypercharge,
            Preset.MCH_ST_Adv_Tools,
            Preset.MCH_ST_Adv_Heatblast,
        ];

        FakeGame.CanWeave = true;          // the Queen is decided inside the oGCD weave block
        FakeGame.TargetIsBoss = true;      // UseQueen's ST path consults ShouldUseQueenST on bosses
        FakeGame.RoleActionReady = false;  // SecondWind / HeadGraze decline

        // the gauge state under test
        FakeGauges.SetByte<MCHGauge>("Battery", battery);

        // between 2-minute bursts: Wildfire/Hypercharge/Reassemble/Barrel Stabilizer spent at the last
        // burst, tools mid-cycle; Gauss/Ricochet stay at fake defaults (1 charge, no overcap, no weave)
        void OnCd(uint action, float remaining, uint charges = 0)
        {
            var cd = FakeGame.Cooldown(action);
            cd.IsCooldown = true;
            cd.CooldownRemaining = remaining;
            cd.RemainingCharges = charges;
        }

        OnCd(MchJob.Wildfire, wildfireRemaining);   // 70s: the burst is far away
        OnCd(MchJob.Hypercharge, 40f);              // spent at the last burst window
        OnCd(MchJob.Reassemble, 30f);
        OnCd(MchJob.BarrelStabilizer, 60f);
        OnCd(MchJob.Drill, 15f);
        OnCd(MchJob.Chainsaw, 15f);
        OnCd(MchJob.Excavator, 15f);
        OnCd(MchJob.AirAnchor, 15f);

        // at level 100 the game hooks Rook Autoturret to the Automaton Queen
        FakeGame.HookOverrides[MchJob.RookAutoturret] = MchJob.AutomatonQueen;
    }

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
            Console.WriteLine($"FAIL {desc} - canary PASSED UNEXPECTEDLY, the harness is not actually " +
                              $"exercising this decision [{detail}]");
        }
        else
        {
            _pass++;
            Console.WriteLine($"FAIL {desc} - canary failed as expected [{detail}]");
        }
    }

    // The packaging keep-lists leave Dalamud's own assemblies out of the output folder; resolve them from
    // the Dalamud dev folder the way the plugin build does (same pattern as GluttonyCombo.HookTeardownHarness).
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
