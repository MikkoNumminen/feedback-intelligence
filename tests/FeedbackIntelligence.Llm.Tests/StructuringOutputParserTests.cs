using FeedbackIntelligence.Core.Domain;
using FeedbackIntelligence.Llm.Structuring;

namespace FeedbackIntelligence.Llm.Tests;

/// <summary>
/// The salvage layer is tested against the EXACT failure shapes the 2026-07-03
/// placeholder run caught (ADR-0004): fenced JSON,
/// category-as-array, invented enum values — plus the strict happy path.
/// </summary>
public class StructuringOutputParserTests
{
    private static readonly DomainDescriptor Retail = TestDomains.Retail();

    private const string ValidJson =
        """{"category": "maito_kylma", "theme": "tuotteiden tuoreus", "severity": "high", "type": "complaint", "language": "fi"}""";

    [Fact]
    public void StrictCleanJson_Succeeds_WithoutFlags()
    {
        var attempt = StructuringOutputParser.Parse(ValidJson, Retail);

        Assert.NotNull(attempt.Structure);
        Assert.False(attempt.Salvaged);
        Assert.False(attempt.Normalized);
        Assert.Empty(attempt.Violations);
        Assert.Equal("maito_kylma", attempt.Structure!.Category);
        Assert.Equal("fi", attempt.Structure.Language);
    }

    [Fact]
    public void FencedJson_PoroShape_IsSalvaged()
    {
        // Verbatim shape from the placeholder run: Poro fenced 27/27 outputs.
        var raw = "```json\n" + ValidJson + "\n```";

        var attempt = StructuringOutputParser.Parse(raw, Retail);

        Assert.NotNull(attempt.Structure);
        Assert.True(attempt.Salvaged);
        Assert.Empty(attempt.Violations);
    }

    [Fact]
    public void CategoryArray_PoroShape_NormalizesToFirstElement_AndLogsDiscard()
    {
        // Verbatim shape from the placeholder run (ph-009, all 3 reps).
        var raw =
            """{"category": ["maito_kylma", "tyokalut"], "theme": "laatu ja palvelu", "severity": "medium", "type": "complaint", "language": "fi"}""";

        var attempt = StructuringOutputParser.Parse(raw, Retail);

        Assert.NotNull(attempt.Structure);
        Assert.True(attempt.Normalized);
        Assert.Equal("maito_kylma", attempt.Structure!.Category);
        Assert.Contains(attempt.Notes, n => n.Contains("array") && n.Contains("category"));
    }

    [Fact]
    public void CategoryArray_WithInvalidFirstElement_IsViolation()
    {
        var raw =
            """{"category": ["kylmäosasto", "tyokalut"], "theme": "laatu", "severity": "medium", "type": "complaint", "language": "fi"}""";

        var attempt = StructuringOutputParser.Parse(raw, Retail);

        Assert.Null(attempt.Structure);
        Assert.Contains(attempt.Violations, v => v.Contains("category"));
    }

    [Fact]
    public void InventedEnumValue_IsViolation()
    {
        var raw =
            """{"category": "kylmäosasto", "theme": "tuoreus", "severity": "high", "type": "complaint", "language": "fi"}""";

        var attempt = StructuringOutputParser.Parse(raw, Retail);

        Assert.Null(attempt.Structure);
        Assert.Contains(attempt.Violations, v => v.Contains("kylmäosasto"));
    }

    [Fact]
    public void EnumCasing_IsNormalized_NotRejected()
    {
        var raw =
            """{"category": "Maito_Kylma", "theme": "tuoreus", "severity": "Medium", "type": "complaint", "language": "FI"}""";

        var attempt = StructuringOutputParser.Parse(raw, Retail);

        Assert.NotNull(attempt.Structure);
        Assert.True(attempt.Normalized);
        Assert.Equal("maito_kylma", attempt.Structure!.Category);
        Assert.Equal("medium", attempt.Structure.Severity);
        Assert.Equal("fi", attempt.Structure.Language);
    }

    [Fact]
    public void MissingField_IsViolation()
    {
        var raw = """{"category": "maito_kylma", "theme": "tuoreus", "severity": "high", "type": "complaint"}""";

        var attempt = StructuringOutputParser.Parse(raw, Retail);

        Assert.Null(attempt.Structure);
        Assert.Contains(attempt.Violations, v => v.Contains("language"));
    }

    [Fact]
    public void ExtraField_IsToleratedWithNote()
    {
        var raw =
            """{"category": "maito_kylma", "theme": "tuoreus", "severity": "high", "type": "complaint", "language": "fi", "confidence": 0.9}""";

        var attempt = StructuringOutputParser.Parse(raw, Retail);

        Assert.NotNull(attempt.Structure);
        Assert.Contains(attempt.Notes, n => n.Contains("confidence"));
    }

    [Fact]
    public void GarbageWithoutJson_IsViolation()
    {
        var attempt = StructuringOutputParser.Parse("Selvä homma, tässä analyysi palautteesta!", Retail);

        Assert.Null(attempt.Structure);
        Assert.Contains(attempt.Violations, v => v.Contains("no parseable JSON"));
    }

    [Fact]
    public void ThinkBlockBeforeJson_IsSalvaged()
    {
        var raw = "<think>Pohditaan osastoa hetki.</think>\n" + ValidJson;

        var attempt = StructuringOutputParser.Parse(raw, Retail);

        Assert.NotNull(attempt.Structure);
        Assert.True(attempt.Salvaged);
    }

    [Fact]
    public void NonStringSeverity_IsViolation()
    {
        var raw =
            """{"category": "maito_kylma", "theme": "tuoreus", "severity": 3, "type": "complaint", "language": "fi"}""";

        var attempt = StructuringOutputParser.Parse(raw, Retail);

        Assert.Null(attempt.Structure);
        Assert.Contains(attempt.Violations, v => v.Contains("severity"));
    }

    [Fact]
    public void ValidSentiment_IsAccepted_AsOptionalSixthField()
    {
        // ADR-0031: sentiment is OPTIONAL — present and valid, it flows straight
        // through onto the structure (lowercased/trimmed, like the other enums).
        var raw =
            """{"category": "maito_kylma", "theme": "tuoreus", "severity": "high", "type": "complaint", "language": "fi", "sentiment": "positive"}""";

        var attempt = StructuringOutputParser.Parse(raw, Retail);

        Assert.NotNull(attempt.Structure);
        Assert.Equal("positive", attempt.Structure!.Sentiment);
    }

    [Fact]
    public void InvalidSentiment_IsSalvaged_NotAViolation()
    {
        // An invented sentiment value nulls the field and leaves a note — unlike an
        // invented category/severity/type, it is NOT a violation, so the rest of the
        // structure still comes through (ADR-0031's salvage, not a strict rejection).
        var raw =
            """{"category": "maito_kylma", "theme": "tuoreus", "severity": "high", "type": "complaint", "language": "fi", "sentiment": "iloinen"}""";

        var attempt = StructuringOutputParser.Parse(raw, Retail);

        Assert.NotNull(attempt.Structure);
        Assert.Null(attempt.Structure!.Sentiment);
        Assert.Contains(attempt.Notes, n => n.Contains("sentiment"));
    }

    [Fact]
    public void MissingSentiment_StructureStillNonNull_FiveFieldBackCompat()
    {
        // No "sentiment" key at all (the pre-ADR-0031 shape, or a model that never
        // emits it) — absence is not a violation and not a note-worthy salvage.
        var raw =
            """{"category": "maito_kylma", "theme": "tuoreus", "severity": "high", "type": "complaint", "language": "fi"}""";

        var attempt = StructuringOutputParser.Parse(raw, Retail);

        Assert.NotNull(attempt.Structure);
        Assert.Null(attempt.Structure!.Sentiment);
    }
}
