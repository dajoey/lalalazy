using System.Collections.Generic;
using GluttonyCombo.AutoRotation;

namespace GluttonyCombo.Core;

/// <summary>
///     Pure, Dalamud-free configuration migration ladder.
/// </summary>
/// <remarks>
///     <para>
///         Lives apart from <see cref="Configuration" /> - which is saturated with Dalamud and
///         ImGui attributes - so <c>tests/GluttonyCombo.ConfigMigrateHarness</c> can compile and
///         replay the exact ladder that ships. The property worth proving is not "the flag ends
///         up true"; it is that the ladder applies <b>once</b> and never fights a user who
///         later turns the setting back off.
///     </para>
///     <para>
///         Why a ladder is needed at all: Newtonsoft serialises every property, so an existing
///         install deserialises its saved value <i>over</i> the field initialiser. Changing a
///         <c>= false</c> to <c>= true</c> therefore reaches new installs only and is a silent
///         no-op for everyone who already has the plugin.
///     </para>
/// </remarks>
internal static class ConfigMigration
{
    /// <summary>
    ///     Configuration schema version written by this build.
    /// </summary>
    /// <remarks>
    ///     Must match <see cref="Configuration.Version" />'s initialiser, so a fresh install
    ///     starts at the top of the ladder and skips every step.
    /// </remarks>
    public const int CurrentVersion = 8;

    /// <summary>Configuration values the ladder can move, in and out.</summary>
    /// <param name="Version">The config's schema version.</param>
    /// <param name="TankbustersBeyondParty">
    ///     Healer setting: also shield the victim of a detected tankbuster outside your party.
    /// </param>
    /// <param name="AutoRezRequireSwiftGlobal">
    ///     The v7-and-earlier single global "Require Swiftcast/Dualcast" auto-rez toggle, read
    ///     back off the JSON by <see cref="HealerSettings.AutoRezRequireSwiftLegacy" />. Null
    ///     when the saved config never had the key (a fresh install), which is the signal to
    ///     leave the per-job fields on their own defaults.
    /// </param>
    /// <param name="RequireSwift">The seven per-job replacements, in and out.</param>
    public readonly record struct State(
        int Version,
        bool TankbustersBeyondParty,
        bool? AutoRezRequireSwiftGlobal = null,
        PerJobRequireSwift RequireSwift = default);

    /// <summary>
    ///     The seven per-job "Require Swiftcast/Dualcast before auto-rezzing" flags.
    /// </summary>
    /// <remarks>
    ///     Keyed by JOB, never by raise spell: SCH and SMN share
    ///     <c>SCH.Resurrection</c>, so keying on the spell would silently fuse them into one
    ///     setting. CNJ and WHM share <c>WHM.Raise</c> and are deliberately one field - same
    ///     job either side of level 30.
    /// </remarks>
    public readonly record struct PerJobRequireSwift(
        bool WHM,
        bool SCH,
        bool AST,
        bool SGE,
        bool SMN,
        bool BLU,
        bool RDM);

    /// <summary>Outcome of a migration pass.</summary>
    /// <param name="State">The values to write back.</param>
    /// <param name="Changed">Whether anything actually moved.</param>
    /// <param name="Notes">One human-readable line per applied step, for the log.</param>
    public readonly record struct Result(State State, bool Changed, IReadOnlyList<string> Notes);

    /// <summary>
    ///     Brings <paramref name="state" /> up to <see cref="CurrentVersion" />, applying each
    ///     step at most once. Idempotent: running it again over its own output changes nothing.
    /// </summary>
    public static Result Migrate(State state)
    {
        var notes = new List<string>();
        var version = state.Version;
        var tankbustersBeyondParty = state.TankbustersBeyondParty;
        var requireSwift = state.RequireSwift;
        var carriedGlobal = state.AutoRezRequireSwiftGlobal;

        // A config from the future is left completely alone - downgrading a user's settings is
        // worse than running an old build against a new config.
        if (version >= CurrentVersion)
            return new Result(state, false, notes);

        // ---- v7: seed "Also shield tankbusters outside your party" ON for existing installs.
        // The setting shipped OFF in v1.0.4.171, but the behaviour is what was actually asked
        // for; a default flip alone would never reach anyone who already had the plugin.
        if (version < 7)
        {
            if (!tankbustersBeyondParty)
            {
                tankbustersBeyondParty = true;
                notes.Add(
                    "Auto-Rotation > Healing Settings > \"Also shield tankbusters outside " +
                    "your party\" has been turned ON by this update, so detected tankbusters " +
                    "on alliance members and trusted NPCs are shielded too. Untick it there " +
                    "to go back to party-only.");
            }
        }

        // ---- v8: split the single "Require Swiftcast/Dualcast" auto-rez toggle into one
        // checkbox per raising job. Carry the user's saved global value onto the six jobs the
        // old toggle actually governed, so nobody's behaviour changes on update.
        //
        // RDM is deliberately NOT seeded from the global. Before this version RDM's
        // requirement was hardcoded ON - it only ever fired Verraise at zero cast time and
        // that single tick could not turn it off - so `true` is what "no change on day one"
        // means for RDM. Seeding it from a global that defaults to false would silently start
        // hard-casting a 10s Verraise for every existing user; the whole point of the new RDM
        // row is that it is opt-OUT, by hand, by someone who wants that.
        if (version < 8)
        {
            // RDM is asserted, not inherited. Any config below v8 predates the per-job fields,
            // so its RDM value can only have come from the field initialiser - but a ladder
            // that depends on an initialiser it does not control is one refactor away from
            // silently flipping RDM to false and hard-casting Verraise for everyone. Setting it
            // here makes the guarantee local to the step that owns it.
            requireSwift = requireSwift with { RDM = true };

            if (carriedGlobal is { } saved && saved)
            {
                requireSwift = requireSwift with
                {
                    WHM = true,
                    SCH = true,
                    AST = true,
                    SGE = true,
                    SMN = true,
                    BLU = true,
                };
                notes.Add(
                    "Auto-Rotation > Healing Settings > \"Require Swiftcast/Dualcast\" for " +
                    "auto-resurrect is now one checkbox per job. Your existing setting was ON, " +
                    "so it has been carried onto WHM/CNJ, SCH, AST, SGE, SMN and BLU. RDM is " +
                    "unchanged and still requires an instant cast; untick RDM there if you " +
                    "want it to hard-cast Verraise.");
            }

            // The shadow has served its purpose either way. Nulling it stops the dead key
            // being written back out, so the step can never run a second time.
            carriedGlobal = null;
        }

        version = CurrentVersion;
        var changed = version != state.Version ||
                      tankbustersBeyondParty != state.TankbustersBeyondParty ||
                      requireSwift != state.RequireSwift ||
                      carriedGlobal != state.AutoRezRequireSwiftGlobal;

        return new Result(
            new State(version, tankbustersBeyondParty, carriedGlobal, requireSwift),
            changed,
            notes);
    }

    #region Adapters

    // The glue between the saved settings object and the pure ladder above. It lives here,
    // Dalamud-free and compiled by tests/GluttonyCombo.ConfigMigrateHarness, so the wiring is
    // asserted by the harness rather than re-implemented inside it. A migration that is proven
    // in the abstract and mis-wired in GluttonyCombo.cs is still a broken migration.

    /// <summary>Reads the migratable values out of a just-loaded configuration.</summary>
    public static State Read(int version, HealerSettings healer) => new(
        version,
        healer.TankbustersBeyondParty,
        healer.AutoRezRequireSwiftLegacy,
        new PerJobRequireSwift(
            healer.AutoRezRequireSwiftWHM,
            healer.AutoRezRequireSwiftSCH,
            healer.AutoRezRequireSwiftAST,
            healer.AutoRezRequireSwiftSGE,
            healer.AutoRezRequireSwiftSMN,
            healer.AutoRezRequireSwiftBLU,
            healer.AutoRezRequireSwiftRDM));

    /// <summary>Writes a migrated <see cref="State" /> back onto the live settings object.</summary>
    public static void Write(State state, HealerSettings healer)
    {
        healer.TankbustersBeyondParty = state.TankbustersBeyondParty;
        healer.AutoRezRequireSwiftLegacy = state.AutoRezRequireSwiftGlobal;
        healer.AutoRezRequireSwiftWHM = state.RequireSwift.WHM;
        healer.AutoRezRequireSwiftSCH = state.RequireSwift.SCH;
        healer.AutoRezRequireSwiftAST = state.RequireSwift.AST;
        healer.AutoRezRequireSwiftSGE = state.RequireSwift.SGE;
        healer.AutoRezRequireSwiftSMN = state.RequireSwift.SMN;
        healer.AutoRezRequireSwiftBLU = state.RequireSwift.BLU;
        healer.AutoRezRequireSwiftRDM = state.RequireSwift.RDM;
    }

    #endregion
}
