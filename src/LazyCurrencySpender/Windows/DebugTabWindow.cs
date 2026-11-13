using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace CurrencySpender.Windows;

// Converted to LalaUiKit per Projects/UI Refresh 2026-11/UI Brief (R1-R13). The Dalamud
// Window base auto-begins/ends this window (Draw() draws content only), so the top bar is
// drawn with the same kit primitives Layout.Shell uses, and the suite frame comes from
// Theme.PushFrame/PopFrame. Diagnostic-only window: no footer (Brief R3).
internal class DebugTabWindow : Window
{
    public DebugTabWindow() : base($"")
    {
        SizeConstraints = new()
        {
            MinimumSize = new(250, 100),
            MaximumSize = new(9999, 9999)
        };
        P.ws.AddWindow(this);
    }

    public override void PreDraw()
    {
        WindowName = $"{P.Name} Debug {P.Version}###DebugTabWindow";
    }

    public override void Draw()
    {
        Theme.PushFrame();
        try
        {
            DrawTopBar();

            Layout.PageHeader(null, FontAwesomeIcon.Bug.ToIconString(), "Debug",
                "Raw agent and rank data for diagnosis.",
                P.Version);
            DebugMainTab.Draw();
        }
        finally
        {
            Theme.PopFrame();
        }
    }

    private void DrawTopBar()
    {
        // Top bar: title left, live-state pill right (R3 place 1) - the same primitives
        // Layout.Shell draws for self-beginning windows.
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetWindowWidth() - (2 * Theme.Unit);
        var barH = Theme.PillHeight;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, p + new Vector2(w, barH), Theme.Pack(Theme.ShellBar), 4f);
        dl.AddRect(p, p + new Vector2(w, barH), Theme.Pack(Theme.SurfaceBorder), 4f);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(2 * Theme.Unit, (barH - ImGui.GetTextLineHeight()) / 2f));
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
        ImGui.TextUnformatted($"{P.Name} Debug");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPosX(p.X + w - (2 * Theme.Unit));
        Widgets.StatusPill(UiState.Idle, "Debug", null, alignRight: true);
        ImGui.SetCursorPosY(p.Y + barH + Theme.ItemSpacing.Y);
    }
}
