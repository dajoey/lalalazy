using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace LazyCrucible;

/// <summary> Suggestion-only note anchored above the game screen it describes. It never clicks a choice.</summary>
internal static unsafe class SuggestionOverlay
{
    public static void Draw()
    {
        if (SelectionScreens.Suggestions.Count == 0)
            return;
        var draw = ImGui.GetForegroundDrawList();
        foreach (var (addonName, text) in SelectionScreens.Suggestions)
        {
            if (!ScreenReader.TryGet(addonName, out var addon) || addon->RootNode is null)
                continue;
            var scale = addon->Scale <= 0 ? 1f : addon->Scale;
            var pos = new Vector2(addon->X, addon->Y) + ImGui.GetMainViewport().Pos;
            var width = addon->RootNode->Width * scale;
            var height = addon->RootNode->Height * scale;
            var wrap = Math.Max(240f, width);
            var textWidth = wrap - 64;
            const string title = "LazyCrucible: Suggested choice";
            const string detail = "Suggestion only · no button is pressed and the choice stays with the player.";
            var titleSize = ImGui.GetFontSize() * Theme.TitleScale;
            var detailSize = ImGui.GetFontSize() * Theme.SecondaryScale;
            var titleHeight = ImGui.CalcTextSize(title).Y * Theme.TitleScale;
            var bodyHeight = ImGui.CalcTextSize(text, false, textWidth).Y;
            var detailHeight = ImGui.CalcTextSize(detail, false, textWidth).Y * Theme.SecondaryScale;
            var boxHeight = titleHeight + bodyHeight + detailHeight + (8 * Theme.Unit);
            var boxMin = new Vector2(pos.X - 2, pos.Y - (2 * Theme.Unit) - boxHeight);
            var boxMax = new Vector2(pos.X + wrap, pos.Y - (2 * Theme.Unit));
            var accent = Theme.Pack(Theme.Accent);

            // The accent edge is clipped to the box's own rounded corners instead of sitting outside them.
            draw.AddRectFilled(boxMin, boxMax, Theme.Pack(Theme.Surface with { W = 0.96f }), 2 * Theme.Unit);
            draw.PushClipRect(boxMin, boxMin + new Vector2(3, boxHeight), true);
            draw.AddRectFilled(boxMin, boxMax, accent, 2 * Theme.Unit);
            draw.PopClipRect();
            draw.AddRect(boxMin, boxMax, Theme.Pack(Theme.SurfaceBorder), 2 * Theme.Unit);
            var iconCenter = boxMin + new Vector2(24, 8 + (titleHeight / 2));
            var radius = 7f;
            draw.AddTriangleFilled(iconCenter + new Vector2(0, -radius), iconCenter + new Vector2(radius, 0), iconCenter + new Vector2(0, radius), accent);
            draw.AddTriangleFilled(iconCenter + new Vector2(0, -radius), iconCenter + new Vector2(0, radius), iconCenter + new Vector2(-radius, 0), accent);
            draw.AddText(ImGui.GetFont(), titleSize, boxMin + new Vector2(48, 8), accent, title);
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), boxMin + new Vector2(48, 12 + titleHeight), Theme.Pack(Theme.TextPrimary), text, textWidth);
            draw.AddText(ImGui.GetFont(), detailSize, boxMin + new Vector2(48, 20 + titleHeight + bodyHeight), Theme.Pack(Theme.TextSecondary), detail, textWidth);
            draw.AddRect(pos, pos + new Vector2(width, height), accent, 6f, ImDrawFlags.None, 2f);
        }
    }
}
