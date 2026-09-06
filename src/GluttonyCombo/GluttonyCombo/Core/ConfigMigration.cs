using System.Collections.Generic;

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
    public const int CurrentVersion = 7;

    /// <summary>Configuration values the ladder can move, in and out.</summary>
    /// <param name="Version">The config's schema version.</param>
    /// <param name="TankbustersBeyondParty">
    ///     Healer setting: also shield the victim of a detected tankbuster outside your party.
    /// </param>
    public readonly record struct State(int Version, bool TankbustersBeyondParty);

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

        version = CurrentVersion;
        var changed = version != state.Version ||
                      tankbustersBeyondParty != state.TankbustersBeyondParty;

        return new Result(
            new State(version, tankbustersBeyondParty), changed, notes);
    }
}
