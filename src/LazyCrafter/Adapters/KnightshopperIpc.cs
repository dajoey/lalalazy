using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace LazyCrafter.Adapters;

/// <summary>
/// Knightshopper's published typed IPC client (its docs/KnightshopperIpc.cs, plugin 1.0.1.6), copied
/// verbatim into LazyCrafter's namespace - the only changes are the namespace line, the enum rename
/// CurrencyId -> KnightshopperIpcCurrencyId (too generic a name for a shared namespace), and this header.
/// Keep this file a faithful copy so it can be diffed against Knightshopper's next docs update.
/// Quantities on StartItems are TARGET inventory totals; ApiVersion 2 is required for StartItems.
/// </summary>
public sealed class KnightshopperIpc : IDisposable
{
    private readonly ICallGateSubscriber<int> apiVersion;
    private readonly ICallGateSubscriber<bool> isBusy;
    private readonly ICallGateSubscriber<int, (int, Guid, string)> start;
    private readonly ICallGateSubscriber<int, (uint ItemId, uint Quantity)[], (int, Guid, string)> startItems;
    private readonly ICallGateSubscriber<Guid, (int, int, int, uint, string)> getStatus;
    private readonly ICallGateSubscriber<Guid, bool> isRunning;
    private readonly ICallGateSubscriber<Guid, bool> cancel;
    private readonly ICallGateSubscriber<Guid, int, string, object> finished;
    private bool disposed;

    public KnightshopperIpc(IDalamudPluginInterface pluginInterface)
    {
        apiVersion = pluginInterface.GetIpcSubscriber<int>("Knightshopper.ApiVersion");
        isBusy = pluginInterface.GetIpcSubscriber<bool>("Knightshopper.IsBusy");
        start = pluginInterface.GetIpcSubscriber<int, (int, Guid, string)>("Knightshopper.Purchase.Start");
        startItems = pluginInterface.GetIpcSubscriber<int, (uint ItemId, uint Quantity)[], (int, Guid, string)>("Knightshopper.Purchase.StartItems");
        getStatus = pluginInterface.GetIpcSubscriber<Guid, (int, int, int, uint, string)>("Knightshopper.Purchase.GetStatus");
        isRunning = pluginInterface.GetIpcSubscriber<Guid, bool>("Knightshopper.Purchase.IsRunning");
        cancel = pluginInterface.GetIpcSubscriber<Guid, bool>("Knightshopper.Purchase.Cancel");
        finished = pluginInterface.GetIpcSubscriber<Guid, int, string, object>("Knightshopper.Purchase.Finished");
        finished.Subscribe(OnFinished);
    }

    public event Action<PurchaseFinished>? Finished;

    public bool IsAvailable => apiVersion.HasFunction;

    public int ApiVersion => apiVersion.InvokeFunc();

    public bool IsBusy => isBusy.InvokeFunc();

    public StartResponse Start(KnightshopperIpcCurrencyId currency)
    {
        var response = start.InvokeFunc((int)currency);
        return new StartResponse((StartResult)response.Item1, response.Item2, response.Item3);
    }

    // Quantities are target inventory totals. Duplicate IDs use the largest quantity.
    // Unique items are capped at one. Purchases are grouped across the fewest unlocked vendors.
    // Full inventory or a rejected transaction fails the operation and reports a message.
    public StartResponse StartItems(KnightshopperIpcCurrencyId currency, (uint ItemId, uint Quantity)[] items)
    {
        var response = startItems.InvokeFunc((int)currency, items);
        return new StartResponse((StartResult)response.Item1, response.Item2, response.Item3);
    }

    public PurchaseStatus GetStatus(Guid operationId)
    {
        var status = getStatus.InvokeFunc(operationId);
        return new PurchaseStatus(
            (PurchaseState)status.Item1,
            status.Item2,
            status.Item3,
            status.Item4,
            status.Item5);
    }

    public bool IsRunning(Guid operationId) => isRunning.InvokeFunc(operationId);

    public bool Cancel(Guid operationId) => cancel.InvokeFunc(operationId);

    public async Task<PurchaseStatus> WaitForCompletionAsync(
        Guid operationId,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(PurchaseFinished value)
        {
            if (value.OperationId == operationId)
                signal.TrySetResult();
        }

        Finished += Handler;
        try
        {
            var interval = pollInterval ?? TimeSpan.FromMilliseconds(500);
            while (true)
            {
                var status = GetStatus(operationId);
                if (status.State == PurchaseState.Unknown)
                    throw new InvalidOperationException("Knightshopper does not know this operation.");
                if (status.IsFinished)
                    return status;

                var delay = Task.Delay(interval, cancellationToken);
                await Task.WhenAny(signal.Task, delay).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            Finished -= Handler;
        }
    }

    private void OnFinished(Guid operationId, int state, string message) =>
        Finished?.Invoke(new PurchaseFinished(operationId, (PurchaseState)state, message));

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        finished.Unsubscribe(OnFinished);
    }
}

public enum KnightshopperIpcCurrencyId
{
    BicolorGemstone = 0,
    CompanySeal = 1,
    Gil = 2,
    Hunt = 3,
    MGP = 4,
    PVP = 5,
    Scrip = 6,
    Tomestone = 7,
    Firmament = 8,
    Cosmocredits = 9,
    OccultCrescent = 10,
}

public enum StartResult
{
    Started = 0,
    Busy = 1,
    InvalidCurrency = 2,
    EmptyList = 3,
    NotReady = 4,
    NotLoggedIn = 5,
    NotAllowed = 6,
    ItemUnavailable = 7,
    InvalidQuantity = 8,
}

public enum PurchaseState
{
    Unknown = 0,
    Running = 1,
    CancellationRequested = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}

public readonly record struct StartResponse(StartResult Result, Guid OperationId, string Message)
{
    public bool Started => Result == StartResult.Started;
}

public readonly record struct PurchaseStatus(
    PurchaseState State,
    int CurrentIndex,
    int TotalItems,
    uint CurrentItemId,
    string Message)
{
    public bool IsFinished => State is PurchaseState.Succeeded or PurchaseState.Failed or PurchaseState.Cancelled;
}

public readonly record struct PurchaseFinished(Guid OperationId, PurchaseState State, string Message);
