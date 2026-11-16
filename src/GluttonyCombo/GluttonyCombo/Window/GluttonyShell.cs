using System;
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
    private const float ControlHeight = 34f;
    private const float WideFooterHeight = 68f;
    private const float NarrowFooterHeight = 96f;

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
        Theme.PushFrame();
        GluttonyChrome.PushControlColors();
        try
        {
            if (window.OpenWindow == OpenWindow.None)
                window.OpenWindow = OpenWindow.PvE;
            var page = Page(window.OpenWindow);
            DrawTopBar(window, page);
            if (!Service.Configuration.UILeftColumnCollapsed)
                DrawFeatureRow(window, page);
            else
                DrawCollapsedFeatureRow();
            DrawPageHeader(page);
            ConflictingPlugins.Draw();

            var footerHeight = FooterEnabled() ? FooterHeight(ImGui.GetContentRegionAvail().X) : 0;
            var bodyHeight = MathF.Max(40, ImGui.GetContentRegionAvail().Y - footerHeight);
            if (ImGui.BeginChild("##gc_shell_body", new Vector2(0, bodyHeight), false))
            {
                DrawBody(window.OpenWindow);
                ImGui.EndChild();
            }
            else
            {
                ImGui.EndChild();
            }
            DrawFooter();
        }
        finally
        {
            GluttonyChrome.PopControlColors();
            Theme.PopFrame();
        }
    }

    private static void DrawBody(OpenWindow page)
    {
        switch (page)
        {
            case OpenWindow.PvE:
                PvEFeatures.Draw();
                break;
            case OpenWindow.PvP:
                PvPFeatures.Draw();
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

    private static void DrawTopBar(ConfigWindow window, GluttonyPage page)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var gap = 2 * Theme.Unit;
        var brandSize = new Vector2(MathF.Max(196, (ImGui.CalcTextSize("GluttonyCombo").X * Theme.TitleScale) + 66), 42);
        var groupSizes = GluttonyShellNavigation.Groups
            .Select(group => new Vector2(MathF.Max(82, ImGui.CalcTextSize(group).X + 30), ControlHeight)).ToArray();
        var state = LiveState();
        var stateLabel = Copy.Word(state);
        var statusSize = new Vector2(MathF.Max(82, ImGui.CalcTextSize(stateLabel).X + 43), ControlHeight);
        var closePos = new Vector2(width - GluttonyChrome.CloseButtonSize, 16);
        var statusPos = new Vector2(closePos.X - gap - statusSize.X, 16);
        var navigationRight = statusPos.X - gap;
        var brandPos = new Vector2(0, 12);
        var groupsWidth = groupSizes.Sum(size => size.X) + ((groupSizes.Length - 1) * gap);
        var besideBrand = brandSize.X + gap + groupsWidth <= navigationRight;
        var positions = new Vector2[groupSizes.Length];
        var x = besideBrand ? brandSize.X + gap : 0;
        var y = besideBrand ? 16f : 62f;
        var navigationBottom = brandPos.Y + brandSize.Y;

        for (var i = 0; i < groupSizes.Length; i++)
        {
            var rightLimit = y == 16f ? navigationRight : width;
            if (x > 0 && x + groupSizes[i].X > rightLimit)
            {
                x = 0;
                y += ControlHeight + gap;
            }
            positions[i] = new Vector2(x, y);
            x += groupSizes[i].X + gap;
            navigationBottom = MathF.Max(navigationBottom, y + ControlHeight);
        }

        var showSearch = GluttonyShellNavigation.ShowSearch(page, JobOpen(page), Service.Configuration.UIShowSearchBar);
        var searchY = navigationBottom + gap;
        var barHeight = showSearch ? searchY + ControlHeight + 12 : navigationBottom + 12;
        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));

        if (GluttonyChrome.BrandButton(draw, origin + brandPos, brandSize))
        {
            FeaturesWindow.OpenJob = null;
            FeaturesWindow.OpenPvPJob = null;
            Navigate(window, GluttonyPage.PvE);
        }
        for (var i = 0; i < GluttonyShellNavigation.Groups.Length; i++)
        {
            var group = GluttonyShellNavigation.Groups[i];
            if (GluttonyChrome.OutlineButton(draw, "group_" + group, group, origin + positions[i], groupSizes[i],
                    GluttonyShellNavigation.Group(page) == group) && GluttonyShellNavigation.Group(page) != group)
                Navigate(window, GluttonyShellNavigation.FirstPage(group, IncludeDebug));
        }
        GluttonyChrome.StatusPill(draw, origin + statusPos, statusSize, stateLabel, state);
        if (GluttonyChrome.CloseButton(draw, "main", origin + closePos))
            window.IsOpen = false;

        if (showSearch)
        {
            var descriptions = page is GluttonyPage.PvE or GluttonyPage.PvP;
            var descriptionWidth = descriptions ? 112f : 0;
            var inputWidth = MathF.Max(120, width - (descriptions ? descriptionWidth + gap : 0));
            ImGui.SetCursorScreenPos(origin + new Vector2(0, searchY));
            ImGui.SetNextItemWidth(inputWidth);
            var padding = Theme.FramePadding;
            padding.Y = MathF.Max(padding.Y, (ControlHeight - ImGui.GetTextLineHeight()) / 2f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, padding);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2 * Theme.Unit);
            var hint = page == GluttonyPage.Settings ? SettingsUI.Info_searchHintText : FeaturesUI.Search_searchHintText;
            ImGui.InputTextWithHint("##gc_shell_search", hint, ref ConfigWindow.Search, 30, ImGuiInputTextFlags.AutoSelectAll);
            ImGui.PopStyleVar(2);
            if (descriptions && GluttonyChrome.OutlineButton(draw, "search_descriptions", "Descriptions",
                    origin + new Vector2(width - descriptionWidth, searchY), new Vector2(descriptionWidth, ControlHeight),
                    ConfigWindow.SearchDescription))
                ConfigWindow.SearchDescription = !ConfigWindow.SearchDescription;
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private static void DrawFeatureRow(ConfigWindow window, GluttonyPage page)
    {
        var content = page == GluttonyPage.PvE && FeaturesWindow.OpenJob is { } job
            ? PvEFeatures.ApprovedContents(job)
            : [];
        var features = GluttonyShellNavigation.Features(page, JobOpen(page), content, IncludeDebug);
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var gap = 3 * Theme.Unit;
        var sizes = features.Select(feature => new Vector2(
            MathF.Min(width, MathF.Max(104, ImGui.CalcTextSize(feature.Label).X + 52)), ControlHeight)).ToArray();
        var positions = new Vector2[features.Count];
        float x = 0;
        float y = 8;
        for (var i = 0; i < features.Count; i++)
        {
            if (x > 0 && x + sizes[i].X > width)
            {
                x = 0;
                y += ControlHeight + (2 * Theme.Unit);
            }
            positions[i] = new Vector2(x, y);
            x += sizes[i].X + gap;
        }
        var barHeight = y + ControlHeight + 8;
        draw.AddRectFilled(origin, origin + new Vector2(width, barHeight), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, barHeight), origin + new Vector2(width, barHeight), Theme.Pack(Theme.SurfaceBorder));
        for (var i = 0; i < features.Count; i++)
        {
            var feature = features[i];
            if (GluttonyChrome.FeatureButton(draw, feature.Id, feature.Label, origin + positions[i], sizes[i],
                    FeatureSelected(window, page, feature)))
                SelectFeature(window, feature);
        }
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, barHeight));
    }

    private static void DrawCollapsedFeatureRow()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        const float height = 50;
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin + new Vector2(0, height), origin + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder));
        if (GluttonyChrome.OutlineButton(draw, "expand_features", "Show destinations", origin + new Vector2(0, 8),
                new Vector2(MathF.Min(180, width), ControlHeight)))
        {
            Service.Configuration.UILeftColumnCollapsed = false;
            Service.Configuration.Save();
        }
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    private static bool FeatureSelected(ConfigWindow window, GluttonyPage page, GluttonyFeature feature)
    {
        if (feature.Page != page)
            return false;
        if (feature.ContentId == "root")
            return FeaturesWindow.OpenJob is null;
        if (feature.ContentId is not null)
            return PvEFeatures.ContentId(FeaturesWindow.CurrentTab) == feature.ContentId;
        return feature.Page != GluttonyPage.PvE || FeaturesWindow.OpenJob is null;
    }

    private static void SelectFeature(ConfigWindow window, GluttonyFeature feature)
    {
        if (feature.ContentId == "root")
        {
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

    private static void DrawPageHeader(GluttonyPage page)
    {
        var (title, description, count) = PageDetails(page);
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var tileSize = new Vector2(42);
        draw.AddRectFilled(origin, origin + tileSize, Theme.Pack(Theme.Accent with { W = 0.14f }), 2 * Theme.Unit);
        GluttonyChrome.Diamond(draw, origin + (tileSize / 2f), 10, Theme.Pack(Theme.Accent));

        if (width >= 720)
        {
            var textX = origin.X + tileSize.X + 12;
            var countWidth = ImGui.CalcTextSize(count).X;
            GluttonyChrome.DrawText(draw, new Vector2(textX, origin.Y),
                GluttonyChrome.FitText(title, width - tileSize.X - 12 - countWidth - 16, Theme.TitleScale),
                Theme.TextPrimary, Theme.TitleScale);
            GluttonyChrome.DrawText(draw, new Vector2(textX, origin.Y + 29),
                GluttonyChrome.FitText(description, width - tileSize.X - 12), Theme.TextSecondary);
            draw.AddText(new Vector2(origin.X + width - countWidth, origin.Y + 12), Theme.Pack(Theme.TextSecondary), count);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, 58));
            ImGui.Separator();
            return;
        }

        GluttonyChrome.DrawText(draw, origin + new Vector2(tileSize.X + 12, 4),
            GluttonyChrome.FitText(title, width - tileSize.X - 12, Theme.TitleScale), Theme.TextPrimary, Theme.TitleScale);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, tileSize.Y + 8));
        Widgets.SecondaryWrapped(description);
        Widgets.Caption(count);
        ImGui.Separator();
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
                    $"{ConfigWindow.groupedPresets.Values.Sum(list => list.Count)} job presets");
            case GluttonyPage.Debug:
                return ("Debug", "Inspect live game, rotation, and IPC state.", $"{ConfigWindow.presetChildren.Count} presets");
            default:
                return (GluttonyCombo.P.Name, "Combos and settings.", "");
        }
    }

    private static void DrawAbout()
    {
        GluttonyChrome.Card("about_links", () =>
        {
            Widgets.Title("Source and credits");
            Widgets.SecondaryWrapped("GluttonyCombo preserves the upstream combo suite and its fork integrations.");
            if (ImGui.Button("GitHub: dajoey/lalalazy"))
                Process.Start(new ProcessStartInfo { FileName = "https://github.com/dajoey/lalalazy", UseShellExecute = true });
            if (ImGui.GetContentRegionAvail().X >= 420)
                ImGui.SameLine();
            if (ImGui.Button("Additional custom action icons by alexisoffline"))
                Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/alexisoffline/", UseShellExecute = true });
        });
        GluttonyChrome.Card("about_support", () =>
        {
            Widgets.Title("Support");
            Widgets.SecondaryWrapped("Conflict warnings stay visible above every page. Reports use the existing telemetry surface.");
            GluttonyCombo.P.Telemetry?.DrawReportButton();
        });
    }

    private static bool FooterEnabled() => AutoRotationController.cfg is { Enabled: true };

    private static float FooterHeight(float width) => width < 600 ? NarrowFooterHeight : WideFooterHeight;

    private static void DrawFooter()
    {
        if (!FooterEnabled())
            return;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = FooterHeight(width);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar));
        draw.AddLine(origin, origin + new Vector2(width, 0), Theme.Pack(Theme.SurfaceBorder));
        var state = LiveState();
        var paused = AutoRotationController.Paused;
        var narrow = width < 600;
        var stopSize = new Vector2(88, 36);
        var stopPos = origin + new Vector2(width - stopSize.X, narrow ? 52 : 16);
        var textRight = narrow ? origin.X + width : stopPos.X - 16;
        var textPos = origin + new Vector2(24, 9);
        draw.AddCircleFilled(origin + new Vector2(10, 19), Theme.DotRadius + 1, Theme.Pack(Copy.MarkColor(state)));
        GluttonyChrome.DrawText(draw, textPos,
            GluttonyChrome.FitText($"{Copy.Word(state)} · AutoRotation", textRight - textPos.X), Theme.TextPrimary);
        GluttonyChrome.DrawText(draw, textPos + new Vector2(0, 27),
            GluttonyChrome.FitText(paused ? "Blocked · automatic actions are paused" : "Automatic action replacement is enabled",
                textRight - textPos.X), Theme.TextSecondary);
        ImGui.SetCursorScreenPos(stopPos);
        if (Widgets.StopButton("Stop"))
            AutoRotationController.ToggleAutoRotation(false);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
    }

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
