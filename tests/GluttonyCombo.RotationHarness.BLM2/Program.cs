// RotationHarness.BLM2 (2026-10-18): offline harness for the real BLM_Helper.UseManaward gate.
//
// WHAT IS REAL HERE: BLM_Helper.cs — the BLM partial with the mana/movement/opener helpers and the
// whole UseManaward gate — compiled UNCHANGED into this console app. UseManaward is private static,
// so the cases drive it through reflection; the object under test is the exact shipping IL.
// Everything the gate READS (player HP, shield, party, incoming damage, the PotD behemoth meteor
// knobs) comes from the harness fakes (Fakes.cs / FakeGame.cs).
//
// THE CASES (PotD behemoth gap-close round, tasks-20261018-potd-gluttony-gap-close-01):
// The solo HP branch of UseManaward (BLM_Helper.cs:416: HP < threshold && !IsInParty()) burns the
// 120s Manaward on chip damage while the behemoth nears its Ecliptic Meteor threshold; the close
// banks it during the hold band. GroupDamageIncoming/raidwide paths are untouched.
//
// THE CANARY: an assertion of the OPPOSITE behaviour, EXPECTED TO FAIL; the harness only exits 0
// when every real case passes AND the canary fails as expected.
//
//   dotnet build tests/GluttonyCombo.RotationHarness.BLM2 -c Release
//   dotnet tests/GluttonyCombo.RotationHarness.BLM2/bin/Release/net10.0-windows7.0/GluttonyCombo.RotationHarness.BLM2.dll

using System.Reflection;
using B = GluttonyCombo.Combos.PvE.BLM;

namespace GluttonyCombo.RotationHarness.BLM2;

internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        AddDalamudResolver();
        return Run();
    }

    // Kept out of Main: Main is JIT-compiled before the resolver below can be registered, and its
    // body references Dalamud types, so the load must happen only after the hook is in place.
    private static int Run()
    {
        Console.WriteLine("-- GluttonyCombo rotation harness: BLM UseManaward, offline --");

        var useManaward = typeof(B).GetMethod("UseManaward",
            BindingFlags.NonPublic | BindingFlags.Static,
            new[] { typeof(int), typeof(bool), typeof(int), typeof(bool) })
            ?? throw new MissingMethodException("BLM.Helper", "UseManaward(int,bool,int,bool)");

        bool Manaward(int hpThreshold = 60, bool simpleLogic = true, int triggerMode = 0, bool soloOption = false) =>
            (bool)useManaward.Invoke(null, new object[] { hpThreshold, simpleLogic, triggerMode, soloOption })!;

        // ---- BLM-1: the pre-meteor hold banks the Manaward on the solo HP branch (RED-first) ----
        FakeGame.Reset();
        FakeGame.HealthPercent = 50f;      // chip damage took the player under the default 60 slider
        FakeGame.BehemothMeteorHold = true;
        Check("BLM-1 hold: solo, player 50% HP (under the 60 slider), behemoth in the pre-meteor band: " +
              "Manaward is BANKED (not pressed on chip damage)",
            !Manaward(), "pressed=true");

        // ---- unchanged: the same chip-damage press outside any behemoth state is preserved ----
        FakeGame.Reset();
        FakeGame.HealthPercent = 50f;
        Check("BLM-1 unchanged: solo, player 50% HP, no behemoth state: today's chip-damage press is preserved",
            Manaward(), "pressed=false");

        // ---- the incoming-damage path is untouched by the hold ----
        FakeGame.Reset();
        FakeGame.BehemothMeteorHold = true;
        FakeGame.GroupDamageIncoming = true;
        Check("BLM-1 unchanged: in the band but raidwide detected (cast on the bar = the window): Manaward presses",
            Manaward(), "pressed=false");

        // ---- advanced mode: triggerMode 1 (HP-only, no raidwide) banks in the band too ----
        FakeGame.Reset();
        FakeGame.HealthPercent = 50f;
        FakeGame.BehemothMeteorHold = true;
        Check("BLM-1 hold (advanced triggerMode 1): HP-only trigger, player 50% HP, in the band: Manaward is BANKED",
            !Manaward(60, simpleLogic: false, triggerMode: 1), "pressed=true");

        FakeGame.Reset();
        FakeGame.HealthPercent = 50f;
        Check("BLM-1 unchanged (advanced triggerMode 1): HP-only trigger, 50% HP, no behemoth state: press preserved",
            Manaward(60, simpleLogic: false, triggerMode: 1), "pressed=false");

        // ---- the shield-up and cooldown gates hold inside the band ----
        FakeGame.Reset();
        FakeGame.HealthPercent = 50f;
        FakeGame.HasShield = true;
        FakeGame.BehemothMeteorHold = true;
        Check("BLM-1 negative: in the band with a shield already up: no Manaward (the HasShield gate holds)",
            !Manaward(), "pressed=true");

        FakeGame.Reset();
        FakeGame.HealthPercent = 95f;
        FakeGame.BehemothMeteorHold = true;
        Check("BLM-1 negative: in the band at 95% HP (above the slider): no Manaward (nothing to bank yet)",
            !Manaward(), "pressed=true");

        // ---- the CANARY: deliberately asserts the opposite; must FAIL ----
        FakeGame.Reset();
        FakeGame.HealthPercent = 50f;
        FakeGame.BehemothMeteorHold = true;
        CheckCanary("CANARY (expected to FAIL): solo at 50% HP in the band, asserting Manaward PRESSES",
            Manaward(), "the opposite assertion held");

        Console.WriteLine(_fail == 0
            ? $"OK ({_pass} checks, canary failed as expected)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
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

    private static void CheckCanary(string desc, bool assertion, string detail = "")
    {
        if (assertion)
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
    // the Dalamud dev folder the way the plugin build does.
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
