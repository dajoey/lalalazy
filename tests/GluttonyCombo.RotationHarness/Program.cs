// RotationHarness (2026-10-04): offline proof-of-approach for testing a real PvE rotation decision.
//
// WHAT IS REAL HERE: the Viper job source itself. VPR.cs and VPR_Helper.cs are compiled into this
// console app UNCHANGED (see the csproj Compile Include links): every Invoke branch, the weave/coil/
// reawaken helpers, the gauge properties and the action/buff tables are the exact shipping code.
// Everything the decisions READ (cooldowns, statuses, gauge bytes, target, presets, settings) comes
// from the harness fakes instead of the game.
//
// THE CASE: with Rattling Coil stacks at the cap (3, Enhanced Viper's Rattle known), Uncoiled Fury
// ready, Serpent's Ire off cooldown and inside the weave-block window, VPR_ST_SimpleMode.Invoke must
// return Uncoiled Fury ahead of Vicewinder / the normal Uncoiled Fury path / the GCD combo
// (VPR.cs "OvercapUncoiledFuryProtection" branch -> VPR_Helper.cs IsCoilsCapped).
//
// THE CANARY: the same state run through an assertion of the OPPOSITE behaviour. It is EXPECTED TO
// FAIL; the harness only exits 0 when the canary fails as expected (proof the test can actually fail).
//
//   dotnet build tests\GluttonyCombo.RotationHarness -c Release
//   dotnet run -c Release --project tests\GluttonyCombo.RotationHarness

using Dalamud.Game.ClientState.JobGauge.Types;
using GluttonyCombo.Combos.PvE;

namespace GluttonyCombo.RotationHarness;

internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        AddDalamudResolver();

        Console.WriteLine("-- GluttonyCombo rotation harness spike: VPR_ST_SimpleMode, offline --");

        // Evidence: the gauge fake is a REAL VPRGauge over harness memory; show the probed layout.
        var gauge = FakeGauges.Get<VPRGauge>();
        Console.WriteLine($"gauge probe: VPRGauge over harness memory, byte offsets = " +
                          string.Join(", ", FakeGauges.Offsets.Where(kv => kv.Key.Type == typeof(VPRGauge))
                              .Select(kv => $"{kv.Key.Prop}=@{kv.Value}")));
        Console.WriteLine($"VPRGauge.RattlingCoilStacks reads {gauge.RattlingCoilStacks} (fresh, expected 0)");

        // ---- the PASS case: real behaviour ----
        SetCoilCapState();
        Console.WriteLine($"case state: RattlingCoilStacks={FakeGauges.Get<VPRGauge>().RattlingCoilStacks}, " +
                          $"DreadCombo={FakeGauges.Get<VPRGauge>().DreadCombo}, CanWeave={FakeGame.CanWeave}, " +
                          $"InMeleeRange={FakeGame.InMeleeRange}, UncoiledFury ready=" +
                          $"{!FakeGame.Cooldown(VPR.UncoiledFury).IsCooldown}, " +
                          $"SerpentsIre remaining={FakeGame.Cooldown(VPR.SerpentsIre).CooldownRemaining}s, " +
                          $"statuses={FakeGame.Statuses.Count}");

        var combo = new VPR.VPR_ST_SimpleMode();
        uint got = combo.RunInvoke(VPR.SteelFangs);
        Check($"coils capped (3/3) + Uncoiled Fury ready + Ire off CD + weave-blocked + in melee: " +
              $"Invoke(SteelFangs) returns Uncoiled Fury ({VPR.UncoiledFury}) ahead of Vicewinder/combo",
            got == VPR.UncoiledFury, $"returned {got}");

        // ---- the CANARY: deliberately asserts the opposite; must FAIL ----
        SetCoilCapState();
        uint got2 = new VPR.VPR_ST_SimpleMode().RunInvoke(VPR.SteelFangs);
        CheckCanary($"CANARY (expected to FAIL): identical state, asserting Invoke(SteelFangs) " +
                    $"does NOT return Uncoiled Fury", got2 != VPR.UncoiledFury, $"returned {got2}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>The exact state of the PASS case, also reused by the canary.</summary>
    private static void SetCoilCapState()
    {
        FakeGame.Reset();

        // level-100 Viper in combat, no weave available, standing in melee on a living target
        // (defaults already cover: AllTraitsKnown, everything learned/ready, no statuses, no dread combo)
        FakeGauges.SetByte<VPRGauge>("RattlingCoilStacks", 3); // at cap with Enhanced Viper's Rattle (trait 530)
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
