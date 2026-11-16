using System.Collections.Generic;

namespace GluttonyCombo.Window;

// Fork: pure navigation policy for the approved two-level shell.
internal enum GluttonyPage
{
    PvE,
    PvP,
    AutoRotation,
    CustomActions,
    Settings,
    About,
    Debug,
}

internal enum GluttonyActivity
{
    Ready,
    Running,
    Blocked,
}

internal sealed record GluttonyContent(string Id, string Label);

internal sealed record GluttonyFeature(string Id, string Label, GluttonyPage Page, string? ContentId = null);

internal static class GluttonyShellNavigation
{
    internal static readonly string[] Groups = ["Combos", "Automation", "System"];

    internal static string Group(GluttonyPage page) => page switch
    {
        GluttonyPage.PvE or GluttonyPage.PvP => "Combos",
        GluttonyPage.AutoRotation or GluttonyPage.CustomActions => "Automation",
        _ => "System",
    };

    internal static GluttonyPage FirstPage(string group, bool includeDebug) => group switch
    {
        "Combos" => GluttonyPage.PvE,
        "Automation" => GluttonyPage.AutoRotation,
        "System" => GluttonyPage.Settings,
        _ => includeDebug ? GluttonyPage.Debug : GluttonyPage.About,
    };

    internal static IReadOnlyList<GluttonyFeature> Features(
        GluttonyPage current,
        bool jobOpen,
        IReadOnlyList<GluttonyContent>? pveContent,
        bool includeDebug)
    {
        var features = new List<GluttonyFeature>();
        switch (Group(current))
        {
            case "Combos":
                if (current == GluttonyPage.PvE && jobOpen)
                {
                    features.Add(new("pve.root", "Jobs", GluttonyPage.PvE, "root"));
                    foreach (var content in pveContent ?? [])
                        features.Add(new($"pve.{content.Id}", content.Label, GluttonyPage.PvE, content.Id));
                }
                else
                {
                    features.Add(new("pve", "PvE jobs", GluttonyPage.PvE));
                }
                // An open PvP job returns to the PvP job list from this entry, as the old Back button did.
                features.Add(current == GluttonyPage.PvP && jobOpen
                    ? new("pvp", "PvP jobs", GluttonyPage.PvP, "root")
                    : new("pvp", "PvP jobs", GluttonyPage.PvP));
                break;
            case "Automation":
                features.Add(new("autorotation", "AutoRotation", GluttonyPage.AutoRotation));
                features.Add(new("customactions", "Custom Actions", GluttonyPage.CustomActions));
                break;
            case "System":
                features.Add(new("settings", "Settings", GluttonyPage.Settings));
                features.Add(new("about", "About", GluttonyPage.About));
                if (includeDebug)
                    features.Add(new("debug", "Debug", GluttonyPage.Debug));
                break;
        }
        return features;
    }

    internal static bool ShowSearch(GluttonyPage page, bool jobOpen, bool showSearchBar) =>
        showSearchBar && (page == GluttonyPage.Settings ||
            ((page == GluttonyPage.PvE || page == GluttonyPage.PvP) && jobOpen));

    internal static GluttonyActivity Activity(bool enabled, bool paused)
    {
        if (!enabled)
            return GluttonyActivity.Ready;
        return paused ? GluttonyActivity.Blocked : GluttonyActivity.Running;
    }
}
