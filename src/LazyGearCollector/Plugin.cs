using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using Lalalazy.Changelog;

namespace LazyGearCollector;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Lazy Gear Collector";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    private const string CommandName = "/lazygear";

    public Configuration Config { get; }
    public ShopGraph Shops { get; }
    public OwnershipScanner Ownership { get; }
    public Planner Planner { get; }
    public List<GearCollection> Collections { get; } = new();

    private readonly WindowSystem _windowSystem = new("LazyGearCollector");
    private readonly CollectorWindow _window;
    private readonly ChangelogGate _changelog;
    private DateTime _nextSnapshotSweep = DateTime.MinValue;
    private uint[] _trackedItemIds = [];

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Inject(this);
        ECommonsMain.Init(pi, this);

        // Read BEFORE anything saves the config: tells the changelog gate "update" from "fresh install".
        var existingInstall = pi.ConfigFile.Exists;
        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();

        Shops = new ShopGraph(DataManager);
        Ownership = new OwnershipScanner(Config);
        Planner = new Planner(Ownership, Shops);

        BuildCollections();

        _window = new CollectorWindow(this);
        _windowSystem.AddWindow(_window);

        // Shared "What's new" popup (repo standing rule): shows this plugin's CHANGELOG once after an update.
        _changelog = new ChangelogGate(new ChangelogGate.Options
        {
            PluginAssembly = typeof(Plugin).Assembly,
            DisplayName = "Lazy Gear Collector",
            ChangelogPath = "src/LazyGearCollector/CHANGELOG.md",
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
        Framework.Update += OnFrameworkUpdate;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Lazy Gear Collector. /lazygear changelog shows what's new.",
        });
    }

    /// <summary>
    /// Register the collections to track. Each entry is a family name prefix as it appears in the
    /// Item sheet - the provider works out the roles, slots, tiers and prices on its own.
    /// </summary>
    private void BuildCollections()
    {
        var provider = new FamilyProvider(DataManager, Shops);

        var registrations = new (string Id, string Prefix, string Display, string Note)[]
        {
            ("phantom-vision", "Phantom Vision", "Phantom Vision (North Horn)",
             "Occult Crescent: North Horn. Base pieces from the expedition antiquarian, upgrades from the expedition armorer."),
        };

        foreach (var (id, prefix, display, note) in registrations)
        {
            try
            {
                var collection = provider.Build(id, prefix, display, note);
                if (collection != null) Collections.Add(collection);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"Failed to build collection '{id}'");
            }
        }

        _trackedItemIds = Collections
            .SelectMany(c => c.Pieces.SelectMany(p => p.Tiers.Select(t => t.ItemId))
                .Concat(c.Currencies))
            .Distinct()
            .ToArray();

        PluginLog.Info($"LazyGearCollector: tracking {_trackedItemIds.Length} item ids across {Collections.Count} collection(s)");
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!ClientState.IsLoggedIn) return;
        if (DateTime.UtcNow < _nextSnapshotSweep) return;
        _nextSnapshotSweep = DateTime.UtcNow.AddSeconds(5);

        try { Ownership.RefreshOpportunisticSnapshots(_trackedItemIds); }
        catch (Exception ex) { PluginLog.Error(ex, "Snapshot sweep failed"); }
    }

    private void ToggleWindow() => _window.IsOpen = !_window.IsOpen;

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
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleWindow;
        _changelog.Dispose();
        _windowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
        ECommonsMain.Dispose();
    }
}
