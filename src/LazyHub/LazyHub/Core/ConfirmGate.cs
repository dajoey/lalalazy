namespace LazyHub.Core;

/// <summary>
/// Two-click confirmation for a control that declares a confirm text: the first click only arms it, a second click
/// on the same control within the window goes through, and anything else (the window passing, a different control)
/// starts over. The clock is passed in so this is testable. No Dalamud types.
/// </summary>
public sealed class ConfirmGate
{
    private readonly long _windowMs;
    private string? _key;
    private long _expiresAt;

    public ConfirmGate(long windowMs = 5000) => _windowMs = windowMs;

    /// <summary>True when the action may run now. With no confirmation needed it always may.</summary>
    public bool ShouldProceed(string key, bool needsConfirm, long nowMs)
    {
        if (!needsConfirm) return true;

        if (_key == key && nowMs <= _expiresAt)
        {
            _key = null;
            return true;
        }

        _key = key;
        _expiresAt = nowMs + _windowMs;
        return false;
    }

    /// <summary>True while a first click on this control is waiting for its second.</summary>
    public bool IsPending(string key, long nowMs) => _key == key && nowMs <= _expiresAt;

    public void Reset() => _key = null;

    /// <summary>
    /// Clears a first click whose window has passed and says so, so the window can drop its "Confirm?" label at the
    /// moment it lapses instead of at the next poll. False when nothing was pending or it is still open.
    /// </summary>
    public bool ClearIfExpired(long nowMs)
    {
        if (_key == null || nowMs <= _expiresAt) return false;
        _key = null;
        return true;
    }
}
