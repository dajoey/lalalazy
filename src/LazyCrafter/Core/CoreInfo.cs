namespace LazyCrafter.Core;

/// <summary>
/// Smoke hook for the harness. Core must stay free of Dalamud/Lumina types;
/// <c>tests/LazyCrafter.Harness</c> compiles this folder without either reference.
/// </summary>
public static class CoreInfo
{
    public const string Version = "0.1.0";

    /// <summary>Returns "OK" when the pure core is linked in. Used by the harness.</summary>
    public static string SelfCheck() => "OK";
}
