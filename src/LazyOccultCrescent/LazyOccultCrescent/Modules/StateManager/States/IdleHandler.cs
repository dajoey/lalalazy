using Ocelot.States;

namespace LazyOccultCrescent.Modules.StateManager.States;

[StateAttribute<State>(State.Idle)]
public class IdleHandler(StateManagerModule module) : BaseHandler(module)
{
    public override State? Handle()
    {
        if (IsInCombat())
        {
            return State.InCombat;
        }

        if (IsInFate())
        {
            return State.InFate;
        }

        if (IsInCriticalEncounter())
        {
            return State.InCriticalEncounter;
        }

        return null;
    }
}
