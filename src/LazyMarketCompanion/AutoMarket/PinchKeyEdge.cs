namespace LazyMarketCompanion.AutoMarket;

// Dalamud-free. Exercised by tests/LazyMarketCompanion.Harness (case 174, PinchKeyCases.cs).
//
// Ported from upstream Dagobert commit 0020488611 ("Toggle Auto Pinch with its hotkey",
// 2026-11-09, AutoPinch.ConsumePinchKeyPress), watched by the .lalalazy-sync/Dagobert.json
// marker: the absorbed price engine's hotkey read the key as a LEVEL - KeyState[PinchKey] is
// true on every frame the key is held - so one held press re-fired the pinch every drawn frame,
// and fired again the moment a finished run freed the busy guard. The fix reads the key as an
// EDGE: exactly one fire per up -> down transition, tracked in a single wasDown cell the plugin
// updates on every draw frame no matter which retainer window is open. The toggle-to-cancel
// half of the upstream commit (press the key again to cancel the running pinch) is NOT ported:
// cancelling stays on LMC's Cancel button and /lmc command.
public static class PinchKeyEdge
{
  /// <summary>
  /// One draw frame's hotkey read. Returns true exactly on the up -> down transition of the key;
  /// <paramref name="wasDown"/> is always refreshed, so a held key stays quiet after its first fire.
  /// Call once per frame from one place and feed the result to every window draw that uses the key.
  /// </summary>
  /// <param name="keyDown">True when the configured pinch key is down this frame (feature enabled AND key held).</param>
  /// <param name="wasDown">The shared edge cell; initialize false and never write it anywhere else.</param>
  public static bool Consume(bool keyDown, ref bool wasDown)
  {
    var pressed = keyDown && !wasDown;
    wasDown = keyDown;
    return pressed;
  }
}
