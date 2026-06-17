#region

using Dalamud.Game.ClientState.Objects.Types;
using GluttonyCombo.CustomComboNS;
using GluttonyCombo.Extensions;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;
using ContentInfo = ECommons.GameHelpers.Content;

#endregion

namespace GluttonyCombo.Combos.PvE.Content;

/// <summary>
///     Encounter-specific safety blocks, for enemies that must not be attacked
///     under certain conditions.
/// </summary>
public static class EncounterSafety
{
    /// <summary>Jagd Doll's Base ID.</summary>
    private const uint JagdDoll = 11338;

    /// <summary>
    ///     Blocks combat actions against The Epic of Alexander (Ultimate)'s
    ///     Jagd Dolls once they are below the 25% HP feed threshold, as any
    ///     further damage risks killing them and wiping the raid.
    /// </summary>
    /// <param name="actionID">
    ///     The action; left as the blocked action.
    /// </param>
    /// <returns>Whether the action should be blocked.</returns>
    public static bool TryGetTEADollBlock(ref uint actionID)
    {
        if (ContentInfo.TerritoryID != 887) // The Epic of Alexander (Ultimate)
            return false;

        return CurrentTarget is not null &&
               CurrentTarget.BaseId is JagdDoll &&
               GetTargetHPPercent() < 25;
    }
}
