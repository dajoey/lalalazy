using LazyMarketCompanion.AutoMarket;

// Offline case 174: the pinch-hotkey edge reader (0.2.8.18, ported from upstream Dagobert
// 0020488611's AutoPinch.ConsumePinchKeyPress). The hotkey used to be read as a level -
// KeyState[PinchKey] true on every frame the key is held - so one held press re-fired the pinch
// every drawn frame and again the moment a finished run freed the busy guard. The fix consumes
// the press as an edge: exactly one fire per up -> down transition, tracked in a single wasDown
// cell the plugin updates on every draw frame. These cases pin that contract against plain
// sequences of per-frame reads; the Draw wiring itself (one Consume before both retainer draws)
// is pinned by the source-scan style checks the plugin's other harnesses use.
internal static class PinchKeyCases
{
  public static void Run(Action<string, bool, string> check)
  {
    // 174a. A held key fires exactly once, then stays quiet.
    var was = false;
    var fires = new[] { true, true, true, true }.Select(k => PinchKeyEdge.Consume(k, ref was)).ToList();
    check("174 pinch key: a held key fires once and only once",
      fires.SequenceEqual([true, false, false, false]), $"fires=[{string.Join(",", fires)}]");

    // 174b. Release, then press again: fires again - the only way to start a second run.
    fires.Clear();
    foreach (var k in new[] { true, true, false, false, true, true, false })
      fires.Add(PinchKeyEdge.Consume(k, ref was));
    check("174 pinch key: release then press fires again, quiet frames stay quiet",
      fires.SequenceEqual([false, false, false, false, true, false, false]), $"fires=[{string.Join(",", fires)}]");

    // 174c. Never down: never fires, and the cell stays false.
    was = false;
    fires.Clear();
    for (var i = 0; i < 10; i++) fires.Add(PinchKeyEdge.Consume(false, ref was));
    check("174 pinch key: never down never fires",
      fires.All(f => !f) && !was, $"anyFire={fires.Any(f => f)} wasDown={was}");

    // 174d. Key already down on the first frame the plugin ever draws: fires once - the helper
    // cannot know history before its first read; upstream's port source behaves the same.
    was = false;
    var first = PinchKeyEdge.Consume(true, ref was);
    check("174 pinch key: key already down on the first drawn frame fires once", first);
  }
}
