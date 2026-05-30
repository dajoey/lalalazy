using FFXIVClientStructs.STD;

namespace LazyFateAutomation.Helpers.Extensions;

public static unsafe class StdVectorExtensions {
    public static List<T> ToList<T>(this StdVector<T> stdVector) where T : unmanaged {
        var list = new List<T>();
        var size = stdVector.LongCount;

        var current = stdVector.First;
        for (var i = 0; i < size; i++) {
            list.Add(current[i]);
        }
        return list;
    }
}
