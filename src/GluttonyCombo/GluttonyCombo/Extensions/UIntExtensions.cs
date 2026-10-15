using FFXIVClientStructs.FFXIV.Client.Game;
using System.Linq;
using GluttonyCombo.CustomComboNS.Functions;
using static GluttonyCombo.Data.ActionWatching;
using static GluttonyCombo.Window.Text;

namespace GluttonyCombo.Extensions;

internal static class UIntExtensions
{
    internal static bool LevelChecked(this uint value) => CustomComboFunctions.ActionLearned(value);

    internal static bool TraitLevelChecked(this uint value) => CustomComboFunctions.TraitLevelChecked(value);

    internal static string ActionName(this uint value) => ActionAndStatusLocalization.GetActionName(value);

    internal static string ItemName(this uint value) => ActionAndStatusLocalization.GetItemName(value);

    /// <summary>
    ///     The action's category, as the auto-rotation sender reads it: a Weaponskill is sent when the GCD is ready, an ability when the animation
    ///     lock is. The four Beastmaster TP axes are category Weaponskill but recast group 16 (the GCD is 58), so they are abilities here
    ///     (r4 rot-32): recorded play shows the game takes them in a weave window and the next GCD then lands on time.
    /// </summary>
    internal static ActionAttackType ActionAttackType(this uint value) =>
        global::GluttonyCombo.Combos.PvE.BST_RotationLogic.OffGcdAxe(value)
            ? global::GluttonyCombo.Data.ActionWatching.ActionAttackType.Ability
            : (ActionAttackType)(ActionSheet.TryGetValue(value, out var actSheet) ? actSheet.ActionCategory.RowId : 0);

    internal static float ActionRange(this uint value) =>
        ActionManager.GetActionRange(value);

    internal static bool IsGroundTargeted(this uint value) =>
        ActionSheet.FirstOrDefault(x => x.Value.RowId == value).Value.TargetArea;

    internal static bool IsEnemyTargetable(this uint value) =>
        ActionSheet.FirstOrDefault(x => x.Value.RowId == value).Value.CanTargetHostile;

    internal static bool IsFriendlyTargetable(this uint value) =>
        ActionSheet.FirstOrDefault(x => x.Value.RowId == value).Value.CanTargetAlly;

    internal static string TraitName(this uint value) => ActionAndStatusLocalization.GetTraitName(value);
}

internal static class UShortExtensions
{
    internal static string StatusName(this ushort value) => ActionAndStatusLocalization.GetStatusName(value);

    internal static string TraitName(this ushort value) => ActionAndStatusLocalization.GetTraitName(value);
}