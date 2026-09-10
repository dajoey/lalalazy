using GluttonyCombo.CustomComboNS;
namespace GluttonyCombo.Combos.PvE;

// Beastmaster (Job.BST = 43) SKELETON (t_f719ab97). NO rotation logic - every preset below
// returns the pressed action unchanged, so the job appears in the feature list and can be
// enabled with zero behavior change. Rotation work is follow-up (parent card t_01325769);
// action/status IDs live in BST_Helper.cs and the gauge overlay in BST_Gauge.cs.
internal partial class BST : Melee
{
    internal class BST_ST_SimpleMode : CustomCombo
    {
        protected internal override Preset Preset => Preset.BST_ST_SimpleMode;

        protected override uint Invoke(uint actionID) => actionID;
    }

    internal class BST_ST_AdvancedMode : CustomCombo
    {
        protected internal override Preset Preset => Preset.BST_ST_AdvancedMode;

        protected override uint Invoke(uint actionID) => actionID;
    }

    internal class BST_AoE_SimpleMode : CustomCombo
    {
        protected internal override Preset Preset => Preset.BST_AoE_SimpleMode;

        protected override uint Invoke(uint actionID) => actionID;
    }
}
