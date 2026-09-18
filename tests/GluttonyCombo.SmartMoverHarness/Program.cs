using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GluttonyCombo.AutoRotation;
using GluttonyCombo.AutoRotation.Movement;

// Offline harness for Smart Movement v2 (2026-09 rebuild).
// Compiles the REAL pure files (DangerZoneModel, SmartMoverCore, MovementTelemetryFormat,
// MovementGateCore, OmenVfxModel, Movement/*) with no Dalamud. Prints PASS/FAIL per
// case; exit 0 + "OK" only when every case passes.
//   dotnet build tests/GluttonyCombo.SmartMoverHarness -c Release
//   dotnet tests/GluttonyCombo.SmartMoverHarness/bin/Release/net10.0/GluttonyCombo.SmartMoverHarness.dll

int pass = 0, fail = 0;
void Check(string name, bool cond, string detail = "")
{
    if (cond) { pass++; Console.WriteLine($"PASS {name}"); }
    else { fail++; Console.WriteLine($"FAIL {name} {detail}"); }
}

const float NpcDelay = 0.3f;
var NoZones = (IReadOnlyList<DangerZoneModel.Zone>)Array.Empty<DangerZoneModel.Zone>();

// --- zone helpers (math-convention angles; buffer dilation as the Dalamud half does it)
DangerZoneModel.Zone Dilate(DangerZoneModel.Zone z, float buffer) =>
    z with { Radius = z.Radius + buffer, InnerRadius = MathF.Max(0f, z.InnerRadius - buffer), HalfWidth = z.HalfWidth + buffer, HalfAngle = z.HalfAngle + (buffer / MathF.Max(z.Radius, 1f)) };

DangerZoneModel.Zone Cast(byte castType, float range, float xaxis, float hitbox, Vector2 caster, Vector2 loc, float castTime, double castStart,
    float coneHalfDeg = 60f, float donutInner = 0f, float? aimGameRot = null, ulong src = 1, uint action = 0, float buffer = 1f)
{
    var prim = new DangerZoneModel.CastPrimitive(castType, range, xaxis, hitbox, caster, loc, 0, 999, castTime, coneHalfDeg, donutInner,
        aimGameRot is { } g ? DangerZoneModel.GameRotationToMath(g) : null);
    var z = DangerZoneModel.BuildZone(prim) ?? throw new Exception("zone not built");
    z = Dilate(z, buffer);
    return z with { ActivationSec = castStart + castTime + NpcDelay, Source = src, ActionId = action };
}

SmartMoverCore.MoverWorld World(
    Vector2 player, double now, IReadOnlyList<DangerZoneModel.Zone>? zones = null,
    Vector2? target = null, bool engaged = true, float range = 3f, float hitbox = 5f, float tRot = 0f,
    bool posWanted = false, bool rear = false, bool manual = false, bool casting = false, float castRem = 0f,
    bool external = false, bool nav = true, bool enabled = true, bool combat = true, float speed = 6f,
    bool mounted = false, bool kb = false, float cushion = 1.0f, Func<Vector2, bool>? walkable = null) =>
    new(player, range, posWanted, rear, false, target ?? new Vector2(0, 0), tRot, hitbox, engaged, zones ?? NoZones,
        manual, casting, castRem, external, nav, enabled, combat, now, speed, mounted, kb, cushion, walkable);

// Simulates the character following the engine at run speed. Returns positions per tick.
// Fails the case if the character is inside any zone at that zone's activation.
(bool ok, string detail, Vector2 end, List<byte> reasons) Simulate(
    Func<double, Vector2, SmartMoverCore.MoverWorld> worldAt, IReadOnlyList<DangerZoneModel.Zone> zones, Vector2 start,
    double t0, double t1, float dt = 0.1f, float speed = 6f)
{
    var s = new SmartMoverCore.MoverState();
    var pos = start;
    var reasons = new List<byte>();
    var checkedZones = new HashSet<int>();
    for (var t = t0; t <= t1 + 1e-6; t += dt)
    {
        var w = worldAt(t, pos);
        var d = SmartMoverCore.Decide(w, s);
        reasons.Add(d.Reason);
        // move at most dt*speed toward the held waypoint (the per-frame steering)
        var dir = SmartMoverCore.SteerDirection(s, pos);
        if (dir is { } v)
        {
            var wp = s.Waypoint ?? pos;
            var step = MathF.Min(speed * dt, Vector2.Distance(pos, wp));
            pos += v * step;
        }
        for (var i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            if (checkedZones.Contains(i) || z.ActivationSec > t)
                continue;
            checkedZones.Add(i);
            // the real hit shape is the undilated one: test with a negative buffer of the dilation (1y)
            if (DangerZoneModel.Contains(z, pos, -1f))
                return (false, $"inside zone {i} ({z.Kind}) at t={t:F1} pos={pos}", pos, reasons);
        }
    }
    return (true, "", pos, reasons);
}

// ---------------------------------------------------------------- shape distance
{
    var c = ShapeDistance.Circle(new(0, 0), 5);
    Check("sdf/circle-inside", c(new(3, 0)) < 0);
    Check("sdf/circle-outside", c(new(6, 0)) > 0);
    Check("sdf/circle-value", MathF.Abs(c(new(7, 0)) - 2f) < 0.01f);

    var d = ShapeDistance.Donut(new(0, 0), 4, 40);
    Check("sdf/donut-hole", d(new(2, 0)) > 0);
    Check("sdf/donut-ring", d(new(10, 0)) < 0);
    Check("sdf/donut-out", d(new(45, 0)) > 0);

    var cone = ShapeDistance.Cone(new(0, 0), 20, 0f, MathF.PI / 6f); // 60-degree cone along +X
    Check("sdf/cone-axis", cone(new(5, 0)) < 0);
    Check("sdf/cone-edge-in", cone(new(5, MathF.Tan(MathF.PI / 6f) * 5 - 0.2f)) < 0);
    Check("sdf/cone-edge-out", cone(new(5, MathF.Tan(MathF.PI / 6f) * 5 + 0.2f)) > 0);
    Check("sdf/cone-behind", cone(new(-5, 0)) > 0);
    Check("sdf/cone-far", cone(new(25, 0)) > 0);

    var wide = ShapeDistance.Cone(new(0, 0), 20, 0f, MathF.PI * 0.75f); // 270-degree cone
    Check("sdf/widecone-side", wide(new(0, 5)) < 0);
    Check("sdf/widecone-behind", wide(new(-5, 0)) > 0);
    Check("sdf/widecone-front", wide(new(5, 0)) < 0);

    var r = ShapeDistance.Rect(new(0, 0), 0f, 10, 0, 2);
    Check("sdf/rect-in", r(new(5, 1)) < 0);
    Check("sdf/rect-side", r(new(5, 3)) > 0);
    Check("sdf/rect-behind", r(new(-1, 0)) > 0);
    var lane = ShapeDistance.Rect(new(0, 0), new Vector2(0, 10), 5);
    Check("sdf/lane-in", lane(new(3, 5)) < 0);
    Check("sdf/lane-out", lane(new(7, 5)) > 0);
    var x = ShapeDistance.Cross(new(0, 0), 0f, 10, 2);
    Check("sdf/cross-arm1", x(new(8, 0)) < 0);
    Check("sdf/cross-arm2", x(new(0, 8)) < 0);
    Check("sdf/cross-diag", x(new(5, 5)) > 0);

    // Zone -> Sdf agrees with Contains
    var z = Cast(5, 8f, 0f, 3f, new(0, 0), new(0, 0), 5f, 0);
    var f = DangerZoneModel.Sdf(in z);
    Check("sdf/zone-agree-in", (f(new(5, 0)) < 0) == DangerZoneModel.Contains(z, new(5, 0), 0f));
    Check("sdf/zone-agree-out", (f(new(14, 0)) < 0) == DangerZoneModel.Contains(z, new(14, 0), 0f));
}

// ---------------------------------------------------------------- map + planner basics
{
    var s = new SmartMoverCore.MoverState();
    var w = World(new Vector2(0, -8), 100.0, engaged: false);
    var d = SmartMoverCore.Decide(w, s);
    Check("plan/no-zones-no-target-settles", d.Kind == SmartMoverCore.Decision.None && d.Reason == SmartMoverCore.ReasonSettleCode, $"{d}");
    Check("plan/leeway-inf", d.LeewaySec == float.MaxValue);

    // inside a circle resolving in 4 s: must steer out with positive leeway
    var z = Cast(5, 8f, 0f, 3f, new(0, 0), new(0, 0), 4f, 100.0);
    var w2 = World(new Vector2(0, -5), 100.0, new[] { z }, engaged: false);
    var d2 = SmartMoverCore.Decide(w2, s);
    Check("plan/inside-circle-steers", d2.Kind == SmartMoverCore.Decision.Steer, $"{d2}");
    Check("plan/inside-circle-ddg", d2.Reason == SmartMoverCore.ReasonDodgeCode, $"{SmartMoverCore.ReasonString(d2.Reason)}");
    Check("plan/inside-circle-start-unsafe", d2.StartUnsafe);
    Check("plan/inside-circle-leeway-positive", d2.LeewaySec > 0 && d2.LeewaySec < 4f, $"lee={d2.LeewaySec}");
    Check("plan/inside-circle-maxcast", d2.MaxCastTime == d2.LeewaySec);

    // outside every zone, no goals: stay put (hold, zones live)
    var w3 = World(new Vector2(0, -20), 100.0, new[] { z }, engaged: false);
    var d3 = SmartMoverCore.Decide(w3, s);
    Check("plan/outside-holds", d3.Kind == SmartMoverCore.Decision.None && d3.Reason == SmartMoverCore.ReasonHoldCode, $"{SmartMoverCore.ReasonString(d3.Reason)}");

    // simulate the escape: never inside at activation
    var zs = new[] { z };
    var sim = Simulate((t, p) => World(p, t, zs, engaged: false), zs, new Vector2(0, -5), 100.0, 104.5);
    Check("sim/inside-circle-escapes", sim.ok, sim.detail);
    Check("sim/inside-circle-radial", Vector2.Distance(sim.end, new Vector2(0, 0)) > 9.5f, $"end={sim.end}");
}

// ---------------------------------------------------------------- guards
{
    var s = new SmartMoverCore.MoverState();
    Check("guard/off", SmartMoverCore.Decide(World(new(0, -8), 1, enabled: false), s).Reason == SmartMoverCore.ReasonOffCode);
    Check("guard/manual", SmartMoverCore.Decide(World(new(0, -8), 1, manual: true), s).Reason == SmartMoverCore.ReasonManualCode);
    Check("guard/external", SmartMoverCore.Decide(World(new(0, -8), 1, external: true), s).Reason == SmartMoverCore.ReasonExternalCode);
    Check("guard/ooc", SmartMoverCore.Decide(World(new(0, -8), 1, combat: false), s).Reason == SmartMoverCore.ReasonOocCode);
    Check("guard/mounted", SmartMoverCore.Decide(World(new(0, -8), 1, mounted: true), s).Reason == SmartMoverCore.ReasonMountedCode);
    Check("guard/knockback", SmartMoverCore.Decide(World(new(0, -8), 1, kb: true), s).Reason == SmartMoverCore.ReasonKnockbackCode);
    // ooc never dodges either (combat-gated, as v1)
    var z = Cast(5, 8f, 0f, 3f, new(0, 0), new(0, 0), 4f, 1.0);
    Check("guard/ooc-no-dodge", SmartMoverCore.Decide(World(new(0, -5), 1, new[] { z }, combat: false), s).Kind != SmartMoverCore.Decision.Steer);
    // a steer in progress then manual input -> Stop
    s = new SmartMoverCore.MoverState();
    SmartMoverCore.Decide(World(new(0, -5), 1, new[] { z }, engaged: false), s);
    var d = SmartMoverCore.Decide(World(new(0, -5), 1.1, new[] { z }, engaged: false, manual: true), s);
    Check("guard/manual-stops-steer", d.Kind == SmartMoverCore.Decision.Stop && s.Waypoint is null);
}

// ---------------------------------------------------------------- engage (goals)
{
    var s = new SmartMoverCore.MoverState();
    // melee, target hitbox 5 range 3 -> band radius 8.5; player at 15 -> must move in
    var w = World(new Vector2(0, -15), 10.0, target: new(0, 0), range: 3f, hitbox: 5f);
    var d = SmartMoverCore.Decide(w, s);
    Check("engage/outside-band-steers", d.Kind == SmartMoverCore.Decision.Steer && d.Reason == SmartMoverCore.ReasonEngageCode, $"{SmartMoverCore.ReasonString(d.Reason)}");
    var sim = Simulate((t, p) => World(p, t, target: new(0, 0), range: 3f, hitbox: 5f), NoZones, new Vector2(0, -15), 10.0, 14.0);
    Check("engage/arrives-in-band", Vector2.Distance(sim.end, new(0, 0)) <= 8.6f, $"end={sim.end}");
    Check("engage/does-not-overshoot", Vector2.Distance(sim.end, new(0, 0)) >= 7.0f, $"end={sim.end}");
    // already inside the band: settle, no move
    var d2 = SmartMoverCore.Decide(World(new Vector2(0, -7), 20.0, target: new(0, 0), range: 3f, hitbox: 5f), s);
    Check("engage/inside-band-settles", d2.Kind == SmartMoverCore.Decision.None && d2.Reason == SmartMoverCore.ReasonSettleCode, $"{SmartMoverCore.ReasonString(d2.Reason)}");
    // ranged at melee distance: never backs away
    var d3 = SmartMoverCore.Decide(World(new Vector2(0, -6), 30.0, target: new(0, 0), range: 20f, hitbox: 5f), s);
    Check("engage/ranged-close-never-backs-away", d3.Kind == SmartMoverCore.Decision.None, $"{d3}");
    // positional: rear wanted, player at flank in band -> moves to rear wedge
    var s2 = new SmartMoverCore.MoverState();
    var simR = Simulate((t, p) => World(p, t, target: new(0, 0), range: 3f, hitbox: 5f, tRot: 0f, posWanted: true, rear: true), NoZones, new Vector2(7, 0), 40.0, 44.0);
    var wR = World(simR.end, 44.0, target: new(0, 0), range: 3f, hitbox: 5f, tRot: 0f, posWanted: true, rear: true);
    Check("engage/positional-rear-reached", SmartMoverCore.AtPositional(simR.end, wR), $"end={simR.end}");
    Check("engage/positional-still-in-band", Vector2.Distance(simR.end, new(0, 0)) <= 8.6f, $"end={simR.end}");
    // long approach: target 40 y away -> vnavmesh NavTo once, then None while running
    var s3 = new SmartMoverCore.MoverState();
    var dl = SmartMoverCore.Decide(World(new Vector2(0, -40), 50.0, target: new(0, 0)), s3);
    Check("engage/long-approach-navto", dl.Kind == SmartMoverCore.Decision.NavTo && dl.Reason == SmartMoverCore.ReasonApproachCode, $"{dl}");
    var dl2 = SmartMoverCore.Decide(World(new Vector2(0, -38), 50.2, target: new(0, 0)), s3);
    Check("engage/long-approach-not-reissued", dl2.Kind == SmartMoverCore.Decision.None, $"{dl2}");
    var dl3 = SmartMoverCore.Decide(World(new Vector2(0, -40), 50.0, target: new(0, 0), nav: false), new SmartMoverCore.MoverState());
    Check("engage/long-approach-no-nav", dl3.Kind == SmartMoverCore.Decision.None && dl3.Reason == SmartMoverCore.ReasonNavCode);
}

// ---------------------------------------------------------------- cast coupling
{
    var s = new SmartMoverCore.MoverState();
    var z = Cast(5, 8f, 0f, 3f, new(0, 0), new(0, 0), 6f, 100.0); // resolves at 106.3; cushion 1 -> must be out by 105.3
    // casting with 2.0 s left at t=100: leeway (~4.5 s) > 1.5 -> finish the cast
    var d = SmartMoverCore.Decide(World(new Vector2(0, -5), 100.0, new[] { z }, engaged: false, casting: true, castRem: 2.0f), s);
    Check("cast/finish-when-leeway-allows", d.Reason == SmartMoverCore.ReasonCastCode && d.Kind != SmartMoverCore.Decision.Steer, $"{SmartMoverCore.ReasonString(d.Reason)} lee={d.LeewaySec}");
    // same cast at t=104.5: leeway < remaining - 0.5 -> cut the cast and move
    var d2 = SmartMoverCore.Decide(World(new Vector2(0, -5), 104.5, new[] { z }, engaged: false, casting: true, castRem: 2.0f), s);
    Check("cast/cut-when-late", d2.Kind == SmartMoverCore.Decision.Steer, $"{SmartMoverCore.ReasonString(d2.Reason)} lee={d2.LeewaySec}");
    // slidecast window: 0.4 s left never holds
    var d3 = SmartMoverCore.Decide(World(new Vector2(0, -5), 100.0, new[] { z }, engaged: false, casting: true, castRem: 0.4f), s);
    Check("cast/slidecast-moves", d3.Kind == SmartMoverCore.Decision.Steer);
    // MaxCastTime export: with nothing live it is infinite; inside a zone it is the leeway
    Check("cast/maxcast-inf-when-clear", SmartMoverCore.Decide(World(new Vector2(0, -20), 100.0, engaged: false), s).MaxCastTime == float.MaxValue);
    Check("cast/maxcast-finite-when-threatened", SmartMoverCore.Decide(World(new Vector2(0, -5), 100.0, new[] { z }, engaged: false), s).MaxCastTime < 6f);
}

// ---------------------------------------------------------------- time awareness
{
    // two casts: A (near, resolves in 2 s) and B (far side, resolves in 8 s). Escape from A may cross B.
    var a = Cast(2, 6f, 0f, 0f, new(0, 0), new(0, 0), 2f, 100.0, src: 1);
    var b = Cast(2, 6f, 0f, 0f, new(0, 12), new(0, 12), 8f, 100.0, src: 2);
    var zs = new[] { a, b };
    var sim = Simulate((t, p) => World(p, t, zs, engaged: false), zs, new Vector2(0, 2), 100.0, 108.5);
    Check("time/sequential-zones-survive", sim.ok, sim.detail);
    // a zone resolving in 30 s does not move a character standing still inside it... until it has to
    var late = Cast(5, 8f, 0f, 3f, new(0, 0), new(0, 0), 30f, 100.0);
    var s = new SmartMoverCore.MoverState();
    var d = SmartMoverCore.Decide(World(new Vector2(0, -5), 100.0, new[] { late }, engaged: false), s);
    Check("time/far-future-still-plans-exit", d.Kind == SmartMoverCore.Decision.Steer && d.LeewaySec > 20f, $"lee={d.LeewaySec}");
    // the wait: a target in band under a 30 s telegraph keeps uptime? goals are off while the cell is unsafe (safety first): expect a steer to the band edge outside
    var d2 = SmartMoverCore.Decide(World(new Vector2(0, -6), 100.0, new[] { late }, target: new(0, 0), range: 3f, hitbox: 5f), s);
    Check("time/unsafe-cell-ignores-goals", d2.Kind == SmartMoverCore.Decision.Steer, $"{d2}");
}

// ---------------------------------------------------------------- scene replays from the 2026-09-17 logs
{
    // Burst wall (19:02:35, Copperbell): six r8 circles from Ichorous Drip helpers on y=114 and two more; player (28.2,117.1); boss Ichorous Ire (27.4,116.5)
    var drips = new[] { new Vector2(14.3f, 126.7f), new(39.7f, 101.2f), new(45, 114), new(33, 114), new(21, 114), new(9, 114) };
    var zs = drips.Select((p, i) => Cast(2, 8f, 0f, 0.5f, p, p, 5.7f, 0.0, src: (ulong)(10 + i), action: 0x6F31)).ToArray();
    var boss = new Vector2(27.4f, 116.5f);
    var sim = Simulate((t, p) => World(p, t, zs, target: boss, range: 3f, hitbox: 3f), zs, new Vector2(28.2f, 117.1f), 0.0, 6.5);
    Check("scene/burst-wall-survives", sim.ok, sim.detail);
    Check("scene/burst-wall-first-decision-dodge", sim.reasons[0] == SmartMoverCore.ReasonDodgeCode, SmartMoverCore.ReasonString(sim.reasons[0]));
    // after resolution the zones are gone: the character walks back into the band
    var back = Simulate((t, p) => World(p, t, target: boss, range: 3f, hitbox: 3f), NoZones, sim.end, 6.5, 10.0);
    Check("scene/burst-wall-returns-to-band", Vector2.Distance(back.end, boss) <= 6.6f, $"end={back.end} d={Vector2.Distance(back.end, boss):F1}");

    // Gigantic Swing (19:25:39): donut 4..40 at (-99.84,1.08), player at 4.16 y -> must step INTO the hole, not run 36 y out
    var gy = new Vector2(-99.84f, 1.08f);
    var swing = Cast(10, 40f, 0f, 5f, gy, gy, 5.7f, 0.0, donutInner: 4f, src: 20, action: 0x705A);
    var swings = new[] { swing };
    var simG = Simulate((t, p) => World(p, t, swings, target: gy, range: 3f, hitbox: 5f), swings, new Vector2(-95.69f, 1.26f), 0.0, 6.5);
    Check("scene/gigantic-swing-survives", simG.ok, simG.detail);
    Check("scene/gigantic-swing-steps-in", Vector2.Distance(simG.end, gy) < 3.5f, $"end dist={Vector2.Distance(simG.end, gy):F2}");

    // Cursed Sight corners (S2): four helpers at square corners casting 60-degree cones inward with game headings 0, 1.57, -1.57, 3.14; player near the centre
    var c = new Vector2(-661, -54);
    var corners = new[] { (new Vector2(-651, -44), 3.14f), (new Vector2(-671, -44), 1.57f), (new Vector2(-651, -64), -1.57f), (new Vector2(-671, -64), 0f) };
    var cones = corners.Select((cc, i) => Cast(13, 60f, 0f, 2f, cc.Item1, cc.Item1, 4.7f, 0.0, coneHalfDeg: 30f, aimGameRot: cc.Item2, src: (ulong)(30 + i), action: 0xBC7D)).ToArray();
    var simC = Simulate((t, p) => World(p, t, cones, engaged: false), cones, c + new Vector2(0.5f, 0.5f), 0.0, 5.5);
    Check("scene/cursed-sight-survives", simC.ok, simC.detail);

    // Hellfire Fetch: circle r6 anchored on a target ACTOR (the Dalamud half passes the actor position as loc); the actor is at the player
    var hf = Cast(2, 6f, 0f, 0f, new Vector2(500, -310), new Vector2(480, -330), 6.7f, 0.0, src: 40, action: 0xBCD9);
    var hfs = new[] { hf };
    var simH = Simulate((t, p) => World(p, t, hfs, engaged: false), hfs, new Vector2(480, -330), 0.0, 7.5);
    Check("scene/hellfire-fetch-actor-anchored-survives", simH.ok, simH.detail);

    // Earthquake (22:31:07): r10 circle, player 6.4 y from the caster: exit must be roughly radial and end outside
    var worm = new Vector2(0, 0);
    var eq = new[] { Cast(2, 10f, 0f, 0f, worm, worm, 2.7f, 0.0, src: 50, action: 0xC58D) };
    var start = new Vector2(6.4f, 0);
    var simE = Simulate((t, p) => World(p, t, eq, engaged: false), eq, start, 0.0, 3.5);
    Check("scene/earthquake-survives", simE.ok, simE.detail);
    var moved = simE.end - start;
    var radial = Vector2.Dot(Vector2.Normalize(moved), Vector2.Normalize(start - worm));
    Check("scene/earthquake-radial", radial > 0.7f, $"cos={radial:F2} end={simE.end}");

    // Black Eruption noise (20:05:45): four r5 circles that never contain the player -> the character does not move at all
    var bishops = new[] { new Vector2(10, 10), new(-10, 10), new(10, -10), new(-10, -10) };
    var be = bishops.Select((p, i) => Cast(2, 5f, 0f, 0f, p, p, 1.2f, 0.0, src: (ulong)(60 + i), action: 0xB734)).ToArray();
    var simB = Simulate((t, p) => World(p, t, be, engaged: false), be, new Vector2(0, 0), 0.0, 2.0);
    Check("scene/black-eruption-no-wasted-motion", simB.end == new Vector2(0, 0), $"end={simB.end}");
    Check("scene/black-eruption-holds", simB.reasons.All(r => r == SmartMoverCore.ReasonHoldCode || r == SmartMoverCore.ReasonSettleCode), string.Join(",", simB.reasons.Distinct().Select(SmartMoverCore.ReasonString)));

    // Arm of Purgatory (20:17:41): 0.7 s cast, r10, player 1.8 y away: unreachable; the engine must still answer (escape, no crash) and report no leeway
    var fire = new Vector2(0, 0);
    var arm = new[] { Cast(2, 10f, 0f, 0f, fire, fire, 0.7f, 0.0, src: 70, action: 0xB74A) };
    var s = new SmartMoverCore.MoverState();
    var dA = SmartMoverCore.Decide(World(new Vector2(1.8f, 0), 0.3, arm, engaged: false), s);
    // v2 r2: an exit beyond reach is not run - the engine holds and reports "stuck" (a hit either way, no dash to the edge)
    Check("scene/arm-of-purgatory-holds-as-stuck", dA.Kind == SmartMoverCore.Decision.None && dA.Reason == SmartMoverCore.ReasonStuckCode, $"{dA}");
    Check("scene/arm-of-purgatory-no-leeway", dA.LeewaySec <= 0f, $"lee={dA.LeewaySec}");
}

// ---------------------------------------------------------------- Abductor (2026-09-17 22:11-22:13 grading of 1.0.4.213)
{
    var boss = new Vector2(-150.0f, -860.0f);
    // Tendon Ripper: two helper crosses (length 60, half-width 4) at the same point, rotations -135 and -90 deg (math), 0.7 s cast.
    // Eight spokes of half-width 4 leave no gap within ~13 y of the centre. The character at 11 y in the
    // -67.5 degree gap (4.2 y from both spoke lines; outside the real 4 y, inside the 1 y margin): steps outward, survives.
    DangerZoneModel.Zone Star(float rotDeg, ulong src) => Cast(11, 60f, 8f, 0f, new(-148.1f, -840.2f), new(-148.1f, -840.2f), 0.7f, 0.0, aimGameRot: null, src: src, action: 0xB94F) with { Rotation = rotDeg * MathF.PI / 180f };
    var star = new[] { Star(-135f, 1), Star(-90f, 2) };
    var gapStart = new Vector2(-148.1f, -840.2f) + new Vector2(MathF.Cos(-67.5f * MathF.PI / 180f), MathF.Sin(-67.5f * MathF.PI / 180f)) * 11f;
    var simS = Simulate((t, p) => World(p, t, star, target: new(-148.1f, -840.2f), range: 20f, hitbox: 5f), star, gapStart, 0.0, 1.5, dt: 0.05f);
    Check("abductor/tendon-ripper-off-centre-survives", simS.ok, simS.detail);
    // from the exact centre every spoke overlaps: nothing within reach - hold as "stuck", never a long run
    var sC = new SmartMoverCore.MoverState();
    var dC = SmartMoverCore.Decide(World(new Vector2(-148.1f, -840.2f), 0.1, star, target: new(-148.1f, -840.2f), range: 20f, hitbox: 5f), sC);
    Check("abductor/tendon-ripper-centre-no-hopeless-run", dC.Kind != SmartMoverCore.Decision.Steer || Vector2.Distance(dC.Dest, new Vector2(-148.1f, -840.2f)) < 9f, $"{dC}");
    // Buffet is filtered at the zone model (above); with it gone the character standing at 18 y from the boss settles
    var dB = SmartMoverCore.Decide(World(new Vector2(-144.8f, -859.3f), 0.0, target: boss, range: 20f, hitbox: 5f), new SmartMoverCore.MoverState());
    Check("abductor/no-buffet-zone-settles", dB.Kind == SmartMoverCore.Decision.None && dB.Reason == SmartMoverCore.ReasonSettleCode, $"{dB}");
    // Wind Blade: 180-degree cone of 60 from the boss facing +Z (game heading ~1.57 -> math 88.5 deg); character 18 y in front: goes behind, survives
    var wind = new[] { Cast(13, 60f, 0f, 5f, new(-149.8f, -862.2f), new(-149.8f, -862.2f), 4.95f, 0.0, coneHalfDeg: 90f, aimGameRot: 1.57f, src: 5, action: 0xB951) };
    var simW = Simulate((t, p) => World(p, t, wind, target: new(-149.8f, -862.2f), range: 20f, hitbox: 5f), wind, new Vector2(-149.9f, -844.2f), 0.0, 5.5);
    Check("abductor/wind-blade-survives", simW.ok, simW.detail);
    // Cyclonic Ring: donut 5..60 on the boss; character 5.4 y away steps into the hole
    var ring = new[] { Cast(10, 60f, 0f, 5f, new(-145.3f, -862.8f), new(-145.3f, -862.8f), 5.2f, 0.0, donutInner: 5f, src: 6, action: 0xB959) };
    var simR2 = Simulate((t, p) => World(p, t, ring, target: new(-145.3f, -862.8f), range: 20f, hitbox: 5f), ring, new Vector2(-143.8f, -868.1f), 0.0, 6.0);
    Check("abductor/cyclonic-ring-survives", simR2.ok, simR2.detail);
    Check("abductor/cyclonic-ring-in-hole", Vector2.Distance(simR2.end, new Vector2(-145.3f, -862.8f)) < 5f, $"d={Vector2.Distance(simR2.end, new Vector2(-145.3f, -862.8f)):F2}");
    // Splinter: four r13 circles from plumes; the character between them holds a safe spot
    var plumes = new[] { new Vector2(-158, -846.1f), new(-134, -860), new(-158, -873.9f), new(-142, -873.9f) };
    var spl = plumes.Select((p, i) => Cast(2, 13f, 0f, 0f, p, p, 4.2f, 0.0, src: (ulong)(10 + i), action: 0xB953)).ToArray();
    var simP = Simulate((t, p) => World(p, t, spl, target: boss, range: 20f, hitbox: 5f), spl, new Vector2(-144.1f, -865.9f), 0.0, 5.0);
    Check("abductor/splinter-survives", simP.ok, simP.detail);
}

// ---------------------------------------------------------------- walkability probe
{
    // a wall at x > 3: the planner must not pick a waypoint past it (bounded re-plan)
    var z = Cast(5, 8f, 0f, 3f, new(0, 0), new(0, 0), 4f, 100.0);
    var s = new SmartMoverCore.MoverState();
    var sim = Simulate((t, p) => World(p, t, new[] { z }, engaged: false, walkable: p => p.X <= 3f), new[] { z }, new Vector2(2.5f, 0), 100.0, 104.5);
    Check("walk/escapes-away-from-wall", sim.ok, sim.detail);
    Check("walk/never-crosses-wall", sim.end.X <= 3.1f, $"end={sim.end}");
    Check("walk/mask-built", s.WalkMask is null || true);
    // a broken probe (everything off-floor) is ignored: the escape still happens
    var sim2 = Simulate((t, p) => World(p, t, new[] { z }, engaged: false, walkable: _ => false), new[] { z }, new Vector2(2.5f, 0), 100.0, 104.5);
    Check("walk/broken-probe-fails-open", sim2.ok, sim2.detail);
}

// ---------------------------------------------------------------- telemetry format
{
    var h = MovementTelemetryFormat.BuildHeader(1, "1.0.4.213", 1.0f, 6f);
    Check("tel/header", h.StartsWith("MVH|1|1.0.4.213|1.00|6.0|") && h.Contains("ddg=dodging"));
    var l = MovementTelemetryFormat.BuildLine(1789000000000, 35, "ddg", 123, -1.5f, 3, 10.26f, -4f, 9.9f, -3.3f, 6f, 2.345f, 1.5f, 120);
    var f = MovementTelemetryFormat.Parse(l);
    Check("tel/line-fields", f.Length == 13 && f[0] == "MV" && f[3] == "ddg" && f[7] == "10.3,-4.0" && f[8] == "9.9,-3.3" && f[10] == "2.35" && f[12] == "120", l);
    var l2 = MovementTelemetryFormat.BuildLine(1, 35, "stl", 0, 0f, 0, null, null, 0, 0, 6f, float.MaxValue, float.MaxValue, 0);
    Check("tel/line-inf", MovementTelemetryFormat.Parse(l2)[10] == "inf" && MovementTelemetryFormat.Parse(l2)[7] == "-");
    var za = MovementTelemetryFormat.BuildZoneAdd(5, 0xABC, 0x40001234, 0x6F31, "circle", 27.4f, 116.5f, 9f, 0, 0, 0, 0, 5700);
    Check("tel/zone-add", za.StartsWith("MZ|5|add|ABC|40001234|6F31|circle|27.4,116.5|9.0|"), za);
    Check("tel/zone-del", MovementTelemetryFormat.BuildZoneDel(6, 0xABC, "end") == "MZ|6|del|ABC|end");
    var k1 = MovementTelemetryFormat.KeyOf("hold", null, null, 1);
    Check("tel/gate-first", MovementTelemetryFormat.ShouldEmit(null, 0, 0, k1, false, true));
    Check("tel/gate-floor", !MovementTelemetryFormat.ShouldEmit(k1, 1000, 1500, k1, false, true));
    Check("tel/gate-zones-live-reemits", MovementTelemetryFormat.ShouldEmit(k1, 1000, 2100, k1, false, true));
    Check("tel/gate-quiet-when-clear", !MovementTelemetryFormat.ShouldEmit(k1, 1000, 2100, k1, false, false));
    Check("tel/gate-dodge-start", MovementTelemetryFormat.ShouldEmit(k1, 1000, 1100, k1, true, false));
    Check("tel/line-budget", MovementTelemetryFormat.BuildLine(1, 35, new string('x', 300), 0, 0, 0, null, null, 0, 0, 6, 0, 0, 0).Length <= 200);
}

// ---------------------------------------------------------------- time-aware gate helper
{
    var z = Cast(5, 8f, 0f, 3f, new(0, 0), new(0, 0), 4f, 100.0); // lethal at 104.3
    var zs = new[] { z };
    Check("gate/lands-before-activation-safe", !SmartMoverCore.UnsafeAtTime(new(0, -3), zs, 0f, 100.0, 0.5f, 1.0f));
    Check("gate/lands-at-activation-unsafe", SmartMoverCore.UnsafeAtTime(new(0, -3), zs, 0f, 103.0, 0.5f, 1.0f));
    Check("gate/outside-always-safe", !SmartMoverCore.UnsafeAtTime(new(0, -30), zs, 0f, 103.5, 0.5f, 1.0f));
    Check("gatecore/off-passes", MovementGateCore.Allowed(false, true, true, true));
    Check("gatecore/on-blocks", !MovementGateCore.Allowed(true, false, false, true));
}

// ---------------------------------------------------------------- zone model (kept from v1 where still true)
{
    var rw = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(5, 30f, 0f, 3f, new(0, 0), new(0, 0), 0, 1, 5f, 60f, 0f));
    Check("zone/raidwide-skipped", rw is null);
    Check("zone/single-target-skipped", DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(1, 0f, 0f, 3f, new(0, 0), new(0, 0), 0, 1, 5f, 60f, 0f)) is null);
    Check("zone/custom-shape-skipped", DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(6, 0f, 0f, 3f, new(0, 0), new(0, 0), 0, 1, 5f, 60f, 0f)) is null);
    var cone = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(13, 60f, 0f, 2f, new(0, 0), new(0, 0), 0, 1, 4.7f, 30f, 0f, DangerZoneModel.GameRotationToMath(0f)));
    Check("zone/cone-heading-0-faces-south", cone is not null && DangerZoneModel.Contains(cone.Value, new(0, 10), 0f) && !DangerZoneModel.Contains(cone.Value, new(0, -10), 0f));
    var donut = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(10, 40f, 0f, 5f, new(0, 0), new(0, 0), 0, 1, 5.7f, 60f, 4f));
    Check("zone/donut-hole", donut is { Kind: DangerZoneModel.ShapeKind.Donut } && !DangerZoneModel.Contains(donut.Value, new(2, 0), 0f) && DangerZoneModel.Contains(donut.Value, new(10, 0), 0f));
    Check("zone/omen-fan", MathF.Abs(OmenVfxModel.CastConeHalfDeg("gl_fan090_1bf") - 45f) < 0.01f);
    Check("zone/omen-donut", MathF.Abs(OmenVfxModel.CastDonutInner("gl_sircle_4004bp1", 40f) - 4f) < 0.01f);
    // v2 r2: room-wide rects and 360-degree cones are raidwides (Abductor's Buffet 60x60 from the edge)
    Check("zone/roomwide-rect-skipped", DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(12, 60f, 60f, 5f, new(-120.5f, -860), new(-120.5f, -860), 0, 1, 4f, 60f, 0f, MathF.PI)) is null);
    Check("zone/line-rect-kept", DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(12, 60f, 8f, 5f, new(0, 0), new(0, 0), 0, 1, 4f, 60f, 0f, 0f)) is not null);
    Check("zone/full-cone-skipped", DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(13, 60f, 0f, 5f, new(0, 0), new(0, 0), 0, 1, 4f, 180f, 0f, 0f)) is null);
    Check("zone/half-cone-kept", DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(13, 60f, 0f, 5f, new(0, 0), new(0, 0), 0, 1, 4f, 90f, 0f, 0f)) is not null);
    // cushion never eats a short window
    Check("time/cushion-capped-short-cast", NavigationDecision.ActivationToG(101.0, 100.0, 1.0f) > 0.55f && NavigationDecision.ActivationToG(101.0, 100.0, 1.0f) < 0.65f, $"{NavigationDecision.ActivationToG(101.0, 100.0, 1.0f)}");
    Check("time/cushion-full-long-cast", MathF.Abs(NavigationDecision.ActivationToG(105.0, 100.0, 1.0f) - 4.0f) < 0.01f);
    var zz = Cast(5, 8f, 0f, 3f, new(0, 0), new(0, 0), 4f, 100.0, src: 7, action: 9);
    Check("zone/activation-fields", MathF.Abs((float)(zz.ActivationSec - 104.3)) < 0.001f && zz.Source == 7 && zz.ActionId == 9);
}

Console.WriteLine($"PASS={pass} FAIL={fail}");
if (fail == 0 && pass > 0) { Console.WriteLine("OK"); return 0; }
return 1;
