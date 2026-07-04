using FeedbackIntelligence.Generator;

namespace FeedbackIntelligence.Generator.Tests;

public class ReportVerifierTests
{
    private const string GroundTruth = """
        {
          "seed": 1, "anchorDate": "2026-07-01", "nonEvidential": true,
          "stories": [
            {
              "id": "dairy", "kind": "recurring_signal",
              "feedbackIds": ["a1", "a2", "a3", "a4"],
              "expectedCategory": "maito_kylma",
              "expectedThemeKeywords": ["tuoreus"],
              "windowFrom": "2026-06-18", "windowTo": "2026-07-01",
              "trend": "worsening", "minGroundedIds": 3, "expectAlert": false
            },
            {
              "id": "safety", "kind": "alert_by_understanding",
              "feedbackIds": ["s1"],
              "expectedCategory": "rakennustarvike",
              "expectedThemeKeywords": ["turvallisuus"],
              "windowFrom": "2026-06-25", "windowTo": "2026-07-01",
              "trend": "stable", "minGroundedIds": 1, "expectAlert": true
            }
          ]
        }
        """;

    private static string Report(
        string dairyDirection = "worsening",
        string dairyIds = """["a1","a2","a3","a4"]""",
        string dairyCategory = "maito_kylma",
        string alerts = """[{"feedbackId":"s1"}]""",
        string windowFrom = "2026-06-10T00:00:00.0000000+00:00",
        string windowTo = "2026-07-02T00:00:00.0000000+00:00") => $$"""
        {
          "windowFrom": "{{windowFrom}}", "windowTo": "{{windowTo}}",
          "alerts": {{alerts}},
          "themes": [
            {
              "category": "{{dairyCategory}}", "title": "Maidon tuoreus",
              "narrative": "Tuoreus heikkenee.", "count": 4,
              "direction": "{{dairyDirection}}", "feedbackIds": {{dairyIds}}
            },
            {
              "category": "rakennustarvike", "title": "Terassi",
              "narrative": "Rakenne petti.", "count": 1,
              "direction": "stable", "feedbackIds": ["s1"]
            }
          ]
        }
        """;

    [Fact]
    public void FullyGroundedReport_Passes()
    {
        var results = ReportVerifier.Verify(GroundTruth, Report());

        Assert.All(results, r => Assert.True(r.Pass));
        Assert.Equal(4, results[0].GroundedIds);
        Assert.True(results[0].KeywordSeen);
    }

    [Fact]
    public void InsufficientGrounding_Fails()
    {
        var results = ReportVerifier.Verify(GroundTruth, Report(dairyIds: """["a1","a2"]"""));

        Assert.False(results[0].GroundingPass);
        Assert.Equal(2, results[0].GroundedIds);
        Assert.False(results[0].Pass);
    }

    [Fact]
    public void GroundingInWrongCategory_DoesNotCount()
    {
        // The story ids appear under the WRONG category: misclassification,
        // not grounding.
        var results = ReportVerifier.Verify(GroundTruth, Report(dairyCategory: "hevi"));

        Assert.False(results[0].GroundingPass);
        Assert.Equal(0, results[0].GroundedIds);
    }

    [Fact]
    public void UnexpectedTrendDirection_WarnsButDoesNotFail()
    {
        // The report's direction is a category AGGREGATE; same-category
        // noise legitimately dilutes it. Warning tier, not a gate.
        var results = ReportVerifier.Verify(GroundTruth, Report(dairyDirection: "declining"));

        Assert.False(results[0].TrendOk);
        Assert.True(results[0].Pass);
    }

    [Fact]
    public void GrowingSatisfiesWorsening()
    {
        // Volume growth without a severity shift still satisfies a "worsening"
        // plant — "worsening" is the stricter, preferred read.
        var results = ReportVerifier.Verify(GroundTruth, Report(dairyDirection: "growing"));

        Assert.True(results[0].TrendOk);
    }

    [Fact]
    public void NoiseSharingTheCategory_DoesNotBreakGrounding()
    {
        // Production-normal: the dairy theme carries story ids PLUS noise ids
        // the LLM classified into the same category.
        var results = ReportVerifier.Verify(GroundTruth,
            Report(dairyIds: """["n9","a1","n8","a2","a3","n7","a4","n6"]"""));

        Assert.True(results[0].GroundingPass);
        Assert.Equal(4, results[0].GroundedIds);
        Assert.True(results[0].Pass);
    }

    [Fact]
    public void ReportWindowNotCoveringStoryWindow_FailsWithDiagnosis()
    {
        // Wrong-report operator error must be named, not read as a grounding
        // regression.
        var results = ReportVerifier.Verify(GroundTruth,
            Report(windowFrom: "2026-07-10T00:00:00.0000000+00:00", windowTo: "2026-07-20T00:00:00.0000000+00:00"));

        Assert.False(results[0].WindowCovered);
        Assert.False(results[0].Pass);
    }

    [Fact]
    public void EmptyGroundTruth_ThrowsInsteadOfVacuousPass()
    {
        var emptyTruth = """{ "seed": 1, "anchorDate": "2026-07-01", "nonEvidential": true, "stories": [] }""";

        Assert.Throws<System.IO.InvalidDataException>(() => ReportVerifier.Verify(emptyTruth, Report()));
    }

    [Fact]
    public void MissingExpectedAlert_Fails()
    {
        var results = ReportVerifier.Verify(GroundTruth, Report(alerts: "[]"));

        Assert.False(results[1].AlertPass);
        Assert.False(results[1].Pass);
    }

    [Fact]
    public void UnparseableReportWindow_IsToleratedNotFailed()
    {
        var results = ReportVerifier.Verify(GroundTruth, Report(windowFrom: "x", windowTo: "y"));

        Assert.True(results[0].WindowCovered);
    }
}
