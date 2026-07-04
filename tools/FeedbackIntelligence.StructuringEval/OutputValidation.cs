using System.Text.Json;
using FeedbackIntelligence.Core.Structuring;
using FeedbackIntelligence.Llm.Structuring;

namespace FeedbackIntelligence.StructuringEval;

public enum ParseOutcome
{
    /// <summary>The raw output (trimmed) was a single JSON object — full discipline.</summary>
    StrictJson,

    /// <summary>JSON was recovered only after stripping thinking traces / fences / prose.</summary>
    SalvagedJson,

    Unparseable,
}

/// <summary>Kind is one of: illegal_enum_value, non_string, missing_field, extra_field.</summary>
public sealed record FieldViolation(string Field, string Kind, string Value);

public sealed record ValidatedOutput(
    ParseOutcome Outcome,
    bool SchemaAdherent,
    IReadOnlyList<FieldViolation> Violations,
    FeedbackStructure? Structure);

public static class OutputValidation
{
    public static ValidatedOutput Validate(string raw)
    {
        var (outcome, doc) = Parse(raw);
        if (doc is null)
            return new ValidatedOutput(ParseOutcome.Unparseable, false, [], null);

        using (doc)
        {
            var root = doc.RootElement;
            var violations = new List<FieldViolation>();

            foreach (var field in StructuringSchema.Fields)
                if (!root.TryGetProperty(field, out _))
                    violations.Add(new FieldViolation(field, "missing_field", ""));

            foreach (var prop in root.EnumerateObject())
                if (!StructuringSchema.Fields.Contains(prop.Name))
                    violations.Add(new FieldViolation(prop.Name, "extra_field", Truncate(prop.Value.ToString(), 40)));

            CheckEnum(root, "category", StructuringSchema.Categories, violations);
            CheckEnum(root, "severity", StructuringSchema.Severities, violations);
            CheckEnum(root, "type", StructuringSchema.Types, violations);
            CheckNonEmptyString(root, "theme", violations);
            CheckNonEmptyString(root, "language", violations);

            FeedbackStructure? structure = null;
            if (violations.Count == 0)
                structure = new FeedbackStructure(
                    root.GetProperty("category").GetString()!,
                    root.GetProperty("theme").GetString()!,
                    root.GetProperty("severity").GetString()!,
                    root.GetProperty("type").GetString()!,
                    root.GetProperty("language").GetString()!);

            return new ValidatedOutput(outcome, violations.Count == 0, violations, structure);
        }
    }

    private static void CheckEnum(JsonElement root, string field, IReadOnlySet<string> allowed, List<FieldViolation> violations)
    {
        if (!root.TryGetProperty(field, out var value))
            return;
        if (value.ValueKind != JsonValueKind.String)
        {
            violations.Add(new FieldViolation(field, "non_string", Truncate(value.ToString(), 40)));
            return;
        }
        var s = value.GetString()!;
        if (!allowed.Contains(s))
            violations.Add(new FieldViolation(field, "illegal_enum_value", s));
    }

    private static void CheckNonEmptyString(JsonElement root, string field, List<FieldViolation> violations)
    {
        if (!root.TryGetProperty(field, out var value))
            return;
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            violations.Add(new FieldViolation(field, "non_string", Truncate(value.ToString(), 40)));
    }

    private static (ParseOutcome Outcome, JsonDocument? Doc) Parse(string raw)
    {
        if (!LlmJsonExtractor.TryExtractObject(raw, out var doc, out var strict))
            return (ParseOutcome.Unparseable, null);
        return (strict ? ParseOutcome.StrictJson : ParseOutcome.SalvagedJson, doc);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
