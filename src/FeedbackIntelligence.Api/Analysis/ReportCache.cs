namespace FeedbackIntelligence.Api.Analysis;

/// <summary>
/// One-entry report cache with ingest-driven invalidation. Purpose: repeated
/// view refreshes must not re-run dozens of LLM calls (starving the desk
/// /interpret path on the shared 2-slot gate), while a new desk entry must
/// appear on the very next refresh — the live-demo centerpiece.
/// </summary>
public sealed class ReportCache
{
    private readonly object _lock = new();
    private (string Key, DateTimeOffset Expires, ManagementReport Report)? _entry;
    private long _epoch;

    /// <summary>The invalidation epoch. Capture it BEFORE reading the items a report
    /// is built from, then pass it to <see cref="Set"/>: if an ingest invalidated the
    /// cache while the (~20 s) generation ran, the epoch changed and the stale report
    /// is NOT cached — so "a fresh desk entry appears on the very next refresh" holds
    /// even when the entry is saved mid-generation (lost-invalidation TOCTOU).</summary>
    public long Epoch
    {
        get { lock (_lock) return _epoch; }
    }

    public bool TryGet(string key, out ManagementReport report)
    {
        lock (_lock)
        {
            if (_entry is { } entry && entry.Key == key && DateTimeOffset.UtcNow < entry.Expires)
            {
                report = entry.Report;
                return true;
            }
        }
        report = null!;
        return false;
    }

    /// <summary>Cache the report only if the epoch is unchanged since
    /// <paramref name="expectedEpoch"/> was captured — a compare-and-set that drops
    /// the write when an ingest invalidated during generation. Returns whether it cached.</summary>
    public bool Set(string key, ManagementReport report, TimeSpan ttl, long expectedEpoch)
    {
        lock (_lock)
        {
            if (_epoch != expectedEpoch)
                return false;
            _entry = (key, DateTimeOffset.UtcNow + ttl, report);
            return true;
        }
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _epoch++;
            _entry = null;
        }
    }
}
