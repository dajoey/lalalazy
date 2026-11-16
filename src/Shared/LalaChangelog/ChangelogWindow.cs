// Shared source (NOT a shared DLL) - compiled into every lalalazy plugin.
using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;

namespace Lalalazy.Changelog;

/// <summary>
/// The "What's new" popup. One collapsible block per version (newest expanded), coloured section
/// headings, word-wrapped bullets, "Got it" + "Open changelog on GitHub" buttons.
/// </summary>
public sealed class ChangelogWindow : Window
{
    private readonly string _displayName;
    private readonly string _githubUrl;
    private readonly Action _onDismiss;

    private IReadOnlyList<ChangelogEntry> _entries = Array.Empty<ChangelogEntry>();
    private Version _current = new(0, 0, 0, 0);
    private Version? _previous;
    private bool _firstOpen = true;

    public ChangelogWindow(string displayName, string githubUrl, Action onDismiss)
        : base($"{displayName} \u2014 What's new##lala-changelog", ImGuiWindowFlags.NoCollapse)
    {
        _displayName = displayName;
        _githubUrl = githubUrl;
        _onDismiss = onDismiss;

        Size = new Vector2(560, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 260),
            MaximumSize = new Vector2(1100, 900),
        };
        RespectCloseHotkey = true;
    }

    public void Show(IReadOnlyList<ChangelogEntry> entries, Version current, Version? previous)
    {
        _entries = entries;
        _current = current;
        _previous = previous;
        WindowName = $"{_displayName} \u2014 What's new in v{current}##lala-changelog";
        IsOpen = true;
        BringToFront();
    }

    public override void OnOpen()
    {
        if (_firstOpen)
        {
            // Centre on the main viewport the first time the window ever appears.
            var vp = ImGui.GetMainViewport();
            var size = Size ?? new Vector2(560, 480);
            Position = vp.Pos + (vp.Size - size) * 0.5f;
            PositionCondition = ImGuiCond.FirstUseEver;
            _firstOpen = false;
        }
    }

    public override void OnClose()
    {
        // Closing with the X or Escape counts as "seen" too, otherwise it re-pops every login.
        _onDismiss();
    }

    public override void Draw()
    {
        var style = ImGui.GetStyle();
        var footer = ImGui.GetFrameHeightWithSpacing() + style.ItemSpacing.Y * 2 + 1;

        // ---- header ----
        ImGui.TextColored(ImGuiColors.ParsedGold, $"{_displayName} v{_current}");
        ImGui.SameLine();
        if (_previous is not null && _previous > new Version(0, 0, 0, 0))
            ImGui.TextDisabled($"updated from v{_previous}");
        else
            ImGui.TextDisabled("release notes");
        ImGui.Separator();
        ImGui.Spacing();

        // ---- scrollable body ----
        if (ImGui.BeginChild("##lala-changelog-body", new Vector2(0, -footer), false))
        {
            if (_entries.Count == 0)
            {
                ImGui.TextDisabled("No release notes were embedded in this build.");
            }
            else
            {
                for (var i = 0; i < _entries.Count; i++)
                    DrawEntry(_entries[i], expanded: i == 0);
            }
        }
        ImGui.EndChild();

        // ---- footer ----
        ImGui.Separator();
        if (ImGui.Button("Got it", new Vector2(120, 0)))
        {
            IsOpen = false; // OnClose -> _onDismiss persists LastSeen
        }
        ImGui.SameLine();
        if (ImGui.Button("Open changelog on GitHub"))
            Util.OpenLink(_githubUrl);
        ImGui.SameLine();
        ImGui.TextDisabled("lalalazy");
    }

    private static void DrawEntry(ChangelogEntry e, bool expanded)
    {
        var label = e.Date is null ? $"v{e.VersionText}" : $"v{e.VersionText}  ({e.Date})";
        if (expanded) ImGui.SetNextItemOpen(true, ImGuiCond.FirstUseEver);
        if (!ImGui.CollapsingHeader($"{label}##lala-cl-{e.VersionText}")) return;

        ImGui.Indent(8f);
        foreach (var s in e.Sections)
        {
            ImGui.Spacing();
            ImGui.TextColored(ColorFor(s.Kind), s.Name);
            ImGui.Indent(12f);
            foreach (var b in s.Bullets)
                ImGui.TextWrapped("\u2022 " + b);
            ImGui.Unindent(12f);
        }
        ImGui.Spacing();
        ImGui.Unindent(8f);
    }

    private static Vector4 ColorFor(ChangelogSectionKind kind) => kind switch
    {
        ChangelogSectionKind.Added => ImGuiColors.HealerGreen,
        ChangelogSectionKind.Changed => ImGuiColors.DalamudOrange,
        ChangelogSectionKind.Fixed => ImGuiColors.TankBlue,
        ChangelogSectionKind.Removed => ImGuiColors.DalamudRed,
        ChangelogSectionKind.Notes => ImGuiColors.DalamudGrey,
        _ => ImGuiColors.DalamudWhite,
    };
}
