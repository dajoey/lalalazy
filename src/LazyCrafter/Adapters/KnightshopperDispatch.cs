using System.Collections.Concurrent;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using LazyCrafter.Core;

namespace LazyCrafter.Adapters;

/// <summary>
/// The Knightshopper leg of a dispatch (0.1.7.7): start one purchase per currency group
/// (StartItems, target inventory totals), wait on the Finished event with a GetStatus poll as the
/// safety net, cancel on Stop. One instance for the plugin's lifetime. Every IPC call is wrapped so a
/// missing, half-loaded or mid-reload Knightshopper degrades to "not available" - never an exception
/// into the dispatch, never a retry loop that could spend twice.
/// </summary>
public sealed class KnightshopperDispatch : IDisposable
{
    private readonly KnightshopperIpc? _ipc;
    private readonly IPluginLog _log;
    /// <summary>Results that already arrived on the Finished event, kept until the dispatch reads them -
    /// Knightshopper keeps recent results for exactly this subscriber-was-late case.</summary>
    private readonly ConcurrentDictionary<Guid, KnightshopperPurchaseFinished> _finished = new();

    public KnightshopperDispatch(IDalamudPluginInterface pi, IPluginLog log)
    {
        _log = log;
        try
        {
            _ipc = new KnightshopperIpc(pi);
            _ipc.Finished += f => _finished[f.OperationId] =
                new KnightshopperPurchaseFinished((KnightshopperPurchaseState)(int)f.State, f.Message);
        }
        catch (Exception ex)
        {
            _log.Warning("[LazyCrafter] Knightshopper IPC could not be attached: {Why}", ex.Message);
            _ipc = null;
        }
    }

    /// <summary>
    /// Knightshopper is loaded AND publishes the ApiVersion-2 purchase functions. False when it is missing
    /// entirely or its general IPC permission is off - either way the dispatch falls back to the stops.
    /// </summary>
    public bool Ready
    {
        get { try { return _ipc is not null && _ipc.IsAvailable && _ipc.ApiVersion >= 2; } catch { return false; } }
    }

    /// <summary>The Finished-event record for this operation, if it already fired (event-before-poll race).</summary>
    public KnightshopperPurchaseFinished? TakeFinished(Guid operationId) =>
        _finished.TryRemove(operationId, out var finished) ? finished : null;

    /// <summary>Start one purchase. Target totals, never quantities: Knightshopper brings the inventory to the total.</summary>
    public KnightshopperStartResponse StartItems(KnightshopperCurrency currency, IReadOnlyList<KnightshopperRouting.KsItem> items)
    {
        if (_ipc is null) return KnightshopperStartResponse.Fail(KnightshopperStartResult.NotReady, "Knightshopper is not loaded");
        try
        {
            var payload = items
                .Where(i => i.TargetTotal > 0)
                .Select(i => (ItemId: i.ItemId, Quantity: checked((uint)i.TargetTotal)))
                .ToArray();
            if (payload.Length == 0) return KnightshopperStartResponse.Fail(KnightshopperStartResult.EmptyList, "nothing to buy");
            var response = _ipc.StartItems((KnightshopperIpcCurrencyId)(int)currency, payload);
            _log.Information("[LazyCrafter] Knightshopper StartItems({Currency}, {Count} items) -> {Result} op {Op}{Msg}",
                currency, payload.Length, response.Result, response.OperationId,
                response.Message.Length == 0 ? "" : $": {response.Message}");
            return new((KnightshopperStartResult)(int)response.Result, response.OperationId, response.Message);
        }
        catch (Exception ex)
        {
            _log.Warning("[LazyCrafter] Knightshopper StartItems threw: {Why}", ex.Message);
            return KnightshopperStartResponse.Fail(KnightshopperStartResult.NotReady, ex.Message);
        }
    }

    /// <summary>Current status of an operation, or null when the IPC cannot answer right now.</summary>
    public KnightshopperPurchaseStatus? Status(Guid operationId)
    {
        if (_ipc is null) return null;
        try
        {
            var s = _ipc.GetStatus(operationId);
            return new((KnightshopperPurchaseState)(int)s.State, s.CurrentIndex, s.TotalItems, s.CurrentItemId, s.Message);
        }
        catch (Exception ex)
        {
            _log.Warning("[LazyCrafter] Knightshopper GetStatus threw: {Why}", ex.Message);
            return null;
        }
    }

    /// <summary>Best-effort cancel; the answer is advisory (an item already being bought may still finish).</summary>
    public bool Cancel(Guid operationId)
    {
        try { return _ipc?.Cancel(operationId) == true; }
        catch (Exception ex) { _log.Warning("[LazyCrafter] Knightshopper Cancel threw: {Why}", ex.Message); return false; }
    }

    public void Dispose() => _ipc?.Dispose();
}

/// <summary>StartItems answer in Core terms (adapter-free enums), so the dispatch never touches IPC types.</summary>
public readonly record struct KnightshopperStartResponse(KnightshopperStartResult Result, Guid OperationId, string Message)
{
    public bool Started => Result == KnightshopperStartResult.Started;

    public static KnightshopperStartResponse Fail(KnightshopperStartResult result, string message) =>
        new(result, Guid.Empty, message);
}

/// <summary>GetStatus answer in Core terms.</summary>
public readonly record struct KnightshopperPurchaseStatus(
    KnightshopperPurchaseState State,
    int CurrentIndex,
    int TotalItems,
    uint CurrentItemId,
    string Message)
{
    public bool IsFinished => State is KnightshopperPurchaseState.Succeeded or KnightshopperPurchaseState.Failed or KnightshopperPurchaseState.Cancelled;
}

/// <summary>Purchase.Finished event payload in Core terms - mapped at the wrapper boundary so no client enum leaks.</summary>
public readonly record struct KnightshopperPurchaseFinished(KnightshopperPurchaseState State, string Message);
