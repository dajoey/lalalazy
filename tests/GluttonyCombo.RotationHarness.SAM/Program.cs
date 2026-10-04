// RotationHarness.SAM (2026-10-04): offline harness for a real Samurai rotation decision.
//
// WHAT IS REAL HERE: the Samurai job source itself. SAM.cs and SAM_Helper.cs are compiled into this
// console app UNCHANGED (see the csproj Compile Include links): every Invoke branch, the Kenki/Sen/
// Senei-timing helpers, the gauge properties and the action/buff tables are the exact shipping code.
// Everything the decisions READ (cooldowns, statuses, gauge bytes, target, presets, settings,
// enemies in range) comes from the harness fakes instead of the game.
//
// THE CASES (SAM-1, the row from Improvements Ranked): a level-100 Samurai mid-fight, weave slot
// open, Meikyo Shisui and Ikishoten already spent, just past a Tendo Setsugekka (the real burst
// moment that opens the Senei window), Senei and Guren both ready and in range. The paired
// characterization case holds ONE enemy inside Guren's line and asserts today's single-target spend
// (Senei) — it passes on the UNCHANGED source and must keep passing after any change. The SAM-1 case
// puts exactly TWO enemies inside Guren's line and asserts the 120 s cooldown is spent on Guren
// (the cleave: 640 + 320 on two targets beats Senei's 800 on one) instead of Senei.
//
// THE CANARY: the one-enemy state run through an assertion of the OPPOSITE behaviour. It is EXPECTED
// TO FAIL; the harness only exits 0 when the canary fails as expected (proof the test can fail).
//
//   dotnet build tests\GluttonyCombo.RotationHarness.SAM -c Release
//   dotnet tests\GluttonyCombo.RotationHarness.SAM\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.SAM.dll

using Dalamud.Game.ClientState.JobGauge.Types;
using GluttonyCombo.Combos.PvE;
using Sam = GluttonyCombo.Combos.PvE.SAM;
using SamPreset = GluttonyCombo.Combos.Preset;

namespace GluttonyCombo.RotationHarness.SAM;

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
    // references Dalamud types (SAMGauge), so the load must happen only after the hook is in place.
    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo rotation harness: SAM_ST_AdvancedMode, offline --");

        // Evidence: the gauge fake is a REAL SAMGauge over harness memory; show the probed layout.
        var gauge = FakeGauges.Get<SAMGauge>();
        Console.WriteLine("gauge probe: SAMGauge over harness memory, byte offsets = " +
                          string.Join(", ", FakeGauges.Offsets.Where(kv => kv.Key.Type == typeof(SAMGauge))
                              .Select(kv => $"{kv.Key.Prop}=@{kv.Value}")));
        Console.WriteLine($"SAMGauge.Kenki reads {gauge.Kenki} (fresh, expected 0)");

        // ---- the paired unchanged-behaviour case (characterization): ONE enemy -> today's Senei ----
        SetSeneiWindowState(1);
        Console.WriteLine($"case state: Kenki={FakeGauges.Get<SAMGauge>().Kenki}, enemiesInRange(Guren)={FakeGame.NumberOfEnemiesInRange(Sam.Guren)}, " +
                          $"SeneiCd={FakeGame.Cooldown(Sam.Senei).CooldownRemaining}s, GurenCd={FakeGame.Cooldown(Sam.Guren).CooldownRemaining}s, " +
                          $"MeikyoCd={FakeGame.Cooldown(Sam.MeikyoShisui).CooldownRemaining}s, IkishotenCd={FakeGame.Cooldown(Sam.Ikishoten).CooldownRemaining}s, " +
                          $"justUsedTendoSetsugekka={FakeGame.JustUsedActions.ContainsKey(Sam.TendoSetsugekka)}, CanWeave={FakeGame.CanWeave}, " +
                          $"Level={FakeGame.Level}, playerStatuses={FakeGame.Statuses.Count}");

        uint gotPair = new Sam.SAM_ST_AdvancedMode().RunInvoke(Sam.Hakaze);
        Check($"SAM-1 unchanged: 1 enemy in Guren range, Senei + Guren ready, Senei gate true: " +
              $"Invoke(Hakaze) still spends the cooldown on Senei ({Sam.Senei})",
            gotPair == Sam.Senei, $"returned {gotPair}");

        // ---- (step 5 adds the SAM-1 two-enemy case here; the characterization below is the
        //      paired unchanged-behaviour case for it) ----

        // ---- the CANARY: deliberately asserts the opposite; must FAIL ----
        SetSeneiWindowState(1);
        uint got2 = new Sam.SAM_ST_AdvancedMode().RunInvoke(Sam.Hakaze);
        CheckCanary($"CANARY (expected to FAIL): identical 1-enemy state, asserting Invoke(Hakaze) " +
                    $"does NOT return Senei", got2 != Sam.Senei, $"returned {got2}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The exact state of the SAM-1 row: level 100, weave slot open, every earlier oGCD declined
    ///     (Meikyo/Ikishoten spent, no Zanshin/Shoha resources), the Senei gate TRUE (just past a
    ///     Tendo Setsugekka — UseSenei's own burst-timing check), Senei and Guren off cooldown and in
    ///     range, and <paramref name="gurenEnemies"/> enemies standing inside Guren's line.
    /// </summary>
    private static void SetSeneiWindowState(int gurenEnemies)
    {
        FakeGame.Reset();

        // level-100 Samurai in combat, weave slot open, standing in melee on a living target
        FakeGame.CanWeave = true;

        // the presets Joey runs on the ST advanced mode: the whole damage/CD suite, opener OFF
        // (mirrors his config: SAM_ST_Adv_Opener is not in EnabledActionsV6)
        foreach (var p in new[]
                 {
                     SamPreset.SAM_ST_AdvancedMode,
                     SamPreset.SAM_ST_Adv_CDs,
                     SamPreset.SAM_ST_Adv_Meikyo,
                     SamPreset.SAM_ST_Adv_Damage,
                     SamPreset.SAM_ST_Adv_Senei,
                     SamPreset.SAM_ST_Adv_Shinten,
                     SamPreset.SAM_ST_Adv_Ikishoten,
                     SamPreset.SAM_ST_Adv_Zanshin,
                     SamPreset.SAM_ST_Adv_Shoha,
                     SamPreset.SAM_ST_Adv_Iaijutsu,
                     SamPreset.SAM_ST_Adv_Tsubame,
                     SamPreset.SAM_ST_Adv_OgiNamikiri,
                     SamPreset.SAM_ST_Adv_Higanbana,
                     SamPreset.SAM_ST_Adv_TenkaGoken,
                     SamPreset.SAM_ST_Adv_Midare,
                     SamPreset.SAM_ST_Adv_TrueNorth,
                     SamPreset.SAM_ST_Adv_Yukikaze,
                     SamPreset.SAM_ST_Adv_Kasha,
                     SamPreset.SAM_ST_Adv_Gekko,
                     SamPreset.SAM_ST_Adv_RangedUptime,
                 })
            FakeGame.EnabledPresets.Add(p);

        // mid-burst Kenki; irrelevant below the Senei branch but realistic
        FakeGauges.SetByte<SAMGauge>("Kenki", 50);

        // the Senei gate: JustUsed(TendoSetsugekka, GCD * 3) is UseSenei's "right after the big
        // finisher" clause (SAM_Helper.cs:421). UsedAgo 1 s inside a 7 s window (< 2.5 * 3).
        FakeGame.JustUsedActions[Sam.TendoSetsugekka] = (UsedAgo: 1f, Window: 7f);

        // every oGCD that outranks the Senei/Guren spend is on cooldown or starved (spent earlier
        // this window), so Invoke reaches — and stays at — the Senei branch:
        FakeGame.PutOnCooldown(Sam.MeikyoShisui, 40f);
        FakeGame.PutOnCooldown(Sam.Ikishoten, 30f);
        // Zanshin needs the ZanshinReady buff (absent), Shoha needs 3 Meditation stacks (0),
        // Tsubame/Ogi/Iaijutsu need their Ready buffs (absent) — nothing else can answer first.

        // the state under test: how many enemies stand inside Guren's line
        FakeGame.EnemyCountsByAction[Sam.Guren] = gurenEnemies;
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
    // the Dalamud dev folder the way the plugin build does (same pattern as the VPR spike).
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
