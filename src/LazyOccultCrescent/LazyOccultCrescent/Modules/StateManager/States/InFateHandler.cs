using Ocelot.States;

namespace LazyOccultCrescent.Modules.StateManager.States;

[StateAttribute<State>(State.InFate)]
public class InFateHandler(StateManagerModule module) : BaseHandler(module)
{
    public override State? Handle()
    {
        if (!IsInFate())
        {
            return IsInCombat() ? State.InCombat : State.Idle;
        }

        return null;
    }
}
