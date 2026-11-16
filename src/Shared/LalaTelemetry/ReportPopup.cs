// Shared source (NOT a shared DLL) - the "Report a problem" button + popup shared by every plugin window.
using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

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

        // The popup is drawn on the draw list at measured positions; the window is auto-sized to that content with no
        // padding, so the header and footer bands run edge to edge.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32(ImGuiCol.WindowBg)) with { W = 1f });
        var open = ImGui.BeginPopupModal(
            PopupId,
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        if (!open)
            return;

        Theme.PushFrame();
        try
        {
            DrawContent();
        }
        finally
        {
            Theme.PopFrame();
        }

        ImGui.EndPopup();
    }

    private void DrawContent()
    {
        const float width = 520f;
        const float pad = 16f;
        const float inputHeight = 110f;
        var winPos = ImGui.GetWindowPos();
        var gutter = PopupChrome.Gutter(width);
        var draw = ImGui.GetWindowDrawList();
        var sm = Theme.SecondaryScale;

        var y = PopupChrome.HeaderBand(draw, winPos, width, gutter, _owner.Hub.Identity.DisplayName, "##lalatelemetry-report-close", out var closeClicked);
        y += PopupChrome.PageHeader(draw, winPos, width, gutter, y, "Report a problem", Description, string.Empty);
        y += 16;

        // ---- card: the question, the text box and (after a write) the report id ----
        var inner = width - (2 * gutter) - (2 * pad);
        var promptH = PopupChrome.WrappedHeight(Prompt, inner);
        var statusH = _lastId is null ? 0f : 8 + PopupChrome.LineHeight(sm);
        var cardH = pad + promptH + 8 + inputHeight + statusH + pad;
        var cardPos = winPos + new Vector2(gutter, y);
        PopupChrome.CardBackground(draw, cardPos, new Vector2(width - (2 * gutter), cardH));
        PopupChrome.Text(draw, cardPos + new Vector2(pad, pad), Prompt, Theme.TextPrimary, 1f, inner);

        var inputPos = cardPos + new Vector2(pad, pad + promptH + 8);
        ImGui.SetCursorScreenPos(inputPos);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.ShellBar);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.SurfaceBorder);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, PopupChrome.Radius);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 8));
        ImGui.InputTextMultiline("##lalatelemetry-report-text", ref _text, MaxChars, new Vector2(inner, inputHeight));
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);

        if (_lastId is not null)
            PopupChrome.Text(draw, inputPos + new Vector2(0, inputHeight + 8), $"Report {_lastId} written.", Theme.TextSecondary, sm);

        y += cardH + 16;

        // ---- footer: Cancel and Write report, right aligned ----
        PopupChrome.FooterBand(draw, winPos, new Vector2(width, y + PopupChrome.FooterHeight), y);
        const string send = "Write report";
        const string cancel = "Cancel";
        var sendSize = new Vector2(PopupChrome.ActionWidth(96, send), PopupChrome.Control);
        var cancelSize = new Vector2(PopupChrome.ActionWidth(96, cancel), PopupChrome.Control);
        var buttonY = y + ((PopupChrome.FooterHeight - PopupChrome.Control) / 2f);
        var sendPos = new Vector2(width - gutter - sendSize.X, buttonY);
        var cancelPos = new Vector2(sendPos.X - PopupChrome.Gap - cancelSize.X, buttonY);

        var write = PopupChrome.ActionButton(draw, "##lalatelemetry-report-send", send, winPos + sendPos, sendSize);
        var dismiss = PopupChrome.ActionButton(draw, "##lalatelemetry-report-cancel", cancel, winPos + cancelPos, cancelSize);

        // Reserve the full popup extent so the auto-sized window matches the drawn bands exactly.
        ImGui.SetCursorScreenPos(winPos);
        ImGui.Dummy(new Vector2(width, y + PopupChrome.FooterHeight));

        if (write)
        {
            var id = _owner.FileReport(_text);
            if (id is not null)
            {
                _lastId = id;
                _text = string.Empty;
                ImGui.CloseCurrentPopup();
            }
        }
        else if (dismiss || closeClicked)
        {
            ImGui.CloseCurrentPopup();
        }
    }

    private const string Prompt = "What happened, and what was expected instead?";

    private const string Description = "The report is written to the local plugin log (dalamud.log) together with the current zone, job, target, recent plugin activity and the windows that are open.";
}
