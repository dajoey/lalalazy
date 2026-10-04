// RotationHarness.NIN (2026-10-04): offline harness for a real Ninja rotation decision.
//
// WHAT IS REAL HERE: the Ninja job source itself. NIN.cs and NIN_Helper.cs are compiled into this
// console app UNCHANGED (see the csproj Compile Include links): every Invoke branch, the Ninki/
// mudra/buff-window helpers, the gauge properties and the action/buff tables are the exact shipping
// code. Everything the decisions READ (cooldowns, statuses, gauge bytes, target, presets, settings,
// enemies in range) comes from the harness fakes instead of the game.
//
// THE CASE (NIN-2, desired behaviour): a level-100 Ninja mid-fight inside a Kunai's Bane window on
// the target, Ninki at exactly 50, weave slot open, all burst cooldowns (Kassatsu, Bunshin, Ten Chi
// Jin, Assassinate, Kunai's Bane, Dokumori/Mug) on cooldown, two enemies inside Hellfrog Medium's
// range. NIN_ST_AdvancedMode.Invoke(SpinningEdge) reaches the Ninki-spend branch (NIN.cs
// "NIN_ST_AdvancedMode_Bhavacakra" block) and must return Hellfrog Medium — the spender that hits
// both enemies — instead of Bhavacakra. The paired case holds the same state at ONE enemy and
// asserts today's single-target spend (Bhavacakra) is unchanged.
//
// THE CANARY: the same state run through an assertion of the OPPOSITE behaviour. It is EXPECTED TO
// FAIL; the harness only exits 0 when the canary fails as expected (proof the test can actually fail).
//
//   dotnet build tests\GluttonyCombo.RotationHarness.NIN -c Release
//   dotnet tests\GluttonyCombo.RotationHarness.NIN\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.NIN.dll

using Dalamud.Game.ClientState.JobGauge.Types;
using GluttonyCombo.Combos.PvE;
using Nin = GluttonyCombo.Combos.PvE.NIN;
using NinPreset = GluttonyCombo.Combos.Preset;

namespace GluttonyCombo.RotationHarness.NIN;

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
    // references Dalamud types (NINGauge), so the load must happen only after the hook is in place.
    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo rotation harness: NIN_ST_AdvancedMode, offline --");

        // Evidence: the gauge fake is a REAL NINGauge over harness memory; show the probed layout.
        var gauge = FakeGauges.Get<NINGauge>();
        Console.WriteLine("gauge probe: NINGauge over harness memory, byte offsets = " +
                          string.Join(", ", FakeGauges.Offsets.Where(kv => kv.Key.Type == typeof(NINGauge))
                              .Select(kv => $"{kv.Key.Prop}=@{kv.Value}")));
        Console.WriteLine($"NINGauge.Ninki reads {gauge.Ninki} (fresh, expected 0)");

        // ---- the PASS case: today's behaviour, on the UNCHANGED job source ----
        SetNinkiSpendState(2);
        Console.WriteLine($"case state: Ninki={FakeGauges.Get<NINGauge>().Ninki}, Kazematoi={FakeGauges.Get<NINGauge>().Kazematoi}, " +
                          $"enemiesInRange(HellfrogMedium)={FakeGame.NumberOfEnemiesInRange(Nin.HellfrogMedium)}, " +
                          $"KunaisBaneOnTarget={FakeGame.CurrentTarget.HasStatus(Nin.Debuffs.KunaisBane)}, CanWeave={FakeGame.CanWeave}, " +
                          $"KassatsuCd={FakeGame.Cooldown(Nin.Kassatsu).CooldownRemaining}s, BunshinCd={FakeGame.Cooldown(Nin.Bunshin).CooldownRemaining}s, " +
                          $"TrickCd={FakeGame.Cooldown(Nin.TrickAttack).CooldownRemaining}s, MugCd={FakeGame.Cooldown(Nin.Mug).CooldownRemaining}s, " +
                          $"playerStatuses={FakeGame.Statuses.Count}");

        var combo = new Nin.NIN_ST_AdvancedMode();
        uint got = combo.RunInvoke(Nin.SpinningEdge);
        Check($"NIN-2: 2 enemies in Hellfrog Medium range + Ninki 50 + Kunai's Bane up: " +
              $"Invoke(SpinningEdge) spends Ninki on Hellfrog Medium ({Nin.HellfrogMedium}), not Bhavacakra",
            got == Nin.HellfrogMedium, $"returned {got}");

        // ---- the paired unchanged-behaviour case: same state, one enemy only ----
        SetNinkiSpendState(1);
        uint gotPair = new Nin.NIN_ST_AdvancedMode().RunInvoke(Nin.SpinningEdge);
        Check($"NIN-2 unchanged: 1 enemy in Hellfrog Medium range, same state: " +
              $"Invoke(SpinningEdge) still spends Ninki on Bhavacakra ({Nin.Bhavacakra})",
            gotPair == Nin.Bhavacakra, $"returned {gotPair}");

        // ---- the CANARY: deliberately asserts the opposite; must FAIL ----
        SetNinkiSpendState(2);
        uint got2 = new Nin.NIN_ST_AdvancedMode().RunInvoke(Nin.SpinningEdge);
        CheckCanary($"CANARY (expected to FAIL): identical 2-enemy state, asserting Invoke(SpinningEdge) " +
                    $"does NOT return Hellfrog Medium", got2 != Nin.HellfrogMedium, $"returned {got2}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The exact state of the NIN-2 row: Ninki 50, Kunai's Bane on the target, weave open, every
    ///     earlier oGCD declined, and <paramref name="hellfrogEnemies"/> enemies inside Hellfrog
    ///     Medium's range. Also reused by the canary.
    /// </summary>
    private static void SetNinkiSpendState(int hellfrogEnemies)
    {
        FakeGame.Reset();

        // level-100 Ninja in combat, weave slot open, standing in melee on a living target
        FakeGame.CanWeave = true;
        FakeGame.LastAction = Nin.SpinningEdge; // out of any mudra; MudraPhase/InMudra false

        // the presets Joey runs on the ST advanced mode (opener deliberately off, as in his config)
        foreach (var p in new[]
                 {
                     NinPreset.NIN_ST_AdvancedMode,
                     NinPreset.NIN_ST_AdvancedMode_Kassatsu,
                     NinPreset.NIN_ST_AdvancedMode_Bunshin,
                     NinPreset.NIN_ST_AdvancedMode_TenChiJin,
                     NinPreset.NIN_ST_AdvancedMode_TenriJindo,
                     NinPreset.NIN_ST_AdvancedMode_Assassinate,
                     NinPreset.NIN_ST_AdvancedMode_Meisui,
                     NinPreset.NIN_ST_AdvancedMode_Bhavacakra,
                     NinPreset.NIN_ST_AdvancedMode_Mug,
                     NinPreset.NIN_ST_AdvancedMode_TrickAttack,
                     NinPreset.NIN_ST_AdvancedMode_StunInterupt,
                     NinPreset.NIN_ST_AdvancedMode_Ninjitsus,
                     NinPreset.NIN_ST_AdvancedMode_Ninjitsus_Hyosho,
                     NinPreset.NIN_ST_AdvancedMode_Ninjitsus_Suiton,
                     NinPreset.NIN_ST_AdvancedMode_Ninjitsus_Raiton,
                 })
            FakeGame.EnabledPresets.Add(p);

        // Ninki at exactly 50 (the Kunai's-Bane pooling threshold), no Kazematoi pressure
        FakeGauges.SetByte<NINGauge>("Ninki", 50);
        FakeGauges.SetByte<NINGauge>("Kazematoi", 2);

        // the Kunai's Bane debuff rides the target (TrickDebuff -> NinkiPool() returns 50)
        FakeGame.CurrentTarget.Statuses.Add(new FakeStatus(Nin.Debuffs.KunaisBane, 12f));

        // every oGCD that outranks the Ninki spend is on cooldown (spent earlier this window):
        // keeps Kassatsu/Bunshin/TCJ/Assassinate/Meisui/Mug/Trick declining so Invoke reaches the
        // Bhavacakra branch. ShadowWalker is absent (past the opener), so Meisui/Trick cannot fire.
        FakeGame.PutOnCooldown(Nin.Kassatsu, 50f);
        FakeGame.PutOnCooldown(Nin.Bunshin, 40f);   // also keeps NinkiPool() off the 85-pool clause
        FakeGame.PutOnCooldown(Nin.TenChiJin, 50f);
        FakeGame.PutOnCooldown(Nin.Assassinate, 50f);
        FakeGame.PutOnCooldown(Nin.TrickAttack, 55f);
        FakeGame.PutOnCooldown(Nin.Mug, 55f);       // also keeps NinkiPool() off the 60-pool clause
        FakeGame.PutOnCooldown(Nin.Meisui, 50f);

        // the state under test: how many enemies stand inside Hellfrog Medium's range
        FakeGame.EnemyCountsByAction[Nin.HellfrogMedium] = hellfrogEnemies;
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
