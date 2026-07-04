using FeedbackIntelligence.Api;

namespace FeedbackIntelligence.Api.Tests;

public class TimestampNormalizerTests
{
    [Fact]
    public void FinnishOffset_NormalizesToSameUtcInstant()
    {
        Assert.True(TimestampNormalizer.TryNormalize("2026-07-01T10:00:00+03:00", out var normalized));

        Assert.Equal("2026-07-01T07:00:00.0000000+00:00", normalized);
    }

    [Fact]
    public void MixedOffsets_CompareCorrectlyAfterNormalization()
    {
        // The raw strings compare WRONG lexically ('09:'+03:00 vs '08:'Z);
        // normalized they must order by instant.
        TimestampNormalizer.TryNormalize("2026-07-01T09:00:00+03:00", out var early); // 06:00Z
        TimestampNormalizer.TryNormalize("2026-07-01T08:00:00Z", out var late);       // 08:00Z

        Assert.True(string.CompareOrdinal(early, late) < 0);
    }

    [Fact]
    public void DateOnly_IsAssumedUtc()
    {
        Assert.True(TimestampNormalizer.TryNormalize("2026-06-18", out var normalized));

        Assert.StartsWith("2026-06-18T00:00:00", normalized);
    }

    [Fact]
    public void Garbage_IsRejected()
    {
        Assert.False(TimestampNormalizer.TryNormalize("eilen kello kolme", out _));
    }
}
