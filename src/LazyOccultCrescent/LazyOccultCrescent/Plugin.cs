using System;
using LazyOccultCrescent.Chains;
using LazyOccultCrescent.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin;
using ECommons;
using ECommons.DalamudServices;
using Dalamud.Interface.Windowing;
using Lalalazy.Changelog;
using Ocelot;
using Ocelot.Chain;

namespace LazyOccultCrescent;

public sealed class Plugin : OcelotPlugin
{
    public override string Name
    {
        get => "LazyOccultCrescent";
    }

    public Config Config { get; }

    // Ocelot's WindowManager owns a private WindowSystem we cannot add to, so the shared changelog
    // popup gets its own one-window system, drawn from our own UiBuilder.Draw hook.
    private readonly WindowSystem _changelogWindows = new("LazyOccultCrescent##changelog");
    private readonly ChangelogGate _changelog;

    public override IOcelotConfig OcelotConfig
    {
        get => Config;
    }

    public static ChainQueue Chain
    {
        get => ChainManager.Get("LOC##main");
    }

    public Plugin(IDalamudPluginInterface plugin)
        : base(plugin, Module.DalamudReflector)
    {
        // Read BEFORE anything saves the config: tells the changelog gate "update" from "fresh install".
        var existingInstall = plugin.ConfigFile.Exists;
        Config = plugin.GetPluginConfig() as Config ?? new Config();

        // Shared "What's new" popup (repo standing rule): shows this plugin's CHANGELOG once after an update.
        _changelog = new ChangelogGate(new ChangelogGate.Options
        {
            PluginAssembly = typeof(Plugin).Assembly,
            DisplayName = "LazyOccultCrescent",
            ChangelogPath = "src/LazyOccultCrescent/CHANGELOG.md",
            Framework = Svc.Framework,
            ClientState = Svc.ClientState,
            Condition = Svc.Condition,
            Log = Svc.Log,
            Windows = _changelogWindows,
            ExistingInstall = existingInstall,
            SeenStore = new DelegateSeenStore(
                () => Config.LastSeenChangelogVersion,
                v => { Config.LastSeenChangelogVersion = v; Config.Save(); }),
        });
        Svc.PluginInterface.UiBuilder.Draw += _changelogWindows.Draw;

        SetupLanguage(plugin);

        OcelotInitialize(OcelotFeature.All);

        ChainHelper.Initialize(this);
    }

    /// <summary>`/lazyoccult changelog` - reopen the "What's new" popup on demand.</summary>
    public void ShowChangelog()
    {
        _changelog.ShowNow();
    }

    public override void Dispose()
    {
        Svc.PluginInterface.UiBuilder.Draw -= _changelogWindows.Draw;
        _changelog.Dispose();
        _changelogWindows.RemoveAllWindows();
        base.Dispose();
    }

    private void SetupLanguage(IDalamudPluginInterface plugin)
    {
        I18N.SetDirectory(plugin.AssemblyLocation.Directory?.FullName!);
        I18N.LoadAllFromDirectory("en", "Translations/en");
        I18N.LoadAllFromDirectory("jp", "Translations/jp");
        I18N.LoadAllFromDirectory("fr", "Translations/fr");
#if DALAMUD_CN
        I18N.LoadAllFromDirectory("zh", "Translations/zh");
#endif

        // @todo: Breakup German and uwu translation
        I18N.LoadFromFile("de", "Translations/de.json");
        I18N.LoadFromFile("uwu", "Translations/uwu.json");

        var lang = Svc.ClientState.ClientLanguage switch
        {
            ClientLanguage.French => "fr",
            ClientLanguage.German => "de",
            ClientLanguage.Japanese => "jp",
#if DALAMUD_CN
            ClientLanguage.ChineseSimplified => "zh",
#endif
            _ => "en",
        };

        I18N.SetLanguage(lang);

        var today = DateTime.Today;
        if (today is { Month: 4, Day: 1 } && Random.Shared.NextDouble() < 0.05)
        {
            I18N.SetLanguage("uwu");
        }
    }

    protected override bool ShouldUpdate()
    {
        return ZoneData.IsInOccultCrescent()
               && !(
                   Svc.Condition[ConditionFlag.BetweenAreas] ||
                   Svc.Condition[ConditionFlag.BetweenAreas51] ||
                   Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
                   Svc.Condition[ConditionFlag.OccupiedInEvent] ||
                   Svc.Condition[ConditionFlag.WatchingCutscene] ||
                   Svc.Condition[ConditionFlag.WatchingCutscene78] ||
                   Svc.Objects.LocalPlayer?.IsTargetable != true
               );
    }
}
