using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.EzHookManager;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lalalazy.Changelog;
using System;
using System.Collections.Generic;

namespace LazySkywardTracker;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Lazy Skyward Tracker";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    private const string CommandName = "/lazysky";

    public Configuration Config { get; }
    public InventoryScanner Scanner { get; private set; } = null!;
    
    private readonly WindowSystem _windowSystem = new("LazySkywardTracker");
    private readonly TrackerWindow _mainWindow;
    private readonly ChangelogGate _changelog;

    public static readonly uint[] AchievementIds = [2491, 2494, 2497, 2500, 2503, 2506, 2509, 2512, 2515, 2518, 2521];

    public static readonly Dictionary<uint, (string Job, string AchName)> SkywardAchievements = new()
    {
        { 2491, ("Carpenter", "Skyward Saw III") },
        { 2494, ("Blacksmith", "Skyward Smithy III") },
        { 2497, ("Armorer", "Skyward Hammer III") },
        { 2500, ("Goldsmith", "Skyward Gemstone III") },
        { 2503, ("Leatherworker", "Skyward Knife III") },
        { 2506, ("Weaver", "Skyward Needle III") },
        { 2509, ("Alchemist", "Skyward Science III") },
        { 2512, ("Culinarian", "Skyward Skillet III") },
        { 2515, ("Miner", "Skyward Sledgehammer III") },
        { 2518, ("Botanist", "Skyward Scythe III") },
        { 2521, ("Fisher", "Skyward Rod III") }
    };

    public static readonly Dictionary<uint, (uint Current, uint Max)> ProgressCache = new();

    private unsafe delegate void ReceiveAchievementProgressDelegate(Achievement* achievement, uint id, uint current, uint max);
    private static EzHook<ReceiveAchievementProgressDelegate>? _receiveAchievementProgressHook;

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Inject(this);
        
        // Initialize ECommons
        ECommonsMain.Init(pi, this);

        // Read BEFORE anything saves the config: tells the changelog gate "update" from "fresh install".
        var existingInstall = pi.ConfigFile.Exists;
        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        Scanner = new InventoryScanner(DataManager);
        
        _mainWindow = new TrackerWindow(this);
        _windowSystem.AddWindow(_mainWindow);

        // Shared "What's new" popup (repo standing rule): shows this plugin's CHANGELOG once after an update.
        _changelog = new ChangelogGate(new ChangelogGate.Options
        {
            PluginAssembly = typeof(Plugin).Assembly,
            DisplayName = "Lazy Skyward Tracker",
            ChangelogPath = "src/LazySkywardTracker/CHANGELOG.md",
            Framework = Framework,
            ClientState = ClientState,
            Condition = Condition,
            Log = PluginLog,
            Windows = _windowSystem,
            ExistingInstall = existingInstall,
            SeenStore = new DelegateSeenStore(
                () => Config.LastSeenChangelogVersion,
                v => { Config.LastSeenChangelogVersion = v; Config.Save(); }),
        });

        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi += ToggleWindow;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Lazy Skyward Tracker control panel. /lazysky changelog shows what's new."
        });

        // Initialize Hook
        try
        {
            unsafe
            {
                _receiveAchievementProgressHook = new EzHook<ReceiveAchievementProgressDelegate>(
                    "C7 81 ?? ?? ?? ?? ?? ?? ?? ?? 89 91 ?? ?? ?? ?? 44 89 81", 
                    ReceiveAchievementProgressDetour
                );
                _receiveAchievementProgressHook.Enable();
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Failed to initialize ReceiveAchievementProgress hook");
        }

        ClientState.Login += OnLogin;
        
        if (ClientState.IsLoggedIn)
        {
            RequestAllProgress();
        }
    }

    private void OnLogin()
    {
        RequestAllProgress();
    }

    public void RequestAllProgress()
    {
        try
        {
            unsafe
            {
                var achievement = Achievement.Instance();
                if (achievement != null)
                {
                    foreach (var id in AchievementIds)
                    {
                        achievement->RequestAchievementProgress(id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Failed to request achievement progress");
        }
    }

    public static unsafe bool IsAchievementCompleted(uint achievementId)
    {
        try
        {
            var achievement = Achievement.Instance();
            if (achievement == null) return false;
            
            // Check completed achievements bitfield at offset 0x0C
            byte* completedAchievementsPtr = (byte*)achievement + 0x0C;
            return (completedAchievementsPtr[achievementId >> 3] & (1 << (int)(achievementId & 7))) != 0;
        }
        catch
        {
            return false;
        }
    }

    private unsafe void ReceiveAchievementProgressDetour(Achievement* achievement, uint id, uint current, uint max)
    {
        try
        {
            if (SkywardAchievements.ContainsKey(id))
            {
                ProgressCache[id] = (current, max);
                PluginLog.Debug($"LazySkywardTracker: ID={id} progress={current}/{max}");
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Error in ReceiveAchievementProgressDetour");
        }
        _receiveAchievementProgressHook?.Original(achievement, id, current, max);
    }

    private void ToggleWindow() => _mainWindow.IsOpen = !_mainWindow.IsOpen;

    private void OnCommand(string command, string args)
    {
        var a = args.Trim();
        if (a.Equals("changelog", StringComparison.OrdinalIgnoreCase) || a.Equals("whatsnew", StringComparison.OrdinalIgnoreCase))
        {
            _changelog.ShowNow();
            return;
        }
        ToggleWindow();
    }

    public void Dispose()
    {
        ClientState.Login -= OnLogin;
        
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleWindow;
        
        _changelog.Dispose();
        _windowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);

        try
        {
            _receiveAchievementProgressHook?.Disable();
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Failed to disable hook");
        }
        
        ECommonsMain.Dispose();
    }
}
