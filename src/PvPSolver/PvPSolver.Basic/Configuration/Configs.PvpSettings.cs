using RotationSolver.Decisions;
using System.Collections.Concurrent;
using System.Globalization;

namespace RotationSolver.Basic.Configuration;

/// <summary>
///     One stored defensive setting. A null member means "use the row's default", so a later change of a default
///     reaches everyone who never touched it; null members are not written to the file.
/// </summary>
internal sealed record PvpDefensiveSetting(
    [property: JsonProperty(NullValueHandling = NullValueHandling.Ignore)] bool? Enabled = null,
    [property: JsonProperty(NullValueHandling = NullValueHandling.Ignore)] float? Percent = null);

/// <summary>
///     Fork-only settings of the PvP settings window and the generic defensives. Plain properties only: no
///     <c>[ConditionBool]</c>, <c>[JobConfig]</c> or <c>[JobChoiceConfig]</c> (those generate members whose hint
///     names would collide with the main file), no key of the main file is renamed, and
///     <see cref="Configs.CurrentVersion"/> stays as it is (a mismatch resets every setting). Absent from the file
///     means the default.
/// </summary>
internal partial class Configs
{
    /// <summary>
    ///     The master switch of the generic defensives table (the 20 rows of <see cref="DefensiveTable"/>).
    ///     Off: none of the rows is used, the Recuperate gate does nothing and the six rows that replaced a fixed HP
    ///     number do not fire.
    /// </summary>
    public bool PvpDefensivesMaster { get; set; } = true;

    /// <summary>
    ///     The stored defensive settings, keyed by the action id as text. Only rows the player changed are present.
    /// </summary>
    public ConcurrentDictionary<string, PvpDefensiveSetting> PvpDefensiveSettings { get; set; } = new();

    private static string DefensiveKey(DefensiveRow row) => row.ActionId.ToString(CultureInfo.InvariantCulture);

    private PvpDefensiveSetting? StoredDefensive(DefensiveRow row) =>
        PvpDefensiveSettings.TryGetValue(DefensiveKey(row), out PvpDefensiveSetting? stored) ? stored : null;

    /// <summary>The row's own enabled flag (the row default when never touched). Ignores the master switch.</summary>
    public bool DefensiveEnabled(DefensiveRow row) => DefensiveTable.ResolveEnabled(row, StoredDefensive(row)?.Enabled);

    /// <summary>The row's HP ratio (0 to 1), clamped to the row's floor, or the row default when never touched.</summary>
    public float DefensivePercent(DefensiveRow row) => DefensiveTable.ResolvePercent(row, StoredDefensive(row)?.Percent);

    /// <summary>The row's HP on the 0 to 100 scale, exactly 30 for a stored 0.3 (see <see cref="DefensiveTable.ToPercentPoints"/>).</summary>
    public float DefensivePercentPoints(DefensiveRow row) => DefensiveTable.ToPercentPoints(DefensivePercent(row));

    /// <summary>Whether the row is in force: the master switch is on and the row is enabled.</summary>
    public bool DefensiveActive(DefensiveRow row) => PvpDefensivesMaster && DefensiveEnabled(row);

    /// <summary>Stores the row's enabled flag; null clears it back to the row default.</summary>
    public void SetDefensiveEnabled(DefensiveRow row, bool? enabled) =>
        UpdateDefensive(row, s => s with { Enabled = enabled });

    /// <summary>Stores the row's HP ratio (clamped to the row's floor); null clears it back to the row default.</summary>
    public void SetDefensivePercent(DefensiveRow row, float? percent) =>
        UpdateDefensive(row, s => s with { Percent = percent is { } p && float.IsFinite(p) ? Math.Clamp(p, row.MinPercent, 1f) : null });

    private void UpdateDefensive(DefensiveRow row, Func<PvpDefensiveSetting, PvpDefensiveSetting> change)
    {
        PvpDefensiveSetting next = change(StoredDefensive(row) ?? new PvpDefensiveSetting());
        if (next.Enabled == null && next.Percent == null)
        {
            _ = PvpDefensiveSettings.TryRemove(DefensiveKey(row), out _);
        }
        else
        {
            PvpDefensiveSettings[DefensiveKey(row)] = next;
        }
    }
}
