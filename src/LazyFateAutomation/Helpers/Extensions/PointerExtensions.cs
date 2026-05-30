using System;

namespace clib.Extensions;

public static class PointerExtensions {
    public static unsafe T* As<T>(this IntPtr ptr) where T : unmanaged => (T*)ptr;
}
