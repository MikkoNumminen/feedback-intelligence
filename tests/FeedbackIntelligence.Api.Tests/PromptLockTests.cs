using System.Security.Cryptography;
using System.Text;

namespace FeedbackIntelligence.Api.Tests;

/// <summary>
/// ADR-0022 prompt lock. The retail live-path prompts are frozen at v0; this guard
/// pins the newline-normalized SHA-256 of each, so any edit — a semantically-equal
/// reword, or even a lone CRLF flip (ADR-0018 moved a safety alert with one) — makes
/// a RED build until the change is deliberately re-validated and the hash updated.
///
/// Hashing is newline-normalized so the lock is stable across a CRLF (Windows dev) vs
/// LF (ubuntu CI) checkout: it guards CONTENT, not line endings.
///
/// To change a locked prompt (procedure also printed by the failing assertion):
///   1. re-run the A4 red-team fixture (RedTeamCoverageTests) — must stay green;
///   2. an announced live check (seed-42 report through real Poro: 0 ungrounded / 0
///      action drops, alerts grounded to real ids);
///   3. update the expected hash below in the SAME commit, citing the re-check.
/// Scope + rationale: docs/decisions/0022-lock-poro-prompts-v0.md.
/// </summary>
public class PromptLockTests
{
    [Theory]
    [InlineData("prompts/structuring-v0.txt",
        // Re-validated 2026-07-14 (ADR-0022 gate, ADR-0028/0031): theme-format
        // constraint + optional model-authored sentiment field. A4 RedTeamCoverage
        // stayed green; announced seed-42 live Poro check on a throwaway DB showed
        // 71 items, 0 ungrounded, 0 action drops, both alerts grounded.
        "4fc90656b772532a74a8e295b8fe96ba051313bc007ab6c69a1bb6833e459f64")]
    [InlineData("domains/retail/prompts/synthesis-v0.txt",
        "2d22cd66934d8ae50d7a47053b8d1b466369a026c2be0ee0f499a38cc8e061d6")]
    [InlineData("domains/retail/prompts/alert-nomination-v0.txt",
        "8cd3b0a3a13bcaaa1eeba0f06b96fc5c194da4a2f4fb6454e043c277b8bee344")]
    [InlineData("domains/retail/prompts/alert-verify-v0.txt",
        "fd7efb60a6f7d0829f39d19658b2bddb80bb968c5c0acd02d4404cc2f4dee968")]
    public void LockedPrompt_MatchesPinnedHash(string relativePath, string expected)
    {
        var path = Path.Combine(TestDomains.RepoRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"locked prompt not found: {relativePath}");

        var actual = NormalizedSha256(path);

        Assert.True(actual == expected,
            $"Locked prompt '{relativePath}' changed (ADR-0022 — frozen at v0).\n" +
            $"  expected {expected}\n" +
            $"  actual   {actual}\n" +
            "If this change is intended, it is GATED:\n" +
            "  1. re-run the A4 red-team fixture (RedTeamCoverageTests) — must stay green;\n" +
            "  2. an announced live check (seed-42 report through real Poro: 0 ungrounded /\n" +
            "     0 action drops, alerts grounded to real ids);\n" +
            "  then set the expected hash above to the 'actual' value in the SAME commit,\n" +
            "  citing the re-check. See docs/decisions/0022-lock-poro-prompts-v0.md.");
    }

    /// <summary>SHA-256 over the file text with newlines normalized to \n and encoded
    /// UTF-8, so a CRLF/LF checkout difference never trips the lock (ADR-0022).</summary>
    private static string NormalizedSha256(string path)
    {
        var text = File.ReadAllText(path).Replace("\r\n", "\n").Replace("\r", "\n");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
