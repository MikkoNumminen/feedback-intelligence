using RetailFeedback.Generator;

namespace RetailFeedback.Generator.Tests;

public class ReportVerifierTests
{
    private const string GroundTruth = """
        {
          "seed": 1, "anchorDate": "2026-07-01", "nonEvidential": true,
          "stories": [
            {
              "id": "dairy", "kind": "recurring_signal",
              "feedbackIds": ["a1", "a2", "a3", "a4"],
              "expectedDepartment": "maito_kylma",
              "expectedThemeKeywords": ["tuoreus"],
              "windowFrom": "2026-06-18", "windowTo": "2026-07-01",
              "trend": "worsening", "minGroundedIds": 3, "expectAlert": false
            },
            {
              "id": "safety", "kind": "alert_by_understanding",
              "feedbackIds": ["s1"],
              "expectedDepartment": "rakennustarvike",
              "expectedThemeKeywords": ["turvallisuus"],
              "windowFrom": "2026-06-25", "windowTo": "2026-07-01",
              "trend": "stable", "minGroundedIds": 1, "expectAlert": true
            }
          ]
        }
        """;

    private static string Report(
        string dairyDirection = "paheneva",
        string dairyIds = """["a1","a2","a3","a4"]""",
        string dairyDepartment = "maito_kylma",
        string alerts = """[{"feedbackId":"s1"}]""") => $$"""
        {
          "windowFrom": "x", "windowTo": "y",
          "alerts": {{alerts}},
          "themes": [
            {
              "department": "{{dairyDepartment}}", "title": "Maidon tuoreus",
              "narrative": "Tuoreus heikkenee.", "count": 4,
              "direction": "{{dairyDirection}}", "feedbackIds": {{dairyIds}}
            },
            {
              "department": "rakennustarvike", "title": "Terassi",
              "narrative": "Rakenne petti.", "count": 1,
              "direction": "vakaa", "feedbackIds": ["s1"]
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
    public void GroundingInWrongDepartment_DoesNotCount()
    {
        // The story ids appear under the WRONG department: misclassification,
        // not grounding.
        var results = ReportVerifier.Verify(GroundTruth, Report(dairyDepartment: "hevi"));

        Assert.False(results[0].GroundingPass);
        Assert.Equal(0, results[0].GroundedIds);
    }

    [Fact]
    public void WrongTrendDirection_Fails()
    {
        var results = ReportVerifier.Verify(GroundTruth, Report(dairyDirection: "laskeva"));

        Assert.False(results[0].TrendPass);
        Assert.False(results[0].Pass);
    }

    [Fact]
    public void KasvavaSatisfiesWorsening()
    {
        // Volume growth without a severity shift still satisfies a "worsening"
        // plant — paheneva is the stricter, preferred read.
        var results = ReportVerifier.Verify(GroundTruth, Report(dairyDirection: "kasvava"));

        Assert.True(results[0].TrendPass);
    }

    [Fact]
    public void MissingExpectedAlert_Fails()
    {
        var results = ReportVerifier.Verify(GroundTruth, Report(alerts: "[]"));

        Assert.False(results[1].AlertPass);
        Assert.False(results[1].Pass);
    }
}
