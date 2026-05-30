namespace LazyFateAutomation.Helpers.Extensions;

public static class IEnumerableExtensions {
    public static void ForEach<TSource>(this IEnumerable<TSource> source, Action<TSource> action) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(action);

        foreach (var item in source) {
            action(item);
        }
    }

    public static bool None<TSource>(this IEnumerable<TSource> source) {
        ArgumentNullException.ThrowIfNull(source);
        if (source is ICollection<TSource> collection)
            return collection.Count is 0;
        using var e = source.GetEnumerator();
        return e.MoveNext();
    }

    public static bool None<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);

        foreach (var item in source) {
            if (predicate(item)) return false;
        }
        return true;
    }
}
