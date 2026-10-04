// GluttonyCombo.RotationHarness.MCH (2026-10-04, SC11 round 6): offline harness for a real Machinist
// rotation decision, cloned from the rot/harness-spike VPR proof (notebook "Offline Harness Approach"
// section 9). A separate project per job is deliberate.
//
// WHAT IS REAL HERE: the Machinist job source itself. MCH.cs and MCH_Helper.cs are compiled into this
// console app UNCHANGED (see the csproj Compile Include links): every Invoke branch, the
// hypercharge/wildfire/queen/reassemble/gauss helpers, the gauge properties and the action/buff tables
// are the exact shipping code. Everything the decisions READ (cooldowns, statuses, gauge bytes, target,
// presets, settings, enemy counts) comes from the harness fakes instead of the game.
//
// THE CASE (MCH-1, the behaviour the Improvements Ranked row wants — The Balance guide and the Icy Veins
// 7.5 changelog both state 6): with the AoE Advanced mode and its Gauss/Ricochet option enabled, the
// player Overheated (Hypercharge window open), weave-blocked, CheckMate + BlazingShot learned and FIVE
// enemies inside Auto Crossbow range, MCH_AoE_AdvancedMode.Invoke(SpreadShot) must return BlazingShot —
// MCH_Helper.cs OverheatGCD: NumberOfEnemiesInRange(AutoCrossbow) >= 6 after the change. The paired case
// pins the behaviour that must NOT change: SIX enemies still returns Auto Crossbow.
//
// THE CANARY: the MCH-1 state run through an assertion of the PRE-change behaviour (Auto Crossbow at
// five enemies). It is EXPECTED TO FAIL; the harness only exits 0 when the canary fails as expected
// (proof the case can actually fail).
//
//   dotnet build tests\GluttonyCombo.RotationHarness.MCH -c Release
//   dotnet tests\GluttonyCombo.RotationHarness.MCH\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.MCH.dll

using Dalamud.Game.ClientState.JobGauge.Types;
using GluttonyCombo.Combos;
// the harness namespace ends in .MCH, which shadows the job class name inside this file; alias it
using MchJob = GluttonyCombo.Combos.PvE.MCH;

namespace GluttonyCombo.RotationHarness.MCH;

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
        Console.WriteLine("-- GluttonyCombo rotation harness: MCH_AoE_AdvancedMode, offline --");

        // Evidence: the gauge fake is a REAL MCHGauge over harness memory; show the probed layout.
        var gauge = FakeGauges.Get<MCHGauge>();
        Console.WriteLine("gauge probe: MCHGauge over harness memory, byte offsets = " +
                          string.Join(", ", FakeGauges.Offsets.Where(kv => kv.Key.Type == typeof(MCHGauge))
                              .Select(kv => $"{kv.Key.Prop}=@{kv.Value}")));
        Console.WriteLine($"MCHGauge.IsOverheated reads {gauge.IsOverheated} (fresh, expected False)");

        // ---- MCH-1: the behaviour the row wants (Auto Crossbow only from 6 targets on) ----
        SetOverheatState(enemiesInAutoCrossbowRange: 5);
        Console.WriteLine($"case state: IsOverheated={FakeGauges.Get<MCHGauge>().IsOverheated}, " +
                          $"CanWeave={FakeGame.CanWeave}, GaussRicochet preset=" +
                          $"{FakeGame.EnabledPresets.Contains(Preset.MCH_AoE_Adv_GaussRicochet)}, " +
                          $"enemies in Auto Crossbow range={FakeGame.EnemiesInRange}, " +
                          $"Heatblast hook -> {FakeGame.HookOverrides[MchJob.Heatblast]} ({MchJob.BlazingShot}), " +
                          $"target HP={FakeGame.TargetHPPercent}%");

        uint got = new MchJob.MCH_AoE_AdvancedMode().RunInvoke(MchJob.SpreadShot);
        Check("MCH-1: AoE Advanced + Overheated + Gauss/Ricochet on + 5 enemies in Auto Crossbow range: " +
              $"Invoke(SpreadShot) returns BlazingShot ({MchJob.BlazingShot}), not Auto Crossbow",
            got == MchJob.BlazingShot, $"returned {got}");

        // ---- MCH-1 paired: the behaviour that must NOT change ----
        SetOverheatState(enemiesInAutoCrossbowRange: 6);
        Console.WriteLine($"paired case state: enemies in Auto Crossbow range={FakeGame.EnemiesInRange}");
        uint gotPaired = new MchJob.MCH_AoE_AdvancedMode().RunInvoke(MchJob.SpreadShot);
        Check("MCH-1 paired: identical state but SIX enemies in Auto Crossbow range: Invoke(SpreadShot) " +
              $"keeps returning Auto Crossbow ({MchJob.AutoCrossbow})",
            gotPaired == MchJob.AutoCrossbow, $"returned {gotPaired}");

        // ---- the CANARY: asserts the PRE-change behaviour; must FAIL ----
        SetOverheatState(enemiesInAutoCrossbowRange: 5);
        uint got2 = new MchJob.MCH_AoE_AdvancedMode().RunInvoke(MchJob.SpreadShot);
        CheckCanary("CANARY (expected to FAIL): identical state to MCH-1, asserting Invoke(SpreadShot) " +
                    $"still returns Auto Crossbow (the old threshold-5 behaviour)",
            got2 == MchJob.AutoCrossbow, $"returned {got2}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>The exact state both cases share: a level-100 Machinist, Overheated, weave-blocked.</summary>
    private static void SetOverheatState(int enemiesInAutoCrossbowRange)
    {
        FakeGame.Reset();

        // level-100 Machinist in combat, no weave available, living target (defaults cover the rest)
        FakeGauges.SetByte<MCHGauge>("IsOverheated", 1); // Hypercharge window open

        // the enabled preset pair for this mode (Improvements Ranked MCH-1 row: "enabled: yes")
        FakeGame.EnabledPresets =
        [
            Preset.MCH_AoE_AdvancedMode,
            Preset.MCH_AoE_Adv_GaussRicochet,
        ];

        // the decision under test: how many enemies stand inside Auto Crossbow's range
        FakeGame.EnemiesInRange = enemiesInAutoCrossbowRange;

        // at level 100 the game hooks Heatblast to BlazingShot; model the live hook table
        FakeGame.HookOverrides[MchJob.Heatblast] = MchJob.BlazingShot;
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
