using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using System;

namespace LazySightseeing;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Lazy Sightseeing";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;

    private const string CommandName = "/lazysight";

    public Configuration Config { get; }
    public AutomationService Automation { get; }
    
    private readonly WindowSystem _windowSystem = new("LazySightseeing");
    private readonly LazySightseeingWindow _mainWindow;

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Inject(this);
        
        // Initialize ECommons
        ECommonsMain.Init(pi, this);

        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        
        Automation = new AutomationService(this);
        _mainWindow = new LazySightseeingWindow(this);
        
        _windowSystem.AddWindow(_mainWindow);

        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi += ToggleWindow;

        Framework.Update += OnFrameworkUpdate;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Lazy Sightseeing control panel."
        });
    }

    public void SaveConfig() => Config.Save();

    private void ToggleWindow() => _mainWindow.IsOpen = !_mainWindow.IsOpen;

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            Automation.Tick();
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "LazySightseeing automation tick failed");
        }
    }

    private void OnCommand(string command, string args)
    {
        if (args.Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            DumpSheets();
            return;
        }
        ToggleWindow();
    }

    private void DumpSheets()
    {
        PluginLog.Information("--- DEBUGGING EXCEL SIGHTSEEING SHEETS ---");
        try
        {
            // Test Adventure sheet
            var advSheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Adventure>();
            if (advSheet != null)
            {
                PluginLog.Information($"Adventure sheet exists! Row count: {advSheet.Count}");
                if (advSheet.Count > 0)
                {
                    var firstRow = advSheet.First();
                    PluginLog.Information("Adventure sheet columns/properties:");
                    foreach (var prop in firstRow.GetType().GetProperties())
                    {
                        PluginLog.Information($"  Property: {prop.Name} (Type: {prop.PropertyType})");
                    }
                }
            }
            else
            {
                PluginLog.Information("Adventure sheet is NULL!");
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Failed to dump sheets");
        }
        PluginLog.Information("--- END DEBUG ---");
    }

    public void Dispose()
    {
        Automation.Stop();
        
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleWindow;
        
        _windowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
        
        ECommonsMain.Dispose();
    }
}
