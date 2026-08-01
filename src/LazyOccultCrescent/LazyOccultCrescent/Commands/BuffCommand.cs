using LazyOccultCrescent.Modules.Buff;
using Ocelot.Commands;
using Ocelot.Modules;

namespace LazyOccultCrescent.Commands;

[OcelotCommand]
public class BuffCommand(Plugin plugin) : OcelotCommand
{
    protected override string Command
    {
        get => "/lazyoccultbuff";
    }

    protected override string Description
    {
        get => "";
    }


    public override void Execute(string command, string arguments)
    {
        plugin.Modules.GetModule<BuffModule>().BuffManager.QueueBuffs();
    }
}
