using FeedbackIntelligence.Core.Domain;
using FeedbackIntelligence.Core.Structuring;

namespace FeedbackIntelligence.Llm.Tests;

/// <summary>Covers <see cref="StructureValidator"/> against the REAL retail domain
/// descriptor (same loader as <see cref="StructuringOutputParserTests"/>) — this is
/// the strict re-validation path for a human-corrected structure, so unlike the
/// ingest parser's salvage, an invalid sentiment here IS an error (ADR-0031).</summary>
public class StructureValidatorTests
{
    private static readonly DomainDescriptor Retail = TestDomains.Retail();

    private static FeedbackStructure Valid(string? sentiment) =>
        new("maito_kylma", "tuoreus", "high", "complaint", "fi", sentiment);

    [Fact]
    public void ValidModelSentiment_ProducesNoErrors()
    {
        var errors = StructureValidator.Validate(Valid("negative"), Retail);

        Assert.Empty(errors);
    }

    [Fact]
    public void InvalidSentiment_IsAnError_MentioningSentiment()
    {
        var errors = StructureValidator.Validate(Valid("foo"), Retail);

        Assert.Contains(errors, e => e.Contains("sentiment"));
    }

    [Fact]
    public void NullSentiment_ProducesNoErrors()
    {
        var errors = StructureValidator.Validate(Valid(null), Retail);

        Assert.Empty(errors);
    }
}
