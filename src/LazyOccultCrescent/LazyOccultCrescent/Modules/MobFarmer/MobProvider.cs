using LazyOccultCrescent.Data;
using Ocelot.Config.Handlers;

namespace LazyOccultCrescent.Modules.MobFarmer;

public class MobProvider : EnumProvider<Mob>
{
    public override string GetLabel(Mob mob)
    {
        return MobData.GetName(mob);
    }
}
