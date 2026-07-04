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

    public void Set(string key, ManagementReport report, TimeSpan ttl)
    {
        lock (_lock)
        {
            _entry = (key, DateTimeOffset.UtcNow + ttl, report);
        }
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _entry = null;
        }
    }
}
