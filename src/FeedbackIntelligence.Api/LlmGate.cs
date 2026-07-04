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
            return await work(ct);
        }
        finally
        {
            _slots.Release();
        }
    }

    public void Dispose() => _slots.Dispose();
}
