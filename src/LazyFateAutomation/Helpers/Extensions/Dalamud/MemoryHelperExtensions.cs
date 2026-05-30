using Dalamud.Memory;

namespace LazyFateAutomation.Helpers.Extensions;

public static class MemoryHelperExtensions {
    extension(MemoryHelper) {
        public static unsafe T ReadField<T>(void* address, int offset) where T : unmanaged => *(T*)((IntPtr)address + offset);
        public static unsafe void WriteField<T>(void* address, int offset, T value) where T : unmanaged => *(T*)((IntPtr)address + offset) = value;
    }
}
