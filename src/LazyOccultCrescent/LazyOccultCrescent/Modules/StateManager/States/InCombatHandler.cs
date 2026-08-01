using Ocelot.States;

namespace LazyOccultCrescent.Modules.StateManager.States;

[StateAttribute<State>(State.InCombat)]
public class InCombatHandler(StateManagerModule module) : BaseHandler(module)
{
    public override State? Handle()
    {
        if (IsInFate())
        {
            return State.InFate;
        }

        if (IsInCriticalEncounter())
        {
            return State.InCriticalEncounter;
        }

        if (!IsInCombat())
        {
            return State.Idle;
        }

        return null;
    }
}
