// Fork (Fight Brain 1a): the countdown strip — a small, click-through list of what the code
// forecast says is coming, newest first. CODE FORECASTS ONLY, exactly as the notebook page
// scopes Stage 1a: no Jev pick is shown until a question has passed its grade.

using Dalamud.Interface.Utility;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using GluttonyCombo.Services;
using System;
using System.Linq;
using System.Numerics;

namespace GluttonyCombo.Window;

internal class FightBrainStrip : Dalamud.Interface.Windowing.Window
{
    internal FightBrainStrip() : base(
        "###FightBrainStrip",
        ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize |
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNav,
        true)
    {
        IsOpen = true;
        RespectCloseHotkey = false;
        Position = new Vector2(30, 240);
        PositionCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var cfg = Service.Configuration.RotationConfig.FightBrain;
        if (!cfg.Enabled || !cfg.ShowStrip || Player.Object is null || !AutoRotation.FightBrain.ForecastSeen)
            return;

        var entries = AutoRotation.FightBrain.StripEntries();
        if (entries.Count == 0)
            return;

        ImGui.TextUnformatted("fight brain");
        foreach (var (hit, seconds) in entries)
        {
            var label = hit.Kind switch
            {
                AutoRotation.FightBrainHitKind.Raidwide => "raidwide",
                AutoRotation.FightBrainHitKind.Tankbuster => "tankbuster",
                AutoRotation.FightBrainHitKind.Knockback => "knockback",
                AutoRotation.FightBrainHitKind.CastHit => hit.Source,
                AutoRotation.FightBrainHitKind.Downtime => "downtime",
                _ => hit.Kind.ToString(),
            };
            ImGui.TextUnformatted($"{seconds,5:F1}s  {label}");
        }
        ImGuiHelpers.ScaledDummy(1);
    }
}
