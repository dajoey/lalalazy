using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Lumina.Excel.Sheets;
using System.Diagnostics.CodeAnalysis;

namespace LazyFateAutomation.Helpers.Extensions;

public static partial class ImGuiExtensions {
    internal static string searchResultsQuery = string.Empty;
    internal static double lastSearchTime;
    internal static Item[] itemSearchResults = [];

    extension(ImGui) {
        public static bool AddItemPopupButton([NotNullWhen(true)] out Item? result, string? buttonLabel = null, Vector2? size = null, Func<Item, bool>? itemSheetFilter = null) {
            result = null;
            if (ImGui.Button(buttonLabel ?? "Add Item", size ?? new Vector2(-1, 0))) {
                searchResultsQuery = "";
                ImGui.OpenPopup("item_search_add");
            }

            using var popup = ImRaii.Popup("item_search_add");
            if (!popup) return false;

            ImGui.Text("Search:");
            var currentTime = ImGui.GetTime();
            if (ImGui.GetTime() > lastSearchTime + 0.1f) {
                lastSearchTime = currentTime;
                itemSearchResults = !string.IsNullOrEmpty(searchResultsQuery)
                    ? uint.TryParse(searchResultsQuery, out var searchId)
                        ? [.. Svc.Data.GetExcelSheet<Item>().Where(x => itemSheetFilter?.Invoke(x) ?? true).Where(x => x.RowId == searchId).Take(20)]
                        : [.. Svc.Data.GetExcelSheet<Item>().Where(x => itemSheetFilter?.Invoke(x) ?? true).Where(x => x.Name.ToString().Contains(searchResultsQuery, StringComparison.OrdinalIgnoreCase)).Take(20)]
                    : [];
            }

            var maxWidth = Math.Max(300f, itemSearchResults.Select(item => ImGui.CalcTextSize($"[#{item.RowId}] {item.Name}").X).DefaultIfEmpty(200f).Max() + 30);
            ImGui.SetNextItemWidth(maxWidth);
            if (!ImGui.IsAnyItemActive()) // required for preventing stealing focus from selectables
                ImGui.SetKeyboardFocusHere();
            ImGui.InputText("##itemSearch", ref searchResultsQuery, 100);
            if (!string.IsNullOrEmpty(searchResultsQuery)) {
                using var child = ImRaii.Child("itemSearchResultsChild", new Vector2(maxWidth + 20, 220), true);
                if (!child) return false;

                foreach (var item in itemSearchResults) {
                    if (Svc.Texture.GetFromGameIcon(new GameIconLookup { IconId = item.Icon }).GetWrapOrDefault() is { Handle: var handle }) {
                        ImGui.Image(handle, new Vector2(16, 16));
                        ImGui.SameLine();
                    }

                    if (!ImGui.Selectable($" [#{item.RowId}] {item.Name}")) continue;
                    result = item;
                    ImGui.CloseCurrentPopup();
                    return true;
                }
            }
            return false;
        }

        public static bool AddCustomItemPopupButton([NotNullWhen(true)] out Item? result, IReadOnlyList<(uint ItemId, string DisplayName)> customItems, string? buttonLabel = null, Vector2? size = null) {
            result = null;
            if (ImGui.Button(buttonLabel ?? "Add Item", size ?? new Vector2(-1, 0))) {
                ImGui.OpenPopup("custom_item_search_add");
            }

            using var popup = ImRaii.Popup("custom_item_search_add");
            if (!popup) return false;

            var maxWidth = Math.Max(300f, customItems.Select(item => ImGui.CalcTextSize($"[#{item.ItemId}] {item.DisplayName}").X).DefaultIfEmpty(200f).Max() + 30);
            using var child = ImRaii.Child("customItemSearchResultsChild", new Vector2(maxWidth + 20, 220), true);
            if (!child) return false;

            foreach (var (itemId, displayName) in customItems) {
                if (Svc.Data.TryGetRow<Item>(itemId, out var item)) {
                    if (Svc.Texture.GetFromGameIcon(new GameIconLookup { IconId = item.Icon }).GetWrapOrDefault() is { Handle: var handle }) {
                        ImGui.Image(handle, new Vector2(16, 16));
                        ImGui.SameLine();
                    }

                    if (!ImGui.Selectable($" [#{itemId}] {displayName}")) continue;
                    result = item;
                    ImGui.CloseCurrentPopup();
                    return true;
                }
            }
            return false;
        }

        public static void SpacedSeparator() {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

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

        public static bool SmallIconButton(FontAwesomeIcon icon, string? id = null) {
            var label = icon.ToIconString();
            if (id != null) {
                label += $"##{id}";
            }

            using var _ = ImRaii.PushFont(UiBuilder.IconFont);
            var ret = ImGui.SmallButton(label);

            return ret;
        }

        public static bool IsItemClickedWithModifier(ImGuiMouseButton button, ImGuiModFlags modifier) => ImGui.IsItemClicked(button) && ImGui.GetIO().KeyMods.HasFlag(modifier);
        public static bool IsItemClickedNoModifiers(ImGuiMouseButton button) => ImGui.IsItemClicked(button) && ImGui.GetIO().KeyMods == ImGuiModFlags.None;

        /// <summary>
        /// Sets up drag and drop source for reordering list items.
        /// Call this after drawing the draggable item (e.g., after a Button or Selectable).
        /// </summary>
        public static void DragDropSource(int index, ReadOnlySpan<byte> payloadType, string? dragPreviewText = null) {
            using var source = ImRaii.DragDropSource();
            if (source) {
                if (!string.IsNullOrEmpty(dragPreviewText))
                    ImGui.Text(dragPreviewText);
                ImGui.SetDragDropPayload(payloadType, BitConverter.GetBytes(index), ImGuiCond.None);
            }
        }

        /// <summary>
        /// Sets up drag and drop target for reordering list items.
        /// Call this after drawing the drop target item.
        /// </summary>
        /// <param name="onReorder">Callback that performs the reorder: (sourceIndex, targetIndex) => { /* reorder logic */ }</param>
        public static void DragDropTarget(int targetIndex, ReadOnlySpan<byte> payloadType, int listCount, Action<int, int> onReorder) {
            using var target = ImRaii.DragDropTarget();
            if (target) {
                var payload = ImGui.AcceptDragDropPayload(payloadType);
                unsafe {
                    if (!payload.IsNull && payload.IsDelivery() && payload.Data != null && payload.DataSize == sizeof(int)) {
                        var sourceIndex = *(int*)payload.Data;
                        if (sourceIndex != targetIndex && sourceIndex >= 0 && sourceIndex < listCount) {
                            // Calculate insert index before removal
                            // When dragging down (sourceIndex < targetIndex), insert after target (at targetIndex+1)
                            // When dragging up (sourceIndex > targetIndex), insert before target (at targetIndex)
                            var insertIndex = sourceIndex < targetIndex ? targetIndex + 1 : targetIndex;

                            // Call the reorder callback with source and calculated insert index
                            onReorder(sourceIndex, insertIndex);
                        }
                    }
                }
            }
        }
    }
}
