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

    public PathfindAndMoveToChain(VNavmesh vnav, Vector3 destination, float arrivalTolerance = 5f)
    {
        this.vnav = vnav;
        this.destination = destination;
        this.arrivalTolerance = arrivalTolerance;
    }

    public static PathfindAndMoveToChain RandomNearby(
        VNavmesh vnav,
        Vector3 destination,
        float maxRadius = 1f,
        float minRadius = 0f)
    {
        var angle = (float)(Random.Shared.NextDouble() * MathF.Tau);
        var distance = minRadius + (float)(Random.Shared.NextDouble() * (maxRadius - minRadius));

        var offsetX = MathF.Cos(angle) * distance;
        var offsetZ = MathF.Sin(angle) * distance;

        destination = new Vector3(destination.X + offsetX, destination.Y, destination.Z + offsetZ);
        destination = vnav.FindPointOnFloor(destination, false, 0.5f) ?? destination;

        return new PathfindAndMoveToChain(vnav, destination);
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
            pathTask = null;
            issued = false;
            sawRunning = false;
            nudged = false;
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
        if ((!issued || !vnav.IsRunning()) && EzThrottler.Throttle("Pathfind.Issue", 1000))
        {
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
