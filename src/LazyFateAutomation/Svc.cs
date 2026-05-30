using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using LazyFateAutomation.Helpers.IPC;

namespace LazyFateAutomation;

public class Svc {
    [PluginService] public static IDalamudPluginInterface Interface { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static ICondition Condition { get; private set; } = null!;
    [PluginService] public static IDataManager Data { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IGameConfig GameConfig { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider Hook { get; private set; } = null!;
    [PluginService] public static IKeyState KeyState { get; private set; } = null!;
    [PluginService] public static IObjectTable Objects { get; private set; } = null!;
    [PluginService] public static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] public static ITextureProvider Texture { get; private set; } = null!;
    [PluginService] public static ICommandManager Commands { get; private set; } = null!;
    [PluginService] public static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] public static IPartyList Party { get; private set; } = null!;
    [PluginService] public static ITargetManager Targets { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static IChatGui Chat { get; private set; } = null!;

    public static IDalamudPluginInterface PluginInterface => Interface;

    public static BossModIPC BossMod => Service.BossMod;
    public static TextAdvanceIpc TextAdvance => Service.TextAdvance;

    public static NavmeshIPC Navmesh { get; private set; } = null!;

    public static void Init(IDalamudPluginInterface pi) {
        pi.Create<Svc>();
        Navmesh = new NavmeshIPC();
    }
}

internal static class LogExtensions {
    public static void Print(this IPluginLog log, string message) => log.Debug($"[LazyFateAutomation] {message}");
    public static void PrintWarning(this IPluginLog log, string message) => log.Warning($"[LazyFateAutomation] {message}");
    public static void PrintError(this IPluginLog log, string message) => log.Error($"[LazyFateAutomation] {message}");
}
