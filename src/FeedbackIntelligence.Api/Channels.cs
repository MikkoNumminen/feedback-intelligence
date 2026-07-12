namespace FeedbackIntelligence.Api;

/// <summary>DI service keys for the ingest/report channels (ADR-0024). The default
/// (unkeyed) registrations are the corpus/demo channel; <see cref="Live"/> is the
/// desk's own pipeline over its own database.</summary>
public static class Channels
{
    public const string Live = "live";
}
