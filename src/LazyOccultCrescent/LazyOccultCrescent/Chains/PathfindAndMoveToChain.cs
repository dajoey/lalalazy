using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using LazyOccultCrescent.Data;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using Ocelot.Chain;
using Ocelot.IPC;

namespace LazyOccultCrescent.Chains;

public class PathfindAndMoveToChain : ChainFactory
{
    private readonly Vector3 destination;

    private readonly VNavmesh vnav;

    // How close counts as arrived, per caller.
    //
    // This has to match what the NEXT step needs, and 5y flat was too loose in
    // the other direction: interacting with an aetheryte needs 3.8y, so a walk
    // that "arrived" at 5y left the teleport out of range. Callers that must end
    // up in interaction range pass their own tolerance; everything else keeps the
    // loose default, because aetherytes are solid objects and vnavmesh parks at
    // their collision edge rather than their origin.
    private readonly float arrivalTolerance;

    // When vnavmesh stops short of a tolerance the caller actually needs, try a
    // direct approach once before giving up rather than accepting a bad position.
    private bool nudged;

    // Checked every tick. Without this the chain has no way to learn that what it
    // is walking to has ceased to exist - and because Drive() re-issues movement
    // whenever vnavmesh is not running, an external vnav.Stop() (which is exactly
    // what the Automator does when a FATE ends) reads as a stall and gets
    // immediately undone. That is how the plugin ended up pathing to a dead FATE
    // and returning to base at the same time.
    private readonly Func<bool>? abortIf;

    // Re-issuing on "vnavmesh is not running" is what lets this chain recover
    // from a failed solve - but it also means anything that stops vnavmesh
    // externally gets overridden. The abort predicate covers the case we know
    // about; this cap covers the ones we do not. If movement has been restarted
    // this many times and still is not sticking, something else is driving and
    // fighting it is worse than giving up.
    private const int MaxIssues = 4;

    private int issues;

    // Long enough that releasing a key mid-stride does not snatch control back,
    // short enough not to feel unresponsive.
    private readonly static TimeSpan ResumeGrace = TimeSpan.FromSeconds(2);

    private bool issued;

    private bool yielded;

    // Distinguishes "vnavmesh has not started yet" from "vnavmesh has finished".
    // IsRunning reads false for a moment after a request goes in, so arrival can
    // only be inferred from it stopping AFTER it was seen running.
    private bool sawRunning;

    private Task<List<Vector3>>? pathTask;

    public PathfindAndMoveToChain(
        VNavmesh vnav,
        Vector3 destination,
        float arrivalTolerance = 5f,
        Func<bool>? abortIf = null)
    {
        this.vnav = vnav;
        this.destination = destination;
        this.arrivalTolerance = arrivalTolerance;
        this.abortIf = abortIf;
    }

    public static PathfindAndMoveToChain RandomNearby(
        VNavmesh vnav,
        Vector3 destination,
        float maxRadius = 1f,
        float minRadius = 0f,
        Func<bool>? abortIf = null)
    {
        var angle = (float)(Random.Shared.NextDouble() * MathF.Tau);
        var distance = minRadius + (float)(Random.Shared.NextDouble() * (maxRadius - minRadius));

        var offsetX = MathF.Cos(angle) * distance;
        var offsetZ = MathF.Sin(angle) * distance;

        destination = new Vector3(destination.X + offsetX, destination.Y, destination.Z + offsetZ);
        destination = vnav.FindPointOnFloor(destination, false, 0.5f) ?? destination;

        return new PathfindAndMoveToChain(vnav, destination, 5f, abortIf);
    }

    protected override Chain Create(Chain chain)
    {
        return chain.Then(new TaskManagerTask(Drive, new TaskManagerConfiguration { TimeLimitMS = 180000 }));
    }

    private bool Drive()
    {
        if (Player.Object == null)
        {
            return false;
        }

        // Bail before anything else can re-issue movement.
        if (abortIf?.Invoke() == true)
        {
            Svc.Log.Debug("[Pathfind] destination no longer valid - abandoning route");
            vnav.Stop();
            return true;
        }

        if (Player.DistanceTo(destination) <= arrivalTolerance)
        {
            vnav.Stop();
            return true;
        }

        // While the player is steering, get out of the way entirely.
        if (ManualControl.Poll())
        {
            if (!yielded)
            {
                Svc.Log.Debug("[Pathfind] manual input detected - yielding control");
                yielded = true;
            }

            if (vnav.IsRunning())
            {
                vnav.Stop();
            }

            // Drop any in-flight or completed route: it starts from where the
            // player used to be.
            // A deliberate hand-off to the player is not a failed solve; reset the
            // restart budget so resuming does not immediately exhaust it.
            pathTask = null;
            issued = false;
            sawRunning = false;
            nudged = false;
            issues = 0;
            return false;
        }

        if (vnav.IsRunning())
        {
            sawRunning = true;
        }
        else if (issued && sawRunning)
        {
            var distance = Player.DistanceTo(destination);

            // vnavmesh stopped short of what the caller needs. Its route ends at
            // the navmesh edge nearest the target, which for a solid object can
            // be outside interaction range - so close the last stretch directly
            // before accepting. Once only; a second failure means it genuinely
            // cannot get closer and stalling helps nobody.
            if (distance > arrivalTolerance && !nudged)
            {
                Svc.Log.Debug($"[Pathfind] vnavmesh stopped {distance:F1}y out, need {arrivalTolerance:F1}y - closing directly");
                nudged = true;
                vnav.FollowPath([destination], false);
                sawRunning = false;
                return false;
            }

            Svc.Log.Debug($"[Pathfind] vnavmesh finished {distance:F1}y from target - accepting");
            return true;
        }

        if (yielded)
        {
            if (!ManualControl.HasSettled(ResumeGrace))
            {
                return false;
            }

            // The whole point: recompute from where the player actually is,
            // rather than dragging them back to the waypoint they abandoned.
            Svc.Log.Debug("[Pathfind] player settled - repathing from current position");
            yielded = false;
        }

        // A completed route is ready to be walked.
        if (pathTask is { IsCompleted: true })
        {
            var task = pathTask;
            pathTask = null;

            if (task.IsCompletedSuccessfully && task.Result is { Count: > 1 })
            {
                var route = AggroAvoidance.Apply(vnav, task.Result, destination, AggroAvoidance.Enabled);
                vnav.FollowPath(route, false);
                issued = true;
                sawRunning = false;
            }
            else
            {
                // Avoidance is a nicety; movement is not. If the explicit
                // pathfind failed, hand it back to vnavmesh wholesale.
                Svc.Log.Debug("[Pathfind] explicit pathfind unavailable - falling back to PathfindAndMoveTo");
                vnav.PathfindAndMoveTo(destination, false);
                issued = true;
                sawRunning = false;
            }

            return false;
        }

        if (pathTask != null)
        {
            return false; // still solving
        }

        // Request a route if we have none, or vnavmesh has given up. Throttled
        // because pathfinding is async and IsRunning reads false for a moment
        // after the request goes in.
        if (issues >= MaxIssues)
        {
            Svc.Log.Debug($"[Pathfind] movement restarted {issues}x without sticking - yielding to whatever else is driving");
            return true;
        }

        if ((!issued || !vnav.IsRunning()) && EzThrottler.Throttle("Pathfind.Issue", 1000))
        {
            issues++;
            try
            {
                pathTask = vnav.Pathfind(Player.Position, destination, false);
            }
            catch (Exception ex)
            {
                Svc.Log.Debug($"[Pathfind] Pathfind threw ({ex.Message}) - falling back");
                vnav.PathfindAndMoveTo(destination, false);
                issued = true;
                sawRunning = false;
            }
        }

        return false;
    }

    public override TaskManagerConfiguration? Config()
    {
        return new TaskManagerConfiguration
        {
            TimeLimitMS = 180000,
        };
    }
}
