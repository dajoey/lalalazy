using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Enums;
using ECommons.DalamudServices;

namespace LazyOccultCrescent.Pathfinding;

public abstract class BasePathfinder(float returnCost = 300f, float teleportCost = 50f) : IPathfinder
{
    public PathfinderState State { get; private set; } = PathfinderState.None;

    private NodeDataSchema data = new();

    protected abstract uint GetStartingNode(Vector3 start, List<uint> nodes);

    public Task<List<PathfinderStep>> FindPath(Vector3 start, List<uint> nodes)
    {
        if (State != PathfinderState.FileLoaded)
        {
            throw new Exception("File not loaded");
        }

        State = PathfinderState.Pathfinding;

        var startNode = GetStartingNode(start, nodes);


        var graph = BuildCostGraph(nodes);
        var ordered = SolveTSPNearestInsertion(startNode, nodes, graph);
        var steps = BuildStepPath(ordered, graph);

        steps.Insert(0, PathfinderStep.WalkToDestination(startNode));

        PrintPath(steps);
        State = PathfinderState.PathfindingDone;
        return Task.FromResult(steps);
    }

    protected (float Cost, List<PathfinderStep> Steps) GetBestSteps(uint fromId, uint toId)
    {
        var bestCost = float.MaxValue;
        List<PathfinderStep> bestSteps = [];

        if (data.NodeToNodeDistances.TryGetValue(fromId, out var directList))
        {
            var direct = directList.FirstOrDefault(x => x.Id == toId);
            if (direct.Id == toId)
            {
                bestCost = direct.Distance;
                bestSteps = [PathfinderStep.WalkToDestination(toId)];
            }
        }

        if (data.NodeToAethernetDistances[fromId].Count > 0)
        {
            var fromShard = data.NodeToAethernetDistances[fromId].OrderBy(x => x.Distance).FirstOrDefault();
            foreach (var (aethernet, list) in data.AethernetToNodeDistances)
            {
                var to = list.FirstOrDefault(x => x.Id == toId);
                if (to.Id != toId)
                {
                    continue;
                }

                var cost = fromShard.Distance + teleportCost + to.Distance;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestSteps =
                    [
                        PathfinderStep.WalkToAethernet(fromShard.Aethernet),
                        PathfinderStep.TeleportToAethernet(aethernet),
                        PathfinderStep.WalkToDestination(toId),
                    ];
                }
            }
        }

        foreach (var (aethernet, list) in data.AethernetToNodeDistances)
        {
            var to = list.FirstOrDefault(x => x.Id == toId);
            if (to.Id != toId)
            {
                continue;
            }

            var cost = returnCost + teleportCost + to.Distance;
            if (cost < bestCost)
            {
                bestCost = cost;
                // Matches either horn's base camp; the literal comparison
                // never fired in North Horn.
                if (ZoneData.IsBaseCamp(aethernet))
                {
                    bestSteps =
                    [
                        PathfinderStep.ReturnToBaseCamp(),
                        PathfinderStep.WalkToDestination(toId),
                    ];
                }
                else
                {
                    bestSteps =
                    [
                        PathfinderStep.ReturnToBaseCamp(),
                        PathfinderStep.TeleportToAethernet(aethernet),
                        PathfinderStep.WalkToDestination(toId),
                    ];
                }
            }
        }

        return (bestCost, bestSteps);
    }

    protected async void LoadFile(string filename)
    {
        State = PathfinderState.LoadingFile;
        var options = new JsonSerializerOptions
        {
            IncludeFields = false,
        };

        var file = Path.Join(ZoneData.GetCurrentZoneDataDirectory(), filename);
        if (!File.Exists(file))
        {
            Svc.Log.Error($"Required file not found: {file}");
            return;
        }

        var json = await File.ReadAllTextAsync(file);
        data = JsonSerializer.Deserialize<NodeDataSchema>(json, options);
        State = PathfinderState.FileLoaded;
    }

    protected Dictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>> BuildCostGraph(List<uint> nodes)
    {
        var graph = new Dictionary<uint, Dictionary<uint, (float, List<PathfinderStep>)>>();

        foreach (var from in nodes)
        {
            graph[from] = new Dictionary<uint, (float, List<PathfinderStep>)>();
            foreach (var to in nodes)
            {
                if (from == to)
                {
                    continue;
                }

                var result = GetBestSteps(from, to);
                graph[from][to] = result;
            }
        }

        return graph;
    }

    protected List<uint> SolveTSPNearestInsertion(
        uint start,
        List<uint> nodes,
        Dictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>> graph)
    {
        if (nodes.Count == 1)
        {
            return [start, nodes.First()];
        }


        var route = new List<uint> { start };
        var unvisited = new HashSet<uint>(nodes.Where(n => n != start));
        while (unvisited.Count > 0)
        {
            var bestInsertionCost = float.MaxValue;
            var bestIndex = -1;
            uint bestNode = 0;

            foreach (var nodeToInsert in unvisited)
            {
                for (var i = 0; i < route.Count; i++)
                {
                    var from = route[i];
                    uint to;

                    if (i == route.Count - 1)
                    {
                        var costAtEnd = graph[from][nodeToInsert].Cost;
                        if (costAtEnd < bestInsertionCost)
                        {
                            bestInsertionCost = costAtEnd;
                            bestIndex = route.Count;
                            bestNode = nodeToInsert;
                        }
                    }
                    else
                    {
                        to = route[i + 1];

                        var addedCost = graph[from][nodeToInsert].Cost + graph[nodeToInsert][to].Cost
                                        - graph[from][to].Cost;

                        if (addedCost < bestInsertionCost)
                        {
                            bestInsertionCost = addedCost;
                            bestIndex = i + 1;
                            bestNode = nodeToInsert;
                        }
                    }
                }
            }

            if (bestIndex != -1)
            {
                route.Insert(bestIndex, bestNode);
                unvisited.Remove(bestNode);
            }
            else
            {
                Svc.Log.Error($"Could not find best insertion point for {unvisited.First()}");
                break;
            }
        }

        return route;
    }

    protected List<PathfinderStep> BuildStepPath(List<uint> orderedNodes, Dictionary<uint, Dictionary<uint, (float Cost, List<PathfinderStep> Steps)>> graph)
    {
        List<PathfinderStep> steps = [];

        for (var i = 0; i < orderedNodes.Count - 1; i++)
        {
            var from = orderedNodes[i];
            var to = orderedNodes[i + 1];
            if (graph.ContainsKey(from) && graph[from].ContainsKey(to))
            {
                steps.AddRange(graph[from][to].Steps);
            }
            else
            {
                Svc.Log.Error($"Graph missing path from {from} to {to}. This should not happen if TSP is correct.");
            }
        }

        return steps;
    }

    protected void PrintPath(List<PathfinderStep> steps)
    {
        Svc.Log.Info("== Pathfinder Steps ==");

        var index = 1;
        var treasureCount = 0;

        foreach (var step in steps)
        {
            var message = step.Type switch
            {
                PathfinderStepType.WalkToNode => $"[{index}] Walk to Destination: {step.NodeId}",
                PathfinderStepType.WalkToAethernet => $"[{index}] Walk to Aethernet: {step.Aethernet}",
                PathfinderStepType.TeleportToAethernet => $"[{index}] Teleport to Aethernet: {step.Aethernet}",
                PathfinderStepType.ReturnToBaseCamp => $"[{index}] Return to Base Camp",
                _ => $"[{index}] Unknown Step Type",
            };

            if (step.Type == PathfinderStepType.WalkToNode)
            {
                treasureCount++;
            }

            Svc.Log.Info(message);
            index++;
        }

        Svc.Log.Info($"== Total treasures visited: {treasureCount} ==");
        Svc.Log.Info("=======================");
    }
}
