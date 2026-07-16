using Microsoft.Extensions.Options;

namespace FeedbackIntelligence.Api;

public sealed class LlmBusyException : Exception
{
    public LlmBusyException() : base("LLM capacity is busy; request shed.")
    {
    }
}

/// <summary>
/// Bounds concurrent LLM work on the single shared GPU. Shedding, not queueing:
/// a request that cannot get a slot within the acquire timeout fails fast with
/// a clean busy signal instead of stacking behind slow generations.
/// </summary>
public sealed class LlmGate(IOptions<IngestOptions> options) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(
        options.Value.LlmMaxConcurrency,
        options.Value.LlmMaxConcurrency);

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        if (!await _slots.WaitAsync(options.Value.LlmAcquireTimeoutMs, ct))
            throw new LlmBusyException();
        try
        {
            return await RunHeldAsync(work, ct);
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <summary>Runs <paramref name="work"/> ONLY if a slot is immediately free
    /// (non-blocking acquire, no wait). Returns false without running when the
    /// gate is saturated — the caller decides what a busy gate means. Used by
    /// /health, which must never add GPU load beyond the gate nor steal a slot
    /// from a live request: a full gate already proves the model is loaded and
    /// generating, so "busy" is itself a healthy answer. Exceptions from the
    /// work (e.g. the health timeout) propagate; the slot is always released.</summary>
    public async Task<bool> TryRunAsync(Func<CancellationToken, Task> work, CancellationToken ct)
    {
        if (!await _slots.WaitAsync(0, ct))
            return false;
        try
        {
            await RunHeldAsync<object?>(async innerCt => { await work(innerCt); return null; }, ct);
            return true;
        }
        finally
        {
            _slots.Release();
        }
    }

    // Bound the generation with a server-side deadline so a HUNG model call
    // cannot hold this slot (and the request thread) forever. The linked source
    // cancels on EITHER the caller's request-abort OR the timeout; on timeout
    // the work throws OperationCanceledException carrying the timeout token (ct
    // is NOT cancelled), so callers' request-cancellation guards
    // (`when (ct.IsCancellationRequested)`) correctly treat it as an LLM failure
    // (structure_failed / deterministic fallback), not a client disconnect.
    // Assumes a slot is already held; the caller's finally releases it.
    private async Task<T> RunHeldAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Value.LlmCallTimeoutMs);
        return await work(timeout.Token);
    }

    public void Dispose() => _slots.Dispose();
}
