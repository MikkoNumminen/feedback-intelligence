using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FeedbackIntelligence.Generator;
using FeedbackIntelligence.Llm;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    // Config and prompts ship with the tool; data paths inside that config are
    // resolved against the caller's working directory (run from the repo root).
    ContentRootPath = AppContext.BaseDirectory,
    Args = args,
});
builder.Logging.ClearProviders();

builder.Services.AddFeedbackIntelligenceLlm(builder.Configuration);
builder.Services.AddOptions<GeneratorOptions>()
    .Bind(builder.Configuration.GetSection(GeneratorOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<GeneratorOptions>, GeneratorOptionsValidator>();
builder.Services.AddSingleton<VariantsRunner>();
builder.Services.AddSingleton<GenerateRunner>();

using var host = builder.Build();

try
{
    _ = host.Services.GetRequiredService<IOptions<LlmOptions>>().Value;
    _ = host.Services.GetRequiredService<IOptions<GeneratorOptions>>().Value;
}
catch (OptionsValidationException ex)
{
    Console.Error.WriteLine("Configuration invalid:");
    foreach (var failure in ex.Failures)
        Console.Error.WriteLine($"  - {failure}");
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    switch (args.FirstOrDefault()?.ToLowerInvariant())
    {
        case "variants":
            return await host.Services.GetRequiredService<VariantsRunner>()
                .RunAsync(force: args.Contains("--force", StringComparer.OrdinalIgnoreCase), cts.Token);

        case "verify":
        {
            var truthPath = ArgValue(args, "--ground-truth");
            var reportPath = ArgValue(args, "--report");
            if (truthPath is null || reportPath is null)
            {
                Console.Error.WriteLine("verify requires --ground-truth <file> and --report <file>.");
                return 1;
            }
            if (!File.Exists(truthPath) || !File.Exists(reportPath))
            {
                Console.Error.WriteLine($"File not found: {(File.Exists(truthPath) ? reportPath : truthPath)}");
                return 1;
            }
            List<ReportVerifier.StoryResult> results;
            try
            {
                results = ReportVerifier.Verify(
                    await File.ReadAllTextAsync(truthPath, cts.Token),
                    await File.ReadAllTextAsync(reportPath, cts.Token));
            }
            catch (InvalidDataException ex)
            {
                // Exit 2 = the gate never ran (operator/data error) — CI must
                // never confuse this with an acceptance failure (exit 1).
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            var allPass = true;
            var trendWarnings = 0;
            foreach (var r in results)
            {
                allPass &= r.Pass;
                if (!r.TrendOk)
                    trendWarnings++;
                Console.WriteLine(
                    $"{(r.Pass ? "PASS" : "FAIL")} {r.StoryId}: grounding {r.GroundedIds}/{r.RequiredIds} required" +
                    $"{(r.WindowCovered ? "" : "; WINDOW MISMATCH — the report window does not cover the story window (wrong report?)")}" +
                    $"; trend {r.ExpectedTrend} -> {r.ReportedDirection} {(r.TrendOk ? "ok" : "WARNING: diluted/contradicted — check same-category noise share")}" +
                    $"{(r.AlertExpected ? $"; alert {(r.AlertPass ? "present" : "MISSING")}" : "")}" +
                    $"; keywords in narrative: {(r.KeywordSeen ? "yes" : "no (informational)")}");
            }
            Console.WriteLine(allPass
                ? $"\nACCEPTANCE: PASS — every planted story is grounded{(trendWarnings > 0 ? $" ({trendWarnings} trend warning(s) — see above)" : "")}."
                : "\nACCEPTANCE: FAIL");
            return allPass ? 0 : 1;
        }

        case "generate":
            var seedIndex = Array.FindIndex(args, a => a.Equals("--seed", StringComparison.OrdinalIgnoreCase));
            if (seedIndex < 0 || seedIndex + 1 >= args.Length || !int.TryParse(args[seedIndex + 1], out var seed))
            {
                Console.Error.WriteLine("generate requires --seed <int>.");
                return 1;
            }
            return await host.Services.GetRequiredService<GenerateRunner>().RunAsync(seed, cts.Token);

        default:
            Console.WriteLine("""
                FeedbackIntelligence.Generator — Phase 1 seeded demo-data generator

                Usage (from the repo root):
                  dotnet run --project tools/FeedbackIntelligence.Generator -- variants [--force]
                  dotnet run --project tools/FeedbackIntelligence.Generator -- generate --seed 42

                  variants   OFFLINE LLM multiplication of the hand-written core corpus
                             (data/corpus/core.jsonl -> variants.jsonl, committed).
                             Runs the LLM: announce first, GPU is shared with the live RAG.
                  generate   deterministic seeded composition from the COMMITTED variants
                             pool -> generated-<seed>.jsonl + ground-truth-<seed>.json.
                             Never calls the LLM.
                  verify     Phase 4 acceptance: --ground-truth <file> --report <file>.
                             ID-grounding, trend and alert checks; exit 0 = PASS.
                """);
            return args.Length == 0 ? 0 : 1;
    }
}
catch (InvalidDataException ex)
{
    // Story/corpus data errors (e.g. a domain's stories.json fails validation, or
    // a story has no matching pool items) are operator/data errors: exit 2 with the
    // message, never a stack trace, and never confusable with a gate failure (exit 1).
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}

static string? ArgValue(string[] args, string name)
{
    var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
