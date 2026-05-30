using FFXIVClientStructs.FFXIV.Client.Network;
using FFXIVClientStructs.Interop;

namespace clib.Extensions;

public static unsafe class PacketDispatcherExtensions {
    extension(PacketDispatcher) {
        public static void TeleportToAethernet(uint currentAetheryte, uint destinationAetheryte) {
            Span<uint> payload = [4, destinationAetheryte];
            PacketDispatcher.SendEventCompletePacket(0x50000 | currentAetheryte, 0, 0, payload.GetPointer(0), (byte)payload.Length, null);
        }
        public static void TeleportToFirmament(uint currentAetheryte) {
            Span<uint> payload = [9];
            PacketDispatcher.SendEventCompletePacket(0x50000 | currentAetheryte, 0, 0, payload.GetPointer(0), (byte)payload.Length, null);
        }
    }
}
