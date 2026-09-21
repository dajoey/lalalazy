// Shared source (NOT a shared DLL) - the "Report a problem" button + popup shared by every plugin window.
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Lalalazy.Telemetry;

/// <summary>
/// A button (or a trailing tab-bar button) that opens a small modal for the free text, then files an RP|
/// report through <see cref="DalamudTelemetry.FileReport"/>. The popup is opened and drawn in the same call,
/// so the ImGui ID stack always matches.
/// </summary>
internal sealed class ReportPopup
{
    private const string PopupId = "Report a problem##lalatelemetry-report";
    private const int MaxChars = ReportBuilder.MaxTextChars;

    private readonly DalamudTelemetry _owner;
    private string _text = string.Empty;
    private bool _openRequested;
    private string? _lastId;

    public ReportPopup(DalamudTelemetry owner)
    {
        _owner = owner;
    }

    public void DrawButton(string label)
    {
        if (ImGui.Button(label + "##lalatelemetry-report-button"))
            _openRequested = true;
        DrawPopup();
    }

    public void DrawTabButton(string label)
    {
        if (ImGui.TabItemButton(label + "##lalatelemetry-report-tab", ImGuiTabItemFlags.Trailing | ImGuiTabItemFlags.NoTooltip))
            _openRequested = true;
        DrawPopup();
    }

    private void DrawPopup()
    {
        if (_openRequested)
        {
            _openRequested = false;
            _lastId = null;
            ImGui.OpenPopup(PopupId);
        }

        ImGui.SetNextWindowSize(new Vector2(460, 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(PopupId, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextWrapped("What happened, and what was expected instead? The report is written to the local plugin log (dalamud.log) together with the current zone, job, target, recent plugin activity and the windows that are open.");
        ImGui.Spacing();
        ImGui.InputTextMultiline("##lalatelemetry-report-text", ref _text, MaxChars, new Vector2(440, 110));

        if (_lastId is not null)
            ImGui.TextDisabled($"Report {_lastId} written.");

        if (ImGui.Button("Write report##lalatelemetry-report-send"))
        {
            var id = _owner.FileReport(_text);
            if (id is not null)
            {
                _lastId = id;
                _text = string.Empty;
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel##lalatelemetry-report-cancel"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }
}
