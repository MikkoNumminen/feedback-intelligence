using System.Text.Json;
using FeedbackIntelligence.Core.Domain;
using FeedbackIntelligence.Core.Structuring;

namespace FeedbackIntelligence.Llm.Structuring;

/// <summary>
/// Pure salvage + validation layer over raw structuring-model output — a
/// mandatory production component (ADR-0004), unit-tested
/// against the exact failure shapes the placeholder run caught: fenced JSON,
/// category-as-array, invented enum values.
/// </summary>
public static class StructuringOutputParser
{
    public sealed record Attempt(
        FeedbackStructure? Structure,
        bool Salvaged,
        bool Normalized,
        IReadOnlyList<string> Notes,
        IReadOnlyList<string> Violations);

    public static Attempt Parse(string raw, DomainDescriptor domain)
    {
        if (!LlmJsonExtractor.TryExtractObject(raw, out var doc, out var strict))
            return new Attempt(null, false, false, [], ["output contains no parseable JSON object"]);

        using (doc)
        {
            var root = doc!.RootElement;
            var notes = new List<string>();
            var violations = new List<string>();
            var normalized = false;

            var category = ReadEnumField(root, "category", domain.Categories, allowArrayFirst: true, notes, violations, ref normalized);
            var severity = ReadEnumField(root, "severity", domain.Severities, allowArrayFirst: false, notes, violations, ref normalized);
            var type = ReadEnumField(root, "type", domain.Types, allowArrayFirst: false, notes, violations, ref normalized);
            var theme = ReadTextField(root, "theme", lowercase: false, notes, violations, ref normalized);
            var language = ReadTextField(root, "language", lowercase: true, notes, violations, ref normalized);
            // OPTIONAL sentiment (ADR-0031): absent is fine (deterministic fallback);
            // present-but-invalid is SALVAGED to null with a note, never a violation —
            // an optional field must not fail an otherwise-valid item.
            var sentiment = ReadOptionalEnumField(root, "sentiment", domain.Sentiments, notes, ref normalized);

            // Extra fields are tolerated in production (only known fields are ever
            // taken) but noted — the eval counts them strictly.
            foreach (var prop in root.EnumerateObject())
                if (!StructuringSchema.KnownFields.Contains(prop.Name))
                    notes.Add($"ignored extra field '{prop.Name}'");

            if (violations.Count > 0)
                return new Attempt(null, !strict, normalized, notes, violations);

            return new Attempt(
                new FeedbackStructure(category!, theme!, severity!, type!, language!, sentiment),
                Salvaged: !strict,
                Normalized: normalized,
                notes,
                violations);
        }
    }

    private static string? ReadEnumField(
        JsonElement root,
        string field,
        IReadOnlySet<string> allowed,
        bool allowArrayFirst,
        List<string> notes,
        List<string> violations,
        ref bool normalized)
    {
        if (!root.TryGetProperty(field, out var value))
        {
            violations.Add($"missing field '{field}'");
            return null;
        }

        string? candidate;
        if (value.ValueKind == JsonValueKind.String)
        {
            candidate = value.GetString();
        }
        else if (allowArrayFirst
            && value.ValueKind == JsonValueKind.Array
            && value.GetArrayLength() > 0
            && value[0].ValueKind == JsonValueKind.String)
        {
            // Measured Poro behavior on multi-category feedback (placeholder
            // run 2026-07-03): an array instead of one value. Primary = first
            // element by rule; the discard is logged, never silent.
            candidate = value[0].GetString();
            notes.Add($"'{field}' was an array {value}; kept first element, discarded the rest");
            normalized = true;
        }
        else
        {
            violations.Add($"'{field}' must be a string, got {value.ValueKind}: {Truncate(value.ToString())}");
            return null;
        }

        var cleaned = (candidate ?? "").Trim().ToLowerInvariant();
        if (cleaned != candidate)
        {
            normalized = true;
            notes.Add($"'{field}' normalized from '{candidate}' to '{cleaned}'");
        }

        if (!allowed.Contains(cleaned))
        {
            violations.Add($"'{field}' value '{candidate}' is not in the allowed set");
            return null;
        }

        return cleaned;
    }

    /// <summary>Reads an OPTIONAL enum field (ADR-0031 sentiment): absent → null,
    /// no violation; present-but-invalid → null + note (salvage), no violation.
    /// The field never fails the structure — the report has a deterministic
    /// fallback for it.</summary>
    private static string? ReadOptionalEnumField(
        JsonElement root, string field, IReadOnlySet<string> allowed, List<string> notes, ref bool normalized)
    {
        if (!root.TryGetProperty(field, out var value))
            return null;
        if (value.ValueKind != JsonValueKind.String)
        {
            notes.Add($"optional '{field}' was {value.ValueKind}, ignored");
            return null;
        }
        var candidate = value.GetString();
        var cleaned = (candidate ?? "").Trim().ToLowerInvariant();
        if (cleaned.Length == 0)
            return null;
        if (cleaned != candidate)
        {
            normalized = true;
            notes.Add($"'{field}' normalized from '{candidate}' to '{cleaned}'");
        }
        if (!allowed.Contains(cleaned))
        {
            notes.Add($"optional '{field}' value '{candidate}' is not in the allowed set, ignored");
            return null;
        }
        return cleaned;
    }

    private static string? ReadTextField(
        JsonElement root,
        string field,
        bool lowercase,
        List<string> notes,
        List<string> violations,
        ref bool normalized)
    {
        if (!root.TryGetProperty(field, out var value))
        {
            violations.Add($"missing field '{field}'");
            return null;
        }

        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            violations.Add($"'{field}' must be a non-empty string, got {value.ValueKind}: {Truncate(value.ToString())}");
            return null;
        }

        var original = value.GetString()!;
        var cleaned = lowercase ? original.Trim().ToLowerInvariant() : original.Trim();
        if (cleaned != original)
        {
            normalized = true;
            notes.Add($"'{field}' normalized from '{original}' to '{cleaned}'");
        }

        return cleaned;
    }

    private static string Truncate(string s) => s.Length <= 60 ? s : s[..60] + "…";
}
