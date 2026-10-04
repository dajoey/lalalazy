// Program.cs — GluttonyCombo rotation harness: BRD ST Advanced Mode, offline
// (task tasks-20261004-ffxiv-rot-r6-brd, goal goals-20261002-rotation-execution-vs-best-practices round 6).
//
// WHAT IS REAL HERE: BRD.cs and BRD_Helper.cs, compiled UNCHANGED from src/ (see csproj
// Compile items). Everything they read — gauge, cooldowns, statuses, presets, config —
// comes from the harness fakes (Fakes.cs / FakeGame.cs). The REAL Dalamud BRD job gauge
// runs over harness-owned memory (FakeGauges, the stub-layer approach), so
// gauge.SoulVoice in the job file is the real property reading fake bytes.
//
// THE CASES: one characterization case that pins today's behaviour on the unchanged
// source, plus a canary with the opposite assertion (must FAIL — proves the harness can
// fail). The improvement's own case and its paired unchanged-behaviour case are added by
// the BRD-1 commit, which flips the failing case to passing with the one-line fix.
//
// RUN:   dotnet build tests\GluttonyCombo.RotationHarness.BRD -c Release
//        dotnet tests\GluttonyCombo.RotationHarness.BRD\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.BRD.dll
using System.Reflection;
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

    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo rotation harness: BRD ST Advanced Mode, offline --");

        // evidence: the real gauge reads harness memory — print the probed byte offsets
        var gauge = FakeGauges.Get<BRDGauge>();
        Console.WriteLine("gauge probe: BRDGauge over harness memory, byte offsets = " +
                          string.Join(", ", FakeGauges.ProbedOffsets
                              .Where(kv => kv.Key.Type == typeof(BRDGauge))
                              .Select(kv => $"{kv.Key.Prop}=@{kv.Value}")));
        Console.WriteLine($"BRDGauge.SoulVoice reads {gauge.SoulVoice} (fresh, expected 0)");

        // ---- characterization: today's pooled-Apex behaviour, unchanged by the improvement ----
        // Apex Pooling on, Soul Voice capped, INSIDE the full buff window (RS/BV/RF up,
        // RS duration 15 s < 18 s): UsePooledApex() is true, so Apex Arrow fires TODAY.
        SetAdvModeState();
        FakeGame.Statuses.Add(new(BRD.Buffs.RagingStrikes, 15f));
        FakeGame.Statuses.Add(new(BRD.Buffs.BattleVoice, 18f));
        FakeGame.Statuses.Add(new(BRD.Buffs.RadiantFinale, 18f));
        SetRagingCooldown(75f); // RS recast 80 s: 5 s elapsed, window open, next window 75 s away
        uint got = new BRD.BRD_ST_AdvMode().RunInvoke(BRD.HeavyShot);
        Check("BRD characterization: Apex pooling on, Soul Voice 100, inside the full buff window (RS 15s left, RS CD 75s): Invoke(HeavyShot) returns Apex Arrow",
            got == BRD.ApexArrow, $"returned {got}");

        // ---- the CANARY: same state, opposite assertion; must FAIL ----
        SetAdvModeState();
        FakeGame.Statuses.Add(new(BRD.Buffs.RagingStrikes, 15f));
        FakeGame.Statuses.Add(new(BRD.Buffs.BattleVoice, 18f));
        FakeGame.Statuses.Add(new(BRD.Buffs.RadiantFinale, 18f));
        SetRagingCooldown(75f);
        uint got2 = new BRD.BRD_ST_AdvMode().RunInvoke(BRD.HeavyShot);
        CheckCanary("CANARY (expected to FAIL): identical state, asserting Invoke(HeavyShot) does NOT return Apex Arrow",
            got2 != BRD.ApexArrow, $"returned {got2}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail} checks failed)");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     Baseline every case starts from: Joey-like BRD ST Advanced Mode (children on,
    ///     opener child off), level-100 Bard in combat on a full-HP living target,
    ///     weave-blocked, no procs, both DoTs fresh on the target (40 s), Soul Voice capped
    ///     at 100. Raging Strikes state is set per case. Values mirror the verdict page's
    ///     description of the shipped preset configuration.
    /// </summary>
    private static void SetAdvModeState()
    {
        FakeGame.Reset();

        foreach (var p in new[]
                 {
                     Preset.BRD_ST_AdvMode, Preset.BRD_Adv_Song, Preset.BRD_Adv_Buffs, Preset.BRD_ST_Adv_oGCD,
                     Preset.BRD_Adv_Pooling, Preset.BRD_Adv_DoT, Preset.BRD_Adv_BuffsEncore,
                     Preset.BRD_ST_ApexArrow, Preset.BRD_Adv_BuffsResonant,
                 })
            FakeGame.EnabledPresets.Add(p); // opener child (BRD_ST_Adv_Balance_Standard) deliberately NOT enabled

        // DoT options (Iron Jaws, application, Raging Jaws, MultiDot) and buff options all ON
        FakeGame.BoolArrayValues["BRD_Adv_DoT_Options"] = new() { [0] = true, [1] = true, [2] = true, [3] = true };
        FakeGame.BoolArrayValues["BRD_Adv_Buffs_Options"] = new() { [0] = true, [1] = true, [2] = true, [3] = true };

        FakeGame.CanWeave = false;
        FakeGame.TargetHPPercent = 100f;
        FakeGame.HookOverrides[BRD.HeavyShot] = BRD.BurstShot; // OriginalHook(HeavyShot) is Burst Shot at 100

        // both DoTs freshly applied (40 s remaining): skips application, Iron Jaws refresh
        // and Raging Jaws snapshot branches, so cases differ only in the RS window state
        FakeGame.TargetStatuses.Add(new(BRD.Debuffs.CausticBite, 40f));
        FakeGame.TargetStatuses.Add(new(BRD.Debuffs.Stormbite, 40f));

        FakeGauges.SetByte<BRDGauge>("SoulVoice", 100);
    }

    private static void SetRagingCooldown(float remaining)
    {
        var rs = FakeGame.Cooldown(BRD.RagingStrikes);
        rs.IsCooldown = true;
        rs.CooldownRemaining = remaining;
        rs.CooldownElapsed = (uint)(80f - remaining); // RS recast is 80 s
        rs.CooldownTotal = 80;
    }

    private static void Check(string name, bool ok, string detail)
    {
        _pass++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {name} [{detail}]");
        if (!ok) _fail++;
    }

    private static void CheckCanary(string name, bool invertedOk, string detail)
    {
        if (invertedOk)
        {
            // the canary PASSED, i.e. the harness failed to fail — treat as a real failure
            Console.WriteLine($"FAIL: {name} [{detail}] — canary was expected to FAIL");
            _fail++;
        }
        else
        {
            Console.WriteLine($"PASS: {name} [{detail}] — failed as expected");
        }

        // canary does not count toward the passing total
    }

    // resolution of Dalamud + Lumina from the live dev install (same pattern as the spike;
    // the dlls are only referenced for types — IStatus, IGameObject, the gauge bases —
    // so no game client is ever needed)
    private static void AddDalamudResolver()
    {
        var dev = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"XIVLauncher\addon\Hooks\dev");
        if (!Directory.Exists(dev))
        {
            var env = Environment.GetEnvironmentVariable("DALAMUD_HOME");
            if (env is not null) dev = env;
        }

        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            var name = new AssemblyName(e.Name).Name + ".dll";
            var path = Path.Combine(dev, name);
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
    }
}
