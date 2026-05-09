using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AutoPotion;

internal class ConfigWindow : Window
{
    private readonly Plugin _plugin;

    public ConfigWindow(Plugin plugin) : base("AutoPotion##cfg")
    {
        _plugin = plugin;
        Size = new Vector2(380, 320);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var c = _plugin.Config;
        var changed = false;

        var master = c.MasterEnable;
        if (ImGui.Checkbox("Enabled", ref master)) { c.MasterEnable = master; changed = true; }
        ImGui.Separator();

        var inCombat = c.OnlyInCombat;
        if (ImGui.Checkbox("Only fire in combat", ref inCombat)) { c.OnlyInCombat = inCombat; changed = true; }
        var inDuty = c.OnlyInDuty;
        if (ImGui.Checkbox("Only fire in a duty", ref inDuty)) { c.OnlyInDuty = inDuty; changed = true; }
        ImGui.Separator();

        ImGui.TextUnformatted("Healing potion");
        ImGui.TextDisabled("Highest-tier Hi-Potion / Mega-Potion / Super-Potion / etc. in your inventory.");
        var hpEnable = c.HpPotionEnable;
        if (ImGui.Checkbox("Auto-use HP potion##hp", ref hpEnable)) { c.HpPotionEnable = hpEnable; changed = true; }
        var hpThr = c.HpPotionThreshold;
        if (ImGui.SliderFloat("HP threshold (%)##hpthr", ref hpThr, 1f, 99f, "%.0f"))
        {
            c.HpPotionThreshold = hpThr; changed = true;
        }
        ImGui.Separator();

        ImGui.TextUnformatted("Deep dungeon regen potion");
        ImGui.TextDisabled("Sustaining (PotD) / Eurekan (HoH) / Orthos (EO) / Pilgrim's. Only fires in a deep dungeon.");
        var rgEnable = c.RegenPotionEnable;
        if (ImGui.Checkbox("Auto-use regen potion##rg", ref rgEnable)) { c.RegenPotionEnable = rgEnable; changed = true; }
        var rgThr = c.RegenPotionThreshold;
        if (ImGui.SliderFloat("HP threshold (%)##rgthr", ref rgThr, 1f, 99f, "%.0f"))
        {
            c.RegenPotionThreshold = rgThr; changed = true;
        }

        if (changed) _plugin.SaveConfig();
    }
}
