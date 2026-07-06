using System.Text.Json;
using FeedbackIntelligence.Core.Security;

namespace FeedbackIntelligence.Llm.Tests;

/// <summary>
/// The A4 red-team regression guard (ADR-0021). A committed fixture of ~12 injection
/// payloads (Finnish + English: override, role, field-injection, forged answer/JSON,
/// row breakout via ASCII newline AND a Unicode separator, fence-marker reassembly,
/// suppression, an A3 directive, a homoglyph marker) + benign controls (dialect and
/// the no-keyword safety story). Each item declares which deterministic layer must
/// handle it AND — for flagged items — the exact symptom class, so tuning that
/// deletes one marker group makes a RED test (not masked by another class firing).
/// It does NOT prove safety — injection is unsolved; it proves the closed holes stay
/// closed and names the one that is not (the homoglyph residual).
/// </summary>
public class RedTeamCoverageTests
{
    public static IEnumerable<object[]> Fixture() =>
        RedTeam.Load().Select(i => new object[]
        {
            i.Id, i.Text, i.Expect, string.Join(",", i.ExpectSignals ?? Array.Empty<string>()),
        });

    [Theory]
    [MemberData(nameof(Fixture))]
    public void RedTeamItem_HandledByItsStatedLayer(string id, string text, string expect, string expectSignals)
    {
        var detected = InjectionSignals.Detect(text);
        var neutralized = UntrustedText.Neutralize(text);

        switch (expect)
        {
            case "flagged": // A2: the injection symptom is caught -> stored needs_review
                Assert.True(detected.Count > 0, $"{id}: expected an injection flag, got none");
                // Pin the SPECIFIC class, so deleting one marker group can't be masked by another.
                foreach (var signal in expectSignals.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    Assert.Contains(signal, detected);
                break;

            case "clean": // benign control: no false flag on either detector
                Assert.True(detected.Count == 0, $"{id}: benign text was flagged {string.Join(",", detected)}");
                Assert.False(NarrativeGuard.LooksActionBearing(text), $"{id}: benign text read as directive");
                break;

            case "neutralized": // A1: the breakout vector cannot survive the inline splice...
                Assert.Empty(detected); // ...and this item carries no A2 symptom (single-layer story)
                Assert.DoesNotContain('\n', neutralized);
                Assert.DoesNotContain('\r', neutralized);
                Assert.DoesNotContain('\t', neutralized);
                Assert.DoesNotContain('"', neutralized);
                Assert.DoesNotContain((char)0x2028, neutralized);
                Assert.DoesNotContain((char)0x2029, neutralized);
                Assert.DoesNotContain(UntrustedText.Open, neutralized);
                Assert.DoesNotContain(UntrustedText.Close, neutralized);
                break;

            case "directive": // A3: an injected directive is caught by the narrative guard...
                Assert.True(NarrativeGuard.LooksActionBearing(text), $"{id}: directive not caught");
                Assert.Empty(detected); // ...and is NOT an A2 injection symptom (single-layer story)
                break;

            case "residual-homoglyph": // NAMED gap: a homoglyph marker evades the exact-ASCII strip
                Assert.True(detected.Count == 0, $"{id}: unexpectedly flagged (fixture assumes it is a residual)");
                Assert.Contains("ALAUTE_LOPPU", neutralized); // the marker tail survives — the residual, pinned
                break;

            default:
                Assert.Fail($"{id}: unknown expect '{expect}'");
                break;
        }
    }

    [Fact]
    public void Detect_IsPureFunction_NoAccumulatedStateAcrossItems()
    {
        // Detect is a pure per-string function — no batch/shared-context API exists, so a
        // malicious item cannot influence a benign neighbor. This is a tripwire against a
        // future stateful refactor, not a claim that a batch API is being tested.
        var items = RedTeam.Load().ToList();
        var benign = items.Single(i => i.Id == "rt-11");
        var malicious = items.Single(i => i.Id == "rt-01");

        Assert.Empty(InjectionSignals.Detect(benign.Text));
        Assert.NotEmpty(InjectionSignals.Detect(malicious.Text));
        var before = InjectionSignals.Detect(benign.Text);
        _ = InjectionSignals.Detect(malicious.Text);
        var after = InjectionSignals.Detect(benign.Text);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Fixture_CoversEveryAttackClass_WithBenignControls()
    {
        var items = RedTeam.Load().ToList();
        Assert.True(items.Count >= 12, "fixture shrank — red-team coverage must not silently drop");

        // Pin each attack class by its label, so an in-place swap can't silently drop a
        // specific vector (e.g. the U+2028 or fence-reassembly case) while keeping counts.
        foreach (var attack in new[]
                 {
                     "fi-override-and-field-injection", "en-override-and-role", "forge-vastaus-kylla",
                     "forge-json-role", "row-breakout-newline", "fence-marker-reassembly",
                     "unicode-line-separator-row-forge", "suppression", "a3-directive-defamation",
                     "homoglyph-fence-marker",
                 })
            Assert.Contains(items, i => i.Attack == attack);

        Assert.True(items.Count(i => i.Expect == "clean") >= 2, "need >=2 benign controls to catch over-flagging");
    }
}

/// <summary>Loads the committed red-team fixture, located via the repo-wide
/// .sln-sentinel walk (TestDomains.RepoRoot) rather than a bespoke bounded walk.</summary>
public static class RedTeam
{
    public sealed record Item(
        string Id, string Attack, string Text, string Expect,
        string[]? ExpectSignals = null, string? Note = null);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEnumerable<Item> Load()
    {
        var path = Path.Combine(TestDomains.RepoRoot(), "data", "eval", "redteam-injection.jsonl");
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (JsonSerializer.Deserialize<Item>(line, Json) is { } item)
                yield return item;
        }
    }
}
