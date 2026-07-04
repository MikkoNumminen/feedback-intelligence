using Microsoft.Extensions.Options;

namespace FeedbackIntelligence.Core.Domain;

/// <summary>Bound from the "Domain" config section; selects the active domain
/// module. Switchable from the .NET CLI: <c>--Domain:Active=game</c>.</summary>
public sealed class DomainOptions
{
    public const string SectionName = "Domain";

    /// <summary>Name of the active domain module — a folder under <see cref="Root"/>.</summary>
    public string Active { get; init; } = "retail";

    /// <summary>Root folder holding domain modules (each a subfolder).</summary>
    public string Root { get; init; } = "domains";
}

public sealed class DomainOptionsValidator : IValidateOptions<DomainOptions>
{
    public ValidateOptionsResult Validate(string? name, DomainOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Active))
            failures.Add("Domain:Active must name a domain module.");
        if (string.IsNullOrWhiteSpace(options.Root))
            failures.Add("Domain:Root must be set.");
        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
