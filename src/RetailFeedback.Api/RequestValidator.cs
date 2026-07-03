using System.Text.RegularExpressions;
using RetailFeedback.Domain.Structuring;

namespace RetailFeedback.Api;

public static partial class RequestValidator
{
    public static List<string> Validate(FeedbackRequest request, IngestOptions options)
    {
        var errors = ValidateText(request.Text, options);

        if (!options.AllowedSources.Contains(request.Source, StringComparer.Ordinal))
            errors.Add($"source must be one of [{string.Join(", ", options.AllowedSources)}], got '{request.Source}'.");

        if (!TimestampNormalizer.TryNormalize(request.Timestamp, out _))
            errors.Add($"timestamp must be a parseable ISO-8601 instant, got '{request.Timestamp}'.");

        if (request.Id is not null && (request.Id.Length > options.IdMaxLength || !IdShape().IsMatch(request.Id)))
            errors.Add($"id must match [A-Za-z0-9._-], max {options.IdMaxLength} chars.");

        // Corrected values from the desk UI must be schema-legal too.
        if (request.AcceptedStructure is not null)
            errors.AddRange(StructureValidator.Validate(request.AcceptedStructure));
        if (request.Corrections is not null)
        {
            if (request.AcceptedStructure is null)
                errors.Add("corrections require an acceptedStructure.");
            // A manual entry after a failed interpretation has no model values
            // to correct — allowing both would let corrections leak into a
            // population the telemetry denominator excludes.
            if (request.ModelInterpretationFailed == true)
                errors.Add("corrections cannot be combined with modelInterpretationFailed.");
            // Field keys feed the per-field correction-rate telemetry (the
            // mechanism replacing the skipped model eval) — free-form keys
            // would fragment those rates.
            errors.AddRange(request.Corrections
                .Where(c => !StructuringSchema.Fields.Contains(c.Field))
                .Select(c => $"correction field '{c.Field}' is not a schema field ({string.Join(", ", StructuringSchema.Fields)})."));
        }

        return errors;
    }

    public static List<string> ValidateText(string? text, IngestOptions options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            errors.Add("text must be non-empty.");
        else if (text.Length > options.InputMaxChars)
            errors.Add($"text exceeds the {options.InputMaxChars}-character cap ({text.Length}).");
        return errors;
    }

    // Safe in URLs and HTTP headers (the id goes into the Location header).
    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")]
    private static partial Regex IdShape();
}
