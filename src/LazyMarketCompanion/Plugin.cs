using System;
using System.IO;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using Lalalazy.Changelog;
using LazyMarketCompanion.Windows;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;

namespace LazyMarketCompanion;

public sealed class Plugin : IDalamudPlugin
{
  [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
  [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
  [PluginService] public static IMarketBoard MarketBoard { get; private set; } = null!;
  [PluginService] public static IClientState ClientState { get; private set; } = null!;
  [PluginService] public static IKeyState KeyState { get; private set; } = null!;
  [PluginService] public static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
  [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
  [PluginService] public static IContextMenu ContextMenu { get; private set; } = null!;
  [PluginService] public static IDataManager DataManager { get; private set; } = null!;
  [PluginService] public static IPluginLog Log { get; private set; } = null!;
  [PluginService] public static IFramework Framework { get; private set; } = null!;
  [PluginService] public static ICondition Condition { get; private set; } = null!;

  public const string LegacyInternalName = "DagobertPriceMatcher";
  private const string CommandName = "/lmc";
  private const string LegacyCommandName = "/pricematch";

#pragma warning disable CS8618
  public static Configuration Configuration { get; private set; }
  public static DalamudLinkPayload ConfigLinkPayload { get; private set; } = null!;
#pragma warning restore CS8618

  private readonly MarketAutomation _automation;
  private readonly ChangelogGate _changelog;
  private readonly bool _ownsLegacyCommand;

  public readonly WindowSystem WindowSystem = new("LazyMarketCompanion");
  private ConfigWindow ConfigWindow { get; init; }

  public Plugin()
  {
    ECommonsMain.Init(PluginInterface, this);

    // Read BEFORE LoadOrImportConfiguration (which saves on the Dagobert-import path): tells the
    // changelog gate "update" from "fresh install".
    var existingInstall = PluginInterface.ConfigFile.Exists;
    Configuration = LoadOrImportConfiguration();

    ConfigWindow = new ConfigWindow();
    WindowSystem.AddWindow(ConfigWindow);

    // Shared "What's new" popup (repo standing rule): shows this plugin's CHANGELOG once after an update.
    _changelog = new ChangelogGate(new ChangelogGate.Options
    {
      PluginAssembly = typeof(Plugin).Assembly,
      DisplayName = "Lazy Market Companion",
      ChangelogPath = "src/LazyMarketCompanion/CHANGELOG.md",
      Framework = Framework,
      ClientState = ClientState,
      Condition = Condition,
      Log = Log,
      Windows = WindowSystem,
      ExistingInstall = existingInstall,
      SeenStore = new DelegateSeenStore(
          () => Configuration.LastSeenChangelogVersion,
          v => { Configuration.LastSeenChangelogVersion = v; Configuration.Save(); }),
    });

    CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
    {
      HelpMessage = "Open Lazy Market Companion. Subcommands: market (auto-market open retainer), pinch (re-price open retainer), sweep (all retainers), cancel, changelog (what's new), telemetry (log price decisions), debug"
    });
    // Only take the old alias if Dagobert is not loaded alongside us; otherwise we'd log an error now
    // and yank Dagobert's command on our Dispose.
    _ownsLegacyCommand = !CommandManager.Commands.ContainsKey(LegacyCommandName);
    if (_ownsLegacyCommand)
    {
      CommandManager.AddHandler(LegacyCommandName, new CommandInfo(OnCommand)
      {
        HelpMessage = "Alias of /lmc (kept from Dagobert Price Matcher)",
        ShowInHelp = false,
      });
    }

    ConfigLinkPayload = ChatGui.AddChatLinkHandler(0, (id, _) => ToggleConfigUI());

    PluginInterface.UiBuilder.Draw += DrawUI;
    PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUI;
    PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
    ContextMenu.OnMenuOpened += OnContextMenuOpened;

    AutoRetainerIPC.Initialize();
    _automation = new MarketAutomation();
    WindowSystem.AddWindow(_automation);

    Log.Information($"[LMC] loaded {PluginInterface.Manifest.AssemblyVersion}; autoMarketItems={Configuration.AutoMarketItems.Count} arInstalled={AutoRetainerIPC.Installed} imported={Configuration.ImportedFromDagobert}");
  }

  public void Dispose()
  {
    _changelog.Dispose();
    WindowSystem.RemoveAllWindows();
    _automation.Dispose();
    AutoRetainerIPC.DisposeInstance();
    CommandManager.RemoveHandler(CommandName);
    if (_ownsLegacyCommand)
      CommandManager.RemoveHandler(LegacyCommandName);
    ContextMenu.OnMenuOpened -= OnContextMenuOpened;
    PluginInterface.UiBuilder.Draw -= DrawUI;
    PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUI;
    PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUI;
    ChatGui.RemoveChatLinkHandler();
    ECommonsMain.Dispose();
  }

  /// <summary>
  /// Own config if present; otherwise a one-time import of pluginConfigs/DagobertPriceMatcher.json
  /// (same field names, plain Newtonsoft JSON, no $type — Dalamud's PluginConfigurations.LoadForType
  /// deserializes by name). Import is attempted once and flagged so it never overwrites later edits.
  /// </summary>
  private static Configuration LoadOrImportConfiguration()
  {
    var own = PluginInterface.GetPluginConfig() as Configuration;
    if (own != null)
      return MigrateIfNeeded(own);

    var config = new Configuration();
    try
    {
      var dir = PluginInterface.ConfigFile.Directory;
      if (dir != null)
      {
        var legacyPath = Path.Combine(dir.FullName, LegacyInternalName + ".json");
        if (File.Exists(legacyPath))
        {
          var imported = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(legacyPath));
          if (imported != null)
          {
            config = imported;
            // The imported Dagobert JSON has no Auto-Market fields at all, so those took this build's
            // defaults - it is already on the current schema and needs no migration.
            config.Version = Configuration.CurrentVersion;
            config.ImportedFromDagobert = true;
            Log.Information($"[LMC] imported settings from {legacyPath}: {config.ItemPriceLimits.Count} price limit(s), {config.SeenRetainers.Count} seen retainer(s), {config.LastKnownRetainerNames.Count} retainer name(s)");
          }
        }
      }
    }
    catch (Exception ex)
    {
      Log.Warning(ex, "[LMC] Dagobert config import failed; starting with defaults");
      config = new Configuration();
    }

    config.ImportedFromDagobert = config.ImportedFromDagobert || true;
    PluginInterface.SavePluginConfig(config);
    return config;
  }

  /// <summary>
  /// Config schema upgrades, applied once per config and saved immediately.
  ///
  /// Changing a C# field initializer only ever affects a FRESH config: Newtonsoft writes every property
  /// into the JSON, so an existing install deserializes its saved value straight over the new default.
  /// Anything that must reach existing users needs a step here.
  ///
  /// v1 -> v2 (0.1.3.0): "Pinch everything after listing" became opt-in. Auto Market used to re-price the
  /// retainer's entire sell inventory after listing, which costs several seconds per existing listing and
  /// was the reason a sweep took minutes; it now re-prices only the listings it just created. Re-tick the
  /// box in /lmc settings to get the old behaviour back.
  /// </summary>
  private static Configuration MigrateIfNeeded(Configuration config)
  {
    if (config.Version >= Configuration.CurrentVersion)
      return config;

    var from = config.Version;

    if (config.Version < 2)
    {
      var wasOn = config.AutoMarketPinchAllAfter;
      config.AutoMarketPinchAllAfter = false;
      config.Version = 2;
      Log.Information($"[LMC] config migrated v{from} -> v2: 'Pinch everything after listing' {(wasOn ? "was ON and has been turned OFF" : "was already off")}; Auto Market now re-prices only the listings it just created. Re-tick it in /lmc settings for the old behaviour.");
    }

    config.Version = Configuration.CurrentVersion;
    config.Save();
    return config;
  }

  /// <summary>Localised Addon-sheet text (retainer menu entries etc.), empty on miss.</summary>
  public static string AddonText(uint row)
  {
    try
    {
      return DataManager.GetExcelSheet<Addon>().TryGetRow(row, out var r) ? r.Text.ToString() : string.Empty;
    }
    catch { return string.Empty; }
  }

  private void OnCommand(string command, string args)
  {
    var sub = args.Trim().ToLowerInvariant();

    // Handled before the switch because it takes an argument: "telemetry on" / "telemetry off".
    if (sub.StartsWith("telemetry", StringComparison.Ordinal))
    {
      HandleTelemetryCommand(sub["telemetry".Length..].Trim());
      return;
    }

    switch (sub)
    {
      case "market":
        _automation.AutoMarketCurrentRetainer();
        return;
      case "pinch":
        _automation.PinchCurrentRetainer();
        return;
      case "sweep":
        _automation.SweepAllRetainers(Configuration.AutoMarketInPinchAllSweep);
        return;
      case "cancel":
        _automation.CancelEverything("cancelled by command");
        return;
      case "changelog":
      case "whatsnew":
        _changelog.ShowNow();
        return;
      case "debug":
        var state = _automation.DebugState();
        Log.Information("[LMC] " + state);
        ChatGui.Print("[LMC] " + state);
        return;
      default:
        ToggleConfigUI();
        return;
    }
  }

  /// <summary>
  /// <c>/lmc telemetry [on|off|toggle|status]</c> - the off-by-default price-decision tap behind
  /// <see cref="Configuration.DecisionTelemetry"/> (mirrors GluttonyCombo's <c>/gluttony telemetry</c>
  /// and AutoPotion's <c>/autopotion telemetry</c>).
  /// </summary>
  private void HandleTelemetryCommand(string sub)
  {
    var current = Configuration.DecisionTelemetry;
    bool? wanted = sub switch
    {
      "on" or "enable" or "1" => true,
      "off" or "disable" or "0" => false,
      "toggle" => !current,
      _ => null,
    };

    if (wanted is null)
    {
      if (sub.Length > 0 && sub != "status")
        ChatGui.Print("[LMC] Usage: /lmc telemetry <on|off|toggle|status>");
      ChatGui.Print($"[LMC] Price-decision telemetry is {(current ? "ON" : "OFF")} " +
                    $"(diagnostic lines starting with {MarketTelemetry.Prefix} in the plugin log).");
      return;
    }

    if (wanted.Value != current)
    {
      Configuration.DecisionTelemetry = wanted.Value;
      Configuration.Save();
    }

    ChatGui.Print($"[LMC] Price-decision telemetry {(wanted.Value ? "ON" : "OFF")}" +
                  (wanted.Value ? $" - writing {MarketTelemetry.Prefix} lines to the plugin log." : "."));
  }

  private void OnContextMenuOpened(IMenuOpenedArgs args)
  {
    if (!Configuration.ShowInventoryContextMenuEntry)
      return;

    if (args.MenuType != ContextMenuType.Inventory)
      return;

    var target = (args.Target as MenuTargetInventory)?.TargetItem;
    var itemId = target?.BaseItemId ?? 0u;
    if (itemId == 0)
      return;

    if (!DataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var item))
      return;

    var hq = target?.IsHq ?? false;
    var onList = Configuration.GetAutoMarketItem(itemId, hq) != null;
    var tradable = !item.IsUntradable && item.ItemSearchCategory.RowId != 0;

    args.AddMenuItem(new MenuItem
    {
      Name = onList ? "Auto-Market: edit" : "Add to Auto-Market",
      PrefixChar = 'L',
      IsEnabled = tradable,
      OnClicked = _ => AddAutoMarketFromMenu(itemId, hq),
    });

    var hasLimit = Configuration.ItemPriceLimits.Any(limit => limit.ItemId == itemId);
    args.AddMenuItem(new MenuItem
    {
      Name = hasLimit ? "Price limits: edit" : "Add price limits",
      PrefixChar = 'L',
      IsEnabled = !item.IsUntradable,
      OnClicked = _ => AddPriceLimitFromMenu(itemId),
    });
  }

  private void AddAutoMarketFromMenu(uint itemId, bool hq)
  {
    try
    {
      var added = Configuration.GetAutoMarketItem(itemId, hq) == null;
      Configuration.GetOrAddAutoMarketItem(itemId, hq);
      Configuration.Save();
      ConfigWindow.OpenAutoMarketTab();

      ChatGui.Print(new SeStringBuilder()
        .AddText("[LMC] ")
        .AddItemLink(itemId, hq)
        .AddText(added ? ": added to Auto-Market." : ": already on Auto-Market.")
        .Build());
    }
    catch (Exception ex)
    {
      Svc.Log.Error(ex, $"[LMC] failed to add item {itemId} to Auto-Market");
    }
  }

  private void AddPriceLimitFromMenu(uint itemId)
  {
    try
    {
      var added = Configuration.GetItemPriceLimit(itemId) == null;
      Configuration.GetOrAddItemPriceLimit(itemId);
      Configuration.Save();
      ConfigWindow.OpenPriceLimitsTab();

      ChatGui.Print(new SeStringBuilder()
        .AddText("[LMC] ")
        .AddItemLink(itemId, false)
        .AddText(added ? ": added to price limits." : ": already in price limits.")
        .Build());
    }
    catch (Exception ex)
    {
      Svc.Log.Error(ex, $"[LMC] failed to add item {itemId} to price limits");
    }
  }

  private void DrawUI()
  {
    WindowSystem.Draw();
  }

  public void ToggleConfigUI() => ConfigWindow.Toggle();
}
