using System.Collections.Generic;
using LazyOccultCrescent.Data;
using Ocelot.Config.Attributes;
using Ocelot.Modules;

namespace LazyOccultCrescent.Modules.MobFarmer;

public class MobFarmerConfig : ModuleConfig
{
    [Checkbox]
    [Label("generic.label.enabled")]
    public bool Enabled { get; set; } = true;

    [MultiEnum(typeof(Mob), nameof(MobProvider))]
    [Searchable]
    public List<Mob> Mobs { get; set; } = [];

    [Checkbox] public bool ConsiderSpecialMobs { get; set; } = false;

    [IntRange(1, 28)] public int MaxMobLevel { get; set; } = 28;

    [FloatRange(10f, 1000f)]
    [RangeIndicator(0.9f, 0.1f, 0.6f)]
    public float MaxEuclideanDistance { get; set; } = 75f;

    [Checkbox] public bool ReturnToStartInWaitingPhase { get; set; } = false;

    [FloatRange(10f, 1000f)]
    [RangeIndicator(0.9f, 0.1f, 0.6f)]
    [DependsOn(nameof(ReturnToStartInWaitingPhase))]
    public float MinEuclideanDistanceToReturnHome { get; set; } = 200f;

    [Checkbox] public bool RenderDebugLines { get; set; } = false;

    [Checkbox]
    [DependsOn(nameof(RenderDebugLines))]
    public bool RenderDebugLinesWhileNotRunning { get; set; } = false;

    public bool ShouldRenderDebugLinesWhileNotRunning
    {
        get => IsPropertyEnabled(nameof(RenderDebugLinesWhileNotRunning));
    }

    [Checkbox] public bool ApplyBattleBell { get; set; } = false;

    [FloatRange(0f, 30f)]
    [DependsOn(nameof(ApplyBattleBell))]
    public float MaximumBattleBellWaitTime { get; set; } = 10f;

    [IntRange(0, 20)] public int MinimumMobsToStartLoop { get; set; } = 0;

    [IntRange(1, 20)] public int MinimumMobsToStartFight { get; set; } = 5;
}
