using System.Text.Json;
using FeedbackIntelligence.Core.Security;

namespace FeedbackIntelligence.Llm.Tests;

/// <summary>
/// The A4 red-team regression guard (ADR-0021). A committed fixture of ~12 injection
/// payloads (Finnish + English: override, role, field-injection, forged answer/JSON,
/// row breakout via ASCII newline AND a Unicode separator, fence-marker reassembly,
/// suppression, an A3 directive, a homoglyph marker) + benign controls (dialect and
/// the no-keyword safety story). Each item declares which deterministic layer must
/// handle it; this test asserts it does. The point is durability: a prompt or model
/// swap, or a "tidy-up" of a marker list, that silently reopens a closed hole makes a
/// RED test here. It does NOT prove safety — injection is unsolved; it proves the
/// closed holes stay closed and names the one that is not (the homoglyph residual).
/// </summary>
public class RedTeamCoverageTests
{
    public static IEnumerable<object[]> Fixture() =>
        RedTeam.Load().Select(i => new object[] { i.Id, i.Text, i.Expect });

    [Theory]
    [MemberData(nameof(Fixture))]
    public void RedTeamItem_HandledByItsStatedLayer(string id, string text, string expect)
    {
        var detected = InjectionSignals.Detect(text);
        var neutralized = UntrustedText.Neutralize(text);

        switch (expect)
        {
            case "flagged": // A2: the injection symptom is caught -> stored needs_review
                Assert.True(detected.Count > 0, $"{id}: expected an injection flag, got none");
                break;

            case "clean": // benign control: no false flag on either detector
                Assert.True(detected.Count == 0, $"{id}: benign text was flagged {string.Join(",", detected)}");
                Assert.False(NarrativeGuard.LooksActionBearing(text), $"{id}: benign text read as directive");
                break;

            case "neutralized": // A1: the breakout vector cannot survive the inline splice
                Assert.DoesNotContain('\n', neutralized);
                Assert.DoesNotContain('\r', neutralized);
                Assert.DoesNotContain('\t', neutralized);
                Assert.DoesNotContain('"', neutralized);
                Assert.DoesNotContain((char)0x2028, neutralized);
                Assert.DoesNotContain((char)0x2029, neutralized);
                Assert.DoesNotContain(UntrustedText.Open, neutralized);
                Assert.DoesNotContain(UntrustedText.Close, neutralized);
                break;

            case "directive": // A3: an injected directive is caught by the narrative guard
                Assert.True(NarrativeGuard.LooksActionBearing(text), $"{id}: directive not caught");
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
    public void Injection_DoesNotLeakAcrossItems_IsolationInvariant()
    {
        // Each item is judged independently; a malicious item in a batch cannot change
        // how a benign neighbor is classified. (Detect is a pure per-string function —
        // this pins that no shared/accumulated state is ever introduced.)
        var items = RedTeam.Load().ToList();
        var benign = items.Single(i => i.Id == "rt-11");
        var malicious = items.Single(i => i.Id == "rt-01");

        Assert.Empty(InjectionSignals.Detect(benign.Text));
        Assert.NotEmpty(InjectionSignals.Detect(malicious.Text));
        // Interleaving the scans does not alter either result.
        var b1 = InjectionSignals.Detect(benign.Text);
        _ = InjectionSignals.Detect(malicious.Text);
        var b2 = InjectionSignals.Detect(benign.Text);
        Assert.Equal(b1, b2);
    }

    [Fact]
    public void Fixture_CoversEveryAttackClass_WithBenignControls()
    {
        var items = RedTeam.Load().ToList();
        Assert.True(items.Count >= 12, "fixture shrank — red-team coverage must not silently drop");
        foreach (var cls in new[] { "flagged", "neutralized", "directive", "residual-homoglyph" })
            Assert.Contains(items, i => i.Expect == cls);
        Assert.True(items.Count(i => i.Expect == "clean") >= 2, "need >=2 benign controls to catch over-flagging");
    }
}

/// <summary>Loads the committed red-team fixture, located by walking up from the
/// test assembly to the repo's data/eval directory.</summary>
public static class RedTeam
{
    public sealed record Item(string Id, string Attack, string Text, string Expect, string? Note = null);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEnumerable<Item> Load()
    {
        foreach (var line in File.ReadAllLines(Locate()))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (JsonSerializer.Deserialize<Item>(line, Json) is { } item)
                yield return item;
        }
    }

    private static string Locate()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "data", "eval", "redteam-injection.jsonl");
            if (File.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException(
            "redteam-injection.jsonl not found walking up from " + AppContext.BaseDirectory);
    }
}
