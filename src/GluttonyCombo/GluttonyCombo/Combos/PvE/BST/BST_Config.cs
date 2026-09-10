using ECommons.ImGuiMethods;
using GluttonyCombo.CustomComboNS.Functions;
using GluttonyCombo.Window.Functions;
using static GluttonyCombo.Window.Functions.UserConfig;
namespace GluttonyCombo.Combos.PvE;

// Beastmaster (BST) rotation config (t_02fe2681). Three new keys only - no migration needed
// (RULES OF THE JOB: "Config: ... new keys only"), drawn under the Advanced Mode preset the
// same way GNB/DNC draw their mitigation/priority radios under their advanced ST preset.
internal partial class BST
{
    internal static class Config
    {
        public static UserBool
            BST_IncludeFamiliarMitigation = new("BST_IncludeFamiliarMitigation"),
            BST_HoldPartingBlowForVantage = new("BST_HoldPartingBlowForVantage", true);

        public static UserInt
            BST_BattlehornSlotOrder = new("BST_BattlehornSlotOrder", 0);

        internal static void Draw(Preset preset)
        {
            switch (preset)
            {
                case Preset.BST_ST_AdvancedMode:
                    DrawAdditionalBoolChoice(BST_IncludeFamiliarMitigation,
                        "Include familiar mitigation Beast Mode",
                        "Uses Beastskin/Vileskin/Scaleskin (Beast Mode mitigation variants) automatically when player HP drops to 80% or below. Off by default.");

                    DrawAdditionalBoolChoice(BST_HoldPartingBlowForVantage,
                        "Hold Parting Blow for Lingering Vantage",
                        "Waits for Lingering Vantage (from Borrow, 1500 potency Parting Blow) before retreating the familiar, instead of retreating the moment its TP is spent (1000 potency).");

                    ImGui.Spacing();
                    ImGuiEx.TextUnderlined("Auto Battlehorn slot order");
                    ImGui.Spacing();
                    DrawHorizontalRadioButton(BST_BattlehornSlotOrder,
                        "Rotate 1 -> 2 -> 3", "Rotates through all three Battlehorn slots so each fresh summon re-arms Borrow and Tempered Release.", 0);
                    DrawHorizontalRadioButton(BST_BattlehornSlotOrder,
                        "Always slot 1", "Always summons the familiar assigned to the first Battlehorn slot.", 1);
                    DrawHorizontalRadioButton(BST_BattlehornSlotOrder,
                        "Always slot 2", "Always summons the familiar assigned to the second Battlehorn slot.", 2);
                    DrawHorizontalRadioButton(BST_BattlehornSlotOrder,
                        "Always slot 3", "Always summons the familiar assigned to the third Battlehorn slot.", 3);
                    break;

                default:
                    break;
            }
        }
    }
}
