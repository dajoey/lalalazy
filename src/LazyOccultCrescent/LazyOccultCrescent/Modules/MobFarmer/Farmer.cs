using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Modules.MobFarmer.States;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Reflection;
using Ocelot.Modules;
using Ocelot.States;
using Ocelot.Windows;

namespace LazyOccultCrescent.Modules.MobFarmer;

public class Farmer : IDisposable
{
    public bool Running { get; private set; } = false;

    public Vector3 StartingPoint { get; private set; } = Vector3.Zero;

    public readonly IRotationPlugin RotationPlugin;

    // Ordered by preference, not by Dictionary luck. GluttonyCombo comes first
    // because it is the only rotation plugin that implements the eight phantom
    // jobs added in 7.55 - a Wrath-driven loop does nothing at all in North Horn
    // on Ninja/White Mage/Black Mage/Dragoon/Summoner/Blue Mage/Red Mage/Necromancer.
    private readonly List<(string Plugin, Func<IModule, IRotationPlugin> Factory)> rotationPlugins =
    [
        ("GluttonyCombo", m => new Gluttony(m)),
        ("WrathCombo", m => new Wrath(m)),
    ];

    public readonly StateMachine<FarmerPhase, MobFarmerModule> StateMachine;

    public Farmer(MobFarmerModule module)
    {
        StateMachine = new StateMachine<FarmerPhase, MobFarmerModule>(FarmerPhase.Waiting, module);

        RotationPlugin = new BlankRotationPlugin();
        foreach (var (plugin, factory) in rotationPlugins)
        {
            if (!DalamudReflector.TryGetDalamudPlugin(plugin, out _, false, true))
            {
                continue;
            }

            try
            {
                RotationPlugin = factory(module);
                Svc.Log.Information($"[MobFarmer] rotation provider: {plugin}");
                break;
            }
            catch (Exception ex)
            {
                // An installed-but-unleasable provider must not abort construction;
                // fall through and try the next one, then the blank provider.
                Svc.Log.Warning($"[MobFarmer] {plugin} unavailable: {ex.Message}");
            }
        }
    }

    public void Update(UpdateContext context)
    {
        if (!context.IsForModule<MobFarmerModule>(out var module))
        {
            return;
        }


        if (!Running || !module.Scanner.Mobs.Any())
        {
            return;
        }

        StateMachine.Update();
    }

    public void Draw(RenderContext context)
    {
        if (!context.IsForModule<MobFarmerModule>(out var module))
        {
            return;
        }

        if (!module.Scanner.Mobs.Any())
        {
            return;
        }

        if (!Running && !module.Config.ShouldRenderDebugLinesWhileNotRunning)
        {
            return;
        }

        if (!module.Config.RenderDebugLines)
        {
            return;
        }

        foreach (var mob in module.Scanner.NotInCombat)
        {
            var color = new Vector4(0.9f, 0.1f, 0.1f, 1f);
            if (module.Config.Mobs.Contains((Mob)mob.NameId))
            {
                color = new Vector4(0.9f, 0.1f, 0.9f, 1f);
            }

            context.DrawLine(mob.Position, color);
        }

        foreach (var mob in module.Scanner.InCombat)
        {
            context.DrawLine(mob.Position, new Vector4(0.1f, 0.9f, 0.1f, 1f));
        }
    }

    public void Toggle(MobFarmerModule module)
    {
        Running = !Running;
        StateMachine.Reset();
        if (!Running)
        {
            return;
        }

        StartingPoint = Player.Position;
        RotationPlugin.PhantomJobOff();
    }

    public void Dispose()
    {
        RotationPlugin.Dispose();
    }
}
