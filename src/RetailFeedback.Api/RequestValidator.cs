using System.Globalization;
using RetailFeedback.Domain.Structuring;

namespace RetailFeedback.Api;

public static class RequestValidator
{
    public static List<string> Validate(FeedbackRequest request, IngestOptions options)
    {
        var errors = ValidateText(request.Text, options);

        if (!options.AllowedSources.Contains(request.Source, StringComparer.Ordinal))
            errors.Add($"source must be one of [{string.Join(", ", options.AllowedSources)}], got '{request.Source}'.");

        if (!DateTimeOffset.TryParse(request.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            errors.Add($"timestamp must be ISO-8601, got '{request.Timestamp}'.");

        if (request.Id is { Length: > 100 })
            errors.Add("id must be at most 100 characters.");

        // Corrected values from the desk UI must be schema-legal too.
        if (request.AcceptedStructure is not null)
            errors.AddRange(StructureValidator.Validate(request.AcceptedStructure));
        if (request.Corrections is not null && request.AcceptedStructure is null)
            errors.Add("corrections require an acceptedStructure.");

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
}
