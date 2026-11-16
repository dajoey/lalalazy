// Fork (Fight Brain 1a): the countdown strip — a small, click-through list of what the code
// forecast says is coming, newest first. CODE FORECASTS ONLY, exactly as the notebook page
// scopes Stage 1a.

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

        // Fork: rounded forecast card (a disposable scope keeps the entry loop as it was); entry source, order and
        // click-through behaviour are unchanged.
        using var card = GluttonyChrome.BeginCard(0, GluttonyChrome.OverlayCardWidth * ImGuiHelpers.GlobalScale);
        // Fork: brand mark and aligned time column for the forecast card.
        GluttonyChrome.ForecastHeader("Fight Brain", $"{entries.Count} code forecasts");
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
            GluttonyChrome.ForecastRow(seconds, label, seconds <= entries[0].Seconds);
        }
        ImGuiHelpers.ScaledDummy(1);
    }
}
