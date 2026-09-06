using GluttonyCombo.Core;
using State = GluttonyCombo.Core.ConfigMigration.State;

namespace GluttonyCombo.ConfigMigrateHarness;

/// <summary>
///     Offline assertions on the configuration migration ladder (GluttonyCombo v1.0.4.173).
///     Compiles the real <see cref="ConfigMigration" /> - no Dalamud, no game - so the v6-&gt;v7
///     step is proven before it touches anyone's saved settings.
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        // ---------------------------------------------------------------------------
        // 1. Joey's real config, read live from omasky on 2026-09-05:
        //    Version 6, HealerSettings.TankbustersBeyondParty = false (serialized, which is
        //    exactly why a default flip alone would be a silent no-op for him).
        // ---------------------------------------------------------------------------
        var live = new State(Version: 6, TankbustersBeyondParty: false);
        var r = ConfigMigration.Migrate(live);

        Check("existing install is migrated", r.Changed);
        Check("...to the current schema version",
            r.State.Version == ConfigMigration.CurrentVersion, r.State.Version.ToString());
        Check("...and the setting is seeded ON", r.State.TankbustersBeyondParty);
        Check("...with exactly one note explaining it", r.Notes.Count == 1, r.Notes.Count.ToString());
        Check("...the note names the setting and how to undo it",
            r.Notes[0].Contains("Also shield tankbusters outside your party", StringComparison.Ordinal) &&
            r.Notes[0].Contains("Untick", StringComparison.Ordinal),
            r.Notes.Count > 0 ? r.Notes[0] : "(none)");

        // ---------------------------------------------------------------------------
        // 2. Idempotence. This is the property that matters: running the ladder again over
        //    its own output must do nothing at all, or every plugin load re-applies it.
        // ---------------------------------------------------------------------------
        var again = ConfigMigration.Migrate(r.State);
        Check("re-running the ladder changes nothing", !again.Changed);
        Check("...and emits no notes", again.Notes.Count == 0, again.Notes.Count.ToString());
        Check("...and leaves the value alone", again.State.TankbustersBeyondParty);

        // ---------------------------------------------------------------------------
        // 3. THE NEGATIVE CONTROL, and the reason this harness exists. A user who takes the
        //    seeded setting back OFF must stay off forever. A ladder that re-asserted the
        //    value on every load would pass every test above and be a bug that argues with
        //    the user - the seed is a one-time nudge, not a policy.
        // ---------------------------------------------------------------------------
        var userTurnedItOff = new State(ConfigMigration.CurrentVersion, TankbustersBeyondParty: false);
        var afterOptOut = ConfigMigration.Migrate(userTurnedItOff);
        Check("a user who turns the setting back off is NOT overridden",
            !afterOptOut.State.TankbustersBeyondParty);
        Check("...and that is recorded as no change", !afterOptOut.Changed);

        // ---------------------------------------------------------------------------
        // 4. A fresh install starts at CurrentVersion and skips the ladder entirely. Its
        //    default comes from the field initialiser, not from a migration.
        // ---------------------------------------------------------------------------
        var fresh = new State(ConfigMigration.CurrentVersion, TankbustersBeyondParty: true);
        var freshResult = ConfigMigration.Migrate(fresh);
        Check("fresh install is not migrated", !freshResult.Changed);
        Check("...and keeps its value", freshResult.State.TankbustersBeyondParty);

        // ---------------------------------------------------------------------------
        // 5. Older configs climb the whole ladder; a config from the future is left alone
        //    rather than being downgraded by an older build.
        // ---------------------------------------------------------------------------
        foreach (var v in new[] { 0, 1, 5, 6 })
        {
            var old = ConfigMigration.Migrate(new State(v, false));
            Check($"config v{v} climbs to v{ConfigMigration.CurrentVersion}",
                old.State.Version == ConfigMigration.CurrentVersion && old.State.TankbustersBeyondParty,
                $"v{old.State.Version} tbp={old.State.TankbustersBeyondParty}");
        }

        var future = new State(99, false);
        var futureResult = ConfigMigration.Migrate(future);
        Check("a config from the future is left untouched",
            !futureResult.Changed && futureResult.State.Version == 99 &&
            !futureResult.State.TankbustersBeyondParty,
            $"v{futureResult.State.Version} changed={futureResult.Changed}");

        // ---------------------------------------------------------------------------
        // 6. An existing install that had ALREADY turned the setting on gets no note - there
        //    is nothing to tell them, the version just moves up.
        // ---------------------------------------------------------------------------
        var alreadyOn = ConfigMigration.Migrate(new State(6, true));
        Check("an install that already had it on is version-bumped", alreadyOn.Changed);
        Check("...but is not told about a change that did not happen",
            alreadyOn.Notes.Count == 0, alreadyOn.Notes.Count.ToString());

        Console.WriteLine();
        Console.WriteLine($"{_pass} passed, {_fail} failed");
        if (_fail == 0) Console.WriteLine("OK");
        return _fail == 0 ? 0 : 1;
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
