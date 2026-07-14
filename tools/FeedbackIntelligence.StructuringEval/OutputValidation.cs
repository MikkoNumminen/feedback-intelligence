using System.Text.Json;
using FeedbackIntelligence.Core.Domain;
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
    // NON-FATAL theme-format advisories (ADR-0028 follow-up): underscores, stray
    // casing, or >4 words in the free-text theme. Reported separately and NEVER
    // folded into SchemaAdherent — a theme with an underscore is still schema-valid,
    // just not the base-form/plain-space shape the prompt now asks for.
    IReadOnlyList<FieldViolation> ThemeFormatWarnings,
    FeedbackStructure? Structure);

public static class OutputValidation
{
    public static ValidatedOutput Validate(string raw, DomainDescriptor domain)
    {
        var (outcome, doc) = Parse(raw);
        if (doc is null)
            return new ValidatedOutput(ParseOutcome.Unparseable, false, [], [], null);

        using (doc)
        {
            var root = doc.RootElement;
            var violations = new List<FieldViolation>();

            foreach (var field in StructuringSchema.Fields)
                if (!root.TryGetProperty(field, out _))
                    violations.Add(new FieldViolation(field, "missing_field", ""));

            foreach (var prop in root.EnumerateObject())
                if (!StructuringSchema.KnownFields.Contains(prop.Name))
                    violations.Add(new FieldViolation(prop.Name, "extra_field", Formatting.Truncate(prop.Value.ToString(), 40)));

            CheckEnum(root, "category", domain.Categories, violations);
            CheckEnum(root, "severity", domain.Severities, violations);
            CheckEnum(root, "type", domain.Types, violations);
            CheckNonEmptyString(root, "theme", violations);
            CheckNonEmptyString(root, "language", violations);
            // Optional sentiment (ADR-0031): absent is fine, but a PRESENT value must
            // be a legal sentiment key — a bad one is a real enum violation.
            if (root.TryGetProperty("sentiment", out _))
                CheckEnum(root, "sentiment", domain.Sentiments, violations);

            // Advisory (never a schema violation): does the theme follow the
            // base-form / lowercase / plain-space shape the prompt asks for?
            var themeFormatWarnings = new List<FieldViolation>();
            if (root.TryGetProperty("theme", out var themeEl) && themeEl.ValueKind == JsonValueKind.String)
                CheckThemeFormat(themeEl.GetString()!, themeFormatWarnings);

            FeedbackStructure? structure = null;
            if (violations.Count == 0)
                structure = new FeedbackStructure(
                    root.GetProperty("category").GetString()!,
                    root.GetProperty("theme").GetString()!,
                    root.GetProperty("severity").GetString()!,
                    root.GetProperty("type").GetString()!,
                    root.GetProperty("language").GetString()!);

            return new ValidatedOutput(outcome, violations.Count == 0, violations, themeFormatWarnings, structure);
        }
    }

    /// <summary>Non-fatal theme-format checks (ADR-0028 follow-up): the prompt asks
    /// for a lowercase, base-form, plain-space noun phrase. Flags underscores,
    /// stray casing, and &gt;4 words — advisory only, never a schema violation.</summary>
    private static void CheckThemeFormat(string theme, List<FieldViolation> warnings)
    {
        var t = theme.Trim();
        if (t.Length == 0)
            return;
        if (t.Contains('_', StringComparison.Ordinal))
            warnings.Add(new FieldViolation("theme", "format_underscore", Formatting.Truncate(t, 40)));
        if (!string.Equals(t, t.ToLowerInvariant(), StringComparison.Ordinal))
            warnings.Add(new FieldViolation("theme", "format_uppercase", Formatting.Truncate(t, 40)));
        if (t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 4)
            warnings.Add(new FieldViolation("theme", "format_wordcount", Formatting.Truncate(t, 40)));
    }

    private static void CheckEnum(JsonElement root, string field, IReadOnlySet<string> allowed, List<FieldViolation> violations)
    {
        if (!root.TryGetProperty(field, out var value))
            return;
        if (value.ValueKind != JsonValueKind.String)
        {
            violations.Add(new FieldViolation(field, "non_string", Formatting.Truncate(value.ToString(), 40)));
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
            violations.Add(new FieldViolation(field, "non_string", Formatting.Truncate(value.ToString(), 40)));
    }

    private static (ParseOutcome Outcome, JsonDocument? Doc) Parse(string raw)
    {
        if (!LlmJsonExtractor.TryExtractObject(raw, out var doc, out var strict))
            return (ParseOutcome.Unparseable, null);
        return (strict ? ParseOutcome.StrictJson : ParseOutcome.SalvagedJson, doc);
    }

}
