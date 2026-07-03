using System.Text;

namespace RetailFeedback.StructuringEval;

public static class MarkdownReport
{
    public static string Render(
        EvalOptions eval,
        IReadOnlyList<EvalInput> items,
        IReadOnlyList<EvalRecord> records,
        string promptPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Structuring eval — {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        if (IsPlaceholderRun(eval))
        {
            sb.AppendLine("> ⚠ **NON-EVIDENTIAL — PIPELINE TEST ONLY.** Inputs are synthetic,");
            sb.AppendLine("> LLM-generated placeholder texts. Per the CLAUDE.md hard rule these");
            sb.AppendLine("> results prove the pipeline and MUST NOT be used to pick the");
            sb.AppendLine("> structuring model. The model decision waits for the hand-written corpus.");
            sb.AppendLine();
        }
        sb.AppendLine(
            $"- Items: {items.Count}; repetitions: {eval.Repetitions}; temperature: {eval.Temperature}; " +
            $"max output tokens: {(eval.MaxOutputTokens > 0 ? eval.MaxOutputTokens.ToString() : "uncapped")}; " +
            $"reasoning off via /no_think soft switch: {eval.DisableThinking}");
        sb.AppendLine($"- Prompt: `{Path.GetFileName(promptPath)}` (identical for all candidates)");
        sb.AppendLine("- Primary metric is PROMPT-ONLY JSON discipline — no constrained decoding.");
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.Append(RenderSummaryTable(eval, records));
        sb.AppendLine();

        sb.AppendLine("## Per-field violations");
        foreach (var model in eval.Candidates)
        {
            sb.AppendLine();
            sb.AppendLine($"### {Short(model)}");
            var groups = records
                .Where(r => r.Model == model)
                .SelectMany(r => r.Validated.Violations)
                .GroupBy(v => (v.Field, v.Kind, v.Value))
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key.Field, StringComparer.Ordinal)
                .ToList();
            if (groups.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("(none)");
                continue;
            }
            sb.AppendLine();
            sb.AppendLine("| field | kind | value | count |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var g in groups)
                sb.AppendLine($"| {g.Key.Field} | {g.Key.Kind} | {Escape(g.Key.Value)} | {g.Count()} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Side by side (sensibility judgment)");
        sb.AppendLine();
        sb.AppendLine("Majority result over repetitions as department/severity/type, then theme of the first adherent repetition.");
        sb.AppendLine("⚠ = repetitions disagreed; ✗ = no schema-adherent output at all. (n/m✓) = adherent repetitions.");
        sb.AppendLine();
        sb.AppendLine($"| id | text |{string.Join("|", eval.Candidates.Select(m => $" {Short(m)} "))}|");
        sb.AppendLine($"|---|---|{string.Concat(Enumerable.Repeat("---|", eval.Candidates.Count))}");
        foreach (var item in items)
        {
            var cells = eval.Candidates.Select(m => Cell(records, m, item.Id));
            sb.AppendLine($"| {item.Id} | {Escape(Truncate(item.Text, 60))} |{string.Join("|", cells)}|");
        }

        return sb.ToString();
    }

    public static string RenderSummaryTable(EvalOptions eval, IReadOnlyList<EvalRecord> records)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"| metric |{string.Join("|", eval.Candidates.Select(m => $" {Short(m)} "))}|");
        sb.AppendLine($"|---|{string.Concat(Enumerable.Repeat("---|", eval.Candidates.Count))}");
        AppendRow(sb, eval, records, "strict JSON rate", rs => Rate(rs.Count(r => r.Validated.Outcome == ParseOutcome.StrictJson), rs.Count));
        AppendRow(sb, eval, records, "parseable rate (incl. salvaged)", rs => Rate(rs.Count(r => r.Validated.Outcome != ParseOutcome.Unparseable), rs.Count));
        AppendRow(sb, eval, records, "schema-adherent rate (of all calls)", rs => Rate(rs.Count(r => r.Validated.SchemaAdherent), rs.Count));
        AppendRow(sb, eval, records, "consistency (items where all reps agree)", rs => Consistency(rs));
        AppendRow(sb, eval, records, "latency mean", rs => $"{rs.Average(r => r.LatencyMs):F0} ms");
        AppendRow(sb, eval, records, "latency p50", rs => $"{Percentile(rs.Select(r => (double)r.LatencyMs).ToList(), 0.5):F0} ms");
        AppendRow(sb, eval, records, "latency max", rs => $"{rs.Max(r => r.LatencyMs)} ms");
        return sb.ToString();
    }

    private static void AppendRow(
        StringBuilder sb,
        EvalOptions eval,
        IReadOnlyList<EvalRecord> records,
        string metric,
        Func<List<EvalRecord>, string> value)
    {
        var cells = eval.Candidates.Select(m =>
        {
            var rs = records.Where(r => r.Model == m).ToList();
            return rs.Count == 0 ? "–" : value(rs);
        });
        sb.AppendLine($"| {metric} | {string.Join(" | ", cells)} |");
    }

    /// <summary>Fraction of items where every repetition was schema-adherent AND
    /// produced the same department/severity/type triple.</summary>
    private static string Consistency(List<EvalRecord> records)
    {
        var byItem = records.GroupBy(r => r.ItemId).ToList();
        var consistent = byItem.Count(g =>
        {
            var structures = g.Select(r => r.Validated.Structure).ToList();
            if (structures.Any(s => s is null))
                return false;
            return structures.Select(s => (s!.Department, s.Severity, s.Type)).Distinct().Count() == 1;
        });
        return Rate(consistent, byItem.Count);
    }

    private static string Cell(IReadOnlyList<EvalRecord> records, string model, string itemId)
    {
        var reps = records.Where(r => r.Model == model && r.ItemId == itemId).ToList();
        var structures = reps
            .Where(r => r.Validated.Structure is not null)
            .Select(r => r.Validated.Structure!)
            .ToList();
        if (structures.Count == 0)
            return " ✗ ";

        var triples = structures.GroupBy(s => (s.Department, s.Severity, s.Type)).OrderByDescending(g => g.Count()).ToList();
        var majority = triples[0].Key;
        var flag = triples.Count > 1 ? " ⚠" : "";
        var theme = structures[0].Theme;
        return $" {majority.Department}/{majority.Severity}/{majority.Type}{flag} “{Escape(theme)}” ({structures.Count}/{reps.Count}✓) ";
    }

    private static string Rate(int hit, int total) => total == 0 ? "–" : $"{100.0 * hit / total:F0}% ({hit}/{total})";

    private static double Percentile(List<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    /// <summary>Placeholder runs are detected from the input path so the
    /// non-evidential label cannot be forgotten (CLAUDE.md hard rule).</summary>
    public static bool IsPlaceholderRun(EvalOptions eval) =>
        eval.InputPath.Contains("placeholder", StringComparison.OrdinalIgnoreCase);

    /// <summary>"hf.co/…/Llama-Poro-2-8B-Instruct-GGUF:Q4_K_M" → "Llama-Poro-2-8B-Instruct-GGUF:Q4_K_M".</summary>
    private static string Short(string model) => model[(model.LastIndexOf('/') + 1)..];

    private static string Escape(string s) => s.Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|");

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
