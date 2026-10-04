// GluttonyCombo.RotationHarness.DNC (2026-10-04, SC11 round 7): offline harness for a real Dancer
// rotation decision, cloned from the rot/harness-spike VPR proof and the MCH round-6 instance
// (notebook "Offline Harness Approach" section 9). A separate project per job is deliberate.
//
// WHAT IS REAL HERE: the Dancer job source itself. DNC.cs and DNC_Helper.cs are compiled into this
// console app UNCHANGED (see the csproj Compile Include links): every Invoke branch, the
// dance-step/finish logic, the Saber Dance emergency and tail blocks, the gauge properties and the
// action/buff tables are the exact shipping code. Everything the decisions READ (cooldowns, statuses,
// gauge bytes, target, presets, settings, enemy counts) comes from the harness fakes instead of the
// game. The dance-partner machinery (Svc/Player reads) is out-of-combat only and never reached here.
//
// THE CASE (DNC-2, the behaviour the Improvements Ranked row wants): the AoE Advanced Saber Dance TAIL
// block (DNC.cs ~line 971) reads the SINGLE-TARGET threshold key (DNC_ST_Adv_SaberThreshold) and its
// `||` is unparenthesised, so the condition parses as
//     [enabled && ready && Esprit >= STthr] || [(TechFinish && Esprit >= 50) && TechIsUp]
// — i.e. an AoE user's higher threshold is bypassed by the ST setting, and the Technical-finish
// disjunct fires Saber Dance even when the option itself is disabled. The emergency block above it
// (~line 949) already has the correct shape: it gates on DNC_AoE_Adv_SaberThreshold with the Esprit
// checks parenthesised inside `enabled && ready && (...)`.
//
// THIS COMMIT ships the CHARACTERIZATION of today's behaviour plus the CANARY asserting the wanted
// behaviour (which must FAIL on the unfixed source — proof the case can actually fail). The fix
// commit flips the characterization to the wanted behaviour and adds the paired unchanged-behaviour
// case (bugfix-test-first discipline).
//
//   dotnet build tests\GluttonyCombo.RotationHarness.DNC -c Release
//   dotnet tests\GluttonyCombo.RotationHarness.DNC\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.DNC.dll

using Dalamud.Game.ClientState.JobGauge.Types;
using GluttonyCombo.Combos;
// the harness namespace ends in .DNC, which shadows the job class name inside this file; alias it
using DncJob = GluttonyCombo.Combos.PvE.DNC;

namespace GluttonyCombo.RotationHarness.DNC;

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
    // references Dalamud types (DNCGauge), so the load must happen only after the hook is in place.
    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo rotation harness: DNC_AoE_AdvancedMode, offline --");

        // Evidence: the gauge fake is a REAL DNCGauge over harness memory; show the probed layout.
        var gauge = FakeGauges.Get<DNCGauge>();
        Console.WriteLine("gauge probe: DNCGauge over harness memory, byte offsets = " +
                          string.Join(", ", FakeGauges.Offsets.Where(kv => kv.Key.Type == typeof(DNCGauge))
                              .Select(kv => $"{kv.Key.Prop}=@{kv.Value}")));
        Console.WriteLine($"DNCGauge.IsDancing reads {gauge.IsDancing} (fresh, expected False)");
        Console.WriteLine($"DNCGauge.Esprit reads {gauge.Esprit} (fresh, expected 0)");

        // ---- characterization: TODAY's behaviour with the thresholds split ----
        // ST 60 / AoE 90, Esprit 70: the emergency block correctly declines (70 < 90) but the tail
        // block reads the ST key, so 70 >= 60 fires Saber Dance anyway.
        SetAoESaberState(esprit: 70, stThreshold: 60, aoeThreshold: 90, saberPresetOn: true);
        Console.WriteLine($"case state: Esprit={FakeGauges.Get<DNCGauge>().Esprit}, " +
                          $"ST SaberThreshold={FakeGame.GetInt("DNC_ST_Adv_SaberThreshold", 50)}, " +
                          $"AoE SaberThreshold={FakeGame.GetInt("DNC_AoE_Adv_SaberThreshold", 50)}, " +
                          $"SaberDance preset={FakeGame.EnabledPresets.Contains(Preset.DNC_AoE_Adv_SaberDance)}, " +
                          $"TechnicalFinish status={FakeGame.Statuses.Count > 0}, " +
                          $"CanWeave={FakeGame.CanWeave}, in combat={FakeGame.InCombat}");
        uint got = new DncJob.DNC_AoE_AdvancedMode().RunInvoke(DncJob.Windmill);
        Check("characterization: split thresholds (ST 60 / AoE 90, Esprit 70): Invoke(Windmill) " +
              $"returns SaberDance ({DncJob.SaberDance}) today — the AoE tail block reads the ST key (DNC-2)",
            got == DncJob.SaberDance, $"returned {got}");

        // ---- the CANARY: asserts the WANTED behaviour; must FAIL on the unfixed source ----
        SetAoESaberState(esprit: 70, stThreshold: 60, aoeThreshold: 90, saberPresetOn: true);
        uint got2 = new DncJob.DNC_AoE_AdvancedMode().RunInvoke(DncJob.Windmill);
        CheckCanary("CANARY (expected to FAIL): identical state, asserting Invoke(Windmill) returns " +
                    $"Windmill ({DncJob.Windmill}) — the AoE tail block honouring the AoE threshold",
            got2 == DncJob.Windmill, $"returned {got2}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The exact state both cases share: a level-100 Dancer in combat, weave-blocked, no procs,
    ///     dance steps available — with the two Esprit thresholds split so only the key choice
    ///     decides whether Saber Dance fires. All other defaults describe a clean fallthrough to
    ///     OriginalHook(Windmill).
    /// </summary>
    private static void SetAoESaberState(int esprit, int stThreshold, int aoeThreshold, bool saberPresetOn)
    {
        FakeGame.Reset();

        FakeGauges.SetByte<DNCGauge>("Esprit", (byte)esprit);

        // the enabled preset pair for this mode (Improvements Ranked DNC-2 row: the Saber Dance child
        // of AoE Advanced; the parent mode carries the Invoke)
        var presets = new HashSet<Preset> { Preset.DNC_AoE_AdvancedMode };
        if (saberPresetOn)
            presets.Add(Preset.DNC_AoE_Adv_SaberDance);
        FakeGame.EnabledPresets = presets;

        // the decision under test: the ST and AoE Esprit thresholds
        FakeGame.IntValues["DNC_ST_Adv_SaberThreshold"] = stThreshold;
        FakeGame.IntValues["DNC_AoE_Adv_SaberThreshold"] = aoeThreshold;
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
