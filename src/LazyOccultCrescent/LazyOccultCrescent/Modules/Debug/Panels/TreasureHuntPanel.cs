using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Enums;
using LazyOccultCrescent.Modules.Data;
using LazyOccultCrescent.Pathfinding;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using Dalamud.Bindings.ImGui;
using Ocelot.Ui;
using Ocelot.Chain;
using Ocelot.IPC;

namespace LazyOccultCrescent.Modules.Debug.Panels;

using TreasureData = (uint id, Vector3 position, uint type);

public class TreasureHuntPanel : Panel
{
    private List<TreasureData> Treasure = [];

    private bool HasRun = false;

    private bool ShouldRun = false;

    private Stopwatch stopwatch = new();

    private uint Progress = 0;

    private int rawInstances = 0;

    private readonly uint MaxProgress = 0;

    private ChainQueue ChainQueue
    {
        get => ChainManager.Get("TreasureHuntPanelChain");
    }

    public unsafe TreasureHuntPanel()
    {
        var layout = LayoutWorld.Instance()->ActiveLayout;
        if (layout == null)
        {
            return;
        }

        if (!layout->InstancesByType.TryGetValue(InstanceType.Treasure, out var mapPtr, false))
        {
            return;
        }

        foreach (ILayoutInstance* instance in mapPtr.Value->Values)
        {
            rawInstances++;
            var transform = instance->GetTransformImpl();
            var position = transform->Translation;
            if (position.Y <= -10f)
            {
                continue;
            }

            var treasureRowId = Unsafe.Read<uint>((byte*)instance + 0x30);
            var sgbId = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Treasure>().GetRow(treasureRowId).SGB.RowId;
            if (sgbId != 1596 && sgbId != 1597)
            {
                continue;
            }

            Treasure.Add((treasureRowId, position, sgbId));
        }

        Treasure = Treasure.OrderBy(t => t.id).ToList();

        var t = Treasure.Count;
        var a = AethernetData.All().Count();
        MaxProgress = (uint)(t * (t - 1 + 2 * a));

        Svc.Log.Information(
            $"[TreasureHunt] territory {ZoneData.CurrentTerritory}: {rawInstances} layout instance(s) -> " +
            $"{t} chest(s) after SGB filter (1596/1597), {a} shard(s). {MaxProgress} path(s) to compute.");

        if (t == 0)
        {
            Svc.Log.Error(
                "[TreasureHunt] No chests matched. Either you are not in an Occult zone, or this zone " +
                "uses SGB ids other than 1596/1597 and the filter needs updating. Refusing to write an empty data file.");
        }
    }

    public override string GetName()
    {
        return "Treasure Hunt Helper";
    }

    public override void Render(DebugModule module)
    {
        OcelotUi.LabelledValue("Bronze", Treasure.Count(t => t.type == 1596)); // 60
        OcelotUi.LabelledValue("Silver", Treasure.Count(t => t.type == 1597)); // 8

        OcelotUi.Indent(() =>
        {
            if (!HasRun)
            {
                if (Treasure.Count == 0)
                {
                    ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "No chests found in this zone - nothing to compute.");
                    ImGui.TextWrapped("Enter South Horn or North Horn and reopen this panel. If you are already in one, the SGB filter needs updating for this zone.");
                    return;
                }

                if (ImGui.Button("Run"))
                {
                    ShouldRun = true;
                }

                return;
            }

            var Completion = (float)Progress / (float)MaxProgress * 100;

            OcelotUi.LabelledValue("Progress: ", $"{Completion:f2}%");
            OcelotUi.Indent(() => OcelotUi.LabelledValue("Calculations: ", $"{Progress}/{MaxProgress}"));
            OcelotUi.LabelledValue("Elapsed: ", stopwatch.Elapsed.ToString("mm\\:ss"));
        });
    }

    public override void Update(DebugModule module)
    {
        if (!ShouldRun || HasRun)
        {
            return;
        }

        ShouldRun = true;
        HasRun = true;

        PrecomputeTreasurePathDistances(module);
    }

    private void PrecomputeTreasurePathDistances(DebugModule module)
    {
        stopwatch.Restart();

        var outputFile = Path.Join(ZoneData.GetCurrentZoneDataDirectory(), "precomputed_treasure_hunt_data.json");

        var vnav = module.GetIPCSubscriber<VNavmesh>();

        NodeDataSchema data = new();
        foreach (var datum in AethernetData.All())
        {
            data.AethernetToNodeDistances[datum.Aethernet] = [];
        }

        foreach (var treasure in Treasure)
        {
            data.NodeToNodeDistances[treasure.id] = [];
            data.NodeToAethernetDistances[treasure.id] = [];

            foreach (var other in Treasure.Where(t => t != treasure))
            {
                ChainQueue.Submit(() =>
                    Chain.Create()
                        .Then(async void (_) =>
                        {
                            var path = await vnav.Pathfind(treasure.position, other.position, false);
                            var distance = CalculatePathLength(path);

                            var nodes = path.Select(p => Position.Create(p)).ToList();

                            data.NodeToNodeDistances[treasure.id].Add(new ToNode(other.id, distance, nodes));

                            Progress++;
                        })
                        .Then(_ => !vnav.IsRunning())
                );
            }

            foreach (var datum in AethernetData.All())
            {
                ChainQueue.Submit(() =>
                    Chain.Create()
                        .Then(async void (_) =>
                        {
                            var path = await vnav.Pathfind(datum.Destination, treasure.position, false);
                            var distance = CalculatePathLength(path);

                            var nodes = path.Select(p => Position.Create(p)).ToList();

                            data.AethernetToNodeDistances[datum.Aethernet].Add(new ToNode(treasure.id, distance, nodes));

                            Progress++;
                        })
                        .Then(_ => !vnav.IsRunning())
                );

                ChainQueue.Submit(() =>
                    Chain.Create()
                        .Then(async void (_) =>
                        {
                            var path = await vnav.Pathfind(datum.Destination, treasure.position, false);
                            var distance = CalculatePathLength(path);

                            var nodes = path.Select(p => Position.Create(p)).ToList();


                            data.NodeToAethernetDistances[treasure.id].Add(new ToAethernet(datum.Aethernet, distance, nodes));

                            Progress++;
                        })
                        .Then(_ => !vnav.IsRunning())
                );
            }
        }

        ChainQueue.Submit(() =>
            Chain.Create()
                .Wait(5000)
                .Then(_ =>
                {
                    stopwatch.Stop();

                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = false,
                        IncludeFields = false,
                    };

                    Svc.Log.Info("Saving file to " + outputFile);
                    var json = JsonSerializer.Serialize(data, options);
                    File.WriteAllTextAsync(outputFile, json);
                })
        );
    }

    private float CalculatePathLength(List<Vector3> path)
    {
        var length = 0f;

        for (var i = 1; i < path.Count; i++)
        {
            length += Vector3.Distance(path[i - 1], path[i]);
        }

        return length;
    }
}
