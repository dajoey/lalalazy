// RotationHarness SMN (SC11 round 6): offline harness for real Summoner rotation decisions.
//
// WHAT IS REAL HERE: the Summoner job source itself. SMN.cs and SMN_Helper.cs are compiled into this
// console app UNCHANGED (see the csproj Compile Include links): every Invoke branch, the
// TryOGCDSpells/TryMitigation/TrySummonSpells helpers, the gauge properties and the action/buff
// tables are the exact shipping code. Everything the decisions READ (cooldowns, statuses, gauge
// bytes, target, presets, settings) comes from the harness fakes instead of the game.
//
// THIS COMMIT carries only characterization cases that pass on the UNCHANGED source (plus the
// canary); the round-6 improvement commits (SMN-1, SMN-2) add their failing-first cases here.
//
//   dotnet build tests\GluttonyCombo.RotationHarness.SMN -c Release
//   dotnet tests\GluttonyCombo.RotationHarness.SMN\bin\Release\net10.0-windows7.0\GluttonyCombo.RotationHarness.SMN.dll

using Dalamud.Game.ClientState.JobGauge.Types;
using GluttonyCombo.Combos;
using GluttonyCombo.Combos.PvE;
// The harness namespace ends in .SMN, so bare `SMN` would resolve to the namespace; alias the job class.
using Smn = GluttonyCombo.Combos.PvE.SMN;

namespace GluttonyCombo.RotationHarness.SMN;

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
    // references Dalamud types (SMNGauge), so the load must happen only after the hook is in place.
    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo rotation harness: SMN_ST_Advanced_Combo, offline --");

        // Evidence: the gauge fake is a REAL SMNGauge over harness memory; show the probed layout.
        var gauge = FakeGauges.Get<SMNGauge>();
        Console.WriteLine("gauge probe: SMNGauge over harness memory, byte offsets = " +
                          string.Join(", ", FakeGauges.Offsets.Where(kv => kv.Key.Type == typeof(SMNGauge))
                              .Select(kv => $"{kv.Key.Prop}=@{kv.Value}")));
        Console.WriteLine($"SMNGauge.AttunementCount reads {gauge.AttunementCount} (fresh, expected 0)");

        // ---- CH1 (characterization, current behaviour): carbuncle needed ----
        // No pet present and Summon Carbuncle ready: NeedToSummon is true, so Invoke(Ruin) must return
        // Summon Carbuncle before anything else. True before and after the SMN-1/SMN-2 changes.
        FakeGame.Reset();
        FakeGame.HasPetPresent = false;
        uint got = new Smn.SMN_ST_Advanced_Combo().RunInvoke(Smn.Ruin);
        Check("CH1 (unchanged behaviour): no pet present, Summon Carbuncle ready: Invoke(Ruin) returns " +
              $"Summon Carbuncle ({Smn.SummonCarbuncle})", got == Smn.SummonCarbuncle, $"returned {got}");

        // ---- the CANARY: deliberately asserts the opposite; must FAIL ----
        FakeGame.Reset();
        FakeGame.HasPetPresent = false;
        uint got2 = new Smn.SMN_ST_Advanced_Combo().RunInvoke(Smn.Ruin);
        CheckCanary($"CANARY (expected to FAIL): identical to CH1, asserting Invoke(Ruin) " +
                    $"does NOT return Summon Carbuncle", got2 != Smn.SummonCarbuncle, $"returned {got2}");

        // ---- CH2 (characterization, current behaviour): out of combat keeps the Ruin filler ----
        // Aethercharge ready, burst option on, Searing Light 5s from ready — but the party is NOT in
        // combat: the demi-summon branch is gated on PartyInCombat, so Invoke(Ruin) keeps returning
        // the Ruin filler. True before and after the SMN-1/SMN-2 changes.
        SetDemiState(inCombat: false);
        uint got3 = new Smn.SMN_ST_Advanced_Combo().RunInvoke(Smn.Ruin);
        Check("CH2 (unchanged behaviour): party not in combat: Invoke(Ruin) returns the Ruin filler " +
              $"({Smn.Ruin}), not Aethercharge", got3 == Smn.Ruin, $"returned {got3}");

        // ---- SMN-1 (round 6): the demi summon must not be delayed for Searing Light ----
        // Balance guide (S6): "In the scenario of having higher than wanted spell speed, do not delay
        // your demi-primals by inserting additional Ruin III casts to fill the gaps."
        // State: Aethercharge ready, party in combat, Searing Light 5s from ready (inside the 3-8s drift
        // window), SearingLight_Burst option on, no Demi out. The drift branch must NOT insert Ruin:
        // Invoke(Ruin) returns Aethercharge. (Fails on the unchanged source: returns Ruin.)
        SetDemiState();
        uint got4 = new Smn.SMN_ST_Advanced_Combo().RunInvoke(Smn.Ruin);
        Check($"SMN-1: Aethercharge ready, in combat, Searing 5s away, burst on, no Demi: Invoke(Ruin) " +
              $"returns Aethercharge ({Smn.Aethercharge}), not the Ruin filler",
            got4 == Smn.Aethercharge, $"returned {got4}");

        // Paired unchanged behaviour: burst option OFF, identical state — Aethercharge fires both before
        // and after the change (the drift branch only runs with the burst option on).
        SetDemiState(burstOn: false);
        uint got5 = new Smn.SMN_ST_Advanced_Combo().RunInvoke(Smn.Ruin);
        Check($"SMN-1-unchanged: burst option off, same state: Invoke(Ruin) returns Aethercharge " +
              $"({Smn.Aethercharge})", got5 == Smn.Aethercharge, $"returned {got5}");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The demi-summon decision state: DemiSummons + SearingLight_Burst enabled, Searing Light 5s
    ///     from ready (inside the 3-8s drift window), no Demi out, Aethercharge ready.
    /// </summary>
    private static void SetDemiState(bool inCombat = true, bool burstOn = true)
    {
        FakeGame.Reset();
        FakeGame.PartyInCombat = inCombat;
        FakeGame.EnabledPresets.Add(Preset.SMN_ST_Advanced_Combo_DemiSummons);
        if (burstOn)
            FakeGame.EnabledPresets.Add(Preset.SMN_ST_Advanced_Combo_SearingLight_Burst);

        var searing = FakeGame.Cooldown(Smn.SearingLight);
        searing.IsCooldown = true;
        searing.CooldownRemaining = 5f;
        searing.CooldownElapsed = 55f;
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
