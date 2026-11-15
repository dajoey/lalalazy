// Enum aliases keep the original Dalamud using directives source-compatible.
global using ImGuiCol = ImGuiNET.ImGuiCol;
global using ImGuiCond = ImGuiNET.ImGuiCond;
global using ImDrawFlags = ImGuiNET.ImDrawFlags;
global using ImGuiWindowFlags = ImGuiNET.ImGuiWindowFlags;
global using ImGuiStyleVar = ImGuiNET.ImGuiStyleVar;

using System.Numerics;
using ImGuiNET;
using NativeImGui = ImGuiNET.ImGui;

namespace Dalamud.Bindings.ImGui;

// Dalamud's texture handle wrapper; ImGui.NET 1.91.6 accepts the underlying IntPtr.
public readonly struct ImTextureID
{
    public ImTextureID(IntPtr value) => Value = value;
    public IntPtr Value { get; }
    public static implicit operator IntPtr(ImTextureID id) => id.Value;
    public static implicit operator ImTextureID(IntPtr value) => new(value);
}

// Source-compatibility adapter for the real UI files and kit. Every call forwards
// to ImGui.NET; drawing and layout decisions stay in the linked production sources.
public static class ImGui
{
    public static void TextColored(Vector4 color, string text) => NativeImGui.TextColored(color, text);
    public static void TextDisabled(string text) => NativeImGui.TextDisabled(text);
    public static void TextWrapped(string text) => NativeImGui.TextWrapped(text);
    public static void TextUnformatted(string text) => NativeImGui.TextUnformatted(text);
    public static bool Button(string label) => NativeImGui.Button(label);
    public static bool Checkbox(string label, ref bool value) => NativeImGui.Checkbox(label, ref value);
    public static void SameLine() => NativeImGui.SameLine();
    public static void Separator() => NativeImGui.Separator();
    public static void Spacing() => NativeImGui.Spacing();
    public static void Indent() => NativeImGui.Indent();
    public static void Unindent() => NativeImGui.Unindent();
    public static void PushStyleColor(ImGuiCol index, Vector4 color) => NativeImGui.PushStyleColor(index, color);
    public static void PushStyleColor(ImGuiCol index, uint color) => NativeImGui.PushStyleColor(index, color);
    public static void PopStyleColor() => NativeImGui.PopStyleColor();
    public static void PopStyleColor(int count) => NativeImGui.PopStyleColor(count);
    public static void PushStyleVar(ImGuiStyleVar index, float value) => NativeImGui.PushStyleVar(index, value);
    public static void PushStyleVar(ImGuiStyleVar index, Vector2 value) => NativeImGui.PushStyleVar(index, value);
    public static void PopStyleVar() => NativeImGui.PopStyleVar();
    public static void PopStyleVar(int count) => NativeImGui.PopStyleVar(count);
    public static ImGuiStylePtr GetStyle() => NativeImGui.GetStyle();
    public static void SetNextItemWidth(float width) => NativeImGui.SetNextItemWidth(width);
    public static bool Combo(string label, ref int currentItem, string items) => NativeImGui.Combo(label, ref currentItem, items);
    public static bool CollapsingHeader(string label) => NativeImGui.CollapsingHeader(label);
    public static bool RadioButton(string label, ref int value, int button) => NativeImGui.RadioButton(label, ref value, button);
    public static bool RadioButton(string label, bool active) => NativeImGui.RadioButton(label, active);
    public static bool TreeNode(string label) => NativeImGui.TreeNode(label);
    public static void TreePop() => NativeImGui.TreePop();
    public static void BulletText(string text) => NativeImGui.BulletText(text);
    public static void Bullet() => NativeImGui.Bullet();
    public static bool BeginCombo(string label, string preview) => NativeImGui.BeginCombo(label, preview);
    public static void EndCombo() => NativeImGui.EndCombo();
    public static bool Selectable(string label, bool selected) => NativeImGui.Selectable(label, selected);
    public static bool BeginChild(string id, Vector2 size, bool border) =>
        NativeImGui.BeginChild(id, size, border ? ImGuiChildFlags.Borders : ImGuiChildFlags.None);
    public static void EndChild() => NativeImGui.EndChild();
    public static bool Begin(string name, ImGuiWindowFlags flags) => NativeImGui.Begin(name, flags);
    public static bool Begin(string name, ref bool open, ImGuiWindowFlags flags) => NativeImGui.Begin(name, ref open, flags);
    public static void End() => NativeImGui.End();
    public static void BeginGroup() => NativeImGui.BeginGroup();
    public static void EndGroup() => NativeImGui.EndGroup();
    public static void Dummy(Vector2 size) => NativeImGui.Dummy(size);
    public static void SetWindowFontScale(float scale) => NativeImGui.SetWindowFontScale(scale);
    public static Vector2 GetCursorScreenPos() => NativeImGui.GetCursorScreenPos();
    public static void SetCursorScreenPos(Vector2 pos) => NativeImGui.SetCursorScreenPos(pos);
    public static Vector2 GetCursorPos() => NativeImGui.GetCursorPos();
    public static void SetCursorPos(Vector2 pos) => NativeImGui.SetCursorPos(pos);
    public static float GetCursorPosX() => NativeImGui.GetCursorPosX();
    public static void SetCursorPosX(float x) => NativeImGui.SetCursorPosX(x);
    public static void SetCursorPosY(float y) => NativeImGui.SetCursorPosY(y);
    public static float GetWindowWidth() => NativeImGui.GetWindowWidth();
    public static Vector2 GetContentRegionAvail() => NativeImGui.GetContentRegionAvail();
    public static float GetTextLineHeight() => NativeImGui.GetTextLineHeight();
    public static float GetFrameHeightWithSpacing() => NativeImGui.GetFrameHeightWithSpacing();
    public static ImDrawListPtr GetWindowDrawList() => NativeImGui.GetWindowDrawList();
    public static bool InvisibleButton(string id, Vector2 size) => NativeImGui.InvisibleButton(id, size);
    public static bool IsItemHovered() => NativeImGui.IsItemHovered();
    public static bool IsItemClicked() => NativeImGui.IsItemClicked();
    public static void SetTooltip(string text) => NativeImGui.SetTooltip(text);
    public static void PushTextWrapPos(float wrapPosX) => NativeImGui.PushTextWrapPos(wrapPosX);
    public static void PopTextWrapPos() => NativeImGui.PopTextWrapPos();
    public static void SetNextWindowPos(Vector2 pos) => NativeImGui.SetNextWindowPos(pos);
    public static void SetWindowPos(string name, Vector2 pos) => NativeImGui.SetWindowPos(name, pos);
    public static void SetNextWindowBgAlpha(float alpha) => NativeImGui.SetNextWindowBgAlpha(alpha);
    public static void SetNextWindowSizeConstraints(Vector2 min, Vector2 max) => NativeImGui.SetNextWindowSizeConstraints(min, max);
    public static void Image(ImTextureID texture, Vector2 size) => NativeImGui.Image(texture, size);
    public static bool SliderFloat(string label, ref float value, float min, float max, string format) =>
        NativeImGui.SliderFloat(label, ref value, min, max, format);
    public static uint ColorConvertFloat4ToU32(Vector4 color) => NativeImGui.ColorConvertFloat4ToU32(color);
    public static ImDrawListPtr GetForegroundDrawList() => NativeImGui.GetForegroundDrawList();
    public static ImGuiViewportPtr GetMainViewport() => NativeImGui.GetMainViewport();
    public static Vector2 CalcTextSize(string text) => NativeImGui.CalcTextSize(text);
    public static Vector2 CalcTextSize(string text, bool hideTextAfterDoubleHash, float wrapWidth) =>
        NativeImGui.CalcTextSize(text, hideTextAfterDoubleHash, wrapWidth);
    public static uint GetColorU32(Vector4 color) => NativeImGui.GetColorU32(color);
    public static ImFontPtr GetFont() => NativeImGui.GetFont();
    public static float GetFontSize() => NativeImGui.GetFontSize();
}
