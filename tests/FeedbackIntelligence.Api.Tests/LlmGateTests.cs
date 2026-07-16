using Microsoft.Extensions.Options;
using FeedbackIntelligence.Api;

namespace FeedbackIntelligence.Api.Tests;

public class LlmGateTests
{
    private static LlmGate Gate(int slots, int acquireMs = 500, int callMs = 120_000) =>
        new(Options.Create(new IngestOptions
        {
            LlmMaxConcurrency = slots,
            LlmAcquireTimeoutMs = acquireMs,
            LlmCallTimeoutMs = callMs,
        }));

    [Fact]
    public async Task TryRunAsync_RunsWork_WhenASlotIsFree()
    {
        var gate = Gate(slots: 2);
        var ran = false;

        var acquired = await gate.TryRunAsync(_ => { ran = true; return Task.CompletedTask; }, CancellationToken.None);

        Assert.True(acquired); // a free slot means the work runs
        Assert.True(ran);
    }

    [Fact]
    public async Task TryRunAsync_SkipsWork_WhenGateIsSaturated_WithoutBlocking()
    {
        // ADR-0040: /health must never add GPU load beyond the gate. Hold BOTH
        // slots, then a non-blocking probe returns false immediately WITHOUT
        // running its work and WITHOUT waiting the acquire timeout.
        var gate = Gate(slots: 2, acquireMs: 10_000); // long acquire — the probe must not wait it
        var release = new TaskCompletionSource();
        var occupied = new TaskCompletionSource();
        var held = 0;

        async Task Hold(CancellationToken _)
        {
            if (Interlocked.Increment(ref held) == 2) occupied.SetResult();
            await release.Task;
        }
        var h1 = gate.RunAsync<object?>(async ct => { await Hold(ct); return null; }, CancellationToken.None);
        var h2 = gate.RunAsync<object?>(async ct => { await Hold(ct); return null; }, CancellationToken.None);
        await occupied.Task; // both slots now held

        var probeRan = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var acquired = await gate.TryRunAsync(_ => { probeRan = true; return Task.CompletedTask; }, CancellationToken.None);
        sw.Stop();

        Assert.False(acquired);              // gate full — not acquired
        Assert.False(probeRan);              // ...so the probe work never ran
        Assert.True(sw.ElapsedMilliseconds < 1_000); // and it did NOT wait the 10 s acquire timeout

        release.SetResult();
        await Task.WhenAll(h1, h2);

        // Slots freed — a probe now runs again (release really returned them).
        Assert.True(await gate.TryRunAsync(_ => Task.CompletedTask, CancellationToken.None));
    }

    [Fact]
    public async Task TryRunAsync_ReleasesSlot_EvenWhenWorkThrows()
    {
        var gate = Gate(slots: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.TryRunAsync(_ => throw new InvalidOperationException("model down"), CancellationToken.None));

        // The single slot must have been released despite the throw — the next
        // acquire (RunAsync) succeeds rather than shedding.
        var ran = await gate.RunAsync(_ => Task.FromResult(true), CancellationToken.None);
        Assert.True(ran);
    }

    [Fact]
    public async Task RunAsync_Sheds_WhenNoSlotFreesWithinAcquireTimeout()
    {
        var gate = Gate(slots: 1, acquireMs: 50);
        var release = new TaskCompletionSource();
        var holding = gate.RunAsync<object?>(async _ => { await release.Task; return null; }, CancellationToken.None);
        await Task.Delay(20); // let the holder take the only slot

        await Assert.ThrowsAsync<LlmBusyException>(() =>
            gate.RunAsync(_ => Task.FromResult(true), CancellationToken.None));

        release.SetResult();
        await holding;
    }
}
