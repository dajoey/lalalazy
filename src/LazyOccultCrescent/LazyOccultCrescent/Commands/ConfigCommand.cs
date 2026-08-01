using System.Collections.Generic;
using Ocelot.Commands;
using Ocelot.Modules;

namespace LazyOccultCrescent.Commands;

[OcelotCommand]
public class ConfigCommand(Plugin plugin) : OcelotCommand
{
    protected override string Command
    {
        get => "/lazyoccultcfg";
    }

    protected override string Description
    {
        get => @"
Opens LazyOccultCrescent config ui
 - /lazyoccultcfg : Opens the config ui
--------------------------------
".Trim();
    }

    protected override IReadOnlyList<string> Aliases
    {
        get => ["/lazyoccultc", "/lazyoccfg", "/lazyocc", "/lazyoccultcrescentconfig"];
    }


    public override void Execute(string command, string arguments)
    {
        plugin.Windows.ToggleConfigUI();
    }
}
