namespace LazyFateAutomation.Helpers.Extensions;

public static class ArrayExtensions {
    public static void ForEach<T>(this T[] _items, Action<T> action) {
        ArgumentNullException.ThrowIfNull(_items);
        ArgumentNullException.ThrowIfNull(action);

        for (var i = 0; i < _items.Length; i++) {
            action(_items[i]);
        }
    }
}
