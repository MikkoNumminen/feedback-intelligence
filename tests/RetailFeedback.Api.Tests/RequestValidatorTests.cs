using RetailFeedback.Api;
using RetailFeedback.Domain.Structuring;

namespace RetailFeedback.Api.Tests;

public class RequestValidatorTests
{
    private static readonly IngestOptions Options = new();

    private static FeedbackRequest Valid() => new(
        null, "desk", "maito oli vanhaa", "2026-07-01T10:00:00+03:00", null, null);

    [Fact]
    public void ValidRequest_PassesClean()
    {
        Assert.Empty(RequestValidator.Validate(Valid(), Options));
    }

    [Fact]
    public void OversizedText_IsRejected_Containment()
    {
        var request = Valid() with { Text = new string('a', Options.InputMaxChars + 1) };

        var errors = RequestValidator.Validate(request, Options);

        Assert.Contains(errors, e => e.Contains("cap"));
    }

    [Fact]
    public void UnknownSource_IsRejected()
    {
        var errors = RequestValidator.Validate(Valid() with { Source = "fax" }, Options);

        Assert.Contains(errors, e => e.Contains("source"));
    }

    [Fact]
    public void NonIsoTimestamp_IsRejected()
    {
        var errors = RequestValidator.Validate(Valid() with { Timestamp = "eilen kello kolme" }, Options);

        Assert.Contains(errors, e => e.Contains("timestamp"));
    }

    [Fact]
    public void CorrectionsWithoutStructure_AreRejected()
    {
        var request = Valid() with { Corrections = [new FieldCorrection("severity", "low", "high")] };

        var errors = RequestValidator.Validate(request, Options);

        Assert.Contains(errors, e => e.Contains("acceptedStructure"));
    }

    [Fact]
    public void NonAsciiOrControlCharId_IsRejected()
    {
        // The id lands in the Location header — a Finnish 'ä' or a newline
        // there would 500 AFTER the row was stored.
        Assert.Contains(RequestValidator.Validate(Valid() with { Id = "kuitti-ä1" }, Options), e => e.Contains("id"));
        Assert.Contains(RequestValidator.Validate(Valid() with { Id = "a\nb" }, Options), e => e.Contains("id"));
        Assert.Empty(RequestValidator.Validate(Valid() with { Id = "gen-42-0001" }, Options));
    }

    [Fact]
    public void CorrectionFieldNames_MustBeSchemaFields()
    {
        var structure = new FeedbackStructure("maito_kylma", "tuoreus", "high", "complaint", "fi");
        var request = Valid() with
        {
            AcceptedStructure = structure,
            Corrections = [new FieldCorrection("vakavuus", "low", "high")],
        };

        var errors = RequestValidator.Validate(request, Options);

        Assert.Contains(errors, e => e.Contains("vakavuus"));
        Assert.Empty(RequestValidator.Validate(
            request with { Corrections = [new FieldCorrection("severity", "low", "high")] }, Options));
    }

    [Fact]
    public void CorrectionsOnModelFailedEntry_AreRejected()
    {
        var structure = new FeedbackStructure("maito_kylma", "tuoreus", "high", "complaint", "fi");
        var request = Valid() with
        {
            AcceptedStructure = structure,
            Corrections = [new FieldCorrection("severity", "low", "high")],
            ModelInterpretationFailed = true,
        };

        Assert.Contains(RequestValidator.Validate(request, Options), e => e.Contains("modelInterpretationFailed"));
    }

    [Fact]
    public void CorrectedStructure_MustStillBeSchemaLegal()
    {
        var request = Valid() with
        {
            AcceptedStructure = new FeedbackStructure("kylmäosasto", "tuoreus", "high", "complaint", "fi"),
        };

        var errors = RequestValidator.Validate(request, Options);

        Assert.Contains(errors, e => e.Contains("kylmäosasto"));
    }
}
