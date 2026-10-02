using System.Collections.Generic;
using System.Linq;

namespace Lalalazy.Crucible;

/// <summary> One guide counter as the need model reads it (generated, see <c>BST_CrucibleGuideNeeds.Generated.cs</c>). </summary>
internal readonly record struct GuideNeedRow(int Board, int Battle, CrucibleNeeds Kind, CrucibleNeedTier Tier, string What, string[] Src);

/// <summary>
///     The fight guide's ability counters for hosts that ship no guide file (GluttonyCombo). Installed as
///     <see cref="CrucibleNeedModel.Extras"/> it makes the rotation read the same Required / Useful needs per battle as
///     LazyCrucible's picker and guide window (task tasks-20261002-crucible-horn-picks-by-needed-abilities-01: the research's
///     counters were visible to the picker but not to the rotation, so a need only the guide knew was never answered in the fight).
/// </summary>
internal static partial class BST_CrucibleGuideNeeds
{
    /// <summary> The battle's guide counters as needs (none when the guide has none for it). </summary>
    public static IReadOnlyList<CrucibleAbilityNeed> NeedsOf(int board, int battle) =>
        Rows.Where(r => r.Board == board && r.Battle == battle)
            .Select(r => new CrucibleAbilityNeed(r.Kind, r.Tier, r.What, r.Src))
            .ToList();

    /// <summary> Make the guide's counters part of every need model (clears the cached models). </summary>
    public static void Install() => CrucibleNeedModel.Extras = NeedsOf;
}
