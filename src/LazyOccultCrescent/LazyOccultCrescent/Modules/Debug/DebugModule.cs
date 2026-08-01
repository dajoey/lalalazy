using System.Collections.Generic;
using System.Numerics;
using LazyOccultCrescent.Modules.Debug.Panels;
using ECommons;
using Dalamud.Bindings.ImGui;
using Ocelot.Modules;

namespace LazyOccultCrescent.Modules.Debug;

#if DEBUG_BUILD
[OcelotModule]
#endif
public class DebugModule(Plugin plugin, Config config) : Module(plugin, config)
{
    private readonly List<Panel> panels =
    [
        new TeleporterPanel(),
        new VnavmeshPanel(),
        new FatesPanel(),
        new CriticalEncountersPanel(),
        new ChainManagerPanel(),
        new EnemyPanel(),
        new StatusPanel(),
        new TargetPanel(),
        new ActivityTargetPanel(),
        new TreasureHuntPanel(),
        new CarrotHuntPanel(),
        new JobLevelPanel(),
    ];

    private int selectedPanelIndex = 0;

    public override bool ShouldInitialize
    {
        get => true;
    }

    public override void PostInitialize()
    {
        if (Plugin.Windows.TryGetWindow<DebugWindow>(out var window) && window != null && !window.IsOpen)
        {
            window.Toggle();
        }
    }

    public void DrawPanels()
    {
        // Determine sizes
        var panelWidth = 200f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        ImGui.BeginGroup();

        // Left panel list
        ImGui.BeginChild("PanelList", new Vector2(panelWidth, 0), true);
        for (var i = 0; i < panels.Count; i++)
        {
            var selected = i == selectedPanelIndex;
            if (ImGui.Selectable(panels[i].GetName(), selected))
            {
                selectedPanelIndex = i;
            }
        }

        ImGui.EndChild();

        ImGui.SameLine(0, spacing);

        // Right panel content
        ImGui.BeginGroup();
        ImGui.BeginChild("PanelContent", new Vector2(0, 0), false);
        panels[selectedPanelIndex].Render(this);
        ImGui.EndChild();
        ImGui.EndGroup();

        ImGui.EndGroup();
    }

    public override void Update(UpdateContext context)
    {
        panels.Each(p => p.Update(this));
    }

    public override void OnTerritoryChanged(uint id)
    {
        panels.Each(p => p.OnTerritoryChanged(id, this));
    }
}
