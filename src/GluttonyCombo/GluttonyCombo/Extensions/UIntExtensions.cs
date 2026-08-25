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

    internal static ActionAttackType ActionAttackType(this uint value) => (ActionAttackType)(ActionSheet.TryGetValue(value, out var actSheet) ? actSheet.ActionCategory.RowId : 0);

    internal static float ActionRange(this uint value) =>
        ActionManager.GetActionRange(value);

    internal static bool IsGroundTargeted(this uint value) =>
        ActionSheet.FirstOrDefault(x => x.Value.RowId == value).Value.TargetArea;

    internal static bool IsEnemyTargetable(this uint value) =>
        ActionSheet.FirstOrDefault(x => x.Value.RowId == value).Value.CanTargetHostile;

    internal static bool IsFriendlyTargetable(this uint value) =>
        ActionSheet.FirstOrDefault(x => x.Value.RowId == value).Value.CanTargetAlly;

    internal static string StatusName(this uint value) => ActionAndStatusLocalization.GetStatusName(value);

    internal static string TraitName(this uint value) => ActionAndStatusLocalization.GetTraitName(value);
}

internal static class UShortExtensions
{
    internal static string StatusName(this ushort value) => ActionAndStatusLocalization.GetStatusName(value);

    internal static string TraitName(this ushort value) => ActionAndStatusLocalization.GetTraitName(value);
}