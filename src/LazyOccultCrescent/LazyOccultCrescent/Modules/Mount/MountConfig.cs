using Ocelot.Config.Attributes;
using Ocelot.Modules;
using ExcelMount = Lumina.Excel.Sheets.Mount;

namespace LazyOccultCrescent.Modules.Mount;

public class MountConfig : ModuleConfig
{
    [ExcelSheet(typeof(ExcelMount), nameof(MountProvider))]
    [Searchable]
    [IllegalModeCompatible]
    public uint Mount { get; set; } = 1;

    [Checkbox] [IllegalModeCompatible] public bool MountRoulette { get; set; } = false;
}
