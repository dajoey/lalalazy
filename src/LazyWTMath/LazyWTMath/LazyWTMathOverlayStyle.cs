using System.Numerics;
using LalaUiKit;

namespace WondrousTailsSolver;

/// <summary>Shared palette for the plugin-owned text attached to the native journal.</summary>
internal static class LazyWTMathOverlayStyle
{
    internal static Vector4 TextColor => Theme.TextPrimary;
    internal static Vector4 OutlineColor => Theme.Ink;
}
