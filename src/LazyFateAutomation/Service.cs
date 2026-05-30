namespace LazyFateAutomation;

public static class Service {
    public static BossModIPC BossMod { get; set; } = null!;
    public static NavmeshIPC Navmesh { get; set; } = null!;
    public static TextAdvanceIpc TextAdvance { get; set; } = null!;
    public static Automation Automation { get; set; } = null!;
}
