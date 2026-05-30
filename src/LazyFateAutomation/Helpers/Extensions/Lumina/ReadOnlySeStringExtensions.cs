using Dalamud.Utility;
using Lumina.Text.ReadOnly;
using System.Text;

namespace LazyFateAutomation.Helpers.Extensions;

public static class ReadOnlySeStringExtensions {
    public static bool ContainsText(this ReadOnlySeString seString, string needle) {
        if (seString.IsEmpty) return false;
        var bytes = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes(needle));
        return SeStringExtensions.ContainsText(seString, bytes);
    }
}
