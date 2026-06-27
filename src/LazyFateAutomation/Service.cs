namespace LazyFateAutomation;

using LazyFateAutomation.Helpers.IPC;
using LazyFateAutomation.Helpers.Services;

public static class Service {
    public static BossModIPC BossMod { get; set; } = null!;
    public static NavmeshIPC Navmesh { get; set; } = null!;
    public static TextAdvanceIpc TextAdvance { get; set; } = null!;
    public static GluttonyComboIPC Gluttony { get; set; } = null!;
    public static Automation Automation { get; set; } = null!;
}
