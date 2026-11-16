using System.Globalization;

namespace Lala.UiRender;

/// <summary>Which look a harness renders with.</summary>
public enum RenderFidelity
{
    /// <summary>ImGui's embedded 13 px font and default dark style. Hermetic: no host file is read.</summary>
    BuiltIn,

    /// <summary>The live Dalamud default font, global scale and Dalamud Standard style (see <see cref="GameLook"/>).</summary>
    Game,
}

/// <summary>
/// Render mode selection shared by every harness: built-in stays the default; game mode is opt-in through the
/// <c>--game</c> command-line switch or <c>LALA_UI_FIDELITY=game</c>, and reads its inputs from
/// <c>LALA_UI_ASSET_DIR</c> (folder holding the Dalamud UI font files) and <c>LALA_UI_SCALE</c> (default 1.0).
/// </summary>
public sealed record RenderOptions(RenderFidelity Fidelity, string? AssetDirectory, float Scale)
{
    public const string FidelityVariable = "LALA_UI_FIDELITY";
    public const string AssetDirectoryVariable = "LALA_UI_ASSET_DIR";
    public const string ScaleVariable = "LALA_UI_SCALE";
    public const string GameSwitch = "--game";

    public static RenderOptions BuiltIn { get; } = new(RenderFidelity.BuiltIn, null, 1f);

    public bool IsGame => Fidelity == RenderFidelity.Game;

    /// <summary>Sub-folder of a harness's output folder this mode writes to ("" for built-in, "game" for game).</summary>
    public string OutputSubdirectory => IsGame ? "game" : "";

    /// <summary>Resolves the mode from the command line and the process environment.</summary>
    public static RenderOptions Resolve(IReadOnlyList<string> args)
        => Resolve(args, Environment.GetEnvironmentVariable);

    public static RenderOptions Resolve(IReadOnlyList<string> args, Func<string, string?> environment)
    {
        var game = args.Any(a => string.Equals(a, GameSwitch, StringComparison.OrdinalIgnoreCase));
        var requested = environment(FidelityVariable);
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (string.Equals(requested, "game", StringComparison.OrdinalIgnoreCase))
                game = true;
            else if (!string.Equals(requested, "builtin", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(requested, "built-in", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{FidelityVariable} must be 'game' or 'builtin', not '{requested}'.");
        }

        if (!game)
            return BuiltIn;

        var directory = environment(AssetDirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException(
                $"Game-fidelity mode needs {AssetDirectoryVariable} to name the folder holding the Dalamud UI font files " +
                $"({string.Join(", ", GameLook.RequiredFontFiles)}).");
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory))
            throw new InvalidOperationException($"{AssetDirectoryVariable} points at a folder that does not exist: {directory}");
        foreach (var file in GameLook.RequiredFontFiles)
            if (!File.Exists(Path.Combine(directory, file)))
                throw new InvalidOperationException($"Game-fidelity mode needs font file '{file}' in {AssetDirectoryVariable} ({directory}), but it is missing.");

        var scale = 1f;
        var scaleText = environment(ScaleVariable);
        if (!string.IsNullOrWhiteSpace(scaleText) &&
            (!float.TryParse(scaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) ||
             !float.IsFinite(scale) || scale < 0.5f || scale > 4f))
            throw new InvalidOperationException($"{ScaleVariable} must be a number between 0.5 and 4, not '{scaleText}'.");

        return new RenderOptions(RenderFidelity.Game, directory, scale);
    }
}
