// RotationHarness WHM (2026-10-04, SC11 round 7): offline cases for the real White Mage rotation.
//
// WHAT IS REAL HERE: the White Mage job source itself. WHM.cs and WHM_Helper.cs are compiled into
// this console app UNCHANGED (see the csproj Compile Include links): every Invoke branch, the weave
// gates, the Dia refresh decision (NeedsDoT), the opener plumbing and the action/buff tables are the
// exact shipping code. Everything the decisions READ (cooldowns, statuses, the WHM gauge, target,
// presets, settings, GCD count, the party-burst detector) comes from the harness fakes instead of
// the game.
//
// THE CASES (round 7, improvement WHM-1 from the ranked list):
//   CHAR   — Dia at 1.5s remaining under the default refresh window (2s): the MainCombo returns Dia
//            (today's normal refresh behaviour; must keep passing).
//   WHM-1  — opt-in "refresh Dia early in buffs": with the option on and the party bursting, a
//            10 s widened refresh window returns Dia at 8 s remaining; three controls pin the
//            option off, the burst off, and the window bound at 10 s (12 s still casts Glare3).
//   OOC    — the 1.0.4.279 out-of-combat stand-down for the area damage weave: out of combat
//            Assize (and the rest of the weave block) waits for combat, matching the Sage
//            damage paths since 1.0.4.278.
//
// THE CANARY: the CHAR state run through an assertion of the OPPOSITE behaviour. It is EXPECTED TO
// FAIL; the harness only exits 0 when the canary fails as expected (proof the test can actually fail).
//
//   dotnet build tests\GluttonyCombo.RotationHarness.WHM -c Release
//   dotnet tests\GluttonyCombo.RotationHarness.WHM\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.WHM.dll

using Dalamud.Game.ClientState.JobGauge.Types;
using GluttonyCombo.Combos.PvE;
using GluttonyCombo.Combos;

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
    // references Dalamud types (WHMGauge), so the load must happen only after the hook is in place.
    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo rotation harness: WHM_ST_MainCombo, offline --");

        // Evidence: the gauge fake is a REAL WHMGauge over harness memory; show the probed layout.
        _ = FakeGauges.Get<WHMGauge>();
        Console.WriteLine("gauge probe: WHMGauge over harness memory, probed props = " + string.Join(", ",
            FakeGauges.Offsets.Where(kv => kv.Key.Type == typeof(WHMGauge))
                .Select(kv => $"{kv.Key.Prop}@{kv.Value}")));
        Console.WriteLine("hook map: Aero -> Dia, Stone1 -> Glare3 (level-100 OriginalHook)");

        // ---- CHAR: characterization of today's Dia refresh (must keep passing) ----
        SetFightState();
        TargetDia(1.5f);
        uint gotChar = InvokeMain();
        Check("CHAR: Dia 1.5s remaining + default refresh window (2.0s) + option unset: " +
              $"Invoke(Stone1) returns Dia ({WHM.Dia}) — today's normal refresh",
            gotChar == WHM.Dia, $"returned {gotChar}");

        // ---- the CANARY: deliberately asserts the opposite; must FAIL ----
        SetFightState();
        TargetDia(1.5f);
        uint gotCanary = InvokeMain();
        CheckCanary($"CANARY (expected to FAIL): identical CHAR state, asserting Invoke(Stone1) " +
                    $"does NOT return Dia", gotCanary != WHM.Dia, $"returned {gotCanary}");

        // ==== WHM-1: opt-in early Dia refresh while the party is bursting (SC11 round 7) ====
        // The improvement: with the option on and Bursting.PartyIsBursting true, the refresh window
        // widens (2 s -> 10 s), so Dia is refreshed early to land inside raid buffs instead of
        // ticking down outside them. Off (the default) or outside a burst: today's 2 s window.

        // WHM-1: option ON + party bursting + Dia 8 s left -> Dia (the widened window reaches it)
        SetFightState();
        SetEarlyInBuffs(on: true);
        FakeGame.PartyIsBursting = true;
        TargetDia(8f);
        uint gotWhm1 = InvokeMain();
        Check("WHM-1: option on + party bursting + Dia 8 s remaining: " +
              $"Invoke(Stone1) returns Dia ({WHM.Dia}) — refreshed early into the burst",
            gotWhm1 == WHM.Dia, $"returned {gotWhm1}");

        // paired control: option OFF + party bursting + 8 s -> Glare3 (today's behaviour unchanged)
        SetFightState();
        SetEarlyInBuffs(on: false);
        FakeGame.PartyIsBursting = true;
        TargetDia(8f);
        uint gotOff = InvokeMain();
        Check("WHM-1 control: option off + party bursting + Dia 8 s remaining: " +
              $"Invoke(Stone1) returns Glare3 ({WHM.Glare3}) — default window (2 s) does not reach 8 s",
            gotOff == WHM.Glare3, $"returned {gotOff}");

        // no-burst control: option ON but nobody bursting + 8 s -> Glare3 (widening needs the burst)
        SetFightState();
        SetEarlyInBuffs(on: true);
        FakeGame.PartyIsBursting = false;
        TargetDia(8f);
        uint gotNoBurst = InvokeMain();
        Check("WHM-1 control: option on + party NOT bursting + Dia 8 s remaining: " +
              $"Invoke(Stone1) returns Glare3 ({WHM.Glare3}) — widening only applies inside a burst",
            gotNoBurst == WHM.Glare3, $"returned {gotNoBurst}");

        // window bound: option ON + bursting + Dia 12 s left -> Glare3 (the widened window is 10 s,
        // so 12 s is still too far out — no re-refresh spam every GCD inside one buff window)
        SetFightState();
        SetEarlyInBuffs(on: true);
        FakeGame.PartyIsBursting = true;
        TargetDia(12f);
        uint gotBound = InvokeMain();
        Check("WHM-1 control: option on + party bursting + Dia 12 s remaining: " +
              $"Invoke(Stone1) returns Glare3 ({WHM.Glare3}) — the widened window caps at 10 s",
            gotBound == WHM.Glare3, $"returned {gotBound}");

        // ==== OOC (1.0.4.279): out of combat the AoE damage weave does not fire ====
        // The same stand-down the Sage damage paths got in 1.0.4.278: out of combat a healer's
        // area damage mode has nothing to weave - Assize, Presence of Mind and Lucid Dreaming
        // all wait for combat. (The harvested logs show no out-of-combat White Mage casts for
        // the player; this is class coverage for the stand-down, not a reproduced fire.)

        SetAoeWeaveState(inCombat: true);
        uint gotWhmOocControl = InvokeAoe();
        Check("WHM-OOC control: in combat + weave window + Assize ready + target within 20 yalms: " +
              $"Invoke(Holy3) returns Assize ({WHM.Assize}) - the weave fires",
            gotWhmOocControl == WHM.Assize, $"returned {gotWhmOocControl}");

        SetAoeWeaveState(inCombat: false);
        uint gotWhmOoc = InvokeAoe();
        Check("WHM-OOC: identical state but OUT OF COMBAT: " +
              $"Invoke(Holy3) returns Holy ({WHM.Holy3}) - no Assize, no weave",
            gotWhmOoc == WHM.Holy3, $"returned {gotWhmOoc}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The shared state: Joey's live WHM_ST_MainCombo configuration (children DoT, GlareIV, Misery,
    ///     LilyOvercap, PresenceOfMind and Lucid on; Opener, Assize and Move_DoT off), level 100, in
    ///     combat, weave window open, standing still, a boss encounter with a living target Dia can be
    ///     applied to, an empty lily gauge, no player buffs, and everything off cooldown except
    ///     Presence of Mind (kept on cooldown so the weave block stays quiet at any GCD count).
    /// </summary>
    private static void SetFightState()
    {
        FakeGame.Reset();

        foreach (var p in new[]
                 {
                     Preset.WHM_ST_MainCombo,
                     Preset.WHM_ST_MainCombo_DoT, Preset.WHM_ST_MainCombo_GlareIV,
                     Preset.WHM_ST_MainCombo_Misery, Preset.WHM_ST_MainCombo_LilyOvercap,
                     Preset.WHM_ST_MainCombo_PresenceOfMind, Preset.WHM_ST_MainCombo_Lucid,
                 })
            FakeGame.EnabledPresets.Add(p);
        // NOT enabled (his live setup): WHM_ST_MainCombo_Opener, WHM_ST_MainCombo_Assize,
        // WHM_ST_MainCombo_Move_DoT, WHM_ST_Simple_DPS.

        FakeGame.InCombat = true;
        FakeGame.PartyInCombatFlag = true;
        FakeGame.CanWeave = true;
        FakeGame.IsMovingFlag = false;
        FakeGame.TimeStoodStillSeconds = 5f;
        FakeGame.InBossEncounter = true;
        FakeGame.TargetIsBoss = true;
        FakeGame.HasBattleTarget = true;
        FakeGame.TargetHPPercent = 100f;
        FakeGame.TargetCanApplyStatus = true;
        FakeGame.NumberOfGcdsUsed = 0;
        FakeGame.HardTargetObject = new FakeGameObject();

        // level-100 hooks: Aero -> Dia, Stone1 -> Glare3
        FakeGame.HookOverrides[WHM.Aero] = WHM.Dia;
        FakeGame.HookOverrides[WHM.Stone1] = WHM.Glare3;

        // keep the weave block quiet regardless of GCD count
        var pom = FakeGame.Cooldown(WHM.PresenceOfMind);
        pom.IsCooldown = true;
        pom.CooldownRemaining = 30f;
    }

    /// <summary>Set the WHM-1 option (WHM_ST_MainCombo_DoT_EarlyInBuffs) in the fake config store.</summary>
    private static void SetEarlyInBuffs(bool on) =>
        FakeGame.BoolValues["WHM_ST_MainCombo_DoT_EarlyInBuffs"] = on;

    /// <summary>
    ///     Put Dia on the fake target with the given remaining time (own status). The status id is the
    ///     DEBUFF id from the real AeroList table (the job code maps action -> debuff through it), not
    ///     the action id.
    /// </summary>
    private static void TargetDia(float remainingSeconds) =>
        FakeGame.TargetStatuses.Add(new FakeStatus(WHM.AeroList[WHM.Dia], remainingSeconds, SourceId: 0));

    private static uint InvokeMain() => new WHM.WHM_ST_MainCombo().RunInvoke(WHM.Stone1);

    private static uint InvokeAoe() => new WHM.WHM_AoE_Simple_DPS().RunInvoke(WHM.Holy3);

    /// <summary>
    ///     The AoE damage weave state for the out-of-combat cases: WHM_AoE_Simple_DPS with Assize
    ///     ready and a target within 20 yalms, the weave window open, an empty lily gauge, no
    ///     player buffs, and Presence of Mind held on cooldown so Assize is the only weave that
    ///     can answer in either combat state.
    /// </summary>
    private static void SetAoeWeaveState(bool inCombat)
    {
        FakeGame.Reset();

        FakeGame.EnabledPresets.Add(Preset.WHM_AoE_Simple_DPS);

        FakeGame.InCombat = inCombat;
        FakeGame.PartyInCombatFlag = inCombat;
        FakeGame.CanWeave = true;
        FakeGame.IsMovingFlag = false;
        FakeGame.HasBattleTarget = true;
        FakeGame.TargetHPPercent = 100f;
        FakeGame.NumberOfGcdsUsed = 0;
        FakeGame.TargetDistance = 10f;
        FakeGame.HardTargetObject = new FakeGameObject();

        FakeGame.HookOverrides[WHM.Holy] = WHM.Holy3;

        _ = FakeGame.Cooldown(WHM.Assize); // ready by default
        var pom = FakeGame.Cooldown(WHM.PresenceOfMind);
        pom.IsCooldown = true;
        pom.CooldownRemaining = 30f;
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
