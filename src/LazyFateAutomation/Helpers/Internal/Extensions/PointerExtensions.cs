namespace LazyFateAutomation.Helpers.Internal.Extensions;

internal static unsafe class PointerExtensions {
    public static T* As<T>(this IntPtr ptr) where T : unmanaged => (T*)ptr;
}
