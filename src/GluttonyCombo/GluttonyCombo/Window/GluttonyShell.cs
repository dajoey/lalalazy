using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using ECommons.ExcelServices;
using GluttonyCombo.AutoRotation;
using GluttonyCombo.Combos.PvE;
using GluttonyCombo.Data.Conflicts;
using GluttonyCombo.Extensions;
using GluttonyCombo.Resources.Localization.UI.AutoRotation;
using GluttonyCombo.Resources.Localization.UI.Features;
using GluttonyCombo.Resources.Localization.UI.MainWindow;
using GluttonyCombo.Resources.Localization.UI.Settings;
using GluttonyCombo.Services;
using GluttonyCombo.Window.Tabs;
using LalaUiKit;
using Debug = GluttonyCombo.Window.Tabs.Debug;

namespace GluttonyCombo.Window;

// Fork: responsive two-level shell around the existing tab and preset renderers.
internal static class GluttonyShell
{
    private static bool IncludeDebug
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    internal static void Draw(ConfigWindow window)
    {
        if (window.OpenWindow == OpenWindow.None)
            window.OpenWindow = OpenWindow.PvE;
        var page = Page(window.OpenWindow);
        var model = BuildModel(window, page, out var features);
        var search = ConfigWindow.Search;
        var descriptions = ConfigWindow.SearchDescription;
        var result = GluttonyShellView.Draw(model, ref search, ref descriptions, DrawBodyPrefix(page), () => DrawBody(window.OpenWindow),
            GluttonyShellNavigation.UsesPageCard(page));
        ConfigWindow.Search = search;
        ConfigWindow.SearchDescription = descriptions;

        if (result.Close)
            window.IsOpen = false;
        if (result.Brand)
        {
            FeaturesWindow.OpenJob = null;
            FeaturesWindow.OpenPvPJob = null;
            Navigate(window, GluttonyPage.PvE);
        }
        if (result.Group is { } group)
            Navigate(window, GluttonyShellNavigation.FirstPage(group, IncludeDebug));
        if (result.Feature >= 0 && result.Feature < features.Count)
            SelectFeature(window, features[result.Feature]);
        if (result.ExpandFeatures)
        {
            Service.Configuration.UILeftColumnCollapsed = false;
            Service.Configuration.Save();
        }
        if (result.FooterStop)
            AutoRotationController.ToggleAutoRotation(false);
    }

    private static ShellModel BuildModel(ConfigWindow window, GluttonyPage page, out IReadOnlyList<GluttonyFeature> features)
    {
        var content = page == GluttonyPage.PvE && FeaturesWindow.OpenJob is { } job
            ? PvEFeatures.ApprovedContents(job)
            : [];
        features = GluttonyShellNavigation.Features(page, JobOpen(page), content, IncludeDebug);
        var (title, description, count) = PageDetails(page);
        // While a search is active the count in the header is the number of matches, not the size of the job's list.
        if (PresetPageSearching(page))
            count = SearchCount(page);
        var state = LiveState();
        var enabled = FooterEnabled();
        var paused = AutoRotationController.Paused;
        var presetPage = page is GluttonyPage.PvE or GluttonyPage.PvP;
        return new ShellModel
        {
            Groups = GluttonyShellNavigation.Groups,
            CurrentGroup = GluttonyShellNavigation.Group(page),
            State = state,
            ShowSearch = GluttonyShellNavigation.ShowSearch(page, JobOpen(page), Service.Configuration.UIShowSearchBar),
            ShowDescriptions = presetPage,
            SearchHint = page == GluttonyPage.Settings ? SettingsUI.Info_searchHintText : FeaturesUI.Search_searchHintText,
            Features = features.Select(feature => new ShellFeature(feature.Id, feature.Label, FeatureSelected(window, page, feature))).ToList(),
            FeaturesCollapsed = Service.Configuration.UILeftColumnCollapsed,
            ExpandHelp = MainWindowUI.Button_ExpandSidebar,
            // The old header showed the plugin version; the brand chip keeps it as its tooltip.
            BrandHelp = $"{GluttonyCombo.P.Name} {GluttonyCombo.P.GetType().Assembly.GetName().Version}",
            // Fork: the problem-report button the old sidebar showed on every page; the shell draws it in the footer.
            ReportButton = GluttonyCombo.P.Telemetry is { } telemetry ? () => telemetry.DrawReportButton() : null,
            Title = title,
            Description = description,
            Meta = count.Length == 0 ? [] : [count],
            Footer = new ShellFooter
            {
                State = state,
                Primary = enabled ? $"{Copy.Word(state)} · AutoRotation" : "Ready · AutoRotation is off",
                Secondary = !enabled ? "Turn it on from the Automation page; combos still work from their own buttons."
                    : paused ? "Automatic actions are paused" : "Automatic action replacement is enabled",
                StopLabel = enabled ? "Stop" : null,
            },
        };
    }

    // Fork: banners that used to sit above the page now scroll with the body, under the shell bands.
    private static Action DrawBodyPrefix(GluttonyPage page) => () =>
    {
        if (page == GluttonyPage.PvE && FeaturesWindow.OpenJob is { } job &&
            GluttonyCombo.P.UIHelper.JobControlled(job) is not null)
            GluttonyCombo.P.UIHelper.ShowIPCControlledIndicatorIfNeeded(job);
        ConflictingPlugins.Draw();
    };

    private static void DrawBody(OpenWindow page)
    {
        switch (page)
        {
            case OpenWindow.PvE:
                PvEFeatures.DrawApproved();
                break;
            case OpenWindow.PvP:
                PvPFeatures.DrawApproved();
                break;
            case OpenWindow.Settings:
                Settings.Draw();
                break;
            case OpenWindow.About:
                DrawAbout();
                break;
            case OpenWindow.Debug:
                Debug.Draw();
                break;
            case OpenWindow.AutoRotation:
                AutoRotationTab.Draw();
                break;
            case OpenWindow.CustomActions:
                CustomActions.Draw();
                break;
        }
    }

    private static bool FeatureSelected(ConfigWindow window, GluttonyPage page, GluttonyFeature feature)
    {
        if (feature.Page != page)
            return false;
        if (feature.ContentId == "root")
            return feature.Page == GluttonyPage.PvP || FeaturesWindow.OpenJob is null;
        if (feature.ContentId is not null)
            return PvEFeatures.ContentId(FeaturesWindow.CurrentTab) == feature.ContentId;
        return feature.Page != GluttonyPage.PvE || FeaturesWindow.OpenJob is null;
    }

    private static void SelectFeature(ConfigWindow window, GluttonyFeature feature)
    {
        if (feature.ContentId == "root")
        {
            if (feature.Page == GluttonyPage.PvP)
            {
                FeaturesWindow.OpenPvPJob = null;
                return;
            }
            FeaturesWindow.OpenJob = null;
            PvEFeatures.SetCurrentTab(FeaturesWindow.FeatureTab.Normal);
            return;
        }
        if (feature.ContentId is not null)
        {
            window.OpenWindow = OpenWindow.PvE;
            PvEFeatures.SetCurrentTab(PvEFeatures.ContentTab(feature.ContentId));
            return;
        }
        Navigate(window, feature.Page);
    }

    private static (string Title, string Description, string Count) PageDetails(GluttonyPage page)
    {
        switch (page)
        {
            case GluttonyPage.PvE when FeaturesWindow.OpenJob is { } job:
                return (job.Name(), "Job combos, options, and content presets.",
                    $"{ConfigWindow.groupedPresets[job].Count(preset => !preset.ShouldBeHidden)} preset groups");
            case GluttonyPage.PvE:
                return (MainWindowUI.Button_PvEFeatures, FeaturesUI.Info_SelectAJob, $"{ConfigWindow.groupedPresets.Count} jobs");
            case GluttonyPage.PvP when FeaturesWindow.OpenPvPJob is { } pvpJob:
                return (pvpJob.Name(), FeaturesUI.Info_pvpDesc,
                    $"{ConfigWindow.groupedPresets[pvpJob].Count(preset => preset.IsPvP && !preset.ShouldBeHidden)} preset groups");
            case GluttonyPage.PvP:
                return (MainWindowUI.Button_PvPFeatures, FeaturesUI.Info_pvpDesc,
                    $"{ConfigWindow.groupedPresets.Count(pair => pair.Value.Any(preset => preset.IsPvP && !preset.ShouldBeHidden))} jobs");
            case GluttonyPage.Settings:
                return (MainWindowUI.Button_Settings, SettingsUI.Header_SettingsAbout, $"{Settings.SettingsList.Count} settings");
            case GluttonyPage.AutoRotation:
                return (MainWindowUI.Button_AutoRotation, AutoRotationUI.Info_Header,
                    $"{Service.Configuration.AutoActions.Count(pair => pair.Value)} auto modes");
            case GluttonyPage.CustomActions:
                var rotationCount = GluttonyCombo.P.CustomActions.Manager.Actions.Count(action => action.Id <= All.AoeHeals);
                var utilityCount = GluttonyCombo.P.CustomActions.Manager.Actions.Count(action => action.Id is > All.Cease and < All.Items);
                return ("Custom Actions", "Drag bespoke rotation and utility actions to the hotbar.",
                    $"{rotationCount} rotation · {utilityCount} utility");
            case GluttonyPage.About:
                return (GluttonyCombo.P.Name, "A lalalazy fork of Wrath Combo. All upstream credit to Team Wrath / PunishXIV.",
                    $"v{GluttonyCombo.P.GetType().Assembly.GetName().Version} · {ConfigWindow.groupedPresets.Values.Sum(list => list.Count)} job presets");
            case GluttonyPage.Debug:
                return ("Debug", "Inspect live game, rotation, and IPC state.", $"{ConfigWindow.presetChildren.Count} presets");
            default:
                return (GluttonyCombo.P.Name, "Combos and settings.", "");
        }
    }

    private static bool PresetPageSearching(GluttonyPage page) =>
        page is GluttonyPage.PvE or GluttonyPage.PvP && JobOpen(page) && FeaturesWindow.IsSearching;

    private static string SearchCount(GluttonyPage page)
    {
        var job = page == GluttonyPage.PvP ? FeaturesWindow.OpenPvPJob : FeaturesWindow.OpenJob;
        if (job is not { } openJob || !ConfigWindow.groupedPresets.TryGetValue(openJob, out var presets))
            return "";
        var matches = presets.Count(preset => preset.IsPvP == (page == GluttonyPage.PvP) && FeaturesWindow.PresetMatchesSearch(preset.Preset));
        return matches == 1 ? "1 result" : $"{matches} results";
    }

    private static void DrawAbout()
    {
        GluttonyChrome.Card("about_links", () =>
        {
            Widgets.Title("Source and credits");
            Widgets.SecondaryWrapped("GluttonyCombo preserves the upstream combo suite and its fork integrations.");
            if (GluttonyChrome.StyledButton("GitHub: dajoey/lalalazy"))
                Process.Start(new ProcessStartInfo { FileName = "https://github.com/dajoey/lalalazy", UseShellExecute = true });
            if (GluttonyChrome.StyledButton("Additional custom action icons by alexisoffline"))
                Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/alexisoffline/", UseShellExecute = true });
        });
        GluttonyChrome.Card("about_support", () =>
        {
            Widgets.Title("Support");
            Widgets.SecondaryWrapped("Conflict warnings stay visible above every page. Reports use the existing telemetry surface.");
            if (GluttonyCombo.P.Telemetry is { } telemetry)
                GluttonyChrome.StyledButtonCallback(ImGui.GetCursorScreenPos(), () => telemetry.DrawReportButton());
        });
    }

    private static bool FooterEnabled() => AutoRotationController.cfg is { Enabled: true };

    private static UiState LiveState()
    {
        var activity = GluttonyShellNavigation.Activity(AutoRotationController.cfg is { Enabled: true }, AutoRotationController.Paused);
        return activity switch
        {
            GluttonyActivity.Running => UiState.Running,
            GluttonyActivity.Blocked => UiState.Blocked,
            _ => UiState.Ready,
        };
    }

    private static bool JobOpen(GluttonyPage page) => page switch
    {
        GluttonyPage.PvE => FeaturesWindow.OpenJob is not null,
        GluttonyPage.PvP => FeaturesWindow.OpenPvPJob is not null,
        _ => false,
    };

    private static GluttonyPage Page(OpenWindow page) => page switch
    {
        OpenWindow.PvP => GluttonyPage.PvP,
        OpenWindow.Settings => GluttonyPage.Settings,
        OpenWindow.AutoRotation => GluttonyPage.AutoRotation,
        OpenWindow.About => GluttonyPage.About,
        OpenWindow.Debug => GluttonyPage.Debug,
        OpenWindow.CustomActions => GluttonyPage.CustomActions,
        _ => GluttonyPage.PvE,
    };

    private static void Navigate(ConfigWindow window, GluttonyPage page) => window.OpenWindow = page switch
    {
        GluttonyPage.PvP => OpenWindow.PvP,
        GluttonyPage.Settings => OpenWindow.Settings,
        GluttonyPage.AutoRotation => OpenWindow.AutoRotation,
        GluttonyPage.About => OpenWindow.About,
        GluttonyPage.Debug => OpenWindow.Debug,
        GluttonyPage.CustomActions => OpenWindow.CustomActions,
        _ => OpenWindow.PvE,
    };
}
