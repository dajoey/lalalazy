using System;
using LazyOccultCrescent.Data;

namespace LazyOccultCrescent.Modules.MobFarmer;

public interface IRotationPlugin : IDisposable
{
    public void PhantomJobOn(Job? job = null);

    public void PhantomJobOff(Job? job = null);
}
