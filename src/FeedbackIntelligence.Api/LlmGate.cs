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
            // Bound the generation with a server-side deadline so a HUNG model call
            // cannot hold this slot (and the request thread) forever. The linked
            // source cancels on EITHER the caller's request-abort OR the timeout;
            // on timeout the work throws OperationCanceledException carrying the
            // timeout token (ct is NOT cancelled), so callers' request-cancellation
            // guards (`when (ct.IsCancellationRequested)`) correctly treat it as an
            // LLM failure (structure_failed / deterministic fallback), not a client
            // disconnect. The finally releases the slot either way.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.Value.LlmCallTimeoutMs);
            return await work(timeout.Token);
        }
        finally
        {
            _slots.Release();
        }
    }

    public void Dispose() => _slots.Dispose();
}
