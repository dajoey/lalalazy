using ECommons.ExcelServices;
using GluttonyCombo.AutoRotation;
using GluttonyCombo.Core;
using Newtonsoft.Json;
using State = GluttonyCombo.Core.ConfigMigration.State;

namespace GluttonyCombo.ConfigMigrateHarness;

/// <summary>
///     Offline assertions on the configuration migration ladder and the per-job auto-rez
///     resolver (GluttonyCombo v1.0.4.174). Compiles the real <see cref="ConfigMigration" /> and
///     the real <see cref="HealerSettings" /> - no Dalamud, no game - so both the v7-&gt;v8 step
///     and the job-&gt;setting mapping are proven before they touch anyone's saved settings.
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        MigrationV7();
        MigrationV8();
        LegacyShadowRoundTrip();
        ResolverTruthTable();

        Console.WriteLine();
        Console.WriteLine($"{_pass} passed, {_fail} failed");
        if (_fail == 0) Console.WriteLine("OK");
        return _fail == 0 ? 0 : 1;
    }

    // =============================================================================
    // v6 -> v7: the tankbuster seed shipped in 1.0.4.173. Kept green so the new step
    // cannot quietly break the old one.
    // =============================================================================
    private static void MigrationV7()
    {
        Section("v6 -> v7 (TankbustersBeyondParty)");

        // Joey's real config as read live from omasky on 2026-09-05 (before 1.0.4.173):
        // Version 6, TankbustersBeyondParty = false and serialized, which is exactly why a
        // default flip alone would have been a silent no-op for him.
        var live = new State(Version: 6, TankbustersBeyondParty: false);
        var r = ConfigMigration.Migrate(live);

        Check("existing install is migrated", r.Changed);
        Check("...to the current schema version",
            r.State.Version == ConfigMigration.CurrentVersion, r.State.Version.ToString());
        Check("...and the setting is seeded ON", r.State.TankbustersBeyondParty);
        Check("...the note names the setting and how to undo it",
            r.Notes.Any(n =>
                n.Contains("Also shield tankbusters outside your party", StringComparison.Ordinal) &&
                n.Contains("Untick", StringComparison.Ordinal)),
            r.Notes.Count > 0 ? r.Notes[0] : "(none)");

        // A user who takes the seeded setting back OFF must stay off forever. A ladder that
        // re-asserted the value on every load would pass every test above.
        var afterOptOut = ConfigMigration.Migrate(
            new State(ConfigMigration.CurrentVersion, TankbustersBeyondParty: false));
        Check("a user who turns the setting back off is NOT overridden",
            !afterOptOut.State.TankbustersBeyondParty);
        Check("...and that is recorded as no change", !afterOptOut.Changed);

        // An existing install that already had it on is version-bumped with nothing to say.
        var alreadyOn = ConfigMigration.Migrate(new State(6, true));
        Check("an install that already had it on is version-bumped", alreadyOn.Changed);
        Check("...but is not told about a change that did not happen",
            !alreadyOn.Notes.Any(n => n.Contains("tankbusters", StringComparison.OrdinalIgnoreCase)),
            string.Join(" | ", alreadyOn.Notes));
    }

    // =============================================================================
    // v7 -> v8: split the single "Require Swiftcast/Dualcast" toggle into seven per-job
    // checkboxes. This is the step this release adds.
    // =============================================================================
    private static void MigrationV8()
    {
        Section("v7 -> v8 (per-job AutoRezRequireSwift)");

        // ---- Case 1: an existing install that had the tick ON.
        var onResult = ConfigMigration.Migrate(
            new State(7, TankbustersBeyondParty: true, AutoRezRequireSwiftGlobal: true));
        var on = onResult.State.RequireSwift;

        Check("old config with the tick ON is migrated", onResult.Changed);
        Check("...WHM/CNJ seeded true", on.WHM);
        Check("...SCH seeded true", on.SCH);
        Check("...AST seeded true", on.AST);
        Check("...SGE seeded true", on.SGE);
        Check("...SMN seeded true", on.SMN);
        Check("...BLU seeded true", on.BLU);
        Check("...RDM is true (was already hardcoded ON; NOT seeded from the global)", on.RDM);
        Check("...the legacy shadow is nulled so the step cannot run twice",
            onResult.State.AutoRezRequireSwiftGlobal is null,
            onResult.State.AutoRezRequireSwiftGlobal?.ToString() ?? "null");
        Check("...and the user is told what happened",
            onResult.Notes.Any(n =>
                n.Contains("Require Swiftcast/Dualcast", StringComparison.Ordinal) &&
                n.Contains("RDM", StringComparison.Ordinal)),
            string.Join(" | ", onResult.Notes));

        // ---- Case 2: THE NEGATIVE CONTROL. An existing install that had the tick OFF. The six
        // stay off - and RDM must STILL be true. Seeding RDM from this false would silently
        // start hard-casting a 10s Verraise for every existing user.
        var offResult = ConfigMigration.Migrate(
            new State(7, TankbustersBeyondParty: true, AutoRezRequireSwiftGlobal: false));
        var off = offResult.State.RequireSwift;

        Check("old config with the tick OFF leaves WHM/CNJ false", !off.WHM);
        Check("...SCH false", !off.SCH);
        Check("...AST false", !off.AST);
        Check("...SGE false", !off.SGE);
        Check("...SMN false", !off.SMN);
        Check("...BLU false", !off.BLU);
        Check("...but RDM is STILL true (the whole point of the negative control)", off.RDM);
        Check("...the legacy shadow is nulled here too",
            offResult.State.AutoRezRequireSwiftGlobal is null);
        Check("...and nothing is claimed about a carry-over that did not happen",
            !offResult.Notes.Any(n => n.Contains("carried onto", StringComparison.Ordinal)),
            string.Join(" | ", offResult.Notes));

        // ---- Case 3: a fresh install. No legacy key at all -> field initialisers win.
        var freshSettings = new HealerSettings();
        var freshRead = ConfigMigration.Read(ConfigMigration.CurrentVersion, freshSettings);
        var freshResult = ConfigMigration.Migrate(freshRead);
        Check("fresh install is not migrated", !freshResult.Changed);
        Check("...six default false",
            !freshSettings.AutoRezRequireSwiftWHM && !freshSettings.AutoRezRequireSwiftSCH &&
            !freshSettings.AutoRezRequireSwiftAST && !freshSettings.AutoRezRequireSwiftSGE &&
            !freshSettings.AutoRezRequireSwiftSMN && !freshSettings.AutoRezRequireSwiftBLU);
        Check("...and RDM defaults true", freshSettings.AutoRezRequireSwiftRDM);

        // ---- Case 4: idempotence. Running the seed twice must not re-apply it. This is the
        // property that decides whether the ladder argues with the user on every load.
        var again = ConfigMigration.Migrate(onResult.State);
        Check("re-running the ladder changes nothing", !again.Changed);
        Check("...and emits no notes", again.Notes.Count == 0, again.Notes.Count.ToString());
        Check("...and leaves the seeded values alone",
            again.State.RequireSwift == onResult.State.RequireSwift);

        // The real re-load shape: user unticks WHM after the migration, restarts, and the
        // ladder must not put it back. The legacy key is gone from their JSON by then.
        var userUnticked = onResult.State with
        {
            RequireSwift = onResult.State.RequireSwift with { WHM = false },
        };
        var afterUntick = ConfigMigration.Migrate(userUnticked);
        Check("a user who unticks a job afterwards is NOT overridden",
            !afterUntick.State.RequireSwift.WHM);
        Check("...and RDM unticked stays unticked too",
            !ConfigMigration.Migrate(
                userUnticked with
                {
                    RequireSwift = userUnticked.RequireSwift with { RDM = false },
                }).State.RequireSwift.RDM);

        // ---- Case 5: a config from the future is left completely alone.
        var future = ConfigMigration.Migrate(new State(99, false, true));
        Check("a config from the future is left untouched",
            !future.Changed && future.State.Version == 99 &&
            future.State.AutoRezRequireSwiftGlobal == true);

        // ---- Case 6: an ancient config climbs BOTH rungs in one pass.
        var ancient = ConfigMigration.Migrate(new State(5, false, true));
        Check("a v5 config climbs the whole ladder",
            ancient.State.Version == ConfigMigration.CurrentVersion);
        Check("...picking up the v7 tankbuster seed", ancient.State.TankbustersBeyondParty);
        Check("...and the v8 per-job carry-over", ancient.State.RequireSwift.WHM);
    }

    // =============================================================================
    // The legacy shadow, through the REAL serializer. A migration that reads a key
    // Newtonsoft never populates is a no-op dressed as a fix.
    // =============================================================================
    private static void LegacyShadowRoundTrip()
    {
        Section("legacy shadow (real Newtonsoft round-trip)");

        // A v1.0.4.173-era saved config, shaped exactly as it sits on disk today.
        const string savedJson = """
            {
              "AutoRez": true,
              "AutoRezRequireSwift": true,
              "AutoRezDPSJobs": true,
              "TankbustersBeyondParty": true
            }
            """;

        var loaded = JsonConvert.DeserializeObject<HealerSettings>(savedJson)!;
        Check("the old AutoRezRequireSwift key still deserialises",
            loaded.AutoRezRequireSwiftLegacy == true,
            loaded.AutoRezRequireSwiftLegacy?.ToString() ?? "null");
        Check("...and the new per-job keys are absent, so initialisers win",
            !loaded.AutoRezRequireSwiftWHM && loaded.AutoRezRequireSwiftRDM);

        // Run the real load path: read -> migrate -> write back.
        var result = ConfigMigration.Migrate(ConfigMigration.Read(7, loaded));
        ConfigMigration.Write(result.State, loaded);

        Check("after migrating, the six carry the old value",
            loaded.AutoRezRequireSwiftWHM && loaded.AutoRezRequireSwiftSCH &&
            loaded.AutoRezRequireSwiftAST && loaded.AutoRezRequireSwiftSGE &&
            loaded.AutoRezRequireSwiftSMN && loaded.AutoRezRequireSwiftBLU);
        Check("...RDM is true", loaded.AutoRezRequireSwiftRDM);
        Check("...and unrelated settings are untouched", loaded.TankbustersBeyondParty);

        var rewritten = JsonConvert.SerializeObject(loaded);
        Check("the dead AutoRezRequireSwift key is no longer written out",
            !rewritten.Contains("\"AutoRezRequireSwift\":", StringComparison.Ordinal),
            rewritten);
        Check("...while the new per-job keys ARE written out",
            rewritten.Contains("\"AutoRezRequireSwiftWHM\":", StringComparison.Ordinal) &&
            rewritten.Contains("\"AutoRezRequireSwiftRDM\":", StringComparison.Ordinal));

        // Reload the rewritten config: the shadow is gone, so the step cannot fire again.
        var reloaded = JsonConvert.DeserializeObject<HealerSettings>(rewritten)!;
        Check("reloading the migrated config leaves the shadow null",
            reloaded.AutoRezRequireSwiftLegacy is null);
        var second = ConfigMigration.Migrate(
            ConfigMigration.Read(ConfigMigration.CurrentVersion, reloaded));
        Check("...so a second pass is a no-op", !second.Changed);

        // NEGATIVE CONTROL for the probe itself: if the serializer round-trip were broken,
        // the assertions above would pass vacuously. Prove a known-present key survives.
        Check("control: a known key survives the same round-trip",
            rewritten.Contains("\"TankbustersBeyondParty\":true", StringComparison.Ordinal),
            rewritten);

        // A config saved by a user who never had the key at all (brand-new install).
        var virgin = JsonConvert.DeserializeObject<HealerSettings>("{}")!;
        Check("a config with no legacy key leaves the shadow null",
            virgin.AutoRezRequireSwiftLegacy is null);
        var virginResult = ConfigMigration.Migrate(ConfigMigration.Read(7, virgin));
        ConfigMigration.Write(virginResult.State, virgin);
        Check("...and the six stay on their false defaults",
            !virgin.AutoRezRequireSwiftWHM && !virgin.AutoRezRequireSwiftBLU);
        Check("...with RDM still true", virgin.AutoRezRequireSwiftRDM);
    }

    // =============================================================================
    // TRAP 1, tested head-on: the resolver must key on JOB, never on raise spell.
    // SCH and SMN both raise with SCH.Resurrection; a switch on the spell fuses them,
    // compiles, builds and passes a smoke test.
    // =============================================================================
    private static void ResolverTruthTable()
    {
        Section("RequireSwiftFor truth table");

        // Every field distinct, so any accidental fusion shows up as a wrong answer.
        var h = new HealerSettings
        {
            AutoRezRequireSwiftWHM = true,
            AutoRezRequireSwiftSCH = true,
            AutoRezRequireSwiftAST = false,
            AutoRezRequireSwiftSGE = true,
            AutoRezRequireSwiftSMN = false,
            AutoRezRequireSwiftBLU = true,
            AutoRezRequireSwiftRDM = false,
        };

        Check("WHM  -> AutoRezRequireSwiftWHM", h.RequireSwiftFor(Job.WHM));
        Check("CNJ  -> the same field as WHM",
            h.RequireSwiftFor(Job.CNJ) == h.RequireSwiftFor(Job.WHM));
        Check("SCH  -> AutoRezRequireSwiftSCH (true)", h.RequireSwiftFor(Job.SCH));
        Check("SMN  -> AutoRezRequireSwiftSMN (false)", !h.RequireSwiftFor(Job.SMN));
        Check("SCH and SMN resolve to DIFFERENT fields - TRAP 1",
            h.RequireSwiftFor(Job.SCH) != h.RequireSwiftFor(Job.SMN));
        Check("AST  -> false", !h.RequireSwiftFor(Job.AST));
        Check("SGE  -> true", h.RequireSwiftFor(Job.SGE));
        Check("BLU  -> true", h.RequireSwiftFor(Job.BLU));
        Check("RDM  -> false", !h.RequireSwiftFor(Job.RDM));

        // Flip SMN alone: only SMN moves. This is the assertion a resSpell-keyed switch fails.
        h.AutoRezRequireSwiftSMN = true;
        Check("flipping SMN alone moves SMN", h.RequireSwiftFor(Job.SMN));
        Check("...and does NOT move SCH", h.RequireSwiftFor(Job.SCH));
        h.AutoRezRequireSwiftSCH = false;
        Check("flipping SCH alone moves SCH", !h.RequireSwiftFor(Job.SCH));
        Check("...and does NOT move SMN", h.RequireSwiftFor(Job.SMN));

        // Flip WHM alone: CNJ follows, because they are deliberately one setting.
        h.AutoRezRequireSwiftWHM = false;
        Check("CNJ follows WHM (intended fusion, same job pre/post-30)",
            !h.RequireSwiftFor(Job.CNJ));

        // Jobs with no raise answer false rather than throwing.
        Check("PLD (no raise) -> false", !h.RequireSwiftFor(Job.PLD));
        Check("GNB (no raise) -> false", !h.RequireSwiftFor(Job.GNB));
        Check("ADV (no job)   -> false", !h.RequireSwiftFor(Job.ADV));

        // An all-true settings object still answers false for a job with no raise - proves the
        // default arm is a real default and not just reading an all-false object.
        var allOn = new HealerSettings
        {
            AutoRezRequireSwiftWHM = true,
            AutoRezRequireSwiftSCH = true,
            AutoRezRequireSwiftAST = true,
            AutoRezRequireSwiftSGE = true,
            AutoRezRequireSwiftSMN = true,
            AutoRezRequireSwiftBLU = true,
            AutoRezRequireSwiftRDM = true,
        };
        Check("control: with every field true, PLD still resolves false",
            !allOn.RequireSwiftFor(Job.PLD));
        Check("control: with every field true, every raiser resolves true",
            allOn.RequireSwiftFor(Job.WHM) && allOn.RequireSwiftFor(Job.CNJ) &&
            allOn.RequireSwiftFor(Job.SCH) && allOn.RequireSwiftFor(Job.AST) &&
            allOn.RequireSwiftFor(Job.SGE) && allOn.RequireSwiftFor(Job.SMN) &&
            allOn.RequireSwiftFor(Job.BLU) && allOn.RequireSwiftFor(Job.RDM));

        // The stubbed Job values must match ECommons' real enum, or this whole table tests a
        // different enum than the one that ships.
        Check("stubbed Job ids match ECommons (CNJ 6, WHM 24, SMN 27, SCH 28)",
            (byte)Job.CNJ == 6 && (byte)Job.WHM == 24 &&
            (byte)Job.SMN == 27 && (byte)Job.SCH == 28);
        Check("...(AST 33, RDM 35, BLU 36, SGE 40)",
            (byte)Job.AST == 33 && (byte)Job.RDM == 35 &&
            (byte)Job.BLU == 36 && (byte)Job.SGE == 40);
    }

    private static void Section(string name)
    {
        Console.WriteLine();
        Console.WriteLine($"-- {name}");
    }

    private static void Check(string what, bool ok, string? actual = null)
    {
        if (ok)
        {
            _pass++;
            Console.WriteLine($"PASS  {what}");
        }
        else
        {
            _fail++;
            Console.WriteLine($"FAIL  {what}" + (actual is null ? "" : $"  (actual: {actual})"));
        }
    }
}
