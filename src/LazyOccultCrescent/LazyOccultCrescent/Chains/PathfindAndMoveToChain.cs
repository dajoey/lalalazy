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

    // vnavmesh parks a little short of the exact point on uneven ground, so an
    // exact-arrival test can hang the chain forever.
    private const float ArrivalTolerance = 3f;

    // Long enough that releasing a key mid-stride does not snatch control back,
    // short enough not to feel unresponsive.
    private readonly static TimeSpan ResumeGrace = TimeSpan.FromSeconds(2);

    private bool issued;

    private bool yielded;

    private Task<List<Vector3>>? pathTask;

    public PathfindAndMoveToChain(VNavmesh vnav, Vector3 destination, float maxRadius = 1f, float minRadius = 0f)
    {
        this.vnav = vnav;
        this.destination = destination;
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

        if (Player.DistanceTo(destination) <= ArrivalTolerance)
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
            return false;
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
            }
            else
            {
                // Avoidance is a nicety; movement is not. If the explicit
                // pathfind failed, hand it back to vnavmesh wholesale.
                Svc.Log.Debug("[Pathfind] explicit pathfind unavailable - falling back to PathfindAndMoveTo");
                vnav.PathfindAndMoveTo(destination, false);
                issued = true;
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
