using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using ECommons.Throttlers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using GluttonyCombo.AutoRotation;
using GluttonyCombo.Core;
using GluttonyCombo.Data.Conflicts;
using GluttonyCombo.Resources.Localization.UI.MainWindow;
using GluttonyCombo.Services;
using GluttonyCombo.Window.Functions;
using GluttonyCombo.Window.Tabs;
using LalaUiKit;
using static GluttonyCombo.Core.PresetStorage;
using static GluttonyCombo.CustomComboNS.Functions.Jobs;
using Debug = GluttonyCombo.Window.Tabs.Debug;
using PunishGui = PunishLib.ImGuiMethods;
namespace GluttonyCombo.Window;

/// <summary> Plugin configuration window. </summary>
internal class ConfigWindow : Dalamud.Interface.Windowing.Window
{
    /// <summary>
    /// Dictionary of top level presets grouped by job, ordered by role and then job order, with their preset data pre-cached for quick access.
    /// </summary>
    internal static readonly Dictionary<Job, List<PresetData>> groupedPresets = GetGroupedPresets();

    /// <summary>
    ///  Dictionary of a preset and an array of it's children, with their preset data pre-cached for quick access.
    /// </summary>
    internal static readonly Dictionary<Preset, (Preset Preset, PresetData Attr)[]> presetChildren = GetPresetChildren();


    #region Search Variables
    internal static string Search = string.Empty;
    internal static string UsableSearch => Search.Trim().ToLowerInvariant();
    internal static bool SearchDescription = true;

    internal static bool IsSearching => !UsableSearch.IsNullOrWhitespace() &&
                                        UsableSearch.Length > 2;
    #endregion

    private static int GetRoleOrder(JobRole role) => role switch
    {
        JobRole.Tank => 0,
        JobRole.Healer => 1,
        JobRole.MeleeDPS => 2,
        JobRole.RangedDPS => 3,
        JobRole.MagicalDPS => 4,
        JobRole.Limited => 5,
        _ => 99
    };

    internal static Dictionary<Job, List<PresetData>> GetGroupedPresets()
    {
        return AllPresets
            .Where(kvp => (int)kvp.Key > 100)
            .Where(kvp => kvp.Value.Parent == null)
            .Where(kvp => kvp.Value.JobInfo != null)
            .OrderBy(kvp => kvp.Value.JobInfo.Job is Job.ADV ? 5 : GetRoleOrder(kvp.Value.JobInfo.Role))
            .ThenByDescending(kvp => kvp.Value.JobInfo.Job is Job.ADV)
            .ThenByDescending(kvp => kvp.Value.JobInfo.Job is Job.MIN)
            .ThenBy(kvp => kvp.Value.JobInfo.Job)
            .ThenBy(kvp => kvp.Value.JobInfo.Order)
            .GroupBy(kvp => kvp.Value.JobInfo.Job)
            .ToDictionary(
                g => g.Key,
                g => g.Select(kvp => kvp.Value).ToList()
            );
    }

    internal static Dictionary<Preset, (Preset Preset, PresetData Info)[]> GetPresetChildren()
    {
        // Initialize dictionary with all presets as keys
        var childCombos = AllPresets.Keys
            .ToDictionary(p => p, _ => new List<Preset>());

        // Build parent → children map using cached Parent
        foreach (var (preset, attrs) in AllPresets)
        {
            if (attrs.Parent is { } parent)
            {
                childCombos[parent].Add(preset);
            }
        }

        // Project to final structure using cached CustomComboInfo
        return childCombos.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
                .Select(child =>
                {
                    var info = PresetStorage.AllPresets[child]!;
                    return (Preset: child, Info: info);
                })
                .OrderBy(tpl => tpl.Info.JobInfo.Order)
                .ToArray()
        );
    }

    public OpenWindow OpenWindow
    {
        get;
        set
        {
            ClearAnySearches();
            field = value;
        }
    } = OpenWindow.PvE;

    /// <summary> Initializes a new instance of the <see cref="ConfigWindow"/> class. </summary>
    public ConfigWindow() : base($"{GluttonyCombo.P.Name} {GluttonyCombo.P.GetType().Assembly.GetName().Version}###WrathCombo")
    {
        RespectCloseHotkey = true;

        SetMinSize();

        Svc.PluginInterface.UiBuilder.DefaultFontHandle.ImFontChanged += SetMinSize;
    }

    private void SetMinSize(IFontHandle? fontHandle = null, ILockedImFont? lockedFont = null) =>
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(700, 100),
        };

    // ---- LalaUiKit conversion (UI Refresh 2026-11, Brief R1-R13) ----
    // The Dalamud Window base auto-begins/ends this window, so the Shell top bar is
    // drawn with the same kit primitives Layout.Shell uses, and the suite frame comes
    // from Theme.PushFrame/PopFrame. Background activity = AutoRotation; its live
    // state shows in the pill (R3 place 1), the nav dot (place 2) and the footer
    // with a Stop button (place 3).

    /// <summary>Live state of the plugin's background activity (AutoRotation), per Brief R5.</summary>
    internal static UiState AutoRotationState()
    {
        var cfg = AutoRotationController.cfg;
        if (cfg is null || !cfg.Enabled)
            return UiState.Ready;
        return AutoRotationController.Paused ? UiState.Blocked : UiState.Running;
    }

    public override void Draw()
    {
        Theme.PushFrame();
        try
        {
            DrawTopBar();
            if (!Service.Configuration.UILeftColumnCollapsed)
                DrawNav();
            else
                DrawExpandButton();
            DrawBody();
            DrawFooter();
        }
        finally
        {
            Theme.PopFrame();
        }

        Presets.DrawDragDrop();
    }

    private void DrawTopBar()
    {
        // Top bar: title left, live-state pill right (R3 place 1) — the same primitives
        // Layout.Shell draws for self-beginning windows.
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetWindowWidth() - (2 * Theme.Unit);
        var barH = Theme.PillHeight;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, p + new Vector2(w, barH), Theme.Pack(Theme.ShellBar), 4f);
        dl.AddRect(p, p + new Vector2(w, barH), Theme.Pack(Theme.SurfaceBorder), 4f);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(2 * Theme.Unit, (barH - ImGui.GetTextLineHeight()) / 2f));
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
        ImGui.TextUnformatted($"{GluttonyCombo.P.Name} {GluttonyCombo.P.GetType().Assembly.GetName().Version}");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPosX(p.X + w - (2 * Theme.Unit));
        var state = AutoRotationState();
        Widgets.StatusPill(state, "AutoRotation",
            state == UiState.Blocked ? "paused" : null, alignRight: true);
        ImGui.SetCursorPosY(p.Y + barH + Theme.ItemSpacing.Y);
    }

    private void DrawNav()
    {
        // Section/feature row (R1 layer 2): icon + live dot; the accent outline marks
        // exactly the selected entry (R4). The AutoRotation entry carries the live dot.
        if (Layout.NavSection("gc_nav_pve", MainWindowUI.Button_PvEFeatures, null,
                FontAwesomeIcon.ShieldAlt.ToIconString(), UiState.Ready,
                OpenWindow == OpenWindow.PvE))
            OpenWindow = OpenWindow.PvE;

        if (Layout.NavSection("gc_nav_pvp", MainWindowUI.Button_PvPFeatures, null,
                FontAwesomeIcon.SkullCrossbones.ToIconString(), UiState.Ready,
                OpenWindow == OpenWindow.PvP))
            OpenWindow = OpenWindow.PvP;

        if (Layout.NavSection("gc_nav_autorotation", MainWindowUI.Button_AutoRotation, null,
                FontAwesomeIcon.Cogs.ToIconString(), AutoRotationState(),
                OpenWindow == OpenWindow.AutoRotation))
            OpenWindow = OpenWindow.AutoRotation;

        if (Layout.NavSection("gc_nav_customactions", "Custom Actions", null,
                FontAwesomeIcon.HandPointer.ToIconString(), UiState.Ready,
                OpenWindow == OpenWindow.CustomActions))
            OpenWindow = OpenWindow.CustomActions;

        if (Layout.NavSection("gc_nav_settings", MainWindowUI.Button_Settings, null,
                FontAwesomeIcon.Cog.ToIconString(), UiState.Ready,
                OpenWindow == OpenWindow.Settings))
            OpenWindow = OpenWindow.Settings;

        if (Layout.NavSection("gc_nav_about", MainWindowUI.Button_About, null,
                FontAwesomeIcon.InfoCircle.ToIconString(), UiState.Ready,
                OpenWindow == OpenWindow.About))
            OpenWindow = OpenWindow.About;

#if DEBUG
        if (Layout.NavSection("gc_nav_debug", "DEBUG", null,
                FontAwesomeIcon.Bug.ToIconString(), UiState.Ready,
                OpenWindow == OpenWindow.Debug))
            OpenWindow = OpenWindow.Debug;
#endif

        // Fork (error reporting): shared "Report a problem" button + popup - writes an RP| report (game
        // state, recent decisions, open windows) to the plugin log. src/Shared/LalaTelemetry.
        ImGui.Dummy(new Vector2(0, Theme.Unit));
        P.Telemetry?.DrawReportButton();

        ConflictingPlugins.Draw();
    }

    private void DrawExpandButton()
    {
        // Sidebar collapse kept: when collapsed, one kit-styled button re-expands the
        // section row (no ad-hoc font scale, Brief R7).
        if (ImGuiEx.IconButton(FontAwesomeIcon.CaretRight, "CollapseButton"))
        {
            Service.Configuration.UILeftColumnCollapsed = false;
            Service.Configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(MainWindowUI.Button_ExpandSidebar);
    }

    private void DrawBody()
    {
        using var rightChild = ImRaii.Child("###WrathRightSide", Vector2.Zero, false);
        if (!rightChild) return;

        if (OpenWindow == OpenWindow.None)
            OpenWindow = OpenWindow.PvE;

        switch (OpenWindow)
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
                Layout.PageHeader(null, FontAwesomeIcon.InfoCircle.ToIconString(),
                    GluttonyCombo.P.Name,
                    "A lalalazy fork of Wrath Combo. All upstream credit to Team Wrath / PunishXIV.",
                    $"{groupedPresets.Values.Sum(list => list.Count)} job presets");
                ImGui.Dummy(new Vector2(0, Theme.Unit));
                if (ImGuiEx.Button("GitHub: dajoey/lalalazy"))
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "https://github.com/dajoey/lalalazy",
                        UseShellExecute = true
                    });
                }
                ImGui.SameLine();
                if (ImGuiEx.Button("Additional custom action icons by alexisoffline"))
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "https://ko-fi.com/alexisoffline/",
                        UseShellExecute = true
                    });
                }
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
        ;
    }

    private void DrawFooter()
    {
        // Footer (R3 place 3) for the plugin's background activity: current action and
        // a Stop button. Stop turns AutoRotation off — the same semantics as unchecking
        // its enable checkbox, saved immediately.
        var cfg = AutoRotationController.cfg;
        if (cfg is not { Enabled: true })
            return;

        Layout.Footer(
            AutoRotationController.Paused ? UiState.Blocked : UiState.Running,
            "AutoRotation",
            AutoRotationController.Paused ? "paused" : null,
            "Stop",
            out var stopClicked);
        if (stopClicked)
            AutoRotationController.ToggleAutoRotation(false);
    }

    public static void ClearAnySearches()
    {
        Search = string.Empty;
        SearchDescription = true;
    }

    public override void OnClose()
    {
        ClearAnySearches();

        // Normal close
        base.OnClose();
    }

    public void Dispose()
    {
        Svc.PluginInterface.UiBuilder.DefaultFontHandle.ImFontChanged -= SetMinSize;
    }
}

public enum OpenWindow
{
    None = 0,
    PvE = 1,
    PvP = 2,
    Settings = 3,
    AutoRotation = 4,
    About = 5,
    Debug = 6,
    CustomActions = 7,
}
