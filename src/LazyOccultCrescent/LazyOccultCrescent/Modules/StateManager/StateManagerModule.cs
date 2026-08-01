using System;
using Ocelot.Modules;
using Ocelot.Windows;

namespace LazyOccultCrescent.Modules.StateManager;

[OcelotModule(6, -1)]
public class StateManagerModule : Module
{
    public override StateManagerConfig Config
    {
        get => PluginConfig.StateManagerConfig;
    }

    private readonly Panel panel = new();

    private readonly StateMachine StateMachine;

    public event Action<StateManagerModule>? OnEnterIdle
    {
        add => StateMachine.Handlers[State.Idle].OnEnter += value;
        remove => StateMachine.Handlers[State.Idle].OnEnter -= value;
    }

    public event Action<StateManagerModule>? OnExitIdle
    {
        add => StateMachine.Handlers[State.Idle].OnExit += value;
        remove => StateMachine.Handlers[State.Idle].OnExit -= value;
    }

    public event Action<StateManagerModule>? OnEnterInCombat
    {
        add => StateMachine.Handlers[State.InCombat].OnEnter += value;
        remove => StateMachine.Handlers[State.InCombat].OnEnter -= value;
    }

    public event Action<StateManagerModule>? OnExitInCombat
    {
        add => StateMachine.Handlers[State.InCombat].OnExit += value;
        remove => StateMachine.Handlers[State.InCombat].OnExit -= value;
    }

    public event Action<StateManagerModule>? OnEnterInFate
    {
        add => StateMachine.Handlers[State.InFate].OnEnter += value;
        remove => StateMachine.Handlers[State.InFate].OnEnter -= value;
    }

    public event Action<StateManagerModule>? OnExitInFate
    {
        add => StateMachine.Handlers[State.InFate].OnExit += value;
        remove => StateMachine.Handlers[State.InFate].OnExit -= value;
    }

    public event Action<StateManagerModule>? OnEnterInCriticalEncounter
    {
        add => StateMachine.Handlers[State.InCriticalEncounter].OnEnter += value;
        remove => StateMachine.Handlers[State.InCriticalEncounter].OnEnter -= value;
    }

    public event Action<StateManagerModule>? OnExitInCriticalEncounter
    {
        add => StateMachine.Handlers[State.InCriticalEncounter].OnExit += value;
        remove => StateMachine.Handlers[State.InCriticalEncounter].OnExit -= value;
    }

    public StateManagerModule(Plugin plugin, Config config)
        : base(plugin, config)
    {
        StateMachine = new StateMachine(State.Idle, this);
    }

    public override void Update(UpdateContext context)
    {
        StateMachine.Update();
    }

    public override bool RenderMainUi(RenderContext context)
    {
        return panel.Draw(this);
    }

    public State GetState()
    {
        return StateMachine.State;
    }

    public string GetStateText()
    {
        return GetState().ToString();
    }
}
