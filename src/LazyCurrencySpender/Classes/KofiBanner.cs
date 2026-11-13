using Dalamud.Interface.Utility;

namespace CurrencySpender.Classes;
public static class KofiBanner
{
    public static string Text = "♥ Ko-fi";
    public static string DonateLink => "https://ko-fi.com/catz1911";
    public static void DrawRaw()
    {
        DrawButton();
    }

    // Kit colours (R4): suite surfaces only - no ad-hoc hex or gradient colours.
    private static uint ColorNormal => LalaUiKit.Theme.Pack(LalaUiKit.Theme.Surface);

    private static uint ColorHovered => ColorNormal;

    private static uint ColorActive => ColorNormal;

    private static readonly uint ColorText = LalaUiKit.Theme.Pack(LalaUiKit.Theme.TextPrimary);

    public static void DrawButton()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, ColorNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorActive);
        ImGui.PushStyleColor(ImGuiCol.Text, ColorText);
        if (ImGui.Button(Text))
        {
            GenericHelpers.ShellStart(DonateLink);
        }
        Popup();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        ImGui.PopStyleColor(4);
    }

    public static void RightTransparentTab(string? text = null)
    {
        text ??= Text;
        var textWidth = ImGui.CalcTextSize(text).X;
        var spaceWidth = ImGui.CalcTextSize(" ").X;
        ImGui.BeginDisabled();
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0f);
        if (ImGui.BeginTabItem(" ".Repeat((int)MathF.Ceiling(textWidth / spaceWidth)), ImGuiTabItemFlags.Trailing))
        {
            ImGui.EndTabItem();
        }
        ImGui.PopStyleVar();
        ImGui.EndDisabled();
    }

    public static void DrawRight()
    {
        var cur = ImGui.GetCursorPos();
        ImGui.SetCursorPosX(cur.X + ImGui.GetContentRegionAvail().X - ImGuiHelpers.GetButtonSize(Text).X);
        DrawRaw();
        ImGui.SetCursorPos(cur);
    }

    private static string SmallPatreonButtonTooltip => $"""
				If you like {Service.PluginInterface.Manifest.Name},
				please consider supporting its developer via Ko-fi.
				""";

    private static void Popup()
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
            ImGuiEx.Text(SmallPatreonButtonTooltip);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
    }
}


