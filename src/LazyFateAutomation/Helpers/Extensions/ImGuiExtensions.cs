using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using System;
using System.Numerics;
using System.Collections.Generic;

namespace LazyFateAutomation.Helpers.Extensions;

public static class ImGuiExtensions {
    public static void TooltipOnHover(string text) {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    public static void TooltipOnHover(bool condition, string text) {
        if (condition && ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    public static void TextV(string s) {
        ImGui.AlignTextToFramePadding();
        ImGui.Text(s);
    }

    public static void TextV(Vector4 c, string s) {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(c, s);
    }

    public static void TextV(EzColor c, string s) {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(c.Vector4, s);
    }

    public static float IconUnitHeight() => ImGuiHelpers.GetButtonSize(FontAwesomeIcon.Trash.ToIconString()).Y;
    public static float IconUnitWidth() => ImGuiHelpers.GetButtonSize(FontAwesomeIcon.Trash.ToIconString()).X;

    public static Vector4 GetContrastingTextColor(Vector4 backgroundColor) {
        var luminance = 0.299f * backgroundColor.X + 0.587f * backgroundColor.Y + 0.114f * backgroundColor.Z;
        return luminance > 0.5f ? new Vector4(0, 0, 0, backgroundColor.W) : new Vector4(1, 1, 1, backgroundColor.W);
    }

    public static Vector4 GetProgressBarTextColor(Vector4 filledColor, Vector4 backgroundColor, float percentage, float textStartX, float textWidth, float barWidth) {
        var filledEndX = barWidth * percentage;
        var textEndX = textStartX + textWidth;

        var overlapStart = Math.Max(textStartX, 0);
        var overlapEnd = Math.Min(textEndX, filledEndX);
        var textOverFilled = Math.Max(0, overlapEnd - overlapStart);
        var textOverBackground = textWidth - textOverFilled;

        Vector4 dominantColor;
        if (textOverFilled > textOverBackground)
            dominantColor = filledColor;
        else if (textOverBackground > textOverFilled)
            dominantColor = backgroundColor;
        else {
            var blendFactor = 0.5f;
            dominantColor = new Vector4(
                filledColor.X * blendFactor + backgroundColor.X * (1 - blendFactor),
                filledColor.Y * blendFactor + backgroundColor.Y * (1 - blendFactor),
                filledColor.Z * blendFactor + backgroundColor.Z * (1 - blendFactor),
                filledColor.W * blendFactor + backgroundColor.W * (1 - blendFactor)
            );
        }

        return GetContrastingTextColor(dominantColor);
    }

    public static void SpacedSeparator() {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    public static void DragDropSource(int index, ReadOnlySpan<byte> payloadType, string? dragPreviewText = null) {
        using var source = ImRaii.DragDropSource();
        if (source) {
            if (!string.IsNullOrEmpty(dragPreviewText))
                ImGui.Text(dragPreviewText);
            ImGui.SetDragDropPayload(payloadType, BitConverter.GetBytes(index), ImGuiCond.None);
        }
    }

    public static void DragDropTarget(int targetIndex, ReadOnlySpan<byte> payloadType, int listCount, Action<int, int> onReorder) {
        using var target = ImRaii.DragDropTarget();
        if (target) {
            var payload = ImGui.AcceptDragDropPayload(payloadType);
            unsafe {
                if (!payload.IsNull && payload.IsDelivery() && payload.Data != null && payload.DataSize == sizeof(int)) {
                    var sourceIndex = *(int*)payload.Data;
                    if (sourceIndex != targetIndex && sourceIndex >= 0 && sourceIndex < listCount) {
                        var insertIndex = sourceIndex < targetIndex ? targetIndex + 1 : targetIndex;
                        onReorder(sourceIndex, insertIndex);
                    }
                }
            }
        }
    }
}
