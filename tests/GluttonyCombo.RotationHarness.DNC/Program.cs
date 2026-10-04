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
// THE CASE (DNC-2): the AoE Advanced Saber Dance TAIL block (DNC.cs ~line 971) read the
// SINGLE-TARGET threshold key (DNC_ST_Adv_SaberThreshold) and its `||` was unparenthesised, so the
// condition parsed as
//     [enabled && ready && Esprit >= STthr] || [(TechFinish && Esprit >= 50) && TechIsUp]
// — an AoE user's higher threshold was bypassed by the ST setting, and the Technical-finish
// disjunct fired Saber Dance even when the option itself was disabled. The fix makes the tail block
// read DNC_AoE_Adv_SaberThreshold with the Esprit checks parenthesised inside `enabled && ready &&
// (...)` — the exact shape the emergency block above it (~line 949) already had.
//
//   DNC-2        : split thresholds (ST 60 / AoE 90, Esprit 70, option ON)  -> Windmill (was SaberDance)
//   DNC-2 paired : equal thresholds (60/60, Esprit 70, option ON)           -> SaberDance, unchanged
//   DNC-2 tech   : option OFF, TechnicalFinish on, Esprit 60, Tech up       -> Windmill (was SaberDance
//                  fired through the bypassed preset/ActionReady gate)
//   CANARY       : the DNC-2 state asserting the PRE-fix behaviour; must FAIL on the fixed source.
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

        // ---- DNC-2: the behaviour the row wants (the AoE threshold gates the AoE tail block) ----
        SetAoESaberState(esprit: 70, stThreshold: 60, aoeThreshold: 90, saberPresetOn: true);
        Console.WriteLine($"case state: Esprit={FakeGauges.Get<DNCGauge>().Esprit}, " +
                          $"ST SaberThreshold={FakeGame.GetInt("DNC_ST_Adv_SaberThreshold", 50)}, " +
                          $"AoE SaberThreshold={FakeGame.GetInt("DNC_AoE_Adv_SaberThreshold", 50)}, " +
                          $"SaberDance preset={FakeGame.EnabledPresets.Contains(Preset.DNC_AoE_Adv_SaberDance)}, " +
                          $"TechnicalFinish status={FakeGame.Statuses.Count > 0}, " +
                          $"CanWeave={FakeGame.CanWeave}, in combat={FakeGame.InCombat}");
        uint got = new DncJob.DNC_AoE_AdvancedMode().RunInvoke(DncJob.Windmill);
        Check("DNC-2: split thresholds (ST 60 / AoE 90, Esprit 70, option on): Invoke(Windmill) returns " +
              $"Windmill ({DncJob.Windmill}) — 70 Esprit is below the AoE user's 90 gate, so the AoE " +
              $"tail block must decline",
            got == DncJob.Windmill, $"returned {got}");

        // ---- DNC-2 paired: the behaviour that must NOT change ----
        // Equal thresholds (Joey's saved 60/60): Esprit 70 is at/above both gates and the emergency
        // block fires exactly as before.
        SetAoESaberState(esprit: 70, stThreshold: 60, aoeThreshold: 60, saberPresetOn: true);
        Console.WriteLine($"paired case state: Esprit={FakeGauges.Get<DNCGauge>().Esprit}, " +
                          $"AoE SaberThreshold={FakeGame.GetInt("DNC_AoE_Adv_SaberThreshold", 50)}");
        uint gotPaired = new DncJob.DNC_AoE_AdvancedMode().RunInvoke(DncJob.Windmill);
        Check("DNC-2 paired: equal thresholds (60/60, Esprit 70, option on): Invoke(Windmill) keeps " +
              $"returning SaberDance ({DncJob.SaberDance}) via the emergency block",
            gotPaired == DncJob.SaberDance, $"returned {gotPaired}");

        // ---- DNC-2 tech: the preset must gate the Technical-finish disjunct too ----
        // Option OFF, TechnicalFinish status on, Esprit 60, Technical Step up: the unparenthesised
        // `||` used to fire Saber Dance through the second disjunct with the option disabled.
        SetAoESaberState(esprit: 60, stThreshold: 50, aoeThreshold: 50, saberPresetOn: false,
            technicalFinish: true);
        Console.WriteLine($"tech case state: Esprit={FakeGauges.Get<DNCGauge>().Esprit}, " +
                          $"SaberDance preset={FakeGame.EnabledPresets.Contains(Preset.DNC_AoE_Adv_SaberDance)}, " +
                          $"TechnicalFinish status={FakeGame.Statuses.Count > 0}, " +
                          $"TechnicalStep off cooldown={FakeGame.Cooldown(DncJob.TechnicalStep).IsCooldown == false}");
        uint gotTech = new DncJob.DNC_AoE_AdvancedMode().RunInvoke(DncJob.Windmill);
        Check("DNC-2 tech: option OFF + TechnicalFinish on + Esprit 60 + Tech up: Invoke(Windmill) " +
              $"returns Windmill ({DncJob.Windmill}) — a disabled option must never fire Saber Dance",
            gotTech == DncJob.Windmill, $"returned {gotTech}");

        // ---- the CANARY: asserts the PRE-fix behaviour; must FAIL ----
        SetAoESaberState(esprit: 70, stThreshold: 60, aoeThreshold: 90, saberPresetOn: true);
        uint got2 = new DncJob.DNC_AoE_AdvancedMode().RunInvoke(DncJob.Windmill);
        CheckCanary("CANARY (expected to FAIL): identical state to DNC-2, asserting Invoke(Windmill) " +
                    $"still returns SaberDance ({DncJob.SaberDance}) — the old ST-key behaviour",
            got2 == DncJob.SaberDance, $"returned {got2}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The exact state the cases share: a level-100 Dancer in combat, weave-blocked, no procs,
    ///     dance steps available — with the two Esprit thresholds set so only the key choice (and the
    ///     option gate) decides whether Saber Dance fires. All other defaults describe a clean
    ///     fallthrough to OriginalHook(Windmill). TechnicalStep sits off cooldown, so a `Tech is up`
    ///     guard passes whenever present.
    /// </summary>
    private static void SetAoESaberState(int esprit, int stThreshold, int aoeThreshold, bool saberPresetOn,
        bool technicalFinish = false)
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

        // the Technical-finish burst disjunct
        if (technicalFinish)
            FakeGame.Statuses.Add(new FakeStatus(DncJob.Buffs.TechnicalFinish, 12f));
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
