using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LazyRetainerLive;

/// <summary>
/// One retainer as served to the status relay. Field names are the relay's wire
/// contract — copied verbatim (camelCase) from read_retainers() in
/// fxiv-status-relay.py, which maps AutoRetainer's DefaultConfig.json
/// OfflineData entries. Keep them byte-identical: the dashboard panel and the
/// relay both key on these exact strings.
/// </summary>
public sealed class RetainerInfo
{
    public string Name = "";
    public int Job;
    public int Level;
    public bool HasVenture;
    public long EndsAt;
    public long Gil;
    public int VentureId;
    public int Mb;
}

/// <summary>
/// One character row: char, world, gil, ventures, seals, inventory, retainers[].
/// Same key order as the relay's Retainers frame.
/// </summary>
public sealed class CharInfo
{
    public string Char = "";
    public string World = "";
    public long Gil;
    public long Ventures;
    public long Seals;
    public long Inventory;
    public List<RetainerInfo> Retainers = [];
}

/// <summary>
/// Wire writer for the relay contract. Written by hand with Utf8JsonWriter so
/// the key order and casing are byte-stable rather than serializer-dependent:
/// the relay overlays this payload onto its file-derived frame, and the panel
/// reads the merged frame, so shape drift here would leak straight through.
/// This class is deliberately Dalamud-free so the exact bytes can be proved
/// offline (scratch console probe) before anything touches the game.
/// </summary>
public static class RetainerWire
{
    /// <summary>HTTP 200 body: {"type":"Retainers","chars":[{...}],"readAt":N,"source":"plugin"}.</summary>
    public static byte[] Write(CharInfo c, long readAt)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("type", "Retainers");
            w.WriteStartArray("chars");
            WriteChar(w, c);
            w.WriteEndArray();
            w.WriteNumber("readAt", readAt);
            w.WriteString("source", "plugin");
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// <summary>
    /// HTTP 503 body, byte-exact as the card specifies:
    /// {"type":"Retainers","chars":[],"source":"plugin","readAt":N}.
    /// Any non-200 makes the relay fall back to AutoRetainer's file.
    /// Served only when NO snapshot was ever built this session (title screen
    /// before the first login) — NOT on logout; see HttpServer.HandleClient.
    /// </summary>
    public static byte[] WriteUnavailable(long readAt)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("type", "Retainers");
            w.WriteStartArray("chars");
            w.WriteEndArray();
            w.WriteString("source", "plugin");
            w.WriteNumber("readAt", readAt);
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    private static void WriteChar(Utf8JsonWriter w, CharInfo c)
    {
        w.WriteStartObject();
        w.WriteString("char", c.Char);
        w.WriteString("world", c.World);
        w.WriteNumber("gil", c.Gil);
        w.WriteNumber("ventures", c.Ventures);
        w.WriteNumber("seals", c.Seals);
        w.WriteNumber("inventory", c.Inventory);
        w.WriteStartArray("retainers");
        foreach (var r in c.Retainers)
        {
            w.WriteStartObject();
            w.WriteString("name", r.Name);
            w.WriteNumber("job", r.Job);
            w.WriteNumber("level", r.Level);
            w.WriteBoolean("hasVenture", r.HasVenture);
            w.WriteNumber("endsAt", r.EndsAt);
            w.WriteNumber("gil", r.Gil);
            w.WriteNumber("ventureId", r.VentureId);
            w.WriteNumber("mb", r.Mb);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }
}
