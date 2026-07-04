namespace FeedbackIntelligence.Core.Structuring;

/// <summary>Validates an already-materialized structure (e.g. a human-corrected
/// one from the desk UI) against schema v0 — corrected values must be legal too.</summary>
public static class StructureValidator
{
    public static List<string> Validate(FeedbackStructure structure)
    {
        var errors = new List<string>();
        if (!StructuringSchema.Departments.Contains(structure.Department))
            errors.Add($"department '{structure.Department}' is not a schema enum value");
        if (!StructuringSchema.Severities.Contains(structure.Severity))
            errors.Add($"severity '{structure.Severity}' is not a schema enum value");
        if (!StructuringSchema.Types.Contains(structure.Type))
            errors.Add($"type '{structure.Type}' is not a schema enum value");
        if (string.IsNullOrWhiteSpace(structure.Theme))
            errors.Add("theme must be a non-empty string");
        if (string.IsNullOrWhiteSpace(structure.Language))
            errors.Add("language must be a non-empty string");
        return errors;
    }
}
