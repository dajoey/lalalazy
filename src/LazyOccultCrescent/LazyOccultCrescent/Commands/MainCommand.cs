using System.Collections.Generic;
using System.Linq;
using LazyOccultCrescent.Modules.Debug;
using ECommons;
using ECommons.DalamudServices;
using Ocelot;
using Ocelot.Commands;
using Ocelot.Modules;

namespace LazyOccultCrescent.Commands;

[OcelotCommand]
public class MainCommand(Plugin plugin) : OcelotCommand
{
    protected override string Command
    {
        get => "/lazyoccult";
    }

    protected override string Description
    {
        get => @"
Opens LazyOccultCrescent main ui
 - /lazyoccult : Opens the main ui
 - /lazyoccult config : opens the config ui
 - /lazyoccult cfg : opens the config ui
 - /lazyoccult changelog : shows what's new in this version
--------------------------------
".Trim();
    }

    protected override IReadOnlyList<string> Aliases
    {
        get => ["/lazyoc", "/lazyoccultcrescent"];
    }

    private readonly IReadOnlyList<string> languageCodes =
    [
        "en", "de", "fr", "jp", "uwu",
    ];

    public override void Execute(string command, string arguments)
    {
        if (arguments is "config" or "cfg")
        {
            plugin.Windows.ToggleConfigUI();
            return;
        }

#if DEBUG_BUILD
        if (arguments == "debug")
        {
            plugin.Windows.GetWindow<DebugWindow>().Toggle();
            return;
        }
#endif

        if (arguments is "changelog" or "whatsnew")
        {
            plugin.ShowChangelog();
            return;
        }

        if (arguments == "buff")
        {
            new BuffCommand(plugin).Execute("/lazyoccultbuff", "");
            return;
        }

        if (arguments.StartsWith("tp"))
        {
            new TeleportCommand(plugin).Execute("/lazyocculttp", arguments.ReplaceFirst("tp", "").Trim());
            return;
        }

        if (arguments.StartsWith("language"))
        {
            var parts = arguments.Split(' ', 2);
            if (parts.Length == 2)
            {
                var code = parts[1].Trim().ToLowerInvariant();
                if (languageCodes.Contains(code))
                {
                    I18N.SetLanguage(code);
                    Svc.Chat.Print($"Language set to: {code}");
                    return;
                }

                Svc.Log.Error($"Unknown language code: {code}");
                return;
            }

            Svc.Chat.Print("Usage: /lazyoccult language <code>");
            return;
        }

        plugin.Windows.ToggleMainUI();
    }
}
