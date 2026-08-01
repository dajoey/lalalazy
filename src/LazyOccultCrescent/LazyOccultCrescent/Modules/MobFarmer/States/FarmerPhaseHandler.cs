using Ocelot.States;

namespace LazyOccultCrescent.Modules.MobFarmer.States;

public abstract class FarmerPhaseHandler(MobFarmerModule module) : StateHandler<FarmerPhase, MobFarmerModule>(module);
