// RotationHarness MNK (2026-10-04): offline harness for testing real Monk rotation decisions.
//
// WHAT IS REAL HERE: the Monk job source itself. Mnk.cs and MNK_Helper.cs are compiled into this
// console app UNCHANGED (see the csproj Compile Include links): every Invoke branch, the PB/weave/
// blitz helpers, the gauge properties and the action/buff tables are the exact shipping code.
// Everything the decisions READ (cooldowns, statuses, gauge bytes, target, presets, settings) comes
// from the harness fakes instead of the game.
//
// THE MNK-1 CASE: with Raptor form active, 3 enemies in range, Four-Point Fury learned, and the
// weave block closed, MNK_AoE_SimpleMode.Invoke(ArmOfTheDestroyer) must return Twin Snakes - the
// Balance guide's rule is "Four-Point Fury is only a gain on 4 targets" (MNK_Helper.cs DoBasicCombo
// onAoE branch, Raptor form). The paired case pins the behaviour that must NOT change: at 4 enemies
// in range the combo still returns Four-Point Fury. This case was proven red on unchanged source.
//
// THE CANARY: the same state run through an assertion of the OPPOSITE behaviour. It is EXPECTED TO
// FAIL; the harness only exits 0 when the canary fails as expected (proof the test can actually fail).
//
//   dotnet build tests\GluttonyCombo.RotationHarness.MNK -c Release
//   dotnet tests\GluttonyCombo.RotationHarness.MNK\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.Mnk.dll

using Dalamud.Game.ClientState.JobGauge.Types;
// Alias: inside namespace GluttonyCombo.RotationHarness.MNK the simple name `MNK` would bind to this
// harness namespace, not the job class — so every job reference goes through the alias.
using Mnk = GluttonyCombo.Combos.PvE.MNK;

namespace GluttonyCombo.RotationHarness.MNK;

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
    // references Dalamud types (MNKGauge), so the load must happen only after the hook is in place.
    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo rotation harness: MNK_AoE_SimpleMode, offline --");

        // Evidence: the gauge fake is a REAL MNKGauge over harness memory; show the probed layout.
        var gauge = FakeGauges.Get<MNKGauge>();
        Console.WriteLine($"gauge probe: MNKGauge over harness memory, byte offsets = " +
                          string.Join(", ", FakeGauges.Offsets.Where(kv => kv.Key.Type == typeof(MNKGauge))
                              .Select(kv => $"{kv.Key.Prop}=@{kv.Value}")));
        Console.WriteLine($"MNKGauge.Chakra reads {gauge.Chakra} (fresh, expected 0)");

        // ---- MNK-1: the behaviour the ranked row wants (proven red on unchanged source) ----
        SetAoEBasicComboState(3);
        Console.WriteLine($"case state: RaptorForm status on, enemies in range={FakeGame.EnemiesInRange(Mnk.ArmOfTheDestroyer)}, " +
                          $"FourPointFury learned={!FakeGame.NotLearned.Contains(Mnk.FourPointFury)}, " +
                          $"CanWeave={FakeGame.CanWeave}, InCombat={FakeGame.InCombat}, " +
                          $"statuses={FakeGame.Statuses.Count}");

        var combo = new Mnk.MNK_AoE_SimpleMode();
        uint got = combo.RunInvoke(Mnk.ArmOfTheDestroyer);
        Check($"MNK-1: AoE basic combo, Raptor form, 3 enemies in range, FPF learned: " +
              $"Invoke(ArmOfTheDestroyer) returns Twin Snakes ({Mnk.TwinSnakes}) - Four-Point Fury is only a gain on 4 targets",
            got == Mnk.TwinSnakes, $"returned {got}");

        // ---- MNK-1 paired case: the behaviour that must NOT change (4 enemies keep Four-Point Fury) ----
        SetAoEBasicComboState(4);
        uint got4 = new Mnk.MNK_AoE_SimpleMode().RunInvoke(Mnk.ArmOfTheDestroyer);
        Check($"MNK-1 (unchanged): AoE basic combo, Raptor form, 4 enemies in range, FPF learned: " +
              $"Invoke(ArmOfTheDestroyer) still returns Four-Point Fury ({Mnk.FourPointFury})",
            got4 == Mnk.FourPointFury, $"returned {got4}");

        // ---- the CANARY: deliberately asserts the opposite of the paired case; must FAIL ----
        SetAoEBasicComboState(4);
        uint got2 = new Mnk.MNK_AoE_SimpleMode().RunInvoke(Mnk.ArmOfTheDestroyer);
        CheckCanary($"CANARY (expected to FAIL): identical 4-enemy state, asserting Invoke(ArmOfTheDestroyer) " +
                    $"does NOT return Four-Point Fury", got2 != Mnk.FourPointFury, $"returned {got2}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The AoE basic-combo decision state: Raptor form active, the given number of enemies in range
    ///     of every melee AoE action, everything learned, weave block closed (defaults cover the rest:
    ///     in combat, no statuses but the form, identity hooks, one-button rotation enabled).
    /// </summary>
    private static void SetAoEBasicComboState(int enemiesInRange)
    {
        FakeGame.Reset();
        FakeGame.Statuses.Add(new FakeStatus(Mnk.Buffs.RaptorForm, 10f));
        FakeGame.EnemiesInRangeCount = enemiesInRange;
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
    // the Dalamud dev folder the way the plugin build does (same pattern as the VPR spike / HookTeardownHarness).
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
