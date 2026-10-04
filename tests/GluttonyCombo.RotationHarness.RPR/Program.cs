// RotationHarness.RPR (2026-10-04, SC11 round 6): offline harness for the Reaper job, built from the
// round-4 VPR spike (branch rot/harness-spike).
//
// WHAT IS REAL HERE: the Reaper job source itself. RPR.cs and RPR_Helper.cs are compiled into this
// console app UNCHANGED (see the csproj Compile Include links): every Invoke branch, the weave/GCD
// helpers, the gauge properties and the action/buff tables are the exact shipping code. Everything the
// decisions READ (cooldowns, statuses, gauge bytes, target debuff, presets, settings, enemy count)
// comes from the harness fakes instead of the game.
//
// THE CHARACTERIZATION CASE: with Arcane Circle ready, inside the weave window, and Shadow of Death
// used a moment ago, RPR_ST_AdvancedMode.Invoke must return Arcane Circle (today's JustUsed rule,
// RPR_Helper.cs UseArcaneCircle). It passes on the UNCHANGED job source and anchors the wiring.
//
// THE CANARY: the same state run through an assertion of the OPPOSITE behaviour. It is EXPECTED TO
// FAIL; the harness only exits 0 when the canary fails as expected (proof the test can actually fail).
//
//   dotnet build tests\GluttonyCombo.RotationHarness.RPR -c Release
//   dotnet tests\GluttonyCombo.RotationHarness.RPR\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.RPR.dll

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
    // references Dalamud types (RPRGauge), so the load must happen only after the hook is in place.
    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo rotation harness: RPR_ST_AdvancedMode / RPR_AoE_AdvancedMode, offline --");

        // Evidence: the gauge fake is a REAL RPRGauge over harness memory; show the probed layout.
        var gauge = FakeGauges.Get<RPRGauge>();
        Console.WriteLine($"gauge probe: RPRGauge over harness memory, byte offsets = " +
                          string.Join(", ", FakeGauges.Offsets.Where(kv => kv.Key.Type == typeof(RPRGauge))
                              .Select(kv => $"{kv.Key.Prop}=@{kv.Value}")));
        Console.WriteLine($"RPRGauge.Soul reads {gauge.Soul}, Shroud {gauge.Shroud} (fresh, expected 0/0)");

        // ---- the CHARACTERIZATION case: today's behaviour, passes on unchanged source ----
        SetArcaneCircleJustUsedSoDState();
        Console.WriteLine($"case state: CanWeave={FakeGame.CanWeave}, ArcaneCircle ready=" +
                          $"{!FakeGame.Cooldown(RPR.ArcaneCircle).IsCooldown}, " +
                          $"JustUsed(SoD)={FakeGame.JustUsedActions.ContainsKey(RPR.ShadowOfDeath)}, " +
                          $"DeathsDesign on target={FakeGame.TargetStatuses.Count}");

        var combo = new RPR.RPR_ST_AdvancedMode();
        uint got = combo.RunInvoke(RPR.Slice);
        Check($"RPR characterization: Shadow of Death just used + Arcane Circle ready + weave window: " +
              $"Invoke(Slice) returns Arcane Circle ({RPR.ArcaneCircle})",
            got == RPR.ArcaneCircle, $"returned {got}");

        // ---- the CANARY: deliberately asserts the opposite; must FAIL ----
        SetArcaneCircleJustUsedSoDState();
        uint got2 = new RPR.RPR_ST_AdvancedMode().RunInvoke(RPR.Slice);
        CheckCanary($"CANARY (expected to FAIL): identical state, asserting Invoke(Slice) " +
                    $"does NOT return Arcane Circle", got2 != RPR.ArcaneCircle, $"returned {got2}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The exact state of the characterization case, also reused by the canary: level-100 Reaper in
    ///     combat, weave window open, only the RPR_ST_ArcaneCircle preset enabled, Arcane Circle ready,
    ///     Death's Design (40 s) on the target, Shadow of Death used half a second ago.
    /// </summary>
    private static void SetArcaneCircleJustUsedSoDState()
    {
        FakeGame.Reset();
        FakeGame.CanWeave = true;
        FakeGame.EnabledPresets.Add(Preset.RPR_ST_ArcaneCircle);
        FakeGame.TargetStatuses.Add(new FakeStatus(RPR.Debuffs.DeathsDesign, 40f));
        FakeGame.JustUsedActions[RPR.ShadowOfDeath] = (0.5f, 2f);
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
