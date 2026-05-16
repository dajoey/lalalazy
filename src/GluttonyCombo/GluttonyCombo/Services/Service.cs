using GluttonyCombo.AutoRotation;
using GluttonyCombo.Core;
using GluttonyCombo.Data;
namespace GluttonyCombo.Services;

/// <summary> Dalamud and plugin services. </summary>
internal class Service
{
    /// <summary> Gets or sets the plugin address resolver. </summary>
    internal static AddressResolver Address { get; set; } = null!;

    /// <summary> Gets or sets the plugin caching mechanism. </summary>
    internal static CustomComboCache ComboCache { get; set; } = null!;

    /// <summary> Gets or sets the plugin configuration. </summary>
    internal static Configuration Configuration { get; set; } = null!;

    /// <summary> Gets or sets the plugin icon replacer. </summary>
    internal static ActionReplacer ActionReplacer { get; set; } = null!;

    /// <summary> Gets or sets the plugin's Auto-Rotation controller for event handling</summary>
    internal static AutoRotationController AutoRotationController { get; set; } = null!;
}