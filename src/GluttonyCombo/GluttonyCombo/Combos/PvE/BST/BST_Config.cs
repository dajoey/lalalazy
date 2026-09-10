namespace GluttonyCombo.Combos.PvE;

// Beastmaster (BST) SKELETON config (t_f719ab97). No user-tunable settings yet - the Draw
// switch is intentionally empty until rotation logic lands. It exists so DebugFile.cs's
// job -> config map (43 => typeof(BST.Config)) and Window/Functions/Presets.cs's per-job
// Config.Draw switch both have a BST arm, the same shape every other job has.
internal partial class BST
{
    internal static class Config
    {
        internal static void Draw(Preset preset)
        {
            switch (preset)
            {
                // No configurable options yet - plumbing only.
                default:
                    break;
            }
        }
    }
}
