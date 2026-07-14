using FeedbackIntelligence.Core.Domain;

namespace FeedbackIntelligence.Core.Structuring;

/// <summary>Validates an already-materialized structure (e.g. a human-corrected
/// one from the desk UI) against the active domain's taxonomy — corrected values
/// must be legal too. The field NAMES are universal; the enum VALUES come from
/// the domain descriptor, never from the core.</summary>
public static class StructureValidator
{
    public static List<string> Validate(FeedbackStructure structure, DomainDescriptor domain)
    {
        var errors = new List<string>();
        if (!domain.Categories.Contains(structure.Category))
            errors.Add($"category '{structure.Category}' is not a domain taxonomy value");
        if (!domain.Severities.Contains(structure.Severity))
            errors.Add($"severity '{structure.Severity}' is not a domain taxonomy value");
        if (!domain.Types.Contains(structure.Type))
            errors.Add($"type '{structure.Type}' is not a domain taxonomy value");
        if (string.IsNullOrWhiteSpace(structure.Theme))
            errors.Add("theme must be a non-empty string");
        if (string.IsNullOrWhiteSpace(structure.Language))
            errors.Add("language must be a non-empty string");
        // Sentiment is optional (ADR-0031): null is valid; a present value must be
        // a domain sentiment key. A human-corrected structure with a bad sentiment
        // IS an error here (unlike the salvaging ingest parser, which nulls it).
        if (structure.Sentiment is { } sentiment && !domain.Sentiments.Contains(sentiment))
            errors.Add($"sentiment '{sentiment}' is not a domain taxonomy value");
        return errors;
    }
}
