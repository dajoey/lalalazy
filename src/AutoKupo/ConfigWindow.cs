using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace AutoKupo;

internal sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration config;

    public ConfigWindow(Configuration config)
        : base("AutoKupo Configuration")
    {
        this.config = config;
        Size = new Vector2(400, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            config.Enabled = enabled;
            config.Save();
        }

        ImGui.Separator();

        var maxDist = config.MaxDistance;
        ImGui.TextUnformatted("Max distance to Lizbeth (yalms):");
        if (ImGui.SliderFloat("##maxDist", ref maxDist, 1f, 15f, $"{maxDist:F1}"))
        {
            config.MaxDistance = maxDist;
            config.Save();
        }

        var maxIter = config.MaxIterations;
        ImGui.TextUnformatted("Max cards per session:");
        if (ImGui.SliderInt("##maxIter", ref maxIter, 1, 500))
        {
            config.MaxIterations = maxIter;
            config.Save();
        }

        var talkDelay = config.TalkClickDelayMs;
        ImGui.TextUnformatted("Dialog click delay (ms):");
        if (ImGui.SliderInt("##talkDelay", ref talkDelay, 50, 2000, $"{talkDelay}ms"))
        {
            config.TalkClickDelayMs = talkDelay;
            config.Save();
        }

        ImGui.Separator();

        var autoInteract = config.AutoInteract;
        if (ImGui.Checkbox("Auto-interact with Lizbeth", ref autoInteract))
        {
            config.AutoInteract = autoInteract;
            config.Save();
        }

        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Commands:");
        ImGui.TextUnformatted("/autokupo      - Toggle on/off");
        ImGui.TextUnformatted("/autokupo stop - Force stop");
    }
}
