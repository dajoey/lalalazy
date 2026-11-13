using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using LalaUiKit;

namespace ArmoireAutoFill.Windows;

// Converted to LalaUiKit per Projects/UI Refresh 2026-11/UI Brief (R1-R13).
// The Dalamud Window base auto-begins/ends this window (Draw() draws content only),
// so the top bar is drawn with the same kit primitives Layout.Shell uses, and the
// suite frame comes from Theme.PushFrame/PopFrame. No background activity lives in
// this window, so it ships Ready only and omits the footer (Brief R3).
public class ConfigWindow : Window
{
    public ConfigWindow()
        : base("Armoire Auto-Fill Settings###ArmoireAutoFillConfig")
    {
        Size = new Vector2(560, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        Theme.PushFrame();
        try
        {
            DrawTopBar();
            Layout.NavSection("aaf_cfg_nav_settings", "Settings", null, FontAwesomeIcon.Cog.ToIconString(), UiState.Ready, true);
            ImGui.Dummy(new Vector2(0, Theme.Unit));

            Layout.PageHeader(null, FontAwesomeIcon.Cog.ToIconString(), "Settings",
                "How the scanner and the armoire filling behave.",
                "1 of 2 options");

            var scanOnLoad = Plugin.Configuration.ScanOnLoad;
            if (Widgets.Checkbox("Auto-scan on login", ref scanOnLoad))
            {
                Plugin.Configuration.ScanOnLoad = scanOnLoad;
                Plugin.Configuration.Save();
            }
            Widgets.Secondary("Automatically scan your inventory when you log in, so the Collection tab is current without a manual rescan.");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Automatically scan your inventory when you log in");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            Widgets.Title("About");
            Widgets.SecondaryWrapped(
                "Armoire Auto-Fill helps you collect all armoire-compatible dungeon gear (level 31+). "
                + "It scans your inventory and shows what pieces you're missing from each dungeon. "
                + "Open the armoire UI at an inn once per session to populate the in-armoire state; "
                + "in-inventory and equipped state is detected automatically.");
        }
        finally
        {
            Theme.PopFrame();
        }
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
        ImGui.TextUnformatted("Armoire Auto-Fill");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPosX(p.X + w - (2 * Theme.Unit));
        Widgets.StatusPill(UiState.Ready, "Settings", null, alignRight: true);
        ImGui.SetCursorPosY(p.Y + barH + Theme.ItemSpacing.Y);
    }
}
