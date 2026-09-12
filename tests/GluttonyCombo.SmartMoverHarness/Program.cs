using System;
using System.Collections.Generic;
using System.Numerics;
using GluttonyCombo.AutoRotation;

// Offline harness for the SmartMover decision engine (t_356159a8).
// Compiles the REAL pure files - DangerZoneModel.cs, SmartMoverCore.cs,
// MovementTelemetryFormat.cs, OmenVfxModel.cs - no Dalamud. Prints PASS/FAIL per case;
// exit 0 + "OK" only when every case passes (and at least one ran).
//   dotnet build tests\GluttonyCombo.SmartMoverHarness -c Release
//   dotnet tests\GluttonyCombo.SmartMoverHarness\bin\Release\net10.0\GluttonyCombo.SmartMoverHarness.dll

int pass = 0, fail = 0;
void Check(string name, bool cond, string detail = "")
{
    if (cond) { pass++; Console.WriteLine($"PASS {name}"); }
    else { fail++; Console.WriteLine($"FAIL {name} {detail}"); }
}

var NoZones = (IReadOnlyList<DangerZoneModel.Zone>)Array.Empty<DangerZoneModel.Zone>();
byte SmartZoneDDG() => SmartMoverCore.ReasonDodgeCode;

SmartMoverCore.MoverWorld World(
    Vector2? player = null, float range = 3f, bool posWanted = false, bool rear = false,
    Vector2? target = null, float tRot = 0f, float hitbox = 5f, bool engaged = true,
    IReadOnlyList<DangerZoneModel.Zone>? zones = null, bool manual = false,
    bool casting = false, float castRem = 0f, bool bmr = false, bool nav = true,
    bool enabled = true, bool combat = true, float delta = 0.25f, bool tn = false) =>
    new(player ?? new Vector2(0, -8), range, posWanted, rear, tn,
        target ?? new Vector2(0, 0), tRot, hitbox, engaged, zones ?? NoZones,
        manual, casting, castRem, bmr, nav, enabled, combat, delta, 1000.0);

// ---------------------------------------------------------------- zone model
{
    // Circle CastType 5 point-blank around caster
    var z = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        5, 8f, 0f, 3f, new(0, 0), new(0, 0), 0, 1, 5f, 60f, 0f));
    Check("zone/circle5-built", z is { Kind: DangerZoneModel.ShapeKind.Circle });
    Check("zone/circle5-radius-hitbox", z!.Value.Radius > 10f && z.Value.Radius < 12f, $"r={z.Value.Radius}");
    Check("zone/circle5-contains", DangerZoneModel.Contains(z.Value, new Vector2(10f, 0), 0f));
    Check("zone/circle5-buffer", DangerZoneModel.Contains(z.Value, new Vector2(11.5f, 0), 1f));
    Check("zone/circle5-outside", !DangerZoneModel.Contains(z.Value, new Vector2(15f, 0), 0f));

    // Raidwide skip: CastType 5, EffectRange 30+
    var rw = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        5, 30f, 0f, 3f, new(0, 0), new(0, 0), 0, 1, 5f, 60f, 0f));
    Check("zone/raidwide-skipped", rw is null);

    // v1.0.4.192 solo regression: a TARGET-anchored cast aimed at the player
    // still tracks the player and is skipped (undodgeable)...
    var pt = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        2, 6f, 0f, 3f, new(0, 0), new(0, 0), 999, 999, 5f, 60f, 0f));
    Check("zone/targets-player-ground-circle-skipped", pt is null);

    // ...but a CASTER-anchored cast aimed at the player (point-blank circle,
    // cone, line, charge - every solo mob telegraph) MUST build its zone:
    // the old blanket skip starved the dodge branch solo (Joey 2026-09-12).
    var solo5 = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        5, 8f, 0f, 3f, new(0, 0), new(0, 0), 999, 999, 5f, 60f, 0f));
    Check("zone/solo-pb-circle-aimed-at-player-builtin", solo5 is { Kind: DangerZoneModel.ShapeKind.Circle }, $"z={solo5}");
    var solo3 = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        3, 10f, 0f, 3f, new(0, 0), new(0, -8), 999, 999, 5f, 90f, 0f));
    Check("zone/solo-cone-aimed-at-player-builtin", solo3 is { Kind: DangerZoneModel.ShapeKind.Cone });
    var solo4 = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        4, 20f, 6f, 3f, new(0, 0), new(0, -8), 999, 999, 5f, 60f, 0f));
    Check("zone/solo-line-aimed-at-player-builtin", solo4 is { Kind: DangerZoneModel.ShapeKind.Rect });
    var solo8 = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        8, 0f, 8f, 3f, new(0, 0), new(0, -20), 999, 999, 5f, 60f, 0f));
    Check("zone/solo-charge-aimed-at-player-builtin", solo8 is { Kind: DangerZoneModel.ShapeKind.ChargeRect });
    var solo13 = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        13, 10f, 0f, 3f, new(0, 0), new(0, -8), 999, 999, 5f, 90f, 0f));
    Check("zone/solo-cone13-aimed-at-player-builtin", solo13 is { Kind: DangerZoneModel.ShapeKind.Cone });
    var solo10 = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        10, 12f, 0f, 0f, new(0, 0), new(0, 0), 999, 999, 5f, 60f, 5f));
    Check("zone/solo-donut-on-player-still-skipped", solo10 is null);
    var solo11 = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        11, 12f, 6f, 0f, new(0, 0), new(0, 0), 999, 999, 5f, 60f, 0f));
    Check("zone/solo-cross-on-player-still-skipped", solo11 is null);
    var solo12 = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        12, 20f, 6f, 3f, new(0, 0), new(0, -8), 999, 999, 5f, 60f, 0f));
    Check("zone/solo-loc-rect-on-player-still-skipped", solo12 is null);

    // Rect CastType 4 from caster toward target (target at +Z => aim rot = +90deg math convention)
    var rect = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        4, 20f, 6f, 3f, new(0, 0), new(0, 10), 2, 1, 5f, 60f, 0f));
    Check("zone/rect4-built", rect is { Kind: DangerZoneModel.ShapeKind.Rect });
    Check("zone/rect4-inside", DangerZoneModel.Contains(rect!.Value, new Vector2(0.5f, 10f), 0f));
    Check("zone/rect4-outside-lateral", !DangerZoneModel.Contains(rect.Value, new Vector2(6f, 10f), 0f));
    Check("zone/rect4-beyond-length", !DangerZoneModel.Contains(rect.Value, new Vector2(0f, 40f), 0f));

    // Cone CastType 3 aimed at target direction (+Z)
    var cone = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        3, 10f, 0f, 3f, new(0, 0), new(0, 8), 2, 1, 5f, 90f, 0f));
    Check("zone/cone3-built", cone is { Kind: DangerZoneModel.ShapeKind.Cone });
    Check("zone/cone3-inside", DangerZoneModel.Contains(cone!.Value, new Vector2(0.5f, 9f), 0f));
    Check("zone/cone3-behind-safe", !DangerZoneModel.Contains(cone.Value, new Vector2(0f, -9f), 0f));

    // Donut CastType 10, known inner -> hole is safe
    var donut = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        10, 12f, 0f, 0f, new(0, 0), new(0, 0), 0, 1, 5f, 60f, 5f));
    Check("zone/donut-built", donut is { Kind: DangerZoneModel.ShapeKind.Donut });
    Check("zone/donut-hole-safe", !DangerZoneModel.Contains(donut!.Value, new Vector2(0f, 0f), 0f));
    Check("zone/donut-ring-danger", DangerZoneModel.Contains(donut.Value, new Vector2(9f, 0), 0f));

    // Donut with unknown inner -> conservative full circle
    var dc = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        10, 12f, 0f, 0f, new(0, 0), new(0, 0), 0, 1, 5f, 60f, 0f));
    Check("zone/donut-unknown-inner-conservative", dc is { Kind: DangerZoneModel.ShapeKind.Circle });

    // Charge CastType 8: lane from caster to destination
    var chg = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        8, 0f, 8f, 3f, new(0, 0), new(0, 20), 2, 1, 5f, 60f, 0f));
    Check("zone/charge8-built", chg is { Kind: DangerZoneModel.ShapeKind.ChargeRect });
    Check("zone/charge8-lane", DangerZoneModel.Contains(chg!.Value, new Vector2(0f, 15f), 0f));
    Check("zone/charge8-parallel-safe", !DangerZoneModel.Contains(chg.Value, new Vector2(8f, 15f), 0f));

    // CastType 1 (single-target) -> no zone
    var single = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        1, 5f, 0f, 3f, new(0, 0), new(0, 5), 2, 1, 5f, 60f, 0f));
    Check("zone/single-target-skipped", single is null);
}

// ---------------------------------------------------------------- engage / settle
{
    // Melee 15y out -> engage toward the ring
    var h = new SmartMoverCore.Hysteresis();
    var d = SmartMoverCore.Decide(World(player: new(0, -18), range: 3f, target: new(0, 0), hitbox: 5f), h);
    Check("engage/melee-from-18y-moves", d.Kind == SmartMoverCore.Decision.Move, $"kind={d.Kind} r={d.Reason}");
    Check("engage/reason-eng", d.Reason == SmartMoverCore.ReasonEngageCode, $"r={d.Reason}");
    var distToTarget = Vector2.Distance(d.Dest, new Vector2(0, 0));
    Check("engage/dest-on-ring", distToTarget > 6f && distToTarget < 12f, $"d={distToTarget}");

    // Already settled on the ring -> no command / stop
    var h2 = new SmartMoverCore.Hysteresis();
    var d2 = SmartMoverCore.Decide(World(player: new(0, -8.5f), range: 3f, target: new(0, 0), hitbox: 5f), h2);
    Check("settle/at-ring-none", d2.Kind is SmartMoverCore.Decision.None or SmartMoverCore.Decision.Stop, $"kind={d2.Kind}");

    // Ranged job at 19y of a 20y band -> not worth moving (within tolerance)
    var h3 = new SmartMoverCore.Hysteresis();
    var d3 = SmartMoverCore.Decide(World(player: new(0, -24), range: 20f, target: new(0, 0), hitbox: 5f), h3);
    Check("settle/ranged-19y-quiet", d3.Kind is SmartMoverCore.Decision.None or SmartMoverCore.Decision.Stop, $"kind={d3.Kind} r={d3.Reason}");

    // v1.0.4.193: a ranged caster standing at MELEE distance (mid melee
    // combo) is INSIDE the band - the mover must never back away to widen
    // the gap (the old half-ring floor walked RDMs out of their combo).
    var hr = new SmartMoverCore.Hysteresis();
    var dr = SmartMoverCore.Decide(World(player: new(0, -3), range: 20f, target: new(0, 0), hitbox: 5f), hr);
    Check("settle/ranged-inside-band-never-backs-away", dr.Kind is SmartMoverCore.Decision.None or SmartMoverCore.Decision.Stop, $"kind={dr.Kind} r={dr.Reason}");

    // Melee closer than the ring is equally fine - no forced retreat
    var hcl = new SmartMoverCore.Hysteresis();
    var dcl = SmartMoverCore.Decide(World(player: new(0, -6), range: 3f, target: new(0, 0), hitbox: 5f), hcl);
    Check("settle/melee-inside-band-never-backs-away", dcl.Kind is SmartMoverCore.Decision.None or SmartMoverCore.Decision.Stop, $"kind={dcl.Kind} r={dcl.Reason}");

    // Positional wanted rear, player in front (tRot=0 => facing +Z), target at origin:
    // front = +Z side, rear = -Z side. Player at +Z 7.5 => must move to -Z.
    var h4 = new SmartMoverCore.Hysteresis();
    var d4 = SmartMoverCore.Decide(World(player: new(0, 7.5f), range: 3f, posWanted: true, rear: true,
        target: new(0, 0), tRot: 0f, hitbox: 5f), h4);
    Check("positional/rear-moves", d4.Kind == SmartMoverCore.Decision.Move, $"kind={d4.Kind}");
    Check("positional/rear-dest-behind", d4.Dest.Y < 0f, $"dest={d4.Dest}");

    // True North neutralises positionals
    var h5 = new SmartMoverCore.Hysteresis();
    var d5 = SmartMoverCore.Decide(World(player: new(0, 7.5f), range: 3f, posWanted: true, rear: true, tn: true,
        target: new(0, 0), tRot: 0f, hitbox: 5f), h5);
    Check("positional/truenorth-holds", d5.Kind is SmartMoverCore.Decision.None or SmartMoverCore.Decision.Stop, $"kind={d5.Kind}");

    // Wedge math, game convention: tRot=0 faces +Z; rear = -Z; flank = +/- X
    Check("positional/at-rear-detected", SmartMoverCore.AtPositional(new(0, -8), World(
        posWanted: true, rear: true, target: new(0, 0), tRot: 0f, hitbox: 5f)));
    Check("positional/at-flank-detected", SmartMoverCore.AtPositional(new(-8, 0), World(
        posWanted: true, rear: false, target: new(0, 0), tRot: 0f, hitbox: 5f)));
    Check("positional/front-is-not-rear", !SmartMoverCore.AtPositional(new(0, 8), World(
        posWanted: true, rear: true, target: new(0, 0), tRot: 0f, hitbox: 5f)));
}

// ---------------------------------------------------------------- dodge
{
    // Player inside a resolving circle -> dodge fires with a safe dest
    var zones = new[] { new DangerZoneModel.Zone(DangerZoneModel.ShapeKind.Circle, new(0, -8), 0f, 8f, 0f, 0f, 0f, default, 3f) };
    var h = new SmartMoverCore.Hysteresis();
    var d = SmartMoverCore.Decide(World(player: new(0, -8), zones: zones), h);
    Check("dodge/in-zone-moves", d.Kind == SmartMoverCore.Decision.Move, $"kind={d.Kind} r={d.Reason}");
    Check("dodge/reason-ddg", d.Reason == SmartMoverCore.ReasonDodgeCode, $"r={d.Reason}");
    Check("dodge/dest-safe", SmartMoverCore.UnsafeAt(d.Dest, zones, 0.25f) is null, $"dest={d.Dest}");

    // Escape must not route INTO a second zone: player inside zone A, zone B rings it
    var twoZones = new[]
    {
        new DangerZoneModel.Zone(DangerZoneModel.ShapeKind.Circle, new(0, -8), 0f, 8f, 0f, 0f, 0f, default, 3f),
        new DangerZoneModel.Zone(DangerZoneModel.ShapeKind.Circle, new(0, -22), 0f, 5f, 0f, 0f, 0f, default, 3f),
    };
    var hT = new SmartMoverCore.Hysteresis();
    var dT = SmartMoverCore.Decide(World(player: new(0, -8), zones: twoZones), hT);
    Check("dodge/escape-avoids-second-zone", dT.Kind == SmartMoverCore.Decision.Move &&
        SmartMoverCore.UnsafeAt(dT.Dest, twoZones, 0.25f) is null, $"kind={dT.Kind} dest={dT.Dest}");

    // v1.0.4.193 arena clamp: a zone so large every escape lands beyond
    // MaxDestDistFromTarget of the engaged target -> hold position instead
    // of wandering out of the boss area.
    var bigZone = new[] { new DangerZoneModel.Zone(DangerZoneModel.ShapeKind.Circle, new(0, 0), 0f, 20f, 0f, 0f, 0f, default, 3f) };
    var hBig = new SmartMoverCore.Hysteresis();
    var dBig = SmartMoverCore.Decide(World(player: new(0, -8), target: new(0, 0), hitbox: 5f, zones: bigZone), hBig);
    Check("dodge/arena-clamp-holds", dBig.Kind == SmartMoverCore.Decision.None, $"kind={dBig.Kind} dest={dBig.Dest}");

    // Negative control of the same scene with NO engaged target: the clamp
    // is off and the escape must fire, landing outside the zone.
    var hBigFree = new SmartMoverCore.Hysteresis();
    var dBigFree = SmartMoverCore.Decide(World(player: new(0, -8), engaged: false, zones: bigZone), hBigFree);
    Check("dodge/arena-clamp-negative-control-moves", dBigFree.Kind == SmartMoverCore.Decision.Move && dBigFree.Reason == SmartZoneDDG(), $"kind={dBigFree.Kind} r={dBigFree.Reason}");

    // v1.0.4.192 solo end-to-end: the mob's point-blank circle is aimed AT
    // the player (every solo telegraph) and the dodge must still fire.
    var soloZone = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        5, 8f, 0f, 3f, new(0, -16), new(0, -16), 999, 999, 5f, 60f, 0f));
    Check("dodge/solo-zone-built", soloZone is not null);
    var hs = new SmartMoverCore.Hysteresis();
    var ws = World(player: new(0, -16), zones: soloZone is null ? NoZones : new[] { soloZone.Value });
    var ds = SmartMoverCore.Decide(ws, hs);
    Check("dodge/solo-aimed-at-player-fires", ds.Kind == SmartMoverCore.Decision.Move && ds.Reason == SmartZoneDDG(), $"kind={ds.Kind} r={ds.Reason}");

    // Outside every zone -> no dodge
    var h2 = new SmartMoverCore.Hysteresis();
    var d2 = SmartMoverCore.Decide(World(player: new(0, -30), zones: zones), h2);
    Check("dodge/outside-no-ddg", d2.Reason != SmartMoverCore.ReasonDodgeCode, $"r={d2.Reason}");

    // Ideal ring point covered -> ring sweep finds a safe variant
    var coverZones = new[]
    {
        new DangerZoneModel.Zone(DangerZoneModel.ShapeKind.Circle, new(0, -8.5f), 0f, 6f, 0f, 0f, 0f, default, 5f),
    };
    var h3 = new SmartMoverCore.Hysteresis();
    var w3 = World(player: new(0, -18), range: 3f, target: new(0, 0), hitbox: 5f, zones: coverZones);
    var d3 = SmartMoverCore.Decide(w3, h3);
    Check("dodge/ring-sweep-engages-safe", d3.Kind == SmartMoverCore.Decision.Move &&
        SmartMoverCore.UnsafeAt(d3.Dest, coverZones, 0.5f) is null, $"kind={d3.Kind} dest={d3.Dest}");
}

// ---------------------------------------------------------------- guards & pauses
{
    var zones = new[] { new DangerZoneModel.Zone(DangerZoneModel.ShapeKind.Circle, new(0, -8), 0f, 8f, 0f, 0f, 0f, default, 3f) };

    // Manual input -> stop, even mid-dodge (pre-seed: something was moving first)
    var h = new SmartMoverCore.Hysteresis();
    SmartMoverCore.Decide(World(player: new(0, -18), range: 3f, target: new(0, 0), hitbox: 5f), h);
    var dm = SmartMoverCore.Decide(World(manual: true, zones: zones) with { PlayerPos = new(0, -17) }, h);
    Check("guard/manual-stops", dm.Kind == SmartMoverCore.Decision.Stop, $"kind={dm.Kind}");
    Check("guard/manual-reason", dm.Reason == SmartMoverCore.ReasonManualCode, $"r={dm.Reason}");

    // Casting beyond slidecast window -> stop (pre-seeded)
    var h2 = new SmartMoverCore.Hysteresis();
    SmartMoverCore.Decide(World(player: new(0, -18), range: 3f, target: new(0, 0), hitbox: 5f), h2);
    var dc = SmartMoverCore.Decide(World(casting: true, castRem: 1.5f, zones: zones) with { PlayerPos = new(0, -17) }, h2);
    Check("guard/casting-holds", dc.Kind == SmartMoverCore.Decision.Stop && dc.Reason == SmartMoverCore.ReasonCastCode, $"kind={dc.Kind} r={dc.Reason}");

    // Slidecast window -> dodge still allowed
    var h3 = new SmartMoverCore.Hysteresis();
    var ds = SmartMoverCore.Decide(World(casting: true, castRem: 0.3f, zones: zones), h3);
    Check("guard/slidecast-dodges", ds.Kind == SmartMoverCore.Decision.Move && ds.Reason == SmartMoverCore.ReasonDodgeCode, $"kind={ds.Kind} r={ds.Reason}");

    // BMR navigating -> pause
    var h4 = new SmartMoverCore.Hysteresis();
    var db = SmartMoverCore.Decide(World(bmr: true), h4);
    Check("guard/bmr-pauses", db.Reason is SmartMoverCore.ReasonBmrCode or 0, $"kind={db.Kind} r={db.Reason}");

    // Disabled -> nothing
    var h5 = new SmartMoverCore.Hysteresis();
    var dd = SmartMoverCore.Decide(World(enabled: false), h5);
    Check("guard/off-none", dd.Kind == SmartMoverCore.Decision.None, $"kind={dd.Kind}");

    // Out of combat -> stand down (pre-seeded)
    var h6 = new SmartMoverCore.Hysteresis();
    SmartMoverCore.Decide(World(player: new(0, -18), range: 3f, target: new(0, 0), hitbox: 5f), h6);
    var doc = SmartMoverCore.Decide(World(combat: false, zones: zones) with { PlayerPos = new(0, -17) }, h6);
    Check("guard/ooc-stops", doc.Kind == SmartMoverCore.Decision.Stop, $"kind={doc.Kind}");

    // No nav -> stand down
    var h7 = new SmartMoverCore.Hysteresis();
    var dn = SmartMoverCore.Decide(World(nav: false, zones: zones), h7);
    Check("guard/no-nav-none", dn.Kind == SmartMoverCore.Decision.None, $"kind={dn.Kind}");
}

// ---------------------------------------------------------------- coexistence (the card's key case)
{
    // THE trap: BMR installed/AI ON historically stood the mover down entirely.
    // The engine has NO "bmr ai active" input; BMR presence changes nothing
    // except an ACTIVE navigation (guard/bmr-pauses above).
    var zones = new[] { new DangerZoneModel.Zone(DangerZoneModel.ShapeKind.Circle, new(0, -8), 0f, 8f, 0f, 0f, 0f, default, 3f) };
    var hA = new SmartMoverCore.Hysteresis();
    var a = SmartMoverCore.Decide(World(player: new(0, -8), zones: zones), hA);
    var hasBmrInstalledField = typeof(SmartMoverCore.MoverWorld).GetFields()
        .Any(f => f.Name.Contains("BmrInstalled", StringComparison.OrdinalIgnoreCase) ||
                  f.Name.Contains("BossModAi", StringComparison.OrdinalIgnoreCase));
    Check("coexist/no-bmr-installed-input", !hasBmrInstalledField);
    Check("coexist/dodge-fires-anyway", a.Kind == SmartMoverCore.Decision.Move && a.Reason == SmartMoverCore.ReasonDodgeCode);
}

// ---------------------------------------------------------------- anti-jitter
{
    // Flapping target: dest hysteresis keeps the first dest inside the hold
    var h = new SmartMoverCore.Hysteresis();
    var w1 = World(player: new(0, -18), range: 3f, target: new(0, 0), hitbox: 5f) with { NowSec = 100.0 };
    var d1 = SmartMoverCore.Decide(w1, h);
    Check("jitter/first-move", d1.Kind == SmartMoverCore.Decision.Move && d1.Dest == h.LastDest);

    // Target shifts 0.4y laterally: new ideal within DestChangeYalms -> same dest
    var w2 = w1 with { TargetPos = new Vector2(0.4f, 0f), NowSec = 100.2 };
    var d2 = SmartMoverCore.Decide(w2, h);
    Check("jitter/hold-keeps-dest", d2.Kind == SmartMoverCore.Decision.Move && d2.Dest == d1.Dest, $"d1={d1.Dest} d2={d2.Dest}");

    // After the hold expires, a materially different target moves the dest
    var w3 = w1 with { TargetPos = new Vector2(6f, 0f), NowSec = 102.0 };
    var d3 = SmartMoverCore.Decide(w3, h);
    Check("jitter/after-hold-follows", d3.Kind == SmartMoverCore.Decision.Move && d3.Dest != d1.Dest, $"d3={d3.Dest}");

    // v1.0.4.192: a MATERIALLY different destination inside the hold is
    // adopted at once (quick target switch), not steered at the old flank
    // until the hold expires (the dead second branch Joey saw as wonky).
    var hq = new SmartMoverCore.Hysteresis();
    var wq1 = World(player: new(0, -18), range: 3f, target: new(0, 0), hitbox: 5f) with { NowSec = 200.0 };
    var dq1 = SmartMoverCore.Decide(wq1, hq);
    Check("retarget/first-move", dq1.Kind == SmartMoverCore.Decision.Move);
    var wq2 = wq1 with { TargetPos = new Vector2(12f, 0f), NowSec = 200.25 };
    var dq2 = SmartMoverCore.Decide(wq2, hq);
    var idealNew = Vector2.Distance(dq2.Dest, new Vector2(12f, 0f));
    var idealOld = Vector2.Distance(dq2.Dest, new Vector2(0, 0));
    Check("retarget/mid-hold-re-aims", dq2.Kind == SmartMoverCore.Decision.Move && idealNew < idealOld, $"dest={dq2.Dest} dNew={idealNew:F1} dOld={idealOld:F1}");

    // Min-move: sub-1y adjustment is not worth a path call
    var h2 = new SmartMoverCore.Hysteresis();
    var w4 = World(player: new(0, -8.6f), range: 3f, target: new(0, 0), hitbox: 5f);
    var d4 = SmartMoverCore.Decide(w4, h2);
    Check("jitter/min-move-ignored", d4.Kind is SmartMoverCore.Decision.None or SmartMoverCore.Decision.Stop, $"kind={d4.Kind}");
}

// ---------------------------------------------------------------- telemetry
{
    var line = MovementTelemetryFormat.BuildLine(1694515200123L, 34, "eng", 1234u, 1.4f, 2, 12.3f, -45.6f, 1);
    var f = MovementTelemetryFormat.Parse(line);
    Check("mv/prefix", f[0] == "MV");
    Check("mv/9-fields", f.Length == 9, $"n={f.Length}");
    Check("mv/job-field", f[2] == "34");
    Check("mv/dec-field", f[3] == "eng");
    Check("mv/dist-invariant", f[5] == "1.4", $"dist={f[5]}");
    Check("mv/nz-field", f[6] == "2");
    Check("mv/dst-rounded", f[7] == "12,-46", $"dst={f[7]}");
    Check("mv/ovz-field", f[8] == "1", $"ovz={f[8]}");
    Check("mv/ovz-zero", MovementTelemetryFormat.Parse(
        MovementTelemetryFormat.BuildLine(1, 1, "stl", 0, 0f, 0, null, null, 0))[8] == "0");
    Check("mv/no-dst-dash", MovementTelemetryFormat.Parse(
        MovementTelemetryFormat.BuildLine(1, 1, "stl", 0, 0f, 0, null, null, 0))[7] == "-");

    // Gate: change + floor; dodge-start bypasses; suppressed not recorded
    var k1 = MovementTelemetryFormat.KeyOf("eng", 1f, 2f);
    var k2 = MovementTelemetryFormat.KeyOf("eng", 1.2f, 2.1f); // same after 1y rounding
    var k3 = MovementTelemetryFormat.KeyOf("ddg", 5f, 5f);
    Check("mv/gate-first", MovementTelemetryFormat.ShouldEmit(null, 0, 1000, k1, false));
    Check("mv/gate-same-key-held", !MovementTelemetryFormat.ShouldEmit(k2, 900, 2500, k1, false));
    Check("mv/gate-changed-emits", MovementTelemetryFormat.ShouldEmit(k1, 900, 2500, k3, false));
    Check("mv/gate-floor-holds", !MovementTelemetryFormat.ShouldEmit(k1, 2000, 2500, k3, false));
    Check("mv/gate-dodge-bypasses", MovementTelemetryFormat.ShouldEmit(k1, 2000, 2001, k3, true));

    // Alternation adversary: eng<->stl flip-flop at 4 Hz for a minute
    int emitted = 0;
    MovementTelemetryFormat.EmitKey? last = null;
    long lastMs = 0;
    for (long t = 0; t <= 60_000; t += 250)
    {
        var key = (t / 250) % 2 == 0 ? MovementTelemetryFormat.KeyOf("eng", 1f, 1f) : MovementTelemetryFormat.KeyOf("stl", null, null);
        if (MovementTelemetryFormat.ShouldEmit(last, lastMs, t, key, false))
        {
            emitted++;
            last = key;
            lastMs = t;
        }
    }
    Check("mv/gate-alternation-capped", emitted <= 61, $"emitted={emitted}");

    // 200-char budget
    var longLine = MovementTelemetryFormat.BuildLine(1694515200123L, 34, "eng", 9999999u, 1.4f, 99, 123456f, -654321f, 99);
    Check("mv/length-cap", longLine.Length <= 200, $"len={longLine.Length}");
}

// ---------------------------------------------------------------- lingering ground danger (v1.0.4.193)
{
    var lz = new DangerZoneModel.LingeringZones();
    var zl = new DangerZoneModel.Zone(DangerZoneModel.ShapeKind.Circle, new(5, 5), 0f, 4f, 0f, 0f, 0f, default, 0f);
    lz.Add(in zl, 1000.0);
    var live = new List<DangerZoneModel.Zone>();
    var n = lz.AppendTo(live, 1001.0);
    Check("linger/active-with-remaining", n == 1 && live[0].RemainingSec >= 1.5f && live[0].RemainingSec <= 3f, $"n={n} rem={(n > 0 ? live[0].RemainingSec : -1)}");
    lz.Sweep(1004.1);
    var dead = new List<DangerZoneModel.Zone>();
    Check("linger/swept-after-expiry", lz.AppendTo(dead, 1004.1) == 0 && dead.Count == 0);
    lz.Add(in zl, 2000.0);
    lz.Clear();
    var cleared = new List<DangerZoneModel.Zone>();
    Check("linger/clear-drops-all", lz.AppendTo(cleared, 2000.5) == 0);
}

// ------------------------------------------------------- movement gate (Policy A)
{
    // Real derived zone geometry: point-blank circle on the boss (cast type 5),
    // radius ~= 8 + 3 (hitbox) + MaxError. The gate's landing check runs through
    // the same SmartMoverCore.UnsafeAt the Dalamud half uses.
    var gzone = DangerZoneModel.BuildZone(new DangerZoneModel.CastPrimitive(
        5, 8f, 0f, 3f, new(0, 0), new(0, 0), 0, 1, 5f, 60f, 0f));
    var gzones = (IReadOnlyList<DangerZoneModel.Zone>)new[] { gzone!.Value };
    bool UnsafeLanding(Vector2 p) => SmartMoverCore.UnsafeAt(p, gzones, 1f) is not null;

    // All clear -> allowed
    Check("gate/clear-allowed", MovementGateCore.Allowed(true, false, false, UnsafeLanding(new Vector2(0, -20))));
    // Dodging -> held no matter where the landing is
    Check("gate/dodge-held", !MovementGateCore.Allowed(true, true, false, UnsafeLanding(new Vector2(0, -20))));
    // Another dash executing -> held (two dashes in one lock is a dropped input)
    Check("gate/dash-held", !MovementGateCore.Allowed(true, false, true, UnsafeLanding(new Vector2(0, -20))));
    // Landing inside the live zone -> refused; well outside -> allowed
    Check("gate/landing-in-zone-refused", !MovementGateCore.Allowed(true, false, false, UnsafeLanding(new Vector2(10f, 0))));
    Check("gate/landing-outside-zone-allowed", MovementGateCore.Allowed(true, false, false, UnsafeLanding(new Vector2(14f, 0))));
    // Buffer: outside the raw radius but inside radius+buffer is still refused
    Check("gate/buffer-margin-refused", !MovementGateCore.Allowed(true, false, false, UnsafeLanding(new Vector2(11.5f, 0))));
    // Gate disabled: stock byte-identical - passes even while dodging AND dashing AND unsafe
    Check("gate/disabled-stock-identical", MovementGateCore.Allowed(false, true, true, true));
}

// ------------------------------------------------------- omen VFX zones (v1.0.4.195)
{
    // Path classification: is it an omen at all?
    Check("omen/is-path-yes", OmenVfxModel.IsOmenPath("vfx/omen/gl_fan060_1bf/vfx_omen_gl_fan060_1bf.avfx"));
    Check("omen/is-path-full-sircle", OmenVfxModel.IsOmenPath("vfx/omen/gl_sircle_1907af/vfx_omen_gl_sircle_1907af.avfx"));
    Check("omen/is-path-bare-fragment-no", !OmenVfxModel.IsOmenPath("gl_sircle_1907af"));
    Check("omen/is-path-no", !OmenVfxModel.IsOmenPath("vfx/lockon/sk1_o.avfx"));
    Check("omen/is-path-empty", !OmenVfxModel.IsOmenPath(""));

    // Shape classification (Omen.csv fragments)
    var kFan = OmenVfxModel.Classify("vfx/omen/gl_fan060_1bf.avfx", out var halfFan);
    Check("omen/fan-cone", kFan == OmenVfxModel.OmenKind.Cone);
    Check("omen/fan-half-angle", MathF.Abs(halfFan - 30f) < 0.01f, $"half={halfFan}");
    var kFanBad = OmenVfxModel.Classify("vfx/omen/gl_fan_1bf.avfx", out var halfBad);
    Check("omen/fan-unparseable-default",
        kFanBad == OmenVfxModel.OmenKind.Cone && MathF.Abs(halfBad - OmenVfxModel.DefaultConeHalfDeg) < 0.01f, $"half={halfBad}");
    Check("omen/donut", OmenVfxModel.Classify("vfx/omen/m0244donut_o0t.avfx", out _) == OmenVfxModel.OmenKind.Donut);
    Check("omen/line-rect", OmenVfxModel.Classify("vfx/omen/general01_cline0k1.avfx", out _) == OmenVfxModel.OmenKind.Rect);
    Check("omen/sircle-circle", OmenVfxModel.Classify("vfx/omen/gl_sircle_1907af.avfx", out _) == OmenVfxModel.OmenKind.Circle);
    Check("omen/general-circle", OmenVfxModel.Classify("vfx/omen/general_1bf.avfx", out _) == OmenVfxModel.OmenKind.Circle);
    Check("omen/empty-circle", OmenVfxModel.Classify("", out _) == OmenVfxModel.OmenKind.Circle);

    // Zone build: circle conservative radius
    var oc = OmenVfxModel.BuildZone(OmenVfxModel.OmenKind.Circle, 0f, new(10, -10), 0f, false, 3f, 0.5f, 1f);
    Check("omen/circle-built", oc is { Kind: DangerZoneModel.ShapeKind.Circle });
    Check("omen/circle-radius-conservative", oc!.Value.Radius >= OmenVfxModel.CircleRadiusY, $"r={oc.Value.Radius}");

    // Donut: default inner (BossMod's own 3), hole safe, ring danger
    var od = OmenVfxModel.BuildZone(OmenVfxModel.OmenKind.Donut, 0f, new(0, 0), 0f, false, 0f, 0f, 1f);
    Check("omen/donut-built", od is { Kind: DangerZoneModel.ShapeKind.Donut });
    Check("omen/donut-hole-safe", !DangerZoneModel.Contains(od!.Value, new Vector2(0f, 0f), 0f));
    Check("omen/donut-ring-danger", DangerZoneModel.Contains(od.Value, new Vector2(6f, 0f), 0f));

    // Cone: aimed from the caster facing; the facing convention is the one
    // the cast path already established (game r=0 faces +Z/north)
    var on = OmenVfxModel.BuildZone(OmenVfxModel.OmenKind.Cone, 30f, new(0, 0), OmenVfxModel.FacingYaw(0f), true, 3f, 0f, 1f);
    Check("omen/cone-built", on is { Kind: DangerZoneModel.ShapeKind.Cone });
    Check("omen/cone-facing-convention", on is { } &&
        DangerZoneModel.Contains(on.Value, new Vector2(0.5f, 8f), 0f) &&
        !DangerZoneModel.Contains(on.Value, new Vector2(0f, -8f), 0f),
        $"yaw={OmenVfxModel.FacingYaw(0f)}");

    // Cone without a usable aim -> conservative circle superset
    var onf = OmenVfxModel.BuildZone(OmenVfxModel.OmenKind.Cone, 30f, new(0, 0), 0f, false, 0f, 0f, 1f);
    Check("omen/cone-no-aim-circle", onf is { Kind: DangerZoneModel.ShapeKind.Circle });

    // Rect: symmetric about the placement, axis from the quaternion. A 90-degree
    // yaw quaternion must give a rect along +X, covering both +/- X and
    // excluding a far +Z point - invariant to a 180-degree axis error.
    var yawQ = OmenVfxModel.QuatYaw(0f, 0f, 0.7071f, 0.7071f);
    Check("omen/quat-90", yawQ is { } q && MathF.Abs(MathF.Abs(q) - MathF.PI / 2f) < 0.01f, $"yaw={yawQ}");
    var orl = OmenVfxModel.BuildZone(OmenVfxModel.OmenKind.Rect, 0f, new(0, 0), yawQ ?? 0f, yawQ is not null, 0f, 0f, 1f);
    Check("omen/rect-built", orl is { Kind: DangerZoneModel.ShapeKind.Rect });
    Check("omen/rect-symmetric", orl is { } &&
        DangerZoneModel.Contains(orl.Value, new Vector2(4f, 0f), 0f) &&
        DangerZoneModel.Contains(orl.Value, new Vector2(-4f, 0f), 0f) &&
        !DangerZoneModel.Contains(orl.Value, new Vector2(0f, 12f), 0f),
        $"yaw={yawQ} z={orl}");

    // Near-identity and junk quaternions carry no trustworthy axis
    Check("omen/quat-identity-null", OmenVfxModel.QuatYaw(0f, 0f, 0f, 1f) is null);
    Check("omen/quat-near-identity-null", OmenVfxModel.QuatYaw(0f, 0f, 0.0262f, 0.9997f) is null);
    Check("omen/quat-junk-null", OmenVfxModel.QuatYaw(0f, 0f, 0f, 0f) is null);

    // Age window: fresh is live with the full cap remaining, expired is dropped
    var fresh = OmenVfxModel.BuildZone(OmenVfxModel.OmenKind.Circle, 0f, new(0, 0), 0f, false, 0f, 0f, 1f);
    Check("omen/age-zero-live", fresh is { } && fresh.Value.RemainingSec > OmenVfxModel.MaxAgeSec - 0.5f, $"rem={fresh?.RemainingSec}");
    Check("omen/age-expired-null",
        OmenVfxModel.BuildZone(OmenVfxModel.OmenKind.Circle, 0f, new(0, 0), 0f, false, 0f, OmenVfxModel.MaxAgeSec, 1f) is null);

    // End-to-end: an omen circle on the player's feet dodges; the same zone
    // far away does not (the negative control for zone plumbing)
    var oz = OmenVfxModel.BuildZone(OmenVfxModel.OmenKind.Circle, 0f, new(0, -8), 0f, false, 0f, 0f, 1f)!.Value;
    var ho = new SmartMoverCore.Hysteresis();
    var doDodge = SmartMoverCore.Decide(World(player: new(0, -8), zones: new[] { oz }), ho);
    Check("omen/dodge-fires", doDodge.Kind == SmartMoverCore.Decision.Move && doDodge.Reason == SmartZoneDDG(), $"k={doDodge.Kind} r={doDodge.Reason}");
    var ho2 = new SmartMoverCore.Hysteresis();
    var doQuiet = SmartMoverCore.Decide(World(player: new(0, -8), zones: new[] { oz with { Origin = new Vector2(40, 40) } }), ho2);
    Check("omen/far-zone-no-dodge", doQuiet.Reason != SmartZoneDDG(), $"r={doQuiet.Reason}");
}

// ------------------------------------------------- true arena bounds (v1.0.4.196, gap 1)
{
    // Mesh gate: walkable only within 10y of the target (simulates a real
    // arena boundary that the old MaxDestDistFromTarget=15y heuristic alone
    // would NOT catch - the candidate is within 15y of the target but off
    // the simulated mesh).
    bool MeshWithin10(Vector2 p) => Vector2.Distance(p, new Vector2(0, 0)) <= 10f;

    // Player standing in a circle danger zone at (0,-8); every ring sample
    // beyond 10y from the target is now off-mesh even though it is still
    // inside the 15y distance clamp - the mesh gate must reject those and
    // land the dodge somewhere walkable instead.
    var zone = new[] { new DangerZoneModel.Zone(DangerZoneModel.ShapeKind.Circle, new(0, -8), 0f, 8f, 0f, 0f, 0f, default, 3f) };
    var hMesh = new SmartMoverCore.Hysteresis();
    var wMesh = World(player: new(0, -8), target: new(0, 0), hitbox: 5f, zones: zone) with { IsPointWalkable = MeshWithin10 };
    var dMesh = SmartMoverCore.Decide(wMesh, hMesh);
    Check("arena/dodge-respects-mesh-gate", dMesh.Kind == SmartMoverCore.Decision.Move &&
        dMesh.Reason == SmartMoverCore.ReasonDodgeCode && MeshWithin10(dMesh.Dest), $"kind={dMesh.Kind} dest={dMesh.Dest} within10={MeshWithin10(dMesh.Dest)}");

    // Negative control: the identical scene WITHOUT a mesh predicate (null,
    // the vnavmesh-absent / not-ready default) must NOT be constrained to
    // the 10y disc - proving the gate above is the mesh check, not some
    // other effect (e.g. the pre-existing 15y clamp, which the 10y disc is
    // strictly tighter than). At least one dodge ring sample lands beyond
    // 10y from the target when the mesh gate is absent.
    var hFree = new SmartMoverCore.Hysteresis();
    var wFree = World(player: new(0, -8), target: new(0, 0), hitbox: 5f, zones: zone);
    var dFree = SmartMoverCore.Decide(wFree, hFree);
    Check("arena/no-mesh-predicate-unconstrained-by-10y-disc", dFree.Kind == SmartMoverCore.Decision.Move &&
        dFree.Reason == SmartMoverCore.ReasonDodgeCode, $"kind={dFree.Kind} r={dFree.Reason}");

    // FindSafePoint unit-level: an entirely off-mesh neighbourhood (predicate
    // always false) must return null (hold) rather than fabricate a landing
    // spot - mirrors the "whole ring unsafe" hold semantics already proven
    // for danger zones.
    var noneWalkable = SmartMoverCore.FindSafePoint(new Vector2(0, -8), zone, new Vector2(0, 0), 15f, _ => false);
    Check("arena/find-safe-point-all-offmesh-holds", noneWalkable is null, $"result={noneWalkable}");

    // FindSafePoint unit-level positive: mesh gate present but not blocking
    // the whole neighbourhood still finds a point (sanity - the plumbing
    // does not accidentally always reject).
    var someWalkable = SmartMoverCore.FindSafePoint(new Vector2(0, -8), zone, new Vector2(0, 0), 15f, MeshWithin10);
    Check("arena/find-safe-point-partial-mesh-succeeds", someWalkable is { } sw && MeshWithin10(sw), $"result={someWalkable}");

    // Engage/settle path: the ideal ring point is off-mesh (west side of the
    // ring only) but a safe walkable variant exists elsewhere on the ring -
    // FindSafeRingPoint must honour the mesh gate exactly like it already
    // honours danger zones.
    bool EastOnly(Vector2 p) => p.X >= -0.5f;
    var ringPoint = SmartMoverCore.FindSafeRingPoint(new Vector2(-8f, 0f), new Vector2(0, 0), NoZones, EastOnly);
    Check("arena/ring-sweep-respects-mesh-gate", ringPoint is { } rp && EastOnly(rp), $"result={ringPoint}");

    // And when EVERY ring point is off-mesh, FindSafeRingPoint holds (null)
    // rather than returning an illegal standing point.
    var ringNone = SmartMoverCore.FindSafeRingPoint(new Vector2(-8f, 0f), new Vector2(0, 0), NoZones, _ => false);
    Check("arena/ring-sweep-all-offmesh-holds", ringNone is null, $"result={ringNone}");

    // End-to-end engage: player far from an off-arena-shaped target ring
    // (ideal point is off-mesh), a walkable ring alternative exists, and the
    // mover must land ON the mesh even though no danger zone is involved.
    var hEng = new SmartMoverCore.Hysteresis();
    var wEng = World(player: new(-18, 0), range: 3f, target: new(0, 0), hitbox: 5f) with { IsPointWalkable = EastOnly };
    var dEng = SmartMoverCore.Decide(wEng, hEng);
    Check("arena/engage-lands-on-mesh", dEng.Kind == SmartMoverCore.Decision.Move && EastOnly(dEng.Dest), $"kind={dEng.Kind} dest={dEng.Dest}");

    // MaxDestDistFromTarget remains an independent backstop even with a mesh
    // gate present (BOTH must pass) - a mesh predicate that allows
    // everything (always true) must still respect the 15y clamp.
    var bigZone = new[] { new DangerZoneModel.Zone(DangerZoneModel.ShapeKind.Circle, new(0, 0), 0f, 20f, 0f, 0f, 0f, default, 3f) };
    var hClamp = new SmartMoverCore.Hysteresis();
    var wClamp = World(player: new(0, -8), target: new(0, 0), hitbox: 5f, zones: bigZone) with { IsPointWalkable = _ => true };
    var dClamp = SmartMoverCore.Decide(wClamp, hClamp);
    Check("arena/distance-clamp-still-applies-with-mesh-allow-all", dClamp.Kind == SmartMoverCore.Decision.None, $"kind={dClamp.Kind} dest={dClamp.Dest}");
}


// ------------------------------------------------- dodge continuity (v1.0.4.197)
{
    // Overlapping-AoE stability: while a dodge is already committed, its
    // destination is KEPT as long as it stays safe - the sampler's "nearest
    // safe point" wobbles 1-2y per tick under overlap and every wobble used
    // to re-aim the dodge (the visible freakout).
    var zones = new[] { new DangerZoneModel.Zone(DangerZoneModel.ShapeKind.Circle, new(0, -8), 0f, 8f, 0f, 0f, 0f, default, 3f) };
    var h = new SmartMoverCore.Hysteresis();
    var d1 = SmartMoverCore.Decide(World(player: new(0, -8), zones: zones) with { NowSec = 300.0 }, h);
    Check("continuity/first-dodge-moves", d1.Kind == SmartMoverCore.Decision.Move && d1.Reason == SmartZoneDDG(), $"kind={d1.Kind} r={d1.Reason}");
    var d2 = SmartMoverCore.Decide(World(player: new(0, -7.9f), zones: zones) with { NowSec = 300.25 }, h);
    Check("continuity/held-dodge-dest-kept", d2.Kind == SmartMoverCore.Decision.Move && d2.Dest == d1.Dest, $"d1={d1.Dest} d2={d2.Dest}");

    // The held destination is released the moment a new zone covers it -
    // continuity must never pin the player inside fresh danger.
    var cover = new[] {
        zones[0],
        new DangerZoneModel.Zone(DangerZoneModel.ShapeKind.Circle, d1.Dest, 0f, 4f, 0f, 0f, 0f, default, 3f),
    };
    var d3 = SmartMoverCore.Decide(World(player: new(0, -7.9f), zones: cover) with { NowSec = 300.5 }, h);
    Check("continuity/covered-held-dest-released", d3.Kind == SmartMoverCore.Decision.Move &&
        SmartMoverCore.UnsafeAt(d3.Dest, cover, 0.25f) is null && d3.Dest != d1.Dest, $"d3={d3.Dest} d1={d1.Dest}");
}

// ---------------------------------------------------------------- shape asserts
{
    Check("shape/zone-carries-remaining", typeof(DangerZoneModel.Zone).GetProperty("RemainingSec") is not null);
    Check("shape/world-has-bmrnav-only", typeof(SmartMoverCore.MoverWorld).GetProperties()
        .Count(p => p.Name.StartsWith("Bmr", StringComparison.OrdinalIgnoreCase)) == 1);
}

Console.WriteLine();
Console.WriteLine($"PASS={pass} FAIL={fail}");
if (fail > 0 || pass == 0) { Console.WriteLine("SUITE FAILED"); return 1; }
Console.WriteLine("OK");
return 0;
