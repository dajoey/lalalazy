using Ocelot.States;

namespace LazyOccultCrescent.Modules.StateManager.States;

[StateAttribute<State>(State.InCriticalEncounter)]
public class InCriticalEncounterHandler(StateManagerModule module) : BaseHandler(module)
{
    public override State? Handle()
    {
        if (!IsInCriticalEncounter())
        {
            return IsInCombat() ? State.InCriticalEncounter : State.Idle;
        }

        return null;
    }
}
