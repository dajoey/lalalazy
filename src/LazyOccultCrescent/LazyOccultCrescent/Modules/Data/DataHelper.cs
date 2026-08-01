using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ECommons.DalamudServices;

namespace LazyOccultCrescent.Modules.Data;

public abstract class DataHelper<T>
{
    protected virtual Dictionary<uint, string> Paths
    {
        get => [];
    }

    private readonly JsonSerializerOptions options = new() { WriteIndented = true };

    private List<T> LoadSchema()
    {
        if (!Paths.TryGetValue(Svc.ClientState.TerritoryType, out var path))
        {
            throw new KeyNotFoundException($"No JSON path configured for territory {Svc.ClientState.TerritoryType}");
        }

        if (!File.Exists(path))
        {
            return [];
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<T>>(json) ?? [];
    }

    private void SaveSchema(List<T> data)
    {
        if (!Paths.TryGetValue(Svc.ClientState.TerritoryType, out var path))
        {
            throw new KeyNotFoundException($"No JSON path configured for territory {Svc.ClientState.TerritoryType}");
        }

        var json = JsonSerializer.Serialize(data, options);
        File.WriteAllText(path, json);
    }

    protected bool HasSharedData(T data)
    {
        return !Paths.ContainsKey(Svc.ClientState.TerritoryType) || LoadSchema().Contains(data);
    }

    public void MarkSharedData(T data)
    {
        if (!Paths.ContainsKey(Svc.ClientState.TerritoryType))
        {
            return;
        }

        var schema = LoadSchema();
        schema.Add(data);
        SaveSchema(schema);
    }
}
