namespace RotationSolver.Decisions;

/// <summary>
///     Fork-only decision core (no Dalamud or game types, compiles standalone): lets a draw loop log a failure once
///     per distinct failure instead of once per frame. <see cref="First(string)"/> is true the first time a signature
///     is seen and false after that; the set is capped, and once it is full nothing new is reported.
/// </summary>
public sealed class OncePerDistinct
{
    /// <summary>The most signatures remembered.</summary>
    public const int Capacity = 64;

    private readonly HashSet<string> _seen = [];

    /// <summary>True the first time the signature is passed in (and the set has room), false afterwards.</summary>
    public bool First(string signature) => _seen.Count < Capacity && _seen.Add(signature);

    /// <summary>Forgets everything (used when the window is opened again, so a fixed fault can be reported again).</summary>
    public void Reset() => _seen.Clear();

    /// <summary>A signature for an exception: its type, message and top stack frame.</summary>
    public static string Signature(Exception exception)
    {
        string? top = exception.StackTrace?.Split('\n', 2)[0].Trim();
        return exception.GetType().FullName + "|" + exception.Message + "|" + top;
    }
}
